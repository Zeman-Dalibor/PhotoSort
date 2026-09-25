--[[
  denoise_batch.lua - unattended AI raw denoise of a folder of photos.

  This script is not meant to be started by hand. The launcher
  (denoise-folder.sh / Denoise-Folder.ps1) starts darktable like this:

    darktable --library <work>/library.db
              --conf plugins/lighttable/neural_restore/raw_strength=<0-100>
              ... more --conf overrides ...
              --luacmd 'dofile("<path>/denoise_batch.lua")'

  and points DT_DENOISE_WORKDIR at a work directory that holds

    job.conf    key=value job description
    files.txt   one absolute path per line - the raws to process

  The script imports those files into the throw-away library, selects them
  and presses "process" in the neural restore module, which is the only way
  to reach darktable's native AI raw denoise (darktable-cli cannot do it and
  the darktable.ai Lua API has no tensor blend, so it cannot honour the
  strength setting). While the batch runs the script watches the output
  folder and finally writes

    result.txt  machine readable result, polled by the launcher
    lua.log     human readable log

  and asks darktable to quit.
]]

local dt = require "darktable"

-- must match _task_suffix() / the DNG branch in src/libs/neural_restore.c
local SUFFIX = "_raw-denoise"

-- action path of the module's "process" button; tried in this order,
-- job.conf may pin one via action_path
local ACTION_CANDIDATES = { "lib/neural restore/process",
                            "lib/neural_restore/process" }

local POLL_MS = 1000    -- output folder polling interval
local SETTLE_MS = 3000  -- grace time after the last DNG appeared
local START_DELAY_MS = 1500
local RETRY_AFTER_S = 60 -- press "process" once more if nothing happened

local WORKDIR = ((os.getenv("DT_DENOISE_WORKDIR") or ""):gsub("\\", "/"))

-- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
-- helpers
-- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

local log_file

local function log(fmt, ...)
  local msg = select("#", ...) > 0 and string.format(fmt, ...) or fmt
  if log_file then
    log_file:write(os.date("%H:%M:%S "), msg, "\n")
    log_file:flush()
  end
  dt.print_log("[denoise_batch] " .. msg)
end

local function read_lines(path)
  local f = io.open(path, "r")
  if not f then return nil end
  local lines = {}
  for line in f:lines() do
    line = line:gsub("^%s+", ""):gsub("%s+$", "")
    if line ~= "" then lines[#lines + 1] = line end
  end
  f:close()
  return lines
end

local function read_config(path)
  local cfg = {}
  for _, line in ipairs(read_lines(path) or {}) do
    local key, value = line:match("^([^=]+)=(.*)$")
    if key then cfg[key] = value end
  end
  return cfg
end

-- size of an existing file, nil when it does not exist
local function file_size(path)
  local f = io.open(path, "rb")
  if not f then return nil end
  local size = f:seek("end")
  f:close()
  return size
end

local function slashes(path)
  return (path:gsub("\\", "/"))
end

local function dirname(path)
  return slashes(path):match("^(.*)/[^/]*$") or "."
end

local function basename_noext(path)
  local name = slashes(path):match("([^/]+)$") or path
  return name:match("^(.*)%.[^.]*$") or name
end

-- Predict the file names the module will write. neural_restore.c builds
-- "<out>/<base><suffix>.dng" and, if that exists, appends _1, _2, ... The
-- images are processed one by one, so replaying the same rule over our list
-- gives the exact set of names to wait for.
local function plan_outputs(files, output_dir)
  local planned, claimed = {}, {}
  for _, file in ipairs(files) do
    local folder = output_dir ~= "" and output_dir or dirname(file)
    local base = basename_noext(file)
    if base:sub(-#SUFFIX) ~= SUFFIX then base = base .. SUFFIX end

    local candidate = string.format("%s/%s.dng", folder, base)
    local index = 0
    while claimed[candidate:lower()] or file_size(candidate) do
      index = index + 1
      candidate = string.format("%s/%s_%d.dng", folder, base, index)
    end
    claimed[candidate:lower()] = true
    planned[#planned + 1] = candidate
  end
  return planned
end

local function write_result(status, message, planned, produced)
  local f = io.open(WORKDIR .. "/result.txt", "w")
  if not f then return end
  f:write("status=", status, "\n")
  f:write("message=", (message or ""):gsub("[\r\n]+", " "), "\n")
  f:write("total=", tostring(planned and #planned or 0), "\n")
  local done = 0
  for index, path in ipairs(planned or {}) do
    if produced and produced[index] then
      done = done + 1
      f:write("output=", path, "\n")
    else
      f:write("missing=", path, "\n")
    end
  end
  f:write("done=", tostring(done), "\n")
  f:close()
end

local function finish(status, message, planned, produced)
  log("%s: %s", status, message)
  write_result(status, message, planned, produced)
  if log_file then
    log_file:close()
    log_file = nil
  end
  -- the launcher terminates the process if this action is not available
  pcall(dt.gui.action, "global/quit", 0, "", "", 1.0)
end

-- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
-- the job
-- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

-- The action path cannot be probed: darktable presses a button for every
-- speed that is not NaN, so asking whether it exists would already start the
-- batch. We press the first candidate and only fall back to the next one if
-- nothing has happened after RETRY_AFTER_S.
local function action_candidates(override)
  if override and override ~= "" then return { override } end
  return ACTION_CANDIDATES
end

local function press(path)
  local ok, err = pcall(dt.gui.action, path, 0, "", "", 1.0)
  if not ok then
    log("WARNING: action '%s' failed: %s", path, tostring(err))
  end
  return ok
end

local function main()
  if WORKDIR == "" then error("DT_DENOISE_WORKDIR is not set") end
  log_file = io.open(WORKDIR .. "/lua.log", "w")

  local cfg = read_config(WORKDIR .. "/job.conf")
  local output_dir = slashes(cfg.output_dir or "")
  local stall_timeout = tonumber(cfg.stall_timeout or "") or 900

  log("darktable %s", dt.configuration.version)
  log("strength %s%%, output %s", cfg.strength or "?",
      output_dir ~= "" and output_dir or "next to the source file")

  if not dt.ai then
    return finish("error",
      "this darktable build has no AI support (darktable.ai is missing)")
  end

  local model = dt.ai.model_for_task("rawdenoise")
  if not model then
    return finish("error",
      "no rawdenoise model is active - open darktable, go to "
      .. "preferences > AI, switch AI features on, download a rawdenoise "
      .. "model and tick its enabled box")
  end
  log("rawdenoise model: %s", model)

  local files = read_lines(WORKDIR .. "/files.txt")
  if not files or #files == 0 then
    return finish("error", "files.txt is missing or empty")
  end

  local images, imported = {}, {}
  for _, path in ipairs(files) do
    local ok, image = pcall(dt.database.import, path)
    if ok and image then
      images[#images + 1] = image
      imported[#imported + 1] = path
    else
      log("WARNING: darktable refused to import %s", path)
    end
  end
  if #images == 0 then
    return finish("error", "none of the listed files could be imported")
  end
  log("imported %d of %d file(s)", #images, #files)

  -- the module acts on the lighttable selection
  pcall(function() dt.gui.current_view(dt.gui.views.lighttable) end)
  pcall(function() dt.gui.libs.neural_restore.visible = true end)
  dt.gui.selection(images)
  dt.control.sleep(START_DELAY_MS)

  local planned = plan_outputs(imported, output_dir)
  local candidates = action_candidates(cfg.action_path)
  local attempt = 1
  log("pressing '%s' for %d image(s)", candidates[attempt], #planned)
  press(candidates[attempt])

  local produced, count = {}, 0
  local last_change = os.time()

  while count < #planned do
    dt.control.sleep(POLL_MS)
    if dt.control.ending then
      return finish("error", "darktable is shutting down", planned, produced)
    end

    for index, path in ipairs(planned) do
      if not produced[index] and file_size(path) then
        produced[index] = true
        count = count + 1
        last_change = os.time()
        log("[%d/%d] %s", count, #planned, path)
      end
    end
    if count == #planned then break end

    local idle = os.time() - last_change
    if count == 0 and attempt < #candidates and idle >= RETRY_AFTER_S then
      attempt = attempt + 1
      log("nothing happened in %d s - trying action '%s'",
          idle, candidates[attempt])
      press(candidates[attempt])
      last_change = os.time()
    elseif idle >= stall_timeout then
      if count == 0 then
        return finish("error",
          string.format("nothing was produced in %d s - the process button "
            .. "of the neural restore module was probably never triggered, "
            .. "see the action path hint in README.md", idle),
          planned, produced)
      end
      return finish("partial",
        string.format("no new DNG for %d s, %d of %d done",
                      idle, count, #planned),
        planned, produced)
    end
  end

  -- the last file may still be flushing
  dt.control.sleep(SETTLE_MS)
  finish("ok", string.format("%d DNG file(s) written", count),
         planned, produced)
end

-- dispatch: --luacmd runs during startup, the GUI is only usable afterwards
dt.control.dispatch(function()
  local ok, err = pcall(main)
  if not ok then
    finish("error", tostring(err))
  end
end)

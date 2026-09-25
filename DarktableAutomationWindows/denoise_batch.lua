--[[
  denoise_batch.lua - unattended AI raw denoise of a folder of photos.

  This script is not meant to be started by hand. The launcher
  (Denoise-Folder.ps1 / denoise-folder.sh) starts darktable like this:

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

-- Action path of the module's "process" button, tried in this order;
-- job.conf may pin one via action_path. darktable builds the path from the
-- module's plugin name, not from its displayed name, so the underscore
-- variant is the one that works.
local ACTION_CANDIDATES = { "lib/neural_restore/process",
                            "lib/neural restore/process" }

local POLL_MS = 1000    -- output folder polling interval
local SETTLE_MS = 3000  -- grace time after the last DNG appeared
local START_DELAY_MS = 3000 -- let the lighttable catch up with the selection
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

local function count_table(t)
  local n = 0
  for _ in pairs(t) do n = n + 1 end
  return n
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

local function write_result(status, message, total, outputs)
  local f = io.open(WORKDIR .. "/result.txt", "w")
  if not f then return end
  outputs = outputs or {}
  f:write("status=", status, "\n")
  f:write("message=", (message or ""):gsub("[\r\n]+", " "), "\n")
  f:write("total=", tostring(total or 0), "\n")
  for _, path in ipairs(outputs) do
    f:write("output=", path, "\n")
  end
  f:write("done=", tostring(#outputs), "\n")
  f:close()
end

local function finish(status, message, total, outputs)
  log("%s: %s", status, message)
  write_result(status, message, total, outputs)
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

-- number of images in the current collection, nil when it cannot be read
local function collection_size()
  local ok, count = pcall(function() return #dt.collection end)
  if not ok then return nil end
  return count
end

-- darktable intersects the selection with the current collection
-- (dt_selection_get_list_query: selected_images IN memory.collected_images),
-- and importing a film roll does not collect it. Without this the lighttable
-- stays empty and the module's process button never becomes sensitive.
-- The library is a throw-away one, so "every film roll" is the safest rule;
-- the folder based rules are fallbacks.
local function collect_images(input_dir)
  local attempts = { { item = "DT_COLLECTION_PROP_FILMROLL", data = "%" } }

  for _, dir in ipairs({ input_dir, slashes(input_dir) }) do
    if dir ~= "" then
      attempts[#attempts + 1] = { item = "DT_COLLECTION_PROP_FOLDERS", data = dir }
      attempts[#attempts + 1] = { item = "DT_COLLECTION_PROP_FILMROLL", data = dir }
    end
  end

  for _, attempt in ipairs(attempts) do
    local ok, err = pcall(function()
      local rule = dt.gui.libs.collect.new_rule()
      rule.mode = "DT_LIB_COLLECT_MODE_AND"
      rule.item = attempt.item
      rule.data = attempt.data
      dt.gui.libs.collect.filter({ rule })
    end)

    if not ok then
      log("WARNING: collect rule %s='%s' failed: %s",
          attempt.item, attempt.data, tostring(err))
    else
      -- the collection is rebuilt on the main thread, give it a moment
      local count = 0
      for _ = 1, 6 do
        dt.control.sleep(500)
        count = collection_size()
        if count == nil or count > 0 then break end
      end
      log("collect rule %s='%s' -> %s image(s)",
          attempt.item, attempt.data, tostring(count))
      if count == nil or count > 0 then return count end
    end
  end
  return 0
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

  -- the module acts on the lighttable selection, and only on images that
  -- are part of the current collection
  pcall(function() dt.gui.current_view(dt.gui.views.lighttable) end)
  pcall(function() dt.gui.libs.neural_restore.visible = true end)

  if collect_images(cfg.input_dir or "") == 0 then
    return finish("error",
      "the imported photos are not in the current collection, so darktable "
      .. "ignores them - see lua.log for the collect rules that were tried")
  end

  dt.gui.selection(images)
  dt.control.sleep(START_DELAY_MS)

  local selected = #dt.gui.selection()
  log("selected %d of %d image(s)", selected, #images)
  if selected == 0 then
    return finish("error", "the imported photos could not be selected")
  end

  local total = #imported
  local planned = plan_outputs(imported, output_dir)
  log("expecting files like %s", planned[1])

  -- Two independent ways of noticing a finished DNG, because guessing the
  -- output path is brittle (variables in the output folder, name collisions,
  -- non-ASCII paths that Lua's io cannot open): the file on disk, and the
  -- image darktable imports into our throw-away library once it is written.
  local known, written, on_disk = {}, {}, {}
  for _, image in ipairs(images) do known[image.id] = true end

  local function scan()
    for index, path in ipairs(planned) do
      if not on_disk[index] and file_size(path) then
        on_disk[index] = path
        log("on disk: %s", path)
      end
    end

    local ok = pcall(function()
      for _, image in ipairs(dt.database) do
        if not known[image.id] then
          known[image.id] = true
          -- only DNGs count, so an unrelated import can never make the
          -- batch look finished before it is
          if image.filename:lower():match("%.dng$") then
            local path = slashes(image.path) .. "/" .. image.filename
            written[image.id] = path
            log("written: %s", path)
          end
        end
      end
    end)
    if not ok then return count_table(on_disk) end

    return math.max(count_table(on_disk), count_table(written))
  end

  local function outputs()
    local list = {}
    for _, path in pairs(written) do list[#list + 1] = path end
    if #list > 0 then return list end
    for _, path in pairs(on_disk) do list[#list + 1] = path end
    return list
  end

  local candidates = action_candidates(cfg.action_path)
  local attempt = 1
  log("pressing '%s' for %d image(s)", candidates[attempt], total)
  press(candidates[attempt])

  local count = 0
  local last_change = os.time()
  local last_beat = os.time()

  while count < total do
    dt.control.sleep(POLL_MS)
    if dt.control.ending then
      return finish("error", "darktable is shutting down", total, outputs())
    end

    local found = scan()
    if found > count then
      count = found
      last_change = os.time()
      log("%d of %d done", count, total)
    end
    if count >= total then break end

    local idle = os.time() - last_change
    if os.time() - last_beat >= 30 then
      last_beat = os.time()
      log("waiting, %d of %d done, %d s since the last one", count, total, idle)
    end

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
          total, outputs())
      end
      return finish("partial",
        string.format("no new DNG for %d s, %d of %d done", idle, count, total),
        total, outputs())
    end
  end

  -- the last file may still be flushing
  dt.control.sleep(SETTLE_MS)
  scan()
  finish("ok", string.format("%d DNG file(s) written", count), total, outputs())
end

-- dispatch: --luacmd runs during startup, the GUI is only usable afterwards
dt.control.dispatch(function()
  local ok, err = pcall(main)
  if not ok then
    finish("error", tostring(err))
  end
end)

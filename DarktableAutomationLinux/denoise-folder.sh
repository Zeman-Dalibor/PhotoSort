#!/usr/bin/env bash
#
# denoise-folder.sh - AI raw denoise for a whole folder, using darktable.
#
# For every raw photo in the folder darktable's "neural restore" module
# writes a denoised DNG (<name>_raw-denoise.dng). The original file is never
# touched. darktable must already be installed, with AI features enabled and
# a rawdenoise model activated (see README.md).

set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
LUA_SCRIPT="$SCRIPT_DIR/denoise_batch.lua"

RAW_EXTENSIONS=(cr2 cr3 nef nrw arw srf sr2 raf orf rw2 pef dng raw rwl
                iiq 3fr fff erf mos mrw dcr kdc x3f)

strength=100
output=""
model=""
recursive=0
stall_timeout=900
darktable_bin="${DARKTABLE_BIN:-}"
action_path=""
force_gui=0
keep_workdir=0
input=""

usage() {
  cat <<'EOF'
Usage: denoise-folder.sh [options] <folder>

Creates a denoised DNG for every raw photo in <folder>.

Options:
  -s, --strength <0-100>  denoise strength, 100 = full model output (default 100)
  -o, --output <folder>   write the DNGs here instead of next to the source
  -m, --model <id>        rawdenoise model to use (default: the active one)
  -r, --recursive         include subfolders
  -t, --timeout <sec>     give up when no new DNG appears for this long (default 900)
  -b, --darktable <path>  path to the darktable binary
  -a, --action <path>     override the darktable action path of the "process"
                          button (default: lib/neural restore/process)
      --gui               show the darktable window instead of hiding it in Xvfb
      --keep-workdir      keep the temporary work directory for debugging
  -h, --help              this help

Examples:
  ./denoise-folder.sh ~/Pictures/2026-09-15
  ./denoise-folder.sh -s 70 -o ~/Pictures/denoised ~/Pictures/2026-09-15
EOF
}

die() { printf 'error: %s\n' "$*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    -s|--strength)  strength="${2:?}"; shift 2 ;;
    -o|--output)    output="${2:?}"; shift 2 ;;
    -m|--model)     model="${2:?}"; shift 2 ;;
    -t|--timeout)   stall_timeout="${2:?}"; shift 2 ;;
    -b|--darktable) darktable_bin="${2:?}"; shift 2 ;;
    -a|--action)    action_path="${2:?}"; shift 2 ;;
    -r|--recursive) recursive=1; shift ;;
    --gui)          force_gui=1; shift ;;
    --keep-workdir) keep_workdir=1; shift ;;
    -h|--help)      usage; exit 0 ;;
    -*)             die "unknown option: $1" ;;
    *)
      [[ -n "$input" ]] && die "only one input folder can be given"
      input="$1"; shift ;;
  esac
done

[[ -n "$input" ]] || { usage >&2; exit 2; }
[[ -d "$input" ]] || die "not a folder: $input"
[[ -f "$LUA_SCRIPT" ]] || die "missing $LUA_SCRIPT"
[[ "$strength" =~ ^[0-9]+$ ]] && (( strength <= 100 )) || die "strength must be 0-100"
[[ "$stall_timeout" =~ ^[0-9]+$ ]] || die "timeout must be a number of seconds"

input="$(cd -- "$input" && pwd)"
if [[ -n "$output" ]]; then
  mkdir -p -- "$output"
  output="$(cd -- "$output" && pwd)"
fi

# ----------------------------------------------------------------- darktable

if [[ -z "$darktable_bin" ]]; then
  darktable_bin="$(command -v darktable || true)"
fi
[[ -n "$darktable_bin" && -x "$darktable_bin" ]] \
  || die "darktable not found - install it or pass --darktable <path>"

if pgrep -x darktable >/dev/null 2>&1; then
  die "darktable is running - close it first, it locks the shared database"
fi

use_xvfb=0
if (( ! force_gui )); then
  if command -v xvfb-run >/dev/null 2>&1; then
    use_xvfb=1
  elif [[ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
    die "no display and no xvfb-run - install xvfb or run inside a desktop session"
  fi
fi

config_dir="${XDG_CONFIG_HOME:-$HOME/.config}/darktable"
darktablerc="$config_dir/darktablerc"

# ------------------------------------------------------------- file list

name_args=()
for ext in "${RAW_EXTENSIONS[@]}"; do
  name_args+=(-iname "*.${ext}" -o)
done
unset 'name_args[${#name_args[@]}-1]'

depth_args=(-maxdepth 1)
(( recursive )) && depth_args=(-mindepth 1)

files=()
while IFS= read -r file; do
  # skip our own output so a second run does not denoise the DNGs again
  [[ "$(basename -- "$file")" == *_raw-denoise* ]] && continue
  files+=("$file")
done < <(find "$input" "${depth_args[@]}" -type f \( "${name_args[@]}" \) -print \
         | LC_ALL=C sort)

(( ${#files[@]} )) || die "no raw photos found in $input"

# same basename with two different raw extensions would make the predicted
# output names ambiguous
declare -A seen_basenames=()
for file in "${files[@]}"; do
  base="$(basename -- "$file")"; base="${base%.*}"
  if [[ -n "${seen_basenames[$base]:-}" ]]; then
    printf 'warning: %s exists with several extensions, output names may shift\n' \
      "$base" >&2
  fi
  seen_basenames["$base"]=1
done

# ------------------------------------------------------------- work directory

workdir="$(mktemp -d -t dt-denoise-XXXXXXXX)"
rc_backup="$workdir/darktablerc.backup"
[[ -f "$darktablerc" ]] && cp -p -- "$darktablerc" "$rc_backup"

dt_pid=""

terminate_darktable() {
  [[ -n "$dt_pid" ]] || return 0
  kill -0 "$dt_pid" 2>/dev/null || return 0
  pkill -TERM -P "$dt_pid" 2>/dev/null || true
  kill -TERM "$dt_pid" 2>/dev/null || true
  for _ in {1..20}; do
    kill -0 "$dt_pid" 2>/dev/null || return 0
    sleep 0.5
  done
  pkill -KILL -P "$dt_pid" 2>/dev/null || true
  kill -KILL "$dt_pid" 2>/dev/null || true
}

# darktable removes its lock files on a clean exit; after a kill they stay
# behind and block the next start
drop_stale_lock() {
  local lock="$1" pid
  [[ -f "$lock" ]] || return 0
  pid="$(tr -dc '0-9' <"$lock")"
  if [[ -z "$pid" ]] || ! kill -0 "$pid" 2>/dev/null; then
    rm -f -- "$lock"
  fi
}

cleanup() {
  terminate_darktable
  drop_stale_lock "$config_dir/data.db.lock"
  drop_stale_lock "$workdir/library.db.lock"
  # --conf values are written back to darktablerc on exit; restore the
  # user's settings so the automation leaves no trace in the GUI
  if [[ -f "$rc_backup" ]]; then
    cp -p -- "$rc_backup" "$darktablerc" || true
  fi
  if (( keep_workdir )); then
    printf 'work directory kept: %s\n' "$workdir"
  else
    rm -rf -- "$workdir"
  fi
}
trap cleanup EXIT

printf '%s\n' "${files[@]}" >"$workdir/files.txt"
{
  printf 'input_dir=%s\n' "$input"
  printf 'output_dir=%s\n' "$output"
  printf 'strength=%s\n' "$strength"
  printf 'stall_timeout=%s\n' "$stall_timeout"
  printf 'action_path=%s\n' "$action_path"
} >"$workdir/job.conf"

# ------------------------------------------------------------- run darktable

conf_output="${output:-\$(FILE_FOLDER)}"

dt_args=(
  --library "$workdir/library.db"
  --conf "plugins/ai/enabled=TRUE"
  --conf "plugins/lighttable/act_on=FALSE"
  --conf "plugins/lighttable/neural_restore/active_page=0"
  --conf "plugins/lighttable/neural_restore/raw_strength=$strength"
  # the Lua script watches the throw-away library to notice finished DNGs
  --conf "plugins/lighttable/neural_restore/add_to_catalog=TRUE"
  --conf "plugins/lighttable/neural_restore/output_directory=$conf_output"
  --conf "write_sidecar_files=never"
  --luacmd "dofile(\"${LUA_SCRIPT//\"/\\\"}\")"
)
[[ -n "$model" ]] && dt_args+=(--conf "plugins/ai/models/active/rawdenoise=$model")

printf 'darktable: %s\n' "$darktable_bin"
printf 'photos:    %d\n' "${#files[@]}"
printf 'strength:  %s%%\n' "$strength"
printf 'output:    %s\n\n' "${output:-next to the source files}"

export DT_DENOISE_WORKDIR="$workdir"
if (( use_xvfb )); then
  xvfb-run -a "$darktable_bin" "${dt_args[@]}" >"$workdir/darktable.log" 2>&1 &
else
  "$darktable_bin" "${dt_args[@]}" >"$workdir/darktable.log" 2>&1 &
fi
dt_pid=$!

# ------------------------------------------------------------- wait

overall_timeout=$(( stall_timeout * (${#files[@]} + 1) + 300 ))
started=$SECONDS
printed=0

while :; do
  if [[ -f "$workdir/lua.log" ]]; then
    total_lines=$(wc -l <"$workdir/lua.log")
    if (( total_lines > printed )); then
      tail -n +$(( printed + 1 )) "$workdir/lua.log"
      printed=$total_lines
    fi
  fi

  [[ -f "$workdir/result.txt" ]] && break
  kill -0 "$dt_pid" 2>/dev/null || break
  if (( SECONDS - started > overall_timeout )); then
    printf '\nerror: timed out after %d s\n' "$(( SECONDS - started ))" >&2
    break
  fi
  sleep 2
done

# give the Lua script's quit request a moment to take effect
for _ in {1..30}; do
  kill -0 "$dt_pid" 2>/dev/null || break
  sleep 1
done

# ------------------------------------------------------------- report

if [[ ! -f "$workdir/result.txt" ]]; then
  printf '\nerror: darktable exited without a result, last log lines:\n\n' >&2
  tail -n 20 "$workdir/darktable.log" >&2 || true
  exit 1
fi

status="$(sed -n 's/^status=//p' "$workdir/result.txt")"
message="$(sed -n 's/^message=//p' "$workdir/result.txt")"
total="$(sed -n 's/^total=//p' "$workdir/result.txt")"
done_count="$(sed -n 's/^done=//p' "$workdir/result.txt")"

printf '\n%s: %s\n' "$status" "$message"
[[ -n "$total" ]] && printf 'written %s of %s DNG file(s)\n' "${done_count:-0}" "$total"

[[ "$status" == "ok" ]] || exit 1

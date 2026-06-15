#!/usr/bin/env bash
# signal.sh — write a per-session state file into the tray app's signal directory.
#
# Usage: signal.sh <state-token> <session-id>
#   state-token  — the state to signal, e.g. "turn-done"
#   session-id   — opaque per-session key (Claude Code UUID); becomes the filename
#
# The script is fail-silent: any failure (bad args, interop unavailable, write error)
# results in a clean exit 0 with no file written and no stderr output to the session.
# Do not use "set -e" — explicit guards on every failure branch ensure exit 0.

state="$1"
session_id="$2"

# 1. Argument validation — fail-silent
if [ -z "$state" ] || [ -z "$session_id" ]; then
    exit 0
fi

# Sanitise session id to prevent path escape.
# Session ids from Claude Code are UUIDs and pass this, but the guard makes the
# write provably stay inside the signal directory.
case "$session_id" in
    */*|*\\*|*..*|.|..)
        exit 0
        ;;
esac

# 2. Verify WSL↔Windows interop tools exist
if ! command -v cmd.exe > /dev/null 2>&1; then
    exit 0
fi

if ! command -v wslpath > /dev/null 2>&1; then
    exit 0
fi

# 3. Resolve %LOCALAPPDATA% via Windows interop
localappdata_win="$(cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')"

if [ -z "$localappdata_win" ] || [ "$localappdata_win" = '%LOCALAPPDATA%' ]; then
    exit 0
fi

# 4. Convert to Linux path
localappdata_unix="$(wslpath -u "$localappdata_win" 2>/dev/null)"

if [ -z "$localappdata_unix" ]; then
    exit 0
fi

# 5. Build signal directory path
signal_dir="$localappdata_unix/keyboard-of-claude/signals"

# 6. Create directory if missing
mkdir -p "$signal_dir" 2>/dev/null || exit 0

# 7. Write the file atomically — write to a temp file in the same directory and
# rename over the target. The tray app watches this directory, so an in-place
# truncate-then-write could be observed mid-write as an empty or partial token;
# rename(2) within the same filesystem is atomic, so the watcher only ever sees
# the complete file. No trailing newline (printf '%s').
tmp_file="$signal_dir/.$session_id.tmp.$$"
printf '%s' "$state" > "$tmp_file" 2>/dev/null || exit 0
mv -f "$tmp_file" "$signal_dir/$session_id" 2>/dev/null || { rm -f "$tmp_file" 2>/dev/null; exit 0; }

exit 0

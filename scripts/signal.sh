#!/usr/bin/env bash
# signal.sh — write, clear, or reap per-Claude-process state files in the tray app's
# signal directory.
#
# Usage: signal.sh <state-token>
#   state-token  — the state to signal:
#                    "working" / "turn-done" / "blocked" — written as the file content
#                    "clear"                 — delete this process's file (return to green)
#                    "reap"                  — clear this process's own file, then delete
#                                              any file whose owning Claude process is dead
#
# State files are keyed by the *Claude process PID* (the `claude` ancestor of this script),
# NOT the conversation/session id. The session id rotates on `/clear`, which orphaned files
# under the old id; the process PID survives `/clear`, so the slot is reused instead of
# orphaned. One file == one running Claude (one terminal). The reaper removes files only
# when their owning process is genuinely gone — never on a timer — so a session that is
# legitimately waiting on the user stays lit for as long as its process lives.
#
# The script is fail-silent: any failure (bad args, interop unavailable, no Claude ancestor,
# write/delete error) results in a clean exit 0 with nothing written/removed and no stderr
# output to the session. Do not use "set -e" — explicit guards on every branch ensure exit 0.

state="$1"

# 1. Argument validation — fail-silent
if [ -z "$state" ]; then
    exit 0
fi

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

# Walk up the process tree from this script to the owning `claude` process and print its
# PID. /proc/<pid>/stat field 2 is the comm in parens (may contain spaces) and field 4 is
# the ppid; we read comm from /proc/<pid>/comm and parse ppid from the text after the last
# ')' to stay robust against odd comm values. Prints nothing and returns 1 if no claude
# ancestor is found within a bounded number of hops.
claude_pid() {
    local p=$$ comm rest depth=0
    while [ "$depth" -lt 12 ]; do
        comm="$(cat "/proc/$p/comm" 2>/dev/null)" || return 1
        if [ "$comm" = claude ]; then
            printf '%s' "$p"
            return 0
        fi
        rest="$(cat "/proc/$p/stat" 2>/dev/null)" || return 1
        rest="${rest##*) }"
        # rest is now "<state> <ppid> <pgrp> ..."; ppid is the second field.
        set -- $rest
        p="$2"
        if [ -z "$p" ] || [ "$p" = 0 ]; then
            return 1
        fi
        depth=$((depth + 1))
    done
    return 1
}

# True if $1 is a numeric PID owned by a live `claude` process.
is_live_claude() {
    case "$1" in
        ''|*[!0-9]*) return 1 ;;
    esac
    [ "$(cat "/proc/$1/comm" 2>/dev/null)" = claude ]
}

# 7. Reap path — clear this process's own file, then remove any file whose owning Claude
#    process is no longer alive. Liveness-based, never time-based.
if [ "$state" = "reap" ]; then
    own_pid="$(claude_pid)"
    if [ -n "$own_pid" ]; then
        rm -f "$signal_dir/$own_pid" 2>/dev/null
    fi
    for f in "$signal_dir"/*; do
        [ -f "$f" ] || continue
        base="${f##*/}"
        # Only manage PID-named files; skip temp files and any non-numeric leftovers.
        case "$base" in
            *[!0-9]*) continue ;;
        esac
        if ! is_live_claude "$base"; then
            rm -f "$f" 2>/dev/null
        fi
    done
    exit 0
fi

# 8. Write/clear path — resolve the owning Claude PID; this is the file key.
key="$(claude_pid)"
if [ -z "$key" ]; then
    exit 0
fi

if [ "$state" = "clear" ]; then
    # Clear path — delete this process's file. rm -f on a missing file exits 0, so this is
    # naturally idempotent. 2>/dev/null silences any unexpected error output.
    rm -f "$signal_dir/$key" 2>/dev/null || exit 0
else
    # Write path — atomically write state to this process's file. Write to a temp file in
    # the same directory and rename over the target. The tray app watches this directory,
    # so an in-place truncate-then-write could be observed mid-write as an empty or partial
    # token; rename(2) within the same filesystem is atomic, so the watcher only ever sees
    # the complete file. No trailing newline (printf '%s').
    tmp_file="$signal_dir/.$key.tmp.$$"
    printf '%s' "$state" > "$tmp_file" 2>/dev/null || exit 0
    mv -f "$tmp_file" "$signal_dir/$key" 2>/dev/null || { rm -f "$tmp_file" 2>/dev/null; exit 0; }
fi

exit 0

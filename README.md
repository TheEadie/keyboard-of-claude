# keyboard-of-claude

Ambient G213 status light for Claude Code sessions on WSL2/Windows.

A tray app watches a signal directory on Windows; Claude Code hooks write one state file per running Claude process into that directory. The tray app reads the files and sets the keyboard colour accordingly — amber when a turn finishes, red when a session is blocked on a permission prompt, green otherwise.

## Repo layout

| Path | Purpose |
|---|---|
| `src/KeyboardOfClaude.Tray` | Windows tray app that watches the signal directory and drives the G213 over HID |
| `scripts/signal.sh` | WSL signal script: writes/clears a state file keyed by the owning `claude` process, and reaps files for dead processes |
| `spike/` | Throwaway HID proof-of-concept (see its own README) |

## Claude Code hooks — turn-done (amber)

### Prerequisite

The tray app must be running. Build and launch it from `src/KeyboardOfClaude.Tray`.

### Stop hook — turns the keyboard amber when a turn finishes

Add the following entry to your **user-level** `~/.claude/settings.json`. Because the effect should apply to all Claude sessions, the hook belongs at the user level, not a project-level settings file.

```json
{
  "hooks": {
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh turn-done"
          }
        ]
      }
    ]
  }
}
```

If you already have a `hooks` block or a `Stop` array in your `~/.claude/settings.json`, append this matcher object to the existing `Stop` array rather than replacing the whole block.

**How the hook works:**

- The `Stop` event fires when a Claude Code turn completes.
- The script is invoked as `bash <abs-path> turn-done`. It needs no session id on the command line: `signal.sh` discovers its owning `claude` process by walking up the process tree and uses that **PID** as the state file's name. The Claude PID is stable across `/clear` and `/compact` (which rotate the conversation/session id but keep the process), so the same file slot is reused rather than orphaned.
- Only the `Stop` event is wired here — `SubagentStop` and other events are not used.

**Note:** The absolute path `/home/eadie/code/keyboard-of-claude/scripts/signal.sh` is specific to this machine's WSL layout. Adjust it if the repo lives at a different path.

### How it works / verifying

With the hook installed and the tray app running, finishing a turn in any Claude session:

1. Calls the hook command `scripts/signal.sh turn-done`.
2. The script resolves `%LOCALAPPDATA%` via `cmd.exe`, converts the path to a Linux mount via `wslpath`, derives the owning `claude` PID, and writes a file at `%LOCALAPPDATA%\keyboard-of-claude\signals\<claude-pid>` containing the text `turn-done`.
3. The tray app detects the new file and turns the G213 keyboard amber.

The script is fail-silent: if `%LOCALAPPDATA%` cannot be resolved, WSL↔Windows interop is unavailable, the directory cannot be created, or the write fails, the script exits 0 with no error surfaced to the Claude session.

**The keyboard stays amber** until a later slice (or manual file removal) clears it — clearing the signal on user reply is out of scope for this slice.

## Claude Code hooks — blocked (red) and clearing back to green

### Prerequisite

The tray app must be running. Build and launch it from `src/KeyboardOfClaude.Tray`. The Stop hook from the previous section should also be installed.

### Three hooks to add

Add the following entries to your **user-level** `~/.claude/settings.json`. These hooks complement the `Stop` hook above and complete the full colour cycle.

If you already have a `Notification`, `UserPromptSubmit`, `PostToolUse`, or `SessionStart` array in your `hooks` block, **append** each matcher object to the existing array rather than replacing it. If those arrays do not exist yet, add them alongside the existing `Stop` array.

```json
{
  "hooks": {
    "Notification": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "jq -e 'select((.message // \"\") | ascii_downcase | contains(\"permission\"))' >/dev/null 2>&1 && bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh blocked"
          }
        ]
      }
    ],
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh clear"
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh clear"
          }
        ]
      }
    ],
    "SessionStart": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh reap"
          }
        ]
      }
    ]
  }
}
```

**Note:** The absolute path `/home/eadie/code/keyboard-of-claude/scripts/signal.sh` is specific to this machine's WSL layout. Adjust it if the repo lives at a different path.

**How each hook works:**

- **`Notification` (blocked → red):** Fires when Claude Code raises a notification. `jq -e 'select(... | contains("permission"))'` reads `message` from stdin and exits non-zero unless the message contains "permission" (case-insensitive), so `signal.sh blocked` runs only for permission prompts. Non-permission notifications (e.g. idle "waiting for your input") leave the state unchanged.
- **`UserPromptSubmit` (clear → green on reply):** Fires when the user submits a prompt. Calls `signal.sh clear`, deleting this Claude process's signal file so the keyboard moves back towards green. Idempotent — works even if no signal file exists yet (e.g. the very first prompt of a session).
- **`PostToolUse` (clear → green on resume):** Fires after every tool call completes. Calls `signal.sh clear` — this ensures that when Claude resumes working after a permission approval (which does not fire `UserPromptSubmit`), the `blocked` state is cleared as soon as the approved tool runs. Since this fires on every tool call, the repeated `cmd.exe` path-resolution is accepted as-is; the fail-silent contract keeps this safe.
- **`SessionStart` (reap → drop stale files):** Fires when a session begins, including on `/clear`, `/compact`, resume, and startup. Calls `signal.sh reap`, which (a) clears this Claude process's own file — so `/clear`/`/compact` immediately drop a stale `turn-done`/`blocked` left from the previous conversation — and (b) removes any signal file whose owning `claude` PID is no longer alive, cleaning up after crashed or closed sessions. Reaping is purely liveness-based (it checks whether the process exists, never how old the file is), so a session that is legitimately waiting on you stays lit indefinitely.

### How it works / verifying

With all five hooks installed (Stop + the four above) and the tray app running:

1. **Permission prompt → red:** When Claude Code requests permission to use a tool, the `Notification` event fires with a message containing "permission". The hook writes `blocked` for the session; the tray app detects this and turns the keyboard **red**.
2. **User reply → green:** When you submit your reply (`UserPromptSubmit`), the hook deletes the session's signal file; the tray returns to **green** (assuming no other sessions are blocked or waiting).
3. **Resume after approval → green:** When you approve the tool and Claude resumes, `PostToolUse` fires on the next tool completion, clearing the `blocked` file and returning the keyboard to **green**.
4. **Turn completes → amber:** When Claude finishes its turn, the `Stop` hook (from the previous section) writes `turn-done`; the keyboard turns **amber**.
5. **`/clear` or `/compact` → green:** Starting a fresh conversation fires `SessionStart`, whose `reap` drops the previous conversation's `turn-done`/`blocked` file (same process, same slot) so the keyboard returns to **green** instead of pinning on a stale state.
6. **Concurrent sessions:** Each file is keyed by the owning `claude` process PID, so one running Claude (one terminal) maps to exactly one file. The tray app aggregates across all files using max-urgency logic (red > amber > green), so the keyboard always reflects the most urgent active session.

All hooks are fail-silent: if `%LOCALAPPDATA%` cannot be resolved, WSL↔Windows interop is unavailable, or a write/delete fails, the script exits 0 with no error surfaced to the Claude session.

### Known limitations

These are accepted tradeoffs of wiring the colour cycle to Claude Code's event model. They are out of scope for this slice but worth knowing:

- **`Notification` matching depends on wording.** The `blocked → red` hook only fires for notifications whose message contains the substring `permission` (case-insensitive). If a future Claude Code version rephrases or localises its permission prompt, the keyboard may stop turning red; conversely, an unrelated notification that happens to contain the word "permission" would falsely turn it red.
- **`PostToolUse` clears unconditionally.** Because `PostToolUse` deletes the session file after *every* tool call (not just an approved one), it can race other hooks. If a tool finishing (clear) interleaves after a near-simultaneous permission prompt (`blocked`) or end-of-turn (`turn-done`) write, the just-written state can be erased and the keyboard wrongly drops to green. Hooks run as independent processes with no ordering guarantee.
- **Denied permissions are not cleared automatically.** If you *deny* a permission request, no tool runs (so `PostToolUse` does not fire) and you may not submit a new prompt (so `UserPromptSubmit` does not fire). The `blocked` file then lingers and the keyboard stays red until the next prompt or tool call clears it.
- **Crashed-session cleanup is deferred, not immediate.** A session that ends abruptly (crash, closed terminal) leaves its file behind until the next `SessionStart` reap runs — i.e. when any Claude session next starts. Until then the stale file can pin the keyboard via max-urgency aggregation. The reap is keyed on process liveness, so it only ever removes files for PIDs that are genuinely gone; a long-waiting live session is never cleared.
- **PID reuse is theoretically possible.** Files are keyed by the `claude` process PID. If that PID is recycled by the OS for a *different* `claude` process after the original exits, a stale file could briefly be treated as live. This is extremely unlikely and self-heals on the new session's next state-writing hook. A recycled PID belonging to a non-`claude` process is reaped normally.

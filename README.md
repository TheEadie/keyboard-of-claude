# keyboard-of-claude

Ambient G213 status light for Claude Code sessions on WSL2/Windows.

A tray app watches a signal directory on Windows; Claude Code hooks write per-session state files into that directory. The tray app reads the files and sets the keyboard colour accordingly — amber when a turn finishes, with more states added by later slices.

## Repo layout

| Path | Purpose |
|---|---|
| `src/KeyboardOfClaude.Tray` | Windows tray app that watches the signal directory and drives the G213 over HID |
| `scripts/signal.sh` | Reusable WSL signal script: writes a per-session state file into the signal directory |
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
            "command": "jq -r '.session_id // empty' | xargs -r -I {} bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh turn-done {}"
          }
        ]
      }
    ]
  }
}
```

If you already have a `hooks` block or a `Stop` array in your `~/.claude/settings.json`, append this matcher object to the existing `Stop` array rather than replacing the whole block.

**How the hook works:**

- The `Stop` event fires when a Claude Code turn completes. Claude Code passes a JSON payload on stdin that includes `session_id`.
- `jq -r '.session_id // empty'` extracts the session id (produces no output if the field is absent).
- `xargs -r` skips invoking the script when the session id is empty, so no stray file is written.
- The script is invoked as `bash <abs-path> turn-done <session-id>`, matching `signal.sh`'s `<state> <session-id>` argument order.
- Only the `Stop` event is wired — `SubagentStop` and other events are not used by this slice.

**Note:** The absolute path `/home/eadie/code/keyboard-of-claude/scripts/signal.sh` is specific to this machine's WSL layout. Adjust it if the repo lives at a different path.

### How it works / verifying

With the hook installed and the tray app running, finishing a turn in any Claude session:

1. Calls the hook command, which passes the session id to `scripts/signal.sh turn-done <session-id>`.
2. The script resolves `%LOCALAPPDATA%` via `cmd.exe`, converts the path to a Linux mount via `wslpath`, and writes a file at `%LOCALAPPDATA%\keyboard-of-claude\signals\<session-id>` containing the text `turn-done`.
3. The tray app detects the new file and turns the G213 keyboard amber.

The script is fail-silent: if `%LOCALAPPDATA%` cannot be resolved, WSL↔Windows interop is unavailable, the directory cannot be created, or the write fails, the script exits 0 with no error surfaced to the Claude session.

**The keyboard stays amber** until a later slice (or manual file removal) clears it — clearing the signal on user reply is out of scope for this slice.

## Claude Code hooks — blocked (red) and clearing back to green

### Prerequisite

The tray app must be running. Build and launch it from `src/KeyboardOfClaude.Tray`. The Stop hook from the previous section should also be installed.

### Three hooks to add

Add the following entries to your **user-level** `~/.claude/settings.json`. These hooks complement the `Stop` hook above and complete the full colour cycle.

If you already have a `Notification`, `UserPromptSubmit`, or `PostToolUse` array in your `hooks` block, **append** each matcher object to the existing array rather than replacing it. If those arrays do not exist yet, add them alongside the existing `Stop` array.

```json
{
  "hooks": {
    "Notification": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "jq -r 'select((.message // \"\") | ascii_downcase | contains(\"permission\")) | .session_id // empty' | xargs -r -I {} bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh blocked {}"
          }
        ]
      }
    ],
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "jq -r '.session_id // empty' | xargs -r -I {} bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh clear {}"
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "jq -r '.session_id // empty' | xargs -r -I {} bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh clear {}"
          }
        ]
      }
    ]
  }
}
```

**Note:** The absolute path `/home/eadie/code/keyboard-of-claude/scripts/signal.sh` is specific to this machine's WSL layout. Adjust it if the repo lives at a different path.

**How each hook works:**

- **`Notification` (blocked → red):** Fires when Claude Code raises a notification. The `jq` pipeline reads `message` from stdin and uses `select(... | contains("permission"))` to filter — only messages containing "permission" (case-insensitive) proceed. When matched, `session_id` is passed to `signal.sh blocked`, writing a `blocked` file for the session. Non-permission notifications (e.g. idle "waiting for your input") produce no output from `jq`, so `xargs -r` skips the script entirely and the session state is unchanged.
- **`UserPromptSubmit` (clear → green on reply):** Fires when the user submits a prompt. Calls `signal.sh clear` with the session id, deleting that session's signal file so the keyboard moves back towards green. Idempotent — works even if no signal file exists yet (e.g. the very first prompt of a session).
- **`PostToolUse` (clear → green on resume):** Fires after every tool call completes. Calls `signal.sh clear` with the session id — this ensures that when Claude resumes working after a permission approval (which does not fire `UserPromptSubmit`), the `blocked` state is cleared as soon as the approved tool runs. Since this fires on every tool call, the repeated `cmd.exe` path-resolution is accepted as-is; the fail-silent contract keeps this safe.

### How it works / verifying

With all four hooks installed (Stop + the three above) and the tray app running:

1. **Permission prompt → red:** When Claude Code requests permission to use a tool, the `Notification` event fires with a message containing "permission". The hook writes `blocked` for the session; the tray app detects this and turns the keyboard **red**.
2. **User reply → green:** When you submit your reply (`UserPromptSubmit`), the hook deletes the session's signal file; the tray returns to **green** (assuming no other sessions are blocked or waiting).
3. **Resume after approval → green:** When you approve the tool and Claude resumes, `PostToolUse` fires on the next tool completion, clearing the `blocked` file and returning the keyboard to **green**.
4. **Turn completes → amber:** When Claude finishes its turn, the `Stop` hook (from the previous section) writes `turn-done`; the keyboard turns **amber**.
5. **Concurrent sessions:** Each session's file is keyed by its `session_id`. The tray app aggregates across all files using max-urgency logic (red > amber > green), so the keyboard always reflects the most urgent active session.

All hooks are fail-silent: if `%LOCALAPPDATA%` cannot be resolved, WSL↔Windows interop is unavailable, or a write/delete fails, the script exits 0 with no error surfaced to the Claude session.

### Known limitations

These are accepted tradeoffs of wiring the colour cycle to Claude Code's event model. They are out of scope for this slice but worth knowing:

- **`Notification` matching depends on wording.** The `blocked → red` hook only fires for notifications whose message contains the substring `permission` (case-insensitive). If a future Claude Code version rephrases or localises its permission prompt, the keyboard may stop turning red; conversely, an unrelated notification that happens to contain the word "permission" would falsely turn it red.
- **`PostToolUse` clears unconditionally.** Because `PostToolUse` deletes the session file after *every* tool call (not just an approved one), it can race other hooks. If a tool finishing (clear) interleaves after a near-simultaneous permission prompt (`blocked`) or end-of-turn (`turn-done`) write, the just-written state can be erased and the keyboard wrongly drops to green. Hooks run as independent processes with no ordering guarantee.
- **Denied permissions are not cleared automatically.** If you *deny* a permission request, no tool runs (so `PostToolUse` does not fire) and you may not submit a new prompt (so `UserPromptSubmit` does not fire). The `blocked` file then lingers and the keyboard stays red until the next prompt or tool call clears it.
- **No session-end cleanup.** A session that ends (crash, closed terminal) without a final `UserPromptSubmit`/`PostToolUse` leaves its `turn-done` or `blocked` file behind. Because the tray aggregates with max-urgency logic, a single stale file can pin the keyboard amber/red across all sessions. Remove stale files manually from the signal directory if this happens.

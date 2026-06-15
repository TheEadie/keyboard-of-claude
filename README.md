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

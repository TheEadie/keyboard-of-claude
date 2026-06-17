# keyboard-of-claude

Ambient G213 status light for Claude Code sessions on WSL2/Windows.

A tray app watches a signal directory; Claude Code hooks write one state file per running Claude process. The keyboard goes **amber** while Claude is working, **flashing red** when a session needs you (permission prompt or finished turn), and **green** when idle.

## 1. Install

Run this from any WSL2 terminal. It clones/updates the repo, publishes the tray app, registers it to auto-start at Windows login, and launches it:

```bash
curl -fsSL https://raw.githubusercontent.com/TheEadie/keyboard-of-claude/main/scripts/install.sh | bash
```

**Prerequisites:** WSL2 with `git` and `jq`, the Windows .NET 10 SDK (`dotnet.exe`) on PATH, and SSH git access to GitHub.

> **Warning:** the installer **hard-resets** the `~/code/keyboard-of-claude` checkout to `origin/main`, discarding local changes there. Commit and push first if you develop in that checkout.

## 2. Add the hooks

The hooks turn the keyboard amber/red/green from Claude Code events. Add them to your **user-level** `~/.claude/settings.json` so they apply to every session.

If you already have a `hooks` block, merge these entries into it (append to existing event arrays rather than replacing them).

```json
{
  "hooks": {
    "UserPromptSubmit": [
      { "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh working" }] }
    ],
    "PostToolUse": [
      { "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh working" }] }
    ],
    "Notification": [
      { "matcher": "permission_prompt", "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh blocked" }] },
      { "matcher": "idle_prompt", "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh turn-done" }] }
    ],
    "PermissionRequest": [
      { "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh blocked" }] }
    ],
    "Stop": [
      { "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh turn-done" }] }
    ],
    "SessionStart": [
      { "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh reap" }] }
    ],
    "SessionEnd": [
      { "hooks": [{ "type": "command", "command": "bash ~/code/keyboard-of-claude/scripts/signal.sh clear" }] }
    ]
  }
}
```

> **Note:** these hooks assume the repo lives at `~/code/keyboard-of-claude`. Adjust the path if it's elsewhere.

That's it. Submit a prompt and the keyboard should turn amber.

## What each hook does

| Event | State | Keyboard |
|---|---|---|
| `UserPromptSubmit` | `working` | amber while Claude works |
| `PostToolUse` | `working` | stays amber; clears `blocked` after you approve a tool |
| `PermissionRequest` | `blocked` | flashing red — a permission dialog is open, incl. the `/plan` approval prompt |
| `Notification` (`permission_prompt`) | `blocked` | flashing red |
| `Notification` (`idle_prompt`) | `turn-done` | flashing red — idle, awaiting you |
| `Stop` | `turn-done` | flashing red — turn finished, awaiting you |
| `SessionStart` | `reap` | drops stale files (incl. on `/clear`, `/compact`) |
| `SessionEnd` | `clear` | back to green on clean exit |

State files are keyed by the owning `claude` process PID. The tray app aggregates across all sessions using max-urgency (flashing red > amber > green). All hooks are fail-silent — they exit 0 even if WSL↔Windows interop is unavailable.

**Stuck colour?** Right-click the tray icon and choose **Reset** to clear all signal files and repaint. It's a momentary clear — live sessions re-signal on their next event.

## Repo layout

| Path | Purpose |
|---|---|
| `src/KeyboardOfClaude.Tray` | Windows tray app that watches the signal directory and drives the G213 over HID |
| `scripts/install.sh` | One-line installer (publish + auto-start + launch) |
| `scripts/signal.sh` | WSL signal script: writes/clears state files and reaps dead processes |
| `spike/` | Throwaway HID proof-of-concept |

# keyboard-of-claude

Ambient G213 status light for Claude Code sessions on WSL2/Windows.

A tray app watches a signal directory on Windows; Claude Code hooks write one state file per running Claude process into that directory. The tray app reads the files and sets the keyboard colour accordingly — amber while work is in progress, flashing red when a session needs you (blocked on a permission prompt, or a turn has finished), green when idle.

## Repo layout

| Path | Purpose |
|---|---|
| `src/KeyboardOfClaude.Tray` | Windows tray app that watches the signal directory and drives the G213 over HID |
| `scripts/signal.sh` | WSL signal script: writes/clears a state file keyed by the owning `claude` process, and reaps files for dead processes |
| `spike/` | Throwaway HID proof-of-concept (see its own README) |

## Install

Run the one-liner below from any WSL2 terminal. It installs the tray app self-contained and registers it to auto-start at every Windows login — no manual launch step required afterwards.

```bash
curl -fsSL https://raw.githubusercontent.com/TheEadie/keyboard-of-claude/main/scripts/install.sh | bash
```

> **Warning: hard-reset.** Running the installer **hard-resets** the `~/code/keyboard-of-claude` checkout to `origin/main`, discarding any local commits and uncommitted changes in that directory. If you are developing in that checkout, commit and push your work first.

### What it does

- **Source:** Clones the repo to `~/code/keyboard-of-claude` if absent; otherwise hard-resets it to `origin/main` so the build always matches the latest remote state.
- **Stop:** Stops any already-running tray instance (its locked exe would otherwise block the publish).
- **Publish:** Publishes the tray app self-contained, single-file, `win-x64` into `%LOCALAPPDATA%\keyboard-of-claude\app` using the Windows `dotnet.exe` toolchain.
- **Shortcut:** Creates (or refreshes) a shortcut in the Windows Startup folder pointing at the published exe — idempotent, replaces any prior shortcut.
- **Launch:** Relaunches the freshly published app immediately, with no log-out/log-in required.

### Prerequisites

- WSL2 with `git` installed.
- Windows .NET 10 SDK (`dotnet.exe`) on PATH.
- Standard WSL2 Windows interop (`cmd.exe`, `wslpath`, `powershell.exe`) — present on all default WSL2 installs.
- SSH-authenticated git access to GitHub (the repo URL uses the SSH form).

Hook setup (turning the keyboard amber/red/green from Claude Code events) remains the manual steps documented in the sections below — hook installation is intentionally out of scope for this installer.

## Claude Code hooks — turn-done (flashing red)

### Prerequisite

The tray app must be running. Build and launch it from `src/KeyboardOfClaude.Tray`.

### Stop hook — flashes the keyboard red when a turn finishes

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
3. The tray app detects the new file and flashes the G213 keyboard red (alternating red and off).

The script is fail-silent: if `%LOCALAPPDATA%` cannot be resolved, WSL↔Windows interop is unavailable, the directory cannot be created, or the write fails, the script exits 0 with no error surfaced to the Claude session.

**The keyboard keeps flashing red** until the session next signals another state (e.g. `working` on the next prompt) or the file is cleared.

## Claude Code hooks — blocked (flashing red), working (amber), and clearing back to green

### Prerequisite

The tray app must be running. Build and launch it from `src/KeyboardOfClaude.Tray`. The Stop hook from the previous section should also be installed.

### Three hooks to add

Add the following entries to your **user-level** `~/.claude/settings.json`. These hooks complement the `Stop` hook above and complete the full colour cycle.

If you already have a `Notification`, `UserPromptSubmit`, `PostToolUse`, `SessionStart`, or `SessionEnd` array in your `hooks` block, **append** each matcher object to the existing array rather than replacing it. If those arrays do not exist yet, add them alongside the existing `Stop` array.

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
            "command": "bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh working"
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh working"
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
    ],
    "SessionEnd": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash /home/eadie/code/keyboard-of-claude/scripts/signal.sh clear"
          }
        ]
      }
    ]
  }
}
```

**Note:** The absolute path `/home/eadie/code/keyboard-of-claude/scripts/signal.sh` is specific to this machine's WSL layout. Adjust it if the repo lives at a different path.

**How each hook works:**

- **`Notification` (blocked → flashing red):** Fires when Claude Code raises a notification. `jq -e 'select(... | contains("permission"))'` reads `message` from stdin and exits non-zero unless the message contains "permission" (case-insensitive), so `signal.sh blocked` runs only for permission prompts. Non-permission notifications (e.g. idle "waiting for your input") leave the state unchanged.
- **`UserPromptSubmit` (working → amber while Claude works):** Fires when the user submits a prompt. Calls `signal.sh working`, writing this Claude process's signal file so the keyboard goes amber for the duration of the turn. This overwrites any previous state (e.g. a lingering `turn-done` from the last turn).
- **`PostToolUse` (working → amber on resume):** Fires after every tool call completes. Calls `signal.sh working` — this keeps the keyboard amber while Claude is working and, in particular, clears a `blocked` state when Claude resumes after a permission approval (which does not fire `UserPromptSubmit`) by overwriting it with `working` as soon as the approved tool runs. Since this fires on every tool call, the repeated `cmd.exe` path-resolution is accepted as-is; the fail-silent contract keeps this safe.
- **`SessionStart` (reap → drop stale files):** Fires when a session begins, including on `/clear`, `/compact`, resume, and startup. Calls `signal.sh reap`, which (a) clears this Claude process's own file — so `/clear`/`/compact` immediately drop a stale `turn-done`/`blocked` left from the previous conversation — and (b) removes any signal file whose owning `claude` PID is no longer alive, cleaning up after crashed or closed sessions. Reaping is purely liveness-based (it checks whether the process exists, never how old the file is), so a session that is legitimately waiting on you stays lit indefinitely.
- **`SessionEnd` (clear → green on clean exit):** Fires when a session ends cleanly (normal exit, logout, `/clear`, `/compact`). Calls `signal.sh clear`, deleting this Claude process's own file so the keyboard returns toward green the moment the session closes — rather than waiting for the next session's `SessionStart` reap. It fires on every `SessionEnd` reason with no filtering; `clear` is idempotent and process-scoped, so overlapping with the `SessionStart` reaper on `/clear`/`/compact` is harmless.

### How it works / verifying

With all six hooks installed (Stop + the five above) and the tray app running:

1. **Prompt submitted → amber:** When you submit a prompt (`UserPromptSubmit`), the hook writes `working`; the tray app turns the keyboard **amber** for as long as Claude is working.
2. **Permission prompt → flashing red:** When Claude Code requests permission to use a tool, the `Notification` event fires with a message containing "permission". The hook writes `blocked` for the session; the tray app detects this and **flashes the keyboard red**.
3. **Resume after approval → amber:** When you approve the tool and Claude resumes, `PostToolUse` fires on the next tool completion, overwriting the `blocked` file with `working` and returning the keyboard to **amber**.
4. **Turn completes → flashing red:** When Claude finishes its turn, the `Stop` hook (from the previous section) writes `turn-done`; the keyboard **flashes red** to signal it is awaiting you.
5. **`/clear`, `/compact`, or clean exit → green:** Starting a fresh conversation fires `SessionStart`, whose `reap` drops the previous conversation's `turn-done`/`blocked`/`working` file (same process, same slot); `SessionEnd` clears the file on clean exit. Either way the keyboard returns to **green** instead of pinning on a stale state.
6. **Concurrent sessions:** Each file is keyed by the owning `claude` process PID, so one running Claude (one terminal) maps to exactly one file. The tray app aggregates across all files using max-urgency logic (flashing red > amber > green), so the keyboard always reflects the most urgent active session.

All hooks are fail-silent: if `%LOCALAPPDATA%` cannot be resolved, WSL↔Windows interop is unavailable, or a write/delete fails, the script exits 0 with no error surfaced to the Claude session.

### Known limitations

These are accepted tradeoffs of wiring the colour cycle to Claude Code's event model. They are out of scope for this slice but worth knowing:

- **`Notification` matching depends on wording.** The `blocked → red` hook only fires for notifications whose message contains the substring `permission` (case-insensitive). If a future Claude Code version rephrases or localises its permission prompt, the keyboard may stop turning red; conversely, an unrelated notification that happens to contain the word "permission" would falsely turn it red.
- **`PostToolUse` writes `working` unconditionally.** Because `PostToolUse` writes `working` after *every* tool call (not just an approved one), it can race other hooks. If a tool finishing (`working`) interleaves after a near-simultaneous permission prompt (`blocked`) or end-of-turn (`turn-done`) write, the just-written attention state can be overwritten and the keyboard wrongly drops to amber. Hooks run as independent processes with no ordering guarantee.
- **Denied permissions are not cleared automatically.** If you *deny* a permission request, no tool runs (so `PostToolUse` does not fire) and you may not submit a new prompt (so `UserPromptSubmit` does not fire). The `blocked` file then lingers and the keyboard keeps flashing red until the next prompt or tool call overwrites it.
- **Abrupt-termination cleanup is deferred, not immediate.** A session that ends *cleanly* now clears its own file at once via the `SessionEnd` hook. But a session that dies *abruptly* — crash, `SIGKILL`, or a terminal closed in a way that does not fire `SessionEnd` — leaves its file behind until the next `SessionStart` reap runs, i.e. when any Claude session next starts. Until then the stale file can pin the keyboard via max-urgency aggregation. The reap is keyed on process liveness, so it only ever removes files for PIDs that are genuinely gone; a long-waiting live session is never cleared. (The tray "Reset" action below is the manual escape hatch for clearing such a lingering file on demand.)
- **PID reuse is theoretically possible.** Files are keyed by the `claude` process PID. If that PID is recycled by the OS for a *different* `claude` process after the original exits, a stale file could briefly be treated as live. This is extremely unlikely and self-heals on the new session's next state-writing hook. A recycled PID belonging to a non-`claude` process is reaped normally.

### Tray menu — Reset

Right-click the tray icon and choose **Reset** to immediately clear all signal files and return the keyboard to green. This is a manual escape hatch for a stuck colour — e.g. a `blocked` (red) state left lingering after a *denied* permission, or a stale file from an abruptly-terminated session that has not yet been reaped. Reset deletes every file in the signal directory (best-effort; locked or vanished files are skipped) and repaints from the result. It is a **momentary** clear, not a persistent mute: any session that is still live will re-signal its state on its next hook event and the keyboard will repaint accordingly. There is no confirmation dialog.

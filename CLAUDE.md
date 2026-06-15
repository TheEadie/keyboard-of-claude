# keyboard-of-claude

Ambient G213 status light for Claude Code — paints the keyboard green/amber/red based on per-session signal files. Runs on WSL2 on Windows.

## Repo structure

| Path | Purpose |
|---|---|
| `src/KeyboardOfClaude.Tray` | The tray app (primary deliverable) |
| `spike/g213-hid-proof` | Throwaway HID proof-of-concept — do not depend on it |

## Build & run

This project targets `net10.0-windows` and must be built with the Windows `dotnet.exe` toolchain. Linux `dotnet` does not support WinForms and MSBuild rejects Linux paths. Run the commands below from the repo root; `dotnet.exe` resolves the relative project path against the current WSL directory.

**Build:**
```
dotnet.exe build src/KeyboardOfClaude.Tray/KeyboardOfClaude.Tray.csproj -c Release
```

**Run:**
```
dotnet.exe run --project src/KeyboardOfClaude.Tray/KeyboardOfClaude.Tray.csproj -c Release
```

If `dotnet.exe` cannot resolve the relative path from WSL, pass the project as a UNC path instead (`\\wsl.localhost\<distro>\...`). The distro name here is `Ubuntu` (not `Ubuntu-24.04`).

## G213 HID protocol (verified)

- **VID** `0x046D` / **PID** `0xC336`
- Transport: HID **output report** via `WriteFile` (NOT `HidD_SetFeature` — that fails `ERROR_INVALID_FUNCTION`)
- Target the **vendor lighting collection**: usage page `0xFF43`, usage `0x0602`
  - Select by matching `HidP_GetCaps` results against these values
- Open **non-exclusively** (`FILE_SHARE_READ | FILE_SHARE_WRITE`) so typing keeps working
- **Report format** (20 bytes, zero-padded to collection's declared output-report length):
  `11 FF 0C 3A <zone> 01 <R> <G> <B> 02 00...`
  - Byte [9] = `0x02` is required
- **Five zones** `0x01`–`0x05`; pace writes ~20 ms apart — back-to-back drops zones
- No commit packet required

See `src/KeyboardOfClaude.Tray/HidNative.cs` (P/Invoke layer) and `src/KeyboardOfClaude.Tray/G213Keyboard.cs` (device wrapper).

## Signal directory

`%LOCALAPPDATA%\keyboard-of-claude\signals` (created on startup if missing)

From WSL: `/mnt/c/Users/<you>/AppData/Local/keyboard-of-claude/signals`

Each file in the directory represents one Claude Code session. File **content** (whitespace-trimmed, case-insensitive) encodes state:

| Content | Meaning | Keyboard colour |
|---|---|---|
| `blocked` | Session needs permission | Red `(255, 0, 0)` |
| `turn-done` | Session awaiting user | Amber `(255, 128, 0)` |
| _(anything else / empty)_ | No action needed | Green `(0, 255, 0)` |

The app paints the colour for the highest-urgency state across all files (`blocked` > `turn-done` > green).

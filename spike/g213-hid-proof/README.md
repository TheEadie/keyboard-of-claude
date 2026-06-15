# g213-hid-proof

This is a **throwaway spike** that proves the Logitech G213 keyboard's backlight can be controlled from a Windows process using raw HID output reports — with no Logitech G HUB or vendor SDK installed, and without interrupting normal typing (non-exclusive HID access). It is the foundational gate for epic #1. The code is intentionally disposable; the tray app (slice #3) will re-implement the HID logic cleanly.

## Protocol (verified against OpenRGB + the physical device)

Lighting commands are sent as **HID output reports** (`WriteFile`, ≈ hidapi `hid_write`), **not** feature reports — `HidD_SetFeature` fails with `ERROR_INVALID_FUNCTION`. They must target the vendor collection on **interface 1, usage page `0xFF43`, usage `0x0602`** (selected via `HidP_GetCaps`). Each of the 5 zones (`0x01`–`0x05`) gets a 20-byte report `11 FF 0C 3A <zone> 01 <R> <G> <B> 02 00…`, with a short delay between writes (the device drops zones if they are sent back-to-back). No commit packet is needed.

## Build and publish (from WSL)

The Windows `dotnet.exe` toolchain requires UNC paths (Linux paths are rejected by MSBuild as unknown switches).

```bash
# Build (compile check)
dotnet.exe build "\\\\wsl.localhost\\Ubuntu\\home\\eadie\\code\\keyboard-of-claude\\spike\\g213-hid-proof\\g213-hid-proof.csproj" -c Release

# Publish self-contained single-file win-x64 EXE
dotnet.exe publish "\\\\wsl.localhost\\Ubuntu\\home\\eadie\\code\\keyboard-of-claude\\spike\\g213-hid-proof\\g213-hid-proof.csproj" -c Release -r win-x64 --self-contained true -o "\\\\wsl.localhost\\Ubuntu\\home\\eadie\\code\\keyboard-of-claude\\spike\\g213-hid-proof\\publish"
```

## Run (from a Windows terminal)

```
\\wsl.localhost\Ubuntu\home\eadie\code\keyboard-of-claude\spike\g213-hid-proof\publish\g213-hid-proof.exe
```

Or from WSL (WSL launches Windows EXEs directly as real Windows processes with USB-device access):

```bash
"/home/eadie/code/keyboard-of-claude/spike/g213-hid-proof/publish/g213-hid-proof.exe"
```

## Expected output

On success the keyboard turns uniform blue and the process prints:

```
G213 set to blue (R=0 G=0 B=255).
```

Exit code `0` on success; `1` if the keyboard is not found; `2` if the HID write fails; `3` for an unexpected error.

## Results

| Field | Value |
|---|---|
| Date run | 2026-06-15 |
| Machine / OS | Windows + WSL2 (Ubuntu); built/run via Windows `dotnet.exe` |
| Keyboard turned blue | Yes — all 5 zones uniform blue |
| Typing continued to work during/after | Yes (non-exclusive HID; only the vendor lighting collection is opened) |
| Exit code | 0 |
| Notes | Required protocol fixes vs. the plan: HID **output** reports (not feature reports), target the vendor collection (interface 1, usage page `0xFF43`, usage `0x0602`), byte `[0x09]=0x02`, and a short delay between zone writes (back-to-back writes drop zones — only 2/5 lit without pacing). |

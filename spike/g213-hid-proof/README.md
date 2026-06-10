# g213-hid-proof

This is a **throwaway spike** that proves the Logitech G213 keyboard's backlight can be controlled from a Windows process using raw HID feature reports — with no Logitech G HUB or vendor SDK installed, and without interrupting normal typing (non-exclusive HID access). It is the foundational gate for epic #1. The code is intentionally disposable; the tray app (slice #3) will re-implement the HID logic cleanly.

## Build and publish (from WSL)

The Windows `dotnet.exe` toolchain requires UNC paths (Linux paths are rejected by MSBuild as unknown switches).

```bash
# Build (compile check)
dotnet.exe build "\\\\wsl.localhost\\Ubuntu-24.04\\home\\eadie\\code\\keyboard-of-claude\\spike\\g213-hid-proof\\g213-hid-proof.csproj" -c Release

# Publish self-contained single-file win-x64 EXE
dotnet.exe publish "\\\\wsl.localhost\\Ubuntu-24.04\\home\\eadie\\code\\keyboard-of-claude\\spike\\g213-hid-proof\\g213-hid-proof.csproj" -c Release -r win-x64 --self-contained true -o "\\\\wsl.localhost\\Ubuntu-24.04\\home\\eadie\\code\\keyboard-of-claude\\spike\\g213-hid-proof\\publish"
```

## Run (from a Windows terminal)

```
\\wsl.localhost\Ubuntu-24.04\home\eadie\code\keyboard-of-claude\spike\g213-hid-proof\publish\g213-hid-proof.exe
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
| Date run | |
| Machine / OS | |
| Keyboard turned blue | |
| Typing continued to work during/after | |
| Exit code | |
| Notes | |

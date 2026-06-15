#!/usr/bin/env bash
# install.sh — Full install/upgrade of the keyboard-of-claude tray app.
#
# Downloads (or hard-resets) the source to ~/code/keyboard-of-claude, publishes
# the tray app self-contained single-file win-x64 into
# %LOCALAPPDATA%\keyboard-of-claude\app, creates/refreshes a Windows Startup
# folder shortcut, and relaunches the app.
#
# Fail-loud contract: every phase exits non-zero with a clear message on failure.
# This is the inverse of signal.sh, which is fail-silent.
#
# Safe to pipe from bash: does not depend on $0 or BASH_SOURCE location.

set -euo pipefail

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

fail() {
    echo "install.sh: ERROR: $*" >&2
    exit 1
}

step() {
    echo "install.sh: $*"
}

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

REPO_URL="git@github.com:TheEadie/keyboard-of-claude.git"
REPO_DIR="$HOME/code/keyboard-of-claude"
BRANCH="main"
PROJECT_REL="src/KeyboardOfClaude.Tray/KeyboardOfClaude.Tray.csproj"
EXE_NAME="KeyboardOfClaude.Tray.exe"

# ---------------------------------------------------------------------------
# Phase A — Prerequisite checks (fail loud)
# ---------------------------------------------------------------------------

step "Phase A: Checking prerequisites..."

command -v git >/dev/null 2>&1 || fail "git is not available on PATH. Install git in WSL and retry."
command -v dotnet.exe >/dev/null 2>&1 || fail "dotnet.exe is not available on PATH. Install the Windows .NET 10 SDK and ensure it is on the PATH accessible from WSL."
command -v cmd.exe >/dev/null 2>&1 || fail "cmd.exe is not available. WSL Windows interop appears to be disabled."
command -v wslpath >/dev/null 2>&1 || fail "wslpath is not available. This script requires WSL2."

# Resolve powershell.exe: prefer PATH, fall back to known System32 location.
if command -v powershell.exe >/dev/null 2>&1; then
    PWSH="$(command -v powershell.exe)"
elif [ -f "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe" ]; then
    PWSH="/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
else
    fail "powershell.exe is not on PATH and was not found at the standard System32 location. WSL Windows interop may be broken."
fi

# Resolve taskkill.exe: prefer PATH, fall back to known System32 location.
if command -v taskkill.exe >/dev/null 2>&1; then
    TASKKILL="$(command -v taskkill.exe)"
elif [ -f "/mnt/c/Windows/System32/taskkill.exe" ]; then
    TASKKILL="/mnt/c/Windows/System32/taskkill.exe"
else
    fail "taskkill.exe is not on PATH and was not found at the standard System32 location."
fi

step "Prerequisites OK (powershell.exe=$PWSH, taskkill.exe=$TASKKILL)"

# ---------------------------------------------------------------------------
# Phase B — Resolve Windows paths
# ---------------------------------------------------------------------------

step "Phase B: Resolving Windows paths..."

localappdata_win="$(cmd.exe /c 'echo %LOCALAPPDATA%' | tr -d '\r')"
[ -n "$localappdata_win" ] || fail "Could not resolve %LOCALAPPDATA% — cmd.exe returned empty."
[ "$localappdata_win" != '%LOCALAPPDATA%' ] || fail "Could not resolve %LOCALAPPDATA% — cmd.exe returned the literal string."

localappdata_unix="$(wslpath -u "$localappdata_win")"
[ -n "$localappdata_unix" ] || fail "wslpath failed to convert LOCALAPPDATA path: $localappdata_win"

# Linux path for the app dir (used for post-publish existence checks).
app_dir_unix="$localappdata_unix/keyboard-of-claude/app"

# Windows path for the app dir (passed to dotnet.exe -o and the shortcut).
app_dir_win="${localappdata_win}\\keyboard-of-claude\\app"

appdata_win="$(cmd.exe /c 'echo %APPDATA%' | tr -d '\r')"
[ -n "$appdata_win" ] || fail "Could not resolve %APPDATA% — cmd.exe returned empty."
[ "$appdata_win" != '%APPDATA%' ] || fail "Could not resolve %APPDATA% — cmd.exe returned the literal string."

startup_win="${appdata_win}\\Microsoft\\Windows\\Start Menu\\Programs\\Startup"
startup_unix="$(wslpath -u "$startup_win")"
[ -n "$startup_unix" ] || fail "wslpath failed to convert Startup path: $startup_win"

step "Resolved app dir (Windows): $app_dir_win"
step "Resolved Startup dir (Windows): $startup_win"

# ---------------------------------------------------------------------------
# Phase C — Source acquisition
# ---------------------------------------------------------------------------

step "Phase C: Ensuring source at $REPO_DIR..."

if [ -d "$REPO_DIR" ] && ! git -C "$REPO_DIR" rev-parse --git-dir >/dev/null 2>&1; then
    fail "$REPO_DIR exists but is not a git repository. Remove or rename it and retry."
fi

if ! git -C "$REPO_DIR" rev-parse --git-dir >/dev/null 2>&1; then
    step "Cloning $REPO_URL -> $REPO_DIR"
    git clone "$REPO_URL" "$REPO_DIR" || fail "git clone failed. Check your SSH key and network access to github.com."
else
    step "Fetching latest from origin..."
    git -C "$REPO_DIR" fetch origin --prune || fail "git fetch failed. Check network access and SSH authentication."

    step "Checking out $BRANCH..."
    git -C "$REPO_DIR" checkout -B "$BRANCH" "origin/$BRANCH" || fail "git checkout of origin/$BRANCH failed."

    step "Hard-resetting to origin/$BRANCH (local changes will be discarded)..."
    git -C "$REPO_DIR" reset --hard "origin/$BRANCH" || fail "git reset --hard failed."

    # Remove stray untracked source files to keep the build deterministic.
    # We deliberately omit -x so .gitignore'd build artefacts (bin/, obj/, publish/)
    # are preserved — nuking them would slow the next build without improving correctness.
    git -C "$REPO_DIR" clean -fd || fail "git clean failed."
fi

step "Source acquisition complete."

# ---------------------------------------------------------------------------
# Phase D — Stop running instance
# ---------------------------------------------------------------------------

step "Phase D: Stopping any running tray instance..."

# taskkill exits non-zero when no matching process is running, which would
# abort the script under set -e. Guard it with an explicit if/else.
if "$TASKKILL" /IM "$EXE_NAME" /F >/dev/null 2>&1; then
    step "Stopped running instance."
else
    step "No running instance to stop."
fi

# Give Windows a moment to release the file lock before we overwrite the exe.
sleep 1

# ---------------------------------------------------------------------------
# Phase E — Publish
# ---------------------------------------------------------------------------

step "Phase E: Publishing self-contained single-file win-x64..."

# Pass the Windows-form output path to dotnet.exe (it is a Windows tool and
# expects Windows paths for both the project and the output directory).
# Convert the project path via wslpath so dotnet.exe receives a valid UNC path
# (\\wsl.localhost\...) — Linux paths are rejected by MSBuild with "Unknown switch".
project_win="$(wslpath -w "$REPO_DIR/$PROJECT_REL")"
[ -n "$project_win" ] || fail "wslpath failed to convert project path: $REPO_DIR/$PROJECT_REL"

dotnet.exe publish "$project_win" \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$app_dir_win" \
    || fail "dotnet.exe publish failed."

# Verify the exe was produced.
[ -f "$app_dir_unix/$EXE_NAME" ] || fail "Publish did not produce $EXE_NAME in $app_dir_unix"

step "Publish complete: $app_dir_unix/$EXE_NAME"

# ---------------------------------------------------------------------------
# Phase F — Startup shortcut (idempotent)
# ---------------------------------------------------------------------------

step "Phase F: Creating Startup folder shortcut..."

lnk_win="${startup_win}\\keyboard-of-claude.lnk"
exe_win="${app_dir_win}\\${EXE_NAME}"

# Build the PowerShell -Command string. Each Windows path is wrapped in
# PowerShell single quotes to survive spaces (e.g. "Start Menu") and
# backslashes. We assemble the string in bash so backslashes are not mangled.
#
# PowerShell escapes a literal single quote inside a single-quoted string by
# doubling it (''), so we double any apostrophe in the paths first — otherwise
# a username like O'Brien (C:\Users\O'Brien\...) would terminate the quoted
# literal early and break the command.
#
# WScript.Shell.CreateShortcut followed by .Save() overwrites an existing .lnk
# at the given path, so re-running install replaces the one shortcut — never
# creates a duplicate.
lnk_ps="${lnk_win//\'/\'\'}"
exe_ps="${exe_win//\'/\'\'}"
app_dir_ps="${app_dir_win//\'/\'\'}"
PWSH_CMD="\$s = (New-Object -ComObject WScript.Shell).CreateShortcut('${lnk_ps}'); \$s.TargetPath = '${exe_ps}'; \$s.WorkingDirectory = '${app_dir_ps}'; \$s.Description = 'keyboard-of-claude tray app'; \$s.Save()"

"$PWSH" -NoProfile -NonInteractive -Command "$PWSH_CMD" \
    || fail "Startup shortcut creation failed (PowerShell exited non-zero)."

# Verify the shortcut was created.
[ -f "$startup_unix/keyboard-of-claude.lnk" ] || fail "Shortcut not found at $startup_unix/keyboard-of-claude.lnk after creation."

step "Startup shortcut created: $lnk_win"

# ---------------------------------------------------------------------------
# Phase G — Relaunch
# ---------------------------------------------------------------------------

step "Phase G: Relaunching tray app..."

# Launch detached via PowerShell Start-Process — NOT `cmd.exe /c start`.
# Under WSL interop, `cmd.exe /c start` blocks indefinitely: the long-running
# GUI process inherits the relayed stdio handles, so the bash call never sees
# EOF and hangs even though the app has launched. Start-Process spawns a fully
# detached Windows process and returns immediately. exe_ps/app_dir_ps are the
# single-quote-escaped paths built in Phase F.
#
# A non-zero exit is treated as a warning only — the install (publish +
# shortcut) has already succeeded at this point.
if "$PWSH" -NoProfile -NonInteractive -Command "Start-Process -FilePath '${exe_ps}' -WorkingDirectory '${app_dir_ps}'" >/dev/null 2>&1; then
    step "Tray app launched."
else
    echo "install.sh: WARNING: Could not auto-launch the tray app. The app has been published and the Startup shortcut is in place — it will start at next login." >&2
fi

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------

step "Install complete."
step "  App:      $app_dir_unix/$EXE_NAME"
step "  Shortcut: $startup_unix/keyboard-of-claude.lnk"
step "  The tray app is now running (or will start at next login)."

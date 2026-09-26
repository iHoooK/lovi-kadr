#!/usr/bin/env bash
# One-command local Debug build and launch for Git Bash on Windows.
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$project_root/src/PromptixCapture/PromptixCapture.csproj"
app="$project_root/src/PromptixCapture/bin/x64/Debug/net10.0-windows/win-x64/LoviKadr.exe"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET 10 SDK was not found. Install it, reopen Git Bash, and try again." >&2
  exit 1
fi

cd "$project_root"

if tasklist.exe //FI "IMAGENAME eq LoviKadr.exe" //NH | grep -qi "LoviKadr.exe"; then
  echo "ERROR: LoviKadr is already running. Close it from the tray, then run this command again." >&2
  exit 1
fi

dotnet build "$project" -c Debug -p:Platform=x64 --nologo

echo
echo "Starting local Debug build and opening its settings."
"$app" --settings

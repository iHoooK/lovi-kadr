#!/usr/bin/env bash
# One-command release build for Git Bash on Windows.
# Produces a portable folder and a single-file Windows installer in release/.
set -euo pipefail

script_directory="${BASH_SOURCE[0]%/*}"
if [[ "$script_directory" == "${BASH_SOURCE[0]}" ]]; then script_directory="."; fi
project_root="$(cd "$script_directory/.." && pwd)"

for tool in rm mkdir cp cygpath powershell.exe; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "ERROR: $tool was not found. Run this script from Git Bash." >&2
    exit 1
  fi
done

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET 10 SDK was not found. Install it, reopen Git Bash, and try again." >&2
  exit 1
fi

find_iscc() {
  local candidate
  for candidate in \
    "$(command -v ISCC.exe 2>/dev/null || true)" \
    "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" \
    "/c/Program Files/Inno Setup 6/ISCC.exe" \
    "${LOCALAPPDATA:-}/Programs/Inno Setup 6/ISCC.exe"; do
    if [[ -n "$candidate" && -f "$candidate" ]]; then
      printf '%s' "$candidate"
      return 0
    fi
  done
  return 1
}

iscc="$(find_iscc || true)"
if [[ -z "$iscc" ]]; then
  echo "ERROR: Inno Setup 6 was not found. Install it and try again." >&2
  exit 1
fi

cd "$project_root"
release_dir="$project_root/release"
portable_dir="$release_dir/LoviKadr-Portable"

if [[ "$release_dir" != "$project_root/release" || -L "$release_dir" ]]; then
  echo "ERROR: Refusing to replace a release directory outside this project." >&2
  exit 1
fi
rm -rf "$release_dir"
mkdir -p ./release/LoviKadr-Portable

dotnet run --project ./tests/PromptixCapture.Tests/PromptixCapture.Tests.csproj -c Release
dotnet publish ./src/PromptixCapture/PromptixCapture.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o "$portable_dir"

cp ./LICENSE ./PRIVACY.md ./THIRD_PARTY_NOTICES.md "$portable_dir/"
cp -R ./licenses "$portable_dir/licenses"

"$portable_dir/LoviKadr.exe" --self-test "$portable_dir/self-test-report.txt"
"$iscc" "/DSourceDir=$(cygpath -w "$portable_dir")" "$(cygpath -w "$project_root/installer/PromptixCapture.iss")"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$project_root/scripts/package-release.ps1")"

echo
echo "Release build finished. Distribute:"
echo "  release/LoviKadr-Setup-x64.exe  (recommended single installer)"
echo "  release/LoviKadr-Portable-x64.zip  (portable archive)"
echo "  release/SHA256SUMS.txt  (checksums for both downloads)"

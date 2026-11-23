#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
arch="${1:-$(uname -m)}"
mode="${2:-preview}"
output="${3:-$root/work/macos-$arch}"
case "$arch" in arm64) rid=osx-arm64 ;; x86_64) rid=osx-x64 ;; *) echo 'Architecture must be arm64 or x86_64' >&2; exit 2 ;; esac
case "$mode" in preview|release) ;; *) echo 'Mode must be preview or release' >&2; exit 2 ;; esac
[[ "$(uname -s)" == Darwin ]] || { echo 'macOS packaging requires a Mac.' >&2; exit 2; }
[[ ! -e "$output" || -d "$output" && -z "$(ls -A "$output")" ]] || { echo 'Output directory must be new or empty; existing files are preserved.' >&2; exit 2; }
command -v dotnet >/dev/null
xcrun --find clang >/dev/null
command -v python3 >/dev/null
if [[ "$mode" == release ]]; then
  python3 -c 'import sys; sys.path.insert(0,sys.argv[1]); from mac_package import release_requirements; import os; release_requirements(os.environ)' "$root/tools"
  [[ -n "${DESKPET_RELEASE_EVIDENCE:-}" ]] || { echo 'Release requires DESKPET_RELEASE_EVIDENCE.' >&2; exit 2; }
  [[ -f "$DESKPET_RELEASE_EVIDENCE" ]] || { echo 'Release evidence file does not exist.' >&2; exit 2; }
  DESKPET_RELEASE_EVIDENCE="$(python3 -c 'from pathlib import Path; import sys; print(Path(sys.argv[1]).resolve(strict=True))' "$DESKPET_RELEASE_EVIDENCE")"
fi
mkdir -p "$output"
output="$(cd "$output" && pwd)"
publish="$output/publish"
artifacts="$output/artifacts"
version="$(python3 -c 'import sys; sys.path.insert(0,sys.argv[1]); from mac_package import version_number; print(version_number())' "$root/tools")"
bash "$root/desktop/DeskPet.Platform.Mac/native/build.sh" "$arch"
cd "$root/desktop"
dotnet publish DeskPet.Desktop/DeskPet.Desktop.csproj -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  -p:Version="$version" -p:BuildInParallel=false -m:1 -nr:false -o "$publish"
sqlite_native="runtimes/$rid/native/libe_sqlite3.dylib"
cmp "$root/vendor/file-compat/sqlite/$sqlite_native" "$publish/$sqlite_native"
lipo "$publish/$sqlite_native" -verify_arch "$arch"
args=(--publish-dir "$publish" --output-directory "$artifacts" --arch "$arch" --mode "$mode" --commit "$(git -C "$root" rev-parse HEAD)")
if [[ "$mode" == release ]]; then args+=(--release-evidence "$DESKPET_RELEASE_EVIDENCE"); fi
python3 "$root/tools/mac_package.py" "${args[@]}"

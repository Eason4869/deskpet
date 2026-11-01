#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
arch="${1:-$(uname -m)}"
case "$arch" in arm64|x86_64) ;; *) echo 'Supported architectures: arm64, x86_64' >&2; exit 2;; esac
# build.sh compiles both DeskPetMac.m and the focused DeskPetObservation.m module.
bash build.sh "$arch"
mkdir -p build
xcrun clang -arch "$arch" -mmacosx-version-min=14.0 -fobjc-arc -fblocks -Wall -Wextra -Werror=implicit-function-declaration \
  native_library_self_test.m -o build/native_library_self_test \
  -L. -lDeskPetMac -Wl,-rpath,@loader_path/.. \
  -framework AppKit -framework AVFoundation -framework Carbon -framework Security -framework ScreenCaptureKit -framework ImageIO
lipo build/native_library_self_test -verify_arch "$arch"
# Bound CI waits without relying on GNU timeout, which is not bundled with macOS.
python3 - "$PWD/build/native_library_self_test" <<'PY'
import subprocess, sys
subprocess.run([sys.argv[1], "--audio-smoke"], check=True, timeout=60)
PY

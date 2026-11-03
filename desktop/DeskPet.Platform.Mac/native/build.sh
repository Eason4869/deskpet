#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
arch="${1:-$(uname -m)}"
case "$arch" in arm64|x86_64) ;; *) echo 'Supported architectures: arm64, x86_64' >&2; exit 2;; esac
[[ "$(uname -s)" == Darwin ]] || { echo 'Native bridge requires macOS and Xcode command line tools.' >&2; exit 2; }
xcrun clang -arch "$arch" -mmacosx-version-min=14.0 -fobjc-arc -fblocks -Wall -Wextra -Werror=implicit-function-declaration \
  -dynamiclib DeskPetMac.m DeskPetObservation.m -o libDeskPetMac.dylib \
  -install_name @rpath/libDeskPetMac.dylib \
  -framework AppKit -framework AVFoundation -framework Carbon -framework Security -framework ServiceManagement -framework ScreenCaptureKit -framework ImageIO
lipo libDeskPetMac.dylib -verify_arch "$arch"

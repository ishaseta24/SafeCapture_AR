#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ "${1:-}" != "--build-only" && $# -ne 0 ]]; then
  echo "Usage: bash Mentra/macos/build.sh [--build-only]" >&2
  exit 2
fi
APP="$PWD/build/SafeCaptureMentra.app"
if /usr/bin/pgrep -f "$APP/Contents/MacOS/SafeCaptureMentra" >/dev/null; then
  echo "Quit SafeCapture Mentra before rebuilding the running app, then rerun this command." >&2
  exit 1
fi
swift package resolve
python3 harden-sdk.py
swift build -c release -Xswiftc -DSAFE_CAPTURE_PRIVATE_DIAGNOSTICS
BIN="$(swift build -c release --show-bin-path)"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN/SafeCaptureMentra" "$APP/Contents/MacOS/SafeCaptureMentra"
cp Info.plist "$APP/Contents/Info.plist"
cp usb-debug.sh "$APP/Contents/Resources/usb-debug.sh"
for bundle in "$BIN"/*.bundle; do
  [[ -d "$bundle" ]] || continue
  # SwiftPM resource lookup checks Bundle.main.resourceURL for bundled apps.
  ditto "$bundle" "$APP/Contents/Resources/$(basename "$bundle")"
done
codesign --force --sign - --entitlements SafeCaptureMentra.entitlements "$APP"
codesign --verify --strict "$APP"
echo "Built and ad-hoc signed: Mentra/macos/build/SafeCaptureMentra.app"
if [[ "${1:-}" != "--build-only" ]]; then
  open "$APP"
fi

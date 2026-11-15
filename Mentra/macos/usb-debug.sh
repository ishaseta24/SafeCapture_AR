#!/bin/bash
# Run from Terminal, not the sandboxed Mac app. No camera or firmware commands.
set -euo pipefail

usage() {
  echo "Usage: bash usb-debug.sh [--remove] GLASSES_SERIAL [PORT]" >&2
  echo "Find the USB-connected glasses serial with: adb devices -l" >&2
}

fail() { echo "USB setup: $*" >&2; exit 1; }

action=setup
if [[ "${1:-}" == "--remove" ]]; then
  action=remove
  shift
fi
if [[ $# -lt 1 || $# -gt 2 ]]; then usage; exit 2; fi

serial="$1"
port="${2:-8091}"
[[ -n "$serial" && "$serial" != -* && ! "$serial" =~ [[:space:]] ]] \
  || fail "Pass exactly one device serial, not a name or option."
[[ "$port" =~ ^[0-9]{1,5}$ ]] || fail "Port must be between 1024 and 65535."
port=$((10#$port))
((port >= 1024 && port <= 65535)) || fail "Port must be between 1024 and 65535."

adb_bin="${ADB:-$(command -v adb || true)}"
[[ -n "$adb_bin" && -x "$adb_bin" ]] \
  || fail "ADB is missing. Install it with: brew install --cask android-platform-tools"
state="$("$adb_bin" -s "$serial" get-state 2>/dev/null)" \
  || fail "Device unavailable or unauthorized. Check the cable and adb devices -l."
[[ "$state" == "device" ]] || fail "Device must be authorized and online."

endpoint="tcp:$port"
if [[ "$action" == "remove" ]]; then
  "$adb_bin" -s "$serial" reverse --remove "$endpoint" >/dev/null \
    || fail "Could not remove this device's port mapping."
  echo "Removed the selected device's reverse mapping for port $port."
  exit 0
fi

device_path="$("$adb_bin" -s "$serial" get-devpath 2>/dev/null)" \
  || fail "Cannot confirm a USB connection."
[[ "$device_path" == usb:* ]] \
  || fail "This mode requires a USB data cable, not wireless ADB."

command -v curl >/dev/null || fail "curl is required to check the Mac receiver."
curl --fail --silent --output /dev/null --noproxy '*' \
  --connect-timeout 2 --max-time 3 "http://127.0.0.1:$port/health" \
  || fail "Start the Mentra receiver on port $port first (npm run start:usb in Mentra/laptop)."

# Do not silently replace another mapping. Re-running the same setup is safe.
mappings="$("$adb_bin" -s "$serial" reverse --list)" \
  || fail "Cannot read reverse mappings."
existing="$(printf '%s\n' "$mappings" | awk -v remote="$endpoint" \
  'NF >= 2 && $(NF-1) == remote { print $NF }')"
if [[ -n "$existing" && "$existing" != "$endpoint" ]]; then
  fail "Port $port already forwards elsewhere. Choose another port or remove that mapping explicitly."
fi
if [[ -z "$existing" ]]; then
  "$adb_bin" -s "$serial" reverse --no-rebind "$endpoint" "$endpoint" >/dev/null \
    || fail "Could not create the reverse tunnel. Check the USB connection."
fi

echo "Configured: glasses 127.0.0.1:$port -> Mac 127.0.0.1:$port over USB."
echo "In SafeCapture Mentra, choose USB (ADB reverse) and use:"
echo "  http://127.0.0.1:$port/mentra/photo"
echo "Confirm the tunnel in the app and enter the receiver token there, not in this command."
echo "Bluetooth still controls capture. Re-run setup after reconnecting the cable."
echo "This verifies setup and Mac receiver availability, not an actual glasses photo upload."

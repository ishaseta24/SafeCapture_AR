#!/usr/bin/env bash
# Updates relayUrl in the Quest's config.json to this Mac's current IP (or hostname).
# Usage:
#   ./update-quest-ip.sh          # uses current IP
#   ./update-quest-ip.sh mdns     # uses Ishas-MacBook-Air-2790.local (works on any network)
set -euo pipefail

PACKAGE="com.Ohlone.Quest"
REMOTE_CFG="/sdcard/Android/data/${PACKAGE}/files/config.json"
TMP_CFG="/tmp/_safecapture_cfg_$$.json"

# Determine host to use
if [[ "${1:-}" == "mdns" ]]; then
  HOST="Ishas-MacBook-Air-2790.local"
else
  HOST=$(ipconfig getifaddr en1 2>/dev/null || ipconfig getifaddr en0 2>/dev/null || echo "")
  if [[ -z "$HOST" ]]; then
    echo "ERROR: Could not determine local IP. Run with 'mdns' to use hostname instead."
    exit 1
  fi
fi

# Read PORT from relay .env (default 3000)
PORT=$(grep -E '^PORT=' "$(dirname "$0")/.env" 2>/dev/null | cut -d= -f2 | tr -d '[:space:]' || echo "3000")
[[ -z "$PORT" ]] && PORT=3000

NEW_URL="http://${HOST}:${PORT}"
echo "Setting relayUrl → ${NEW_URL}"

# Check Quest is connected
if ! adb devices | grep -q "device$"; then
  echo "ERROR: No Quest connected via adb."
  exit 1
fi

# Pull → patch relayUrl only → push → cleanup
adb pull "$REMOTE_CFG" "$TMP_CFG" 2>/dev/null
node --input-type=module << EOF
import fs from 'fs';
const cfg = JSON.parse(fs.readFileSync('${TMP_CFG}', 'utf8'));
cfg.relayUrl = '${NEW_URL}';
fs.writeFileSync('${TMP_CFG}', JSON.stringify(cfg, null, 2));
EOF
adb push "$TMP_CFG" "$REMOTE_CFG"
rm -f "$TMP_CFG"

echo "Done. Restart the Quest app to pick up the new relay URL."

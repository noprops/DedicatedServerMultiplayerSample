#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/load_dsms_vm_json.sh"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <slot:A|B>" >&2
  exit 1
fi

SLOT="$(normalize_slot "$1")"
CONFIG_PATH="$(dsms_config_path)"

if [[ ! -f "$CONFIG_PATH" ]]; then
  echo "Missing DSMS VM config: $CONFIG_PATH" >&2
  exit 1
fi

python3 - <<'PY' "$CONFIG_PATH" "$SLOT"
import json
import sys

config_path, slot = sys.argv[1:]
with open(config_path, "r", encoding="utf-8") as f:
    data = json.load(f)

slots = data.get("slots", {})
if slot not in slots:
    raise SystemExit(f"Slot {slot} not found in {config_path}")

data["currentWorkSlot"] = slot

with open(config_path, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, sort_keys=True)
    f.write("\n")
PY

echo "Set currentWorkSlot to $SLOT in $CONFIG_PATH"

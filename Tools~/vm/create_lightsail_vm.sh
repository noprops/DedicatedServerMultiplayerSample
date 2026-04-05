#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/load_dsms_vm_json.sh"

if [[ "${1:-}" =~ ^[AaBb]$ ]]; then
  SLOT="$(resolve_slot_or_current "$1")"
  shift
else
  SLOT="$(resolve_slot_or_current "")"
fi

if [[ $# -lt 3 ]]; then
  echo "Usage: $0 [slot:A|B] <instance-name> <availability-zone> <blueprint-id> [bundle-id]" >&2
  exit 1
fi

INSTANCE_NAME="$1"
AVAILABILITY_ZONE="$2"
BLUEPRINT_ID="$3"
BUNDLE_ID="${4:-nano_3_0}"
REGION="${AWS_REGION:-ap-northeast-1}"
PORT_RANGE_START="${PORT_RANGE_START:-7777}"
PORT_RANGE_END="${PORT_RANGE_END:-7792}"
LAUNCHER_PORT="${LAUNCHER_PORT:-8080}"
LAUNCHER_TOKEN="${VM_LAUNCHER_TOKEN:-$(openssl rand -hex 20)}"
PROJECT_ROOT="$(resolve_project_root)"
CONFIG_PATH="$(dsms_config_path)"
SSH_KEY_PATH_VALUE="${DSMS_VM_SSH_KEY_PATH:-${LIGHTSAIL_SSH_KEY_PATH:-}}"
BASE_URL_SECRET_NAME="$(dsms_slot_secret_base_url_name "$SLOT")"
TOKEN_SECRET_NAME="$(dsms_slot_secret_token_name "$SLOT")"

aws lightsail create-instance \
  --region "$REGION" \
  --instance-name "$INSTANCE_NAME" \
  --availability-zone "$AVAILABILITY_ZONE" \
  --blueprint-id "$BLUEPRINT_ID" \
  --bundle-id "$BUNDLE_ID"

echo "Waiting for public IP..."
IP_ADDRESS=""
for _ in $(seq 1 30); do
  IP_ADDRESS="$(aws lightsail get-instance \
    --region "$REGION" \
    --instance-name "$INSTANCE_NAME" \
    --query 'instance.publicIpAddress' \
    --output text 2>/dev/null || true)"
  if [[ -n "$IP_ADDRESS" && "$IP_ADDRESS" != "None" ]]; then
    break
  fi
  sleep 5
done

if [[ -z "$IP_ADDRESS" || "$IP_ADDRESS" == "None" ]]; then
  echo "Failed to resolve public IP for $INSTANCE_NAME" >&2
  exit 1
fi

python3 - <<'PY' "$CONFIG_PATH" "$SLOT" "$INSTANCE_NAME" "$IP_ADDRESS" "$SSH_KEY_PATH_VALUE" "$LAUNCHER_TOKEN" "$LAUNCHER_PORT"
import json
import os
import sys

config_path, slot, instance_name, ip_address, ssh_key_path, launcher_token, launcher_port = sys.argv[1:]

if os.path.exists(config_path):
    with open(config_path, "r", encoding="utf-8") as f:
        data = json.load(f)
else:
    data = {}

if not data.get("currentWorkSlot"):
    data["currentWorkSlot"] = slot

slots = data.setdefault("slots", {})
slots.setdefault("A", {})
slots.setdefault("B", {})
slots[slot] = {
    "instanceName": instance_name,
    "host": ip_address,
    "publicIp": ip_address,
    "sshKeyPath": ssh_key_path,
    "launcherToken": launcher_token,
    "launcherBaseUrl": f"http://{ip_address}:{launcher_port}",
    "enabled": True
}

with open(config_path, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, sort_keys=True)
    f.write("\n")
PY

cat <<EOF
VM created.

Slot:
  $SLOT

Instance:
  Name: $INSTANCE_NAME
  Region: $REGION
  Public IP: $IP_ADDRESS

Project VM config written:
  $CONFIG_PATH

Set these Unity project secrets:
  $BASE_URL_SECRET_NAME = http://$IP_ADDRESS:$LAUNCHER_PORT
  $TOKEN_SECRET_NAME    = $LAUNCHER_TOKEN

Recommended next steps:
  1. Open TCP port $LAUNCHER_PORT
  2. Open UDP ports $PORT_RANGE_START-$PORT_RANGE_END
  3. If needed, edit currentWorkSlot or sshKeyPath for slot $SLOT in $CONFIG_PATH
  4. Upload Linux dedicated server build
  5. Upload VmLauncher~ contents
  6. Install dsms-vm-launcher.service
EOF

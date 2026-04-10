#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/load_dsms_vm_json.sh"

SLOT_ARG=""
PACKAGE_ROOT_INDEX=1
if [[ "${1:-}" =~ ^[AaBb]$ ]]; then
  SLOT_ARG="$1"
  PACKAGE_ROOT_INDEX=2
fi

SLOT="$(resolve_slot_or_current "$SLOT_ARG")"
load_dsms_project_identity
load_dsms_vm_slot "$SLOT"

PROJECT_ID="${DSMS_PROJECT_ID:-}"
PROJECT_NAME="${DSMS_PROJECT_NAME:-}"
ENVIRONMENT="${DSMS_ENVIRONMENT:-}"
VM_INSTANCE_NAME="${DSMS_VM_INSTANCE_NAME:-}"
SSH_KEY_PATH="${DSMS_VM_SSH_KEY_PATH:-}"
VM_HOST="${DSMS_VM_HOST:-}"
LAUNCHER_TOKEN="${DSMS_VM_LAUNCHER_TOKEN:-}"
PUBLIC_IP="${DSMS_VM_PUBLIC_IP:-}"
MAX_CONCURRENT_MATCHES="${DSMS_VM_MAX_CONCURRENT_MATCHES:-}"
PACKAGE_ROOT="${!PACKAGE_ROOT_INDEX:-Packages/info.mygames888.dedicatedservermultiplayersample}"

require_value "ssh-key-path or DSMS_VM_SSH_KEY_PATH" "$SSH_KEY_PATH"
require_value "vm-host or DSMS_VM_HOST" "$VM_HOST"
require_value "projectId or DSMS_PROJECT_ID" "$PROJECT_ID"
require_value "projectName or DSMS_PROJECT_NAME" "$PROJECT_NAME"
require_value "environment or DSMS_ENVIRONMENT" "$ENVIRONMENT"
require_value "launcher-token or DSMS_VM_LAUNCHER_TOKEN" "$LAUNCHER_TOKEN"
require_value "public-ip or DSMS_VM_PUBLIC_IP" "$PUBLIC_IP"
require_value "maxConcurrentMatches or DSMS_VM_MAX_CONCURRENT_MATCHES" "$MAX_CONCURRENT_MATCHES"

VM_LAUNCHER_DIR="$PACKAGE_ROOT/VmLauncher~"
REMOTE_DIR="${REMOTE_DIR:-~/dsms-launcher}"
VM_USER="${VM_USER:-ubuntu}"
LAUNCHER_PORT="${VM_LAUNCHER_PORT:-8080}"

TMP_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT

REMOTE_CONFIG_JSON="$(ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "if [ -f $REMOTE_DIR/config.json ]; then cat $REMOTE_DIR/config.json; fi")"

python3 - <<'PY' "$REMOTE_CONFIG_JSON" "$PROJECT_ID" "$PROJECT_NAME" "$ENVIRONMENT" "$SLOT" "$VM_INSTANCE_NAME" "$LAUNCHER_TOKEN"
import json
import sys

remote_json, project_id, project_name, environment, slot, instance_name, launcher_token = sys.argv[1:]

if not remote_json.strip():
    raise SystemExit(0)

remote = json.loads(remote_json)
remote_project_id = str(remote.get("projectId", "")).strip()
remote_project_name = str(remote.get("projectName", "")).strip()
remote_slot = str(remote.get("slot", "")).strip()
remote_token = str(remote.get("launcherToken", "")).strip()

if remote_project_id:
    if remote_project_id != project_id:
        raise SystemExit(
            f"Remote VM ownership mismatch: remote projectId={remote_project_id!r}, local projectId={project_id!r}"
        )
    if remote_slot and remote_slot != slot:
        raise SystemExit(
            f"Remote VM slot mismatch: remote slot={remote_slot!r}, local slot={slot!r}"
        )
else:
    if remote_token and remote_token != launcher_token:
        raise SystemExit(
            "Remote VM has no ownership metadata and launcherToken does not match local dsms-vm.json. "
            "Refusing to deploy launcher onto a possibly foreign VM."
        )

    if remote_project_name and remote_project_name != project_name:
        raise SystemExit(
            f"Remote VM projectName mismatch without projectId metadata: remote={remote_project_name!r}, local={project_name!r}"
        )
PY

cp "$VM_LAUNCHER_DIR/server_launcher.py" "$TMP_DIR/server_launcher.py"
python3 - <<'PY' "$VM_LAUNCHER_DIR/config.example.json" "$TMP_DIR/config.json" "$PUBLIC_IP" "$LAUNCHER_TOKEN" "$LAUNCHER_PORT" "$MAX_CONCURRENT_MATCHES" "$PROJECT_ID" "$PROJECT_NAME" "$ENVIRONMENT" "$SLOT" "$VM_INSTANCE_NAME"
import json
import sys
src, dst, ip, token, port, max_concurrent_matches, project_id, project_name, environment, slot, instance_name = sys.argv[1:]
with open(src, "r", encoding="utf-8") as f:
    data = json.load(f)
data["publicIp"] = ip
data["launcherToken"] = token
data["bindPort"] = int(port)
data["maxConcurrentMatches"] = int(max_concurrent_matches)
data["projectId"] = project_id
data["projectName"] = project_name
data["environment"] = environment
data["slot"] = slot
data["instanceName"] = instance_name
with open(dst, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, sort_keys=True)
    f.write("\n")
PY

scp -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no \
  "$TMP_DIR/server_launcher.py" \
  "$TMP_DIR/config.json" \
  "$VM_LAUNCHER_DIR/dsms-vm-launcher.service" \
  "$VM_USER@$VM_HOST:$REMOTE_DIR/"

ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "mkdir -p $REMOTE_DIR && chmod +x $REMOTE_DIR/server_launcher.py"

ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "sudo systemctl restart dsms-vm-launcher.service && systemctl is-active dsms-vm-launcher.service >/dev/null"

python3 - <<'PY' \
  "$TMP_DIR/config.json" \
  "$(ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" "cat $REMOTE_DIR/config.json")"
import json
import sys

local_path, remote_json = sys.argv[1:]

with open(local_path, "r", encoding="utf-8") as f:
    local_data = json.load(f)

remote_data = json.loads(remote_json)

keys_to_verify = [
    "projectId",
    "projectName",
    "environment",
    "slot",
    "instanceName",
    "publicIp",
    "launcherToken",
    "bindPort",
    "maxConcurrentMatches",
]

mismatches = []
for key in keys_to_verify:
    local_value = local_data.get(key)
    remote_value = remote_data.get(key)
    if local_value != remote_value:
        mismatches.append(f"{key}: local={local_value!r}, remote={remote_value!r}")

if mismatches:
    raise SystemExit(
        "VM launcher config verification failed after deploy:\n  " +
        "\n  ".join(mismatches)
    )

print("VM launcher config verification passed.")
PY

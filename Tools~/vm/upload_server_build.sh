#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/load_dsms_vm_json.sh"

SLOT_ARG=""
if [[ "${1:-}" =~ ^[AaBb]$ ]]; then
  SLOT_ARG="$1"
  shift
fi

SLOT="$(resolve_slot_or_current "$SLOT_ARG")"
load_dsms_project_identity
load_dsms_vm_slot "$SLOT"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 [slot:A|B] <server-build-dir> [remote-dir]" >&2
  exit 1
fi

PROJECT_ID="${DSMS_PROJECT_ID:-}"
PROJECT_NAME="${DSMS_PROJECT_NAME:-}"
ENVIRONMENT="${DSMS_ENVIRONMENT:-}"
SSH_KEY_PATH="${DSMS_VM_SSH_KEY_PATH:-}"
VM_HOST="${DSMS_VM_HOST:-}"
SERVER_BUILD_DIR="$1"
REMOTE_DIR="${2:-~/dsms-server}"
REMOTE_LAUNCHER_DIR="${REMOTE_LAUNCHER_DIR:-~/dsms-launcher}"

VM_USER="${VM_USER:-ubuntu}"

require_value "ssh-key-path or DSMS_VM_SSH_KEY_PATH" "$SSH_KEY_PATH"
require_value "vm-host or DSMS_VM_HOST" "$VM_HOST"
require_value "projectId or DSMS_PROJECT_ID" "$PROJECT_ID"
require_value "projectName or DSMS_PROJECT_NAME" "$PROJECT_NAME"
require_value "environment or DSMS_ENVIRONMENT" "$ENVIRONMENT"

if [[ ! -d "$SERVER_BUILD_DIR" ]]; then
  echo "Server build directory not found: $SERVER_BUILD_DIR" >&2
  exit 1
fi

REMOTE_CONFIG_JSON="$(ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "if [ -f $REMOTE_LAUNCHER_DIR/config.json ]; then cat $REMOTE_LAUNCHER_DIR/config.json; fi")"

python3 - <<'PY' "$REMOTE_CONFIG_JSON" "$PROJECT_ID" "$PROJECT_NAME" "$SLOT"
import json
import sys

remote_json, project_id, project_name, slot = sys.argv[1:]

if not remote_json.strip():
    raise SystemExit(
        "Remote launcher config not found. Refusing server build upload until launcher ownership is deployed."
    )

remote = json.loads(remote_json)
remote_project_id = str(remote.get("projectId", "")).strip()
remote_project_name = str(remote.get("projectName", "")).strip()
remote_slot = str(remote.get("slot", "")).strip()

if not remote_project_id:
    raise SystemExit(
        "Remote launcher ownership metadata is missing. Run deploy_vm_launcher.sh first so the VM is claimed by this project."
    )

if remote_project_id != project_id:
    raise SystemExit(
        f"Remote VM ownership mismatch: remote projectId={remote_project_id!r}, local projectId={project_id!r}"
    )

if remote_project_name and remote_project_name != project_name:
    raise SystemExit(
        f"Remote VM projectName mismatch: remote={remote_project_name!r}, local={project_name!r}"
    )

if remote_slot and remote_slot != slot:
    raise SystemExit(
        f"Remote VM slot mismatch: remote slot={remote_slot!r}, local slot={slot!r}"
    )
PY

ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "rm -rf $REMOTE_DIR && mkdir -p $REMOTE_DIR"

scp -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no -r \
  "$SERVER_BUILD_DIR/." \
  "$VM_USER@$VM_HOST:$REMOTE_DIR/"

ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "chmod +x $REMOTE_DIR/DedicatedServer.x86_64"

echo "Uploaded Linux dedicated server build to $VM_USER@$VM_HOST:$REMOTE_DIR"

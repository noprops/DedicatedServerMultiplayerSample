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
load_dsms_vm_slot "$SLOT"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 [slot:A|B] <server-build-dir> [remote-dir]" >&2
  exit 1
fi

SSH_KEY_PATH="${DSMS_VM_SSH_KEY_PATH:-}"
VM_HOST="${DSMS_VM_HOST:-}"
SERVER_BUILD_DIR="$1"
REMOTE_DIR="${2:-~/dsms-server}"

VM_USER="${VM_USER:-ubuntu}"

require_value "ssh-key-path or DSMS_VM_SSH_KEY_PATH" "$SSH_KEY_PATH"
require_value "vm-host or DSMS_VM_HOST" "$VM_HOST"

if [[ ! -d "$SERVER_BUILD_DIR" ]]; then
  echo "Server build directory not found: $SERVER_BUILD_DIR" >&2
  exit 1
fi

ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "rm -rf $REMOTE_DIR && mkdir -p $REMOTE_DIR"

scp -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no -r \
  "$SERVER_BUILD_DIR/." \
  "$VM_USER@$VM_HOST:$REMOTE_DIR/"

ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "chmod +x $REMOTE_DIR/DedicatedServer.x86_64"

echo "Uploaded Linux dedicated server build to $VM_USER@$VM_HOST:$REMOTE_DIR"

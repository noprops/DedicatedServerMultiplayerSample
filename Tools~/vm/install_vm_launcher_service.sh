#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/load_dsms_vm_json.sh"

SLOT_ARG=""
if [[ "${1:-}" =~ ^[AaBb]$ ]]; then
  SLOT_ARG="$1"
fi

SLOT="$(resolve_slot_or_current "$SLOT_ARG")"
load_dsms_vm_slot "$SLOT"

SSH_KEY_PATH="${DSMS_VM_SSH_KEY_PATH:-}"
VM_HOST="${DSMS_VM_HOST:-}"
VM_USER="${VM_USER:-ubuntu}"
REMOTE_DIR="${REMOTE_DIR:-~/dsms-launcher}"
SERVICE_NAME="${SERVICE_NAME:-dsms-vm-launcher.service}"

require_value "ssh-key-path or DSMS_VM_SSH_KEY_PATH" "$SSH_KEY_PATH"
require_value "vm-host or DSMS_VM_HOST" "$VM_HOST"

ssh -i "$SSH_KEY_PATH" -o StrictHostKeyChecking=no "$VM_USER@$VM_HOST" \
  "sudo mv $REMOTE_DIR/$SERVICE_NAME /etc/systemd/system/$SERVICE_NAME && \
   sudo systemctl daemon-reload && \
   sudo systemctl enable $SERVICE_NAME && \
   sudo systemctl restart $SERVICE_NAME && \
   sudo systemctl --no-pager --full status $SERVICE_NAME | sed -n '1,20p'"

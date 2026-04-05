#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/load_dsms_vm_json.sh"

SLOT_ARG=""
REGION_ARG_INDEX=1
if [[ "${1:-}" =~ ^[AaBb]$ ]]; then
  SLOT_ARG="$1"
  REGION_ARG_INDEX=2
fi

SLOT="$(resolve_slot_or_current "$SLOT_ARG")"
load_dsms_vm_slot "$SLOT"

INSTANCE_NAME="${DSMS_VM_INSTANCE_NAME:-}"
REGION="${!REGION_ARG_INDEX:-${AWS_REGION:-ap-northeast-1}}"
LAUNCHER_PORT="${LAUNCHER_PORT:-8080}"
PORT_RANGE_START="${PORT_RANGE_START:-7777}"
PORT_RANGE_END="${PORT_RANGE_END:-7792}"

require_value "instance-name or DSMS_VM_INSTANCE_NAME" "$INSTANCE_NAME"

aws lightsail open-instance-public-ports \
  --region "$REGION" \
  --instance-name "$INSTANCE_NAME" \
  --port-info "fromPort=$LAUNCHER_PORT,toPort=$LAUNCHER_PORT,protocol=TCP"

aws lightsail open-instance-public-ports \
  --region "$REGION" \
  --instance-name "$INSTANCE_NAME" \
  --port-info "fromPort=$PORT_RANGE_START,toPort=$PORT_RANGE_END,protocol=UDP"

echo "Opened TCP $LAUNCHER_PORT and UDP $PORT_RANGE_START-$PORT_RANGE_END for $INSTANCE_NAME in $REGION"

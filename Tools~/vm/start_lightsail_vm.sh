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

require_value "instance-name for slot $SLOT" "$INSTANCE_NAME"

aws lightsail start-instance \
  --region "$REGION" \
  --instance-name "$INSTANCE_NAME"

echo "Started Lightsail instance $INSTANCE_NAME for slot $SLOT in $REGION"

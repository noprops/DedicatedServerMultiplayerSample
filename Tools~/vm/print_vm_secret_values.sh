#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/load_dsms_vm_json.sh"

SLOT_ARG=""
PORT_ARG_INDEX=1
if [[ "${1:-}" =~ ^[AaBb]$ ]]; then
  SLOT_ARG="$1"
  PORT_ARG_INDEX=2
fi

SLOT="$(resolve_slot_or_current "$SLOT_ARG")"
load_dsms_vm_slot "$SLOT"

HOST_VALUE="${DSMS_VM_PUBLIC_IP:-}"
TOKEN_VALUE="${DSMS_VM_LAUNCHER_TOKEN:-}"
LAUNCHER_PORT="${!PORT_ARG_INDEX:-8080}"
BASE_URL_SECRET_NAME="$(dsms_slot_secret_base_url_name "$SLOT")"
TOKEN_SECRET_NAME="$(dsms_slot_secret_token_name "$SLOT")"

require_value "public-ip-or-dns or DSMS_VM_PUBLIC_IP" "$HOST_VALUE"
require_value "launcher-token or DSMS_VM_LAUNCHER_TOKEN" "$TOKEN_VALUE"

cat <<EOF
$BASE_URL_SECRET_NAME=http://$HOST_VALUE:$LAUNCHER_PORT
$TOKEN_SECRET_NAME=$TOKEN_VALUE
EOF

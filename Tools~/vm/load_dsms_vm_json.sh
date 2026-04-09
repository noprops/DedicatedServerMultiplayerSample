#!/usr/bin/env bash

set -euo pipefail

resolve_project_root() {
  if [[ -n "${PROJECT_ROOT:-}" ]]; then
    printf '%s\n' "$PROJECT_ROOT"
    return
  fi

  pwd
}

dsms_config_path() {
  local project_root
  project_root="$(resolve_project_root)"
  printf '%s/dsms-vm.json\n' "$project_root"
}

unity_services_settings_path() {
  local project_root
  project_root="$(resolve_project_root)"
  printf '%s/ProjectSettings/Packages/com.unity.services.core/Settings.json\n' "$project_root"
}

unity_project_settings_asset_path() {
  local project_root
  project_root="$(resolve_project_root)"
  printf '%s/ProjectSettings/ProjectSettings.asset\n' "$project_root"
}

detect_dsms_project_id() {
  local settings_path
  settings_path="$(unity_project_settings_asset_path)"
  if [[ ! -f "$settings_path" ]]; then
    return 0
  fi

  python3 - <<'PY' "$settings_path"
import re
import sys

settings_path = sys.argv[1]
with open(settings_path, "r", encoding="utf-8") as f:
    text = f.read()

match = re.search(r'^\s*cloudProjectId:\s*(.+?)\s*$', text, re.MULTILINE)
if match:
    print(match.group(1).strip())
PY
}

detect_dsms_environment() {
  local settings_path
  settings_path="$(unity_services_settings_path)"
  if [[ ! -f "$settings_path" ]]; then
    return 0
  fi

  python3 - <<'PY' "$settings_path"
import json
import sys

settings_path = sys.argv[1]
with open(settings_path, "r", encoding="utf-8") as f:
    data = json.load(f)

value = str(data.get("EnvironmentName", "")).strip()
if value:
    print(value)
PY
}

detect_dsms_project_name() {
  basename "$(resolve_project_root)"
}

normalize_slot() {
  local slot="${1:-}"
  slot="$(printf '%s' "$slot" | tr '[:lower:]' '[:upper:]')"
  case "$slot" in
    A|B) printf '%s\n' "$slot" ;;
    *)
      echo "Invalid slot: $slot (expected A or B)" >&2
      exit 1
      ;;
  esac
}

dsms_current_work_slot() {
  local config_path
  config_path="$(dsms_config_path)"

  if [[ ! -f "$config_path" ]]; then
    echo "Missing DSMS VM config: $config_path" >&2
    exit 1
  fi

  python3 - <<'PY' "$config_path"
import json
import sys

config_path = sys.argv[1]
with open(config_path, "r", encoding="utf-8") as f:
    data = json.load(f)

slot = data.get("currentWorkSlot", "")
if not slot:
    raise SystemExit("Missing currentWorkSlot in dsms-vm.json")

print(slot)
PY
}

resolve_slot_or_current() {
  local maybe_slot="${1:-}"
  if [[ -n "$maybe_slot" ]]; then
    normalize_slot "$maybe_slot"
    return
  fi

  normalize_slot "$(dsms_current_work_slot)"
}

validate_dsms_vm_uniqueness() {
  local config_path
  config_path="$(dsms_config_path)"

  python3 - <<'PY' "$config_path"
import json
import sys

config_path = sys.argv[1]
with open(config_path, "r", encoding="utf-8") as f:
    data = json.load(f)

slots = data.get("slots", {})
a = slots.get("A", {}) if isinstance(slots.get("A", {}), dict) else {}
b = slots.get("B", {}) if isinstance(slots.get("B", {}), dict) else {}

if not a.get("enabled") or not b.get("enabled"):
    raise SystemExit(0)

fields = [
    "instanceName",
    "host",
    "publicIp",
    "launcherBaseUrl",
]

duplicates = []
for field in fields:
    a_value = str(a.get(field, "")).strip()
    b_value = str(b.get(field, "")).strip()
    if a_value and b_value and a_value == b_value:
        duplicates.append(f"{field}={a_value}")

if duplicates:
    raise SystemExit(
        "Invalid dsms-vm.json: slot A and slot B must not point at the same VM:\n  " +
        "\n  ".join(duplicates)
    )
PY
}

dsms_slot_secret_base_url_name() {
  local slot
  slot="$(normalize_slot "$1")"
  printf 'DSMS_VM_%s_LAUNCHER_BASE_URL\n' "$slot"
}

dsms_slot_secret_token_name() {
  local slot
  slot="$(normalize_slot "$1")"
  printf 'DSMS_VM_%s_LAUNCHER_TOKEN\n' "$slot"
}

load_dsms_project_identity() {
  local config_path
  config_path="$(dsms_config_path)"

  if [[ ! -f "$config_path" ]]; then
    echo "Missing DSMS VM config: $config_path" >&2
    exit 1
  fi

  eval "$(
    python3 - <<'PY' "$config_path"
import json
import shlex
import sys

config_path = sys.argv[1]
with open(config_path, "r", encoding="utf-8") as f:
    data = json.load(f)

mapping = {
    "DSMS_PROJECT_ID": data.get("projectId", ""),
    "DSMS_PROJECT_NAME": data.get("projectName", ""),
    "DSMS_ENVIRONMENT": data.get("environment", ""),
}

for key, value in mapping.items():
    print(f"{key}={shlex.quote(str(value))}")
PY
  )"
}

load_dsms_vm_slot() {
  local slot config_path
  slot="$(normalize_slot "$1")"
  config_path="$(dsms_config_path)"

  if [[ ! -f "$config_path" ]]; then
    echo "Missing DSMS VM config: $config_path" >&2
    exit 1
  fi

  validate_dsms_vm_uniqueness

  eval "$(
    python3 - <<'PY' "$config_path" "$slot"
import json
import shlex
import sys

config_path, slot = sys.argv[1:]
with open(config_path, "r", encoding="utf-8") as f:
    data = json.load(f)

slot_data = data.get("slots", {}).get(slot)
if not isinstance(slot_data, dict):
    raise SystemExit(f"Slot {slot} not found in {config_path}")

mapping = {
    "DSMS_VM_SLOT": slot,
    "DSMS_VM_INSTANCE_NAME": slot_data.get("instanceName", ""),
    "DSMS_VM_HOST": slot_data.get("host", ""),
    "DSMS_VM_PUBLIC_IP": slot_data.get("publicIp", ""),
    "DSMS_VM_SSH_KEY_PATH": slot_data.get("sshKeyPath", ""),
    "DSMS_VM_LAUNCHER_TOKEN": slot_data.get("launcherToken", ""),
    "DSMS_VM_LAUNCHER_BASE_URL": slot_data.get("launcherBaseUrl", ""),
    "DSMS_VM_MAX_CONCURRENT_MATCHES": slot_data.get("maxConcurrentMatches", ""),
}

for key, value in mapping.items():
    print(f"{key}={shlex.quote(str(value))}")
PY
  )"
}

require_value() {
  local name="$1"
  local value="${2:-}"
  if [[ -z "$value" ]]; then
    echo "Missing required value: $name" >&2
    exit 1
  fi
}

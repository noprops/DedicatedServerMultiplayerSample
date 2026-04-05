#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
  echo "Usage: $0 <project-id> <environment-name> <matchmaker-environment.mme> <queue-or-config> [more-config-paths...]" >&2
  exit 1
fi

PROJECT_ID="$1"
ENVIRONMENT_NAME="$2"
shift 2

export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${DOTNET_BUNDLE_EXTRACT_BASE_DIR:-/tmp}"

ugs deploy "$@" -p "$PROJECT_ID" -e "$ENVIRONMENT_NAME"

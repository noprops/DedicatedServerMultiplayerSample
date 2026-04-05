#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <project-id> <environment-name> [slot:A|B|ALL] [package-root]" >&2
  exit 1
fi

PROJECT_ID="$1"
ENVIRONMENT_NAME="$2"
SLOT="${3:-ALL}"
PACKAGE_ROOT="${4:-Packages/info.mygames888.dedicatedservermultiplayersample}"
SLOT_UPPER="$(printf '%s' "$SLOT" | tr '[:lower:]' '[:upper:]')"

export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${DOTNET_BUNDLE_EXTRACT_BASE_DIR:-/tmp}"
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/dsms-cloudcode-import.XXXXXX")"
IMPORT_DIR="$WORK_DIR/import"
IMPORT_ZIP="$WORK_DIR/ugs.ccmzip"

cleanup() {
  rm -rf "$WORK_DIR"
}
trap cleanup EXIT

mkdir -p "$IMPORT_DIR"

pack_module() {
  local module_name="$1"
  local csproj_path="$2"
  local publish_dir="$WORK_DIR/$module_name/publish"
  local module_zip="$WORK_DIR/$module_name/module.zip"

  mkdir -p "$WORK_DIR/$module_name"
  dotnet publish "$csproj_path" -c Release -o "$publish_dir" >/dev/null
  (
    cd "$publish_dir"
    zip -qr "$module_zip" .
  )
  cp "$module_zip" "$IMPORT_DIR/$module_name"
}

write_manifest() {
  local modules_json="$1"
  printf '%s\n' "$modules_json" > "$IMPORT_DIR/cloud-code modules"
}

import_modules() {
  (
    cd "$IMPORT_DIR"
    zip -q "$IMPORT_ZIP" "cloud-code modules" "$@"
  )
  ugs cc modules import "$WORK_DIR" ugs.ccmzip -p "$PROJECT_ID" -e "$ENVIRONMENT_NAME"
}

case "$SLOT_UPPER" in
  A)
    pack_module \
      "MatchmakerVmHostingA" \
      "$PACKAGE_ROOT/CloudCode~/matchmaker-vm-hosting-a/Module~/Project/MatchmakerVmHostingA.csproj"
    write_manifest '[{"Name":"MatchmakerVmHostingA","Language":1,"Body":"","Parameters":[],"LastPublishedDate":"","SolutionPath":"","CcmPath":"MatchmakerVmHostingA.ccm","ModuleName":"MatchmakerVmHostingA","Type":"","Path":"MatchmakerVmHostingA.ccm","Progress":0.0,"Status":{"Message":"","MessageDetail":"","MessageSeverity":0}}]'
    import_modules "MatchmakerVmHostingA"
    ;;
  B)
    pack_module \
      "MatchmakerVmHostingB" \
      "$PACKAGE_ROOT/CloudCode~/matchmaker-vm-hosting-b/Module~/Project/MatchmakerVmHostingB.csproj"
    write_manifest '[{"Name":"MatchmakerVmHostingB","Language":1,"Body":"","Parameters":[],"LastPublishedDate":"","SolutionPath":"","CcmPath":"MatchmakerVmHostingB.ccm","ModuleName":"MatchmakerVmHostingB","Type":"","Path":"MatchmakerVmHostingB.ccm","Progress":0.0,"Status":{"Message":"","MessageDetail":"","MessageSeverity":0}}]'
    import_modules "MatchmakerVmHostingB"
    ;;
  ALL)
    pack_module \
      "MatchmakerVmHostingA" \
      "$PACKAGE_ROOT/CloudCode~/matchmaker-vm-hosting-a/Module~/Project/MatchmakerVmHostingA.csproj"
    pack_module \
      "MatchmakerVmHostingB" \
      "$PACKAGE_ROOT/CloudCode~/matchmaker-vm-hosting-b/Module~/Project/MatchmakerVmHostingB.csproj"
    write_manifest '[{"Name":"MatchmakerVmHostingA","Language":1,"Body":"","Parameters":[],"LastPublishedDate":"","SolutionPath":"","CcmPath":"MatchmakerVmHostingA.ccm","ModuleName":"MatchmakerVmHostingA","Type":"","Path":"MatchmakerVmHostingA.ccm","Progress":0.0,"Status":{"Message":"","MessageDetail":"","MessageSeverity":0}},{"Name":"MatchmakerVmHostingB","Language":1,"Body":"","Parameters":[],"LastPublishedDate":"","SolutionPath":"","CcmPath":"MatchmakerVmHostingB.ccm","ModuleName":"MatchmakerVmHostingB","Type":"","Path":"MatchmakerVmHostingB.ccm","Progress":0.0,"Status":{"Message":"","MessageDetail":"","MessageSeverity":0}}]'
    import_modules "MatchmakerVmHostingA" "MatchmakerVmHostingB"
    ;;
  *)
    echo "Invalid slot: $SLOT (expected A, B, or ALL)" >&2
    exit 1
    ;;
esac

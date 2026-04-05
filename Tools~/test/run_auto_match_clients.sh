#!/bin/zsh
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <app-path> <instance-count> [queue-name]"
  exit 1
fi

APP_PATH_INPUT="$1"
INSTANCE_COUNT="$2"
QUEUE_NAME="${3:-competitive-queue}"

if [[ "$APP_PATH_INPUT" != /* ]]; then
  APP_PATH="$(cd "$(dirname "$APP_PATH_INPUT")" && pwd)/$(basename "$APP_PATH_INPUT")"
else
  APP_PATH="$APP_PATH_INPUT"
fi

if [[ ! -d "$APP_PATH" ]]; then
  echo "App bundle not found: $APP_PATH" >&2
  exit 1
fi

if [[ ! -f "$APP_PATH/Contents/Info.plist" ]]; then
  echo "Not a valid macOS app bundle: $APP_PATH" >&2
  exit 1
fi

EXECUTABLE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP_PATH/Contents/Info.plist" 2>/dev/null || true)"
if [[ -z "$EXECUTABLE_NAME" || ! -x "$APP_PATH/Contents/MacOS/$EXECUTABLE_NAME" ]]; then
  echo "App bundle executable not found: $APP_PATH/Contents/MacOS/$EXECUTABLE_NAME" >&2
  exit 1
fi

for ((i=1; i<=INSTANCE_COUNT; i++)); do
  printf -v INSTANCE_ID "%02d" "$i"
  open -n "$APP_PATH" --args \
    -autoMatch \
    -queueName "$QUEUE_NAME" \
    -instanceIndex "$INSTANCE_ID" \
    -playerNamePrefix LoadClient \
    -autoMatchDelayMs 1000 \
    -autoMatchJitterMs 1500 \
    -autoQuitOnSuccess true \
    -autoQuitOnFailure true \
    -autoQuitTimeoutSeconds 180 \
    -autoChoiceStrategy cycle

  sleep 1
done

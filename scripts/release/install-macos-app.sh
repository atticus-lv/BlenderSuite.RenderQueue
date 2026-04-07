#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: install-macos-app.sh <dmg-path> [--app-name NAME] [--target-dir DIR]

Mounts a DMG, removes old BlenderRenderQueue app bundles from the target directory,
installs the app bundle using its standard name, and detaches the image.
EOF
}

if [[ $# -lt 1 ]]; then
  usage
  exit 1
fi

DMG_PATH=""
APP_NAME="BlenderRenderQueue"
TARGET_DIR="/Applications"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --app-name)
      APP_NAME="${2:-}"
      shift 2
      ;;
    --target-dir)
      TARGET_DIR="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      if [[ -z "$DMG_PATH" ]]; then
        DMG_PATH="$1"
      else
        echo "Unknown argument: $1" >&2
        usage
        exit 1
      fi
      shift
      ;;
  esac
done

if [[ -z "$DMG_PATH" || ! -f "$DMG_PATH" ]]; then
  echo "DMG not found: $DMG_PATH" >&2
  exit 1
fi

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This script must be run on macOS." >&2
  exit 1
fi

mkdir -p "$TARGET_DIR"

ATTACH_OUTPUT="$(hdiutil attach "$DMG_PATH" -nobrowse -readonly)"
MOUNT_POINT="$(printf '%s\n' "$ATTACH_OUTPUT" | sed -n 's|^.*\t\(/Volumes/.*\)$|\1|p' | tail -n 1)"

if [[ -z "$MOUNT_POINT" ]]; then
  echo "Failed to determine mount point for $DMG_PATH" >&2
  exit 1
fi

cleanup() {
  if [[ -n "${MOUNT_POINT:-}" && -d "${MOUNT_POINT:-}" ]]; then
    hdiutil detach "$MOUNT_POINT" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

APP_SOURCE="$MOUNT_POINT/$APP_NAME.app"
if [[ ! -d "$APP_SOURCE" ]]; then
  echo "App bundle not found in DMG: $APP_SOURCE" >&2
  exit 1
fi

STANDARD_APP_PATH="$TARGET_DIR/$APP_NAME.app"

for existing in "$TARGET_DIR"/"$APP_NAME"*.app; do
  if [[ ! -e "$existing" ]]; then
    continue
  fi

  rm -rf "$existing"
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
    -u "$existing" >/dev/null 2>&1 || true
done

ditto "$APP_SOURCE" "$STANDARD_APP_PATH"

echo "Installed: $STANDARD_APP_PATH"

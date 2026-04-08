#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: build-macos-dmg.sh [osx-arm64|osx-x64] [--aot] [--install] [--no-open]

Builds a macOS self-contained publish, wraps it in a .app bundle, and creates a .dmg.
If no RID is provided, the current machine architecture is used.
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT_FILE="$REPO_ROOT/src/BlenderRenderQueue/BlenderRenderQueue.csproj"
APP_NAME="BlenderRenderQueue"
APP_BUNDLE_NAME="${APP_NAME}.app"
PUBLISH_ROOT="$REPO_ROOT/install/macOS/publish"
BUILD_ROOT="$REPO_ROOT/install/macOS/build"
STAGING_ROOT="$REPO_ROOT/install/macOS/staging"
OUTPUT_ROOT="$REPO_ROOT/install/macOS/output"
SYMBOLS_ROOT="$REPO_ROOT/install/macOS/symbols"
ICON_SOURCE="$REPO_ROOT/src/BlenderRenderQueue/Assets/logo.png"
RID=""
OPEN_OUTPUT="true"
INSTALL_APP="false"
AOT_ENABLED="false"

for arg in "$@"; do
  case "$arg" in
    osx-arm64|osx-x64)
      RID="$arg"
      ;;
    --aot)
      AOT_ENABLED="true"
      ;;
    --no-open)
      OPEN_OUTPUT="false"
      ;;
    --install)
      INSTALL_APP="true"
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$RID" ]]; then
  case "$(uname -m)" in
    arm64|aarch64)
      RID="osx-arm64"
      ;;
    x86_64)
      RID="osx-x64"
      ;;
    *)
      echo "Unable to infer a supported macOS RID from architecture $(uname -m)." >&2
      usage
      exit 1
      ;;
  esac
fi

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This script must be run on macOS." >&2
  exit 1
fi

if [[ "$AOT_ENABLED" == "true" ]]; then
  BUILD_FLAVOR="aot"
  BUILD_TITLE="Native AOT"
  PUBLISH_AOT="true"
  DMG_SUFFIX="aot"
  VOLUME_SUFFIX="AOT"
else
  BUILD_FLAVOR="non-aot"
  BUILD_TITLE="self-contained non-AOT"
  PUBLISH_AOT="false"
  DMG_SUFFIX="non-aot"
  VOLUME_SUFFIX="non-AOT"
fi

APP_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT_FILE" | head -n 1)"
if [[ -z "$APP_VERSION" ]]; then
  echo "Failed to read <Version> from $PROJECT_FILE" >&2
  exit 1
fi

PUBLISH_DIR="$PUBLISH_ROOT/$BUILD_FLAVOR/$RID"
BUILD_DIR="$BUILD_ROOT/$BUILD_FLAVOR/$RID"
STAGING_DIR="$STAGING_ROOT/$BUILD_FLAVOR/$RID"
SYMBOLS_DIR="$SYMBOLS_ROOT/$BUILD_FLAVOR/$RID"
APP_DIR="$BUILD_DIR/$APP_BUNDLE_NAME"
DMG_PATH="$OUTPUT_ROOT/${APP_NAME}-${APP_VERSION}-${RID}-${DMG_SUFFIX}.dmg"
APP_EXECUTABLE="$APP_DIR/Contents/MacOS/$APP_NAME"
ICONSET_DIR="$BUILD_DIR/${APP_NAME}.iconset"
ICON_FILE="$APP_DIR/Contents/Resources/${APP_NAME}.icns"

echo "==> Building macOS ${BUILD_TITLE} publish"
echo "RID: $RID"
echo "Version: $APP_VERSION"

rm -rf "$PUBLISH_DIR" "$BUILD_DIR" "$STAGING_DIR" "$SYMBOLS_DIR"
mkdir -p "$PUBLISH_DIR" "$BUILD_DIR" "$STAGING_DIR" "$SYMBOLS_DIR" "$OUTPUT_ROOT"

dotnet publish "$PROJECT_FILE" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishAot="$PUBLISH_AOT" \
  -o "$PUBLISH_DIR"

echo "==> Creating .app bundle"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"

if compgen -G "$APP_DIR/Contents/MacOS/*.dSYM" > /dev/null; then
  mkdir -p "$SYMBOLS_DIR"
  mv "$APP_DIR/Contents/MacOS/"*.dSYM "$SYMBOLS_DIR"/
fi

chmod +x "$APP_EXECUTABLE"

ICON_PLIST=""
if [[ -f "$ICON_SOURCE" ]] && command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
  rm -rf "$ICONSET_DIR"
  mkdir -p "$ICONSET_DIR"
  sips -z 16 16 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16.png" >/dev/null
  sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32.png" >/dev/null
  sips -z 64 64 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128.png" >/dev/null
  sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256.png" >/dev/null
  sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512.png" >/dev/null
  cp "$ICON_SOURCE" "$ICONSET_DIR/icon_512x512@2x.png"
  iconutil -c icns "$ICONSET_DIR" -o "$ICON_FILE"
  ICON_PLIST=$'    <key>CFBundleIconFile</key>\n    <string>BlenderRenderQueue</string>'
fi

cat > "$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleExecutable</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.atticus.blenderrenderqueue</string>
    ${ICON_PLIST}
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${APP_VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${APP_VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

echo "==> Creating DMG"
cp -R "$APP_DIR" "$STAGING_DIR/"
ln -s /Applications "$STAGING_DIR/Applications"
rm -f "$DMG_PATH"
hdiutil create \
  -volname "${APP_NAME} ${APP_VERSION} (${VOLUME_SUFFIX})" \
  -srcfolder "$STAGING_DIR" \
  -ov \
  -format UDZO \
  "$DMG_PATH" >/dev/null

echo "==> Done"
echo "App bundle: $APP_DIR"
echo "DMG: $DMG_PATH"
if compgen -G "$SYMBOLS_DIR/*.dSYM" > /dev/null; then
  echo "Symbols: $SYMBOLS_DIR"
fi

if [[ "$INSTALL_APP" == "true" ]]; then
  echo "==> Installing app bundle"
  "$SCRIPT_DIR/install-macos-app.sh" "$DMG_PATH" --app-name "$APP_NAME"
fi

rm -rf "$APP_DIR" "$STAGING_DIR"
if [[ -d "$BUILD_DIR" ]] && [[ -z "$(find "$BUILD_DIR" -mindepth 1 -maxdepth 1 ! -name "${APP_NAME}.iconset" -print -quit)" ]]; then
  rm -rf "$BUILD_DIR"
fi

if [[ "$OPEN_OUTPUT" == "true" ]]; then
  open "$OUTPUT_ROOT"
fi

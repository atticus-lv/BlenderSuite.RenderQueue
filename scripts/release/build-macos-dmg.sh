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
PROJECT_FILE="$REPO_ROOT/src/BlenderSuite.RenderQueue/BlenderSuite.RenderQueue.csproj"
APP_NAME="BlenderSuite.RenderQueue"
APP_DISPLAY_NAME="Blender Suite: Render Queue"
APP_EXECUTABLE_NAME="BlenderSuite.RenderQueue"
APP_BUNDLE_NAME="${APP_NAME}.app"
APP_PRODUCT_ID="a8239aab-c146-434c-85c1-d6d56bc9b77c"
APP_BUNDLE_IDENTIFIER="com.atticus.blenderrenderqueue"
ADHOC_SIGN="${MACOS_ADHOC_SIGN:-true}"
PUBLISH_ROOT="$REPO_ROOT/install/macOS/publish"
BUILD_ROOT="$REPO_ROOT/install/macOS/build"
STAGING_ROOT="$REPO_ROOT/install/macOS/staging"
OUTPUT_ROOT="$REPO_ROOT/install/macOS/output"
SYMBOLS_ROOT="$REPO_ROOT/install/macOS/symbols"
ICON_SOURCE="$REPO_ROOT/src/BlenderSuite.RenderQueue/Assets/logo.png"
DMG_BACKGROUND_SOURCE="$REPO_ROOT/install/macOS/assets/dmg-background.png"
DMG_BACKGROUND_NAME="DmgBackground.png"
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
RW_DMG_PATH="$BUILD_DIR/${APP_NAME}-${APP_VERSION}-${RID}-${DMG_SUFFIX}-rw.dmg"
APP_EXECUTABLE="$APP_DIR/Contents/MacOS/$APP_EXECUTABLE_NAME"
ICONSET_DIR="$BUILD_DIR/${APP_NAME}.iconset"
ICON_FILE="$APP_DIR/Contents/Resources/AppIcon.icns"
DMG_VOLUME_NAME="${APP_NAME} ${APP_VERSION} (${VOLUME_SUFFIX})"

create_dmg_background() {
  local output_path="$1"

  python3 - "$output_path" <<'PY'
import math
import struct
import sys
import zlib

width = 640
height = 360
pixels = bytearray(width * height * 3)

def mix(a, b, t):
    return int(a + (b - a) * t)

def put(x, y, color):
    if 0 <= x < width and 0 <= y < height:
        i = (y * width + x) * 3
        pixels[i:i + 3] = bytes(color)

def blend(x, y, color, alpha):
    if 0 <= x < width and 0 <= y < height:
        i = (y * width + x) * 3
        inv = 1.0 - alpha
        pixels[i] = int(pixels[i] * inv + color[0] * alpha)
        pixels[i + 1] = int(pixels[i + 1] * inv + color[1] * alpha)
        pixels[i + 2] = int(pixels[i + 2] * inv + color[2] * alpha)

for y in range(height):
    vertical = y / max(1, height - 1)
    for x in range(width):
        left_glow = max(0.0, 1.0 - math.hypot((x - 130) / 360, (y - 118) / 260))
        center_glow = max(0.0, 1.0 - math.hypot((x - 344) / 270, (y - 202) / 190))
        right_glow = max(0.0, 1.0 - math.hypot((x - 520) / 330, (y - 120) / 250))
        vignette = min(1.0, math.hypot((x - width / 2) / width, (y - height / 2) / height) * 1.45)
        r = mix(31, 14, vertical) + int(14 * left_glow) + int(8 * center_glow) - int(12 * vignette)
        g = mix(37, 23, vertical) + int(18 * left_glow) + int(18 * center_glow) + int(5 * right_glow) - int(11 * vignette)
        b = mix(45, 30, vertical) + int(24 * left_glow) + int(30 * center_glow) + int(18 * right_glow) - int(9 * vignette)
        put(x, y, (max(0, r), max(0, g), max(0, b)))

for y in range(28, height, 24):
    for x in range(24, width, 24):
        blend(x, y, (118, 138, 150), 0.055)

for x in range(-80, width + 80, 42):
    for step in range(0, height + 160):
        xx = x + step
        yy = step - 80
        if 0 <= xx < width and 0 <= yy < height:
            blend(xx, yy, (85, 105, 116), 0.035)

def rounded_rect(cx, cy, w, h, radius, color, alpha):
    left = int(cx - w / 2)
    right = int(cx + w / 2)
    top = int(cy - h / 2)
    bottom = int(cy + h / 2)
    for yy in range(top, bottom):
        for xx in range(left, right):
            dx = max(left + radius - xx, 0, xx - (right - radius - 1))
            dy = max(top + radius - yy, 0, yy - (bottom - radius - 1))
            if dx * dx + dy * dy <= radius * radius:
                blend(xx, yy, color, alpha)

def ring(cx, cy, radius, thickness, color, alpha):
    outer = radius
    inner = radius - thickness
    for yy in range(cy - outer - 2, cy + outer + 3):
        for xx in range(cx - outer - 2, cx + outer + 3):
            dist = math.hypot(xx - cx, yy - cy)
            if inner <= dist <= outer:
                edge = 1.0 - min(abs(dist - inner), abs(dist - outer)) / max(1, thickness)
                blend(xx, yy, color, alpha * (0.45 + 0.55 * edge))

def draw_arrow(cx1, cy, cx2):
    for radius, alpha in ((96, 0.035), (62, 0.05), (34, 0.075)):
        for yy in range(cy - radius, cy + radius):
            for xx in range(320 - radius, 420 + radius):
                dist = min(abs(yy - cy), math.hypot(xx - 420, yy - cy))
                if dist <= radius:
                    blend(xx, yy, (89, 173, 218), alpha * (1 - dist / radius))

    for x in range(cx1, cx2):
        t = (x - cx1) / max(1, cx2 - cx1)
        color = (mix(76, 143, t), mix(154, 216, t), mix(202, 238, t))
        for dy in range(-4, 5):
            blend(x, cy + dy, color, max(0.0, 0.78 - abs(dy) * 0.13))

    tip = cx2 + 26
    for yy in range(cy - 23, cy + 24):
        span = 23 - abs(yy - cy)
        for xx in range(cx2 - 2, tip + 1):
            if xx >= tip - span * 1.35:
                blend(xx, yy, (130, 213, 246), 0.72)
    for yy in range(cy - 16, cy + 17):
        span = 16 - abs(yy - cy)
        for xx in range(cx2 + 4, tip - 1):
            if xx >= tip - span * 1.28:
                blend(xx, yy, (35, 69, 86), 0.18)

for cx, cy in ((195, 195), (495, 195)):
    rounded_rect(cx + 2, cy + 12, 156, 156, 30, (0, 0, 0), 0.16)
    rounded_rect(cx, cy, 156, 156, 30, (255, 255, 255), 0.06)
    rounded_rect(cx, cy, 148, 148, 26, (17, 27, 34), 0.12)
    rounded_rect(cx, cy - 2, 140, 140, 24, (108, 190, 227), 0.035)
    ring(cx, cy, 78, 2, (188, 224, 239), 0.52)
    ring(cx, cy, 62, 1, (120, 194, 226), 0.14)

draw_arrow(286, 195, 418)

for cx, cy, strength in ((195, 195, 0.07), (495, 195, 0.07), (355, 195, 0.09)):
    for radius, alpha in ((82, strength), (48, strength * 0.7), (22, strength * 0.5)):
        for yy in range(cy - radius, cy + radius):
            for xx in range(cx - radius, cx + radius):
                dist = math.hypot(xx - cx, yy - cy)
                if dist <= radius:
                    blend(xx, yy, (92, 168, 206), alpha * (1 - dist / radius))
            if dist <= radius:
                blend(xx, yy, (92, 154, 188), alpha * (1 - dist / radius))

def chunk(kind, data):
    return (
        struct.pack(">I", len(data))
        + kind
        + data
        + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    )

raw = b"".join(b"\x00" + pixels[y * width * 3:(y + 1) * width * 3] for y in range(height))
png = (
    b"\x89PNG\r\n\x1a\n"
    + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    + chunk(b"IDAT", zlib.compress(raw, 9))
    + chunk(b"IEND", b"")
)

with open(sys.argv[1], "wb") as output:
    output.write(png)
PY
}

configure_dmg_view() {
  local volume_name="$1"
  local mount_dir="$2"
  local background_path="$mount_dir/$APP_BUNDLE_NAME/Contents/Resources/$DMG_BACKGROUND_NAME"

  rm -rf "$mount_dir/.fseventsd" 2>/dev/null || true

  osascript <<EOF
tell application "Finder"
    tell disk "$volume_name"
        open
        set current view of container window to icon view
        set toolbar visible of container window to false
        set statusbar visible of container window to false
        set bounds of container window to {120, 120, 760, 480}
        set viewOptions to icon view options of container window
        set arrangement of viewOptions to not arranged
        set icon size of viewOptions to 104
        set background picture of viewOptions to POSIX file "$background_path"
        set position of item "$APP_BUNDLE_NAME" of container window to {170, 168}
        set position of item "Applications" of container window to {470, 168}
        update without registering applications
        delay 1
        close
    end tell
end tell
EOF
}

sign_app_bundle() {
  local app_dir="$1"

  if [[ "$ADHOC_SIGN" != "true" ]]; then
    echo "==> Skipping app bundle ad-hoc signing"
    return
  fi

  echo "==> Ad-hoc signing .app bundle"
  codesign --force --deep --sign - --timestamp=none "$app_dir"
  codesign --verify --deep --strict --verbose=2 "$app_dir"
}

sign_dmg() {
  local dmg_path="$1"

  if [[ "$ADHOC_SIGN" != "true" ]]; then
    echo "==> Skipping DMG ad-hoc signing"
    return
  fi

  echo "==> Ad-hoc signing DMG"
  codesign --force --sign - --timestamp=none "$dmg_path"
  codesign --verify --verbose=2 "$dmg_path"
}

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
  sips -z 1024 1024 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null
  iconutil -c icns "$ICONSET_DIR" -o "$ICON_FILE"
  ICON_PLIST=$'    <key>CFBundleIconFile</key>\n    <string>AppIcon.icns</string>\n    <key>CFBundleIconName</key>\n    <string>AppIcon</string>'
fi

cat > "$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>${APP_DISPLAY_NAME}</string>
    <key>CFBundleExecutable</key>
    <string>${APP_EXECUTABLE_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>${APP_BUNDLE_IDENTIFIER}</string>
    <key>BRQApplicationId</key>
    <string>${APP_PRODUCT_ID}</string>
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

if [[ -f "$DMG_BACKGROUND_SOURCE" ]]; then
  cp "$DMG_BACKGROUND_SOURCE" "$APP_DIR/Contents/Resources/$DMG_BACKGROUND_NAME"
else
  create_dmg_background "$APP_DIR/Contents/Resources/$DMG_BACKGROUND_NAME"
fi

sign_app_bundle "$APP_DIR"

echo "==> Creating DMG"
cp -R "$APP_DIR" "$STAGING_DIR/"
ln -s /Applications "$STAGING_DIR/Applications"
touch "$STAGING_DIR/$APP_BUNDLE_NAME"

rm -f "$DMG_PATH" "$RW_DMG_PATH"
hdiutil create \
  -volname "$DMG_VOLUME_NAME" \
  -srcfolder "$STAGING_DIR" \
  -ov \
  -format UDRW \
  "$RW_DMG_PATH" >/dev/null

MOUNT_DIR="/Volumes/$DMG_VOLUME_NAME"
if [[ -d "$MOUNT_DIR" ]]; then
  hdiutil detach "$MOUNT_DIR" >/dev/null 2>&1 || true
fi

hdiutil attach "$RW_DMG_PATH" \
  -readwrite \
  -noverify \
  -noautoopen \
  -mountpoint "$MOUNT_DIR" >/dev/null

if [[ -f "$ICON_FILE" ]]; then
  cp "$ICON_FILE" "$MOUNT_DIR/.VolumeIcon.icns"
  if command -v SetFile >/dev/null 2>&1; then
    SetFile -a V "$MOUNT_DIR/.VolumeIcon.icns" 2>/dev/null || true
    SetFile -a C "$MOUNT_DIR" 2>/dev/null || true
  fi
fi

configure_dmg_view "$DMG_VOLUME_NAME" "$MOUNT_DIR"
rm -rf "$MOUNT_DIR/.fseventsd" 2>/dev/null || true
sync
hdiutil detach "$MOUNT_DIR" >/dev/null

hdiutil convert "$RW_DMG_PATH" \
  -format UDZO \
  -imagekey zlib-level=9 \
  -o "$DMG_PATH" >/dev/null
rm -f "$RW_DMG_PATH"

sign_dmg "$DMG_PATH"

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

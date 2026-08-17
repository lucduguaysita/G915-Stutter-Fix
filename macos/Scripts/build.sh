#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

CONFIGURATION="${1:-release}"

# Build one binary carrying both slices, so a single .app runs on Apple Silicon
# and on Intel. GitHub retired its Intel hosted runners, so we can no longer
# build the x86_64 slice on its own machine; the arm64 runner cross-compiles it.
ARCHS=(--arch arm64 --arch x86_64)
swift build -c "$CONFIGURATION" "${ARCHS[@]}"

# Multi-arch output lands in .build/apple/Products/<Config>/, not .build/<config>/.
BIN="$(swift build -c "$CONFIGURATION" "${ARCHS[@]}" --show-bin-path)/G915StutterFix"
APP="$ROOT/dist/G915StutterFix.app"
MACOS_DIR="$APP/Contents/MacOS"
RESOURCES_DIR="$APP/Contents/Resources"

rm -rf "$APP"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
cp "$BIN" "$MACOS_DIR/G915StutterFix"
chmod +x "$MACOS_DIR/G915StutterFix"
printf 'APPL????' > "$APP/Contents/PkgInfo"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>G915StutterFix</string>
  <key>CFBundleIdentifier</key>
  <string>com.g915stutterfix.macos</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>G915 Stutter Fix</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHumanReadableCopyright</key>
  <string>MIT</string>
</dict>
</plist>
PLIST

# Ad-hoc sign so Accessibility can target a stable bundle id.
codesign --force --deep --sign - "$APP" 2>/dev/null || true

echo "Built: $APP"
lipo -info "$MACOS_DIR/G915StutterFix"
echo "Open with: open \"$APP\""
echo "Then grant Accessibility (System Settings → Privacy & Security → Accessibility)."

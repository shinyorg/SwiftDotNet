#!/bin/bash
set -euo pipefail

# Builds SwiftDotNetMaps.xcframework — the companion MapKit renderer for the `Map` control on Apple
# platforms. It is a SEPARATE framework from SwiftDotNetBridge (so MapKit stays out of the core bridge);
# apps that want maps link it and call swiftdotnet_register_maps() at startup.
#
# Prereq: build/SwiftDotNetBridge.xcframework must already exist (run build-xcframework.sh first) — this
# links against it and imports its module. iOS 17 / macOS 14 (SwiftUI MapContentBuilder). tvOS is skipped:
# SwiftUI's Map content builder isn't available there.

HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/MapRenderer.swift"
BRIDGE_SRC="$HERE/../SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift"
OUT="$HERE/../../build"
WORK="$OUT/_maps_work"
MODULE="SwiftDotNetMaps"
BRIDGE="SwiftDotNetBridge"
XCF="$OUT/$BRIDGE.xcframework"

[ -d "$XCF" ] || { echo "error: $XCF not found — run build-xcframework.sh first"; exit 1; }

rm -rf "$WORK" "$OUT/$MODULE.xcframework"
mkdir -p "$WORK"

# slice name → (target, sdk, bridge xcframework slice dir)
build_slice () {
  local name="$1" target="$2" sdk_name="$3" bridge_slice="$4"
  local dir="$WORK/$name"
  local fw="$dir/$MODULE.framework"
  local sdk; sdk="$(xcrun --sdk "$sdk_name" --show-sdk-path)"
  local bridge_fw_dir="$XCF/$bridge_slice"       # contains $BRIDGE.framework
  mkdir -p "$fw"

  echo "→ slice $name: regenerating bridge swiftmodule to import against"
  # The xcframework ships only the dylib, not the .swiftmodule, so emit a matching module (same source,
  # same target, library-evolution) purely to satisfy `import SwiftDotNetBridge`.
  swiftc -emit-module -emit-module-path "$dir/$BRIDGE.swiftmodule" \
    -module-name "$BRIDGE" -target "$target" -sdk "$sdk" -enable-library-evolution -O \
    "$BRIDGE_SRC"

  echo "→ slice $name: compiling MapRenderer ($target)"
  swiftc \
    -emit-library \
    -emit-module -emit-module-path "$dir/$MODULE.swiftmodule" \
    -module-name "$MODULE" \
    -target "$target" \
    -sdk "$sdk" \
    -enable-library-evolution \
    -O \
    -I "$dir" \
    -F "$bridge_fw_dir" -framework "$BRIDGE" \
    -Xlinker -install_name -Xlinker "@rpath/$MODULE.framework/$MODULE" \
    -o "$fw/$MODULE" \
    "$SRC"

  cat > "$fw/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleExecutable</key><string>$MODULE</string>
  <key>CFBundleIdentifier</key><string>com.swiftdotnet.$MODULE</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>$MODULE</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>MinimumOSVersion</key><string>17.0</string>
</dict>
</plist>
PLIST
}

build_slice "sim" "arm64-apple-ios17.0-simulator" "iphonesimulator" "ios-arm64-simulator"
build_slice "dev" "arm64-apple-ios17.0"           "iphoneos"        "ios-arm64"
build_slice "mac" "arm64-apple-macos14.0"         "macosx"          "macos-arm64"

echo "→ creating xcframework"
xcodebuild -create-xcframework \
  -framework "$WORK/sim/$MODULE.framework" \
  -framework "$WORK/dev/$MODULE.framework" \
  -framework "$WORK/mac/$MODULE.framework" \
  -output "$OUT/$MODULE.xcframework"

echo "✅ built $OUT/$MODULE.xcframework"

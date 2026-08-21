#!/bin/bash
set -euo pipefail

# Builds SwiftDotNetWidgets.xcframework (iOS device + iOS simulator, arm64) from the widget-subset
# interpreter sources.
#
# Two consumers link this, and they are different binaries:
#   * the APP, via <NativeReference> from SwiftDotNet.Live.Apple, for the @_cdecl entry points that drive
#     ActivityKit and WidgetCenter;
#   * the WIDGET EXTENSION, which uses SDNLiveView / SDNTimelineProvider / SDNActivityAttributes to
#     actually render. The extension contains no .NET at all.
#
# macOS and tvOS slices are deliberately absent: ActivityKit is iOS-only, and while WidgetKit exists on
# macOS, nothing here has been run there. Adding a slice that has never been exercised would make the
# status tables dishonest.
#
# No .swiftinterface is emitted, unlike a typical library-evolution build. AppIntents marks
# IntentParameter.init() unavailable, which makes the generated textual interface fail its own
# verification pass; the binary .swiftmodule is what the extension consumes anyway.

HERE="$(cd "$(dirname "$0")" && pwd)"
SRC=("$HERE"/Sources/SwiftDotNetWidgets/*.swift)
OUT="$HERE/../../build"
WORK="$OUT/_widgets_work"
MODULE="SwiftDotNetWidgets"
MIN_IOS="17.0"

rm -rf "$WORK" "$OUT/$MODULE.xcframework"
mkdir -p "$WORK"

SDK_SIM="$(xcrun --sdk iphonesimulator --show-sdk-path)"
SDK_DEV="$(xcrun --sdk iphoneos --show-sdk-path)"

build_slice () {
  local name="$1" target="$2" sdk="$3"
  local dir="$WORK/$name"
  local fw="$dir/$MODULE.framework"
  mkdir -p "$fw/Modules"

  echo "-> compiling slice: $name ($target)"
  swiftc \
    -emit-library \
    -emit-module -emit-module-path "$fw/Modules/$MODULE.swiftmodule" \
    -module-name "$MODULE" \
    -target "$target" \
    -sdk "$sdk" \
    -enable-library-evolution \
    -O \
    -Xlinker -install_name -Xlinker "@rpath/$MODULE.framework/$MODULE" \
    -o "$fw/$MODULE" \
    "${SRC[@]}"

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
  <key>MinimumOSVersion</key><string>$MIN_IOS</string>
</dict>
</plist>
PLIST
}

build_slice "sim" "arm64-apple-ios${MIN_IOS}-simulator" "$SDK_SIM"
build_slice "dev" "arm64-apple-ios${MIN_IOS}"           "$SDK_DEV"

echo "-> creating xcframework"
xcodebuild -create-xcframework \
  -framework "$WORK/sim/$MODULE.framework" \
  -framework "$WORK/dev/$MODULE.framework" \
  -output "$OUT/$MODULE.xcframework"

echo "built $OUT/$MODULE.xcframework"

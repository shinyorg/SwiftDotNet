#!/bin/bash
set -euo pipefail

# Builds SwiftDotNetBridge.xcframework (dynamic frameworks: iOS device + iOS simulator + macOS, arm64)
# from the Swift bridge sources, consumable by .NET for iOS/macOS via <NativeReference>.

HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/Sources/SwiftDotNetBridge/Bridge.swift"
OUT="$HERE/../../build"
WORK="$OUT/_bridge_work"
MODULE="SwiftDotNetBridge"
MIN_IOS="17.0"
MIN_MAC="14.0"
MIN_TVOS="17.0"

rm -rf "$WORK" "$OUT/$MODULE.xcframework"
mkdir -p "$WORK"

SDK_SIM="$(xcrun --sdk iphonesimulator --show-sdk-path)"
SDK_DEV="$(xcrun --sdk iphoneos --show-sdk-path)"
SDK_MAC="$(xcrun --sdk macosx --show-sdk-path)"
SDK_TVSIM="$(xcrun --sdk appletvsimulator --show-sdk-path)"
SDK_TVDEV="$(xcrun --sdk appletvos --show-sdk-path)"

build_slice () {
  local name="$1" target="$2" sdk="$3"
  local dir="$WORK/$name"
  local fw="$dir/$MODULE.framework"
  mkdir -p "$fw"

  echo "→ compiling slice: $name ($target)"
  swiftc \
    -emit-library \
    -emit-module -emit-module-path "$dir/$MODULE.swiftmodule" \
    -module-name "$MODULE" \
    -target "$target" \
    -sdk "$sdk" \
    -enable-library-evolution \
    -O \
    -Xlinker -install_name -Xlinker "@rpath/$MODULE.framework/$MODULE" \
    -o "$fw/$MODULE" \
    "$SRC"

  # Minimal iOS framework bundle (flat layout — no Versions dir on iOS).
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

# macOS uses the versioned framework bundle layout (Versions/A/...), unlike the flat iOS one.
build_mac_slice () {
  local dir="$WORK/mac"
  local fw="$dir/$MODULE.framework"
  mkdir -p "$fw/Versions/A/Resources" "$fw/Versions/A/Modules"

  echo "→ compiling slice: mac (arm64-apple-macos${MIN_MAC})"
  swiftc \
    -emit-library \
    -emit-module -emit-module-path "$fw/Versions/A/Modules/$MODULE.swiftmodule" \
    -module-name "$MODULE" \
    -target "arm64-apple-macos${MIN_MAC}" \
    -sdk "$SDK_MAC" \
    -enable-library-evolution \
    -O \
    -Xlinker -install_name -Xlinker "@rpath/$MODULE.framework/Versions/A/$MODULE" \
    -o "$fw/Versions/A/$MODULE" \
    "$SRC"

  cat > "$fw/Versions/A/Resources/Info.plist" <<PLIST
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
  <key>LSMinimumSystemVersion</key><string>$MIN_MAC</string>
</dict>
</plist>
PLIST

  ln -sfn A "$fw/Versions/Current"
  ln -sfn "Versions/Current/$MODULE" "$fw/$MODULE"
  ln -sfn "Versions/Current/Resources" "$fw/Resources"
  ln -sfn "Versions/Current/Modules" "$fw/Modules"
}

build_slice "sim" "arm64-apple-ios${MIN_IOS}-simulator" "$SDK_SIM"
build_slice "dev" "arm64-apple-ios${MIN_IOS}"           "$SDK_DEV"
build_slice "tvsim" "arm64-apple-tvos${MIN_TVOS}-simulator" "$SDK_TVSIM"
build_slice "tvdev" "arm64-apple-tvos${MIN_TVOS}"          "$SDK_TVDEV"
build_mac_slice

echo "→ creating xcframework"
xcodebuild -create-xcframework \
  -framework "$WORK/sim/$MODULE.framework" \
  -framework "$WORK/dev/$MODULE.framework" \
  -framework "$WORK/tvsim/$MODULE.framework" \
  -framework "$WORK/tvdev/$MODULE.framework" \
  -framework "$WORK/mac/$MODULE.framework" \
  -output "$OUT/$MODULE.xcframework"

echo "✅ built $OUT/$MODULE.xcframework"

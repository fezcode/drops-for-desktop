#!/usr/bin/env bash
# Wraps a published osx build (dist/<rid>) as Drops.app and a .dmg with an Applications link.
# Runs on macOS only: sips, iconutil, codesign and hdiutil are Apple's.
# The app is signed ad-hoc ("-"): Apple Silicon refuses unsigned code, but ad-hoc is not notarized, so the first
# open needs right-click > Open (or: xattr -dr com.apple.quarantine /Applications/Drops.app).
set -euo pipefail
rid="$1"; version="$2"
root="$(cd "$(dirname "$0")/../.." && pwd)"
src="$root/dist/$rid"
out="$root/dist/macos-$rid"
app="$out/Drops.app"
rm -rf "$out"; mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp -R "$src/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/Drops"

iconset="$out/Drops.iconset"; mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z $size $size "$root/Assets/icon-1024.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z $((size*2)) $((size*2)) "$root/Assets/icon-1024.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/Drops.icns"
rm -rf "$iconset"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Drops</string>
  <key>CFBundleDisplayName</key><string>Drops</string>
  <key>CFBundleIdentifier</key><string>com.fezcode.drops</string>
  <key>CFBundleExecutable</key><string>Drops</string>
  <key>CFBundleIconFile</key><string>Drops</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$app"
codesign --verify --deep "$app"

stage="$out/dmg"; mkdir -p "$stage"
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Applications"
dmg="$root/dist/Drops-$version-$rid.dmg"
rm -f "$dmg"
hdiutil create -volname "Drops $version" -srcfolder "$stage" -ov -format UDZO "$dmg" >/dev/null
echo "$dmg"

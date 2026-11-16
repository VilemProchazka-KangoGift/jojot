#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 4 ]; then
    echo "Usage: bash installer/package-unix.sh VERSION RID PUBLISH_DIR OUTPUT_DIR" >&2
    exit 2
fi

version=$1
rid=$2
publish=$(cd "$3" && pwd)
mkdir -p "$4"
output=$(cd "$4" && pwd)
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
icon="$repo/experiments/JoJot.Avalonia/Assets/jojot.svg"
stage=$(mktemp -d)
trap 'rm -rf -- "$stage"' EXIT

case "$rid" in
    linux-x64)
        app="$stage/opt/jojot"
        install -d "$app" "$stage/usr/bin" "$stage/usr/share/applications" \
            "$stage/usr/share/icons/hicolor/scalable/apps" "$stage/DEBIAN"
        cp -a "$publish/." "$app/"
        find "$app" -type f -name '*.dbg' -delete
        chmod 755 "$app/JoJot.Avalonia"
        glibc=$(
            find "$app" -type f \( -name '*.so' -o -name JoJot.Avalonia \) -exec objdump -T {} + |
                grep -o 'GLIBC_[0-9.]*' | sort -Vu | tail -1 | cut -d_ -f2
        )
        test -n "$glibc"
        cat >"$stage/usr/bin/jojot" <<'EOF'
#!/bin/sh
exec /opt/jojot/JoJot.Avalonia "$@"
EOF
        chmod 755 "$stage/usr/bin/jojot"
        install -m 644 "$icon" "$stage/usr/share/icons/hicolor/scalable/apps/jojot.svg"
        cat >"$stage/usr/share/applications/jojot.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=JoJot
Exec=jojot
Icon=jojot
Terminal=false
Categories=Utility;TextEditor;
EOF
        cat >"$stage/DEBIAN/control" <<EOF
Package: jojot
Version: $version
Architecture: amd64
Maintainer: Vilem Prochazka
Depends: libc6 (>= $glibc), libgcc-s1, libstdc++6, zlib1g, libicu78 | libicu77 | libicu76 | libicu74 | libicu72, libx11-6, libx11-xcb1, libxcb1, libxext6, libice6, libsm6, libfontconfig1, libfreetype6
Description: JoJot desktop notepad
 Tabbed notes with autosave and X11 workspace sessions.
EOF
        dpkg-deb --root-owner-group --build "$stage" "$output/JoJot-$version-linux-amd64.deb"
        ;;
    osx-arm64|osx-x64)
        contents="$stage/JoJot.app/Contents"
        install -d "$contents/MacOS"
        cp -a "$publish/." "$contents/MacOS/"
        find "$contents/MacOS" -type f -name '*.pdb' -delete
        chmod 755 "$contents/MacOS/JoJot.Avalonia"
        cat >"$contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key><string>io.github.vilemprochazka-kangogift.jojot</string>
    <key>CFBundleName</key><string>JoJot</string>
    <key>CFBundleExecutable</key><string>JoJot.Avalonia</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF
        archive="$output/JoJot-$version-$rid.zip"
        if [ -e "$archive" ]; then
            echo "Archive already exists: $archive" >&2
            exit 1
        fi
        (cd "$stage" && python3 -m zipfile -c "$archive" JoJot.app)
        ;;
    *)
        echo "Unsupported package target: $rid" >&2
        exit 2
        ;;
esac

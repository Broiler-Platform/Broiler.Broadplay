#!/usr/bin/env sh
set -eu

if [ "$#" -ne 1 ] || [ ! -x "$1" ]; then
    printf '%s\n' 'Usage: sh scripts/install-broadplay-desktop.sh /absolute/path/to/Broiler.Broadplay.Linux' >&2
    exit 2
fi
case "$1" in
    /*) ;;
    *) printf '%s\n' 'The executable path must be absolute.' >&2; exit 2 ;;
esac
# The Desktop Entry Exec key expands percent field codes and requires backslash
# escapes at both the key-value and argument parsing layers.
case "$1" in
    *'%'*|*'"'*|*'\'*|*'$'*|*'`'*|*'
'*) printf '%s\n' 'The install path contains unsupported desktop-entry characters.' >&2; exit 2 ;;
esac

applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$applications"
entry="$applications/org.broiler.Broadplay.desktop"
cat > "$entry" <<EOF
[Desktop Entry]
Type=Application
Name=Broiler Broadplay
Exec="$1" --window %F
Terminal=false
Categories=AudioVideo;Player;
MimeType=audio/mpeg;audio/wav;audio/flac;audio/ogg;audio/mp4;video/mp4;video/webm;video/x-matroska;audio/x-mpegurl;
EOF
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$applications"
fi

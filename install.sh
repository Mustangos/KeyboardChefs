#!/bin/bash
# Installs BepInEx into Overcooked! 2 and puts KeyboardChefs.dll into its plugins folder: the prebuilt
# DLL next to this script (release zip) or one compiled from KeyboardChefs.cs (source checkout).
set -euo pipefail

GAME="${OC2_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Overcooked! 2}"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_macos_universal_5.4.23.5.zip"
BEPINEX_SHA256="01c2ae782eb016dfd6c345a18dbd2dcafffb3d9d318449d6486689f426b4a323"
DOTNET="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"
HERE="$(cd "$(dirname "$0")" && pwd)"
MANAGED="$GAME/Overcooked2.app/Contents/Resources/Data/Managed"

[ -d "$MANAGED" ] || { echo "Overcooked! 2 not found in: $GAME (set OC2_DIR)" >&2; exit 1; }

if [ ! -f "$GAME/BepInEx/core/BepInEx.dll" ]; then
    tmp="$(mktemp -d)"
    curl -sSL -o "$tmp/bepinex.zip" "$BEPINEX_URL"
    echo "$BEPINEX_SHA256  $tmp/bepinex.zip" | shasum -a 256 -c -
    unzip -q -o "$tmp/bepinex.zip" -d "$GAME"
    rm -r "$tmp"
    sed -i '' 's/^executable_name=""/executable_name="Overcooked2.app"/' "$GAME/run_bepinex.sh"
    chmod +x "$GAME/run_bepinex.sh"
    echo "BepInEx installed."
fi

mkdir -p "$GAME/BepInEx/plugins"
if [ -f "$HERE/KeyboardChefs.dll" ]; then
    cp "$HERE/KeyboardChefs.dll" "$GAME/BepInEx/plugins/"
    echo "Plugin copied: $GAME/BepInEx/plugins/KeyboardChefs.dll"
else
    [ -x "$DOTNET" ] || { echo ".NET SDK not found; install it into ~/.dotnet (see README)" >&2; exit 1; }
    CSC="$(ls -d "$(dirname "$(readlink -f "$DOTNET")")"/sdk/*/Roslyn/bincore/csc.dll | tail -1)"
    refs=()
    for f in "$MANAGED"/*.dll "$GAME/BepInEx/core/BepInEx.dll"; do refs+=("-r:$f"); done
    "$DOTNET" "$CSC" -nologo -noconfig -nostdlib -target:library -langversion:7.3 -optimize \
        -out:"$GAME/BepInEx/plugins/KeyboardChefs.dll" "${refs[@]}" "$HERE/KeyboardChefs.cs"
    echo "Plugin built: $GAME/BepInEx/plugins/KeyboardChefs.dll"
fi
echo
echo "Steam > Overcooked! 2 > Properties > Launch Options:"
echo "\"$GAME/run_bepinex.sh\" %command%"

#!/usr/bin/env bash
# Packs avatar-characters/ into build/avatar-characters_<version>.zip, ready to
# drop into Factorio's mods folder or upload to the mod portal.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(python3 -c "import json;print(json.load(open('$REPO/avatar-characters/info.json'))['version'])")"
NAME="avatar-characters_${VERSION}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp -r "$REPO/avatar-characters" "$STAGE/$NAME"
mkdir -p "$REPO/build"
rm -f "$REPO/build/$NAME.zip"
(cd "$STAGE" && python3 -m zipfile -c "$REPO/build/$NAME.zip" "$NAME")
echo "built build/$NAME.zip"

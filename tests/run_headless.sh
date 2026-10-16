#!/usr/bin/env bash
# Loads the mod in a headless Factorio server and runs the swap tests.
#
#   tests/run_headless.sh /path/to/factorio [--space-age]
#
# /path/to/factorio is an unpacked Factorio (headless works) directory.
set -euo pipefail
FACTORIO_DIR="$(cd "$1" && pwd)"
SPACE_AGE="${2:-}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/mods"
cp -r "$REPO/dragon-character" "$WORK/mods/dragon-character"
cp -r "$REPO/tests/dragon-character-test" "$WORK/mods/dragon-character-test"
if [ "$SPACE_AGE" = "--space-age" ]; then DLC=true; else DLC=false; fi
cat > "$WORK/mods/mod-list.json" <<JSON
{"mods":[{"name":"base","enabled":true},{"name":"dragon-character","enabled":true},
{"name":"dragon-character-test","enabled":true},{"name":"space-age","enabled":$DLC},
{"name":"quality","enabled":$DLC},{"name":"elevated-rails","enabled":$DLC}]}
JSON
"$FACTORIO_DIR/bin/x64/factorio" --mod-directory "$WORK/mods" --create "$WORK/test.zip" > "$WORK/create.log" 2>&1 \
  || { cat "$WORK/create.log"; exit 1; }
"$FACTORIO_DIR/bin/x64/factorio" --mod-directory "$WORK/mods" --benchmark "$WORK/test.zip" \
  --benchmark-ticks 5 > "$WORK/run.log" 2>&1 || true
if grep -q "DRAGON-TEST: PASS" "$WORK/run.log"; then
  echo "DRAGON-TEST: PASS ($([ "$DLC" = true ] && echo "with Space Age" || echo "base game"))"
else
  cat "$WORK/run.log"
  exit 1
fi

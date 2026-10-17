#!/usr/bin/env bash
# Loads the mod in a headless Factorio server and runs the tests in
# tests/dragon-character-test.
#
#   tests/run_headless.sh /path/to/factorio [options]
#
#   --space-age     also enable Space Age, Quality and Elevated Rails
#   --test-packs    add two synthetic avatar packs (tools/avatar_pack/make_test_pack.py),
#                   test them, then load the save again without them, as when a
#                   pack is removed
#   --broken-packs  check that each deliberately broken pack stops the game from
#                   loading with the expected error
#   --pack PATH     add an avatar pack (folder or zip) to the test; may be repeated
#
# /path/to/factorio is an unpacked Factorio (headless works) directory.
set -euo pipefail
FACTORIO_DIR="$(cd "$1" && pwd)"
shift
DLC=false
TEST_PACKS=false
BROKEN_PACKS=false
PACKS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --space-age) DLC=true ;;
    --test-packs) TEST_PACKS=true ;;
    --broken-packs) BROKEN_PACKS=true ;;
    --pack) PACKS+=("$(realpath "$2")"); shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done
REPO="$(cd "$(dirname "$0")/.." && pwd)"
FACTORIO="$FACTORIO_DIR/bin/x64/factorio"
MAKE_PACK="$REPO/tools/avatar_pack/make_test_pack.py"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
if [ "$DLC" = true ]; then LABEL="with Space Age"; else LABEL="base game"; fi

fail() {
  echo "$1" >&2
  exit 1
}

# new_mods_dir DIR: the mod, the test mod and a mod-list that enables every mod in DIR
new_mods_dir() {
  mkdir -p "$1"
  cp -r "$REPO/dragon-character" "$1/dragon-character"
  cp -r "$REPO/tests/dragon-character-test" "$1/dragon-character-test"
}

# write_mod_list DIR: enables base, the DLC (or not) and every mod in DIR
write_mod_list() {
  python3 - "$1" "$DLC" <<'PY'
import json, os, sys, zipfile
mods_dir, dlc = sys.argv[1], sys.argv[2] == "true"
names = []
for entry in sorted(os.listdir(mods_dir)):
    path = os.path.join(mods_dir, entry)
    if os.path.isdir(path):
        info = json.load(open(os.path.join(path, "info.json")))
    elif entry.endswith(".zip"):
        with zipfile.ZipFile(path) as zf:
            info = json.loads(zf.read(next(n for n in zf.namelist() if n.count("/") == 1 and n.endswith("/info.json"))))
    else:
        continue
    names.append(info["name"])
mods = [{"name": "base", "enabled": True}]
mods += [{"name": n, "enabled": dlc} for n in ("space-age", "quality", "elevated-rails")]
mods += [{"name": n, "enabled": True} for n in names]
json.dump({"mods": mods}, open(os.path.join(mods_dir, "mod-list.json"), "w"))
PY
}

# run_benchmark MODS SAVE LOG EXPECTED: runs a few ticks and checks the test passed
run_benchmark() {
  "$FACTORIO" --mod-directory "$1" --benchmark "$2" --benchmark-ticks 5 > "$3" 2>&1 || true
  if grep -qF "$4" "$3"; then
    echo "$4 ($LABEL)"
  else
    cat "$3"
    fail "expected \"$4\""
  fi
}

if [ "$BROKEN_PACKS" = true ]; then
  for kind in $(python3 "$MAKE_PACK" --list-broken); do
    dir="$WORK/broken-$kind"
    new_mods_dir "$dir/mods"
    expected="$(python3 "$MAKE_PACK" --out "$dir/mods" --id broken --broken "$kind" | sed -n 's/^EXPECTED-ERROR: //p')"
    [ -n "$expected" ] || fail "no expected error for $kind"
    if python3 "$REPO/tools/validate_sprites.py" --pack "$dir/mods/dragon-avatar-broken" > "$dir/validate.log"; then
      fail "BROKEN-PACK $kind: tools/validate_sprites.py accepted it"
    fi
    write_mod_list "$dir/mods"
    if "$FACTORIO" --mod-directory "$dir/mods" --create "$dir/test.zip" > "$dir/create.log" 2>&1; then
      fail "BROKEN-PACK $kind: Factorio loaded it"
    fi
    grep -qF "$expected" "$dir/create.log" || { cat "$dir/create.log"; fail "BROKEN-PACK $kind: expected \"$expected\""; }
    echo "BROKEN-PACK $kind: PASS ($LABEL)"
    rm -rf "$dir"
  done
fi

MODS="$WORK/mods"
new_mods_dir "$MODS"
PACK_NAMES=()
if [ "$TEST_PACKS" = true ]; then
  python3 "$MAKE_PACK" --out "$MODS" --id test --title "Test Avatar" --flight --tint --max-sheet 1024 > /dev/null
  # a 30-character title of 90 bytes, and default timing scaled to 6 run / 8 mine frames
  python3 "$MAKE_PACK" --out "$MODS" --id small --title "$(printf '竜%.0s' $(seq 30))" --ppt 32 \
    --default-timing --frames run=6,mine=8 > /dev/null
  PACK_NAMES+=(dragon-avatar-test dragon-avatar-small)
fi
for pack in "${PACKS[@]+"${PACKS[@]}"}"; do
  cp -r "$pack" "$MODS/"
  PACK_NAMES+=("$(basename "$pack")")
done
for name in "${PACK_NAMES[@]+"${PACK_NAMES[@]}"}"; do
  python3 "$REPO/tools/validate_sprites.py" --pack "$MODS/$name" || fail "$name: tools/validate_sprites.py failed"
done
write_mod_list "$MODS"

"$FACTORIO" --mod-directory "$MODS" --create "$WORK/test.zip" > "$WORK/create.log" 2>&1 \
  || { cat "$WORK/create.log"; exit 1; }
if [ "$TEST_PACKS" = true ]; then
  run_benchmark "$MODS" "$WORK/test.zip" "$WORK/run.log" "DRAGON-TEST: PASS (with avatar packs)"
  # remove the packs from the save, as a player would after /dragon-revert-all
  for name in "${PACK_NAMES[@]}"; do
    rm -rf "${MODS:?}/$name"
  done
  write_mod_list "$MODS"
  run_benchmark "$MODS" "$WORK/test.zip" "$WORK/removed.log" "DRAGON-TEST: PASS (avatar packs removed)"
else
  run_benchmark "$MODS" "$WORK/test.zip" "$WORK/run.log" "DRAGON-TEST: PASS"
fi

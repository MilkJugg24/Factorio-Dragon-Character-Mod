"""Checks that every sprite sheet referenced by dragon-character/prototypes/sprite-data.lua
exists and has exactly the size Factorio will expect from the metadata. With --pack, checks
avatar packs (docs/avatar-pack-format.md) instead: folders or mod zips.

The headless server does not load graphics, so it can not catch these mistakes.

    python3 tools/validate_sprites.py
    python3 tools/validate_sprites.py --pack path/to/dragon-avatar-mika_1.0.0.zip [more packs...]
"""

import argparse
import os
import re
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lua_table  # noqa: E402
import avatar_pack.check as pack_check  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MOD = os.path.join(REPO, "dragon-character")
LUA = os.path.join(MOD, "prototypes", "sprite-data.lua")
MAX_SIZE = 8192
# Every animation the prototypes use, with (direction_count, frame_count)
EXPECTED = {
    "idle": None, "idle-gun": None, "run": None, "run-gun": 18, "mine": None,
    "hover": None, "hover-gun": None, "fly": None, "fly-gun": 18, "take-off": None, "corpse": 1,
}
LAYERS = ("base", "mask", "hoodie", "shorts", "shadow")
# Layers only some animations have (run-gun feeds flipped_shadow_running_with_gun)
EXTRA_LAYERS = {"run-gun": ("shadow-flipped",)}


def check_dragon():
    with open(LUA) as fh:
        data = lua_table.loads(fh.read())
    errors = []
    checked = 0
    for body in ("male", "female"):
        if body not in data:
            errors.append(f"missing body {body}")
            continue
        for anim, dirs in EXPECTED.items():
            meta = data[body].get(anim)
            if not meta:
                errors.append(f"{body}: missing animation {anim}")
                continue
            if dirs and meta["direction_count"] != dirs:
                errors.append(f"{body}/{anim}: direction_count {meta['direction_count']} != {dirs}")
            for layer in LAYERS + EXTRA_LAYERS.get(anim, ()):
                info = meta["layers"].get(layer)
                if not info:
                    errors.append(f"{body}/{anim}: missing layer {layer}")
                    continue
                path = info["filename"].replace("__dragon-character__", MOD)
                if not os.path.exists(path):
                    errors.append(f"missing file {path}")
                    continue
                w, h = Image.open(path).size
                ew = info["width"] * meta["frame_count"]
                eh = info["height"] * meta["direction_count"]
                if (w, h) != (ew, eh):
                    errors.append(f"{path}: size {w}x{h}, expected {ew}x{eh}")
                if w > MAX_SIZE or h > MAX_SIZE:
                    errors.append(f"{path}: larger than {MAX_SIZE}px")
                if Image.open(path).mode != "RGBA":
                    errors.append(f"{path}: not RGBA")
                checked += 1
    for name in ("thumbnail.png", "graphics/icons/dragon-character.png", "graphics/icons/shortcut-x56.png",
                 "graphics/icons/shortcut-x24.png", "graphics/icons/body-male.png",
                 "graphics/icons/body-female.png"):
        if not os.path.exists(os.path.join(MOD, name)):
            errors.append(f"missing {name}")
    with open(os.path.join(MOD, "scripts", "swatches.lua")) as fh:
        for swatch in re.findall(r'name = "([a-z-]+)"', fh.read()):
            if not os.path.exists(os.path.join(MOD, "graphics", "gui", f"swatch-{swatch}.png")):
                errors.append(f"missing swatch sprite {swatch}")
    if errors:
        print("\n".join(errors))
        return 1
    print(f"OK: {checked} sprite sheets match their metadata")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pack", nargs="+", metavar="PATH", help="avatar pack folders or zips to check")
    args = parser.parse_args(argv)
    if args.pack:
        failed = 0
        for path in args.pack:
            failed += 0 if pack_check.report(path) else 1
        return 1 if failed else 0
    return check_dragon()


if __name__ == "__main__":
    sys.exit(main())

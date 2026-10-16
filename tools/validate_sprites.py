"""Checks that every sprite sheet referenced by dragon-character/prototypes/sprite-data.lua
exists and has exactly the size Factorio will expect from the metadata.

The headless server does not load graphics, so it can not catch these mistakes.

    python3 tools/validate_sprites.py
"""

import os
import re
import sys

from PIL import Image

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


def parse(text):
    """Tiny parser for the generated Lua table (nested tables, strings, numbers)."""
    tokens = re.findall(r'\[\"[^\"]*\"\]|[A-Za-z_][A-Za-z0-9_]*|"[^"]*"|-?[0-9.]+(?:e-?[0-9]+)?|[{}=,]', text)
    pos = 0

    def value():
        nonlocal pos
        tok = tokens[pos]
        if tok == "{":
            pos += 1
            items = {}
            arr = []
            while tokens[pos] != "}":
                if pos + 1 < len(tokens) and tokens[pos + 1] == "=":
                    key = tokens[pos]
                    key = key[2:-2] if key.startswith('["') else key
                    pos += 2
                    items[key] = value()
                else:
                    arr.append(value())
                if tokens[pos] == ",":
                    pos += 1
            pos += 1
            return items if items else arr
        pos += 1
        if tok.startswith('"'):
            return tok[1:-1]
        return float(tok) if "." in tok or "e" in tok else int(tok)

    start = tokens.index("return") + 1
    pos = start
    return value()


def main():
    with open(LUA) as fh:
        data = parse(re.sub(r"--[^\n]*", "", fh.read()))
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
            for layer in LAYERS:
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


if __name__ == "__main__":
    sys.exit(main())

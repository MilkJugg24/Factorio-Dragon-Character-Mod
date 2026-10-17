"""Generate all dragon sprite sheets, icons and the Lua sprite metadata.

Usage (from the repository root):

    python3 -m tools.dragon_render.generate                 # everything
    python3 -m tools.dragon_render.generate --only idle,run  # some animations
    python3 -m tools.dragon_render.generate --extras         # icons/thumbnail/swatches/corpse only

Outputs go into the mod folder (``dragon-character/``).  Existing animation
sheets are skipped unless ``--force`` is given.
"""

import argparse
import json
import math
import os
import sys
import time

import numpy as np
from PIL import Image

from . import anims, model, render
from .model import V, Y, rot

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MOD_NAME = "dragon-character"
MOD_DIR = os.path.join(REPO, MOD_NAME)
SPRITE_DIR = os.path.join(MOD_DIR, "graphics", "entity", "dragon")
META_JSON = os.path.join(os.path.dirname(__file__), "sprite_meta.json")
LUA_OUT = os.path.join(MOD_DIR, "prototypes", "sprite-data.lua")

LAYERS = ("base", "mask", "hoodie", "shorts", "shadow")
SCALE = 0.5
MAX_SHEET = 4096

# Default dragon colour used for icons and the thumbnail (Factorio's default player orange)
ICON_TINT = (0.869, 0.5, 0.130)


def to_u8(img):
    return (np.clip(img, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def shadow_rgba(alpha):
    out = np.zeros(alpha.shape + (4,), dtype=np.uint8)
    out[..., 3] = to_u8(alpha)
    return out


def bbox_union(frames):
    """Union bounding box (x0, y0, x1, y1) of non transparent pixels; None if empty."""
    acc = None
    for f in frames:
        a = f[..., 3] > 0
        if not a.any():
            continue
        acc = a if acc is None else (acc | a)
    if acc is None:
        return None
    ys = np.where(acc.any(axis=1))[0]
    xs = np.where(acc.any(axis=0))[0]
    return int(xs[0]), int(ys[0]), int(xs[-1]) + 1, int(ys[-1]) + 1


def write_sheet(frames, dirs, nframes, anchor, path):
    """frames: list indexed [dir * nframes + frame] of uint8 RGBA canvases."""
    bb = bbox_union(frames)
    if bb is None:
        # completely empty layer: emit a 1x1 transparent frame
        bb = (anchor[0], anchor[1], anchor[0] + 1, anchor[1] + 1)
    x0, y0, x1, y1 = bb
    x0 = max(x0 - 1, 0)
    y0 = max(y0 - 1, 0)
    h_, w_ = frames[0].shape[:2]
    x1 = min(x1 + 1, w_)
    y1 = min(y1 + 1, h_)
    fw, fh = x1 - x0, y1 - y0
    sheet = np.zeros((fh * dirs, fw * nframes, 4), dtype=np.uint8)
    if sheet.shape[0] > MAX_SHEET or sheet.shape[1] > MAX_SHEET:
        raise RuntimeError(f"sheet too large {sheet.shape} for {path}")
    for d in range(dirs):
        for f in range(nframes):
            sheet[d * fh:(d + 1) * fh, f * fw:(f + 1) * fw] = frames[d * nframes + f][y0:y1, x0:x1]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(sheet, "RGBA").save(path, optimize=True)
    cx = x0 + fw / 2.0 - anchor[0]
    cy = y0 + fh / 2.0 - anchor[1]
    shift = [round(cx * SCALE / 32.0, 5), round(cy * SCALE / 32.0, 5)]
    return {"width": fw, "height": fh, "shift": shift}


def graphics_path(body, name, layer):
    return os.path.join(SPRITE_DIR, body, f"{name}-{layer}.png")


def mod_path(body, name, layer):
    return f"__{MOD_NAME}__/graphics/entity/dragon/{body}/{name}-{layer}.png"


# Animations that also get a "shadow-flipped" layer (Factorio's flipped_shadow_running_with_gun):
# the shadow drawn while the engine shows the sprite mirrored for a gun pointing west.
FLIPPED_SHADOW_ANIMATIONS = ("run-gun",)


def mirror_transform():
    """Mirrors the model left/right, turning a pose aiming east into one aiming west."""
    def f(v):
        return V(-v[0], v[1], v[2])

    return f, f


def animation_layers(anim):
    return LAYERS + (("shadow-flipped",) if anim in FLIPPED_SHADOW_ANIMATIONS else ())


def render_animation(body_name, anim):
    body = model.BODIES[body_name]
    dirs, nframes, fn, _ = anims.ANIMATIONS[anim]
    layers = animation_layers(anim)
    frames = {k: [] for k in layers}
    t0 = time.time()
    for d in range(dirs):
        o, kw = anims.direction(anim, d)
        for f in range(nframes):
            pose = fn(body, anims.frame_phase(anim, f), **kw)
            out = render.render_frame(body, pose, o)
            for k in ("base", "mask", "hoodie", "shorts"):
                frames[k].append(to_u8(out[k]))
            frames["shadow"].append(shadow_rgba(out["shadow"]))
            if "shadow-flipped" in layers:
                # the shadow of the mirrored dragon, still cast to the east
                out = render.render_frame(body, pose, (1.0 - o) % 1.0, want_clothes=False,
                                          xf_extra=mirror_transform())
                frames["shadow-flipped"].append(shadow_rgba(out["shadow"]))
        print(f"  {body_name}/{anim}: direction {d + 1}/{dirs} ({time.time() - t0:.0f}s)", flush=True)
    meta = {"direction_count": dirs, "frame_count": nframes, "layers": {}}
    for k in layers:
        anchor = ((render.SHADOW_AX, render.SHADOW_AY) if k.startswith("shadow")
                  else (render.ANCHOR_X, render.ANCHOR_Y))
        info = write_sheet(frames[k], dirs, nframes, anchor, graphics_path(body_name, anim, k))
        info["filename"] = mod_path(body_name, anim, k)
        meta["layers"][k] = info
    return meta


# --------------------------------------------------------------------------
# Corpse
# --------------------------------------------------------------------------

def corpse_transform():
    """Rotates the posed model onto its side so it lies on the ground."""
    ang = -math.pi / 2.0
    lift = V(0.0, 0.0, 0.2)

    def f(v):
        return rot(v, Y, ang) + lift + V(0.75, 0.0, 0.0)

    def d(v):
        return rot(v, Y, ang)

    return f, d


def render_corpse(body_name):
    body = model.BODIES[body_name]
    pose = anims.corpse_pose(body)
    out = render.render_frame(body, pose, 0.30, xf_extra=corpse_transform())
    meta = {"direction_count": 1, "frame_count": 1, "layers": {}}
    for k in LAYERS:
        img = shadow_rgba(out["shadow"]) if k == "shadow" else to_u8(out[k])
        anchor = ((render.SHADOW_AX, render.SHADOW_AY) if k.startswith("shadow")
                  else (render.ANCHOR_X, render.ANCHOR_Y))
        info = write_sheet([img], 1, 1, anchor, graphics_path(body_name, "corpse", k))
        info["filename"] = mod_path(body_name, "corpse", k)
        meta["layers"][k] = info
    return meta


# --------------------------------------------------------------------------
# Icons, thumbnail and GUI swatches
# --------------------------------------------------------------------------

def _composite_rgba(out, tint, hoodie=False, shorts=False):
    comp = render.composite(out, tint, hoodie=hoodie, shorts=shorts, background=None)
    return to_u8(comp)


def _fit(img, size, margin=0):
    a = img[..., 3] > 8
    ys = np.where(a.any(axis=1))[0]
    xs = np.where(a.any(axis=0))[0]
    crop = img[ys[0]:ys[-1] + 1, xs[0]:xs[-1] + 1]
    h, w = crop.shape[:2]
    side = max(h, w)
    sq = np.zeros((side, side, 4), dtype=np.uint8)
    sq[(side - h) // 2:(side - h) // 2 + h, (side - w) // 2:(side - w) // 2 + w] = crop
    inner = size - 2 * margin
    im = Image.fromarray(sq, "RGBA").resize((inner, inner), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(im, (margin, margin), im)
    return canvas


def head_portrait(body_name, ppt=240):
    """Large render of the head and shoulders, facing the viewer at an angle."""
    body = model.BODIES[body_name]
    pose = anims.idle(body, 0.0)
    w = h = 300
    # anchor (feet) placed below the canvas so that the head (z ~ 1.42) is centred
    head_screen = 1.42 * math.cos(render.CAM_ELEVATION) * render.CAM_STRETCH * ppt
    out = render.render_frame(body, pose, 0.62, want_clothes=False, want_shadow=False,
                              canvas=(w, h, w // 2, int(h * 0.47 + head_screen)), ppt=ppt, ss=2)
    img = _composite_rgba(out, ICON_TINT)
    # round badge: soft circular cut so the cropped body does not end in a hard line
    yy, xx = np.mgrid[0:h, 0:w]
    dist = np.sqrt((xx - w / 2 + 0.5) ** 2 + (yy - h / 2 + 0.5) ** 2)
    fade = np.clip((w * 0.5 - dist) / 4.0, 0.0, 1.0)
    img[..., 3] = (img[..., 3] * fade).astype(np.uint8)
    return img


def write_icons():
    icon_dir = os.path.join(MOD_DIR, "graphics", "icons")
    os.makedirs(icon_dir, exist_ok=True)
    portrait = head_portrait("male")
    _fit(portrait, 64, 1).save(os.path.join(icon_dir, "dragon-character.png"), optimize=True)
    _fit(portrait, 56, 2).save(os.path.join(icon_dir, "shortcut-x56.png"), optimize=True)
    _fit(portrait, 24, 0).save(os.path.join(icon_dir, "shortcut-x24.png"), optimize=True)
    # body shape option icons for the GUI
    for b in ("male", "female"):
        body = model.BODIES[b]
        out = render.render_frame(body, anims.idle(body, 0.0), 0.56, want_shadow=False,
                                  canvas=(220, 260, 110, 230), ppt=120, ss=2)
        _fit(_composite_rgba(out, ICON_TINT), 64, 2).save(
            os.path.join(icon_dir, f"body-{b}.png"), optimize=True)


def write_thumbnail():
    size = 144
    big = size * 2
    yy, xx = np.mgrid[0:big, 0:big]
    r = np.clip(np.sqrt((xx - big / 2) ** 2 + (yy - big / 2) ** 2) / (big * 0.75), 0, 1)
    base = np.array([44, 39, 34], dtype=float)
    glow = np.array([104, 80, 54], dtype=float)
    col = glow * (1 - r[..., None]) + base * r[..., None]
    bg = np.zeros((big, big, 4), dtype=np.uint8)
    bg[..., :3] = np.clip(col, 0, 255).astype(np.uint8)
    bg[..., 3] = 255
    img = Image.fromarray(bg, "RGBA")
    ppt = 112
    # (body, colour, hoodie, shorts, orientation, pose, centre x, feet y)
    specs = [
        ("female", (0.20, 0.62, 0.86), False, True, 0.36, "idle", 96, 258),
        ("male", ICON_TINT, True, True, 0.64, "idle", 194, 258),
    ]
    for b, tint, hoodie, shorts, o, pose_name, cx, feet in specs:
        body = model.BODIES[b]
        pose = anims.hover(body, 0.0, False) if pose_name == "hover" else anims.idle(body, 0.0)
        cw, ch = 260, 288
        out = render.render_frame(body, pose, o, want_shadow=False,
                                  canvas=(cw, ch, cw // 2, ch - 20), ppt=ppt, ss=2)
        rgba = Image.fromarray(_composite_rgba(out, tint, hoodie=hoodie, shorts=shorts), "RGBA")
        img.alpha_composite(rgba, (cx - cw // 2, feet - (ch - 20)) if feet - (ch - 20) >= 0 else (cx - cw // 2, 0))
    img = img.resize((size, size), Image.LANCZOS)
    img.save(os.path.join(MOD_DIR, "thumbnail.png"), optimize=True)


def load_swatches():
    """Reads the preset colours from the mod's scripts/swatches.lua."""
    import re
    path = os.path.join(MOD_DIR, "scripts", "swatches.lua")
    pat = re.compile(r'name = "([a-z-]+)", color = \{ r = ([0-9.]+), g = ([0-9.]+), b = ([0-9.]+) \}')
    with open(path) as fh:
        return [(m.group(1), tuple(float(m.group(i)) for i in (2, 3, 4)))
                for m in pat.finditer(fh.read())]


def write_swatches():
    d = os.path.join(MOD_DIR, "graphics", "gui")
    os.makedirs(d, exist_ok=True)
    size = 32
    swatches = load_swatches()
    assert swatches, "no swatches found"
    for name, col in swatches:
        sw = np.zeros((size, size, 4), dtype=np.uint8)
        c = np.array(col)
        sw[..., :3] = to_u8(c * 0.55)[None, None, :]
        sw[2:-2, 2:-2, :3] = to_u8(c)[None, None, :]
        # subtle top highlight
        sw[2:10, 2:-2, :3] = to_u8(np.minimum(c * 1.12 + 0.04, 1.0))[None, None, :]
        sw[..., 3] = 255
        sw[0, 0, 3] = sw[0, -1, 3] = sw[-1, 0, 3] = sw[-1, -1, 3] = 0
        Image.fromarray(sw, "RGBA").save(os.path.join(d, f"swatch-{name}.png"), optimize=True)


# --------------------------------------------------------------------------
# Metadata
# --------------------------------------------------------------------------

def load_meta():
    if os.path.exists(META_JSON):
        with open(META_JSON) as fh:
            return json.load(fh)
    return {}


def save_meta(meta):
    with open(META_JSON, "w") as fh:
        json.dump(meta, fh, indent=1, sort_keys=True)
    write_lua(meta)


def _lua_value(v, indent):
    pad = "  " * indent
    if isinstance(v, dict):
        items = []
        for k in sorted(v):
            key = k if k.isidentifier() else f'["{k}"]'
            items.append(f"{pad}  {key} = {_lua_value(v[k], indent + 1)}")
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    if isinstance(v, (list, tuple)):
        return "{" + ", ".join(_lua_value(x, indent) for x in v) + "}"
    if isinstance(v, str):
        return '"' + v + '"'
    if isinstance(v, float):
        return repr(round(v, 5))
    return str(v)


def write_lua(meta):
    os.makedirs(os.path.dirname(LUA_OUT), exist_ok=True)
    with open(LUA_OUT, "w") as fh:
        fh.write("-- Generated by tools/dragon_render/generate.py - do not edit by hand.\n")
        fh.write("-- Sprite sheet sizes and shifts for every dragon animation layer.\n")
        fh.write("-- Sheets are laid out with one row per direction (north first, clockwise).\n")
        fh.write("return " + _lua_value(meta, 0) + "\n")


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="", help="comma separated animation names")
    ap.add_argument("--bodies", default="male,female")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--extras", action="store_true", help="only icons, thumbnail, swatches, corpse")
    ap.add_argument("--no-extras", action="store_true")
    args = ap.parse_args(argv)
    meta = load_meta()
    bodies = [b for b in args.bodies.split(",") if b]
    names = [a for a in args.only.split(",") if a] or list(anims.ANIMATIONS)
    if not args.extras:
        for b in bodies:
            meta.setdefault(b, {})
            for a in names:
                done = all(os.path.exists(graphics_path(b, a, k)) for k in animation_layers(a))
                if done and not args.force and a in meta[b]:
                    print(f"skip {b}/{a}")
                    continue
                print(f"render {b}/{a}", flush=True)
                meta[b][a] = render_animation(b, a)
                save_meta(meta)
    if not args.no_extras:
        for b in bodies:
            meta.setdefault(b, {})
            print(f"render {b}/corpse", flush=True)
            meta[b]["corpse"] = render_corpse(b)
        save_meta(meta)
        print("icons / thumbnail / swatches", flush=True)
        write_icons()
        write_thumbnail()
        write_swatches()
    save_meta(meta)
    print("done")


if __name__ == "__main__":
    sys.exit(main())

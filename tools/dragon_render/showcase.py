"""Builds the animated README banner from the rendered sprite sheets.

Usage (from the repository root)::

    python3 -m tools.dragon_render.showcase          # -> docs/showcase.gif

Only needs ``numpy`` and ``pillow``: it reads the finished sheets in the mod
folder and composites them the way Factorio draws them, so run it after
``generate`` whenever the sprites change.  Each row is one body shape, each
column one animation, and the dragons turn through all eight directions.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SPRITE_DIR = os.path.join(REPO, "dragon-character", "graphics", "entity", "dragon")
META_JSON = os.path.join(os.path.dirname(__file__), "sprite_meta.json")
OUT_GIF = os.path.join(REPO, "docs", "showcase.gif")

# Sheets are drawn at scale 0.5, so one tile is 64 sheet pixels.
PX_PER_TILE = 64

# animation_speed from prototypes/dragon-animations.lua, in frames per game tick
SPEEDS = {"idle": 0.06, "run": 0.6, "run-gun": 0.6, "mine": 0.415, "hover": 0.27, "fly": 0.3}

# (sheet, column label)
COLUMNS = [
    ("idle", "Idle"),
    ("run", "Running"),
    ("run-gun", "Shooting"),
    ("mine", "Mining"),
    ("hover", "Hovering"),
    ("fly", "Flying"),
]

# (body, row label, scale colour, hoodie, shorts)
ROWS = [
    ("male", "Male", (0.869, 0.500, 0.130), True, True),
    ("female", "Female", (0.155, 0.540, 0.898), False, True),
]

# Facing order shown in the loop, as 8-direction sheet rows (0 = north, clockwise):
# start facing the viewer and turn clockwise.
FACINGS = [4, 5, 6, 7, 0, 1, 2, 3]

# 18-direction gun sheets (anims.GUN_MOVE_DIRECTIONS): the row aiming and running the
# same way, for gun directions north..south. West-facing rows are mirrored, as in Factorio.
GUN_FORWARD_ROWS = {0: 0, 1: 4, 2: 9, 3: 13, 4: 17}

CELL_W = 180          # sheet pixels per column
CELL_H = 276          # sheet pixels per row
FEET_X = 0.44         # ground position across a cell; shadows fall to the right
FEET_Y = 196          # ground line inside a cell (flying dragons reach 180 above it)
LABEL_W = 100         # left column with the row labels
HEADER_H = 44         # top row with the column labels
RIGHT_PAD = 72        # room for the last column's shadow
OUT_SCALE = 0.8       # final downscale of the whole banner

FPS = 20
SECONDS_PER_FACING = 1.2
TINT_ALPHA = 0.5      # matches render.composite
SHADOW_ALPHA = 0.5
COLORS = 255          # GIF palette size (one index spare)

BG_CENTRE = np.array([66, 58, 49], dtype=float) / 255.0
BG_EDGE = np.array([34, 30, 27], dtype=float) / 255.0
TEXT = (232, 220, 200)


def load_sheet(body, name, layer):
    im = Image.open(os.path.join(SPRITE_DIR, body, f"{name}-{layer}.png")).convert("RGBA")
    return np.asarray(im, dtype=np.float32) / 255.0


class Animation:
    def __init__(self, body, name, meta):
        self.dirs = meta["direction_count"]
        self.frames = meta["frame_count"]
        self.speed = SPEEDS[name]
        self.layers = {}
        for k in ("base", "mask", "hoodie", "shorts", "shadow", "shadow-flipped"):
            info = meta["layers"].get(k)
            if info:
                self.layers[k] = (load_sheet(body, name, k), info["width"], info["height"], info["shift"])

    def frame(self, layer, facing8, t):
        mirror = False
        if self.dirs == 18:
            mirror = facing8 > 4
            d = GUN_FORWARD_ROWS[8 - facing8 if mirror else facing8]
            if mirror and layer == "shadow" and "shadow-flipped" in self.layers:
                layer, mirror = "shadow-flipped", False
        else:
            d = round(facing8 * self.dirs / 8) % self.dirs
        sheet, w, h, shift = self.layers[layer]
        f = int(t * self.speed * 60) % self.frames
        img = sheet[d * h:(d + 1) * h, f * w:(f + 1) * w]
        if mirror:
            return img[:, ::-1], (-shift[0], shift[1])
        return img, shift


def paste(dst, img, cx, cy, mode, tint=None):
    """Blends img (straight alpha RGBA) centred on (cx, cy) into dst (RGB)."""
    h, w = img.shape[:2]
    x0, y0 = int(round(cx - w / 2)), int(round(cy - h / 2))
    sx0, sy0 = max(0, -x0), max(0, -y0)
    sx1, sy1 = min(w, dst.shape[1] - x0), min(h, dst.shape[0] - y0)
    if sx1 <= sx0 or sy1 <= sy0:
        return
    src = img[sy0:sy1, sx0:sx1]
    region = dst[y0 + sy0:y0 + sy1, x0 + sx0:x0 + sx1]
    a = src[..., 3:4]
    if mode == "shadow":
        region *= 1.0 - a * SHADOW_ALPHA
    elif mode == "tint":
        # runtime tint: premultiplied mask colour * tint, coverage * tint alpha
        region[:] = src[..., :3] * a * np.asarray(tint, dtype=np.float32) + region * (1.0 - a * TINT_ALPHA)
    else:
        region[:] = src[..., :3] * a + region * (1.0 - a)


def background(w, h):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((xx - w / 2) / (w * 0.6)) ** 2 + ((yy - h / 2) / (h * 0.75)) ** 2)
    r = np.clip(r, 0.0, 1.0)[..., None]
    return (BG_CENTRE * (1 - r) + BG_EDGE * r).astype(np.float32)


def font(size, bold=False):
    names = (["DejaVuSans-Bold.ttf", "Arial Bold.ttf", "arialbd.ttf"] if bold
             else ["DejaVuSans.ttf", "Arial.ttf", "arial.ttf"])
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            pass
    return ImageFont.load_default(size)


def draw_labels(img):
    """Draws the column and row labels onto the downscaled backdrop."""
    s = OUT_SCALE
    d = ImageDraw.Draw(img)
    f_col = font(round(20 * s), bold=True)
    f_row = font(round(22 * s), bold=True)
    for i, (_, label) in enumerate(COLUMNS):
        cx = (LABEL_W + (i + FEET_X) * CELL_W) * s
        d.text((cx, HEADER_H * 0.65 * s), label, fill=TEXT, font=f_col, anchor="mm")
    for j, (_, label, tint, *_rest) in enumerate(ROWS):
        cy = (HEADER_H + j * CELL_H + FEET_Y * 0.7) * s
        d.text((LABEL_W * 0.5 * s, cy), label, fill=TEXT, font=f_row, anchor="mm")
        sw = round(18 * s)
        col = tuple(int(c * 255) for c in tint)
        d.rounded_rectangle([LABEL_W * 0.5 * s - sw / 2, cy + 18 * s, LABEL_W * 0.5 * s + sw / 2, cy + 18 * s + sw],
                            radius=3, fill=col)
    return img


def build(out_path):
    with open(META_JSON) as fh:
        meta = json.load(fh)
    anims = {(b, n): Animation(b, n, meta[b][n]) for b, *_ in ROWS for n, _ in COLUMNS}

    W = LABEL_W + CELL_W * len(COLUMNS) + RIGHT_PAD
    H = HEADER_H + CELL_H * len(ROWS)
    bg = background(W, H)
    out_size = (round(W * OUT_SCALE), round(H * OUT_SCALE))

    per_facing = round(SECONDS_PER_FACING * FPS)
    frames = []
    for facing in FACINGS:
        for i in range(per_facing):
            # each facing restarts its animations, so the loop is seamless
            t = i / FPS
            canvas = bg.copy()
            for j, (body, _, tint, hoodie, shorts) in enumerate(ROWS):
                for c, (name, _) in enumerate(COLUMNS):
                    a = anims[(body, name)]
                    ax = LABEL_W + (c + FEET_X) * CELL_W
                    ay = HEADER_H + j * CELL_H + FEET_Y
                    order = [("shadow", "shadow"), ("base", "over"), ("mask", "tint")]
                    if shorts:
                        order.append(("shorts", "over"))
                    if hoodie:
                        order.append(("hoodie", "over"))
                    for layer, mode in order:
                        img, shift = a.frame(layer, facing, t)
                        paste(canvas, img, ax + shift[0] * PX_PER_TILE, ay + shift[1] * PX_PER_TILE,
                              mode, tint)
            rgb = Image.fromarray((np.clip(canvas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB")
            rgb = draw_labels(rgb.resize(out_size, Image.LANCZOS))
            frames.append(rgb)

    # one shared palette keeps the background from flickering between frames
    # (built from one frame per facing so every side of the dragons is covered)
    picks = frames[per_facing // 2::per_facing]
    sample = Image.new("RGB", (out_size[0], out_size[1] * len(picks)))
    for k, f in enumerate(picks):
        sample.paste(f, (0, out_size[1] * k))
    palette = sample.quantize(colors=COLORS, method=Image.Quantize.MEDIANCUT)
    pal_frames = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    pal_frames[0].save(out_path, save_all=True, append_images=pal_frames[1:], loop=0,
                       duration=round(1000 / FPS), optimize=True, disposal=1)
    print(f"wrote {out_path}: {out_size[0]}x{out_size[1]}, {len(frames)} frames, "
          f"{os.path.getsize(out_path) / 1e6:.1f} MB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default=OUT_GIF, help="output GIF path")
    build(ap.parse_args().out)


if __name__ == "__main__":
    main()

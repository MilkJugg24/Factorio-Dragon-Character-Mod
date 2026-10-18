"""Sprite sheet writing: 8-bit quantization, cropping and the sheet layout.

Only needs ``numpy`` and ``pillow`` (not the numba renderer), so the avatar
exporter's tests can cross-check against it.
"""

import os

import numpy as np
from PIL import Image

SCALE = 0.5
MAX_SHEET = 4096


def to_u8(img):
    return (np.clip(img, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


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

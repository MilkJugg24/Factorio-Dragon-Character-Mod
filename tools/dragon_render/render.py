"""Frame rendering: camera setup, render passes and supersampling."""

import math

import numpy as np

from . import sdf
from .model import build_scene
from .scene import BELLY, BODY, HOODIE, SHORTS, Scene, material_arrays

# Final sprite resolution: 64 px per tile (rendered for Factorio "scale = 0.5")
PPT = 64
SS = 2  # supersampling factor per axis

# Camera: orthographic, looking north and down, vertically stretched a bit so
# the character reads at a similar height to the vanilla engineer.
CAM_ELEVATION = math.radians(45.0)
CAM_STRETCH = 1.25

# Light used for shading (from the west, slightly towards the camera, above)
SHADE_LIGHT = np.array([-0.70, 0.35, 0.90])
SHADE_LIGHT = SHADE_LIGHT / np.linalg.norm(SHADE_LIGHT)
# Light used for the ground shadow (shadows fall to the east like vanilla)
SHADOW_LIGHT = np.array([-1.0, -0.12, 1.6])
SHADOW_LIGHT = SHADOW_LIGHT / np.linalg.norm(SHADOW_LIGHT)

# Canvas (final pixels) and anchor = entity position
CANVAS_W = 224
CANVAS_H = 256
ANCHOR_X = 112
ANCHOR_Y = 180

SHADOW_W = 320
SHADOW_H = 192
SHADOW_AX = 120
SHADOW_AY = 80

MAT_ALB, MAT_TINT, MAT_SPEC, MAT_EMIT = material_arrays()


def camera():
    phi = CAM_ELEVATION
    R = np.array([1.0, 0.0, 0.0])
    U = np.array([0.0, -math.sin(phi), math.cos(phi)])
    D = np.array([0.0, -math.cos(phi), -math.sin(phi)])
    return np.concatenate([R, U, D, [CAM_STRETCH]])


CAM = camera()


def orientation_transform(orientation):
    """Model -> world transform for a Factorio orientation (0 = north, clockwise)."""
    a = 2.0 * math.pi * orientation
    f = np.array([math.sin(a), -math.cos(a), 0.0])
    r = np.array([math.cos(a), math.sin(a), 0.0])
    M = np.column_stack([r, f, [0.0, 0.0, 1.0]])
    return (lambda v: M @ v), (lambda v: M @ v)


def _downsample(img, ss):
    if ss == 1:
        return img
    h, w = img.shape[0] // ss, img.shape[1] // ss
    return img.reshape(h, ss, w, ss, img.shape[2]).mean(axis=(1, 3))


def _unpremultiply(img):
    out = img.copy()
    a = img[..., 3:4]
    nz = a[..., 0] > 1e-6
    out[..., :3][nz] = img[..., :3][nz] / a[nz]
    out[..., :3] = np.clip(out[..., :3], 0.0, 1.0)
    return out


def _pass(arrays, active, target, w, h, ax, ay, ss, ppt=PPT):
    P, T, gstart, gend, GC, GCn = arrays
    act = np.array(active, dtype=np.int64)
    bmin, bmax = Scene.bounds(P, T, gstart, gend, act)
    color = np.zeros((h * ss, w * ss, 4))
    mask = np.zeros((h * ss, w * ss, 4))
    sdf.render(P, T, gstart, gend, GC, GCn, act, target,
               w * ss, h * ss, float(ppt * ss), float(ax * ss), float(ay * ss), CAM, SHADE_LIGHT,
               bmin, bmax, MAT_ALB, MAT_TINT, MAT_SPEC, MAT_EMIT, BELLY, color, mask)
    return _unpremultiply(_downsample(color, ss)), _unpremultiply(_downsample(mask, ss))


def render_frame(body, pose, orientation, want_clothes=True, want_shadow=True,
                 xf_extra=None, canvas=None, ppt=PPT, ss=SS, shadow_canvas=None):
    """Render all layers of one frame.

    Returns dict of float arrays (H, W, 4) straight alpha:
    base, mask, hoodie, shorts and (H', W') alpha array 'shadow'.
    """
    xf, xd = orientation_transform(orientation)
    if xf_extra is not None:
        f0, d0 = xf, xd
        ef, ed = xf_extra
        xf = lambda v: f0(ef(v))  # noqa: E731
        xd = lambda v: d0(ed(v))  # noqa: E731
    w, h, ax, ay = canvas or (CANVAS_W, CANVAS_H, ANCHOR_X, ANCHOR_Y)
    sc = build_scene(pose, body, xf, xd, hoodie=want_clothes, shorts=want_clothes)
    arrays = sc.build()
    out = {}
    out["base"], out["mask"] = _pass(arrays, (1, 0, 0, 0), -1, w, h, ax, ay, ss, ppt)
    if want_clothes:
        out["hoodie"], _ = _pass(arrays, (1, 1, 0, 0), HOODIE, w, h, ax, ay, ss, ppt)
        out["shorts"], _ = _pass(arrays, (1, 0, 1, 0), SHORTS, w, h, ax, ay, ss, ppt)
    if want_shadow:
        P, T, gstart, gend, GC, GCn = arrays
        sw, sh, sax, say = shadow_canvas or (SHADOW_W, SHADOW_H, SHADOW_AX, SHADOW_AY)
        alpha = np.zeros((sh, sw))
        sdf.render_ground_shadow(P, T, gstart, gend, GC, GCn, np.array([1, 0, 0, 0], dtype=np.int64),
                                 sw, sh, float(ppt), float(sax), float(say),
                                 CAM, SHADOW_LIGHT, alpha)
        out["shadow"] = alpha
    return out


def composite(layers, tint, hoodie=False, shorts=False, background=None, shadow_alpha=0.55,
              tint_alpha=0.5):
    """Preview composite of the layers the way Factorio draws them."""
    base = layers["base"]
    h, w = base.shape[:2]
    if background is None:
        out = np.zeros((h, w, 4))
    else:
        out = np.zeros((h, w, 4))
        out[..., :3] = background
        out[..., 3] = 1.0

    def over(dst, src):
        a = src[..., 3:4]
        res = dst.copy()
        res[..., :3] = src[..., :3] * a + dst[..., :3] * (1 - a)
        res[..., 3:4] = a + dst[..., 3:4] * (1 - a)
        return res

    if "shadow" in layers and background is not None:
        sh = layers["shadow"]
        # place the shadow canvas relative to the main canvas anchors
        canvas_sh = np.zeros((h, w, 4))
        oy = ANCHOR_Y - SHADOW_AY
        ox = ANCHOR_X - SHADOW_AX
        for y in range(sh.shape[0]):
            ty = y + oy
            if 0 <= ty < h:
                xs = np.arange(sh.shape[1]) + ox
                ok = (xs >= 0) & (xs < w)
                canvas_sh[ty, xs[ok], 3] = sh[y, ok] * shadow_alpha
        out = over(out, canvas_sh)
    out = over(out, base)
    # runtime tint: premultiplied mask colour * tint, coverage * tint alpha
    m = layers["mask"]
    a = m[..., 3:4]
    res = out.copy()
    res[..., :3] = m[..., :3] * a * np.asarray(tint)[None, None, :] + out[..., :3] * (1 - a * tint_alpha)
    res[..., 3:4] = np.maximum(out[..., 3:4], a)
    out = res
    if shorts and "shorts" in layers:
        out = over(out, layers["shorts"])
    if hoodie and "hoodie" in layers:
        out = over(out, layers["hoodie"])
    return out

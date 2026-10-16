"""Scene builder: collects primitives into the flat arrays used by sdf.py."""

import numpy as np

from . import sdf

# Groups
BODY = 0
HOODIE = 1
SHORTS = 2

# Materials (index into MATERIALS)
SCALE = 0
BELLY = 1
HORN = 2
CLAW = 3
EYE = 4
MEMBRANE = 5
SPINE = 6
HOODIE_MAT = 7
HOODIE_DARK = 8
STRING = 9
SHORTS_MAT = 10
SHORTS_BAND = 11
GUN_METAL = 12
GUN_LIGHT = 13
WOOD = 14
STEEL = 15
PUPIL = 16

# name: (albedo rgb, runtime tint weight, specular, emissive)
MATERIALS = [
    ((0.40, 0.40, 0.40), 1.00, 0.10, 0.0),   # SCALE (fully player-tinted)
    ((0.97, 0.88, 0.70), 0.20, 0.06, 0.0),   # BELLY
    ((0.74, 0.68, 0.57), 0.00, 0.16, 0.0),   # HORN
    ((0.20, 0.19, 0.22), 0.00, 0.35, 0.0),   # CLAW
    ((1.00, 0.80, 0.20), 0.00, 0.70, 0.55),  # EYE
    ((0.96, 0.88, 0.76), 0.55, 0.02, 0.0),   # MEMBRANE
    ((0.86, 0.81, 0.70), 0.35, 0.15, 0.0),   # SPINE
    ((0.38, 0.39, 0.43), 0.00, 0.02, 0.0),   # HOODIE
    ((0.27, 0.28, 0.31), 0.00, 0.02, 0.0),   # HOODIE_DARK
    ((0.93, 0.93, 0.93), 0.00, 0.05, 0.0),   # STRING
    ((0.21, 0.31, 0.52), 0.00, 0.03, 0.0),   # SHORTS
    ((0.13, 0.18, 0.31), 0.00, 0.03, 0.0),   # SHORTS_BAND
    ((0.22, 0.23, 0.26), 0.00, 0.45, 0.0),   # GUN_METAL
    ((0.46, 0.47, 0.51), 0.00, 0.45, 0.0),   # GUN_LIGHT
    ((0.53, 0.35, 0.19), 0.00, 0.10, 0.0),   # WOOD
    ((0.64, 0.66, 0.70), 0.00, 0.60, 0.0),   # STEEL
    ((0.05, 0.03, 0.02), 0.00, 0.50, 0.0),   # PUPIL
]


def material_arrays():
    alb = np.array([m[0] for m in MATERIALS], dtype=np.float64)
    tint = np.array([m[1] for m in MATERIALS], dtype=np.float64)
    spec = np.array([m[2] for m in MATERIALS], dtype=np.float64)
    emit = np.array([m[3] for m in MATERIALS], dtype=np.float64)
    return alb, tint, spec, emit


def _norm(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


class Scene:
    """Collects primitives in *world* space.

    ``xf`` is a callable mapping model-space points to world space and
    ``xd`` maps model-space directions to world space.
    """

    def __init__(self, xf, xd):
        self.xf = xf
        self.xd = xd
        self.rows = []  # (group, P row, T row)

    def _row(self, ptype, group, mat, k, belly, belly_t, clips):
        P = np.zeros(sdf.P_SIZE)
        T = np.zeros(sdf.I_SIZE, dtype=np.int64)
        T[sdf.I_TYPE] = ptype
        T[sdf.I_GROUP] = group
        T[sdf.I_MAT] = mat
        P[sdf.P_K] = k
        if belly is not None:
            P[sdf.P_BELLY:sdf.P_BELLY + 3] = _norm(self.xd(np.asarray(belly, float)))
            P[sdf.P_BELLY_T] = belly_t
        clips = list(clips or [])
        T[sdf.I_NCLIP] = len(clips)
        for j, (point, normal) in enumerate(clips[:2]):
            n = _norm(self.xd(np.asarray(normal, float)))
            p = self.xf(np.asarray(point, float))
            off = sdf.P_CLIP1 + 4 * j
            P[off:off + 3] = n
            P[off + 3] = float(np.dot(n, p))
        return P, T

    def sphere(self, c, r, mat, group=BODY, k=0.0, belly=None, belly_t=0.5, clips=None):
        P, T = self._row(sdf.SPHERE, group, mat, k, belly, belly_t, clips)
        c = self.xf(np.asarray(c, float))
        P[0:3] = c
        P[sdf.P_R] = r
        P[sdf.P_BOUND:sdf.P_BOUND + 3] = c
        P[sdf.P_BOUND + 3] = r
        self.rows.append((group, P, T))

    def cone(self, a, b, r1, r2, mat, group=BODY, k=0.0, belly=None, belly_t=0.5, clips=None):
        P, T = self._row(sdf.ROUND_CONE, group, mat, k, belly, belly_t, clips)
        a = self.xf(np.asarray(a, float))
        b = self.xf(np.asarray(b, float))
        P[0:3] = a
        P[3:6] = b
        P[sdf.P_R] = r1
        P[sdf.P_R + 1] = r2
        P[sdf.P_BOUND:sdf.P_BOUND + 3] = (a + b) * 0.5
        P[sdf.P_BOUND + 3] = np.linalg.norm(b - a) * 0.5 + max(r1, r2)
        self.rows.append((group, P, T))

    def ellipsoid(self, c, radii, axes, mat, group=BODY, k=0.0, belly=None, belly_t=0.5,
                  clips=None):
        """axes: three model-space unit vectors (local x, y, z)."""
        P, T = self._row(sdf.ELLIPSOID, group, mat, k, belly, belly_t, clips)
        c = self.xf(np.asarray(c, float))
        P[0:3] = c
        P[sdf.P_R:sdf.P_R + 3] = radii
        rot = np.array([_norm(self.xd(np.asarray(a, float))) for a in axes])
        P[sdf.P_ROT:sdf.P_ROT + 9] = rot.reshape(-1)
        P[sdf.P_BOUND:sdf.P_BOUND + 3] = c
        P[sdf.P_BOUND + 3] = max(radii)
        self.rows.append((group, P, T))

    def triangle(self, a, b, c, thickness, mat, group=BODY, k=0.0, clips=None):
        P, T = self._row(sdf.TRIANGLE, group, mat, k, None, 0.0, clips)
        pts = [self.xf(np.asarray(v, float)) for v in (a, b, c)]
        P[0:3], P[3:6], P[6:9] = pts
        P[sdf.P_R] = thickness
        cen = (pts[0] + pts[1] + pts[2]) / 3.0
        P[sdf.P_BOUND:sdf.P_BOUND + 3] = cen
        P[sdf.P_BOUND + 3] = max(np.linalg.norm(p - cen) for p in pts) + thickness
        self.rows.append((group, P, T))

    def build(self):
        rows = sorted(self.rows, key=lambda r: r[0])
        n = len(rows)
        P = np.zeros((n, sdf.P_SIZE))
        T = np.zeros((n, sdf.I_SIZE), dtype=np.int64)
        gstart = np.zeros(sdf.MAX_GROUPS, dtype=np.int64)
        gend = np.zeros(sdf.MAX_GROUPS, dtype=np.int64)
        for i, (g, p, t) in enumerate(rows):
            P[i] = p
            T[i] = t
        for g in range(sdf.MAX_GROUPS):
            idx = [i for i, r in enumerate(rows) if r[0] == g]
            if idx:
                gstart[g] = idx[0]
                gend[g] = idx[-1] + 1
        GC = np.zeros((sdf.MAX_GROUPS, 2, 4))
        GCn = np.zeros(sdf.MAX_GROUPS, dtype=np.int64)
        # bounding box of all primitives (per group set computed by caller)
        return P, T, gstart, gend, GC, GCn

    @staticmethod
    def bounds(P, T, gstart, gend, active, pad=0.05):
        lo = np.full(3, 1e9)
        hi = np.full(3, -1e9)
        for g in range(sdf.MAX_GROUPS):
            if not active[g]:
                continue
            for i in range(gstart[g], gend[g]):
                c = P[i, sdf.P_BOUND:sdf.P_BOUND + 3]
                r = P[i, sdf.P_BOUND + 3] + P[i, sdf.P_K]
                lo = np.minimum(lo, c - r)
                hi = np.maximum(hi, c + r)
        return lo - pad, hi + pad

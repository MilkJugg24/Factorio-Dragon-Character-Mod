"""Signed-distance-field ray marcher used to render the dragon sprites.

The scene is a flat list of primitives (spheres, round cones, ellipsoids and
thin triangles) grouped into "groups" (body, hoodie, shorts).  Primitives in a
group are blended together with a polynomial smooth-min; groups are combined
with a hard min so that clothing keeps crisp edges.

Everything performance sensitive is compiled with numba.
"""

import math

import numpy as np
from numba import njit, prange

# Primitive types
SPHERE = 0
ROUND_CONE = 1
ELLIPSOID = 2
TRIANGLE = 3

# Column layout of the float parameter array (one row per primitive)
P_A = 0          # 0:3   point a / centre
P_B = 3          # 3:6   point b
P_C = 6          # 6:9   point c (triangles)
P_R = 9          # 9:12  radii (r1, r2) / ellipsoid radii / triangle thickness
P_ROT = 12       # 12:21 ellipsoid world->local rotation (row major)
P_K = 21         # smooth blend radius
P_CLIP1 = 22     # 22:26 clip plane (nx, ny, nz, d): keeps n.p <= d
P_CLIP2 = 26     # 26:30 second clip plane
P_BELLY = 30     # 30:33 belly axis (world space, zero = none)
P_BELLY_T = 33   # belly threshold
P_BOUND = 34     # 34:38 bounding sphere (cx, cy, cz, r)
P_SIZE = 38

# Integer column layout
I_TYPE = 0
I_GROUP = 1
I_MAT = 2
I_NCLIP = 3
I_SIZE = 4

MAX_GROUPS = 4


@njit(cache=True, fastmath=True, inline="always")
def _smin(a, b, k):
    if k <= 0.0:
        return min(a, b)
    h = max(k - abs(a - b), 0.0) / k
    return min(a, b) - h * h * k * 0.25


@njit(cache=True, fastmath=True)
def _dot2(x, y, z):
    return x * x + y * y + z * z


@njit(cache=True, fastmath=True)
def _sd_round_cone(px, py, pz, ax, ay, az, bx, by, bz, r1, r2):
    bax = bx - ax
    bay = by - ay
    baz = bz - az
    l2 = bax * bax + bay * bay + baz * baz
    if l2 < 1e-12:
        return math.sqrt(_dot2(px - ax, py - ay, pz - az)) - max(r1, r2)
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    pax = px - ax
    pay = py - ay
    paz = pz - az
    y = pax * bax + pay * bay + paz * baz
    z = y - l2
    qx = pax * l2 - bax * y
    qy = pay * l2 - bay * y
    qz = paz * l2 - baz * y
    x2 = qx * qx + qy * qy + qz * qz
    y2 = y * y * l2
    z2 = z * z * l2
    s = 1.0 if rr >= 0.0 else -1.0
    k = s * rr * rr * x2
    sz = 1.0 if z >= 0.0 else -1.0
    sy = 1.0 if y >= 0.0 else -1.0
    if sz * a2 * z2 > k:
        return math.sqrt(x2 + z2) * il2 - r2
    if sy * a2 * y2 < k:
        return math.sqrt(x2 + y2) * il2 - r1
    return (math.sqrt(x2 * a2 * il2) + y * rr) * il2 - r1


@njit(cache=True, fastmath=True)
def _ud_triangle(px, py, pz, ax, ay, az, bx, by, bz, cx, cy, cz):
    bax = bx - ax
    bay = by - ay
    baz = bz - az
    pax = px - ax
    pay = py - ay
    paz = pz - az
    cbx = cx - bx
    cby = cy - by
    cbz = cz - bz
    pbx = px - bx
    pby = py - by
    pbz = pz - bz
    acx = ax - cx
    acy = ay - cy
    acz = az - cz
    pcx = px - cx
    pcy = py - cy
    pcz = pz - cz
    # nor = cross(ba, ac)
    nx = bay * acz - baz * acy
    ny = baz * acx - bax * acz
    nz = bax * acy - bay * acx
    # cross(ba, nor) . pa
    c1x = bay * nz - baz * ny
    c1y = baz * nx - bax * nz
    c1z = bax * ny - bay * nx
    s1 = 1.0 if (c1x * pax + c1y * pay + c1z * paz) >= 0.0 else -1.0
    c2x = cby * nz - cbz * ny
    c2y = cbz * nx - cbx * nz
    c2z = cbx * ny - cby * nx
    s2 = 1.0 if (c2x * pbx + c2y * pby + c2z * pbz) >= 0.0 else -1.0
    c3x = acy * nz - acz * ny
    c3y = acz * nx - acx * nz
    c3z = acx * ny - acy * nx
    s3 = 1.0 if (c3x * pcx + c3y * pcy + c3z * pcz) >= 0.0 else -1.0
    if s1 + s2 + s3 < 2.0:
        t = (bax * pax + bay * pay + baz * paz) / max(_dot2(bax, bay, baz), 1e-12)
        t = min(max(t, 0.0), 1.0)
        d1 = _dot2(bax * t - pax, bay * t - pay, baz * t - paz)
        t = (cbx * pbx + cby * pby + cbz * pbz) / max(_dot2(cbx, cby, cbz), 1e-12)
        t = min(max(t, 0.0), 1.0)
        d2 = _dot2(cbx * t - pbx, cby * t - pby, cbz * t - pbz)
        t = (acx * pcx + acy * pcy + acz * pcz) / max(_dot2(acx, acy, acz), 1e-12)
        t = min(max(t, 0.0), 1.0)
        d3 = _dot2(acx * t - pcx, acy * t - pcy, acz * t - pcz)
        return math.sqrt(min(d1, min(d2, d3)))
    d = nx * pax + ny * pay + nz * paz
    return math.sqrt(d * d / max(_dot2(nx, ny, nz), 1e-12))


@njit(cache=True, fastmath=True)
def prim_dist(i, px, py, pz, P, T):
    t = T[i, I_TYPE]
    if t == SPHERE:
        d = math.sqrt(_dot2(px - P[i, 0], py - P[i, 1], pz - P[i, 2])) - P[i, P_R]
    elif t == ROUND_CONE:
        d = _sd_round_cone(px, py, pz, P[i, 0], P[i, 1], P[i, 2], P[i, 3], P[i, 4], P[i, 5],
                           P[i, P_R], P[i, P_R + 1])
    elif t == ELLIPSOID:
        dx = px - P[i, 0]
        dy = py - P[i, 1]
        dz = pz - P[i, 2]
        lx = P[i, 12] * dx + P[i, 13] * dy + P[i, 14] * dz
        ly = P[i, 15] * dx + P[i, 16] * dy + P[i, 17] * dz
        lz = P[i, 18] * dx + P[i, 19] * dy + P[i, 20] * dz
        rx = P[i, P_R]
        ry = P[i, P_R + 1]
        rz = P[i, P_R + 2]
        k0 = math.sqrt(_dot2(lx / rx, ly / ry, lz / rz))
        k1 = math.sqrt(_dot2(lx / (rx * rx), ly / (ry * ry), lz / (rz * rz)))
        if k1 < 1e-9:
            d = -min(rx, min(ry, rz))
        else:
            d = k0 * (k0 - 1.0) / k1
    else:
        d = _ud_triangle(px, py, pz, P[i, 0], P[i, 1], P[i, 2], P[i, 3], P[i, 4], P[i, 5],
                         P[i, 6], P[i, 7], P[i, 8]) - P[i, P_R]
    nclip = T[i, I_NCLIP]
    if nclip >= 1:
        d = max(d, P[i, 22] * px + P[i, 23] * py + P[i, 24] * pz - P[i, 25])
    if nclip >= 2:
        d = max(d, P[i, 26] * px + P[i, 27] * py + P[i, 28] * pz - P[i, 29])
    return d


@njit(cache=True, fastmath=True)
def scene(px, py, pz, P, T, gstart, gend, GC, GCn, active):
    """Returns (distance, group) for the combination of all active groups."""
    best = 1e9
    bestg = -1
    for g in range(MAX_GROUPS):
        if active[g] == 0:
            continue
        dg = 1e9
        for i in range(gstart[g], gend[g]):
            k = P[i, P_K]
            bound = math.sqrt(_dot2(px - P[i, 34], py - P[i, 35], pz - P[i, 36])) - P[i, 37]
            if bound > dg + k:
                continue
            di = prim_dist(i, px, py, pz, P, T)
            dg = _smin(dg, di, k)
        for j in range(GCn[g]):
            dg = max(dg, GC[g, j, 0] * px + GC[g, j, 1] * py + GC[g, j, 2] * pz - GC[g, j, 3])
        if dg < best:
            best = dg
            bestg = g
    return best, bestg


@njit(cache=True, fastmath=True)
def scene_d(px, py, pz, P, T, gstart, gend, GC, GCn, active):
    d, g = scene(px, py, pz, P, T, gstart, gend, GC, GCn, active)
    return d


@njit(cache=True, fastmath=True)
def nearest_prim(px, py, pz, g, P, T, gstart, gend):
    best = 1e9
    bi = gstart[g]
    for i in range(gstart[g], gend[g]):
        d = prim_dist(i, px, py, pz, P, T)
        if d < best:
            best = d
            bi = i
    return bi


@njit(cache=True, fastmath=True)
def soft_shadow(ox, oy, oz, lx, ly, lz, P, T, gstart, gend, GC, GCn, active, mint, maxt, k):
    res = 1.0
    t = mint
    for _ in range(48):
        h = scene_d(ox + lx * t, oy + ly * t, oz + lz * t, P, T, gstart, gend, GC, GCn, active)
        res = min(res, k * h / t)
        if res < 0.002:
            return 0.0
        t += min(max(h, 0.006), 0.08)
        if t > maxt:
            break
    return min(max(res, 0.0), 1.0)


@njit(cache=True, fastmath=True)
def _ray_aabb(ox, oy, oz, dx, dy, dz, bmin, bmax):
    t0 = -1e9
    t1 = 1e9
    o = (ox, oy, oz)
    d = (dx, dy, dz)
    for a in range(3):
        if abs(d[a]) < 1e-12:
            if o[a] < bmin[a] or o[a] > bmax[a]:
                return 1.0, -1.0
        else:
            ta = (bmin[a] - o[a]) / d[a]
            tb = (bmax[a] - o[a]) / d[a]
            if ta > tb:
                ta, tb = tb, ta
            t0 = max(t0, ta)
            t1 = min(t1, tb)
    return t0, t1


@njit(cache=True, fastmath=True, parallel=True)
def render(P, T, gstart, gend, GC, GCn, active, target_group,
           W, H, ppt, ax, ay, cam, light, bmin, bmax,
           mat_albedo, mat_tint, mat_spec, mat_emit, belly_mat,
           out_color, out_mask):
    """Ray-march one image.

    cam = [Rx,Ry,Rz, Ux,Uy,Uz, Dx,Dy,Dz, stretch]
    out_color: premultiplied RGBA of shaded albedo.
    out_mask:  premultiplied greyscale lighting * tint weight (for runtime tint).
    Only pixels whose first hit belongs to ``target_group`` are written
    (target_group < 0 means any group).
    """
    Rx, Ry, Rz = cam[0], cam[1], cam[2]
    Ux, Uy, Uz = cam[3], cam[4], cam[5]
    Dx, Dy, Dz = cam[6], cam[7], cam[8]
    stretch = cam[9]
    Lx, Ly, Lz = light[0], light[1], light[2]
    for iy in prange(H):
        for ix in range(W):
            sx = (ix + 0.5 - ax) / ppt
            sy = (ay - (iy + 0.5)) / ppt / stretch
            ox = sx * Rx + sy * Ux - 6.0 * Dx
            oy = sx * Ry + sy * Uy - 6.0 * Dy
            oz = sx * Rz + sy * Uz - 6.0 * Dz
            t0, t1 = _ray_aabb(ox, oy, oz, Dx, Dy, Dz, bmin, bmax)
            if t0 > t1:
                continue
            t = max(t0, 0.0)
            hit = False
            g = -1
            for _ in range(160):
                px = ox + Dx * t
                py = oy + Dy * t
                pz = oz + Dz * t
                d, g = scene(px, py, pz, P, T, gstart, gend, GC, GCn, active)
                if d < 0.0012:
                    hit = True
                    break
                t += d * 0.9
                if t > t1:
                    break
            if not hit:
                continue
            if target_group >= 0 and g != target_group:
                continue
            px = ox + Dx * t
            py = oy + Dy * t
            pz = oz + Dz * t
            # normal (tetrahedron technique)
            e = 0.0015
            d1 = scene_d(px + e, py - e, pz - e, P, T, gstart, gend, GC, GCn, active)
            d2 = scene_d(px - e, py - e, pz + e, P, T, gstart, gend, GC, GCn, active)
            d3 = scene_d(px - e, py + e, pz - e, P, T, gstart, gend, GC, GCn, active)
            d4 = scene_d(px + e, py + e, pz + e, P, T, gstart, gend, GC, GCn, active)
            nx = d1 - d2 - d3 + d4
            ny = -d1 - d2 + d3 + d4
            nz = -d1 + d2 - d3 + d4
            nl = math.sqrt(nx * nx + ny * ny + nz * nz)
            if nl < 1e-12:
                nx, ny, nz = -Dx, -Dy, -Dz
            else:
                nx /= nl
                ny /= nl
                nz /= nl
            # material
            pi = nearest_prim(px, py, pz, g, P, T, gstart, gend)
            m = T[pi, I_MAT]
            ar = mat_albedo[m, 0]
            ag = mat_albedo[m, 1]
            ab = mat_albedo[m, 2]
            tint = mat_tint[m]
            spec_k = mat_spec[m]
            emit = mat_emit[m]
            bthr = P[pi, P_BELLY_T]
            bdot = nx * P[pi, P_BELLY] + ny * P[pi, P_BELLY + 1] + nz * P[pi, P_BELLY + 2]
            if bthr > 0.0 and bdot > bthr - 0.15:
                w = min(max((bdot - (bthr - 0.15)) / 0.3, 0.0), 1.0)
                w = w * w * (3.0 - 2.0 * w)
                bm = belly_mat
                ar = ar * (1.0 - w) + mat_albedo[bm, 0] * w
                ag = ag * (1.0 - w) + mat_albedo[bm, 1] * w
                ab = ab * (1.0 - w) + mat_albedo[bm, 2] * w
                tint = tint * (1.0 - w) + mat_tint[bm] * w
                spec_k = spec_k * (1.0 - w) + mat_spec[bm] * w
            # ambient occlusion
            occ = 0.0
            sca = 1.0
            for j in range(5):
                hh = 0.012 + 0.03 * j
                dd = scene_d(px + nx * hh, py + ny * hh, pz + nz * hh,
                             P, T, gstart, gend, GC, GCn, active)
                occ += (hh - dd) * sca
                sca *= 0.75
            ao = min(max(1.0 - 2.2 * occ, 0.0), 1.0)
            # lighting
            ndl = nx * Lx + ny * Ly + nz * Lz
            diff = max(ndl, 0.0)
            sh = 1.0
            if diff > 0.0:
                sh = soft_shadow(px + nx * 0.004, py + ny * 0.004, pz + nz * 0.004, Lx, Ly, Lz,
                                 P, T, gstart, gend, GC, GCn, active, 0.01, 1.5, 10.0)
            sky = 0.55 + 0.45 * nz
            # fill light from the camera side
            fill = max(-(nx * Dx + ny * Dy + nz * Dz), 0.0)
            vdn = max(-(nx * Dx + ny * Dy + nz * Dz), 0.0)
            rim = (1.0 - vdn) ** 3
            light_v = 0.30 * ao * sky + 0.78 * diff * sh + 0.18 * fill * ao + 0.10 * rim * ao
            # specular (Blinn)
            hx = Lx - Dx
            hy = Ly - Dy
            hz = Lz - Dz
            hl = math.sqrt(hx * hx + hy * hy + hz * hz)
            spec = 0.0
            if hl > 0.0 and spec_k > 0.0:
                ndh = max((nx * hx + ny * hy + nz * hz) / hl, 0.0)
                spec = spec_k * (ndh ** 24) * sh
            cr = ar * (light_v * (1.0 - emit) + emit) + spec
            cg = ag * (light_v * (1.0 - emit) + emit) + spec
            cb = ab * (light_v * (1.0 - emit) + emit) + spec
            # The runtime-tinted mask is drawn over this layer.  Vanilla player
            # colours have alpha 0.5, so the engine only covers (1 - 0.5 * tint)
            # of what is underneath; pre-darken accordingly so fully tinted areas
            # come out as pure (mask * player colour).
            keep = (1.0 - tint) / (1.0 - 0.5 * tint)
            out_color[iy, ix, 0] = min(cr, 1.0) * keep
            out_color[iy, ix, 1] = min(cg, 1.0) * keep
            out_color[iy, ix, 2] = min(cb, 1.0) * keep
            out_color[iy, ix, 3] = 1.0
            grey = min(light_v * 1.08 + spec * 0.6, 1.0)
            out_mask[iy, ix, 0] = grey * tint
            out_mask[iy, ix, 1] = grey * tint
            out_mask[iy, ix, 2] = grey * tint
            out_mask[iy, ix, 3] = tint


@njit(cache=True, fastmath=True, parallel=True)
def render_ground_shadow(P, T, gstart, gend, GC, GCn, active,
                         W, H, ppt, ax, ay, cam, shadow_light, out_alpha):
    """Shadow cast on the ground plane z=0 (alpha only)."""
    Rx, Ry, Rz = cam[0], cam[1], cam[2]
    Ux, Uy, Uz = cam[3], cam[4], cam[5]
    Dx, Dy, Dz = cam[6], cam[7], cam[8]
    stretch = cam[9]
    Lx, Ly, Lz = shadow_light[0], shadow_light[1], shadow_light[2]
    for iy in prange(H):
        for ix in range(W):
            sx = (ix + 0.5 - ax) / ppt
            sy = (ay - (iy + 0.5)) / ppt / stretch
            ox = sx * Rx + sy * Ux - 6.0 * Dx
            oy = sx * Ry + sy * Uy - 6.0 * Dy
            oz = sx * Rz + sy * Uz - 6.0 * Dz
            if abs(Dz) < 1e-9:
                continue
            t = -oz / Dz
            gx = ox + Dx * t
            gy = oy + Dy * t
            s = soft_shadow(gx, gy, 0.0, Lx, Ly, Lz, P, T, gstart, gend, GC, GCn, active,
                            0.005, 4.0, 14.0)
            out_alpha[iy, ix] = 1.0 - s

"""Procedural anthro dragon: skeleton posing and primitive construction.

Model space: x = right, y = forward, z = up.  Units are Factorio tiles.
The feet stand on z = 0 at the origin (which becomes the entity position).
"""

import math
from dataclasses import dataclass, field

import numpy as np

from .scene import (BELLY, BODY, CLAW, EYE, GUN_LIGHT, GUN_METAL, HOODIE, HOODIE_DARK,
                    HOODIE_MAT, HORN, MEMBRANE, PUPIL, SCALE, SHORTS, SHORTS_BAND,
                    SHORTS_MAT, SPINE, STEEL, STRING, WOOD, Scene)

X = np.array([1.0, 0.0, 0.0])
Y = np.array([0.0, 1.0, 0.0])
Z = np.array([0.0, 0.0, 1.0])


def V(x, y, z):
    return np.array([x, y, z], dtype=float)


def norm(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def lerp(a, b, t):
    return a + (b - a) * t


def rot(v, axis, ang):
    """Rodrigues rotation of vector v about unit axis."""
    axis = norm(axis)
    c = math.cos(ang)
    s = math.sin(ang)
    return v * c + np.cross(axis, v) * s + axis * np.dot(axis, v) * (1 - c)


def rot_about(p, pivot, axis, ang):
    return pivot + rot(p - pivot, axis, ang)


def two_bone(root, target, l1, l2, pole):
    d = target - root
    dist = np.linalg.norm(d)
    dirn = d / max(dist, 1e-6)
    dist = min(max(dist, abs(l1 - l2) + 1e-3), l1 + l2 - 1e-3)
    cos_a = (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)
    a = math.acos(min(max(cos_a, -1.0), 1.0))
    pp = pole - np.dot(pole, dirn) * dirn
    if np.linalg.norm(pp) < 1e-6:
        pp = np.cross(dirn, X)
    pp = norm(pp)
    joint = root + dirn * math.cos(a) * l1 + pp * math.sin(a) * l1
    end = root + dirn * dist
    return joint, end


def smoothstep(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


@dataclass
class BodyShape:
    name: str
    shoulder_w: float
    chest_r: tuple
    waist_r: tuple
    hip_r: tuple
    hip_w: float
    arm_r: float
    forearm_r: float
    thigh_r: float
    neck_r: float
    bust: bool
    horn_style: str
    eye_r: float


MALE = BodyShape(
    name="male", shoulder_w=0.212, chest_r=(0.198, 0.148, 0.160), waist_r=(0.148, 0.128, 0.11),
    hip_r=(0.165, 0.130, 0.12), hip_w=0.100, arm_r=0.064, forearm_r=0.056, thigh_r=0.100,
    neck_r=0.078, bust=False, horn_style="male", eye_r=0.024)

FEMALE = BodyShape(
    name="female", shoulder_w=0.165, chest_r=(0.152, 0.128, 0.148), waist_r=(0.100, 0.096, 0.105),
    hip_r=(0.198, 0.148, 0.128), hip_w=0.112, arm_r=0.052, forearm_r=0.046, thigh_r=0.102,
    neck_r=0.064, bust=True, horn_style="female", eye_r=0.027)

HEAD_SCALE = 1.28

BODIES = {"male": MALE, "female": FEMALE}


@dataclass
class Pose:
    pelvis: np.ndarray = None
    tr: np.ndarray = None   # torso right
    tf: np.ndarray = None   # torso forward
    tu: np.ndarray = None   # torso up
    hip_r: np.ndarray = None  # pelvis frame (for hips/legs)
    hip_f: np.ndarray = None
    hip_u: np.ndarray = None
    waist: np.ndarray = None
    chest: np.ndarray = None
    neck_base: np.ndarray = None
    neck_top: np.ndarray = None
    head: np.ndarray = None
    hr: np.ndarray = None
    hf: np.ndarray = None
    hu: np.ndarray = None
    breath: float = 0.0
    arms: dict = field(default_factory=dict)   # side -> (shoulder, elbow, hand, hand_dir)
    legs: dict = field(default_factory=dict)   # side -> (hip, knee, ankle, ball, toe)
    tail: list = field(default_factory=list)   # list of (point, radius)
    wings: dict = field(default_factory=dict)  # side -> dict of points
    prop: tuple = None
    jaw_open: float = 0.0


# --------------------------------------------------------------------------
# Wing key poses (torso-local coordinates relative to the chest point, right wing)
# --------------------------------------------------------------------------
WING_FOLDED = {
    "W0": V(0.085, -0.105, 0.075),
    "W1": V(0.235, -0.235, 0.420),
    "W2": V(0.265, -0.275, 0.010),
    "F1": V(0.285, -0.300, -0.480),
    "F2": V(0.215, -0.270, -0.470),
    "F3": V(0.140, -0.220, -0.410),
    "B": V(0.075, -0.105, -0.230),
}

WING_SPREAD = {
    "W0": V(0.085, -0.105, 0.075),
    "W1": V(0.400, -0.150, 0.230),
    "W2": V(0.740, -0.130, 0.270),
    "F1": V(1.150, -0.220, 0.070),
    "F2": V(1.020, -0.380, -0.170),
    "F3": V(0.720, -0.420, -0.320),
    "B": V(0.095, -0.125, -0.260),
}


def wing_points(spread, flap, fold_in=0.0):
    """Right wing points in torso-local space.

    spread: 0 folded .. 1 fully spread; flap: radians (positive = up);
    fold_in: 0..1 partially folds the hand during the upstroke.
    """
    pts = {k: lerp(WING_FOLDED[k], WING_SPREAD[k], spread) for k in WING_FOLDED}
    if fold_in > 0:
        w2 = pts["W2"]
        for k in ("F1", "F2", "F3"):
            pts[k] = lerp(pts[k], w2 + (pts[k] - w2) * 0.55 + V(0, -0.10, -0.05), fold_in)
    if abs(flap) > 1e-6:
        piv = pts["W0"]
        for k in ("W1", "W2", "F1", "F2", "F3"):
            pts[k] = rot_about(pts[k], piv, Y, -flap * (0.6 + 0.4 * spread))
    return pts


# --------------------------------------------------------------------------
# Pose construction
# --------------------------------------------------------------------------

def torso_frame(lean, twist=0.0, roll=0.0):
    tu = rot(Z, X, lean)
    tf = rot(Y, X, lean)
    tr = X.copy()
    if abs(twist) > 1e-9:
        tf = rot(tf, tu, twist)
        tr = rot(tr, tu, twist)
    if abs(roll) > 1e-9:
        tu = rot(tu, tf, roll)
        tr = rot(tr, tf, roll)
    return tr, tf, tu


def build_pose(body, pelvis, lean=0.0, twist=0.0, roll=0.0, hip_twist=0.0, breath=0.0,
               head_pitch=0.0, head_yaw=0.0, head_roll=0.0):
    p = Pose()
    p.pelvis = pelvis
    p.breath = breath
    p.tr, p.tf, p.tu = torso_frame(lean, twist, roll)
    p.hip_r, p.hip_f, p.hip_u = torso_frame(lean * 0.5, hip_twist, roll * 0.5)
    p.waist = pelvis + p.tu * 0.165 + p.tf * 0.008
    p.chest = pelvis + p.tu * 0.345 + p.tf * 0.012 + p.tu * breath * 0.006
    p.neck_base = pelvis + p.tu * 0.475 + p.tf * 0.03
    # the neck curves forward; the head stays closer to level than the torso
    neck_dir = norm(p.tu * 0.85 + p.tf * 0.5)
    p.neck_top = p.neck_base + neck_dir * 0.13
    hr, hf, hu = torso_frame(lean * 0.35 + head_pitch, twist * 0.5 + head_yaw, head_roll)
    p.hr, p.hf, p.hu = hr, hf, hu
    p.head = p.neck_top + hu * 0.075 + hf * 0.035
    return p


def shoulder(p, body, side):
    return p.chest + p.tr * (side * body.shoulder_w) + p.tu * (0.105 + p.breath * 0.004) - p.tf * 0.005


def set_arm(p, body, side, hand_target, pole, hand_dir=None):
    s = shoulder(p, body, side)
    elbow, hand = two_bone(s, hand_target, 0.235, 0.215, pole)
    if hand_dir is None:
        hand_dir = norm(hand - elbow)
    p.arms[side] = (s, elbow, hand, hand_dir)


def hip_joint(p, body, side):
    return p.pelvis + p.hip_r * (side * body.hip_w) - p.hip_u * 0.035


def set_leg(p, body, side, ball, heel_lift=0.13, heel_back=0.10, toe_pitch=0.0, pole=None):
    hip = hip_joint(p, body, side)
    fwd = norm(V(p.hip_f[0], p.hip_f[1], 0.0))
    ankle_target = ball - fwd * heel_back + Z * heel_lift
    if pole is None:
        pole = fwd + p.hip_r * side * 0.25
    knee, ankle = two_bone(hip, ankle_target, 0.300, 0.300, pole)
    toe_dir = norm(rot(fwd, np.cross(fwd, Z) * -1.0, toe_pitch))
    toe = ball + toe_dir * 0.085 - Z * 0.012
    p.legs[side] = (hip, knee, ankle, ball, toe)


def set_tail(p, lift=0.0, sway=0.0, sway_phase=0.0, curl=0.0, n=10, side_bias=0.0):
    base = p.pelvis - p.hip_f * 0.10 - p.hip_u * 0.02
    back = norm(V(-p.hip_f[0], -p.hip_f[1], 0.0))
    side = np.cross(Z, back)  # perpendicular, horizontal
    pts = []
    base_z = base[2]
    length = 0.80
    for i in range(n + 1):
        s = i / n
        d = length * s
        # resting tail drags on the ground; lifted tail stays more horizontal
        z_rest = base_z * (1 - s) ** 1.7 + 0.065 * s
        z_lift = base_z - 0.30 * s + 0.12 * s * s
        z = lerp(z_rest, z_lift, lift)
        z += curl * 0.18 * smoothstep(0.65, 1.0, s)
        lateral = (sway * math.sin(sway_phase - s * 2.2) + side_bias) * s ** 1.3
        pt = V(base[0], base[1], 0.0) + back * d + side * lateral
        pt[2] = max(z, 0.035 + 0.02 * (1 - s))
        r = 0.125 * (1 - s) ** 0.85 + 0.018 * s
        pts.append((pt, r))
    p.tail = pts


def set_wings(p, spread, flap, fold_in=0.0, jitter=0.0):
    for side in (1, -1):
        loc = wing_points(spread, flap, fold_in)
        world = {}
        for k, v in loc.items():
            vv = V(v[0] * side, v[1], v[2])
            if jitter and k in ("W1", "W2", "F1", "F2", "F3"):
                vv = vv + V(side * jitter * 0.3, 0, jitter)
            world[k] = p.chest + p.tr * vv[0] + p.tf * vv[1] + p.tu * vv[2]
        p.wings[side] = world


# --------------------------------------------------------------------------
# Props
# --------------------------------------------------------------------------

def hold_gun(p, body, aim_pitch=0.0):
    aim = norm(V(p.tf[0], p.tf[1], 0.0))
    aim = rot(aim, np.cross(aim, Z), -aim_pitch) if aim_pitch else aim
    side = norm(np.cross(aim, Z))
    stock = p.chest + side * 0.055 + aim * 0.075 - Z * 0.075
    muzzle = stock + aim * 0.52
    grip_r = stock + aim * 0.13 - Z * 0.035
    grip_l = stock + aim * 0.31 - Z * 0.02
    set_arm(p, body, 1, grip_r, -p.tf * 0.4 + p.tr * 0.6 - Z * 0.8, aim)
    set_arm(p, body, -1, grip_l, -p.tf * 0.2 - p.tr * 0.7 - Z * 0.8, aim)
    p.prop = ("gun", stock, muzzle, aim)


PICK_LENGTH = 0.70  # handle length from the upper hand to the head


def hold_pick(p, body, alpha):
    """alpha: handle angle in radians measured from forward-horizontal, up positive."""
    fwd = norm(V(p.tf[0], p.tf[1], 0.0))
    d = fwd * math.cos(alpha) + Z * math.sin(alpha)
    e = -fwd * math.sin(alpha) + Z * math.cos(alpha)
    pivot = p.chest + p.tu * 0.09 + fwd * 0.03
    grip = pivot + d * 0.33
    side = norm(np.cross(fwd, Z))
    set_arm(p, body, 1, grip + side * 0.035, -fwd * 0.3 + side * 0.8 - Z * 0.5, d)
    set_arm(p, body, -1, grip - side * 0.035 - d * 0.07, -fwd * 0.3 - side * 0.8 - Z * 0.5, d)
    base = grip - d * 0.10
    # long enough that the blade reaches the ground in front of the feet at the blow
    top = grip + d * PICK_LENGTH
    p.prop = ("pick", base, top, d, e)


# --------------------------------------------------------------------------
# Building primitives
# --------------------------------------------------------------------------

def _ell_axes(r, f, u):
    return (r, f, u)


def add_head(sc, p, body):
    H, hr, hf, hu = p.head, p.hr, p.hf, p.hu
    k_ = HEAD_SCALE

    def L(x, y, z):
        return H + (hr * x + hf * y + hu * z) * k_

    sc.ellipsoid(L(0, 0, 0), (0.100 * k_, 0.112 * k_, 0.094 * k_), (hr, hf, hu), SCALE, k=0.0)
    # snout and jaw
    sc.cone(L(0, 0.06, -0.012), L(0, 0.205, -0.035), 0.074 * k_, 0.052 * k_, SCALE, k=0.05)
    sc.cone(L(0, 0.04, -0.062), L(0, 0.185, -0.075 - p.jaw_open * 0.04), 0.064 * k_, 0.036 * k_, SCALE,
            k=0.04, belly=-hu + hf * 0.2, belly_t=0.55)
    # cheeks / jaw corners
    for s in (1, -1):
        sc.sphere(L(s * 0.062, 0.02, -0.045), 0.045 * k_, SCALE, k=0.05)
        # brow ridge
        sc.ellipsoid(L(s * 0.052, 0.072, 0.038), (0.036 * k_, 0.032 * k_, 0.020 * k_), (hr, hf, hu), SCALE, k=0.03)
        # nostril bump
        sc.sphere(L(s * 0.022, 0.195, -0.012), 0.016 * k_, SCALE, k=0.02)
    # eyes (hard edged so they stay readable)
    for s in (1, -1):
        e = L(s * 0.058, 0.086, 0.010)
        er = body.eye_r * k_
        sc.sphere(e, er, EYE)
        sc.sphere(e + norm(hf * 0.85 + hr * s * 0.5) * er * 0.72, er * 0.45, PUPIL)
    # horns
    if body.horn_style == "male":
        for s in (1, -1):
            a = L(s * 0.055, 0.005, 0.058)
            b = L(s * 0.088, -0.095, 0.092)
            c = L(s * 0.104, -0.185, 0.098)
            d = L(s * 0.108, -0.245, 0.128)
            sc.cone(a, b, 0.034 * k_, 0.024 * k_, HORN, k=0.015)
            sc.cone(b, c, 0.024 * k_, 0.014 * k_, HORN, k=0.008)
            sc.cone(c, d, 0.014 * k_, 0.004 * k_, HORN, k=0.004)
            # cheek spikes
            sc.cone(L(s * 0.085, -0.010, -0.040), L(s * 0.145, -0.090, -0.035), 0.018 * k_, 0.004 * k_,
                    SPINE, k=0.01)
    else:
        for s in (1, -1):
            a = L(s * 0.046, 0.012, 0.062)
            b = L(s * 0.066, -0.100, 0.092)
            c = L(s * 0.070, -0.205, 0.078)
            d = L(s * 0.058, -0.275, 0.115)
            sc.cone(a, b, 0.026 * k_, 0.017 * k_, HORN, k=0.012)
            sc.cone(b, c, 0.017 * k_, 0.010 * k_, HORN, k=0.006)
            sc.cone(c, d, 0.010 * k_, 0.003 * k_, HORN, k=0.003)
        # soft head crest
        for i, (y, z, h) in enumerate(((-0.02, 0.092, 0.045), (-0.075, 0.080, 0.050),
                                       (-0.125, 0.055, 0.042))):
            base = L(0, y, z)
            tip = base + norm(hu * 0.6 - hf * 0.8) * h * k_
            sc.cone(base, tip, 0.016 * k_, 0.003, SPINE, k=0.01)
    # spines down the back of the head / neck
    for t in (0.15, 0.55, 0.9):
        q = lerp(p.neck_top, p.neck_base, t)
        back = -norm(p.tf * 0.6 + p.hf * 0.4)
        base = q + back * (body.neck_r * 0.85) + p.tu * 0.01
        tip = base + norm(back * 0.8 + p.tu * 0.6) * 0.055
        sc.cone(base, tip, 0.018, 0.003, SPINE, k=0.01)


def add_torso(sc, p, body, group=BODY, inflate=0.0, mat=SCALE, hem=None, neck_clip=None):
    tr, tf, tu = p.tr, p.tf, p.tu
    br = 1.0 + 0.03 * p.breath
    cr = body.chest_r
    belly = None if group != BODY else tf
    clips_hem = [hem] if hem is not None else None
    sc.ellipsoid(p.chest, (cr[0] * br + inflate, cr[1] * br + inflate, cr[2] + inflate),
                 (tr, tf, tu), mat, group=group, k=0.0, belly=belly, belly_t=0.42)
    wr = body.waist_r
    sc.ellipsoid(p.waist, (wr[0] + inflate, wr[1] + inflate, wr[2] + inflate), (tr, tf, tu), mat,
                 group=group, k=0.10, belly=belly, belly_t=0.42, clips=clips_hem)
    hr_ = body.hip_r
    sc.ellipsoid(p.pelvis, (hr_[0] + inflate, hr_[1] + inflate, hr_[2] + inflate),
                 (p.hip_r, p.hip_f, p.hip_u), mat, group=group, k=0.09, belly=belly, belly_t=0.5,
                 clips=clips_hem)
    if body.bust:
        for s in (1, -1):
            c = p.chest + tr * (s * 0.058) + tf * (cr[1] * 0.62) - tu * 0.022
            sc.sphere(c, 0.056 + inflate, mat, group=group, k=0.05 + inflate * 1.5,
                      belly=belly, belly_t=0.42)
    # neck
    nclips = [neck_clip] if neck_clip is not None else None
    sc.cone(p.neck_base - tu * 0.04, p.neck_top, body.neck_r * 1.25 + inflate,
            body.neck_r + inflate, mat, group=group, k=0.07, belly=belly, belly_t=0.5,
            clips=nclips)


def add_arm(sc, p, body, side, group=BODY, inflate=0.0, mat=SCALE, cuff=None, sleeve=False):
    s, e, h, hd = p.arms[side]
    sc.sphere(s, body.arm_r * 1.28 + inflate, mat, group=group, k=0.06)
    sc.cone(s, e, body.arm_r + inflate, body.arm_r * 0.86 + inflate, mat, group=group, k=0.035)
    if sleeve:
        # sleeve ends a bit before the wrist
        cuff_pt = lerp(e, h, 0.72)
        sc.cone(e, cuff_pt, body.forearm_r + inflate, body.forearm_r * 0.92 + inflate, mat,
                group=group, k=0.03)
        fd = norm(h - e)
        rr = body.forearm_r * 0.95 + inflate * 1.1
        sc.cone(cuff_pt - fd * 0.06, cuff_pt + fd * 0.03, rr, rr, HOODIE_DARK, group=group, k=0.0,
                clips=[(cuff_pt, fd), (cuff_pt - fd * 0.032, -fd)])
        return
    sc.cone(e, h, body.forearm_r, body.forearm_r * 0.80, mat, group=group, k=0.035)
    # hand + claws
    sc.sphere(h + hd * 0.02, 0.047, SCALE, group=group, k=0.03)
    perp = norm(np.cross(hd, Z)) if abs(np.dot(hd, Z)) < 0.95 else X
    for j, off in enumerate((-0.022, 0.0, 0.022)):
        base = h + hd * 0.045 + perp * off
        tip = base + hd * 0.045 - Z * 0.012
        sc.cone(base, tip, 0.012, 0.004, CLAW, group=group, k=0.004)


def add_leg(sc, p, body, side):
    hip, knee, ankle, ball, toe = p.legs[side]
    sc.cone(hip, knee, body.thigh_r, body.thigh_r * 0.72, SCALE, k=0.06)
    sc.cone(knee, ankle, body.thigh_r * 0.62, 0.046, SCALE, k=0.035)
    sc.cone(ankle, ball, 0.046, 0.040, SCALE, k=0.03)
    fwd = norm(toe - ball)
    perp = norm(np.cross(fwd, Z))
    for off in (-0.028, 0.0, 0.028):
        b = ball + perp * off
        t = toe + perp * off * 1.3
        sc.cone(b, t, 0.029, 0.018, SCALE, k=0.02)
        sc.cone(t - fwd * 0.005, t + fwd * 0.03 - Z * 0.012, 0.012, 0.004, CLAW, k=0.004)


def add_tail(sc, p):
    pts = p.tail
    for i in range(len(pts) - 1):
        (a, ra), (b, rb) = pts[i], pts[i + 1]
        sc.cone(a, b, ra, rb, SCALE, k=0.05 if i == 0 else 0.035, belly=-Z, belly_t=0.62)
    # spines along the top of the tail
    for i in range(2, len(pts) - 1, 2):
        (a, ra), (b, _) = pts[i], pts[i + 1]
        d = norm(b - a)
        up = norm(Z - d * np.dot(Z, d))
        base = a + up * ra * 0.8
        tip = base + norm(up * 0.7 - d * 0.7) * (0.03 + ra * 0.25)
        sc.cone(base, tip, 0.012 + ra * 0.12, 0.003, SPINE, k=0.008)
    # spade on the tip
    (a, _), (b, rb) = pts[-2], pts[-1]
    d = norm(b - a)
    up = norm(Z - d * np.dot(Z, d))
    side = norm(np.cross(d, up))
    sc.ellipsoid(b + d * 0.03, (0.05, 0.055, 0.012), (side, d, up), SCALE, k=0.02)


def add_wing(sc, p, side):
    w = p.wings[side]
    W0, W1, W2, F1, F2, F3, B = (w[k] for k in ("W0", "W1", "W2", "F1", "F2", "F3", "B"))
    sc.cone(W0, W1, 0.034, 0.025, SCALE, k=0.03)
    sc.cone(W1, W2, 0.025, 0.018, SCALE, k=0.012)
    sc.cone(W2, W2 + norm(W2 - W1) * 0.05 + V(0, 0, 0.02), 0.014, 0.003, CLAW, k=0.0)
    for f in (F1, F2, F3):
        sc.cone(W2, f, 0.013, 0.004, SCALE, k=0.008)
    th = 0.0055

    def mid(a, b, pull):
        return lerp(lerp(a, b, 0.5), W2, pull)

    m12 = mid(F1, F2, 0.20)
    m23 = mid(F2, F3, 0.20)
    m3b = mid(F3, B, 0.16)
    tris = [(W0, W1, W2), (W2, F1, m12), (W2, m12, F2), (W2, F2, m23), (W2, m23, F3),
            (W2, F3, m3b), (W2, m3b, B), (W0, W2, B)]
    for a, b, c in tris:
        sc.triangle(a, b, c, th, MEMBRANE, k=0.004)


def add_prop(sc, p):
    if p.prop is None:
        return
    if p.prop[0] == "gun":
        _, stock, muzzle, aim = p.prop
        down = norm(np.cross(np.cross(aim, Z), aim))
        down = -down if np.dot(down, Z) > 0 else down
        sc.cone(stock - aim * 0.06, stock + aim * 0.05, 0.030, 0.028, GUN_LIGHT, k=0.0)
        sc.cone(stock + aim * 0.05, stock + aim * 0.36, 0.034, 0.028, GUN_METAL, k=0.01)
        sc.cone(stock + aim * 0.36, muzzle, 0.014, 0.012, GUN_LIGHT, k=0.0)
        sc.cone(stock + aim * 0.22, stock + aim * 0.20 + down * 0.10, 0.018, 0.016, GUN_METAL,
                k=0.0)
        sc.cone(stock + aim * 0.12, stock + aim * 0.10 + down * 0.07, 0.015, 0.014, GUN_METAL,
                k=0.0)
    else:
        _, base, top, d, e = p.prop
        sc.cone(base, top, 0.017, 0.015, WOOD, k=0.0)
        head = top - d * 0.02
        sc.cone(head + e * 0.11, head, 0.020, 0.030, STEEL, k=0.01)
        sc.cone(head, head - e * 0.18 - d * 0.035, 0.030, 0.006, STEEL, k=0.01)


def add_hoodie(sc, p, body):
    th = 0.028
    hem_pt = p.pelvis + p.tu * 0.02
    hem = (hem_pt, -p.tu)
    neck_pt = p.neck_base + p.tu * 0.035
    neck_clip = (neck_pt, p.tu)
    add_torso(sc, p, body, group=HOODIE, inflate=th, mat=HOODIE_MAT, hem=hem, neck_clip=neck_clip)
    for side in (1, -1):
        add_arm(sc, p, body, side, group=HOODIE, inflate=th * 0.85, mat=HOODIE_MAT, sleeve=True)
    tr, tf, tu = p.tr, p.tf, p.tu
    # hem band
    hr_ = body.hip_r
    sc.ellipsoid(hem_pt + tu * 0.025, (hr_[0] + th + 0.008, hr_[1] + th + 0.006, 0.030),
                 (p.hip_r, p.hip_f, p.hip_u), HOODIE_DARK, group=HOODIE, k=0.0,
                 clips=[(hem_pt, -tu)])
    # hood resting on the back of the neck, and collar
    sc.ellipsoid(p.neck_base - tf * 0.085 + tu * 0.005, (0.125, 0.075, 0.065), (tr, tf, tu),
                 HOODIE_MAT, group=HOODIE, k=0.05)
    sc.ellipsoid(p.neck_base - tf * 0.12 - tu * 0.035, (0.09, 0.05, 0.075), (tr, tf, tu),
                 HOODIE_DARK, group=HOODIE, k=0.04)
    # front pocket
    cr = body.chest_r
    wr = body.waist_r
    sc.ellipsoid(p.waist + tf * (wr[1] + th + 0.004) - tu * 0.03, (0.085, 0.018, 0.048),
                 (tr, tf, tu), HOODIE_DARK, group=HOODIE, k=0.012)
    # drawstrings
    for s in (1, -1):
        top = p.neck_base + tr * (s * 0.035) + tf * (cr[1] * 0.75 + th) - tu * 0.01
        bot = top - tu * 0.13 + tf * 0.03
        sc.cone(top, bot, 0.008, 0.007, STRING, group=HOODIE, k=0.0)
        sc.sphere(bot, 0.011, STRING, group=HOODIE)


def add_shorts(sc, p, body):
    th = 0.024
    waist_pt = p.pelvis + p.hip_u * 0.075
    waist_clip = (waist_pt, p.hip_u)
    hr_ = body.hip_r
    sc.ellipsoid(p.pelvis, (hr_[0] + th, hr_[1] + th, hr_[2] + th),
                 (p.hip_r, p.hip_f, p.hip_u), SHORTS_MAT, group=SHORTS, k=0.0, clips=[waist_clip])
    for side in (1, -1):
        hip, knee, _, _, _ = p.legs[side]
        d = norm(knee - hip)
        cut = lerp(hip, knee, 0.58)
        sc.cone(hip, knee, body.thigh_r + th, body.thigh_r * 0.72 + th, SHORTS_MAT, group=SHORTS,
                k=0.06, clips=[(cut, d), waist_clip])
        # hem cuff (clipped so the round-cone end caps do not bulge out)
        cr_ = body.thigh_r * 0.84 + th * 1.15
        sc.cone(cut - d * 0.06, cut + d * 0.02, cr_, cr_, SHORTS_BAND, group=SHORTS, k=0.0,
                clips=[(cut, d), (cut - d * 0.028, -d)])
    # waistband
    sc.ellipsoid(waist_pt - p.hip_u * 0.012, (hr_[0] + th + 0.006, hr_[1] + th + 0.004, 0.022),
                 (p.hip_r, p.hip_f, p.hip_u), SHORTS_BAND, group=SHORTS, k=0.0,
                 clips=[(waist_pt + p.hip_u * 0.004, p.hip_u)])


def build_scene(p, body, xf, xd, hoodie=True, shorts=True):
    sc = Scene(xf, xd)
    add_torso(sc, p, body)
    add_head(sc, p, body)
    for side in (1, -1):
        add_arm(sc, p, body, side)
        add_leg(sc, p, body, side)
        add_wing(sc, p, side)
    add_tail(sc, p)
    add_prop(sc, p)
    if hoodie:
        add_hoodie(sc, p, body)
    if shorts:
        add_shorts(sc, p, body)
    return sc

"""Animation definitions: map (animation, body, phase) to a Pose."""

import math

import numpy as np

from .model import (V, Y, Z, build_pose, hip_joint, hold_gun, hold_pick, lerp, norm, rot_about,
                    set_arm, set_leg, set_tail, set_wings, shoulder, smoothstep)

TAU = 2.0 * math.pi

# Elevation of the body while flying (mech armor flight in Space Age)
FLY_HEIGHT = 0.42


def _relaxed_arms(p, body, swing=0.0, spread=0.0):
    for side in (1, -1):
        s = shoulder(p, body, side)
        target = (s + p.tr * side * (0.055 + spread) + p.tf * (0.07 + swing * side)
                  - p.tu * 0.40)
        set_arm(p, body, side, target, -p.tf + p.tr * side * 0.35)


def _standing_legs(p, body, stance=0.0, front=0.0):
    for side in (1, -1):
        ball = V(side * (body.hip_w + 0.018 + stance), 0.07 + front * side, 0.035)
        set_leg(p, body, side, ball)


def idle(body, t, gun=False):
    b = math.sin(TAU * t)
    pel = V(0, -0.005, 0.70 + 0.005 * b)
    p = build_pose(body, pel, lean=0.05, breath=b, head_pitch=-0.03 + 0.025 * math.sin(TAU * t + 1.2),
                   head_yaw=0.05 * math.sin(TAU * t))
    if gun:
        hold_gun(p, body)
    else:
        _relaxed_arms(p, body, swing=0.0)
    _standing_legs(p, body)
    set_tail(p, lift=0.0, sway=0.055, sway_phase=TAU * t)
    set_wings(p, spread=0.05 + 0.025 * (b * 0.5 + 0.5), flap=0.0)
    return p


def _turn(v, move):
    """Turns a model-space vector clockwise (seen from above) by ``move`` radians."""
    return rot_about(v, V(0, 0, 0), Z, -move) if move else v


def run(body, t, gun=False, move=0.0):
    """``move``: running direction relative to the facing (gun) direction, clockwise radians.
    The hips, legs and tail run that way while the torso, head and gun keep facing forward."""
    ph = TAU * t
    pel = V(0, 0.0, 0.675 - 0.028 * math.cos(2 * ph))
    p = build_pose(body, pel, lean=0.27 if not gun else 0.18, twist=0.10 * math.sin(ph),
                   hip_twist=-0.12 * math.sin(ph) - move, head_pitch=-0.12 if not gun else -0.06)
    for side, phi in ((-1, t), (1, t + 0.5)):
        phi = phi % 1.0
        # as long a stride as the legs reach: the planted foot slides less
        y = 0.05 + 0.30 * math.cos(TAU * phi)
        swing = math.sin(TAU * (phi - 0.5)) if phi > 0.5 else 0.0
        z = 0.035 + 0.16 * swing
        ball = _turn(V(side * (body.hip_w + 0.012), y, z), move)
        set_leg(p, body, side, ball, heel_lift=0.13 + 0.05 * swing, toe_pitch=0.5 * swing)
    if gun:
        hold_gun(p, body)
    else:
        for side, phi in ((-1, t), (1, t + 0.5)):
            a = math.cos(TAU * (phi + 0.5))
            s = shoulder(p, body, side)
            target = s + p.tr * side * 0.07 + p.tf * (0.10 + 0.20 * a) - p.tu * (0.30 - 0.06 * a)
            set_arm(p, body, side, target, -p.tf * 1.0 + p.tr * side * 0.3 - p.tu * 0.2)
    set_tail(p, lift=0.8, sway=0.075, sway_phase=ph + 1.0)
    set_wings(p, spread=0.03, flap=0.0, jitter=0.012 * math.sin(2 * ph))
    return p


MINE_ALPHA = [-30, 5, 45, 90, 125, 148, 152, 75, -58, -62, -57, -44]
# torso lean per frame: positive leans back (wind-up), negative leans forward into the blow
MINE_LEAN = [-0.08, 0.04, 0.08, 0.03, -0.05, -0.09, -0.09, -0.05, -0.30, -0.34, -0.30, -0.18]
MINE_FRAMES = len(MINE_ALPHA)
# 1-based frame index in which the pick hits the ground
MINE_IMPACT_FRAME = 9


def mine(body, t):
    i = int(round(t * MINE_FRAMES)) % MINE_FRAMES
    alpha = math.radians(MINE_ALPHA[i])
    lean = MINE_LEAN[i]
    raise_f = smoothstep(40, 150, MINE_ALPHA[i])
    # crouch into the blow so the pick reaches the ground in front of the feet
    crouch = smoothstep(0.1, 0.3, -lean)
    pel = V(0, -0.02 + 0.04 * crouch, 0.665 - 0.10 * crouch + 0.03 * raise_f)
    p = build_pose(body, pel, lean=lean, head_pitch=-0.15 + 0.25 * (lean - 0.1))
    hold_pick(p, body, alpha)
    for side in (1, -1):
        ball = V(side * (body.hip_w + 0.055), 0.15 if side < 0 else -0.05, 0.035)
        set_leg(p, body, side, ball)
    set_tail(p, lift=0.25 + 0.3 * raise_f, sway=0.03, sway_phase=TAU * t)
    set_wings(p, spread=0.06 + 0.16 * raise_f, flap=0.15 * raise_f)
    return p


def _dangling_legs(p, body, back=0.0, move=0.0):
    for side in (1, -1):
        hip = hip_joint(p, body, side)
        ball = hip + _turn(V(side * 0.025, 0.05 - back, -0.52 + back * 0.25), move)
        set_leg(p, body, side, ball, heel_lift=0.16, heel_back=0.03 + back * 0.1,
                toe_pitch=0.9 + back)


def hover(body, t, gun=False, elevation=FLY_HEIGHT, phase_shift=0.0):
    ph = TAU * t + phase_shift
    flap = 0.68 * math.cos(ph)
    fold = 0.35 * max(0.0, -math.sin(ph))
    pel = V(0, 0, 0.70 + elevation + 0.035 * math.sin(ph))
    p = build_pose(body, pel, lean=0.10, head_pitch=-0.05)
    if gun:
        hold_gun(p, body)
    else:
        _relaxed_arms(p, body, spread=0.03)
    _dangling_legs(p, body)
    set_tail(p, lift=1.0, sway=0.06, sway_phase=ph)
    set_wings(p, spread=1.0, flap=flap, fold_in=fold)
    return p


def fly(body, t, gun=False, move=0.0):
    """``move``: as in run(); the hips, legs and tail point along the flight direction."""
    ph = TAU * t
    flap = 0.75 * math.cos(ph)
    fold = 0.4 * max(0.0, -math.sin(ph))
    pel = V(0, 0, 0.70 + FLY_HEIGHT + 0.03 * math.sin(ph))
    p = build_pose(body, pel, lean=0.55, hip_twist=-move, head_pitch=-0.35)
    if gun:
        hold_gun(p, body)
    else:
        _relaxed_arms(p, body, swing=0.0, spread=0.02)
    _dangling_legs(p, body, back=0.28, move=move)
    set_tail(p, lift=1.0, sway=0.05, sway_phase=ph)
    set_wings(p, spread=1.0, flap=flap, fold_in=fold)
    return p


def take_off(body, t):
    """t in [0, 1]: standing -> first frame of the hover cycle."""
    crouch = smoothstep(0.0, 0.28, t) * (1 - smoothstep(0.28, 0.55, t))
    lift = smoothstep(0.3, 1.0, t)
    # The wings rise before they open: spread wings show from above (in the shadow) long before
    # they show from the side, so opening them first made the shadow run ahead of the body.
    spread = smoothstep(0.2, 0.6, t)
    # wings go up while opening, beat down once, and come back up for the hover cycle
    if t < 0.5:
        flap = 0.7 * smoothstep(0.0, 0.35, t)
    elif t < 0.8:
        flap = lerp(0.7, -0.55, smoothstep(0.5, 0.8, t))
    else:
        flap = lerp(-0.55, 0.68, smoothstep(0.8, 1.0, t))
    pel = V(0, 0, 0.70 - 0.11 * crouch + FLY_HEIGHT * lift)
    p = build_pose(body, pel, lean=0.05 + 0.25 * crouch + 0.05 * lift, head_pitch=-0.05)
    _relaxed_arms(p, body, spread=0.03 * spread)
    if lift < 0.05:
        _standing_legs(p, body)
    else:
        for side in (1, -1):
            hip = hip_joint(p, body, side)
            ground_ball = V(side * (body.hip_w + 0.018), 0.07, 0.035)
            dangle = hip + V(side * 0.025, 0.05, -0.52)
            ball = lerp(ground_ball, dangle, smoothstep(0.05, 0.7, lift))
            set_leg(p, body, side, ball, heel_lift=0.13 + 0.03 * lift, heel_back=0.10 - 0.07 * lift,
                    toe_pitch=0.9 * lift)
    set_tail(p, lift=lift, sway=0.04, sway_phase=TAU * t)
    set_wings(p, spread=0.05 + 0.95 * spread, flap=flap)
    return p


def corpse_pose(body):
    pel = V(0, 0, 0.66)
    p = build_pose(body, pel, lean=0.15, head_pitch=0.25, head_yaw=0.3)
    for side in (1, -1):
        s = shoulder(p, body, side)
        set_arm(p, body, side, s + p.tf * 0.25 - p.tu * 0.18 + p.tr * side * 0.05,
                -p.tf + p.tr * side * 0.2 - p.tu)
    for side in (1, -1):
        hip = hip_joint(p, body, side)
        ball = hip + V(side * 0.02, 0.18 + 0.06 * side, -0.38)
        set_leg(p, body, side, ball, heel_lift=0.10, heel_back=0.12)
    set_tail(p, lift=0.6, sway=0.0, side_bias=0.35, curl=0.0)
    set_wings(p, spread=0.45, flap=0.35)
    return p


# Direction layout Factorio expects for 18-direction running_with_gun / flying_with_gun
# (taken from the vanilla engineer's sheet). Each row is (gun direction, moving direction)
# in eighths of a turn clockwise from north. Only gun directions north..south (the east
# half) exist: for a gun pointing west the engine mirrors the sprite horizontally, and for
# the remaining moving directions it plays a row backwards (running backwards).
GUN_MOVE_DIRECTIONS = [
    (0, 0), (0, 1), (0, 2),
    (1, 0), (1, 1), (1, 2), (1, 3),
    (2, 0), (2, 1), (2, 2), (2, 3),
    (3, 1), (3, 2), (3, 3), (3, 4),
    (4, 2), (4, 3), (4, 4),
]
GUN_MOVE_ANIMATIONS = ("run-gun", "fly-gun")


def direction(anim, d):
    """Sheet row d of an animation -> (facing orientation, extra pose arguments)."""
    if anim in GUN_MOVE_ANIMATIONS:
        gun, move = GUN_MOVE_DIRECTIONS[d]
        return gun / 8.0, {"move": (move - gun) * TAU / 8.0}
    return d / ANIMATIONS[anim][0], {}


# name -> (direction_count, frame_count, pose function(body, t, **direction args), looping)
# Looping animations sample t in [0, 1); one-shot animations sample t in [0, 1].
ANIMATIONS = {
    "idle": (8, 8, lambda b, t: idle(b, t, False), True),
    "idle-gun": (8, 8, lambda b, t: idle(b, t, True), True),
    "run": (8, 12, lambda b, t: run(b, t, False), True),
    "run-gun": (18, 12, lambda b, t, move=0.0: run(b, t, True, move), True),
    "mine": (8, MINE_FRAMES, mine, True),
    "hover": (8, 8, lambda b, t: hover(b, t, False), True),
    "hover-gun": (8, 8, lambda b, t: hover(b, t, True), True),
    "fly": (8, 8, lambda b, t: fly(b, t, False), True),
    "fly-gun": (18, 8, lambda b, t, move=0.0: fly(b, t, True, move), True),
    "take-off": (8, 8, take_off, False),
}


def frame_phase(anim, frame):
    _, frames, _, loop = ANIMATIONS[anim]
    return frame / frames if loop else frame / (frames - 1)

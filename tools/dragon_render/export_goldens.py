"""Writes reference poses of the dragon for the exporter's C# port of model.py / anims.py.

Usage (from the repository root):

    python3 -m tools.dragon_render.export_goldens            # rewrite the golden file
    python3 -m tools.dragon_render.export_goldens --check    # exit 1 if it is out of date

Output: unity/dotnet/Core.Tests/Goldens/dragon_poses.json, read by the Core tests in
unity/dotnet/Core.Tests/Dragon/. For both bodies it holds the pose of every frame of every
animation in anims.ANIMATIONS plus the corpse, once for each distinct ``move`` value of the
animation's sheet rows (anims.direction). Rows that only differ in their facing give the same
pose, so they are not repeated.

Each case is one line: body, animation, frame, t (anims.frame_phase), move, and the pose with
every model.Pose field the port covers (the tail and wings are left out). Vectors are [x, y, z]
and floats are written with repr precision, so the C# side can compare to 1e-9. Arms and legs
are keyed by side like Pose.arms / Pose.legs ("1" = right, "-1" = left); the prop is null,
{"kind": "gun", "stock", "muzzle", "aim"} or {"kind": "pick", "base", "top", "d", "e"}.
The output only depends on the code, so running it twice gives the same file. --check compares
numbers to CHECK_TOLERANCE rather than text, because math libraries may differ in the last digit.
"""

import argparse
import json
import os
import sys

from . import anims, model

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_JSON = os.path.join(REPO, "unity", "dotnet", "Core.Tests", "Goldens", "dragon_poses.json")
FORMAT_VERSION = 1
CHECK_TOLERANCE = 1e-12

VECTOR_FIELDS = ("pelvis", "tr", "tf", "tu", "hip_r", "hip_f", "hip_u", "waist", "chest",
                 "neck_base", "neck_top", "head", "hr", "hf", "hu")
SCALAR_FIELDS = ("breath", "jaw_open")
ARM_POINTS = ("shoulder", "elbow", "hand", "hand_dir")
LEG_POINTS = ("hip", "knee", "ankle", "ball", "toe")
PROP_POINTS = {"gun": ("stock", "muzzle", "aim"), "pick": ("base", "top", "d", "e")}
BODY_FIELDS = ("shoulder_w", "chest_r", "waist_r", "hip_r", "hip_w", "arm_r", "forearm_r",
               "thigh_r", "neck_r", "bust", "horn_style", "eye_r")
CORPSE = "corpse"


def vec(v):
    return [float(v[0]), float(v[1]), float(v[2])]


def limbs(by_side, names):
    return {str(side): dict(zip(names, (vec(p) for p in by_side[side]))) for side in (1, -1)}


def prop(p):
    if p is None:
        return None
    kind = p[0]
    out = {"kind": kind}
    out.update(zip(PROP_POINTS[kind], (vec(v) for v in p[1:])))
    return out


def pose_dict(p):
    out = {k: vec(getattr(p, k)) for k in VECTOR_FIELDS}
    out.update({k: float(getattr(p, k)) for k in SCALAR_FIELDS})
    out["arms"] = limbs(p.arms, ARM_POINTS)
    out["legs"] = limbs(p.legs, LEG_POINTS)
    out["prop"] = prop(p.prop)
    return out


def body_dict(body):
    out = {}
    for k in BODY_FIELDS:
        v = getattr(body, k)
        out[k] = [float(x) for x in v] if isinstance(v, tuple) else v
    return out


def distinct_moves(anim):
    """The move values of an animation's rows, each once, in row order."""
    dirs = anims.ANIMATIONS[anim][0]
    moves = []
    for d in range(dirs):
        move = anims.direction(anim, d)[1].get("move")
        if move not in moves:
            moves.append(move)
    return moves


def cases(body_name):
    body = model.BODIES[body_name]
    for anim, (_, frames, fn, _) in anims.ANIMATIONS.items():
        for move in distinct_moves(anim):
            kw = {} if move is None else {"move": move}
            for frame in range(frames):
                t = anims.frame_phase(anim, frame)
                yield {"body": body_name, "animation": anim, "frame": frame, "t": float(t),
                       "move": float(move or 0.0), "pose": pose_dict(fn(body, t, **kw))}
    yield {"body": body_name, "animation": CORPSE, "frame": 0, "t": 0.0, "move": 0.0,
           "pose": pose_dict(anims.corpse_pose(body))}


def dumps(value):
    return json.dumps(value, separators=(",", ":"), allow_nan=False)


def golden_text():
    """The golden file's text: a small header, then one case per line."""
    header = {
        "format_version": FORMAT_VERSION,
        "source": "tools/dragon_render/export_goldens.py",
        "head_scale": model.HEAD_SCALE,
        "mine_alpha": anims.MINE_ALPHA,
        "mine_lean": anims.MINE_LEAN,
        "bodies": {name: body_dict(body) for name, body in model.BODIES.items()},
    }
    lines = ["{"]
    lines.extend(f"{dumps(k)}:{dumps(v)}," for k, v in header.items())
    lines.append('"cases":[')
    rows = [dumps(c) for name in model.BODIES for c in cases(name)]
    lines.append(",\n".join(rows))
    lines.append("]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def same(a, b):
    """True when two parsed golden files hold the same data, numbers within CHECK_TOLERANCE."""
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, float) and isinstance(b, float):
        return abs(a - b) <= CHECK_TOLERANCE
    return type(a) is type(b) and a == b


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true",
                        help="do not write; exit 1 if the golden file differs from the current code")
    args = parser.parse_args(argv)
    text = golden_text()
    if args.check:
        try:
            with open(OUT_JSON, encoding="utf-8") as fh:
                current = json.load(fh)
        except (FileNotFoundError, ValueError):
            current = None
        if not same(current, json.loads(text)):
            print(f"{os.path.relpath(OUT_JSON, REPO)} is out of date; "
                  "run python3 -m tools.dragon_render.export_goldens", file=sys.stderr)
            return 1
        print(f"{os.path.relpath(OUT_JSON, REPO)} is up to date")
        return 0
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with open(OUT_JSON, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(f"wrote {os.path.relpath(OUT_JSON, REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

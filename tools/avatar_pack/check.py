"""Checks an avatar pack (a folder or a mod zip) against docs/avatar-pack-format.md: the same
rules dragon-character enforces when the game loads, plus the sheet sizes and other things only
the graphics files can tell. Used by `tools/validate_sprites.py --pack`.
"""

import io
import json
import math
import os
import re
import zipfile

from PIL import Image

import lua_table
from avatar_pack import spec

VRAM_WARNING_MB = 256


class PackFiles:
    """Files of a pack, from a folder or from a zip holding one top-level folder."""

    def __init__(self, path):
        self.path = path
        self.zip = None
        if os.path.isdir(path):
            self.root_name = os.path.basename(os.path.normpath(path))
            self.names = set()
            for root, _, files in os.walk(path):
                for name in files:
                    self.names.add(os.path.relpath(os.path.join(root, name), path).replace(os.sep, "/"))
        else:
            self.zip = zipfile.ZipFile(path)
            tops = {name.split("/", 1)[0] for name in self.zip.namelist()}
            if len(tops) != 1:
                raise ValueError("the zip must hold exactly one folder")
            self.root_name = tops.pop()
            prefix = self.root_name + "/"
            self.names = {n[len(prefix):] for n in self.zip.namelist() if n.startswith(prefix) and not n.endswith("/")}

    def exists(self, name):
        return name in self.names

    def read(self, name):
        if self.zip:
            return self.zip.read(self.root_name + "/" + name)
        with open(os.path.join(self.path, name), "rb") as fh:
            return fh.read()

    def image(self, name):
        return Image.open(io.BytesIO(self.read(name)))


class Checker:
    def __init__(self, files):
        self.files = files
        self.errors = []
        self.warnings = []
        self.sheets = {}  # file name -> (width, height)
        self.mod_name = None

    def error(self, message):
        self.errors.append(message)

    def warn(self, message):
        self.warnings.append(message)

    # -- files around the description -------------------------------------------------------

    def check_info(self):
        if not self.files.exists("info.json"):
            self.error("info.json is missing")
            return None
        try:
            info = json.loads(self.files.read("info.json").decode("utf-8"))
        except ValueError as e:
            self.error(f"info.json is not valid JSON: {e}")
            return None
        name = info.get("name")
        if not isinstance(name, str) or not name.startswith(spec.MOD_PREFIX):
            self.error(f'info.json: name must be "{spec.MOD_PREFIX}<id>", not {name!r}')
            return None
        title = info.get("title")
        if not isinstance(title, str) or not title:
            self.error("info.json: title is missing")
        elif len(title.encode("utf-8")) > spec.INFO_TITLE_MAX_BYTES:
            self.error(f"info.json: title is {len(title.encode('utf-8'))} bytes; Factorio crashes on start when it"
                       f" is longer than {spec.INFO_TITLE_MAX_BYTES} bytes")
        version = info.get("version")
        if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+", version):
            self.error(f"info.json: version must look like 1.0.0, not {version!r}")
        if info.get("factorio_version") != "2.0":
            self.error('info.json: factorio_version must be "2.0"')
        if self.files.root_name not in (name, f"{name}_{version}"):
            self.warn(f'the pack folder is "{self.files.root_name}"; Factorio expects "{name}_{version}"'
                      f' or "{name}"')
        dependency_ok = False
        for dep in info.get("dependencies") or []:
            m = re.fullmatch(r"\s*dragon-character\s*>=\s*(\d+)\.(\d+)\.(\d+)\s*", dep) if isinstance(dep, str) else None
            if m and tuple(int(g) for g in m.groups()) >= spec.MIN_MAIN_VERSION:
                dependency_ok = True
        if not dependency_ok:
            self.error('info.json: dependencies must include "dragon-character >= 0.2.0"')
        return name

    def check_data_lua(self):
        if not self.files.exists("data.lua"):
            self.error("data.lua is missing")
            return
        text = self.files.read("data.lua").decode("utf-8", "replace")
        if text.split() != spec.DATA_LUA.split():
            self.error("data.lua is not the standard one from docs/avatar-pack-format.md")

    def check_extras(self):
        if not self.files.exists("thumbnail.png"):
            self.warn("thumbnail.png is missing (shown in the mod list)")
        else:
            size = self.files.image("thumbnail.png").size
            if size != (144, 144):
                self.warn(f"thumbnail.png is {size[0]}x{size[1]}, Factorio expects 144x144")
        if not self.files.exists("locale/en/locale.cfg"):
            self.warn("locale/en/locale.cfg is missing")
        if not self.files.exists("README.txt"):
            self.warn("README.txt is missing (source avatar, its author and permission to use it)")

    # -- the description -------------------------------------------------------------------

    def file(self, path, what):
        """Checks that path points into the pack and returns the file name inside it, or None."""
        prefix = f"__{self.mod_name}__/"
        if not isinstance(path, str) or not path.startswith(prefix) or ".." in path:
            self.error(f"{what} must be a file inside the pack ({prefix}...)")
            return None
        name = path[len(prefix):]
        if not self.files.exists(name):
            self.error(f"{what}: {name} does not exist")
            return None
        return name

    def check_sheet(self, name, where, expected, allowed_heights):
        img = self.files.image(name)
        w, h = img.size
        self.sheets[name] = (w, h)
        if img.mode != "RGBA":
            self.error(f"{where}: {name} is {img.mode}, not RGBA")
        if w > spec.MAX_SHEET_SIZE or h > spec.MAX_SHEET_SIZE:
            self.error(f"{where}: {name} is {w}x{h}, larger than {spec.MAX_SHEET_SIZE} px")
        if w != expected[0] or h not in allowed_heights:
            self.error(f"{where}: {name} is {w}x{h}, expected {expected[0]}x{expected[1]}")

    def check_layer(self, anim, key, meta, info):
        where = f'animation "{anim}" layer "{key}"'
        if not isinstance(info, dict):
            self.error(f"{where} is not a table")
            return
        limit = spec.MAX_SHEET_SIZE
        w, h = info.get("width"), info.get("height")
        if not is_int(w, 1, limit) or not is_int(h, 1, limit):
            self.error(f"{where} needs an integer width and height between 1 and {limit}")
            return
        frames, dirs = meta["frame_count"], meta["direction_count"]
        if frames * w > limit:
            self.error(f"{where} is wider than {limit} px (frame_count x width)")
        shift = info.get("shift")
        if not (isinstance(shift, list) and len(shift) == 2 and all(is_number(v) for v in shift)):
            self.error(f"{where} needs a shift of two numbers")
        if "filenames" in info:
            paths = info["filenames"]
            lines = info.get("lines_per_file")
            if not isinstance(paths, list) or not paths:
                self.error(f"{where} has an empty filenames list")
                return
            if not is_int(lines, 1) or lines * h > limit:
                self.error(f"{where} needs lines_per_file between 1 and floor({limit} / height)")
                return
            needed = math.ceil(dirs / lines)
            if len(paths) != needed:
                self.error(f"{where} has {len(paths)} files but needs {needed} for {dirs} directions"
                           f" at {lines} per file")
                return
            for k, path in enumerate(paths):
                name = self.file(path, f"{where} file {k + 1}")
                if name:
                    rows = min(lines, dirs - k * lines)
                    # the last file may be cut short or padded to the full lines_per_file
                    self.check_sheet(name, where, (frames * w, rows * h), {rows * h, lines * h})
        else:
            name = self.file(info.get("filename"), f"{where} filename")
            if dirs * h > limit:
                self.error(f"{where} is taller than {limit} px; split it with filenames + lines_per_file")
            elif name:
                self.check_sheet(name, where, (frames * w, dirs * h), {dirs * h})

    def check_animation(self, name, meta, tintable):
        where = f'animation "{name}"'
        if not isinstance(meta, dict):
            self.error(f"{where} is not a table")
            return
        if meta.get("direction_count") != spec.DIRECTIONS[name]:
            self.error(f"{where} must have {spec.DIRECTIONS[name]} directions, not {meta.get('direction_count')}")
            return
        if not is_int(meta.get("frame_count"), 1, spec.MAX_FRAMES):
            self.error(f"{where} needs an integer frame_count between 1 and {spec.MAX_FRAMES}")
            return
        layers = meta.get("layers")
        if not isinstance(layers, dict):
            self.error(f"{where} has no layers")
            return
        required = ["base", "shadow"] + (["mask"] if tintable else [])
        for key in required:
            if key not in layers:
                self.error(f'{where} is missing its "{key}" layer')
            else:
                self.check_layer(name, key, meta, layers[key])
        optional = spec.OPTIONAL_LAYERS.get(name, ())
        for key in layers:
            if key in optional:
                self.check_layer(name, key, meta, layers[key])
            elif key not in required:
                self.warn(f'{where} layer "{key}" is not used' + (" (tintable is false)" if key == "mask" else ""))

    def check_timing(self, timing, animations):
        if timing is None:
            timing = {}
        if not isinstance(timing, dict):
            self.error("timing must be a table")
            return
        d = timing.get("distance_per_frame")
        if d is not None and (not is_number(d) or d <= 0 or d > 2):
            self.error("timing.distance_per_frame must be a number above 0 and at most 2")
        speeds = timing.get("animation_speed")
        if speeds is not None:
            if not isinstance(speeds, dict):
                self.error("timing.animation_speed must be a table")
            else:
                for name, speed in speeds.items():
                    if name not in spec.DIRECTIONS:
                        self.error(f'timing.animation_speed names an unknown animation "{name}"')
                    elif not is_number(speed) or speed <= 0:
                        self.error(f"timing.animation_speed.{name} must be a positive number")
        mine, run = animations.get("mine"), animations.get("run")
        if not all(isinstance(a, dict) and is_int(a.get("frame_count"), 1, spec.MAX_FRAMES) for a in (mine, run)):
            return  # already reported by check_animation
        mine_frames, run_frames = mine["frame_count"], run["frame_count"]
        defaults = spec.default_timing(run_frames, mine_frames)
        impact = timing.get("mine_impact_frame", defaults["mine_impact_frame"])
        if not is_int(impact, 1, mine_frames):
            self.error(f"timing.mine_impact_frame must be a frame of the mine animation (1..{mine_frames})")
        for field in ("left_foot_frames", "right_foot_frames"):
            frames = timing.get(field, defaults[field])
            if not isinstance(frames, list) or not frames:
                self.error(f"timing.{field} must be a non-empty list of frame numbers")
                continue
            for frame in frames:
                if not is_int(frame, 1, run_frames):
                    self.error(f"timing.{field} has frame {frame} outside 1..{run_frames}")

    def check_description(self, mod_name):
        self.mod_name = mod_name
        path = "prototypes/avatar-pack.lua"
        if not self.files.exists(path):
            self.error(f"{path} is missing")
            return
        try:
            d = lua_table.loads(self.files.read(path).decode("utf-8"))
        except (lua_table.LuaSyntaxError, UnicodeDecodeError) as e:
            self.error(f"{path} is not a plain Lua table: {e}")
            return
        if not isinstance(d, dict):
            self.error(f"{path} must return a table with named fields")
            return
        version = d.get("format_version")
        if not is_int(version, 1):
            self.error("has no valid format_version")
            return
        if version > spec.SUPPORTED_VERSION:
            self.error(f"uses avatar pack format {version}, but these tools only know up to {spec.SUPPORTED_VERSION}")
            return
        pack_id = d.get("id")
        if not spec.valid_id(pack_id):
            self.error(f'has an invalid id "{pack_id}" (lowercase letters, digits and -, at most '
                       f"{spec.MAX_ID_LENGTH} characters, not starting with corpse-)")
            return
        if spec.MOD_PREFIX + pack_id != mod_name:
            self.error(f'id "{pack_id}" does not match the mod name "{mod_name}"')
            return
        title = d.get("title")
        if not isinstance(title, str) or not 1 <= len(title) <= spec.MAX_TITLE_LENGTH:
            self.error(f"needs a title of 1 to {spec.MAX_TITLE_LENGTH} characters")
        if d.get("author") is not None and not isinstance(d.get("author"), str):
            self.error("has an author that is not a string")
        icon = d.get("icon")
        if not isinstance(icon, dict) or not is_int(icon.get("size"), 1, 512):
            self.error("needs an icon with a filename and a size")
        else:
            name = self.file(icon.get("filename"), "icon")
            if name:
                w, h = self.files.image(name).size
                if (w, h) != (icon["size"], icon["size"]):
                    self.error(f"icon {name} is {w}x{h}, but its size is {icon['size']}")
        ppt = d.get("pixels_per_tile", 64)
        if ppt not in (64, 32) or isinstance(ppt, bool):
            self.error(f"has pixels_per_tile {ppt}; only 64 and 32 are supported")
        for flag in ("tintable", "flight"):
            if d.get(flag) is not None and not isinstance(d.get(flag), bool):
                self.error(f"{flag} must be true or false")
        tintable = d.get("tintable") is True
        flight = d.get("flight") is True
        animations = d.get("animations")
        if not isinstance(animations, dict):
            self.error("has no animations")
            return
        for name in spec.REQUIRED:
            if name not in animations:
                self.error(f'is missing the required "{name}" animation')
            else:
                self.check_animation(name, animations[name], tintable)
        for name in spec.FLIGHT:
            if flight and name not in animations:
                self.error(f'has flight = true but no "{name}" animation')
            elif not flight and name in animations:
                self.error(f'has a "{name}" animation but flight is not true')
            elif flight:
                self.check_animation(name, animations[name], tintable)
        for name in animations:
            if name not in spec.DIRECTIONS:
                self.warn(f'animation "{name}" is not part of the format and is ignored')
        self.check_timing(d.get("timing"), animations)
        unused = sorted(n for n in self.files.names
                        if n.startswith("graphics/") and n.endswith(".png") and n not in self.sheets
                        and n != (icon or {}).get("filename", "").split("__/", 1)[-1])
        for name in unused:
            self.warn(f"{name} is not used")

    def run(self):
        mod_name = self.check_info()
        self.check_data_lua()
        self.check_extras()
        if mod_name:
            self.check_description(mod_name)

    def vram_mb(self):
        return sum(w * h * 4 for w, h in self.sheets.values()) / 2 ** 20


def is_int(value, low, high=None):
    return (isinstance(value, int) and not isinstance(value, bool) and value >= low
            and (high is None or value <= high))


def is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def check(path):
    """Returns the Checker after checking the pack at path (folder or zip)."""
    try:
        files = PackFiles(path)
    except (OSError, ValueError, zipfile.BadZipFile) as e:
        checker = Checker(None)
        checker.error(f"can not read the pack: {e}")
        return checker
    checker = Checker(files)
    checker.run()
    return checker


def report(path):
    """Prints the result of checking one pack. Returns True if it has no errors."""
    checker = check(path)
    label = os.path.basename(os.path.normpath(path))
    for message in checker.errors:
        print(f"{label}: error: {message}")
    for message in checker.warnings:
        print(f"{label}: warning: {message}")
    if checker.errors:
        return False
    mb = checker.vram_mb()
    print(f"OK: {label}: {len(checker.sheets)} sprite sheets, about {mb:.0f} MB of video memory")
    if mb > VRAM_WARNING_MB:
        print(f"{label}: warning: more than {VRAM_WARNING_MB} MB of video memory; consider 32 pixels per tile,"
              " fewer frames or leaving out flight")
    return True

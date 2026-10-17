"""Constants of the avatar pack format (docs/avatar-pack-format.md).

Keep in sync with dragon-character/scripts/pack-format.lua; tests/run_headless.sh --test-pack
checks that both accept the same packs and reject the same mistakes.
"""

import re

DATA_TYPE = "dragon-character.avatar-pack"
SUPPORTED_VERSION = 1
MOD_PREFIX = "dragon-avatar-"
ID_PATTERN = re.compile(r"[a-z0-9][a-z0-9-]*")
MAX_ID_LENGTH = 35
MAX_TITLE_LENGTH = 80
MAX_SHEET_SIZE = 4096
MAX_FRAMES = 256
MIN_MAIN_VERSION = (0, 2, 0)

DIRECTIONS = {
    "idle": 8, "idle-gun": 8, "run": 8, "run-gun": 18, "mine": 8, "corpse": 1,
    "hover": 8, "hover-gun": 8, "fly": 8, "fly-gun": 18, "take-off": 8,
}
REQUIRED = ("idle", "idle-gun", "run", "run-gun", "mine", "corpse")
FLIGHT = ("hover", "hover-gun", "fly", "fly-gun", "take-off")
OPTIONAL_LAYERS = {"run-gun": ("shadow-flipped",)}

DEFAULT_TIMING = {
    "distance_per_frame": 0.119,
    "mine_impact_frame": 9,
    "left_foot_frames": [1],
    "right_foot_frames": [7],
}

DATA_LUA = '''local pack = require("prototypes.avatar-pack")
data:extend({
  {
    type = "mod-data",
    name = "dragon-avatar-" .. pack.id,
    data_type = "dragon-character.avatar-pack",
    data = pack,
  },
})
'''


def valid_id(pack_id):
    return (isinstance(pack_id, str) and 1 <= len(pack_id) <= MAX_ID_LENGTH
            and ID_PATTERN.fullmatch(pack_id) is not None and not pack_id.startswith("corpse-"))

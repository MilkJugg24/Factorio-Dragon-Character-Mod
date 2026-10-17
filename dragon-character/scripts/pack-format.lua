-- Constants of the avatar pack format (docs/avatar-pack-format.md), shared by
-- the data stage and the control stage.

local format = {}

format.DATA_TYPE = "dragon-character.avatar-pack"
format.SUPPORTED_VERSION = 1

-- mod, mod-data and character prototype names are MOD_PREFIX .. id
format.MOD_PREFIX = "dragon-avatar-"
format.CORPSE_PREFIX = "dragon-avatar-corpse-"
format.SPRITE_PREFIX = "dragon-character-avatar-"

format.ID_PATTERN = "^[a-z0-9][a-z0-9%-]*$"
format.MAX_ID_LENGTH = 35
format.MAX_TITLE_LENGTH = 80
format.MAX_SHEET_SIZE = 4096
format.MAX_FRAMES = 256

-- animation name -> required direction count
format.DIRECTIONS = {
  ["idle"] = 8,
  ["idle-gun"] = 8,
  ["run"] = 8,
  ["run-gun"] = 18,
  ["mine"] = 8,
  ["corpse"] = 1,
  ["hover"] = 8,
  ["hover-gun"] = 8,
  ["fly"] = 8,
  ["fly-gun"] = 18,
  ["take-off"] = 8,
}

format.REQUIRED = { "idle", "idle-gun", "run", "run-gun", "mine", "corpse" }
format.FLIGHT = { "hover", "hover-gun", "fly", "fly-gun", "take-off" }

-- Layers an animation may have besides base/shadow/mask.
format.OPTIONAL_LAYERS = { ["run-gun"] = { "shadow-flipped" } }

-- Values used when a pack's `timing` leaves them out; they match the dragon.
format.DEFAULT_TIMING = {
  distance_per_frame = 0.119,
  mine_impact_frame = 9,
  left_foot_frames = { 1 },
  right_foot_frames = { 7 },
  animation_speed = {
    ["idle"] = 0.06,
    ["idle-gun"] = 0.06,
    ["run"] = 0.6,
    ["run-gun"] = 0.6,
    ["mine"] = 0.415,
    ["hover"] = 0.27,
    ["hover-gun"] = 0.27,
    ["fly"] = 0.3,
    ["fly-gun"] = 0.3,
    ["take-off"] = 0.3,
    ["corpse"] = 1,
  },
}

function format.character_name(id)
  return format.MOD_PREFIX .. id
end

function format.corpse_name(id)
  return format.CORPSE_PREFIX .. id
end

function format.sprite_name(id)
  return format.SPRITE_PREFIX .. id
end

-- The pack id of an avatar character/corpse prototype name, or nil.
function format.id_of_character(name)
  if type(name) ~= "string" or name:sub(1, #format.CORPSE_PREFIX) == format.CORPSE_PREFIX then
    return nil
  end
  if name:sub(1, #format.MOD_PREFIX) == format.MOD_PREFIX then
    return name:sub(#format.MOD_PREFIX + 1)
  end
  return nil
end

function format.id_of_corpse(name)
  if type(name) == "string" and name:sub(1, #format.CORPSE_PREFIX) == format.CORPSE_PREFIX then
    return name:sub(#format.CORPSE_PREFIX + 1)
  end
  return nil
end

function format.valid_id(id)
  return type(id) == "string" and #id >= 1 and #id <= format.MAX_ID_LENGTH
    and id:match(format.ID_PATTERN) ~= nil and id:sub(1, 7) ~= "corpse-"
end

return format

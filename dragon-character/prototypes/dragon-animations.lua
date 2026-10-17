-- Character animations of the built-in dragon, from the generated sprite metadata.

local sprite_data = require("prototypes.sprite-data")
local builder = require("prototypes.animation-builder")
local format = require("scripts.pack-format")

local animations = {}

local timing = format.DEFAULT_TIMING

-- 1-based frame of the mining animation in which the pick hits the ground.
animations.mine_impact_frame = timing.mine_impact_frame

-- Frames of the running cycle (12 frames = one stride) where each foot lands.
animations.left_foot_frames = timing.left_foot_frames
animations.right_foot_frames = timing.right_foot_frames

-- One running cycle covers two steps of the vanilla engineer's length.
animations.distance_per_frame = timing.distance_per_frame

-- Layers in draw order: body, tinted scales, shorts, hoodie, shadow.
local function look(body, outfit)
  local list = {
    { key = "base" },
    { key = "mask", extra = { apply_runtime_tint = true } },
  }
  if outfit.shorts then
    list[#list + 1] = { key = "shorts" }
  end
  if outfit.hoodie then
    list[#list + 1] = { key = "hoodie" }
  end
  list[#list + 1] = { key = "shadow", extra = { draw_as_shadow = true } }
  return {
    get_meta = function(name)
      return sprite_data[body] and sprite_data[body][name]
    end,
    layer_list = list,
    scale = 0.5,
    speeds = timing.animation_speed,
    mine_impact_frame = animations.mine_impact_frame,
    label = body,
  }
end

-- A single CharacterArmorAnimation used for every armor.
-- `flight` adds the in-air animations used by flight-capable armor (Space Age mech armor).
function animations.armor_animation(body, outfit, flight)
  return builder.armor_animation(look(body, outfit), flight)
end

function animations.corpse_picture(body, outfit)
  return builder.corpse_picture(look(body, outfit))
end

return animations

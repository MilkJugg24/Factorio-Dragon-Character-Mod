-- Turns the vanilla character into the default dragon and creates one
-- character (and corpse) prototype per body shape / outfit combination and per
-- installed avatar pack.
-- Runs in data-final-fixes so the copies inherit changes other mods made to
-- the vanilla character (inventory size, reach, ...).

local appearance = require("scripts.appearance")
local format = require("scripts.pack-format")
local builder = require("prototypes.animation-builder")
local animations = require("prototypes.dragon-animations")
local avatar_packs = require("prototypes.avatar-packs")

local MOD_PATH = "__dragon-character__"
local ICON = MOD_PATH .. "/graphics/icons/dragon-character.png"

local vanilla_character = data.raw["character"]["character"]
local vanilla_corpse = data.raw["character-corpse"]["character-corpse"]
if not vanilla_character then
  error("dragon-character: the vanilla \"character\" prototype is missing")
end

-- Flight animations are only needed (and their sprites only loaded) when some
-- armor can fly, e.g. Space Age's mech armor.
local flight = false
for _, armor in pairs(data.raw["armor"] or {}) do
  if armor.provides_flight then
    flight = true
    break
  end
end

-- Copies are made from untouched vanilla data before the vanilla prototypes are changed.
local character_template = table.deepcopy(vanilla_character)
local corpse_template = vanilla_corpse and table.deepcopy(vanilla_corpse)

-- look = { icon, icon_size, armor_animation, corpse_picture, timing, corpse_name }
local function apply_look(proto, look)
  proto.icon = look.icon
  proto.icon_size = look.icon_size
  proto.icons = nil
  proto.animations = { look.armor_animation }
  proto.mining_with_tool_particles_animation_positions = { look.timing.mine_impact_frame }
  proto.running_sound_animation_positions = look.timing.left_foot_frames
  proto.moving_sound_animation_positions = look.timing.right_foot_frames
  proto.left_footprint_frames = look.timing.left_foot_frames
  proto.right_footprint_frames = look.timing.right_foot_frames
  proto.distance_per_frame = look.timing.distance_per_frame
  if corpse_template then
    proto.character_corpse = look.corpse_name
  end
end

local function apply_corpse_look(proto, look)
  proto.icon = look.icon
  proto.icon_size = look.icon_size
  proto.icons = nil
  proto.picture = nil
  proto.pictures = { look.corpse_picture }
  proto.armor_picture_mapping = {}
end

local function new_character(name, look)
  local character = table.deepcopy(character_template)
  character.name = name
  character.localised_name = { "entity-name.character" }
  character.localised_description = vanilla_character.localised_description
  character.hidden_in_factoriopedia = true
  character.factoriopedia_alternative = "character"
  apply_look(character, look)
  return character
end

local function new_corpse(name, look)
  local corpse = table.deepcopy(corpse_template)
  corpse.name = name
  corpse.localised_name = { "entity-name.character-corpse" }
  corpse.hidden_in_factoriopedia = true
  corpse.factoriopedia_alternative = "character-corpse"
  apply_corpse_look(corpse, look)
  return corpse
end

local new_prototypes = {}

-- The dragon: body shapes x outfits.
for _, variant in ipairs(appearance.variants()) do
  local outfit = { hoodie = variant.hoodie, shorts = variant.shorts }
  local look = {
    icon = ICON,
    icon_size = 64,
    armor_animation = animations.armor_animation(variant.body, outfit, flight),
    corpse_picture = animations.corpse_picture(variant.body, outfit),
    timing = {
      mine_impact_frame = animations.mine_impact_frame,
      left_foot_frames = animations.left_foot_frames,
      right_foot_frames = animations.right_foot_frames,
      distance_per_frame = animations.distance_per_frame,
    },
    corpse_name = variant.corpse,
  }
  if variant.character == "character" then
    apply_look(vanilla_character, look)
    if vanilla_corpse then
      apply_corpse_look(vanilla_corpse, look)
    end
  else
    new_prototypes[#new_prototypes + 1] = new_character(variant.character, look)
    if corpse_template then
      new_prototypes[#new_prototypes + 1] = new_corpse(variant.corpse, look)
    end
  end
end

-- Avatar packs: one look each.
for _, pack in ipairs(avatar_packs.collect()) do
  local pack_look = avatar_packs.look(pack)
  local look = {
    icon = pack.icon.filename,
    icon_size = pack.icon.size,
    armor_animation = builder.armor_animation(pack_look, flight),
    corpse_picture = builder.corpse_picture(pack_look),
    timing = pack.timing,
    corpse_name = format.corpse_name(pack.id),
  }
  new_prototypes[#new_prototypes + 1] = new_character(format.character_name(pack.id), look)
  if corpse_template then
    new_prototypes[#new_prototypes + 1] = new_corpse(format.corpse_name(pack.id), look)
  end
  new_prototypes[#new_prototypes + 1] = {
    type = "sprite",
    name = format.sprite_name(pack.id),
    filename = pack.icon.filename,
    size = pack.icon.size,
    flags = { "gui-icon" },
  }
end

data:extend(new_prototypes)

-- Turns the vanilla character into the default dragon and creates one
-- character (and corpse) prototype per body shape / outfit combination.
-- Runs in data-final-fixes so the copies inherit changes other mods made to
-- the vanilla character (inventory size, reach, ...).

local appearance = require("scripts.appearance")
local animations = require("prototypes.dragon-animations")

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

local function dragonize_character(proto, variant)
  local outfit = { hoodie = variant.hoodie, shorts = variant.shorts }
  proto.icon = ICON
  proto.icon_size = 64
  proto.icons = nil
  proto.animations = { animations.armor_animation(variant.body, outfit, flight) }
  proto.mining_with_tool_particles_animation_positions = { animations.mine_impact_frame }
  proto.running_sound_animation_positions = animations.left_foot_frames
  proto.moving_sound_animation_positions = animations.right_foot_frames
  proto.left_footprint_frames = animations.left_foot_frames
  proto.right_footprint_frames = animations.right_foot_frames
  proto.distance_per_frame = animations.distance_per_frame
  if corpse_template then
    proto.character_corpse = variant.corpse
  end
end

local function dragonize_corpse(proto, variant)
  local outfit = { hoodie = variant.hoodie, shorts = variant.shorts }
  proto.icon = ICON
  proto.icon_size = 64
  proto.icons = nil
  proto.picture = nil
  proto.pictures = { animations.corpse_picture(variant.body, outfit) }
  proto.armor_picture_mapping = {}
end

local new_prototypes = {}

for _, variant in ipairs(appearance.variants()) do
  if variant.character == "character" then
    dragonize_character(vanilla_character, variant)
    if vanilla_corpse then
      dragonize_corpse(vanilla_corpse, variant)
    end
  else
    local character = table.deepcopy(character_template)
    character.name = variant.character
    character.localised_name = { "entity-name.character" }
    character.localised_description = vanilla_character.localised_description
    character.hidden_in_factoriopedia = true
    character.factoriopedia_alternative = "character"
    dragonize_character(character, variant)
    new_prototypes[#new_prototypes + 1] = character

    if corpse_template then
      local corpse = table.deepcopy(corpse_template)
      corpse.name = variant.corpse
      corpse.localised_name = { "entity-name.character-corpse" }
      corpse.hidden_in_factoriopedia = true
      corpse.factoriopedia_alternative = "character-corpse"
      dragonize_corpse(corpse, variant)
      new_prototypes[#new_prototypes + 1] = corpse
    end
  end
end

data:extend(new_prototypes)

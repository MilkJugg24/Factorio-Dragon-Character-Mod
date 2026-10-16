-- Shared between the data stage and the control stage: the list of dragon
-- variants and the mapping between an appearance and its prototype names.
--
-- Body shape and outfits are baked into separate character prototypes (the
-- graphics of a prototype can not change at runtime), while the scale color
-- comes from the player color through the runtime-tinted mask layer.

local appearance = {}

appearance.bodies = { "male", "female" }

appearance.outfits = {
  { key = "plain", hoodie = false, shorts = false },
  { key = "hoodie", hoodie = true, shorts = false },
  { key = "shorts", hoodie = false, shorts = true },
  { key = "hoodie-shorts", hoodie = true, shorts = true },
}

-- The vanilla "character" prototype is turned into this variant, so players
-- who never open the customization window keep the vanilla prototype.
appearance.default = { body = "male", hoodie = false, shorts = false }

-- Matches Factorio's default player color.
appearance.default_color = { r = 0.869, g = 0.5, b = 0.130, a = 0.5 }

local PREFIX = "dragon-character-"
local CORPSE_PREFIX = "dragon-character-corpse-"

function appearance.outfit_key(hoodie, shorts)
  if hoodie and shorts then
    return "hoodie-shorts"
  elseif hoodie then
    return "hoodie"
  elseif shorts then
    return "shorts"
  end
  return "plain"
end

function appearance.is_default(body, hoodie, shorts)
  local d = appearance.default
  return body == d.body and (hoodie or false) == d.hoodie and (shorts or false) == d.shorts
end

function appearance.character_name(body, hoodie, shorts)
  if appearance.is_default(body, hoodie, shorts) then
    return "character"
  end
  return PREFIX .. body .. "-" .. appearance.outfit_key(hoodie, shorts)
end

function appearance.corpse_name(body, hoodie, shorts)
  if appearance.is_default(body, hoodie, shorts) then
    return "character-corpse"
  end
  return CORPSE_PREFIX .. body .. "-" .. appearance.outfit_key(hoodie, shorts)
end

-- All (body, outfit) combinations, including the default one.
function appearance.variants()
  local list = {}
  for _, body in ipairs(appearance.bodies) do
    for _, outfit in ipairs(appearance.outfits) do
      list[#list + 1] = {
        body = body,
        hoodie = outfit.hoodie,
        shorts = outfit.shorts,
        character = appearance.character_name(body, outfit.hoodie, outfit.shorts),
        corpse = appearance.corpse_name(body, outfit.hoodie, outfit.shorts),
      }
    end
  end
  return list
end

local by_character = {}
local by_corpse = {}
for _, v in ipairs(appearance.variants()) do
  by_character[v.character] = v
  by_corpse[v.corpse] = v
end

-- Returns the variant for a character prototype name, or nil if the name does
-- not belong to this mod (e.g. a character from another mod).
function appearance.from_character_name(name)
  return by_character[name]
end

function appearance.is_dragon_corpse(name)
  return by_corpse[name] ~= nil
end

function appearance.is_mod_variant(name)
  return name ~= "character" and by_character[name] ~= nil
end

function appearance.is_mod_corpse(name)
  return name ~= "character-corpse" and by_corpse[name] ~= nil
end

return appearance

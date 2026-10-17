-- Turns characters and corpses back into vanilla prototypes, so that this mod
-- (or one avatar pack) can be removed without Factorio deleting them, and the
-- items they hold, when the save is loaded.

local appearance = require("scripts.appearance")
local format = require("scripts.pack-format")
local swap = require("scripts.swap")

local revert = {}

-- pack_id: only revert what uses that avatar pack; nil: revert everything to
-- the default dragon (the vanilla "character").
-- prefs_of(player_index) -> prefs; apply(player) applies a player's prefs;
-- pending is storage.pending (offline players are converted when they rejoin).
-- Returns { corpses = converted corpses, remaining = characters still using
-- a mod prototype (offline, dead or busy players) }.
function revert.run(pack_id, prefs_of, apply, pending)
  local function is_target_character(name)
    if pack_id then
      return format.id_of_character(name) == pack_id
    end
    return appearance.is_mod_variant(name)
  end
  local function is_target_corpse(name)
    if pack_id then
      return format.id_of_corpse(name) == pack_id
    end
    return appearance.is_mod_corpse(name)
  end

  local d = appearance.default
  for _, player in pairs(game.players) do
    local prefs = prefs_of(player.index)
    if not pack_id or prefs.avatar == pack_id then
      prefs.avatar = nil
      if not pack_id then
        prefs.body, prefs.hoodie, prefs.shorts = d.body, d.hoodie, d.shorts
      end
      if player.connected then
        apply(player)
      else
        pending[player.index] = true
      end
    end
  end

  local corpses = 0
  local remaining = 0
  for _, surface in pairs(game.surfaces) do
    for _, corpse in pairs(surface.find_entities_filtered({ type = "character-corpse" })) do
      if is_target_corpse(corpse.name) and swap.replace_corpse(corpse, "character-corpse") then
        corpses = corpses + 1
      end
    end
    for _, character in pairs(surface.find_entities_filtered({ type = "character" })) do
      if is_target_character(character.name) then
        remaining = remaining + 1
      end
    end
  end
  return { corpses = corpses, remaining = remaining }
end

return revert

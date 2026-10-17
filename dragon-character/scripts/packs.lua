-- Installed avatar packs at runtime, from their mod-data prototypes.
-- Built from prototypes only (never stored), so it is the same on every peer.

local format = require("scripts.pack-format")

local packs = {}

local cache = nil

local function build()
  local list = {}
  for name, mod_data in pairs(prototypes.mod_data) do
    if mod_data.data_type == format.DATA_TYPE then
      local d = mod_data.data
      local id = d and d.id
      if id and prototypes.entity[format.character_name(id)] then
        list[#list + 1] = {
          id = id,
          title = d.title or id,
          author = d.author,
          tintable = d.tintable == true,
          sprite = format.sprite_name(id),
          character = format.character_name(id),
          corpse = format.corpse_name(id),
        }
      end
    end
  end
  table.sort(list, function(a, b)
    local ta, tb = a.title:lower(), b.title:lower()
    if ta ~= tb then
      return ta < tb
    end
    return a.id < b.id
  end)
  local by_id = {}
  for _, pack in ipairs(list) do
    by_id[pack.id] = pack
  end
  return { list = list, by_id = by_id }
end

local function data()
  if not cache then
    cache = build()
  end
  return cache
end

-- Sorted by title: { id, title, author, tintable, sprite, character, corpse }
function packs.list()
  return data().list
end

function packs.get(id)
  if id == nil then
    return nil
  end
  return data().by_id[id]
end

return packs

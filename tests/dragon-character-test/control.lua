-- Headless test for dragon-character's character swap logic.
-- Run with tests/run_headless.sh; prints "DRAGON-TEST: PASS" or errors.

local swap = require("__dragon-character__/scripts/swap")
local appearance = require("__dragon-character__/scripts/appearance")

local failures = {}

local function check(condition, message)
  if not condition then
    failures[#failures + 1] = message
  end
end

local function count_items(entity)
  local counts = {}
  for index = 1, entity.get_max_inventory_index() do
    local inventory = entity.get_inventory(index)
    if inventory then
      for _, item in pairs(inventory.get_contents()) do
        counts[item.name] = (counts[item.name] or 0) + item.count
      end
    end
  end
  return counts
end

local function same_counts(a, b)
  for name, count in pairs(a) do
    if b[name] ~= count then
      return false, name .. ": " .. tostring(count) .. " vs " .. tostring(b[name])
    end
  end
  for name, count in pairs(b) do
    if a[name] ~= count then
      return false, name .. ": " .. tostring(a[name]) .. " vs " .. tostring(count)
    end
  end
  return true
end

local function test_prototypes()
  for _, variant in ipairs(appearance.variants()) do
    check(prototypes.entity[variant.character] ~= nil, "missing character prototype " .. variant.character)
    check(prototypes.entity[variant.corpse] ~= nil, "missing corpse prototype " .. variant.corpse)
  end
end

local function test_transfer(surface, force)
  force.character_logistic_requests = true
  force.character_trash_slot_count = 10

  local old = surface.create_entity({ name = "character", position = { 0, 0 }, force = force })
  old.character_inventory_slots_bonus = 5
  old.character_running_speed_modifier = 0.25
  old.character_health_bonus = 50

  local armor = old.get_inventory(defines.inventory.character_armor)
  armor.insert({ name = "power-armor-mk2" })
  local grid = armor[1].grid
  grid.put({ name = "fission-reactor-equipment" })
  grid.put({ name = "personal-roboport-equipment" })
  grid.put({ name = "battery-mk2-equipment" })
  local equipment_before = #grid.equipment

  old.get_inventory(defines.inventory.character_guns).insert({ name = "submachine-gun" })
  old.get_inventory(defines.inventory.character_guns).insert({ name = "pistol" })
  old.get_inventory(defines.inventory.character_ammo).insert({ name = "firearm-magazine", count = 200 })
  old.get_inventory(defines.inventory.character_ammo).insert({ name = "piercing-rounds-magazine", count = 50 })
  old.selected_gun_index = 2

  -- fill every main slot, including the ones that only exist thanks to the armor
  local main = old.get_main_inventory()
  main.set_filter(3, "copper-plate")
  for i = 1, #main do
    if i == 3 then
      main[i].set_stack({ name = "copper-plate", count = 100 })
    else
      main[i].set_stack({ name = "iron-plate", count = 100 })
    end
  end
  local main_size = #main
  old.get_inventory(defines.inventory.character_trash).insert({ name = "stone", count = 50 })

  local point = old.get_requester_point()
  local section = point.get_section(1) or point.add_section()
  section.set_slot(1, { value = "iron-gear-wheel", min = 50 })
  point.add_section("dragon-test-group")
  local sections_before = point.sections_count

  old.health = 120
  local before = count_items(old)

  local new = surface.create_entity({ name = "dragon-character-female-hoodie", position = { 0, 0 }, force = force })
  swap.transfer(old, new)

  local ok, diff = same_counts(before, count_items(new))
  check(ok, "item counts differ after transfer: " .. tostring(diff))
  check(next(count_items(old)) == nil, "old character still holds items")
  check(#surface.find_entities_filtered({ type = "item-entity" }) == 0, "items were spilled on the ground")
  local new_armor = new.get_inventory(defines.inventory.character_armor)[1]
  check(new_armor.valid_for_read and new_armor.name == "power-armor-mk2", "armor not moved")
  check(new_armor.valid_for_read and #new_armor.grid.equipment == equipment_before, "equipment grid not kept")
  check(#new.get_main_inventory() == main_size, "main inventory size changed: " .. #new.get_main_inventory() .. " vs " .. main_size)
  local filter = new.get_main_inventory().get_filter(3)
  check(filter and filter.name == "copper-plate", "inventory filter not copied")
  check(new.character_inventory_slots_bonus == 5, "inventory slot bonus not copied")
  check(math.abs(new.character_running_speed_modifier - 0.25) < 1e-6, "running speed bonus not copied")
  check(new.character_health_bonus == 50, "health bonus not copied")
  check(math.abs(new.health - 120) < 1e-6, "health not copied: " .. new.health)
  check(new.selected_gun_index == 2, "selected gun not copied")
  local new_point = new.get_requester_point()
  check(new_point.sections_count == sections_before, "logistic sections: " .. new_point.sections_count .. " vs " .. sections_before)
  local slot = new_point.get_section(1).get_slot(1)
  check(slot and slot.value and slot.value.name == "iron-gear-wheel" and slot.min == 50, "logistic request not copied")
  local group_found = false
  for i = 1, new_point.sections_count do
    if new_point.get_section(i).group == "dragon-test-group" then
      group_found = true
    end
  end
  check(group_found, "logistic group section not copied")
  old.destroy()
  new.destroy()
end

local function test_corpse(surface, force)
  local corpse = surface.create_entity({
    name = "dragon-character-corpse-male-shorts",
    position = { 5, 5 },
    force = force,
    inventory_size = 5,
  })
  corpse.get_inventory(defines.inventory.character_corpse).insert({ name = "iron-plate", count = 42 })
  local new = swap.replace_corpse(corpse, "character-corpse")
  check(new and new.valid and new.name == "character-corpse", "corpse not replaced")
  check(not corpse.valid, "old corpse still exists")
  check(new and new.get_inventory(defines.inventory.character_corpse).get_item_count("iron-plate") == 42,
    "corpse items not kept")
end

script.on_event(defines.events.on_tick, function(event)
  if event.tick ~= 2 then
    return
  end
  local surface = game.surfaces[1]
  local force = game.forces.player
  test_prototypes()
  test_transfer(surface, force)
  test_corpse(surface, force)
  if #failures > 0 then
    error("DRAGON-TEST: FAIL\n" .. table.concat(failures, "\n"))
  end
  log("DRAGON-TEST: PASS")
  print("DRAGON-TEST: PASS")
end)

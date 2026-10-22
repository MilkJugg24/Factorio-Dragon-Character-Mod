-- Data stage checks. Runs after dragon-character's data-final-fixes (this mod
-- depends on it), so every character prototype exists by now.

local appearance = require("__dragon-character__/scripts/appearance")
local legacy = require("prototypes.legacy-dragon-animations")

local failures = {}

local function check(condition, message)
  if not condition then
    failures[#failures + 1] = message
  end
end

-- Returns nil when equal, otherwise the path of the first difference.
local function diff(a, b, path)
  if type(a) ~= type(b) then
    return path .. ": " .. type(a) .. " vs " .. type(b)
  end
  if type(a) ~= "table" then
    if a ~= b then
      return path .. ": " .. tostring(a) .. " vs " .. tostring(b)
    end
    return nil
  end
  for k, v in pairs(a) do
    local d = diff(v, b[k], path .. "." .. tostring(k))
    if d then
      return d
    end
  end
  for k, v in pairs(b) do
    if a[k] == nil then
      return path .. "." .. tostring(k) .. ": nil vs " .. type(v)
    end
  end
  return nil
end

local flight = false
for _, armor in pairs(data.raw["armor"] or {}) do
  if armor.provides_flight then
    flight = true
    break
  end
end

---------------------------------------------------------------------------
-- The dragon is unchanged by the avatar pack refactor (0.2.0)
---------------------------------------------------------------------------

for _, variant in ipairs(appearance.variants()) do
  local outfit = { hoodie = variant.hoodie, shorts = variant.shorts }
  local character = data.raw["character"][variant.character]
  local corpse = data.raw["character-corpse"][variant.corpse]
  local label = variant.character
  local d = diff(legacy.armor_animation(variant.body, outfit, flight), character.animations[1], label)
  check(d == nil, "dragon animations changed: " .. tostring(d))
  d = diff(legacy.corpse_picture(variant.body, outfit), corpse.pictures[1], variant.corpse)
  check(d == nil, "dragon corpse changed: " .. tostring(d))
  check(#character.animations == 1, label .. ": more than one armor animation")
  check(character.distance_per_frame == legacy.distance_per_frame, label .. ": distance_per_frame changed")
  d = diff({ legacy.mine_impact_frame }, character.mining_with_tool_particles_animation_positions, label)
  check(d == nil, "mining particle frames changed: " .. tostring(d))
  check(diff(legacy.left_foot_frames, character.running_sound_animation_positions, "") == nil,
    label .. ": running sound frames changed")
  check(diff(legacy.right_foot_frames, character.moving_sound_animation_positions, "") == nil,
    label .. ": moving sound frames changed")
  check(diff(legacy.left_foot_frames, character.left_footprint_frames, "") == nil, label .. ": footprints changed")
  check(diff(legacy.right_foot_frames, character.right_footprint_frames, "") == nil, label .. ": footprints changed")
  check(character.character_corpse == variant.corpse, label .. ": wrong corpse")
end

---------------------------------------------------------------------------
-- Prototypes built from the packs written by tools/avatar_pack/make_test_pack.py
---------------------------------------------------------------------------

local function layer_count(animation)
  return #animation.layers
end

-- --id test --flight --tint --max-sheet 1024: split sheets, mask layers, custom timing
if mods["dragon-avatar-test"] then
  local c = data.raw["character"]["dragon-avatar-test"]
  local corpse = data.raw["character-corpse"]["dragon-avatar-corpse-test"]
  check(c ~= nil, "pack test: no character prototype")
  check(corpse ~= nil, "pack test: no corpse prototype")
  check(data.raw["sprite"]["dragon-character-avatar-test"] ~= nil, "pack test: no GUI sprite")
  if c and corpse then
    local anim = c.animations[1]
    local run_gun = anim.running_with_gun
    check(layer_count(run_gun) == 3, "pack test: run-gun should have base, mask and shadow")
    local base = run_gun.layers[1]
    check(base.filename == nil and base.filenames and #base.filenames == 3, "pack test: run-gun base not split in 3")
    check(base.lines_per_file == 8 and base.slice == 8, "pack test: run-gun lines_per_file/slice")
    check(base.direction_count == 18 and base.frame_count == 8 and base.line_length == 8, "pack test: run-gun counts")
    check(base.scale == 0.5, "pack test: scale should be 0.5 at 64 pixels per tile")
    check(run_gun.layers[2].apply_runtime_tint == true, "pack test: mask not tinted")
    check(run_gun.layers[3].draw_as_shadow == true, "pack test: shadow not drawn as shadow")
    check(anim.flipped_shadow_running_with_gun ~= nil, "pack test: no flipped shadow")
    check(anim.idle.layers[1].filename == "__dragon-avatar-test__/graphics/idle-base.png", "pack test: idle filename")
    check(anim.idle.layers[1].animation_speed == 0.08, "pack test: idle speed override")
    check(anim.running.layers[1].animation_speed == 0.5, "pack test: run speed override")
    check(anim.mining_with_tool.layers[1].animation_speed == 0.415, "pack test: default mine speed")
    check(c.distance_per_frame == 0.15, "pack test: distance_per_frame")
    check(c.mining_with_tool_particles_animation_positions[1] == 6, "pack test: mine impact frame")
    check(c.running_sound_animation_positions[1] == 1 and c.moving_sound_animation_positions[1] == 5,
      "pack test: foot frames")
    check(c.character_corpse == "dragon-avatar-corpse-test", "pack test: corpse link")
    check(c.icon == "__dragon-avatar-test__/graphics/icon.png", "pack test: icon")
    check(c.inventory_size == data.raw["character"]["character"].inventory_size, "pack test: inventory size")
    check(layer_count(corpse.pictures[1]) == 3, "pack test: corpse layers")
    if flight then
      check(anim.flying_with_gun and #anim.flying_with_gun.layers[1].filenames == 3, "pack test: fly-gun split")
      check(anim.take_off.layers[1].filename == "__dragon-avatar-test__/graphics/take-off-base.png",
        "pack test: own take-off")
      check(diff({ 5, 4, 3, 2, 1 }, anim.landing.layers[1].frame_sequence, "") == nil, "pack test: landing reversed")
    else
      check(anim.flying == nil, "pack test: flight animations without flying armor")
    end
  end
end

-- --id small --ppt 32 --default-timing --frames run=6,mine=8: no flight, no
-- tint, 32 pixels per tile, timing defaults scaled to its frame counts
if mods["dragon-avatar-small"] then
  local c = data.raw["character"]["dragon-avatar-small"]
  check(c ~= nil, "pack small: no character prototype")
  if c then
    local anim = c.animations[1]
    check(layer_count(anim.idle) == 2, "pack small: idle should have base and shadow")
    check(anim.idle.layers[1].scale == 1, "pack small: scale should be 1 at 32 pixels per tile")
    check(c.distance_per_frame == 0.238, "pack small: default distance_per_frame")
    check(c.mining_with_tool_particles_animation_positions[1] == 6, "pack small: scaled mine impact frame")
    check(diff({ 1 }, c.running_sound_animation_positions, "") == nil, "pack small: default left foot frames")
    check(diff({ 4 }, c.moving_sound_animation_positions, "") == nil, "pack small: scaled right foot frames")
    if flight then
      -- no flight animations: the ground ones are shown in the air
      check(diff(anim.running, anim.flying, "") == nil, "pack small: flying should fall back to running")
      check(diff(anim.running_with_gun, anim.flying_with_gun, "") == nil, "pack small: fly-gun fallback")
      check(diff(anim.idle, anim.idle_in_air, "") == nil, "pack small: hover fallback")
      check(diff(anim.idle, anim.landing, "") == nil and anim.landing.layers[1].frame_sequence == nil,
        "pack small: landing fallback")
    end
  end
end

if #failures > 0 then
  error("DRAGON-TEST: FAIL (data stage)\n" .. table.concat(failures, "\n"))
end
log("DRAGON-TEST: data stage checks passed")

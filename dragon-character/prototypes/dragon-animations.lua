-- Builds character animation definitions from the generated sprite metadata.

local sprite_data = require("prototypes.sprite-data")

local animations = {}

-- 1-based frame of the mining animation in which the pick hits the ground.
animations.mine_impact_frame = 9

-- Frames of the running cycle (12 frames = one stride) where each foot lands.
animations.left_foot_frames = { 1 }
animations.right_foot_frames = { 7 }

-- One running cycle covers two steps of the vanilla engineer's length.
animations.distance_per_frame = 0.119

local speeds = {
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
}

local function get_meta(body, name)
  local meta = sprite_data[body] and sprite_data[body][name]
  if not meta then
    error("dragon-character: missing sprite data for " .. body .. "/" .. name)
  end
  return meta
end

local function layer(meta, key, extra, rotated)
  local info = meta.layers[key]
  local l = {
    filename = info.filename,
    width = info.width,
    height = info.height,
    shift = info.shift,
    scale = 0.5,
    frame_count = meta.frame_count,
    line_length = meta.frame_count,
    usage = "player",
  }
  if rotated then
    l.direction_count = meta.direction_count
  end
  for k, v in pairs(extra or {}) do
    l[k] = v
  end
  return l
end

-- Layers in draw order: body, tinted scales, shorts, hoodie, shadow.
local function layers(body, name, outfit, rotated, extra)
  local meta = get_meta(body, name)
  local common = { animation_speed = speeds[name] }
  for k, v in pairs(extra or {}) do
    common[k] = v
  end
  local function with(t)
    local r = {}
    for k, v in pairs(common) do r[k] = v end
    for k, v in pairs(t or {}) do r[k] = v end
    return r
  end
  local list = {
    layer(meta, "base", with(), rotated),
    layer(meta, "mask", with({ apply_runtime_tint = true }), rotated),
  }
  if outfit.shorts then
    list[#list + 1] = layer(meta, "shorts", with(), rotated)
  end
  if outfit.hoodie then
    list[#list + 1] = layer(meta, "hoodie", with(), rotated)
  end
  list[#list + 1] = layer(meta, "shadow", with({ draw_as_shadow = true }), rotated)
  return { layers = list }
end

local function rotated(body, name, outfit, extra)
  return layers(body, name, outfit, true, extra)
end

-- Shadow drawn instead of the regular one while the engine mirrors running_with_gun
-- (gun pointing west): the mirrored dragon's shadow, still falling to the east.
local function flipped_shadow(body, name)
  local extra = { animation_speed = speeds[name], draw_as_shadow = true }
  return { layers = { layer(get_meta(body, name), "shadow-flipped", extra, true) } }
end

local function reversed_sequence(n)
  local seq = {}
  for i = n, 1, -1 do
    seq[#seq + 1] = i
  end
  return seq
end

-- A single CharacterArmorAnimation used for every armor.
-- `flight` adds the in-air animations used by flight-capable armor (Space Age mech armor).
function animations.armor_animation(body, outfit, flight)
  local anim = {
    idle = rotated(body, "idle", outfit),
    idle_with_gun = rotated(body, "idle-gun", outfit),
    running = rotated(body, "run", outfit),
    running_with_gun = rotated(body, "run-gun", outfit),
    flipped_shadow_running_with_gun = flipped_shadow(body, "run-gun"),
    mining_with_tool = rotated(body, "mine", outfit),
    mining_with_tool_particles_animation_positions = { animations.mine_impact_frame },
  }
  if flight then
    local take_off_frames = get_meta(body, "take-off").frame_count
    anim.idle_in_air = rotated(body, "hover", outfit)
    anim.idle_with_gun_in_air = rotated(body, "hover-gun", outfit)
    anim.flying = rotated(body, "fly", outfit)
    anim.flying_with_gun = rotated(body, "fly-gun", outfit)
    anim.take_off = rotated(body, "take-off", outfit)
    anim.landing = rotated(body, "take-off", outfit, { frame_sequence = reversed_sequence(take_off_frames) })
  end
  return anim
end

function animations.corpse_picture(body, outfit)
  return layers(body, "corpse", outfit, false)
end

return animations

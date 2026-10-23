-- Plays the Space Age travel test by itself in a single-player game: the same calls as the console
-- commands in the test instructions, on a timeline, with a log of the player's state every 10 ticks.
local OUT = "space-test/"
local lines = {}

local function say(text)
  local line = string.format("[tick %d, +%d] %s", game.tick, game.tick - (storage.t0 or game.tick), text)
  log("DRIVER: " .. line)
  lines[#lines + 1] = line
  helpers.write_file(OUT .. "driver.txt", line .. "\n", true)
end

local function player()
  for _, p in pairs(game.players) do if p.connected then return p end end
end

local function items(p)
  local inv = p.character and p.get_main_inventory()
  if not inv then return "-" end
  local t = {}
  for _, it in pairs(inv.get_contents()) do t[#t + 1] = it.name .. "=" .. it.count end
  table.sort(t)
  return table.concat(t, ", ")
end

local function state(p)
  local c = p.character
  local pod = c and c.valid and (function() local ok, v = pcall(function() return c.cargo_pod end); return ok and v end)()
  local flying = c and c.valid and (function() local ok, v = pcall(function() return c.is_flying end); return ok and v end)()
  local ctrl = "?"
  for k, v in pairs(defines.controllers) do if v == p.controller_type then ctrl = k end end
  return string.format("controller=%s character=%s on %s surface=%s pod=%s flying=%s pos=%s appearance=%s",
    ctrl, c and c.valid and c.name or "none", c and c.valid and c.surface.name or "-", p.surface.name, tostring(pod and true or false), tostring(flying),
    c and c.valid and string.format("(%.1f, %.1f)", c.position.x, c.position.y) or "-",
    serpent.line(remote.call("avatar-characters", "get_appearance", p.index)))
end

local function shot(p, name, gui)
  game.take_screenshot({ player = p, by_player = p, show_gui = gui ~= false, zoom = 1, daytime = 0.5,
    resolution = { p.display_resolution.width, p.display_resolution.height }, path = OUT .. name .. ".png" })
  say("screenshot " .. name)
end

local steps = {}
local function at(dt, f) steps[#steps + 1] = { dt = dt, f = f } end

at(0, function(p)
  p.insert({ name = "iron-plate", count = 37 })
  p.insert({ name = "transport-belt", count = 5 })
  say("set_appearance chibi -> " .. tostring(remote.call("avatar-characters", "set_appearance", p.index, { avatar = "harness-chibi" })))
  say("start: " .. state(p) .. " | items " .. items(p))
  storage.items_before = items(p)
end)
at(60, function(p) shot(p, "01-start-chibi") end)
at(90, function(p)
  local pl = p.force.create_space_platform({ name = "test", planet = "nauvis", starter_pack = "space-platform-starter-pack" })
  storage.platform = pl
  say("create_space_platform -> " .. tostring(pl and pl.name) .. ", state " .. tostring(pl and pl.state))
  local ok, err = pcall(function() return pl.apply_starter_pack() end)
  say("apply_starter_pack -> " .. tostring(ok) .. " " .. tostring(err) .. ", hub " .. tostring(pl.hub and pl.hub.valid))
end)
at(390, function(p)
  local ok, err = pcall(function() return p.enter_space_platform(p.force.platforms[1]) end)
  say("enter_space_platform -> " .. tostring(ok) .. " " .. tostring(err))
end)
at(450, function(p)
  game.print(p.character and p.character.name or "no character")
  say("game.print(character name) printed: " .. (p.character and p.character.name or "no character"))
  say("on the platform: " .. state(p))
  shot(p, "02-on-platform")
end)
at(480, function(p)
  remote.call("avatar-characters", "open_window", p.index)
  say("window opened: " .. tostring(p.gui.screen["avatar_characters_frame"] ~= nil))
end)
at(510, function(p)
  say("set_appearance generic -> " .. tostring(remote.call("avatar-characters", "set_appearance", p.index, { avatar = "harness-generic" })))
  say("after picking generic: " .. state(p))
end)
at(540, function(p) shot(p, "03-window-on-platform") end)
at(600, function(p)
  p.opened = nil
  local f = p.gui.screen["avatar_characters_frame"]
  say("window closed: " .. tostring(f == nil or not f.valid))
end)
at(630, function(p)
  local ok, res = pcall(function() return p.land_on_planet() end)
  storage.left_at = game.tick
  say("land_on_planet -> " .. tostring(ok) .. " " .. tostring(res) .. " | " .. state(p))
end)
for k = 1, 32 do at(630 + 45 * k, function(p)
  local pos = p.position
  game.take_screenshot({ player = p, by_player = p, surface = p.surface, position = pos, zoom = 1, daytime = 0.5,
    resolution = { 960, 540 }, path = string.format("%s04-descent-%02d.png", OUT, k), force_render = true })
  say(string.format("screenshot 04-descent-%02d at %s on %s", k, serpent.line({ math.floor(pos.x), math.floor(pos.y) }), p.surface.name))
end) end
at(2160, function(p)
  say("after landing: " .. state(p) .. " | items " .. items(p))
  say("items before going up: " .. tostring(storage.items_before))
  shot(p, "05-landed")
end)
at(2220, function(p)
  storage.died_at = game.tick
  p.character.die()
  say("died: " .. state(p) .. ", ticks_to_respawn " .. tostring(p.ticks_to_respawn))
end)
at(2280, function(p) shot(p, "06-dead") end)
at(2900, function(p)
  say("respawn check: " .. state(p) .. ", ticks_to_respawn " .. tostring(p.ticks_to_respawn))
end)
at(3000, function(p)
  say("after respawning: " .. state(p) .. " | items " .. items(p))
  shot(p, "07-respawned")
end)
at(3060, function(p)
  helpers.write_file(OUT .. "done.txt", "done\n")
  say("done")
end)

script.on_init(function()
  if remote.interfaces["freeplay"] then
    remote.call("freeplay", "set_skip_intro", true)
    remote.call("freeplay", "set_disable_crashsite", true)
  end
end)

script.on_event(defines.events.on_tick, function()
  local p = player()
  if not p then return end
  if not storage.t0 then
    storage.t0 = game.tick + 120
    storage.next = 1
    storage.last = ""
  end
  local dt = game.tick - storage.t0
  if dt < 0 then return end
  while steps[storage.next] and steps[storage.next].dt <= dt do
    steps[storage.next].f(p)
    storage.next = storage.next + 1
  end
  -- every 10 ticks after leaving the platform or dying: log any change of state
  if (storage.left_at or storage.died_at) and dt % 10 == 0 then
    local s = state(p)
    if s:gsub("pos=%b()", "") ~= storage.last:gsub("pos=%b()", "") then
      say("state change: " .. s)
      storage.last = s
    end
  end
end)

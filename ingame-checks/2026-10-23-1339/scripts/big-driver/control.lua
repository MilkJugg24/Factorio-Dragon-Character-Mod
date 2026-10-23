-- Big bundle test: the avatar window, then all 12 pack characters side by side, idle and then running,
-- with the tick logged every 300 ticks (UPS from the log's timestamps).
local OUT = "big-test/"

local function say(text)
  local line = string.format("[tick %d] %s", game.tick, text)
  log("DRIVER: " .. line)
  helpers.write_file(OUT .. "driver.txt", line .. "\n", true)
end

local function player()
  for _, p in pairs(game.players) do if p.connected then return p end end
end

script.on_init(function()
  if remote.interfaces["freeplay"] then
    remote.call("freeplay", "set_skip_intro", true)
    remote.call("freeplay", "set_disable_crashsite", true)
  end
end)

script.on_event(defines.events.on_tick, function()
  local p = player()
  if not p then return end
  if not storage.t0 then storage.t0 = game.tick + 120 end
  local dt = game.tick - storage.t0
  if dt < 0 then return end
  if dt == 0 then
    local packs = remote.call("avatar-characters", "get_avatar_packs")
    say("packs installed: " .. #packs)
    remote.call("avatar-characters", "open_window", p.index)
    say("window open: " .. tostring(p.gui.screen["avatar_characters_frame"] ~= nil))
  elseif dt == 30 then
    game.take_screenshot({ player = p, by_player = p, show_gui = true, zoom = 1, daytime = 0,
      resolution = { p.display_resolution.width, p.display_resolution.height }, path = OUT .. "window-12.png" })
    say("screenshot window-12")
  elseif dt == 90 then
    p.opened = nil
    local f = p.gui.screen["avatar_characters_frame"]
    if f and f.valid then f.destroy() end
    -- the command from the test instructions, with game.player = p
    storage.chars = {}
    for i = 1, 12 do
      local n = string.format("avatar-pack-big-%02d", i)
      storage.chars[i] = p.surface.create_entity({ name = n, position = { i * 2, 5 }, force = "player" })
    end
    p.teleport({ 13, 1 })
    say("created " .. #storage.chars .. " characters")
  elseif dt == 150 then
    game.take_screenshot({ player = p, by_player = p, show_gui = true, zoom = 1, daytime = 0,
      resolution = { p.display_resolution.width, p.display_resolution.height }, path = OUT .. "12-characters.png" })
    game.take_screenshot({ surface = p.surface, position = { 13, 4.5 }, zoom = 2, daytime = 0, force_render = true,
      resolution = { 1792, 448 }, path = OUT .. "12-characters-close.png" })
    say("screenshots 12-characters")
    helpers.write_file(OUT .. "ready.txt", "ready\n")
  end
  -- from +15 s to +30 s everyone runs east and west, turning every 2 s
  if dt >= 900 and dt < 1800 and storage.chars then
    local dir = (math.floor((dt - 900) / 120) % 2 == 0) and defines.direction.east or defines.direction.west
    for _, c in pairs(storage.chars) do
      if c.valid then c.walking_state = { walking = true, direction = dir } end
    end
  elseif dt == 1800 and storage.chars then
    for _, c in pairs(storage.chars) do if c.valid then c.walking_state = { walking = false, direction = defines.direction.south } end end
  end
  if dt > 0 and dt % 300 == 0 then say("tick mark") end
  if dt == 900 then say("running starts") end
  if dt == 2100 then
    helpers.write_file(OUT .. "done.txt", "done\n")
    say("done")
  end
end)

local appearance = require("scripts.appearance")
local swap = require("scripts.swap")
local gui = require("scripts.gui")
local swatches = require("scripts.swatches")
local packs = require("scripts.packs")
local revert = require("scripts.revert")

-- How often (in ticks) appearance changes that had to wait are retried.
local RETRY_INTERVAL = 30

-- storage.players[player_index] = { body, hoodie, shorts, avatar, status, gui_location }
--   avatar: id of the chosen avatar pack, nil for the dragon
-- storage.pending[player_index] = true while the player's character does not yet match
-- storage.swap_tick[player_index] = tick of the player's last character swap
local function init_storage()
  storage.players = storage.players or {}
  storage.pending = storage.pending or {}
  storage.swap_tick = storage.swap_tick or {}
end

local function prefs_of(player_index)
  local prefs = storage.players[player_index]
  if not prefs then
    local d = appearance.default
    prefs = { body = d.body, hoodie = d.hoodie, shorts = d.shorts }
    storage.players[player_index] = prefs
  end
  return prefs
end

-- Statuses that do not clear up by themselves; everything else (leaving a
-- vehicle, finishing crafting, ...) is retried.
local PERMANENT = { missing = true, ["avatar-missing"] = true }

local function prototype_exists(name)
  return prototypes.entity[name] ~= nil
end

-- Makes the player's character match their chosen appearance, or remembers to
-- try again later. Returns nil on success, otherwise a status string.
local function apply_appearance(player)
  local prefs = prefs_of(player.index)
  -- `note` is "avatar-missing" when the chosen avatar pack is gone; the dragon is used instead
  local target, note = appearance.resolve(prefs, prototype_exists)
  local character = player.character
  local status
  if character and character.valid and character.name == target then
    status = nil
  elseif character and character.valid and not appearance.is_own_character(character.name) then
    -- A character prototype from another mod (e.g. a temporary jetpack
    -- character). Leave it alone and try again once it is ours again.
    status = "foreign"
  elseif not prototypes.entity[target] then
    status = "missing"
  else
    status = swap.blocked_reason(player)
    if not status then
      -- set before swapping: on_gui_closed may fire while the character changes
      storage.swap_tick[player.index] = game.tick
      local new_character, err = swap.replace(player, target)
      status = err
      if new_character then
        -- switching characters may close whatever the player had open; keep
        -- the customization window open
        local frame = gui.get_frame(player)
        if frame and player.opened ~= frame then
          player.opened = frame
        end
      end
    end
  end
  if status and not PERMANENT[status] then
    storage.pending[player.index] = true
  else
    storage.pending[player.index] = nil
  end
  status = status or note
  prefs.status = status
  gui.refresh(player, prefs)
  return status
end

local function retry_if_pending(event)
  local index = event.player_index
  if index and storage.pending[index] then
    local player = game.get_player(index)
    if player and player.connected then
      apply_appearance(player)
    end
  end
end

local function set_color(player, color)
  player.color = { r = color.r, g = color.g, b = color.b, a = appearance.default_color.a }
end

local function toggle_gui(player)
  local prefs = prefs_of(player.index)
  if gui.get_frame(player) then
    gui.close(player, prefs)
  else
    gui.open(player, prefs)
  end
end

local function setup_player(player)
  prefs_of(player.index)
  gui.update_top_button(player)
end

---------------------------------------------------------------------------
-- Lifecycle
---------------------------------------------------------------------------

script.on_init(function()
  init_storage()
  for _, player in pairs(game.players) do
    setup_player(player)
    apply_appearance(player)
  end
end)

script.on_configuration_changed(function()
  init_storage()
  for _, player in pairs(game.players) do
    -- rebuild the window in case its layout changed between versions
    local prefs = prefs_of(player.index)
    if prefs.avatar and not packs.get(prefs.avatar) then
      -- the preference is kept, so reinstalling the pack brings the look back
      player.print({ "dragon-character.avatar-pack-gone", prefs.avatar })
    end
    local was_open = gui.get_frame(player) ~= nil
    gui.close(player, prefs)
    setup_player(player)
    storage.pending[player.index] = true
    if player.connected then
      apply_appearance(player)
      if was_open then
        gui.open(player, prefs)
      end
    end
  end
end)

script.on_event(defines.events.on_player_created, function(event)
  local player = game.get_player(event.player_index)
  setup_player(player)
  apply_appearance(player)
end)

script.on_event(defines.events.on_player_joined_game, function(event)
  local player = game.get_player(event.player_index)
  -- a window left open when the player disconnected is no longer their
  -- opened GUI (Esc would not close it), so start without it
  gui.close(player, prefs_of(player.index))
  setup_player(player)
  retry_if_pending(event)
end)

script.on_event(defines.events.on_player_removed, function(event)
  storage.players[event.player_index] = nil
  storage.pending[event.player_index] = nil
  storage.swap_tick[event.player_index] = nil
end)

script.on_event(defines.events.on_player_respawned, function(event)
  local player = game.get_player(event.player_index)
  if player then
    apply_appearance(player)
  end
end)

script.on_event(defines.events.on_player_died, function(event)
  local player = game.get_player(event.player_index)
  if player then
    storage.pending[player.index] = true
    -- dying closes the opened GUI without raising on_gui_closed, which would
    -- leave the window on screen with Esc no longer closing it
    gui.close(player, prefs_of(player.index))
  end
end)

for _, name in ipairs({
  "on_player_controller_changed",
  "on_player_driving_changed_state",
  "on_cutscene_finished",
  "on_cutscene_cancelled",
  "on_player_toggled_map_editor",
}) do
  script.on_event(defines.events[name], retry_if_pending)
end

script.on_nth_tick(RETRY_INTERVAL, function()
  if not next(storage.pending) then
    return
  end
  for index in pairs(storage.pending) do
    local player = game.get_player(index)
    if not player then
      storage.pending[index] = nil
    elseif player.connected then
      apply_appearance(player)
    end
  end
end)

script.on_event(defines.events.on_runtime_mod_setting_changed, function(event)
  if event.setting == "dragon-character-show-top-button" and event.player_index then
    local player = game.get_player(event.player_index)
    if player then
      gui.update_top_button(player)
    end
  end
end)

---------------------------------------------------------------------------
-- Opening the window
---------------------------------------------------------------------------

script.on_event(defines.events.on_lua_shortcut, function(event)
  if event.prototype_name == gui.SHORTCUT then
    toggle_gui(game.get_player(event.player_index))
  end
end)

script.on_event("dragon-character-toggle-gui", function(event)
  toggle_gui(game.get_player(event.player_index))
end)

---------------------------------------------------------------------------
-- Window interaction
---------------------------------------------------------------------------

local function set_body(player, body)
  local prefs = prefs_of(player.index)
  if prefs.body ~= body then
    prefs.body = body
    apply_appearance(player)
  else
    gui.refresh(player, prefs)
  end
end

local function set_outfit(player, outfit, state)
  local prefs = prefs_of(player.index)
  if (prefs[outfit] or false) ~= state then
    prefs[outfit] = state
    apply_appearance(player)
  end
end

local function set_avatar(player, id)
  local prefs = prefs_of(player.index)
  if prefs.avatar ~= id then
    prefs.avatar = id
    apply_appearance(player)
  else
    gui.refresh(player, prefs)
  end
end

local function reset(player)
  local prefs = prefs_of(player.index)
  local d = appearance.default
  prefs.body, prefs.hoodie, prefs.shorts = d.body, d.hoodie, d.shorts
  prefs.avatar = nil
  set_color(player, appearance.default_color)
  apply_appearance(player)
  gui.refresh_color(player)
end

local function swatch_color(name)
  for _, swatch in ipairs(swatches) do
    if swatch.name == name then
      return swatch.color
    end
  end
  return nil
end

local function is_ours(element)
  return element and element.valid and element.tags and element.tags.dragon_action ~= nil
end

script.on_event(defines.events.on_gui_click, function(event)
  local element = event.element
  if not is_ours(element) then
    return
  end
  local player = game.get_player(event.player_index)
  local tags = element.tags
  local action = tags.dragon_action
  if action == "toggle" then
    toggle_gui(player)
  elseif action == "close" then
    gui.close(player, prefs_of(player.index))
  elseif action == "swatch" then
    local color = swatch_color(tags.swatch)
    if color then
      set_color(player, color)
      gui.refresh_color(player)
    end
  elseif action == "body" and element.type == "sprite-button" then
    set_body(player, tags.body)
  elseif action == "look" then
    local id = tags.avatar
    if id == "" then
      set_avatar(player, nil)
    elseif packs.get(id) then
      set_avatar(player, id)
    end
  elseif action == "reset" then
    reset(player)
  end
end)

script.on_event(defines.events.on_gui_checked_state_changed, function(event)
  local element = event.element
  if not is_ours(element) then
    return
  end
  local player = game.get_player(event.player_index)
  local tags = element.tags
  if tags.dragon_action == "body" then
    set_body(player, tags.body)
  elseif tags.dragon_action == "outfit" then
    set_outfit(player, tags.outfit, element.state)
  end
end)

script.on_event(defines.events.on_gui_value_changed, function(event)
  local element = event.element
  if not (is_ours(element) and element.tags.dragon_action == "slider") then
    return
  end
  local player = game.get_player(event.player_index)
  local color = player.color
  color[element.tags.channel] = element.slider_value / 255
  set_color(player, color)
  gui.refresh_color(player, element)
end)

local function on_text(event, confirmed)
  local element = event.element
  if not is_ours(element) then
    return
  end
  local player = game.get_player(event.player_index)
  local action = element.tags.dragon_action
  if action == "channel_text" then
    local value = tonumber(element.text)
    if value then
      local color = player.color
      color[element.tags.channel] = math.max(0, math.min(255, value)) / 255
      set_color(player, color)
    end
    gui.refresh_color(player, (not confirmed) and element or nil)
  elseif action == "hex" then
    local color = gui.parse_hex(element.text)
    if color then
      set_color(player, color)
      gui.refresh_color(player, (not confirmed) and element or nil)
    elseif confirmed then
      gui.refresh_color(player)
    end
  end
end

script.on_event(defines.events.on_gui_text_changed, function(event)
  on_text(event, false)
end)

script.on_event(defines.events.on_gui_confirmed, function(event)
  on_text(event, true)
end)

script.on_event(defines.events.on_gui_closed, function(event)
  local element = event.element
  if element and element.valid and element.name == gui.FRAME then
    local player = game.get_player(event.player_index)
    if storage.swap_tick[player.index] == game.tick then
      -- closed as a side effect of replacing the character, not by the player
      player.opened = element
      return
    end
    gui.close(player, prefs_of(player.index))
  end
end)

---------------------------------------------------------------------------
-- Commands
---------------------------------------------------------------------------

commands.add_command("dragon", { "dragon-character.command-dragon-help" }, function(command)
  local player = command.player_index and game.get_player(command.player_index)
  if player then
    toggle_gui(player)
  end
end)

-- Before removing the mod (or an avatar pack), every character and corpse using
-- one of its prototypes must be turned back into the vanilla ones, otherwise the
-- game deletes them (and the items they hold) when the save is loaded.
local function revert_all(pack_id)
  return revert.run(pack_id, prefs_of, apply_appearance, storage.pending)
end

commands.add_command("dragon-revert-all", { "dragon-character.command-revert-help" }, function(command)
  local caller = command.player_index and game.get_player(command.player_index)
  local function reply(message)
    if caller then
      caller.print(message)
    else
      game.print(message)
    end
  end
  if caller and not caller.admin then
    reply({ "dragon-character.admin-only" })
    return
  end
  local pack_id = command.parameter and command.parameter:match("^%s*(%S+)%s*$")
  local result = revert_all(pack_id)
  if pack_id then
    reply({ "dragon-character.revert-pack-done", pack_id, result.corpses, result.remaining })
  else
    reply({ "dragon-character.revert-done", result.corpses, result.remaining })
  end
end)

---------------------------------------------------------------------------
-- Remote interface for other mods
---------------------------------------------------------------------------

remote.add_interface("dragon-character", {
  -- Event raised after a player's character entity was replaced:
  -- { player_index, old_character, new_character, old_unit_number, new_unit_number }
  get_on_character_swapped_event = function()
    return swap.on_character_swapped
  end,
  -- Returns { body = "male"|"female", hoodie = bool, shorts = bool, avatar = pack id or nil }
  get_appearance = function(player_index)
    local prefs = prefs_of(player_index)
    return { body = prefs.body, hoodie = prefs.hoodie, shorts = prefs.shorts, avatar = prefs.avatar }
  end,
  -- Changes any of body/hoodie/shorts/avatar (a pack id, or false for the
  -- dragon). Returns nil when applied, or a status string when the change has
  -- to wait (e.g. "vehicle", "crafting") or failed ("unknown-avatar").
  set_appearance = function(player_index, new)
    local player = game.get_player(player_index)
    if not player then
      return "no-player"
    end
    new = new or {}
    if new.avatar and not packs.get(new.avatar) then
      return "unknown-avatar"
    end
    local prefs = prefs_of(player_index)
    if new.body == "male" or new.body == "female" then
      prefs.body = new.body
    end
    if new.hoodie ~= nil then
      prefs.hoodie = new.hoodie and true or false
    end
    if new.shorts ~= nil then
      prefs.shorts = new.shorts and true or false
    end
    if new.avatar == false then
      prefs.avatar = nil
    elseif new.avatar ~= nil then
      prefs.avatar = new.avatar
    end
    return apply_appearance(player)
  end,
  -- Installed avatar packs: array of { id, title, author, tintable, character, corpse }
  get_avatar_packs = function()
    local list = {}
    for _, pack in ipairs(packs.list()) do
      list[#list + 1] = {
        id = pack.id,
        title = pack.title,
        author = pack.author,
        tintable = pack.tintable,
        character = pack.character,
        corpse = pack.corpse,
      }
    end
    return list
  end,
  -- The avatar pack id of a character prototype name, or nil.
  is_avatar_character = function(name)
    return appearance.avatar_id_of_character(name)
  end,
  -- Same as /dragon-revert-all [pack_id]. Returns { corpses, remaining }.
  revert_all = function(pack_id)
    return revert_all(pack_id)
  end,
  -- True if the character prototype name is one of the dragon variants
  -- (including the vanilla "character", which this mod turns into a dragon).
  is_dragon_character = function(name)
    return appearance.from_character_name(name) ~= nil
  end,
})

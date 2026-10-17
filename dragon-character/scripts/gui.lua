-- The dragon customization window and the optional top-left button.

local mod_gui = require("mod-gui")
local swatches = require("scripts.swatches")
local packs = require("scripts.packs")

local gui = {}

gui.FRAME = "dragon_character_frame"
gui.TOP_BUTTON = "dragon_character_top_button"
gui.SHORTCUT = "dragon-character-customize"

local CHANNELS = { "r", "g", "b" }

local function to255(v)
  return math.max(0, math.min(255, math.floor((v or 0) * 255 + 0.5)))
end

local function find(element, name)
  if element.name == name then
    return element
  end
  for _, child in pairs(element.children) do
    local found = find(child, name)
    if found then
      return found
    end
  end
  return nil
end

function gui.get_frame(player)
  local frame = player.gui.screen[gui.FRAME]
  if frame and frame.valid then
    return frame
  end
  return nil
end

function gui.hex_of(color)
  return string.format("#%02X%02X%02X", to255(color.r), to255(color.g), to255(color.b))
end

-- Parses "#RRGGBB", "RRGGBB" or "#RGB". Returns a color with components 0..1 or nil.
function gui.parse_hex(text)
  local hex = (text or ""):gsub("^%s*#?", ""):gsub("%s*$", "")
  if #hex == 3 then
    hex = hex:sub(1, 1):rep(2) .. hex:sub(2, 2):rep(2) .. hex:sub(3, 3):rep(2)
  end
  if #hex ~= 6 or hex:find("[^%x]") then
    return nil
  end
  return {
    r = tonumber(hex:sub(1, 2), 16) / 255,
    g = tonumber(hex:sub(3, 4), 16) / 255,
    b = tonumber(hex:sub(5, 6), 16) / 255,
  }
end

function gui.update_top_button(player)
  local show = player.mod_settings["dragon-character-show-top-button"].value
  local top = player.gui.top
  if not show and not (top.mod_gui_top_frame or top.mod_gui_button_flow) then
    -- nothing to remove, and get_button_flow() would create empty frames
    return
  end
  local flow = mod_gui.get_button_flow(player)
  local button = flow[gui.TOP_BUTTON]
  if show and not button then
    flow.add({
      type = "sprite-button",
      name = gui.TOP_BUTTON,
      style = mod_gui.button_style,
      sprite = "dragon-character-icon",
      tooltip = { "dragon-character.top-button-tooltip" },
      tags = { dragon_action = "toggle" },
    })
  elseif button and not show then
    button.destroy()
  end
  -- drop the shared mod-gui frames once no mod has a button left in them
  if not show and flow.name == "mod_gui_inner_frame" and #flow.children == 0 then
    flow.parent.destroy()
  end
end

local function section_label(parent, caption)
  parent.add({ type = "label", style = "caption_label", caption = caption })
end

function gui.close(player, prefs)
  local frame = gui.get_frame(player)
  if frame then
    if prefs then
      prefs.gui_location = frame.location
    end
    frame.destroy()
  end
  player.set_shortcut_toggled(gui.SHORTCUT, false)
end

function gui.open(player, prefs)
  gui.close(player, prefs)
  local frame = player.gui.screen.add({ type = "frame", name = gui.FRAME, direction = "vertical" })
  if prefs.gui_location then
    frame.location = prefs.gui_location
  else
    frame.auto_center = true
  end

  -- title bar
  local titlebar = frame.add({ type = "flow", direction = "horizontal" })
  titlebar.drag_target = frame
  titlebar.style.horizontal_spacing = 8
  titlebar.add({
    type = "label",
    style = "frame_title",
    caption = { "dragon-character.window-title" },
    ignored_by_interaction = true,
  })
  local drag = titlebar.add({ type = "empty-widget", style = "draggable_space_header", ignored_by_interaction = true })
  drag.style.height = 24
  drag.style.horizontally_stretchable = true
  drag.style.right_margin = 4
  titlebar.add({
    type = "sprite-button",
    style = "frame_action_button",
    sprite = "utility/close",
    tooltip = { "dragon-character.close" },
    tags = { dragon_action = "close" },
  })

  -- frames have no cell spacing of their own, so lay the content out in a flow
  local content_frame = frame.add({ type = "frame", style = "inside_shallow_frame_with_padding", direction = "horizontal" })
  local content = content_frame.add({ type = "flow", direction = "horizontal" })
  content.style.horizontal_spacing = 12

  -- live preview of the character
  local preview_frame = content.add({ type = "frame", style = "deep_frame_in_shallow_frame", direction = "vertical" })
  local camera = preview_frame.add({
    type = "camera",
    name = "dragon_preview",
    position = player.position,
    surface_index = player.surface.index,
    zoom = 1.6,
  })
  camera.style.width = 230
  camera.style.height = 300

  local options = content.add({ type = "flow", direction = "vertical" })
  options.style.vertical_spacing = 6
  options.style.width = 330

  local function section(name)
    local flow = options.add({ type = "flow", name = name, direction = "vertical" })
    flow.style.vertical_spacing = 6
    return flow
  end

  -- look: the dragon or one of the installed avatar packs (also shown when the
  -- chosen pack is gone, so the dragon can be picked to clear that choice)
  local installed = packs.list()
  if #installed > 0 or prefs.avatar then
    local look_section = section("dragon_look_section")
    section_label(look_section, { "dragon-character.look" })
    local pane = look_section.add({ type = "scroll-pane", horizontal_scroll_policy = "never" })
    pane.style.maximal_height = 168
    local looks = pane.add({ type = "table", name = "dragon_looks", column_count = 4 })
    looks.style.horizontal_spacing = 4
    looks.style.vertical_spacing = 4
    looks.add({
      type = "sprite-button",
      name = "dragon_look_",
      style = "dragon_character_body_button",
      sprite = "dragon-character-icon",
      tooltip = { "dragon-character.look-dragon" },
      tags = { dragon_action = "look", avatar = "" },
    })
    for _, pack in ipairs(installed) do
      local tooltip = pack.title
      if pack.author and pack.author ~= "" then
        tooltip = { "dragon-character.look-author", pack.title, pack.author }
      end
      looks.add({
        type = "sprite-button",
        name = "dragon_look_" .. pack.id,
        style = "dragon_character_body_button",
        sprite = pack.sprite,
        tooltip = tooltip,
        tags = { dragon_action = "look", avatar = pack.id },
      })
    end
    look_section.add({ type = "line" })
  end

  -- scale color
  local color_section = section("dragon_color_section")
  section_label(color_section, { "dragon-character.color" })
  local grid = color_section.add({ type = "table", name = "dragon_swatches", column_count = 9 })
  grid.style.horizontal_spacing = 2
  grid.style.vertical_spacing = 2
  for _, swatch in ipairs(swatches) do
    grid.add({
      type = "sprite-button",
      name = "dragon_swatch_" .. swatch.name,
      style = "dragon_character_swatch_button",
      sprite = "dragon-character-swatch-" .. swatch.name,
      tooltip = { "dragon-character.swatch-" .. swatch.name },
      tags = { dragon_action = "swatch", swatch = swatch.name },
    })
  end
  local rgb = color_section.add({ type = "table", column_count = 3 })
  rgb.style.horizontal_spacing = 8
  rgb.style.vertical_spacing = 4
  for _, channel in ipairs(CHANNELS) do
    rgb.add({ type = "label", caption = { "dragon-character.channel-" .. channel } })
    local slider = rgb.add({
      type = "slider",
      name = "dragon_slider_" .. channel,
      minimum_value = 0,
      maximum_value = 255,
      value = 0,
      value_step = 1,
      discrete_values = true,
      tags = { dragon_action = "slider", channel = channel },
    })
    slider.style.width = 200
    rgb.add({
      type = "textfield",
      name = "dragon_text_" .. channel,
      style = "dragon_character_value_textfield",
      numeric = true,
      allow_decimal = false,
      allow_negative = false,
      lose_focus_on_confirm = true,
      tags = { dragon_action = "channel_text", channel = channel },
    })
  end
  local hex_flow = color_section.add({ type = "flow", direction = "horizontal" })
  hex_flow.style.vertical_align = "center"
  hex_flow.style.horizontal_spacing = 8
  hex_flow.add({ type = "label", caption = { "dragon-character.hex" } })
  local hex = hex_flow.add({
    type = "textfield",
    name = "dragon_hex",
    lose_focus_on_confirm = true,
    tooltip = { "dragon-character.hex-tooltip" },
    tags = { dragon_action = "hex" },
  })
  hex.style.width = 90

  -- body shape
  local body_section = section("dragon_body_section")
  body_section.add({ type = "line" })
  section_label(body_section, { "dragon-character.body" })
  local bodies = body_section.add({ type = "flow", direction = "horizontal" })
  bodies.style.horizontal_spacing = 24
  for _, body in ipairs({ "male", "female" }) do
    local column = bodies.add({ type = "flow", direction = "vertical" })
    column.style.horizontal_align = "center"
    column.add({
      type = "sprite-button",
      name = "dragon_body_button_" .. body,
      style = "dragon_character_body_button",
      sprite = "dragon-character-body-" .. body,
      tooltip = { "dragon-character.body-" .. body },
      tags = { dragon_action = "body", body = body },
    })
    column.add({
      type = "radiobutton",
      name = "dragon_body_radio_" .. body,
      caption = { "dragon-character.body-" .. body },
      state = false,
      tags = { dragon_action = "body", body = body },
    })
  end

  -- outfits
  local outfit_section = section("dragon_outfit_section")
  outfit_section.add({ type = "line" })
  section_label(outfit_section, { "dragon-character.outfits" })
  local outfits = outfit_section.add({ type = "flow", direction = "horizontal" })
  outfits.style.horizontal_spacing = 24
  for _, outfit in ipairs({ "hoodie", "shorts" }) do
    outfits.add({
      type = "checkbox",
      name = "dragon_outfit_" .. outfit,
      caption = { "dragon-character." .. outfit },
      state = false,
      tags = { dragon_action = "outfit", outfit = outfit },
    })
  end

  local status = options.add({ type = "label", name = "dragon_status" })
  status.style.single_line = false
  status.style.maximal_width = 330
  status.style.font_color = { 1, 0.75, 0.3 }

  -- bottom buttons
  local buttons = frame.add({ type = "flow", style = "dialog_buttons_horizontal_flow" })
  buttons.add({
    type = "button",
    style = "back_button",
    caption = { "dragon-character.reset" },
    tooltip = { "dragon-character.reset-tooltip" },
    tags = { dragon_action = "reset" },
  })
  local filler = buttons.add({ type = "empty-widget", style = "draggable_space" })
  filler.style.horizontally_stretchable = true
  filler.style.height = 32
  filler.drag_target = frame
  buttons.add({
    type = "button",
    style = "confirm_button",
    caption = { "dragon-character.done" },
    tags = { dragon_action = "close" },
  })

  player.opened = frame
  player.set_shortcut_toggled(gui.SHORTCUT, true)
  gui.refresh(player, prefs)
  gui.refresh_color(player)
end

-- Updates the color widgets. `skip` is the element the change came from, so
-- the text the player is typing is not overwritten.
function gui.refresh_color(player, skip)
  local frame = gui.get_frame(player)
  if not frame then
    return
  end
  local color = player.color
  for _, channel in ipairs(CHANNELS) do
    local value = to255(color[channel])
    local slider = find(frame, "dragon_slider_" .. channel)
    if slider and slider ~= skip then
      slider.slider_value = value
    end
    local text = find(frame, "dragon_text_" .. channel)
    if text and text ~= skip then
      text.text = tostring(value)
    end
  end
  local hex = find(frame, "dragon_hex")
  if hex and hex ~= skip then
    hex.text = gui.hex_of(color)
  end
  local grid = find(frame, "dragon_swatches")
  if grid then
    for _, swatch in ipairs(swatches) do
      local button = grid["dragon_swatch_" .. swatch.name]
      if button then
        local c = swatch.color
        button.toggled = to255(c.r) == to255(color.r) and to255(c.g) == to255(color.g)
          and to255(c.b) == to255(color.b)
      end
    end
  end
end

function gui.refresh(player, prefs)
  local frame = gui.get_frame(player)
  if not frame then
    return
  end
  local camera = find(frame, "dragon_preview")
  if camera then
    local character = player.character
    if character and character.valid then
      camera.entity = character
    else
      camera.entity = nil
      camera.position = player.position
      camera.surface_index = player.surface.index
    end
  end
  -- an avatar pack that is no longer installed shows (and counts as) the dragon
  local pack = packs.get(prefs.avatar)
  local is_dragon = pack == nil
  local looks = find(frame, "dragon_looks")
  if looks then
    for _, button in pairs(looks.children) do
      local id = button.tags.avatar
      button.toggled = (is_dragon and id == "") or (pack ~= nil and id == pack.id)
    end
  end
  local sections = {
    dragon_color_section = is_dragon or pack.tintable,
    dragon_body_section = is_dragon,
    dragon_outfit_section = is_dragon,
  }
  for name, visible in pairs(sections) do
    local flow = find(frame, name)
    if flow then
      flow.visible = visible
    end
  end
  for _, body in ipairs({ "male", "female" }) do
    local button = find(frame, "dragon_body_button_" .. body)
    if button then
      button.toggled = prefs.body == body
    end
    local radio = find(frame, "dragon_body_radio_" .. body)
    if radio then
      radio.state = prefs.body == body
    end
  end
  for _, outfit in ipairs({ "hoodie", "shorts" }) do
    local box = find(frame, "dragon_outfit_" .. outfit)
    if box then
      box.state = prefs[outfit] and true or false
    end
  end
  local status = find(frame, "dragon_status")
  if status then
    if prefs.status == "avatar-missing" then
      status.caption = { "dragon-character.status-avatar-missing", prefs.avatar or "" }
    elseif prefs.status then
      status.caption = { "dragon-character.status-" .. prefs.status }
    else
      status.caption = ""
    end
  end
  -- the color widgets are only refreshed when the color changes, so this
  -- periodic refresh never overwrites what the player is typing
end

return gui

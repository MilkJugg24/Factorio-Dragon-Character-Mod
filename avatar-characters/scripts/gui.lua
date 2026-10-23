-- The avatar window and the optional top-left button.

local mod_gui = require("mod-gui")
local packs = require("scripts.packs")

local gui = {}

gui.FRAME = "avatar_characters_frame"
gui.TOP_BUTTON = "avatar_characters_top_button"
gui.SHORTCUT = "avatar-characters-choose"

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

function gui.update_top_button(player)
  local show = player.mod_settings["avatar-characters-show-top-button"].value
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
      sprite = "avatar-characters-icon",
      tooltip = { "avatar-characters.top-button-tooltip" },
      tags = { avatar_action = "toggle" },
    })
  elseif button and not show then
    button.destroy()
  end
  -- drop the shared mod-gui frames once no mod has a button left in them
  if not show and flow.name == "mod_gui_inner_frame" and #flow.children == 0 then
    flow.parent.destroy()
  end
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
    caption = { "avatar-characters.window-title" },
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
    tooltip = { "avatar-characters.close" },
    tags = { avatar_action = "close" },
  })

  -- frames have no cell spacing of their own, so lay the content out in a flow
  local content_frame = frame.add({ type = "frame", style = "inside_shallow_frame_with_padding", direction = "horizontal" })
  local content = content_frame.add({ type = "flow", direction = "horizontal" })
  content.style.horizontal_spacing = 12

  -- live preview of the character
  local preview_frame = content.add({ type = "frame", style = "deep_frame_in_shallow_frame", direction = "vertical" })
  local camera = preview_frame.add({
    type = "camera",
    name = "avatar_preview",
    position = player.position,
    surface_index = player.surface.index,
    zoom = 1.6,
  })
  camera.style.width = 230
  camera.style.height = 300

  local options = content.add({ type = "flow", direction = "vertical" })
  options.style.vertical_spacing = 6
  options.style.width = 330

  -- the engineer and every installed avatar pack
  options.add({ type = "label", style = "caption_label", caption = { "avatar-characters.look" } })
  local pane = options.add({ type = "scroll-pane", horizontal_scroll_policy = "never" })
  pane.style.maximal_height = 248
  local looks = pane.add({ type = "table", name = "avatar_looks", column_count = 4 })
  looks.style.horizontal_spacing = 4
  looks.style.vertical_spacing = 4
  looks.add({
    type = "sprite-button",
    name = "avatar_look_",
    style = "avatar_characters_look_button",
    sprite = "entity/character",
    tooltip = { "avatar-characters.look-engineer" },
    tags = { avatar_action = "look", avatar = "" },
  })
  local installed = packs.list()
  for _, pack in ipairs(installed) do
    local tooltip = pack.title
    if pack.author and pack.author ~= "" then
      tooltip = { "avatar-characters.look-author", pack.title, pack.author }
    end
    looks.add({
      type = "sprite-button",
      name = "avatar_look_" .. pack.id,
      style = "avatar_characters_look_button",
      sprite = pack.sprite,
      tooltip = tooltip,
      tags = { avatar_action = "look", avatar = pack.id },
    })
  end
  if #installed == 0 then
    local none = options.add({ type = "label", caption = { "avatar-characters.no-packs-installed" } })
    none.style.single_line = false
    none.style.maximal_width = 330
  end

  local chosen = options.add({ type = "label", name = "avatar_chosen" })
  chosen.style.single_line = false
  chosen.style.maximal_width = 330

  local status = options.add({ type = "label", name = "avatar_status" })
  status.style.single_line = false
  status.style.maximal_width = 330
  status.style.font_color = { 1, 0.75, 0.3 }

  -- bottom buttons
  local buttons = frame.add({ type = "flow", style = "dialog_buttons_horizontal_flow" })
  local filler = buttons.add({ type = "empty-widget", style = "draggable_space" })
  filler.style.horizontally_stretchable = true
  filler.style.height = 32
  filler.drag_target = frame
  buttons.add({
    type = "button",
    style = "confirm_button",
    caption = { "avatar-characters.done" },
    tags = { avatar_action = "close" },
  })

  player.opened = frame
  player.set_shortcut_toggled(gui.SHORTCUT, true)
  gui.refresh(player, prefs)
end

function gui.refresh(player, prefs)
  local frame = gui.get_frame(player)
  if not frame then
    return
  end
  local camera = find(frame, "avatar_preview")
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
  -- an avatar pack that is no longer installed shows (and counts as) the engineer
  local pack = packs.get(prefs.avatar)
  local looks = find(frame, "avatar_looks")
  if looks then
    for _, button in pairs(looks.children) do
      local id = button.tags.avatar
      button.toggled = (pack == nil and id == "") or (pack ~= nil and id == pack.id)
    end
  end
  local chosen = find(frame, "avatar_chosen")
  if chosen then
    if pack then
      chosen.caption = { "avatar-characters.chosen-pack", pack.title }
    else
      chosen.caption = { "avatar-characters.chosen-engineer" }
    end
  end
  local status = find(frame, "avatar_status")
  if status then
    if prefs.status == "avatar-missing" then
      status.caption = { "avatar-characters.status-avatar-missing", prefs.avatar or "" }
    elseif prefs.status then
      status.caption = { "avatar-characters.status-" .. prefs.status }
    else
      status.caption = ""
    end
  end
end

return gui

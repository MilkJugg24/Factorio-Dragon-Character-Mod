-- Shortcut, hotkey, GUI sprite and styles for the avatar window.

local MOD_PATH = "__avatar-characters__"

data:extend({
  {
    type = "custom-input",
    name = "avatar-characters-toggle-gui",
    key_sequence = "CONTROL + SHIFT + A",
    action = "lua",
    consuming = "none",
    order = "a",
  },
  {
    type = "shortcut",
    name = "avatar-characters-choose",
    order = "z[avatar-characters]",
    action = "lua",
    toggleable = true,
    associated_control_input = "avatar-characters-toggle-gui",
    icon = MOD_PATH .. "/graphics/icons/shortcut-x56.png",
    icon_size = 56,
    small_icon = MOD_PATH .. "/graphics/icons/shortcut-x24.png",
    small_icon_size = 24,
  },
  {
    type = "sprite",
    name = "avatar-characters-icon",
    filename = MOD_PATH .. "/graphics/icons/avatar-characters.png",
    size = 64,
    flags = { "gui-icon" },
  },
})

data.raw["gui-style"]["default"]["avatar_characters_look_button"] = {
  type = "button_style",
  parent = "slot_button",
  size = 72,
  padding = 4,
}

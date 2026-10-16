-- Shortcut, hotkey, GUI sprites and styles for the customization window.

local MOD_PATH = "__dragon-character__"

data:extend({
  {
    type = "custom-input",
    name = "dragon-character-toggle-gui",
    key_sequence = "CONTROL + SHIFT + D",
    action = "lua",
    consuming = "none",
    order = "a",
  },
  {
    type = "shortcut",
    name = "dragon-character-customize",
    order = "z[dragon-character]",
    action = "lua",
    toggleable = true,
    associated_control_input = "dragon-character-toggle-gui",
    icon = MOD_PATH .. "/graphics/icons/shortcut-x56.png",
    icon_size = 56,
    small_icon = MOD_PATH .. "/graphics/icons/shortcut-x24.png",
    small_icon_size = 24,
  },
  {
    type = "sprite",
    name = "dragon-character-icon",
    filename = MOD_PATH .. "/graphics/icons/dragon-character.png",
    size = 64,
    flags = { "gui-icon" },
  },
  {
    type = "sprite",
    name = "dragon-character-body-male",
    filename = MOD_PATH .. "/graphics/icons/body-male.png",
    size = 64,
    flags = { "gui-icon" },
  },
  {
    type = "sprite",
    name = "dragon-character-body-female",
    filename = MOD_PATH .. "/graphics/icons/body-female.png",
    size = 64,
    flags = { "gui-icon" },
  },
})

local swatches = require("scripts.swatches")
for _, swatch in ipairs(swatches) do
  data:extend({
    {
      type = "sprite",
      name = "dragon-character-swatch-" .. swatch.name,
      filename = MOD_PATH .. "/graphics/gui/swatch-" .. swatch.name .. ".png",
      size = 32,
      flags = { "gui-icon" },
    },
  })
end

local styles = data.raw["gui-style"]["default"]

styles["dragon_character_swatch_button"] = {
  type = "button_style",
  parent = "slot_button",
  size = 32,
  padding = 2,
}

styles["dragon_character_body_button"] = {
  type = "button_style",
  parent = "slot_button",
  size = 72,
  padding = 4,
}

styles["dragon_character_value_textfield"] = {
  type = "textbox_style",
  parent = "slider_value_textfield",
  width = 48,
}

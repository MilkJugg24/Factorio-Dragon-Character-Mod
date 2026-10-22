-- An untouched copy of the vanilla character, so the screenshots can show the engineer next to the
-- dragon (dragon-character turns "character" itself into the dragon, in data-final-fixes).
local engineer = table.deepcopy(data.raw["character"]["character"])
engineer.name = "screenshot-engineer"
engineer.hidden = true
data:extend({ engineer })

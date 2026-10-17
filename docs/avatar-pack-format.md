# Avatar pack format (version 1)

An **avatar pack** is a small Factorio mod that adds one extra look to the
Dragon Character mod (`dragon-character` 0.2.0 or newer). The pack only ships
sprite sheets and a description of them. `dragon-character` reads that
description, creates the character and corpse prototypes, and offers the look
in its customization window.

A Unity avatar exporter that writes packs automatically is in development.
Anything that follows this document works, including hand-drawn packs. To
check a pack before loading it in the game:

```sh
python3 tools/validate_sprites.py --pack path/to/dragon-avatar-<id>_<version>.zip
```

`tools/avatar_pack/make_test_pack.py` writes a synthetic example pack.

## Names

| Thing | Name |
| --- | --- |
| Pack id | `<id>`: lowercase letters, digits and `-`; starts with a letter or digit; at most 35 characters; must not start with `corpse-` |
| Mod name (`info.json`) | `dragon-avatar-<id>` |
| Zip file | `dragon-avatar-<id>_<version>.zip`, containing one folder `dragon-avatar-<id>_<version>/` (or `dragon-avatar-<id>/`) |
| mod-data prototype | `dragon-avatar-<id>`, with `data_type = "dragon-character.avatar-pack"` |
| Character prototype (created by dragon-character) | `dragon-avatar-<id>` |
| Corpse prototype (created by dragon-character) | `dragon-avatar-corpse-<id>` |
| GUI sprite (created by dragon-character from `icon`) | `dragon-character-avatar-<id>` |

## Files

```
dragon-avatar-<id>_<version>/
  info.json
  data.lua
  prototypes/avatar-pack.lua
  locale/en/locale.cfg
  thumbnail.png                  144 x 144
  graphics/icon.png              64 x 64, shown in the customization window
  graphics/<animation>-<layer>.png
  graphics/<animation>-<layer>-<n>.png   (when a sheet is split, n = 1, 2, ...)
  README.txt                     source avatar, its author, permission to use it
```

`info.json`:

```json
{
  "name": "dragon-avatar-<id>",
  "version": "1.0.0",
  "title": "Dragon Character avatar: <title>",
  "author": "<author>",
  "factorio_version": "2.0",
  "description": "...",
  "dependencies": ["dragon-character >= 0.2.0"]
}
```

The `info.json` title must be at most **100 bytes** of UTF-8 (about 33 Japanese
characters): Factorio 2.0 stops with a crash, without an error message, when a
mod's title is longer. Shorten it there if needed; the pack's own `title` below
can still be up to 80 characters.

`data.lua` is always exactly:

```lua
local pack = require("prototypes.avatar-pack")
data:extend({
  {
    type = "mod-data",
    name = "dragon-avatar-" .. pack.id,
    data_type = "dragon-character.avatar-pack",
    data = pack,
  },
})
```

## `prototypes/avatar-pack.lua`

A plain Lua table literal (no code), so tools can read it.

```lua
return {
  format_version = 1,
  id = "mika",
  title = "Mika",                -- shown in the customization window, at most 80 characters
  author = "someone",            -- optional
  generator = { name = "factorio-avatar-exporter", version = "1.0.0" },  -- optional
  icon = { filename = "__dragon-avatar-mika__/graphics/icon.png", size = 64 },
  pixels_per_tile = 64,          -- 64 (sprites drawn at scale 0.5) or 32 (scale 1)
  tintable = false,              -- true: every animation has a "mask" layer tinted with the player colour
  flight = true,                 -- true: the flight animations below are all present
  timing = {                     -- optional, every field optional (defaults below)
    distance_per_frame = 0.119,
    mine_impact_frame = 9,
    left_foot_frames = { 1 },
    right_foot_frames = { 7 },
    animation_speed = { idle = 0.06 },  -- per animation, see the table below
  },
  animations = {
    idle = {
      direction_count = 8,
      frame_count = 8,
      layers = {
        base = {
          filename = "__dragon-avatar-mika__/graphics/idle-base.png",
          width = 92, height = 150, shift = { 0.0, -1.03125 },
        },
        shadow = {
          filename = "__dragon-avatar-mika__/graphics/idle-shadow.png",
          width = 140, height = 70, shift = { 0.6, 0.05 },
        },
      },
    },
    ["run-gun"] = {
      direction_count = 18,
      frame_count = 12,
      layers = {
        base = {
          filenames = {
            "__dragon-avatar-mika__/graphics/run-gun-base-1.png",
            "__dragon-avatar-mika__/graphics/run-gun-base-2.png",
          },
          lines_per_file = 13,
          width = 110, height = 300, shift = { 0.0, -1.2 },
        },
        shadow = { ... },
        ["shadow-flipped"] = { ... },   -- optional, see "Gun animations"
      },
    },
    -- run, idle-gun, mine, corpse, and with flight = true: hover, hover-gun, fly, fly-gun, take-off
  },
}
```

## Animations

| Name | Directions | Default speed | Used for |
| --- | --- | --- | --- |
| `idle` | 8 | 0.06 | standing |
| `idle-gun` | 8 | 0.06 | standing with a gun |
| `run` | 8 | 0.6 | running |
| `run-gun` | 18 | 0.6 | running with a gun (see below) |
| `mine` | 8 | 0.415 | mining with a pickaxe |
| `corpse` | 1 | 1 | the corpse (1 frame recommended) |
| `hover` | 8 | 0.27 | flying in place (mech armor) |
| `hover-gun` | 8 | 0.27 | flying in place with a gun |
| `fly` | 8 | 0.3 | flying |
| `fly-gun` | 18 | 0.3 | flying with a gun (see below) |
| `take-off` | 8 | 0.3 | taking off; landing plays it backwards |

- `idle`, `idle-gun`, `run`, `run-gun`, `mine` and `corpse` are required.
- The flight animations are all present (`flight = true`) or all absent. A pack
  without them still works with flying armor: the ground animations are used
  in the air.
- The frame count of each animation is up to the pack, from 1 to 255
  (Factorio's limit). Looping animations are
  sampled at `t = frame / frame_count`; `take-off` at `t = frame / (frame_count - 1)`
  so that its last frame is the first hover frame.
- `timing.mine_impact_frame` (1-based) is when the mining sound and particles
  play. `left_foot_frames` / `right_foot_frames` are the running frames where a
  foot lands. `distance_per_frame` is how far the character moves per running
  frame; it should match the stride so feet don't slide.
- Timing fields a pack leaves out get these defaults, which scale with its own
  frame counts (for 12-frame `run` and `mine` animations, like the dragon's,
  they are 9, `{ 1 }` and `{ 7 }`):

  | Field | Default |
  | --- | --- |
  | `distance_per_frame` | 0.119 |
  | `mine_impact_frame` | `max(1, floor(mine frames x 9 / 12 + 0.5))` |
  | `left_foot_frames` | `{ 1 }` |
  | `right_foot_frames` | `{ floor(run frames / 2) + 1 }` |
  | `animation_speed` | the "Default speed" column above |

### Gun animations (`run-gun`, `fly-gun`)

These 18 rows are not 18 evenly spaced facings. As in the vanilla engineer's
sheet, each row is a pair *(gun direction, moving direction)* for guns aiming
into the eastern half. The engine mirrors the sprite for guns aiming west, and
plays a row backwards when running away from the gun. Directions are in eighths
of a turn clockwise from north:

| Row | Gun | Moving | Row | Gun | Moving | Row | Gun | Moving |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 0 | 0 | 7 | 1 | 3 | 13 | 3 | 2 |
| 2 | 0 | 1 | 8 | 2 | 0 | 14 | 3 | 3 |
| 3 | 0 | 2 | 9 | 2 | 1 | 15 | 3 | 4 |
| 4 | 1 | 0 | 10 | 2 | 2 | 16 | 4 | 2 |
| 5 | 1 | 1 | 11 | 2 | 3 | 17 | 4 | 3 |
| 6 | 1 | 2 | 12 | 3 | 1 | 18 | 4 | 4 |

The torso, head and gun face the gun direction; the hips and legs turn toward
the moving direction.

`run-gun` may have a `shadow-flipped` layer: the shadow of the *mirrored*
sprite, still cast to the east. The engine draws it instead of `shadow` while
it mirrors the sprite. Without it, the shadow of a west-aiming character falls
to the west.

## Layers

| Layer | Required | Drawn | Content |
| --- | --- | --- | --- |
| `base` | yes | normally | the character, straight-alpha RGBA |
| `mask` | when `tintable` | with `apply_runtime_tint` over `base` | greyscale shading of the parts that take the player colour |
| `shadow` | yes | with `draw_as_shadow` | the ground shadow: black, alpha = shadow strength |
| `shadow-flipped` | no (`run-gun` only) | with `draw_as_shadow` | see above |

## Sheets

- One row per direction (north first, then clockwise), one column per frame.
- Every frame in a sheet has the same `width` and `height`; crop them to the
  union of all frames of that animation and layer.
- `shift` is the offset of the frame centre from the entity position, in tiles:
  `(frame centre - anchor) / pixels_per_tile`, where the anchor is the ground
  point under the character.
- No file may be larger than 4096 px in either direction. A single row
  (`frame_count x width`) must fit in 4096 px.
- When `direction_count x height` is more than 4096 px, split the sheet by
  rows: use `filenames` instead of `filename` and set
  `lines_per_file = floor(4096 / height)`. File *k* holds rows
  `(k-1) x lines_per_file` up to the next file's first row; the last file may
  be shorter. `#filenames` must be `ceil(direction_count / lines_per_file)`.
- PNG, RGBA, 8 bits per channel, straight (not premultiplied) alpha, sRGB.

## Rendering conventions

Packs look right next to the dragon and vanilla entities when rendered the same
way as `tools/dragon_render`:

- **World:** x east, y south, z up, in tiles.
- **Camera:** orthographic, looking north and 45 degrees down. Screen-space
  vertical is stretched by 1.25. 64 pixels per tile (32 for low-resolution
  packs).
- **Lights:** shading light from `(-0.70, 0.35, 0.90)` (normalised; west,
  slightly south, above). The ground shadow is a projection onto the ground
  along `(-1.0, -0.12, 1.6)` (normalised), so shadows fall east like vanilla
  shadows.
- **Size:** a standing character is about 1.5 tiles tall on screen. In the
  flight animations the body is raised 0.42 tiles off the ground.

## Validation

`dragon-character` refuses to load (with an error naming the pack and the
problem) when a pack:

- has a `format_version` it does not support, or no `format_version`;
- has an invalid `id`, or a mod-data name or mod name that does not match it;
- is missing a required animation or layer, or has only some of the flight
  animations, or `tintable = true` without a `mask` everywhere;
- has the wrong `direction_count` for an animation;
- has a non-integer or out-of-range `frame_count` (1 to 255), `width` or
  `height`, or a `shift` that is not two numbers;
- has a `title` that is empty or longer than 80 characters;
- points `filename` / `filenames` outside its own mod, or has a `filenames`
  count that does not match `lines_per_file`;
- has `timing` values out of range.

Refusing to load is deliberate: skipping a broken pack would make Factorio
delete every character using it, together with the items they carry.

## Removing a pack

Characters and corpses using a pack's look stop existing when the pack is
removed, and Factorio deletes them **with their items** when the save loads.
Before removing a pack from a save, an admin runs `/dragon-revert-all <id>`,
which turns everyone using it back into the dragon.

## Versioning

New optional fields do not change `format_version`. A change in meaning bumps
`format_version` and the minimum `dragon-character` version in `info.json`, so
an old `dragon-character` reports the missing dependency instead of
misreading the pack.

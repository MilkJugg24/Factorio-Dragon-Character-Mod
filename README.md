# Factorio Avatar Exporter

Play Factorio 2.1 as your own avatar. Two parts work together:

- **The Factorio Avatar Exporter**, a Unity package. It takes a humanoid avatar (a VRChat avatar, for
  example) in your Unity project, poses it in every animation the game needs (idle, running, shooting,
  mining, flying with mech armor, the corpse) and renders the sprites, lit like the game's characters or
  with lights you set yourself. Its output is an **avatar pack**: a small Factorio mod named
  `avatar-pack-<name>`.
- **Avatar Characters**, a Factorio mod (`avatar-characters/`). It turns the installed avatar packs into
  characters. Every player picks their own look in the avatar window, or stays the engineer.

Work in progress: the exporter poses your avatar in every animation and writes the pack (wings flap and
stretch, tails sway), but fingers don't grip yet. See the
[exporter's README](unity/Packages/com.milkjugg24.factorio-avatar-exporter/README.md).

The newest release on the [Releases](../../releases) page has both downloads side by side: the
`.unitypackage` to import into Unity, and the `avatar-characters_<version>.zip` mod every player needs.

This project began as the Dragon Character mod, a dragon that replaced the engineer. That mod is archived
on the [`archive/dragon-character`](../../tree/archive/dragon-character) branch.

## Avatar Characters (the mod)

- Open the avatar window from the shortcut bar, the button in the top-left corner, **Ctrl + Shift + A**,
  or `/avatar`. It shows your character and one button per installed pack, plus the engineer.
- Your choice is kept in the save. If you're in a vehicle, crafting, or similar, your new look is
  applied as soon as you're done.
- On its own the mod changes nothing: everyone is the engineer until packs are installed.
- With Space Age, packs that have flight animations fly with mech armor; packs without them show their
  ground animations in the air.

### Installing

Put the mod zip and the pack zips, still zipped, into Factorio's `mods` folder:

- Windows: `%APPDATA%\Factorio\mods`
- Linux: `~/.factorio/mods`
- macOS: `~/Library/Application Support/factorio/mods`

### Multiplayer

Factorio can't load graphics while a game runs, so every character is in a mod, and **every player needs
the same mods, the same versions, as the server**. The game refuses to join otherwise. It downloads
missing mods by itself only when they're on the official mod portal.

For a private server, each player exports their own pack and sends it to the server owner, who combines
them with **Combine Packs for a Server** in the exporter into one **avatar bundle**
(`avatar-bundle-<name>_<version>.zip`). Everyone installs that one zip next to Avatar Characters; each
player picks their own character, and everyone sees everyone else's choice. A character keeps its name
whether it comes as a pack or in a bundle, so saves keep their characters when the bundle grows or a
server switches from separate packs. The exporter's README has the steps.

Each character costs video memory on every player's computer (the validator below prints how much). The
game compresses sprites, so it's about a tenth of their uncompressed size: in a test, a bundle of 12
characters at 64 pixels per tile added about 120 MB of video memory and a second of loading time, with no
effect on the frame rate. Only very large servers need 32-pixel packs.

### Removing a pack or the mod

A pack's characters and corpses stop existing when the pack is removed, and Factorio deletes them **with
their items** when the save loads. Before removing anything:

1. Make sure every player who uses it is online, alive and not in a vehicle.
2. Run `/avatar-revert-all <id>` to turn everyone using that pack back into the engineer (`<id>` is the
   part of the mod name after `avatar-pack-`; the mod name works too). For a bundle, do this for each of
   its characters that goes. Use `/avatar-revert-all` alone
   before removing Avatar Characters itself. Admins only. It reports anything it couldn't convert.
3. Save, then disable the pack or the mod.

If a pack goes missing anyway, players who chose it are shown as the engineer, and get their look back
when it's installed again.

### Licensing

Many avatars may only be used in VRChat. Only share a pack made from someone else's avatar if its
author allows it.

### Compatibility notes

- The vanilla `character` prototype stays the engineer. Each pack, or each character of a bundle, adds
  `avatar-pack-<id>` character and `avatar-pack-corpse-<id>` corpse prototypes, copied from `character` in `data-final-fixes`, so they
  inherit changes other mods make to it.
- If another mod temporarily swaps your character (a jetpack, for example), this mod leaves it alone and
  reapplies your look once you're back to a normal character.
- Remote interface `avatar-characters`:
  - `get_appearance(player_index)` returns `{avatar = pack id or nil}`.
  - `set_appearance(player_index, {avatar = id or false})` (`false` is the engineer). It returns:
    - `nil` when applied;
    - `"avatar-missing"` when applied, but the pack isn't installed so the engineer is shown;
    - the reason the change has to wait, such as `"vehicle"`;
    - `"unknown-avatar"` or `"no-player"` when nothing was changed.
  - `get_avatar_packs()`, `is_avatar_character(name)` and `open_window(player_index)`.
  - `revert_all(pack_id)`, which returns `nil` when `pack_id` names no pack.
  - `get_on_character_swapped_event()`. That event is raised whenever the mod replaces a player's
    character entity, with `old_character` and `new_character`.

## Development

### Repository layout

| Path | What it is |
| --- | --- |
| `avatar-characters/` | The Factorio mod. |
| `unity/Packages/com.milkjugg24.factorio-avatar-exporter/` | The Unity package: `Core/` (Unity-free, tested here) and `Editor/`. |
| `unity/dotnet/` | Builds and tests Core outside Unity, the pack harness, and a compile check of `Editor/` against Unity's API. |
| `docs/avatar-pack-format.md` | What an avatar pack contains, for anyone making one by hand. |
| `tools/avatar_pack/` | The pack format in Python: checker, synthetic test packs, preview GIFs. |
| `tools/validate_sprites.py` | Checks avatar packs: the format and every sheet's size. |
| `tools/make_unitypackage.py`, `tools/build_mod.sh` | Build the `.unitypackage` and the mod zip into `build/`. |
| `tests/` | Headless and in-game Factorio tests, the Unity test runner. |

### Testing

`tests/run_headless.sh` loads the mod in a headless Factorio server, which validates every prototype.
It then runs `tests/avatar-characters-test`. That test swaps a fully equipped character into each pack
and back, and checks that nothing was lost: power armor with an equipment grid, a full inventory
including the armor's bonus slots, guns, ammo, trash, filters, logistic requests and bonuses.

```sh
tests/run_headless.sh /path/to/factorio               # base game
tests/run_headless.sh /path/to/factorio --space-age   # with Space Age, Quality, Elevated Rails
```

Options for avatar packs (combine them freely):

| Option | What it does |
| --- | --- |
| `--test-packs` | Adds two synthetic packs. Checks their prototypes, swapping into them and `/avatar-revert-all <id>`, then loads the save again without the packs. |
| `--broken-packs` | Checks that each deliberately broken pack stops the game from loading with the expected error, and that `tools/validate_sprites.py --pack` rejects it too. |
| `--pack PATH` | Adds a pack (folder or zip) and runs the generic pack checks on it. |

The exporter's Unity-free Core has NUnit tests. There is also an end-to-end check that builds a pack
from a synthetic rig and loads it in Factorio; it needs the .NET 8 SDK:

```sh
dotnet test unity/dotnet/Core.Tests
tests/run_avatar_pipeline.sh /path/to/factorio [--space-age]
```

The Python tools have their own unit tests, which need no Factorio:

```sh
python3 -m unittest discover -s tests -p "test_*.py"
```

The synthetic packs come from `tools/avatar_pack/make_test_pack.py`. Every frame is a labelled stand-in
figure, so a pack can also be tried in the real game:

```sh
python3 tools/avatar_pack/make_test_pack.py --out build/packs --flight --tint --zip
python3 tools/validate_sprites.py --pack build/packs/avatar-pack-test_1.0.0.zip
```

The headless server is a free download from factorio.com. It can't connect a player, so the avatar
window and the sprites themselves are checked in the real game. `tests/INGAME-CHECKS.md` covers that,
and `tests/UNITY-SPIKE.md` covers the exporter in Unity.

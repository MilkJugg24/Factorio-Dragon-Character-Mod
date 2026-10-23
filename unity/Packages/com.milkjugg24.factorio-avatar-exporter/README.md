# Factorio Avatar Exporter

Play Factorio 2.0 as your own avatar. This Unity tool turns a humanoid avatar (a VRChat avatar, for
example) into an **avatar pack**: a small Factorio mod with your avatar idling, running, shooting, mining,
flying with mech armor and lying as a corpse, in every direction, with its shadow. In the game, the
**Avatar Characters** mod lets every player pick their own look.

Downloads and source: https://github.com/MilkJugg24/Factorio-VRChat-Avatar-Exporter (see **Releases**).

## Step by step

### 1. What you need

- Unity 2022.3 with a project that has your avatar in it (your VRChat avatar project works).
- Factorio 2.0. Space Age is optional; it adds flying with mech armor.
- From the newest release on the Releases page, both downloads:
  - `FactorioAvatarExporter_<version>.unitypackage` (this tool);
  - `avatar-characters_<version>.zip` (the Factorio mod every player needs).

### 2. Install the exporter

In Unity choose **Assets ▸ Import Package ▸ Custom Package...**, pick the `.unitypackage` and click
**Import**. It lands in `Assets/FactorioAvatarExporter` (where this README is). To update it later, import
a newer `.unitypackage` the same way.

### 3. Export your avatar

1. Choose **Tools ▸ Factorio Avatar Exporter ▸ Exporter**.
2. Drag your avatar into the **Avatar** field: the object with the VRChat avatar descriptor, from the
   Hierarchy, or its prefab. Press **Load avatar**. The exporter works on a copy in a hidden scene; your
   scene and your avatar are not changed.
3. Look it over in the preview: pick an animation, a direction and a frame, or press **Play**. The
   yellow cross is where your character stands in the game.
4. Under **Avatar pack**, fill in:
   - **Name**: shown in the game. The **Pack id** is made from it; keep it the same when you export a
     new version later.
   - **Author** (optional) and **Version** (start with 1.0.0).
   - **Resolution**: 64 pixels per tile is sharp. 32 uses a quarter of the video memory, which is worth it
     on a server with many avatars.
   - **Flight animations**: on, unless you never fly with mech armor.
   - Tick **I made this avatar, or its author allows me to use it this way**, and if you like, add a note
     (the avatar's license, or who gave permission). It goes into the pack's README.
5. Press **Export All** and choose where to save the pack. Keep its file name,
   `avatar-pack-<id>_<version>.zip`: Factorio expects a mod's zip to be named that way. Then the exporter
   renders every animation, which takes a few minutes; you can cancel from the progress bar.

Next to your project's `Assets` folder, `FactorioAvatarExport/<avatar>/preview/` also gets `report.txt`
(what the exporter found and did, and how much video memory the pack needs) and a preview image of every
animation.

### 4. Install it in Factorio

Put two files, **still zipped** (don't unzip them), into Factorio's `mods` folder:

- your `avatar-pack-<id>_<version>.zip`;
- `avatar-characters_<version>.zip` from the release.

The `mods` folder is:

- Windows: `%APPDATA%\Factorio\mods` (paste that into the address bar of a File Explorer window);
- macOS: `~/Library/Application Support/factorio/mods`;
- Linux: `~/.factorio/mods`.

### 5. Play as your avatar

Start Factorio (restart it if it was running). New mods are switched on by default; you can check under
**Mods** in the main menu. In a game, open the avatar window with the button in the shortcut bar, the
button in the top-left corner, **Ctrl + Shift + A**, or the chat command `/avatar`, and click your avatar.
Your choice is saved with the game. If you're in a vehicle or crafting, your new look is applied as soon
as you're done.

## Playing together

Factorio can't load new pictures while a game runs, so every pack is a mod, and **every player needs
exactly the same mods as the server**, the same versions too. The game won't let anyone join otherwise.
Collect the Avatar Characters zip and every player's pack into one folder, share it (a zip of it on
Discord, for example), and have everyone put the files into their `mods` folder.

Each pack uses video memory on every player's computer; `report.txt` says how much. With many avatars,
export them at 32 pixels per tile.

## Updating your avatar

Raise the **Version** (1.0.1, 1.1.0, ...), keep the **Pack id**, press **Export All**, and replace the old
zip in the `mods` folder with the new one (delete the old file). On a server, everyone needs the new
version.

## Removing a pack (or Avatar Characters) from a save

When a pack is removed, Factorio deletes the characters and corpses that use it **together with the items
they carry**. Before removing one, an admin types `/avatar-revert-all <id>` in the chat (`<id>` is the
pack id), which turns everyone using it back into the engineer. Before removing Avatar Characters itself,
type `/avatar-revert-all`. Everyone should be online, alive and out of vehicles; the command reports
anything it couldn't change. Then save, and remove the mod.

## Permission

Many avatars may only be used in VRChat. Only make a pack of an avatar you made, or whose author allows
it, and only share the pack with others if the author allows that too.

## If something goes wrong

- **"has no humanoid rig":** the avatar's model needs **Animation Type: Humanoid** in its import
  settings (Rig tab), as VRChat avatars have.
- **Part of the outfit floats away or stays behind:** outfits added with VRCFury's Armature Link or Modular
  Avatar's Merge Armature, and objects held on by constraints, are joined to the body only in Play mode,
  so the exporter holds them on itself:
  - an object a parent constraint held follows the bone it was held to;
  - a part's bones follow the body's bones of the same name, also with a prefix or suffix
    (`Chest_Hoodie` follows `Chest`);
  - anything else follows the nearest bone of the trunk (hips, spine, chest, head).

  The report lists every part and what it follows; please report the line of a part that still floats.
- **Pose check over 1 px** (shown under the preview): the posing doesn't fit your rig exactly; please
  report it with `report.txt`.
- **Pink or light-blue patches:** a shader is missing or wasn't compiled yet. Make sure the avatar looks
  right in the Scene view first, and try again.
- **The pack doesn't show up in Factorio:** check that both zips are in the `mods` folder, still zipped,
  with their original names, and that both mods are enabled under **Mods** in the main menu.

## What it doesn't do yet

- Fingers don't close around the gun and pickaxe, which are simple stand-in shapes.
- Wings, tails and ears stay in their rest pose in every animation.
- The player colour doesn't tint the avatar.

## More about the exporter window

- **Report** (under the Avatar field): height, how big it is drawn, leg style (plantigrade or
  digitigrade), optional bones it lacks, scripts removed from the copy, warnings about the rig, and which
  loose parts it held on to the body.
- **Pose check:** how far the avatar's bones ended up from where the exporter meant to put them, in
  pixels.
- **Export Selected Animation:** saves only the animation shown as one image (a row per direction, a
  column per frame) in `FactorioAvatarExport/<avatar>/preview/`.

## The rendering spike

**Tools ▸ Factorio Avatar Exporter ▸ Spike: Render Selected Avatar** is the first test of whether avatars
render cleanly for sprites: the selected avatar, as it stands, from Factorio's camera in 8 directions, into
`FactorioAvatarSpike/<avatar>/` next to `Assets`. `strip-on-grey.png` shows all 8 directions;
`report.txt` has sizes, colour space and the removed scripts. In the repository,
`tests/run_unity_spike.ps1` runs the spike, the poses or a whole pack export without opening the Editor
(see `tests/UNITY-SPIKE.md` there).

## For developers

- `Core/`: everything that decides what to draw and writes the pack. It has no Unity references and is
  developed and tested outside Unity (`unity/dotnet/` in the repository).
- `Editor/`: the Unity side: the window, the stage the avatar is posed on, the camera and the renderer.
- Instead of the `.unitypackage`, **Window ▸ Package Manager ▸ + ▸ Add package from disk...** on this
  folder's `package.json` works too. Use one way or the other, not both, or Unity reports the assemblies
  twice.

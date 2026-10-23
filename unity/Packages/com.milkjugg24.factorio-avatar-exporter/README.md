# Factorio Avatar Exporter (Unity package)

Turns a humanoid avatar (a VRChat avatar, for example) into an avatar pack for the Avatar Characters
Factorio mod, so you can play as your avatar (every player picks their own). Work in progress: this
version **previews your avatar posed in every animation** of an avatar pack. Writing the pack itself
comes next. It also still has the **rendering spike**, the first test of whether avatars render
cleanly for sprites.

- `Core/`: everything that decides what to draw and writes the pack. It has no Unity references and is
  developed and tested outside Unity (see `unity/dotnet/README.md`).
- `Editor/`: the Unity side.

## The exporter window (preview)

Open **Tools ▸ Factorio Avatar Exporter ▸ Exporter**, drag your avatar into the Avatar field (the object
with the VRChat avatar descriptor, or its prefab) and press **Load avatar**. A copy is made in a hidden
scene; your scene and avatar are not changed.

- **Report:** height, how big it is drawn, leg style (plantigrade or digitigrade), optional bones it
  lacks, scripts removed from the copy, warnings about the rig, and which loose parts it held on to the
  body (see below).
- **Preview:** choose an animation, a direction and a frame, or press **Play**. The yellow cross is where
  the character stands in the game. Every animation of a pack is there:
  - idle, run and mine, with or without a gun;
  - flight (hover, fly, take-off);
  - the corpse.
- **Pose check:** how far the avatar's bones ended up from where the exporter meant to put them, in
  pixels. Under 1 px means the posing works on your rig. If it says more, please report it.
- **Export All:** saves a sheet per animation (a row per direction, a column per frame) and
  `report.txt` with the report and each animation's pose check, in
  `FactorioAvatarExport/<avatar>/preview/` next to `Assets`. Handy for sending. **Export Selected
  Animation** saves only the animation shown.

Outfits and accessories with their own armature (joined to the body by VRCFury's Armature Link or Modular
Avatar's Merge Armature, which only do it in Play mode), and objects held on by constraints, would be left
behind when the copy's scripts are removed. The exporter holds them on itself:
- an object a parent constraint held follows the bone the constraint held it to;
- a part's bones follow the body's bones of the same name, also when the part adds a prefix or suffix to
  them (`Chest_Hoodie` follows `Chest`);
- a part with neither follows, as a whole, the nearest bone of the trunk (hips, spine, chest, head).

The report lists each part and what it follows. If something still floats away, please report its
line in the report.

What it doesn't do yet:
- fingers don't close around the gun or pickaxe, and the gun and pickaxe themselves aren't drawn;
- no ground shadows;
- wings, tails and ears stay in their rest pose in every animation;
- no pack output.

## Trying the rendering spike

You need Unity 2022.3 (the VRChat version) and a project with an avatar in it.

1. Download `FactorioAvatarExporter_<version>.unitypackage` from the newest release on the repository's
   Releases page (the mod zip next to it is for Factorio). In Unity choose **Assets ▸ Import Package ▸ Custom Package...**, pick the
   file and click **Import**. It is installed in `Assets/FactorioAvatarExporter`; importing a newer version
   updates it in place.

   (Developers can instead use **Window ▸ Package Manager ▸ + ▸ Add package from disk...** on this folder's
   `package.json`. Use one way or the other, not both, or Unity reports the assemblies twice.)
2. Select your avatar in the Hierarchy (the object with the VRChat avatar descriptor), or its prefab.
3. Choose **Tools ▸ Factorio Avatar Exporter ▸ Spike: Render Selected Avatar**.

Your open scene is not changed: the avatar is copied into a hidden preview scene, its scripts (VRChat
components, PhysBones and similar) are removed from the copy, and the copy is rendered from Factorio's
sprite camera in 8 directions, on black and on white. The renders are turned into transparent sprites.
The avatar stays in whatever pose it is in (usually a T-pose or A-pose); poses come later.

The output goes to `FactorioAvatarSpike/<avatar name>/`, next to your project's `Assets` folder:

| File | What it shows |
| --- | --- |
| `strip-on-grey.png` | All 8 directions side by side on grey. The main thing to look at. |
| `strip.png`, `direction-1.png` ... `direction-8.png` | The transparent sprites (north first, clockwise; 5 is south, facing you). |
| `south-on-black.png`, `south-on-white.png` | The raw renders the sprites were made from. |
| `report.txt` | Sizes, timing, colour space and which scripts were removed. |

What to look for, and please report:

- Does the avatar look like it does in the Scene view (colours, outlines, shading)?
- Are there dark or light fringes around the edges, or a fog or halo around the avatar?
- Is anything missing (hair, accessories, outfit parts) or cut off at the edges?
- Did Unity's Console show errors?

The most useful set is one avatar using Poiyomi, one using lilToon, and any plain humanoid. Sending back
`strip-on-grey.png` and `report.txt` for each (and screenshots of any Console errors) is enough.

Outfits added with Modular Avatar or VRCFury are only merged into the avatar in Play mode, so they may be
missing from the spike's renders. (The exporter window holds them on; the spike doesn't.)

The spike can also run without opening the Editor, for testing several avatars in one go:
`tests/run_unity_spike.ps1` in the repository does that (see `tests/UNITY-SPIKE.md` there).

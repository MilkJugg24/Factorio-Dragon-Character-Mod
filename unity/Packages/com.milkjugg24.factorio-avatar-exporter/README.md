# Factorio Avatar Exporter (Unity package)

Turns a humanoid avatar (a VRChat avatar, for example) into an avatar pack for the Dragon Character
Factorio mod. Work in progress: this version has only the **rendering spike**, a test of whether avatars
render cleanly for sprites before the full exporter window is built.

- `Core/`: everything that decides what to draw and writes the pack. It has no Unity references and is
  developed and tested outside Unity (see `unity/dotnet/README.md`).
- `Editor/`: the Unity side.

## Trying the rendering spike

You need Unity 2022.3 (the VRChat version) and a project with an avatar in it.

1. Add the package. In Unity, open **Window ▸ Package Manager**, click **+ ▸ Add package from disk...**
   and pick `unity/Packages/com.milkjugg24.factorio-avatar-exporter/package.json` from this repository.
   You can also copy the `com.milkjugg24.factorio-avatar-exporter` folder into your project's `Packages/`
   folder.
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
missing from the spike's renders. That is expected for now.

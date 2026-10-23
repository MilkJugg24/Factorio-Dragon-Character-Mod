# Testing the Unity exporter's rendering spike

The exporter's rendering spike photographs an avatar the way Factorio sprites are drawn, from the
sprite camera, in 8 directions, on black and on white. It turns the photos into transparent sprites.
It answers one question before the full exporter is built: do real avatar shaders (Poiyomi, lilToon,
VRChat's own) come out clean? See `unity/Packages/com.milkjugg24.factorio-avatar-exporter/README.md`
for what it does and what to look for.

You need a Unity 2022.3 project with avatars in it (a VRChat avatar project), opened at least once
in Unity, so that its shaders are imported.

## By hand

1. Download `FactorioAvatarExporter_<version>.unitypackage` from the newest release.
2. In Unity: **Assets ▸ Import Package ▸ Custom Package...**, pick it, **Import**.
3. Select the avatar in the Hierarchy and choose **Tools ▸ Factorio Avatar Exporter ▸ Spike: Render Selected Avatar**.
4. The results open in your file browser: `FactorioAvatarSpike/<avatar>/` next to `Assets`.

## Unattended (script)

With the project **closed** in Unity, from the repository folder in PowerShell:

```powershell
# every avatar at the top level of a scene
powershell -ExecutionPolicy Bypass -File tests\run_unity_spike.ps1 -Project "<project folder>" -Scene "Assets/<scene>.unity"
# or one prefab
powershell -ExecutionPolicy Bypass -File tests\run_unity_spike.ps1 -Project "<project folder>" -Avatar "Assets/<path>/<avatar>.prefab"
```

The script builds the `.unitypackage` from this repository (needs Python), imports it into the project
(it lands in `Assets/FactorioAvatarExporter`, the same as importing by hand) and has Unity render without
opening a window. It finds Unity through Unity Hub from the project's version, or takes `-Unity <Unity.exe>`.
Add `-SkipImport` if the package is already in the project. Add `-Mode poses` to render every animation
posed by the exporter instead of the spike's T-pose: one sheet per animation (`<avatar>/<animation>.png`, a
row per direction) and a `report.txt` with the rig report and, per animation, how far the bones ended up
from where the exporter meant to put them.

Results go to `unity-spike/<date-time>/` in the repository:

| File | What it is |
| --- | --- |
| `summary.txt` | Each avatar's report (size, canvas, colour space, removed scripts, time) or the error. |
| `<avatar>/strip-on-grey.png` | All 8 directions on grey: the main thing to look at. |
| `<avatar>/direction-1.png` ... `strip.png` | The transparent sprites. |
| `<avatar>/south-on-black.png`, `south-on-white.png` | The raw renders the sprites were made from. |
| `unity-import.log`, `unity-render.log` | Unity's logs; errors and exceptions are in here. |

## Sending the results to Claude

Commit the results folder to a branch named `unity-spike` and push it, not to `main`:

```powershell
git checkout -B unity-spike
git add unity-spike
git commit -m "Unity spike results"
git push -u origin unity-spike
```

The import also changes the Unity project (it adds `Assets/FactorioAvatarExporter`). Don't commit the
Unity project to this repository; only the `unity-spike` folder.

### With a local Claude Code session

Paste into a Claude Code session started in this repository on the PC with Unity:

> Pull `main`. Then test the Unity avatar exporter's rendering spike, following `tests/UNITY-SPIKE.md`.
> Find my VRChat Unity project(s) (Unity Hub lists them in `%APPDATA%\UnityHub\projects-v1.json`;
> ask me if it's unclear which to use) and the scene or prefabs with my avatars. Make sure the project
> is closed in Unity, then run `tests\run_unity_spike.ps1` on it, once as it is and once with `-Mode poses`. Install Python's Pillow first if
> needed (`py -m pip install pillow`). Prefer an avatar using Poiyomi, one using lilToon, and any other
> humanoid. If the script or Unity fails, read `unity-render.log` and `unity-import.log`. Fix only the
> test script, or the exporter's `Editor/` code if it doesn't compile, and note every fix. Look at each
> avatar's `strip-on-grey.png` and the raw renders, and the animation sheets of the poses run, and write
> `unity-spike/<date-time>/OBSERVATIONS.md`: per avatar, which shaders it uses, whether it looks like it
> does in Unity's Scene view (colours, outlines, shading), fringes or halos at the edges, missing or
> cut-off parts, Console errors, and for the poses whether each animation looks right (limbs bent the
> natural way, nothing twisted, feet on the ground when they should be) and the pose check numbers.
> Commit the results folder (and any fixes) to the branch `unity-spike` and push it; don't push to
> `main` and don't commit the Unity project. Finish with a short summary.

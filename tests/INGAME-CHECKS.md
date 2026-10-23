# In-game checks (screenshots from the real game)

The headless tests check that the mod loads and swaps characters, but a headless server draws
nothing. These checks run the real game on a Windows PC and screenshot the engineer and every avatar
pack side by side, idling, running, running with a gun, mining, flying with mech armor and dying. The game plays everything by itself and closes when done.

## Running them

You need Factorio 2.1 installed (Steam is fine; Space Age optional, for the flight scene) and,
for the test pack, GIFs and the report, Python 3 with Pillow (`py -m pip install pillow`).

From the repository folder, in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File tests\run_ingame_checks.ps1
```

Options:

- `-Factorio <folder>`: the Factorio install, if not `C:\Program Files (x86)\Steam\steamapps\common\Factorio`;
- `-Pack <zip or folder>, ...`: avatar packs to show (for example one from the exporter's pack harness or
  from Unity); without it a synthetic test pack and the pack harness's stick figure are made;
- `-NoSpaceAge`: leave the DLC off.

A Factorio window opens for a few minutes and closes again. Don't touch it while it plays (the game's
player takes control of each character for the shooting and mining scenes). Your saves, mods and
settings are not touched: the game runs from a temporary folder.

## What you get

`ingame-checks/<date-time>/` in the repository:

| File | What it is |
| --- | --- |
| `REPORT.md` | Every scene with its GIF, and what each character did (distance walked, shots fired, ore mined, flight, corpse), with anything wrong marked **problem**. |
| `<scene>.gif` | The scene at the game's real speed, every character in its own labelled lane. Compare each avatar's legs with the engineer's. |
| `<scene>-strip.png` | The same frames side by side. |
| `<scene>/` | The raw screenshots (64 pixels per tile, the sprites' own resolution). |
| `window/window.png` | The avatar window, opened for the game's player after it became the first avatar pack. |
| `manifest.json`, `factorio.log` | The raw results and the game's log. |

## Sending them to Claude

Commit the folder to a branch named `ingame-checks` and push it (not to `main`):

```powershell
git checkout -B ingame-checks
git add ingame-checks
git commit -m "In-game checks"
git push -u origin ingame-checks
```

Then tell the Claude session working on the project that the results are on that branch.

### With a local Claude Code session

A Claude Code session running on the PC (the Claude Desktop app, or `claude remote-control` in this
folder) can do all of it. Paste it:

> Pull `main`, then run `powershell -ExecutionPolicy Bypass -File tests\run_ingame_checks.ps1` from the
> repository folder (install Python's Pillow first if needed: `py -m pip install pillow`). If the script
> fails, read the error and `factorio.log`, fix what is needed to make it run, and note the fix. Then look
> at `REPORT.md` and the GIFs, add a short "Observations" section to `REPORT.md` with anything that looks
> wrong (sprites out of place, shadows, legs too fast or slow compared with the engineer, missing
> frames), commit the `ingame-checks` folder (and any script fixes) to the branch `ingame-checks` and push
> it. Don't push to `main`.

## Without Windows

`tests/run_ingame_dryrun.sh <factorio> [--space-age]` plays the same scenes in a headless Factorio
(no screenshots, and the shooting and mining scenes are skipped because they need a player) and
checks that everyone walked, flew and left a corpse. CI runs it on every push.

# Space Age travel and a big bundle (2026-10-23)

- **Code:** main at `39e9a0e`, Factorio 2.1.23 (Steam) with Space Age, Quality and Elevated Rails.
- **Mod:** `avatar-characters_0.3.1.zip`, zipped from the repo's `avatar-characters` folder.
- **Isolation:** every game ran from its own folder under `%TEMP%` (own `config.ini` and write-data), with
  no personal saves or mods.
- **How it was driven:** no desktop control was available in this session, so nothing was clicked or
  typed in the game. Each run was a fresh single-player game, driven by a small test mod
  (`scripts/space-driver`, `scripts/big-driver`). It makes the same Lua calls as the console commands
  in the instructions, on a timeline, and logs the player's state every 10 ticks.
- **Results:** screenshots, `driver.txt` timelines and each run's `factorio-current.log` are in the
  sub-folders. The runner is `scripts/run_game.ps1`.

**Machine:**
- CPU: Intel i7-9750H, 32 GB RAM.
- GPU: **NVIDIA GeForce RTX 2060 laptop GPU, 6 GB**. Factorio reports "Dedicated video memory size
  5955 MB", with a budget of 5187 MB; driver 32.0.16.1047.
- The display is on the Intel UHD 630; Factorio renders on the RTX 2060.

## Part A: Space Age travel (bundle "Test server" 1.0.1: harness-generic + harness-chibi)

There were three runs:
- `space-travel/`: the instructions' commands exactly, including `leave_space_platform()`.
- `space-travel-land/`: the same, but with `land_on_planet()` to go down, because `leave_space_platform`
  doesn't send a cargo pod (see step 3).
- `space-travel-land-repeat/`: a repeat of the second, because that game closed by itself after the
  death (see "Unexplained exit" below).

Times below are ticks from the start of each run (`driver.txt`).

1. **Platform:**
   - Set up as Harness chibi (`set_appearance` returned nil, so it applied at once).
   - `create_space_platform{name = "test", planet = "nauvis", starter_pack = "space-platform-starter-pack"}`
     and `apply_starter_pack()` made the platform with its hub.
   - 5 s later, `enter_space_platform(force.platforms[1])` returned **true**. The player is in remote
     view of `platform-1` (controller `remote`), and the character entity, still
     `avatar-pack-harness-chibi`, is on the platform with them.
2. **On the platform:**
   - `game.print(game.player.character and game.player.character.name or "no character")` prints
     **`avatar-pack-harness-chibi`**.
   - Picking **Harness generic** in the window: `set_appearance` returns `"remote"`, and the character
     stays chibi.
   - The window (`03-window-on-platform.png`) highlights Harness generic and says **"You are Harness
     generic."**. Under that, in yellow: **"Your new look will be applied when you return to your
     character."**
3. **Going back down:**
   - **`leave_space_platform()` returned nothing, and no cargo pod came.** The Factorio API says it
     "Ejects this player from the current space platform if in a platform. The player is left on the
     platform at the position of the hub."
     - The player stayed in remote view of the platform, with the chibi character on it, for the
       remaining 15 s. The descent screenshots in `space-travel/` show the platform.
     - The window's "when you return to your character" status stayed.
   - With **`land_on_planet()`** (returns **true**), the cargo pod drops. Second and third run, identical
     timings:
     - +630: the character is in the cargo pod on the platform (controller `character`, still chibi).
     - +1140: the pod is in Nauvis's sky, at about (119, −239), still chibi in the pod.
     - **+1740: the pod has landed.** The player is out of it and the character is
       **`avatar-pack-harness-generic`**, at (−0.4, −0.4).
     - So the look changed the moment the player left the pod: 18.5 s after `land_on_planet`, 10 s
       after the pod reached Nauvis. The status is checked every 10 ticks, and the first check with
       no pod already showed generic.
     - **Items: all there.** Main inventory before going up and after landing: burner mining drill 1,
       iron plate 45, stone furnace 1, transport belt 5, wood 1.
     - `04-descent-*.png` follow the pod down: the platform, space, the planet's surface, the pod with
       its thruster, and touchdown with the figure next to the pod (frame 25).
4. **Death and respawn:**
   - `character.die()`: the player becomes a ghost on Nauvis, with `ticks_to_respawn` 600.
   - **Respawned as `avatar-pack-harness-generic`** after 10 s, in all three runs. In the first run the
     player died on the platform, still chibi, and still respawned on Nauvis as generic.
5. **Log search** (`factorio-current.log` of all three runs):
   - **no "error"**;
   - "avatar-characters" appears only in the loading lines:
     ```
     Loading mod settings avatar-characters 0.3.1 (settings.lua)
     Loading mod avatar-characters 0.3.1 (data.lua)
     Loading mod avatar-characters 0.3.1 (data-final-fixes.lua)
     Checksum of avatar-characters: 2360450080
     Checksum for script __avatar-characters__/control.lua: 199544188
     ```

**Unexplained exit.** The second run's game closed by itself about 1 s after `die()` (tick 2340).
- There was nothing after the "died" line in its log: no error, no stack trace, no "Goodbye".
- There was no Windows crash event and no dump.
- The repeat run did exactly the same and finished normally. The first run, which died on the
  platform, also finished normally.
- Seen once in three, so I can't say whether it has anything to do with the mod.

## Part B: a big bundle

### The bundle

- **12 harness packs** `big-01` … `big-12`, titled "Big 01" … "Big 12", at 64 pixels per tile with flight.
- **Rigs:** generic, chibi and minimal in turn (01 generic, 02 chibi, 03 minimal, …).
- **Heights:** 1.40, 1.45, 1.51, 1.56, 1.62, 1.67, 1.73, 1.78, 1.84, 1.89, 1.95 and 2.00 m.
- **Sizes:** each pack zip is 2.20–2.27 MB. Building the 12 packs and the bundle took 181 s.
- **`avatar-bundle-big-server_1.0.0.zip`: 27,974,417 bytes (26.7 MB) on disk.** It holds 329 files,
  289 of them PNG, 27.4 MB unpacked.

### Start-up to the main menu, memory, atlases (`big-bundle/startup/`)

Each configuration was started twice from its own folder:
- "first": a fresh folder, without Factorio's caches;
- "again": the same folder a second time.

The time is from launch until the log's "Factorio initialised" (the main menu appears right after).
Memory was read about 4 s later at the main menu. "Working set" and "private" are what Task Manager
shows for the process. "GPU dedicated" is Windows' per-process GPU counter, the same as Task Manager's
"Dedicated GPU memory" column.

| Video memory usage | Mods | First | Again | Working set | Private | GPU dedicated |
| --- | --- | --- | --- | --- | --- | --- |
| all (default) | avatar-characters | 36.1 s | 24.6 s | 839 MB | 5,072 MB | 4,078 MB |
| all (default) | + big bundle | 37.4 s | 25.7 s | 901 MB | 5,184 MB | 4,194 MB |
| low | avatar-characters | 41.2 s | 30.6 s | 2,991 MB | 5,383 MB | 2,137 MB |
| low | + big bundle | 42.0 s | 31.5 s | 2,968 MB | 5,506 MB | 2,181 MB |

So the 12-character bundle costs about **1 s of start-up**, **about 110–120 MB of GPU memory** at
"all" and **about 40 MB** at "low".

The game's own lines (the "again" runs):

```
all, without the bundle:
  23.360 Sprites loaded
  23.939 Estimated VRAM usage for textures: 3857.74 MB (Atlases: 3757.92 MB, Textures: 99.82 MB)
  23.939 Real VRAM usage for the whole program: 4148.82 MB
all, with the bundle:
  24.454 Sprites loaded
  25.003 Estimated VRAM usage for textures: 3976.07 MB (Atlases: 3876.24 MB, Textures: 99.82 MB)
  25.003 Real VRAM usage for the whole program: 4256.82 MB
low, without the bundle:
   5.364 Created virtual atlas pages 4096x4096x90
   5.364 Created virtual atlas of size 65536x24576 with physical texture size 8192x8192
  28.069 Sprites loaded
  29.830 Estimated VRAM usage for textures: 1869.14 MB (Atlases: 1769.32 MB, Textures: 99.82 MB)
  29.831 Real VRAM usage for the whole program: 2143.82 MB
low, with the bundle:
   5.526 Created virtual atlas pages 4096x4096x94
   5.526 Created virtual atlas of size 65536x24576 with physical texture size 8192x8192
  29.011 Sprites loaded
  30.810 Estimated VRAM usage for textures: 1907.97 MB (Atlases: 1808.14 MB, Textures: 99.82 MB)
  30.811 Real VRAM usage for the whole program: 2179.82 MB
```

How the atlases change at "all" with the bundle:
- the plain sprite atlases go from 16384×16384, 16384×16384, 16384×16380 and 16384×14376 to three
  16384×16384, one 16384×16368 and a new 8192×2792;
- the shadow atlases go from 16384×16384, 16384×16340 and 16384×5124 to two 16384×16384 and one
  16384×9964;
- the icon atlas goes from 8192×3984 to 8192×4000.

At "low" the bundle adds 4 virtual atlas pages (90 → 94).

### The avatar window with 12 (`big-bundle/game-*/window-12.png`)

- **It shows all 12**, in 13 buttons: the engineer, then Big 01 … Big 12 in order, 4 to a row.
- The look list is a scroll pane with a maximum height of 248 px. So three rows (engineer to Big 11)
  are visible, **Big 12's button is cut off at the bottom**, and a scrollbar appears.
- The window is centred, with Done visible.
- The buttons show only a picture. Every figure is scaled to fill its button, so **the heights don't
  show**. **Generic and minimal figures look identical**; only chibi's big head stands out.
- Telling Big 01 from Big 04 takes the tooltip, the pack's title and author, which the screenshot
  can't show.

### 12 characters side by side (`big-bundle/game-*/12-characters*.png`, `screen-*.png`)

- All 12 were made with the instructions' command, `avatar-pack-big-01` … `-12` at (2, 5) … (24, 5).
- They stood for 15 s, then **ran east and west together for 15 s, turning every 2 s**.
- They draw correctly, each with its shadow.
- **All 12 are drawn the same size.** The 1.40–2.00 m heights given to the harness don't show in the
  game: Big 01 (1.40 m) and Big 10 (1.89 m) stand equally tall. So the exporter scales every body to
  one game size. Only the chibi rig's big head and short body tell the figures apart.
- **No stutter measured:**
  - The game's tick counter advanced exactly 300 ticks every 5.0 s of log time, standing and running:
    **60.0 UPS** in every interval.
  - The FPS overlay (`show-fps=always`) reads **60 FPS** in all five desktop captures.
  - The time-usage panel did not appear even with `show-time-usage=always`. Showing it needs F4, which
    couldn't be pressed here, so there is no time breakdown.
- **Memory with the 12 on screen:** working set 667–705 MB, private 4.9–5.0 GB, GPU dedicated
  4,195 MB, the same as at the menu.

### Video memory usage set to "low"

- **Start-up:** about 5–6 s slower than "all", with or without the bundle.
- **Memory:** about half the GPU memory (2.1–2.2 GB instead of 4.1–4.2 GB), but **3.4× the working set**
  at the menu (about 2.9–3.0 GB against 0.84–0.90 GB) and slightly more private memory.
- **In game with the 12:** working set 2.8 GB, GPU dedicated 2,181 MB. **Still 60 UPS and 60 FPS**, no
  stutter, while standing and running.
- **Look:**
  - The figures are visibly **softer and blockier** than at "all", most of all the chibi heads
    (`game-low/12-characters-close.png` against `game-all/`).
  - That's the low setting's lower-resolution sprites, the same for every sprite, not something in
    the packs.
- The terrain differs between the two runs because each run makes a new random map.

## Not covered

- Nothing was clicked: the avatar window was opened and the look picked through the mod's remote
  interface. The window's buttons and the Ctrl+Shift+A shortcut were not tested in the game.
- No human watched for stutter. The FPS and UPS figures come from the overlay and the log.
- Factorio's time-usage panel is missing (see above).

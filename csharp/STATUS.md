# Blur Map Builder in C#: status

**In short:** I compared every tab with the Python program and ported what was missing. The C# version now has all
7 tabs, Play, first-start setup, the object library and the import windows. The list of small differences is below.
The build is about **7 times faster** and the 2D map runs at 60 pictures a second.

**Not tried in the game yet, and no person has used it yet.** All tests ran with a robot mouse on a copy of your maps.
Blur was never started. The Python program stays your real program. `map_builder` and your game folders were not
changed, and nothing was committed or uploaded.

## Try this first

1. Make a backup copy of your maps folder: `J:\blur - user 1\Blur Map Builder\maps`.
2. Start the program with this command in PowerShell (you don't have to copy anything):

```powershell
& "E:\blur - bak\map_builder_csharp\publish\Blur Map Builder.exe" --game "J:\blur - user 1"
```

3. Click through the tabs. Drag a road point in 2D. In 3D, click an object and drag its handles.
4. Press **Build + install**. It takes about 10 seconds.
   This replaces `rebrn\gamedata.pak`, `rebrn\root.pak` and the map's level files, the same as the Python program.
5. Press **Play (F5)** and drive your map. Look at these things (they are drawn differently, see below):
   the START / FINISH arch, the sky, the minimap, the menu picture, the small car icons.
6. To go back: open the Python program and press Build + install.

The C# window keeps its own auto-save the same way as Python, and asks before closing if you changed something.

## What was added in this round, tab by tab

- **Top bar:** Play (F5), Check for updates, the update source button.
- **Left panel:** Delete location, Import / Export `.blurmap`. New map, Duplicate and Rename now work as in Python.
- **2D map:** 90-degree turns (T / Y), lap track by dropping the end on the start, drag branch and split ends, insert
  into a branch, rows on a branch, 3 lanes, measure with up to 50 points, keys 1-8, chevrons, tight-turn marks,
  height / width / corner radius, Start at / Finish at, mirror and reverse as in Python.
- **Checks:** overlap with other maps, total maps, start at a branch, branch balance, surface summary.
- **Right panel:** point, power-up, branch and split panels, lap track, minimap (auto, export, own picture, preview).
- **3D:** Move / Rotate / Scale handles, typed values, tilts, scaling, groups, Ctrl+D, Solo, road points, start camera
  (add, remove, play), Drive, flat view, real shapes, hover pictures, Bake, Path scatter, .glb import window, object list
  with search and groups, library sorted by kind / place / category, red problem beacons.
- **Collision:** see-through walls, white ticks, ground grid.
- **AI path:** lane width, state text, branch / split tools on the bar, the routes through branches.
- **Lighting:** a full page: sliders, colour pickers, preview, sky pictures, custom sky, Light up placed objects, reset.
- **Surface:** brush buttons, sides, side type, Clear paint, smooth painted shapes.
- **Custom car:** everything: new / copy / delete, class, body, icons, paint, wheels, stats with the chart, .glb body with
  fit, the car in 3D.
- **Tab changes:** the window stays still with a small loading bar, and the new tab appears whole. Tabs that open fast show no bar.
- **Other:** first-start setup, start-up picture, help window, layouts as in Python, crash reports.

## Top tabs in two groups + custom car sound (2026-11-15, nothing committed, sound not tried in game)

- **Top bar:** the tabs sit in two rounded groups, MAP (2D map ... Lighting) and CARS (Custom car, Car skins) (`TabGroup` in
  Look.cs). The bar's height now follows the groups' real height (the robot's size check failed by 2 px before; the tour
  now runs that check too: all 4 sizes pass).
- **Custom car: Body / Sound tabs** at the top (name, class, stats and the 3D view stay for both). Sound tab: "Engine sound
  of" any game car (or the body car's), 🎲 Random, ↺ Body car's sound. Saved as `car["sound"]` (the Python program ignores it).
- **Build:** the AmaxVehicle record of a custom car gets the 13 sound fields of the chosen car, byte for byte
  (`CarSound.Fields`: vehicleAudioEntity + AI / PS3 / boss versions, dry / wet wheel sounds, AudioRPMLimit, AudioGarageID,
  AudioForcedInduction, AudioEngineType, AudioExhaustType). No sound picked = the build is the same as before.
  Checked with `bmbcli soundtest GAME WORLD.json CAR...` (the cars made in memory: their sound fields = the chosen car's).
  **Not heard in game yet.**
- **Sound tab, second round:** no stats on it any more; the list shows each car with its engine ("Dodge Challenger Tuner (Dodge V8)");
  **▶ Listen** plays a short rev of the chosen engine (every rev step of its bank, engine + exhaust, cross-faded, then off the gas).
  `bmbcli revwav GAME OUTDIR CAR...` writes those as WAV; `bmbcli revwav GAME all` = every engine -> its bank (all 33 found).
- **How the game's car sounds work (found for custom sounds, next step):** car record `vehicleAudioEntity` -> AudioEntityProxy
  (audio.bin) -> AudioEntityDef (audioentities.bin, e.g. "Dodge_V10", parts Engine / Exhaust / Controller) -> AudioObjects
  (audio.bin, filename = `\audio\dat\dodge_v10_engine.dat` in root.pak) -> the .dat names its bank and sounds ("Dodge_V10",
  "Viper_Engine_792" ...) -> `\audio\dodge_v10.baf`. Bank = 'BANK' header (name at +18, wave count at +12), one 'WAVE' header per
  sound (name +12, data start +44, length +48, rate +64, samples +68), then the data. Car sounds: format 7 = 4-bit ADPCM, VAG
  style, 33-byte blocks (filter/scale byte + 64 samples), 48 kHz mono (`SoundBank.Decode`). An engine bank: engine loops at
  6-8 rev steps + off the gas, exhaust the same, induction, turbo spool / air / blow-off. Tyre / drift sounds are one shared
  entity for all cars (Wheel_dry, wheel_dry.baf). Own sounds need: a VAG encoder, a copy of the bank with a new name, copies of
  the .dat files naming it, new AudioObjects + a copy of the entity + a new proxy, and the car pointing at it.

- **Own engine sound (built 2026-11-15, NOT heard in game yet):** Sound tab > MY OWN ENGINE SOUND: Use my own sound, Import .wav
  (copied to `maps\cars\sounds\`; PCM 8/16/24/32 bit or float, any rate, mixed to mono 48 kHz), Recorded at (rpm), Listen.
  Saved as `car["sound_own"]` (file, empty = off), `sound_own_file` (kept when turned off), `sound_rpm`.
  The build (`OwnSound.cs`, `CarBuild.SoundFiles` / `SoundAliases` / AddCars): for custom car i on the engine of its sound car
  (else body car): a copy of that engine's bank named `zb<i>` + the rest of its name (same length), its engine + exhaust loops
  (+ off the gas) = the player's sound pitched to each rev step (rev / recorded rev), looped without a click, as loud as the game
  sound it replaces, encoded to the game's 4-bit ADPCM (encoder: 45 dB on a game loop); copies of the .dat files that name the
  bank (their own name + the bank name swapped, same length); in audio.bin new AudioObjects (uids BA500000 + 16 i + k) for those
  .dat files and a new AudioEntityProxy (+9) pointing at a copy of the entity (audioentities.bin, +8, references moved to the new
  objects); the car's vehicleAudioEntity -> the new proxy. Root pack: the new names are aliases of the game files, then filled.
  `bmbcli owntest GAME ENGINECAR SOUND.wav RPM OUTDIR` checks it all in memory; a dry build on the scratch copy made the files.
  **First in-game test (2026-11-15): still the game sound.** Cause: Blur REBORN mounts cache\gamedata_boot.pak while it starts
  and reads the sound tables (audio.bin, audioentities.bin) from it then (the car records are read later, from rebrn\gamedata.pak).
  **Fix:** two more Blur REBORN.exe patches (0xA66660 "GAME:\cache\gamedata_boot.pak", 0xA668BC "cache/gamedata_boot.pak" ->
  rebrn\, same length); setup links rebrn\gamedata_boot.pak to the game's own; the build writes work\gamedata_boot_track.pak =
  the game's boot pack + the two sound tables when a car has its own sound (else an unchanged copy), installed into rebrn\.
  The game's cache\ is not changed. The map builder remakes Blur REBORN.exe on its next start (setup runs once more).
  **Second in-game test: game-car engine swap works, own sound still not heard.** Installed data was right (the car -> BA500009).
  Missing piece: `\audio\datbanklists.wbl` (root pack, read once at start) lists the banks each .dat needs ("Toyota_i6_engine.dat,
  Toyota_i6.baf,Car_Common.baf,;"); the copies were not in it, so their bank never loaded. The build now writes that file with a
  line per .dat copy (the game line, its bank swapped for the copy's).
  Still unknown until heard: whether two banks may hold sounds with the same names.
  **Third in-game test: still the game car's sound.** Two faults in the bank copy: (1) the Exhaust sounds are stereo (byte +75 of
  the wave header = 2; the 33-byte ADPCM blocks take turns left, right ...) and the copy wrote mono data into them; (2) the
  Induction layer (also one sound per rpm step, as loud as the engine) was kept, so the game car still came through.
  **Fix:** Engine + Exhaust + Induction steps are replaced; stereo waves get each block twice (same sound both sides); the copy's
  bank is now exactly as long as the game's. `SoundBank.Decode` reads the left side of stereo waves. The car's
  aiVehicleAudioEntity also points at the copy (in case the game uses that one for the player). Turbo / supercharger extras stay.
  **Fourth in-game test: no engine sound at all.** Reading: the game plays the full entity only for the focus car (vehicle 0 in a
  one-screen race); every other car, likely the player in the LAN quick race too, plays aiVehicleAudioEntity. The AI player code
  can't drive the full entity, so the car went quiet. **Fix:** `OwnSound.MakePlans` makes a second copy from the AI entity
  ("VW_flat4_AI" -> "zb0flat4_AI": its .dats, its bank with the own sound, uids +10..+15 of the car's 16); the car's
  aiVehicleAudioEntity points at that one. The AI banks are 36 kHz: the loop's speed now uses each wave's own rate.
  Dry build checked (both copies, wbl lines, sizes). **Fifth in-game test (2026-11-15): WORKS - the siren is heard on the custom car.**
  (The BMB_SOUND_PROXY_TEST switch is removed.) The app exe still has the switch until its next build (it does nothing unless set).

## Lighting tab: car preview + simpler controls (2026-11-11, nothing committed, not tried in game)

- **Preview** (`LightPreview.cs`): a car built in code (smooth body, cabin with windows and pillars, 4 wheels with rims;
  2704 triangles) on a ground plane, drawn by a small software rasterizer (no GL). Clear-coat paint with fresnel mirrors
  the sky picture (yours or the preset's, read at 1024 x 256 from the same files the build uses); sun, sky light, light on
  objects, baked light (ground), lamp glow (emissive), fog + low fog, exposure (middle grey / min / max on the picture's
  average) and bloom follow the sliders. Soft shadow under the car, sun shadow when the sun is on. Drag = turn the camera.
  16-45 ms while dragging, 40-60 ms for the sharp picture (2x2 samples) that follows.
  Second pass, closer to how the game draws: exposure = middle grey / average brightness (centre weighted), kept between
  exposure min and max, then a straight gamma 2.2 (no film curve); soft bloom (x / (2 + x) above the threshold); distance fog
  exp(-distance x density) + a height-fog layer under 5 m; sky = picture x sky brightness, sky rotation = part of a full turn.
  The car is lit only by the light on objects x the baked colour (so with light on objects at 0 it is dark, like placed
  models), plus reflections that grow toward its edges and a rim light. Sun and sky light light the ground.
- **Third pass (after the user's in-game test: "does not look like the preview"):** the preview car is now the game's own
  **Dodge Challenger Tuner** (`GameCar.cs`): read from `root.pak.original` (a lower detail level, 9045 triangles, no
  interior), its blue body picture `aotexture_blue` on the parts with the picture, its main blue on the plain-paint parts,
  lamps / glass / tyres / rims by material. Made once into `Blur Map Builder\light_preview_car.bin` (about 1 MB) in the
  background at start-up, again when root.pak.original or the car format changes (so after an update too); loads in
  ~15 ms; the code car is the fallback if the game files cannot be read. Also: the ground is lit by the baked light (the
  user's pink sun / sky colours did not tint the ground in game), the sky = picture x alpha x alpha x brightness (as the
  game draws it), the sky's own fog colour + curve, depth-fog scale, height-fog height / falloff / scale and the 4 glow
  levels are read from the preset's record (the tab does not show them), glow = 4 blur sizes, a wider camera lens.
  Mount Haruna's height-fog scale is 0, so "Low fog" does nothing there. 20-25 ms while dragging, about 45 ms sharp.
  `BMB_ONLY=lightcar` (+ `BMB_WORLD=...world.json`) renders a world's lighting from behind the car;
  `BMB_ONLY=carinfo` lists a car model's parts and saves its pictures.
- **Fourth pass (second in-game test: white sky, pink ground, silver car):** the far ground now goes fully into the fog colour
  (no blend into the sky picture any more); the car's reflections are x the exposure (the game reflects a live picture of the
  scene that already has the exposure in it, then exposes again: that is why the paint looks silvery). The sky picture itself is
  not fogged in the game (only its own thin horizon fog), so the white sky in the test is most likely the map's 66 placed skyline
  objects far away, fully pink-fogged; the preview has no placed objects. Check: Fog density 0 -> the sky picture should show.
  **Confirmed in game (2026-11-11):** with Fog density 0 the city shows again: the white came from the fogged placed objects.
- **Fifth pass, the ground:** the game's asphalt picture (tarmacplain02, the Surface tab's tarmac) tiled every 3 m (mip levels,
  no flicker far away), its brightness set to the old plain ground's; wet patches (~20 %, placed by the game's cloudy mask
  uv_distort_04, 30 m) are darker and mirror the sky and the car (the car is drawn a second time upside down under the ground).
  30-40 ms while dragging, 70-80 ms sharp.
- **Sixth pass, a city ring:** simple blocks 250-700 m around the car (3-degree slices, some gaps), concrete lit by the baked
  light, about 1 in 5 windows glowing with the emissive colour ("Lamps and windows glow"); they take the distance fog colour
  like placed objects in game (with the user's pink fog 0.0054 they turn pink-white, as in the in-game test). The car and the
  wet patches reflect them. `BMB_FOG=0.0054` adds a lightcar picture of the world with that fog. 30-40 ms dragging, 65-100 ms sharp.
- **Seventh pass, every slider checked** (`BMB_SLIDERS=1` + `BMB_WORLD`: each slider 0 -> max, how much the picture
  changes). User: Sun, Sky light and Glow work in game. Now: Sun and Sky light = colour x strength (fits the old in-game test:
  sun 20 made everything far too bright; every city has strength 0) and light the ground and the buildings (the car stays lit
  by the light on objects x baked). Fixed a bug of this round that had set Baked + Lamps glow to 0 in the preview. Glow works
  but only shows on parts brighter than the glow threshold after the exposure (lamps; more with a brighter exposure).
  Still no effect in the preview: the strength of Baked and of Lamps glow (cities with strength 0 still look lit in game),
  and Sun when its colour is black (black x strength = 0).
- **Open question (the line below is OUT OF DATE for Sun / Sky light, see the seventh pass):** the game seems to use only the RGB of the Sun / Sky light / Baked / Emissive colours, not
  their 4th number (what our "strength" sliders change). So those 4 sliders may do nothing in game; the preview ignores them
  too. Check in game; if true, those sliders should scale the colour instead.
- **Controls** (`Panels.cs`, LightBuild): 8 main rows with plain names in 3 groups (Light / Sky and fog / Camera), the other 7
  under a folded **Advanced** button that says how many of them you changed. Fog = colour + density on one row. Tooltips
  give the old technical names. `*` still marks changed values; saved values and keys are unchanged.
- Self-test: `BMB_ONLY=lightcar` = car pictures for 5 presets + timings (`scratch\ui\lightcar_*.png`); the tour also takes
  `tour_light_more` (Advanced open) and `tour_light_other_layout`. Tour passed in English and Russian; full robot NOT run.

## UI / UX round (2026-11-08, test build publish_new14, nothing committed)

- One slim top bar: tabs on the left; ⚙ menu, Undo / Redo icons, Save, Play, Build on the right. Labels shorten (Save = ✔,
  Build) only when the row is too narrow; the full names and shortcuts are in the tooltips.
- ⚙ menu (UiChrome.cs, dark menus = DarkMenu): check for updates, update source, window layouts (load / save as / reset /
  delete), language, help, version. The bottom bar is only the status line + the language box.
- Map list: a row of icons (new map, new location, duplicate, rename, ⋯) and the same as a right-click menu; delete moved
  into the menus (was 8 big buttons, 2 of them red).
- Tool bars: Undo / Redo no longer repeated per tab; 3D and Collision have no bar of their own (the view's row has ❔);
  Lighting has no Fit. When a bar would wrap, its buttons drop the "(S)" key hints first (FitBar / Compact).
- 3D: the MAP panel steps aside below 1700 px wide (the 3D tab has its own two panels).
- Car list drawn like the map list (RoundList). Snap is a toggle button. Ctrl+1..9 = the tabs.
- Self-test: `BMB_ONLY=tour` = pictures of every tab, menus and windows (scratch\ui\tour_*.png); pictures now use
  PrintWindow (the window is captured even when covered).
- NOT run: the full robot test (some of its checks look at the old bottom bar / buttons and may need updating before a push).

## 2.3.0 second round (2026-11-07, not tried in the game yet, nothing committed)

- **Fast start** (assets/quickstart.lua v0.15, copied into the game by Play / Build: `Setup.RefreshPlugin`): with `fast = true` in
  target.lua (Play window checkbox, on by default, saved in data/play.json) the plugin does what the keys do on the intro
  screens: Activision movie -> GUI.StartGame(), Bizarre movie -> GoScreen Legal, attract movie -> AttractSplash_GoStartScreen(),
  Press Start -> ContextTable[Main].MessageUpdate(ButtonStart, 0, true) (the game's own invite code does this). Also for a
  normal start. The LAN walk waits less (600 frames max instead of 900, shorter step delays). The Amax login box is untouched.
  Checked only in a Lua 5.1 simulation (lupa), NOT in the game.
- **Landscape tab = the 3D view** (View3DLand.cs): ray marched brush, live textured hills, ring under the mouse, Paint tool,
  "Auto texture by slope" (land["auto_tex"], land["paint"] on the 8 m grid). Heights between the 8 m points = cubic B-spline,
  built at 4 / 6 / 8 ... m (finest that fits). Slope textures: ground / dirt / rock, borders get mixed pictures
  (grassdirt, dirtrock). Auto hills fill the strip to the road kerb with flat ground.
- **Game pictures** (GameTex.cs): read from base/root.pak.original models, index in data/gametex.json: tarmacplain02 (asphalt),
  ambient_rock02 (rock), concreteblock1, hay01 (dry grass; tinted = grass), drilltowertexture01 (worn), uv_distort_04 (mask).
  New surfaces: rock, drygrass, concrete (+ internal patch, worn, grassdirt, dirtrock). The exe did not grow.
- **Varied asphalt**: 4 variants made from the game asphalt + worn surface in cloudy patches, and random repair patches along
  every road (Surface.AutoPatches, not stored, never over the middle line).
- **Auto bumps** (Surface tab, BUMPS group): Terrain.AutoBumps, Auto or custom (height, spacing, longest bit, most road between).
- Short check: `BMB_ONLY=land bash tools/run_ui_test.sh` (23 checks + a build). Full robot NOT run (only before a push).
  Test exe: publish_new13.

## What is still different from Python

- **Wheels:** the C# build now copies the chosen car's **tyres and rim rings** into your car as well. In Python only the rim's face
  changes, so a wheel from another car shows the body car's tyre around it. See "Wheels" below.
- **Updates:** the button says if a new version is out and opens its page. It does not replace the program itself.
- **Map Tester** is not a separate exe. Start the same exe with `--tester`.
- **.exr skies** give an error. Use .hdr, .png or .jpg. (In Python they need an extra package too.)
- **Real shapes:** 55 of 61 sampled game models were read. The rest stay boxes. Bake made pictures for 869 of 949 models.
- **The 3D road** is drawn with the build's real road model, not Python's simpler drawing. So it should match the game
  better, but it looks a little different from Python's 3D tab.
- **Look:** scroll bars are the white Windows ones, not the slim blue ones, and the drop-down menus look a little different.
- **Saved layouts** are kept apart from Python's (`ui_layouts_csharp.json`).
- The first start does not download the BlurImportExport add-on. C# does not need it.

## Wheels

A wheel has 3 parts: the rim's face is in the game's wheel file; the tyre and the rim ring are inside the body car's model. Now all 3
follow your Wheels choice.

- A chosen tyre often has more points than the body car has room for. Then it gets fewer sides, but keeps its shape. The build log says "less detailed".
- Tested with every game car's wheels on the 4 body cars imported bodies use: 232 pairs, 227 complete. In the other 5, a corner keeps the
  body car's tyre or rim, and the build log gives a WARNING.
- The driving physics stay the body car's. A much bigger tyre may sink into the road a little, and a smaller one may float.
- **Tilt the road** (new, C# only; the Python program ignores it): select a point in 2D, use `,` and `.`, the Tilt row, or **Auto tilt for this bend**. In 3D: click a point, press R, drag the ring, or type the tilt.
  Orange edge = goes up, blue edge = goes down. Most 20°. The road, collision, walls, power-ups and AI path all follow it.
  The tilt comes in slowly (the road edges climb at most about 6 %), so it needs room: flat parts keep about 110 m clear around them, and a sharp change between close points is softened.
  It stays flat at the start, finish, bridges, branches and lane splits. The low side never goes under the ground: the road rises as much as that needs.
  **Not tested in game yet**, so the feel of the car and the AI on a tilted road are unknown. A map with no tilt builds byte for byte as before.
- **3D object list:** every row, named group and whole list has an eye (hide or show, in this view only: the game and the map file do not change) and a target (solo). Track points have the eye only. The eye resets when you open another map or close the program. X still removes an object from the game.
- **3D view light:** a low sun and hard shadows on the ground (button Sun + shadows). Shadows fall on the ground only, not on the road or on objects.
- **Car skins tab (2.1.0, 2026-11-03):** skins for the game's Boss / Legend / livery / Rat cars (38 cars; plain-paint cars have no body picture).
  Per car: Export picture, Export UV map, Load my skin, Use the game's picture, Skin on. "Build skins" writes the skins that are on straight into
  Blur REBORN's `rebrn
oot.pak` (and the game's picture back for skins turned off); Build + install keeps them too. Blur.exe and `cache\` are never touched.
  Skins live in `Blur Map Builder\skins\` (same files as the Python prototype). Car list, body picture and UV map checked against Python: same 38 cars.
  **Seen in game 2026-11-03: the user's Corvette C3 Rat skin shows in Blur REBORN.**
  **Also in normal Blur** (tick, off by default, asks first): Build skins also makes normal Blur's `cache
oot.pak` = the original + the skins;
  turned off (or Uninstall all), the original comes back byte for byte. Car models are only in root.pak (root_streaming.pak has none). **Seen in normal Blur too (2026-11-03): the Corvette C3 Rat skin shows.**
- **Panels:** the small arrow at the top corner of Locations, Object library, Objects in this map and the Map panel folds it to a thin strip; the arrow brings it back. It is kept with the layout.
- **Release:** version 2.4.0 (2.0.0 on 2026-10-31, 2.1.0 with Car skins on 2026-11-03, 2.2.0 plain-paint skins, 2.3.0 Landscape / Simple layout on 2026-11-10, 2.4.0 Lighting preview + car sounds on 2026-11-15; full robot 252/252), file name `Blur Map Builder.exe`, the name the Python updater looks for. It updates itself from GitHub releases like Python did; the last version stays beside it as `.old`. `Blur Map Tester.exe` stays the small Python one (it uses the same files). Released 2026-10-31 as v2.0.0; the source is in the `csharp` folder of the GitHub repo.
- **Language:** English or Russian. The first start asks once; the box at the bottom right changes it (the program restarts). The build log and names from the game stay in English. The choice is kept in `%LOCALAPPDATA%\BlurMapBuilderCS\language.txt`, not in the game folder.
- **Look:** the old icon, rounded buttons, dark lists and scroll bars, short help on every tab and window (Help, F1 or the ? button; All keys opens the long list).
- **Window size:** tested at 960x600, 1100x680, 1280x720 and 1920x1040. Narrow windows get shorter names, two rows on top; the 3D tab drops the object list and map panel below 1240 wide. Lighting and Custom car no longer show the map panels.
- The Wheels menu shows a picture of each car's wheel when the mouse is over a name.
- **A wheel that seems to disappear in a turn:** the car leans, so the wheels on the outside of the turn push up. If an imported body hangs lower than
  the top of the tyre, the wheel goes up inside the body. With `mdpro ii` the body's underside is 7 to 14 cm below the tyre top even standing still.
  Raise it with **Fit the body on the wheels: Up**, about 0.10 to 0.16 more. This explanation is measured from the files, not seen in game.
- **Not tested in game yet.** Check the tyre colour too: the tyre uses the body car's tyre picture.

## Speed, measured on this PC

| What | Python | C# |
|---|---|---|
| Whole build, 5 cars | 73.2 s | **9.9 s** |
| First start: library + all files | minutes | **3 s** |
| 2D map: drag a road point | 9 pictures a second | **60 a second** (the screen's limit) |
| Open the 3D tab | 1.9 s | 0.65 s |
| Turn the 3D view | 60 a second | 60 a second (no gain) |
| Program opens | 1.7 s | 1.5 s, then the start-up picture stays 2.5 s as in Python |

## Test results

- **Robot test of the window: 214 of 214 passed, in English and in Russian** (run again after the tilt + look change; the Windows test also tries 4 window sizes on every tab). It used the tools of every tab, undid everything, and the map came back
  exactly. `world.json` was the same byte for byte afterwards. Then it built from the window.
- **Your map + 5 cars, compared with Python file by file:**
  - gamedata pack: **all 623 files the same**.
  - root pack: **3,666 of 3,677 files the same**, including all 5 car models.
  - Level files: all the same (collision, lighting, race path).
- **The 11 root files that differ.** Expected, but check them in game:
  - 3 START / FINISH arch models, 1 minimap, the menu picture bundle: drawn by Windows, not Pillow. Same shapes, other pixels.
  - 1 sky: the same picture, packed by my simpler packer.
  - 4 car icon sheets: the same silhouettes, traced a little differently.
  - 1 painted model: the same triangles in another order.
- **First start on a copy of your game, compared with what Python made:** `Blur REBORN.exe`, both backups, the level template
  and all 53 unpacked files are the same byte for byte. The object library has the same 2,052 objects with the same names and sizes.
- **A brand-new install** opens and builds. That test found a real crash (no `world.json` yet), now fixed.
- **Other bugs found and fixed:** the road disappearing in the 3D view from far away, and the 2D Mirror working on the wrong axis.

**Not tested:**

- Inside the game, or by a person with a real mouse.
- Pressing Play. The robot only wrote the Play file on the copy and opened the window, because Blur must not be started.
- `--tester`.
- The update window when a newer version exists. (The "Up to date" answer from GitHub was tested.)
- The Windows file and colour pickers: the robot passes the file names directly.

**One open problem from before:** on a 3-lane split, 5 points of the divider's look sit up to 7 mm from Python's. You can't see it,
its collision is the same, and I still don't know the cause.

## Good to know

- Choices I made: **C# with .NET 10**, Windows Forms, and OpenGL drawn directly, with no extra packages for drawing.
  Details are in `tools\README.md`.
- `.dotnet` (784 MB) is only needed to change the code. The program in `publish` runs without it.
- `publish\Blur Map Builder.exe` is 59 MB and holds everything it needs.
- Copy it next to `Blur.exe` and it starts without `--game`.

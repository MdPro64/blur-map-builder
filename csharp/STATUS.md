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

## Community wishes, round 2 (2026-11-16, after 2.5.0; NOT released, NOT committed, NOT tried in game)

- **Delete a car = its files go too.** Custom car > Delete also sends the car's imported body (.glb), own sound (.wav) and
  logo picture to the Recycle Bin, unless another car still uses the same file. (Undo brings the car back, not the files.)
- **Own sound keeps the rest.** Checked: only the engine, exhaust and intake loops are the player's sound; turbo,
  supercharger, blow-off, gear and the other sounds stay the game engine's (OwnSound.cs head comment fixed).
- **Floating brake lights fixed** (imported bodies). Two faults: (1) `CarModel.Kind` sorted "e_BrakeLight_*" as a wheel part
  ("brake"), so the brake light shapes were never hidden; now "brakelight" = light. (2) Each light's box is in the model
  twice: the element list (before the vertex data) and a part list at the end, rounded a little differently; the game puts
  the light trails at the centre of the second one. Both copies now go under the ground (the second matched within 1 mm).
  Bowler container: 15 lights, 30 boxes, 8874 points (was 12 boxes, 8699 points). `bmbcli glbmodel GAME BODY.glb OUT.model`.
- **Own logo** (Body page > Logo > Import logo): picture copied to `maps\cars\logos\`, `car["logo"]`. Build: a copy of the
  maker record (AmaxManufacturer, uid CA4F0000 + i, name `<car>_logo`, of the replaced car when replacing) whose UiTexture
  = `man_bmb_<i>`; the car's `Manufacturer` points at it; the picture goes into ui_frontend.bpr like the game logos (256x128
  + 128x64, DXT5, format 5, 2 mips; `Bpr.SetPicture` overload). Maker name stays. `bmbcli logotest GAME WORLD.json OUTDIR`
  checks records + reads the picture back. Not yet seen in game: whether a second record with the same maker name shows up
  twice in a maker list.
- **Own other sounds** (Sound tab > MY OWN OTHER SOUNDS, `car["sounds"][kind]` = .wav in `maps\cars\sounds\`): supercharger
  whine (`_sc_N`, loops, pitched by N / middle N), turbo spool (`_turbo_spool`, `_Induction_boost`), turbo air (`_turbo_air`,
  `_ext_air`), turbo blow-off (`_turbo_bov*`, plays once), gear shift (`Transmission_race_gear_up/down`), backfire pops
  (`pop_NN`, `bang_*`). The last two are in the shared bank Car_Common: the car then gets its own copy `zb<i>_Common`, its
  engine/exhaust .dat copies name it, and its bank-list lines too. Loops keep the game sound's length, once-sounds the
  player's; each as loud as the game's. `OwnSound.Kinds` / `Sounds` / `MakeBank(game, name, sounds, common)`.
  **Bug found + fixed:** the rev-step names also come as `_eng_`, `_ext_`, `_extA_`, `_ind_` (BMW_V8, CCXR_V8, LR_V8 ...):
  those exhaust / intake loops were never replaced, so e.g. the M3's exhaust stayed the game's. Checked: `bmbcli kindtest`
  (BMW_V8: all 28 sounds changed + 14 in the shared copy; Focus RS / Audi TT: spool, air, blow-off). `bmbcli bankwaves GAME ALL`
  lists every bank's sound names.
- **AI mods (Game tweaks switch `ai_mods`, 2026-11-17, not released, not tested in game):** `GameTweaks.SetAiMods` writes 2 byte
  patches into the existing Blur REBORN.exe at Build + install (off = original bytes back; anything else there = left alone):
  file 0x7F9502 the AI's call to OfflineValues -> new code in the int3 gap at 0x9F2F20 (VA 0xDF3B20) that, in custom races
  with Mods on, calls FillOutLevelStateCar with a loadout. Round 2 (the player's shop rules; round 1 showed nothing in game):
  slot 1 = Drifter for AI 0-1, Front Runner for the rest; slot 2 = Splash Damage / Decoy Drop / Shielding Efficiency; slot 3
  = Silent Running / Mastermine / ECM, from fixed random tables (same on every PC). The gap patch is now 224 bytes; round 1
  bytes are upgraded in place. Two counters in unused .tls memory (VA 0x1259F00 AI set up, 0x1259F04 AI given mods) are
  read with `scratch/aimods_counters.ps1` while the game runs; CLI `aimods GAME on|off` patches only the exe.
  Round 3 (the Drifter AI never slid: AI drive with extra grip forces, `g_AiCorneringBoost` 2.25 at VA 0x1134A7C and
  `g_AiTorqueHack` at 0x1134A44, used in the AI steering-forces helper 0x4BDBA0): the setup code also sets the Drifter
  cars' bits (VA 0x1259F08); the helper's only call (0x4C2B7A, in the AI driving step, car number = its argument) goes
  to drift code in the zero tail of .text (VA 0xE52880): fast + hard steering -> handbrake tap, then until the corner
  ends 3x turn force and 0.35x grip help. Settings at VA 0x1259F20 change live: `aimods_counters.ps1 -Set ...`.
  Untested in game; tuning expected.
  Research + addresses: `scratch/ai_mods_research.md`. Every player needs the same setting. Backups: `Blur REBORN
  (before ai mods).exe` in both test copies.
- **Released 2.6.1 (2026-11-17):** Russian fix, Game tweaks tab (more AI, harder AI, AI use mods = Front Runner + 2
  random mods), lasting car numbers online, REBORN normal-start fix. Power-up icons ON HOLD (`PowerupIcons.OnHold`: the
  section is hidden and no picture is built; they did not work in game). Full robot: English 252/252; Russian 251/252, the
  one fail (Auto bumps) is leftover test data: the saved scratch map keeps the last run's bumps + seed, so a second run in
  any language makes the same bumps (English failed the same way on its second run).
- **Power-up icons, round 4 (2026-11-17, dev only, OnHold = false here; 2.6.1 shipped with them on hold):** user 1 test
  with numbers 1-8: the power-up options MENU shows them (perk_icons.ipf in root.pak works), the ROAD did not: the race
  reads the pickup models from cacheoot.pak (loaded first). Fix: Blur REBORN reads rebrn/boot.pak (new ExePatch at
  0xAB23F4; setup backs up cacheoot.pak as baseoot.pak.original and links rebrnoot.pak to it); the build writes
  workoot_track.pak with the own pickup models (`Build.BuildBootPak` / `InstallBootPak`), road icons are no longer put in
  root. Normal Blur (icons_normal): cacheoot.pak = original + models, put back when off (`Skins.InstallNormalBoot`,
  installed.json normal_boot). Bolt's road pickup is the game's "xp" pickup (xppickup.model, icon_xp_01). The new
  ExePatch makes setup rerun once: MakeReborn rewrites Blur REBORN.exe from Blur.exe (AI mods come back at the next build).
- **Power-up icons, round 5 (2026-11-17, dev):** the road needs the pickup models in BOTH boot.pak and root.pak (the race
  reads Barge/Mine/Nitro/Repair from boot, Shunt/Shock/Shield/Bolt from root). The road card's middle is rebuilt from its
  sides (old symbol's glow gone). The HELD icons (behind the car) are not perk_icons.ipf: they are `perk_sheet` (DXT1 card)
  + `perk_sheet_inverted` (DXT5 symbol) in \ui\data\ui_common.bpr, 128x1024, 96 px per cell (mine, barge, shunt, shock,
  nitro, bolt 1, bolt 3, shield, repair, bolt 2) + `perk_sheet_support` (bolt 4/3/2/1, star): `HeldIcons.cs`.
- **Power-up icons, round 6 (2026-11-17, dev):** user: held icons work. Added: (1) WHOLE icon per power-up (`<key>.whole.png`,
  cut from the middle to 4:3 = 256x192, never stretched): its brightness fills the road card (`RoadIcons.MakeWhole`) and the
  held card (`HeldIcons.DrawWhole`; not-selected = same, alpha 130); the game still tints it; the menu (vector ipf) uses the
  picture's shape. (2) Bolt (2 left) / (1 left) kinds `bolt2` / `bolt1`, held only (road + menu use the Bolt of 3).
  (3) The held NOT-SELECTED look like the game's: perk_sheet_inverted = white, alpha = a glowing card with the symbol cut out
  dark (alpha 100, black) + brighter glow round it; SELECTED (perk_sheet) = white symbol with soft light round it.
- **Updater restart crash (2.6.0 -> 2.6.1, fixed after 2.6.1, not released):** after Update now, 2.6.0 showed a crash
  report (FileNotFound System.Diagnostics.Process in MainForm.Restart) and again on close. The update itself was fine (new
  exe in place, old one .old). Cause: a single-file exe reads its parts from its own file when first used; the swap had
  already replaced the file. Fix: `Updates.Install` uses Process / ProcessStartInfo before the swap, and the restart is
  wrapped in try/catch (also around the call: the missing part fails while Restart is compiled). Only updates FROM 2.6.1
  on get the fix; 2.6.0 users: press OK and start the map builder again.
- **Drifter AI ON HOLD (2026-11-17, user):** every AI gets Front Runner in slot 1 for now (setup code compares the AI
  number with 0 instead of 2, so the Drifter bits, drift driving, speed keeper and drift-car swap never run). The drift
  parts stay in the exe for later: drift code VA 0xE52880, speed keeper 0xDF6790 (forward push setting 0x1259F38),
  drift-car swap 0xDEEE60 + table 0xDF3F58 (A Mustang GT-R, B Challenger Tuner, C Shelby GT500, D ACS1 Coupe).
- **Russian bug, ALSO IN 2.6.0 (fixed 2026-11-17):** one ru.txt entry ("Could not import this file:
{}") was written
  over 3 lines, so its English became "{}" and matched every text: the whole window showed "Не удалось импортировать
  файл:". The entry is removed (line 138 has it right) and Lang.Use now skips any entry whose English is only "{}".
- **Icons "back to normal" (2026-11-17):** checked: user 2's packs have the player's icons (perk_icons.ipf 418728 bytes vs
  551894 original); the player's shunt picture is an arrow almost like the game's own. User 1 has no pictures set.
- **Held icons, round 3:** still empty in game after round 2 - the quads turned the wrong way: the game draws only
  triangles turning like its own (cross product > 0 in u, v); fixed in `HudIcons.Quad`, checked on the installed file.
- **Game tweaks, round 2 (2026-11-17, not released):** user tested: Hard AI + 19 AI work. Held icons were empty (also in the
  race settings' power-up options): the 3 floats at the shape header + 4 are NOT a colour (a black picture made the icon
  vanish) - now one shape, header untouched, from `PowerupIcons.Silhouette` (see-through left out; a picture with light and
  clearly darker parts: only the light parts, dark = holes; one shade = all filled). Road icons: card = dark silhouette,
  inverse = white silhouette (the game tints them). "Also in normal Blur" (tweaks.json `icons_normal`): `Skins.InstallNormal`
  now builds normal Blur's cache root.pak = original + skins (if on) + icons (if on), and runs after every Build + install.
  Build + install has a 4th card "Game tweaks" (Installed: work\installed\tweaks = tweaks.json + powerups\; not ticked =
  `GameTweaks.From` = that copy) and a tag per card (REBORN ONLY / REBORN + NORMAL BLUR IF ON). AI with mods (e.g. Drifter
  for 1-2 AI): NOT possible from data - network AI get `mUpgrades.OfflineValues()` in the game code; needs a code patch.
- **Done after 2.6.0 (not released yet): GAME TWEAKS tab** (group GAME between CARS and SHARE; Blur REBORN only, settings in
  `<data>\tweaks.json`, pictures in `<data>\powerups\`). `GameTweaks.cs`: "More AI" sets multiplayer.bin MultiplayerConfig
  `max_ai_per_console` 4 -> 19 (the network custom race AI count = min(free places, the race's Add AI, players x that));
  quickstart.lua adds 19 to the custom race "Add AI" list (2..18) when target.lua has `ai_max = 19`. "Harder AI" rewrites
  `gamedata\events\multiplayer_hard.csv` (per section 9 rows: to, distance, max track speed, min track speed, on, group,
  skill template, power-up template, blank): max speed 1.0, min 0.65, skill template 70 (= bossbattlet5_hard's). Icons:
  `HudIcons.cs` = held icons (`\ui\data\ipf\perk_icons.ipf`, vector: per power-up a group of a frame + symbol shapes, colour
  = 3 floats at header+4; the picture becomes <= 6 colour shapes of blocks, 72 grid; `_x2` = one white shape);
  `RoadIcons.cs` = road pickups (`\models\perks\pickups\<kind>pickup.model`: pictures icon_<kind>_01 + _inverse, 256 DXT1 9
  mips, data right after "DXT1" 0 3; the card keeps its glow, the old symbol is filled). No bolt road model exists. Test
  commands: mpconfig, harderai, hudicons, roadicons, bprnames, modelinfo. NOT tested in game yet: the 19th AI entry, skill
  template 70 in multiplayer, the tint of coloured icons, and whether root.pak beats boot.pak for the road pickups (they are in
  both; if not, REBORN needs its own boot.pak: "cache/boot.pak" is at 0xAB23F4 in the exe).
- **Done after 2.6.0 (not released yet): Play / Build only see Blur running from their OWN game folder** (`Play.GameRunning`
  checks the process path), so a second game copy on the same PC (J:lur - user 2) can run beside it. New test commands:
  `bmbcli install GAME` (= Build + install, cars pinned first) and `bmbcli carsto FROMGAME TOGAME OUTDIR` (FROM's cars
  exported as .blurcar and imported into TO, replacing TO's cars: a second player). Two-player test setup on one PC:
  J:lur - user 2 = a copy of user 1; its Blur REBORN.exe has the game's Windows-wide lock / event names changed (BlurMutex,
  MutexThreadPause, EventSignalQuit, StatusDirtyEvent, LayerStatusDirtyEvent, StartLoading, FinishedLoading, BaseDevice,
  DvcMutex, MemTrack, TimingMgr, ViewMutex: last letter -> 2), else the 2nd copy quits, loads forever, or freezes at race start; the original is kept as "Blur REBORN (original).exe". Both copies share the Blur save in AppData (backup: J:lur save backup).
- **Done after 2.6.0 (not released yet): a lasting number per custom car (online).** Online, Blur sends only each
  player's car uid (and paint uid); a uid the other game does not have shows as "Downloadable Content". Before, a custom
  car's uid was 0xCA400000 + 4 * its place in the list, so different players' cars clashed. Now each car has `net_id`
  (CarNetId.cs): new on New / Copy, kept by .blurcar export / import (an import that clashes with a car you have gets a
  new one), and cars from before get a random one the first time the app loads them (world saved at once; not from the
  name: every player's "My car" would clash). A .blurcar from 2.6.0 has no net_id: re-export it with the new version. Uids = 0xCA400000 + 4 * net_id
  (car, physics, paint, wheels); a clash with a game uid or an earlier car moves to the next free slot. Sound / logo uids
  stay per list place (local only). Checked with `bmbcli netidtest GAME WORLD.json`. Players must Build + install cars once.
- **Fixed after 2.6.0 (not released yet):** a normal start of Blur REBORN went to the custom LAN lobby by itself when the map
  builder was closed before the game (amax\plugins\quickstart	arget.lua stayed `enabled = true`). Now the target goes back to
  off 3 minutes after the game is seen, when the game closes, or when the map builder closes; the Map Tester does the same.
- **Done after 2.6.0 (not released yet):** the import-body warning no longer uses a fixed 10,000 triangles. It shows only
  when `CarUi.Fits` says the body does not fit the Bowler container, and gives the real numbers (points needed / room, about
  14,600 triangles). `CarUi.GlbMaxTris` is now worked out from the container's room (also used by the "Export a game car" hint).
- **Build + install: what to install** (2026-11-16): a window ticks Maps / Custom cars / Car skins (the changed ones pre-ticked).
  An unticked part goes in exactly as the last install had it: `Installed.cs` keeps copies in `work\installed\` (maps.json =
  the locations, cars.json, skins\ = skins.json + the pictures that are on), written after every good install (and by Build
  skins). Only skins ticked = the quick `Skins.Install`. `bmbcli build ... -maps|-cars|-skins remember` tested: unticked
  part = the old one. No speed gain beyond skins-only (a full build is ~10 s).
- **Export / Import tab** (SHARE group, `SharePanel.cs`, `Packs.cs`): .blurmap now also carries the map's imported models
  (`imports/`, a free slot on import) and the location's sky picture (`sky/`); .blurcar = car.json + body / sounds / logo
  files; .blurskin = skins.json (picture names kept: they are the game's slots, e.g. `..._scratch#DXT5`) + pictures. Import
  puts files in our folders under plain names; a different file of the same name gets _2. `bmbcli packtest` round-trips all
  three (twice: no doubles). Flame pipes: a replacer at most as many as the replaced car, a new car 2 (`CarExhaust.Max`).
- **Every driving sound** (after an in-game test: own gear sound fell back to the game's above ~100): the gear sounds play from
  the SHARED `Car_common_road.dat` (names Car_Common), which was not copied. Now, when a car changes a shared-bank kind, every
  .dat of its entity naming Car_Common is copied too (`MakePlans(..., common)`). More kinds: gearbox whine
  (`Transmission_straight/helix`), gear crunch (`gears_crunch_N`, `Grinding_loop_N`), damage hiss/fire (`steam_loop`,
  `car_fire02L`), and tyres: rolling (`*_Road`), skid (`Asphalt_Lateral_N`), brakes (`Asphalt_Brakes*`), wheelspin
  (`Asphalt_Spin`). Tyre kinds copy the tyre entity (Wheel_dry / Wheel_dry_AI, uids BA580000 + 16 i, bank `zb<i>el_dry`) and
  point the car's dryWheelAudioEntity / aiDryWheelAudioEntity at it (`MakeWheelPlans`). Not changeable per car (shared
  entities, not driving): collisions, bumps, puddles, rumble strips, off-road surfaces. `bmbcli usedsounds GAME CAR` lists
  every sound a car can play by .dat and bank. NOT heard in game yet.
- **Exhaust flames** (Body page > Exhaust flames: + Pipe, − Pipe, Game spots; fire dots in the 3D preview to drag; a mirrored
  twin moves with it): the AmaxVehicle record's `exhaustEmitters` = points in the car model's space (= the preview's space,
  checked with `bmbcli flamelook`). `car["exhaust"]` = [[x,y,z],...]; the build (`CarExhaust.Write`) overwrites the copied
  record's list, growing it with new items when there are more pipes (copies of the last one). Works for replaced cars too.
  `bmbcli exhausttest GAME WORLD.json`. Not seen in game yet.
- **Export a game car** (Body page > Export a game car (.glb), a menu by class): `CarExport.Glb` = every part at the finest
  detail, grouped (body with its picture, plain paint in the picture's main colour, glass, lights, tyres, rims, brakes,
  trim), axes as the body import reads them. ~30,000 triangles; checked by a round trip through the body import and a
  render. `bmbcli carexport GAME OUTDIR CAR...`.

## Community wishes (2026-11-15, after 2.4.0; NOT released, NOT tried in game)

- **Road side walls switch:** map panel "Road side walls (solid road edges)", map key `road_walls` (default on). Off: no edge walls
  in the collision (lane-split dividers stay) and none drawn in 2D (`Layout.RoadWalls`, `Network.WallRuns`).
- **Custom walls:** 2D Wall tool (W, group WALLS): click points, Enter / right click = done, Backspace / Esc; Erase tool deletes one.
  3D: library button "Draw a wall" (click on the ground or your own models, so the wall follows their height) + "Remove the last
  wall"; walls show as orange fences in 3D and in Collision. Map key `walls`: [{"points": [[x,z] or [x,y,z]], "h": 6}].
  Collision: `Layout.CustomWallPieces` = a strip facing each way (both sides solid), points every 8 m, from 1.5 m under the
  point to its height; invisible in game. Dry build checked: road walls off -> the collision file 364 KB -> 58 KB, walls in it.
- **Car replacer:** Custom car > Body: "Replace a game car (see your car online too)", car key `replace` = a game car's name.
  The build edits that game car's record in place: wheels (model + scales), the sound fields / own sound, DriverOffset for .glb
  bodies; its name, handling, stats, class and paints stay (fair online). Its own model file \modelsehicles\<car>.model gets this
  car's body (base model, wheel swap or .glb). Not added as a new car. Dry build checked (Viper ACR gets the Opel body file).
  Open: a Car skins skin on the same game car writes the same file (whichever is built last wins).
- **First test round fixes (2026-11-16):** road walls off now also removes the seen wall (the road model's wall mask is all
  "cut", `Network.WallMask`; the network cache key includes it). Collision tab shows the landscape (hill ground light green,
  its sides red). The 3D camera is kept per map (`View3D.CamForMap`, key = location id / map id). Custom car: the stats hide
  when a game car is replaced; the car preview sits on the right (sash "car_view_side") and keeps one size while turning
  (fits the circle the car sweeps). Replacer wheels: the record already pointed at the chosen wheels (checked with
  `bmbcli wheelof`); now the replaced car's own wheel file is also overwritten when no other game car uses it. Not tried in game.
- **Replacer wheels, second try:** in game the Mustang GT-R (record -> BMB_My_car_3 wheels) still showed its own wheels, so the
  game shows wheels from the car's own wheel file. Now the replacer leaves the record's wheel model alone and always writes the
  chosen wheels into the replaced car's own wheel file (a Legend / boss version sharing that file gets them too; the log says so).
  Wheel scale / depth fields still come from the chosen wheels' car. Also new: preview handles (wheels, body move, size), ground
  grid, camera buttons (Top / Side / Front / Orbit). Not tried in game.
- **Replacer, third round:** wheels do change in game (Shelby got the Foose rims), but they sit where the replaced car's own
  handling puts them, while the .glb body was fitted around the body car's wheels. Now an imported body on a replacer is fitted
  around the replaced car (`CarModel.FitCar` / `FitOf`: preview, side view and the built model), its wheel spacing (track / wb)
  is ignored and hidden (the replaced car keeps its wheel spots), and the driver seat comes from the replaced car. A game car can
  be replaced by one custom car only (the list marks taken ones). Not tried in game.
- **Replacer, fourth round (the user's choice):** fitting the body around the replaced car's wheels did not suit bodies of every
  shape, so it is back to the body car (as for a new car), and the replaced car takes this car's wheel spots too: the build
  clones the replaced car's dynamics (CA4000x1 = its handling) with geometry.wheelBase / trackWidthFront / trackWidthRear from
  the body car + the fit, and the record points at that copy. Body, tyres and their spots are this car's; the rest of the
  handling and the stats stay the game car's. Dry build checked (Mustang GT-R -> CA400001). Tried in game: works.
- **Bigger imported bodies (2026-11-16):** the room is the free trim space in a game model (`bmbcli glbroom GAME` lists every
  car: Bowler Nemesis ~14,600 triangles is the most; Opel ~9,600, Navara Rat ~6,700, Navara OffRoad ~8,800). Now every imported
  body is written into the Bowler model (`CarGlb.Container`), whatever the class; GlbBody stays the class's start car for a new
  car's handling. The fit, the preview, the wheel spots (geometry = Bowler's + fit), the driver seat and the wheel swap all use
  the container. NOT built / tested yet when written (the command checker was down). Later: grow model files for 20,000+.

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

### Start-up show and sounds (2026-11-16)
- Start sound (assets/startup.wav) and a "build done" sound (assets/build_done.wav) are built into the exe (converted to 16-bit WAV with ffmpeg from the user's files). A failed build still gives the Windows error sound.
- The splash is a see-through window over the whole screen, on its own thread, in the light-painting style of the user's reference video: an orange and a green stroke swoop in from the screen edges and each draw half of a rounded neon frame around the picture; the tails fade, the frame stays lit and flashes when it closes, then everything fades. Points every 3 px for smooth lines. A click on the picture ends it sooner. Built; the user checks the look.
- Splash Lab (`splash_lab.bat` / `--splash-lab`, src/Bmb.App/SplashLab.cs): sliders for speed, stroke count (1-6, the frame is shared out between them, each swoops in from the nearest screen edge), thickness, taper, glow, tail, gap, hold, and a colour per stroke; Play shows the real splash, Replay loops. Save writes `splash_style.json` next to the exe and the start-up reads it. Before a release: bake the user's saved values into SplashStyle's defaults (the json isn't shipped). `reopen.bat` closes and reopens the app on J.
- 2026-11-16: the user's lab look is baked into SplashStyle's defaults (2 strokes, speed 0.79, thick 1.13, glow 2.16, gap 0.19) and goes out in the next push. The start sound is now a style setting (Sound) too. The Splash Lab stays OURS ONLY: no button or menu, only `--splash-lab`; don't ship or push `splash_lab.bat` / `reopen.bat`, and don't mention the lab in release notes. If the user saves again (e.g. the sound), re-bake from bin/Release/net10.0-windows/splash_style.json.

### Custom car tab layout + game-car export by parts (2026-11-17)
- The Body page is now cards: IN GAME (new car or replace), 3D MODEL (body + wheels pickers, import / export / game body; an imported body shows its fit as steppers with their numbers: Body = size / forward / up, Wheels = front width / rear width / length, then Turn 90° (shows the angle) and Reset fit), LOOK (paint with a note when it can't be changed, logo with a "game logo" placeholder, menu + mini icon), EXHAUST FLAMES, then the STATS card. Body, wheels and menu icon lists show nice names (Skins.Nice through the combo Format event). Russian lines added.
- Look-only pictures: `BMB_ONLY=carui` self-test mode (`scratch/run_carui.sh`, add `BMB_LANG=ru`) saves carui_plain / _plain_down / _glb / _glb_small / _replace in scratch\ui.
- Export a game car (.glb): every game part is its own object with the game's element name and tree (car > e_exterior > doors > mirrors; e_wheels > each wheel > tyre / rim / disc / caliper), a wheel's object point at its middle; looks are shared per kind. Import back still reads it as one body. CLI `carelements GAME CAR` lists a car's elements and meshes.

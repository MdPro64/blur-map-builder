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
- **Release:** version 2.3.0 (2.0.0 on 2026-10-31, 2.1.0 with Car skins on 2026-11-03, 2.2.0 plain-paint skins, 2.3.0 Landscape / Simple layout on 2026-11-10), file name `Blur Map Builder.exe`, the name the Python updater looks for. It updates itself from GitHub releases like Python did; the last version stays beside it as `.old`. `Blur Map Tester.exe` stays the small Python one (it uses the same files). Released 2026-10-31 as v2.0.0; the source is in the `csharp` folder of the GitHub repo.
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

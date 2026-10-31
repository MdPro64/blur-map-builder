# For developers

`src\Bmb.Core` = formats + build (no window), `src\Bmb.App` = the window, `src\Bmb.Cli` = test tool, `assets` = our own files built into the exe (Amax plugin, icon, start-up picture).

In the window: `MainForm.cs` shell + keys + layouts, `Panels.cs` right panel + checks + Lighting, `Dialogs.cs` help + build + Play + start of the program, `MapView.cs` 2D, `View3D.cs` scene + handles + camera, `View3DPanels.cs` 3D bars + library + object list + Path scatter, `CarPanel.cs` Custom car, `Look.cs` rounded buttons + icon + short help cards, `Lang.cs` + `assets\ru.txt` + `assets\ru_keys.txt` the Russian (a robot run with BMB_LANG=ru lists what is missing in `scratch\ui\lang_missing.txt`), `SelfTest.cs` + `SelfLook.cs` the robot.
In the core: `Ui.cs` what the window needs (previews, car numbers, sharing, updates, Play), `Setup.cs` the first start.

## Decisions

- .NET 10 (LTS; .NET 8 ends in November 2026). The local SDK is in `.dotnet`, set `DOTNET_ROOT` to it.
- Windows Forms: starts fast, resizable panels come ready made. OpenGL by plain P/Invoke (`Gl.cs`), fixed function, no NuGet package:
  2D and 3D share one control, so a resize or a tab change costs nothing.
- `world.json` is kept as plain dictionaries (`JObj`), never as classes: unknown keys survive, and Python and C# can share the file.
- The port follows the Python line by line, so a difference can be found by comparing. Same number rules (`Py`), same
  random numbers (`PyRandom` = Python's Random, `NumpyRng` = numpy's default_rng), same picture resize (`Pic.ResizeLanczos` = Pillow).
- Faster because: no temp files between steps, no re-parsing of the whole database per edit, compression and picture
  coding on all cores, memory-mapped packs.
- The add-on's reader is not used. `CpModel.Read` walks the same layout without reading vertex values, and it stops
  on a broken model instead of running away (the Python reader took 50 GB on one here).
- Not ported on purpose: Pillow's line / polygon / text drawing, its BC3 packer, numpy's normal(). GDI+ and a simple packer
  take their place (see STATUS.md).

## Rebuild

```bash
.dotnet/dotnet.exe build BlurMapBuilder.slnx -c Release
```

```bash
.dotnet/dotnet.exe publish src/Bmb.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Tests

All of them work in `scratch\` (nothing goes to a game folder). First make the test area:

```bash
powershell -ExecutionPolicy Bypass -File tools/make_scratch.ps1
```

- `python tools/compare_core.py`: formats against Python on the real game files (needs `DOTNET_ROOT`).
- `bash tools/run_py_build.sh py_full`: the unchanged Python build on the scratch copy.
- `bmbcli build GAME MAPS WORK LEVELS [dry] [nocars] [noroot]`: the C# build.
- `python tools/compare_build.py scratch/py_full scratch/cs_full`: two builds, file by file.
- `bash tools/run_ui_test.sh`: the window with a robot mouse (every tab); `scratch\ui\ui_timing.txt` + screenshots. It needs `scratch\sample.glb` (any .glb) and uses `BMB_PREVIEW` so hover pictures stay in `scratch`: `base` is a link into the game.
- `python tools/audit_ui.py`: every text of the Python window that the C# source does not hold (finds forgotten tools).
- `python tools/compare_setup.py scratch/setup_game "J:/blur - user 1"`: the C# first start against Python's. The exe's `--setup` runs it; `scratch\setup_game` needs REAL copies of Blur.exe, cache\gamedata.pak + root.pak, levels\showroom and amax\plugins\plugins.lua.
- `bmbcli bake GAME`: Bake 3D pictures, with `BMB_PREVIEW` set.
- `python tools/py_ui_bench.py`: the Python window's speed.

`scratch\game\Blur Map Builder\base` is a LINK to the game's base files while the test area exists. Remove it with
`cmd /c rmdir` before deleting `scratch`: a recursive delete could follow it into the game folder.

The app reads `BMB_GAME`, `BMB_MAPS`, `BMB_WORK`, `BMB_PACKS`, `BMB_LEVELS`, `BMB_PREVIEW`; a real run uses the game folder for all of them.

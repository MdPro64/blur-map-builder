# Blur Map Builder — by MDPRO

> **Needs Blur PC version 1.2.** Blur 1.0 is not supported.

Make your own tracks for **Blur** (Bizarre Creations, 2010, PC): draw the road, add branches, lane splits, bridges,
power-ups, surfaces (dirt, water...), scenery and lighting — then race them in the game with **Blur REBORN**.

![Blur Map Builder](release/icon.ico)

## What you get

| File (in your Blur folder) | What it is |
|---|---|
| **Blur Map Builder.exe** | the map editor (2D / 3D / collision / AI / lighting / surface tabs), Build + install |
| **Blur Map Tester.exe** | starts the game straight into a LAN race on the map you built last |
| **Blur REBORN.exe** | made on your PC from your own `Blur.exe`: plays the game with your maps |

Your original `Blur.exe` and the game's own files are **never changed** — normal Blur keeps working as before.

## Install (players)

1. Download **Blur Map Builder.exe** and **Blur Map Tester.exe** from the
   [latest release](../../releases/latest).
2. Put both into your **Blur game folder** (where `Blur.exe` is).
3. Start **Blur Map Builder.exe**. The first start sets everything up from your own game files (about 5 minutes,
   once; an internet connection is needed once for the model reader).
4. Draw a map → **Build + install** → **Play** (or start **Blur Map Tester.exe**).

* Needs: **Blur for PC version 1.2** (retail / Steam) — it does **not** work with Blur 1.0; Windows 10/11. Amax (the Blur online mod) is needed for the quick-test LAN
  race (Map Tester); everything else works without it.
* Windows SmartScreen may warn about a new program: *More info → Run anyway*.
* Updates: the map builder checks this page when it starts and offers new versions. Your maps are kept
  (they live in `Blur Map Builder\maps` in your game folder).
* Something went wrong? The map builder saves a report in `Blur Map Builder\logs` — send it to **mdproii** on
  Discord.

## For developers

The whole map builder is plain Python — read it, change it, build your own.

```
pip install numpy pillow PyOpenGL pyopengltk
python road_designer.py          # (from a checkout inside your Blur folder)
python release/build_exe.py      # the two .exe files (needs pyinstaller)
```

* `road_designer.py` + `designer_*.py` + `view3d.py` — the editor
* `flat_earth/` — the build: roads (`road.py`, `road_mesh.py`, `network.py`), race path (`gen_aid.py`), level
  (`build.py`), surfaces, lighting, start cameras (`intro_cam.py`: the `.cycle` camera format)
* `kslf/`, `pak/`, `aid/`, `physics/`, `model/`, `tex/`, `ui/` — readers / writers of the game's file formats
* `setup_game.py` — the first start (everything made from the player's own game); `paths.py` — where things are
* `amax_plugin/quickstart/` — the Amax plugin that opens a LAN race on your map
* `research/tools/` — reverse-engineering helpers used while decoding the game

## Credits

* **MDPRO** — Blur Map Builder
* **Bizarre Creations / Activision** — Blur (this is a free fan project; you need your own copy of the game)
* **The Amax team** — Amax
* **TimberStalker** — [BlurImportExport](https://github.com/TimberStalker/BlurImportExport) (the model reader is
  downloaded from his repository on the first start; it is not included here)
* Python, numpy, Pillow, PyOpenGL, pyopengltk

License: MIT (the map builder's own code — see `LICENSE`).

# Blur Map Builder — Guide

Every button of the map builder, tab by tab. The numbers on each picture match the list next to it.

> **Needs Blur PC version 1.2.** Install: see the [main page](../README.md#install-players).

## Your first map in 5 steps

1. **New map** (Ctrl+N) in the left panel, give it a name.
2. Pick **Add point (A)** and click on the map to draw the road. Use **Move (M)** to drag points.
3. **Auto rows** in the right panel puts power-ups along the road.
4. **Build + install** (Ctrl+B), wait until the Building window says it is done.
5. **Play** (F5): start Blur REBORN, or use **Blur Map Tester.exe** for a quick LAN race on your map.

Press **F1** in the map builder at any time to see every key.

---

## 1. Top bar and 2D tools
The tabs, the buttons that are always there (Check for updates, Save, Play, Build + install) and the tools of the
2D map tab.

![Top bar and 2D tools](guide/01_top_bar_and_tools.png)

## 2. Locations, maps and the map view
A **location** is a world with its own lighting and grip; its **maps** are the tracks in it.

![Locations and maps](guide/02_maps_panel.png)

## 3. Right panel — the map
What you see when nothing is selected: the settings of the whole map, the automatic scenery, the minimap and
the checks.

![Right panel - the map](guide/03_sidebar_map.png)

## 4. Right panel — a point selected
Click a road point with **Select (S)**: its settings appear at the top of the right panel.

![Right panel - a point](guide/04_sidebar_point.png)

## 5. Right panel — a power-up selected
Click a power-up or a row with **Select (S)**.

![Right panel - a power-up](guide/05_sidebar_powerup.png)

## 6. The 3D tab
See your map in 3D, place the game's own objects (or your own 3D models) and set the camera before the race.

![3D tab](guide/06_3d.png)

## 7. The Collision tab
What the car will hit in the game: check it before you build.

![Collision tab](guide/07_collision.png)

## 8. The AI path tab
Where the AI cars drive.

![AI path tab](guide/08_ai_path.png)

## 9. The Lighting tab
The light of the whole location (all its maps). Change it, Build + install, look in the game.

![Lighting tab](guide/09_lighting.png)

## 10. The Surface tab
Paint what the road is made of, and how much it changes the driving.

![Surface tab](guide/10_surface.png)

## 11. Import your own 3D model (.glb)
3D tab > **Import 3D model (.glb)**. The map builder checks the model (size, triangles, textures) before it joins
your object library under **Your models**; then place it like any other object.

* **.glb** = one file with the model and its textures. Blender: *File > Export > glTF 2.0*, Format *glTF Binary*.
* Keep it light: under 10,000 triangles is best (more may make the game lag; over 50,000 is refused).
  Textures are shown at 256 x 256 in the game.
* **Collision:** none - cars drive through it. Tick **Solid** to make it a wall: a simple **box** around the whole
  model, not its exact shape (so don't use a model as a road or ramp).
* Want to try it? [samples/MDPRO_test_crate.glb](../samples/MDPRO_test_crate.glb) - a 4 m crate, every side labelled.

![Import a 3D model](guide/11_import_model.png)

## 12. A new version
The map builder checks for a new version when it starts (and when you click **Check for updates**).

![A new version](guide/12_update.png)

## 13. Updating
The download runs while the window keeps working - don't close it: the map builder restarts by itself.

![Updating](guide/13_update_download.png)

## 14. Update source
The **gear** next to Check for updates: where updates come from. If MDPRO stops making updates, someone else can
carry on - put their GitHub page here. Only use a page you trust.

![Update source](guide/14_update_source.png)

---

Problems? The map builder saves a report in `Blur Map Builder\logs` — send it to **mdproii** on Discord.

*(The pictures are made by `release/guide/capture_guide.py` and `annotate_guide.py`, so they can be made again
when the map builder changes.)*

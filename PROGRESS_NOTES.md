# Our Blur map builder: where we are

Hi! This is the easy-to-read version of what we've built so far and what's still on the list.
(The full technical diary is in `_mod_project\LOG.md`, if you ever want the details.)

Last updated: 15 October 2026

---

## What we've done so far

**Getting a new track into the game**
- Figured out how Blur stores its tracks, then added our own location, **Flat Earth**, and the map **Home Run v1**.
  Both show up in the game's menu.
- The race works properly: start grid, checkpoints, finish line, AI cars that follow the road, and the minimap
  during the race.
- Power-ups sit on the road, in rows of 5 or one at a time, and you pick their types.
- Bloom (the glow effect) can be switched on from the options menu.

**The road designer** (`Open road designer.bat`)
- Draw your road from above: add points, move them, make corners, set the width and height of each point.
- Curved roads, automatic bridges where the road crosses itself, and ramps.
- Several locations, and several maps in each one.
- Undo / redo, auto-save, and checks that warn you about problems (turns too tight, too steep...).
- One button builds everything and puts it in the game.

**How the track looks in the game**
- A TrackMania-style road: smooth grey road with raised white edges that curve up, and glowing blue lines.
- The road is now made of a few long pieces that follow the curves exactly. No more broken edges on bends and
  no more flickering. ✅ (you confirmed this one!)
- Solid walls along the road, so you can't drive off.
- Glowing chevron arrows on the bends, lamps, barriers, billboards, plus big ships, cranes, a ferris wheel and a
  radio mast far away so the world doesn't look empty.

**The 3D view** (buttons at the top: 2D / 3D / Collision, or F3)
- See your whole track in 3D before building it, using the same layout the game gets.
- **Collision** view: shows exactly where the car can hit things.
- Red warning beacons on problem spots.
- Blender-style controls:
  - middle mouse turns the view around the spot you clicked
  - Shift + middle mouse moves the view
  - the wheel zooms
  - the numpad keys give top / front / side views
- Click any object to select it, then move / rotate / scale it:
  - with the coloured handles (arrows, rings, cubes, like Blender)
  - with the G / R / S keys
  - or by typing numbers in the little panel at the bottom left
- Rotation works on all 3 axes, and scale can stretch along just one axis.
- Hide objects you don't want (X), and bring them back (Alt+H).
- Track points (the orange dots) can be moved in 3D too. Raise one and you get a ramp or a bridge.
- Power-ups show as small coloured rectangles. You can click them, move them, change their type, or delete
  them.
- **AI path** tab (next to 2D / 3D / Collision): see where the AI cars will drive. Leave it on Auto, make a
  racing line with one click, or draw the line yourself with the Pen. *(new today!)*

---

## Last things we did (please test!)

**The object library is here** (3D tab, side bar on the left).
- Every object in Blur that can be placed: **2,052 of them**, in 4 groups, each sorted by city and then by type
  (e.g. *Scenery > LA Long Beach docks > Containers*). Type in **Search** to find something fast.
  - **Scenery - look only**: just a shape (949). Tick *Solid* to make it a wall.
  - **Breakable - smash / knock over**: lamp posts, bins, fences, signs... (360)
  - **Moves when hit (physics)**: cars push them around (574)
  - **Animated**: objects with their own animation (169)
- Click an object in the list, then click in the 3D view to put it there. Click again for another one. **Esc** or
  right-click stops.
- Placed objects work like the others: click to select, then the handles or **G / R / S** to move / turn / scale.
  **X** deletes. Ctrl+Z undoes.
- **Solid** checkbox in the location / rotation / scale panel: ticked, the object becomes a wall the car can't drive
  through. Unticked, it's just a shape. It works on ANY object, the ships and cranes too. You can see the solid
  boxes in the *Collision* view (red).
- In the 3D view objects are boxes of their real size (colour = group). Real 3D shapes come later.
- Limits per location: the game has 144 places for scenery / moving / animated objects (the road pieces, start
  lines and ships use some of them), and 356 for breakable ones. If you place too many, the top bar says how many
  didn't fit.
- **Tested in the game (15 Oct): all good.** All 4 groups show up, and Solid works.

Earlier today: start / finish can be moved (drag the squares on the lines), corners fixed, 90 left / right turns
the rest of the road, the black barriers are gone, minimaps work, a new location builds.

---

## What's left to do

**Things to test in the game**
- [ ] Ships / cranes you scaled or stretched: do they look right in the game?
- [ ] Tilted objects (rotation X / Y) in the game
- [ ] Moved power-ups and raised track points (ramps) after a build
- [x] A second map works! (fixed the "cars keep respawning" bug: map 2's things now have their own ID numbers)
- [ ] The new **AI path** tab: try a racing line or draw your own, then race and watch the AI follow it
- [x] A second location, built and raced

**Next big features**
- [x] **Object library**: a side bar with every object in Blur, sorted into folders, so you can add new things
      to the map and choose how each one behaves:
      - solid (like a wall)
      - breakable (the car knocks it over)
      - just for looks
- [ ] Show the real 3D shapes of the objects in the 3D view (right now they're simple boxes)
- [ ] Duplicate objects (Shift+D, like Blender)

**Smaller things**
- [ ] Close the two ends of the road with walls (right now you can drive off the start and the end)
- [ ] The whole ground outside the walls is drivable. Maybe block it off
- [ ] Crowd sounds at the start line, and harbour sounds near the ships
- [ ] The route shape picture in the location menu
- [ ] Power-ups only appear when you're close. See if we can make them show from further away

**Done on 20 Oct (late):**
- [x] White sky / too much glow fixed: bloom starts OFF again (Options > Graphics > Bloom works) and maps with
      their own lighting keep their sky now (tested in the game)
- [x] Bridges: walls no longer climb up to the bridge (please check in game)
- [x] Play / Quick Start: no longer breaks when "Previously on Blur" shows (please check)
- [x] New maps start with only lights, arch, street lamps and arrow boards
- [x] Buttons no longer get wider when you hover them

---

## Quick reminders

- Start the designer with **`Open road designer.bat`** (F1 shows all the keys).
- After changes: **Save**, then **Build + install**, then play with **`Play modded Blur.bat`**.
- Close Blur before building, or the new ground file can't be copied in.
- Deleting a map or location only changes the designer. Press **Build + install** to remove it from the game too.
- Lots of maps / locations are fine now: the build makes room in the game's file list by itself (fixed 15 Oct,
  "build failed" when adding a new location).
- Ctrl+Z undoes almost anything in the designer, in 2D and in 3D.
- Working on the other PC? Copy the whole `E:\blur - bak` folder across, and everything comes with it.

**Sharing maps** *(new)*
- **Export this map** (left panel) saves a tiny `.blurmap` file (about 1-10 KB) - send it to a friend (Discord,
  e-mail...). They press **Import map**, then **Build + install** - their PC makes the game files itself.

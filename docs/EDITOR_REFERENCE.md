# Track editor reference: what good editors have, and what our Flat Earth designer has

Sources (2026-10-14 search): TrackMania 2020 (Wikipedia, player reports), ModNation Racers Track Studio
(PlayStation Blog walkthrough, GameSpot, Shacknews, Game Informer), rFactor / Chapman Track Creator, Fortnite Rocket
Racing (UEFN docs), SODA Off-Road Track Designer.

| Feature | Seen in | Our designer |
|---|---|---|
| Draw the road as a smooth curve through points | rFactor/Chapman splines, Rocket Racing splines | YES (Catmull-Rom through points) |
| Per-point width | ModNation advanced edit (road / fence / run-off per cross-section) | YES (width per point, `W` / `Q` keys) |
| Per-point height (lift), ramps, bridges | ModNation lift, rFactor elevation, piece editors' bridge pieces | YES (height per point + AUTO BRIDGES where the road crosses itself) |
| Sharp corners (90 degree) | piece editors (TrackMania blocks) | YES (corner points with a radius, `T` = make a 90-degree turn) |
| Banking | ModNation bank, rFactor banking transitions | not yet (the race file can store it; Flat Earth floor is flat) |
| Undo / redo | TrackMania (Ctrl+Z) | YES (30 steps, Ctrl+Z / Ctrl+Y) |
| Select tool + Move tool + property panel | all | YES (`S` select, `M` move, panel on the right) |
| Snap to grid | TrackMania block grid | YES (`G` toggles, 10 m) |
| Item / power-up placement | ModNation props, TrackMania items | YES (power-up rows `R`, single power-ups `P` with a type, `1`-`8` types) |
| Auto-populate | ModNation "Auto Populate" | YES ("Auto power-ups" = rows every N m; scenery is automatic) |
| Checkpoints / start / finish | Rocket Racing checkpoint devices, SODA checkpoints | automatic (start, 2 splits, finish along the road) |
| Validation before publishing | TrackMania (author time), Rocket Racing wrong-way warnings | YES (checklist: length, turn radius, crossings need bridges, ramp steepness, power-up limit) |
| Several tracks per world | TrackMania maps, ModNation | YES (locations and maps inside a location) |
| Track preview picture | TrackMania thumbnail | YES (made automatically from the road shape, used as the menu picture) |
| Test drive | TrackMania test mode, ModNation "drive the track" | partly: "Build + install" then play (the game must restart to load a new build) |
| Hotkeys + help overlay | all | YES (`F1` / `H` shows every key) |
| Autosave | common | YES (every change is kept in an autosave file; "Save" writes the real file) |
| Terrain sculpting / painting | ModNation deform / paint terrain | not possible yet (Flat Earth is one flat floor) |
| Copy / paste parts, mirror, reverse | TrackMania copy-paste | YES: reverse direction (`V`), mirror (`X`), duplicate map |
| Measuring / stats | common | YES (length, estimated lap time, tightest turn, steepest ramp) |
| Road surfaces / tunnels by painting | ModNation | not yet |
| Scenery placement | ModNation props, TrackMania items | automatic (ships, cranes, lamps, barriers, billboards); manual later |

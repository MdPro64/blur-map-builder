"""Press F9 in the game: saves the car pose + the nearest main-route (Logic) point to my_routes/start_pick.json"""
import json
import os
import time

import lap_line
import track_map as tm

g = tm.bt.Game()
keys = tm.bt.Keys()
print("Drive to your new start spot and press F9 in the game...", flush=True)
while not (keys.pressed(0x78) and g.has_focus()):
    time.sleep(0.01)
tm.bt.beep(1200)
pose = tm.bt.read_pose(g, tm.bt.find_physics(g))
t, pts = lap_line.logic_points(g)
x, _, z = pose["pos"]
k = lap_line.nearest_point(pts, x, z)
out = {"pos": pose["pos"], "rot": pose["rot"], "logic_index": k, "logic_count": len(pts)}
path = os.path.join(tm.HERE, "my_routes", "start_pick.json")
json.dump(out, open(path, "w"), indent=1)
print("Saved: car at (%.0f, %.0f), nearest route point %d of %d" % (x, z, k, len(pts)), flush=True)

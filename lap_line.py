"""
Move the race's start/finish line (lap line) to where your car is: press F9 in the game.

The race progress formula (code at 0xB04A80) is:
  progress = laps + ((your point on the main route - LINE) / points), wrapped into 0..1
where LINE is an int stored in the track data at +0x14 (track data = [world]... see track_map).
  T+0x14  lap/start line point index on the main (Logic) route (415 on the test track)
  T+0x1C  second line index (-1 on lap tracks; probably the finish of point-to-point races)
  T+0x38  1 / number of points,  T+0x3C  number of points,  T+0x40  point spacing (4 m)
A race restart reloads the track data, so run this again after a restart.
"""
import struct
import sys
import time

import track_map as tm

LINE = 0x14


def logic_points(g):
    t = tm.find_track_data(g)
    items, count = g.u32(t + 8), g.u32(t + 0x10)
    for i in range(count):
        s = g.u32(items + 4 * i)
        if g.read(s + 0x1C, 1)[0] & 0x20:
            ptr, n = g.u32(s + 0x48), g.u32(s + 0x4C)
            raw = g.read(ptr, n * tm.POINT_SIZE)
            return t, [struct.unpack_from("<2f", raw, k * tm.POINT_SIZE) for k in range(n)]
    sys.exit("No main route found.")


def nearest_point(points, x, z):
    return min(range(len(points)), key=lambda k: (points[k][0] - x) ** 2 + (points[k][1] - z) ** 2)


def set_line_at(g, pose):
    t, pts = logic_points(g)
    x, _, z = pose["pos"]
    k = nearest_point(pts, x, z)
    old = struct.unpack("<i", g.read(t + LINE, 4))[0]
    g.write(t + LINE, struct.pack("<i", k))
    print("Lap line moved: point %d -> point %d (of %d)." % (old, k, len(pts)), flush=True)
    return old, k


if __name__ == "__main__":
    g = tm.bt.Game()
    keys = tm.bt.Keys()
    print("Drive to where the new start/finish line should be and press F9 in the game...", flush=True)
    while not (keys.pressed(0x78) and g.has_focus()):
        time.sleep(0.01)
    tm.bt.beep(1200)
    set_line_at(g, tm.bt.read_pose(g, tm.bt.find_physics(g)))

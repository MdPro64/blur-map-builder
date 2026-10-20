"""Watch every racer's lap record (vtable 0xEC3488) while the user drives: prints changes of the
checkpoint state (+0x20: 0 -> 1 at split 1 -> 2 at split 2 -> lap++ at the start/finish) and lap (+0x24).
Read-only.  usage: python watch_laps.py [seconds]"""
import struct
import sys
import time

import numpy as np

sys.path.insert(0, r"E:\blur - bak\_mod_project\tools")
sys.path.insert(0, r"E:\blur - bak\map_builder")
import memscan  # noqa: E402
import track_map as tm  # noqa: E402

g = tm.bt.Game()
T = tm.find_track_data(g)
recs = []
for base, size in memscan.regions(g):
    a = memscan.read_array(g, base, size, np.uint32)
    if a is None:
        continue
    for i in np.nonzero(a == 0xEC3488)[0]:
        addr = base + 4 * int(i)
        if g.u32(addr + 8) == T:
            recs.append(addr)
print("lap records:", len(recs), flush=True)
if not recs:
    sys.exit("No race loaded: start a race first, then run this again.")
import route_edit  # noqa: E402
mine = next(route_edit.trackers(g))            # racer 0 = you
recs = [r for r in recs if g.u32(r + 0x18) == mine] or recs
print("watching %d record(s): %s" % (len(recs), " ".join("%08X" % r for r in recs)), flush=True)
seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 120
last = {}
t0 = time.time()
next_ping = 10
while time.time() - t0 < seconds:
    pose = tm.bt.read_pose(g, tm.bt.find_physics(g))
    for r in recs:
        d = g.read(r, 0xB0)
        if not d:
            continue
        state, lap = struct.unpack_from("<ii", d, 0x20)
        prog = struct.unpack_from("<f", d, 0xA4)[0]
        key = (state, lap)
        if last.get(r) != key:
            where = " car at (%.0f, %.0f)" % (pose["pos"][0], pose["pos"][2]) if pose else ""
            print("%6.1fs  record %08X  state %d  lap %d  progress %.2f%s" % (time.time() - t0, r, state, lap, prog, where), flush=True)
            last[r] = key
    if time.time() - t0 >= next_ping:
        if pose:
            print("%6.1fs  (still watching) car at (%.0f, %.0f)" % (time.time() - t0, pose["pos"][0], pose["pos"][2]), flush=True)
        next_ping += 10
    time.sleep(0.05)

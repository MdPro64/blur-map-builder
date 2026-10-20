"""Look for the scoring settings: round, increasing Drift/Super/Mega distances at +0xA0/+0xA4/+0xA8."""
import sys
import numpy as np
sys.path.insert(0, r"E:\blur - bak\_mod_project\tools")
import memscan
import blur_trainer as b

g = b.Game()
NAMES = {0x20: "MessageDelayTimer", 0x24: "ComboTime", 0x28: "DriftAngleMin", 0x2C: "DriftAngleMax",
         0x30: "DriftSlipMin", 0x34: "DriftSlipMax", 0x84: "Speed1MPH", 0x88: "Speed2MPH", 0x8C: "Speed3MPH",
         0xA0: "Drift", 0xA4: "SuperDrift", 0xA8: "MegaDrift", 0xBC: "Long", 0xC0: "Medium", 0xC4: "Short"}
for base, size in memscan.regions(g):
    a = memscan.read_array(g, base, size)
    if a is None or a.size < 0x60:
        continue
    d, s, m = a[:-2], a[1:-1], a[2:]
    with np.errstate(all="ignore"):
        ok = (d >= 5) & (d < s) & (s < m) & (m <= 5000)
        ok &= (d == np.round(d)) & (s == np.round(s)) & (m == np.round(m))
    for i in np.nonzero(ok)[0]:
        start = int(i) - 0xA0 // 4
        if start < 0 or start + 0xC8 // 4 >= a.size:
            continue
        ang_min, ang_max = a[start + 0x28 // 4], a[start + 0x2C // 4]
        if not (0 <= ang_min < ang_max <= 180):
            continue
        obj = base + 4 * start
        print("possible scoring settings at 0x%08X:" % obj,
              ", ".join("%s=%g" % (n, a[start + o // 4]) for o, n in NAMES.items()))


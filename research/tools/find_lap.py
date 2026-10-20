"""Find the lap counter in Blur's memory with 4 F8 snapshots taken while the user races.

  1. a few seconds BEFORE crossing the start/finish line        (lap N)
  2. just AFTER crossing it                                    (lap N+1)
  3. a bit later, still before the next crossing               (lap N+1)
  4. just after crossing the line again                        (lap N+2)
Keeps 4-byte numbers that went +1, stayed, +1. Prints the survivors.
"""
import struct
import sys
import time

import numpy as np

sys.path.insert(0, r"E:\blur - bak\_mod_project\tools")
sys.path.insert(0, r"E:\blur - bak\trainer")
import blur_trainer as b  # noqa: E402
import memscan  # noqa: E402

g = b.Game()
keys = b.Keys()
VK_F8 = 0x77


def wait_f8(msg):
    print(msg, flush=True)
    keys.pressed(VK_F8)
    while not keys.pressed(VK_F8):
        time.sleep(0.01)
    b.beep(1000)


wait_f8("1/4: a few seconds BEFORE the start/finish line, press F8")
regs = memscan.regions(g)   # list memory areas NOW (a race restart allocates new ones)
s0 = {}
for base, size in regs:
    a = memscan.read_array(g, base, size, np.int32)
    if a is not None:
        s0[base] = a
print("  snapshot 1 taken", flush=True)

wait_f8("2/4: just AFTER crossing the line (lap counter went up), press F8")
cands = {}
changed = 0
for base, size in regs:
    a0 = s0.pop(base, None)
    a1 = memscan.read_array(g, base, size, np.int32)
    if a0 is None or a1 is None:
        continue
    changed += int(np.count_nonzero(a1 != a0))
    m = (a1 == a0 + 1) & (a0 >= 0) & (a0 < 50)
    idx = np.nonzero(m)[0]
    if idx.size:
        cands[base] = (size, idx, [a0[idx], a1[idx]])
print("  numbers that changed since press 1: %d (0 = the game was frozen/paused)" % changed)
print("  candidates:", sum(c[1].size for c in cands.values()), flush=True)


def step(rule, msg):
    wait_f8(msg)
    for base in list(cands):
        size, idx, vals = cands[base]
        a = memscan.read_array(g, base, size, np.int32)
        if a is None:
            del cands[base]
            continue
        vals = vals + [a[idx]]
        m = rule(vals)
        if not m.any():
            del cands[base]
            continue
        cands[base] = (size, idx[m], [v[m] for v in vals])
    print("  candidates:", sum(c[1].size for c in cands.values()), flush=True)


step(lambda v: v[2] == v[1], "3/4: drive on (don't cross the line yet), press F8")
step(lambda v: v[3] == v[2] + 1, "4/4: just after crossing the line AGAIN, press F8")

print("\nRESULTS (address: values at the 4 presses)")
n = 0
for base, (size, idx, vals) in cands.items():
    for k, i in enumerate(idx):
        print("0x%08X: %s" % (base + 4 * int(i), " ".join("%d" % v[k] for v in vals)))
        n += 1
        if n >= 80:
            sys.exit()

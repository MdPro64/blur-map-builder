"""Find the live drift-distance number by snapshots taken with F8 while the user drives."""
import struct, sys, time
import numpy as np
sys.path.insert(0, r"E:\blur - bak\_mod_project\tools")
import memscan
import blur_trainer as b

g = b.Game()
keys = b.Keys()
VK_F8 = 0x77


def wait_f8(step):
    print(step, flush=True)
    keys.pressed(VK_F8)
    while not keys.pressed(VK_F8):
        time.sleep(0.01)
    b.beep(1000)


wait_f8("1/5: drive STRAIGHT (no drifting), press F8")
regs = memscan.regions(g)
s0 = {}
for base, size in regs:
    a = memscan.read_array(g, base, size)
    if a is not None:
        s0[base] = a
print("  snapshot 1 taken", flush=True)

wait_f8("2/5: start a LONG drift, press F8 in the middle of it")
cands = {}
for base, size in regs:
    a0 = s0.pop(base, None)
    a1 = memscan.read_array(g, base, size)
    if a0 is None or a1 is None:
        continue
    with np.errstate(all="ignore"):
        m = (a0 == 0) & (a1 > 0.2) & (a1 < 1e5)
    idx = np.nonzero(m)[0]
    if idx.size:
        cands[base] = (size, idx, [a0[idx], a1[idx]])
print("  candidates:", sum(c[1].size for c in cands.values()), flush=True)


def step(rule, msg):
    wait_f8(msg)
    for base in list(cands):
        size, idx, vals = cands[base]
        a = memscan.read_array(g, base, size)
        if a is None:
            del cands[base]
            continue
        vals = vals + [a[idx]]
        with np.errstate(all="ignore"):
            m = rule(vals)
        if not m.any():
            del cands[base]
            continue
        cands[base] = (size, idx[m], [v[m] for v in vals])
    print("  candidates:", sum(c[1].size for c in cands.values()), flush=True)


step(lambda v: v[2] > v[1] + 0.2, "3/5: KEEP drifting (same drift), press F8 again")
step(lambda v: v[3] < v[2], "4/5: stop drifting, drive straight for 2 seconds, press F8")
step(lambda v: (v[4] > 0.2) & (v[4] < 1e5), "5/5: start a NEW drift, press F8 in the middle of it")

print("\nRESULTS (address: straight, drift, drift later, after, new drift)")
n = 0
for base, (size, idx, vals) in cands.items():
    for k, i in enumerate(idx):
        print("0x%08X: %s" % (base + 4 * int(i), "  ".join("%9.2f" % v[k] for v in vals)))
        n += 1
        if n >= 60:
            sys.exit()


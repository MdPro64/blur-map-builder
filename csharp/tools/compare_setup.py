"""What the C# first start made (scratch\\setup_game) against what the Python first start made in the real game (read only).
usage: python compare_setup.py SCRATCH_GAME REAL_GAME"""
import filecmp
import hashlib
import json
import os
import sys

new, old = sys.argv[1], sys.argv[2]
nb, ob = [os.path.join(g, "Blur Map Builder", "base") for g in (new, old)]


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def same(a, b, name):
    ok = os.path.exists(a) and os.path.exists(b) and sha(a) == sha(b)
    print(("SAME " if ok else "DIFF ") + name)
    return ok


same(os.path.join(new, "Blur REBORN.exe"), os.path.join(old, "Blur REBORN.exe"), "Blur REBORN.exe")
for f in ("gamedata.pak.original", "root.pak.original"):
    same(os.path.join(nb, f), os.path.join(ob, f), f)
for f in ("level.level", "level.levelrender", "level.levelstream"):
    same(os.path.join(nb, "level_template", f), os.path.join(ob, "level_template", f), "level_template/" + f)
n = d = 0
for root, _, files in os.walk(os.path.join(ob, "unpacked")):
    for f in files:
        a = os.path.join(root, f)
        b = os.path.join(nb, os.path.relpath(a, ob))
        n += 1
        if not (os.path.exists(b) and sha(a) == sha(b)):
            d += 1
            print("   differs:", os.path.relpath(a, ob))
print("unpacked: %d files, %d differ" % (n, d))
if os.path.exists(os.path.join(ob, "..", "..", "amax", "plugins", "quickstart", "quickstart.lua")):
    same(os.path.join(new, "amax", "plugins", "quickstart", "quickstart.lua"), os.path.join(old, "amax", "plugins", "quickstart", "quickstart.lua"), "quickstart.lua")
A = {e["id"]: e for e in json.load(open(os.path.join(ob, "catalog.json")))["objects"]}
B = {e["id"]: e for e in json.load(open(os.path.join(nb, "catalog.json")))["objects"]}
print("catalog: python %d objects, C# %d, in both %d" % (len(A), len(B), len(set(A) & set(B))))
print("  only python:", list(set(A) - set(B))[:5])
print("  only C#:", list(set(B) - set(A))[:5])
diff = {}
box = szd = 0
for k in set(A) & set(B):
    a, b = A[k], B[k]
    for f in ("kind", "name", "loc", "cat", "model", "uid", "weight"):
        if a.get(f) != b.get(f):
            diff[f] = diff.get(f, 0) + 1
    if a["sized"] != b["sized"]:
        szd += 1
    elif a["sized"] and max(abs(x - y) for x, y in zip(a["lo"] + a["hi"], b["lo"] + b["hi"])) > 0.01:
        box += 1
        if box <= 3:
            print("  box:", k, a["lo"], a["hi"], b["lo"], b["hi"])
print("  fields that differ:", diff, " sized differs:", szd, " boxes differ:", box)
print("  sized: python %d, C# %d" % (sum(e["sized"] for e in A.values()), sum(e["sized"] for e in B.values())))

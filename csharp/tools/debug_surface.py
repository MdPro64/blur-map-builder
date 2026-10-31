"""prints the Python surface pieces of the scratch map, to compare with the C# ones (debug helper)"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MB = os.path.join(os.path.dirname(ROOT), "map_builder")
os.environ.update(BMB_GAME=os.path.join(ROOT, "scratch", "game"), BMB_INSTALLED="1")
sys.dont_write_bytecode = True
sys.path[:0] = [os.path.join(MB, "flat_earth"), MB] + [os.path.join(MB, p) for p in ("kslf", "pak", "aid", "physics", "car")]
os.chdir(os.path.join(MB, "flat_earth"))
import build as fe  # noqa: E402
import network  # noqa: E402
import road as rm  # noqa: E402
import surface  # noqa: E402
import world  # noqa: E402

w = world.assign_ids(world.load())
m = w["locations"][0]["maps"][0]
r = rm.for_map(m)
net = network.get(m, r)
print("paint cells", len(surface.settings(m)["paint"]), {k: v for k, v in surface.settings(m).items() if k != "paint"})
sp = surface.physics_patches(fe, m, net)
out = [{"mat": q, "c": [[round(v, 6) for v in c] for c in p[0]], "f": len(p[1])} for p, q in sp]
print(len(sp), sum(len(s["c"]) for s in out), sum(s["f"] for s in out))
json.dump(out, open(os.path.join(ROOT, "scratch", "py_patches.json"), "w"))
o = surface.overlay_chunks(fe, m, net)
print(len(o), [(c["key"], len(c["verts"]), len(c["strip"]), c["anchor"]) for c in o])
json.dump([[list(v) for v in c["verts"]] for c in o], open(os.path.join(ROOT, "scratch", "py_overlay.json"), "w"))

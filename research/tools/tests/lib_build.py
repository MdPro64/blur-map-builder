"""dry build with 4 library objects on map 1 (in memory), then check the packs"""
import copy, os, struct, sys
os.environ["FE_DRY"] = "1"
MB = r"E:\blur - bak\map_builder"
sys.path[:0] = [os.path.join(MB, "flat_earth"), os.path.join(MB, "kslf"), os.path.join(MB, "pak")]
import build as fe
import road_layout as RL
import build_track as B
import dump, pak

cat = RL.catalog()
pick = {}
for e in cat.values():
    for kind, name in (("scenery", "Cement Mixer"), ("breakable", "Crossing Sign"), ("movable", "Metro Vent"),
                       ("animated", "RH Police Car01")):
        if e["kind"] == kind and e["name"] == name and kind not in pick:
            pick[kind] = e
print({k: (e["id"], e["model"]) for k, e in pick.items()})
real_world = fe._world


def test_world():
    w = real_world()
    m = w["locations"][0]["maps"][0]
    m["objects"] = [{"id": pick[k]["id"], "x": 400.0 + 20 * i, "z": 2200.0, "h": 0.0, "turn": 0.3}
                    for i, k in enumerate(("scenery", "breakable", "movable", "animated"))]
    m["edits"] = dict(m.get("edits") or {})
    m["edits"]["obj:0"] = {"solid": True}
    return w
fe._world = test_world
out = r'E:\blur - bak\_mod_project\work\gamedata_track.pak'
# ---- checks
P = pak.Pak(out)
tmp = os.path.join(os.environ["TEMP"], "amax_test.bin")
open(tmp, "wb").write(P.read(P.find("gamedata/xt/amax.bin")))
A = dump.File(tmp)
du = RL.scenery_def_uid(pick["scenery"]["uid"])
oi = next(oi for u, t, oi in A.refs if u == du)
top = A.top(A.O[oi])
print("amax: scenery def %08X -> Model %s PhysType %s DrawDistance %s" % (du, top["Model"], top["PhysType"], top["DrawDistance"]))
names = [f["name"] for f in P.files if f["name"].lower().endswith("flat_earth\\actorpack.xt")]
print("actorpacks:", names)
ap = os.path.join(os.environ["TEMP"], "ap_test.bin")
open(ap, "wb").write(P.read(P.find(names[0])))
D = dump.File(ap)
want = {du: "scenery def", int(pick["movable"]["uid"], 16): "movable def", int(pick["animated"]["uid"], 16): "animated def",
        int(pick["breakable"]["uid"], 16): "breakable record"}
for u, t, o in D.refs:
    if u in want:
        print("actorpack refers to %08X (%s, %s)" % (u, want[u], D.T[t]["name"]))


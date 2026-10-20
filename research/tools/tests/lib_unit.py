"""road_layout library pieces with a fake catalog (no real catalog needed)"""
import copy, os, sys
os.environ["FE_DRY"] = "1"
MB = r"E:\blur - bak\map_builder"
sys.path[:0] = [os.path.join(MB, "flat_earth"), os.path.join(MB, "physics")]
import build as fe, gen_aid, road_layout as RL

RL._catalog = {
    "scenery:011AFD8D": {"id": "scenery:011AFD8D", "kind": "scenery", "uid": "011AFD8D", "name": "Container",
                         "lo": [-13.7, 0, 0], "hi": [0, 2.9, 2.4], "loc": "x", "cat": "y"},
    "breakable:9D39678A": {"id": "breakable:9D39678A", "kind": "breakable", "uid": "9D39678A", "name": "Lamp",
                           "lo": [-.6, 0, -.6], "hi": [.6, 10, .6], "loc": "x", "cat": "y"},
    "movable:12345678": {"id": "movable:12345678", "kind": "movable", "uid": "12345678", "name": "Box",
                         "lo": [-1, 0, -1], "hi": [1, 1, 1], "loc": "x", "cat": "y"},
}
m = copy.deepcopy(fe._world()["locations"][0]["maps"][0])
m["objects"] = [{"id": "scenery:011AFD8D", "x": 500.0, "z": 2100.0, "h": 0.0, "turn": 0.5},
                {"id": "breakable:9D39678A", "x": 520.0, "z": 2100.0, "h": 0.0, "turn": 0.0},
                {"id": "movable:12345678", "x": 540.0, "z": 2100.0, "h": 2.0, "turn": 0.0},
                {"id": "missing:0", "x": 0, "z": 0}]
m["edits"] = dict(m.get("edits") or {})
m["edits"]["obj:0"] = {"solid": True, "dx": 10.0, "scale": 2.0}
m["edits"]["ship:0"] = {"solid": True}
m["edits"]["tanker:0"] = {"solid": True}
road = fe.map_road(m)
_, info = gen_aid.build_path(road, fe.GROUND_Y, lines=gen_aid.race_lines(road, m))
tiles, tk = RL.tile_spots(fe, road, info, m, keys=True)
lamps, lk = RL.lamp_spots(fe, road, info, m, keys=True)
print("tile list", len(tiles), "obj keys:", [(k, t[:5]) for t, k in zip(tiles, tk) if k.startswith("obj")])
print("lamp list", len(lamps), "obj keys:", [(k, l[:5]) for l, k in zip(lamps, lk) if k.startswith("obj")])
print("scenery def uid %08X" % RL.scenery_def_uid("011AFD8D"))
boxes = RL.solid_boxes(fe, road, info, m)
print("solid boxes:", len(boxes))
for corners, faces in boxes:
    xs = [c[0] for c in corners]; ys = [c[1] for c in corners]; zs = [c[2] for c in corners]
    print("  x %.1f..%.1f  y %.1f..%.1f  z %.1f..%.1f" % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
pieces, decks = RL.collision_pieces(fe, road, info, m)
print("collision pieces", len(pieces))
print("scenery models in a world with this map:", RL.scenery_models({"locations": [{"maps": [m]}]}))

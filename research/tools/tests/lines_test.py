"""movable start / finish: race_lines + build_path + the level build (dry, into the scratchpad)"""
import copy, math, os, sys, struct
os.environ["FE_DRY"] = "1"
MB = r"E:\blur - bak\map_builder"
sys.path[:0] = [os.path.join(MB, "flat_earth"), os.path.join(MB, "aid")]
import build as fe
import gen_aid, aid, road_layout

fe.WORK = os.path.join(os.path.dirname(os.path.abspath(__file__)), "work_test")
wld = fe._world()
m = wld["locations"][0]["maps"][0]
road = fe.map_road(m)
print("Home Run v1: length %.0f m, %d points" % (road.length, len(road)))

for st, fin in ((None, None), (400, 1500), (0, 99999), (1200, 1000), (800, 900)):
    mm = dict(m)
    if st is not None:
        mm["start_m"], mm["finish_m"] = st, fin
    si, fi = gen_aid.race_lines(road, mm)
    data, info = gen_aid.build_path(road, fe.GROUND_Y, lines=(si, fi))
    sp = {s[0]: (s[2], s[3]) for s in aid.splines(data)}
    g = info["gates"]
    ok = (info["start_index"] == si and info["finish_index"] == fi and g["Finish"] + gen_aid.GATE_POINTS <= len(road)
          and g["Start"] >= 0 and g["Start"] < g["InterOne"] < g["InterTwo"] < g["Finish"])
    s0, s1 = road_layout._line_s(road, info)
    pk = road_layout.pickup_spots(fe, road, info, mm)
    pss = [road.project(p[0], p[2])[0] for p in pk]
    print("asked %-12s -> start %5.0f m  finish %5.0f m  gates %s  %s  power-ups %d between %.0f..%.0f m"
          % ((st, fin), road.dist[si], road.dist[fi], g, "OK" if ok else "BAD", len(pk), min(pss), max(pss)))

# default must equal the old fixed spots
si, fi = gen_aid.race_lines(road, {})
assert (si, fi) == (42, len(road) - 62), (si, fi)
print("defaults = old spots: OK")

# full level build of the location with a moved start
w2 = copy.deepcopy(wld)
w2["locations"] = w2["locations"][:1]
w2["locations"][0]["maps"][0]["start_m"] = 400.0
w2["locations"][0]["maps"][0]["finish_m"] = 1500.0
packs, aids, lists = fe.build_level_files(print, w2)
print("level build OK:", {k: len(v) for k, v in aids.items()})

"""Build "My First Track" (USA_LA_River_7): a copy of LA River route 6 with ITS OWN start line, checkpoints and
start grid, placed at the spot picked with pick_spot.py (F9). Writes the modded gamedata pack.

  python build_track.py                 builds from tracks/my_first_track.json (written by map_editor.py)
  python build_track.py --pick [f.json] sets the start from an F9 pick (my_routes/start_pick.json), then builds
Settings file: {"name", "location_name", "start" (Logic point), optional "ground_y",
                "pickups": {list uid: {"<index>": {"x", "z", optional "type"}}}}
Steps:
  1. usa_la_river_7.aid = route 6's .aid with Start_Finish / Inter_One / Inter_Two moved along the LOGIC route
     (same spacing as route 6: split 1 = start + 370 points, split 2 = start + 715)
  2. actorpack.xt: copies of route 6's start lists (grids FE3946E4 / FF08E43E with 20 StartPosition +
     4 StartLineInfo, rolling start B1CE1859), every positioned actor turned + moved to the new start
  3. amax.bin: route USA_LA_River_7 (copy of route 6, own name/thumbnail) using the new grid lists
  4. multiplayer.bin: route added to the custom-race list;  cityandroutenames.loc: names
  5. gamedata pack: all of the above (+ the new .aid as a NEW file in the pack)"""
import json
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [os.path.join(HERE, p) for p in ("kslf", "pak", "aid")]
import aid       # noqa: E402
import dump      # noqa: E402
import kslf_write  # noqa: E402
import loc       # noqa: E402
import pak       # noqa: E402

import paths      # noqa: E402
PROJ = paths.DATA
U = paths.UNPACKED
W = os.path.join(paths.WORK, "mod")
CITY = "usa_la_river"
SRC_ROUTE, SRC_ROUTE_OBJ = "usa_la_river_6", 3081
NEW_ROUTE, NEW_UID, NEW_LOC = "USA_LA_River_7", 0xFFFFFF01, 0xF00D0007
NEW_NAME = "My First Track"
# route 6 lists that sit at the start (-> uid of our moved copy): single-player grid, multiplayer grid,
# rolling-start scripts/splines/trigger (route 6 starts rolling, ~200 m before the line)
GRID_LISTS = {"FE3946E4": 0xFFFFFF10, "FF08E43E": 0xFFFFFF11, "B1CE1859": 0xFFFFFF12,
              "6770C5DD": 0xFFFFFF13}    # route 6's flares/effects: OUR copy puts every flare across the start line
MARKER_LIST = "6770C5DD"
# route 6's power-up lists (single player / multiplayer): copied as OUR lists only if the settings change them
PICKUP_LISTS = {"ECFB5B8B": 0xFFFFFF14, "5AAC9F7D": 0xFFFFFF15}
TRACK_FILE = os.path.join(paths.TRACKS, "my_first_track.json")
# power-ups float this far above the collision ground (median of all 357 original LA River power-up spots)
PICKUP_ABOVE_GROUND = 0.70
_ground = []


def ground():
    """the level's collision surface (decoded from levels/<city>/Level.levelphysics), loaded once"""
    if not _ground:
        path = os.path.join(paths.LEVELS, CITY, "Level.levelphysics")
        try:
            sys.path.insert(0, os.path.join(HERE, "physics"))
            import levelphysics
            _ground.append(levelphysics.Ground(path))
        except Exception as e:                      # keep building with the old guess
            print("collision ground not available:", e)
            _ground.append(None)
    return _ground[0]
SPLIT1, SPLIT2 = 370, 715          # route 6: start 235, split 1 605, split 2 950 (Logic point numbers)
GATE_LEAD = 2                      # a gate's first point sits 2 Logic points before its line
ROUTE6_LINE = 235                  # route 6's start line (Logic point)
START_TEXT = (551.3, 2270.3)       # route 6's "START" text effect (x, z): the middle of the road at the line


def logic_xz(d, L, i):
    p = aid.point(d, L[4] + i * aid.POINT)
    return p[0], -p[2]             # game x, z (the file stores z negated)


def pick_start(d, L, pick):
    """Logic point nearest the F9 spot whose direction matches the car's (the track overlaps itself)"""
    x, _, z = pick["pos"]
    fx, fz = pick["rot"][6], pick["rot"][8]
    m = L[3] - 1
    best = None
    for i in range(m):
        px, pz = logic_xz(d, L, i)
        nx, nz = logic_xz(d, L, (i + 1) % m)
        if (nx - px) * fx + (nz - pz) * fz <= 0:
            continue
        dist = (px - x) ** 2 + (pz - z) ** 2
        if best is None or dist < best[0]:
            best = (dist, i)
    return best[1]


def frame(d, L, i):
    """(x, z, heading angle) of the Logic route at point i"""
    m = L[3] - 1
    x, z = logic_xz(d, L, i)
    nx, nz = logic_xz(d, L, (i + 1) % m)
    return x, z, math.atan2(nx - x, nz - z)


def ground_height(d, start):
    """road height at Logic point `start`: low height (field 9) of the nearest MainLoop (AI line) point
    (the LOGIC route's heights are dummies)"""
    S = {s[0]: s for s in aid.splines(d)}
    L, M = S["LOGIC"], S["MainLoop"]
    x, z = logic_xz(d, L, start)
    best = min(range(M[3]), key=lambda i: (logic_xz(d, M, i)[0] - x) ** 2 + (logic_xz(d, M, i)[1] - z) ** 2)
    return aid.point(d, M[4] + best * aid.POINT)[9]         # field 9 = road surface height (10 = ceiling)


def build_aid(start):
    d = bytearray(open(os.path.join(U, "levels", CITY, SRC_ROUTE + ".aid"), "rb").read())
    S = {s[0]: s for s in aid.splines(d)}
    L = S["LOGIC"]
    m = L[3] - 1
    table_at, lists = aid.link_table(d, L)
    for name, offset in (("Start_Finish", 0), ("Inter_One", SPLIT1), ("Inter_Two", SPLIT2)):
        new_first = (start + offset - GATE_LEAD) % m
        aid.move_gate(d, S[name], L, new_first)
        # the game wakes a gate up through the Logic route's per-point link lists, which are stored in the
        # file: move them with the gate (route 6: lines at 235 / 605 / 950)
        aid.move_links(lists, name, (ROUTE6_LINE + offset - GATE_LEAD) % m, new_first, m)
    d = d[:table_at] + aid.write_links(lists)
    return bytes(d), L


def build_actorpack(d_aid, L, old_start, new_start, dy, ground_y, pickups=None):
    """copies of route 6's start lists where every positioned actor SLIDES along the Logic route by
    (new_start - old_start) points: it keeps its offset (along, sideways) and its heading relative to the
    nearest route point, so the grid / rolling start follow the road's bends."""
    m = L[3] - 1
    shift = new_start - old_start
    frames = [frame(d_aid, L, i) for i in range(m)]
    widths = [aid.point(d_aid, L[4] + i * aid.POINT)[3] for i in range(m)]

    def nearest(x, z):                 # only around the old start (the track overlaps itself elsewhere)
        rng = [(old_start + k) % m for k in range(-150, 40)]
        return min(rng, key=lambda i: (frames[i][0] - x) ** 2 + (frames[i][1] - z) ** 2)

    def slide(mat):
        """a Logic point is the EDGE of the road, which runs `width` m sideways from it: keep the actor's
        distance along the road, scale its sideways distance by the road width ratio (narrower road = no
        cars in the walls), keep its heading relative to the road"""
        x, z = mat[9], mat[11]
        j = nearest(x, z)
        jn = (j + shift) % m
        ox, oz, oa = frames[j]
        nx, nz, na = frames[jn]
        rot = na - oa
        c, s = math.cos(rot), math.sin(rot)

        def turn(vx, vz):
            return vx * c + vz * s, -vx * s + vz * c
        for r in range(3):                                    # ax, ay, az directions
            mat[3 * r], mat[3 * r + 2] = turn(mat[3 * r], mat[3 * r + 2])
        dx, dz = x - ox, z - oz
        along = dx * math.sin(oa) + dz * math.cos(oa)
        side = (dx * math.cos(oa) - dz * math.sin(oa)) * widths[jn] / widths[j]
        mat[9] = nx + along * math.sin(na) + side * math.cos(na)
        mat[11] = nz + along * math.cos(na) - side * math.sin(na)
        mat[10] += dy

    # which side of the Logic point the road lies on: route 6's "START" text sits in the middle of the road
    sx, sz, sa = frames[old_start]
    road_side = 1.0 if (START_TEXT[0] - sx) * math.cos(sa) - (START_TEXT[1] - sz) * math.sin(sa) > 0 else -1.0
    flare_no = [0]

    def flare_row(mat, count, ground_y):
        """put flare number k of `count` across the road at the new start line"""
        lx, lz, la = frames[new_start]
        w = widths[new_start]
        k = flare_no[0]
        flare_no[0] += 1
        side = road_side * w * (0.06 + 0.88 * k / max(count - 1, 1))
        mat[9] = lx + side * math.cos(la)
        mat[11] = lz - side * math.sin(la)
        mat[10] = ground_y

    path = os.path.join(U, "levels", CITY, "actorpack.xt")
    P = dump.File(path)
    uid2obj = {"%08X" % u: oi for u, t, oi in P.refs if oi != 0xFFFF}
    data = open(path, "rb").read()
    used = set()
    for o in P.O:
        for a in P.top(o)["LevelActors"]:
            if isinstance(a, dict):
                used.add(a.get("SerialisableUid"))
    new_uids = (u for u in range(0x7A000000, 0x7B000000) if u not in used)   # actor ids nobody uses
    type_ref = {"%08X" % u: i for i, (u, t, oi) in enumerate(P.refs)}     # PickupType uid -> ref index
    # height for moved power-ups: the collision ground under the spot (pickup_height). The 3 nearest ORIGINAL
    # power-up spots only pick the right level on bridges (and are the fallback).
    spots = {}
    for o in P.O:
        for a in P.top(o)["LevelActors"]:
            if isinstance(a, dict) and a.get("_type") == "PickupPosition":
                w = a["WorldMatrix"]["aw"]
                spots[(round(w["vx"], 1), round(w["vz"], 1))] = w["vy"]
    spots = [(x, z, y) for (x, z), y in spots.items()]

    def guess_height(x, z):
        near = sorted(spots, key=lambda p: (p[0] - x) ** 2 + (p[1] - z) ** 2)[:3]
        w = [1 / (math.hypot(p[0] - x, p[1] - z) + 1) for p in near]
        return sum(p[2] * k for p, k in zip(near, w)) / sum(w)

    def pickup_height(x, z):
        """the real ground (collision mesh, physics/levelphysics.py) + PICKUP_ABOVE_GROUND; on bridges /
        tunnels the surface closest to the old nearby-power-up guess. Falls back to that guess."""
        guess = guess_height(x, z)
        g = ground().ground(x, z, near_y=guess) if ground() else None
        return guess if g is None else g + PICKUP_ABOVE_GROUND
    lists = dict(GRID_LISTS)
    lists.update({u: PICKUP_LISTS[u] for u in (pickups or {}) if pickups[u]})
    for old_uid, new_uid in sorted(lists.items(), key=lambda x: x[1]):   # new uids must go UP (sorted table)
        oi = uid2obj[old_uid]
        obj = P.O[oi]
        if old_uid in PICKUP_LISTS:
            edits = pickups[old_uid]

            def patch(buf, pool, blocks, obj=obj, types=P.T, edits=edits):
                """power-ups stay where they are, except the ones the settings move / change"""
                for bi, b in enumerate(obj["blocks"]):
                    if b["kind"] == 0 and derives(types, b["type"], "ActorCreationData"):
                        for e in range(b["count"]):
                            struct.pack_into("<I", buf, blocks[bi] + e * b["size"], next(new_uids))
                    if b["name"] != "PickupPosition":
                        continue
                    for e in range(b["count"]):
                        ed = edits.get(str(e))
                        if not ed:
                            continue
                        at = blocks[bi] + e * b["size"]
                        if "x" in ed:                                     # WorldMatrix (+12) position aw (+36)
                            struct.pack_into("<f", buf, at + 12 + 36, ed["x"])
                            struct.pack_into("<f", buf, at + 12 + 44, ed["z"])
                            y = ed["y"] if "y" in ed else pickup_height(ed["x"], ed["z"])
                            struct.pack_into("<f", buf, at + 12 + 40, y)
                        if ed.get("type"):
                            struct.pack_into("<I", buf, at + 60, type_ref[ed["type"]])   # PickupType ref
            _, data = kslf_write.clone_object(None, None, oi, None, new_uid, patch, data_in=data)
            P = _reload(data)
            continue
        flares = sum(b["count"] for b in obj["blocks"] if b["name"] == "EffectGenerator") if old_uid == MARKER_LIST else 0
        flare_no[0] = 0

        def patch(buf, pool, blocks, obj=obj, types=P.T, flares=flares):
            for bi, b in enumerate(obj["blocks"]):
                if b["kind"] == 0 and derives(types, b["type"], "ActorCreationData"):
                    for e in range(b["count"]):             # every actor needs its OWN SerialisableUid (+0)
                        struct.pack_into("<I", buf, blocks[bi] + e * b["size"], next(new_uids))
                if b["kind"] != 0 or not positioned(types, b["type"]):
                    continue
                for e in range(b["count"]):
                    at = blocks[bi] + e * b["size"] + 12          # WorldMatrix (Mat43) of a PosActorCreate
                    mat = list(struct.unpack_from("<12f", buf, at))
                    if flares and b["name"] == "EffectGenerator":
                        flare_row(mat, flares, ground_y)
                    else:
                        slide(mat)
                    struct.pack_into("<12f", buf, at, *mat)
        _, data = kslf_write.clone_object(None, None, oi, None, new_uid, patch, data_in=data)
        P = _reload(data)                           # (existing objects keep their indices)
    return data


def derives(types, t, name):
    if types[t]["name"] == name:
        return True
    return any(derives(types, b, name) for b, _ in types[t]["bases"])


def positioned(types, t):
    """True if type t derives from PosActorCreate (WorldMatrix at +12; children like spline knots and trigger
    boxes are stored relative to it, so moving the matrix moves them too)"""
    if types[t]["name"] == "PosActorCreate":
        return True
    return any(positioned(types, b) for b, _ in types[t]["bases"])


def _reload(data):
    import tempfile
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".xt")
    f.write(data)
    f.close()
    try:
        return dump.File(f.name)
    finally:
        os.unlink(f.name)


def build_amax(list_map):
    def patch(data, pool, blocks):
        struct.pack_into("<i", data, blocks[0] + 16, 7)            # Index
        struct.pack_into("<I", data, blocks[0] + 20, NEW_LOC)      # LocString
        struct.pack_into("<I", data, blocks[0] + 32, len(pool))    # Thumbnail -> own value
        pool += b"LA_River7\0"
    tmp = os.path.join(W, "amax_step1.bin")
    kslf_write.clone_object(os.path.join(U, "xt", "amax.bin"), tmp, SRC_ROUTE_OBJ, NEW_ROUTE, NEW_UID, patch)
    A = dump.File(tmp)
    refs = {"%08X" % u: i for i, (u, t, oi) in enumerate(A.refs)}

    def swap_lists(ctx):
        new_idx = {refs[old]: ctx.add_uid(new, "LevelActorList")
                   for old, new in sorted(list_map.items(), key=lambda x: x[1])}
        for bi, (t, cnt, kind) in enumerate(ctx.rows):
            if ctx.types[t]["name"] == "LevelActorList" and kind == 2:
                for e in range(cnt):
                    at = ctx.blocks[bi] + 4 * e
                    v = struct.unpack_from("<I", ctx.data, at)[0]
                    if v in new_idx:
                        struct.pack_into("<I", ctx.data, at, new_idx[v])
    out = kslf_write.edit_object(tmp, os.path.join(W, "amax.bin"), len(A.O) - 1, swap_lists)
    os.unlink(tmp)
    return out


def build_multiplayer():
    def add_route(c):
        ROUTES, ARENAS = 160, 168
        blk, first, cnt = struct.unpack_from("<HHI", c.data, ROUTES)
        ablk, afirst, acnt = struct.unpack_from("<HHI", c.data, ARENAS)
        ref = c.add_uid(NEW_UID, "Route")
        c.insert(blk, first + cnt, struct.pack("<I", ref))
        struct.pack_into("<HHI", c.data, ROUTES, blk, first, cnt + 1)
        struct.pack_into("<HHI", c.data, ARENAS, ablk, afirst + 1, acnt)
    return kslf_write.edit_object(os.path.join(U, "xt", "multiplayer.bin"), os.path.join(W, "multiplayer.bin"),
                                  585, add_route)


def load_settings():
    if os.path.exists(TRACK_FILE):
        return json.load(open(TRACK_FILE))
    return {"name": "My First Track", "location_name": "MY River", "start": 699, "pickups": {}}


def save_settings(cfg):
    os.makedirs(os.path.dirname(TRACK_FILE), exist_ok=True)
    json.dump(cfg, open(TRACK_FILE, "w"), indent=1)


def build(cfg, log=print, extra=None, first_track=True):
    """build the gamedata pack from the settings; returns the pack path. `extra` = list of add(parts, log)
    functions that may change parts["files"] {pack path: bytes} / parts["aliases"] {new path: existing path}
    before the pack is written (other maps: flat_earth/build.py). first_track=False: WITHOUT the old test route
    "My First Track" (USA_LA_River_7, 2026-10-13) and without renaming LA River - the game's own amax.bin,
    multiplayer.bin and names are the start for the other maps."""
    os.makedirs(W, exist_ok=True)
    if not first_track:
        parts = {"aliases": {}, "files": {
            "gamedata/xt/amax.bin": open(os.path.join(U, "xt", "amax.bin"), "rb").read(),
            "gamedata/xt/multiplayer.bin": open(os.path.join(U, "xt", "multiplayer.bin"), "rb").read(),
            "gamedata/xt/loc/cityandroutenames.loc": open(os.path.join(U, "xt", "loc", "cityandroutenames.loc"),
                                                          "rb").read()}}
        return _write_pack(parts, extra, log)
    d6 = open(os.path.join(U, "levels", CITY, SRC_ROUTE + ".aid"), "rb").read()
    L6 = {s[0]: s for s in aid.splines(d6)}["LOGIC"]
    start = int(cfg["start"]) % (L6[3] - 1)
    log("start line at Logic point %d (route 6 starts at 235)" % start)
    d_aid, L = build_aid(start)
    open(os.path.join(W, NEW_ROUTE.lower() + ".aid"), "wb").write(d_aid)
    ground = cfg.get("ground_y")
    if ground is None:
        ground = ground_height(d6, start)
    grid_y = -0.22                                 # route 6's grid height
    pickups = {k: v for k, v in (cfg.get("pickups") or {}).items() if v}
    log("building actor lists (%d power-up list(s) edited)" % len(pickups))
    actorpack = build_actorpack(d6, L6, 235, start, ground - grid_y, ground, pickups)
    open(os.path.join(W, "actorpack.xt"), "wb").write(actorpack)
    list_map = dict(GRID_LISTS)
    list_map.update({u: PICKUP_LISTS[u] for u in pickups})
    log("building game database (route, multiplayer list, names)")
    amax = build_amax(list_map)
    mp = build_multiplayer()
    locdata = loc.write(os.path.join(U, "xt", "loc", "cityandroutenames.loc"), os.path.join(W, "cityandroutenames.loc"),
                        add=[(NEW_LOC, "CRN_LA_RIVER_7", cfg.get("name") or NEW_NAME)],
                        change={0x653725EA: cfg.get("location_name") or "MY River"})
    aid_path = "gamedata/levels/%s/%s.aid" % (CITY, NEW_ROUTE.lower())
    parts = {"aliases": {aid_path: "gamedata/levels/%s/%s.aid" % (CITY, SRC_ROUTE)},
             "files": {"gamedata/xt/amax.bin": amax, "gamedata/xt/multiplayer.bin": mp,
                       "gamedata/xt/loc/cityandroutenames.loc": locdata,
                       "gamedata/levels/%s/actorpack.xt" % CITY: actorpack, aid_path: d_aid}}
    return _write_pack(parts, extra, log)


def _write_pack(parts, extra, log):
    """the gamedata pack = the game's original + parts (after the extra maps added theirs)"""
    for add in extra or []:               # other maps (e.g. flat_earth/build.py) add their files / edits
        add(parts, log)
    log("writing the pack")
    orig = paths.GAMEDATA_ORIGINAL
    out = paths.GAMEDATA_OUT
    tmp = out + ".%d.tmp" % os.getpid()
    pak.add_aliases(orig, tmp, parts["aliases"])
    pak.replace_files(tmp, out, parts["files"])
    os.unlink(tmp)
    log("built " + out)
    return out


def main():
    cfg = load_settings()
    if "--pick" in sys.argv:
        args = [a for a in sys.argv[1:] if a != "--pick"]
        pick = json.load(open(args[0] if args else os.path.join(HERE, "my_routes", "start_pick.json")))
        d6 = open(os.path.join(U, "levels", CITY, SRC_ROUTE + ".aid"), "rb").read()
        L6 = {s[0]: s for s in aid.splines(d6)}["LOGIC"]
        cfg["start"] = pick_start(d6, L6, pick)
        cfg["ground_y"] = pick["pos"][1] - 0.5         # the car's centre is ~0.5 m above the road
        save_settings(cfg)
    build(cfg)


if __name__ == "__main__":
    main()

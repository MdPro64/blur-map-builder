"""
Everything placed along a Flat Earth road (width and height may change along it): road pieces, start / finish strips,
lamps, barriers, billboards, obstacles, road-block containers, power-ups, crowds, horizon ships / cranes, and the
collision pieces (edge walls, containers, bridge decks).

Placement works by DISTANCE ALONG THE ROAD (s, metres) and a sideways offset (lateral, + = the road's side
direction): road.at(s, lateral) -> (x, z, heading); road.h_at(s) = height above the ground (bridges), road.w_at(s)
= width. Model turns: an actor turned by `t` maps its model x axis to (cos t, -sin t); for a road heading a
(forward = (sin a, cos a)): t = a - pi/2 -> model x along the road; t = a + pi/2 / a - pi/2 -> faces the road from
the + / - side; t = a + pi -> model x across the road. `fe` = the build module (its constants).
"""
import json
import math
import os
import sys
import zlib

HALF_PI = math.pi / 2

# ---- per-map settings (designer: MAP panel) ----
# m["pickup_step"]    automatic power-up rows every this many m (default 150)
# m["pickup_per_row"] power-ups in a row (default 5); a row item may have its own "n"
# m["scenery"]        {"ships", "cranes", "landmarks", "lamps", "billboards", "chevrons", "signs": True / False}
PICKUP_STEP_DEFAULT, PICKUP_PER_ROW_DEFAULT = 150.0, 5
SCENERY = (("ships", "Ships (horizon)"), ("cranes", "Cranes"), ("landmarks", "Ferris wheel + radio mast"),
           ("lamps", "Street lamps"), ("billboards", "Billboards"), ("chevrons", "Arrow boards on bends"),
           ("signs", "Start / finish lights"), ("buildings", "Tall buildings"), ("arch", "Start / finish arch"))
# the default for every map without its own switches (user 2026-10-20 / 21: before, a map without "scenery" -
# e.g. a new player's first map - had everything on)
NEW_MAP_SCENERY = {"ships": False, "cranes": False, "landmarks": False, "lamps": True, "billboards": False,
                   "chevrons": True, "signs": True, "buildings": False, "arch": True}


def pickup_step(m):
    return max(40.0, min(600.0, float((m or {}).get("pickup_step") or PICKUP_STEP_DEFAULT)))


def row_size(m, it=None):
    """power-ups in a row: the row's own "n", else the map's setting, else 5 (1..9)"""
    n = (it or {}).get("n") or (m or {}).get("pickup_per_row") or PICKUP_PER_ROW_DEFAULT
    return max(1, min(9, int(n)))


def scenery_on(m, key):
    """the map's switch for an automatic scenery kind (NEW_MAP_SCENERY unless the user set it)"""
    return bool(((m or {}).get("scenery") or {}).get(key, NEW_MAP_SCENERY.get(key, True)))
ELEVATED = 0.5                       # a road part higher than this (m) counts as bridge / ramp


def _line_s(road, info):
    """(start, finish) in m along the road; a lap track (closed loop) races start -> all the way round -> start,
    so its finish is start + length (road.at & co. wrap round)"""
    s0 = road.dist[info["start_index"]]
    if getattr(road, "closed", False):
        return s0, s0 + road.length
    return s0, road.dist[info["finish_index"]]


def _y(fe, road, s):
    return fe.GROUND_Y + road.h_at(s)


def _elevated(road, s):
    return road.h_at(s) > ELEVATED


# ---- the TILE list (144 DynamicObjActors): road pieces, line strips, containers, big scenery ----------------------

MAX_PIECES = 144 - 6 - 24 - 2          # tile list minus line strips + their containers, road blocks, ferris + radio


def piece_width(fe, w):
    """the road-piece model (width bucket) for a road w m wide"""
    for b in sorted(fe.TILE_WIDTHS):
        if b >= w - 0.5:
            return b
    return max(fe.TILE_WIDTHS)


def road_piece_spots(fe, road):
    """road pieces of ~fe.PIECE_STEP metres (models fe.PIECE_LEN long: the overlap hides the joins on bends), each
    the width model that covers the road there, tilted on ramps"""
    n = max(1, int(math.ceil(road.length / fe.PIECE_STEP)))
    if n > MAX_PIECES:
        raise ValueError("road too long: %.0f m (at most %.0f m)" % (road.length, MAX_PIECES * fe.PIECE_STEP))
    step = road.length / n
    spots = []
    for k in range(n):
        s = step * (k + 0.5)
        x, z, a = road.at(s)
        w = max(road.w_at(step * k), road.w_at(s), road.w_at(step * (k + 1)))
        spots.append((x, _y(fe, road, s) - fe.TILE_TOP_BELOW, z, fe.TILE_DEFS[piece_width(fe, w)], a - HALF_PI,
                      road.pitch_at(s)))
    return spots


def block_spots(fe, road, info, m):
    """road-block containers (centre x, z, heading of the road there) - 2 per obstacle group"""
    out = []
    for g, (s, lane) in enumerate(obstacle_groups(fe, road, info, m)):
        w = road.w_at(s)
        other = (-1, 0, 1)[(g + 1) % 3] * w / 4
        for ds, lat in ((12, lane), (40, other)):
            out.append(road.at(s + ds, lat))
    return out


def tile_spots(fe, road, info, m, keys=False):
    if getattr(fe, "ROAD_MESH", False):          # the seamless road: a few long models that follow the road
        import road_mesh
        spots = road_mesh.spots(fe, road, m)
    else:
        spots = road_piece_spots(fe, road)
    s0, s1 = _line_s(road, info)
    for s in ((s0,) if getattr(road, "closed", False) else (s0, s1)):     # a lap track: one start/finish strip
        x, z, a = road.at(s)
        spots.append((x, _y(fe, road, s) - fe.LINE_TOP_BELOW, z, fe.LINE_DEF_UID, a - HALF_PI, road.pitch_at(s)))
        # (the red containers at both ends of the strip were removed at the user's request, 2026-10-20; they had
        # no collision - only fe.TEST_DEF_UID spots here)
        # the START / FINISH ARCH over the line (build.ARCHES): sign facing the cars (turn a + pi: model +z points
        # back at them), its x scaled so the pillars stand just outside the walls / raised edges
        if scenery_on(m, "arch") and getattr(fe, "ARCHES", None):
            name = "arch_lap" if getattr(road, "closed", False) else ("arch_start" if s == s0 else "arch_finish")
            uid = next(b[4] for b in fe.BIG if b[0] == name)
            sx = (road.w_at(s) / 2 + 3.5) / (fe.ARCH_W / 2 - fe.ARCH_PILLAR / 2)
            spots.append((x, _y(fe, road, s), z, uid, a + math.pi, 0.0, [sx, 1.0, 1.0]))
    for cx, cz, a in block_spots(fe, road, info, m):
        # container model: x -13.7..0, z 0..2.4 from its corner; turned a + pi its x axis points along the road's
        # side direction and its z axis backwards -> corner = centre + side * 6.85 + forward * 1.2
        fx, fz = math.sin(a), math.cos(a)
        sx, sz = -fz, fx
        L, _, W = fe.CONTAINER
        spots.append((cx + sx * L / 2 + fx * W / 2, fe.GROUND_Y, cz + sz * L / 2 + fz * W / 2, fe.BLOCK_DEF_UID, a + math.pi))
    kinds = [tile_kind(fe, t[3]) for t in spots]
    placed, groups = [], {}                        # the user's library objects: whole objects while there is room
    for sp, key in placed_spots(fe, m)[0]:
        groups.setdefault(key, []).append((sp, key))
    for g in groups.values():                      # (an imported model is several pieces: all of it or none)
        if len(spots) + len(placed) + len(g) > 144:
            break
        placed += g
    spots += [sp for sp, key in placed]
    kinds += [key for sp, key in placed]
    for s, lat in (chevron_panels(road, room=144 - len(spots) - BIG_MIN, m=m) if scenery_on(m, "chevrons") else []):
        # vertical panel just behind the solid wall; turned a - pi/2 its arrows point along the road
        x, z, a = road.at(s, lat)
        spots.append((x, _y(fe, road, s), z, fe.CHEVRON_DEF_UID, a - HALF_PI))
        kinds.append("chevron")
    big = big_spots(fe, road, info, room=144 - len(spots), m=m)
    spots += big
    kinds += [tile_kind(fe, t[3]) for t in big]
    assert len(spots) <= 144, len(spots)
    spots, ks = apply_edits(spots, kinds, m.get("edits") or {}, scalable=True)
    return (spots, ks) if keys else spots


# ---- the user's changes to placed objects (3D view: G move / R turn / S scale / X hide) -----------------------------
# m["edits"] = {key: {"dx", "dy", "dz" (m), "turn" (radians, added), "scale" (tile list only), "hidden"}};
# key = "<kind>:<n>" = the n-th object of that kind as the layout places it (offsets stay relative to that spot,
# so a moved ship keeps its place next to the road when the road changes a little)

FIXED_KINDS = {"road", "line", "linebox", "block"}       # part of the road layout: not edited by hand


def actor_axes(turn, rx=0.0, rz=0.0):
    """world vectors of a model's x, y (up), z axes: R = Ry(turn) . Rz(rz) . Rx(rx) (right-handed, radians).
    rx = tilt about the model's x axis (Blender X / red), rz = about its z axis (Blender Y / green; also the road
    pieces' pitch), turn = about up (Blender Z / blue). turn alone: x -> (cos, 0, -sin), z -> (sin, 0, cos)."""
    c, s = math.cos(turn), math.sin(turn)
    ca, sa = math.cos(rx), math.sin(rx)
    cb, sb = math.cos(rz), math.sin(rz)

    def ry(v):
        return (c * v[0] + s * v[2], v[1], -s * v[0] + c * v[2])

    def rzz(v):
        return (cb * v[0] - sb * v[1], sb * v[0] + cb * v[1], v[2])

    def rxx(v):
        return (v[0], ca * v[1] - sa * v[2], sa * v[1] + ca * v[2])
    return tuple(ry(rzz(rxx(e))) for e in ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)))


def tile_kind(fe, uid):
    for b in fe.BIG:
        if b[4] == uid:
            return b[0]
    return {fe.LINE_DEF_UID: "line", fe.TEST_DEF_UID: "linebox", fe.BLOCK_DEF_UID: "block",
            fe.CHEVRON_DEF_UID: "chevron"}.get(uid, "road")


def lamp_kind(fe, sp):
    if len(sp) > 4:
        for k, v in fe.ROADSIDE.items():
            if v[1] == sp[4]:
                return k
    return "lamp"


def object_keys(kinds):
    """keys "<kind>:<n>" (n-th of its kind); a kind that already is a key ("obj:3", the user's placed object) stays"""
    count, out = {}, []
    for k in kinds:
        if ":" in k:
            out.append(k)
            continue
        out.append("%s:%d" % (k, count.get(k, 0)))
        count[k] = count.get(k, 0) + 1
    return out


def apply_edits(spots, kinds, edits, scalable):
    """(edited spots, their keys); hidden objects are left out. Tile spots get their scale as the 7th value."""
    out, keys = [], []
    for sp, key in zip(spots, object_keys(kinds)):
        e = edits.get(key)
        if e and key.split(":")[0] not in FIXED_KINDS:
            if e.get("hidden"):
                continue
            sp = list(sp)
            sp[0] += e.get("dx", 0.0)
            sp[1] += e.get("dy", 0.0)
            sp[2] += e.get("dz", 0.0)
            ti = 4 if scalable else 3                      # the turn: tile spots (x, y, z, uid, turn..), lamps (x, y, z, turn..)
            while len(sp) <= ti:
                sp.append(0.0)
            sp[ti] += e.get("turn", 0.0)
            k = e.get("scale", 1.0)                         # one number, or [x, y up, z] in the model's axes
            tilt = [e.get("rx", 0.0), e.get("rz", 0.0)]    # tilts about the model's x / z axes (radians)
            if scalable:                                   # tile spots: .., turn, pitch, scale, tilt
                while len(sp) < 6:
                    sp.append(0.0)                         # pitch
                sp[6:] = [k, tilt]
            elif any(tilt):                                # lamp spots: x, y, z, turn, uid, tilt
                sp[5:] = [tilt]
            sp = tuple(sp)
        out.append(sp)
        keys.append(key)
    return out, keys


def _net(m, road):
    import network
    return network.get(m, road) if m is not None else None


def big_spots(fe, road, info, room, m=None):
    """horizon wall: ships along both sides (turned along the road), cranes behind them, ferris wheel at the
    start, radio mast at the finish - skipping any spot too close to another part of the road (or a branch);
    at most `room`"""
    net = _net(m, road)

    def clear(x, z, a, half_len, margin):
        if net is None or not net.branches:
            return True
        fx, fz = math.sin(a), math.cos(a)
        return not any(net.near_branch(x + fx * t, z + fz * t, margin) for t in (-half_len, 0.0, half_len))
    defs = {b[0]: b for b in fe.BIG}
    s0, s1 = _line_s(road, info)
    spots = []
    for s, lat, name in (((max(s0 - 60, 0), -(road.w_at(s0) / 2 + 40), "ferris"),
                          (min(s1 + 60, road.length), road.w_at(s1) / 2 + 40, "radio"))
                         if scenery_on(m, "landmarks") else ()):
        x, z, a = road.at(s, lat)
        if not road.near_road(x, z, road.width / 2 + 20) and clear(x, z, a, 0.0, 40.0):
            spots.append((x, fe.GROUND_Y, z, defs[name][4], 0.0))
    ships = []
    for side in ((-1, 1) if scenery_on(m, "ships") else ()):
        s, k = -150.0 if side < 0 else -20.0, 0 if side < 0 else 1
        while s < road.length + 150:
            name = ("tanker", "cruise")[k % 2]
            length = 333.0 if name == "tanker" else 263.0
            x, z, a = road.at(s + length / 2, side * (road.width / 2 + fe.SHIP_Z - 30))
            if not road.near_road(x, z, fe.SHIP_Z - 20) and all(math.hypot(x - o[0], z - o[2]) > 200 for o in ships) \
                    and clear(x, z, a, length / 2, 50.0):
                ships.append((x, fe.GROUND_Y + defs[name][5], z, defs[name][4], a))   # ship model: long axis = model z
            s += length + 10
            k += 1
    cranes = []
    for side in ((-1, 1) if scenery_on(m, "cranes") else ()):
        s = 300.0 if side < 0 else 450.0
        while s < road.length - 200:
            x, z, a = road.at(s, side * (road.width / 2 + fe.CRANE_Z - 30))
            if not road.near_road(x, z, fe.CRANE_Z - 30) and clear(x, z, a, 0.0, 40.0):
                cranes.append((x, fe.GROUND_Y, z, defs["crane"][4], a - HALF_PI))
            s += road.length / max(fe.CRANES_PER_SIDE, 1)
    towers = []
    if scenery_on(m, "buildings") and getattr(fe, "TOWERS", None):
        k = 0
        for side in (-1, 1):
            s = 60.0 if side < 0 else 180.0
            while s < road.length:
                name, wx, dz, h, seed = fe.TOWERS[k % len(fe.TOWERS)]
                lat = side * (road.w_at(s) / 2 + fe.TOWER_Z + (k * 37 % 90))
                x, z, a = road.at(s, lat)
                gap = max(wx, dz) / 2 + 60
                if (not road.near_road(x, z, gap + road.width / 2) and clear(x, z, a, 0.0, gap)
                        and all(math.hypot(x - o[0], z - o[2]) > max(wx, dz) + 40 for o in towers)):
                    towers.append((x, fe.GROUND_Y, z, defs[name][4], a))
                k += 1
                s += fe.TOWER_STEP
    for spot in ships + cranes + towers:
        if len(spots) >= room:
            break
        spots.append(spot)
    return spots[:room]


# ---- the LAMP list (356 PhysicalObjectActors): lamps, barriers, billboards, line signs, obstacles -----------------

def obstacle_groups(fe, road, info, m):
    """(s, lane offset) of each obstacle group: between the power-up rows, alternating left / middle / right;
    none on bridges / ramps, none when the map turns obstacles off"""
    if not m.get("obstacles", False):
        return []
    s0, s1 = _line_s(road, info)
    span = s1 - s0
    net = _net(m, road)
    busy = [(a * 4.0 - 80, b * 4.0 + 80) for a, b in net.busy_ranges()] if net else []
    L = road.length
    out = []
    for g in range(fe.OBSTACLE_GROUPS):
        s = s0 + span * (g + 1) / (fe.OBSTACLE_GROUPS + 1) + fe.PICKUP_STEP / 2
        if any(_elevated(road, s + d) for d in (-80, 0, 50, 120)):
            continue
        if any(a <= s + sh <= b for a, b in busy for sh in ((0.0, L, -L) if road.closed else (0.0,))):
            continue                     # a fork / merge / lane split: no road block there
        out.append((s, (-1, 0, 1)[g % 3] * road.w_at(s) / 4))
    return out


CHEVRON_RADIUS = 260.0     # bends tighter than this get chevron boards on their outside
CHEVRON_STEP = 6           # one board every 6 road points (24 m)
MAX_CHEVRONS = 110


def chevron_spots(road):
    """[(s, lateral, turn function)]: yellow chevron boards on the OUTSIDE of every bend, just behind the wall,
    facing the cars that come into the bend (heading increases = right turn = the outside is the +side)"""
    n = len(road)
    step = CHEVRON_STEP
    while True:
        out = []
        i = 2
        while i < n - 2:
            da = (road.heading[i + 2] - road.heading[i - 2] + math.pi) % (2 * math.pi) - math.pi
            radius = 4 * 4.0 / abs(da) if abs(da) > 1e-6 else 1e9
            if radius < CHEVRON_RADIUS and road.height[i] < 0.05:
                side = 1 if da > 0 else -1
                out.append((road.dist[i], side * (road.width_at[i] / 2 + 5), lambda a: a + math.pi))
                i += step
            else:
                i += 1
        if len(out) <= MAX_CHEVRONS:
            return out
        step += 2


BIG_MIN = 6                # tile-list places kept for the horizon ships / cranes / ferris wheel
PANEL_LEN = 16.0           # = build.CHEVRON_LEN
CHEVRON_OUT = 3.0          # m outside the road edge: behind the raised edge (build.BANK reaches 2.2 m)


def chevron_panels(road, room, m=None):
    """[(s, lateral)] chevron panels on the outside of bends, CHEVRON_OUT m outside the edge (behind the raised edge).
    Each bend gets panels end to end; if the tile list has no room for all, every 2nd / 3rd ... one is kept.
    None where a branch leaves / joins (m given)."""
    net = _net(m, road)
    n = len(road)
    seg = int(round(PANEL_LEN / 4.0))                      # road points per panel
    cand = []
    i = 2
    while i < n - 2:
        da = (road.heading[i + 2] - road.heading[i - 2] + math.pi) % (2 * math.pi) - math.pi
        radius = 16.0 / abs(da) if abs(da) > 1e-6 else 1e9
        if radius < CHEVRON_RADIUS and road.height[i] < 0.05:
            side = 1 if da > 0 else -1
            lat = side * (road.width_at[i] / 2 + CHEVRON_OUT)
            x, z, _ = road.at(road.dist[i], lat)
            if net is None or not net.near_branch(x, z, PANEL_LEN):
                cand.append((road.dist[i], lat))
            i += seg
        else:
            i += 1
    if room <= 0:
        return []
    keep = 1
    while len(cand[::keep]) > room:
        keep += 1
    return cand[::keep]


def lamp_spots(fe, road, info, m, spread=1.0, keys=False):
    """lamps, barriers, billboards, line signs, obstacles; longer roads spread them out (the list has 356 actors)"""
    placed = placed_spots(fe, m)[1][:LAMP_PLACED_MAX]       # the user's breakable library objects
    spots = _lamp_spots(fe, road, info, m, spread)
    for _ in range(60):
        if len(spots) + len(placed) <= 356:
            break
        spread *= 1.15
        spots = _lamp_spots(fe, road, info, m, spread)
    spots = spots[:356 - len(placed)]
    kinds = [lamp_kind(fe, sp) for sp in spots] + [key for sp, key in placed]
    spots += [sp for sp, key in placed]
    # (physical objects: moved / turned / hidden, never scaled - the game's physics would not follow)
    spots, ks = apply_edits(spots, kinds, m.get("edits") or {}, scalable=False)
    return (spots, ks) if keys else spots


def _lamp_spots(fe, road, info, m, spread):
    R = fe.ROADSIDE
    spots = []
    net = _net(m, road)
    if net is not None and not net.branches:
        net = None

    def add(s, lat, turn_of_a, uid, check=True):
        x, z, a = road.at(s, lat)
        if check and lat and not road.off_road(x, z, lat):
            return                       # would stand on the road (inside of a tight turn / another part of the road)
        if check and net is not None and net.near_branch(x, z, 4.0):
            return                       # would stand on a branch road
        spots.append((x, _y(fe, road, s), z, turn_of_a(a), uid))
    s = 10.0
    while scenery_on(m, "lamps") and s <= road.length - 10:
        if not _elevated(road, s):
            lat = road.w_at(s) / 2 + 10
            add(s, lat, lambda a: a + HALF_PI, R["lamp"][1])
            add(s, -lat, lambda a: a - HALF_PI, R["lamp"][1])
        s += fe.LAMP_STEP * spread
    s = 10.0 + fe.BARRIER_STEP / 2
    while fe.EDGE_BARRIERS and s <= road.length - 10:
        # on the ground: outside the solid wall; on a bridge: a railing just inside the deck edge
        up = _elevated(road, s)
        lat = road.w_at(s) / 2 + (-1.5 if up else WALL_MARK)
        for side in (-1, 1):
            add(s, side * lat, lambda a: a - HALF_PI, R["barrier"][1])
        s += fe.BARRIER_STEP * spread * (0.35 if up else 1)
    s = 10.0 + fe.BILLBOARD_STEP / 2
    while scenery_on(m, "billboards") and s <= road.length - 10:
        if not _elevated(road, s):
            add(s, road.w_at(s) / 2 + 20, lambda a: a + HALF_PI, R["billboard"][1])
        s2 = s + fe.BILLBOARD_STEP / 2
        if s2 <= road.length - 10 and not _elevated(road, s2):
            add(s2, -(road.w_at(s2) / 2 + 20), lambda a: a - HALF_PI, R["billboard"][1])
        s += fe.BILLBOARD_STEP
    for ls in (_line_s(road, info) if scenery_on(m, "signs") else ()):
        lat = road.w_at(ls) / 2 + 10
        for side in (-1, 1):
            face = (lambda a: a + HALF_PI) if side > 0 else (lambda a: a - HALF_PI)
            add(ls - 6, side * lat, face, R["flashing"][1])
            add(ls + 6, side * lat, face, R["light"][1])
    for gs, lane in obstacle_groups(fe, road, info, m):
        add(gs, lane, lambda a: a, R["rwbarrier"][1], False)
        for k, (ds, dl) in enumerate(((-3, -3), (-3, 3), (3, -5), (3, 5))):
            add(gs + ds, lane + dl, lambda a: a - HALF_PI, R["barrel"][1] if k < 2 else R["cone"][1], False)
    return spots


# ---- the user's objects from the library (designer: 3D tab side bar) ----------------------------------------------
# m["objects"] = [{"id": catalog id ("scenery:<GameModel uid>", "breakable:<ElementObjectRecord uid>", "movable:" /
# "animated:<DynamicObjDef uid>"), "x", "z", "h" (height above the ground), "turn" (radians)}]. Key "obj:<index>":
# moved / turned / scaled / hidden / made solid through m["edits"] like every other object.

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import paths  # noqa: E402
CATALOG = paths.CATALOG
SCENERY_DEF_BASE = 0xF1B00000      # DynamicObjDefs the build makes for scenery models: this + 20 bits of a hash
LAMP_PLACED_MAX = 340              # the lamp list keeps a few places for the start / finish signs
KIND_TEXT = {"scenery": "look only - tick Solid to make it a wall", "breakable": "breaks / falls when hit",
             "movable": "cars push it around", "animated": "animated object",
             "import": "your own model (.glb) - look only, tick Solid to make it a box wall"}
_catalog = None


def catalog():
    """{id: entry} of library/catalog.json (made by library/make_catalog.py; {} if it is missing) + the player's
    imported models (model_import.py)"""
    global _catalog
    if _catalog is None:
        try:
            _catalog = {e["id"]: e for e in json.load(open(CATALOG))["objects"]}
        except (OSError, ValueError):
            _catalog = {}
        import model_import
        _catalog.update((e["id"], e) for e in model_import.entries())
    return _catalog


def reload_catalog():
    """after an import: read the imported models again"""
    global _catalog
    _catalog = None


def scenery_def_uid(model_uid):
    """the DynamicObjDef uid the build makes for a scenery model (GameModel uid, hex text)"""
    return SCENERY_DEF_BASE | (zlib.crc32(model_uid.upper().encode()) & 0xFFFFF)


def placed_spots(fe, m):
    """([(tile spot, key)], [(lamp spot, key)]) of the map's library objects"""
    tiles, lamps = [], []
    for i, o in enumerate(m.get("objects") or []):
        e = catalog().get(o.get("id"))
        if e is None:
            continue
        x, z, a = float(o["x"]), float(o["z"]), float(o.get("turn", 0.0))
        y = fe.GROUND_Y + float(o.get("h", 0.0))
        key = "obj:%d" % i
        if e["kind"] == "breakable":
            lamps.append(((x, y, z, a, int(e["uid"], 16)), key))
        elif e["kind"] == "import":                 # one actor per model piece, all at the object's place
            import model_import
            for k in range(e["pieces"]):
                tiles.append(((x, y, z, model_import.uids(e["slot"], k)[1], a), key))
        else:
            uid = scenery_def_uid(e["uid"]) if e["kind"] == "scenery" else int(e["uid"], 16)
            tiles.append(((x, y, z, uid, a), key))
    return tiles, lamps


def scenery_models(wld):
    """GameModel uids (hex) of every scenery object placed in the world (the build makes their DynamicObjDefs)"""
    out = set()
    for loc in wld["locations"]:
        for m in loc["maps"]:
            for o in m.get("objects") or []:
                e = catalog().get(o.get("id"))
                if e and e["kind"] == "scenery":
                    out.add(e["uid"].upper())
    return sorted(out)


# each kind's box in its model space (x0, x1, y0 up, y1, z0, z1): what a click selects and what "Solid" makes a wall
OBJECT_BOX = {
    "tanker": (-18, 18, 0, 36, -166, 166), "cruise": (-18, 18, -7.8, 28, -131, 131),
    "crane": (-60, 60, 0, 71, -13, 13), "ferris": (-22, 22, 0, 48, -3, 3), "radio": (-3, 3, 0, 60, -3, 3),
    "tower_a": (-20, 20, 0, 140, -20, 20), "tower_b": (-27.5, 27.5, 0, 95, -17.5, 17.5),
    "tower_c": (-15, 15, 0, 200, -15, 15), "tower_d": (-35, 35, 0, 60, -22.5, 22.5),
    "chevron": (-8, 8, 0, 2.5, -.3, .3), "line": (-1.5, 1.5, 0, .3, -34, 34), "linebox": (-13.7, 0, 0, 2.9, 0, 2.4),
    "block": (-13.7, 0, 0, 2.9, 0, 2.4),
    "lamp": (-.6, .6, 0, 10.5, -.6, .6), "barrier": (-1.8, 1.8, 0, 1, -.4, .4), "billboard": (-4, 4, 0, 8, -.3, .3),
    "flashing": (-.8, .8, 0, 2.2, -.15, .15), "light": (-.75, .75, 0, 6.8, -.3, .3), "barrel": (-.4, .4, 0, 1, -.4, .4),
    "rwbarrier": (-1.2, 1.2, 0, 1.1, -.2, .2), "cone": (-.25, .25, 0, .8, -.25, .25),
}


def object_box(m, key):
    """model-space box of the object with this key, or None"""
    kind, _, n = key.partition(":")
    if kind == "obj":
        objs = m.get("objects") or []
        e = catalog().get(objs[int(n)].get("id")) if int(n) < len(objs) else None
        if e is None:
            return None
        (x0, y0, z0), (x1, y1, z1) = e["lo"], e["hi"]
        return (x0, x1, y0, y1, z0, z1)
    return OBJECT_BOX.get(kind)


def solid_boxes(fe, road, info, m):
    """collision boxes of every object the user ticked "Solid" (edits[key]["solid"]): its box, scaled and turned"""
    import write_flat
    edits = m.get("edits") or {}
    if not any(e.get("solid") for e in edits.values()):
        return []
    out = []
    lists = (tile_spots(fe, road, info, m, keys=True) + (4, 6), lamp_spots(fe, road, info, m, keys=True) + (3, None))
    seen = set()
    for spots, keys, ti, si in lists:
        for sp, key in zip(spots, keys):
            if not (edits.get(key) or {}).get("solid") or key in seen:
                continue
            seen.add(key)                          # (an imported model: several pieces, one box)
            box = object_box(m, key)
            if box is None:
                continue
            x0, x1, y0, y1, z0, z1 = box
            k = sp[si] if si is not None and len(sp) > si else 1.0
            kx, ky, kz = (k,) * 3 if isinstance(k, (int, float)) else k
            t = sp[ti] if len(sp) > ti else 0.0
            xc, zc = (x0 + x1) / 2 * kx, (z0 + z1) / 2 * kz
            # model x -> (cos t, -sin t), model z -> (sin t, cos t)
            cx = sp[0] + xc * math.cos(t) + zc * math.sin(t)
            cz = sp[2] - xc * math.sin(t) + zc * math.cos(t)
            hl, hw = max((z1 - z0) / 2 * kz, 0.3), max((x1 - x0) / 2 * kx, 0.3)
            out.append(write_flat.turned_box(cx, cz, sp[1] + y0 * ky, sp[1] + max(y1 * ky, y0 * ky + 1.0), hl, hw, t))
    return out


# ---- power-ups, sounds -----------------------------------------------------------------------------------------

WALL_MARK = 2.5            # concrete barriers just behind the (invisible) solid wall at width / 2 + 1

AUTO_PATTERN = ("Nitro", "Shunt", "Shield", "Barge", "Bolt", "Shock", "Mine", "Repair")
MAX_PICKUPS = 109


def auto_pickups(fe, road, info, m=None):
    """the automatic layout: a row every fe.PICKUP_STEP m between the lines (as {"kind": "row", x, z})"""
    import network
    s0, s1 = _line_s(road, info)
    return network.get(m if m is not None else {}, road).auto_rows(s0, s1, pickup_step(m),
                                                                   MAX_PICKUPS // row_size(m))


def pickup_items(fe, road, info, m):
    """the map's power-up list: its own, or the automatic rows when it has none ([{kind, x, z, type?}])"""
    items = m.get("pickups")
    return auto_pickups(fe, road, info, m) if items is None else items


def pickup_spots(fe, road, info, m, owners=False):
    """(x, y, z, heading, PickupType uid) for the map's power-ups: rows of fe.PICKUP_LANES across the road (type:
    the row's, or a mix) and single ones; the automatic rows when the map has no list. At most MAX_PICKUPS.
    owners=True: also [index in pickup_items() of each spot's row / single item]"""
    import world
    import network
    net = network.get(m, road)
    main = road
    items = pickup_items(fe, main, info, m)
    spots, own = [], []
    k = 0
    for n, it in enumerate(items):
        lanes = row_size(m, it)
        own += [n] * (1 if it.get("kind") == "single" else lanes)
        road = net.pickup_road(it)                        # a row on a branch: across the branch
        s, lat = road.project(it["x"], it["z"])
        if road is main and it.get("kind") != "single":
            s = net.clear_of_splits(s)                    # never inside a lane split: just before / after it
        if it.get("kind") == "single":
            _, _, a = road.at(s)
            t = it.get("type") or AUTO_PATTERN[k % len(AUTO_PATTERN)]
            spots.append((it["x"], _y(fe, road, s) + 0.70, it["z"], a, world.PICKUP_TYPES[t]))
            k += 1
            continue
        w = road.w_at(s)
        for li in range(lanes):
            lat = (li - (lanes - 1) / 2) * w / (lanes + 1)
            x, z, a = road.at(s, lat)
            t = it.get("type") or AUTO_PATTERN[k % len(AUTO_PATTERN)]
            spots.append((x, _y(fe, road, s) + 0.70, z, a, world.PICKUP_TYPES[t]))
            k += 1
    return (spots[:MAX_PICKUPS], own[:MAX_PICKUPS]) if owners else spots[:MAX_PICKUPS]


def audio_spots(fe, road, info):
    def uid(name):
        src, ours = fe.AUDIO[name]
        return ours or int(src, 16)
    s0, s1 = _line_s(road, info)
    x, z, _ = road.at((s0 + s1) / 2)
    spots = [(x, _y(fe, road, (s0 + s1) / 2) + 2, z, uid("haruna"))]
    for s in (s0 + (s1 - s0) / 3, s0 + 2 * (s1 - s0) / 3):
        side = road.w_at(s) / 2 + 3
        for lat in (side, -side):
            x, z, _ = road.at(s, lat)
            spots.append((x, _y(fe, road, s) + 2, z, uid("crowd_m")))
    for ls in (s0 + 90, s1):                        # start crowds 90 m after the line (see build.audio_spots)
        side = road.w_at(ls) / 2 + 3
        for ds, lat, name in ((0, side, "crowd_l"), (0, -side, "crowd_l"), (-25, -side, "crowd_m"),
                              (25, side, "crowd_s"), (-25, side, "crowd_s")):
            x, z, _ = road.at(ls + ds, lat)
            spots.append((x, _y(fe, road, ls + ds) + 2, z, uid(name)))
    return spots[:15]


# ---- collision: edge walls (following width + height), turned containers, bridge decks ------------------------

def _run_walls(fe, road, net):
    """the walls of a road with branches: network.wall_runs (the pieces between the forks / merges, ending exactly
    where the main and the branch walls cross), every road point, facing the road"""
    import write_flat
    area = net.areas().get(id(road))
    pieces = []
    for side in (-1, 1):
        for run in net.wall_runs(road, side, heights=True):
            pts, nrm, y0, hs = [], [], [], []
            for k, (x, z, h) in enumerate(run):
                if pts and (x - pts[-1][0]) ** 2 + (z - pts[-1][1]) ** 2 < 0.04:
                    continue
                pts.append((x, z))
                hs.append(h)
            if len(pts) < 2:
                continue
            for k, (x, z) in enumerate(pts):
                a, b = pts[max(k - 1, 0)], pts[min(k + 1, len(pts) - 1)]
                dx, dz = b[0] - a[0], b[1] - a[1]
                ln = math.hypot(dx, dz) or 1.0
                nx, nz = -dz / ln, dx / ln
                if area is not None and area.depth(x + nx, z + nz, hs[k]) < area.depth(x - nx, z - nz, hs[k]):
                    nx, nz = -nx, -nz                        # facing the road (into it)
                nrm.append((nx, nz))
                y0.append(fe.GROUND_Y + hs[k])                # the road's own height (not the bridge above)
            for k in range(0, len(pts) - 1, 120):
                b = y0[k:k + 121]
                if len(pts[k:k + 121]) >= 2:
                    pieces.append(write_flat.wall_strip(pts[k:k + 121], b, [v + 3.0 for v in b], nrm[k:k + 121]))
    return pieces


def _walls(fe, road, mask=None):
    """the solid edge walls of one road (1 m outside its edges, 3 m high); mask = network.wall_mask: no wall where
    another road joins (the wall stops one point before the cut and starts again one after)"""
    import write_flat
    pieces = []
    if getattr(road, "kind", "") == "divider":
        return _divider_walls(fe, road)
    every = 2                                       # a wall corner every 2 road points (8 m)
    idx = list(range(0, len(road), every))
    if idx[-1] != len(road) - 1:
        idx.append(len(road) - 1)
    if getattr(road, "closed", False):
        idx.append(0)                               # a lap track: the walls close at the start line
    for si, side in enumerate((-1, 1)):
        cut = mask[si] if mask else None
        runs, cur = [], []
        for i in idx:
            if cut and cut[i]:
                if cur:
                    runs.append(cur)
                cur = []
            else:
                cur.append(i)
        if cur:
            runs.append(cur)
        edge = road.edge(side, 1.0)                     # no loops on the inside of tight turns
        for run in runs:
            if len(run) < 2:
                continue
            pts, normals, y0 = [], [], []
            for i in run:
                sx, sz = road.side(i)
                if pts and (edge[i][0] - pts[-1][0]) ** 2 + (edge[i][1] - pts[-1][1]) ** 2 < 0.25 and i != run[-1]:
                    continue                              # squashed loop: one corner is enough
                pts.append(edge[i])
                normals.append((-sx * side, -sz * side))  # facing the road
                y0.append(fe.GROUND_Y + road.height[i])
            for k in range(0, len(pts) - 1, 120):         # pieces of <= 121 points (242 corners)
                b = y0[k:k + 121]
                if len(pts[k:k + 121]) >= 2:
                    pieces.append(write_flat.wall_strip(pts[k:k + 121], b, [v + 3.0 for v in b], normals[k:k + 121]))
    return pieces


DIVIDER_SOLID = 2.0          # m from a lane divider's middle to its solid sides (the visible ridge reaches 2.7 m)


def _divider_walls(fe, road):
    """a lane divider is solid from the OUTSIDE (the walls of a road face inwards: cars got into the divider and
    could not get out): both sides face away from its middle, and both ends are closed (facing the cars)"""
    import write_flat
    n = len(road)
    idx = list(range(0, n, 2))
    if idx[-1] != n - 1:
        idx.append(n - 1)
    pieces = []
    taper = getattr(road, "taper", None) or [1.0] * n
    side_pts = {}
    for side in (-1, 1):
        pts, y0 = [], []
        for i in idx:
            cx, cz = road.centre[i]
            sx, sz = road.side(i)
            d = DIVIDER_SOLID * taper[i]                       # the V-shaped ends: the wall follows the nose
            pts.append((cx + sx * side * d, cz + sz * side * d))
            y0.append(fe.GROUND_Y + road.height[i])
        # normals: square to the wall line itself (on the V they point forwards / backwards a little)
        nrm = []
        for k in range(len(pts)):
            a, b = pts[max(k - 1, 0)], pts[min(k + 1, len(pts) - 1)]
            dx, dz = b[0] - a[0], b[1] - a[1]
            ln = math.hypot(dx, dz) or 1.0
            nx, nz = -dz / ln, dx / ln                         # one of the two perpendiculars ...
            sx, sz = road.side(idx[k])
            if nx * sx * side + nz * sz * side < 0:            # ... the one facing away from the middle
                nx, nz = -nx, -nz
            nrm.append((nx, nz))
        side_pts[side] = (pts, y0)
        for k in range(0, len(pts) - 1, 120):
            b = y0[k:k + 121]
            pieces.append(write_flat.wall_strip(pts[k:k + 121], b, [v + 3.0 for v in b], nrm[k:k + 121]))
    for end, i, sign in ((0, 0, -1), (-1, n - 1, 1)):              # the ends (closed; a point when tapered)
        a, b = side_pts[-1][0][end], side_pts[1][0][end]
        if math.hypot(a[0] - b[0], a[1] - b[1]) < 0.2:
            continue                                               # the V meets in a point: nothing to close
        fx, fz = road.forward(i)
        y = side_pts[-1][1][end]
        pieces.append(write_flat.wall_strip([a, b], [y, y], [y + 3.0, y + 3.0],
                                            [(fx * sign, fz * sign), (fx * sign, fz * sign)]))
    return pieces


def _decks(fe, road):
    """drivable decks wherever the road is raised (+ one point of overlap with the ground at both ends)"""
    import write_flat
    decks = []
    raised = [i for i in range(len(road)) if road.height[i] > 0.05]
    runs = []
    for i in raised:
        if runs and i == runs[-1][-1] + 1:
            runs[-1].append(i)
        else:
            runs.append([i])
    for run in runs:
        run = [max(run[0] - 1, 0)] + run + [min(run[-1] + 1, len(road) - 1)]
        for k in range(0, len(run) - 1, 126):
            part = run[k:k + 127]
            left, right = [], []
            for i in part:
                cx, cz = road.centre[i]
                sx, sz = road.side(i)
                h = fe.GROUND_Y + road.height[i]
                half = road.width_at[i] / 2 + 1
                left.append((cx - sx * half, h, cz - sz * half))
                right.append((cx + sx * half, h, cz + sz * half))
            decks.append(write_flat.deck_strip(left, right))
    return decks


def collision_pieces(fe, road, info, m):
    """(solid pieces, drivable deck pieces) of the map's whole road network"""
    import write_flat
    import network
    net = network.get(m, road)
    pieces, decks = [], []
    for r in net.roads():
        if r.kind != "divider" and net.branches:
            pieces += _run_walls(fe, r, net)             # exact pieces: the main and the branch walls meet (a V)
        else:
            pieces += _walls(fe, r, net.wall_mask(r))
        if r.kind != "divider":
            decks += _decks(fe, r)
    L, H, W = fe.CONTAINER
    for cx, cz, a in block_spots(fe, road, info, m):
        # the container lies ACROSS the road: half length along the road = W / 2, across = L / 2
        pieces.append(write_flat.turned_box(cx, cz, fe.GROUND_Y, fe.GROUND_Y + H, W / 2, L / 2, a))
    pieces += solid_boxes(fe, road, info, m)          # objects the user made "Solid" in the 3D view
    return pieces, decks


# ---- the AI's corridor: around road blocks the AI line narrows to the widest free gap ------------------------

def blocked_spans(fe, road, info, m):
    """(s from, s to, lateral from, lateral to) of everything the AI must drive around: containers + obstacle groups"""
    L, H, W = fe.CONTAINER
    spans = []
    for g, (s, lane) in enumerate(obstacle_groups(fe, road, info, m)):
        other = (-1, 0, 1)[(g + 1) % 3] * road.w_at(s) / 4
        spans.append((s - 6, s + 6, lane - 7, lane + 7))                     # barrier, barrels, cones
        for ds, lat in ((12, lane), (40, other)):                           # the two containers
            spans.append((s + ds - W / 2 - 1, s + ds + W / 2 + 1, lat - L / 2 - 1.5, lat + L / 2 + 1.5))
    return spans


# ---- the AI path (designer's "AI path" tab): m["ai_path"] = {"mode": "drawn", "points": [[x, z]...], "lane": m}
# = the user's own line (drawn with the pen or made by "Racing line"); missing / mode "auto" = the automatic path
# (the whole road, narrowing round road blocks). The AI drives inside a lane `lane` m wide around the line.
AI_LANE = 12.0


def project_seq(road, pts):
    """[(s, lat)] of a line of points along the road (each point searched near the previous one: a road that
    crosses itself does not make the line jump to the other part)"""
    n, out, i0 = len(road), [], None
    for x, z in pts:
        rng = range(n) if i0 is None else range(max(0, i0 - 40), min(n, i0 + 60))
        i = min(rng, key=lambda k: (road.centre[k][0] - x) ** 2 + (road.centre[k][1] - z) ** 2)
        if i0 is not None and (road.centre[i][0] - x) ** 2 + (road.centre[i][1] - z) ** 2 > 150 ** 2:
            i = min(range(n), key=lambda k: (road.centre[k][0] - x) ** 2 + (road.centre[k][1] - z) ** 2)
        i0 = i
        cx, cz = road.centre[i]
        fx, fz = road.forward(i)
        sx, sz = road.side(i)
        out.append((road.dist[i] + (x - cx) * fx + (z - cz) * fz, (x - cx) * sx + (z - cz) * sz))
    return out


def _interp(samples, s):
    """lat at s from [(s, lat)] sorted by s (ends held)"""
    if s <= samples[0][0]:
        return samples[0][1]
    if s >= samples[-1][0]:
        return samples[-1][1]
    lo, hi = 0, len(samples) - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if samples[mid][0] <= s:
            lo = mid
        else:
            hi = mid
    (s0, a), (s1, b) = samples[lo], samples[hi]
    return a + (b - a) * (s - s0) / max(s1 - s0, 1e-9)


def ai_profile(road, m):
    """the user's AI line as a sideways offset per road point (kept inside the road), or None = automatic"""
    p = m.get("ai_path") or {}
    if p.get("mode") != "drawn" or len(p.get("points") or []) < 2:
        return None
    lane = float(p.get("lane", AI_LANE))
    samples = sorted(project_seq(road, p["points"]))
    out = []
    for i, s in enumerate(road.dist):
        room = max(road.width_at[i] / 2 - lane / 2 - 1.0, 0.0)
        out.append(max(-room, min(room, _interp(samples, s))))
    return out


def ai_line_points(road, lats, every=2):
    """[[x, z]] of a sideways-offset profile (one point every `every` road points + the last)"""
    idx = list(range(0, len(road), every))
    if idx[-1] != len(road) - 1:
        idx.append(len(road) - 1)
    out = []
    for i in idx:
        x, z, _ = road.at(road.dist[i], lats[i])
        out.append([round(x, 2), round(z, 2)])
    return out


def merge_ai_stroke(road, m, stroke, blend=25.0):
    """a pen stroke [(x, z)...] replaces the AI line where it runs (ends blended over `blend` m into the line
    already there - the middle of the road when there was none). Returns the new line's points."""
    old = ai_profile(road, m) or [0.0] * len(road)
    st = sorted(project_seq(road, stroke))
    if len(st) < 2:
        return ai_line_points(road, old)
    s0, s1 = st[0][0], st[-1][0]
    new = []
    for i, s in enumerate(road.dist):
        if s0 <= s <= s1:
            new.append(_interp(st, s))
        elif s0 - blend < s < s0:
            t = (s0 - s) / blend
            new.append(st[0][1] * (1 - t) + old[i] * t)
        elif s1 < s < s1 + blend:
            t = (s - s1) / blend
            new.append(st[-1][1] * (1 - t) + old[i] * t)
        else:
            new.append(old[i])
    return ai_line_points(road, new)


def racing_line(road, lane=AI_LANE, full_at_radius=70.0):
    """an automatic racing line: towards the inside of every bend (all the way in at a radius of `full_at_radius`
    m or tighter), smoothed so it swings in before the bend and out after it. Returns its points."""
    n = len(road)
    curv = []
    for i in range(n):
        a, b = max(i - 3, 0), min(i + 3, n - 1)
        da = (road.heading[b] - road.heading[a] + math.pi) % (2 * math.pi) - math.pi
        curv.append(da / max(road.dist[b] - road.dist[a], 1e-6))
    # a turn with a rising heading bends towards -side: the inside is a negative offset
    lat = []
    for i in range(n):
        room = max(road.width_at[i] / 2 - lane / 2 - 1.0, 0.0)
        lat.append(-room * max(-1.0, min(1.0, curv[i] * full_at_radius)))
    for _ in range(3):                                    # smooth over +-40 m, three times
        k = 10
        pre = [0.0]
        for v in lat:
            pre.append(pre[-1] + v)
        lat = [(pre[min(i + k + 1, n)] - pre[max(i - k, 0)]) / (min(i + k + 1, n) - max(i - k, 0)) for i in range(n)]
    return ai_line_points(road, lat)


def ai_corridor(fe, road, info, m, lead=70.0, margin=2.5):
    """per road point (lo, hi): sideways range for the AI line (see _ai_corridor); inside a lane split the main AI
    line keeps to the first lane (the other lanes are alternative routes: network.alt_routes)"""
    import network
    net = network.get(m, road)
    out = _ai_corridor(fe, road, info, m, lead, margin)
    if net.splits:
        out = [net.main_lane(i, lo, hi) for i, (lo, hi) in enumerate(out)]
    return out


def _ai_corridor(fe, road, info, m, lead=70.0, margin=2.5):
    """per road point (lo, hi): sideways range for the AI line. The user's own line (AI path tab): a lane around
    it. Automatic: the whole road; near a block (from `lead` m before to 10 m after) the widest gap left between
    the blocks and the walls, easing in and out over `lead` m."""
    prof = ai_profile(road, m)
    if prof is not None:
        half_lane = float((m.get("ai_path") or {}).get("lane", AI_LANE)) / 2
        out = []
        for i, lat in enumerate(prof):
            half = road.width_at[i] / 2 - margin
            lo, hi = max(lat - half_lane, -half), min(lat + half_lane, half)
            out.append((lo, max(hi, lo + 4.0)))
        return out
    spans = blocked_spans(fe, road, info, m)
    out = []
    for i, s in enumerate(road.dist):
        half = road.width_at[i] / 2 - margin
        lo, hi = -half, half
        near = [b for b in spans if b[0] - lead <= s <= b[1] + 10]
        if near:
            cuts = sorted((b[2] - margin, b[3] + margin) for b in near)
            gaps, cur = [], -half
            for a, b in cuts:
                if a > cur:
                    gaps.append((cur, a))
                cur = max(cur, b)
            if cur < half:
                gaps.append((cur, half))
            if gaps:
                g_lo, g_hi = max(gaps, key=lambda g: g[1] - g[0])
                d = min(min(abs(s - (b[0] - lead)), abs(s - (b[1] + 10))) for b in near)
                w = min(1.0, d / (lead * 0.6))          # 0 at the edge of the zone -> 1 inside it
                lo, hi = lo + (g_lo - lo) * w, hi + (g_hi - hi) * w
        out.append((lo, max(hi, lo + 4.0)))
    return out

"""
Flat Earth's actorpack.xt = a copy of Mount Haruna's (the game's sprint route), with:
  * the start-grid lists (E0B89805 single player, 3D66BA69 multiplayer: 20 StartPosition, 4 StartLineInfo,
    2 TriggerRegion, 1 NodeActor) moved as one rigid block from Haruna's start line onto ours
  * the power-up lists (699ADE84 single player, 73B55A31 multiplayer: 109 PickupPosition each) laid out in rows of
    3 across our road between the start and the finish
Positioned actors keep a WorldMatrix (Mat43: ax, ay, az, aw; az = forward, aw = position) at +12 of their record;
children (trigger boxes, spline knots) are stored relative to it, so moving the matrix moves them too.
"""
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [os.path.join(HERE, "..", p) for p in ("kslf",)]
import dump        # noqa: E402
import kslf_write  # noqa: E402
sys.path.insert(0, HERE)
import road_layout  # noqa: E402  (actor_axes: the model axes of a turned / tilted actor)

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import paths  # noqa: E402
UNPACKED = paths.UNPACKED
SOURCE = os.path.join(UNPACKED, "levels", "jpn_mharuna", "actorpack.xt")
GRID_LISTS = ("E0B89805", "3D66BA69")
PICKUP_LISTS = ("699ADE84", "73B55A31")
PICKUP_ABOVE_GROUND = 0.70


def derives(types, t, name):
    if types[t]["name"] == name:
        return True
    return any(derives(types, b, name) for b, _ in types[t]["bases"])


def positioned(types, t):
    return derives(types, t, "PosActorCreate")


def each_actor(ctx):
    """(type name, offset of the actor's WorldMatrix in ctx.data) for every positioned actor"""
    for bi, (t, cnt, kind) in enumerate(ctx.rows):
        if kind != 0 or not positioned(ctx.types, t):
            continue
        size = ctx.types[t]["size"]
        for e in range(cnt):
            yield ctx.types[t]["name"], ctx.blocks[bi] + e * size + 12


GRID_GAP = 8.0          # front row this far behind the start line
CAR_ABOVE_GROUND = 1.0


def start_frame(ctx):
    """Haruna's grid frame from its 20 StartPositions: (x, y, z, forward angle) of the point in the middle of the
    FRONT row, y = the cars' mean height (the StartLineInfo markers are not at the grid)"""
    cars = [struct.unpack_from("<12f", ctx.data, at) for name, at in each_actor(ctx) if name == "StartPosition"]
    fx = sum(m[6] for m in cars) / len(cars)
    fz = sum(m[8] for m in cars) / len(cars)
    ang = math.atan2(fx, fz)
    ux, uz = math.sin(ang), math.cos(ang)
    cx = sum(m[9] for m in cars) / len(cars)
    cz = sum(m[11] for m in cars) / len(cars)
    front = max((m[9] - cx) * ux + (m[11] - cz) * uz for m in cars)
    return cx + ux * front, sum(m[10] for m in cars) / len(cars), cz + uz * front, ang


def move_grid(line, angle, ground_y, finish):
    """fn for edit_object: start grid in 2 staggered columns, front row GRID_GAP m behind our start line, cars
    CAR_ABOVE_GROUND above our ground; start-line markers on the line, the finish marker on the finish line;
    other actors of the list moved rigidly with the grid"""
    lx = line[0] - math.sin(angle) * GRID_GAP
    lz = line[1] - math.cos(angle) * GRID_GAP
    ground_y += CAR_ABOVE_GROUND

    def fn(ctx):
        hx, hy, hz, ha = start_frame(ctx)
        rot = angle - ha
        c, s = math.cos(rot), math.sin(rot)

        def turn(vx, vz):                     # rotate around the vertical axis by `rot`
            return vx * c + vz * s, -vx * s + vz * c
        ux, uz = math.sin(angle), math.cos(angle)          # our forward
        axis = (uz, 0.0, -ux, 0.0, 1.0, 0.0, ux, 0.0, uz)   # side, up, forward rows (as the game's grid)
        car = marker = 0
        for name, at in each_actor(ctx):
            m = list(struct.unpack_from("<12f", ctx.data, at))
            if name == "StartPosition":       # Haruna's is a rolling start along a bend: lay out a neat grid
                row, col = car // 2, (-5.0 if car % 2 == 0 else 5.0)
                back = 8.0 * row + (4.0 if car % 2 else 0.0)        # staggered rows
                m = list(axis) + [lx - ux * back + uz * col, ground_y, lz - uz * back - ux * col]
                car += 1
            elif name == "StartLineInfo":     # Type (+92 of the record) 3 = finish sign, others on the start line
                kind = struct.unpack_from("<I", ctx.data, at - 12 + 92)[0]
                px, pz = finish if kind == 3 else line
                m = list(axis) + [px, ground_y - CAR_ABOVE_GROUND, pz]
                marker += 1
            else:                             # Haruna's Hand-of-God zones (reset any car inside them) + their reset
                # node: moved with the grid they caught every car at the start ("empty world" until a respawn)
                # -> parked far under the map
                m[9], m[10], m[11] = lx, ground_y - 1000.0, lz
            struct.pack_into("<12f", ctx.data, at, *m)
    return fn


PARK_BELOW = 300.0      # unused actors go this far under the floor (out of reach, out of sight)


def place_pickups(spots, axis):
    """fn for edit_object: power-up k goes to spots[k] (x, y, z[, heading]), turned like the road (`axis` = the 9
    rotation floats, or the spot's own heading); the rest are parked under the floor"""
    def fn(ctx):
        k = 0
        for name, at in each_actor(ctx):
            if name != "PickupPosition":
                continue
            sp = spots[min(k, len(spots) - 1)]
            x, y, z = sp[:3]
            if k >= len(spots):
                y -= PARK_BELOW
            rot = axis
            if len(sp) > 3:                    # own heading (curved road): forward = (sin a, cos a)
                a = sp[3]
                rot = (math.cos(a), 0.0, -math.sin(a), 0.0, 1.0, 0.0, math.sin(a), 0.0, math.cos(a))
            struct.pack_into("<12f", ctx.data, at, *rot, x, y, z)
            if k < len(spots) and len(sp) > 4 and sp[4]:          # own power-up type (PickupType uid)
                struct.pack_into("<I", ctx.data, at - 12 + 60, ctx.add_uid(int(sp[4], 16), "PickupType"))
            k += 1
    return fn


AUDIO_LIST = "AD458635"        # Haruna: 15 AudioActors (crowds, flares) - the route lists it


def place_audio(spots):
    """fn for edit_object: AudioActor k -> spots[k] (x, y, z[, AudioEntityProxy uid to play]) (extra actors
    parked under the floor)"""
    def fn(ctx):
        k = 0
        for name, at in each_actor(ctx):
            if name != "AudioActor":
                continue
            if k < len(spots):
                x, y, z = spots[k][:3]
            else:
                x, y, z = spots[-1][0], spots[-1][1] - PARK_BELOW, spots[-1][2]
            struct.pack_into("<3f", ctx.data, at + 36, x, y, z)
            if k < len(spots) and len(spots[k]) > 3 and spots[k][3]:
                # AudioActor +60 = AudioComponent (inline AudioComponentComp, 8 bytes), its AudioEntity ref at +4
                struct.pack_into("<I", ctx.data, at - 12 + 64, ctx.add_uid(spots[k][3], "AudioEntityProxy"))
            k += 1
    return fn


TILE_LIST = "879EC5CA"         # Haruna: 144 DynamicObjActors (not used by any route) -> our road tiles
TILE_LIST_AS = "3ED4CF3F"      # ... served under the uid of the lamp-post list, which the route already lists


def place_tiles(tiles, def_uid):
    """fn for edit_object: DynamicObjActor k -> tiles[k] (x, y, z[, def uid[, turn]]), showing DynamicObjDef
    `def_uid` or the tile's own (external refs, defined in amax.bin); actors beyond len(tiles) go to the last spot"""
    def fn(ctx):
        uids = sorted({t[3] if len(t) > 3 and t[3] else def_uid for t in tiles})
        refs = {u: ctx.add_uid(u, "DynamicObjDef") for u in uids}
        k = 0
        for name, at in each_actor(ctx):
            if name != "DynamicObjActor":
                continue
            t = tiles[min(k, len(tiles) - 1)]
            x, y, z = t[:3]
            if k >= len(tiles):          # spare tile actors: under the floor (not on top of the last tile)
                y -= PARK_BELOW
            ang = t[4] if len(t) > 4 else 0.0           # turn about the up axis (radians)
            p = t[5] if len(t) > 5 else 0.0             # pitch: model x tilted up by p (ramps)
            k_ = t[6] if len(t) > 6 else 1.0            # scale (the user's, 3D view S): number or [x, y, z]
            kx, ky, kz = (k_,) * 3 if isinstance(k_, (int, float)) else k_
            rx, rz = t[7] if len(t) > 7 else (0.0, 0.0)  # the user's tilts (3D view rotate X / Y)
            # rows = the model's x, y, z axes in the world (pitch = a turn about the model's z axis), times the scale
            ax, ay, az = road_layout.actor_axes(ang, rx, p + rz)
            rows = [v * kx for v in ax] + [v * ky for v in ay] + [v * kz for v in az]
            struct.pack_into("<12f", ctx.data, at, *rows, x, y, z)
            rec = at - 12
            struct.pack_into("<I", ctx.data, rec + 60, refs[t[3] if len(t) > 3 and t[3] else def_uid])   # DynamicObjType
            struct.pack_into("<I", ctx.data, rec + 80, 1)          # IgnoreAlignment
            k += 1
    return fn


LAMP_LIST = "3ED4CF3F"         # Haruna: 356 lamp posts (PhysicalObjectActor, LampB) -> along our road
LAMP_LIST_AS = "DE7D2096"      # ... served under the uid of Haruna's trigger list, which the route lists


def place_lamps(spots):
    """fn for edit_object: PhysicalObjectActor k -> spots[k] (x, y, z, turn in radians about the up axis[, uid of the
    ElementObjectRecord to show, defined in amax.bin - default: Haruna's lamp post]); extras parked under the floor"""
    def fn(ctx):
        uids = sorted({sp[4] for sp in spots if len(sp) > 4})
        refs = {u: ctx.add_uid(u, "ElementObjectRecord") for u in uids}
        k = 0
        for name, at in each_actor(ctx):
            if name != "PhysicalObjectActor":
                continue
            sp = spots[min(k, len(spots) - 1)]
            x, y, z, a = sp[:4]
            if k >= len(spots):
                y -= PARK_BELOW
            rx, rz = sp[5] if len(sp) > 5 else (0.0, 0.0)  # the user's tilts (3D view)
            ax, ay, az = road_layout.actor_axes(a, rx, rz)
            struct.pack_into("<12f", ctx.data, at, *ax, *ay, *az, x, y, z)
            if len(sp) > 4:
                struct.pack_into("<I", ctx.data, at - 12 + 60, refs[sp[4]])      # Definition
            k += 1
    return fn


def swap_uids(path, uid_a, uid_b):
    """let uid_a name the object uid_b named and the other way round (swap the object indices in section 4)"""
    import kslf
    d = bytearray(open(path, "rb").read())
    o4, n4 = kslf.Kslf(bytes(d)).sections[4]
    rows = {struct.unpack_from("<I", d, o4 + 8 * i)[0]: o4 + 8 * i for i in range(n4)}
    a, b = rows[int(uid_a, 16)], rows[int(uid_b, 16)]
    ia, ib = struct.unpack_from("<H", d, a + 6)[0], struct.unpack_from("<H", d, b + 6)[0]
    struct.pack_into("<H", d, a + 6, ib)
    struct.pack_into("<H", d, b + 6, ia)
    # each object's section-5 row also names its uid row (first u32): swap those too
    o5, n5 = kslf.Kslf(bytes(d)).sections[5]
    ra, rb = (a - o4) // 8, (b - o4) // 8
    struct.pack_into("<I", d, o5 + 16 * ia, rb)
    struct.pack_into("<I", d, o5 + 16 * ib, ra)
    open(path, "wb").write(d)


def build(out_path, start_line, finish_line, forward_angle, ground_y, road_middle_offset, width=40.0,
          tiles=None, tile_def_uid=None, lamps=None, pickup_step=None, audio=None, lanes=3, pickup_spots=None):
    P = dump.File(SOURCE)
    uid2obj = {"%08X" % u: oi for u, t, oi in P.refs if oi != 0xFFFF}
    # power-up rows: 3 across the road, from 60 m after the start to 40 m before the finish
    sx, sz = start_line[0] + road_middle_offset[0], start_line[2] + road_middle_offset[1]
    fx, fz = finish_line[0] + road_middle_offset[0], finish_line[2] + road_middle_offset[1]
    length = math.hypot(fx - sx, fz - sz)
    ux, uz = (fx - sx) / length, (fz - sz) / length
    side = (-uz, ux)
    count = max(sum(b["count"] for b in P.O[uid2obj[u]]["blocks"] if b["name"] == "PickupPosition")
                for u in PICKUP_LISTS)
    if pickup_step:                 # evenly spaced rows, only between the start and the finish lines
        rows = min(int(length // pickup_step), count // lanes)
        first, span = pickup_step / 2, length - pickup_step
    else:
        rows = (count + lanes - 1) // lanes
        first, span = 60, length - 100
    spots = []
    for r in range(rows):
        d = first + span * r / max(rows - 1, 1)
        for li in range(lanes):
            lane = (li - (lanes - 1) / 2) * width / (lanes + 1)
            spots.append((sx + ux * d + side[0] * lane, ground_y + PICKUP_ABOVE_GROUND, sz + uz * d + side[1] * lane))

    if pickup_spots is not None:        # curved road: the caller laid the rows out along the road
        spots = list(pickup_spots)
        rows = len(spots) // max(lanes, 1)
    tmp = out_path + ".tmp"
    src = SOURCE
    edits = [(u, move_grid((start_line[0] + road_middle_offset[0], start_line[2] + road_middle_offset[1]),
                           forward_angle, ground_y, (fx, fz))) for u in GRID_LISTS]
    a = forward_angle
    road_axis = (math.cos(a), 0.0, -math.sin(a), 0.0, 1.0, 0.0, math.sin(a), 0.0, math.cos(a))
    edits += [(u, place_pickups(spots, road_axis)) for u in PICKUP_LISTS]
    if tiles:
        edits.append((TILE_LIST, place_tiles(tiles, tile_def_uid)))
    if lamps:
        edits.append((LAMP_LIST, place_lamps(lamps)))
    if audio:
        edits.append((AUDIO_LIST, place_audio(audio)))
    # external uids must be added in rising order: add every one the edits need first, all at once
    need = []
    if tiles:
        need += [(t[3] if len(t) > 3 and t[3] else tile_def_uid, "DynamicObjDef") for t in tiles]
    if lamps:
        need += [(sp[4], "ElementObjectRecord") for sp in lamps if len(sp) > 4]
    if audio:
        need += [(sp[3], "AudioEntityProxy") for sp in audio if len(sp) > 3 and sp[3]]
    need = sorted(set(need))
    if need:
        def reserve(ctx):
            for uid, tname in need:
                ctx.add_uid(uid, tname)
        edits.insert(0, (GRID_LISTS[0], reserve))
    for i, (uid, fn) in enumerate(edits):
        dst = out_path if i == len(edits) - 1 else tmp + str(i)
        kslf_write.edit_object(src, dst, uid2obj[uid], fn)
        if src != SOURCE:
            os.remove(src)
        src = dst
    if tiles:
        swap_uids(out_path, TILE_LIST, TILE_LIST_AS)       # tiles <-> lamps: the lamps now answer to TILE_LIST
        if lamps:
            swap_uids(out_path, TILE_LIST, LAMP_LIST_AS)   # lamps <-> Haruna's triggers
    return {"pickup_rows": rows, "pickups": len(spots)}


if __name__ == "__main__":
    sys.path.insert(0, HERE)
    import gen_aid
    _, info = gen_aid.build(x0=-1000.0, z0=-20.0, length=2000.0, y=90.6)
    out = os.path.join(paths.FE_WORK, "actorpack.xt")
    print(build(out, info["start_line"], info["finish_line"], math.atan2(1.0, 0.0), 90.6, info["road_middle_offset"]))
    P = dump.File(out)
    uid2obj = {"%08X" % u: oi for u, t, oi in P.refs if oi != 0xFFFF}
    for u in GRID_LISTS + PICKUP_LISTS:
        acts = [a for a in P.top(P.O[uid2obj[u]])["LevelActors"] if isinstance(a, dict) and "WorldMatrix" in a]
        pos = [(a["_type"], round(a["WorldMatrix"]["aw"]["vx"]), round(a["WorldMatrix"]["aw"]["vy"], 1),
                round(a["WorldMatrix"]["aw"]["vz"])) for a in acts]
        print(u, len(pos), pos[:4], "...")


# ---- a whole LOCATION: several maps in one actorpack ------------------------------------------------------------
# every map needs its own copy of these Haruna lists; the route's ActorLists name them (map 0 uses Haruna's
# own, with the tile/lamp contents moved by swap_uids as before)
ROLE_SOURCE = {"tiles": TILE_LIST, "lamps": LAMP_LIST, "grid_sp": GRID_LISTS[0], "grid_mp": GRID_LISTS[1],
               "pick_sp": PICKUP_LISTS[0], "pick_mp": PICKUP_LISTS[1], "audio": AUDIO_LIST}
ROLE_ROUTE_UID = {"tiles": TILE_LIST_AS, "lamps": LAMP_LIST_AS, "grid_sp": GRID_LISTS[0], "grid_mp": GRID_LISTS[1],
                  "pick_sp": PICKUP_LISTS[0], "pick_mp": PICKUP_LISTS[1], "audio": AUDIO_LIST}
ROLES = ("tiles", "lamps", "grid_sp", "grid_mp", "pick_sp", "pick_mp", "audio")


def renumber_serials(salt):
    """fn for edit_object: every actor of a COPIED list gets its own SerialisableUid (u32 at +0 of each actor
    record; nothing in the actorpack refers to these values). The copies otherwise share the ids of map 1's
    actors - suspected cause of map 2's cars resetting all the time (2026-10-15)."""
    import zlib

    def fn(ctx):
        k = 0
        for bi, (t, cnt, kind) in enumerate(ctx.rows):
            if kind != 0:
                continue
            fs = [f for f in kslf_write._all_fields(ctx.types, t) if f.get("name") == "SerialisableUid"]
            if not fs:
                continue
            size = ctx.types[t]["size"]
            for e in range(cnt):
                at = ctx.blocks[bi] + e * size + fs[0]["offset"]
                struct.pack_into("<I", ctx.data, at, zlib.crc32(b"%08X:%d" % (salt, k)) | 0x80000000)
                k += 1
    return fn


def _needs(m):
    need = []
    need += [(t[3] if len(t) > 3 and t[3] else m["tile_def_uid"], "DynamicObjDef") for t in m["tiles"]]
    need += [(sp[4], "ElementObjectRecord") for sp in m["lamps"] if len(sp) > 4]
    need += [(sp[3], "AudioEntityProxy") for sp in m["audio"] if len(sp) > 3 and sp[3]]
    return need


def build_location(out_path, maps):
    """maps: list of dicts {start_line, finish_line, start_angle, ground_y, width, tiles, tile_def_uid, lamps, audio,
    pickup_spots (x, y, z, heading, type uid hex), list_uids (None for the first map, else 7 new uids)}.
    Returns, per map, {Haruna list uid as the route names it: the uid our copy has}."""
    P = dump.File(SOURCE)
    uid2obj = {"%08X" % u: oi for u, t, oi in P.refs if oi != 0xFFFF}
    data = open(SOURCE, "rb").read()
    tmp = out_path + ".work"

    def edit(obj_index, fn):
        nonlocal data
        open(tmp + "a", "wb").write(data)
        kslf_write.edit_object(tmp + "a", tmp + "b", obj_index, fn)
        data = open(tmp + "b", "rb").read()

    # 1. every external uid any map needs goes into the uid table first, each at its sorted place
    #    (any uid works now; later add_uid calls find these rows)
    need = sorted({n for m in maps for n in _needs(m)} | {(int(sp[4], 16), "PickupType") for m in maps
                                                            for sp in m["pickup_spots"] if len(sp) > 4 and sp[4]})
    for uid, tname in need:
        data, _ = kslf_write.insert_uid(data, uid, tname)
    # 2. copies of the 7 lists for every map after the first (new uids, rising)
    objs = []
    for m in maps:
        if not m.get("list_uids"):
            objs.append({r: uid2obj[ROLE_SOURCE[r]] for r in ROLES})
            continue
        mine = {}
        for r, uid in zip(ROLES, m["list_uids"]):
            f = _tmpfile(data)
            n = len(dump.File(f).O)
            os.remove(f)
            _, data = kslf_write.clone_object(None, None, uid2obj[ROLE_SOURCE[r]], None, uid, None, data_in=data)
            mine[r] = n
            edit(n, renumber_serials(uid))             # own actor ids (not map 1's)
        objs.append(mine)
    # 3. lay out every map in its lists
    for m, o in zip(maps, objs):
        sl, fl, a = m["start_line"], m["finish_line"], m["start_angle"]
        grid = move_grid((sl[0], sl[2]), a, sl[1], (fl[0], fl[2]))
        edit(o["grid_sp"], grid)
        edit(o["grid_mp"], grid)
        road_axis = (math.cos(a), 0.0, -math.sin(a), 0.0, 1.0, 0.0, math.sin(a), 0.0, math.cos(a))
        edit(o["pick_sp"], place_pickups(m["pickup_spots"], road_axis))
        edit(o["pick_mp"], place_pickups(m["pickup_spots"], road_axis))
        edit(o["tiles"], place_tiles(m["tiles"], m["tile_def_uid"]))
        edit(o["lamps"], place_lamps(m["lamps"]))
        edit(o["audio"], place_audio(m["audio"]))
    open(out_path, "wb").write(data)
    for f in (tmp + "a", tmp + "b"):
        if os.path.exists(f):
            os.remove(f)
    # 4. map 0: its tiles / lamps answer to the uids Haruna's route lists (as build() did)
    swap_uids(out_path, TILE_LIST, TILE_LIST_AS)
    swap_uids(out_path, TILE_LIST, LAMP_LIST_AS)
    out = []
    for m in maps:
        if not m.get("list_uids"):
            out.append({})
        else:
            out.append({ROLE_ROUTE_UID[r]: uid for r, uid in zip(ROLES, m["list_uids"])})
    return out


def _tmpfile(data):
    import tempfile
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".xt")
    f.write(data)
    f.close()
    return f.name

"""STEP 2 of the car's own model: the imported .glb as the body of the body car's model (same file layout).
  * every body part of the body car is hidden: its strips are filled with one repeated index (nothing drawn);
    tyres / rims / brakes stay
  * the .glb, fitted on the body like the 3D view (car_model.fit_glb), is cut into chunks; each chunk goes into one
    still body part (exterior / interior) of the TRIM materials (veh_Default_b_c_f[_h].fx: they draw one of the
    car's own pictures through g_MeshTexture, no paint): its vertex range (position, normal, texture spot, baked
    light) and its strip room (all detail levels' room; every level draws that strip, so the car is whole at any
    distance)
  * the .glb's texture replaces the pictures "<car>_1" and "<car>_2" (every stored size, 16 x 16 .. 512 x 512)
  * the wheels stay where the body car has them (an own wheel spacing changed the REAL body car too): the .glb
    is fitted to them (size / forward / up / turn)
  * one point per (position, texture spot), so texture seams stay; too big for the body car (GLB_BODY of the
    class, room()): what fits is placed and the log says so - the player makes the body smaller himself (no
    simplifying here: the fast_simplification package gave errors in the exe; the tab shows the room)
  * the body car's lights put under the ground
Strip facts (cpmodel.py): u16 indices local to the part's vertex base; an even-position triangle (a, b, c) faces
along cross(b - a, c - a).
Car model facts (2026-10-26): material_index = index into the fx file list; the colour channel (0xB, 9) is BAKED
LIGHT (ambient amount + light direction, see _light_bytes; read from the disassembled shader veh_Default_b_c_f in
shaders-speed.pak), not a colour; the pixel shader shows lerp(material colour, texture, texture alpha) * that light.
The car's own pictures are stored as several records, one per
size: "...1c 00 00 00, w, h, 1, mips, DXTn, 0, 3, 'RA' 0 0, size, 0x10, 16 bytes" then the data (16 x 16 = all its
mips, the bigger ones only their top level)."""
import os
import re
import struct
import sys
from collections import defaultdict

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import car_mesh
import car_model

TYPE_SIZE = {6: 12, 0xB: 4, 8: 4, 9: 8, 0xA: 4}
TRIM_FX = "veh_default_b_c_f"           # the trim shaders (they read the car's own pictures, no paint)
TRIM_PICTURES = (b"_1", b"_2")          # the car's own pictures the .glb's texture goes into
STILL = ("e_exterior", "e_interior")    # parts that never move; the body shell first
MAX_TRIS = 10_000                       # an imported body above this: the tab warns (the Astra holds ~12k)
# the body car an imported .glb goes into, per class (A B C D): the roomiest that also has the pictures <car>_1 / _2
# for the texture (RatRod / Supervan have none) - measured 2026-10-28 (room = points; car.glb needs 8,941)
GLB_BODY = ("Opel_AstraExtreme_Stock",  # 10,570 (tested in game)
            "Bowler_Nemesis_Stock",     # 16,128
            "Nissan_Navara_Rat",        #  7,375 (the roomiest of class C)
            "Nissan_Navara_OffRoad")    #  9,703


def _light_bytes(n):
    """the colour channel = BAKED LIGHT (vertex shader: x = ambient amount, yzw = light direction, 2x - 1, into the
    SH sky light; 0 everywhere = a black car): a fit over the Astra's lit body points, n = (x, up, forward)"""
    vals = (71 + 15 * n[1], 119 - 37 * n[1], 127 + 45 * n[2], 127 - 47 * n[0])
    return tuple(max(0, min(255, int(round(v)))) for v in vals)
LEVEL = re.compile(rb"\x1c\x00\x00\x00(....)(....)\x01\x00\x00\x00(....)DXT1\x00\x00\x00\x00\x03\x00\x00\x00RA\x00\x00"
                   rb"(....)", re.S)


def stripify(tris):
    """triangles [(a, b, c)] -> one index strip: greedy walks over shared edges, joined by degenerate triangles
    (each walk starts at an even position, so every triangle keeps its winding). Walks start at the triangle
    with the fewest free neighbours (edges / corners first) and take the best of its 3 turns: long walks = few
    joins (about 1.3 - 2 indices a triangle instead of 3.5)"""
    import heapq
    edge = defaultdict(list)
    for i, (a, b, c) in enumerate(tris):
        for e in ((a, b), (b, c), (c, a)):
            edge[e].append(i)
    used = [False] * len(tris)

    def free_nb(i):
        a, b, c = tris[i]
        return sum(1 for e in ((b, a), (c, b), (a, c)) for j in edge[e] if not used[j] and j != i)

    def walk(first, taken):
        s = list(first)
        got = []
        while True:
            x, y = (s[-2], s[-1]) if len(s) % 2 else (s[-1], s[-2])       # the last triangle's last edge, as
            nxt = next((j for j in edge[(y, x)] if not used[j] and j not in taken), None)  # its neighbour walks it
            if nxt is None:
                return s, got
            taken.add(nxt)
            got.append(nxt)
            s.append(next(v for v in tris[nxt] if v != x and v != y))

    heap = [(free_nb(i), i) for i in range(len(tris))]
    heapq.heapify(heap)
    out = []
    while heap:
        k, start = heapq.heappop(heap)
        if used[start]:
            continue
        if k != free_nb(start):                     # its count went down meanwhile: back in at the right place
            heapq.heappush(heap, (free_nb(start), start))
            continue
        a, b, c = tris[start]
        best = None
        for first in ((a, b, c), (b, c, a), (c, a, b)):
            s, got = walk(first, {start})
            if best is None or len(got) > len(best[1]):
                best = (s, got)
        s, got = best
        used[start] = True
        touched = {start}
        for j in got:
            used[j] = True
            touched.add(j)
        for j in touched:                           # neighbours of the walk have fewer free neighbours now
            x, y, z = tris[j]
            for e in ((y, x), (z, y), (x, z)):
                for n in edge[e]:
                    if not used[n]:
                        heapq.heappush(heap, (free_nb(n), n))
        if out:
            out += [out[-1], s[0]]
            if len(out) % 2:
                out.append(s[0])
        out += s
    return out


def _weld_uv(tri, uv):
    """one point per (position, texture spot): (points, faces, their uv)"""
    key = np.c_[np.round(tri / 1e-4), np.round(uv / 1e-5)].astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    f = inv.reshape(-1, 3)
    f = f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]
    return tri[first], f, uv[first]


def _rooms(M):
    """(meshes [(mesh, element name, vertex stream)], rooms [(mesh, stream)], points room, index room) of a car
    model: the body parts drawn with the trim shaders (they show the car's own picture, no paint) of the still
    parts only - doors / boot / mirrors / wiper / exhausts / steering wheel / gear stick move, come off or are
    cut by their own small box (in game: holes at the side). Every body car has its own room (2026-10-28: the
    Astra 10.5k points, the BMW M3 Drift 4.4k, some none)"""
    els = M["elements"]
    fx = [f.lower() for f in M["fx_files"]]

    def types(defn):
        return tuple(d["type"] for d in defn if d.get("prefix", 0) == 0)
    sidx = {types(vs["definition"]): i for i, vs in enumerate(M["vertex_streams"])}
    meshes = [(m, els[m["object_index"]]["name"] if m["object_index"] < len(els) else "",
               sidx.get(types(M["vert_definitions"][m["definition"]]))) for m in M["meshes"]]
    rooms = []
    for m, el, s in meshes:
        if s is None or el.lower() not in STILL or not fx[m["material_index"]].startswith(TRIM_FX):
            continue
        defs = M["vertex_streams"][s]["definition"]
        if defs[0]["type"] == 9 and any(d["type"] == 11 and d["channel"] == 3 for d in defs) and \
                any(d["type"] == 9 and d["channel"] == 5 for d in defs):
            rooms.append((m, s))
    rooms.sort(key=lambda r: (STILL.index(els[r[0]["object_index"]]["name"].lower()), -r[0]["data1"][0]["vert_count"]))
    vroom = sum(m["data1"][0]["vert_count"] for m, s in rooms)
    iroom = sum(_index_room(m) for m, s in rooms)
    return meshes, rooms, vroom, iroom


_room_cache = {}


def room(base):
    """(points, indices) a body car has for an imported .glb (cached in WORK\\car_preview\\glb_rooms.json)"""
    import json
    import paths
    path = os.path.join(paths.WORK, "car_preview", "glb_rooms.json")
    if not _room_cache and os.path.exists(path):
        try:
            _room_cache.update(json.load(open(path)))
        except (OSError, ValueError):
            pass
    if base not in _room_cache:
        import game_preview
        _, _, v, i = _rooms(car_mesh._read(game_preview._model_bytes(car_model.model_path(base))))
        _room_cache[base] = [v, i]
        os.makedirs(os.path.dirname(path), exist_ok=True)
        json.dump(_room_cache, open(path, "w"))
    return tuple(_room_cache[base])


_need_cache = {}


def need(glb_path):
    """(points, triangles) of an imported .glb with its texture seams kept (what has to fit the body car's room)"""
    key = (glb_path, os.path.getmtime(glb_path))
    if key not in _need_cache:
        raw = car_model.read_glb_body(glb_path)
        pts, faces, _ = _weld_uv(raw["glb"].reshape(-1, 3).astype(np.float64),
                                 raw["glb_uv"].reshape(-1, 2).astype(np.float64))
        _need_cache[key] = (len(pts), len(faces))
    return _need_cache[key]


def fits(base, glb_path):
    """the whole .glb fits this body car (points + 10 %: cut into the parts, points at the cuts count twice - the
    Navara OffRoad's 9,703 placed 98 % of a body needing 8,941; and its strip: about 2.3 indices a triangle)"""
    v, i = room(base)
    p, t = need(glb_path)
    return v >= 1.1 * p and i >= 2.3 * t


def roomy_bodies(cls, glb_path):
    """the class's pickable body cars the .glb fits, the roomiest first"""
    import cars_data as cd
    names = [c["name"] for c in cd.class_cars(cls)]
    return sorted((n for n in names if fits(n, glb_path)), key=lambda n: -room(n)[0])


def glb_model(car, glb_path, log=print):
    """bytes of the body car's model with the .glb as its body"""
    import game_preview
    data = bytearray(game_preview._model_bytes(car_model.model_path(car["base"])))
    M = car_mesh._read(bytes(data))
    meshes, rooms, vroom, iroom = _rooms(M)

    # ---- the .glb: fitted, one point per (position, texture spot) - texture seams always kept. (Points shared at
    # the seams fitted more, but a seam point got the AVERAGE of two unrelated texture spots: the whole body a
    # mosaic - user 2026-10-28, Navara OffRoad body. Too big = holes + the warning below.)
    raw = car_model.read_glb_body(glb_path)
    fitted = car_model.fit_glb(raw, car_model.read_game_car(car["base"]), car.get("glb_fit") or {})
    tri = fitted["glb"].reshape(-1, 3).astype(np.float64)
    uv = raw["glb_uv"].reshape(-1, 2).astype(np.float64)
    pts, faces, uvs = _weld_uv(tri, uv)
    placed = _place(data, M, meshes, rooms, pts, faces, uvs)
    log("   imported body: %d triangles, %d placed in %d trim parts (room %d points, %d indices)" % (
        len(faces), placed, len(rooms), vroom, iroom))
    if placed < len(faces):
        log("   WARNING: the class's body car %s has room for only part of this body - %d of its %d triangles are "
            "missing (holes). Make the .glb smaller (Blender: Decimate) - the Custom car tab says to about how many "
            "triangles." % (car["base"], len(faces) - placed, len(faces)))
    wheels = _wheel_spots(M)
    _hide_lights(data, M, meshes, log)
    _grow_boxes(data, M, np.vstack([pts] + [w + d for w in wheels for d in ((-.2, -.4, -.4), (.2, .4, .4))]), log)
    _put_texture(data, raw.get("glb_image"), log)
    return bytes(data)


def _wheel_spots(M):
    """the body car's wheel centres (x, up, forward). They are NOT moved any more: the game draws the wheels where
    the physics (geometry.wheelBase / trackWidth) puts them, not at these spots - and other physics values on a
    custom car gave the REAL body car the same wheel spacing (tests 2026-10-28)"""
    spots = {tuple(e["matrix"]["position"]) for e in M["elements"]
             if isinstance(e, dict) and car_model._corner(e["name"]) and
             (car_model._kind(e["name"]) in ("tyre", "rim") or e["name"].lower().startswith("e_wheel_"))}
    return [np.array([x, up, fwd]) for x, fwd, up in spots]


BURIED_BOX = (-0.01, -20.0, -0.01, 0.01, -19.99, 0.01)  # (min x, up, fwd, max ...): a speck 20 m under the ground


def _hide_lights(data, M, meshes, log):
    """the body car's lights: their triangles are hidden already, but the game still drew their glows (brake /
    indicator flares) where the body car has its lights (user 2026-10-28; an empty box, then a box under the
    ground did not stop them) - so their POINTS go 20 m under the ground too, and their parts get a tiny box
    there (in the part list only)"""
    moved = 0
    for m, el, s in meshes:
        if s is None or car_model._kind(el) != "light":
            continue
        vs = M["vertex_streams"][s]
        stride, kind = vs["bytes"], vs["definition"][0]["type"]
        vbase = m["data2"][0]["vOffset"] // stride            # (as _place: level 0's base; the other levels'
        for d1 in m["data1"]:                                  # data2 held nonsense on the Navara OffRoad)
            for j in range(d1["vert_count"]):
                k = vbase + d1["vert_offset"] + j
                if not 0 <= k < vs["count"]:
                    continue                                   # never outside the stream
                at = vs["start"] + stride * k
                if kind == 9:
                    struct.pack_into("<4e", data, at, 0.0, -20.0, 0.0, 0.0)
                elif kind == 6:
                    struct.pack_into("<3f", data, at, 0.0, -20.0, 0.0)
                moved += 1
    head = min(vs["start"] for vs in M["vertex_streams"])
    new = struct.pack("<6f", *BURIED_BOX)
    done = 0
    for e in M["elements"]:
        if not isinstance(e, dict) or car_model._kind(e["name"]) != "light":
            continue
        bb = e["bounding_box"]
        old = struct.pack("<6f", bb[0]["x"], bb[0]["z"], bb[0]["y"], bb[1]["x"], bb[1]["z"], bb[1]["y"])
        if old != new:
            done += data.count(old, 0, head)
            data[:head] = data[:head].replace(old, new)
    log("   the body car's lights hidden (%d points + %d boxes put under the ground)" % (moved, done))


def _grow_boxes(data, M, pts, log):
    """the boxes of the still parts and of the models (the game skips a part whose box is off screen) grown to
    hold the whole .glb body: every copy of each old box (stored as min x, up, forward, max x, up, forward)"""
    lo, hi = pts.min(0) - 0.02, pts.max(0) + 0.02
    boxes = [e["bounding_box"] for e in M["elements"] if e and e.get("name", "").lower() in STILL]
    boxes += [m["bounding_box"] for m in M["models"] if m and "bounding_box" in m]
    done = 0
    for bb in boxes:
        old = (bb[0]["x"], bb[0]["z"], bb[0]["y"], bb[1]["x"], bb[1]["z"], bb[1]["y"])
        new = tuple(np.minimum(old[:3], lo)) + tuple(np.maximum(old[3:], hi))
        if new == old or old[0] > old[3]:
            continue
        a, b = struct.pack("<6f", *old), struct.pack("<6f", *new)
        done += data.count(a)
        data[:] = data.replace(a, b)
    log("   boxes grown to hold the body: %d" % done)


def _put_texture(data, img, log):
    """the .glb's texture into every stored size of the car's own pictures "<car>_1" and "<car>_2" (the trim
    shaders read one of them: in game the body showed the lights picture _2 - tests 2026-10-26)"""
    import cpmodel
    import dxt
    from PIL import Image
    if img is None:
        log("   (the .glb has no texture: the body shows the car's trim picture)")
        return
    names = [(m.start(), m.group(1)) for m in re.finditer(rb"([A-Za-z0-9_]{3,})RA\x01", data)]
    for suffix in TRIM_PICTURES:
        pick = [(a, n) for a, n in names if n.lower().endswith(suffix)]
        if not pick:
            log("   (no picture <car>%s found)" % suffix.decode())
            continue
        a, name = pick[0]
        end = next((b for b, _ in names if b > a), len(data))
        sizes = []
        for m in LEVEL.finditer(data, a, end):
            w, h, mips, size = (struct.unpack("<I", m.group(i))[0] for i in (1, 2, 3, 4))
            px = img.resize((w, h), Image.LANCZOS).tobytes()
            enc = cpmodel.encode_mips(px, w, h, mips) if size == cpmodel.texture_bytes(w, h, mips) else \
                dxt.dxt1_encode(px, w, h)
            if len(enc) != size:
                log("   (picture %s %d x %d: unexpected size, left as it is)" % (name.decode(), w, h))
                continue
            data[m.end() + 20:m.end() + 20 + size] = enc
            sizes.append(w)
        log("   the .glb's texture -> %s (%s)" % (name.decode(), ", ".join("%d" % s for s in sizes)))


def _index_room(m):
    """index room of a part: its detail levels' strips lie one after another, so level 0 can take all of them
    (every level then draws that one strip)"""
    d1 = m["data1"]
    n = d1[0]["face_count"]
    for a, b in zip(d1, d1[1:]):
        if b["face_offset"] != a["face_offset"] + a["face_count"]:
            break
        n += b["face_count"]
    return n


def _lod_records(data, M):
    """{(mesh index, level): byte offset of its data1 record (face offset, face count, vert offset, vert count)}"""
    out = {}
    start = 0
    for i, m in enumerate(M["meshes"]):
        for lod, d1 in enumerate(m["data1"]):
            rec = b"RA\x00\x00" + struct.pack("<4I", d1["face_offset"], d1["face_count"], d1["vert_offset"],
                                              d1["vert_count"])
            at = data.find(rec, start)
            if at >= 0:
                out[(i, lod)] = at + 4
                start = at + 4
    return out


def _place(data, M, meshes, rooms, pts, faces, uvs):
    """hide the body car's body, write the triangles into the rooms; returns how many were placed.
    Per point: position, normal (smooth over all points at the same spot), no dent, the .glb's texture spot and
    the baked light for its normal."""
    key = np.round(pts / 1e-4).astype(np.int64)
    _, same = np.unique(key, axis=0, return_inverse=True)
    same = same.reshape(-1)
    fn = np.cross(pts[faces[:, 1]] - pts[faces[:, 0]], pts[faces[:, 2]] - pts[faces[:, 0]])
    acc = np.zeros((same.max() + 1, 3))
    for k in range(3):
        np.add.at(acc, same[faces[:, k]], fn)
    nrm = acc[same]
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9
    # triangles in the order one big strip walks them (neighbours stay together in a chunk)
    walk = stripify([tuple(f) for f in faces])
    seen, order = set(), []
    lookup = {tuple(sorted(f)): i for i, f in enumerate(faces)}
    for i in range(len(walk) - 2):
        k = tuple(sorted(walk[i:i + 3]))
        j = lookup.get(k)
        if j is not None and j not in seen and len(set(k)) == 3:
            seen.add(j)
            order.append(j)
    order += [j for j in range(len(faces)) if j not in seen]
    lods = _lod_records(data, M)
    index = {id(m): i for i, m in enumerate(M["meshes"])}

    def put_indices(m, lod, values):
        fs = M["face_streams"][m["face_stream_index"]]
        d1 = m["data1"][lod]
        struct.pack_into("<%dH" % d1["face_count"], data, fs["start"] + 2 * d1["face_offset"], *values)

    # ---- hide the body car's body (tyres / rims / brakes stay)
    for m, el, s in meshes:
        if car_model._kind(el) not in ("tyre", "rim"):
            for lod, d1 in enumerate(m["data1"]):
                if d1["face_count"]:
                    put_indices(m, lod, [d1["vert_offset"]] * d1["face_count"])

    # ---- the chunks
    pos = 0
    for m, s in rooms:
        if pos >= len(order):
            break
        vs = M["vertex_streams"][s]
        d1 = m["data1"][0]
        room = _index_room(m)
        stride = vs["bytes"]
        offs, o = {}, 0
        for dd in vs["definition"]:
            k = (dd["type"], dd["channel"]) + ((dd["sub_channel"],) if dd["channel"] == 5 and dd["type"] == 11 else ())
            offs.setdefault(k, o)
            o += TYPE_SIZE.get(dd["type"], 4)
        # as many triangles as the vertex range holds, then fewer until the strip fits
        verts, n = {}, 0
        while pos + n < len(order):
            new = [v for v in faces[order[pos + n]] if v not in verts]
            if len(verts) + len(new) > d1["vert_count"]:
                break
            for v in new:
                verts[v] = len(verts)
            n += 1
        while n:
            take = order[pos:pos + n]
            used = {v for i in take for v in faces[i]}
            local = {v: j for j, v in enumerate(sorted(used, key=lambda v: verts[v]))}
            strip = stripify([tuple(d1["vert_offset"] + local[v] for v in faces[i]) for i in take])
            if len(strip) <= room:
                break
            n = int(n * room / len(strip) * 0.97)
        if not n:
            continue
        vbase = m["data2"][0]["vOffset"] // stride
        for v, j in local.items():
            at = vs["start"] + stride * (vbase + d1["vert_offset"] + j)
            struct.pack_into("<4e", data, at, pts[v][0], pts[v][1], pts[v][2], 0.0)   # 4th = 0 in every game
            # car point (1.0 gave spikes at the first crash: the game's crush code reads it)
            nv = nrm[v]
            if (11, 3) in offs:
                struct.pack_into("<4B", data, at + offs[(11, 3)],
                                 *(max(0, min(254, int(round(127 + 127 * c)))) for c in nv), 0)
            # (11, 5, 2) / (11, 5, 3) = the point's DENT: position / normal move by (byte * 2 - 1) * damage
            # (veh_Paint vertex shader; 2026-10-27: tangents there threw the body into spikes at a power-up hit)
            for k in ((11, 5, 2), (11, 5, 3)):
                if k in offs:
                    struct.pack_into("<4B", data, at + offs[k], 127, 127, 127, 0)
            if (11, 9) in offs:
                struct.pack_into("<4B", data, at + offs[(11, 9)], *_light_bytes(nv))
            if (9, 5) in offs:
                struct.pack_into("<4e", data, at + offs[(9, 5)], uvs[v][0], uvs[v][1], 0.0, 0.0)
        fs = M["face_streams"][m["face_stream_index"]]
        struct.pack_into("<%dH" % room, data, fs["start"] + 2 * d1["face_offset"],
                         *(strip + [strip[-1]] * (room - len(strip))))
        # every detail level draws this one strip (over all the levels' room): a cut strip left holes at a distance
        i = index[id(m)]
        for lod in range(len(m["data1"])):
            if (i, lod) in lods:
                struct.pack_into("<4I", data, lods[(i, lod)], d1["face_offset"], room,
                                 d1["vert_offset"], d1["vert_count"])
        pos += n
    return pos

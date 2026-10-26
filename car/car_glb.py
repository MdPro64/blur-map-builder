"""STEP 2 of the car's own model: the imported .glb as the body of the body car's model (same file layout).
  * every body part of the body car is hidden: its strips are filled with one repeated index (nothing drawn);
    tyres / rims / brakes stay
  * the .glb, fitted on the body like the 3D view (car_model.fit_glb), is cut into chunks; each chunk goes into one
    body part of the TRIM materials (veh_Default_b_c_f[_h].fx: they draw the car's own picture "<car>_1" through
    g_MeshTexture, no paint): its vertex range (position, normal, texture spot) and its strip room (detail level 0;
    the other levels are pointed at level 0's strip, so the car is whole at any distance)
  * the .glb's texture replaces the picture "<car>_1" (every stored size, 16 x 16 .. 512 x 512)
  * one point per (position, texture spot), so texture seams stay; only when that does not fit: one point per
    position, simplified (fast_simplification) to what fits, texture spot of the nearest .glb point
Strip facts (cpmodel.py): u16 indices local to the part's vertex base; an even-position triangle (a, b, c) faces
along cross(b - a, c - a).
Car model facts (2026-10-26): material_index = index into the fx file list; the colour channel (0xB, 9) is damage
data (neutral = 0, 127, 127, 127), not a colour. The car's own pictures are stored as several records, one per
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
TRIM_FX = "veh_default_b_c_f"           # the trim shaders that read "<car>_1"
NO_DAMAGE = (0, 127, 127, 127)
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


def _weld(tri):
    key = np.round(tri / 1e-4).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    inv = inv.reshape(-1)
    pts = np.zeros((inv.max() + 1, 3))
    np.add.at(pts, inv, tri)
    pts /= np.bincount(inv)[:, None]
    f = inv.reshape(-1, 3)
    return pts, f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]


def _weld_uv(tri, uv):
    """one point per (position, texture spot): (points, faces, their uv)"""
    key = np.c_[np.round(tri / 1e-4), np.round(uv / 1e-5)].astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    f = inv.reshape(-1, 3)
    f = f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]
    return tri[first], f, uv[first]


def glb_model(car, glb_path, log=print):
    """bytes of the body car's model with the .glb as its body"""
    import fast_simplification
    import game_preview
    data = bytearray(game_preview._model_bytes(car_model.model_path(car["base"])))
    M = car_mesh._read(bytes(data))
    els = M["elements"]
    fx = [f.lower() for f in M["fx_files"]]

    def types(defn):
        return tuple(d["type"] for d in defn if d.get("prefix", 0) == 0)
    sidx = {types(vs["definition"]): i for i, vs in enumerate(M["vertex_streams"])}
    meshes = [(m, els[m["object_index"]]["name"] if m["object_index"] < len(els) else "",
               sidx.get(types(M["vert_definitions"][m["definition"]]))) for m in M["meshes"]]
    # room = the body parts drawn with the trim shaders (they show the car's own picture, no paint)
    rooms = []
    for m, el, s in meshes:
        if s is None or car_model._kind(el) in ("tyre", "rim") or not fx[m["material_index"]].startswith(TRIM_FX):
            continue
        defs = M["vertex_streams"][s]["definition"]
        if defs[0]["type"] == 9 and any(d["type"] == 11 and d["channel"] == 3 for d in defs) and \
                any(d["type"] == 9 and d["channel"] == 5 for d in defs):
            rooms.append((m, s))
    rooms.sort(key=lambda r: -r[0]["data1"][0]["vert_count"])
    vroom = sum(m["data1"][0]["vert_count"] for m, s in rooms)
    iroom = sum(m["data1"][0]["face_count"] for m, s in rooms)

    # ---- the .glb: fitted; texture seams kept if it fits, else simplified
    raw = car_model.read_glb_body(glb_path)
    fitted = car_model.fit_glb(raw, car_model.read_game_car(car["base"]), car.get("glb_fit") or {})
    tri = fitted["glb"].reshape(-1, 3).astype(np.float64)
    uv = raw["glb_uv"].reshape(-1, 2).astype(np.float64)
    src = bytes(data)
    pts, faces, uvs = _weld_uv(tri, uv)
    n0 = len(faces)
    placed = _place(data, M, meshes, rooms, pts, faces, uvs)
    how = "texture seams kept"
    if placed < len(faces):
        pts0, faces0 = _weld(tri)
        spot = _nearest_lookup(tri, uv)
        target = int(min(vroom * 0.9, iroom / 2.6))
        for attempt in range(6):                 # fewer triangles until all of them fit (no holes)
            data[:] = src
            pts, faces = pts0, faces0
            if len(faces) > target:
                pts, faces = fast_simplification.simplify(pts.astype(np.float32), faces.astype(np.int64),
                                                          1 - target / len(faces))
                pts, faces = np.asarray(pts, np.float64), np.asarray(faces, np.int64)
            placed = _place(data, M, meshes, rooms, pts, faces, spot(pts))
            if placed >= len(faces):
                break
            target = int(target * placed / len(faces) * 0.97)
        how = "simplified"
    log("   imported body: %d triangles -> %d (%s), %d placed in %d trim parts (room %d points, %d indices)" % (
        n0, len(faces), how, placed, len(rooms), vroom, iroom))
    _put_texture(data, raw.get("glb_image"), log, car["name"])
    return bytes(data)


def _nearest_lookup(points, values, cell=0.03):
    """fn(new points) -> their values: the mean value of the .glb's points in the same 3 cm cell (or the
    nearest filled cell around it) - the simplified mesh's points are not the .glb's own"""
    key = np.floor(points / cell).astype(np.int64)
    cells = {}
    for k, c in zip(map(tuple, key), values):
        a = cells.setdefault(k, [np.zeros(values.shape[1]), 0])
        a[0] += c
        a[1] += 1
    mean = {k: a[0] / a[1] for k, a in cells.items()}

    def fn(pts):
        out = np.zeros((len(pts), values.shape[1]))
        for i, k in enumerate(map(tuple, np.floor(pts / cell).astype(np.int64))):
            for r in range(4):
                near = [mean[(k[0] + dx, k[1] + dy, k[2] + dz)] for dx in range(-r, r + 1)
                        for dy in range(-r, r + 1) for dz in range(-r, r + 1)
                        if (k[0] + dx, k[1] + dy, k[2] + dz) in mean]
                if near:
                    out[i] = np.mean(near, axis=0)
                    break
        return out
    return fn


def _put_texture(data, img, log, car_name):
    """the .glb's texture into every stored size of the car's own picture "<car>_1" (the trim shaders' texture),
    under a NEW texture id (the u32 before the first size record): the game keeps one texture per id, so with the
    body car's id it showed the body car's picture (in game test 2026-10-26: a black body)"""
    import zlib
    import cpmodel
    import dxt
    from PIL import Image
    if img is None:
        log("   (the .glb has no texture: the body shows the car's trim picture)")
        return
    names = [(m.start(), m.group(1)) for m in re.finditer(rb"([A-Za-z0-9_]{3,})RA\x01", data)]
    pick = [(a, n) for a, n in names if n.lower().endswith(b"_1")]
    if not pick:
        log("   (no trim picture <car>_1 found: the .glb's texture is not used)")
        return
    a, name = pick[0]
    end = next((b for b, _ in names if b > a), len(data))
    sizes = []
    for k, m in enumerate(LEVEL.finditer(data, a, end)):
        if k == 0:
            old = struct.unpack_from("<I", data, m.start() - 8)[0]
            new = old ^ (zlib.crc32(("bmb car " + car_name).encode()) | 1)
            struct.pack_into("<I", data, m.start() - 8, new)
            # ... and its own name too, same length (in case the game finds pictures by name)
            renamed = (b"bmb%08x" % new + b"x" * len(name))[:len(name) - 2] + b"_1"
            data[a:a + len(name)] = renamed
            log("   texture id %08X -> %08X, name %s -> %s" % (old, new, name.decode(), renamed.decode()))
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
    Per point: position, normal (smooth over all points at the same spot), tangent + binormal, the .glb's texture
    spot and no damage."""
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
            if len(strip) <= d1["face_count"]:
                break
            n = int(n * d1["face_count"] / len(strip) * 0.97)
        if not n:
            continue
        vbase = m["data2"][0]["vOffset"] // stride
        for v, j in local.items():
            at = vs["start"] + stride * (vbase + d1["vert_offset"] + j)
            struct.pack_into("<4e", data, at, pts[v][0], pts[v][1], pts[v][2], 1.0)
            nv = nrm[v]
            t = np.cross(nv, (0.0, 1.0, 0.0)) if abs(nv[1]) < 0.9 else np.cross(nv, (1.0, 0.0, 0.0))
            t /= np.linalg.norm(t) + 1e-9
            for k, vec in (((11, 3), nv), ((11, 5, 2), t), ((11, 5, 3), np.cross(nv, t))):
                if k in offs:
                    struct.pack_into("<4B", data, at + offs[k],
                                     *(max(0, min(254, int(round(127 + 127 * c)))) for c in vec), 0)
            if (11, 9) in offs:
                struct.pack_into("<4B", data, at + offs[(11, 9)], *NO_DAMAGE)
            if (9, 5) in offs:
                struct.pack_into("<4e", data, at + offs[(9, 5)], uvs[v][0], uvs[v][1], 0.0, 0.0)
        put_indices(m, 0, strip + [strip[-1]] * (d1["face_count"] - len(strip)))
        # the other detail levels draw level 0's strip: a cut strip left holes at a distance
        i = index[id(m)]
        for lod in range(1, len(m["data1"])):
            if (i, lod) in lods:
                struct.pack_into("<4I", data, lods[(i, lod)], d1["face_offset"], d1["face_count"],
                                 d1["vert_offset"], d1["vert_count"])
        pos += n
    return pos

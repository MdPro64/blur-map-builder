"""STEP 2 of the car's own model: the imported .glb as the body of the body car's model (same file layout).
  * every body part of the body car is hidden: its strips are filled with one repeated index (nothing drawn);
    tyres / rims / brakes stay
  * the .glb, fitted on the body like the 3D view (car_model.fit_glb), welded into one mesh and simplified
    (fast_simplification) to what fits, is cut into chunks; each chunk goes into one body part (not glass /
    lights / plates / mirrors; the painted panels first): its vertex range (position, normal) and its strip room
    (detail level 0; levels 1 / 2 get as much of the same strip as fits)
Strip facts (cpmodel.py): u16 indices local to the part's vertex base; an even-position triangle (a, b, c) faces
along cross(b - a, c - a)."""
import os
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


def stripify(tris):
    """triangles [(a, b, c)] -> one index strip: greedy walks over shared edges, joined by degenerate triangles
    (each walk starts at an even position, so every triangle keeps its winding)"""
    edge = defaultdict(list)
    for i, (a, b, c) in enumerate(tris):
        for e in ((a, b), (b, c), (c, a)):
            edge[e].append(i)
    used = [False] * len(tris)
    out = []
    for start in range(len(tris)):
        if used[start]:
            continue
        used[start] = True
        s = list(tris[start])
        while True:
            x, y = (s[-2], s[-1]) if len(s) % 2 else (s[-1], s[-2])       # the last triangle's last edge, as
            nxt = next((j for j in edge[(y, x)] if not used[j]), None)    # its neighbour walks it
            if nxt is None:
                break
            used[nxt] = True
            s.append(next(v for v in tris[nxt] if v != x and v != y))
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


def glb_model(car, glb_path, log=print):
    """bytes of the body car's model with the .glb as its body"""
    import fast_simplification
    import game_preview
    data = bytearray(game_preview._model_bytes(car_model.model_path(car["base"])))
    M = car_mesh._read(bytes(data))
    els = M["elements"]

    def types(defn):
        return tuple(d["type"] for d in defn if d.get("prefix", 0) == 0)
    sidx = {types(vs["definition"]): i for i, vs in enumerate(M["vertex_streams"])}
    meshes = [(m, els[m["object_index"]]["name"] if m["object_index"] < len(els) else "",
               sidx.get(types(M["vert_definitions"][m["definition"]]))) for m in M["meshes"]]
    # room = the body parts' materials except glass / lights / plates / mirrors; the painted panels first (the
    # materials of the biggest e_exterior and e_bonnet), the others (trim: may show darker) after them
    glassy, total = defaultdict(int), defaultdict(int)
    for m, el, s in meshes:
        total[m["material_index"]] += m["data1"][0]["vert_count"]
        if any(w in el.lower() for w in ("window", "light", "licen", "mirror")):
            glassy[m["material_index"]] += m["data1"][0]["vert_count"]
    skip_mat = {k for k in total if glassy[k] * 2 > total[k]}
    # ... and the materials of the inside (interior, steering wheel, gear stick: dark)
    skip_mat |= {m["material_index"] for m, el, s in meshes
                 if any(w in el.lower() for w in ("interior", "steering", "gearstick"))
                 and m["data1"][0]["vert_count"] > 50}
    paint = set()
    for name in ("e_exterior", "e_bonnet"):
        cand = [m for m, el, s in meshes if el.lower() == name and m["material_index"] not in skip_mat]
        if cand:
            paint.add(max(cand, key=lambda m: m["data1"][0]["vert_count"])["material_index"])
    rooms = []
    for m, el, s in meshes:
        if s is None or car_model._kind(el) in ("tyre", "rim") or m["material_index"] in skip_mat:
            continue
        defs = M["vertex_streams"][s]["definition"]
        if defs[0]["type"] == 9 and any(d["type"] == 11 and d["channel"] == 3 for d in defs):
            rooms.append((m, s))
    # ONE material only: chunks in different materials looked different in game (paint / white / dark patches)
    per_mat = defaultdict(int)
    for m, s in rooms:
        per_mat[m["material_index"]] += m["data1"][0]["vert_count"]
    one = max(per_mat, key=per_mat.get)
    rooms = [r for r in rooms if r[0]["material_index"] == one]
    rooms.sort(key=lambda r: -r[0]["data1"][0]["vert_count"])
    vroom = sum(m["data1"][0]["vert_count"] for m, s in rooms)
    iroom = sum(m["data1"][0]["face_count"] for m, s in rooms)

    # ---- the .glb: fitted, welded, simplified to fit
    fitted = car_model.fit_glb(car_model.read_glb_body(glb_path), car_model.read_game_car(car["base"]),
                               car.get("glb_fit") or {})
    tri = fitted["glb"].reshape(-1, 3).astype(np.float64)
    pts0, faces0 = _weld(tri)
    colour = _colour_lookup(tri, np.repeat(fitted["glb_rgb"], 3, axis=0))
    n0 = len(faces0)
    src = bytes(data)
    target = int(min(vroom * 1.5, iroom / 2.6))
    for attempt in range(6):                     # fewer triangles until all of them fit (no holes)
        data[:] = src
        pts, faces = pts0, faces0
        if len(faces) > target:
            pts, faces = fast_simplification.simplify(pts.astype(np.float32), faces.astype(np.int64),
                                                      1 - target / len(faces))
            pts, faces = np.asarray(pts, np.float64), np.asarray(faces, np.int64)
        placed = _place(data, M, meshes, rooms, pts, faces, colour(pts))
        if placed >= len(faces):
            break
        target = int(target * placed / len(faces) * 0.97)
    log("   imported body: %d triangles -> %d, %d placed in %d body parts (room %d points, %d indices)" % (
        n0, len(faces), placed, len(rooms), vroom, iroom))
    return bytes(data)


def _colour_lookup(points, rgb, cell=0.03):
    """fn(new points) -> their colours: the mean colour of the .glb's points in the same 3 cm cell (or the
    nearest filled cell around it) - the simplified mesh's points are not the .glb's own"""
    key = np.floor(points / cell).astype(np.int64)
    cells = {}
    for k, c in zip(map(tuple, key), rgb):
        a = cells.setdefault(k, [np.zeros(3), 0])
        a[0] += c
        a[1] += 1
    mean = {k: a[0] / a[1] for k, a in cells.items()}

    def fn(pts):
        out = np.full((len(pts), 3), 200.0)
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


def _place(data, M, meshes, rooms, pts, faces, rgb):
    """hide the body car's body, write the triangles into the rooms; returns how many were placed.
    Per point: position, normal, tangent + binormal (from the normal), one clean texture spot (the uv of the
    body car's highest body point: plain paint) and the colour channel = the .glb's colour there."""
    nrm = np.zeros_like(pts)
    fn = np.cross(pts[faces[:, 1]] - pts[faces[:, 0]], pts[faces[:, 2]] - pts[faces[:, 0]])
    for k in range(3):
        np.add.at(nrm, faces[:, k], fn)
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

    def put_indices(m, lod, values):
        fs = M["face_streams"][m["face_stream_index"]]
        d1 = m["data1"][lod]
        struct.pack_into("<%dH" % d1["face_count"], data, fs["start"] + 2 * d1["face_offset"], *values)

    # one clean texture spot: the uv of the highest point of the biggest body part
    m0, s0 = rooms[0]
    vs0 = M["vertex_streams"][s0]
    o, uv_off = 0, None
    for dd in vs0["definition"]:
        if dd["type"] == 9 and dd["channel"] == 5 and uv_off is None:
            uv_off = o
        o += TYPE_SIZE.get(dd["type"], 4)
    rows = np.frombuffer(bytes(data), np.uint8, vs0["count"] * vs0["bytes"], vs0["start"]).reshape(vs0["count"], -1)
    b0 = m0["data2"][0]["vOffset"] // vs0["bytes"] + m0["data1"][0]["vert_offset"]
    sub = rows[b0:b0 + m0["data1"][0]["vert_count"]]
    top = int(np.argmax(sub[:, 0:8].copy().view(np.float16)[:, 1].astype(np.float32)))
    ref_uv = tuple(sub[top, uv_off:uv_off + 8].copy().view(np.float16).astype(float)) if uv_off is not None else None

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
            for key, vec in (((11, 3), nv), ((11, 5, 2), t), ((11, 5, 3), np.cross(nv, t))):
                if key in offs:
                    struct.pack_into("<4B", data, at + offs[key],
                                     *(max(0, min(254, int(round(127 + 127 * c)))) for c in vec), 0)
            if (11, 9) in offs:
                struct.pack_into("<4B", data, at + offs[(11, 9)], *(int(max(0, min(255, c))) for c in rgb[v]), 255)
            if ref_uv is not None and (9, 5) in offs:
                struct.pack_into("<4e", data, at + offs[(9, 5)], *ref_uv)
        for lod, dl in enumerate(m["data1"]):
            if dl["face_count"]:
                s_ = strip[:dl["face_count"]]
                if lod and len(strip) > dl["face_count"]:
                    s_ = s_[:len(s_) - (len(s_) % 2)]            # (a cut strip ends on a whole triangle pair)
                put_indices(m, lod, s_ + [s_[-1]] * (dl["face_count"] - len(s_)))
        pos += n
    return pos

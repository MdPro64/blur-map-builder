"""
Write a Level.levelphysics whose ground is ONE big flat square (2 triangles), using an existing level's
physics file as the frame (its Header / Routes chunks, base shape fields, surface table and the trailer of the
Main stream are kept). Format: see levelphysics.py.

  python write_flat.py SOURCE OUT --y 90.6 --x0 -2000 --x1 2000 --z0 -2000 --z1 2000
"""
import argparse
import struct

import numpy as np

import levelphysics as lp


def mesh_end(data, mesh):
    """end of the mesh object: after the triangles comes u32 count + count x 12-byte entries (version > 6)"""
    p = mesh["stream_pos"]
    n = struct.unpack_from("<I", data, p)[0]
    return p + 4 + 12 * n


BOX_FACES = (                          # (4 corner indices of the 8 box corners, outward normal)
    ((4, 5, 7, 6), (0, 1, 0)),         # top
    ((0, 1, 5, 4), (0, 0, -1)),        # z min side
    ((2, 6, 7, 3), (0, 0, 1)),         # z max side
    ((0, 4, 6, 2), (-1, 0, 0)),        # x min side
    ((1, 3, 7, 5), (1, 0, 0)),         # x max side
)


def box_mesh(box, base):
    """box = (x0, x1, y0, y1, z0, z1): 8 corners (index bit 0 = x1, bit 1 = z1, bit 2 = y1) and 5 faces (no
    bottom) as triangles with the given outward normals"""
    x0, x1, y0, y1, z0, z1 = box
    corners = [[(x0, x1)[i & 1], (y0, y1)[i >> 2 & 1], (z0, z1)[i >> 1 & 1]] for i in range(8)]
    faces = []
    for (a, b, c, d), n in BOX_FACES:
        faces.append(((base + a, base + b, base + c), n))
        faces.append(((base + a, base + c, base + d), n))
    return corners, faces


# A second mesh object for SOLID things (walls, road blocks), copied from LA River's wall mesh (its 2nd physics object):
# object header (u32 type 0 + 52 bytes of base fields; base[4] 0x5D and base[7] 1 differ from the road mesh's
# 0x1017F / 0), group header (93 = 0x5D = collision type "wall"; the road's groups use 383) and the per-triangle
# u32. The road mesh's group type made boxes act like ramps (wheels only) and walls passable.
WALL_HEADER = bytes.fromhex("00000000000000000ad7233c00000000000000005d000000100000000000803f01000000"
                            "0000000000000000000000000000000000000000")
WALL_GROUP = (93, 2, 2, 0)
WALL_TRI = (2193622843, 0)
TRAILER = 84                               # bytes after a mesh object's 12-byte entry list


def pack_normal(n):
    """the triangle's u32 ("surface") = its unit face normal: x 11-bit signed * 1023 (bits 0-10), y 11-bit * 1023
    (bits 11-21), z 10-bit * 511 (bits 22-31) - checked on LA River (001FF800 = (0, 1, 0), 7F809812 = (.018, .02, 1));
    the game pushes cars out along it (one copied value for every box face pushed cars one way)"""
    n = np.asarray(n, float)
    n = n / (np.linalg.norm(n) + 1e-12)
    x = int(round(n[0] * 1023)) & 0x7FF
    y = int(round(n[1] * 1023)) & 0x7FF
    z = int(round(n[2] * 511)) & 0x3FF
    return x | y << 11 | z << 22


def tri_normal(verts, a, b, c):
    """face normal = cross(b - a, c - a) (the game's convention: a floor's points up)"""
    return np.cross(verts[b] - verts[a], verts[c] - verts[a])


def turned_box(cx, cz, y0, y1, half_len, half_wid, heading):
    """a box turned to `heading` (forward = (sin a, cos a)): piece = (corners, faces with outward normals)"""
    import math
    fx, fz = math.sin(heading), math.cos(heading)
    sx, sz = -fz, fx
    corners, faces = [], []
    for i in range(8):
        f = half_len if i & 1 else -half_len
        w = half_wid if i >> 1 & 1 else -half_wid
        corners.append([cx + fx * f + sx * w, (y0, y1)[i >> 2 & 1], cz + fz * f + sz * w])
    dirs = {(0, 1, 0): (0, 1, 0), (0, 0, -1): (-sx, 0, -sz), (0, 0, 1): (sx, 0, sz),
            (-1, 0, 0): (-fx, 0, -fz), (1, 0, 0): (fx, 0, fz)}
    for (a, b, c, d), n in BOX_FACES:
        faces.append(((a, b, c), dirs[n]))
        faces.append(((a, c, d), dirs[n]))
    return corners, faces


def wall_strip(points, y0, y1, normals):
    """a vertical wall along points [(x, z)...] from y0 to y1 (numbers, or one per point), facing `normals`
    [(nx, nz)...] (one per point): piece = (corners, faces)"""
    corners, faces = [], []
    for k, (x, z) in enumerate(points):
        b = y0[k] if isinstance(y0, (list, tuple)) else y0
        t = y1[k] if isinstance(y1, (list, tuple)) else y1
        corners += [[x, b, z], [x, t, z]]
    for i in range(len(points) - 1):
        nx = (normals[i][0] + normals[i + 1][0]) / 2
        nz = (normals[i][1] + normals[i + 1][1]) / 2
        b0, t0, b1, t1 = 2 * i, 2 * i + 1, 2 * i + 2, 2 * i + 3
        faces.append(((b0, b1, t1), (nx, 0, nz)))
        faces.append(((b0, t1, t0), (nx, 0, nz)))
    return corners, faces


def deck_strip(left, right):
    """a drivable road surface between two edge lines [(x, y, z)...] (bridges, ramps): piece = (corners, faces),
    faces pointing up"""
    corners, faces = [], []
    for a, b in zip(left, right):
        corners += [list(a), list(b)]
    for i in range(len(left) - 1):
        l0, r0, l1, r1 = 2 * i, 2 * i + 1, 2 * i + 2, 2 * i + 3
        faces.append(((l0, r0, r1), (0, 1, 0)))
        faces.append(((l0, r1, l1), (0, 1, 0)))
    return corners, faces


def pack_pieces(pieces, limit=256):
    """group pieces (corners, faces with piece-local indices) into mesh objects of at most `limit` corners"""
    objects, cur_v, cur_f = [], [], []
    for corners, faces in pieces:
        assert len(corners) <= limit, "piece too big"
        if len(cur_v) + len(corners) > limit:
            objects.append((cur_v, cur_f))
            cur_v, cur_f = [], []
        base = len(cur_v)
        cur_v += corners
        cur_f += [((a + base, b + base, c + base), n) for (a, b, c), n in faces]
    if cur_v:
        objects.append((cur_v, cur_f))
    return objects


def wall_object(boxes, table, root, y_lo, y_hi):
    """a complete mesh object with the boxes (5 faces each), wall collision type"""
    verts, wanted = [], []
    for box in boxes:
        corners, faces = box_mesh(box, len(verts))
        verts += corners
        wanted += faces
    assert len(verts) <= 256, "a triangle's corners are bytes: at most 256 vertices in one node"
    verts = np.array(verts, np.float32)
    return verts, wanted


def build_flat(source, y, x0, x1, z0, z1, boxes=(), pieces=(), decks=(), patches=()):
    """flat square floor (road collision) + solid boxes (x0, x1, y0, y1, z0, z1) - e.g. road blocks, edge walls -
    in a second mesh object with the wall collision type"""
    data = open(source, "rb").read()
    mesh = lp.read_mesh(source)
    body, size = lp.find_chunk(data, "Main")
    start = body + 4 + 4 + 4 + 8 * 4 + 12 + 4 + (4 if mesh["version"] > 6 else 0)   # the mesh counts
    end = mesh_end(data, mesh)

    # keep the source's surface table, its first group header (with our triangle count) and the surface of
    # the most upward-facing triangle (floor, not wall)
    V = mesh["verts"]
    T = lp.triangles(mesh)
    n = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    floor = int(np.argmax(np.abs(n[:, 1])))
    up_is_positive = n[floor, 1] > 0
    surf, flags = mesh["surfaces"][floor]
    g = mesh["groups"][0][1]

    sign = 1 if up_is_positive else -1        # winding like the source floor (cross(b - a, c - a) up when +)

    def wind(verts, wanted):
        tris = []
        for (a, b, c), n in wanted:
            cr = np.cross(verts[b] - verts[a], verts[c] - verts[a])
            tris.append((a, b, c) if np.dot(cr, n) * sign > 0 else (a, c, b))
        return tris

    verts = np.array([[x0, y, z0], [x1, y, z0], [x1, y, z1], [x0, y, z1]], np.float32)
    tris = wind(verts, [((0, 1, 2), (0, 1, 0)), ((0, 2, 3), (0, 1, 0))])
    groups = [tris]

    table = data[start + 16 + 24 + len(V) * 12:start + 16 + 24 + len(V) * 12 + 4 * len(mesh["table"])]
    out = bytearray()
    out += struct.pack("<4I", len(verts), len(mesh["table"]), 16 * len(groups) + 12 * len(tris), 1)
    # mesh box: give it height (a 0-high box breaks the box encoding: node boxes are stored as fractions of it,
    # suspected cause of fired weapons "hitting something" at once)
    out += struct.pack("<6f", x0, y - 2.0, z0, x1, y + 20.0, z1)
    out += verts.tobytes()
    out += table
    root = mesh["nodes"][0]                                   # keep the root box encoding (whole mesh)
    out += lp.NODE.pack(*root[:6], len(tris), 0, 0, len(verts))
    for grp in groups:                                        # a group header before every 255 triangles
        out += lp.GROUP.pack(g[0], g[1], g[2], g[3], len(grp))
        for a, b, c in grp:
            out += lp.TRI.pack(a, b, c, pack_normal(tri_normal(verts, a, b, c)), flags)
    out += struct.pack("<I", 0)                               # no extra 12-byte entries

    # the solid things: mesh objects (wall collision type) right after the floor object's trailer. Axis-aligned
    # boxes + ready pieces (turned boxes, wall strips), packed into objects of at most 256 corners each
    all_pieces = [box_mesh(b, 0) for b in boxes] + list(pieces)
    wall = b""
    for corners, faces in pack_pieces(all_pieces):
        wverts, wanted = np.array(corners, np.float32), faces
        # one-sided, facing out of the box; each triangle's u32 = its own packed normal (pack_normal). (Two-sided
        # copies did not help while every triangle carried one copied normal.)
        wtris = wind(wverts, wanted)
        lo, hi = wverts.min(0), wverts.max(0)
        wgroups = [wtris[i:i + 255] for i in range(0, len(wtris), 255)]
        w = bytearray(WALL_HEADER)
        w += struct.pack("<4I", len(wverts), len(mesh["table"]), 16 * len(wgroups) + 12 * len(wtris), 1)
        w += struct.pack("<6f", lo[0] - 1, lo[1] - 2.0, lo[2] - 1, hi[0] + 1, hi[1] + 2.0, hi[2] + 1)
        w += wverts.tobytes()
        w += table
        w += lp.NODE.pack(*root[:6], len(wtris), 0, 0, len(wverts))
        for grp in wgroups:
            w += lp.GROUP.pack(*WALL_GROUP, len(grp))
            for a, b, c in grp:
                w += lp.TRI.pack(a, b, c, pack_normal(tri_normal(wverts, a, b, c)), WALL_TRI[1])
        w += struct.pack("<I", 0)
        w += data[end:end + TRAILER]                          # same trailer as the floor object's
        wall += bytes(w)

    # raised road (bridge decks, ramps): more ROAD-type mesh objects (header = the floor object's own), drivable
    road_header = data[body + 8:start]
    deck = b""
    # surface patches (dirt, water ...): drivable like decks, grouped by their EFFECT MATERIAL (the group header's
    # 3rd value: 1 Tarmac, 4 Dirt ... see flat_earth/surface.py); a deck is material g[2] (the floor's own)
    jobs = [(list(decks), g[2])]
    by_mat = {}
    for piece, mat in patches:
        by_mat.setdefault(mat, []).append(piece)
    jobs += [(lst, mat) for mat, lst in sorted(by_mat.items())]
    for job_pieces, mat in jobs:
        for corners, faces in pack_pieces(job_pieces):
            dverts = np.array(corners, np.float32)
            dtris = wind(dverts, faces)
            lo, hi = dverts.min(0), dverts.max(0)
            dgroups = [dtris[i:i + 255] for i in range(0, len(dtris), 255)]
            w = bytearray(road_header)
            w += struct.pack("<4I", len(dverts), len(mesh["table"]), 16 * len(dgroups) + 12 * len(dtris), 1)
            w += struct.pack("<6f", lo[0] - 1, lo[1] - 2.0, lo[2] - 1, hi[0] + 1, hi[1] + 20.0, hi[2] + 1)
            w += dverts.tobytes()
            w += table
            w += lp.NODE.pack(*root[:6], len(dtris), 0, 0, len(dverts))
            for grp in dgroups:
                w += lp.GROUP.pack(g[0], g[1], mat, g[3], len(grp))
                for a, b, c in grp:
                    w += lp.TRI.pack(a, b, c, pack_normal(tri_normal(dverts, a, b, c)), flags)
            w += struct.pack("<I", 0)
            w += data[end:end + TRAILER]
            deck += bytes(w)

    new = bytearray(data[:start]) + out + data[end:end + TRAILER] + wall + deck + data[end + TRAILER:]
    delta = len(new) - len(data)
    main_hdr = body - 16
    struct.pack_into("<I", new, main_hdr + 8, struct.unpack_from("<I", data, main_hdr + 8)[0] + delta)
    struct.pack_into("<I", new, 0x0C, struct.unpack_from("<I", data, 0x0C)[0] + delta)
    return bytes(new)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("out")
    ap.add_argument("--y", type=float, required=True)
    ap.add_argument("--x0", type=float, default=-2000)
    ap.add_argument("--x1", type=float, default=2000)
    ap.add_argument("--z0", type=float, default=-2000)
    ap.add_argument("--z1", type=float, default=2000)
    a = ap.parse_args()
    open(a.out, "wb").write(build_flat(a.source, a.y, a.x0, a.x1, a.z0, a.z1))
    m = lp.read_mesh(a.out)
    gr = lp.Ground(a.out)
    print("written %s: %d verts, %d triangles; ground at (0, 0) = %s, at (%g, %g) = %s"
          % (a.out, len(m["verts"]), len(m["tris_raw"]), gr.ground(0, 0), a.x1 - 1, a.z1 - 1,
             gr.ground(a.x1 - 1, a.z1 - 1)))

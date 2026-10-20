"""
General .model (CPModel) editor: turn any simple game model into a flat quad of our size, keeping the file layout
(same counts, same offsets) - so it works without decoding the whole ARCH format.

Layout facts (Blender add-on BlurImportExport reader + our checks):
  * vertex stream: count x stride bytes; elements by type 6 float3 (position, y up), 0xB 4 bytes (channel 3 normal:
    127 = 0, 254 = +1; channel 9 colour), 8 half2 (uv), 9 half4
  * face stream: u16 indices, meshes (LODs) = (face offset, face count); type 1 = triangle strip, 0 = list;
    a strip triangle (a, b, c) at an even position faces along cross(b - a, c - a) in the source data
  * textures: "<name>RA" ... u32 width, u32 height, u32 ?, u32 mips, "DXT1", 8 bytes, data (all mips)
  * material texture "<name>_ipr0" (shaders with g_tMaterial): GREEN = glow (emissive): 0 on ordinary objects,
    255 on lamps / traffic lights / lit signs
  * bounding boxes (6 floats min xyz / max xyz) appear several times; found by value
"""
import io
import math
import contextlib
import os
import re
import struct
import sys
import types

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import paths  # noqa: E402
ADDON = paths.ADDON
sys.path[:0] = [os.path.join(HERE, "..", "tex")]
import dxt  # noqa: E402

TYPE_SIZE = {6: 12, 0xB: 4, 8: 4, 9: 8, 0xA: 4}


def _reader():
    """the Blender add-on's reader, without Blender"""
    for m in ["bpy", "bmesh", "bpy.props", "bpy.types", "bpy_extras", "bpy_extras.io_utils"]:
        sys.modules.setdefault(m, types.ModuleType(m))
    sys.modules["bpy_extras.io_utils"].ImportHelper = type("IH", (), {})
    for n in ["StringProperty", "BoolProperty", "EnumProperty"]:
        setattr(sys.modules["bpy.props"], n, lambda **k: None)
    sys.modules["bpy.types"].MeshVertex = object
    sys.modules["bpy.types"].Operator = type("OP", (), {})
    if ADDON not in sys.path:
        sys.path.insert(0, ADDON)
    import BlurImportExport as B
    B.Reader.read_short = lambda s: struct.unpack('<h', s.read(2))[0]
    B.Reader.read_float = lambda s: struct.unpack('<f', s.read(4))[0]
    B.Reader.read_half = lambda s: struct.unpack('<e', s.read(2))[0]
    return B


def parse(data):
    """dict: vstart, vcount, stride, elements [(type, channel, offset)], fstart, fcount, meshes [(face off, count,
    type)], bbox (raw 6 floats of the main element), textures {name: (data offset, w, h, mips)}"""
    import tempfile
    B = _reader()
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".model")
    f.write(data)
    f.close()
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            M = B.read_cpmodel_data(None, f.name)
    finally:
        os.unlink(f.name)
    assert len(M["vertex_streams"]) == 1 and len(M["face_streams"]) == 1, "only 1 vertex + 1 face stream supported"
    vs, fs = M["vertex_streams"][0], M["face_streams"][0]
    off, elements = 0, []
    for e in vs["definition"]:
        elements.append((e["type"], e["channel"], off))
        off += TYPE_SIZE[e["type"]]
    assert off == vs["bytes"], (off, vs["bytes"])
    meshes = []
    for m in M["meshes"]:
        assert m["data2"][0]["vOffset"] == 0
        meshes.append((m["data1"][0]["face_offset"], m["data1"][0]["face_count"], m["face_type"]))
    bb = M["elements"][1]["bounding_box"] if len(M["elements"]) > 1 else M["elements"][0]["bounding_box"]
    raw_bb = (bb[0]["x"], bb[0]["z"], bb[0]["y"], bb[1]["x"], bb[1]["z"], bb[1]["y"])
    textures = {}
    for m in re.finditer(rb"([A-Za-z0-9_]+)RA", data):
        j = data.find(b"DXT1", m.end())
        if j < 0 or j - m.end() > 80:
            continue
        w, h, _, mips = struct.unpack_from("<IIII", data, j - 16)   # WIDTH first (the chevron test showed it)
        textures[m.group(1).decode()] = (j + 12, w, h, mips)
    return {"vstart": vs["start"], "vcount": vs["count"], "stride": vs["bytes"], "elements": elements,
            "fstart": fs["start"], "fcount": fs["count"], "meshes": meshes, "bbox": raw_bb, "textures": textures}


def new_texture_ids(data, salt):
    """give every texture of the model a new id (the u32 after its first name, also repeated in the texture header):
    two models made from the same source otherwise share textures in game (the game keeps one texture per id)"""
    d = bytes(data)
    for name in parse(d)["textures"]:
        m = re.search(re.escape(name.encode()) + b"(....)RA", d, re.S)
        old = m.group(1)
        new = struct.pack("<I", struct.unpack("<I", old)[0] ^ salt)
        d = d.replace(old, new)
    return d


def mip_sizes(w, h, mips):
    out = []
    for _ in range(mips):
        out.append((w, h))
        w, h = max(w // 2, 1), max(h // 2, 1)
    return out


def texture_bytes(w, h, mips):
    return sum(max(a // 4, 1) * max(b // 4, 1) * 8 for a, b in mip_sizes(w, h, mips))


def encode_mips(px, w, h, mips):
    out = bytearray()
    for i, (mw, mh) in enumerate(mip_sizes(w, h, mips)):
        if i:
            px, w, h = _half(px, w, h)
        bw, bh = max(w, 4), max(h, 4)
        pad = bytearray(bw * bh * 3)
        for y in range(bh):
            for x in range(bw):
                s = (min(y, h - 1) * w + min(x, w - 1)) * 3
                pad[(y * bw + x) * 3:(y * bw + x) * 3 + 3] = px[s:s + 3]
        out += dxt.dxt1_encode(bytes(pad), bw, bh)
    return bytes(out)


def _half(px, w, h):
    nw, nh = max(w // 2, 1), max(h // 2, 1)
    out = bytearray(nw * nh * 3)
    for y in range(nh):
        for x in range(nw):
            for c in range(3):
                s = sum(px[(min(2 * y + dy, h - 1) * w + min(2 * x + dx, w - 1)) * 3 + c] for dy in (0, 1) for dx in (0, 1))
                out[(y * nw + x) * 3 + c] = s // 4
    return bytes(out), nw, nh


def solid(w, h, rgb):
    return bytes(rgb) * (w * h)


def noise(w, h, base, spread, seed=7):
    import random
    r = random.Random(seed)
    px = bytearray(w * h * 3)
    for i in range(w * h):
        v = base + r.randint(-spread, spread)
        if r.random() < 0.04:
            v += r.randint(15, 35)
        v = max(0, min(255, v))
        px[3 * i:3 * i + 3] = bytes((v, v, min(v + 2, 255)))
    return bytes(px)


def make_profile(data, length, profile, uv_u, textures=None):
    """a cross-section pulled along x: profile = [(z across, y up, v texture), ...] in order of rising z (one side
    -> the other); the piece runs x -length/2 .. +length/2 with u 0 .. uv_u. One-sided, facing up / into the road
    (in game the strip pairs (end, start) of each profile point, in rising z, face UP - checked in game).
    Each detail level (mesh) gets its own vertices (the levels share vertex numbers in the source).
    Returns (new bytes, info)."""
    P = parse(data)
    d = bytearray(data)
    hx = length / 2
    idx = list(struct.unpack_from("<%dH" % P["fcount"], d, P["fstart"]))
    sets = []
    for foff, fcnt, ftype in P["meshes"]:
        mine = []
        for i in idx[foff:foff + fcnt]:
            if i not in mine:
                mine.append(i)
        sets.append(mine)
    need = 2 * len(profile)
    taken = set()
    # meshes with the fewest vertices first; prefer vertices no other mesh uses
    order = sorted(range(len(sets)), key=lambda k: len(sets[k]))
    chosen = {}
    for k in order:
        others = set().union(*(sets[j] for j in range(len(sets)) if j != k))
        pick = [v for v in sets[k] if v not in taken and v not in others]
        pick += [v for v in sets[k] if v not in taken and v not in pick]
        assert len(pick) >= need, "source mesh %d has too few vertices for this profile" % k
        chosen[k] = pick[:need]
        taken.update(chosen[k])
    ys = [p[1] for p in profile]
    zs = [p[0] for p in profile]
    for k, (foff, fcnt, ftype) in enumerate(P["meshes"]):
        verts = chosen[k]
        assert fcnt >= need, "too few indices"
        strip = []
        for j, (z, y, v) in enumerate(profile):
            # normal: across the profile (perpendicular to the neighbouring segment), y up
            z0, y0 = profile[max(j - 1, 0)][:2]
            z1, y1 = profile[min(j + 1, len(profile) - 1)][:2]
            tz, ty = z1 - z0, y1 - y0
            ln = math.hypot(tz, ty) or 1.0
            nz, ny = -ty / ln, tz / ln                      # the up / inward side
            nb = (127, int(127 + 127 * ny), int(127 + 127 * nz), 0)
            # (end, start) for each profile point in rising z faces UP in game (the in-game test 2026-10-15: the
            # (start, end) order showed only the backs - the road looked gone)
            for e, (x, u) in enumerate(((hx, uv_u), (-hx, 0.0))):
                vi = verts[2 * j + e]
                strip.append(vi)
                at = P["vstart"] + P["stride"] * vi
                for typ, ch, off in P["elements"]:
                    if typ == 6:
                        struct.pack_into("<3f", d, at + off, x, y, z)
                    elif typ == 0xB and ch == 3:
                        struct.pack_into("<4B", d, at + off, *nb)
                    elif typ == 0xB:
                        struct.pack_into("<4B", d, at + off, 255, 255, 255, 255)
                    elif typ == 8:
                        struct.pack_into("<2e", d, at + off, u, v)
                    elif typ == 9:
                        struct.pack_into("<4e", d, at + off, u, v, 0, 0)
        if ftype == 1:
            new = strip + [strip[-1]] * (fcnt - len(strip))
        else:
            new = []
            for j in range(len(profile) - 1):
                a, b, c, e = strip[2 * j], strip[2 * j + 1], strip[2 * j + 2], strip[2 * j + 3]
                new += [a, b, c, b, e, c]
            assert len(new) <= fcnt
            new += [new[-1]] * (fcnt - len(new))
        struct.pack_into("<%dH" % fcnt, d, P["fstart"] + 2 * foff, *new)
    ob = P["bbox"]
    tol = 0.15 * max(ob[3] - ob[0], ob[4] - ob[1], ob[5] - ob[2])
    vend = P["vstart"] + P["stride"] * P["vcount"]
    tex_ranges = [(o, o + texture_bytes(w, h, m)) for o, w, h, m in P["textures"].values()]
    box = (-hx, min(ys) - 0.01, min(zs), hx, max(ys) + 0.01, max(zs))
    at = 0
    found = []
    while at < len(d) - 24:
        if P["vstart"] <= at < vend or any(a <= at < b for a, b in tex_ranges):
            at += 1
            continue
        v = struct.unpack_from("<6f", d, at)
        if all(abs(a - b) <= tol for a, b in zip(v, ob)) and v[0] < v[3] and v[1] <= v[4] and v[2] < v[5]:
            struct.pack_into("<6f", d, at, *box)
            found.append(at)
            at += 24
            continue
        at += 1
    for name, px in (textures or {}).items():
        o, w, h, m = P["textures"][name]
        enc = encode_mips(px, w, h, m)
        assert len(enc) == texture_bytes(w, h, m)
        d[o:o + len(enc)] = enc
    return bytes(d), {"boxes": found, "verts": chosen}


def write_bbox(d, P, box):
    """every 6-float window close to the source's main box -> box; returns their offsets"""
    ob = P["bbox"]
    tol = 0.15 * max(ob[3] - ob[0], ob[4] - ob[1], ob[5] - ob[2])
    vend = P["vstart"] + P["stride"] * P["vcount"]
    tex_ranges = [(o, o + texture_bytes(w, h, m)) for o, w, h, m in P["textures"].values()]
    at, found = 0, []
    while at < len(d) - 24:
        if P["vstart"] <= at < vend or any(a <= at < b for a, b in tex_ranges):
            at += 1
            continue
        v = struct.unpack_from("<6f", d, at)
        if all(abs(a - b) <= tol for a, b in zip(v, ob)) and v[0] < v[3] and v[1] <= v[4] and v[2] < v[5]:
            struct.pack_into("<6f", d, at, *box)
            found.append(at)
            at += 24
            continue
        at += 1
    return found


def make_mesh(data, verts, strip, encoded=None):
    """ANY mesh into a one-mesh, triangle-strip model: verts = [(x, y, z, nx, ny, nz, u, v)] (vertex numbers 0..),
    strip = indices (an even-position triangle (a, b, c) faces along cross(b - a, c - a)); the rest of the strip is
    padded with degenerate triangles. encoded = {texture name: bytes from encode_mips} (same size as the source's).
    Returns (new bytes, info)."""
    P = parse(data)
    assert len(P["meshes"]) == 1 and P["meshes"][0][2] == 1, "needs a model with one triangle-strip mesh"
    assert len(verts) <= P["vcount"], "too many vertices (%d > %d)" % (len(verts), P["vcount"])
    foff, fcnt, _ = P["meshes"][0]
    assert len(strip) <= fcnt, "too many indices (%d > %d)" % (len(strip), fcnt)
    d = bytearray(data)
    for vi, (x, y, z, nx, ny, nz, u, v) in enumerate(verts):
        at = P["vstart"] + P["stride"] * vi
        for typ, ch, off in P["elements"]:
            if typ == 6:
                struct.pack_into("<3f", d, at + off, x, y, z)
            elif typ == 0xB and ch == 3:
                struct.pack_into("<4B", d, at + off, *(max(0, min(254, int(round(127 + 127 * c)))) for c in (nx, ny, nz)), 0)
            elif typ == 0xB:
                struct.pack_into("<4B", d, at + off, 255, 255, 255, 255)
            elif typ == 8:
                struct.pack_into("<2e", d, at + off, u, v)
            elif typ == 9:
                struct.pack_into("<4e", d, at + off, u, v, 0, 0)
    new = list(strip) + [strip[-1]] * (fcnt - len(strip))
    struct.pack_into("<%dH" % fcnt, d, P["fstart"] + 2 * foff, *new)
    xs, ys, zs = [v[0] for v in verts], [v[1] for v in verts], [v[2] for v in verts]
    found = write_bbox(d, P, (min(xs), min(ys) - 0.01, min(zs), max(xs), max(ys) + 0.01, max(zs)))
    for name, enc in (encoded or {}).items():
        o, w, h, m = P["textures"][name]
        assert len(enc) == texture_bytes(w, h, m), name
        d[o:o + len(enc)] = enc
    return bytes(d), {"boxes": found}


def make_quad(data, size_x, size_z, uv_u, uv_v, textures=None, vertical=False):
    """flat quad (both sides), top at y = 0, centred; textures = {texture name: RGB bytes at its own w x h}.
    vertical=True: a wall instead - size_x long (x), size_z high (y from 0 up), in the plane z = 0.
    Returns (new bytes, info)."""
    P = parse(data)
    d = bytearray(data)
    hx, hz = size_x / 2, size_z / 2
    corners = [(-hx, -hz, 0, 0), (-hx, hz, 0, uv_v), (hx, -hz, uv_u, 0), (hx, hz, uv_u, uv_v)]
    idx = list(struct.unpack_from("<%dH" % P["fcount"], d, P["fstart"]))
    used = []
    for foff, fcnt, ftype in P["meshes"]:
        mine = []
        for i in idx[foff:foff + fcnt]:
            if i not in mine:
                mine.append(i)
        assert len(mine) >= 4 and fcnt >= 12, "mesh too small"
        A, Bv, C, D = mine[:4]
        for v, (x, z, u, w) in zip((A, Bv, C, D), corners):
            at = P["vstart"] + P["stride"] * v
            for typ, ch, off in P["elements"]:
                if typ == 6:
                    if vertical:
                        struct.pack_into("<3f", d, at + off, x, z + hz, 0.0)
                    else:
                        struct.pack_into("<3f", d, at + off, x, 0.0, z)
                elif typ == 0xB and ch == 3:
                    struct.pack_into("<4B", d, at + off, *((127, 127, 254, 0) if vertical else (127, 254, 127, 0)))
                elif typ == 0xB:
                    struct.pack_into("<4B", d, at + off, 255, 255, 255, 255)
                elif typ == 8:
                    struct.pack_into("<2e", d, at + off, u, w)
                elif typ == 9:
                    struct.pack_into("<4e", d, at + off, u, w, 0, 0)
        if ftype == 1:      # strip: one side, degenerate joins, the other side
            new = [A, Bv, C, D, D, A, A, C, Bv, D]
            new += [D] * (fcnt - len(new))
        else:               # list
            new = [A, Bv, C, Bv, D, C, A, C, Bv, Bv, C, D]
            new += [D] * (fcnt - len(new))
        struct.pack_into("<%dH" % fcnt, d, P["fstart"] + 2 * foff, *new)
        used.append((A, Bv, C, D))
    # bounding boxes: every 6-float window close to the source's main box
    ob = P["bbox"]
    tol = 0.15 * max(ob[3] - ob[0], ob[4] - ob[1], ob[5] - ob[2])
    vend = P["vstart"] + P["stride"] * P["vcount"]
    tex_ranges = [(o, o + texture_bytes(w, h, m)) for o, w, h, m in P["textures"].values()]
    box = (-hx, 0.0, -0.01, hx, 2 * hz, 0.01) if vertical else (-hx, -0.01, -hz, hx, 0.01, hz)
    found = []
    at = 0
    while at < len(d) - 24:
        if P["vstart"] <= at < vend or any(a <= at < b for a, b in tex_ranges):
            at += 1
            continue
        v = struct.unpack_from("<6f", d, at)
        if all(abs(a - b) <= tol for a, b in zip(v, ob)) and v[0] < v[3] and v[1] <= v[4] and v[2] < v[5]:
            struct.pack_into("<6f", d, at, *box)
            found.append(at)
            at += 24
            continue
        at += 1
    # textures
    for name, px in (textures or {}).items():
        o, w, h, m = P["textures"][name]
        enc = encode_mips(px, w, h, m)
        assert len(enc) == texture_bytes(w, h, m)
        d[o:o + len(enc)] = enc
    return bytes(d), {"boxes": found, "quads": used, "textures": {k: v[1:] for k, v in P["textures"].items()}}

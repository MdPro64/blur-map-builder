"""Level light samples (levelrender chunks LSData + LSTree).

Chunk = name[8] + u32 size + u16 0 + u16 0x41 + data.
LSData: u32 count + count x 24 bytes = 12 float16 = R, G, B x (x, y, z, overall) spherical harmonics (L1).
LSTree: u32 plane count + planes (4 floats: nx, ny, nz, d)
        u32 point count + points (u32 LSData index, 3 x float16 position)  (10 bytes)
        tree nodes in preorder, u32 words: inner = (1, plane index) followed by its two subtrees,
                                            leaf  = (0xFFFFFFFF, first point, point count)
Objects get their light from the points of the leaf their position falls into.
"""
import struct

import numpy as np


def find_chunk(d, name):
    o = d.find(name.encode() + b"\0" * (8 - len(name)))
    assert o > 0 and d.find(name.encode() + b"\0" * (8 - len(name)), o + 1) < 0, name
    size = struct.unpack_from("<I", d, o + 8)[0]
    return o, o + 16, size


def parse(d):
    _, b, size = find_chunk(d, "LSTree")
    end = b + size
    n_planes = struct.unpack_from("<I", d, b)[0]
    planes = [struct.unpack_from("<4f", d, b + 4 + 16 * i) for i in range(n_planes)]
    p = b + 4 + 16 * n_planes
    n_pts = struct.unpack_from("<I", d, p)[0]
    pts = []
    for i in range(n_pts):
        idx = struct.unpack_from("<I", d, p + 4 + 10 * i)[0]
        xyz = np.frombuffer(d[p + 8 + 10 * i:p + 14 + 10 * i], dtype="<f2").astype(float).tolist()
        pts.append((idx, xyz))
    q = p + 4 + 10 * n_pts
    n_nodes = struct.unpack_from("<I", d, q)[0]
    words = list(struct.unpack_from("<%dI" % ((end - q - 4) // 4), d, q + 4))

    def node(i):
        if words[i] == 0xFFFFFFFF:
            return ("leaf", words[i + 1], words[i + 2]), i + 3
        assert words[i] == 1, (i, words[i])
        a, j = node(i + 2)
        bnode, k = node(j)
        return ("inner", words[i + 1], a, bnode), k
    tree, used = node(0)
    return {"planes": planes, "points": pts, "tree": tree, "words_used": used, "words": len(words), "n_nodes": n_nodes,
            "offset": b, "size": size}


def rebuild(d, bounds, y, sh):
    """new LSData + LSTree with the SAME sizes (same sample / plane / point / node counts), covering `bounds`
    (x0, x1, z0, z1) instead of the template's area: the area is split into (inner nodes + 1) cells by a kd tree
    (one plane per inner node; first child = the plane's positive side), each cell gets its share of the points on
    a small grid at heights y + 1.5 and y + 8 (bridges); every sample = `sh` (12 floats: R, G, B x (x, y, z, all))."""
    T = parse(d)
    out = bytearray(d)
    # samples
    _, sb, _ = find_chunk(d, "LSData")
    n_samples = struct.unpack_from("<I", d, sb)[0]
    out[sb + 4:sb + 4 + 24 * n_samples] = np.tile(np.array(sh, dtype="<f2"), n_samples).tobytes()
    n_planes, n_pts, n_nodes = len(T["planes"]), len(T["points"]), T["n_nodes"]
    n_inner = (n_nodes - 1) // 2
    n_leaves = n_nodes - n_inner
    assert n_inner <= n_planes and n_leaves >= 1
    planes, words, cells = [], [], []

    def build(x0, x1, z0, z1, n):
        if n == 1:
            cells.append((x0, x1, z0, z1))
            words.extend([0xFFFFFFFF, len(cells) - 1, 0])        # first / count filled in below
            return
        a = n // 2                                            # leaves in the first (positive side) child
        if x1 - x0 >= z1 - z0:
            v = x1 - (x1 - x0) * a / n                         # first child = x >= v (a leaves), second = x < v
            planes.append((1.0, 0.0, 0.0, -v))
            words.extend([1, len(planes) - 1])
            build(v, x1, z0, z1, a)
            build(x0, v, z0, z1, n - a)
        else:
            v = z1 - (z1 - z0) * a / n
            planes.append((0.0, 0.0, 1.0, -v))
            words.extend([1, len(planes) - 1])
            build(x0, x1, v, z1, a)
            build(x0, x1, z0, v, n - a)
    build(bounds[0], bounds[1], bounds[2], bounds[3], n_leaves)
    assert len(planes) == n_inner and len(cells) == n_leaves
    while len(planes) < n_planes:
        planes.append(planes[-1])
    # points: n_pts shared out over the cells, each cell's on a grid (two heights)
    pts, first = [], []
    for c, (x0, x1, z0, z1) in enumerate(cells):
        k = n_pts // n_leaves + (1 if c < n_pts % n_leaves else 0)
        first.append((len(pts), k))
        per_layer = (k + 1) // 2
        g = max(1, int(np.ceil(np.sqrt(per_layer))))
        for i in range(k):
            layer, j = divmod(i, per_layer)
            gx, gz = j % g, j // g
            pts.append(((x0 + (gx + 0.5) * (x1 - x0) / g), y + (1.5 if layer == 0 else 8.0),
                        (z0 + (gz + 0.5) * (z1 - z0) / g)))
    assert len(pts) == n_pts
    li = 0
    for i in range(len(words)):
        if words[i] == 0xFFFFFFFF and (i == 0 or words[i - 1] != 1):
            pass
    # fill the leaves' (first, count) in preorder (cells were appended in the same order)
    i, leaf = 0, 0
    while i < len(words):
        if words[i] == 0xFFFFFFFF:
            words[i + 1], words[i + 2] = first[leaf]
            leaf += 1
            i += 3
        else:
            i += 2
    # write: planes, points, nodes (same layout as parse)
    b = T["offset"]
    for k, pl in enumerate(planes):
        struct.pack_into("<4f", out, b + 4 + 16 * k, *pl)
    p = b + 4 + 16 * n_planes
    for k, (x, yy, z) in enumerate(pts):
        struct.pack_into("<I", out, p + 4 + 10 * k, k % n_samples)
        out[p + 8 + 10 * k:p + 14 + 10 * k] = np.array([x, yy, z], dtype="<f2").tobytes()
    q = p + 4 + 10 * n_pts
    assert struct.unpack_from("<I", out, q)[0] == n_nodes
    assert len(words) == T["words_used"], (len(words), T["words_used"])
    struct.pack_into("<%dI" % len(words), out, q + 4, *words)
    return bytes(out)


def rebuild_points(d, pts, sh):
    """like rebuild, but the light points are GIVEN (exactly as many as the template has: [(x, y, z)]) and the kd tree
    is split around them (balanced: every leaf gets its share of points)."""
    T = parse(d)
    out = bytearray(d)
    _, sb, _ = find_chunk(d, "LSData")
    n_samples = struct.unpack_from("<I", d, sb)[0]
    out[sb + 4:sb + 4 + 24 * n_samples] = np.tile(np.array(sh, dtype="<f2"), n_samples).tobytes()
    n_planes, n_pts, n_nodes = len(T["planes"]), len(T["points"]), T["n_nodes"]
    assert len(pts) == n_pts, (len(pts), n_pts)
    n_inner = (n_nodes - 1) // 2
    n_leaves = n_nodes - n_inner
    planes, words, ordered = [], [], []

    def build(P, n):
        if n == 1:
            words.extend([0xFFFFFFFF, len(ordered), len(P)])
            ordered.extend(P)
            return
        a = n // 2
        xs = [p[0] for p in P]
        zs = [p[2] for p in P]
        axis = 0 if max(xs) - min(xs) >= max(zs) - min(zs) else 2
        P = sorted(P, key=lambda p: p[axis])
        k = len(P) - max(1, int(round(len(P) * a / n)))          # P[k:] (the upper part) -> first child
        k = min(max(k, 1), len(P) - 1)
        lo, hi = k, k                                             # equal values must stay on one side
        while hi < len(P) and P[hi - 1][axis] == P[hi][axis]:
            hi += 1
        while lo > 1 and P[lo - 1][axis] == P[lo][axis]:
            lo -= 1
        if P[lo - 1][axis] != P[lo][axis] and (hi >= len(P) or k - lo <= hi - k):
            k = lo
        elif hi < len(P):
            k = hi
        v = (P[k - 1][axis] + P[k][axis]) / 2.0
        planes.append((1.0, 0.0, 0.0, -v) if axis == 0 else (0.0, 0.0, 1.0, -v))
        words.extend([1, len(planes) - 1])
        build(P[k:], a)
        build(P[:k], n - a)
    # positions are stored as float16: build the tree on the stored (rounded) values
    build([tuple(np.array(p, dtype=np.float16).astype(float)) for p in pts], n_leaves)
    assert len(planes) == n_inner and len(ordered) == n_pts
    while len(planes) < n_planes:
        planes.append(planes[-1])
    b = T["offset"]
    for k, pl in enumerate(planes):
        struct.pack_into("<4f", out, b + 4 + 16 * k, *pl)
    p = b + 4 + 16 * n_planes
    for k, (x, yy, z) in enumerate(ordered):
        struct.pack_into("<I", out, p + 4 + 10 * k, k % n_samples)
        out[p + 8 + 10 * k:p + 14 + 10 * k] = np.array([x, yy, z], dtype="<f2").tobytes()
    q = p + 4 + 10 * n_pts
    assert len(words) == T["words_used"], (len(words), T["words_used"])
    struct.pack_into("<%dI" % len(words), out, q + 4, *words)
    return bytes(out)


def road_points(roads, n, ground_y):
    """n light points along the roads: middle, both edges + 20 m and, every 3rd spot, 150 m out (big scenery);
    1.5 m above the road (bridges included)"""
    total = sum(r.length for r in roads)
    per_spot = 3 + 2 / 3.0
    step = max(total * per_spot / n, 2.0)
    pts = []
    for r in roads:
        s, k = step / 2, 0
        while s < r.length:
            y = ground_y + r.h_at(s) + 1.5
            w = r.w_at(s) / 2
            for lat in ((0.0, w + 20, -(w + 20)) + ((w + 150, -(w + 150)) if k % 3 == 0 else ())):
                x, z, _ = r.at(s, lat)
                pts.append((x, y if abs(lat) < w + 30 else ground_y + 1.5, z))
            s += step
            k += 1
    if len(pts) > n:                                 # thin out evenly to exactly n
        idx = np.linspace(0, len(pts) - 1, n).round().astype(int)
        pts = [pts[i] for i in idx]
    while len(pts) < n:                              # pad with copies a bit higher
        x, y, z = pts[len(pts) % max(1, len(pts))]
        pts.append((x, y + 6.0, z))
    return pts


def leaves(t, path=()):
    if t[0] == "leaf":
        yield t, path
    else:
        yield from leaves(t[2], path + ((t[1], 0),))
        yield from leaves(t[3], path + ((t[1], 1),))

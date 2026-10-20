"""
Read the collision mesh (the invisible ground the car drives on) from levels/<name>/Level.levelphysics.

File: chunk header "  CPPhysic" (12 bytes) + size/count, then chunks of {name[8], u32 size, u16, u16 kind}.
The "Main" chunk is a physics object stream (game loader FUN_009520a0): "Head", u32 version (10),
then objects {u32 type, data} until type 0x0FFFFFFF. Type 0 = mesh shape (FUN_008f1650, vtable 0xEBB858):
  base fields (FUN_00412360): 8 x u32/float, vec3, u32, u32 (version > 6)
  mesh (FUN_009de000, version > 9):
    u32 vertex count, u32 table count, u32 triangle-buffer bytes, u32 node count,
    vec3 bbox min, vec3 bbox max, vertex count x vec3,
    table count x u32 (surface table),
    node count x node (FUN_009d83b0): 6 x i16 box, u32 tri count, u32 child offset, u32 first vertex,
                                      u32 vertex count            (28 bytes)
    then for every LEAF node in depth-first order (first child = next node, second = node + offset):
      group header (FUN_009d8260): u32, u16, u32, u32, u8 tris-in-group     (15 bytes)
      triangle (FUN_009d7f40): u8 a, u8 b, u8 c, u32 surface, u32 flags     (11 bytes)
      a new group header comes whenever the current group's triangles are used up.
"""
import struct

import numpy as np

NODE = struct.Struct("<6h4I")
GROUP = struct.Struct("<IHIIB")
TRI = struct.Struct("<3B2I")


class Reader:
    def __init__(self, data, pos):
        self.d, self.p = data, pos

    def take(self, st):
        v = st.unpack_from(self.d, self.p)
        self.p += st.size
        return v

    def u32(self):
        v = struct.unpack_from("<I", self.d, self.p)[0]
        self.p += 4
        return v


def find_chunk(data, name):
    """Position and size of a top-level chunk's body."""
    p = 0x14                                    # after "  CPPhysic\0\0" + u32 size + u16 + u16
    while p + 16 <= len(data):
        cname = data[p:p + 8].rstrip(b"\0").decode("latin1")
        size = struct.unpack_from("<I", data, p + 8)[0]   # includes this 16-byte chunk header
        if cname == name:
            return p + 16, size - 16
        p += size
    raise ValueError("chunk %s not found" % name)


def read_mesh(path):
    data = open(path, "rb").read()
    body, size = find_chunk(data, "Main")
    r = Reader(data, body)
    assert data[r.p:r.p + 4] == b"Head", "not a physics stream"
    r.p += 4
    version = r.u32()
    shape_type = r.u32()
    assert shape_type == 0, "first object is type %d, expected a mesh (0)" % shape_type
    r.p += 8 * 4 + 12 + 4 + (4 if version > 6 else 0)          # base shape fields
    nverts, ntable, tribytes, nnodes = r.take(struct.Struct("<4I"))
    bbox = r.take(struct.Struct("<6f"))
    verts = np.frombuffer(data, np.float32, nverts * 3, r.p).reshape(-1, 3).copy()
    r.p += nverts * 12
    table = np.frombuffer(data, np.uint32, ntable, r.p).copy()
    r.p += ntable * 4
    nodes = [r.take(NODE) for _ in range(nnodes)]
    tris, surfaces, groups = [], [], []
    stack = [0]
    while stack:
        i = stack.pop()
        box0, box1, box2, box3, box4, box5, ntri, child, vfirst, vcount = nodes[i]
        if ntri == 0:                            # inner node: visit "next" first, "offset" later
            stack.append(i + child)
            stack.append(i + 1)
            continue
        group = r.take(GROUP)
        left = group[4]
        groups.append((i, group))
        for _ in range(ntri):
            if left == 0:
                group = r.take(GROUP)
                left = group[4]
                groups.append((i, group))
            a, b, c, surf, flags = r.take(TRI)
            tris.append((i, group[0], a, b, c))
            surfaces.append((surf, flags))
            left -= 1
    end = body + size
    return {"version": version, "bbox": bbox, "verts": verts, "table": table, "nodes": nodes,
            "tris_raw": tris, "surfaces": surfaces, "groups": groups,
            "stream_pos": r.p, "chunk_end": end, "tribytes": tribytes}


def triangles(mesh):
    """(N, 3) vertex indices. A triangle's 3 bytes count from its tree node's first vertex
    (verified: every index lands inside the node's vertex range)."""
    nodes = mesh["nodes"]
    return np.array([(nodes[n][8] + a, nodes[n][8] + b, nodes[n][8] + c)
                     for n, g0, a, b, c in mesh["tris_raw"]], dtype=np.int64)


class Ground:
    """Height of the collision surface under any (x, z). Uses a 16 m grid of triangle buckets."""
    CELL = 16.0

    def __init__(self, path):
        self.mesh = read_mesh(path)
        V = self.mesh["verts"].astype(np.float64)
        T = triangles(self.mesh)
        self.A, self.B, self.C = V[T[:, 0]], V[T[:, 1]], V[T[:, 2]]
        lo = np.minimum(np.minimum(self.A, self.B), self.C)
        hi = np.maximum(np.maximum(self.A, self.B), self.C)
        self.cells = {}
        for t in range(len(T)):
            for i in range(int(lo[t, 0] // self.CELL), int(hi[t, 0] // self.CELL) + 1):
                for k in range(int(lo[t, 2] // self.CELL), int(hi[t, 2] // self.CELL) + 1):
                    self.cells.setdefault((i, k), []).append(t)

    def heights(self, x, z):
        """All surface heights at (x, z), highest first (bridges / tunnels give several)."""
        ts = self.cells.get((int(x // self.CELL), int(z // self.CELL)), [])
        out = []
        for t in ts:
            a, b, c = self.A[t], self.B[t], self.C[t]
            d = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
            if abs(d) < 1e-9:
                continue
            w1 = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / d
            w2 = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / d
            w3 = 1 - w1 - w2
            if min(w1, w2, w3) >= -1e-6:
                out.append(w1 * a[1] + w2 * b[1] + w3 * c[1])
        return sorted(out, reverse=True)

    def ground(self, x, z, near_y=None):
        """The surface height at (x, z): the one closest to near_y if given (to pick the right level on
        bridges), else the highest."""
        hs = self.heights(x, z)
        if not hs:
            return None
        if near_y is None:
            return hs[0]
        return min(hs, key=lambda h: abs(h - near_y))


if __name__ == "__main__":
    import sys
    m = read_mesh(sys.argv[1] if len(sys.argv) > 1 else r"E:\blur - bak\levels\usa_la_river\Level.levelphysics")
    print("version", m["version"], "verts", len(m["verts"]), "nodes", len(m["nodes"]),
          "triangles", len(m["tris_raw"]), "groups", len(m["groups"]))
    print("stream stopped at 0x%X, chunk ends at 0x%X (%d bytes left)"
          % (m["stream_pos"], m["chunk_end"], m["chunk_end"] - m["stream_pos"]))
    print("expected triangle buffer bytes", m["tribytes"], "= 16*groups + 12*tris ->",
          16 * len(m["groups"]) + 12 * len(m["tris_raw"]))

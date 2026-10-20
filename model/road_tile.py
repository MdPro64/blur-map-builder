"""
Make a flat square "road tile" .model by editing a copy of an existing game model in place (same file layout,
same counts, new contents). Source: models\\objects\\usa_la_river\\misc\\dynamic_cardboardbox01.model (28648 bytes):
one texture (DXT1 128x256, 9 mips), shader default_NB_NL_NM_NV.fx, one vertex stream of 108 vertices x 20 bytes
{float3 position (y up), 4 bytes normal (127 = 0, 254 = +1), half2 uv}, one triangle-strip index stream (206 u16),
two meshes (LODs): verts 0..53 / strip 0..102 (near) and verts 54..107 / strip 103..205 (far, indices absolute).

The tile: a square `size` m wide, its top at y = 0 (actor position = the middle of the top), double-sided (both
windings), normals up, uv 0..`uv_repeat`. Every bounding box of the source (6 floats min xyz / max xyz, found by
value) becomes the tile's box. The texture is replaced by `texture_rgb` (128x256 RGB bytes, mips made here).

  python road_tile.py OUT.model [--size 42] [--repeat 8]
"""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.dirname(HERE)
sys.path[:0] = [os.path.join(MB, "pak"), os.path.join(MB, "tex")]
import dxt  # noqa: E402
import pak  # noqa: E402

SOURCE = "\\models\\objects\\usa_la_river\\misc\\dynamic_cardboardbox01.model"
SOURCE_SIZE = 28648
VSTART, NVERT, VSIZE = 0x5B0B, 108, 20
FSTART, NIDX = 0x63AB, 206
LOD_VERTS, LOD_IDX = 54, 103
TEX_START, TEX_W, TEX_H, TEX_MIPS = 0x4A4, 128, 256, 9
NORMAL_UP = (127, 254, 127, 0)


def source_bytes(root_pak=None):
    import paths
    p = pak.Pak(root_pak or paths.MODEL_PAK)
    d = p.read(p.find(SOURCE))
    assert len(d) == SOURCE_SIZE and d[:9] == b"  CPModel", "unexpected source model"
    assert struct.unpack_from("<I", d, TEX_START - 16)[0] == 9 and d[TEX_START - 12:TEX_START - 8] == b"DXT1"
    assert d[VSTART + VSIZE * NVERT:VSTART + VSIZE * NVERT + 4] == b"RA\0\0"
    return d


def mip_sizes(w, h, mips):
    out = []
    for _ in range(mips):
        out.append((w, h))
        w, h = max(w // 2, 1), max(h // 2, 1)
    return out


def downsample(px, w, h):
    nw, nh = max(w // 2, 1), max(h // 2, 1)
    out = bytearray(nw * nh * 3)
    for y in range(nh):
        for x in range(nw):
            for c in range(3):
                s = 0
                for dy in (0, 1):
                    for dx in (0, 1):
                        sx, sy = min(2 * x + dx, w - 1), min(2 * y + dy, h - 1)
                        s += px[(sy * w + sx) * 3 + c]
                out[(y * nw + x) * 3 + c] = s // 4
    return bytes(out), nw, nh


def encode_mips(px, w, h, mips):
    """DXT1 data of all mip levels (levels below 4x4 are one 8-byte block made from a padded image)"""
    out = bytearray()
    for i, (mw, mh) in enumerate(mip_sizes(w, h, mips)):
        if i:
            px, w, h = downsample(px, w, h)
        bw, bh = max(w, 4), max(h, 4)
        pad = bytearray(bw * bh * 3)
        for y in range(bh):
            for x in range(bw):
                s = (min(y, h - 1) * w + min(x, w - 1)) * 3
                pad[(y * bw + x) * 3:(y * bw + x) * 3 + 3] = px[s:s + 3]
        out += dxt1_blocks(bytes(pad), bw, bh)
    return bytes(out)


def dxt1_blocks(px, w, h):
    return dxt.dxt1_encode(px, w, h)


def asphalt(w=TEX_W, h=TEX_H, seed=7):
    """grey asphalt: dark grey with small random speckles"""
    import random
    r = random.Random(seed)
    px = bytearray(w * h * 3)
    for i in range(w * h):
        v = 205 + r.randint(-12, 12)
        if r.random() < 0.04:
            v += r.randint(15, 35)
        px[3 * i:3 * i + 3] = bytes((v, v, min(v + 2, 255)))
    return bytes(px)


def is_bbox(v):
    x0, y0, z0, x1, y1, z1 = v
    return -0.4 < x0 < -0.2 and -0.01 < y0 < 0.01 and -0.4 < z0 < -0.2 and 0.2 < x1 < 0.4 and 0.4 < y1 < 0.8 \
        and 0.2 < z1 < 0.4


def build(size=42.0, uv_repeat=8.0, texture_rgb=None, root_pak=None, size_z=None, uv_v=None):
    """square `size` m (or size x size_z: x by z), uv 0..uv_repeat along x and 0..uv_v along z"""
    d = bytearray(source_bytes(root_pak))
    h, hz = size / 2, (size_z if size_z is not None else size) / 2
    uv_v = uv_repeat if uv_v is None else uv_v
    # vertices: per LOD, 0-3 the corners (strip order), 4-7 the same corners in mirrored order (other side)
    corners = [(-h, -hz, 0, 0), (-h, hz, 0, uv_v), (h, -hz, uv_repeat, 0), (h, hz, uv_repeat, uv_v)]
    order = [0, 1, 2, 3, 0, 2, 1, 3]
    for lod in range(2):
        for i in range(LOD_VERTS):
            x, z, u, v = corners[order[min(i, 7)]]
            struct.pack_into("<3f4B2e", d, VSTART + VSIZE * (lod * LOD_VERTS + i), x, 0.0, z, *NORMAL_UP, u, v)
        base = lod * LOD_VERTS
        # ONE side only: the mirrored corners 4-7 (in game, the strip 0,1,2,3 alone is the UNDERSIDE: the road
        # vanished; both sides together showed but stayed dark)
        strip = [4, 5, 6, 7] + [7] * (LOD_IDX - 4)
        struct.pack_into("<%dH" % LOD_IDX, d, FSTART + 2 * lod * LOD_IDX, *[base + s for s in strip])
    # bounding boxes
    box = (-h, -0.01, -hz, h, 0.01, hz)
    vend, tend = VSTART + VSIZE * NVERT, TEX_START + texture_bytes()
    found = []
    for at in range(len(d) - 24):
        if VSTART <= at < vend or TEX_START <= at < tend:
            continue
        if is_bbox(struct.unpack_from("<6f", d, at)):
            struct.pack_into("<6f", d, at, *box)
            found.append(at)
    assert len(found) >= 7, found
    # texture
    data = encode_mips(texture_rgb or asphalt(), TEX_W, TEX_H, TEX_MIPS)
    assert len(data) == texture_bytes()
    d[TEX_START:TEX_START + len(data)] = data
    return bytes(d), found


def checker(w=TEX_W, h=TEX_H, cell=32):
    """black and white squares (start / finish line)"""
    px = bytearray(w * h * 3)
    for y in range(h):
        for x in range(w):
            v = 235 if (x // cell + y // cell) % 2 else 20
            px[3 * (y * w + x):3 * (y * w + x) + 3] = bytes((v, v, v))
    return bytes(px)


def texture_bytes():
    return sum(max(w // 4, 1) * max(h // 4, 1) * 8 for w, h in mip_sizes(TEX_W, TEX_H, TEX_MIPS))


if __name__ == "__main__":
    import argparse
    a = argparse.ArgumentParser()
    a.add_argument("out")
    a.add_argument("--size", type=float, default=42.0)
    a.add_argument("--repeat", type=float, default=8.0)
    args = a.parse_args()
    data, found = build(args.size, args.repeat)
    open(args.out, "wb").write(data)
    print("written", args.out, len(data), "bytes; boxes at", [hex(x) for x in found])

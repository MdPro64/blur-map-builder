"""walk an ipf sequentially (minimap.py layout) and report node boundaries"""
import os, sys, struct
MB = r"E:\blur - bak\map_builder"
sys.path.insert(0, os.path.join(MB, "pak")); sys.path.insert(0, os.path.join(MB, "flat_earth"))
import pak, minimap

which = sys.argv[1]
if which == "la":
    p = pak.Pak(r"E:\blur - bak\_mod_project\..\cache\root.pak")
    import build
    p = pak.Pak(build.ROOT_BASE)
    d = p.read(p.find(minimap.SRC_GAME_IPF))
else:
    p = pak.Pak(r"E:\blur - bak\cache\root.pak")
    d = p.read(p.find("\\ui\\data\\ipf\\minimap\\flat_earth_game.ipf"))
print("len", len(d), "head", d[:8].hex())


def walk(pos, depth):
    kind, flags, nl = struct.unpack_from("<III", d, pos)
    name = d[pos + 12:pos + 12 + nl].decode(errors="replace")
    q = pos + 12 + nl
    if kind == 3:
        cnt = struct.unpack_from("<I", d, q)[0]; q += 4
        print("  " * depth + "GROUP %r flags %d kids %d @%X" % (name, flags, cnt, pos))
        for _ in range(cnt):
            q = walk(q, depth + 1)
        return q
    if name == 'bounds':
        print('  ' * depth + 'BOUNDS @%X' % pos)
        return 0x1D3
    start = q
    q += 34 + 24 + 16
    npoly = struct.unpack_from("<I", d, q)[0]; q += 4
    nverts = 0
    for _ in range(npoly):
        nv = struct.unpack_from("<I", d, q)[0]; q += 4 + 24 * nv; nverts += nv
    ntri = struct.unpack_from("<I", d, q)[0]; q += 4
    for _ in range(ntri):
        n = struct.unpack_from("<I", d, q)[0]; q += 4 + 2 * n
    tail = struct.unpack_from("<I", d, q)[0]; q += 4
    print("  " * depth + "SHAPE %r kind %d flags %d @%X polys %d verts %d tris %d tail %d hdr %s end %X"
          % (name, kind, flags, pos, npoly, nverts, ntri, tail, d[start:start + 34].hex() + ' | ' + str(struct.unpack_from('<10f', d, start + 34)), q))
    return q


pos = walk(8, 0)
print("end", hex(pos), "file", hex(len(d)))

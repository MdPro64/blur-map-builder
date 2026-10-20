"""Blur KSLF (.bin / actorpack.xt) reader: header sections + the scrambled string table.
Format (from Blur.exe FUN_00584700 / FUN_005871b0):
  "KSLF", u32 version (1 = plain strings, 2 = scrambled), then 8 (offset, count/size) pairs.
  Section 4 (pair index 3) is the string blob. Version 2 scrambles it as one stream:
  key = 0xCC; for byte i: out = key - (in ^ 0xB3); key += out - i   (all mod 256)
usage: python kslf.py FILE [FILE...]   prints the strings of each file"""
import struct
import sys

MAGIC = b"KSLF"


def descramble(b):
    k, out = 0xCC, bytearray()
    for i, c in enumerate(b):
        o = (k - (c ^ 0xB3)) & 0xFF
        out.append(o)
        k = (k + o - i) & 0xFF
    return bytes(out)


def scramble(plain):
    """Inverse of descramble (for writing files back)."""
    k, out = 0xCC, bytearray()
    for i, o in enumerate(plain):
        out.append(((k - o) & 0xFF) ^ 0xB3)
        k = (k + o - i) & 0xFF
    return bytes(out)


class Kslf:
    def __init__(self, data):
        if data[:4] != MAGIC:
            raise ValueError("not a KSLF file")
        self.data = data
        self.version = struct.unpack_from("<I", data, 4)[0]
        h = struct.unpack_from("<16I", data, 8)
        self.sections = [(h[i], h[i + 1]) for i in range(0, 16, 2)]
        off, size = self.sections[3]
        raw = data[off:off + size]
        self.strings_blob = descramble(raw) if self.version == 2 else raw
        self.strings_off = off

    def strings(self):
        """(offset inside the blob, text) for every 0-terminated string"""
        out, start = [], 0
        for i, c in enumerate(self.strings_blob):
            if c == 0:
                if i > start:
                    out.append((start, self.strings_blob[start:i].decode("latin1")))
                start = i + 1
        return out


if __name__ == "__main__":
    for path in sys.argv[1:]:
        k = Kslf(open(path, "rb").read())
        print("==", path, "version", k.version, "sections", k.sections)
        for off, s in k.strings():
            print("  %5X %s" % (off, s))


def descramble_at(b, i0=0):
    k, out = 0xCC, bytearray()
    for j, c in enumerate(b):
        o = (k - (c ^ 0xB3)) & 0xFF
        out.append(o)
        k = (k + o - (i0 + j)) & 0xFF
    return bytes(out)


class Objects:
    """Top-level objects of a KSLF file. Section 5 = one 16-byte row per top-level object:
    {id, number of object-table rows, data size, string-pool size}; section 6 = object table rows
    {type, count, kind: 0 inline structs, 1 pointers (4 B), 2 arrays (8 B header in memory, 4 B in file)}.
    Section 7 = per object: data blocks (each 4-aligned) followed by the object's scrambled string pool."""

    def __init__(self, k, types):
        d = k.data
        (o5, n5), (o6, n6), (o7, n7) = k.sections[5], k.sections[6], k.sections[7]
        rows = [struct.unpack_from("<3H", d, o6 + 6 * i) for i in range(n6)]
        self.objects = []
        pos, row = o7, 0
        for i in range(n5):
            oid, nrows, dsize, ssize = struct.unpack_from("<4I", d, o5 + 16 * i)
            blocks, p = [], pos
            for t, cnt, kind in rows[row:row + nrows]:
                size = types[t]["size"] if kind == 0 else 4
                blocks.append({"type": t, "name": types[t]["name"], "count": cnt, "kind": kind, "offset": p,
                               "size": size})
                p = (p + size * cnt + 3) & ~3
            pool = d[pos + dsize:pos + dsize + ssize]
            self.objects.append({"id": oid, "blocks": blocks, "data": pos, "dsize": dsize,
                                 "pool_raw": pool, "pool": descramble(pool)})
            pos += dsize + ssize
            row += nrows

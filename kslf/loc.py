"""Blur .loc text files ("OLTX"), plain UTF-16 text by language.
Layout: 0x00 "OLTX", u32 version 1, then 6 blocks described by {u32 offset, u32 size} pairs:
the KEY block at (u32@0x08, u32@0x0C) and 5 language blocks; the language table at 0x14 is
{u32 count=5, then per language: u32 offset, u32 size, char code[4], 12 pad... } (see read()).
Every block: u32 count, u32 8 (header size), count x {u32 LocId hash, u32 offset from block start},
then the strings (key block: ASCII names like CRN_LA_RIVER_6; language blocks: UTF-16 text).
Language block offsets are absolute; the key block offset is relative to 0x84.
usage: python loc.py FILE [filter]"""
import struct
import sys

BASE = 0x84


def blocks(d):
    """[(name, absolute block offset, size)]: key block at 0x84 + u32@0x08 (size u32@0x0C); language i has
    its code at 0x1C + 0x18*i and its absolute {offset, size} 0x10 bytes after the code."""
    out = [("keys", BASE + struct.unpack_from("<I", d, 8)[0], struct.unpack_from("<I", d, 12)[0])]
    n = struct.unpack_from("<I", d, 0x14)[0]
    for i in range(n):
        e = 0x1C + 0x18 * i
        code = d[e:e + 4].rstrip(b"\0").decode()
        off, size = struct.unpack_from("<II", d, e + 0x10)
        out.append((code, off, size))
    return out


def read_block(d, b, wide):
    n = struct.unpack_from("<I", d, b)[0]
    hdr = struct.unpack_from("<I", d, b + 4)[0]
    table = {}
    for j in range(n):
        h, o = struct.unpack_from("<II", d, b + hdr + 8 * j)
        p = b + o
        if wide:
            q = p
            while d[q:q + 2] != b"\0\0":
                q += 2
            table[h] = d[p:q].decode("utf-16-le")
        else:
            table[h] = d[p:d.index(b"\0", p)].decode("latin1")
    return table


def read(path):
    d = open(path, "rb").read()
    assert d[:4] == b"OLTX"
    return {name: read_block(d, b, name != "keys") for name, b, _ in blocks(d)}


if __name__ == "__main__":
    t = read(sys.argv[1])
    f = sys.argv[2].lower() if len(sys.argv) > 2 else ""
    for h, key in sorted(t["keys"].items(), key=lambda x: x[1]):
        en = t.get("en", {}).get(h)
        if f in key.lower() or (en and f in en.lower()):
            print("%08X %-36s %s" % (h, key, en))


def read_ordered(path):
    """(raw bytes, [(hash, key)] in file order, {lang: {hash: text}} , [lang codes])"""
    d = open(path, "rb").read()
    bl = blocks(d)
    _, kb, _ = bl[0]
    n = struct.unpack_from("<I", d, kb)[0]
    keys = []
    for j in range(n):
        h, o = struct.unpack_from("<II", d, kb + 8 + 8 * j)
        keys.append((h, d[kb + o:d.index(b"\0", kb + o)].decode("latin1")))
    langs = {code: read_block(d, b, True) for code, b, _ in bl[1:]}
    return d, keys, langs, [c for c, _, _ in bl[1:]]


def _block(entries, wide, order):
    """entries {hash: text}; order = list of hashes in table order. Strings are stored in table order."""
    n = len(order)
    table, strings = bytearray(), bytearray()
    base = 8 + 8 * n
    for h in order:
        table += struct.pack("<II", h, base + len(strings))
        strings += entries[h].encode("utf-16-le") + b"\0\0" if wide else entries[h].encode("latin1") + b"\0"
    return struct.pack("<II", n, 8) + table + strings


def write(path_in, path_out, add=None, change=None):
    """Rebuild a .loc. add = [(hash, key, text or {lang: text})]; change = {hash: text or {lang: text}}."""
    d, keys, langs, codes = read_ordered(path_in)
    for h, key, text in add or []:
        assert h not in langs[codes[0]], "hash already used"
        keys.append((h, key))
        for c in codes:
            langs[c][h] = text[c] if isinstance(text, dict) else text
    for h, text in (change or {}).items():
        for c in codes:
            langs[c][h] = text[c] if isinstance(text, dict) else text
    keys.sort(key=lambda x: x[1])
    keyblock = _block({h: k for h, k in keys}, False, [h for h, _ in keys])
    out = bytearray(d[:BASE + struct.unpack_from("<I", d, 8)[0]])      # header up to the key block
    out += keyblock
    struct.pack_into("<I", out, 12, len(out) - 0x10)
    for i, c in enumerate(codes):
        start = len(out)
        out += _block(langs[c], True, sorted(langs[c]))
        struct.pack_into("<II", out, 0x1C + 0x18 * i + 0x10, start, len(out) - start)
    open(path_out, "wb").write(out)
    return bytes(out)

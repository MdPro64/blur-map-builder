"""Blur PAK files (cache/*.pak, magic "2KAP", little-endian). Read, replace files, add aliases.

Header: "2KAP", u32 version (2), u32 align (0x800), u32 0x20, u8 ?, u8 entry kind (+0x11: 1 = 0x40-byte
entries with SHA-1, 0 = 0x28-byte entries), ...; names: u32 size at 0x38 then the names at 0x3C, or (if that
size is 0) u32 size at `align` and the names after it. Names are XOR-scrambled with KEY.
Directory (next `align` boundary): u32 count, u32 bytes (= count * entry size), then entries SORTED by hash:
  +0  u32 hash  = ~crc32(lower-case path incl. the leading backslash, e.g. "\\gamedata\\xt\\amax.bin")
  +4  u32 name offset    +8 u32 ?    +12 u32 size    +16 u64 time    +24 u64 offset (in `align` units)
  +32 u32 game_crc(uncompressed data)  +36 u32 stored (zlib) size
  0x40 entries only: +40 u32 0, +44 SHA-1 of the stored data (20 bytes)
Stored data: 0x40 packs = one zlib stream; 0x28 packs = u32 0x10000, then per 64 KB chunk u32 length +
raw deflate data (stored size includes these headers).
The game checks a signature over the directory: modified packs need Blur_mod.exe (one-byte bypass)."""
import hashlib
import struct
import sys
import zlib

KEY = b"VXo40j3@$%\\%`x"
NO_DATA = 0xFFFFFFFFFFFFFFFF


def game_crc(data):
    """What the game checks (FUN_00cb7340): sum of ~crc32 over each 32 KB block of the uncompressed data"""
    return sum(zlib.crc32(data[i:i + 0x8000]) ^ 0xFFFFFFFF for i in range(0, len(data), 0x8000)) & 0xFFFFFFFF


def xor(b):
    return bytes(c ^ KEY[i % len(KEY)] for i, c in enumerate(b))


def name_hash(name):
    name = "\\" + name.replace("/", "\\").lstrip("\\")
    return zlib.crc32(name.lower().encode("latin1")) ^ 0xFFFFFFFF


class Pak:
    def __init__(self, path):
        self.path = path
        d = self.d = open(path, "rb").read()
        assert d[:4] == b"2KAP", d[:4]
        self.version, self.align = struct.unpack_from("<II", d, 4)
        self.esize = 0x40 if d[0x11] == 1 else 0x28
        pos = 0x38
        nsize = struct.unpack_from("<I", d, pos)[0]
        if nsize == 0:
            pos = self.align
            nsize = struct.unpack_from("<I", d, pos)[0]
        self.nsize_at = pos
        self.names_off = pos + 4
        self.names = xor(d[self.names_off:self.names_off + nsize])
        self.dir_off = (self.names_off + nsize + self.align - 1) // self.align * self.align
        count, nbytes = struct.unpack_from("<II", d, self.dir_off)
        assert nbytes == count * self.esize, (count, nbytes, self.esize)
        self.files = []
        for i in range(count):
            a = self.dir_off + 8 + self.esize * i
            h, noff, unk, size, time, off, crc, zsize = struct.unpack_from("<IIIIQQII", d, a)
            name = self.names[noff:self.names.index(b"\0", noff)].decode("latin1")
            self.files.append({"at": a, "hash": h, "name_off": noff, "unk": unk, "size": size, "time": time,
                               "offset": off, "crc": crc, "zsize": zsize, "name": name})
        self.dir_end = self.dir_off + 8 + self.esize * count

    def find(self, name):
        h = name_hash(name)
        for e in self.files:
            if e["hash"] == h:
                return e
        return None

    def stored(self, e):
        a = e["offset"] * self.align
        return self.d[a:a + e["zsize"]]

    def read(self, e):
        if e["offset"] == NO_DATA:
            return None
        raw = self.stored(e)
        if self.esize == 0x40:
            return zlib.decompress(raw)[:e["size"]]
        return unchunk(raw)[:e["size"]]


def unchunk(raw):
    """0x28-entry packs: u32 chunk size (0x10000), then per chunk u32 length + raw deflate data"""
    csize, at, out = struct.unpack_from("<I", raw, 0)[0], 4, bytearray()
    while at < len(raw):
        n = struct.unpack_from("<I", raw, at)[0]
        out += zlib.decompressobj(-15).decompress(raw[at + 4:at + 4 + n])
        at += 4 + n
    return bytes(out)


def chunk(data, csize=0x10000):
    out = bytearray(struct.pack("<I", csize))
    for i in range(0, len(data), csize):
        c = zlib.compressobj(9, zlib.DEFLATED, -15)
        z = c.compress(data[i:i + csize]) + c.flush()
        out += struct.pack("<I", len(z)) + z
    return bytes(out)


def _finish(out, p, dst):
    pad = (len(out) + p.align - 1) // p.align * p.align - len(out)
    out += bytes(pad)
    open(dst, "wb").write(out)


def replace_files(src, dst, new_files):
    """Copy pak `src` to `dst`, giving each {pak path: bytes} new contents. The new data is zlib-compressed
    and appended (aligned); the entry gets the new size, offset, CRC, stored size (and SHA-1)."""
    p = Pak(src)
    out = bytearray(p.d)
    for name, data in new_files.items():
        e = p.find(name)
        if e is None:
            raise KeyError(name)
        comp = zlib.compress(data, 9) if p.esize == 0x40 else chunk(data)
        pos = (len(out) + p.align - 1) // p.align * p.align
        out += bytes(pos - len(out)) + comp
        a = e["at"]
        struct.pack_into("<I", out, a + 12, len(data))
        struct.pack_into("<Q", out, a + 24, pos // p.align)
        struct.pack_into("<II", out, a + 32, game_crc(data), len(comp))
        if p.esize == 0x40:
            out[a + 44:a + 64] = hashlib.sha1(comp).digest()
    _finish(out, p, dst)
    check = Pak(dst)
    for name, data in new_files.items():
        assert check.read(check.find(name)) == data, name
    return len(out)


def add_aliases(src, dst, aliases):
    """Copy pak `src` to `dst` with extra entries {new path: existing path} that point at the SAME stored
    data. The names blob and the directory must still fit in their areas (checked)."""
    p = Pak(src)
    d = bytearray(p.d)
    names = bytearray(p.names)
    entries = [bytes(d[e["at"]:e["at"] + p.esize]) for e in p.files]
    for new, old in aliases.items():
        new = "\\" + new.replace("/", "\\").lstrip("\\")
        e = p.find(old)
        if e is None:
            raise KeyError(old)
        if p.find(new) is not None:
            raise KeyError("already there: " + new)
        ent = bytearray(d[e["at"]:e["at"] + p.esize])
        struct.pack_into("<II", ent, 0, name_hash(new), len(names))
        names += new.encode("latin1") + b"\0"
        entries.append(bytes(ent))
    entries.sort(key=lambda b: struct.unpack_from("<I", b, 0)[0])
    hashes = [struct.unpack_from("<I", b, 0)[0] for b in entries]
    assert len(set(hashes)) == len(hashes), "hash collision"
    first_data = min(f["offset"] for f in p.files if f["offset"] != NO_DATA) * p.align
    al = p.align
    dir_off = max(p.dir_off, (p.names_off + len(names) + al - 1) // al * al)
    end = dir_off + 8 + p.esize * len(entries)
    shift = max(0, (end + al - 1) // al * al - first_data)
    if dir_off != p.dir_off or shift:
        # no room left: grow the names / directory areas and move all the stored data back by `shift` bytes
        # (whole `align` blocks; every entry offset + shift / align). Header +0x24 = names area size (from the
        # first `align` boundary to the directory), +0x28 = directory area size (to the first data) - as in
        # every game pack.
        assert p.nsize_at == al and struct.unpack_from("<II", d, 0x24) == (p.dir_off - al, first_data - p.dir_off), \
            "unknown pack layout"
        for k, ent in enumerate(entries):
            off = struct.unpack_from("<Q", ent, 24)[0]
            if off != NO_DATA:
                ent = bytearray(ent)
                struct.pack_into("<Q", ent, 24, off + shift // al)
                entries[k] = bytes(ent)
        d = d[:p.names_off] + bytes(first_data + shift - p.names_off) + d[first_data:]
        struct.pack_into("<II", d, 0x24, dir_off - al, first_data + shift - dir_off)
    struct.pack_into("<I", d, p.nsize_at, len(names))
    d[p.names_off:p.names_off + len(names)] = xor(bytes(names))
    struct.pack_into("<II", d, dir_off, len(entries), p.esize * len(entries))
    d[dir_off + 8:end] = b"".join(entries)
    open(dst, "wb").write(d)
    check = Pak(dst)
    for new, old in aliases.items():
        a, b = check.find(new), check.find(old)
        assert a is not None and a["offset"] == b["offset"] and a["size"] == b["size"], new
    return len(d)


if __name__ == "__main__":
    p = Pak(sys.argv[1])
    print("entry size", hex(p.esize), "files", len(p.files), "dir", hex(p.dir_off), "-", hex(p.dir_end))
    flt = sys.argv[2].lower() if len(sys.argv) > 2 else ""
    for e in p.files:
        if flt in e["name"].lower():
            print("%-70s %9d" % (e["name"], e["size"]))

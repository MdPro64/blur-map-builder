"""Dump KSLF objects as readable text.
usage: python dump.py FILE [NameFilter]      (prints every top-level object whose Name contains NameFilter)
Encodings found so far:
  sz8 = u32 offset into the object's string pool; ref (flags 5) = u32 index into section 4
  {uid, type, object index or 0xFFFF = in another file}; arrays (flags 0x10x) = {u16 block, u16 first, u32 count}
  where block is a block index inside the same top-level object; enum (flags 1) = u32; inline struct (flags 3)."""
import struct
import sys

import kslf
import schema

PRIM = {"i32": "<i", "u32": "<I", "f32": "<f", "bool": "<I", "u8": "<B", "i16": "<h", "u16": "<H", "i8": "<b",
        "LocId": "<I", "u64": "<Q", "f64": "<d"}


class File:
    def __init__(self, path):
        self.d = open(path, "rb").read()
        self.k = kslf.Kslf(self.d)
        self.T = schema.load_schema(self.k)
        self.O = kslf.Objects(self.k, self.T).objects
        o4, n4 = self.k.sections[4]
        self.refs = [struct.unpack_from("<IHH", self.d, o4 + 8 * i) for i in range(n4)]

    def all_fields(self, t):
        """fields of type t including its base classes (bases first)"""
        out = []
        for b, off in self.T[t]["bases"]:
            out += [dict(f, offset=f["offset"] + off) for f in self.all_fields(b)]
        return out + self.T[t]["fields"]

    def string(self, obj, off):
        p = obj["pool"]
        if off == 0xFFFFFFFF or off >= len(p):
            return None
        return p[off:p.index(b"\0", off)].decode("latin1")

    def ref(self, i):
        if i == 0xFFFFFFFF or i >= len(self.refs):
            return None
        uid, t, oi = self.refs[i]
        return {"uid": "%08X" % uid, "type": self.T[t]["name"], "object": None if oi == 0xFFFF else oi}

    def value(self, obj, t, at, depth):
        if t >= len(self.T):
            return "0x%08X" % struct.unpack_from("<I", self.d, at)[0]
        tn = self.T[t]["name"]
        if tn == "sz8":
            return self.string(obj, struct.unpack_from("<I", self.d, at)[0])
        if tn in PRIM:
            return struct.unpack_from(PRIM[tn], self.d, at)[0]
        if self.T[t]["fields"] or self.T[t]["bases"]:
            return self.struct(obj, t, at, depth + 1)
        return "0x%08X" % struct.unpack_from("<I", self.d, at)[0]

    def struct(self, obj, t, at, depth=0):
        out = {}
        for f in self.all_fields(t):
            a, fl = at + f["offset"], f["flags"]
            if fl == 5:
                out[f["name"]] = self.ref(struct.unpack_from("<I", self.d, a)[0])
            elif fl == 4:
                out[f["name"]] = self.owned(obj, struct.unpack_from("<I", self.d, a)[0], depth)
            elif fl == 1:
                out[f["name"]] = struct.unpack_from("<I", self.d, a)[0]
            elif fl & 0x100:
                blk, first, cnt = struct.unpack_from("<HHI", self.d, a)
                if blk == 0xFFFF or cnt == 0:
                    out[f["name"]] = []
                    continue
                b = obj["blocks"][blk]
                items = []
                for j in range(first, first + cnt):
                    ea = b["offset"] + j * b["size"]
                    if b["kind"] == 0:
                        items.append(self.value(obj, b["type"], ea, depth) if depth < 6 else "...")
                    elif fl & 0xF == 4:
                        items.append(self.owned(obj, struct.unpack_from("<I", self.d, ea)[0], depth))
                    else:
                        items.append(self.ref(struct.unpack_from("<I", self.d, ea)[0]))
                out[f["name"]] = items
            else:
                out[f["name"]] = self.value(obj, f["type"], a, depth) if depth < 6 else "..."
        return out

    def owned(self, obj, v, depth):
        """owned pointer = (element index << 16) | block index inside the same top-level object"""
        if v == 0xFFFFFFFF:
            return None
        blk, idx = v & 0xFFFF, v >> 16
        if blk >= len(obj["blocks"]):
            return "0x%08X" % v
        b = obj["blocks"][blk]
        if depth >= 6:
            return "<%s>" % b["name"]
        val = self.struct(obj, b["type"], b["offset"] + idx * b["size"], depth + 1)
        return dict({"_type": b["name"]}, **val)

    def top(self, obj):
        b = obj["blocks"][0]
        return self.struct(obj, b["type"], b["offset"])


if __name__ == "__main__":
    import json
    f = File(sys.argv[1])
    want = sys.argv[2] if len(sys.argv) > 2 else ""
    for i, o in enumerate(f.O):
        v = f.top(o)
        name = v.get("Name") if isinstance(v, dict) else None
        if want and not (name and want.lower() in name.lower()):
            continue
        print("#%d %s %s" % (i, o["blocks"][0]["name"], json.dumps(v, indent=1, default=str)))

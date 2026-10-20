"""Print the class schema stored in a KSLF file (types, base classes, fields).
usage: python schema.py FILE [TypeName ...]"""
import struct
import sys

import kslf


def load_schema(k):
    d, S = k.data, k.strings_blob

    def nm(off):
        return S[off:S.index(b"\0", off)].decode("latin1")
    (t0, tn), (b0, bn), (f0, fn) = k.sections[0], k.sections[1], k.sections[2]
    types, fi, bi = [], 0, 0
    for i in range(tn):
        name_off, nfields, nbases, kind, unk, size = struct.unpack_from("<6H", d, t0 + 12 * i)
        fields = []
        for j in range(nfields):
            fo, ftype, foff, flags = struct.unpack_from("<4H", d, f0 + 8 * (fi + j))
            fields.append({"name": nm(fo), "type": ftype, "offset": foff, "flags": flags})
        bases = [struct.unpack_from("<2H", d, b0 + 4 * (bi + j)) for j in range(nbases)]
        fi += nfields
        bi += nbases
        types.append({"index": i, "name": nm(name_off), "kind": kind, "size": size, "unk": unk,
                      "fields": fields, "bases": bases})
    return types


if __name__ == "__main__":
    k = kslf.Kslf(open(sys.argv[1], "rb").read())
    types = load_schema(k)
    want = set(sys.argv[2:])
    for t in types:
        if want and t["name"] not in want:
            continue
        bases = ", ".join("%s@+%d" % (types[b]["name"], o) for b, o in t["bases"])
        print("%3d %-32s kind %d size %4d %s" % (t["index"], t["name"], t["kind"], t["size"], ("bases " + bases) if bases else ""))
        for f in t["fields"]:
            tn = types[f["type"]]["name"] if f["type"] < len(types) else "?%d" % f["type"]
            print("      +%-4d %-28s %-24s flags %X" % (f["offset"], f["name"], tn, f["flags"]))

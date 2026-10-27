"""ui_frontend.bpr (root.pak ui\\data, magic "2RZB" v3): the menu's picture bundle. Track menu pictures are found by the
route's Thumbnail name (amax.bin Route.Thumbnail), 512x256 DXT1 + one 256x128 mip (81920 bytes).

Layout: header {"2RZB", u8 3, u8 2, u16 size of records + names, u32 records size, u32 data size, u32 0} (0x14
bytes); records (28 bytes, chained by u16 size; the LAST record has size 0): {u16 size, u16 name offset, u32 data
offset, u16 kind 1, u16 0, u32 data size, u32 0, u16 format (1 = DXT1, 5 = ...), u16 mips, u16 w, u16 h}; names
(zero-ended); data (offsets relative to its start = 0x14 + header u16); then a tail (1392 bytes in the game's file)
that must stay at the end.

  python bpr.py IN.bpr OUT.bpr NAME PICTURE.png      (adds or replaces picture NAME)
"""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tex"))
import dxt  # noqa: E402

REC = struct.Struct("<HHIHHIIHHHH")


def parse(d):
    magic, v1, v2, rn_size, rec_size, data_size, _ = struct.unpack_from("<4sBBHIII", d, 0)
    assert magic == b"2RZB", magic
    recs, p = [], 0x14
    while True:
        r = list(REC.unpack_from(d, p))
        recs.append(r)
        if r[0] == 0:
            break
        p += r[0]
    names_at = 0x14 + rec_size
    data_at = 0x14 + rn_size
    names = d[names_at:data_at]
    data = d[data_at:data_at + data_size]
    tail = d[data_at + data_size:]
    return {"recs": recs, "names": bytearray(names), "data": bytearray(data), "tail": tail, "v": (v1, v2)}


def name_of(b, r):
    o = r[1]
    return b["names"][o:b["names"].index(b"\0", o)].decode("latin1")


def write(b):
    recs = b["recs"]
    raw = b"".join(REC.pack(REC.size if i < len(recs) - 1 else 0, *r[1:]) for i, r in enumerate(recs))
    rn = len(raw) + len(b["names"])
    assert rn < 0x10000, "records + names too big for the u16 header field"
    head = struct.pack("<4sBBHIII", b"2RZB", b["v"][0], b["v"][1], rn, len(raw), len(b["data"]), 0)
    return head + raw + bytes(b["names"]) + bytes(b["data"]) + b["tail"]


def thumbnail_dxt(png_path, w=512, h=256):
    """DXT1 level 512x256 + 256x128 (what track pictures use)"""
    from PIL import Image
    img = Image.open(png_path).convert("RGB")
    out = b""
    for lw, lh in ((w, h), (w // 2, h // 2)):
        px = img.resize((lw, lh), Image.LANCZOS).tobytes()
        out += dxt.dxt1_encode(px, lw, lh)
    return out


def set_picture(bpr_bytes, name, png_path):
    b = parse(bpr_bytes)
    pic = thumbnail_dxt(png_path)
    for r in b["recs"]:
        if name_of(b, r) == name:                    # replace: new data at the end of the data area
            r[2], r[5] = len(b["data"]), len(pic)
            b["data"] += pic
            return write(b)
    name_off = len(b["names"])
    b["names"] += name.encode("latin1") + b"\0"
    rec = [REC.size, name_off, len(b["data"]), 1, 0, len(pic), 0, 1, 2, 512, 256]
    b["data"] += pic
    b["recs"].insert(len(b["recs"]) - 1, rec)       # before the last (size 0) record
    return write(b)


def alias_picture(bpr_bytes, name, like):
    """a new picture `name` that shows picture `like` (its record copied: same data, size and format)"""
    b = parse(bpr_bytes)
    names = [name_of(b, r) for r in b["recs"]]
    if name in names:
        return bpr_bytes
    src = b["recs"][names.index(like)]
    name_off = len(b["names"])
    b["names"] += name.encode("latin1") + b"\0"
    b["recs"].insert(len(b["recs"]) - 1, [REC.size, name_off] + list(src[2:]))
    return write(b)


if __name__ == "__main__":
    src, dst, name, png = sys.argv[1:5]
    out = set_picture(open(src, "rb").read(), name, png)
    open(dst, "wb").write(out)
    b = parse(out)
    print("written", dst, len(out), "bytes,", len(b["recs"]), "pictures; ", name, "->",
          [r for r in b["recs"] if name_of(b, r) == name])

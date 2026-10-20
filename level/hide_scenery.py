"""
Hide a level's scenery graphics (Level.levelrender / Level.levelstream) without decoding the ARCH format:
every draw node carries {bbox min xyz, bbox max xyz, f32 visible-from distance, f32 visible-to distance}
(e.g. -8.5e37..8.5e37 = always, 128..513 = a middle LOD). Found by value (a sane bbox followed by a sane range)
and the range is set to 0..0, so it is never drawn.

  python hide_scenery.py SRC_LEVEL_DIR OUT_DIR
"""
import os
import struct
import sys


def is_record(v):
    x0, y0, z0, x1, y1, z1, a, b = v
    if not (-1e5 < x0 <= x1 < 1e5 and -1e5 < y0 <= y1 < 1e5 and -1e5 < z0 <= z1 < 1e5 and x1 - x0 > 0.01):
        return False
    return a < b and (abs(a) > 1e30 or 0 <= a < 1e6) and (abs(b) > 1e30 or 0 < b < 1e6)


def hide(data, loose=False):
    """loose=True: every sane record, also without the "RA" tags around it - what the Flat Earth level template
    (showroom files, 754 + 958 nodes) was made with; strict crashed nothing there but hides less"""
    import numpy as np
    d = bytearray(data)
    a = np.frombuffer(bytes(data), np.uint8)
    n = len(a) - 32
    b27, b31 = a[27:27 + n], a[31:31 + n]
    cand = np.nonzero(np.isin(b27, (0x7E, 0xFE)) | np.isin(b31, (0x7E, 0x42, 0x43, 0x44, 0x45, 0x46)))[0]
    hits = []
    last = -32
    for i in cand.tolist():
        if i < last + 32:
            continue
        # a real draw-node record sits between two "RA\0\0" archive tags (the loose match without them crashed
        # the level loader on LA River: false hits inside other data)
        tagged = d[i - 4:i] == b"RA\0\0" and d[i + 32:i + 36] == b"RA\0\0"
        if (loose or tagged) and is_record(struct.unpack_from("<8f", d, i)):
            struct.pack_into("<2f", d, i + 24, 0.0, 0.0)
            hits.append(i)
            last = i
    return bytes(d), hits


if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for name in ("Level.levelrender", "Level.levelstream"):
        data, hits = hide(open(os.path.join(src, name), "rb").read())
        open(os.path.join(out, name.lower()), "wb").write(data)
        print(name, "draw nodes hidden:", len(hits))

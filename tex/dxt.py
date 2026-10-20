"""DXT1 decode/encode + tiny PNG writer/reader (no extra packages needed)."""
import struct
import zlib


def _c565(c):
    return ((c >> 11 & 31) * 255 // 31, (c >> 5 & 63) * 255 // 63, (c & 31) * 255 // 31)


def dxt1_decode(data, w, h):
    px = bytearray(w * h * 3)
    i = 0
    for by in range(0, h, 4):
        for bx in range(0, w, 4):
            c0, c1, bits = struct.unpack_from("<HHI", data, i)
            i += 8
            a, b = _c565(c0), _c565(c1)
            if c0 > c1:
                pal = [a, b, tuple((2 * x + y) // 3 for x, y in zip(a, b)), tuple((x + 2 * y) // 3 for x, y in zip(a, b))]
            else:
                pal = [a, b, tuple((x + y) // 2 for x, y in zip(a, b)), (0, 0, 0)]
            for k in range(16):
                x, y = bx + k % 4, by + k // 4
                if x < w and y < h:
                    px[(y * w + x) * 3:(y * w + x) * 3 + 3] = bytes(pal[bits >> (2 * k) & 3])
    return bytes(px)


def _to565(c):
    r, g, b = c
    return (r * 31 // 255) << 11 | (g * 63 // 255) << 5 | (b * 31 // 255)


def dxt1_encode(px, w, h):
    """simple DXT1 encoder: endpoints = the two block pixels farthest apart (good enough for photos)"""
    out = bytearray()
    for by in range(0, h, 4):
        for bx in range(0, w, 4):
            block = []
            for k in range(16):
                x, y = min(bx + k % 4, w - 1), min(by + k // 4, h - 1)
                block.append(tuple(px[(y * w + x) * 3:(y * w + x) * 3 + 3]))
            lo = min(block, key=lambda c: c[0] * 3 + c[1] * 6 + c[2])
            hi = max(block, key=lambda c: c[0] * 3 + c[1] * 6 + c[2])
            c0, c1 = _to565(hi), _to565(lo)
            if c0 < c1:
                c0, c1 = c1, c0
            if c0 == c1:
                out += struct.pack("<HHI", c0, c1, 0)
                continue
            a, b = _c565(c0), _c565(c1)
            pal = [a, b, tuple((2 * x + y) // 3 for x, y in zip(a, b)), tuple((x + 2 * y) // 3 for x, y in zip(a, b))]
            bits = 0
            for k, c in enumerate(block):
                j = min(range(4), key=lambda j: sum((c[t] - pal[j][t]) ** 2 for t in range(3)))
                bits |= j << (2 * k)
            out += struct.pack("<HHI", c0, c1, bits)
    return bytes(out)


def write_png(path, px, w, h):
    raw = b"".join(b"\0" + px[y * w * 3:(y + 1) * w * 3] for y in range(h))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d))
    open(path, "wb").write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                           + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

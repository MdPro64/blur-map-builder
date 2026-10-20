"""Blur .aid files (gamedata/levels/<city>/<route name>.aid) = the race layout of one route.
Partly decoded: the file holds named splines, each record = {u32 name length, name, 8 zero bytes, u32 flags,
u32 point count, points}, point = 0x143 bytes: 11 floats {x, height, -z, width, a, b, c, 0, d, h1, h2}
(z is stored NEGATED; side x = a, side z = -b), then u32 11 + 11 lane samples of 25 bytes.
Flags (as in memory): 0x01 main AI line, 0x02 start/finish gate, 0x08 split 1, 0x10 split 2, 0x20 Logic route,
0x40/0xC0 other. The links between the Logic route and the gates are NOT stored: the game builds them on load.
Junction records (Junction..., naming two splines) sit between the AI lines and the gates."""
import re
import struct

POINT = 0x143


def splines(d):
    """[(name, record offset, flags, count, first point offset)] for every spline record"""
    out = []
    for m in re.finditer(rb"([\x03-\x20])\x00\x00\x00([A-Za-z][A-Za-z0-9_]{2,31})\x00{8}", d):
        if len(m.group(2)) != m.group(1)[0]:
            continue
        flags, count = struct.unpack_from("<II", d, m.end())
        out.append((m.group(2).decode(), m.start(), flags, count, m.end() + 8))
    return out


def point(d, at):
    return list(struct.unpack_from("<11f", d, at))


def move_gate(d, gate, logic, first):
    """Re-place gate spline `gate` (a splines() row) on the Logic spline so that gate point k = Logic point
    first + k (wrapping on a loop). Copies position, width and side/forward from the Logic points; keeps the
    gate's own height fields (1, 9, 10) and lane samples."""
    _, _, _, gn, gp = gate
    _, _, _, ln, lp = logic
    m = ln - 1                       # loop: the last Logic point repeats the first
    for k in range(gn):
        src = point(d, lp + ((first + k) % m) * POINT)
        dst = point(d, gp + k * POINT)
        for f in (0, 2, 3, 4, 5, 6, 7, 8):
            dst[f] = src[f]
        struct.pack_into("<11f", d, gp + k * POINT, *dst)


def gate_offset(d, gate, logic):
    """index of the Logic point nearest to the gate's first point"""
    _, _, _, gn, gp = gate
    _, _, _, ln, lp = logic
    gx, _, gz = point(d, gp)[:3]
    return min(range(ln), key=lambda i: (point(d, lp + i * POINT)[0] - gx) ** 2 + (point(d, lp + i * POINT)[2] - gz) ** 2)


# ---- link table: one list per LOGIC point, at the END of the file -------------------------------------------
# list = u32 count, then count x {u32 point index, u32 name length, spline name}; these become the in-memory
# per-point link lists (spline +0x54) that wake the gates up. Found by trying start offsets until exactly
# `count` lists end at EOF.

def _parse_links(d, s, n):
    lists, a = [], s
    for _ in range(n):
        if a + 4 > len(d):
            return None
        c = struct.unpack_from("<I", d, a)[0]
        a += 4
        if c > 64:
            return None
        cur = []
        for _ in range(c):
            if a + 8 > len(d):
                return None
            idx, ln = struct.unpack_from("<II", d, a)
            if not 1 <= ln <= 32:
                return None
            name = d[a + 8:a + 8 + ln]
            if not all(48 <= x < 123 for x in name):
                return None
            cur.append((idx, name.decode()))
            a += 8 + ln
        lists.append(cur)
    return lists, a


def link_table(d, logic):
    """(start offset, lists) of the Logic link table"""
    n = logic[3]
    lp_end = logic[4] + n * POINT
    for s in range(lp_end, len(d)):
        r = _parse_links(d, s, n)
        if r and r[1] == len(d):
            return s, r[0]
    raise ValueError("link table not found")


def write_links(lists):
    out = bytearray()
    for cur in lists:
        out += struct.pack("<I", len(cur))
        for idx, name in cur:
            out += struct.pack("<II", idx, len(name)) + name.encode()
    return bytes(out)


def move_links(lists, gate, old_first, new_first, m):
    """move every (k, gate) link from Logic point old_first + delta to new_first + delta (same pattern)"""
    moved = {}
    for p, cur in enumerate(lists):
        mine = [e for e in cur if e[1] == gate]
        if mine:
            delta = (p - old_first + m // 2) % m - m // 2
            moved[delta] = mine
            cur[:] = [e for e in cur if e[1] != gate]
    for delta, entries in moved.items():
        lists[(new_first + delta) % m].extend(entries)
    return len(moved)

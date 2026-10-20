"""
Generate a SPRINT race layout (.aid) on a straight line, from scratch (template for the lane samples:
jpn_mharuna_1.aid, the game's own sprint route).

File = f32 6.0, u32 spline count; spline records {u32 name length, name, 8 zero bytes, u32 flags, u32 count,
count x point}; u32 Logic point count; link table (one list per Logic point: u32 n, n x {u32 point, u32 name
length, name}).  Point (0x143 bytes) = 11 floats {x, height, -z, width, a, b, c, tilt, d, h1, h2} + u32 11 +
11 lane samples x 25 bytes.  The point sits on the EDGE of the road; the road runs `width` m along the side
direction (side = (-forward z, forward x); a = side x, b = -side z; c, d = the same scaled by the banking,
tilt = its vertical part).  Links: Logic point p -> AI line points p-1..p+1, and gate point k whenever
|p - (gate base + k)| <= 1.
"""
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "aid"))
import aid  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import paths  # noqa: E402
TEMPLATE = os.path.join(paths.UNPACKED, "levels", "jpn_mharuna", "jpn_mharuna_1.aid")
STEP = 4.0
GATE_POINTS = 24
GATE_LEAD = 2          # a gate's line is its point 1..2 (LA River builds put the first point 2 before the line)


def lanes_from_template():
    """the u32 11 + 11 lane samples of one template point, per spline kind"""
    d = open(TEMPLATE, "rb").read()
    rows = {}
    for name, at, flags, count, first in aid.splines(d):
        if name == "Start":                       # Haruna's Start has an extra u32 (flags 0x80): skip it
            first += 4
        rows[name] = d[first + 44:first + aid.POINT]
    return rows


# the 3 height fields (height, h1, h2) are NOT the road height: measured on LA River route 6 (median, minus the
# collision ground): AI line = road +7.8 / -2.9 / +7.8 (a "tube" weapons and AI travel in - all equal to the road
# made fired weapons hit something at once), Logic = road +101 everywhere, gates = road +1
# TEST RESULT: Logic +101 / gates +1 broke the finish line (it worked with everything at road height) and did not cure
# the weapons -> Logic and gates back at road height; the AI line keeps its measured tube
HEIGHTS = {"Main": (7.8, -2.9, 7.8), "Logic": (0.0, 0.0, 0.0), "gate": (0.0, 0.0, 0.0)}


def make_point(x, y, z, width, fx, fz, lane_bytes, kind="gate"):
    sx, sz = -fz, fx                               # side direction (game z)
    h, h1, h2 = HEIGHTS.get(kind, HEIGHTS["gate"])
    return struct.pack("<11f", x, y + h, -z, width, sx, -sz, sx, 0.0, -sz, y + h1, y + h2) + lane_bytes


def lane_steps(points):
    """fill in each point's per-lane distance from the previous point (lane sample float 1; lane position = point +
    side (floats 4, 5) * width * lane fraction (float 4)) - as every real .aid has it (Brighton ~4 m a point).
    The copied template block said 0 m everywhere: on a LAP track the AI's look-ahead (0xB1A8B0: walk round the
    closed line until the lane lengths add up to the braking distance) then went round forever = the load freeze."""
    out, prev = [], None
    for raw in points:
        b = bytearray(raw)
        x, _, z, w, sx, sz = struct.unpack_from("<6f", b, 0)
        n = struct.unpack_from("<I", b, 44)[0]
        cur = []
        for k in range(n):
            at = 48 + 25 * k
            f = struct.unpack_from("<f", b, at + 16)[0]
            lx, lz = x + sx * w * f, z + sz * w * f
            step = 0.0 if prev is None else math.hypot(lx - prev[k][0], lz - prev[k][1])
            struct.pack_into("<f", b, at + 4, step)
            cur.append((lx, lz))
        prev = cur
        out.append(bytes(b))
    return out


def record(name, flags, points, frm="", to=""):
    """one spline record; a JUNCTION names the route it leaves (frm) and the alternative route it leads to (to)"""
    points = lane_steps(points)
    head = b"".join(struct.pack("<I", len(t.encode())) + t.encode() for t in (name, frm, to))
    return head + struct.pack("<II", flags, len(points)) + b"".join(points)


# ---- alternative routes (road branches / split lanes: network.alt_routes) -----------------------------------------
# as the game's own tracks: an "Alt" spline (flags 0) that starts on the main AI line and ends on it, and a plain
# junction (flags 0x40, FROM "Main" TO the alt) over the alt's first points; every point is linked to the Logic
# points beside it in the link table.

JUNCTION_FLAGS = 0x40
JUNCTION_POINTS = 16          # = network.JUNCTION_POINTS: the first 12 on the main line + 4 leaving it


def alt_records(alts, lanes):
    """(alt spline records, junction records)"""
    ra, rj = [], []
    for a in alts or ():
        P = [make_point(x, yy, z, w, fx, fz, lanes["Main"], "Main") for x, yy, z, w, fx, fz in a["points"]]
        ra.append(record(a["name"], 0, P))
        rj.append(record(a["junction"], JUNCTION_FLAGS, P[:min(JUNCTION_POINTS, len(P))], "Main", a["name"]))
    return ra, rj


def alt_links(lists, alts, n, closed):
    """add every alt / junction point to the link lists of the Logic points beside it (n = Logic points without the
    loop's repeated last one)"""
    for a in alts or ():
        for k, p in enumerate(a["logic"]):
            for q in (p - 1, p, p + 1):
                if closed:
                    q %= n
                elif not 0 <= q < len(lists):
                    continue
                targets = [q] + ([n] if closed and q == 0 and len(lists) > n else [])
                for t in targets:
                    lists[t].append((k, a["name"]))
                    if k < JUNCTION_POINTS:
                        lists[t].append((k, a["junction"]))


def band_points(road, ranges=None):
    """[(x, z, width, fx, fz)] per road point of a band ranges[i] = (lo, hi) sideways from the road's middle (default:
    the road itself): (x, z) = its lo edge, width = across to its hi edge, (fx, fz) = the direction whose side
    vector points from the lo edge to the hi edge. In a turn tighter than the band's half width the inner edge would
    fold back (a loop): both edges are made loop-free first (road._unloop, as the road's own edges) - a folded
    Logic band made the game reset cars in a hairpin."""
    import road as road_mod
    n = len(road)
    L, R, la, ra = [], [], [], []
    for i in range(n):
        lo, hi = ranges[i] if ranges else (-road.width_at[i] / 2, road.width_at[i] / 2)
        cx, cz = road.centre[i]
        sx, sz = road.side(i)
        L.append((cx + sx * lo, cz + sz * lo))
        R.append((cx + sx * hi, cz + sz * hi))
        la.append(lo)
        ra.append(hi)
    L = road_mod._unloop(L, la, road.centre)
    R = road_mod._unloop(R, ra, road.centre)
    out = []
    for i in range(n):
        dx, dz = R[i][0] - L[i][0], R[i][1] - L[i][1]
        w = math.hypot(dx, dz)
        if w < 1.0:                                      # (degenerate: keep the road's own direction)
            sx, sz = road.side(i)
            w = max(w, 1.0)
        else:
            sx, sz = dx / w, dz / w
        out.append((L[i][0], L[i][1], w, sz, -sx))      # make_point: side = (-fz, fx) = (sx, sz)
    return out


def logic_point(road, i, y, band, lanes, pts=None):
    """a LOGIC point: the band's edge + its width (band[i] = (lo, hi) sideways from the road's middle; default =
    the road). Cars outside the band are reset - network.logic_band widens it over branches. pts = band_points"""
    x, z, w, fx, fz = (pts or band_points(road, band))[i]
    return make_point(x, y, z, w, fx, fz, lanes["Logic"], "Logic")


def clear_gate(base, avoid, n, closed, lo=0, hi=None):
    """move a split gate (its GATE_POINTS points from `base`) out of the branch / split stretches `avoid` (main road
    index ranges): cars on a branch would not pass it and the lap would not count"""
    hi = n - GATE_POINTS - 1 if hi is None else hi
    for _ in range(12):
        hit = None
        for a, b in avoid or ():
            for k in range(base, base + GATE_POINTS):
                if (closed and (k - a) % n <= (b - a)) or (not closed and a <= k <= b):
                    hit = b
                    break
            if hit is not None:
                break
        if hit is None:
            return base
        base = hit + 2
        if closed:
            base %= n
        elif base > hi:
            return min(base, hi)
    return base


def build(x0, z0, length, y, width=40.0, fx=1.0, fz=0.0, splits=(1 / 3, 2 / 3), start_at=40, finish_from_end=40):
    """straight road from (x0, z0) along (fx, fz). Returns (aid bytes, info dict)."""
    lanes = lanes_from_template()
    n = int(length // STEP) + 1
    logic_xyz = [(x0 + fx * STEP * i, y, z0 + fz * STEP * i) for i in range(n)]

    def pts(kind, first, count):
        return [make_point(*logic_xyz[first + k], width, fx, fz, lanes[kind], kind) for k in range(count)]

    gates = {"Start": (2, start_at), "InterOne": (8, int(n * splits[0])), "InterTwo": (16, int(n * splits[1])),
             "Finish": (4, n - finish_from_end - GATE_POINTS)}
    recs = [record("Main", 0, pts("Main", 0, n))]
    for name in ("Start", "InterOne", "InterTwo"):
        flags, base = gates[name]
        recs.append(record(name, flags, pts(name if name != "Start" else "Start", base, GATE_POINTS)))
    recs.append(record("Logic", 32, pts("Logic", 0, n)))
    flags, base = gates["Finish"]
    recs.append(record("Finish", flags, pts("Finish", base, GATE_POINTS)))

    lists = []
    for p in range(n):
        cur = [(k, "Main") for k in (p - 1, p, p + 1) if 0 <= k < n]
        for name, (flags, base) in gates.items():
            cur += [(k, name) for k in range(GATE_POINTS) if abs(p - (base + k)) <= 1]
        lists.append(cur)
    data = struct.pack("<fI", 6.0, len(recs)) + b"".join(recs) + struct.pack("<I", n) + aid.write_links(lists)
    info = {"points": n, "gates": {k: v[1] for k, v in gates.items()},
            "start_line": logic_xyz[gates["Start"][1] + GATE_LEAD], "finish_line": logic_xyz[gates["Finish"][1] + GATE_LEAD],
            "road_middle_offset": (-fz * width / 2, fx * width / 2)}
    return data, info


START_DEFAULT = 40 + GATE_LEAD          # road point of the start line when the map doesn't say (168 m)
FINISH_DEFAULT_FROM_END = 40 + GATE_POINTS - GATE_LEAD    # ... and of the finish line, counted from the end
START_MIN_M = 110.0                     # the 20-car grid stands up to ~100 m behind the start line
FINISH_END_MIN_M = (GATE_POINTS + 2) * STEP   # the finish gate runs 22 points past its line
MIN_RACE_M = 300.0


def race_lines(road, m=None):
    """(start index, finish index) of the race on this road: the map's own "start_m" / "finish_m" (metres along
    the road, set in the designer) or the defaults; kept where the grid and the gates fit"""
    n = len(road)
    if getattr(road, "closed", False):                # a lap track: start = finish, anywhere on the loop
        si = int(round(m["start_m"] / STEP)) % n if m and m.get("start_m") is not None else START_DEFAULT % n
        return si, si
    lo = int(math.ceil(START_MIN_M / STEP))
    hi = n - 1 - int(math.ceil(FINISH_END_MIN_M / STEP))
    gap = int(MIN_RACE_M / STEP)
    m = m or {}
    si = int(round(m["start_m"] / STEP)) if m.get("start_m") is not None else START_DEFAULT
    fi = int(round(m["finish_m"] / STEP)) if m.get("finish_m") is not None else n - FINISH_DEFAULT_FROM_END
    si = max(min(si, hi - gap), min(lo, START_DEFAULT))
    fi = min(max(fi, si + gap), hi)
    if fi <= si:                         # a very short road: the old fixed spots
        si, fi = START_DEFAULT, n - FINISH_DEFAULT_FROM_END
    return si, fi


def build_path(road, y, splits=(1 / 3, 2 / 3), start_at=40, finish_from_end=40, corridor=None, lines=None,
               alts=None, avoid=None, band=None):
    """the same sprint layout along a CURVED road (road.Road: centre points every STEP m + headings).
    Every point sits on the road's edge (centre - side * width / 2) and faces along the local direction.
    lines = (start index, finish index) from race_lines (the splits then sit at 1/3 and 2/3 between them)."""
    assert abs(road.dist[1] - STEP) < 1e-6
    if getattr(road, "closed", False):
        return build_loop(road, y, splits, corridor, lines, alts, avoid, band)
    n_all = len(road)
    if lines:
        si, fi = lines
        start_at, finish_from_end = si - GATE_LEAD, n_all - GATE_POINTS - (fi - GATE_LEAD)
    lanes = lanes_from_template()
    n = len(road)
    H = getattr(road, "height", [0.0] * n)              # height above the ground per point (bridges)
    road_band = band_points(road)                       # the road's own edges (loop-free in tight turns)
    ai_band = band_points(road, corridor)
    logic_band = band_points(road, band)

    def pts(kind, first, count):
        out = []
        for k in range(count):
            x, z, w, fx, fz = road_band[first + k]
            out.append(make_point(x, y + H[first + k], z, w, fx, fz, lanes[kind], kind))
        return out

    def ai_points():
        """the AI line ("Main"): its corridor may be narrower than the road (corridor[i] = (lo, hi) sideways from
        the centre): the AI steers into the free lane around road blocks"""
        out = []
        for i in range(n):
            x, z, w, fx, fz = ai_band[i]
            out.append(make_point(x, y + H[i], z, w, fx, fz, lanes["Main"], "Main"))
        return out

    if lines:
        split_at = [int(lines[0] + (lines[1] - lines[0]) * f) - GATE_LEAD for f in splits]
    else:
        split_at = [int(n * f) for f in splits]
    fin = n - finish_from_end - GATE_POINTS
    split_at = [clear_gate(b, avoid, n, False, start_at + GATE_POINTS + 1, fin - GATE_POINTS - 1) for b in split_at]
    gates = {"Start": (2, start_at), "InterOne": (8, split_at[0]), "InterTwo": (16, split_at[1]),
             "Finish": (4, fin)}
    ra, rj = alt_records(alts, lanes)
    recs = ra + [record("Main", 0, ai_points())]
    for name in ("Start", "InterOne", "InterTwo"):
        flags, base = gates[name]
        recs.append(record(name, flags, pts(name, base, GATE_POINTS)))
    recs.append(record("Logic", 32, [logic_point(road, i, y + H[i], band, lanes, logic_band) for i in range(n)]))
    recs += rj
    flags, base = gates["Finish"]
    recs.append(record("Finish", flags, pts("Finish", base, GATE_POINTS)))
    lists = []
    for p in range(n):
        cur = [(k, "Main") for k in (p - 1, p, p + 1) if 0 <= k < n]
        for name, (flags, base) in gates.items():
            cur += [(k, name) for k in range(GATE_POINTS) if abs(p - (base + k)) <= 1]
        lists.append(cur)
    alt_links(lists, alts, n, False)
    data = struct.pack("<fI", 6.0, len(recs)) + b"".join(recs) + struct.pack("<I", n) + aid.write_links(lists)
    si, fi = gates["Start"][1] + GATE_LEAD, gates["Finish"][1] + GATE_LEAD
    info = {"points": n, "gates": {k: v[1] for k, v in gates.items()},
            "start_index": si, "finish_index": fi,
            "start_line": (road.centre[si][0], y + H[si], road.centre[si][1]), "start_angle": road.heading[si],
            "finish_line": (road.centre[fi][0], y + H[fi], road.centre[fi][1]), "finish_angle": road.heading[fi]}
    return data, info


def build_loop(road, y, splits=(1 / 3, 2 / 3), corridor=None, lines=None, alts=None, avoid=None, band=None):
    """a LAP track (closed road), laid out like LA River's circuit (usa_la_river_6.aid): the AI line and the Logic
    line are closed loops (their first point repeated at the end, flags as LA's MainLoop 0x01 / LOGIC 0x21), ONE gate
    Start_Finish (flags 2) on the start line, the splits a third and two thirds of the way round, no Finish gate;
    every index wraps round the seam."""
    lanes = lanes_from_template()
    n = len(road)
    H = getattr(road, "height", [0.0] * n)
    si = (lines[0] if lines else START_DEFAULT) % n
    road_band = band_points(road)                       # loop-free edges in tight turns (see band_points)
    ai_band = band_points(road, corridor)
    logic_band = band_points(road, band)

    def pt(kind, i):
        i %= n
        x, z, w, fx, fz = road_band[i]
        return make_point(x, y + H[i], z, w, fx, fz, lanes[kind], kind)

    def ai_pt(i):
        i %= n
        x, z, w, fx, fz = ai_band[i]
        return make_point(x, y + H[i], z, w, fx, fz, lanes["Main"], "Main")

    gates = {"Start": (2, si - GATE_LEAD),
             "InterOne": (8, clear_gate((si + int(n * splits[0]) - GATE_LEAD) % n, avoid, n, True)),
             "InterTwo": (16, clear_gate((si + int(n * splits[1]) - GATE_LEAD) % n, avoid, n, True))}
    ra, rj = alt_records(alts, lanes)
    recs = ra + [record("Main", 1, [ai_pt(i) for i in range(n + 1)])]      # closed: point 0 again at the end
    for name in ("Start", "InterOne", "InterTwo"):
        flags, base = gates[name]
        recs.append(record(name, flags, [pt(name, base + k) for k in range(GATE_POINTS)]))
    recs.append(record("Logic", 0x21, [logic_point(road, i % n, y + H[i % n], band, lanes, logic_band)
                                       for i in range(n + 1)]))
    recs += rj
    lists = []
    for p in range(n + 1):
        cur = [(k % n, "Main") for k in (p - 1, p, p + 1)]
        for name, (flags, base) in gates.items():
            for k in range(GATE_POINTS):
                d = (p - (base + k)) % n
                if min(d, n - d) <= 1:
                    cur.append((k, name))
        lists.append(cur)
    alt_links(lists, alts, n, True)
    data = struct.pack("<fI", 6.0, len(recs)) + b"".join(recs) + struct.pack("<I", n + 1) + aid.write_links(lists)
    line = (road.centre[si][0], y + H[si], road.centre[si][1])
    info = {"points": n + 1, "gates": {k: v[1] % n for k, v in gates.items()}, "start_index": si,
            "finish_index": si, "start_line": line, "start_angle": road.heading[si], "finish_line": line,
            "finish_angle": road.heading[si], "closed": True}
    return data, info


if __name__ == "__main__":
    data, info = build(x0=-1000.0, z0=-20.0, length=2000.0, y=90.6)
    out = os.path.join(paths.FE_WORK, "flat_earth_1.aid")
    open(out, "wb").write(data)
    print("written", os.path.normpath(out), len(data), "bytes", info)
    S = aid.splines(data)
    print([(s[0], s[2], s[3]) for s in S])
    off, lists = aid.link_table(data, [s for s in S if s[0] == "Logic"][0])
    print("link table at", off, "lists", len(lists), "first gate links:",
          [(p, l) for p, l in enumerate(lists) if any(e[1] == "Start" for e in l)][:3])

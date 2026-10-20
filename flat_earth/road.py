"""
A Flat Earth road shape: a smooth curve through control points, sampled every STEP metres.

Control point = {"x", "z", optional "w" (road width here), "h" (height above the ground), "corner" (radius in m:
a sharp turn - straight lines meeting in an arc - instead of a smooth curve)}. Old files with [x, z] pairs work too.
Width and height change smoothly from point to point. AUTO BRIDGES: where the road crosses an earlier part of
itself, the later part is lifted onto a deck (BRIDGE_HEIGHT m) with ramps of slope BRIDGE_GRADE.

Coordinates are the game's (x, z); heading angle a: forward = (sin a, cos a); side = (-forward z, forward x).
"""
import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
import sys  # noqa: E402
sys.path.insert(0, os.path.dirname(HERE))
import paths  # noqa: E402
ROAD_FILE = os.path.join(paths.TRACKS, "flat_earth_road.json")
STEP = 4.0
BRIDGE_HEIGHT = 9.0           # deck height above the ground (a car is ~1.5 m high: lots of room under it)
BRIDGE_GRADE = 0.07           # ramp slope (7 %)
MIN_CORNER = 25.0


def default_road(ox=790.0, oz=2160.0, length=3000.0, width=60.0):
    """the original straight Home Run road"""
    return {"points": [[ox - length / 2, oz], [ox + length / 2, oz]], "width": width}


def load(path=ROAD_FILE):
    if os.path.exists(path):
        return json.load(open(path))
    return default_road()


def save(road, path=ROAD_FILE):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    json.dump(road, open(path, "w"), indent=1)


def for_map(m):
    """the Road of a world map (lap track = closed loop: m["laps"] is a number of laps)"""
    return Road(m["points"], m.get("width", 60.0), m.get("auto_bridges", True), closed=bool(m.get("laps")))


def norm_point(p, width):
    """any point form -> dict with x, z, w, h, corner"""
    if isinstance(p, dict):
        return {"x": float(p["x"]), "z": float(p["z"]), "w": float(p.get("w") or width),
                "h": float(p.get("h") or 0.0), "corner": float(p.get("corner") or 0.0)}
    return {"x": float(p[0]), "z": float(p[1]), "w": float(width), "h": 0.0, "corner": 0.0}


def _catmull(p0, p1, p2, p3, n):
    """centripetal Catmull-Rom segment p1 -> p2 as n points (p1 included, p2 not)"""
    def tj(ti, a, b):
        return ti + max(math.hypot(b[0] - a[0], b[1] - a[1]), 1e-6) ** 0.5
    t0 = 0.0
    t1 = tj(t0, p0, p1)
    t2 = tj(t1, p1, p2)
    t3 = tj(t2, p2, p3)
    out = []
    for k in range(n):
        t = t1 + (t2 - t1) * k / n
        pts = []
        for (a, ta), (b, tb) in (((p0, t0), (p1, t1)), ((p1, t1), (p2, t2)), ((p2, t2), (p3, t3))):
            w = (t - ta) / (tb - ta)
            pts.append((a[0] + (b[0] - a[0]) * w, a[1] + (b[1] - a[1]) * w))
        a1, a2, a3 = pts
        b1 = [a1[i] + (a2[i] - a1[i]) * (t - t0) / (t2 - t0) for i in (0, 1)]
        b2 = [a2[i] + (a3[i] - a2[i]) * (t - t1) / (t3 - t1) for i in (0, 1)]
        out.append(tuple(b1[i] + (b2[i] - b1[i]) * (t - t1) / (t2 - t1) for i in (0, 1)))
    return out


def _with_corners(pts):
    """replace every corner point by its arc (straight lines in and out, an arc of the point's radius): returns
    [(x, z, w, h, straight)] where straight=True marks arc points (they must not be smoothed away)"""
    out = []
    for k, p in enumerate(pts):
        r = p["corner"]
        if 0 < k < len(pts) - 1 and r > 0:
            a, b = pts[k - 1], pts[k + 1]
            ux, uz = p["x"] - a["x"], p["z"] - a["z"]
            vx, vz = b["x"] - p["x"], b["z"] - p["z"]
            lu, lv = math.hypot(ux, uz), math.hypot(vx, vz)
            if lu > 1e-6 and lv > 1e-6:
                ux, uz, vx, vz = ux / lu, uz / lu, vx / lv, vz / lv
                turn = math.atan2(ux * vz - uz * vx, ux * vx + uz * vz)
                if abs(turn) > 1e-3:
                    r = max(r, MIN_CORNER, p["w"] / 2 + 10)    # tighter: the inside edge would fold back
                    d = min(r * math.tan(abs(turn) / 2), 0.45 * lu, 0.45 * lv)   # tangent length
                    r = d / math.tan(abs(turn) / 2)
                    p["corner_used"] = r                       # the radius that fits (shown in the designer)
                    t1 =(p["x"] - ux * d, p["z"] - uz * d)
                    # arc centre: from t1 along the inside normal
                    sgn = 1 if turn > 0 else -1
                    nx, nz = -uz * sgn, ux * sgn
                    cx, cz = t1[0] + nx * r, t1[1] + nz * r
                    a0 = math.atan2(t1[0] - cx, t1[1] - cz)
                    steps = max(2, int(abs(turn) * r / 2))
                    for i in range(steps + 1):
                        # point = centre + r (sin ang, cos ang): a LEFT turn (turn > 0) runs anticlockwise = ang
                        # falling, a right turn the other way (was "- sgn * turn": right corners folded back)
                        ang = a0 - turn * i / steps
                        out.append((cx + r * math.sin(ang), cz + r * math.cos(ang), p["w"], p["h"], True))
                    continue
        out.append((p["x"], p["z"], p["w"], p["h"], False))
    return out


def _unloop(pts, lat, centre, cell=30.0):
    """an offset line folds back (a loop or a notch) where a turn is tighter than the offset. Every offset point that
    lies closer to the road's middle line than its own offset (minus 1 m) is inside the road: it is dropped and the
    good points on both sides are joined by a straight line (the inside of a hairpin becomes one sharp corner)."""
    grid = {}
    for k, (x, z) in enumerate(centre):
        grid.setdefault((int(x // cell), int(z // cell)), []).append(k)
    n = len(pts)
    good = []
    for i, (x, z) in enumerate(pts):
        need = abs(lat[i]) - 1.0
        r = int(need // cell) + 1
        gx, gz = int(x // cell), int(z // cell)
        ok = True
        for ax in range(gx - r, gx + r + 1):
            for az in range(gz - r, gz + r + 1):
                for k in grid.get((ax, az), ()):
                    if (centre[k][0] - x) ** 2 + (centre[k][1] - z) ** 2 < need * need:
                        ok = False
                        break
                if not ok:
                    break
            if not ok:
                break
        good.append(ok)
    if all(good) or not any(good):
        return list(pts)
    out = list(pts)
    i = 0
    while i < n:
        if good[i]:
            i += 1
            continue
        j = i
        while j < n and not good[j]:
            j += 1
        a, b = i - 1, j                                  # good neighbours (may be off the ends)
        for m in range(i, j):
            if a < 0:
                out[m] = pts[b]
            elif b >= n:
                out[m] = pts[a]
            else:
                t = (m - a) / (b - a)
                out[m] = (pts[a][0] + (pts[b][0] - pts[a][0]) * t, pts[a][1] + (pts[b][1] - pts[a][1]) * t)
        i = j
    return out


def _smooth(v0, v1, t):
    t = t * t * (3 - 2 * t)
    return v0 + (v1 - v0) * t


class Road:
    """centre line sampled every STEP m: centre[i] = (x, z), heading[i], dist[i], width_at[i], height[i]
    (height above the ground, incl. auto bridges)"""

    def __init__(self, points, width=60.0, auto_bridges=True, closed=False, **_):
        """closed = a LAP track: the road goes on from its last point back to the first (smooth loop)"""
        pts = [norm_point(p, width) for p in points]
        assert len(pts) >= (3 if closed else 2), "a road needs at least %d points" % (3 if closed else 2)
        self.width = float(width)
        self.points = pts
        self.closed = bool(closed)
        if self.closed:                                   # the loop: back to the first point (no corner there)
            first = dict(pts[0], corner=0.0)
            pts = [first] + pts[1:] + [dict(first)]
        ctl = _with_corners(pts)
        # dense curve (~1 m): straight between arc points, Catmull-Rom elsewhere; width / height eased between points
        P = [(c[0], c[1]) for c in ctl]
        if self.closed:                                   # neighbours wrap round the loop
            ext = [P[-2]] + P + [P[1]]
        else:
            ext = [(2 * P[0][0] - P[1][0], 2 * P[0][1] - P[1][1])] + P + [(2 * P[-1][0] - P[-2][0], 2 * P[-1][1] - P[-2][1])]
        dense = []                   # (x, z, w, h)
        for i in range(len(ctl) - 1):
            seg = math.hypot(P[i + 1][0] - P[i][0], P[i + 1][1] - P[i][1])
            n = max(int(seg), 2)
            if ctl[i][4] and ctl[i + 1][4]:                  # inside an arc / straight between two corners
                xy = [(P[i][0] + (P[i + 1][0] - P[i][0]) * k / n, P[i][1] + (P[i + 1][1] - P[i][1]) * k / n) for k in range(n)]
            else:
                # next to an arc: a phantom neighbour along the arc's own direction, so the smooth curve leaves /
                # enters the corner in line with it (no bulge or S where a corner meets a smooth point)
                p0, p3 = ext[i], ext[i + 3]
                if ctl[i][4] and i > 0:
                    dx, dz = P[i][0] - P[i - 1][0], P[i][1] - P[i - 1][1]
                    k = seg / max(math.hypot(dx, dz), 1e-9)
                    p0 = (P[i][0] - dx * k, P[i][1] - dz * k)
                if ctl[i + 1][4] and i + 2 < len(P):
                    dx, dz = P[i + 2][0] - P[i + 1][0], P[i + 2][1] - P[i + 1][1]
                    k = seg / max(math.hypot(dx, dz), 1e-9)
                    p3 = (P[i + 1][0] + dx * k, P[i + 1][1] + dz * k)
                xy = _catmull(p0, ext[i + 1], ext[i + 2], p3, n)
            for k, (x, z) in enumerate(xy):
                t = k / n
                dense.append((x, z, _smooth(ctl[i][2], ctl[i + 1][2], t), _smooth(ctl[i][3], ctl[i + 1][3], t)))
        dense.append((P[-1][0], P[-1][1], ctl[-1][2], ctl[-1][3]))
        acc = [0.0]
        for a, b in zip(dense, dense[1:]):
            acc.append(acc[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
        self.length = acc[-1]
        n = int(self.length // STEP) + 1
        self.centre, self.width_at, self.manual_h, j = [], [], [], 0
        for i in range(n):
            s = i * STEP
            while j < len(acc) - 2 and acc[j + 1] < s:
                j += 1
            w = (s - acc[j]) / max(acc[j + 1] - acc[j], 1e-9)
            a, b = dense[j], dense[j + 1]
            self.centre.append((a[0] + (b[0] - a[0]) * w, a[1] + (b[1] - a[1]) * w))
            self.width_at.append(a[2] + (b[2] - a[2]) * w)
            self.manual_h.append(a[3] + (b[3] - a[3]) * w)
        if self.closed and n > 2 and math.hypot(self.centre[-1][0] - self.centre[0][0],
                                                self.centre[-1][1] - self.centre[0][1]) < STEP * 0.5:
            for lst in (self.centre, self.width_at, self.manual_h):     # the last sample sits on the first one
                lst.pop()
            n -= 1
        self.heading = []
        for i in range(n):
            if self.closed:
                a, b = self.centre[(i - 1) % n], self.centre[(i + 1) % n]
            else:
                a = self.centre[max(i - 1, 0)]
                b = self.centre[min(i + 1, n - 1)]
            self.heading.append(math.atan2(b[0] - a[0], b[1] - a[1]))
        self.dist = [i * STEP for i in range(n)]
        self._edges = {}
        self.crossings = self.find_crossings()
        self.height = list(self.manual_h)
        if auto_bridges:
            for over, under, flat in self.crossings:
                self._lift(over, flat)

    def __len__(self):
        return len(self.centre)

    # ---- bridges ----
    def find_crossings(self):
        """[(index on the LATER pass = the bridge, index on the earlier pass, flat half-length in m)]: two parts of
        the road closer than their half widths + 8 m, at least a turn apart (grid lookup: fast enough to redraw
        while dragging)"""
        n = len(self)
        out = []
        cell = 60.0
        grid = {}
        idx = list(range(0, n, 2))
        for i in idx:
            x, z = self.centre[i]
            grid.setdefault((int(x // cell), int(z // cell)), []).append(i)
        for j in idx:
            x, z = self.centre[j]
            cx, cz = int(x // cell), int(z // cell)
            best = None
            for gx in (cx - 1, cx, cx + 1):
                for gz in (cz - 1, cz, cz + 1):
                    for i in grid.get((gx, gz), ()):
                        near = max(60, int((self.width_at[i] + self.width_at[j]) * 2 / STEP))
                        if j - i < near or (self.closed and n - (j - i) < near):   # (a loop meets itself at the seam)
                            continue
                        lim = (self.width_at[i] + self.width_at[j]) / 2 + 8
                        dx, dz = x - self.centre[i][0], z - self.centre[i][1]
                        dd = dx * dx + dz * dz
                        if dd < lim * lim and (best is None or dd < best[0]):
                            best = (dd, i)
            if best is None:
                continue
            i = best[1]
            if out:
                lj, li_, _ = out[-1]
                near = math.hypot(x - self.centre[lj][0], z - self.centre[lj][1])
                if near < 2.5 * max(self.width_at[i], self.width_at[j]) and abs(li_ - i) < 60:
                    continue                              # the same crossing
            cross = abs(math.sin(self.heading[i] - self.heading[j]))
            flat = (self.width_at[i] / 2 + 14) / max(cross, 0.35)
            out.append((j, i, flat))
        return out

    def _lift(self, j, flat):
        """deck of BRIDGE_HEIGHT around point j (flat for +-flat m) with ramps"""
        ramp = 1.5 * BRIDGE_HEIGHT / BRIDGE_GRADE         # the eased ramp is 1.5x steeper in its middle
        sj = self.dist[j]
        for k in range(len(self)):
            d = abs(self.dist[k] - sj)
            if d <= flat:
                h = BRIDGE_HEIGHT
            elif d <= flat + ramp:
                h = _smooth(BRIDGE_HEIGHT, 0.0, (d - flat) / ramp)
            else:
                continue
            self.height[k] = max(self.height[k], h)

    # ---- queries ----
    def index_at(self, s):
        if self.closed:
            return int(round(s / STEP)) % len(self)
        return max(0, min(len(self) - 1, int(round(s / STEP))))

    def _seg(self, s):
        """(i, j, w): s lies between road points i and j, w of the way (a loop wraps round: past the end = again
        from the start; the last point joins the first)"""
        n = len(self)
        if self.closed:
            s = s % self.length
            i = min(int(s // STEP), n - 1)
            if i == n - 1:
                return i, 0, (s - i * STEP) / max(self.length - i * STEP, 1e-9)
            return i, i + 1, (s - i * STEP) / STEP
        s = max(0.0, min(self.length, s))
        i = min(int(s // STEP), n - 2)
        return i, i + 1, (s - i * STEP) / STEP

    def at(self, s, lateral=0.0):
        """(x, z, heading) at distance s along the road, `lateral` m to the side (+ = side direction)"""
        i, j, w = self._seg(s)
        (x0, z0), (x1, z1) = self.centre[i], self.centre[j]
        x, z = x0 + (x1 - x0) * w, z0 + (z1 - z0) * w
        a0, a1 = self.heading[i], self.heading[j]
        da = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi
        a = a0 + da * w
        sx, sz = -math.cos(a), math.sin(a)          # side = (-forward z, forward x)
        return x + sx * lateral, z + sz * lateral, a

    def h_at(self, s):
        """height above the ground at distance s"""
        i, j, w = self._seg(s)
        return self.height[i] + (self.height[j] - self.height[i]) * w

    def w_at(self, s):
        i, j, w = self._seg(s)
        return self.width_at[i] + (self.width_at[j] - self.width_at[i]) * w

    def pitch_at(self, s, span=8.0):
        return math.atan2(self.h_at(s + span / 2) - self.h_at(s - span / 2), span)

    def forward(self, i):
        a = self.heading[i]
        return math.sin(a), math.cos(a)

    def edge(self, side, extra=0.0):
        """the road's edge line, one (x, z) per road point: side +1 = the side direction, -1 = the other; `extra` m
        outside the road. Inside a turn tighter than half the road the edge is one sharp corner (no loop)."""
        key = (side, extra)
        if key not in self._edges:
            raw, lats = [], []
            for i in range(len(self)):
                cx, cz = self.centre[i]
                sx, sz = self.side(i)
                lat = side * (self.width_at[i] / 2 + extra)
                raw.append((cx + sx * lat, cz + sz * lat))
                lats.append(lat)
            self._edges[key] = _unloop(raw, lats, self.centre)
        return self._edges[key]

    def off_road(self, x, z, lat):
        """False if a roadside thing meant to stand `lat` m from the middle would land on the road (the inside of a
        tight turn, or another part of the road nearby)"""
        need = abs(lat) - 2.0
        c2 = need * need
        return not any((cx - x) ** 2 + (cz - z) ** 2 < c2 for cx, cz in self.centre[::2])

    def side(self, i):
        fx, fz = self.forward(i)
        return -fz, fx

    def min_radius(self):
        """tightest turn radius (m) along the road"""
        best = 1e9
        for i in range(1, len(self) - 1):
            da = (self.heading[i + 1] - self.heading[i - 1] + math.pi) % (2 * math.pi) - math.pi
            if abs(da) > 1e-6:
                best = min(best, 2 * STEP / abs(da))
        return best

    def max_grade(self):
        """steepest slope (fraction) along the road"""
        return max([abs(self.height[i + 1] - self.height[i]) / STEP for i in range(len(self) - 1)] or [0.0])

    def bounds(self, margin=0.0):
        xs = [c[0] for c in self.centre]
        zs = [c[1] for c in self.centre]
        return min(xs) - margin, max(xs) + margin, min(zs) - margin, max(zs) + margin

    def near_road(self, x, z, clearance):
        """True if (x, z) is within `clearance` m of the road's centre line anywhere"""
        c2 = clearance * clearance
        return any((cx - x) ** 2 + (cz - z) ** 2 < c2 for cx, cz in self.centre[::2])

    def project(self, x, z):
        """(distance along the road, sideways offset) of the road point nearest (x, z)"""
        i = min(range(len(self)), key=lambda k: (self.centre[k][0] - x) ** 2 + (self.centre[k][1] - z) ** 2)
        sx, sz = self.side(i)
        return self.dist[i], (x - self.centre[i][0]) * sx + (z - self.centre[i][1]) * sz

    def unbridged_crossings(self):
        """crossings where both parts are at about the same height (= the walls would block the road)"""
        return [(j, i) for j, i, _ in self.crossings if abs(self.height[j] - self.height[i]) < BRIDGE_HEIGHT - 2]


if __name__ == "__main__":
    r = Road(**load())
    print("road: %.0f m, %d points, tightest turn radius %.0f m, crossings %d" % (r.length, len(r), r.min_radius(), len(r.crossings)))
    test = Road([[0, 0], {"x": 600, "z": 0, "corner": 40}, {"x": 600, "z": 600, "corner": 40},
                 {"x": 300, "z": 600, "w": 40}, [300, -400], [300, -800]], 60)
    print("90-degree test: %.0f m, min radius %.0f m, crossings %s, max height %.1f, steepest %.1f %%, width %.0f..%.0f"
          % (test.length, test.min_radius(), [(j, i, round(f)) for j, i, f in test.crossings], max(test.height),
             100 * test.max_grade(), min(test.width_at), max(test.width_at)))

"""
The ROAD NETWORK of a map: the main road + BRANCHES + lane SPLITS.

m["branches"] = [{"from_m", "to_m", "points": [{"x", "z", optional "h", "w"}...], "width"}]
    a second road that leaves the main road `from_m` m along it and joins it again at `to_m` (a lap track: to_m may
    run past the loop's end). It starts and ends running along the main road's edge (on its surface, BRANCH_LEAD m)
    on the side its first drawn point is on, so the fork and the merge are smooth.
m["splits"] = [{"from_m", "to_m", "lanes": 2 | 3}]
    dividing walls along the main road from `from_m` to `to_m`: 2 lanes = one wall in the middle, 3 = two walls.

Walls are cut wherever another road's surface is (the openings of a fork / merge); a branch's surface sinks a little
under the main road where they overlap (no flicker). AI: the main AI line keeps lane 0 of a split; every other lane
and every branch is an alternative AI route (gen_aid: an "Alt" spline + a "Junction" over its first points, like the
game's own tracks).
"""
import math

import road as road_mod

STEP = road_mod.STEP
BRANCH_LEAD = 40.0          # m along the main road's edge at both ends of a branch
BRANCH_WIDTH = 30.0
MIN_BRANCH_GAP = 120.0      # a branch joins at least this far after it leaves
DIVIDER_W = 1.0             # a lane divider = a narrow "road" whose two raised edges make the wall
DIVIDER_HALF = 3.2          # m each side of a divider's middle the AI must keep clear of (wall + its sloped edge)
SPLIT_EASE = 70.0           # the AI eases into its lane over this many m before a split
SPLIT_MIN = 60.0
DIVIDER_NOSE = 24.0         # m over which a divider grows from a point (its V-shaped ends)
SINK = 0.3                  # a branch's surface under the main road sinks this far (hidden, no flicker)
JUNCTION_POINTS = 16        # an alternative route's first points = its junction (64 m)
ALT_ON_MAIN = 12            # an alternative route's first points lie ON the main AI line (48 m before it leaves)


def _seg_x(p, q, a, b):
    """crossing of segments p-q and a-b: (t on p-q, u on a-b, point) or None"""
    rx, rz = q[0] - p[0], q[1] - p[1]
    sx, sz = b[0] - a[0], b[1] - a[1]
    den = rx * sz - rz * sx
    if abs(den) < 1e-12:
        return None
    t = ((a[0] - p[0]) * sz - (a[1] - p[1]) * sx) / den
    u = ((a[0] - p[0]) * rz - (a[1] - p[1]) * rx) / den
    if 0.0 <= t <= 1.0 and 0.0 <= u <= 1.0:
        return t, u, (p[0] + rx * t, p[1] + rz * t)
    return None


class Area:
    """fast 'how far inside this road is (x, z)' (metres; < 0 = outside) from the road's points"""

    def __init__(self, road, cell=40.0):
        self.road, self.cell, self.grid = road, cell, {}
        for i, (x, z) in enumerate(road.centre):       # per point: middle, forward, half width, height (made once)
            fx, fz = road.forward(i)
            self.grid.setdefault((int(math.floor(x / cell)), int(math.floor(z / cell))), []).append(
                (x, z, fx, fz, road.width_at[i] / 2, road.height[i], i))
        self.reach = int(math.ceil(max(road.width_at) / 2 / cell)) + 1
        self.bb = road.bounds(max(road.width_at) / 2 + 10.0)
        self._near = {}             # cell -> the points of all cells within reach (made when first asked)

    def _points_near(self, gx, gz):
        key = (gx, gz)
        pts = self._near.get(key)
        if pts is None:
            pts = []
            for ax in range(gx - self.reach, gx + self.reach + 1):
                for az in range(gz - self.reach, gz + self.reach + 1):
                    pts += self.grid.get((ax, az), ())
            self._near[key] = pts
        return pts

    def depth(self, x, z, h=None):
        """half width - distance to the nearest road middle point (+ the step error), -1e9 if far away; h = only
        the road's parts at about that height (a self-crossing road: not the bridge above / the road below)"""
        return self.depth_h(x, z, h)[0]

    def depth_h(self, x, z, h=None):
        """(depth, road height there); h: see depth"""
        x0, x1, z0, z1 = self.bb
        if not (x0 <= x <= x1 and z0 <= z <= z1):
            return -1e9, 0.0
        best, bh = -1e9, 0.0
        lim = STEP * 0.75
        for cx, cz, fx, fz, hw, ph, _ in self._points_near(int(math.floor(x / self.cell)), int(math.floor(z / self.cell))):
            dx, dz = x - cx, z - cz
            along = dx * fx + dz * fz
            if along > lim or along < -lim:
                continue                          # another sample is nearer along the road
            if h is not None and abs(ph - h) > 3.0:
                continue                          # another level (bridge / under it)
            side = dz * fx - dx * fz
            d = hw - (side if side >= 0 else -side)
            if d > best:
                best, bh = d, ph
        return best, bh

    def surface_h(self, x, z, h=None):
        """(depth, the road's surface height there) - the height interpolated between the road points along the
        road, as the drivable deck (road_layout._decks) is made; h: see depth"""
        x0, x1, z0, z1 = self.bb
        if not (x0 <= x <= x1 and z0 <= z <= z1):
            return -1e9, 0.0
        best, bi, ba = -1e9, None, 0.0
        lim = STEP * 0.75
        for cx, cz, fx, fz, hw, ph, i in self._points_near(int(math.floor(x / self.cell)), int(math.floor(z / self.cell))):
            dx, dz = x - cx, z - cz
            along = dx * fx + dz * fz
            if along > lim or along < -lim:
                continue
            if h is not None and abs(ph - h) > 3.0:
                continue
            side = dz * fx - dx * fz
            d = hw - (side if side >= 0 else -side)
            if d > best:
                best, bi, ba = d, i, along
        if bi is None:
            return best, 0.0
        r = self.road
        n = len(r.height)
        j = bi + (1 if ba >= 0 else -1)
        if getattr(r, "closed", False):
            j %= n
        elif j < 0 or j >= n:
            return best, r.height[bi]
        t = min(1.0, abs(ba) / STEP)
        return best, r.height[bi] + (r.height[j] - r.height[bi]) * t


def _side_of(main, b):
    """+1 / -1: the main road's side the branch goes out on (where its first drawn point is)"""
    pts = b.get("points") or []
    if not pts:
        return 1
    _, lat = main.project(pts[0]["x"], pts[0]["z"])
    return 1 if lat >= 0 else -1


def branch_span(main, b):
    """(from_m, to_m) kept valid: inside the road (a sprint), to after from"""
    s0 = float(b.get("from_m", 0.0))
    s1 = float(b.get("to_m", s0 + 400.0))
    if main.closed:
        s0 %= main.length
        while s1 <= s0:
            s1 += main.length
        s1 = min(s1, s0 + main.length - 2 * BRANCH_LEAD)
    else:
        s0 = max(0.0, min(s0, main.length - MIN_BRANCH_GAP - 10))
        s1 = max(s0 + MIN_BRANCH_GAP, min(s1, main.length - 5))
    return s0, s1


def branch_road(main, b):
    """the branch as a Road (its points run from the fork to the merge)"""
    side = _side_of(main, b)
    w = float(b.get("width", BRANCH_WIDTH))
    s0, s1 = branch_span(main, b)
    lead = min(BRANCH_LEAD, (s1 - s0) / 4)

    def on_main(s):
        wl = min(w, max(8.0, main.w_at(s) - 6.0))           # on the main road: narrower than it (widens after)
        lat = side * max(main.w_at(s) / 2 - wl / 2 - 2.0, 0.0)     # 2 m inside the main road's edge
        x, z, _ = main.at(s, lat)
        return {"x": x, "z": z, "w": wl, "h": main.h_at(s)}
    # 3 points along the main road at both ends: the branch runs straight along it before it bends away
    ctl = [on_main(s0), on_main(s0 + lead / 2), on_main(s0 + lead)]
    for p in b.get("points") or []:
        ctl.append({"x": p["x"], "z": p["z"], "w": p.get("w") or w, "h": p.get("h") or 0.0,
                    "corner": p.get("corner") or 0.0})
    ctl += [on_main(s1 - lead), on_main(s1 - lead / 2), on_main(s1)]
    r = road_mod.Road(ctl, w, auto_bridges=False)
    r.kind, r.span, r.out_side = "branch", (s0, s1), side
    _level_ends(main, r)
    return r


HEIGHT_HOLD = 20.0          # m past the main road's wall a branch still keeps the main road's height
HEIGHT_EASE = 40.0          # ... then it eases to its own heights over this many m
MAX_GRADE = 0.10            # a branch is never steeper than this (10 %): the cars would jump


def _level_ends(main, r):
    """the ends of a branch keep EXACTLY the main road's height while they run on / beside it (no bump where the
    cars cross over), then ease into the branch's own heights"""
    n = len(r)
    ma = Area(main)
    hold = int(HEIGHT_HOLD / STEP)
    ease = max(1, int(HEIGHT_EASE / STEP))

    def main_h(i):
        d, h = ma.depth_h(*r.centre[i])        # the nearest main road point's height (fast grid lookup)
        if d < -1e8:
            s, _ = main.project(*r.centre[i])
            return main.h_at(s)
        return h

    def near(i):
        return ma.depth(*r.centre[i]) > -(r.width_at[i] / 2 + 3.0)        # the branch still touches the main road
    for order in (range(n), range(n - 1, -1, -1)):
        order = list(order)
        last_near = 0
        for k, i in enumerate(order):
            if near(i):
                last_near = k
            elif k - last_near > hold + ease:
                break
        for k, i in enumerate(order[:last_near + hold + ease + 1]):
            if k <= last_near + hold:
                r.height[i] = main_h(i)
            else:
                t = (k - last_near - hold) / ease
                t = t * t * (3 - 2 * t)
                r.height[i] = main_h(i) + (r.height[i] - main_h(i)) * t
    # never steeper than MAX_GRADE: a point drawn too high (or low) for the room it has is brought down (up)
    pinned = {i: r.height[i] for i in range(n) if near(i)}
    want = list(r.height)
    g = MAX_GRADE * STEP
    for _ in range(4):
        for idx in (range(1, n), range(n - 2, -1, -1)):
            for i in idx:
                j = i - 1 if idx.step == 1 else i + 1
                r.height[i] = max(r.height[j] - g, min(r.height[j] + g, r.height[i]))
            for i, h in pinned.items():
                r.height[i] = h
    r.lowered = max([abs(a - b) for a, b in zip(want, r.height)] or [0.0])    # shown in the designer's checks


def split_span(main, sp):
    s0 = float(sp.get("from_m", 0.0))
    s1 = float(sp.get("to_m", s0 + 200.0))
    if main.closed:
        s0 %= main.length
        while s1 <= s0:
            s1 += main.length
        s1 = min(s1, s0 + main.length - 50)
    else:
        s0 = max(0.0, min(s0, main.length - SPLIT_MIN))
        s1 = max(s0 + SPLIT_MIN, min(s1, main.length))
    return s0, s1


def lane_count(sp):
    return 3 if int(sp.get("lanes", 2)) >= 3 else 2


def divider_lats(main, s, lanes):
    """the dividers' sideways offsets at s (lanes of equal width)"""
    w = main.w_at(s)
    return [w * (k / lanes - 0.5) for k in range(1, lanes)]


def lane_ranges(main, s, lanes):
    """[(lo, hi)] sideways range of every lane at s (between the walls, minus their sloped edges)"""
    w = main.w_at(s)
    cuts = [-w / 2] + divider_lats(main, s, lanes) + [w / 2]
    out = []
    for k in range(lanes):
        lo = cuts[k] + (DIVIDER_HALF if k > 0 else 0.0)
        hi = cuts[k + 1] - (DIVIDER_HALF if k < lanes - 1 else 0.0)
        out.append((lo, hi))
    return out


def divider_roads(main, sp):
    s0, s1 = split_span(main, sp)
    lanes = lane_count(sp)
    out = []
    for k in range(lanes - 1):
        ctl, s = [], s0
        while True:
            lat = divider_lats(main, s, lanes)[k]
            x, z, _ = main.at(s, lat)
            ctl.append({"x": x, "z": z, "w": DIVIDER_W, "h": main.h_at(s)})
            if s >= s1:
                break
            s = min(s + 8.0, s1)
        r = road_mod.Road(ctl, DIVIDER_W, auto_bridges=False)
        r.kind, r.span = "divider", (s0, s1)
        # V-shaped nose at both ends: 0 (a point) -> full width over DIVIDER_NOSE m (smooth), walls follow it
        r.taper = []
        for i in range(len(r)):
            t = min(1.0, r.dist[i] / DIVIDER_NOSE, (r.length - r.dist[i]) / DIVIDER_NOSE)
            t = max(0.0, t)
            r.taper.append(t * t * (3 - 2 * t))
        out.append(r)
    return out


class Network:
    """main + branches + dividers of one map, with the wall cut-outs and the AI's alternative routes"""

    def __init__(self, m, main=None):
        self.m = m
        self.main = main or road_mod.for_map(m)
        self.main.kind = "main"
        self.branches, self.splits, self.dividers, self.problems = [], [], [], []
        for k, b in enumerate(m.get("branches") or []):
            try:
                self.branches.append((b, branch_road(self.main, b)))
            except Exception as ex:                      # noqa: BLE001 - shown in the designer's checks
                self.problems.append("branch %d: %s" % (k + 1, ex))
        for k, sp in enumerate(m.get("splits") or []):
            try:
                ds = divider_roads(self.main, sp)
                self.splits.append((sp, ds))
                self.dividers += ds
            except Exception as ex:                      # noqa: BLE001
                self.problems.append("split %d: %s" % (k + 1, ex))
        self._areas = None
        self._masks = {}

    # every road with a surface + walls (branches and dividers after the main road)
    def roads(self):
        return [self.main] + [r for _, r in self.branches] + self.dividers

    def drivable(self):
        return [self.main] + [r for _, r in self.branches]

    def areas(self):
        if self._areas is None:
            self._areas = {id(r): Area(r) for r in self.drivable()}
        return self._areas

    # ---- walls at the forks / merges: they MEET (a V), no gap -------------------------------------------------
    # Fork (merge = the same backwards): the main road's wall on the branch side runs until the branch's OUTER wall
    # crosses it (P1: one corner, main wall -> branch outer wall), and starts again where the branch's INNER wall
    # crosses it (P2 = the tip of the V; the branch's inner wall starts there too). The V is as wide as the two
    # curves make it. Found by intersecting the wall lines (1 m outside the edges); a branch whose crossings can't
    # be found falls back to the old rule (cut where another road's surface is).

    def _junctions(self):
        """{id(road): {side: [(s from, s to)] cut intervals (m along that road, may wrap on a lap track)}}, set of
        branch ids handled"""
        if getattr(self, "_jn", None) is not None:
            return self._jn
        cuts, done = {}, set()
        tips = []
        self._tips = tips
        main = self.main
        n = len(main)

        def add(road, side, a, b):
            cuts.setdefault(id(road), {}).setdefault(side, []).append((a, b))
        for b, br in self.branches:
            o = br.out_side
            M = main.edge(o, 1.0)
            Bo, Bi = br.edge(o, 1.0), br.edge(-o, 1.0)
            j0 = int(br.span[0] / STEP)
            j1 = int(math.ceil(br.span[1] / STEP))

            def mwin(j):
                lo, hi = j - 30, j + 60
                return [k % n if main.closed else k for k in range(lo, hi) if main.closed or 0 <= k < n - 1]

            def mwin_end(j):
                lo, hi = j - 60, j + 30
                return [k % n if main.closed else k for k in range(lo, hi) if main.closed or 0 <= k < n - 1]

            def cross(B, order, win):
                """first crossing of branch wall line B (its segments in `order`) with the main wall line (main
                segments `win`): (s on main, s on branch, point)"""
                for k in order:
                    p, q = B[k], B[k + 1]
                    for mk in win:
                        mk2 = (mk + 1) % n if main.closed else mk + 1
                        hit = _seg_x(p, q, M[mk], M[mk2])
                        if hit:
                            t, u, pt = hit
                            sm = main.dist[mk] + u * (main.dist[mk2] - main.dist[mk] if mk2 else main.length - main.dist[mk])
                            sb = br.dist[k] + t * (br.dist[k + 1] - br.dist[k])
                            return sm, sb, pt
                return None
            nb = len(br)
            fwd, back = range(nb - 1), range(nb - 2, -1, -1)
            p1 = cross(Bo, fwd, mwin(j0))
            p2 = cross(Bi, fwd, mwin(j0))
            p4 = cross(Bo, back, mwin_end(j1))
            p3 = cross(Bi, back, mwin_end(j1))
            if not (p1 and p2 and p3 and p4) or not (p1[1] < p3[1] and p2[1] < p4[1]):
                continue                                         # unusual shape: the old rule for this branch
            done.add(id(br))
            tips.append((p2[2], br, "fork"))                     # the V tips (gore blocks: road_mesh)
            tips.append((p3[2], br, "merge"))
            add(main, o, p1[0], p2[0])                           # the opening of the fork
            add(main, o, p3[0], p4[0])                           # ... and of the merge
            add(br, o, -1.0, p1[1])                              # outer wall: from P1 to P4
            add(br, o, p4[1], br.length + 1.0)
            add(br, -o, -1.0, p2[1])                             # inner wall: from the V tip P2 to P3
            add(br, -o, p3[1], br.length + 1.0)
        self._jn = (cuts, done)
        return self._jn

    def cut_bounds(self, road):
        """{side index 0 / 1: [m along the road where an exact fork / merge wall cut begins or ends]} - the road
        model puts extra cross-sections there, so its raised edge ends exactly at the wall crossing (road_mesh)"""
        cuts, _ = self._junctions()
        mine = cuts.get(id(road), {})
        return {si: sorted(e % road.length if road.closed else e for a, b in mine.get(side, ()) for e in (a, b))
                for si, side in enumerate((-1, 1))}

    def in_cut(self, road, side, s):
        """True where that side's raised edge / wall is cut at s m (the exact fork / merge intervals)"""
        return self._in_cut(road, side, s)

    def _in_cut(self, road, side, sq):
        cuts, _ = self._junctions()
        L = road.length
        for a, b in cuts.get(id(road), {}).get(side, ()):
            if road.closed and b < a:                            # wraps over the start / finish line
                if sq >= a or sq <= b:
                    return True
            elif a < sq < b:
                return True
        return False

    def wall_runs(self, road, side, heights=False):
        """[[(x, z)...]] the pieces of that road's wall line (1 m outside its edge) that are NOT cut - with the exact
        crossing points at their ends, so the main and the branch walls meet. heights=True: [(x, z, h)...] with the
        road's OWN height there (not a map lookup - under a bridge that would give the bridge's height)"""
        edge = road.edge(side, 1.0)
        if heights:
            edge = [(x, z, road.height[i]) for i, (x, z) in enumerate(edge)]
        mask = self.wall_mask(road)[0 if side < 0 else 1]
        n = len(road)
        cuts, done = self._junctions()
        exact = id(road) in done or (road is self.main and cuts.get(id(road)))
        idx = list(range(n)) + ([0] if road.closed else [])
        runs, cur = [], []

        def s_of(k, i):
            return road.length if (road.closed and k == len(idx) - 1) else road.dist[i]
        for k, i in enumerate(idx):
            if mask[i]:
                if cur:
                    if exact and k > 0:                          # end exactly at the cut's start
                        cur.append(self._cut_point(road, side, idx[k - 1], i, edge))
                    runs.append(cur)
                cur = []
            else:
                if not cur and exact and k > 0 and mask[idx[k - 1]]:
                    cur.append(self._cut_point(road, side, i, idx[k - 1], edge))   # start exactly at its end
                cur.append(edge[i])
        if cur:
            runs.append(cur)
        return [r for r in runs if len(r) >= 2]

    def _cut_point(self, road, side, i_in, i_out, edge):
        """the point between wall points i_in (kept) and i_out (cut) where the cut interval begins / ends"""
        cuts, _ = self._junctions()
        si, so = road.dist[i_in], road.dist[i_out]
        if road.closed and abs(si - so) > road.length / 2:
            so = so + road.length if so < si else so - road.length
        best = None
        for a, b in cuts.get(id(road), {}).get(side, ()):
            for e in (a, b):
                for sh in ((0.0, road.length, -road.length) if road.closed else (0.0,)):
                    ee = e + sh
                    if min(si, so) - 1e-6 <= ee <= max(si, so) + 1e-6:
                        best = ee
        if best is None or so == si:
            return edge[i_in]
        t = (best - si) / (so - si)
        a, b = edge[i_in], edge[i_out]
        return tuple(a[k] + (b[k] - a[k]) * t for k in range(len(a)))

    def wall_mask(self, road):
        """[side -1 list, side +1 list] of True where that road's wall / raised edge is cut: at the forks / merges by
        the exact wall crossings (_junctions); elsewhere where another road's surface is"""
        key = id(road)
        if key in self._masks:
            return self._masks[key]
        n = len(road)
        mask = [[False] * n, [False] * n]
        if road.kind != "divider" and self.branches:
            cuts, done = self._junctions()
            mine = cuts.get(id(road), {})
            for si, side in enumerate((-1, 1)):
                for i in range(n):
                    mask[si][i] = self._in_cut(road, side, road.dist[i])
            areas = self.areas()
            # the old rule for what the crossings don't cover (a branch without crossings, or crossing elsewhere)
            others = [r for r in self.drivable() if r is not road and id(r) not in done and not
                      (road.kind == "branch" and id(road) in done and r is self.main)]
            if road.kind == "main":
                others = [r for r in self.drivable() if r is not road and id(r) not in done]
            for si, side in enumerate((-1, 1)):
                edge = road.edge(side, 1.0)
                for i in range(n):
                    if mask[si][i]:
                        continue
                    x, z = edge[i]
                    for o in others:
                        d, h = areas[id(o)].depth_h(x, z)
                        need = -2.5 if (road.kind == "branch" and o.kind == "main") else 1.5
                        if d > need and abs(h - road.height[i]) < 3.0:
                            mask[si][i] = True
                            break
        self._masks[key] = mask
        return mask

    def sink(self, road):
        """function (x, z) -> metres to lower a surface point of `road` (a branch under the main road: hidden)"""
        if road.kind != "branch":
            return None
        main_area = self.areas()[id(self.main)]

        def f(x, z):
            return -SINK if main_area.depth(x, z) > 0.3 else 0.0
        return f

    def clip(self, road):
        """function (i, x, z) -> (x, z) for a BRANCH's surface points: a point on the main road moves out to the main
        road's edge (along the branch's cross line, towards its outer edge) - the branch surface starts exactly where
        the main road ends: no two surfaces on top of each other (they flickered in game). None for other roads."""
        if road.kind != "branch":
            return None
        ma = self.areas()[id(self.main)]
        outer = road.edge(road.out_side)

        def f(i, x, z):
            if ma.depth(x, z) <= -0.3:                         # (on the edge counts as on the road)
                return x, z
            ox, oz = outer[i]
            dx, dz = ox - x, oz - z                            # towards the branch's outer edge ...
            ln = math.hypot(dx, dz)
            if ln < 0.5:                                       # (the point IS the outer edge)
                sx, sz = road.side(i)
                dx, dz, ln = sx * road.out_side, sz * road.out_side, 1.0
            dx, dz = dx / ln, dz / ln
            b = ln
            while ma.depth(x + dx * b, z + dz * b) > 0.0 and b < 200.0:
                b += 4.0                                       # ... and beyond it while still on the main road
            a = 0.0
            for _ in range(18):                                # inside at a, outside at b: find the main edge
                t = (a + b) / 2
                if ma.depth(x + dx * t, z + dz * t) > 0.0:
                    a = t
                else:
                    b = t
            return x + dx * b, z + dz * b
        return f

    def clear_of_splits(self, s, margin=12.0):
        """s moved out of every lane split (+ margin): to whichever end is nearer (power-up rows)"""
        L = self.main.length
        for sp, _ in self.splits:
            s0, s1 = split_span(self.main, sp)
            for sh in ((0.0, L, -L) if self.main.closed else (0.0,)):
                t = s + sh
                if s0 - margin < t < s1 + margin:
                    return (s0 - margin if t - s0 < s1 - t else s1 + margin) - sh
        return s

    def near_branch(self, x, z, margin=0.0):
        """True if (x, z) is on a branch (or within margin m of it): roadside things there are left out"""
        if not self.branches:
            return False
        areas = self.areas()
        return any(areas[id(r)].depth(x, z) > -margin for _, r in self.branches)

    # ---- AI ----
    def split_weight(self, s):
        """(lanes, weight 0..1) of the split around s (eases in over SPLIT_EASE m before it, out 20 m after)"""
        L = self.main.length
        for sp, _ in self.splits:
            s0, s1 = split_span(self.main, sp)
            for shift in ((0.0, L, -L) if self.main.closed else (0.0,)):
                t = s + shift
                if s0 <= t <= s1:
                    return lane_count(sp), 1.0
                if s0 - SPLIT_EASE < t < s0:
                    return lane_count(sp), (t - (s0 - SPLIT_EASE)) / SPLIT_EASE
                if s1 < t < s1 + 20:
                    return lane_count(sp), 1.0 - (t - s1) / 20
        return 0, 0.0

    def main_lane(self, i, lo, hi):
        """the main AI line's (lo, hi) at road point i: lane 0 inside a split"""
        lanes, w = self.split_weight(self.main.dist[i])
        if not lanes:
            return lo, hi
        a, b = lane_ranges(self.main, self.main.dist[i], lanes)[0]
        return lo + (a - lo) * w, hi + (b - hi) * w

    def alt_routes(self, y, corridor=None, margin=2.5):
        """[{"name", "points": [(x, y, z, width, fx, fz)], "logic": [main road index per point]}] - every split lane
        but the first and every branch. corridor = the main AI line's (lo, hi) per road point: an alternative route
        starts ON it (its first ALT_ON_MAIN points = the main line's: the junction is there, as on the game's own
        tracks) and a branch ends on it again."""
        main = self.main
        n = len(main)
        out = []

        def main_pt(j):
            i = j % n
            half = main.width_at[i] / 2 - margin
            lo, hi = corridor[i] if corridor else (-half, half)
            cx, cz = main.centre[i]
            sx, sz = main.side(i)
            fx, fz = main.forward(i)
            return (cx + sx * lo, y + main.height[i], cz + sz * lo, max(hi - lo, 4.0), fx, fz)
        for sp, _ in self.splits:
            s0, s1 = split_span(main, sp)
            lanes = lane_count(sp)
            i0 = int(math.floor((s0 - SPLIT_EASE) / STEP))
            i1 = int(math.ceil((s1 + 20) / STEP))
            if not main.closed:
                i0, i1 = max(i0, 0), min(i1, n - 1)
            for lane in range(1, lanes):
                pts, logic = [], []
                for j in range(i0 - ALT_ON_MAIN, i1 + 1):
                    if not main.closed and not 0 <= j < n:
                        continue
                    i = j % n
                    s = main.dist[i]
                    half = main.width_at[i] / 2 - margin
                    _, w = self.split_weight(s)
                    if j < i0:
                        w = 0.0                          # on the main line (the junction)
                    a, b = lane_ranges(main, s, lanes)[lane]
                    base_lo, base_hi = corridor[i] if corridor else (-half, half)
                    lo, hi = base_lo + (a - base_lo) * w, base_hi + (b - base_hi) * w
                    cx, cz = main.centre[i]
                    sx, sz = main.side(i)
                    fx, fz = main.forward(i)
                    pts.append((cx + sx * lo, y + main.height[i], cz + sz * lo, max(hi - lo, 4.0), fx, fz))
                    logic.append(j)
                out.append({"points": pts, "logic": logic})
        for b, r in self.branches:
            s0, s1 = r.span
            j0, j1 = s0 / STEP, s1 / STEP
            nb = len(r)
            pts, logic = [], []
            f0 = int(round(j0))
            for j in range(f0 - ALT_ON_MAIN, f0):            # on the main AI line up to the fork (the junction)
                if main.closed or 0 <= j < n:
                    pts.append(main_pt(j))
                    logic.append(j)
            for i in range(nb):
                half = r.width_at[i] / 2 - margin
                cx, cz = r.centre[i]
                sx, sz = r.side(i)
                fx, fz = r.forward(i)
                pts.append((cx - sx * half, y + r.height[i], cz - sz * half, 2 * half, fx, fz))
                logic.append(int(round(j0 + (j1 - j0) * i / max(nb - 1, 1))))
            t0 = int(round(j1))
            for j in range(t0 + 1, t0 + 1 + ALT_ON_MAIN // 2):  # and back on it after the merge
                if main.closed or 0 <= j < n:
                    pts.append(main_pt(j))
                    logic.append(j)
            out.append({"points": pts, "logic": logic})
        for k, a in enumerate(out):
            a["name"] = "Alt%d" % (k + 1)
            a["junction"] = "Junction%d" % (k + 1)
        return out

    # ---- power-ups ----
    def pickup_road(self, it):
        """the road a power-up item stands on: its branch ("bi") or the main road"""
        bi = it.get("bi")
        if bi is not None and 0 <= bi < len(self.branches):
            return self.branches[bi][1]
        return self.main

    def auto_rows(self, s0, s1, step, max_rows):
        """the automatic power-up rows: every `step` m between the lines on the main road + along every branch
        (at most max_rows in all; the branches get theirs first, the main road keeps at least 4)"""
        extra = []
        for bi, (b, r) in enumerate(self.branches):
            usable = r.length - 2 * BRANCH_LEAD
            k = max(1, int(usable // step))
            for j in range(k):
                x, z, _ = r.at(BRANCH_LEAD + usable * (j + 0.5) / k)
                extra.append({"kind": "row", "x": round(x, 1), "z": round(z, 1), "bi": bi})
        extra = extra[:max(0, max_rows - 4)]
        span = s1 - s0
        rows = max(0, min(int(span // step), max_rows - len(extra)))
        first, room = step / 2, span - step
        out = []
        for k in range(rows):
            x, z, _ = self.main.at(self.clear_of_splits(s0 + first + room * k / max(rows - 1, 1)))
            out.append({"kind": "row", "x": round(x, 1), "z": round(z, 1)})
        return out + extra

    def logic_band(self, alts, margin=12.0, spread=10):
        """[(lo, hi)] per main road point: the sideways range of the LOGIC line (the game resets a car that leaves
        it - on every game track the Logic band is wide enough to hold all its alternative routes, up to 437 m).
        Default: the main road (as always); widened to cover every branch point beside it (+ margin), spread over
        +-spread points so the band changes smoothly."""
        main = self.main
        n = len(main)
        lo = [-main.width_at[i] / 2 for i in range(n)]
        hi = [main.width_at[i] / 2 for i in range(n)]
        if not self.branches:
            return list(zip(lo, hi))
        need_lo, need_hi = list(lo), list(hi)
        for b, r in self.branches:
            s0, s1 = r.span
            j0, j1 = s0 / STEP, s1 / STEP
            nb = len(r)
            for k in range(0, nb, 2):
                guess = int(round(j0 + (j1 - j0) * k / max(nb - 1, 1)))
                for side in (-1, 1):
                    ex, ez = r.edge(side, 2.0)[k]                 # the branch's wall line (both sides)
                    best = None
                    for jj in range(guess - 60, guess + 61):      # the main point whose cross line runs through it
                        if not main.closed and not 0 <= jj < n:
                            continue
                        i = jj % n
                        cx, cz = main.centre[i]
                        fx, fz = main.forward(i)
                        along = (ex - cx) * fx + (ez - cz) * fz
                        if best is None or abs(along) < abs(best[0]):
                            sx, sz = main.side(i)
                            best = (along, i, (ex - cx) * sx + (ez - cz) * sz)
                    if best is None or abs(best[0]) > 3 * STEP:
                        continue
                    _, i, lat = best
                    need_lo[i] = min(need_lo[i], lat - margin)
                    need_hi[i] = max(need_hi[i], lat + margin)
        out = []
        for i in range(n):                                       # widest need within +-spread points
            a, b = lo[i], hi[i]
            for d in range(-spread, spread + 1):
                j = i + d
                if main.closed:
                    j %= n
                elif not 0 <= j < n:
                    continue
                a, b = min(a, need_lo[j]), max(b, need_hi[j])
            out.append((a, b))
        return out

    def balance(self):
        """[(branch index, branch length, main length it replaces, ratio)] - how a branch compares with the main
        road between the fork and the merge (1.0 = the same driving distance)"""
        out = []
        for bi, (b, r) in enumerate(self.branches):
            s0, s1 = r.span
            main_len = max(s1 - s0, 1.0)
            out.append((bi, r.length, main_len, r.length / main_len))
        return out

    def busy_ranges(self):
        """[(first, last)] main road indices where branches / splits are (the split gates must not be there:
        cars on a branch would miss them and the lap would not count)"""
        out = []
        for _, r in self.branches:
            out.append((int(r.span[0] / STEP) - 5, int(math.ceil(r.span[1] / STEP)) + 5))
        for sp, _ in self.splits:
            s0, s1 = split_span(self.main, sp)
            out.append((int((s0 - SPLIT_EASE) / STEP), int(math.ceil((s1 + 20) / STEP))))
        return out


_cache = {}


def get(m, main=None):
    """the Network of map m (cached per main Road object: the build asks for it many times)"""
    key = id(main) if main is not None else None
    sig = repr((m.get("branches"), m.get("splits")))       # edited since? (the same dict changes in the designer)
    c = _cache.get(key) if key is not None else None
    if c is not None and c.m is m and c.main is main and c.sig == sig:
        return c
    net = Network(m, main)
    net.sig = sig
    if key is not None:
        if len(_cache) > 16:
            _cache.clear()
        _cache[key] = net
    return net

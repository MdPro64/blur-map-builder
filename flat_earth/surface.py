"""
ROAD SURFACES: what the cars drive on (grip) and what it looks like.

The game's collision triangles carry an EFFECT MATERIAL number in their group header (the 3rd value: 1 = Tarmac,
4 = Dirt ...; racingphysics.bin maps it to a driving surface with its own grip: Dirt = 80 % grip at speed, rolling
friction 6, rough). Our flat floor is Tarmac; a painted area is a thin collision patch 2 cm above it with another
material (write_flat.build_flat patches) + a flat textured model on top (overlay chunks, the road_mesh way).

Map data: m["surface"] = {
    "road": key        the default type of the whole road (main + branches; "tarmac" = the normal road)
    "sides": metres    0 = off; > 0 = the outer `sides` m of both road edges are `side_type` (asphalt in the middle)
    "side_type": key
    "variety": bool    varied asphalt: dark / light patches, repairs and cracks (look only)
    "paint": {"ix,iz": key}   brush cells (CELL m squares; "tarmac" = asphalt painted over a dirt road / sides)
}
"""
import math
import struct

import numpy as np

# key: (label, effect material, 2D colour, base RGB, material texture (shine, glow))
SURFACES = {
    "tarmac": ("Asphalt", 1, "#565c66", (88, 92, 98), (30, 60)),
    "dirt": ("Dirt", 4, "#8a6a42", (122, 92, 60), (8, 35)),
    "roughdirt": ("Rough dirt", 33, "#6c5234", (98, 74, 50), (6, 30)),
    "gravel": ("Gravel", 32, "#9a9286", (140, 134, 122), (10, 40)),   # DirtLightRough (no Gravel grip entry)
    "grass": ("Grass", 6, "#4f7d3a", (70, 108, 52), (6, 35)),
    "sand": ("Wet sand", 9, "#c2a878", (176, 152, 110), (40, 40)),
    "water": ("Water (shallow)", 34, "#3a78a8", (52, 92, 120), (200, 70)),
    "deepwater": ("Deep water", 22, "#1e4a7a", (28, 58, 92), (220, 60)),
}
ORDER = ("dirt", "roughdirt", "gravel", "grass", "sand", "water", "deepwater", "tarmac")
CELL = 2.0                 # brush cell size (m)
STEP_M = 4.0               # road point spacing (road.STEP)
PATCH_LIFT = 0.02          # collision patch above the floor (m)
OVERLAY_LIFT = 0.07        # painted look above the road / ground (m)
TEX_M = 8.0                # overlay textures repeat every 8 m
MAX_QUADS = 780            # per overlay model (the source mesh: 3232 vertices, 4861 strip indices)


def settings(m):
    s = dict((m or {}).get("surface") or {})
    s.setdefault("road", "tarmac")
    s.setdefault("sides", 0.0)
    s.setdefault("side_type", "dirt")
    s.setdefault("variety", False)
    s.setdefault("paint", {})
    if s["road"] not in SURFACES:
        s["road"] = "tarmac"
    if s["side_type"] not in SURFACES:
        s["side_type"] = "dirt"
    return s


def is_default(m):
    s = settings(m)
    return s["road"] == "tarmac" and not s["sides"] and not s["variety"] and not s["paint"]


def cell_of(x, z):
    return int(math.floor(x / CELL)), int(math.floor(z / CELL))


def _feather(radius):
    return max(1.5, 0.3 * radius)


def brush(m, x, z, radius, key):
    """paint (key) or erase (key None) a FEATHERED circle: a cell's strength falls from 1 inside the circle to 0
    `_feather` m outside it (0.5 = exactly on the circle: the painted outline). Paint keeps the stronger of the
    old and the new strength; another surface only takes a cell where the new stroke is stronger. Erasing lowers
    the strength (cells under 0.05 go). Returns how many cells changed."""
    surf = m.setdefault("surface", {})
    paint = surf.setdefault("paint", {})
    f = _feather(radius)
    reach = radius + f / 2
    i0, j0 = cell_of(x - reach, z - reach)
    i1, j1 = cell_of(x + reach, z + reach)
    n = 0
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            d = math.hypot((i + 0.5) * CELL - x, (j + 0.5) * CELL - z)
            st = max(0.0, min(1.0, (radius - d) / f + 0.5))
            if st <= 0.0:
                continue
            k = "%d,%d" % (i, j)
            old_key, old_s = _parse(paint.get(k))
            if key is None:
                if old_key is None:
                    continue
                new_s = min(old_s, 1.0 - st)
                if new_s < 0.05:
                    del paint[k]
                elif new_s < old_s:
                    paint[k] = _fmt(old_key, new_s)
                else:
                    continue
                n += 1
            elif old_key == key:
                if st > old_s + 1e-6:
                    paint[k] = _fmt(key, st)
                    n += 1
            elif old_key is None or st > old_s:
                paint[k] = _fmt(key, st)
                n += 1
    return n


def _parse(v):
    """a paint value: "key" (strength 1, older maps) or "key:0.62" -> (key, strength); None -> (None, 0)"""
    if not v:
        return None, 0.0
    if ":" in v:
        k, st = v.split(":", 1)
        return k, float(st)
    return v, 1.0


def _fmt(key, st):
    return key if st >= 0.995 else "%s:%.2f" % (key, st)


def paint_strengths(m):
    """{(ix, iz): (key, strength)}"""
    out = {}
    for k, v in (settings(m)["paint"] or {}).items():
        key, st = _parse(v)
        if key in SURFACES:
            i, j = k.split(",")
            out[(int(i), int(j))] = (key, st)
    return out


def _paint(m):
    """{(ix, iz): key} of the cells painted at least half strength (inside the painted outline)"""
    return {c: k for c, (k, st) in paint_strengths(m).items() if st >= 0.5}


_base_cache = {}


def base_cells(m, net, with_sides=False):
    """{(ix, iz): key} of the road surfaces BEFORE the paint: the road type + the sides (empty for a plain
    asphalt road). with_sides: also return the set of side cells (the paint never covers them). Cached until the
    roads or these settings change."""
    s = settings(m)
    key = (id(net), getattr(net, "sig", None), id(net.main), len(net.main), round(net.main.length, 2),
           s["road"], s["sides"], s["side_type"])
    hit = _base_cache.get(key)
    if hit is None or hit[0] is not net:
        hit = (net, _base_cells(m, net))
        if len(_base_cache) > 8:
            _base_cache.clear()
        _base_cache[key] = hit
    out, sides = hit[1]
    return (dict(out), set(sides)) if with_sides else dict(out)


def _base_cells(m, net):
    s = settings(m)
    out, sides = {}, set()
    if s["road"] == "tarmac" and not s["sides"]:
        return out, sides
    side = float(s["sides"])
    main_area = net.areas()[id(net.main)] if net.branches else None
    step = CELL / 2                                       # every 1 m along and across (the cells are 2 m)
    for r in net.drivable():
        cut = net.wall_mask(r)                             # the sides stop where the walls stop (openings)
        fac = (side_factor(net, r, 0), side_factor(net, r, 1))     # ... narrowing smoothly towards them
        n = int(r.length // step) + 1
        for q in range(n):
            sq = q * step
            i = r.index_at(sq)
            cx, cz, a_ = r.at(sq)
            sx, sz = -math.cos(a_), math.sin(a_)          # the road's side direction there
            half = r.w_at(sq) / 2
            k = -half
            while k <= half:
                x, z = cx + sx * k, cz + sz * k
                if r is not net.main and main_area is not None and main_area.depth(x, z) > 0:
                    k += step                              # a branch on the main road: the main road decides there
                    continue
                c = cell_of(x, z)
                si_ = 0 if k < 0 else 1
                if side > 0 and abs(k) > half - side * fac[si_][i] and not cut[si_][i]:
                    out[c] = s["side_type"]                # the sides win over the middle (cells on the line)
                    sides.add(c)
                else:
                    out.setdefault(c, s["road"])
                k += step
    return out, sides


SIDE_TAPER = 25.0          # m: the sides narrow to nothing this far before an opening (and widen after it)


def side_factor(net, road, si):
    """[0..1] per road point: how much of the side strip is there (0 in a branch opening, easing up to 1 over
    SIDE_TAPER m on both sides of it)"""
    cut = net.wall_mask(road)[si]
    n = len(road)
    if not any(cut):
        return [1.0] * n
    big = 10 ** 9
    dist = [0 if cut[i] else big for i in range(n)]
    rng = list(range(n))
    for order in (rng, rng[::-1]):                       # nearest cut point, both ways (a loop wraps: twice)
        for _ in range(2 if road.closed else 1):
            prev = None
            for i in order:
                if prev is not None:
                    dist[i] = min(dist[i], dist[prev] + 1)
                prev = i
            if road.closed:
                dist[order[0]] = min(dist[order[0]], dist[order[-1]] + 1)
    out = []
    for d in dist:
        t = min(1.0, d * STEP_M / SIDE_TAPER)
        out.append(t * t * (3 - 2 * t))
    return out


def side_gaps(m, net):
    """[(road, side, [road point indices...])] runs where a road's side strip is CUT (a branch leaves / joins
    there, like its wall): the road texture has the side band all along, so these get the road type laid over it"""
    s = settings(m)
    out = []
    if not s["sides"]:
        return out
    for r in net.drivable():
        cut = net.wall_mask(r)
        for si, side in enumerate((-1, 1)):
            run = []
            for i in range(len(r)):
                if cut[si][i]:
                    run.append(i)
                elif run:
                    out.append((r, side, run))
                    run = []
            if run:
                out.append((r, side, run))
    return out


def grid(m, net):
    """{(ix, iz): key} - the final surface of every cell that is NOT plain asphalt (the floor is asphalt): the road
    type and the sides, the paint on top - but never over the sides (they stay on top). net = network.Network"""
    out, sides = base_cells(m, net, with_sides=True)
    for c, k in _paint(m).items():
        if c not in sides:
            out[c] = k
    return {c: k for c, k in out.items() if k != "tarmac"}


def _heights(net):
    """function (x, z) -> height above the ground of the road there (0 off the road)"""
    areas = net.areas()
    roads = net.drivable()

    def h(x, z):
        best, hh = 0.0, 0.0
        for r in roads:
            d, rh = areas[id(r)].depth_h(x, z)
            if d > -CELL and (d > best or best == 0.0):
                best, hh = d, rh
        return hh
    return h


def rects(cells, height):
    """merge cells into rectangles: [(key, x0, z0, x1, z1, y)] - runs along x, then equal runs stacked along z"""
    rows = {}
    for (i, j), k in cells.items():
        rows.setdefault(j, []).append((i, k))
    runs = {}                                   # (i0, i1, key, y rounded) -> list of rows j
    for j, lst in rows.items():
        lst.sort()
        a = 0
        while a < len(lst):
            b = a
            while b + 1 < len(lst) and lst[b + 1][0] == lst[b][0] + 1 and lst[b + 1][1] == lst[a][1]:
                b += 1
            i0, i1, key = lst[a][0], lst[b][0], lst[a][1]
            y = round(height(((i0 + i1 + 1) / 2) * CELL, (j + 0.5) * CELL) * 2) / 2
            runs.setdefault((i0, i1, key, y), []).append(j)
            a = b + 1
    out = []
    for (i0, i1, key, y), js in runs.items():
        js.sort()
        a = 0
        while a < len(js):
            b = a
            while b + 1 < len(js) and js[b + 1] == js[b] + 1:
                b += 1
            out.append((key, i0 * CELL, js[a] * CELL, (i1 + 1) * CELL, (js[b] + 1) * CELL, y))
            a = b + 1
    return out


LEVEL_M = 3.0              # road heights this far apart are different LEVELS (a bridge and the road under it)


def _level_base(m, net):
    """as _base_cells, but per road LEVEL: ({(ix, iz, L): key}, side cells, {(ix, iz, L): a road height there}).
    A bridge and the road under it share the 2D cells - one flat 2D grid put the lower road's sides on the bridge
    deck (bumps; user 2026-10-20)."""
    s = settings(m)
    out, sides, hs = {}, set(), {}
    if s["road"] == "tarmac" and not s["sides"]:
        return out, sides, hs
    side = float(s["sides"])
    main_area = net.areas()[id(net.main)] if net.branches else None
    step = CELL / 2
    for r in net.drivable():
        cut = net.wall_mask(r)
        fac = (side_factor(net, r, 0), side_factor(net, r, 1))
        n = int(r.length // step) + 1
        for q in range(n):
            sq = q * step
            i = r.index_at(sq)
            cx, cz, a_ = r.at(sq)
            sx, sz = -math.cos(a_), math.sin(a_)
            half = r.w_at(sq) / 2
            h = r.h_at(sq)
            lev = int(round(h / LEVEL_M))
            k = -half
            while k <= half:
                x, z = cx + sx * k, cz + sz * k
                if r is not net.main and main_area is not None and main_area.depth(x, z, h) > 0:
                    k += step                              # a branch on the main road (same level): the main decides
                    continue
                c = cell_of(x, z) + (lev,)
                hs.setdefault(c, h)
                si_ = 0 if k < 0 else 1
                if side > 0 and abs(k) > half - side * fac[si_][i] and not cut[si_][i]:
                    out[c] = s["side_type"]
                    sides.add(c)
                else:
                    out.setdefault(c, s["road"])
                k += step
    return out, sides, hs


def level_grid(m, net):
    """({(ix, iz, L): key} of every non-asphalt cell per road level, {(ix, iz, L): a road height there}): the road
    type and the sides of each level; the paint (2D) goes on every level of its cell (off the road: the ground)"""
    out, sides, hs = _level_base(m, net)
    levels = {}
    for i, j, lev in hs:
        levels.setdefault((i, j), []).append(lev)
    for c2, k in _paint(m).items():
        for lev in levels.get(c2, [0]):
            c = c2 + (lev,)
            if c not in sides:
                out[c] = k
                hs.setdefault(c, 0.0)
    return {c: k for c, k in out.items() if k != "tarmac"}, hs


def physics_patches(fe, m, net):
    """[(piece, effect material)] collision patches (write_flat deck pieces) per road level, ON the road surface:
    flat parts merged into rectangles, sloped parts (bridge ramps) as strips whose corners follow the road (flat
    patches rounded to 0.5 m made steps on the ramps - the cars jumped, user 2026-10-20)"""
    import write_flat
    if is_default(m):
        return []
    cells, hs = level_grid(m, net)
    areas = net.areas()
    roads = net.drivable()
    vcache = {}

    def vh(i, j, lev, href):
        """road surface height at grid corner (i, j) on level lev (href: a height of that level nearby)"""
        key = (i, j, lev)
        v = vcache.get(key)
        if v is None:
            x, z = i * CELL, j * CELL
            best, v = -1e9, None
            for r in roads:
                d, h = areas[id(r)].surface_h(x, z, href)
                if d > -CELL * 1.5 and d > best:
                    best, v = d, h
            if v is None:
                v = href if lev else 0.0           # off the road (paint on the ground / beside a deck)
            vcache[key] = v
        return v

    flat, slope = {}, {}
    for (i, j, lev), k in cells.items():
        href = hs.get((i, j, lev), 0.0)
        cs = [vh(i, j, lev, href), vh(i + 1, j, lev, href), vh(i, j + 1, lev, href), vh(i + 1, j + 1, lev, href)]
        if max(cs) - min(cs) < 0.02:
            flat[(i, j)] = flat.get((i, j), []) + [(k, round(sum(cs) / 4, 2))]
        else:
            slope[(i, j, lev)] = (k, href)
    out = []
    # flat: merge like rects() per (surface, height)
    by_kh = {}
    for (i, j), lst in flat.items():
        for k, y in lst:
            by_kh.setdefault((k, y), {})[(i, j)] = k
    for (k, y), cl in by_kh.items():
        for key, x0, z0, x1, z1, _ in rects(cl, lambda x, z: 0.0):
            yy = fe.GROUND_Y + y + PATCH_LIFT
            out.append((write_flat.deck_strip([(x0, yy, z0), (x0, yy, z1)], [(x1, yy, z0), (x1, yy, z1)]),
                        SURFACES[key][1]))
    # sloped: runs along z (one column, same surface + level), corners on the road
    cols = {}
    for (i, j, lev), (k, href) in slope.items():
        cols.setdefault((i, lev, k), []).append((j, href))
    for (i, lev, k), lst in cols.items():
        lst.sort()
        a = 0
        while a < len(lst):
            b = a
            while b + 1 < len(lst) and lst[b + 1][0] == lst[b][0] + 1 and b - a < 120:
                b += 1
            left, right = [], []
            for t in range(a, b + 2):
                j = lst[a][0] + (t - a)
                href = lst[min(t, b)][1]
                z = j * CELL
                left.append((i * CELL, fe.GROUND_Y + vh(i, j, lev, href) + PATCH_LIFT, z))
                right.append(((i + 1) * CELL, fe.GROUND_Y + vh(i + 1, j, lev, href) + PATCH_LIFT, z))
            out.append((write_flat.deck_strip(left, right), SURFACES[k][1]))
            a = b + 1
    return out


# ---------------------------------------------------------------------------------------------- textures (numpy)
def _tile_noise(w, h, seed, scale):
    """tileable smooth noise 0..1 (w x h), blobs about `scale` px"""
    rng = np.random.default_rng(seed)
    a = rng.random((h, w))
    k = max(1, int(scale))
    for _ in range(3):                                   # box blur with wrap-around = tileable
        a = sum(np.roll(a, s, 0) for s in range(-k, k + 1)) / (2 * k + 1)
        a = sum(np.roll(a, s, 1) for s in range(-k, k + 1)) / (2 * k + 1)
    a -= a.min()
    return a / max(a.max(), 1e-9)


def material_texture(key, w, h, seed=1):
    """(diffuse RGB bytes, material bytes) of a surface, tileable (overlays)"""
    base = np.array(SURFACES[key][3], float)
    shine, glow = SURFACES[key][4]
    n1 = _tile_noise(w, h, seed, w / 32)
    n2 = _tile_noise(w, h, seed + 7, w / 128)
    fine = np.random.default_rng(seed + 3).random((h, w))
    if key in ("water", "deepwater"):
        f = 0.85 + 0.25 * n1 + 0.06 * np.sin(n2 * 12.0)
    elif key == "grass":
        f = 0.75 + 0.35 * n1 + 0.25 * (fine - 0.5)
    elif key == "gravel":
        f = 0.80 + 0.20 * n1 + 0.45 * (fine - 0.5)
    elif key == "sand":
        f = 0.90 + 0.15 * n1 + 0.06 * np.sin(np.arange(h)[:, None] * 2 * np.pi * 6 / h + n2 * 4)
    else:                                                 # dirt / rough dirt / tarmac
        f = 0.78 + 0.30 * n1 + 0.20 * (n2 - 0.5) + 0.22 * (fine - 0.5)
    rgb = np.clip(base[None, None, :] * f[:, :, None], 0, 255).astype(np.uint8)
    mat = np.zeros((h, w, 3), np.uint8)
    mat[:, :, 0] = shine
    mat[:, :, 1] = glow
    return rgb.tobytes(), mat.tobytes()


def _cracks(img, mask, rng, count, w, h):
    """dark crack lines (random walks, wrapping along u) where mask is True"""
    for _ in range(count):
        x, y = rng.integers(0, w), rng.integers(0, h)
        ang = rng.random() * 2 * np.pi
        for _ in range(rng.integers(20, 70)):
            ang += rng.normal(0, 0.5)
            x = (x + int(round(np.cos(ang) * 2))) % w
            y = int(y + round(np.sin(ang) * 2))
            if not 0 <= y < h:
                break
            if mask[y, x]:
                img[y, x] = img[y, x] * 0.45


def road_texture(fe, w, h, width, m, variant=0):
    """the road texture of road_texture_profile with the map's surface: the road type, the sides (asphalt in the
    middle), varied asphalt (patches + cracks; `variant` = a different pattern per road model)"""
    diff, mat = fe.road_texture_profile(w, h, width)
    s = settings(m)
    if s["road"] == "tarmac" and not s["sides"] and not s["variety"]:
        return diff, mat
    img = np.frombuffer(diff, np.uint8).reshape(h, w, 3).astype(float)
    mt = np.frombuffer(mat, np.uint8).reshape(h, w, 3).copy()
    keep = img.min(axis=2) > 200                       # the white road markings (middle line) stay as they are
    keep_px = img[keep].copy()
    prof = fe.road_profile(width)
    nb = len(fe.BANK)
    v_l, v_r = prof[nb - 1][2], prof[nb][2]
    rows = (np.arange(h) + 0.5) / h
    across = (rows - (v_l + v_r) / 2) / max(v_r - v_l, 1e-9) * width          # metres from the middle per row
    on_road = (rows > v_l + 1.0 / h) & (rows < v_r - 1.0 / h)                  # not the edge lines / banks
    rng = np.random.default_rng(11 + variant)

    def surface_rows(key):
        t, tm = material_texture(key, w, h, seed=3 + variant)
        return np.frombuffer(t, np.uint8).reshape(h, w, 3).astype(float), np.frombuffer(tm, np.uint8).reshape(h, w, 3)
    # the road type (keeps the edge lines; no middle line on a non-asphalt road)
    if s["road"] != "tarmac":
        t, tm = surface_rows(s["road"])
        img[on_road] = t[on_road]
        mt[on_road] = tm[on_road]
    elif s["variety"]:
        blot = _tile_noise(w, h, 21 + variant, w / 20)
        img[on_road] *= (0.80 + 0.40 * blot[on_road])[:, :, None]
        patch = _tile_noise(w, h, 31 + variant, w / 10) > 0.78                 # repaired, darker squares-ish
        sel = patch & on_road[:, None]
        img[sel] *= 0.72
        mask = np.repeat(on_road[:, None], w, 1)
        _cracks(img, mask, rng, 18, w, h)
    if s["road"] == "tarmac":
        img[keep] = keep_px
    if s["sides"] > 0:
        side_rows = on_road & (np.abs(across) > width / 2 - float(s["sides"]))
        t, tm = surface_rows(s["side_type"])
        # a soft edge between asphalt and the sides (2 rows)
        img[side_rows] = t[side_rows]
        mt[side_rows] = tm[side_rows]
    return np.clip(img, 0, 255).astype(np.uint8).tobytes(), mt.tobytes()


# ---------------------------------------------------------------------------------------------- overlay models
def paint_shapes(m, net):
    """{key: (full squares [(x0, z0, x1, z1)], edge polygons [[(x, z)...]])} - the painted areas as SMOOTH shapes:
    marching squares over the cell strengths (samples at the cell middles), outline where the strength is 0.5;
    the sides (base cells) stay free of paint. Squares whose 4 corners are all inside are full (merged later)."""
    ps = paint_strengths(m)
    if not ps:
        return {}
    _, sides = base_cells(m, net, with_sides=True)
    out = {}
    for key in sorted(set(k for k, _ in ps.values())):
        val = {c: st for c, (k, st) in ps.items() if k == key and c not in sides}
        squares = set()
        for (i, j) in val:
            for di in (-1, 0):
                for dj in (-1, 0):
                    squares.add((i + di, j + dj))
        full, polys = [], []
        for (i, j) in squares:
            cs = ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))
            a = [val.get(c, 0.0) for c in cs]
            pos = [((ci + 0.5) * CELL, (cj + 0.5) * CELL) for ci, cj in cs]
            inside = [v >= 0.5 for v in a]
            if all(inside):
                full.append((i, j))
                continue
            if not any(inside):
                continue
            poly = []
            for k in range(4):
                n = (k + 1) % 4
                if inside[k]:
                    poly.append(pos[k])
                if inside[k] != inside[n]:
                    t = (0.5 - a[k]) / (a[n] - a[k])
                    poly.append((pos[k][0] + (pos[n][0] - pos[k][0]) * t, pos[k][1] + (pos[n][1] - pos[k][1]) * t))
            if len(poly) >= 3:
                polys.append(poly)
        # full squares -> rectangles (rows, then stacked)
        rows = {}
        for i, j in full:
            rows.setdefault(j, []).append(i)
        runs = {}
        for j, lst in rows.items():
            lst.sort()
            a = 0
            while a < len(lst):
                b = a
                while b + 1 < len(lst) and lst[b + 1] == lst[b] + 1:
                    b += 1
                runs.setdefault((lst[a], lst[b]), []).append(j)
                a = b + 1
        rects_ = []
        for (i0_, i1_), js in runs.items():
            js.sort()
            a = 0
            while a < len(js):
                b = a
                while b + 1 < len(js) and js[b + 1] == js[b] + 1:
                    b += 1
                rects_.append(((i0_ + 0.5) * CELL, (js[a] + 0.5) * CELL, (i1_ + 1.5) * CELL, (js[b] + 1.5) * CELL))
                a = b + 1
        out[key] = (rects_, polys)
    return out


def _up(a, b, c):
    """the triangle (a, b, c) of (x, y, z) points ordered to face up (cross(b - a, c - a).y > 0)"""
    cy = (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2])
    return (a, b, c) if cy > 0 else (a, c, b)


MAX_TRIS = 760             # per overlay model (3 vertices + 5-6 strip indices each)


def overlay_chunks(fe, m, net):
    """[{"key", "anchor", "verts", "strip"}] - flat textured models over the PAINTED areas, smooth outlines
    (paint_shapes; the road type and the sides are in the road texture itself), grouped by surface"""
    shapes = paint_shapes(m, net)
    gaps = side_gaps(m, net)
    if not shapes and not gaps:
        return []
    base = base_cells(m, net)
    hgt = _heights(net)
    areas, roads = net.areas(), net.drivable()

    def on_road(x, z, href):
        """... and each corner ON that level's road surface (flat triangles stepped on the ramps)"""
        best, v = -1e9, href
        for r in roads:
            d, h = areas[id(r)].surface_h(x, z, href)
            if d > -CELL * 1.5 and d > best:
                best, v = d, h
        return v
    out = []
    # openings: the road type laid over the side band where the sides are cut (as the walls)
    st = settings(m)
    if gaps:
        gtris = []
        S = float(st["sides"])
        for r in net.drivable():
            clip = net.clip(r)
            n = len(r)
            W = float(r.width)                       # the width its road texture was made for (road_texture)
            for si, side in enumerate((-1, 1)):
                fac = side_factor(net, r, si)
                e_own, e_other = r.edge(side), r.edge(-side)
                pairs = [(i, i + 1) for i in range(n - 1)] + ([(n - 1, 0)] if r.closed else [])
                for a, b in pairs:
                    if fac[a] >= 0.999 and fac[b] >= 0.999:
                        continue
                    q = []
                    for i in (a, b):
                        # the side band as the GPU stretches the texture: across the road model's REAL edges (a
                        # branch's are clipped to the main road's edge - measured in metres the band squeezed there
                        # stayed uncovered: a thin side-coloured line along the opening, user 2026-10-20)
                        ex, ez = e_own[i]
                        ox, oz = e_other[i]
                        if clip:
                            ex, ez = clip(i, ex, ez)
                            ox, oz = clip(i, ox, oz)
                        t_lo = min(S / W, 0.5)                            # the band's inner edge ...
                        t_hi = min(S * fac[i] / W, 0.5)                   # ... to where the (narrower) side begins
                        pl = (ex + (ox - ex) * t_lo, ez + (oz - ez) * t_lo)
                        ph = (ex + (ox - ex) * t_hi, ez + (oz - ez) * t_hi)
                        q.append((pl, ph))
                    (l0, h0), (l1, h1) = q
                    gtris += [(l0, l1, h1), (l0, h1, h0)]
        shapes = dict(shapes)
        shapes.setdefault(st["road"], ([], []))
        shapes["_gap"] = gtris
    for key, val in sorted(shapes.items(), key=lambda kv: kv[0]):
        if key == "_gap":
            continue
        rects_, polys = val
        tris = []
        if key == st["road"] and "_gap" in shapes:
            tris += shapes["_gap"]
        for x0, z0, x1, z1 in rects_:
            tris.append(((x0, z0), (x1, z0), (x1, z1)))
            tris.append(((x0, z0), (x1, z1), (x0, z1)))
        for poly in polys:
            for k in range(1, len(poly) - 1):
                tris.append((poly[0], poly[k], poly[k + 1]))
        # (asphalt painted onto an asphalt road needs no look of its own - the openings' asphalt does)
        if key == "tarmac":
            gap = set(map(id, shapes.get("_gap", [])))
            tris = [t for t in tris if id(t) in gap or base.get(cell_of(*t[0])) not in (None, "tarmac")]
        tris.sort(key=lambda t: (round(t[0][1] / 300), t[0][0]))
        for a0 in range(0, len(tris), MAX_TRIS):
            part = tris[a0:a0 + MAX_TRIS]
            xs = [p[0] for t in part for p in t]
            zs = [p[1] for t in part for p in t]
            cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
            anchor = (cx, fe.GROUND_Y, cz)
            verts, strip = [], []
            for t in part:
                (ax, az), (bx, bz), (qx, qz) = t
                if abs((bz - az) * (qx - ax) - (bx - ax) * (qz - az)) < 1e-6:
                    continue                                # a zero-size sliver (straight outline)
                href = hgt(*t[0])                   # which level (a bridge / the road under it) ...
                pts = _up(*[(p[0], on_road(p[0], p[1], href) - fe.TILE_TOP_BELOW + OVERLAY_LIFT, p[1]) for p in t])
                b0 = len(verts)
                for x, y, z in pts:
                    verts.append((x - cx, y, z - cz, 0.0, 1.0, 0.0, (x - cx) / TEX_M, (z - cz) / TEX_M))
                if strip:
                    strip += [strip[-1], b0]                # degenerate join
                    if len(strip) % 2:
                        strip.append(b0)                    # the new triangle starts at an even place
                strip += [b0, b0 + 1, b0 + 2]
            out.append({"key": key, "anchor": anchor, "verts": verts, "strip": strip})
    return out


def texture_salt(m, key, variant=0):
    """texture id salt of a surface texture of map m (each its own pictures)"""
    return 0x0F3C0000 | ((m["route_uid"] & 0xFF) << 8) | (SURFACES[key][1] & 0x3F) << 2 | (variant & 3)


# ---------------------------------------------------------------------------------------------- how they DRIVE
# Our maps use LA River's grip table (City.surfaceMappings = A5F91096 in racingphysics.bin): dirt there is only
# 0.8 grip with rolling friction 1 (it felt like asphalt). The build clones it as OUR table (FEEL_TABLE_UID, our
# cities point to it - the game's own tracks keep theirs) with these values per driving surface:
# (grip at speed, from mph, rolling friction, roughness, rear grip)
FEEL_SOURCE_UID = 0xA5F91096
FEEL_TABLE_UID = 0xF1D50001
# one grip table per grip level in use (the grip is per LOCATION now - an imported map keeps its maker's)
FEEL_UIDS = {"strong": 0xF1D50001, "extreme": 0xF1D50002}


def loc_feel(wld, loc):
    """the grip of a location: its own (Surface tab, or brought by an imported map), else the old all-maps one"""
    return loc.get("surface_feel") or wld.get("surface_feel", "strong")
FEEL = {
    "game": {},
    "strong": {"Dirt": (0.72, 45.0, 4.0, 1.0, 0.92), "Grass": (0.62, 40.0, 6.0, 1.2, 0.90),
               "DirtLightRough": (0.80, 45.0, 3.0, 1.4, 0.94), "DirtHeavyRough": (0.70, 40.0, 9.0, 2.0, 0.90),
               "WetSand": (0.60, 40.0, 10.0, 1.25, 0.90), "Water": (0.55, 35.0, 12.0, 0.9, 0.85),
               "WaterDeep": (1.0, 1.0, 25.0, 2.0, 1.0)},
    "extreme": {"Dirt": (0.55, 35.0, 8.0, 1.4, 0.85), "Grass": (0.45, 30.0, 10.0, 1.5, 0.82),
                "DirtLightRough": (0.65, 35.0, 6.0, 1.8, 0.88), "DirtHeavyRough": (0.55, 30.0, 16.0, 2.6, 0.82),
                "WetSand": (0.45, 30.0, 18.0, 1.6, 0.82), "Water": (0.40, 30.0, 20.0, 1.2, 0.78),
                "WaterDeep": (1.0, 1.0, 45.0, 2.5, 1.0)},
}
FEEL_NAMES = (("game", "Like the game"), ("strong", "Strong"), ("extreme", "Extreme"))


def feel_table(rp_data, feel, table_uid=None):
    """racingphysics.bin bytes with OUR grip table (a copy of LA River's, FEEL_TABLE_UID) using FEEL[feel];
    returns (data, uid) - uid None for "game" (the cities keep LA River's table)"""
    import kslf
    import kslf_write
    import dump
    import schema
    import tempfile
    import os
    vals = FEEL.get(feel) or {}
    if not vals:
        return rp_data, None
    table_uid = table_uid or FEEL_UIDS.get(feel, FEEL_TABLE_UID)
    k = kslf.Kslf(rp_data)
    o4, n4 = k.sections[4]
    src = None
    for i in range(n4):
        uid, ty, obj = struct.unpack_from("<IHH", rp_data, o4 + 8 * i)
        if uid == FEEL_SOURCE_UID:
            src = obj
    assert src is not None, "LA River's grip table not found"
    tmp = os.path.join(tempfile.gettempdir(), "fe_rp_%d.bin" % os.getpid())
    open(tmp, "wb").write(rp_data)
    names = [p["Surface"]["name"].strip() for p in dump.File(tmp).top(dump.File(tmp).O[src])["properties"]]
    _, data = kslf_write.clone_object_any(src, None, table_uid, None, data_in=rp_data)
    # the copy's index (objects are in uid order of creation: find it by its uid)
    k2 = kslf.Kslf(data)
    o4, n4 = k2.sections[4]
    new_obj = next(struct.unpack_from("<IHH", data, o4 + 8 * i)[2] for i in range(n4)
                   if struct.unpack_from("<I", data, o4 + 8 * i)[0] == table_uid)
    types = schema.load_schema(k2)
    ts = next(i for i, t in enumerate(types) if t["name"] == "RP_TrackSurface")

    def fn(ctx):
        b = next(bi for bi, (t, cnt, kind) in enumerate(ctx.rows) if t == ts and kind == 0)
        base = ctx.blocks[b]
        for e, name in enumerate(names):
            v = vals.get(name)
            if v:
                at = base + 36 * e
                struct.pack_into("<5f", ctx.data, at + 12, *v)      # GripScaleDynamic .. RearGripScale
    tmp2 = tmp + ".out"
    open(tmp, "wb").write(data)
    out = kslf_write.edit_object(tmp, tmp2, new_obj, fn)
    for f in (tmp, tmp2):
        try:
            os.remove(f)
        except OSError:
            pass
    return out, table_uid

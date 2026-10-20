"""
Seamless road: the whole road (flat middle + the TrackMania raised edges) as a few long models ("chunks") that
follow the road exactly - no straight pieces, no overlap (the old 56 m pieces could not follow bends: the edges did
not line up, and the 8 m overlaps flickered).

Every road point (one every 4 m) gives a cross-section of len(BANK) * 2 points (outer bank top on the -1 side ..
outer bank top on the +1 side) from road.edge(side, offset), at the road's height there. A chunk covers up to
CHUNK_LEN m and shares its first / last cross-section with its neighbours. Its model is made from SOURCE (one
triangle-strip mesh, 3232 vertices / 4861 indices, same glow shader as our other models) by cpmodel.make_mesh; the
actor stands at the chunk's middle point, turn 0 (model axes = world axes).

Texture: u along the road (repeats every 14 m; half floats, so u is kept within -16..16 around a multiple of 14 m
-> neighbours line up), v across = the fixed v of each profile point (build.road_profile) so the edge lines of the
texture sit on the edge vertices.
"""
import math

SOURCE = "\\models\\objects\\shared\\us\\rh_telegraphpoles.model"
DIFF, MAT = "telegraphpoles_diff", "telegraphpoles_ipr0"
CHUNK_LEN = 420.0
U_REPEAT = 14.0
BASE_UID = 0xF1A70000            # per map: BASE_UID | (route uid low byte) << 8; model = + 2k, def = + 2k + 1


EDGE_RAMP = 15.0          # m: the raised edge slopes down to nothing over this far before a fork / merge corner
DIVIDER_LIFT = 0.08       # m: lane dividers sit this much higher (their flat ends flickered on the road surface)

def uids(m, k):
    """(GameModel uid, DynamicObjDef uid) of chunk k of map m"""
    b = BASE_UID | ((m.get("route_uid", 0xFFFFFF21) & 0xFF) << 8)
    assert k < 64, "road too long for its uid block"
    return b + 2 * k, b + 2 * k + 1


def model_file(m, k):
    return "models\\objects\\flat_earth\\road_%02x_%02d.model" % (m["route_uid"] & 0xFF, k)


def _rings(fe, road, mask=None, sink=None, clip=None, ramp=None):
    """[profile point][road point] = (x, y, z) world; y above GROUND_Y.
    mask (network.wall_mask): [side -1, side +1] lists, True = no raised edge there (it folds flat onto the road
    edge: another road joins there). sink(x, z): metres to move a point down (a branch under the main road).
    ramp: [side -1, side +1] lists of 0..1 - the raised edge SLOPES DOWN (lower and narrower) towards a fork / merge
    corner: 1 = full, 0 = gone into the road edge (the user's idea, 2026-10-20: like the lane dividers' ends)"""
    bank = fe.BANK
    taper = None
    if getattr(road, "kind", "") == "divider":    # a lane divider: a ridge - top in the middle, down to the road
        top = bank[-1][1]
        bank = [(o, top - h) for o, h in bank]
        taper = getattr(road, "taper", None)      # its V-shaped ends: narrower (and lower) towards a point
    order = [(-1, o, h) for o, h in reversed(bank)] + [(1, o, h) for o, h in bank]
    out = []
    for side, o, h in order:
        e = road.edge(side, o) if o > 0 else road.edge(side)
        flat = road.edge(side)
        cut = mask[0 if side < 0 else 1] if mask else None
        ring = []
        for i in range(len(road)):
            if cut and cut[i]:
                x, z, hh = flat[i][0], flat[i][1], 0.0
            else:
                x, z, hh = e[i][0], e[i][1], h
                if ramp is not None:
                    f = ramp[0 if side < 0 else 1][i]
                    x, z, hh = flat[i][0] + (x - flat[i][0]) * f, flat[i][1] + (z - flat[i][1]) * f, hh * f
            if taper is not None:
                t = taper[i]
                cx, cz = road.centre[i]
                x, z, hh = cx + (x - cx) * t, cz + (z - cz) * t, hh * min(1.0, 0.35 + 0.65 * t)
            if clip:
                x, z = clip(i, x, z)
            y = road.height[i] - fe.TILE_TOP_BELOW + hh + (DIVIDER_LIFT if taper is not None else 0.0)
            if sink:
                y += sink(x, z)
            ring.append((x, y, z))
        out.append(ring)
    return out


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _unit(a):
    ln = math.sqrt(a[0] ** 2 + a[1] ** 2 + a[2] ** 2)
    return (a[0] / ln, a[1] / ln, a[2] / ln) if ln > 1e-9 else (0.0, 1.0, 0.0)


def chunks(fe, road, mask=None, sink=None, clip=None, bounds=None, ramp=None):
    """[{anchor (x, y, z) world, verts [(x, y, z, nx, ny, nz, u, v)] local, strip [indices], first, last}]
    bounds (network.cut_bounds): {side index: [m]} where an exact fork / merge cut of the raised edge begins / ends:
    two extra cross-sections a few cm apart go there - the edge stands at full height up to the wall crossing and
    ends in an upright face (it used to fold over a whole 4 m step: twisted slivers + small gaps at the corners)"""
    rings = _rings(fe, road, mask, sink, clip, ramp)
    P, n = len(rings), len(road)
    half = P // 2                                 # profile points 0 .. half - 1: side -1; half .. P - 1: side +1
    vs = [v for _, _, v in fe.road_profile(road.width)]
    mid = P // 2 - 1                              # the flat middle: between profile points mid and mid + 1
    # split into chunks of at most CHUNK_LEN m
    cuts = [0]
    while cuts[-1] < n - 1:
        i = cuts[-1] + 1
        while i < n - 1 and road.dist[i + 1] - road.dist[cuts[-1]] <= CHUNK_LEN:
            i += 1
        cuts.append(i)
    out = []
    for i0, i1 in zip(cuts, cuts[1:]):
        im = (i0 + i1) // 2
        ax, az = road.centre[im]
        anchor = (ax, fe.GROUND_Y, az)
        uc = U_REPEAT * round(road.dist[im] / U_REPEAT)
        idx = list(range(i0, i1 + 1))
        dists = [road.dist[i] for i in idx]
        if getattr(road, "closed", False) and i1 == n - 1:
            idx.append(0)                          # a lap track: the last chunk runs on to the first point
            dists.append(road.length)
        if mask and bounds:                        # exact cut cross-sections between the road points
            idx, dists = _with_cuts(road, idx, dists, mask, bounds)
        cap = getattr(road, "kind", "") == "divider"     # a lane divider: close both ends (a face, not a hole)
        if cap and i0 == 0:
            idx.insert(0, -1)
            dists.insert(0, road.dist[0])
        if cap and i1 == n - 1:
            idx.append(-2)
            dists.append(road.dist[n - 1])
        verts = []
        for p in range(P):
            for i, di in zip(idx, dists):
                if not isinstance(i, tuple) and i < 0:   # end face: every profile point -> the middle, at the ground
                    e = 0 if i == -1 else n - 1
                    fx, fz = road.forward(e)
                    sgn = -1 if i == -1 else 1
                    cx, cz = road.centre[e]
                    verts.append((cx - anchor[0], road.height[e] - fe.TILE_TOP_BELOW, cz - anchor[2],
                                  fx * sgn, 0.0, fz * sgn, (di - uc) / U_REPEAT, vs[p]))
                    continue
                if isinstance(i, tuple):                 # an exact cut cross-section between road points
                    ia_, ib_, tt, state = i

                    def at(q):
                        # the edge has sloped down to nothing at the corner (ramp): a plain interpolation of the
                        # rings (the road surface keeps its clipped edge - no branch surface over the main road)
                        src = rings[q]
                        (x0, y0, z0), (x1, y1, z1) = src[ia_], src[ib_]
                        return (x0 + (x1 - x0) * tt, y0 + (y1 - y0) * tt, z0 + (z1 - z0) * tt)
                    x, y, z = at(p)
                    a = _sub(at(min(p + 1, P - 1)), at(max(p - 1, 0)))
                    lng = _sub(rings[mid][ib_], rings[mid][ia_])
                    nrm = _unit(_cross(lng, a))
                    up = _cross(lng, _sub(at(mid + 1), at(mid)))
                    if up[1] < 0:
                        nrm = (-nrm[0], -nrm[1], -nrm[2])
                    verts.append((x - anchor[0], y, z - anchor[2], *nrm, (di - uc) / U_REPEAT, vs[p]))
                    continue
                x, y, z = rings[p][i]
                # normal: across x along (sign: up on the flat middle)
                a = _sub(rings[min(p + 1, P - 1)][i], rings[max(p - 1, 0)][i])
                if getattr(road, "closed", False):
                    ib, ia = (i - 1) % n, (i + 1) % n
                else:
                    ib, ia = max(i - 1, 0), min(i + 1, n - 1)
                lng = _sub(rings[mid][ia], rings[mid][ib])
                nrm = _unit(_cross(lng, a))
                up = _cross(lng, _sub(rings[mid + 1][i], rings[mid][i]))
                if up[1] < 0:
                    nrm = (-nrm[0], -nrm[1], -nrm[2])
                verts.append((x - anchor[0], y, z - anchor[2], *nrm, (di - uc) / U_REPEAT, vs[p]))
        N = len(idx)

        def V(p, j):
            return p * N + j
        # winding: the road surface (the flat middle band) must face up. Decided by the WHOLE band (area-weighted vote
        # over every quad) - one test triangle in the middle could sit where a branch is clipped / folded onto the
        # main road's edge: then the whole model came out upside down = invisible (end of a branch, user 2026-10-20)
        vote = 0.0
        for j in range(N - 1):
            for a, b, c in ((V(mid, j), V(mid + 1, j), V(mid, j + 1)), (V(mid + 1, j), V(mid + 1, j + 1), V(mid, j + 1))):
                pa, pb, pc = verts[a][:3], verts[b][:3], verts[c][:3]
                vote += _cross(_sub(pb, pa), _sub(pc, pa))[1]
        flip = vote < 0
        strip = []
        for p in range(P - 1):
            band = []
            for j in range(N):
                pair = (V(p, j), V(p + 1, j))
                band += reversed(pair) if flip else pair
            if strip:
                strip += [strip[-1], band[0]]      # degenerate join, keeps every band on an even position
            strip += band
        out.append({"anchor": anchor, "verts": verts, "strip": strip, "first": i0, "last": i1})
    return out


def _with_cuts(road, idx, dists, mask, bounds, gap=0.03):
    """idx / dists of one chunk + the exact cut cross-sections: per interval between two road points where a side's
    raised edge goes from standing to folded (or back) and an exact cut boundary lies, two entries
    (i, j, fraction, (side -1 cut, side +1 cut)) `gap` m apart around the boundary"""
    out_i, out_d = [], []
    for k in range(len(idx)):
        i, d = idx[k], dists[k]
        out_i.append(i)
        out_d.append(d)
        if k + 1 == len(idx):
            break
        j, dj = idx[k + 1], dists[k + 1]
        seg = dj - d
        if seg <= 1e-6:
            continue
        marks = []                                 # (fraction, side index)
        for si in (0, 1):
            if mask[si][i] == mask[si][j]:
                continue
            for b in bounds.get(si, ()):
                for bb in (b, b + road.length, b - road.length) if getattr(road, "closed", False) else (b,):
                    if d < bb < dj:
                        marks.append(((bb - d) / seg, si))
        if not marks:
            continue

        def state(tt):
            out = []
            for si in (0, 1):
                tb = [f for f, s in marks if s == si]
                if mask[si][i] == mask[si][j]:
                    out.append(mask[si][i])
                elif tb:
                    out.append(mask[si][i] if tt < tb[0] else mask[si][j])
                else:
                    out.append(mask[si][i] if tt < 0.5 else mask[si][j])
            return tuple(out)
        eps = min(gap / seg, 0.25)
        pts = sorted({min(max(f + s * eps, 1e-4), 1 - 1e-4) for f, _ in marks for s in (-1, 1)})
        for tt in pts:
            out_i.append((i, j, tt, state(tt)))
            out_d.append(d + seg * tt)
    return out_i, out_d


def network_chunks(fe, m, main=None):
    """[(road, its chunks)] of the map's whole network (main road, branches, lane dividers); the chunk numbers k
    (uids / model files) run on through all of them in this order"""
    import network
    net = network.get(m, main)
    out = []
    use_edges = bool(net.branches)               # a network with forks / merges: the raised edges are separate
    for r in net.roads():                        # models that follow the walls round every corner (edge_chunks)
        if use_edges and r.kind != "divider":
            n = len(r)
            out.append((r, chunks(fe, r, [[True] * n, [True] * n], net.sink(r), net.clip(r))))   # flat road only
        else:
            bounds = net.cut_bounds(r)
            out.append((r, chunks(fe, r, net.wall_mask(r), net.sink(r), net.clip(r), bounds,
                                  edge_ramp(r, net.wall_mask(r), bounds))))
    if use_edges:
        cs = edge_chunks(fe, net, main if main is not None else net.main)
        if cs:
            out.append((EdgeRoad((main if main is not None else net.main).width), cs))
    return out


# ---- CONTINUOUS RAISED EDGES (maps with branches; the user's choice 2026-10-20 "number 1") ------------------------
# The walls of every road are network.wall_runs (they end exactly where a main and a branch wall cross). Here the
# runs are joined end to start into chains that go round the forks / merges (the boundary of the drivable area,
# the road always on the LEFT), and the raised-edge profile is pulled along each chain: from the road edge (1 m
# inside the wall line) up to the top 1.2 m outside it - one wall that bends round each corner, a rounded nose at
# the V tips. Same texture rows as the road models' raised edges.
EDGE_JOIN = 0.15          # m: run ends closer than this are the same corner
EDGE_ROUND = math.radians(25.0)     # turns sharper than this get a rounded join (several cross-sections)
EDGE_CHUNK = 300          # cross-sections per model


class EdgeRoad:
    """stands in for a road in network_chunks' list: the edge models (texture of the main road's width)"""
    kind = "edge"
    closed = False

    def __init__(self, width):
        self.width = width


def edge_chains(net):
    """[([(x, z, h)...], loop)] - the wall runs of every drivable road, the road on the left, joined at shared ends"""
    runs = []
    areas = net.areas()
    for r in net.drivable():
        area = areas[id(r)]
        for side in (-1, 1):
            for run in net.wall_runs(r, side, heights=True):
                pts = []
                for x, z, h in run:
                    if not pts or (x - pts[-1][0]) ** 2 + (z - pts[-1][1]) ** 2 > 0.01:
                        pts.append((x, z, h))
                if len(pts) < 2:
                    continue
                k = len(pts) // 2
                ax, az = pts[max(k - 1, 0)][:2]
                bx, bz = pts[min(k + 1, len(pts) - 1)][:2]
                dx, dz = bx - ax, bz - az
                ln = math.hypot(dx, dz) or 1.0
                lx, lz = -dz / ln, dx / ln                       # left of travel
                mx, mz, mh = pts[k]
                if area.depth(mx + lx * 1.5, mz + lz * 1.5, mh) < area.depth(mx - lx * 1.5, mz - lz * 1.5, mh):
                    pts.reverse()                                # the road must be on the left
                runs.append(pts)

    def close(a, b):
        return (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 < EDGE_JOIN ** 2
    chains = []
    used = [False] * len(runs)
    for i0 in range(len(runs)):
        if used[i0]:
            continue
        i = i0                                   # back to the start of this chain (unless it is a loop)
        seen = {i}
        while True:
            prev = next((j for j in range(len(runs))
                         if not used[j] and j not in seen and close(runs[j][-1], runs[i][0])), None)
            if prev is None:
                break
            i = prev
            seen.add(i)
        chain = list(runs[i])
        used[i] = True
        while True:
            nxt = next((j for j in range(len(runs)) if not used[j] and close(runs[j][0], chain[-1])), None)
            if nxt is None:
                break
            chain += runs[nxt][1:]
            used[nxt] = True
        loop = len(chain) > 3 and close(chain[0], chain[-1])
        chains.append((chain[:-1] if loop else chain, loop))
    return chains


def _edge_stations(chain, loop):
    """[(x, z, h, nx, nz, along m, chain point index)] - cross-sections along a chain: n = the outward normal (right of travel), mitred
    at gentle turns, several turning ones at sharp turns (a rounded join)"""
    n = len(chain)
    out = []
    along = 0.0

    def d(a, b):
        dx, dz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dz) or 1.0
        return dx / ln, dz / ln, ln
    for i in range(n):
        p = chain[i]
        a = chain[i - 1] if (i > 0 or loop) else None
        b = chain[(i + 1) % n] if (i < n - 1 or loop) else None
        if i > 0:
            along += d(chain[i - 1], p)[2]
        din = d(a, p) if a is not None else None
        dout = d(p, b) if b is not None else None
        if din is None or dout is None:
            dd = din or dout
            out.append((p[0], p[1], p[2], dd[1], -dd[0], along, i, False))
            continue
        n_in = (din[1], -din[0])
        n_out = (dout[1], -dout[0])
        ang = math.atan2(din[0] * dout[1] - din[1] * dout[0], din[0] * dout[0] + din[1] * dout[1])
        if abs(ang) <= EDGE_ROUND:
            mx, mz = n_in[0] + n_out[0], n_in[1] + n_out[1]
            ml = math.hypot(mx, mz) or 1.0
            scale = min(1.0 / max(math.cos(ang / 2), 0.5), 2.0)
            out.append((p[0], p[1], p[2], mx / ml * scale, mz / ml * scale, along, i, False))
        else:
            steps = int(math.ceil(abs(ang) / math.radians(15)))
            a0 = math.atan2(n_in[1], n_in[0])
            for s in range(steps + 1):
                q = a0 + ang * s / steps
                # a turn to the RIGHT bends the wall's outward side inwards (a V tip): no outward part there
                out.append((p[0], p[1], p[2], math.cos(q), math.sin(q), along + s * 1e-3, i, ang < 0))
    if loop and out:
        out.append(out[0][:5] + (along + d(chain[-1], chain[0])[2], 0, out[0][7]))
    return out


def edge_chunks(fe, net, main):
    """[{anchor, verts, strip, first, last}] - the continuous raised-edge models of a network"""
    bank = fe.BANK
    nb = len(bank)
    prof = fe.road_profile(main.width)
    # across the edge: from the road edge (1 m inside the wall line) to the top; v as the road models' left raised
    # edge (outer top = prof[0], road edge = prof[nb - 1])
    profile = [(o - 1.0, h, prof[nb - 1 - j][2]) for j, (o, h) in enumerate(bank)]
    P = len(profile)
    out = []
    chains = edge_chains(net)
    # how far is the NEXT wall outward (a narrow island between two roads near a V tip)? A ray from each wall point
    # along its outward normal against every other wall segment (not the two at this point): the outward part of
    # the profile stops at half that distance - the two walls' tops meet in the island's middle instead of
    # crossing (an X).
    reach = profile[-1][0]                         # the profile's outward reach (m beyond the wall line)
    grid = {}
    for ci, (chain, loop) in enumerate(chains):
        n = len(chain)
        for i in range(n if loop else n - 1):
            a, b = chain[i], chain[(i + 1) % n]
            for gx in range(int(min(a[0], b[0]) // 4), int(max(a[0], b[0]) // 4) + 1):
                for gz in range(int(min(a[1], b[1]) // 4), int(max(a[1], b[1]) // 4) + 1):
                    grid.setdefault((gx, gz), []).append((a, b, ci, i))

    def room(ci, vi, n_pts, loop, x, z, nx, nz):
        best = 2 * reach + 1.0
        own = {(vi - 1) % n_pts if loop else vi - 1, vi}
        seen = set()
        for gx in range(int((x - 4) // 4), int((x + 4) // 4) + 1):
            for gz in range(int((z - 4) // 4), int((z + 4) // 4) + 1):
                for a, b, cj, sj in grid.get((gx, gz), ()):
                    if (cj, sj) in seen or (cj == ci and sj in own):
                        continue
                    seen.add((cj, sj))
                    ex, ez = b[0] - a[0], b[1] - a[1]
                    den = nx * ez - nz * ex
                    if abs(den) < 1e-9:
                        continue
                    wx, wz = a[0] - x, a[1] - z
                    tt = (wx * ez - wz * ex) / den          # along the ray
                    u = (wx * nz - wz * nx) / den           # along the segment
                    if tt > 0.05 and -1e-6 <= u <= 1 + 1e-6:
                        best = min(best, tt)
        return min(reach, best / 2)
    for ci, (chain, loop) in enumerate(chains):
        st = _edge_stations(chain, loop)
        if len(st) < 2:
            continue
        lim = []
        for (x, z, h, nx, nz, sa, vi, concave) in st:
            ln = math.hypot(nx, nz) or 1.0
            lim.append(0.0 if concave else room(ci, vi, len(chain), loop, x, z, nx / ln, nz / ln) / ln)
        for s0 in range(0, len(st) - 1, EDGE_CHUNK):
            part = st[s0:min(s0 + EDGE_CHUNK + 1, len(st))]
            if len(part) < 2:
                continue
            ax = sum(q[0] for q in part) / len(part)
            az = sum(q[1] for q in part) / len(part)
            anchor = (ax, fe.GROUND_Y, az)
            uc = U_REPEAT * round(part[len(part) // 2][5] / U_REPEAT)
            N = len(part)
            plim = lim[s0:s0 + len(part)]
            pos = [[(x + nx * min(lat, lm), h - fe.TILE_TOP_BELOW + hh, z + nz * min(lat, lm))
                    for (x, z, h, nx, nz, _, _, _), lm in zip(part, plim)] for lat, hh, _ in profile]
            verts = []
            for p in range(P):
                for j in range(N):
                    x, y, z = pos[p][j]
                    a = _sub(pos[min(p + 1, P - 1)][j], pos[max(p - 1, 0)][j])
                    lng = _sub(pos[0][min(j + 1, N - 1)], pos[0][max(j - 1, 0)])
                    nrm = _unit(_cross(lng, a))
                    nx, nz = part[j][3], part[j][4]
                    if nrm[1] - (nrm[0] * nx + nrm[2] * nz) < 0:     # facing up / towards the road
                        nrm = (-nrm[0], -nrm[1], -nrm[2])
                    verts.append((x - ax, y, z - az, *nrm, (part[j][5] - uc) / U_REPEAT, profile[p][2]))

            def V(p, j):
                return p * N + j
            vote = 0.0
            for p in range(P - 1):
                for j in range(N - 1):
                    A, B, C = verts[V(p, j)][:3], verts[V(p + 1, j)][:3], verts[V(p, j + 1)][:3]
                    cr = _cross(_sub(B, A), _sub(C, A))
                    nx, nz = part[j][3], part[j][4]
                    vote += cr[1] - (cr[0] * nx + cr[2] * nz)
            flip = vote < 0
            strip = []
            for p in range(P - 1):
                band = []
                for j in range(N):
                    pair = (V(p, j), V(p + 1, j))
                    band += reversed(pair) if flip else pair
                if strip:
                    strip += [strip[-1], band[0]]
                strip += band
            out.append({"anchor": anchor, "verts": verts, "strip": strip, "first": 0, "last": N - 1})
    return out


def edge_ramp(road, mask, bounds, length=EDGE_RAMP):
    """[side -1, side +1] lists of 0..1 per road point: the raised edge slopes down over `length` m to nothing at
    every exact fork / merge corner (bounds = network.cut_bounds); None when the road has no such corners"""
    if not any(bounds.get(si) for si in (0, 1)):
        return None
    out = []
    for si in (0, 1):
        bs = bounds.get(si, ())
        row = []
        for i in range(len(road)):
            if mask[si][i] or not bs:
                row.append(0.0 if mask[si][i] else 1.0)
                continue
            s = road.dist[i]
            d = min(abs(s - b) for b in bs)
            if getattr(road, "closed", False):
                d = min(d, min(road.length - abs(s - b) for b in bs))
            u = min(1.0, d / length)
            row.append(u * u * (3 - 2 * u))
        out.append(row)
    return out


def overlay_chunks(fe, m, main=None):
    """the painted surface patches' models (flat_earth/surface.py): they come after the road chunks in the uid /
    model-file numbering"""
    import network
    import surface
    return surface.overlay_chunks(fe, m, network.get(m, main or __import__("road").for_map(m)))


def model_count(fe, m, main=None):
    return sum(len(cs) for _, cs in network_chunks(fe, m, main)) + len(overlay_chunks(fe, m, main))


def texture_salt(m, ri):
    """texture id salt of road ri of map m (0 = the main road: the old salt; each road width needs its own
    pictures - the texture's edge lines sit at that width's edge)"""
    if ri == 0:
        return 0x0F1A7F00 | (m["route_uid"] & 0xFF)
    return 0x0F2B0000 | ((m["route_uid"] & 0xFF) << 8) | (ri & 0xFF)


def spots(fe, road, m):
    """tile-list spots (x, y, z, def uid, turn) of the map's road chunks (the whole network)"""
    out, k = [], 0
    for _, cs in network_chunks(fe, m, road):
        for c in cs:
            out.append((c["anchor"][0], c["anchor"][1], c["anchor"][2], uids(m, k)[1], 0.0))
            k += 1
    for c in overlay_chunks(fe, m, road):                  # painted surfaces (dirt, water ...)
        out.append((c["anchor"][0], c["anchor"][1], c["anchor"][2], uids(m, k)[1], 0.0))
        k += 1
    return out

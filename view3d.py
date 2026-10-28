"""3D view for the Flat Earth Track Designer (OpenGL inside the tkinter window: PyOpenGL + pyopengltk, both open
source; install with  python -m pip install --user PyOpenGL pyopengltk).

The scene is made by the SAME layout code the build uses (flat_earth/build.py + road_layout.py), so what you see is
where things will be in the game: road surface with its height (bridges, ramps), solid walls, start / finish strips,
power-ups (colour = type), road-block containers, obstacles, lamps, barriers, billboards, chevron panels, ships,
cranes, ferris wheel, radio mast. Objects are simple shapes at their real size, position and turn.

Mouse: left-drag = turn around, right-drag = move, wheel = zoom.  Buttons: Top view, Reset, Drive (fly along the
road from the start to the finish).  World axes: x, y up, z; drawn with z flipped so the 3D view matches the 2D map.
"""
import json
import math
import os
import sys
import time
import tkinter as tk

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [os.path.join(HERE, "flat_earth"), os.path.join(HERE, "library")]
import road_layout  # noqa: E402
from road_layout import actor_axes  # noqa: E402  (model axes of a turned / tilted object, as the build writes them)
import view3d_scatter  # noqa: E402  (Path scatter: objects along a drawn path)

try:
    from OpenGL import GL as gl
    from OpenGL import GLU as glu
    from pyopengltk import OpenGLFrame
    HAVE_GL = True
    GL_ERROR = ""
except Exception as ex:          # noqa: BLE001 - shown to the user
    HAVE_GL = False
    GL_ERROR = str(ex)
    OpenGLFrame = tk.Frame

SUN = np.array([0.45, 0.8, 0.35])
SUN = SUN / np.linalg.norm(SUN)
SUN_X, SUN_Y, SUN_Z = (float(v) for v in SUN)

MIN_RADIUS = 35.0          # as the designer's checks (road_designer.MIN_RADIUS)
MAX_GRADE = 0.12           # steeper than 12 % = a problem beacon

REAL_SHAPES = [False]      # the "Real shapes" tick: the game's objects as their own models (library/game_preview.py)
KIND_RGB = {"scenery": (.6, .66, .75), "breakable": (1, .72, .3), "movable": (.35, .8, 1), "animated": (.8, .5, 1),
            "import": (.35, .95, .55)}
TYPE_RGB = {"Shunt": (1, .25, .25), "Barge": (1, .62, .1), "Shock": (.23, .63, 1), "Mine": (1, .88, .29),
            "Bolt": (.69, .44, 1), "Nitro": (.25, .88, 1), "Shield": (.25, 1, .44), "Repair": (1, 1, 1)}


def kvec(k):
    """an object's scale: one number (all axes) or [x, y up, z] in the model's own axes"""
    return (float(k),) * 3 if isinstance(k, (int, float)) else tuple(float(v) for v in k)


def kmul(a, b):
    """two scales multiplied; one number again when all three are the same"""
    v = [x * y for x, y in zip(kvec(a), kvec(b))]
    return v[0] if max(v) - min(v) < 1e-9 else v


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


# ===================================================================================================== scene

class Mesh:
    """triangles with a colour per triangle (shaded by the sun when added), plus lines"""

    def __init__(self):
        self.v, self.c = [], []          # opaque triangles: 3 vertices / 3 colours each
        self.tv, self.tc = [], []        # transparent triangles
        self.lv, self.lc = [], []        # lines
        self.cur_rot = (0.0, 0.0)        # tilts of the object being drawn (box() uses them)
        self.owner = None                # key of the object being drawn (Solo shows only its triangles)
        self.owners = {}                 # owner key -> number; per vertex: its owner's number (-1 = none)
        self.vo, self.tvo, self.lo = [], [], []

    def _oid(self):
        return -1 if self.owner is None else self.owners.setdefault(self.owner, len(self.owners))

    def tri(self, a, b, c, col, alpha=1.0, shade=True, both=True):
        # plain floats (numpy on single 3-vectors was most of the 3D tab's start time)
        a = (float(a[0]), float(a[1]), float(a[2]))
        b = (float(b[0]), float(b[1]), float(b[2]))
        c = (float(c[0]), float(c[1]), float(c[2]))
        k = 1.0
        if shade:
            ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
            vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
            nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
            ln = math.sqrt(nx * nx + ny * ny + nz * nz)
            if ln > 1e-9:
                k = 0.45 + 0.55 * abs((nx * SUN_X + ny * SUN_Y + nz * SUN_Z) / ln)
        col4 = (col[0] * k, col[1] * k, col[2] * k, alpha)
        tv, tc, to = (self.v, self.c, self.vo) if alpha >= 1.0 else (self.tv, self.tc, self.tvo)
        tv += [a, b, c]
        tc += [col4] * 3
        to += [self._oid()] * 3

    def quad(self, a, b, c, d, col, alpha=1.0, shade=True):
        self.tri(a, b, c, col, alpha, shade)
        self.tri(a, c, d, col, alpha, shade)

    def line(self, a, b, col, alpha=1.0):
        self.lv += [a, b]
        self.lc += [(col[0], col[1], col[2], alpha)] * 2
        self.lo += [self._oid()] * 2

    def box(self, centre, size, turn, col, lift=0.0, origin="centre", k=1.0):
        """box size (sx, sy, sz) in model axes (x, y up, z); placed like an actor: local x -> world
        (cos t, 0, -sin t), local z -> (sin t, 0, cos t). origin 'centre' = centre of the bottom face; or a
        (x0, x1, z0, z1) model-space footprint. k = the actor's scale"""
        cx, cy, cz = centre
        ax, ay, az = actor_axes(turn, *self.cur_rot)       # cur_rot: the object's tilts (rx, rz), set per object
        if origin == "centre":
            x0, x1, z0, z1 = -size[0] / 2, size[0] / 2, -size[2] / 2, size[2] / 2
        else:
            x0, x1, z0, z1 = origin
        kx, ky, kz = kvec(k)
        x0, x1, z0, z1 = x0 * kx, x1 * kx, z0 * kz, z1 * kz
        y0, y1 = lift * ky, (lift + size[1]) * ky

        def w(lx, ly, lz):
            return tuple((cx, cy, cz)[i] + lx * ax[i] + ly * ay[i] + lz * az[i] for i in range(3))
        p = [w(x, y, z) for y in (y0, y1) for z in (z0, z1) for x in (x0, x1)]
        # p index: bit0 x, bit1 z, bit2 y
        for f in ((4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)):
            self.quad(*(p[i] for i in f), col)

    def arrays(self):
        def a(x, n):
            return np.ascontiguousarray(np.array(x, np.float32).reshape(-1, n)) if x else np.zeros((0, n), np.float32)
        def o(x):
            return np.array(x, np.int32)
        return {"v": a(self.v, 3), "c": a(self.c, 4), "tv": a(self.tv, 3), "tc": a(self.tc, 4),
                "lv": a(self.lv, 3), "lc": a(self.lc, 4), "vo": o(self.vo), "tvo": o(self.tvo), "lo": o(self.lo),
                "owners": dict(self.owners)}


def build_collision(m):
    """what the CAR can hit, as the build writes it into the collision floor: the ground (drivable, green), solid
    walls / boxes (red; a short white tick shows the side that pushes cars away - walls are solid from that side
    only), bridge decks (drivable, yellow)"""
    os.environ.setdefault("FE_DRY", "1")
    import build as fe
    import gen_aid
    import road_layout
    road = fe.map_road(m)
    lines = gen_aid.race_lines(road, m)
    _, info = gen_aid.build_path(road, fe.GROUND_Y, lines=lines)
    corridor = road_layout.ai_corridor(fe, road, info, m)
    _, info = gen_aid.build_path(road, fe.GROUND_Y, corridor=corridor, lines=lines)
    pieces, decks = road_layout.collision_pieces(fe, road, info, m)
    g = fe.GROUND_Y
    M = Mesh()
    x0, x1, z0, z1 = road.bounds(1500.0)
    M.quad((x0, g, z0), (x1, g, z0), (x1, g, z1), (x0, g, z1), (.1, .35, .15), shade=False)
    gx = math.floor(x0 / 100) * 100
    while gx < x1:
        M.line((gx, g + .02, z0), (gx, g + .02, z1), (.15, .45, .2))
        gx += 100
    gz = math.floor(z0 / 100) * 100
    while gz < z1:
        M.line((x0, g + .02, gz), (x1, g + .02, gz), (.15, .45, .2))
        gz += 100
    # the road outline on the ground (for orientation)
    for side in (-1, 1):
        e = road.edge(side)
        for i in range(len(road) - 1):
            M.line((e[i][0], g + .05, e[i][1]), (e[i + 1][0], g + .05, e[i + 1][1]), (.6, .7, .65))
    walls = boxes = 0
    for corners, faces in pieces:
        is_box = len(corners) == 8
        boxes += is_box
        walls += not is_box
        for (a, b, c), nrm in faces:
            pa, pb, pc = (np.array(corners[k], float) for k in (a, b, c))
            M.tri(pa, pb, pc, (.95, .18, .15), alpha=0.55)
            ctr = (pa + pb + pc) / 3
            nn = np.array(nrm, float)
            if np.linalg.norm(nn) > 0 and abs(nn[1]) < 0.5:
                M.line(ctr, ctr + nn / np.linalg.norm(nn) * 1.5, (1, 1, 1))
    for corners, faces in decks:
        for (a, b, c), nrm in faces:
            M.tri(corners[a], corners[b], corners[c], (1, .85, .15), alpha=0.8)
    path = []
    for i in range(0, len(road), 2):
        path.append((road.centre[i][0], g + road.height[i], road.centre[i][1], road.heading[i]))
    stats = "COLLISION of %s: green = drivable ground, red = solid (%d wall pieces, %d boxes; white ticks = the " \
            "side that pushes cars away), yellow = bridge decks (%d)" % (m.get("name", "map"), walls, boxes, len(decks))
    return M.arrays(), path, road.bounds(0.0), stats


def build_scene(m):
    """everything of one map (dict from tracks/world.json) as a Mesh + the drive path; raises on a broken road"""
    os.environ.setdefault("FE_DRY", "1")
    import build as fe
    import gen_aid
    import road_layout
    import world as world_mod

    road = fe.map_road(m)
    lines = gen_aid.race_lines(road, m)
    _, info = gen_aid.build_path(road, fe.GROUND_Y, lines=lines)
    corridor = road_layout.ai_corridor(fe, road, info, m)
    _, info = gen_aid.build_path(road, fe.GROUND_Y, corridor=corridor, lines=lines)
    tiles, tile_keys = road_layout.tile_spots(fe, road, info, m, keys=True)
    tile_free = road_layout.TILE_ROOM["free"]
    lamps, lamp_keys = road_layout.lamp_spots(fe, road, info, m, keys=True)
    pickups, pickup_owner = road_layout.pickup_spots(fe, road, info, m, owners=True)
    pickup_list = [dict(it) for it in road_layout.pickup_items(fe, road, info, m)]
    g = fe.GROUND_Y
    M = Mesh()
    n = len(road)
    picks = []          # what a click can select: {key, label, pos, turn, scale, box (local, unscaled), editable, scalable}

    def pick(key, pos, turn, box, k=1.0, scalable=True, rot=(0.0, 0.0), label=None):
        kind = key.split(":")[0]
        picks.append({"key": key, "label": label or OBJECT_LABELS.get(kind, kind), "pos": tuple(pos[:3]), "turn": turn,
                      "scale": k, "box": box, "editable": kind not in road_layout.FIXED_KINDS, "scalable": scalable,
                      "rot": tuple(rot)})

    placed_n = [0]
    drawn = set()
    textured = []       # imported models: [{key, image, v, c, uv}] drawn with their texture

    def placed_box(key, pos, turn, k, rot, scalable):
        """a library object: its model's box (colour = what it does), selectable; an imported model: its own
        triangles (coloured from its texture)"""
        e = road_layout.catalog().get(m["objects"][int(key[4:])].get("id"), {})
        bx = road_layout.object_box(m, key)
        if bx is None or key in drawn:                   # (an imported model: one spot per model piece)
            return
        drawn.add(key)
        x0_, x1_, y0_, y1_, z0_, z1_ = bx
        M.cur_rot = tuple(rot)
        mats = []
        if e.get("kind") == "import":
            import model_import
            mats = model_import.preview(e["model"])
        elif REAL_SHAPES[0] and e.get("model"):        # a game object: its own model once it has been read
            import game_preview
            mats = game_preview.loader().get(e["model"]) or []
        if mats:                                         # its own triangles + textures (draw() binds them)
            A = np.array(actor_axes(turn, *rot), np.float64)
            kv = np.array(kvec(k), np.float64)
            sun = np.array((SUN_X, SUN_Y, SUN_Z))
            for mt in mats:
                wpos = (mt["pos"] * kv) @ A + np.array(pos[:3], np.float64)
                if mt["nrm"] is None:                    # (game objects: flat, from each triangle)
                    t3 = wpos.reshape(-1, 3, 3)
                    wn = np.repeat(np.cross(t3[:, 1] - t3[:, 0], t3[:, 2] - t3[:, 0]), 3, axis=0)
                else:
                    wn = (mt["nrm"] / kv) @ A
                wn /= np.maximum(np.linalg.norm(wn, axis=1, keepdims=True), 1e-9)
                light = 0.45 + 0.55 * np.abs(wn @ sun) if mt["glow"] < 0.5 else np.ones(len(wn))
                col = np.c_[np.repeat(light[:, None], 3, axis=1), np.ones(len(wn))]
                textured.append({"key": mt["key"], "image": mt["image"], "v": wpos.astype(np.float32),
                                 "c": col.astype(np.float32), "uv": mt["uv"], "owner": key})
        else:
            M.box(pos, (x1_ - x0_, max(y1_ - y0_, .05), z1_ - z0_), turn, KIND_RGB.get(e.get("kind"), (.7, .7, .7)),
                  lift=y0_, origin=(x0_, x1_, z0_, z1_), k=k)
        M.cur_rot = (0.0, 0.0)
        solid = ((m.get("edits") or {}).get(key) or {}).get("solid")
        pick(key, pos, turn, bx, k, scalable=scalable, rot=rot,
             label="%s  (%s, %s / %s)%s" % (e.get("name", "?"), road_layout.KIND_TEXT.get(e.get("kind"), ""),
                                           e.get("loc", ""), e.get("cat", ""), "  - SOLID" if solid else ""))
        placed_n[0] += 1

    # ground: a big dark plain + a 100 m grid
    x0, x1, z0, z1 = road.bounds(1500.0)
    M.quad((x0, g - 0.05, z0), (x1, g - 0.05, z0), (x1, g - 0.05, z1), (x0, g - 0.05, z1), (.11, .13, .15), shade=False)
    gx = math.floor(x0 / 100) * 100
    while gx < x1:
        M.line((gx, g - .04, z0), (gx, g - .04, z1), (.2, .23, .27))
        gx += 100
    gz = math.floor(z0 / 100) * 100
    while gz < z1:
        M.line((x0, g - .04, gz), (x1, g - .04, gz), (.2, .23, .27))
        gz += 100

    # road surface between its loop-free edges, at its height; kerbs, glowing edge lines, dashed middle line
    el, er = road.edge(-1), road.edge(1)
    ys = [g + 0.03 + road.height[i] for i in range(n)]       # a little above the grid lines
    for i in range(n - 1):
        y0, y1 = ys[i], ys[i + 1]
        M.quad((*el[i][:1], y0, el[i][1]), (el[i + 1][0], y1, el[i + 1][1]), (er[i + 1][0], y1, er[i + 1][1]),
               (er[i][0], y0, er[i][1]), (.36, .37, .4))
    bank = getattr(fe, "BANK", None) if getattr(fe, "ROAD_STYLE", "") == "trackmania" else None
    import network
    import road_mesh
    net = network.get(m, road)
    for side in (-1, 1):
        e = road.edge(side)
        cut = net.wall_mask(road)[0 if side < 0 else 1]
        for i in range(n - 1):
            M.line((e[i][0], ys[i] + .05, e[i][1]), (e[i + 1][0], ys[i + 1] + .05, e[i + 1][1]), (.16, .9, 1))
        if bank:                                     # TrackMania: the edge curves up into a low (dark) wall
            rings = [(road.edge(side, o) if o > 0 else e, hgt) for o, hgt in bank]
            for (r0, h0), (r1, h1) in zip(rings, rings[1:]):
                for i in range(n - 1):
                    if cut[i] or cut[i + 1]:
                        continue                     # a branch joins here: no raised edge
                    M.quad((r0[i][0], ys[i] + h0, r0[i][1]), (r0[i + 1][0], ys[i + 1] + h0, r0[i + 1][1]),
                           (r1[i + 1][0], ys[i + 1] + h1, r1[i + 1][1]), (r1[i][0], ys[i] + h1, r1[i][1]),
                           tuple(c / 255 for c in getattr(fe, "BANK_COLOUR", (225, 228, 235))))
            top, ht = rings[-1]
            for i in range(n - 1):
                if cut[i] or cut[i + 1]:
                    continue
                M.line((top[i][0], ys[i] + ht + .03, top[i][1]), (top[i + 1][0], ys[i + 1] + ht + .03, top[i + 1][1]),
                       (.2, .5, 1))
        else:                                        # flat style: red / white kerbs
            k1 = road.edge(side, 1.0)
            for i in range(n - 1):
                col = (.85, .1, .1) if (i // 2) % 2 else (.95, .95, .95)
                M.quad((e[i][0], ys[i] + .02, e[i][1]), (e[i + 1][0], ys[i + 1] + .02, e[i + 1][1]),
                       (k1[i + 1][0], ys[i + 1] + .02, k1[i + 1][1]), (k1[i][0], ys[i] + .02, k1[i][1]), col, shade=False)
    for i in range(0, n - 1, 4):
        a, b = road.centre[i], road.centre[min(i + 2, n - 1)]
        M.line((a[0], ys[i] + .06, a[1]), (b[0], ys[min(i + 2, n - 1)] + .06, b[1]), (.9, .9, .9))
    # BRANCHES and lane DIVIDERS: their surface + raised edges exactly as the build makes them (road_mesh rings)
    bank_rgb = tuple(c / 255 for c in getattr(fe, "BANK_COLOUR", (225, 228, 235)))
    for r in net.roads()[1:]:
        if not bank:
            break
        rings = road_mesh._rings(fe, r, net.wall_mask(r), net.sink(r), net.clip(r))
        P, nr = len(rings), len(r)
        mid = P // 2 - 1
        for p in range(P - 1):
            col = (.36, .37, .4) if p == mid else bank_rgb
            for i in range(nr - 1):
                a0, a1, b0, b1 = rings[p][i], rings[p][i + 1], rings[p + 1][i + 1], rings[p + 1][i]
                if p != mid and abs(a0[1] - b1[1]) < 1e-3 and abs(a1[1] - b0[1]) < 1e-3 and \
                        abs(a0[0] - b1[0]) + abs(a0[2] - b1[2]) < 1e-3:
                    continue                         # a cut edge (folded flat)
                M.quad((a0[0], g + a0[1] + fe.TILE_TOP_BELOW + .03, a0[2]), (a1[0], g + a1[1] + fe.TILE_TOP_BELOW + .03, a1[2]),
                       (b0[0], g + b0[1] + fe.TILE_TOP_BELOW + .03, b0[2]), (b1[0], g + b1[1] + fe.TILE_TOP_BELOW + .03, b1[2]), col)
        if r.kind == "divider":                       # its two end faces
            for e in (0, nr - 1):
                cx, cz = r.centre[e]
                bottom = (cx, g + r.height[e] + .03, cz)
                for p in range(P - 1):
                    a0, b0 = rings[p][e], rings[p + 1][e]
                    M.tri((a0[0], g + a0[1] + fe.TILE_TOP_BELOW + .03, a0[2]),
                          (b0[0], g + b0[1] + fe.TILE_TOP_BELOW + .03, b0[2]), bottom, bank_rgb)
        if r.kind == "branch":
            for side in (-1, 1):
                ee = r.edge(side)
                for i in range(nr - 1):
                    M.line((ee[i][0], g + r.height[i] + .08, ee[i][1]), (ee[i + 1][0], g + r.height[i + 1] + .08, ee[i + 1][1]),
                           (.16, .9, 1))
    # SURFACES (Surface tab): road type, sides, painted shapes - smooth, as the build makes them
    import surface as surface_mod
    if not surface_mod.is_default(m):
        st = surface_mod.settings(m)

        def strip(rr, lo_of, hi_of, col, lift, cut=None):
            for i in range(len(rr) - 1):
                if cut and (cut[i] or cut[i + 1]):
                    continue                             # an opening (the wall is cut there too)
                q = []
                for k, f in ((i, lo_of), (i + 1, lo_of), (i + 1, hi_of), (i, hi_of)):
                    cx, cz = rr.centre[k]
                    sx, sz = rr.side(k)
                    lat = f(rr, k)
                    q.append((cx + sx * lat, g + rr.height[k] + lift, cz + sz * lat))
                M.quad(q[0], q[1], q[2], q[3], col)
        for rr in net.drivable():
            if st["road"] != "tarmac":
                strip(rr, lambda r_, k: -r_.width_at[k] / 2, lambda r_, k: r_.width_at[k] / 2,
                      tuple(c / 255 for c in surface_mod.SURFACES[st["road"]][3]), .06)
            if st["sides"] > 0:
                sw = float(st["sides"])
                col = tuple(c / 255 for c in surface_mod.SURFACES[st["side_type"]][3])
                mask = net.wall_mask(rr)
                f0, f1 = surface_mod.side_factor(net, rr, 0), surface_mod.side_factor(net, rr, 1)
                strip(rr, lambda r_, k: -r_.width_at[k] / 2, lambda r_, k: -r_.width_at[k] / 2 + sw * f0[k], col, .08,
                      mask[0])
                strip(rr, lambda r_, k: r_.width_at[k] / 2 - sw * f1[k], lambda r_, k: r_.width_at[k] / 2, col, .08,
                      mask[1])
        for ch in surface_mod.overlay_chunks(fe, m, net):
            col = tuple(c / 255 for c in surface_mod.SURFACES[ch["key"]][3])
            ax, ay, az = ch["anchor"]
            vs = ch["verts"]
            for t in range(0, len(vs), 3):
                pa, pb, pc = [(vs[t + q][0] + ax, ay + vs[t + q][1] + fe.TILE_TOP_BELOW + .03, vs[t + q][2] + az)
                              for q in range(3)]
                M.tri(pa, pb, pc, col)
    # bridges: deck sides + pillars under raised parts
    for i in range(n - 1):
        if road.height[i] > 0.5 or road.height[i + 1] > 0.5:
            for e in (el, er):
                M.quad((e[i][0], ys[i] - 1.2, e[i][1]), (e[i + 1][0], ys[i + 1] - 1.2, e[i + 1][1]),
                       (e[i + 1][0], ys[i + 1], e[i + 1][1]), (e[i][0], ys[i], e[i][1]), (.5, .5, .55))
        if road.height[i] > 3 and i % 6 == 0:
            for lat in (-road.width_at[i] / 3, road.width_at[i] / 3):
                x, z, a = road.at(road.dist[i], lat)
                M.box((x, g, z), (2.0, ys[i] - g - 1.2, 2.0), a, (.55, .55, .6))

    # the tile list: road pieces (the ribbon above shows them), line strips, containers, chevrons, big scenery
    defs = {b[4]: b[0] for b in fe.BIG}
    L, H, W = fe.CONTAINER
    for t, key in zip(tiles, tile_keys):
        x, y, z, uid, turn = t[:5]
        k = t[6] if len(t) > 6 else 1.0
        rot = tuple(t[7]) if len(t) > 7 else (0.0, 0.0)
        M.cur_rot = rot
        M.owner = key
        box = PICK_BOX.get(key.split(":")[0])
        if box:
            pick(key, (x, y, z), turn, box, k, rot=rot)
        if key.startswith("obj:"):                       # the user's object from the library: its model's box
            placed_box(key, (x, y, z), turn, k, rot, True)
            continue
        A = actor_axes(turn, *rot)

        def to_world(lx, ly, lz=0.0):                           # model point -> world (turn + tilts)
            return tuple((x, y, z)[i] + lx * A[0][i] + ly * A[1][i] + lz * A[2][i] for i in range(3))
        if uid == fe.LINE_DEF_UID:                       # checkered strip: LINE_W along the road x LINE_LEN across
            c, s = math.cos(turn), math.sin(turn)
            cells = 12
            ln = fe.LINE_LEN * kvec(k)[2]                # (stretched across a road wider than 60 m)
            for k in range(cells):
                for j in range(2):
                    lz0 = -ln / 2 + ln * k / cells
                    lz1 = lz0 + ln / cells
                    lx0 = -fe.LINE_W / 2 + fe.LINE_W * j / 2
                    lx1 = lx0 + fe.LINE_W / 2
                    q = [(x + lx * c + lz * s, y + .08, z - lx * s + lz * c) for lx, lz in
                         ((lx0, lz0), (lx1, lz0), (lx1, lz1), (lx0, lz1))]
                    M.quad(*q, (.95, .95, .95) if (k + j) % 2 else (.05, .05, .05), shade=False)
        elif uid in (fe.TEST_DEF_UID, fe.BLOCK_DEF_UID):  # containers (model corner origin: x -13.7..0, z 0..2.4)
            M.box((x, y, z), (L, H, W), turn, (.75, .12, .1) if uid == fe.BLOCK_DEF_UID else (.85, .2, .15),
                  origin=(-L, 0.0, 0.0, W), k=k)
        elif uid == fe.CHEVRON_DEF_UID:                   # vertical panel: local x along, y up; arrows point +x
            hl, h = fe.CHEVRON_LEN / 2 * kvec(k)[0], fe.CHEVRON_H * kvec(k)[1]
            w = to_world
            M.quad(w(-hl, 0), w(hl, 0), w(hl, h), w(-hl, h), (.08, .1, .14), shade=False)
            for k_ in range(fe.CHEVRON_COUNT):
                cx0 = -hl + 2 * hl * (k_ + .25) / fe.CHEVRON_COUNT
                cx1 = -hl + 2 * hl * (k_ + .75) / fe.CHEVRON_COUNT
                M.tri(w(cx0, h * .15), w(cx1, h * .5), w(cx0, h * .85), (1, 1, 1), shade=False)
        elif uid in defs:
            name = defs[uid]
            if name in ("tanker", "cruise"):                # model long axis = z
                ln = 333.0 if name == "tanker" else 263.0
                M.box((x, y, z), (36, 18, ln), turn, (.3, .32, .36) if name == "tanker" else (.85, .85, .9),
                      lift=-7.8 if name == "cruise" else 0.0, k=k)
                M.box((x, y, z), (30, 26, 40), turn, (.8, .8, .8), lift=10.0 if name == "tanker" else 2.0,
                      origin=(-15, 15, ln / 2 - 60, ln / 2 - 20), k=k)
            elif name.startswith("arch_"):                   # the start / finish arch: pillars + the sign beam
                for i, (x0, x1, z0, z1, y0, y1) in enumerate(fe.arch_parts()):
                    M.box((x, y, z), (x1 - x0, y1 - y0, z1 - z0), turn, (.08, .12, .2) if i == 2 else (.3, .32, .36),
                          lift=y0, origin=(x0, x1, z0, z1), k=k)
            elif name.startswith("tower_"):                  # our skyscrapers: their boxes, facade colour
                col = tuple(c / 255 for c in fe.TOWER_STYLE.get(name, {}).get("frame", (40, 51, 69)))
                for x0, x1, z0, z1, y0, y1 in fe.tower_parts(name):
                    M.box((x, y, z), (x1 - x0, y1 - y0, z1 - z0), turn, col, lift=y0, origin=(x0, x1, z0, z1), k=k)
            elif name == "crane":                           # gantry: legs at both ends of model x, a beam on top
                for lx in (-60.0, 56.0):
                    M.box((x, y, z), (4, 71, 26), turn, (.15, .35, .75), origin=(lx, lx + 4, -13, 13), k=k)
                M.box((x, y, z), (132, 6, 10), turn, (.15, .35, .75), lift=62, k=k)
            elif name == "ferris":
                kx, ky, _ = kvec(k)
                hub = to_world(0, 26 * ky)
                pts = [to_world(22 * kx * math.cos(q), 26 * ky + 22 * ky * math.sin(q)) for q in np.linspace(0, 2 * math.pi, 33)]
                for a, b in zip(pts, pts[1:]):
                    M.line(a, b, (1, .85, .4))
                    M.line(hub, a, (1, .85, .4), .5)
                M.box((x, y, z), (3, 26, 3), turn, (.7, .7, .7), k=k)
            elif name == "radio":
                for j in range(6):
                    M.box((x, y, z), (6, 10, 6), turn, (.85, .15, .15) if j % 2 else (.95, .95, .95), lift=10 * j, k=k)
        # (road pieces: shown by the road surface)

    # the lamp list: lamps, barriers, billboards, line signs, obstacles
    R = {v[1]: k for k, v in fe.ROADSIDE.items()}
    for sp, key in zip(lamps, lamp_keys):
        x, y, z, turn = sp[:4]
        kind = R.get(sp[4]) if len(sp) > 4 else "lamp"
        M.cur_rot = tuple(sp[5]) if len(sp) > 5 else (0.0, 0.0)
        M.owner = key
        if key.startswith("obj:"):                       # the user's breakable object from the library
            placed_box(key, (x, y, z), turn, 1.0, M.cur_rot, False)
            continue
        if kind in PICK_BOX:
            pick(key, (x, y, z), turn, PICK_BOX[kind], scalable=False, rot=M.cur_rot)
        if kind == "lamp":
            M.box((x, y, z), (.4, 10, .4), turn, (.6, .62, .65))
            M.box((x, y, z), (1.2, .5, 1.2), turn, (1, .9, .55), lift=10)
        elif kind == "barrier":
            M.box((x, y, z), (3.6, 1.0, .8), turn, (.62, .62, .6))
        elif kind == "billboard":
            for lx in (-3.0, 3.0):
                M.box((x, y, z), (.3, 4, .3), turn, (.4, .4, .4), origin=(lx - .15, lx + .15, -.15, .15))
            M.box((x, y, z), (8, 4, .4), turn, (.95, .55, .1), lift=4)
        elif kind == "flashing":
            M.box((x, y, z), (1.6, 2.2, .3), turn, (1, .85, .1))
        elif kind == "light":
            M.box((x, y, z), (.5, 6, .5), turn, (.5, .5, .5))
            M.box((x, y, z), (1.5, .8, .6), turn, (1, 1, .9), lift=6)
        elif kind == "barrel":
            M.box((x, y, z), (.8, 1.0, .8), turn, (1, .45, .05))
        elif kind == "rwbarrier":
            M.box((x, y, z), (2.4, 1.1, .4), turn, (.95, .2, .2))
        elif kind == "cone":
            M.box((x, y, z), (.5, .8, .5), turn, (1, .5, .05))

    M.cur_rot = (0.0, 0.0)

    # power-ups: small flat rectangles just above the road, colour = type (as in the 2D view)
    name_of = {v: k for k, v in world_mod.PICKUP_TYPES.items()}
    lane = {}
    for (x, y, z, a, t), owner in zip(pickups, pickup_owner):
        col = TYPE_RGB.get(name_of.get(t, ""), (.8, .8, .8))
        M.owner = "pickup:%d" % owner                    # (Solo: the whole row)
        M.box((x, y + .3, z), (1.6, .12, 2.6), a, col)
        it = pickup_list[owner]
        row = it.get("kind") != "single"
        lane[owner] = lane.get(owner, -1) + 1
        picks.append({"key": "pickup:%d:%d" % (owner, lane[owner]), "pos": (x, y + .3, z), "turn": a, "scale": 1.0,
                      "rot": (0.0, 0.0), "box": (-.8, .8, -.3, .5, -1.3, 1.3), "editable": True, "scalable": False,
                      "pickup": owner, "row": row, "ptype": it.get("type"),
                      "label": "Power-up %s: %s  (%s %d)" % ("row" if row else "single", it.get("type") or
                                                             ("mixed" if row else name_of.get(t, "?")),
                                                             "row" if row else "item", owner + 1)})

    M.owner = None

    # the road's control points (as in the 2D view: first green, last white, others orange) - selectable, G moves
    # them (Z = height); drawn as dots on top by the view
    pts = m.get("points") or []
    for i, pt in enumerate(pts):
        pd = pt if isinstance(pt, dict) else {"x": pt[0], "z": pt[1]}
        hgt = float(pd.get("h") or 0.0)
        px, pz = float(pd["x"]), float(pd["z"])
        if hgt > 0.05:
            M.line((px, g, pz), (px, g + hgt, pz), (1, .7, .15), .6)
        picks.append({"key": "point:%d" % i, "label": "Track point %d of %d" % (i + 1, len(pts)),
                      "pos": (px, g + hgt, pz), "turn": 0.0, "scale": 1.0, "rot": (0.0, 0.0), "box": (-3, 3, 0, 1, -3, 3),
                      "editable": True, "scalable": False, "point": i, "width": float(pd.get("w") or m.get("width", 60)),
                      "col": (.21, .88, .35) if i == 0 else ((1, 1, 1) if i == len(pts) - 1 else (1, .69, .13))})

    # problems: red beacons (tight turns, crossings without a bridge, too steep) - as the designer's checks
    problems = 0
    marked = []

    def beacon(x, y, z):
        if any(math.hypot(x - px, z - pz) < 40 for px, pz in marked):
            return
        marked.append((x, z))
        M.box((x, y, z), (3, 25, 3), 0.0, (1, .15, .15))
        M.box((x, y, z), (7, 3, 7), 0.0, (1, .3, .3), lift=25)
    for i in range(1, n - 1):
        da = (road.heading[i + 1] - road.heading[i - 1] + math.pi) % (2 * math.pi) - math.pi
        if abs(da) > 1e-6 and 2 * 4.0 / abs(da) < MIN_RADIUS:
            beacon(road.centre[i][0], ys[i], road.centre[i][1])
            problems += 1
    for j, i in road.unbridged_crossings():
        beacon(road.centre[j][0], ys[j], road.centre[j][1])
        problems += 1
    for i in range(n - 1):
        if abs(road.height[i + 1] - road.height[i]) / 4.0 > MAX_GRADE:
            beacon(road.centre[i][0], ys[i], road.centre[i][1])
            problems += 1

    # drive path: start line -> finish line (centre, a bit above the road)
    s0, s1 = road_layout._line_s(road, info)
    path = []
    s = max(s0 - 60, 0)
    while s <= min(s1 + 40, road.length):
        x, z, a = road.at(s)
        path.append((x, g + road.h_at(s), z, a))
        s += 4.0
    stats = "%s: %.0f m, %d power-ups, %d bridge(s), %s" % (
        m.get("name", "map"), road.length, len(pickups), len(road.crossings),
        "no problems" if not problems else "%d PROBLEM SPOT(S) = red beacons" % len(marked))
    nobj = len(m.get("objects") or [])
    hidden = sum(1 for i in range(nobj) if ((m.get("edits") or {}).get("obj:%d" % i) or {}).get("hidden"))
    if nobj:
        stats += ", %d placed object(s)" % placed_n[0]
        if placed_n[0] + hidden < nobj:
            stats += " - %d DID NOT FIT (the game's object lists are full)" % (nobj - placed_n[0] - hidden)
    arrays = M.arrays()
    arrays["textured"] = textured
    arrays["picks"] = picks
    arrays["ground"] = g
    arrays["tile_free"] = tile_free          # places left for placed objects (each piece of an imported model = 1)
    arrays["pickups"] = pickup_list          # the list the power-ups came from (edits send a changed copy back)
    arrays["auto_pickups"] = m.get("pickups") is None
    return arrays, path, road.bounds(0.0), stats


# what each kind of object is (shown when selected) and its box for clicking (model space, before scaling):
# (x0, x1, y0, y1, z0, z1) - as drawn above
OBJECT_LABELS = {
    "tanker": "Tanker ship (LA docks, 333 m) - look only", "cruise": "Cruise ship (263 m) - look only",
    "crane": "Gantry crane (132 m) - look only", "ferris": "Ferris wheel - look only",
    "radio": "Radio mast - look only", "chevron": "Chevron panel (bend arrows) - look only",
    "line": "Start / finish strip (fixed: part of the road)", "linebox": "Container at the line (fixed)",
    "block": "Road-block container (fixed: obstacles)",
    "lamp": "Lamp post - can be knocked down", "barrier": "Concrete barrier - can be knocked down",
    "billboard": "Billboard - can be knocked down", "flashing": "Flashing sign - can be knocked down",
    "light": "Standing light - can be knocked down", "barrel": "Barrel - can be knocked down",
    "rwbarrier": "Road-works barrier - can be knocked down", "cone": "Cone - can be knocked down",
}
PICK_BOX = road_layout.OBJECT_BOX       # (also what "Solid" turns into a wall box)
MULTI = "__group__"                     # the selection key while several objects are selected (View3D._multi_pick)


def pick_axes(p, dturn=0.0, drot=(0.0, 0.0)):
    return actor_axes(p["turn"] + dturn, p["rot"][0] + drot[0], p["rot"][1] + drot[1])


def box_corners(p, delta=(0, 0, 0), dturn=0.0, dscale=1.0, drot=(0.0, 0.0)):
    """the 8 world corners of pick p's box (with a preview move / turn / scale / tilt)"""
    x0, x1, y0, y1, z0, z1 = p["box"]
    kx, ky, kz = kvec(kmul(p["scale"], dscale))
    A = pick_axes(p, dturn, drot)
    o = [p["pos"][i] + delta[i] for i in range(3)]
    return [tuple(o[i] + lx * kx * A[0][i] + ly * ky * A[1][i] + lz * kz * A[2][i] for i in range(3))
            for ly in (y0, y1) for lz in (z0, z1) for lx in (x0, x1)]


def ray_hit(p, origin, direction):
    """distance along the ray to pick p's box (None = missed). Small things get a bigger box (easier to click)."""
    x0, x1, y0, y1, z0, z1 = p["box"]
    kx, ky, kz = kvec(p["scale"])
    grow = 0.8
    lo = [min(x0 * kx, -grow), y0 * ky - grow * 0.5, min(z0 * kz, -grow)]
    hi = [max(x1 * kx, grow), y1 * ky + grow * 0.5, max(z1 * kz, grow)]
    A = pick_axes(p)
    rel = [origin[i] - p["pos"][i] for i in range(3)]
    # into the model's axes (they are unit and at right angles: a dot product each)
    o = tuple(sum(rel[i] * A[j][i] for i in range(3)) for j in range(3))
    d = tuple(sum(direction[i] * A[j][i] for i in range(3)) for j in range(3))
    t0, t1 = 0.0, 1e12
    for i in range(3):
        if abs(d[i]) < 1e-12:
            if not lo[i] <= o[i] <= hi[i]:
                return None
            continue
        a, b = (lo[i] - o[i]) / d[i], (hi[i] - o[i]) / d[i]
        if a > b:
            a, b = b, a
        t0, t1 = max(t0, a), min(t1, b)
        if t0 > t1:
            return None
    return t0


def _view_setting(name, default=None, save=False):
    """3D tab settings kept between starts (<data>/view3d.json); save=True stores default as the new value"""
    import paths
    f = os.path.join(paths.DATA, "view3d.json")
    try:
        d = json.load(open(f))
    except (OSError, ValueError):
        d = {}
    if save:
        d[name] = default
        try:
            json.dump(d, open(f, "w"))
        except OSError:
            pass
        return default
    return d.get(name, default)


# ===================================================================================================== view

class GLView(OpenGLFrame):
    def __init__(self, master, owner, **kw):
        super().__init__(master, **kw)
        self.owner = owner

    def initgl(self):
        gl.glClearColor(.07, .09, .12, 1)
        gl.glEnable(gl.GL_DEPTH_TEST)
        gl.glEnable(gl.GL_BLEND)
        gl.glBlendFunc(gl.GL_SRC_ALPHA, gl.GL_ONE_MINUS_SRC_ALPHA)
        gl.glEnable(gl.GL_LINE_SMOOTH)

    def redraw(self):
        self.owner.draw(self.width, self.height)


class View3D(view3d_scatter.ScatterTools, tk.Frame):
    def __init__(self, master, bg="#16191e", fg="#d8dee6", panel="#20242b"):
        super().__init__(master, bg=bg)
        self.scene = None
        self.path = []
        self.drive = None
        self.yaw, self.pitch, self.dist = -0.6, 0.55, 2500.0
        self.target = [0.0, 0.0, 0.0]
        self.drag = None
        self.ortho = False
        self.mode = "3d"
        self.m = None              # the map shown (its "edits" = the user's object changes)
        self.sel = None            # key of the selected object ("tanker:2")
        self.multi = []            # several selected (Shift / Ctrl+click in the list, Shift+click in the view): keys
        self.op = None             # G / R / S in progress: {kind, axis, start mouse, ...}
        self.on_edit = None        # fn(new edits dict): the designer stores it (undo, save) and rebuilds
        self.on_point = None       # fn(point index, {x, z, h, w}): the designer moves a track point
        self.on_pickups = None     # fn(new power-up list): the designer stores it
        self.on_objects = None     # fn(new object list, new edits): the designer stores them (library objects)
        self.placing = None        # library entry being placed (click in the view = one more of it)
        self._mats = None          # projection x modelview of the last frame (clicks -> rays)
        bar = tk.Frame(self, bg=panel)
        bar.pack(side="top", fill="x")
        for txt, cmd in (("⬇ Top view", self.top), ("⛶ Reset", self.reset), ("▶ Drive", self.start_drive),
                         ("■ Stop", self.stop_drive)):
            tk.Button(bar, text=txt, command=cmd, bg=panel, fg=fg, font=("Segoe UI Symbol", 9)).pack(side="left", padx=2,
                                                                                                    pady=2)
        self.real_var = tk.BooleanVar(value=_view_setting("real_shapes", False))
        REAL_SHAPES[0] = self.real_var.get()
        if REAL_SHAPES[0]:                                  # (on from last time: watch the background reader)
            self.after(1500, self._poll_shapes)
        self.info = tk.Label(bar, text="", bg=panel, fg="#9aa8b8")
        self.info.pack(side="left", padx=8)
        tk.Label(bar, text="click = select | G move  R turn  S scale  X hide | middle-drag turn view | Shift+middle "
                           "move view | wheel zoom | Numpad 7/1/3/5 | Home all", bg=panel, fg="#6f7c8a").pack(side="right",
                                                                                                    padx=8)
        # START CAMERA (the fly-by before the countdown, flat_earth/intro_cam.py): the user's own views per shot
        # (Add view = this view's camera), or the automatic shots when a shot has none
        self.top_bar = bar
        self.on_start_cams = None  # fn(new m["start_cams"] dict): the designer stores it (undo, save)
        self.cam_anim = None       # (start time, frames) while "Play" shows the start camera
        cb = self.cam_bar = tk.Frame(self, bg=panel)
        tk.Label(cb, text="START CAMERA", bg=panel, fg="#7fd0ff", font=("Segoe UI", 9, "bold")).pack(side="left",
                                                                                                padx=(6, 4))
        self.cam_shot = tk.StringVar(value="vista")
        for val, txt in (("vista", "Fly-in"), ("pack", "Grid shot")):
            tk.Radiobutton(cb, text=txt, variable=self.cam_shot, value=val, indicatoron=False, bg=panel, fg=fg,
                           command=self._cam_info, font=("Segoe UI", 9)).pack(side="left", padx=1, pady=2)
        for txt, cmd in (("＋ Add view", self.cam_add), ("－ Remove last", self.cam_remove),
                         ("▶ Play", self.cam_play), ("↺ Auto", self.cam_auto)):
            tk.Button(cb, text=txt, command=cmd, bg=panel, fg=fg, font=("Segoe UI Symbol", 9)).pack(side="left",
                                                                                                   padx=2, pady=2)
        self.cam_label = tk.Label(cb, text="", bg=panel, fg="#9aa8b8", font=("Segoe UI", 9))
        self.cam_label.pack(side="left", padx=(16, 8))
        cb.pack(side="top", fill="x")
        # shown while an object is selected: Move / Rotate / Scale (which handles are drawn on it), Hide, Reset
        sb = "#2a2f1a"
        self.sel_frame = tk.Frame(self, bg=sb)
        self.gizmo = tk.StringVar(value="move")
        for val, txt in (("move", "✥ Move (G)"), ("rotate", "↻ Rotate (R)"), ("scale", "⤢ Scale (S)")):
            tk.Radiobutton(self.sel_frame, text=txt, variable=self.gizmo, value=val, indicatoron=False, bg="#3a4228",
                           fg="white", selectcolor="#c08a1a", activebackground="#4a5430", padx=8, pady=2,
                           command=self.refresh, font=("Segoe UI Symbol", 9, "bold")).pack(side="left", padx=1, pady=2)
        self.solo = None           # Solo: owner key of the only object drawn ("obj:3", "pickup:2"), or None
        self.solo_btn = tk.Button(self.sel_frame, text="◎ Solo (/)", command=lambda: self.toggle_solo(),
                                  bg="#3a3428", fg="white", font=("Segoe UI Symbol", 9))
        self.solo_btn.pack(side="left", padx=3)
        for txt, cmd in (("✖ Hide (X)", lambda: self._hide()),
                         ("↺ Reset object", lambda: self._reset_object())):
            tk.Button(self.sel_frame, text=txt, command=cmd, bg="#3a3428", fg="white",
                      font=("Segoe UI Symbol", 9)).pack(side="left", padx=3)
        self.sel_bar = tk.Label(self.sel_frame, text="", bg=sb, fg="#f0e080", anchor="w", font=("Segoe UI", 9))
        self.sel_bar.pack(side="left", fill="x", expand=True, padx=6)
        self._hot = None           # handle under the mouse (drawn brighter)
        self.panel = tk.Frame(self, bg="#20242b", highlightbackground="#4a5060", highlightthickness=1)
        self._panel_entries = {}
        if not HAVE_GL:
            tk.Label(self, text="3D view needs two free packages. Open a command window and run:\n\n"
                                "python -m pip install --user PyOpenGL pyopengltk\n\nthen restart the designer.\n\n(%s)"
                                % GL_ERROR, bg=bg, fg=fg, font=("Segoe UI", 11), justify="left").pack(expand=True)
            self.gl = None
            return
        self.lib = self._build_library(self, panel, fg)      # object library side bar (3D mode only)
        self.outl = self._build_outliner(self, panel, fg)    # every object of the map (right side bar)
        self.gl = GLView(self, self, width=900, height=600)
        self.gl.pack(fill="both", expand=True)
        self.gl.animate = 0
        g = self.gl
        # Blender-style: middle-drag orbit, Shift+middle pan, Ctrl+middle zoom, wheel zoom; Alt+left = middle
        # (laptops); numpad 1 / 3 / 7 front / side / top (Ctrl: opposite), 5 flat (orthographic), Home = all
        g.bind("<ButtonPress-2>", self._mid_press)
        g.bind("<B2-Motion>", self._motion)
        g.bind("<ButtonRelease-2>", lambda e: self._release())
        g.bind("<Alt-ButtonPress-1>", self._mid_press)
        g.bind("<Alt-B1-Motion>", self._motion)
        g.bind("<ButtonRelease-1>", self._left_release)
        g.bind("<ButtonPress-1>", self._left_press)
        g.bind("<B1-Motion>", self._hover_motion)          # dragging a handle
        g.bind("<ButtonPress-3>", lambda e: (self._sc_draw_toggle(False) if self._sc_drawing() else
                                             self._stop_placing() if self.placing else self._cancel_op()) or "break")
        g.bind("<Motion>", self._hover_motion)
        # objects (Blender keys): G move, R turn, S scale; during one: X / Y = only along world x / z, Z = up-down,
        # click / Enter = OK, right-click / Esc = cancel. X / Delete = hide, Alt+G / R / S = undo that change,
        # Alt+H = show all hidden objects
        for key, fn in (("<g>", lambda e: self._key_op("move")), ("<r>", lambda e: self._key_op("turn")),
                        ("<s>", lambda e: self._key_op("scale")),
                        ("<x>", lambda e: self._axis_or_hide("x")), ("<y>", lambda e: self._axis("y")),
                        ("<z>", lambda e: self._axis("z")),
                        ("<Delete>", lambda e: self.delete_selection()), ("<Return>", lambda e: self._confirm_op()),
                        ("<KP_Enter>", lambda e: self._confirm_op()), ("<Escape>", lambda e: self._escape()),
                        ("<Alt-g>", lambda e: self._clear("move")), ("<Alt-r>", lambda e: self._clear("turn")),
                        ("<Alt-s>", lambda e: self._clear("scale")), ("<Alt-h>", lambda e: self._unhide_all()),
                        ("<slash>", lambda e: self.toggle_solo()), ("<KP_Divide>", lambda e: self.toggle_solo()),
                        ("<Control-d>", lambda e: self.duplicate()), ("<Control-D>", lambda e: self.duplicate()),
                        ("<BackSpace>", lambda e: self._sc_pop() if self._sc_drawing() else None)):
            # (Windows: the plain "z" binding also caught Ctrl+Z - undo never arrived; Ctrl keys go on to the designer)
            g.bind(key, lambda e, fn=fn, key=key: None if e.state & 0x4 and "Control" not in key else (fn(e), "break")[1])
        g.bind("<MouseWheel>", self._wheel)
        g.bind("<Enter>", lambda e: g.focus_set())
        for key, fn in (("<KP_7>", lambda e: self.view_axis("top")), ("<KP_Home>", lambda e: self.view_axis("top")),
                        ("<Control-KP_7>", lambda e: self.view_axis("bottom")),
                        ("<KP_1>", lambda e: self.view_axis("front")), ("<KP_End>", lambda e: self.view_axis("front")),
                        ("<Control-KP_1>", lambda e: self.view_axis("back")),
                        ("<KP_3>", lambda e: self.view_axis("right")), ("<KP_Next>", lambda e: self.view_axis("right")),
                        ("<Control-KP_3>", lambda e: self.view_axis("left")),
                        ("<KP_5>", lambda e: self.toggle_ortho()), ("<KP_Begin>", lambda e: self.toggle_ortho()),
                        ("<Home>", lambda e: self.reset()),
                        ("<KP_4>", lambda e: self.orbit_step(-1, 0)), ("<KP_6>", lambda e: self.orbit_step(1, 0)),
                        ("<KP_8>", lambda e: self.orbit_step(0, 1)), ("<KP_2>", lambda e: self.orbit_step(0, -1)),
                        ("<KP_Add>", lambda e: self.zoom_step(0.8)), ("<KP_Subtract>", lambda e: self.zoom_step(1.25))):
            g.bind(key, fn)

    # ---- data
    def _real_shapes(self):
        """the tick: game objects drawn as their own models (read once in the background, then fast) or boxes"""
        REAL_SHAPES[0] = bool(self.real_var.get())
        _view_setting("real_shapes", REAL_SHAPES[0], save=True)
        self.__dict__.pop("_scenes", None)
        if self.m is not None:
            self.set_map(self.m)
        if REAL_SHAPES[0]:
            self._poll_shapes()

    def _poll_shapes(self):
        """models arrive from the background reader: draw again (the boxes turn into real shapes)"""
        import game_preview
        ld = game_preview.loader()
        if not REAL_SHAPES[0]:
            return
        if ld.changed() and self.m is not None:
            self.__dict__.pop("_scenes", None)
            self.set_map(self.m)
        if ld.waiting():
            self.info.config(text="Reading the game's models... (once - then they are fast)", fg="#f0e080")
        try:
            self.after(600, self._poll_shapes)
        except tk.TclError:
            pass

    def set_map(self, m, mode=None):
        """(re)build the scene of map m ("3d" = what you will see, "collision" = what the car can hit); returns an
        error text or None"""
        self.mode = mode or self.mode
        self.m = m
        self.op = None
        if self.gl is not None:
            if self.mode == "3d":
                self.lib.pack(side="left", fill="y", before=self.gl)
                self.outl.pack(side="right", fill="y", before=self.gl)
                self.cam_bar.pack(side="top", fill="x", after=self.top_bar)
            else:
                self._stop_placing()
                self.scatter_close()
                self.lib.pack_forget()
                self.outl.pack_forget()
                self.cam_bar.pack_forget()
        self._cam_info()
        try:
            key = (self.mode, REAL_SHAPES[0], json.dumps({k: v for k, v in m.items() if k != "start_cams"}, sort_keys=True,
                                         default=str))          # (the start camera views: not in the scene)
            cache = self.__dict__.setdefault("_scenes", {})   # the last few scenes: back to an unchanged map = instant
            if key not in cache:
                cache[key] = (build_collision if self.mode == "collision" else build_scene)(m)
                while len(cache) > 4:
                    del cache[next(iter(cache))]
            self.scene, self.path, self.bounds, stats = cache[key]
        except Exception as ex:          # noqa: BLE001 - shown in the bar
            self.scene = None
            self.info.config(text="cannot show this road: %s" % ex, fg="#ff6060")
            return str(ex)
        self.info.config(text=stats + "   (%d triangles)" % ((len(self.scene["v"]) + len(self.scene["tv"])) // 3),
                         fg="#9aa8b8")
        nobj = len(m.get("objects") or [])
        self.multi = [k for k in self.multi if (k.startswith("obj:") and int(k[4:]) < nobj) or self._pick(k)]
        self._fill_outliner()
        if not getattr(self, "_placed", False):
            self.reset()
            self._placed = True
        if self._pick(self.sel) is None:
            self.select(None, keep_multi=True)
        else:
            self.select(self.sel, keep_multi=True)
        self.refresh()
        return None

    # ---- objects: select, G / R / S, hide
    def picks(self):
        return (self.scene or {}).get("picks", [])

    def _pick(self, key):
        if key == MULTI:
            return self._multi_pick()
        return next((p for p in self.picks() if p["key"] == key), None) if key else None

    # ---- several selected: ONE pick for all of them (a box round them all, its middle = the pivot), so the same
    #      handles / G R S keys work; when let go every member gets its own change (_member_change)
    def _members(self):
        out = []
        for k in self.multi:
            q = next((p for p in self.picks() if p["key"] == k), None)
            if q is not None and q["editable"] and "point" not in q and "pickup" not in q:
                out.append(q)
        return out

    def _multi_pick(self):
        ms = self._members()
        if len(ms) < 2:
            return None
        cs = np.array([c for q in ms for c in box_corners(q)], np.float64)
        lo, hi = cs.min(0), cs.max(0)
        cx, cz = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2
        return {"key": MULTI, "label": "%d objects" % len(ms), "pos": (cx, lo[1], cz), "turn": 0.0, "rot": (0.0, 0.0),
                "scale": 1.0, "box": (lo[0] - cx, hi[0] - cx, 0.0, hi[1] - lo[1], lo[2] - cz, hi[2] - cz),
                "editable": True, "scalable": True, "multi": True}

    @staticmethod
    def _member_change(g, q, op):
        """(delta, dturn, dscale) of member q while the group g has op: it swings round the group's middle (turn,
        about up only), its distance from the middle grows with the scale (all axes, from the group's bottom);
        objects that can't be scaled only move"""
        s = kvec(op.get("dscale", 1.0))[0]
        t = op.get("dturn", 0.0)
        rel = np.array(q["pos"], float) - np.array(g["pos"], float)
        new = rel * s
        c, si = math.cos(t), math.sin(t)
        new = np.array([new[0] * c + new[2] * si, new[1], -new[0] * si + new[2] * c])   # (an actor's turn)
        d = new - rel + np.array(op.get("delta", (0.0, 0.0, 0.0)), float)
        return tuple(float(v) for v in d), t, (s if q["scalable"] else 1.0)

    def edits(self):
        return dict((self.m or {}).get("edits") or {})

    def select(self, key, keep_multi=False):
        if key == MULTI:                                # (the map rebuilt after a group change: still the group)
            self._set_multi(self.multi)
            return
        if not keep_multi:
            self.multi = []
        self.sel = key
        p = self._pick(key)
        if p is None:
            self.sel = None
            self.sel_frame.pack_forget()
        else:
            e = self.edits().get(key, {})
            changed = []
            if e.get("dx") or e.get("dy") or e.get("dz"):
                changed.append("moved %.0f m" % math.sqrt(e.get("dx", 0) ** 2 + e.get("dy", 0) ** 2 + e.get("dz", 0) ** 2))
            if e.get("turn"):
                changed.append("turned %+.0f°" % math.degrees(e["turn"]))
            k = kvec(e.get("scale", 1.0))
            if max(abs(v - 1.0) for v in k) > 1e-6:
                changed.append("scale x%.2f" % k[0] if max(k) - min(k) < 1e-9 else
                               "scale x%.2f / y%.2f / z%.2f" % (k[0], k[2], k[1]))
            if not p["editable"]:
                how = "part of the road layout - it moves with the road"
            else:
                how = "drag a handle, or G / R / %sX keys   Esc = deselect" % ("S / " if p["scalable"] else "")
            self.sel_bar.config(text="%s   [%s]   %s   |   %s" % (p["label"], key, ", ".join(changed) or "as placed",
                                                                  how))
            self.sel_frame.pack(side="top", fill="x", before=self.gl)
        if self.solo is not None:                       # Solo follows the selection; nothing selected = all back
            if p is None or "point" in p:
                self._solo_off()
            elif self._solo_key(p) != self.solo:
                self._solo_on(p)
        self._outliner_show(p)
        self._panel_show(p)
        self.refresh()

    # ---- the outliner (right side bar): every object of the map by group; the selection is highlighted
    OUTLINER_GROUPS = (("obj", "Placed objects"), ("side", "Roadside & scenery"), ("fixed", "Road layout (fixed)"),
                       ("pickup", "Power-ups"), ("point", "Track points"))

    def _build_outliner(self, parent, bg, fg):
        from tkinter import ttk
        f = tk.Frame(parent, bg=bg, width=270)
        f.pack_propagate(False)
        self.outl_title = tk.Label(f, text="OBJECTS IN THIS MAP", bg=bg, fg="#8fb6e0", font=("Segoe UI", 9, "bold"))
        self.outl_title.pack(anchor="w", padx=6, pady=(6, 2))
        tk.Label(f, text="Search:", bg=bg, fg=fg).pack(anchor="w", padx=6)
        self.outl_search = tk.StringVar()
        tk.Entry(f, textvariable=self.outl_search, bg="#1a1e24", fg=fg, insertbackground=fg,
                 relief="flat").pack(fill="x", padx=6, pady=(0, 4))
        self.outl_search.trace_add("write", lambda *a: self._fill_outliner())
        tk.Label(f, text="Click = select   Shift / Ctrl+click = more   Double-click = go to it\n"
                         "Delete = delete   Ctrl+G = group   Ctrl+Shift+G = ungroup   / = Solo", bg=bg, fg="#6f7c8a",
                 font=("Segoe UI", 8), justify="left").pack(side="bottom", anchor="w", padx=6, pady=(2, 6))
        box = tk.Frame(f, bg=bg)
        box.pack(fill="both", expand=True, padx=(6, 0))
        self.outl_tree = ttk.Treeview(box, show="tree", selectmode="extended", style="Lib.Treeview")
        sb = ttk.Scrollbar(box, command=self.outl_tree.yview)
        self.outl_tree.configure(yscrollcommand=sb.set)
        sb.pack(side="right", fill="y")
        self.outl_tree.pack(side="left", fill="both", expand=True)
        self.outl_tree.tag_configure("group", foreground="#8fb6e0")
        self.outl_tree.bind("<<TreeviewSelect>>", lambda e: self._outliner_pick())
        self.outl_tree.bind("<Double-1>", lambda e: self._outliner_go())
        # Delete with the mouse over the list: the same as in the view (the list has the keys then, not the view)
        self.outl_tree.bind("<Delete>", lambda e: (self.delete_selection(), "break")[1])
        self.outl_tree.bind("<ButtonPress-3>", self._outliner_menu)
        self._outl_open = {"grp:obj"}
        self._outl_sync = False
        return f

    def _outliner_group(self, p):
        if "point" in p:
            return "point"
        if "pickup" in p:
            return "pickup"
        if p["key"].startswith("obj:"):
            return "obj"
        return "side" if p["editable"] else "fixed"

    def _fill_outliner(self):
        t = getattr(self, "outl_tree", None)
        if t is None:
            return
        nested = t.get_children("grp:obj") if t.exists("grp:obj") else ()
        for g in list(t.get_children()) + [g for g in nested if g.startswith("group:")]:
            if t.item(g, "open"):           # (the user's groups inside "Placed objects" too)
                self._outl_open.add(g)
            else:
                self._outl_open.discard(g)
        for g in t.get_children():
            t.delete(g)
        find = self.outl_search.get().strip().lower()
        rows, n = {}, 0
        for p in self.picks():
            iid = self._solo_key(p)                     # (a power-up row: one line for all its lanes)
            if iid in rows.setdefault(self._outliner_group(p), {}):
                continue
            short = p["label"].split("  (")[0].split(" - ")[0]
            text = "%s   [%s]" % (short, iid)
            if find and find not in text.lower():
                continue
            rows[self._outliner_group(p)][iid] = text
        objs = (self.m or {}).get("objects") or []
        cat = road_layout.catalog()
        for i, o in enumerate(objs):                    # placed objects the game has no room for: listed too
            iid = "obj:%d" % i                           # (so they can be found and deleted)
            if iid not in rows.setdefault("obj", {}):
                text = "%s   [%s]   - does not fit, not shown" % ((cat.get(o.get("id")) or {}).get("name", "?"), iid)
                if not find or find in text.lower():
                    rows["obj"][iid] = text
        for g, title in self.OUTLINER_GROUPS:
            items = rows.get(g) or {}
            if not items:
                continue
            gid = "grp:" + g
            t.insert("", "end", iid=gid, text="%s  (%d)" % (title, len(items)), tags=("group",),
                     open=bool(find) or gid in self._outl_open)
            if g == "obj":                              # the user's groups first (Ctrl+G, Path scatter), then the rest
                groups = {}
                for iid in items:
                    i = int(iid[4:])
                    name = objs[i].get("group") if i < len(objs) else None
                    if name:
                        groups.setdefault(name, []).append(iid)
                for name, members in groups.items():
                    nid = "group:" + name
                    t.insert(gid, "end", iid=nid, text="\u25A3 %s  (%d)" % (name, len(members)), tags=("group",),
                             open=bool(find) or nid in self._outl_open)
                    for iid in members:
                        t.insert(nid, "end", iid=iid, text=items.pop(iid))
                        n += 1
            for iid, text in items.items():
                t.insert(gid, "end", iid=iid, text=text)
                n += 1
        self.outl_title.config(text="OBJECTS IN THIS MAP  (%d)" % n)
        self._outliner_show(self._pick(self.sel))

    def _outliner_show(self, p):
        """highlight the selected object's line (its group opened, scrolled into view)"""
        t = getattr(self, "outl_tree", None)
        if t is None:
            return
        if self.multi:
            iids = [k for k in self.multi if t.exists(k)]
        else:
            iids = [self._solo_key(p)] if p is not None and t.exists(self._solo_key(p)) else []
        self._outl_sync = True
        try:
            if iids:
                for iid in iids:                        # (a group's object: its group line and the list open)
                    up = t.parent(iid)
                    while up:
                        t.item(up, open=True)
                        up = t.parent(up)
                t.selection_set(iids)
                t.see(iids[0])
            elif t.selection():
                t.selection_remove(*t.selection())
        finally:
            self.after_idle(lambda: setattr(self, "_outl_sync", False))

    def _outliner_pick(self):
        if self._outl_sync:
            return
        keys = []
        objs = (self.m or {}).get("objects") or []
        sel = self.outl_tree.selection()
        for iid in sel:
            if iid.startswith("grp:"):
                continue
            if iid.startswith("group:"):                # a group line: all its objects
                keys += ["obj:%d" % i for i, o in enumerate(objs) if o.get("group") == iid[6:]]
            elif iid.startswith("obj:"):
                keys.append(iid)
            else:
                keys += [p["key"] for p in self.picks() if self._solo_key(p) == iid][:1]
        keys = list(dict.fromkeys(keys))
        if len(keys) == 1 and self._pick(keys[0]) is not None and not any(i.startswith("group:") for i in sel):
            if keys[0] != self.sel or self.multi:
                self.select(keys[0])
        elif keys:
            self._set_multi(keys, sync=False)

    def _outliner_menu(self, e):
        """right-click in the list: Duplicate / Group / Ungroup / Delete for the selection (the line under the mouse
        joins it first when it is not selected yet)"""
        t = self.outl_tree
        iid = t.identify_row(e.y)
        if not iid or iid.startswith("grp:"):
            return "break"
        if iid not in t.selection():
            t.selection_set(iid)
            self.update_idletasks()
            self._outliner_pick()
        idx = self._sel_objects()
        grouped = any(((self.m or {}).get("objects") or [])[i].get("group") for i in idx)
        menu = tk.Menu(self, tearoff=0, bg="#20242b", fg="#d8dee6", activebackground="#1e9bff",
                       activeforeground="white")
        menu.add_command(label="Duplicate   (Ctrl+D)", command=self.duplicate,
                         state="normal" if idx else "disabled")
        menu.add_command(label="Group   (Ctrl+G)", command=self.group_selection,
                         state="normal" if len(idx) >= 2 else "disabled")
        menu.add_command(label="Ungroup   (Ctrl+Shift+G)", command=self.ungroup_selection,
                         state="normal" if grouped else "disabled")
        menu.add_separator()
        menu.add_command(label="Delete   (Delete)", command=self.delete_selection)
        try:
            menu.tk_popup(e.x_root, e.y_root)
        finally:
            menu.grab_release()
        return "break"

    def _set_multi(self, keys, sync=True):
        """several objects selected: yellow boxes; Delete / Ctrl+G / Ctrl+D work on all of them, and one set of
        handles (move / turn about up / size) moves all of them together (sel = MULTI)"""
        self.op = None
        if self.solo is not None:
            self._solo_off()
        self.multi = list(keys)
        g = self._multi_pick()                          # the movable ones together: handles + G / R / S
        self.sel = MULTI if g else None
        if g:
            self.sel_bar.config(text="%s selected   |   drag a handle, or G move / R turn / S size (all of them "
                                     "together)   Delete, Ctrl+G group, Ctrl+D copy   Esc = none" % g["label"])
            self.sel_frame.pack(side="top", fill="x", before=self.gl)
        else:
            self.sel_frame.pack_forget()
        self._panel_show(None)
        if sync:
            self._outliner_show(None)
        self.info.config(text="%d selected - Delete = delete them, Ctrl+G = group, Ctrl+D = copy, Esc = none"
                              % len(keys), fg="#f0e080")
        self.refresh()

    def _outliner_go(self):
        """double-click: the view flies to the object (keeps its angle; closer if far away)"""
        p = self._pick(self.sel)
        if p is None:
            return
        cs = np.array(box_corners(p), np.float64)
        self.target = list((cs.min(0) + cs.max(0)) / 2)
        self.dist = min(self.dist, float(max(cs.max(0) - cs.min(0))) * 4.0 + 40.0)
        self.drive = self.cam_anim = None
        self.refresh()

    # ---- Solo (Blender's local view): only the selected object, framed; / again (or Esc) = everything back
    @staticmethod
    def _solo_key(p):
        return "pickup:%d" % p["pickup"] if "pickup" in p else p["key"]

    def toggle_solo(self):
        if self.solo is not None:
            self._solo_off()
            return
        p = self._pick(self.sel)
        if p is None or "point" in p or p.get("multi"):
            self.info.config(text="Solo: select ONE object first (a track point has no shape)", fg="#f0e080")
            return
        self._solo_cam = (list(self.target), self.dist, self.yaw, self.pitch, self.ortho)
        self._solo_on(p)

    def _solo_on(self, p):
        self.solo = self._solo_key(p)
        group = [q for q in self.picks() if self._solo_key(q) == self.solo]
        cs = np.array([c for q in group for c in box_corners(q)], np.float64)
        lo, hi = cs.min(0), cs.max(0)
        self.target = list((lo + hi) / 2)
        self.dist = float(max(hi - lo)) * 2.2 + 4.0
        self.drive = self.cam_anim = None
        self.solo_btn.config(text="◎ Show all (/)", bg="#c08a1a")
        self.info.config(text="SOLO: only %s   ( / or Esc = show everything )" % p["label"], fg="#f0e080")
        self.refresh()

    def _solo_off(self):
        self.solo = None
        cam = getattr(self, "_solo_cam", None)
        if cam:
            self.target, self.dist, self.yaw, self.pitch, self.ortho = list(cam[0]), cam[1], cam[2], cam[3], cam[4]
        self.solo_btn.config(text="◎ Solo (/)", bg="#3a3428")
        self.info.config(text="", fg="#9aa8b8")
        self.refresh()

    def _solo_scene(self, sc):
        """the scene arrays with only the Solo object's triangles / lines (cached per scene + object)"""
        key = (id(sc), self.solo)
        if getattr(self, "_solo_cache", (None,))[0] != key:
            oid = sc.get("owners", {}).get(self.solo, -2)
            out = dict(sc)
            for v, c, o in (("v", "c", "vo"), ("tv", "tc", "tvo"), ("lv", "lc", "lo")):
                keep = sc[o] == oid if len(sc.get(o, ())) == len(sc[v]) else np.zeros(len(sc[v]), bool)
                out[v], out[c] = np.ascontiguousarray(sc[v][keep]), np.ascontiguousarray(sc[c][keep])
            out["textured"] = [t for t in sc.get("textured", []) if t.get("owner") == self.solo]
            self._solo_cache = (key, out)
        return self._solo_cache[1]

    # ---- the panel at the bottom left: location / rotation / scale of the selection, typed values
    def _panel_show(self, p):
        for w in self.panel.winfo_children():
            w.destroy()
        self._panel_entries, self._panel_orig = {}, {}
        if p is None or self.mode != "3d":
            self.panel.place_forget()
            return
        P, F = "#20242b", "#d8dee6"
        tk.Label(self.panel, text=p["label"], bg=P, fg="#f0e080", font=("Segoe UI", 9, "bold"),
                 wraplength=300, justify="left").grid(row=0, column=0, columnspan=4, sticky="w", padx=6, pady=(4, 2))
        for col, (t, c) in enumerate((("X", "#ff5a5a"), ("Y", "#6ad04a"), ("Z", "#5a8cff")), start=1):
            tk.Label(self.panel, text=t, bg=P, fg=c, font=("Segoe UI", 9, "bold")).grid(row=1, column=col)
        g = self.scene["ground"]
        e = self.edits().get(p["key"], {})
        rows = [("Location (m)", "loc", (p["pos"][0], p["pos"][2], p["pos"][1] - g))]
        if "pickup" in p:                               # a row: its middle (the list item), on the road
            it = self.scene["pickups"][p["pickup"]]
            rows = [("Location (m)", "loc", (it["x"], it["z"], p["pos"][1] - g))]
        elif "point" in p:
            rows.append(("Road width (m)", "width", (p["width"],)))
        else:
            k = kvec(p["scale"])
            turn = (math.degrees(p["turn"]) + 180) % 360 - 180
            rows.append(("Rotation (°)", "rot", (math.degrees(p["rot"][0]), math.degrees(p["rot"][1]), turn)))
            rows.append(("Scale", "scale", (k[0], k[2], k[1])))
        state = "normal" if p["editable"] else "disabled"
        for r, (title, name, vals) in enumerate(rows, start=2):
            tk.Label(self.panel, text=title, bg=P, fg=F, font=("Segoe UI", 9)).grid(row=r, column=0, sticky="w", padx=6)
            for j, v in enumerate(vals):
                ent = tk.Entry(self.panel, width=9, bg="#1a1e24", fg=F, insertbackground=F, relief="flat",
                               justify="right", disabledbackground="#262a30")
                txt = ("%.3f" if name == "scale" else "%.1f") % v
                ent.insert(0, txt)
                self._panel_orig[(name, j)] = (txt, v)        # untouched boxes keep the exact value
                ent.config(state=state)
                if (name == "scale" and not p["scalable"]) or ("pickup" in p and j == 2):
                    ent.config(state="disabled")
                ent.grid(row=r, column=j + 1, padx=2, pady=1)
                ent.bind("<Return>", lambda ev: (self._panel_apply(), "break")[1])
                ent.bind("<KP_Enter>", lambda ev: (self._panel_apply(), "break")[1])
                ent.bind("<Escape>", lambda ev: (self.gl.focus_set(), self._panel_show(self._pick(self.sel))))
                self._panel_entries[(name, j)] = ent
        r = len(rows) + 2
        if "pickup" in p:                               # type menu + delete
            import world as world_mod
            tk.Label(self.panel, text="Type", bg=P, fg=F, font=("Segoe UI", 9)).grid(row=r, column=0, sticky="w", padx=6)
            names = (["mixed"] if p["row"] else []) + list(world_mod.PICKUP_TYPES)
            var = tk.StringVar(value=p["ptype"] or ("mixed" if p["row"] else names[0]))
            om = tk.OptionMenu(self.panel, var, *names, command=lambda v, p=p: self._pickup_type(p, v))
            om.config(bg="#1a1e24", fg=F, highlightthickness=0, activebackground="#2c3440", width=8)
            om.grid(row=r, column=1, columnspan=2, sticky="w", padx=2)
            tk.Button(self.panel, text="✖ Delete", bg="#4a2a2a", fg="white", font=("Segoe UI Symbol", 9),
                      command=lambda p=p: self._delete_pickup(p)).grid(row=r, column=3, sticky="e", padx=4, pady=2)
            r += 1
        if p["editable"] and "point" not in p and "pickup" not in p:
            # Solid: a wall box in the collision floor (the car stops at it) - or just a shape the car drives through
            self._solid = tk.BooleanVar(value=bool(e.get("solid")))
            tk.Checkbutton(self.panel, text="Solid - stops the car like a wall (off = just a shape)",
                           variable=self._solid, command=lambda p=p: self._set_solid(p), bg=P, fg=F,
                           selectcolor="#1a1e24", activebackground=P, activeforeground=F,
                           font=("Segoe UI", 9)).grid(row=r, column=0, columnspan=4, sticky="w", padx=4)
            r += 1
            tk.Button(self.panel, text="↺ Reset", bg="#3a3428", fg="white", font=("Segoe UI Symbol", 9),
                      command=self._reset_object).grid(row=r, column=0, sticky="w", padx=6, pady=4)
        tk.Label(self.panel, text="type a value + Enter" if p["editable"] else "fixed: moves with the road",
                 bg=P, fg="#6f7c8a", font=("Segoe UI", 8)).grid(row=r, column=1, columnspan=3, sticky="e", padx=6)
        self.panel.place(in_=self.gl, relx=0.0, rely=1.0, x=8, y=-8, anchor="sw")
        self.panel.lift()

    def _panel_values(self, name, n):
        out = []
        for j in range(n):
            try:
                txt = self._panel_entries[(name, j)].get().strip()
                orig = self._panel_orig.get((name, j))
                out.append(orig[1] if orig and txt == orig[0] else float(txt.replace(",", ".")))
            except (KeyError, ValueError):
                return None
        return out

    def _panel_apply(self):
        """the panel's values -> the object's edit (or the track point)"""
        p = self._pick(self.sel)
        if p is None or not p["editable"] or self.op or p.get("multi"):
            return
        g = self.scene["ground"]
        loc = self._panel_values("loc", 3)
        if loc is None:
            self.sel_bar.config(text="Please type numbers only (e.g. 12.5)")
            return
        if "pickup" in p:
            items = [dict(it) for it in self.scene["pickups"]]
            it = items[p["pickup"]]
            if abs(loc[0] - it["x"]) > 1e-6 or abs(loc[1] - it["z"]) > 1e-6:
                it["x"], it["z"] = round(loc[0], 1), round(loc[1], 1)
                self.set_pickups(items)
            return
        if "point" in p:
            w = self._panel_values("width", 1)
            vals = {"x": loc[0], "z": loc[1], "h": max(0.0, loc[2])}
            if w:
                vals["w"] = max(5.0, w[0])
            now = {"x": p["pos"][0], "z": p["pos"][2], "h": p["pos"][1] - g, "w": p["width"]}
            vals = {k: v for k, v in vals.items() if abs(v - now[k]) > 1e-6}    # only what was changed
            if vals:
                self.set_point(p["point"], **vals)
            return
        rot, sc = self._panel_values("rot", 3), self._panel_values("scale", 3)
        if rot is None or (p["scalable"] and sc is None):
            self.sel_bar.config(text="Please type numbers only (e.g. 12.5)")
            return
        old = self.edits().get(p["key"], {})
        e = dict(old)
        # the layout's own place = now - the edit so far; the new edit = typed value - that
        e["dx"] = loc[0] - (p["pos"][0] - old.get("dx", 0.0))
        e["dz"] = loc[1] - (p["pos"][2] - old.get("dz", 0.0))
        e["dy"] = loc[2] + g - (p["pos"][1] - old.get("dy", 0.0))
        e["rx"], e["rz"] = math.radians(rot[0]), math.radians(rot[1])
        base_turn = p["turn"] - old.get("turn", 0.0)
        e["turn"] = (math.radians(rot[2]) - base_turn + math.pi) % (2 * math.pi) - math.pi
        if p["scalable"]:
            e["scale"] = kmul([max(0.01, v) for v in (sc[0], sc[2], sc[1])], 1.0)
        same = all(abs(kvec(e.get(k, d))[0] - kvec(old.get(k, d))[0]) < 1e-4 and
                   abs(kvec(e.get(k, d))[-1] - kvec(old.get(k, d))[-1]) < 1e-4 and
                   abs(kvec(e.get(k, d))[1] - kvec(old.get(k, d))[1]) < 1e-4
                   for k, d in (("dx", 0.0), ("dy", 0.0), ("dz", 0.0), ("rx", 0.0), ("rz", 0.0), ("turn", 0.0),
                                ("scale", 1.0)))
        if not same:
            self._store(p["key"], e)

    def _reset_object(self):
        p = self._pick(self.sel)
        if p is None or "point" in p or "pickup" in p:
            return
        if p.get("multi"):                              # several: each back to as placed
            new = self.edits()
            for q in self._members():
                new[q["key"]] = {k: v for k, v in new.get(q["key"], {}).items() if k in ("hidden", "solid")}
            self._store_all(new)
            return
        e = {k: v for k, v in self.edits().get(p["key"], {}).items() if k in ("hidden", "solid")}
        self._store(p["key"], e)

    def _set_solid(self, p):
        e = dict(self.edits().get(p["key"], {}))
        e["solid"] = bool(self._solid.get())
        self._store(p["key"], e)

    # ---- handles on the selected object (Blender gizmo): red X, green Y (= our world z), blue Z (up)
    AXES = {"x": ((1.0, 0.0, 0.0), (.95, .25, .25)), "y": ((0.0, 0.0, 1.0), (.4, .85, .3)),
            "z": ((0.0, 1.0, 0.0), (.3, .5, 1.0))}

    def _handles(self):
        """(centre, length in m, [(axis, end point)], ring points) of the selected object's handles, or None"""
        p = self._pick(self.sel)
        if p is None or not p["editable"] or self._mats is None or self.mode != "3d":
            return None
        if self.gizmo.get() == "scale" and not p["scalable"]:
            return None
        if ("point" in p or "pickup" in p) and self.gizmo.get() != "move":
            return None                                     # track points / power-ups: move only
        op = self.op or {}
        d = op.get("delta", (0, 0, 0)) if op.get("kind") == "move" else (0, 0, 0)
        if "point" in p or "pickup" in p:
            c = np.array(p["pos"], float) + np.array(d)
        else:
            cs = box_corners(p)
            c = np.array([p["pos"][0] + d[0], (cs[0][1] + cs[7][1]) / 2 + d[1], p["pos"][2] + d[2]])
        s = self._screen(c, depth=True)
        if s is None:
            return None
        a = self._unproject(s[0], s[1], s[2])
        b = self._unproject(s[0] + 90, s[1], s[2])          # handles ~90 pixels long whatever the zoom
        if a is None or b is None:
            return None
        ln = float(np.linalg.norm(b - a))
        axes = []
        dt, dr = op.get("dturn", 0.0), op.get("drot", (0.0, 0.0))
        A = pick_axes(p, dt, dr)                            # the object's own axes (scale handles, red ring)
        own = {"x": A[0], "y": A[2], "z": A[1]}
        for name, (v, _) in self.AXES.items():
            if name == "z" and "pickup" in p:
                continue                                    # power-ups sit on the road: no up / down
            axes.append((name, c + ln * np.array(own[name] if self.gizmo.get() == "scale" else v)))
        # rotate: three rings, each round the axis it turns about - blue: up (turn), green: the turned object's
        # own z (Blender Y), red: its own (tilted) x
        normals = {"z": (0.0, 1.0, 0.0), "y": actor_axes(p["turn"] + dt)[2], "x": A[0]}
        rings = {}
        for name, nv in normals.items():
            nv = np.array(nv, float)
            u = np.cross(nv, (0.0, 1.0, 0.0) if abs(nv[1]) < 0.9 else (1.0, 0.0, 0.0))
            u /= np.linalg.norm(u)
            v = np.cross(nv, u)
            rings[name] = ([c + 0.85 * ln * (math.cos(q) * u + math.sin(q) * v) for q in np.linspace(0, 2 * math.pi, 49)],
                           nv)
        if p.get("multi"):                                  # several: turn about up only, size all axes (the dot)
            rings = {"z": rings["z"]}
            if self.gizmo.get() == "scale":
                axes = []
        return c, ln, axes, rings

    def _handle_at(self, mx, my):
        """which handle is under the mouse: "x" / "y" / "z" (arrows, cubes; rings in rotate mode), "centre", None"""
        h = self._handles()
        if h is None:
            return None
        c, ln, axes, rings = h
        sc = self._screen(c)
        if sc is None:
            return None

        def seg_dist(a, b):
            ax, ay = a
            bx, by = b
            vx, vy = bx - ax, by - ay
            t = max(0.0, min(1.0, ((mx - ax) * vx + (my - ay) * vy) / max(vx * vx + vy * vy, 1e-9)))
            return math.hypot(mx - ax - t * vx, my - ay - t * vy)
        if math.hypot(mx - sc[0], my - sc[1]) < 10 and self.gizmo.get() != "rotate":
            return "centre"
        if self.gizmo.get() == "rotate":
            best = None
            for name, (ring, _) in rings.items():
                pts = [self._screen(q) for q in ring]
                if all(pts):
                    dd = min(seg_dist(a, b) for a, b in zip(pts, pts[1:]))
                    if dd < 8 and (best is None or dd < best[0]):
                        best = (dd, name)
            return best[1] if best else None
        best = None
        for name, end in axes:
            se = self._screen(end)
            if se is None:
                continue
            dd = seg_dist(sc, se)
            if dd < 9 and (best is None or dd < best[0]):
                best = (dd, name)
        return best[1] if best else None

    def _draw_handles(self):
        h = self._handles()
        if h is None:
            return
        c, ln, axes, rings = h
        op = self.op or {}
        active = op.get("axis") if op else self._hot
        gl.glDisable(gl.GL_DEPTH_TEST)
        mode = self.gizmo.get()

        def colour(name, base):
            return (1.0, .9, .2) if name == active else base
        if mode == "rotate":                              # three rings (red X, green Y, blue Z = turn)
            gl.glLineWidth(1.5)                           # a faint ball outline, as Blender
            gl.glColor4f(.8, .8, .8, .35)
            gl.glBegin(gl.GL_LINE_STRIP)
            s0 = self._screen(c, depth=True)
            for q in np.linspace(0, 2 * math.pi, 49):
                w = self._unproject(s0[0] + 0.9 * 90 * math.cos(q), s0[1] + 0.9 * 90 * math.sin(q), s0[2])
                if w is not None:
                    gl.glVertex3f(*w)
            gl.glEnd()
            for name, (ring, _) in rings.items():
                gl.glLineWidth(4.0 if name == active else 2.5)
                gl.glColor3f(*colour(name, self.AXES[name][1]))
                gl.glBegin(gl.GL_LINE_STRIP)
                for q in ring:
                    gl.glVertex3f(*q)
                gl.glEnd()
        else:
            for name, end in axes:
                col = colour(name, self.AXES[name][1])
                gl.glLineWidth(4.0 if name == active else 2.5)
                gl.glColor3f(*col)
                gl.glBegin(gl.GL_LINES)
                gl.glVertex3f(*c)
                gl.glVertex3f(*end)
                gl.glEnd()
                d = (end - c) / ln
                # a side vector, then the tip: a cone (move) or a cube (scale)
                side = np.cross(d, (0, 1, 0)) if abs(d[1]) < 0.9 else np.array((1.0, 0, 0))
                side = side / np.linalg.norm(side)
                up = np.cross(d, side)
                r = ln * 0.07
                gl.glBegin(gl.GL_TRIANGLES)
                if mode == "move":
                    tip = end + d * ln * 0.22
                    ring_pts = [end + r * (math.cos(q) * side + math.sin(q) * up) for q in np.linspace(0, 2 * math.pi, 9)]
                    for a, b in zip(ring_pts, ring_pts[1:]):
                        gl.glVertex3f(*a)
                        gl.glVertex3f(*b)
                        gl.glVertex3f(*tip)
                else:
                    cube = [end + r * (sx * side + sy * up + sz * d) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
                    for f in ((0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5)):
                        for i in (f[0], f[1], f[2], f[0], f[2], f[3]):
                            gl.glVertex3f(*cube[i])
                gl.glEnd()
            gl.glPointSize(11.0 if active == "centre" else 8.0)  # the middle: move on the ground / scale all
            gl.glColor3f(*colour("centre", (.95, .95, .95)))
            gl.glBegin(gl.GL_POINTS)
            gl.glVertex3f(*c)
            gl.glEnd()
            gl.glPointSize(1.0)
        gl.glLineWidth(1.0)
        gl.glEnable(gl.GL_DEPTH_TEST)

    def _ray(self, mx, my):
        """world ray (origin, unit direction) under the mouse"""
        if self._mats is None:
            return None
        inv, w, h = self._mats
        nx, ny = 2.0 * mx / max(w, 1) - 1.0, 1.0 - 2.0 * my / max(h, 1)
        a = inv @ np.array([nx, ny, -1.0, 1.0])
        b = inv @ np.array([nx, ny, 1.0, 1.0])
        a, b = a[:3] / a[3], b[:3] / b[3]
        d = b - a
        return a, d / np.linalg.norm(d)

    def _screen(self, p, depth=False):
        """window (x, y) of world point p (+ its depth -1..1 for _unproject)"""
        if self._mats is None:
            return None
        inv, w, h = self._mats
        c = self._fwd @ np.array([p[0], p[1], p[2], 1.0])
        if c[3] < 1e-9:
            return None                                    # behind the camera
        x, y = (c[0] / c[3] + 1) * w / 2, (1 - c[1] / c[3]) * h / 2
        return (x, y, c[2] / c[3]) if depth else (x, y)

    def _unproject(self, x, y, z):
        """world point at window (x, y), depth z (-1 near .. 1 far)"""
        if self._mats is None:
            return None
        inv, w, h = self._mats
        a = inv @ np.array([2.0 * x / w - 1.0, 1.0 - 2.0 * y / h, z, 1.0])
        return a[:3] / a[3] if abs(a[3]) > 1e-12 else None

    def _left_press(self, e):
        self.gl.focus_set()
        if self._sc_drawing() and self.mode == "3d":    # Path scatter: one more path point
            self._sc_click(e.x, e.y)
            return "break"
        if self.placing and self.mode == "3d" and not self.op:
            self._place_at(e.x, e.y)
            return "break"
        if self.op:
            self._confirm_op()
            return "break"
        h = self._handle_at(e.x, e.y)
        if h is not None:                               # drag a handle: move / turn / scale until the button is up
            kind = {"move": "move", "rotate": "turn", "scale": "scale"}[self.gizmo.get()]
            self._start_op(kind, axis=None if h in ("centre", "ring") else h, m=(e.x, e.y), drag=True)
            return "break"
        if self.solo is not None:                       # Solo: the shown object stays selected (handles above)
            return "break"
        for p in self.picks():                          # track points first: a dot within 10 pixels
            if "point" in p:
                s = self._screen((p["pos"][0], p["pos"][1] + 0.5, p["pos"][2]))
                if s and math.hypot(s[0] - e.x, s[1] - e.y) < 10:
                    self.gizmo.set("move")
                    self.select(p["key"])
                    return "break"
        r = self._ray(e.x, e.y)
        if r is None:
            return "break"
        best = None
        for p in self.picks():
            if "point" in p:
                continue
            t = ray_hit(p, r[0], r[1])
            if t is not None and (best is None or t < best[0]):
                best = (t, p["key"])
        if e.state & 0x0001 and best:                   # Shift+click: add to / take out of the selection
            cur = list(self.multi) or ([self.sel] if self.sel else [])
            cur = [k for k in cur if k != best[1]] if best[1] in cur else cur + [best[1]]
            if len(cur) == 1:
                self.select(cur[0])
            elif cur:
                self._set_multi(cur)
            else:
                self.select(None)
            return "break"
        self.select(best[1] if best else None)
        return "break"

    def _ground_hit(self, mx, my, y):
        r = self._ray(mx, my)
        if r is None or abs(r[1][1]) < 1e-6:
            return None
        t = (y - r[0][1]) / r[1][1]
        return None if t < 0 else r[0] + t * r[1]

    def _left_release(self, e):
        self._release()
        if self.op and self.op.get("drag"):
            self._confirm_op()
        return "break"

    def _key_op(self, kind):
        """G / R / S: also shows that kind of handles"""
        self.gizmo.set({"move": "move", "turn": "rotate", "scale": "scale"}[kind])
        x, y = self.gl.winfo_pointerx() - self.gl.winfo_rootx(), self.gl.winfo_pointery() - self.gl.winfo_rooty()
        self._start_op(kind, m=(x, y))

    def _start_op(self, kind, axis=None, m=(0, 0), drag=False):
        p = self._pick(self.sel)
        if p is None or self.mode != "3d" or self.op:
            return
        if not p["editable"]:
            self.select(self.sel)
            return
        if kind == "scale" and not p["scalable"]:
            self.sel_bar.config(text="%s: cannot be scaled%s - move / rotate only" % (
                p["label"], "" if "point" in p else " (the game's physics would not follow)"))
            return
        if "point" in p and kind != "move":
            self.sel_bar.config(text="Track point: move only (G, or drag the arrows). Width: the panel below.")
            return
        if "pickup" in p and kind != "move":
            self.sel_bar.config(text="Power-up: move only (G, or drag the arrows). Type: the panel below.")
            return
        h = self._handles()
        cen = self._screen(h[0]) if h else self._screen(p["pos"])
        if kind == "turn" and axis is None:
            axis = "z"                                  # R alone = turn about up (then X / Y / Z switch, as Blender)
        self.op = {"kind": kind, "axis": axis, "m0": m, "cen": cen, "hit0": self._ground_hit(m[0], m[1], p["pos"][1]),
                   "delta": (0.0, 0.0, 0.0), "dturn": 0.0, "dscale": 1.0, "drot": (0.0, 0.0), "drag": drag,
                   "centre": h[0] if h else np.array(p["pos"]), "len": h[1] if h else 10.0, "turn0": p["turn"],
                   "own": pick_axes(p), "rings": h[3] if h else None}
        self._op_text()
        self.refresh()

    def _axis(self, a):
        if self.op and self.sel == MULTI and self.op["kind"] != "move":
            return                                      # several: turn about up / size all axes only
        if self.op and self.op["kind"] in ("move", "scale"):
            self.op["axis"] = None if self.op["axis"] == a else a
            self._op_text()
        elif self.op and self.op["kind"] == "turn" and not self.op["drag"]:
            self.op.update(axis=a, dturn=0.0, drot=(0.0, 0.0))
            self._op_text()
            self.refresh()

    def _axis_or_hide(self, a):
        if self.op:
            self._axis(a)
        else:
            self._hide()

    def _axis_screen(self, op, name):
        """an axis of the running op on screen: (centre (x, y), pixel vector of op["len"] metres along it)"""
        A = op["own"]
        v = self.AXES[name][0] if op["kind"] == "move" else {"x": A[0], "y": A[2], "z": A[1]}[name]
        a = self._screen(op["centre"])
        b = self._screen(op["centre"] + op["len"] * np.array(v))
        if a is None or b is None:
            return None
        return a, (b[0] - a[0], b[1] - a[1])

    def _hover_motion(self, e):
        op = self.op
        if not op:                                       # no op: light up the handle under the mouse
            hot = self._handle_at(e.x, e.y)
            if hot != self._hot:
                self._hot = hot
                self.refresh()
            return
        p = self._pick(self.sel)
        if op["kind"] in ("move", "scale") and op["axis"]:
            ax = self._axis_screen(op, op["axis"])
            if ax is not None:
                (cx, cy), (vx, vy) = ax
                vv = max(vx * vx + vy * vy, 1e-6)
                if op["kind"] == "move":                 # metres = mouse movement along the axis on screen
                    amt = ((e.x - op["m0"][0]) * vx + (e.y - op["m0"][1]) * vy) / vv * op["len"]
                    v = self.AXES[op["axis"]][0]
                    op["delta"] = (float(v[0] * amt), float(v[1] * amt), float(v[2] * amt))
                else:                                    # scale along one axis: mouse distance along it, now / at start
                    s0 = ((op["m0"][0] - cx) * vx + (op["m0"][1] - cy) * vy) / vv
                    s1 = ((e.x - cx) * vx + (e.y - cy) * vy) / vv
                    f = max(0.05, s1 / s0) if abs(s0) > 1e-3 else 1.0
                    k = [1.0, 1.0, 1.0]
                    k[{"x": 0, "z": 1, "y": 2}[op["axis"]]] = f      # model axes: x, y up, z (= Blender Y)
                    op["dscale"] = k
        elif op["kind"] == "move":                       # free: the ground point under the mouse
            h = self._ground_hit(e.x, e.y, p["pos"][1])
            if h is not None and op["hit0"] is not None:
                op["delta"] = (float(h[0] - op["hit0"][0]), 0.0, float(h[2] - op["hit0"][2]))
        elif op["kind"] == "scale" and op["drag"]:       # the middle dot: drag right / up = bigger, all axes
            op["dscale"] = max(0.05, 1.0 + ((e.x - op["m0"][0]) - (e.y - op["m0"][1])) / 150.0)
        elif op["cen"] is not None:
            cx, cy = op["cen"]
            if op["kind"] == "turn":
                a0 = math.atan2(op["m0"][1] - cy, op["m0"][0] - cx)
                a1 = math.atan2(e.y - cy, e.x - cx)
                ang = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi
                if e.state & 0x0004:                           # Ctrl: 15 degree steps
                    ang = round(ang / math.radians(15)) * math.radians(15)
                # the mouse going round clockwise on screen turns the object clockwise as you see it: the sign
                # depends on whether the ring's axis points at you or away (the view is mirrored in z, see draw)
                nv = {"z": (0.0, 1.0, 0.0), "y": actor_axes(op["turn0"])[2], "x": op["own"][0]}[op["axis"]]
                ray = self._ray(cx, cy)
                if ray is not None and float(np.dot(nv, ray[1])) > 0:
                    ang = -ang
                if op["axis"] == "z":
                    op["dturn"] = ang
                elif op["axis"] == "x":
                    op["drot"] = (ang, 0.0)
                else:
                    op["drot"] = (0.0, ang)
            else:
                d0 = math.hypot(op["m0"][0] - cx, op["m0"][1] - cy)
                op["dscale"] = max(0.05, math.hypot(e.x - cx, e.y - cy) / max(d0, 1.0))
        self._op_text()
        self.refresh()

    def _op_text(self):
        op = self.op
        if op["kind"] == "move":
            txt = "MOVE  x %+.1f  z %+.1f  up %+.1f m" % (op["delta"][0], op["delta"][2], op["delta"][1])
            txt += "   (only along: %s)" % {"x": "world X", "y": "world Z (Blender Y)", "z": "up/down"}[op["axis"]] \
                if op["axis"] else "   X / Y = only along one axis, Z = up/down"
        elif op["kind"] == "turn":
            a = {"z": op["dturn"], "x": op["drot"][0], "y": op["drot"][1]}[op["axis"]]
            txt = "ROTATE %s  %+.0f°   (Ctrl = 15° steps%s)" % (
                {"x": "X (red)", "y": "Y (green)", "z": "Z (blue, up)"}[op["axis"]], math.degrees(a),
                "" if op["drag"] else "; X / Y / Z = other axis")
        else:
            k = kvec(op["dscale"])
            txt = "SCALE  x%.2f" % k[0] if max(k) - min(k) < 1e-9 else \
                "SCALE  x%.2f  y%.2f  z%.2f" % (k[0], k[2], k[1])
            if not op["drag"]:
                txt += "   (X / Y / Z = only along one axis)"
        end = "let go = OK   (Esc = cancel)" if op["drag"] else "click / Enter = OK   right-click / Esc = cancel"
        self.sel_bar.config(text="%s   |   %s" % (txt, end))

    def _cancel_op(self):
        if self.op:
            self.op = None
            self.select(self.sel)

    def _escape(self):
        if self._sc_drawing():
            self._sc_draw_toggle(False)
        elif self.placing:
            self._stop_placing()
        elif self.op:
            self._cancel_op()
        elif self.solo is not None:                     # first Esc: everything back (the selection stays)
            self._solo_off()
        else:
            self.select(None)

    def _store(self, key, e):
        new = self.edits()
        new[key] = e
        self._store_all(new)

    def _store_all(self, new):
        out = {}
        for key, e in new.items():
            e = {k: v for k, v in e.items() if not (k in ("dx", "dy", "dz", "turn", "rx", "rz") and abs(v) < 1e-6)
                 and not (k == "scale" and max(abs(x - 1.0) for x in kvec(v)) < 1e-6) and not (k in ("hidden", "solid") and not v)}
            if e:
                out[key] = e
        new = out
        if self.on_edit:
            self.on_edit(new)
        else:                                          # standalone viewer: change the map in place
            self.m["edits"] = new
            self.set_map(self.m)

    def _confirm_op(self):
        if self._sc_drawing() and not self.op:          # Enter: the path is done
            self._sc_draw_toggle(False)
            return
        op, key = self.op, self.sel
        if not op or key is None:
            return
        self.op = None
        p = self._pick(key)
        if p is not None and p.get("multi"):            # several: each member its own change, one undo step
            new = self.edits()
            for q in self._members():
                d, t, s = self._member_change(p, q, op)
                e = dict(new.get(q["key"], {}))
                e["dx"], e["dy"], e["dz"] = e.get("dx", 0.0) + d[0], e.get("dy", 0.0) + d[1], e.get("dz", 0.0) + d[2]
                e["turn"] = e.get("turn", 0.0) + t
                e["scale"] = kmul(e.get("scale", 1.0), s)
                new[q["key"]] = e
            self._store_all(new)
            return
        if p is not None and "pickup" in p:             # a power-up row / single: move its list item
            d = op["delta"]
            if abs(d[0]) > 1e-6 or abs(d[2]) > 1e-6:
                items = [dict(it) for it in self.scene["pickups"]]
                it = items[p["pickup"]]
                it["x"], it["z"] = round(it["x"] + d[0], 1), round(it["z"] + d[2], 1)
                self.set_pickups(items)
            return
        if p is not None and "point" in p:              # a track point: change the road itself
            d = op["delta"]
            vals = {}
            if abs(d[0]) > 1e-6 or abs(d[2]) > 1e-6:
                vals.update(x=p["pos"][0] + d[0], z=p["pos"][2] + d[2])
            if abs(d[1]) > 1e-6:
                vals["h"] = max(0.0, p["pos"][1] - self.scene["ground"] + d[1])
            if vals:
                self.set_point(p["point"], **vals)
            return
        e = dict(self.edits().get(key, {}))
        e["dx"] = e.get("dx", 0.0) + op["delta"][0]
        e["dy"] = e.get("dy", 0.0) + op["delta"][1]
        e["dz"] = e.get("dz", 0.0) + op["delta"][2]
        e["turn"] = e.get("turn", 0.0) + op["dturn"]
        e["rx"] = e.get("rx", 0.0) + op["drot"][0]
        e["rz"] = e.get("rz", 0.0) + op["drot"][1]
        e["scale"] = kmul(e.get("scale", 1.0), op["dscale"])
        self._store(key, e)

    def set_point(self, i, **values):
        """track point i gets new x / z / h (height) / w (width); the designer stores it (undo, auto-save)"""
        values = {k: round(float(v), 3) for k, v in values.items()}
        if self.on_point:
            self.on_point(i, values)
        else:
            pt = self.m["points"][i]
            if not isinstance(pt, dict):
                pt = self.m["points"][i] = {"x": pt[0], "z": pt[1]}
            pt.update(values)
            self.set_map(self.m)

    def set_pickups(self, items):
        """the map's whole power-up list (the automatic rows become a list the first time); designer: undo + save"""
        if self.on_pickups:
            self.on_pickups(items)
        else:
            self.m["pickups"] = items
            self.set_map(self.m)

    def _delete_pickup(self, p):
        items = [dict(it) for it in self.scene["pickups"]]
        del items[p["pickup"]]
        self.sel = None
        self.set_pickups(items)

    def _pickup_type(self, p, name):
        items = [dict(it) for it in self.scene["pickups"]]
        if name == "mixed":
            items[p["pickup"]].pop("type", None)
        else:
            items[p["pickup"]]["type"] = name
        self.set_pickups(items)

    def _hide(self):
        p = self._pick(self.sel)
        if p is not None and p.get("multi"):            # several: X = Delete (all of them)
            self.delete_selection()
            return
        if p is not None and "pickup" in p and self.mode == "3d":
            self._delete_pickup(p)                      # X / Delete on a power-up: remove its row / single
            return
        if p is None or not p["editable"] or self.mode != "3d" or "point" in p:
            return
        if p["key"].startswith("obj:"):
            self.delete_object(int(p["key"][4:]))
            return
        e = dict(self.edits().get(p["key"], {}))
        e["hidden"] = True
        self.sel = None
        self._store(p["key"], e)

    def _clear(self, what):
        p = self._pick(self.sel)
        if p is None or self.op:
            return
        if p.get("multi"):                              # several: that change undone on each of them
            new = self.edits()
            for q in self._members():
                e = dict(new.get(q["key"], {}))
                for k in {"move": ("dx", "dy", "dz"), "turn": ("turn", "rx", "rz"), "scale": ("scale",)}[what]:
                    e.pop(k, None)
                new[q["key"]] = e
            self._store_all(new)
            return
        e = dict(self.edits().get(p["key"], {}))
        if "point" in p or "pickup" in p:
            return
        for k in {"move": ("dx", "dy", "dz"), "turn": ("turn", "rx", "rz"), "scale": ("scale",)}[what]:
            e.pop(k, None)
        self._store(p["key"], e)

    def _unhide_all(self):
        new = {k: {a: b for a, b in e.items() if a != "hidden"} for k, e in self.edits().items()}
        self._store_all(new)

    # ---- the object library: every object of the game (library/catalog.json), by kind > place > category. Pick one,
    #      then click in the view to place it (again and again); Esc / right-click = stop placing
    KIND_TITLES = (("import", "Your models (imported .glb)"), ("scenery", "Scenery - look only (tick Solid = wall)"), ("breakable", "Breakable - smash / knock over"),
                   ("movable", "Moves when hit (physics)"), ("animated", "Animated"))

    def _build_library(self, parent, bg, fg):
        from tkinter import ttk
        f = tk.Frame(parent, bg=bg, width=300)
        f.pack_propagate(False)
        tk.Label(f, text="OBJECT LIBRARY", bg=bg, fg="#8fb6e0", font=("Segoe UI", 9, "bold")).pack(anchor="w", padx=6,
                                                                                                pady=(6, 0))
        tk.Button(f, text="⬆ Import 3D model (.glb)...", command=self._import_model, bg="#1e9bff", fg="white",
                  font=("Segoe UI Symbol", 9, "bold")).pack(fill="x", padx=6, pady=(4, 2))
        tk.Button(f, text="〰 Path scatter (objects along a path)...", command=self.scatter_open, bg="#7a3a8a",
                  fg="white", font=("Segoe UI Symbol", 9, "bold")).pack(fill="x", padx=6, pady=(0, 6))
        tk.Label(f, text="Search:", bg=bg, fg=fg).pack(anchor="w", padx=6)
        self.lib_search = tk.StringVar()
        tk.Entry(f, textvariable=self.lib_search, bg="#1a1e24", fg=fg, insertbackground=fg,
                 relief="flat").pack(fill="x", padx=6, pady=(0, 4))
        self.lib_search.trace_add("write", lambda *a: self._fill_library())
        st = ttk.Style(f)
        try:
            st.theme_use("clam")          # (the Windows theme ignores the dark list colour; no other ttk widgets here)
        except tk.TclError:
            pass
        st.configure("Lib.Treeview", background="#08111f", fieldbackground="#08111f", foreground=fg, rowheight=20,
                     borderwidth=0, relief="flat", bordercolor="#08111f", lightcolor="#08111f", darkcolor="#08111f")
        st.map("Lib.Treeview", background=[("selected", "#1e9bff")], foreground=[("selected", "white")])
        st.configure("Vertical.TScrollbar", background="#13233f", troughcolor="#08111f", bordercolor="#08111f",
                     arrowcolor="#8095b6", lightcolor="#13233f", darkcolor="#13233f")
        # bottom (packed first: it keeps its place): Bake 3D pictures + the Real shapes switch
        low = tk.Frame(f, bg=bg)
        low.pack(side="bottom", fill="x", padx=6, pady=(4, 8))
        self.bake_btn = tk.Button(low, text="\u2B1A Bake 3D pictures", command=self._bake, bg="#1e9bff", fg="white",
                                  font=("Segoe UI Symbol", 9, "bold"))
        self.bake_btn.pack(fill="x")
        self.bake_note = tk.Label(low, text="Makes a picture of every object (once, a few minutes) - then hover an "
                                            "object in the list to see it.", bg=bg, fg="#8095b6", wraplength=285,
                                  justify="left", font=("Segoe UI", 8))
        self.bake_note.pack(fill="x", pady=(2, 6))
        tk.Checkbutton(low, text="\u25C6  REAL SHAPES in the 3D view", variable=self.real_var,
                       command=self._real_shapes, indicatoron=False, bg="#14a38b", fg="white",
                       font=("Segoe UI Symbol", 11, "bold")).pack(fill="x")
        tk.Label(low, text="On: placed game objects show as their own models (read once, then fast). Off: boxes.",
                 bg=bg, fg="#8095b6", wraplength=285, justify="left", font=("Segoe UI", 8)).pack(fill="x", pady=(2, 0))
        box = tk.Frame(f, bg=bg)
        box.pack(fill="both", expand=True, padx=(6, 0))
        self.lib_tree = ttk.Treeview(box, show="tree", selectmode="browse", style="Lib.Treeview")
        sb = ttk.Scrollbar(box, command=self.lib_tree.yview)
        self.lib_tree.configure(yscrollcommand=sb.set)
        sb.pack(side="right", fill="y")
        self.lib_tree.pack(side="left", fill="both", expand=True)
        for kind, col in KIND_RGB.items():
            self.lib_tree.tag_configure(kind, foreground="#%02x%02x%02x" % tuple(int(255 * c) for c in col))
        self.lib_tree.bind("<<TreeviewSelect>>", lambda e: self._lib_select())
        self.lib_tree.bind("<Motion>", self._lib_hover)
        self.lib_tree.bind("<Leave>", lambda e: self._hover_hide())
        self.lib_info = tk.Label(f, text="", bg=bg, fg="#f0e080", wraplength=285, justify="left", anchor="w",
                                 font=("Segoe UI", 9))
        self.lib_info.pack(fill="x", padx=6, pady=4)
        tk.Label(f, text="Pick an object, then click in the view to place it (again and again). Esc or right-click = "
                         "stop. Placed objects: select, then G / R / S or the handles; X = delete.", bg=bg,
                 fg="#6f7c8a", wraplength=285, justify="left", font=("Segoe UI", 8)).pack(fill="x", padx=6, pady=(0, 6))
        self._fill_library()
        return f

    # ---- hover an object in the library: its 3D picture (library/thumb.py; game models: game_preview.py)
    def _lib_hover(self, ev):
        iid = self.lib_tree.identify_row(ev.y)
        e = road_layout.catalog().get(iid) if iid else None
        if e is None:
            return self._hover_hide()
        x = self.lib_tree.winfo_rootx() + self.lib_tree.winfo_width() + 8
        y = ev.y_root - 40
        if getattr(self, "_hover_id", None) == iid and self._hover_win is not None:
            self._hover_win.geometry("+%d+%d" % (x, y))
            return
        self._hover_id = iid
        self._hover_tries = 0
        self._hover_show(e, x, y)

    def _hover_picture(self, e):
        """(PIL picture or None, text when there is none); starts the reading of a game model in the background"""
        import game_preview
        import thumb
        cache = self.__dict__.setdefault("_hover_pics", {})
        if e["id"] in cache:
            return cache[e["id"]], "" if cache[e["id"]] is not None else "No picture"
        if e.get("kind") == "import":
            import model_import
            img = thumb.render(model_import.preview(e["model"]), seconds=1.5)     # (the hover never waits long)
            cache[e["id"]] = img
            return img, "" if img else "No picture (too big to draw quickly)"
        model = e.get("model")
        if not model:
            return None, "No picture"
        tp = game_preview.thumb_path(model)
        if os.path.exists(tp):
            from PIL import Image
            cache[e["id"]] = Image.open(tp).convert("RGB")
            return cache[e["id"]], ""
        ld = game_preview.loader()
        if ld.get(model) is not None:                  # read before (Real shapes): the picture is quick to make
            try:
                game_preview.make_thumb(model, seconds=1.5)                    # (the hover never waits long)
                return self._hover_picture(e)
            except Exception:                         # noqa: BLE001 - no picture then
                cache[e["id"]] = None
                return None, "No picture (too big to draw quickly)"
        if game_preview.key_of(model) in ld.failed:
            return None, "No picture (the game's model reader can't read this one)"
        return None, "Reading the model..."

    def _hover_show(self, e, x, y):
        from PIL import ImageTk
        import blur_theme as T
        self._hover_hide(keep_id=True)
        img, text = self._hover_picture(e)
        w = tk.Toplevel(self)
        w.overrideredirect(True)
        w.attributes("-topmost", True)
        w.configure(bg=T.EDGE)
        inner = tk.Frame(w, bg=T.PANEL)
        inner.pack(padx=1, pady=1)
        if img is not None:
            self._hover_photo = ImageTk.PhotoImage(img)
            tk.Label(inner, image=self._hover_photo, bg=T.PANEL).pack(padx=6, pady=(6, 2))
        else:
            tk.Label(inner, text=text, bg=T.PANEL, fg="#f0e080", width=24, height=6, wraplength=170,
                     font=("Segoe UI", 9)).pack(padx=6, pady=(6, 2))
        (x0, y0, z0), (x1, y1, z1) = e["lo"], e["hi"]
        tk.Label(inner, text=e["name"], bg=T.PANEL, fg=T.TEXT, font=("Segoe UI", 9, "bold"),
                 wraplength=180).pack(padx=6)
        tk.Label(inner, text="%.0f x %.0f x %.0f m" % (x1 - x0, z1 - z0, y1 - y0) if e.get("sized") else "",
                 bg=T.PANEL, fg=T.MUTED, font=("Segoe UI", 8)).pack(padx=6, pady=(0, 6))
        w.geometry("+%d+%d" % (x, y))
        self._hover_win = w
        if img is None and text.startswith("Reading"):     # ask again until the background reader has it
            iid = e["id"]
            self.after(500, lambda: self._hover_retry(iid, x, y))

    def _hover_retry(self, iid, x, y):
        if getattr(self, "_hover_id", None) != iid or self._hover_win is None:
            return
        e = road_layout.catalog().get(iid)
        if e is None:
            return
        self._hover_tries = getattr(self, "_hover_tries", 0) + 1
        if self._hover_tries > 20:                     # 10 s without a picture: skip it, no picture
            self.__dict__.setdefault("_hover_pics", {})[iid] = None
        self._hover_show(e, x, y)

    def _hover_hide(self, keep_id=False):
        w = getattr(self, "_hover_win", None)
        if w is not None:
            try:
                w.destroy()
            except tk.TclError:
                pass
        self._hover_win = None
        if not keep_id:
            self._hover_id = None

    def _bake(self):
        """Bake 3D pictures: every object of the library (press again = stop)"""
        import game_preview
        b = getattr(self, "_baker", None)
        if b is not None and b.running():
            b.stop = True
            self.bake_btn.config(text="\u2B1A Bake 3D pictures (stopping...)")
            return
        models = [e["model"] for e in road_layout.catalog().values() if e.get("kind") != "import" and e.get("model")]
        self._baker = game_preview.Baker(models)
        if not self._baker.total:
            self.bake_btn.config(text="\u2714 3D pictures baked", bg="#58d06a", fg="#0a0f18")
            return
        self.bake_btn.config(bg="#ff8a1e", fg="white")
        self._bake_tick()

    def _bake_tick(self):
        b = self._baker
        if b.running():
            self.bake_btn.config(text="\u23F8 Baking 3D pictures... %d / %d  (click = stop)" % (min(b.done, b.total),
                                                                                             b.total))
            self.after(400, self._bake_tick)
            return
        if b.stop:
            self.bake_btn.config(text="\u2B1A Bake 3D pictures (stopped - click to go on)", bg="#1e9bff", fg="white")
        else:
            self.bake_btn.config(text="\u2714 3D pictures baked%s" % (" (%d without a picture)" % b.failed
                                                                     if b.failed else ""), bg="#58d06a",
                                 fg="#0a0f18")
        self.__dict__.pop("_hover_pics", None)

    def _import_model(self):
        """pick a .glb -> the check window (size, texture, warnings) -> it joins "Your models" and is placed"""
        from tkinter import filedialog, messagebox
        import model_import
        top = self.lib_tree.winfo_toplevel()
        path = filedialog.askopenfilename(parent=top, title="Import a 3D model",
                                          filetypes=[("glTF Binary (.glb)", "*.glb"), ("All files", "*.*")])
        if not path:
            return
        top.config(cursor="watch")
        top.update()
        try:
            info = model_import.analyse(path)
        except model_import.ImportError_ as ex:
            messagebox.showerror("Import 3D model", str(ex), parent=top)
            return
        except Exception as ex:                       # noqa: BLE001 - a file we could not read
            messagebox.showerror("Import 3D model", "Could not read this file:\n%s" % ex, parent=top)
            return
        finally:
            top.config(cursor="")
        if not self._import_window(top, path, info):
            return
        try:
            cid = model_import.add(path, info)
        except (model_import.ImportError_, OSError) as ex:
            messagebox.showerror("Import 3D model", str(ex), parent=top)
            return
        road_layout.reload_catalog()
        self.lib_search.set("")
        self._fill_library()
        try:
            self.lib_tree.see(cid)
            self.lib_tree.selection_set(cid)           # -> _lib_select: click in the view to place it
        except tk.TclError:
            pass

    def _import_window(self, top, path, info):
        """the check: what the model is + the notes (green ok / blue info / yellow warning / red refused);
        True = import it"""
        import blur_theme as T
        import model_import
        colours = {"ok": "#58d06a", "info": T.HEAD, "warn": "#ffd23b", "stop": "#ff6b6b"}
        marks = {"ok": "✔", "info": "ℹ", "warn": "⚠", "stop": "✖"}
        w = tk.Toplevel(top)
        w.title("Import 3D model")
        w.configure(bg=T.BG)
        w.resizable(False, False)
        w.transient(top)
        head = tk.Frame(w, bg=T.PANEL)
        head.pack(fill="x")
        tk.Label(head, text="IMPORT 3D MODEL", bg=T.PANEL, fg=T.HEAD, font=("Segoe UI", 9, "bold")).pack(
            anchor="w", padx=16, pady=(10, 0))
        tk.Label(head, text=info["name"], bg=T.PANEL, fg=T.TEXT, font=("Segoe UI", 15, "bold")).pack(
            anchor="w", padx=16)
        sx, sy, sz = info["size"]
        tex = ", ".join("%d x %d" % t for t in info["textures"]) or "none"
        tk.Label(head, text="%d triangles   ·   %.1f x %.1f x %.1f m (wide x long x high)   ·   %d material(s)"
                            "   ·   texture %s" % (info["tris"], sx, sz, sy, info["materials"], tex),
                 bg=T.PANEL, fg=T.MUTED, font=("Segoe UI", 9)).pack(anchor="w", padx=16, pady=(0, 10))
        tk.Frame(w, bg=T.ORANGE, height=2).pack(fill="x")
        body = tk.Frame(w, bg=T.BG)
        body.pack(fill="x", padx=16, pady=10)
        for level, text in info["notes"]:
            row = tk.Frame(body, bg=T.BG)
            row.pack(fill="x", pady=2)
            tk.Label(row, text=marks[level], bg=T.BG, fg=colours[level], font=("Segoe UI Symbol", 10, "bold"),
                     width=2, anchor="n").pack(side="left", anchor="n")
            tk.Label(row, text=text, bg=T.BG, fg=T.TEXT if level != "stop" else colours[level], wraplength=470,
                     justify="left", font=("Segoe UI", 9)).pack(side="left", anchor="w")
        stop = model_import.blocked(info)
        if not stop:
            tk.Label(w, text="It joins the object library under \"Your models\" (kept with your maps). Then click in "
                             "the view to place it; G / R / S move, turn, scale it.", bg=T.BG, fg=T.MUTED,
                     wraplength=500, justify="left", font=("Segoe UI", 8)).pack(anchor="w", padx=16)
        foot = tk.Frame(w, bg=T.BG)
        foot.pack(fill="x", padx=16, pady=(8, 14))
        res = {"ok": False}

        def done(ok):
            res["ok"] = ok
            w.destroy()
        tk.Button(foot, text="Cancel" if not stop else "Close", command=lambda: done(False), bg=T.GLASS,
                  fg=T.TEXT, font=("Segoe UI", 10)).pack(side="right")
        if not stop:
            tk.Button(foot, text="⬆ Import + place", command=lambda: done(True), bg=T.ORANGE, fg="white",
                      font=("Segoe UI Symbol", 10, "bold")).pack(side="right", padx=(0, 8))
        w.update_idletasks()
        w.geometry("+%d+%d" % (top.winfo_rootx() + (top.winfo_width() - w.winfo_reqwidth()) // 2,
                               top.winfo_rooty() + (top.winfo_height() - w.winfo_reqheight()) // 3))
        w.grab_set()
        w.wait_window()
        return res["ok"]

    def _fill_library(self):
        t = self.lib_tree
        t.delete(*t.get_children())
        cat = road_layout.catalog()
        if not cat:
            self.lib_info.config(text="No library yet: run map_builder\\library\\make_catalog.py")
            return
        q = self.lib_search.get().strip().lower()
        for kind, title in self.KIND_TITLES:
            items = [e for e in cat.values() if e["kind"] == kind and
                     (not q or q in ("%s %s %s" % (e["name"], e["loc"], e["cat"])).lower())]
            if not items:
                continue
            top = t.insert("", "end", text="%s  (%d)" % (title, len(items)), open=bool(q), tags=(kind,))
            nodes = {}
            for e in sorted(items, key=lambda e: (e["loc"] != "Shared (all cities)", e["loc"], e["cat"], e["name"])):
                if e["loc"] not in nodes:
                    nodes[e["loc"]] = t.insert(top, "end", text=e["loc"], open=bool(q))
                ck = (e["loc"], e["cat"])
                if ck not in nodes:
                    nodes[ck] = t.insert(nodes[e["loc"]], "end", text=e["cat"], open=bool(q))
                (x0, y0, z0), (x1, y1, z1) = e["lo"], e["hi"]
                size = "%.0f x %.0f x %.0f m" % (x1 - x0, z1 - z0, y1 - y0) if e["sized"] else "size ?"
                t.insert(nodes[ck], "end", iid=e["id"], text="%s   (%s)" % (e["name"], size), tags=(kind,))

    def _lib_select(self):
        sel = self.lib_tree.selection()
        e = road_layout.catalog().get(sel[0]) if sel else None
        if e is None:                                  # a group: open / close it
            if sel:
                self.lib_tree.item(sel[0], open=not self.lib_tree.item(sel[0], "open"))
            return
        if self.sc is not None:                        # Path scatter open: the object joins its list
            self._sc_lib_add(e)
            self.lib_tree.selection_remove(*sel)
            return
        self.placing = e
        self.select(None)
        if self.gl is not None:
            self.gl.config(cursor="crosshair")
        self.lib_info.config(text="PLACING: %s\n%s\nclick in the view (Esc = stop)" % (
            e["name"], road_layout.KIND_TEXT.get(e["kind"], "")))
        self.info.config(text="Placing %s: click where it should stand" % e["name"], fg="#f0e080")

    def _stop_placing(self):
        if self.placing is None:
            return
        self.placing = None
        if self.gl is not None:
            self.gl.config(cursor="")
        self.lib_info.config(text="")
        try:
            self.lib_tree.selection_remove(*self.lib_tree.selection())
        except tk.TclError:
            pass

    def _place_at(self, mx, my):
        p = self._point_under(mx, my)
        if p is None or self.m is None:
            return
        g = self.scene.get("ground", 0.0) if self.scene else 0.0
        objs = [dict(o) for o in self.m.get("objects") or []]
        objs.append({"id": self.placing["id"], "x": round(float(p[0]), 2), "z": round(float(p[2]), 2),
                     "h": round(max(0.0, float(p[1]) - g), 2), "turn": 0.0})
        self.sel = "obj:%d" % (len(objs) - 1)
        self._set_objects(objs, self.edits())

    def delete_object(self, i):
        """remove placed object i (and its edits; the later objects' keys move down by one)"""
        self.sel = None
        self._set_objects(*self._without({i}, self.edits()))

    def _without(self, gone, edits):
        """(objects, edits) with the placed objects of index set `gone` removed (the later keys renumbered)"""
        old = self.m.get("objects") or []
        keep = [i for i in range(len(old)) if i not in gone]
        moved = {j: n for n, j in enumerate(keep)}
        new = {}
        for key, e in edits.items():
            if key.startswith("obj:"):
                j = int(key[4:])
                if j not in moved:
                    continue
                key = "obj:%d" % moved[j]
            new[key] = e
        return [dict(old[i]) for i in keep], new

    def delete_selection(self):
        """Delete: the selected object(s) - placed ones are removed, the road's own ones hidden; one Ctrl+Z undoes"""
        if not self.multi:
            self._hide()
            return
        if self.mode != "3d" or self.op:
            return
        edits = self.edits()
        gone, hidden = set(), 0
        for k in self.multi:
            if k.startswith("obj:"):
                gone.add(int(k[4:]))
                continue
            p = self._pick(k)
            if p is not None and p["editable"] and "point" not in p and "pickup" not in p:
                e = dict(edits.get(k, {}))
                e["hidden"] = True
                edits[k] = e
                hidden += 1
        if not gone and not hidden:
            return
        self.multi, self.sel = [], None
        self._set_objects(*self._without(gone, edits))
        self.info.config(text="Deleted %d object(s)%s - Ctrl+Z undoes" % (
            len(gone), ", hid %d of the road's own" % hidden if hidden else ""), fg="#f0e080")

    def _sel_objects(self):
        """indexes of the selected placed objects (several, or the one)"""
        keys = self.multi or ([self.sel] if self.sel else [])
        return sorted({int(k[4:]) for k in keys if k.startswith("obj:")})

    def _new_group_name(self, prefix):
        used = {o.get("group") for o in (self.m or {}).get("objects") or []}
        n = 1
        while "%s %d" % (prefix, n) in used:
            n += 1
        return "%s %d" % (prefix, n)

    def group_selection(self):
        """Ctrl+G: the selected placed objects become one group (a line in the list; click it = all of them)"""
        idx = self._sel_objects()
        if self.m is None or self.mode != "3d" or len(idx) < 2:
            self.info.config(text="Ctrl+G: select 2 or more of YOUR placed objects first (Shift / Ctrl+click in the "
                                  "list, Shift+click in the view)", fg="#f0e080")
            return
        name = self._new_group_name("Group")
        objs = [dict(o) for o in self.m.get("objects") or []]
        for i in idx:
            objs[i]["group"] = name
        self._outl_open.add("group:" + name)
        self._set_objects(objs, self.edits())
        self.info.config(text="%s: %d objects - click its line in the list to select them all" % (name, len(idx)),
                         fg="#f0e080")

    def ungroup_selection(self):
        """Ctrl+Shift+G: the selected objects leave their group"""
        idx = self._sel_objects()
        objs = [dict(o) for o in (self.m or {}).get("objects") or []]
        if self.mode != "3d" or not any(objs[i].get("group") for i in idx):
            return
        for i in idx:
            objs[i].pop("group", None)
        self._set_objects(objs, self.edits())
        self.info.config(text="Ungrouped %d object(s)" % len(idx), fg="#f0e080")

    def duplicate(self):
        """Ctrl+D: a copy of the selected placed object, a bit to the side (its turn / size / tilt / Solid too)"""
        if self.multi:
            self._duplicate_many()
            return
        p = self._pick(self.sel)
        if p is None or self.mode != "3d" or self.op or not p["key"].startswith("obj:"):
            self.info.config(text="Ctrl+D: select one of YOUR placed objects first (the road's own objects can't be "
                                  "copied)", fg="#f0e080")
            return
        i = int(p["key"][4:])
        objs = [dict(o) for o in self.m.get("objects") or []]
        if not 0 <= i < len(objs):
            return
        cs = np.array(box_corners(p), np.float64)
        o = dict(objs[i])
        o["x"] = round(float(o["x"]) + max(2.0, float(cs[:, 0].max() - cs[:, 0].min()) * 0.25), 2)
        objs.append(o)
        edits = self.edits()
        if p["key"] in edits:
            edits["obj:%d" % (len(objs) - 1)] = dict(edits[p["key"]])
        self.sel = "obj:%d" % (len(objs) - 1)
        self._set_objects(objs, edits)

    def _duplicate_many(self):
        """Ctrl+D with several selected: copies of all the placed ones, side by side as a set (their group -> a new
        group of the copies); the copies are selected"""
        idx = self._sel_objects()
        if self.m is None or self.mode != "3d" or self.op or not idx:
            return
        cs = [c for i in idx if self._pick("obj:%d" % i) for c in box_corners(self._pick("obj:%d" % i))]
        width = float(np.ptp(np.array(cs)[:, 0])) if cs else 0.0
        objs = [dict(o) for o in self.m.get("objects") or []]
        edits = self.edits()
        renamed, new = {}, []
        for i in idx:
            o = dict(objs[i])
            o["x"] = round(float(o["x"]) + max(2.0, width * 0.25), 2)
            if o.get("group"):
                if o["group"] not in renamed:
                    base = o["group"].rsplit(" ", 1)
                    name = self._new_group_name(base[0] if len(base) == 2 and base[1].isdigit() else o["group"])
                    while name in renamed.values():
                        name += "+"
                    renamed[o["group"]] = name
                    self._outl_open.add("group:" + name)
                o["group"] = renamed[o["group"]]
            objs.append(o)
            if "obj:%d" % i in edits:
                edits["obj:%d" % (len(objs) - 1)] = dict(edits["obj:%d" % i])
            new.append("obj:%d" % (len(objs) - 1))
        self.multi, self.sel = new, MULTI                 # (the copies: selected, with the group handles)
        self._set_objects(objs, edits)

    def _set_objects(self, objs, edits):
        if self.on_objects:
            self.on_objects(objs, edits)
        else:                                          # standalone viewer
            self.m["objects"] = objs
            self.m["edits"] = edits
            self.set_map(self.m)

    def show(self, on):
        if self.gl is not None:
            self.gl.animate = 16 if on else 0
            if on:
                self.gl.after(30, self.refresh)

    def refresh(self):
        if self.gl is not None:
            try:
                self.gl.tkExpose(None)
            except Exception:            # noqa: BLE001 - window not mapped yet
                pass

    # ---- camera
    def reset(self):
        x0, x1, z0, z1 = getattr(self, "bounds", (-500, 500, -500, 500))
        self.target = [(x0 + x1) / 2, 0.0, (z0 + z1) / 2]
        self.dist = max(x1 - x0, z1 - z0) * 1.1 + 200
        self.yaw, self.pitch = -0.6, 0.55
        self.drive = self.cam_anim = None
        self.refresh()

    def top(self):
        self.reset()
        self.yaw, self.pitch = 0.0, 1.55
        self.refresh()

    def start_drive(self):
        if self.path:
            self.drive = time.time()
            self.show(True)

    def stop_drive(self):
        self.drive = None
        self.cam_anim = None

    # ---- start camera (intro_cam.py): the user's views per shot; Play = what the game shows before the countdown
    def _view_eye(self):
        """(eye, look) of the orbit camera now, world coordinates"""
        tx, ty, tz = self.target
        cp = math.cos(self.pitch)
        eye = [tx + self.dist * cp * math.sin(self.yaw), ty + self.dist * math.sin(self.pitch),
               tz + self.dist * cp * math.cos(self.yaw)]
        return eye, [tx, ty, tz]

    def _cam_views(self):
        return {k: list(v) for k, v in ((self.m or {}).get("start_cams") or {}).items() if v}

    def _cam_store(self, cams):
        if self.on_start_cams:
            self.on_start_cams(cams)
        elif self.m is not None:                       # standalone viewer
            self.m["start_cams"] = cams
        self._cam_info()

    def _cam_info(self, extra=""):
        import intro_cam
        cams = self._cam_views()
        parts = []
        for shot, label, _, _ in intro_cam.SHOTS:
            n = len(cams.get(shot) or [])
            parts.append("%s: %s" % (label, "%d view%s" % (n, "" if n == 1 else "s") if n else "auto"))
        self.cam_label.config(text="   ".join(parts) + (extra or "   (place the view, Add view; Play = as in game)"))

    def cam_add(self):
        import intro_cam
        if self.m is None:
            return
        self.drive = self.cam_anim = None
        cams = self._cam_views()
        shot = self.cam_shot.get()
        views = cams.get(shot) or []
        if len(views) >= intro_cam.MAX_VIEWS:
            self._cam_info("   - at most %d views per shot" % intro_cam.MAX_VIEWS)
            return
        eye, look = self._view_eye()
        views.append({"eye": [round(v, 2) for v in eye], "look": [round(v, 2) for v in look]})
        cams[shot] = views
        self._cam_store(cams)
        ground = (self.scene or {}).get("ground", 0.0)
        far = math.dist(eye, look)
        self._cam_info("   - view %d: camera %.0f m up, %.0f m from what it looks at%s" % (
            len(views), eye[1] - ground, far, "  (FAR: zoom in closer - in game only the road shows from so far)"
            if far > 250 or eye[1] - ground > 120 else ""))

    def cam_remove(self):
        cams = self._cam_views()
        shot = self.cam_shot.get()
        if cams.get(shot):
            cams[shot].pop()
            if not cams[shot]:
                del cams[shot]
            self._cam_store(cams)

    def cam_auto(self):
        cams = self._cam_views()
        if cams.pop(self.cam_shot.get(), None) is not None:
            self._cam_store(cams)

    def cam_play(self):
        """show the start camera (fly-in, then the grid shot) exactly as the build makes it"""
        if self.m is None or self.gl is None:
            return
        os.environ.setdefault("FE_DRY", "1")
        import build as fe
        import gen_aid
        import intro_cam
        try:
            r = fe.map_road(self.m)
            s0 = r.dist[gen_aid.race_lines(r, self.m)[0]]
            frames = intro_cam.play_frames(r, s0, fe.GROUND_Y, self.m)
        except Exception as ex:                        # noqa: BLE001 - shown in the bar
            self._cam_info("   - cannot play: %s" % ex)
            return
        self.drive = None
        self.cam_anim = (time.time(), frames)
        self.show(True)

    def _mid_press(self, e):
        self.gl.focus_set()
        mode = "move" if e.state & 0x0001 else ("zoom" if e.state & 0x0004 else "turn")   # Shift / Ctrl
        self.drag = (mode, e.x, e.y)
        self.drive = self.cam_anim = None
        self._pivot = self._point_under(e.x, e.y) if mode == "turn" else None
        if mode == "move" and not self.ortho:            # pan at the depth of what is under the mouse: the grabbed
            p = self._point_under(e.x, e.y)              # thing follows the mouse, near or far
            if p is not None and self._depth_of(p) > 0.5:
                self._keep_eye(self._depth_of(p))

    def _point_under(self, mx, my):
        """the world point under the mouse (whatever is drawn there: road, ship, ground...), or None = nothing there.
        Draws the frame again into the hidden buffer and reads its depth at the mouse."""
        if self.gl is None or self.scene is None:
            return None
        try:
            self.gl.tkMakeCurrent()
            w, h = self.gl.winfo_width(), self.gl.winfo_height()
            snap, self.snapshot = getattr(self, "snapshot", None), None
            self.draw(w, h)
            self.snapshot = snap
            z = gl.glReadPixels(int(mx), int(h - 1 - my), 1, 1, gl.GL_DEPTH_COMPONENT, gl.GL_FLOAT)
            z = float(np.asarray(z, dtype=np.float32).reshape(-1)[0])
        except Exception:                # noqa: BLE001 - no depth: fall back to the ground
            z = 1.0
        if z < 0.999999:
            return self._unproject(mx, my, 2.0 * z - 1.0)
        return self._ground_hit(mx, my, self.scene.get("ground", 0.0)) if self.scene else None

    def _orbit(self, dyaw, dpitch, pivot):
        """turn the view by dyaw / dpitch round the world point `pivot` (Blender: orbit round the point under the
        mouse): the camera keeps looking the same way relative to the scene, nothing jumps"""
        def direction(yaw, pitch):
            cp = math.cos(pitch)
            return np.array((cp * math.sin(yaw), math.sin(pitch), cp * math.cos(yaw)))
        t = np.array(self.target, float)
        pv = np.array(pivot, float)
        # 1. yaw: round the vertical line through the pivot
        c, s = math.cos(dyaw), math.sin(dyaw)
        r = t - pv
        t = pv + np.array((r[0] * c + r[2] * s, r[1], -r[0] * s + r[2] * c))
        self.yaw += dyaw
        # 2. pitch: round the camera's horizontal side axis through the pivot (Rodrigues)
        new_pitch = min(1.55, max(-1.55, self.pitch + dpitch))
        d0, d1 = direction(self.yaw, self.pitch), direction(self.yaw, new_pitch)
        ax = np.cross(d0, d1)
        sn = np.linalg.norm(ax)
        if sn > 1e-9:
            ax /= sn
            ang = math.atan2(sn, float(np.dot(d0, d1)))
            r = t - pv
            r = r * math.cos(ang) + np.cross(ax, r) * math.sin(ang) + ax * float(np.dot(ax, r)) * (1 - math.cos(ang))
            t = pv + r
        self.pitch = new_pitch
        self.target = [float(v) for v in t]

    def view_axis(self, which):
        """numpad views (Blender): top / bottom / front / back / right / left"""
        self.drive = None
        self.yaw, self.pitch = {"top": (0.0, 1.55), "bottom": (0.0, -1.55), "front": (0.0, 0.0),
                                "back": (math.pi, 0.0), "right": (math.pi / 2, 0.0), "left": (-math.pi / 2, 0.0)}[which]
        self.refresh()

    def toggle_ortho(self):
        self.ortho = not self.ortho
        self.refresh()

    def orbit_step(self, dx, dy):
        self.yaw -= dx * math.radians(15)
        self.pitch = min(1.55, max(-1.55, self.pitch + dy * math.radians(15)))
        self.refresh()

    def zoom_step(self, k):
        self.dist = min(40000.0, max(5.0, self.dist * k))
        self.refresh()

    def _release(self):
        self.drag = None

    def _motion(self, e):
        if not self.drag:
            return
        mode, px, py = self.drag
        dx, dy = e.x - px, e.y - py
        self.drag = (mode, e.x, e.y)
        if mode == "turn":
            pivot = getattr(self, "_pivot", None)
            if pivot is None:                            # nothing under the mouse: round the view centre
                self.yaw -= dx * 0.008
                self.pitch = min(1.55, max(-1.55, self.pitch + dy * 0.006))
            else:
                self._orbit(-dx * 0.008, dy * 0.006, pivot)
        elif mode == "zoom":
            new = self.dist * (1.0 + dy * 0.01)
            if new < 2.0:                                # very close: fly on forward instead of stopping
                d = self._view_dir()
                self.target = [self.target[i] + d[i] * max(0.3, -dy * 0.05) for i in range(3)]
            else:
                self.dist = min(40000.0, new)
        else:
            # pan like Blender: the point at the centre of the view follows the mouse, whatever the camera angle
            # (screen -> world through the last frame's matrices; the old fixed formula went sideways at some angles)
            c = self._screen(self.target, depth=True)
            if c is not None:
                a = self._unproject(c[0], c[1], c[2])
                b = self._unproject(c[0] - dx, c[1] - dy, c[2])
                if a is not None and b is not None:
                    self.target = [float(self.target[i] + b[i] - a[i]) for i in range(3)]
        self.refresh()

    # ---- eye / depth helpers (the view = an orbit: target, yaw, pitch, dist)
    def _eye(self):
        return self._view_eye()[0]

    def _view_dir(self):
        cp = math.cos(self.pitch)
        return [-cp * math.sin(self.yaw), -math.sin(self.pitch), -cp * math.cos(self.yaw)]

    def _keep_eye(self, depth):
        """move the orbit centre along the view to `depth` m in front of the eye (the camera stays put): turning,
        panning and zooming then work at that distance"""
        eye, d = self._eye(), self._view_dir()
        depth = max(0.5, depth)
        self.target = [eye[i] + d[i] * depth for i in range(3)]
        self.dist = depth

    def _depth_of(self, p):
        """distance of world point p in front of the eye, along the view"""
        eye, d = self._eye(), self._view_dir()
        return sum((p[i] - eye[i]) * d[i] for i in range(3))

    def _wheel(self, e):
        """zoom TOWARDS the thing under the mouse (Blender style) and never stop: each notch moves the eye 18 % of the
        way to it (at least 1 m, so it flies on through when very close); out = back the same way"""
        self.drive = self.cam_anim = None
        p = self._point_under(e.x, e.y) if self.scene else None
        eye = self._eye()
        hit = p is not None and self._depth_of(p) > 0.5
        if not hit:
            p = self.target                              # nothing there: towards the view centre (flies on)
        v = [p[i] - eye[i] for i in range(3)]
        ln = math.sqrt(sum(x * x for x in v)) or 1.0
        step = max(1.0, ln * 0.18) if e.delta > 0 else -max(1.0, ln * 0.22)
        if hit and e.delta > 0:
            step = min(step, ln - 1.5)                   # never into / through what is under the mouse
            if step <= 0.0:
                return
        if self.ortho:                                   # flat view: zooming = the scale only
            self.dist = min(40000.0, max(5.0, self.dist * (0.85 if e.delta > 0 else 1.18)))
            self.refresh()
            return
        new_eye = [eye[i] + v[i] / ln * step for i in range(3)]
        d = self._view_dir()
        depth = sum((p[i] - new_eye[i]) * d[i] for i in range(3))
        if depth < 0.5:                                  # passed it: keep a few metres ahead
            depth = max(5.0, ln * 0.5)
        self.target = [new_eye[i] + d[i] * depth for i in range(3)]
        self.dist = min(40000.0, depth)
        self.refresh()

    # ---- drawing
    @staticmethod
    def _box_lines(cs, col):
        gl.glDisable(gl.GL_DEPTH_TEST)
        gl.glLineWidth(2.0)
        gl.glColor4f(*col)
        gl.glBegin(gl.GL_LINES)
        # corner index: bit0 x, bit1 z, bit2 y
        for a, b in ((0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7)):
            gl.glVertex3f(*cs[a])
            gl.glVertex3f(*cs[b])
        gl.glEnd()
        gl.glLineWidth(1.0)
        gl.glEnable(gl.GL_DEPTH_TEST)

    def _gl_texture(self, key, image):
        """the GL texture of an imported model's material (made once per picture, mipmapped, repeating)"""
        cache = self.__dict__.setdefault("_gl_textures", {})
        if key not in cache:
            tid = gl.glGenTextures(1)
            gl.glBindTexture(gl.GL_TEXTURE_2D, tid)
            gl.glPixelStorei(gl.GL_UNPACK_ALIGNMENT, 1)
            for p_, v_ in ((gl.GL_TEXTURE_WRAP_S, gl.GL_REPEAT), (gl.GL_TEXTURE_WRAP_T, gl.GL_REPEAT),
                           (gl.GL_TEXTURE_MAG_FILTER, gl.GL_LINEAR),
                           (gl.GL_TEXTURE_MIN_FILTER, gl.GL_LINEAR_MIPMAP_LINEAR)):
                gl.glTexParameteri(gl.GL_TEXTURE_2D, p_, v_)
            glu.gluBuild2DMipmaps(gl.GL_TEXTURE_2D, gl.GL_RGB, image.width, image.height, gl.GL_RGB,
                                  gl.GL_UNSIGNED_BYTE, image.tobytes())
            cache[key] = tid
        return cache[key]

    def draw(self, w, h):
        gl.glViewport(0, 0, max(w, 1), max(h, 1))
        gl.glClear(gl.GL_COLOR_BUFFER_BIT | gl.GL_DEPTH_BUFFER_BIT)
        gl.glMatrixMode(gl.GL_PROJECTION)
        gl.glLoadIdentity()
        aspect = max(w, 1) / max(h, 1)
        cam = None
        if self.cam_anim is not None:                       # Play: the start camera, frame by frame (30 fps)
            f = int((time.time() - self.cam_anim[0]) * 30)
            if f < len(self.cam_anim[1]):
                cam = self.cam_anim[1][f]
            else:
                self.cam_anim = None
        if cam is not None:
            glu.gluPerspective(math.degrees(cam[2]), aspect, 0.5, 60000.0)
        elif self.ortho and self.drive is None:              # flat view (numpad 5): no perspective
            half = self.dist * 0.45
            gl.glOrtho(-half * aspect, half * aspect, -half, half, -60000.0, 60000.0)
        else:
            glu.gluPerspective(55.0, aspect, 1.0, 60000.0)
        gl.glMatrixMode(gl.GL_MODELVIEW)
        gl.glLoadIdentity()
        if cam is not None:
            import intro_cam
            eye, q, _ = cam
            fw, up = intro_cam.rotate(q, (0, 0, 1)), intro_cam.rotate(q, (0, 1, 0))
            glu.gluLookAt(eye[0], eye[1], -eye[2], eye[0] + fw[0], eye[1] + fw[1], -(eye[2] + fw[2]),
                          up[0], up[1], -up[2])
        elif self.drive is not None and self.path:
            k = (time.time() - self.drive) * 14.0          # path points per second (4 m each: ~200 km/h)
            i = int(k)
            if i >= len(self.path) - 6:
                self.drive = None
                i = len(self.path) - 6
            x, y, z, a = self.path[max(i, 0)]
            lx, ly, lz, _ = self.path[min(i + 6, len(self.path) - 1)]
            glu.gluLookAt(x - 14 * math.sin(a), y + 6, -(z - 14 * math.cos(a)), lx, ly + 2, -lz, 0, 1, 0)
        else:
            tx, ty, tz = self.target
            cp = math.cos(self.pitch)
            ex = tx + self.dist * cp * math.sin(self.yaw)
            ez = tz + self.dist * cp * math.cos(self.yaw)
            ey = ty + self.dist * math.sin(self.pitch)
            up = (0, 1, 0) if abs(self.pitch) < 1.5 else \
                (math.sin(self.yaw) * (1 if self.pitch > 0 else -1), 0, -math.cos(self.yaw) * (1 if self.pitch > 0 else -1))
            glu.gluLookAt(ex, ey, -ez, tx, ty, -tz, *up)
        if not self.scene:
            return
        gl.glScalef(1.0, 1.0, -1.0)                         # world z -> drawn -z (the 3D view matches the 2D map)
        mv = np.array(gl.glGetDoublev(gl.GL_MODELVIEW_MATRIX)).reshape(4, 4).T
        pj = np.array(gl.glGetDoublev(gl.GL_PROJECTION_MATRIX)).reshape(4, 4).T
        try:
            self._fwd = pj @ mv
            self._mats = (np.linalg.inv(self._fwd), max(w, 1), max(h, 1))
        except np.linalg.LinAlgError:
            self._mats = None
        sc = self._solo_scene(self.scene) if self.solo is not None else self.scene
        gl.glEnableClientState(gl.GL_VERTEX_ARRAY)
        gl.glEnableClientState(gl.GL_COLOR_ARRAY)
        for v, c, prim in ((sc["v"], sc["c"], gl.GL_TRIANGLES), (sc["lv"], sc["lc"], gl.GL_LINES)):
            if len(v):
                gl.glVertexPointer(3, gl.GL_FLOAT, 0, v)
                gl.glColorPointer(4, gl.GL_FLOAT, 0, c)
                gl.glDrawArrays(prim, 0, len(v))
        if sc.get("textured"):                              # imported models: their own textures
            gl.glEnable(gl.GL_TEXTURE_2D)
            gl.glEnableClientState(gl.GL_TEXTURE_COORD_ARRAY)
            for t in sc["textured"]:
                gl.glBindTexture(gl.GL_TEXTURE_2D, self._gl_texture(t["key"], t["image"]))
                gl.glVertexPointer(3, gl.GL_FLOAT, 0, t["v"])
                gl.glColorPointer(4, gl.GL_FLOAT, 0, t["c"])
                gl.glTexCoordPointer(2, gl.GL_FLOAT, 0, t["uv"])
                gl.glDrawArrays(gl.GL_TRIANGLES, 0, len(t["v"]))
            gl.glDisableClientState(gl.GL_TEXTURE_COORD_ARRAY)
            gl.glBindTexture(gl.GL_TEXTURE_2D, 0)
            gl.glDisable(gl.GL_TEXTURE_2D)
        if len(sc["tv"]):                                   # see-through walls last, without writing depth
            gl.glDepthMask(gl.GL_FALSE)
            gl.glVertexPointer(3, gl.GL_FLOAT, 0, sc["tv"])
            gl.glColorPointer(4, gl.GL_FLOAT, 0, sc["tc"])
            gl.glDrawArrays(gl.GL_TRIANGLES, 0, len(sc["tv"]))
            gl.glDepthMask(gl.GL_TRUE)
        gl.glDisableClientState(gl.GL_VERTEX_ARRAY)
        gl.glDisableClientState(gl.GL_COLOR_ARRAY)
        p = self._pick(self.sel)
        op = self.op or {}
        if self.mode == "3d" and self.solo is None:         # track points: dots on top of everything
            gl.glDisable(gl.GL_DEPTH_TEST)
            gl.glPointSize(9.0)
            gl.glBegin(gl.GL_POINTS)
            for q in self.picks():
                if "point" in q:
                    gl.glColor3f(*((1, .95, .2) if q is p else q["col"]))
                    d = op.get("delta", (0, 0, 0)) if q is p else (0, 0, 0)
                    gl.glVertex3f(q["pos"][0] + d[0], q["pos"][1] + d[1] + 0.5, q["pos"][2] + d[2])
            gl.glEnd()
            gl.glPointSize(1.0)
            gl.glEnable(gl.GL_DEPTH_TEST)
        self._sc_draw_gl()
        movable = {q["key"] for q in self._members()} if p is not None and p.get("multi") else set()
        for k in self.multi:                                # several selected: a yellow box each
            q = self._pick(k)
            if q is not None and "point" not in q:
                if op and k in movable:                     # (moving with the group: where it goes + a dim box)
                    d, t, s = self._member_change(p, q, op)
                    self._box_lines(box_corners(q), (.6, .6, .3, .5))
                    self._box_lines(box_corners(q, d, t, s), (1, .9, .1, 1))
                else:
                    self._box_lines(box_corners(q), (1, .9, .1, 1))
        if p is not None:                                   # the selection: a yellow box (seen through everything)
            group = [q for q in self.picks() if "pickup" in q and q["pickup"] == p["pickup"]] if "pickup" in p \
                else ([] if "point" in p else [p])         # a power-up row: all its lanes light up
            for q in group:
                cs = box_corners(q, op.get("delta", (0, 0, 0)), op.get("dturn", 0.0), op.get("dscale", 1.0),
                                 op.get("drot", (0.0, 0.0)))
                if op and not q.get("multi"):               # where it was: a dim box
                    self._box_lines(box_corners(q), (.6, .6, .3, .5))
                self._box_lines(cs, (.45, .8, 1, .8) if q.get("multi") else (1, .9, .1, 1))   # (several: blue)
            self._draw_handles()
        if getattr(self, "snapshot", None):                # save what was drawn (testing / sharing)
            path, self.snapshot = self.snapshot, None
            data = gl.glReadPixels(0, 0, w, h, gl.GL_RGB, gl.GL_UNSIGNED_BYTE)
            from PIL import Image
            Image.frombytes("RGB", (w, h), data).transpose(Image.FLIP_TOP_BOTTOM).save(path)


if __name__ == "__main__":                 # standalone: python view3d.py  (first map of tracks/world.json)
    import world as world_mod
    root = tk.Tk()
    root.title("Flat Earth 3D view")
    v = View3D(root)
    v.pack(fill="both", expand=True)
    w = world_mod.load()
    root.after(200, lambda: (v.set_map(w["locations"][0]["maps"][0]), v.show(True)))
    root.mainloop()

"""MINI CAR ICON (car select list, race HUD): the game draws each car as a flat side silhouette, nose to the
left, with its blue glow. They are vector shapes in root.pak \\ui\\data\\ipf\\car_icons_a..d.ipf, one per car,
named by the car's Name in lowercase (e.g. "opel_astraextreme_stock").

.ipf (see flat_earth/minimap.py): header (u32 0x100000, u32 1), then nodes {u32 kind (3 group / 1 shape), u32
flags, u32 name length, name}; a group is followed by u32 child count + the children. A shape: 34 header bytes
(colour white, style), 6 floats box (u0, v0, 0, u1, v1, 0), 16 bytes, u32 n + n convex polygons {u32 k, k x 6
floats (u, v, a, b, c, d)}, u32 n + n index lists {u32 k, k x u16}, u32 0. Plain fill points are (u, v, 0, 0, 1, 0)
(the game's own also hold curve points). Car icons: u 0 .. 1 = the car's length, v 0 (top) .. height / length.

Per custom car (world["cars"][i]["mini_icon"]): AUTO = made from the car's side view (car_model.car_parts), or the
name of a game car's silhouette (copied under our name). Not set: AUTO for an imported body, else the body car's."""
import struct

import numpy as np

import car_model

AUTO = "auto"
SHEETS = ["\\ui\\data\\ipf\\car_icons_%s.ipf" % c for c in "abcd"]   # one per class (A B C D); a car's shape goes
                                                                     # into its class's sheet ("layer 1" group)
WIDE = 480                        # side view raster width (pixels = the icon's length)
_HEAD = bytes.fromhex("010000000000803f0000803f0000803f0e0010000f000f001000110000000000ff00")   # the Astra's


def _sheet(path):
    import game_preview
    return game_preview._model_bytes(path.lstrip("\\"))


def _skip(d, p):
    """end of the node at p"""
    kind, flags, nl = struct.unpack_from("<III", d, p)
    q = p + 12 + nl
    if kind == 3:
        n = struct.unpack_from("<I", d, q)[0]
        q += 4
        for _ in range(n):
            q = _skip(d, q)
        return q
    q += 34 + 24 + 16
    for item in (24, 2):                        # polygons (k points of 6 floats), then index lists (k u16)
        n = struct.unpack_from("<I", d, q)[0]
        q += 4
        for _ in range(n):
            k = struct.unpack_from("<I", d, q)[0]
            q += 4 + k * item
    return q + 4


def _top(d):
    """(group child-count offset, [(name, start, end)]) of a car icon sheet's one top group"""
    nl = struct.unpack_from("<I", d, 16)[0]
    at = 20 + nl
    n = struct.unpack_from("<I", d, at)[0]
    out, p = [], at + 4
    for _ in range(n):
        q = _skip(d, p)
        nl = struct.unpack_from("<I", d, p + 8)[0]
        out.append((d[p + 12:p + 12 + nl].decode("latin1"), p, q))
        p = q
    return at, out


_shapes = None


def game_shapes():
    """{car name lowercase: its shape node bytes} of every game car"""
    global _shapes
    if _shapes is None:
        _shapes = {}
        for path in SHEETS:
            d = _sheet(path)
            for name, a, b in _top(d)[1]:
                _shapes.setdefault(name.lower(), d[a:b])
    return _shapes


def renamed(node, name):
    nl = struct.unpack_from("<I", node, 8)[0]
    nb = name.encode("latin1")
    return node[:8] + struct.pack("<I", len(nb)) + nb + node[12 + nl:]


def side_mask(parts):
    """L picture (WIDE x height): the car seen from its left side, nose to the left, each wheel with a thin dark
    ring around it (the game's icons look like that)"""
    from PIL import Image, ImageDraw
    tris = np.concatenate([t for k, t in parts.items() if k != "glb_rgb"]).astype(np.float64)
    fwd, up = tris[..., 2], tris[..., 1]
    f1, u0, u1 = fwd.max(), up.min(), up.max()
    L = f1 - fwd.min()
    sc = (WIDE - 1) / L
    h = int(np.ceil((u1 - u0) * sc)) + 1
    im = Image.new("L", (WIDE, h), 0)
    d = ImageDraw.Draw(im)
    xs, ys = (f1 - fwd) * sc, (u1 - up) * sc
    for x, y in zip(xs, ys):
        d.polygon([(x[0], y[0]), (x[1], y[1]), (x[2], y[2])], fill=255)
    tyres = parts.get("tyre")
    if tyres is not None:
        pts = tyres.reshape(-1, 3)
        for front in (True, False):
            w = pts[(pts[:, 2] > (f1 - L / 2)) == front]
            if not len(w):
                continue
            cx, cy = (f1 - (w[:, 2].min() + w[:, 2].max()) / 2) * sc, (u1 - (w[:, 1].min() + w[:, 1].max()) / 2) * sc
            r = (w[:, 1].max() - w[:, 1].min()) / 2 * sc
            g = max(2.0, 0.012 * WIDE)
            d.ellipse([cx - r - g, cy - r - g, cx + r + g, cy + r + g], fill=0)
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    return im


def mask_shape(name, im):
    """the mask as a fill shape: rectangles of runs that go on unchanged over several rows (like the minimap)"""
    w, h = im.size
    px = np.asarray(im) > 127
    rects, open_runs = [], {}
    for y in range(h + 1):
        runs = []
        if y < h:
            row = np.r_[False, px[y], False]
            edges = np.flatnonzero(row[1:] != row[:-1])
            runs = list(zip(edges[::2], edges[1::2]))
        nxt = {}
        for run in runs:
            nxt[run] = open_runs.pop(run, y)
        for (x0, x1), y0 in open_runs.items():
            rects.append((x0, y0, x1, y))
        open_runs = nxt
    k = 1.0 / w
    polys = [[(x0 * k, y0 * k), (x1 * k, y0 * k), (x1 * k, y1 * k), (x0 * k, y1 * k)]      # counter-clockwise
             for x0, y0, x1, y1 in rects]                                                    # in (u, v), as minimap
    nb = name.encode("latin1")
    out = struct.pack("<III", 1, 1, len(nb)) + nb + _HEAD
    out += struct.pack("<6f", 0.0, 0.0, 0.0, 1.0, h * k, 0.0) + struct.pack("<4f", 0, 0, 0, 0)
    out += struct.pack("<I", len(polys))
    for p in polys:
        out += struct.pack("<I", 4) + b"".join(struct.pack("<6f", u, v, 0.0, 0.0, 1.0, 0.0) for u, v in p)
    out += struct.pack("<I", len(polys))
    for p in polys:
        out += struct.pack("<I", 6) + struct.pack("<6H", 3, 0, 1, 1, 2, 3)
    return out + struct.pack("<I", 0)


def choice(car):
    """AUTO or a game car's shape name"""
    c = car.get("mini_icon")
    if c and (c == AUTO or c.lower() in game_shapes()):
        return c if c == AUTO else c.lower()
    return AUTO if car.get("body_glb") else car["base"].lower()


def shape_of(car, name):
    """our car's shape node (named `name`)"""
    c = choice(car)
    if c == AUTO:
        return mask_shape(name.lower(), side_mask(car_model.car_parts(car)))
    return renamed(game_shapes()[c], name.lower())


def preview(car, width=240):
    """small RGBA picture of the mini icon (blue on transparent) for the tab"""
    from PIL import Image
    c = choice(car)
    if c == AUTO:
        im = side_mask(car_model.car_parts(car))
    else:
        im = draw(game_shapes()[c])
    im = im.resize((width, max(1, int(im.size[1] * width / im.size[0]))), Image.LANCZOS)
    out = Image.new("RGBA", im.size, (90, 180, 255, 0))
    out.putalpha(im)
    return out


def draw(node, width=WIDE):
    """L picture of a shape node (only its plain polygons: good enough for a preview)"""
    from PIL import Image, ImageDraw
    nl = struct.unpack_from("<I", node, 8)[0]
    if struct.unpack_from("<I", node, 0)[0] == 3:              # a group: its first shape
        return draw(node[12 + nl + 4:], width)
    q = 12 + nl + 34
    bb = struct.unpack_from("<6f", node, q)
    q += 40
    im = Image.new("L", (width, max(1, int(np.ceil(bb[4] * width)) + 1)), 0)
    d = ImageDraw.Draw(im)
    n = struct.unpack_from("<I", node, q)[0]
    q += 4
    for _ in range(n):
        k = struct.unpack_from("<I", node, q)[0]
        v = np.array(struct.unpack_from("<%df" % (6 * k), node, q + 4)).reshape(k, 6)
        q += 4 + 24 * k
        d.polygon([(a * width, b * width) for a, b in v[:, :2]], fill=255)
    return im


def files(wld, names, log=print):
    """root.pak {path: bytes}: the car icon sheet with one shape per custom car (names = their game Names)"""
    cars = wld.get("cars") or []
    out = {}
    for sheet in sorted({SHEETS[c["class"]] for c in cars}):     # each class has its own sheet (a = class A ...):
        d = bytearray(_sheet(sheet))                              # a class B car in sheet a was not found (2026-10-28)
        at, kids = _top(bytes(d))
        have = {n.lower() for n, a, b in kids}
        add = b""
        count = len(kids)
        for car, name in zip(cars, names):
            if SHEETS[car["class"]] != sheet or name.lower() in have:
                continue
            add += shape_of(car, name)
            count += 1
            log("   mini icon %s (class %s): %s" % (name, "ABCD"[car["class"]],
                                                    "side view" if choice(car) == AUTO else "like " + choice(car)))
        struct.pack_into("<I", d, at, count)
        out[sheet] = bytes(d) + add
    return out

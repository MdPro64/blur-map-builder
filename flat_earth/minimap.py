"""In-race minimap of our locations (root.pak, looked up by the CITY name, lowercase):

  ui\\data\\minimap\\<city>.texture             1280 x 720 DXT1 picture of the whole city area (the city bounds)
  ui\\data\\ipf\\minimap\\<city>_game.ipf        vector shapes: "bounds" + per route "<route>" (the road ribbon) and
                                               "<route>_alt" (the start line)

Decoded from LA River (2026-10-14):
- City.boundsMin / boundsMax (x, world z) are 16:9 and cover the minimap. Shape coordinates are normalised by the
  bounds WIDTH: u = (x - minX) / width, v = (maxZ - z) / width (so v runs 0 .. 0.5625, top = north = max z).
- .ipf = header (u32 0x100000, u32 1) + a tree of nodes {u32 kind (3 group / 1 shape), u32 flags, u32 name length,
  name}; a group is followed by u32 child count. A FILL shape: u32 1, 3 floats colour, 16 bytes (unused / style),
  u16 style, 6 floats bbox (u0, v0, 0, u1, v1, 0), 16 bytes, u32 polygon count + polygons {u32 n, n x 6 floats
  (u, v, 0, 0, 1, 0)}, u32 count + triangle lists {u32 n, n x u16 vertex index}, u32 0.
- .texture = 0x54-byte header ("Texture" / "Header" / "Data" chunks, 1280 x 720, DXT1) + the DXT1 data.
"""
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tex"))
import dxt   # noqa: E402

W, H = 1280, 720
ASPECT = H / W                                   # 0.5625
SRC_GAME_IPF = "\\ui\\data\\ipf\\minimap\\usa_la_river_game.ipf"
SRC_TEXTURE = "\\ui\\data\\minimap\\usa_la_river.texture"
# in usa_la_river_game.ipf: the bounds node and the route / start-line shape headers we copy
_BOUNDS = (0x33, 0x1D3)
_ALT_NODE, _ROUTE_NODE = 0x1D3, 0x58B


def game_ipf_name(city):
    return "\\ui\\data\\ipf\\minimap\\%s_game.ipf" % city.lower()


def texture_name(city):
    return "\\ui\\data\\minimap\\%s.texture" % city.lower()


def city_bounds(roads, margin=1000.0):
    """16:9 bounds (x0, x1, z0, z1) around all the location's roads + margin"""
    x0 = min(r.bounds()[0] for r in roads) - margin
    x1 = max(r.bounds()[1] for r in roads) + margin
    z0 = min(r.bounds()[2] for r in roads) - margin
    z1 = max(r.bounds()[3] for r in roads) + margin
    w, h = x1 - x0, z1 - z0
    if h < w * ASPECT:
        grow = (w * ASPECT - h) / 2
        z0, z1 = z0 - grow, z1 + grow
    else:
        grow = (h / ASPECT - w) / 2
        x0, x1 = x0 - grow, x1 + grow
    return x0, x1, z0, z1


def _uv(b, x, z):
    x0, x1, z0, z1 = b
    return (x - x0) / (x1 - x0), (z1 - z) / (x1 - x0)


def _header(src, node):
    """the 34 header bytes after a shape node's name"""
    nl = struct.unpack_from("<I", src, node + 8)[0]
    p = node + 12 + nl
    return src[p:p + 34]


def _node(name, flags, kind=1):
    nb = name.encode()
    return struct.pack("<III", kind, flags, len(nb)) + nb


def _fill_shape(name, header, polys, extra=None):
    """polys = [[(u, v), ...] convex, counter-clockwise in (u, v)]"""
    us = [u for p in polys for u, v in p]
    vs = [v for p in polys for u, v in p]
    bb = (min(us), min(vs), 0.0, max(us), max(vs), 0.0)
    out = _node(name, 2) + header + struct.pack("<6f", *bb)
    out += struct.pack("<4f", *(extra or (0.0, 0.0, 0.0, 0.0)))
    out += struct.pack("<I", len(polys))
    for p in polys:
        out += struct.pack("<I", len(p)) + b"".join(struct.pack("<6f", u, v, 0.0, 0.0, 1.0, 0.0) for u, v in p)
    out += struct.pack("<I", len(polys))
    for p in polys:
        if len(p) == 4:
            idx = (3, 0, 1, 1, 2, 3)
        else:
            idx = tuple(i for k in range(1, len(p) - 1) for i in (0, k, k + 1))
        out += struct.pack("<I", len(idx)) + struct.pack("<%dH" % len(idx), *idx)
    return out + struct.pack("<I", 0)


def _ccw(p):
    a = sum(p[k][0] * p[(k + 1) % len(p)][1] - p[(k + 1) % len(p)][0] * p[k][1] for k in range(len(p)))
    return p if a > 0 else p[::-1]


def route_polys(b, road, every=3, min_width_m=None):
    """the road as a ribbon of quads (u, v)"""
    width = b[1] - b[0]
    min_w = min_width_m if min_width_m is not None else width * 0.010      # at least ~13 px of 1280
    el, er = [], []
    for i in range(len(road)):
        half = max(road.width_at[i], min_w) / 2
        cx, cz = road.centre[i]
        sx, sz = road.side(i)
        el.append(_uv(b, cx - sx * half, cz - sz * half))
        er.append(_uv(b, cx + sx * half, cz + sz * half))
    idx = list(range(0, len(road), every))
    if idx[-1] != len(road) - 1:
        idx.append(len(road) - 1)
    return [_ccw([el[i], el[j], er[j], er[i]]) for i, j in zip(idx, idx[1:])]


def start_polys(b, road, s, thick_m=None):
    width = b[1] - b[0]
    t = thick_m if thick_m is not None else width * 0.004
    half = road.w_at(s) / 2 + width * 0.004
    pts = []
    for ds, lat in ((-t / 2, -half), (-t / 2, half), (t / 2, half), (t / 2, -half)):
        x, z, _ = road.at(s + ds, lat)
        pts.append(_uv(b, x, z))
    return [_ccw(pts)]


def export_png(path, b, roads, highlight=None):
    """a 1280 x 720 black picture with the roads in WHITE (the highlighted one) / grey (others), covering the bounds
    b; the bounds are saved next to it (<path>.json) so the picture can be traced back onto the world later.
    Paint over it (white = drawn on the minimap) and pick it as the map's own minimap."""
    import json
    from PIL import Image, ImageDraw
    im = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(im)
    sc = W / (b[1] - b[0])
    for k, r in enumerate(roads):
        pts = [tuple(c * W for c in _uv(b, x, z)) for x, z in r.centre[::2]]
        col = (255, 255, 255) if highlight is None or k == highlight else (70, 70, 70)
        d.line(pts, fill=col, width=max(6, int(r.width * sc)), joint="curve")
    im.save(path)
    json.dump({"bounds": list(b)}, open(path + ".json", "w"))
    return path


def image_polys(b, path, img_bounds=None, size=(640, 360), threshold=128):
    """trace a picture into minimap polygons: every bright pixel (> threshold) is drawn. The picture covers
    img_bounds (saved by export_png in <path>.json; else b). Pixel runs of a row become rectangles, merged with the
    same run on the next rows."""
    import json
    from PIL import Image
    if img_bounds is None and os.path.exists(path + ".json"):
        img_bounds = json.load(open(path + ".json"))["bounds"]
    ib = img_bounds or b
    im = Image.open(path).convert("L").resize(size)
    w, h = im.size
    px = im.load()
    span = ib[1] - ib[0]
    zspan = ib[3] - ib[2]

    def world(x, y):          # picture pixel corner -> world (x, z)
        return ib[0] + x / w * span, ib[3] - y / h * zspan
    rects, open_runs = [], {}
    for y in range(h + 1):
        runs = []
        if y < h:
            x = 0
            while x < w:
                if px[x, y] > threshold:
                    x0 = x
                    while x < w and px[x, y] > threshold:
                        x += 1
                    runs.append((x0, x))
                else:
                    x += 1
        nxt = {}
        for run in runs:
            nxt[run] = open_runs.pop(run, y)          # continue a rectangle with the same run, or start one
        for (x0, x1), y0 in open_runs.items():
            rects.append((x0, y0, x1, y))
        open_runs = nxt
    polys = []
    for x0, y0, x1, y1 in rects:
        corners = [world(x0, y0), world(x1, y0), world(x1, y1), world(x0, y1)]
        polys.append(_ccw([_uv(b, x, z) for x, z in corners]))
    return polys


def game_ipf(src, b, routes):
    """src = usa_la_river_game.ipf bytes; routes = [(route name, road, start distance, own polygons or None)]"""
    route_head = _header(src, _ROUTE_NODE)
    alt_head = _header(src, _ALT_NODE)
    kids = [src[_BOUNDS[0]:_BOUNDS[1]]]
    for name, road, s0, polys in routes:
        sp = start_polys(b, road, s0)
        us = [u for u, v in sp[0]]
        vs = [v for u, v in sp[0]]
        kids.append(_fill_shape(name.lower() + "_alt", alt_head, sp,
                                (min(us), min(vs), max(us) - min(us), max(vs) - min(vs))))
        kids.append(_fill_shape(name.lower(), route_head, polys or route_polys(b, road)))
    out = struct.pack("<II", 0x100000, 1)
    out += _node("game", 0, 3) + struct.pack("<I", 1)
    out += _node("<group>", 1, 3) + struct.pack("<I", len(kids))
    return out + b"".join(kids)


def picture(b, roads, highlight=None):
    """the 1280 x 720 background: dark ground, a grid every 250 m, every road of the location (light grey with a
    cyan edge) - the game draws the route ribbon + the cars on top"""
    from PIL import Image, ImageDraw
    im = Image.new("RGB", (W, H), (16, 22, 30))
    d = ImageDraw.Draw(im)
    x0, x1, z0, z1 = b
    sc = W / (x1 - x0)

    def px(x, z):
        u, v = _uv(b, x, z)
        return u * W, v * W
    g = math.floor(x0 / 250) * 250
    while g < x1:
        d.line([px(g, z0), px(g, z1)], fill=(24, 32, 44))
        g += 250
    g = math.floor(z0 / 250) * 250
    while g < z1:
        d.line([px(x0, g), px(x1, g)], fill=(24, 32, 44))
        g += 250
    for r in roads:
        pts = [px(x, z) for x, z in r.centre[::2]] + [px(*r.centre[-1])]
        w = max(3, int(r.width * sc))
        d.line(pts, fill=(40, 200, 230), width=w + 4, joint="curve")
        d.line(pts, fill=(70, 76, 86), width=w, joint="curve")
    return im


def texture(src_tex, im):
    """our picture as a .texture (header copied from src_tex, a 1280 x 720 DXT1 one)"""
    assert struct.unpack_from("<II", src_tex, 0x38) == (W, H) and src_tex[0x48:0x4C] == b"DXT1"
    return src_tex[:0x54] + dxt.dxt1_encode(im.convert("RGB").tobytes(), W, H)

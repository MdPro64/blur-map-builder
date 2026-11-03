# The YouTube thumbnail (1280 x 720): the Blur logo + the real Home Run track, tilted towards the viewer and glowing in the logo's colours.
#   python tools/make_thumbnail.py LOGO.png     -> thumbnail_v2.png
import colorsys, json, os, random, sys
import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S = 2
W, H = 1280 * S, 720 * S
FONT = r"C:\Windows\Fonts\seguibl.ttf"
GREEN, YELLOW, PINK, ORANGE = (60, 255, 150), (235, 225, 60), (255, 60, 210), (255, 122, 26)

world = json.load(open(os.path.join(ROOT, "scratch", "game", "Blur Map Builder", "maps", "world.json"), encoding="utf8"))
pts = [(p["x"], p["z"]) if isinstance(p, dict) else tuple(p) for p in world["locations"][0]["maps"][0]["points"]]


def catmull(p, per=40):
    o, n = [], len(p)
    for i in range(n):
        a, b, c, d = p[(i - 1) % n], p[i], p[(i + 1) % n], p[(i + 2) % n]
        for k in range(per):
            t = k / per
            o.append(tuple(0.5 * (2 * b[q] + (c[q] - a[q]) * t + (2 * a[q] - 5 * b[q] + 4 * c[q] - d[q]) * t * t + (3 * b[q] - a[q] - 3 * c[q] + d[q]) * t ** 3) for q in (0, 1)))
    return o


def rainbow(t):
    stops = [GREEN, YELLOW, PINK, (70, 170, 255), GREEN]
    t = (t % 1.0) * (len(stops) - 1); i = int(t); f = t - i
    return tuple(int(stops[i][c] + (stops[i + 1][c] - stops[i][c]) * f) for c in range(3))


def blobs():
    """soft colour behind everything, like the glow around the logo"""
    g = Image.new("RGB", (W // 8, H // 8), (5, 7, 12)); d = ImageDraw.Draw(g)
    for (x, y, r, c) in [(150, 560, 300, (10, 70, 45)), (1180, 60, 300, (46, 12, 52)), (760, 700, 300, (10, 45, 80))]:
        d.ellipse([(x - r) * S / 8, (y - r) * S / 8, (x + r) * S / 8, (y + r) * S / 8], fill=c)
    return g.filter(ImageFilter.GaussianBlur(55)).resize((W, H), Image.BICUBIC)


# the track, flat and big
F = 3000
Q = F / 1800
x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts); z0, z1 = min(p[1] for p in pts), max(p[1] for p in pts)
k = 0.84 * F / max(x1 - x0, z1 - z0)
path = catmull(pts)
scr = lambda p: (F / 2 + (p[0] - (x0 + x1) / 2) * k, F / 2 - (p[1] - (z0 + z1) / 2) * k)
line = [scr(p) for p in path]; n = len(line)
edge = Image.new("RGB", (F, F), (0, 0, 0)); road = Image.new("RGB", (F, F), (0, 0, 0)); de = ImageDraw.Draw(edge); dr = ImageDraw.Draw(road)
for i in range(n):
    a, b = line[i], line[(i + 1) % n]
    de.line((a, b), fill=rainbow(i / n), width=int(76 * Q)); de.ellipse((a[0] - 38 * Q, a[1] - 38 * Q, a[0] + 38 * Q, a[1] + 38 * Q), fill=rainbow(i / n))
    dr.line((a, b), fill=(44, 48, 58), width=int(60 * Q)); dr.ellipse((a[0] - 30 * Q, a[1] - 30 * Q, a[0] + 30 * Q, a[1] + 30 * Q), fill=(44, 48, 58))
for i in range(0, n - 8, 16): dr.line(line[i:i + 8], fill=(235, 240, 245), width=int(5 * Q))
flat = Image.composite(road, edge, road.convert("L").point(lambda v: 255 if v else 0))
random.seed(4)
for i in range(30, n, 62):
    x, y = line[i]; c = rainbow(random.random())
    ImageDraw.Draw(flat).ellipse((x - 17 * Q, y - 17 * Q, x + 17 * Q, y + 17 * Q), fill=c)


def coeffs(dst, src):
    m = []
    for (x, y), (u, v) in zip(dst, src):
        m += [[x, y, 1, 0, 0, 0, -u * x, -u * y], [0, 0, 0, x, y, 1, -v * x, -v * y]]
    return np.linalg.solve(np.array(m, float), np.array(src, float).reshape(8))


corners = [(0, 0), (F, 0), (F, F), (0, F)]
to = [(740 * S, 60 * S), (1330 * S, 120 * S), (1300 * S, 900 * S), (250 * S, 780 * S)]    # tipped back and turned
c = coeffs(to, corners)
tilted = flat.transform((W, H), Image.PERSPECTIVE, c, Image.BICUBIC)
mask = flat.convert("L").point(lambda v: 255 if v else 0).transform((W, H), Image.PERSPECTIVE, c, Image.BICUBIC).filter(ImageFilter.GaussianBlur(0.8))
glow = Image.composite(edge.transform((W, H), Image.PERSPECTIVE, c, Image.BICUBIC), Image.new("RGB", (W, H)), mask).filter(ImageFilter.GaussianBlur(26 * S))

img = ImageChops.screen(blobs(), glow)
img = ImageChops.screen(img, glow)
img.paste(tilted, (0, 0), mask)

# speed streaks in the logo's colours
streaks = Image.new("RGB", (W, H)); ds = ImageDraw.Draw(streaks)
random.seed(9)
for _ in range(9):
    y = random.randint(500, 690) * S; x = random.randint(620, 1000) * S; L = random.randint(120, 300) * S
    ds.line((x, y, x + L, y - L // 9), fill=tuple(v // 2 for v in rainbow(random.random())), width=2 * S)
img = ImageChops.screen(img, streaks.filter(ImageFilter.GaussianBlur(1)))

# keep the left side calm for the title
fade = np.clip((np.arange(W) / S - 430) / 230, 0, 1)[None, :, None]
a = np.array(img, float); b = np.array(blobs(), float)
img = Image.fromarray((b + (a - b) * fade).astype("uint8"))

# the logo: black is nothing, so "screen" keeps its glow
logo = Image.open(sys.argv[1]).convert("RGB")
w = 560 * S; logo = logo.resize((w, w * logo.height // logo.width), Image.LANCZOS)
pad = Image.new("RGB", (W, H)); pad.paste(logo, (-10 * S, -36 * S))
img = ImageChops.screen(img, pad)

d = ImageDraw.Draw(img)
f = lambda v: ImageFont.truetype(FONT, v * S)
for txt, y, size in (("MAP", 262, 138), ("BUILDER", 390, 112)):
    d.text((50 * S, y * S), txt, font=f(size), fill=(255, 255, 255))
glowv = Image.new("RGB", (W, H)); ImageDraw.Draw(glowv).rounded_rectangle((52 * S, 540 * S, 250 * S, 660 * S), 26 * S, fill=ORANGE)
img = ImageChops.screen(img, glowv.filter(ImageFilter.GaussianBlur(18 * S)))
d = ImageDraw.Draw(img)
d.rounded_rectangle((52 * S, 540 * S, 250 * S, 660 * S), 26 * S, fill=ORANGE)
d.text((151 * S, 598 * S), "V2", font=f(90), fill=(255, 255, 255), anchor="mm")
img = img.resize((1280, 720), Image.LANCZOS)
img.save(os.path.join(ROOT, "thumbnail_v2.png"))
img.resize((320, 180), Image.LANCZOS).save(os.path.join(ROOT, "scratch", "ui", "thumb_small.png"))

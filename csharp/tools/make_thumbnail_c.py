# A third YouTube thumbnail for testing: a driver's view of a banked neon road, with a headline.
#   python tools/make_thumbnail_c.py LOGO.png     -> thumbnail_v2_c.png
import os, sys
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S = 3
W, H = 1280 * S, 720 * S
FONT = r"C:\Windows\Fonts\seguibl.ttf"
GREEN, YELLOW, PINK, ORANGE = (60, 255, 150), (235, 225, 60), (255, 60, 210), (255, 122, 26)
HZ = 330 * S                                             # the horizon


def mix(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
def edge_col(t): return mix(GREEN, YELLOW, t * 2) if t < .5 else mix(YELLOW, PINK, t * 2 - 1)


# sky + ground
img = Image.new("RGB", (W, H))
d = ImageDraw.Draw(img)
for y in range(H):
    d.line((0, y, W, y), fill=mix((6, 8, 16), (46, 16, 62), (y / HZ) ** 2.2) if y < HZ else mix((22, 12, 34), (5, 8, 14), min(1, (y - HZ) / (H - HZ) * 1.6)))
for i in range(-24, 25):                                 # grid running to the horizon
    d.line((1010 * S + i * 26 * S, HZ, 1010 * S + i * 620 * S, H), fill=(36, 34, 62), width=S)
for k in range(1, 15):
    y = HZ + (H - HZ) * (k / 14) ** 2.4
    d.line((0, y, W, y), fill=(36, 34, 62), width=S)

# the road: from under the camera, a bend to the left, then back to the horizon. It banks in the bend
steps = 700
L, R, C = [], [], []
for q in range(steps + 1):
    t = q / steps; p = t ** 0.62
    y = H + 40 * S - (H + 40 * S - HZ - 6 * S) * p
    half = (470 * S) * (1 - p) ** 1.25 + 3 * S
    x = (760 + 250 * p - 330 * (1 - p) * p * 4 * (.5 - .5 * p) * 1.0) * S
    bank = 150 * S * (4 * p * (1 - p)) ** 1.5 * (1 - p) ** .6        # the left side lifts in the bend
    L.append((x - half, y - bank)); R.append((x + half, y + bank * .35)); C.append((x, y - bank * .32))
d = ImageDraw.Draw(img)
glow = Image.new("RGB", (W, H)); dg = ImageDraw.Draw(glow)
for q in range(steps):
    t = q / steps; w = max(3 * S, int(16 * S * (1 - t) ** 1.4)); col = edge_col(t)
    d.polygon([L[q], R[q], R[q + 1], L[q + 1]], fill=mix((60, 64, 76), (24, 22, 40), t ** .7))
    for E in (L, R): dg.line((E[q], E[q + 1]), fill=col, width=w + 12 * S)
for q in range(0, steps - 12, 42):
    n = 21; t = q / steps
    d.polygon([(C[q][0] - 8 * S * (1 - t), C[q][1]), (C[q][0] + 8 * S * (1 - t), C[q][1]), (C[q + n][0] + 7 * S * (1 - t), C[q + n][1]), (C[q + n][0] - 7 * S * (1 - t), C[q + n][1])], fill=(235, 240, 245))
img = ImageChops.screen(img, glow.filter(ImageFilter.GaussianBlur(20 * S)))
d = ImageDraw.Draw(img)
for q in range(steps):
    t = q / steps; w = max(2 * S, int(12 * S * (1 - t) ** 1.4)); col = mix(edge_col(t), (255, 255, 255), .25)
    for E in (L, R): d.line((E[q], E[q + 1]), fill=col, width=w)
sun = Image.new("RGB", (W, H)); ImageDraw.Draw(sun).ellipse((C[-1][0] - 150 * S, HZ - 150 * S, C[-1][0] + 150 * S, HZ + 150 * S), fill=(120, 40, 140))
img = ImageChops.screen(img, sun.filter(ImageFilter.GaussianBlur(70 * S)))

# calm behind the words
shade = Image.new("L", (W, H)); ds = ImageDraw.Draw(shade)
for x in range(W): ds.line((x, 0, x, H), fill=int(230 * max(0, min(1, 1 - (x - 470 * S) / (300 * S)))))
img = Image.composite(Image.new("RGB", (W, H), (7, 9, 16)), img, shade.filter(ImageFilter.GaussianBlur(8 * S)))

# the logo, small
logo = Image.open(sys.argv[1]).convert("RGB")
w = 300 * S; logo = logo.resize((w, w * logo.height // logo.width), Image.LANCZOS)
pad = Image.new("RGB", (W, H)); pad.paste(logo, (W - w - 6 * S, -6 * S))
img = ImageChops.screen(img, pad)

d = ImageDraw.Draw(img)
f = lambda v: ImageFont.truetype(FONT, v * S)
d.text((46 * S, 40 * S), "BUILD", font=f(142), fill=(255, 255, 255))
d.text((46 * S, 200 * S), "YOUR OWN", font=f(98), fill=(255, 255, 255))
d.text((46 * S, 322 * S), "TRACKS", font=f(124), fill=(60, 255, 150))
d.rounded_rectangle((50 * S, 520 * S, 548 * S, 650 * S), 26 * S, fill=ORANGE)
d.text((299 * S, 582 * S), "V2  OUT NOW", font=f(62), fill=(255, 255, 255), anchor="mm")
img = img.resize((1280, 720), Image.LANCZOS)
img.save(os.path.join(ROOT, "thumbnail_v2_c.png"))
img.resize((320, 180), Image.LANCZOS).save(os.path.join(ROOT, "scratch", "ui", "thumb_small.png"))

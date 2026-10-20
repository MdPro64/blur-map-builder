"""corner tests: several roads with corner points; numbers + pictures"""
import math, os, sys
MB = r"E:\blur - bak\map_builder"
sys.path.insert(0, os.path.join(MB, "flat_earth"))
import road as R
from PIL import Image, ImageDraw

OUT = os.path.dirname(os.path.abspath(__file__))


def P(x, z, c=0.0):
    return {"x": x, "z": z, "corner": c}


CASES = {
    # a square-ish loop: 4 corners of 40 m
    "square40": [P(0, 0), P(600, 0, 40), P(600, 600, 40), P(0, 600, 40), P(0, 1200)],
    # bigger radii than the legs allow
    "square150": [P(0, 0), P(600, 0, 150), P(600, 600, 150), P(0, 600, 150), P(0, 1200)],
    # close corners (short legs 150 m)
    "zigzag": [P(0, 0), P(300, 0, 40), P(300, 150, 40), P(600, 150, 40), P(600, 300, 40), P(900, 300)],
    # corner next to smooth points
    "mixed": [P(0, 0), P(400, 50), P(800, 0, 60), P(900, 400), P(1300, 500, 60), P(1700, 300)],
    # hairpin (180-ish) corner
    "hairpin": [P(0, 0), P(800, 0, 60), P(820, 200, 60), P(0, 200)],
    # sharp 135
    "sharp": [P(0, 0), P(700, 0, 50), P(200, 400), P(-200, 600)],
}


def analyse(name, pts):
    r = R.Road(pts, 60.0, False)
    n = len(r)
    # heading change per step (deg) and radius
    worst_kink = 0.0
    rad = []
    for i in range(1, n - 1):
        da = (r.heading[i + 1] - r.heading[i - 1] + math.pi) % (2 * math.pi) - math.pi
        rad.append(2 * R.STEP / abs(da) if abs(da) > 1e-6 else 1e9)
    for i in range(2, n - 2):
        d1 = (r.heading[i] - r.heading[i - 1] + math.pi) % (2 * math.pi) - math.pi
        d2 = (r.heading[i + 1] - r.heading[i] + math.pi) % (2 * math.pi) - math.pi
        worst_kink = max(worst_kink, abs(d2 - d1))
    # curvature sign flips (wiggles) = S bends that nobody asked for
    sgn = []
    for i in range(1, n - 1):
        da = (r.heading[i + 1] - r.heading[i - 1] + math.pi) % (2 * math.pi) - math.pi
        sgn.append(0 if abs(da) < math.radians(0.3) else (1 if da > 0 else -1))
    flips = sum(1 for a, b in zip([s for s in sgn if s], [s for s in sgn if s][1:]) if a != b)
    # for each corner: the tightest radius within 80 m of the point vs asked
    rep = []
    for p in r.points:
        if p["corner"]:
            near = [rad[i - 1] for i in range(1, n - 1)
                    if math.hypot(r.centre[i][0] - p["x"], r.centre[i][1] - p["z"]) < max(200, p["corner"] * 2)]
            rep.append("asked %.0f got %.0f" % (p["corner"], min(near) if near else -1))
    print("%-10s len %5.0f  min radius %5.0f  worst kink %.1f deg/step  wiggle flips %d  | %s"
          % (name, r.length, min(rad), math.degrees(worst_kink), flips, "; ".join(rep)))
    # picture
    xs = [c[0] for c in r.centre]; zs = [c[1] for c in r.centre]
    x0, x1, z0, z1 = min(xs) - 80, max(xs) + 80, min(zs) - 80, max(zs) + 80
    sc = 700 / max(x1 - x0, z1 - z0)
    im = Image.new("RGB", (int((x1 - x0) * sc) + 1, int((z1 - z0) * sc) + 1), (20, 24, 30))
    d = ImageDraw.Draw(im)
    f = lambda x, z: ((x - x0) * sc, (z1 - z) * sc)
    for side in (-1, 1):
        d.line([f(*q) for q in r.edge(side)], fill=(60, 200, 230), width=2)
    d.line([f(*q) for q in r.centre], fill=(220, 220, 220), width=1)
    for p in pts:
        x, y = f(p["x"], p["z"])
        d.rectangle([x - 4, y - 4, x + 4, y + 4], outline=(255, 180, 30) if p["corner"] else (120, 120, 120))
    im.save(os.path.join(OUT, "corner_%s.png" % name))


for k, v in CASES.items():
    analyse(k, v)

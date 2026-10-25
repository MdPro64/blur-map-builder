"""The game's cars (amax.bin AmaxVehicle): name, class, menu stats - and the fair limits of each class.
Read once from the unpacked game data, then kept in WORK/cars_cache.json."""
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.dirname(HERE)
for p in (MB, os.path.join(MB, "kslf")):
    if p not in sys.path:
        sys.path.insert(0, p)
import paths

CLASSES = "ABCD"                                       # the game's class 0..3
STATS = (("speed", "UiSpeed", "Top speed"), ("acceleration", "UiAcceleration", "Acceleration"),
         ("drift", "UiDrift", "Drift"), ("grip", "UiGrip", "Grip"), ("strength", "UiStrength", "Strength"))
CACHE = os.path.join(paths.WORK, "cars_cache.json")
_cars = None


def _read_game():
    import kslf
    import schema
    d = open(os.path.join(paths.UNPACKED, "xt", "amax.bin"), "rb").read()
    k = kslf.Kslf(d)
    types = schema.load_schema(k)

    def fields(t, base=0):
        for b, off in types[t]["bases"]:
            yield from fields(b, base + off)
        for f in types[t]["fields"]:
            yield f["name"], f["offset"] + base, types[f["type"]]["name"] if f["type"] < len(types) else ""
    out = []
    for o in kslf.Objects(k, types).objects:
        b = o["blocks"][0]
        if types[b["type"]]["name"] != "AmaxVehicle":
            continue
        f = {n: (off, tn) for n, off, tn in fields(b["type"])}

        def num(name):
            off, tn = f[name]
            return struct.unpack_from({"f32": "<f", "u8": "<B"}.get(tn, "<i"), d, b["offset"] + off)[0]

        def text(name):
            at = struct.unpack_from("<I", d, b["offset"] + f[name][0])[0]
            return bytes(o["pool"][at:o["pool"].index(b"\0", at)]).decode("latin1")
        car = {"name": text("Name"), "class": num("class"), "icon": text("UiIconName"), "mph": num("UiMaxSpeed")}
        for key, field, _ in STATS:
            car[key] = num(field)
        out.append(car)
    names = set()
    return [c for c in sorted(out, key=lambda c: (c["class"], c["name"])) if not (c["name"] in names or names.add(c["name"]))]


def game_cars():
    global _cars
    if _cars is None:
        try:
            _cars = json.load(open(CACHE))
        except (OSError, ValueError):
            _cars = _read_game()
            try:
                os.makedirs(os.path.dirname(CACHE), exist_ok=True)
                json.dump(_cars, open(CACHE, "w"), indent=0)
            except OSError:
                pass
    return _cars


def class_cars(cls):
    return [c for c in game_cars() if c["class"] == cls]


def limits(cls):
    """{stat: (lowest, highest)} of the real cars in the class, plus 'budget' = the class's average stat total
    (a custom car may spend at most that many points) and 'mph' = (slowest, fastest) top speed"""
    cars = class_cars(cls)
    out = {key: (min(c[key] for c in cars), max(c[key] for c in cars)) for key, _, _ in STATS}
    out["budget"] = round(sum(sum(c[k] for k, _, _ in STATS) for c in cars) / len(cars))
    out["mph"] = (min(c["mph"] for c in cars), max(c["mph"] for c in cars))
    return out


def top_speed(cls, speed):
    """mph for a speed stat: the class's slowest .. fastest top speed across its speed range"""
    lim = limits(cls)
    lo, hi = lim["speed"]
    t = 0.5 if hi == lo else (speed - lo) / (hi - lo)
    return round(lim["mph"][0] + t * (lim["mph"][1] - lim["mph"][0]))


def total(car):
    return sum(car["stats"][k] for k, _, _ in STATS)


def fair(car):
    """the car's stats pulled inside its class's limits and budget (lowest stats first are kept)"""
    lim = limits(car["class"])
    s = car["stats"]
    for k, _, _ in STATS:
        s[k] = max(lim[k][0], min(lim[k][1], int(s.get(k, lim[k][0]))))
    for k, _, _ in reversed(STATS):              # over budget: take points off strength, grip, ... first
        while total(car) > lim["budget"] and s[k] > lim[k][0]:
            s[k] -= 1
    return car


def new_car(cls=1, base=None):
    base = base or class_cars(cls)[0]["name"]
    src = next(c for c in game_cars() if c["name"] == base)
    car = {"name": "My car", "class": cls, "base": base, "icon": src["icon"],
           "stats": {k: src[k] for k, _, _ in STATS}, "paint": ["#1e9bff", "#0b1426"]}
    return fair(car)


if __name__ == "__main__":
    for i, c in enumerate(CLASSES):
        print(c, limits(i), len(class_cars(i)), "cars")

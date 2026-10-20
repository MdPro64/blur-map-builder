"""
The designer's world: LOCATIONS (each one a level of its own, listed in the game's location menu) holding MAPS
(race routes, listed under the location). Stored in map_builder/tracks/world.json:

{"locations": [{"id": "flat_earth", "name": "Flat Earth",
                "maps": [{"id": "flat_earth_1", "name": "Home Run v1", "width": 60,
                          "points": [{"x", "z", "w"?, "h"?, "corner"?}, ...],
                          "auto_bridges": true,
                          "pickups": [{"kind": "row", "x", "z", "type"?}, {"kind": "single", "x", "z", "type"}]
                                     or missing = automatic rows,
                          "obstacles": false}]}]}

Game ids: location 0 / its first map keep the original Flat Earth ids (city FFFFFF20, route FFFFFF21); every other
location / map gets ids from EXTRA_BASE upward, in this order (cities and routes as they come in the file); the
actor lists of every map except a location's first come from LIST_BASE upward (7 per map).
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
import sys  # noqa: E402
sys.path.insert(0, os.path.dirname(HERE))
import paths  # noqa: E402
WORLD_FILE = paths.WORLD_FILE
OLD_ROAD_FILE = os.path.join(paths.TRACKS, "flat_earth_road.json")
EXTRA_BASE = 0xFFFFFF80          # extra cities / routes (amax.bin)
LIST_BASE = 0xFFFFFFA0           # extra maps' actor lists (7 each)
LISTS_PER_MAP = 7
MAX_MAPS = 12
LOC_BASE = 0xF00D0100            # names of extra locations / maps (cityandroutenames.loc)

PICKUP_TYPES = {"Shunt": "2BEC03D3", "Barge": "60A35DF0", "Shock": "3A03CB15", "Mine": "2BB324A4",
                "Bolt": "4CF8489B", "Nitro": "8DAF6717", "Shield": "6F453F79", "Repair": "09B1575A"}


def default_world():
    from road import load as load_road        # the old single-road file (or the straight road)
    r = load_road(OLD_ROAD_FILE)
    return {"locations": [{"id": "flat_earth", "name": "Flat Earth", "maps": [
        {"id": "flat_earth_1", "name": "Home Run v1", "width": r.get("width", 60.0), "points": r["points"],
         "auto_bridges": True, "obstacles": False}]}]}


def load(path=WORLD_FILE):
    if os.path.exists(path):
        return json.load(open(path))
    return default_world()


def save(world, path=WORLD_FILE):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    json.dump(world, open(tmp, "w"), indent=1)
    os.replace(tmp, path)


def slug(name):
    s = re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")
    return s or "track"


def all_maps(world):
    return [(li, mi, loc, m) for li, loc in enumerate(world["locations"]) for mi, m in enumerate(loc["maps"])]


def assign_ids(world):
    """fills every location / map with its game ids (city/route uid, loc string id, internal names, folder)"""
    nxt = EXTRA_BASE
    lst = LIST_BASE
    locid = LOC_BASE
    for li, loc in enumerate(world["locations"]):
        folder = "flat_earth" if li == 0 else slug(loc["id"])
        loc["folder"] = folder
        # the game opens .\levels\<city name>\level.level: the name must equal the folder (case does not matter)
        loc["city_name"] = "Flat_Earth" if li == 0 else "_".join(w.capitalize() for w in folder.split("_"))
        if li == 0:
            loc["city_uid"], loc["loc_id"] = 0xFFFFFF20, 0xF00D0010
        else:
            loc["city_uid"], loc["loc_id"] = nxt, locid
            nxt += 1
            locid += 1
        for mi, m in enumerate(loc["maps"]):
            m["route_name"] = "%s_%d" % (loc["city_name"], mi + 1)
            if li == 0 and mi == 0:
                m["route_uid"], m["loc_id"] = 0xFFFFFF21, 0xF00D0011
            else:
                m["route_uid"], m["loc_id"] = nxt, locid
                nxt += 1
                locid += 1
            if mi > 0:
                m["list_uids"] = list(range(lst, lst + LISTS_PER_MAP))
                lst += LISTS_PER_MAP
            else:
                m["list_uids"] = None
    assert nxt <= LIST_BASE, "too many locations / maps"
    assert lst <= 0xFFFFFFFF, "too many maps"
    return world

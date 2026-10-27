"""Lighting of our locations (the designer's LIGHTING tab).

A City's EnvironmentSettings (amax City +52 .. +132) are references to LightingSettings / LightFlare / PostFx /
RoadTrack / Shadow / CarLight / Wind records (+ the same for Flashback, then headlight scale, dirt, reflection /
environment resolution, far clip). A location picks a game city as its PRESET (all of those are copied from it);
the values the user changes go into our OWN copy of the preset's LightingSettings record (uid LIGHT_UID_BASE + location
index), which the city then points to.

world.json: location["lighting"] = {"preset": "LA River (day)", "values": {"sun": [r, g, b, strength], ...}}
(missing = the old default: build.LIGHT_CITY_OBJ = Amboy, unchanged).
"""
import json
import os
import struct

HERE = os.path.dirname(os.path.abspath(__file__))
PRESET_FILE = os.path.join(HERE, "lighting_presets.json")
LIGHT_UID_BASE = 0xF1C00000

# name -> amax City object index (unpacked gamedata amax.bin)
PRESETS = {
    "Amboy (desert day)": 2226, "LA River (day)": 805, "LA Downtown": 2829, "LA Observatory": 2149,
    "Long Beach Docks": 3689, "Barcelona Gracia (night)": 1994, "Barcelona Oval": 221, "Brighton": 526,
    "London Hackney": 1116, "Tokyo Shutoko": 696, "Mount Haruna": 1929, "NY Dumbo": 1996,
    "San Francisco Russian Hill": 1513, "San Francisco Sausalito": 1545,
}
DEFAULT_PRESET = "Amboy (desert day)"

# value name -> (byte offset in the LightingSettings record, number of floats, label, slider max)
FIELDS = {
    "sun": (0, 4, "Sun colour + strength", 60.0),
    "sky": (16, 4, "Sky (ambient) colour + strength", 4.0),
    "baked": (32, 4, "Baked light colour + strength", 4.0),
    "emissive": (48, 4, "Emissive (glow) colour + strength", 4.0),
    "fog_colour": (64, 4, "Fog colour", 1.0),
    "fog_density": (80, 1, "Distance fog density", 0.01),
    "height_fog": (96, 1, "Height fog density", 0.01),
    "sky_rotation": (116, 1, "Sky picture rotation", 6.3),
    "sky_intensity": (120, 1, "Sky picture brightness", 0.2),
    "middle_grey": (152, 1, "Exposure (middle grey)", 1.0),
    "exposure_min": (156, 1, "Exposure min", 4.0),
    "exposure_max": (160, 1, "Exposure max", 8.0),
    "bloom": (164, 1, "Bloom strength (0 = no glow)", 1.5),
    # every game city uses 1.0; LOWER = more of the picture glows (the old slider max 0.2 made everything glow)
    "bloom_threshold": (184, 1, "Bloom threshold (higher = less glow)", 4.0),
}


# LEVEL light samples (levelrender chunk "LSData": u32 count + 24-byte records = 12 half floats = R, G, B x 4 SH
# numbers (x, y, z, overall): y = light from above, overall >= 0; LA River's lit samples ~0.1). Objects in the game
# get ALL their light from these (every real city has Sun strength 0). Our levels have none where the track is, so
# the build writes one light into EVERY sample of the location's levelrender.
#   name -> (floats, label, slider max, default)
SAMPLE_FIELDS = {
    "object_light": (4, "Light on placed objects (colour + strength)", 0.6, [1.0, 0.97, 0.92, 0.0]),
    "object_light_up": (1, "   ... coming from above (0 = all round)", 1.0, [0.6]),
}
LIGHT_UP = {"object_light": [1.0, 0.97, 0.92, 0.14], "object_light_up": [0.6]}


def effective(loc):
    """(preset name, values) a location really gets: its own Lighting tab settings, or - when the user never touched
    the Lighting tab (a new or imported location: no "lighting" key) - Amboy (desert day) with the placed objects
    lit up (LIGHT_UP), so nothing comes out dark (user 2026-10-20)"""
    lt = loc.get("lighting")
    if not lt:
        return DEFAULT_PRESET, dict(LIGHT_UP)
    return lt.get("preset") or DEFAULT_PRESET, dict(lt.get("values") or {})


def sh_record(values):
    """the 12 floats (R, G, B x (x, y, z, overall)) of the location's object light, or None (strength 0)"""
    col = (values.get("object_light") or SAMPLE_FIELDS["object_light"][3])
    up = (values.get("object_light_up") or SAMPLE_FIELDS["object_light_up"][3])[0]
    if col[3] <= 0:
        return None
    rec = []
    for c in col[:3]:
        s = col[3] * c
        rec += [0.075 * up * s, 0.8 * up * s, -0.075 * up * s, s]          # x, y (from above), z, overall
    return rec


def write_level_light(path, values, roads, ground_y):
    """the location's object light into its levelrender: new LIGHT POINTS along its roads (the template's points
    are in the showroom building, far from our tracks -> objects got no light) + every sample = the object light.
    Same chunk sizes as before. Returns the number of points (0 = nothing written)."""
    import sys
    sys.path.insert(0, os.path.join(HERE, "..", "level"))
    import lstree
    sh = sh_record(values)
    if sh is None:
        return 0
    d = open(path, "rb").read()
    n = len(lstree.parse(d)["points"])
    new = lstree.rebuild_points(d, lstree.road_points(roads, n, ground_y), sh)
    open(path, "wb").write(new)
    return n


def write_samples(path, values):
    """(old: samples only, the template's points stay) set every light sample of a levelrender file to the
    location's object light (if its strength > 0). Returns the number of samples written."""
    import numpy as np
    col = (values.get("object_light") or SAMPLE_FIELDS["object_light"][3])
    up = (values.get("object_light_up") or SAMPLE_FIELDS["object_light_up"][3])[0]
    strength = col[3]
    if strength <= 0:
        return 0
    d = bytearray(open(path, "rb").read())
    o = d.find(b"LSData\0\0")
    assert o > 0 and d.find(b"LSData\0\0", o + 1) < 0, "LSData chunk not found once"
    n = struct.unpack_from("<I", d, o + 16)[0]
    assert struct.unpack_from("<I", d, o + 8)[0] >= 4 + 24 * n
    rec = []
    for c in col[:3]:
        s = strength * c
        rec += [0.25 * up * s * 0.3, up * s * 0.8, -0.25 * up * s * 0.3, s]   # x, y (above), z, overall
    block = np.tile(np.array(rec, dtype="<f2"), n).tobytes()
    d[o + 20:o + 20 + 24 * n] = block
    open(path, "wb").write(d)
    return n


def presets():
    """{preset name: {value name: [floats]}} (made once from the unpacked amax.bin)"""
    if os.path.exists(PRESET_FILE):
        out = json.load(open(PRESET_FILE))
        for p in out.values():
            for k, f in SAMPLE_FIELDS.items():
                p.setdefault(k, list(f[3]))
        return out
    import sys
    sys.path.insert(0, os.path.join(HERE, "..", "kslf"))
    import dump
    sys.path.insert(0, os.path.join(HERE, ".."))
    import paths
    F = dump.File(os.path.join(paths.UNPACKED, "xt", "amax.bin"))
    out = {}
    for name, ci in PRESETS.items():
        o = F.O[ls_object(F, ci)]
        b = o["blocks"][0]["offset"]
        out[name] = {k: list(struct.unpack_from("<%df" % n, F.d, b + off)) for k, (off, n, _, _) in FIELDS.items()}
    json.dump(out, open(PRESET_FILE, "w"), indent=1)
    for p in out.values():
        for k, f in SAMPLE_FIELDS.items():
            p.setdefault(k, list(f[3]))
    return out


def sky_files(wld, maps_dir, read_root, log=print):
    """root.pak ({path: bytes}, {path: alias of}) of the locations' custom skies (loc["lighting"]["sky_image"], a
    file in maps_dir): each one under the name lighting.prepare gives its record; read_root(path) = a base file"""
    import sys
    sys.path.insert(0, os.path.join(HERE, "..", "kslf"))
    sys.path.insert(0, os.path.join(HERE, ".."))
    import dump
    import paths
    import sky
    files, aliases = {}, {}
    F = None
    for li, loc in enumerate(wld["locations"]):
        lt = loc.get("lighting") or {}
        if not lt.get("sky_image"):
            continue
        src = os.path.join(maps_dir, lt["sky_image"])
        if not os.path.exists(src):
            log("custom sky of %s: %s is missing - the preset's sky stays" % (loc["name"], src))
            continue
        F = F or dump.File(os.path.join(paths.UNPACKED, "xt", "amax.bin"))
        city = PRESETS.get(lt.get("preset"), PRESETS[DEFAULT_PRESET])
        old = sky.record_sky(F.d, F.O[ls_object(F, city)])[0]
        name = "\\" + sky.own_name(old, li).replace("/", "\\")
        base = "\\" + old.replace("/", "\\")
        files[name] = sky.texture(read_root(base), src)
        aliases[name] = base
        log("custom sky of %s: %s -> %s" % (loc["name"], lt["sky_image"], name))
    return files, aliases


def ls_object(F, city_obj):
    """object index of a City's LightingSettings record"""
    uid = F.top(F.O[city_obj])["EnvironmentSettings"]["lightingSettings"]["uid"]
    return next(oi for u, t, oi in F.refs if "%08X" % u == uid)


def prepare(amax, wld, dump, kslf_write, tmp):
    """adds our LightingSettings copies; returns (amax, {location index: 80 environment bytes for City +52..+132})"""
    out = {}
    edited = {}                                      # preset city object -> the values written into its record
    for li, loc in enumerate(wld["locations"]):
        lt = loc.get("lighting")
        if not lt:
            continue
        city = PRESETS.get(lt.get("preset"), PRESETS[DEFAULT_PRESET])
        vals = {k: v for k, v in (lt.get("values") or {}).items() if k in FIELDS}   # (level samples: build_level_files)
        uid = None
        if vals and city not in edited:
            # IN PLACE: the preset's own record gets the values (a COPY loses its sky picture in game - white sky,
            # even an unchanged copy; tested 2026-10-20). Only in our packs; one location per preset.
            A = dump.File(tmp(amax))
            b = A.O[ls_object(A, city)]["blocks"][0]["offset"]
            amax = bytearray(amax)
            for k, v in vals.items():
                off, n = FIELDS[k][:2]
                struct.pack_into("<%df" % n, amax, b + off, *[float(x) for x in v[:n]])
            amax = bytes(amax)
            edited[city] = vals
        elif vals and vals != edited[city]:
            # a second location with the same preset but other values: a copy (white sky risk)
            A = dump.File(tmp(amax))
            src = ls_object(A, city)
            uid = LIGHT_UID_BASE + li

            def patch(data, pool, blocks, vals=vals):
                b = blocks[0]
                for k, v in vals.items():
                    if k in FIELDS:
                        off, n = FIELDS[k][:2]
                        struct.pack_into("<%df" % n, data, b + off, *[float(x) for x in v[:n]])
            _, amax = kslf_write.clone_object_any(src, None, uid, patch, data_in=amax)
        A = dump.File(tmp(amax))                       # rows may have moved: read the city bytes now
        if lt.get("sky_image"):                        # custom sky: the record's sky name -> ours (sky.py)
            import sky
            oi = next(oi for u, t, oi in A.refs if u == uid) if uid is not None else ls_object(A, city)
            old = sky.record_sky(amax, A.O[oi])[0]
            amax = sky.set_record_sky(amax, A.O[oi], sky.own_name(old, li))
            A = dump.File(tmp(amax))
        b = A.O[city]["blocks"][0]["offset"]
        env = bytearray(amax[b + 52:b + 132])
        if uid is not None:
            row = next(i for i, (u, t, oi) in enumerate(A.refs) if u == uid)
            struct.pack_into("<I", env, 0, row)        # EnvironmentSettings.lightingSettings -> our copy
        out[li] = bytes(env)
    return amax, out

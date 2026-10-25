"""BUILD the player's custom cars (world["cars"], Custom car tab) into the game's database.
Per car (uids CA400000 + 4 * i: car, physics, paint):
  racingphysics.bin  RP_CarDynamics = copy of the body car's, every stat moved along its class's trend (below)
  vehicles.bin       VEH_PaintJob   = copy of the body car's first paint, our two colours
  amax.bin           AmaxVehicle    = copy of the body car: own name, class stats, icon, top speed, physics, paint,
                                      wheels of the wheel car (wheelModel + sizes)
  multiplayer.bin    car list + unlocked at rank 1
  loc/vehicles.loc   the name
and root.pak (model_aliases): the game finds a car's model by its Name: models\\vehicles\\<name>.model (+ wheels\\)
-> aliases of the body car's model and the wheel car's wheels.

FAIR HANDLING: for each stat the real cars of the class give a trend (least squares: field value against the menu
stat), e.g. class B cars with more drift have a weaker rear grip. The body car's value moves along that trend by
(our stat - its stat) and is clamped to the lowest .. highest value of the class - never past the class."""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.dirname(HERE)
for p in (HERE, os.path.join(MB, "kslf")):
    if p not in sys.path:
        sys.path.insert(0, p)
import cars_data as cd
import kslf
import kslf_write
import schema

UID0 = 0xCA400000
LOC0 = 0x4D440000
TREND = {                                         # stat -> RP_CarDynamics fields that follow it
    "speed": ("friction.airFriction", "gears.finalRatio"),
    "acceleration": ("engine.engineTorque_Rpm.yScale",),
    "drift": ("steering.slideRecovery", "steering.steerSpringDrift", "brakes.handbrakeForce",
              "tyres.rearTyres.transverseGripScale", "tyres.rearTyres.combinedSlipForwards_Mph.y0",
              "tyres.rearTyres.combinedSlipForwards_Mph.y1"),
    "grip": ("tyres.frontTyres.grip.yScale", "tyres.rearTyres.grip.yScale", "tyres.frontTyres.transverseGripScale",
             "friction.downforce"),
}
CAR_TREND = {"strength": ("HealthGroup", "weightClass")}          # AmaxVehicle ints 0..2
PACK = {"i32": "<i", "u32": "<I", "f32": "<f", "bool": "<I", "u8": "<B", "u16": "<H", "i16": "<h", "LocId": "<I",
        "sz8": "<I"}


class Db:
    """one KSLF file: its types, objects, uid rows; fields by dotted path"""

    def __init__(self, data):
        self.data = data
        self.k = kslf.Kslf(data)
        self.types = schema.load_schema(self.k)
        self.objs = kslf.Objects(self.k, self.types).objects
        o4, n4 = self.k.sections[4]
        self.rows = [struct.unpack_from("<IHH", data, o4 + 8 * i) for i in range(n4)]

    def tname(self, i):
        return self.types[self.objs[i]["blocks"][0]["type"]]["name"]

    def _fields(self, t, base=0):
        for b, off in self.types[t]["bases"]:
            yield from self._fields(b, base + off)
        for f in self.types[t]["fields"]:
            yield f["name"], f["offset"] + base, f["type"]

    def field(self, t, path):
        off = 0
        for part in path.split("."):
            _, o, ft = next(x for x in self._fields(t) if x[0] == part)
            off, t = off + o, ft
        return off, self.types[t]["name"]

    def get(self, i, path):
        b = self.objs[i]["blocks"][0]
        off, tn = self.field(b["type"], path)
        return struct.unpack_from(PACK.get(tn, "<I"), self.data, b["offset"] + off)[0]

    def obj_of_uid(self, uid):
        r = next(r for r in self.rows if r[0] == uid)
        return r[2]

    def row_of_uid(self, uid):
        return next(i for i, r in enumerate(self.rows) if r[0] == uid)

    def ref_uid(self, i, path):
        return self.rows[self.get(i, path)][0]

    def name(self, i):
        o = self.objs[i]
        at = struct.unpack_from("<I", self.data, o["blocks"][0]["offset"] + 12)[0]
        return bytes(o["pool"][at:o["pool"].index(b"\0", at)]).decode("latin1")

    def cars(self):
        return {self.name(i): i for i in range(len(self.objs)) if self.tname(i) == "AmaxVehicle"}


def internal_names(cars):
    """the cars' Names in the game files (unique; the game finds the model by it)"""
    import car_tab
    out, used = [], set()
    for c in cars:
        n = car_tab.internal_name(c["name"])
        while n.lower() in used:
            n += "_2"
        used.add(n.lower())
        out.append(n)
    return out


def _fit(xs, ys):
    """slope of the least-squares line (0 if the stat does not vary)"""
    mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
    sxx = sum((x - mx) ** 2 for x in xs)
    return 0.0 if sxx == 0 else sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / sxx


def handling(car, amax, rp):
    """{RP field: value} and {AmaxVehicle field: value} for the car - the body car's, moved along the class trend"""
    names = amax.cars()
    cls_cars = [c for c in cd.class_cars(car["class"]) if c["name"] in names]
    base = next(c for c in cd.game_cars() if c["name"] == car["base"])
    bi = names[car["base"]]
    rpi = {c["name"]: rp.obj_of_uid(amax.ref_uid(names[c["name"]], "dynamics")) for c in cls_cars}
    rp_out, car_out = {}, {}
    for stat, fields in TREND.items():
        d = car["stats"][stat] - base[stat]
        for f in fields:
            ys = [rp.get(rpi[c["name"]], f) for c in cls_cars]
            v0 = rp.get(rpi[car["base"]], f)
            v = v0 + _fit([c[stat] for c in cls_cars], ys) * d
            rp_out[f] = max(min(ys), min(max(ys), v))
    for stat, fields in CAR_TREND.items():
        d = car["stats"][stat] - base[stat]
        for f in fields:
            ys = [amax.get(names[c["name"]], f) for c in cls_cars]
            v = amax.get(bi, f) + _fit([c[stat] for c in cls_cars], ys) * d
            car_out[f] = int(round(max(min(ys), min(max(ys), v))))
    return rp_out, car_out


def _set(db_types_field, data, at, t, path, value):
    off, tn = db_types_field(t, path)
    struct.pack_into(PACK.get(tn, "<I"), data, at + off, value)


def add_cars(files, wld, U, log=print):
    """put world["cars"] into the database files ({pack path: bytes}; missing ones are read from U\\xt)"""
    cars = wld.get("cars") or []
    if not cars:
        return
    rd = lambda n: files.get("gamedata/xt/" + n) or open(os.path.join(U, "xt", *n.split("/")), "rb").read()
    amax_d, rp_d, veh_d, mp_d, vloc = (rd(n) for n in ("amax.bin", "racingphysics.bin", "vehicles.bin",
                                                       "multiplayer.bin", "loc/vehicles.loc"))
    names = internal_names(cars)
    added = []
    for i, (car, name) in enumerate(zip(cars, names)):
        car_uid, dyn_uid, paint_uid = UID0 + 4 * i, UID0 + 4 * i + 1, UID0 + 4 * i + 2
        picks = [x["name"] for x in cd.class_cars(car["class"])]
        if car["base"] not in picks:                 # a boss / legend copy does not show in the car select
            car = dict(car, base=picks[0])
        amax, rp = Db(amax_d), Db(rp_d)
        bi = amax.cars()[car["base"]]
        wi = amax.cars().get(car.get("wheels") or car["base"], bi)
        rp_vals, car_vals = handling(car, amax, rp)
        # ---- physics
        ri = rp.obj_of_uid(amax.ref_uid(bi, "dynamics"))
        rt = rp.objs[ri]["blocks"][0]["type"]

        def dyn_patch(data, pool, blocks):
            for f, v in rp_vals.items():
                _set(rp.field, data, blocks[0], rt, f, v)
        _, rp_d = kslf_write.clone_object_any(ri, None, dyn_uid, dyn_patch, data_in=rp_d)
        # ---- paint: the body car's first paint job, our colours
        vt = amax.objs[bi]["blocks"][0]["type"]
        p_off, _ = amax.field(vt, "baseVehicle.paintJobs")
        at = amax.objs[bi]["blocks"][0]["offset"] + p_off
        blk, first, cnt = struct.unpack_from("<HHI", amax_d, at)
        src_paint = amax.rows[struct.unpack_from("<I", amax_d, amax.objs[bi]["blocks"][blk]["offset"] + 4 * first)[0]][0]
        veh = Db(veh_d)
        pi = veh.obj_of_uid(src_paint)
        pt = veh.objs[pi]["blocks"][0]["type"]
        c_off, _ = veh.field(pt, "colours")
        cols = [tuple(int(h[k:k + 2], 16) / 255.0 for k in (1, 3, 5)) for h in car["paint"]]

        def paint_patch(data, pool, blocks):
            cb, cf, cc = struct.unpack_from("<HHI", data, blocks[0] + c_off)
            for j in range(cc):
                struct.pack_into("<3f", data, blocks[cb] + 12 * (cf + j), *cols[min(j, 1)])
        _, veh_d = kslf_write.clone_object_any(pi, None, paint_uid, paint_patch, data_in=veh_d)
        # ---- the car
        amax_d, _ = kslf_write.insert_uid(amax_d, dyn_uid, "RP_CarDynamics")
        amax_d, _ = kslf_write.insert_uid(amax_d, paint_uid, "VEH_PaintJob")
        amax = Db(amax_d)
        bi = amax.cars()[car["base"]]
        wi = amax.cars().get(car.get("wheels") or car["base"], bi)
        dyn_row, paint_row = amax.row_of_uid(dyn_uid), amax.row_of_uid(paint_uid)
        wheel_vals = {f: amax.get(wi, f) for f in ("wheelModel", "wheelScaleFront", "wheelScaleRear",
                                                   "wheelDepthFront", "wheelDepthRear")}
        s = car["stats"]
        mph = cd.top_speed(car["class"], s["speed"])
        sets = dict(car_vals, LocString=LOC0 + i, dynamics=dyn_row, UiSpeed=s["speed"],
                    UiAcceleration=s["acceleration"], UiDrift=s["drift"], UiGrip=s["grip"], UiStrength=s["strength"],
                    UiMaxSpeed=mph, GameplayTopSpeed=mph, BossAbilitySlot=0, **{"class": car["class"]}, **wheel_vals)
        icon_off, _ = amax.field(vt, "UiIconName")

        def car_patch(data, pool, blocks):
            for f, v in sets.items():
                _set(amax.field, data, blocks[0], vt, f, v)
            struct.pack_into("<I", data, blocks[0] + icon_off, len(pool))
            pool += car["icon"].encode("latin1") + b"\0"
            pb, pf, _ = struct.unpack_from("<HHI", data, blocks[0] + p_off)
            struct.pack_into("<I", data, blocks[pb] + 4 * pf, paint_row)             # paint 1 = ours, only one
            struct.pack_into("<HHI", data, blocks[0] + p_off, pb, pf, 1)
        _, amax_d = kslf_write.clone_object_any(bi, name, car_uid, car_patch, data_in=amax_d)
        added.append(car_uid)
        log("custom car %s (%s, class %s, body %s, wheels %s): %d mph, stats %s" % (
            car["name"], name, cd.CLASSES[car["class"]], car["base"], car.get("wheels") or car["base"], mph,
            " ".join("%s %d" % (k, v) for k, v in s.items())))
        if car.get("body_glb"):
            log("   (its imported body %s is not used in game yet: the game shows %s)" % (car["body_glb"], car["base"]))
    files["gamedata/xt/racingphysics.bin"] = rp_d
    files["gamedata/xt/vehicles.bin"] = veh_d
    files["gamedata/xt/amax.bin"] = amax_d
    files["gamedata/xt/multiplayer.bin"] = _car_lists(mp_d, added)
    files["gamedata/xt/loc/vehicles.loc"] = _names(vloc, [(LOC0 + i, "VEH_BMB_%d" % i, c["name"]) for i, c in enumerate(cars)])


def _car_lists(mp_d, uids):
    """multiplayer.bin: the cars in the car list + unlocked at rank 1. Both lists share one block with other
    lists, so each gets a fresh copy (old entries + ours) at the END of the block; nothing else moves."""
    for u in uids:
        mp_d, _ = kslf_write.insert_uid(mp_d, u, "AmaxVehicle")
    mp = Db(mp_d)
    mi = next(i for i in range(len(mp.objs)) if mp.tname(i) == "MultiplayerConfig")
    mt = mp.objs[mi]["blocks"][0]["type"]
    veh_off, _ = mp.field(mt, "vehicles")
    lvl_off, _ = mp.field(mt, "leveling_data")
    lvl_t = next(i for i, t in enumerate(mp.types) if t["name"] == "MultiplayerLevel")
    unl_off, _ = mp.field(lvl_t, "VehicleUnlocks")

    def fn(c):
        refs = b"".join(struct.pack("<I", c.add_uid(u, "AmaxVehicle")) for u in uids)

        def extend(where):
            at = where()
            blk, first, cnt = struct.unpack_from("<HHI", c.data, at)
            if blk == 0xFFFF:
                blk = next(i for i, r in enumerate(c.rows) if c.types[r[0]]["name"] == "AmaxVehicle" and r[2] == 2)
                old = b""
            else:
                old = bytes(c.data[c.blocks[blk] + 4 * first:c.blocks[blk] + 4 * (first + cnt)])
            end = c.rows[blk][1]
            c.insert(blk, end, old + refs)
            struct.pack_into("<HHI", c.data, where(), blk, end, cnt + len(uids))  # (the insert may move it)
        extend(lambda: veh_off)

        def rank1():
            lblk, lfirst, _ = struct.unpack_from("<HHI", c.data, lvl_off)
            return c.blocks[lblk] + lfirst * mp.types[lvl_t]["size"] + unl_off
        extend(rank1)
    return _edit(mp_d, mi, fn)


def _tmp(data):
    import tempfile
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".bin")
    f.write(data)
    f.close()
    return f.name


def _edit(data, i, fn):
    a, b = _tmp(data), _tmp(b"")
    try:
        return kslf_write.edit_object(a, b, i, fn)
    finally:
        os.unlink(a)
        os.unlink(b)


def _names(vloc, add):
    import loc
    a, b = _tmp(vloc), _tmp(b"")
    try:
        return loc.write(a, b, add=add)
    finally:
        os.unlink(a)
        os.unlink(b)


def wheel_file(amax, car):
    """a car's wheels model name: its AmaxWheelModel's fileName (not always the car's name - e.g. the Shelby's
    is shelby_mustanggt500)"""
    wi = amax.obj_of_uid(amax.ref_uid(amax.cars()[car], "wheelModel"))
    o = amax.objs[wi]
    off, _ = amax.field(o["blocks"][0]["type"], "fileName")
    at = struct.unpack_from("<I", amax.data, o["blocks"][0]["offset"] + off)[0]
    return bytes(o["pool"][at:o["pool"].index(b"\0", at)]).decode("latin1")


def model_aliases(wld, U):
    """root.pak {new path: existing path}: each car's model + wheels by its Name"""
    cars = wld.get("cars") or []
    if not cars:
        return {}
    amax = Db(open(os.path.join(U, "xt", "amax.bin"), "rb").read())
    out = {}
    for car, name in zip(cars, internal_names(cars)):
        folder = "\\models\\vehicles\\"
        out[folder + name.lower() + ".model"] = folder + car["base"].lower() + ".model"
        out[folder + "wheels\\" + name.lower() + ".model"] = \
            folder + "wheels\\" + wheel_file(amax, car.get("wheels") or car["base"]).lower() + ".model"
    return out

"""BUILD the player's custom cars (world["cars"], Custom car tab) into the game's database.
Per car (uids CA400000 + 4 * i: car, physics, paint, its own copy of the wheel car's AmaxWheelModel):
  racingphysics.bin  RP_CarDynamics = copy of the body car's, every stat moved along its class's trend (below);
                                      its geometry (wheel base / track) never changed: the real car took it over
  vehicles.bin       VEH_PaintJob   = copy of the body car's first paint, our two colours
  amax.bin           AmaxVehicle    = copy of the body car: own name, class stats, icon, top speed, physics, paint,
                                      wheels of the wheel car (wheelModel + sizes)
  multiplayer.bin    car list + unlocked at rank 1
  loc/vehicles.loc   the name
and root.pak (model_aliases): the game finds a car's model by its Name: models\\vehicles\\<name>.model (+ wheels\\)
-> aliases of the body car's model and the wheel car's wheels.

FAIR HANDLING: each stat sets its physics fields by its place in the class: a stat at the class's highest = the
field at the class's highest (or lowest, if the class's cars go that way - the least-squares trend of the field
against the menu stat, e.g. more drift = weaker rear grip). So a stat at the top drives like the class's most
extreme car for it - and never past the class."""
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
DRIVER_DOWN = 1.5                                  # an imported body: the driver this much lower (out of sight)
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


def world_cars(wld):
    """world["cars"] as the build uses them: a car with an imported .glb body is always built on its class's body
    car (car_glb.GLB_BODY - the tab does the same; a world saved before that may still name another one)"""
    import car_glb
    if os.environ.get("BMB_NO_CARS"):              # (test: build as if there were no custom cars)
        return []
    return [dict(c, base=car_glb.GLB_BODY[c["class"]]) if c.get("body_glb") else c for c in wld.get("cars") or []]


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
    lim = cd.limits(car["class"])

    def place(stat, ys, slope):
        """the stat's place in its class range (0 = lowest car, 1 = highest) -> the same place in the field's
        class range, in the direction the class's cars go (slope)"""
        lo, hi = lim[stat]
        t = 0.5 if hi == lo else (car["stats"][stat] - lo) / float(hi - lo)
        return min(ys) + (t if slope >= 0 else 1 - t) * (max(ys) - min(ys))
    for stat, fields in TREND.items():
        xs = [c[stat] for c in cls_cars]
        for f in fields:
            per_kg = f == "engine.engineTorque_Rpm.yScale"        # engine power: per kg of the car's weight
            ys = [rp.get(rpi[c["name"]], f) / (rp.get(rpi[c["name"]], "mass.mass") if per_kg else 1.0)
                  for c in cls_cars]
            rp_out[f] = place(stat, ys, _fit(xs, ys)) * (rp.get(rpi[car["base"]], "mass.mass") if per_kg else 1.0)
    for stat, fields in CAR_TREND.items():
        xs = [c[stat] for c in cls_cars]
        for f in fields:
            ys = [amax.get(names[c["name"]], f) for c in cls_cars]
            car_out[f] = int(round(place(stat, ys, _fit(xs, ys))))
    return rp_out, car_out


def _set(db_types_field, data, at, t, path, value):
    off, tn = db_types_field(t, path)
    struct.pack_into(PACK.get(tn, "<I"), data, at + off, value)


def add_cars(files, wld, U, log=print):
    """put world["cars"] into the database files ({pack path: bytes}; missing ones are read from U\\xt)"""
    cars = world_cars(wld)
    if not cars:
        return
    rd = lambda n: files.get("gamedata/xt/" + n) or open(os.path.join(U, "xt", *n.split("/")), "rb").read()
    amax_d, rp_d, veh_d, mp_d, vloc = (rd(n) for n in ("amax.bin", "racingphysics.bin", "vehicles.bin",
                                                       "multiplayer.bin", "loc/vehicles.loc"))
    names = internal_names(cars)
    added = []
    for i, (car, name) in enumerate(zip(cars, names)):
        car_uid, dyn_uid, paint_uid, wheel_uid = (UID0 + 4 * i + k for k in range(4))
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
        # (no own wheel spacing: the game draws the wheels where geometry.wheelBase / trackWidth put them, and a
        # custom car with other values gave the REAL body car its spacing too - tested 2026-10-28; the imported
        # body is fitted to the body car's wheels instead)

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
        if car.get("body_glb"):                      # an imported body: its own colours (in its points), white paint
            cols = [(1.0, 1.0, 1.0)] * 2

        def paint_patch(data, pool, blocks):
            cb, cf, cc = struct.unpack_from("<HHI", data, blocks[0] + c_off)
            for j in range(cc):
                struct.pack_into("<3f", data, blocks[cb] + 12 * (cf + j), *cols[min(j, 1)])
        _, veh_d = kslf_write.clone_object_any(pi, None, paint_uid, paint_patch, data_in=veh_d)
        # ---- the car
        amax_d, _ = kslf_write.insert_uid(amax_d, dyn_uid, "RP_CarDynamics")
        amax_d, _ = kslf_write.insert_uid(amax_d, paint_uid, "VEH_PaintJob")
        # our OWN copy of the wheel car's AmaxWheelModel, under our OWN file name (wheels\<our name>.model = an
        # alias of the wheel car's file, model_aliases): the game loads a wheel set once per file name - with
        # the shared one the REAL car got our wheel spacing (user 2026-10-28: its files + data unchanged, normal
        # Blur fine; a copied record with the same name did not help)
        amax = Db(amax_d)
        wm = amax.obj_of_uid(amax.ref_uid(amax.cars().get(car.get("wheels") or car["base"], bi), "wheelModel"))
        fn_off, _ = amax.field(amax.objs[wm]["blocks"][0]["type"], "fileName")

        def wheel_patch(data, pool, blocks, name=name):
            struct.pack_into("<I", data, blocks[0] + fn_off, len(pool))
            pool += name.encode("latin1") + b"\0"
        _, amax_d = kslf_write.clone_object_any(wm, None, wheel_uid, wheel_patch, data_in=amax_d)
        amax = Db(amax_d)
        bi = amax.cars()[car["base"]]
        wi = amax.cars().get(car.get("wheels") or car["base"], bi)
        dyn_row, paint_row = amax.row_of_uid(dyn_uid), amax.row_of_uid(paint_uid)
        wheel_vals = {f: amax.get(wi, f) for f in ("wheelScaleFront", "wheelScaleRear", "wheelDepthFront",
                                                   "wheelDepthRear")}
        wheel_vals["wheelModel"] = amax.row_of_uid(wheel_uid)
        s = car["stats"]
        mph = cd.top_speed(car["class"], s["speed"])
        sets = dict(car_vals, LocString=LOC0 + i, dynamics=dyn_row, UiSpeed=s["speed"],
                    UiAcceleration=s["acceleration"], UiDrift=s["drift"], UiGrip=s["grip"], UiStrength=s["strength"],
                    UiMaxSpeed=mph, GameplayTopSpeed=mph, BossAbilitySlot=0, **{"class": car["class"]}, **wheel_vals)
        if car.get("body_glb"):                      # the body car's driver stuck out of the imported body (Bowler:
            sets["DriverOffset.vy"] = amax.get(bi, "DriverOffset.vy") - DRIVER_DOWN     # an open buggy): down
        icon_off, _ = amax.field(vt, "UiIconName")

        def car_patch(data, pool, blocks):
            for f, v in sets.items():
                _set(amax.field, data, blocks[0], vt, f, v)
            struct.pack_into("<I", data, blocks[0] + icon_off, len(pool))
            pool += name.encode("latin1") + b"\0"            # own icon name: the car list's silhouette (car_icon)
                                                               # + the menu picture (menu_pictures) are found by it
            pb, pf, _ = struct.unpack_from("<HHI", data, blocks[0] + p_off)
            struct.pack_into("<I", data, blocks[pb] + 4 * pf, paint_row)             # paint 1 = ours, only one
            struct.pack_into("<HHI", data, blocks[0] + p_off, pb, pf, 1)
        _, amax_d = kslf_write.clone_object_any(bi, name, car_uid, car_patch, data_in=amax_d)
        added.append(car_uid)
        log("custom car %s (%s, class %s, body %s, wheels %s): %d mph, stats %s" % (
            car["name"], name, cd.CLASSES[car["class"]], car["base"], car.get("wheels") or car["base"], mph,
            " ".join("%s %d" % (k, v) for k, v in s.items())))
        if car.get("body_glb"):
            log("   (imported body %s on the wheels of %s - written with the root pack)" % (car["body_glb"], car["base"]))
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
    cars = world_cars(wld)
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


def menu_pictures(frontend, wld):
    """ui_frontend.bpr with each car's menu picture under its own icon name (= its Name): the chosen game icon's"""
    import bpr
    cars = world_cars(wld)
    for car, name in zip(cars, internal_names(cars)):
        frontend = bpr.alias_picture(frontend, name, car["icon"])
    return frontend


def icon_files(wld, log=print):
    """root.pak {path: bytes}: the car icon sheet with each car's mini icon (car_icon.py)"""
    import car_icon
    return car_icon.files(dict(wld, cars=world_cars(wld)), internal_names(world_cars(wld)), log)


def model_files(wld, log=print):
    """root.pak {path: bytes}: the own model of each car with an imported body (car_glb.py: the .glb as the body
    of the body car's model) + every car's OWN copy of its wheels file (wheels\\<name>.model was only an alias =
    the same stored data as the real car's: the game kept one wheel set for both, the real car got our wheel
    spacing - user 2026-10-28; without our cars it was right)"""
    import car_glb
    import game_preview
    import paths
    cars = world_cars(wld)
    out = {}
    amax = None
    for car, name in zip(cars, internal_names(cars)):
        if car.get("body_glb"):
            out["\\models\\vehicles\\" + name.lower() + ".model"] = car_glb.glb_model(
                car, os.path.join(paths.TRACKS, "cars", car["body_glb"]), log)
        amax = amax or Db(open(os.path.join(paths.UNPACKED, "xt", "amax.bin"), "rb").read())
        src = "models\\vehicles\\wheels\\%s.model" % wheel_file(amax, car.get("wheels") or car["base"]).lower()
        out["\\models\\vehicles\\wheels\\" + name.lower() + ".model"] = game_preview._model_bytes(src)
    return out

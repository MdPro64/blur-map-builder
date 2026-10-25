"""TEST CAR: copy of Dodge_Challenger_Tuner (class B) named MDPRO, other icon, acceleration + top speed pushed to
300 mph, so we can see in game whether an added car works.
  amax.bin          AmaxVehicle clone (new uid, name, menu stats, top speed, icon, own physics)
  racingphysics.bin RP_CarDynamics clone (engine torque x2)
  multiplayer.bin   added to the car list + unlocked at rank 1
  loc/vehicles.loc  the name "MDPRO"
  root.pak          models\vehicles\mdpro.model (+ wheels) = aliases of the source car's model
usage: python make_test_car.py [game folder]   (default: J:\\blur - user 1; edits rebrn\\gamedata.pak, backup first)"""
import os
import shutil
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.dirname(HERE)
sys.path[:0] = [os.path.join(MB, p) for p in ("kslf", "pak")]
import kslf
import kslf_write
import loc
import pak
import schema

GAME = sys.argv[1] if len(sys.argv) > 1 else r"J:\blur - user 1"
PAK = os.path.join(GAME, "rebrn", "gamedata.pak")
BACKUP = PAK + ".before_car"
SRC_CAR = "Dodge_Challenger_Tuner"
ICON_CAR = "Koenigsegg_CCXR_Stock"            # a different car's menu icon
NAME = "MDPRO"
CAR_UID, DYN_UID = 0xCA400000, 0xCA400001       # "car" uids, unused in the game files
LOC_ID = 0x4D445052                           # "MDPR"
TOP_SPEED = 300
TORQUE_X = 3.0
PACK = {"i32": "<i", "u32": "<I", "f32": "<f", "bool": "<I", "u8": "<B", "u16": "<H", "i16": "<h", "LocId": "<I",
        "sz8": "<I"}


def fields(types, t, base=0):
    for b, off in types[t]["bases"]:
        yield from fields(types, b, base + off)
    for f in types[t]["fields"]:
        yield f["name"], f["offset"] + base, f["flags"], f["type"]


def field(types, t, path):
    """(offset, type name) of a dotted field path inside type t (inline structs only)"""
    off = 0
    for part in path.split("."):
        name, o, flags, ft = next(x for x in fields(types, t) if x[0] == part)
        off, t = off + o, ft
    return off, types[t]["name"]


def load(data):
    k = kslf.Kslf(data)
    types = schema.load_schema(k)
    return k, types, kslf.Objects(k, types).objects


def uid_rows(data, k):
    o4, n4 = k.sections[4]
    return [struct.unpack_from("<IHH", data, o4 + 8 * i) for i in range(n4)]


def cstr(pool, at):
    return bytes(pool[at:pool.index(b"\0", at)]).decode("latin1")


def find_car(data, name):
    k, types, objs = load(data)
    for i, o in enumerate(objs):
        b = o["blocks"][0]
        if types[b["type"]]["name"] == "AmaxVehicle" and cstr(o["pool"], struct.unpack_from("<I", data, b["offset"] + 12)[0]) == name:
            return i, o, types, k
    raise SystemExit("car not found: " + name)


def main():
    if not os.path.exists(BACKUP):
        shutil.copy2(PAK, BACKUP)
    p = pak.Pak(BACKUP)
    rd = lambda n: p.read(p.find(n))
    amax, rp = rd("gamedata/xt/amax.bin"), rd("gamedata/xt/racingphysics.bin")
    mp, vloc = rd("gamedata/xt/multiplayer.bin"), rd("gamedata/xt/loc/vehicles.loc")

    # ---- the car's physics: clone its RP_CarDynamics with a stronger engine
    ci, car, types, k = find_car(amax, SRC_CAR)
    vt = car["blocks"][0]["type"]
    dyn_off, _ = field(types, vt, "dynamics")
    dyn_uid = uid_rows(amax, k)[struct.unpack_from("<I", amax, car["blocks"][0]["offset"] + dyn_off)[0]][0]
    rk, rtypes, robjs = load(rp)
    ri = uid_rows(rp, rk)[[r[0] for r in uid_rows(rp, rk)].index(dyn_uid)][2]
    ys_off, _ = field(rtypes, robjs[ri]["blocks"][0]["type"], "engine.engineTorque_Rpm.yScale")

    def dyn_patch(data, pool, blocks):
        v = struct.unpack_from("<f", data, blocks[0] + ys_off)[0]
        struct.pack_into("<f", data, blocks[0] + ys_off, v * TORQUE_X)
        print("engine torque scale %.0f -> %.0f" % (v, v * TORQUE_X))
    _, rp = kslf_write.clone_object_any(ri, None, DYN_UID, dyn_patch, data_in=rp)

    # ---- the car itself
    amax, dyn_row = kslf_write.insert_uid(amax, DYN_UID, "RP_CarDynamics")
    ci, car, types, k = find_car(amax, SRC_CAR)
    _, icon_car, _, _ = find_car(amax, ICON_CAR)
    icon_off, _ = field(types, vt, "UiIconName")
    icon_pool = icon_car["pool"]
    icon = cstr(icon_pool, struct.unpack_from("<I", amax, icon_car["blocks"][0]["offset"] + icon_off)[0])
    sets = {"LocString": LOC_ID, "dynamics": dyn_row, "UiSpeed": 10, "UiAcceleration": 10, "UiMaxSpeed": TOP_SPEED,
            "GameplayTopSpeed": TOP_SPEED, "class": 1}

    def car_patch(data, pool, blocks):
        for name, value in sets.items():
            off, tn = field(types, vt, name)
            struct.pack_into(PACK.get(tn, "<I"), data, blocks[0] + off, value)
        struct.pack_into("<I", data, blocks[0] + icon_off, len(pool))
        pool += icon.encode("latin1") + b"\0"
    _, amax = kslf_write.clone_object_any(ci, NAME, CAR_UID, car_patch, data_in=amax)
    print("car %s (%08X), icon %s, physics %08X" % (NAME, CAR_UID, icon, DYN_UID))

    # ---- multiplayer: car list + unlocked at rank 1. Both lists share one block with other lists, so each gets
    # a fresh copy (old entries + ours) at the END of the block; nothing else moves.
    mp, _ = kslf_write.insert_uid(mp, CAR_UID, "AmaxVehicle")       # any uid (add_uid only takes the highest)
    mk, mtypes, mobjs = load(mp)
    mi = next(i for i, o in enumerate(mobjs) if mtypes[o["blocks"][0]["type"]]["name"] == "MultiplayerConfig")
    mt = mobjs[mi]["blocks"][0]["type"]
    veh_off, _ = field(mtypes, mt, "vehicles")
    lvl_off, _ = field(mtypes, mt, "leveling_data")
    lvl_t = next(i for i, t in enumerate(mtypes) if t["name"] == "MultiplayerLevel")
    unl_off, _ = field(mtypes, lvl_t, "VehicleUnlocks")

    def mp_fn(c):
        ref = c.add_uid(CAR_UID, "AmaxVehicle")

        def extend(where):
            at = where()                                   # (an insert before it moves it: ask again after)
            blk, first, cnt = struct.unpack_from("<HHI", c.data, at)
            if blk == 0xFFFF:
                blk = next(i for i, r in enumerate(c.rows) if c.types[r[0]]["name"] == "AmaxVehicle" and r[2] == 2)
                old = b""
            else:
                old = bytes(c.data[c.blocks[blk] + 4 * first:c.blocks[blk] + 4 * (first + cnt)])
            end = c.rows[blk][1]
            c.insert(blk, end, old + struct.pack("<I", ref))
            struct.pack_into("<HHI", c.data, where(), blk, end, cnt + 1)
            return cnt + 1
        n = extend(lambda: veh_off)

        def rank1():
            lblk, lfirst, _ = struct.unpack_from("<HHI", c.data, lvl_off)
            return c.blocks[lblk] + lfirst * mtypes[lvl_t]["size"] + unl_off
        u = extend(rank1)
        print("multiplayer: %d cars in the list, %d unlocked at rank 1" % (n, u))
    tmp_in, tmp_out = os.path.join(HERE, "_mp_in.bin"), os.path.join(HERE, "_mp_out.bin")
    open(tmp_in, "wb").write(mp)
    mp = kslf_write.edit_object(tmp_in, tmp_out, mi, mp_fn)

    # ---- name
    open(tmp_in, "wb").write(vloc)
    vloc = loc.write(tmp_in, tmp_out, add=[(LOC_ID, "VEH_MDPRO", NAME)])
    os.remove(tmp_in)
    os.remove(tmp_out)

    pak.replace_files(BACKUP, PAK, {"gamedata/xt/amax.bin": amax, "gamedata/xt/racingphysics.bin": rp,
                                    "gamedata/xt/multiplayer.bin": mp, "gamedata/xt/loc/vehicles.loc": vloc})
    print("written:", PAK, "(original kept as", BACKUP + ")")

    # ---- 3D model: the game loads models\vehicles\<car name>.model (+ wheels) - point ours at the source car's
    root = os.path.join(GAME, "rebrn", "root.pak")
    if not os.path.exists(root + ".before_car"):
        shutil.copy2(root, root + ".before_car")
    src, new = SRC_CAR.lower(), NAME.lower()
    folder = "\\models\\vehicles\\"
    pak.add_aliases(root + ".before_car", root, {
        folder + new + ".model": folder + src + ".model",
        folder + "wheels\\" + new + ".model": folder + "wheels\\" + src + ".model"})
    print("written:", root, "(model aliases)")


if __name__ == "__main__":
    main()

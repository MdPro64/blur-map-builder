"""
FIRST START: make everything the map builder needs from the player's OWN game (nothing of the game is shipped).

Steps (each one is skipped when already done; run_all(log) runs them in order):
  check_game        Blur.exe is a version we know (the 4 places we change hold the expected bytes)
  backup_packs      cache\\gamedata.pak + cache\\root.pak -> <data>\\base\\*.original (only if they are untouched)
  unpack            the few files the build reads, out of gamedata.pak
  level_template    levels\\showroom with its scenery hidden (the level every location starts from)
  fetch_addon       TimberStalker's BlurImportExport model reader (his public GitHub repo, pinned + hash-checked)
  make_catalog      the object library (every placeable object of the game) for the 3D tab
  make_reborn       "Blur REBORN.exe" = Blur.exe + 4 changes: the pak-signature check off and gamedata.pak /
                    root.pak read from rebrn\\ (Blur.exe and cache\\ stay as they are)
  install_plugin    our Amax QuickStart plugin (the map tester's "start a race on my map")
"""
import hashlib
import os
import shutil
import struct
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [HERE] + [os.path.join(HERE, p) for p in ("pak", "level", "kslf")]
import paths  # noqa: E402

# ---- Blur.exe -> Blur REBORN.exe: (file offset, original bytes, new bytes)
EXE_PATCHES = [
    (0x8B667A, b"\x75", b"\xEB"),                                          # skip the pak signature check
    (0xA668F8, b"cache/gamedata.pak\0", b"rebrn/gamedata.pak\0"),         # mount list
    (0xA66628, b"GAME:\\cache\\gamedata.pak\0", b"GAME:\\rebrn\\gamedata.pak\0"),
    (0xA66978, b"cache/root.pak\0", b"rebrn/root.pak\0"),
]
KNOWN_EXE_SHA256 = {"dba0264b7808a5e38d99c8c162ecd71ab06a48fbc424feb1b9a3bc1bff2fbf3f"}   # Blur PC 1.2 (tested; 1.0 does not work)

ADDON_URL = ("https://raw.githubusercontent.com/TimberStalker/BlurImportExport/"
             "bfe0c8e113fb0562907401af33129dcd9bee07f8/__init__.py")
ADDON_SHA256 = "db04925c0d7a3734aecdd79f4112f5e3a9aff58ae0b4f2e508077af16e5af61c"

UNPACK = ["gamedata/levels/jpn_mharuna/actorpack.xt", "gamedata/levels/jpn_mharuna/jpn_mharuna_1.aid",
          "gamedata/levels/usa_la_river/actorpack.xt", "gamedata/levels/usa_la_river/usa_la_river_6.aid"]
UNPACK_PREFIX = "gamedata/xt/"                     # + everything under xt\ (the game database, ~30 MB)
OUR_MARK = "gamedata/levels/flat_earth/actorpack.xt"   # in OUR built packs only (a pack with it is not original)

PLUGIN_LINE = 'require("quickstart.quickstart"), -- Blur Map Builder: custom LAN race on the map being tested'


class SetupError(Exception):
    pass


def _sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


# ---------------------------------------------------------------------------------------------- the steps
def check_game(log=print):
    if not os.path.exists(paths.BLUR_EXE):
        raise SetupError("Blur.exe was not found in %s.\nPut \"%s.exe\" into your Blur folder (next to Blur.exe) and "
                         "start it from there." % (paths.GAME, paths.APP_NAME))
    for need in (os.path.join(paths.CACHE, "gamedata.pak"), os.path.join(paths.CACHE, "root.pak"),
                 os.path.join(paths.LEVELS, "showroom", "level.levelrender")):
        if not os.path.exists(need):
            raise SetupError("A game file is missing: %s" % need)
    d = open(paths.BLUR_EXE, "rb").read()
    for off, old, new in EXE_PATCHES:
        if d[off:off + len(old)] != old:
            raise SetupError("This Blur.exe is not a version the map builder knows. The map builder needs Blur "
                             "version 1.2 (Blur 1.0 does not work - update the game to 1.2). Nothing was changed.\n"
                             "Already on 1.2? Send %s on Discord the file "
                             "%s" % (paths.DISCORD, os.path.join(paths.LOGS, "setup.log")))
    if not os.path.isdir(paths.AMAX):
        log("NOTE: Amax was not found (amax\\). The map tester's quick race needs Amax; everything else works.")
    sha = hashlib.sha256(d).hexdigest()
    log("Blur.exe OK (%s)" % ("known version" if sha in KNOWN_EXE_SHA256 else "untested version, same bytes"))


def _is_original(pak_path):
    import pak
    try:
        return pak.Pak(pak_path).find(OUR_MARK) is None
    except Exception:          # noqa: BLE001 - not readable: not usable either
        return False


def backup_packs(log=print):
    os.makedirs(paths.BASE, exist_ok=True)
    for name, dst in (("gamedata.pak", paths.GAMEDATA_ORIGINAL), ("root.pak", paths.ROOT_ORIGINAL)):
        if os.path.exists(dst):
            continue
        src = os.path.join(paths.CACHE, name)
        if not _is_original(src):
            raise SetupError("cache\\%s is not the game's original (an older map build is installed in it).\n"
                             "Put the original back (verify the game files in Steam / reinstall), then start again."
                             % name)
        log("copying the original cache\\%s (%d MB) ..." % (name, os.path.getsize(src) >> 20))
        shutil.copyfile(src, dst + ".part")
        os.replace(dst + ".part", dst)


def unpack(log=print):
    import pak
    p = pak.Pak(paths.GAMEDATA_ORIGINAL)
    names = {}
    for e in p.files:
        n = p.names[e["name_off"]:p.names.index(b"\0", e["name_off"])].decode("latin1").replace("\\", "/").lstrip("/")
        names[n.lower()] = e
    want = [n for n in names if n.startswith(UNPACK_PREFIX)] + [n.lower() for n in UNPACK]
    done = 0
    for n in want:
        e = names.get(n)
        if e is None:
            raise SetupError("gamedata.pak has no %s" % n)
        out = os.path.join(paths.UNPACKED, *n.split("/")[1:])         # (UNPACKED = ...\gamedata)
        if os.path.exists(out) and os.path.getsize(out) == e["size"]:
            continue
        data = p.read(e)
        if data is None:
            continue
        os.makedirs(os.path.dirname(out), exist_ok=True)
        open(out, "wb").write(data)
        done += 1
    log("game data: %d file(s) unpacked" % done)


def level_template(log=print):
    import hide_scenery
    out = paths.LEVEL_TEMPLATE
    if all(os.path.exists(os.path.join(out, f)) for f in ("level.level", "level.levelrender", "level.levelstream")):
        return
    os.makedirs(out, exist_ok=True)
    src = os.path.join(paths.LEVELS, "showroom")
    shutil.copyfile(os.path.join(src, "level.level"), os.path.join(out, "level.level"))
    for name in ("level.levelrender", "level.levelstream"):
        data, hits = hide_scenery.hide(open(os.path.join(src, name), "rb").read(), loose=True)
        open(os.path.join(out, name), "wb").write(data)
        log("level template %s: %d scenery nodes hidden" % (name, len(hits)))


def fetch_addon(log=print):
    dst = os.path.join(paths.ADDON, "BlurImportExport", "__init__.py")
    if os.path.exists(dst) and _sha256(dst) == ADDON_SHA256:
        return
    log("downloading the model reader (BlurImportExport by TimberStalker, github.com/TimberStalker) ...")
    try:
        data = urllib.request.urlopen(ADDON_URL, timeout=60).read()
    except OSError as ex:
        raise SetupError("Could not download the model reader (internet needed once): %s" % ex)
    if hashlib.sha256(data).hexdigest() != ADDON_SHA256:
        raise SetupError("The downloaded model reader is not the expected file - nothing was used.")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    open(dst, "wb").write(data)


def make_catalog(log=print):
    if os.path.exists(paths.CATALOG):
        return
    log("making the object library (every placeable object of the game; a few minutes, once) ...")
    sys.path.insert(0, os.path.join(HERE, "library"))
    import make_catalog as mc
    mc.main(log=log)


def make_reborn(log=print):
    d = bytearray(open(paths.BLUR_EXE, "rb").read())
    for off, old, new in EXE_PATCHES:
        assert d[off:off + len(old)] == old
        d[off:off + len(new)] = new
    if not (os.path.exists(paths.REBORN_EXE) and open(paths.REBORN_EXE, "rb").read() == bytes(d)):
        open(paths.REBORN_EXE, "wb").write(bytes(d))
        log("made Blur REBORN.exe (Blur.exe itself is not changed)")
    # until the first build, rebrn\ holds the game's ORIGINAL packs (hard links: no extra disk space) - with an empty
    # rebrn\ Blur REBORN crashed at start (user 2026-10-20). Installs REPLACE these files (never write into them),
    # so the originals behind the links are never changed.
    os.makedirs(paths.PACK_DIR, exist_ok=True)
    for name, src in (("gamedata.pak", paths.GAMEDATA_ORIGINAL), ("root.pak", paths.ROOT_ORIGINAL)):
        dst = os.path.join(paths.PACK_DIR, name)
        if os.path.exists(dst):
            continue
        try:
            os.link(src, dst)
        except OSError:
            shutil.copyfile(src, dst + ".part")
            os.replace(dst + ".part", dst)
        log("rebrn\\%s = the game's original (until your first build)" % name)


def install_plugin(log=print):
    if not os.path.isdir(paths.PLUGINS):
        return
    src = os.path.join(HERE, "amax_plugin", "quickstart")
    dst = os.path.join(paths.PLUGINS, "quickstart")
    os.makedirs(dst, exist_ok=True)
    for f in os.listdir(src):
        if f.endswith(".lua") and f != "target.lua":
            shutil.copyfile(os.path.join(src, f), os.path.join(dst, f))
    if not os.path.exists(paths.QUICKSTART_TARGET):
        open(paths.QUICKSTART_TARGET, "w", newline="\n").write("return { enabled = false }\n")
    lst = os.path.join(paths.PLUGINS, "plugins.lua")
    if os.path.exists(lst):
        s = open(lst, "rb").read().decode("utf-8-sig")
        if "quickstart.quickstart" not in s:
            i = s.rfind("}")
            s = s[:i].rstrip() + ("\n" if s[:i].rstrip().endswith(",") else ",\n") + "\t" + PLUGIN_LINE + "\n}\n"
            open(lst, "wb").write(s.encode("utf-8"))             # no BOM (the game's Lua)
            log("Amax: QuickStart plugin added to plugins.lua")


STEPS = [("Checking the game", check_game), ("Backing up the original packs", backup_packs),
         ("Unpacking the game data", unpack), ("Level template", level_template),
         ("Model reader", fetch_addon), ("Object library", make_catalog), ("Blur REBORN.exe", make_reborn),
         ("Amax plugin", install_plugin)]


def needed():
    """True if a step still has work to do (quick checks only)"""
    return not all(os.path.exists(p) for p in (paths.GAMEDATA_ORIGINAL, paths.ROOT_ORIGINAL,
                                               os.path.join(paths.UNPACKED, "xt", "amax.bin"),
                                               os.path.join(paths.LEVEL_TEMPLATE, "level.levelstream"),
                                               os.path.join(paths.ADDON, "BlurImportExport", "__init__.py"),
                                               paths.CATALOG, paths.REBORN_EXE))


def run_all(log=print, progress=None):
    paths.ensure_dirs()
    with open(os.path.join(paths.LOGS, "setup.log"), "a", encoding="utf-8") as f:
        def both(msg):
            f.write(msg + "\n")
            f.flush()
            log(msg)
        for k, (label, fn) in enumerate(STEPS):
            if progress:
                progress(k, len(STEPS), label)
            both("== " + label)
            fn(both)
        if progress:
            progress(len(STEPS), len(STEPS), "Ready")


if __name__ == "__main__":
    run_all()

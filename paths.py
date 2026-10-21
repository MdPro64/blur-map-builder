"""
WHERE EVERYTHING IS - one place for every folder / file the map builder uses.

Two layouts:
  DEV        the source checkout: map_builder\\ next to the game (E:\\blur - bak\\map_builder), the old working
             folders (_mod_project\\work, unpacked, backups ...) - what the project grew up with.
  INSTALLED  "Blur Map Builder.exe" copied into the player's Blur folder (PyInstaller exe, or BMB_INSTALLED=1 for a
             test from source): everything the builder makes lives in  <game>\\Blur Map Builder\\  and is made from
             the player's OWN game files by setup_game.py on the first start (nothing of the game is shipped).
Both: the built packs go to <game>\\rebrn\\ and are played with "Blur REBORN.exe" (the game's own Blur.exe and
cache\\ packs are never changed).
"""
import os
import sys

APP_NAME = "Blur Map Builder"
VERSION = "1.0.2"
GITHUB_REPO = "MdPro64/blur-map-builder"       # GitHub owner/name: updates come from its latest release
DISCORD = "mdproii"

FROZEN = bool(getattr(sys, "frozen", False))
APP = (getattr(sys, "_MEIPASS", None) if FROZEN else None) or os.path.dirname(os.path.abspath(__file__))


def _game_dir():
    env = os.environ.get("BMB_GAME")
    if env:
        return os.path.abspath(env)
    if FROZEN:
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(APP)                    # the checkout sits in the game folder


GAME = _game_dir()
INSTALLED = FROZEN or os.environ.get("BMB_INSTALLED") == "1"

# the game's own files (read only)
BLUR_EXE = os.path.join(GAME, "Blur.exe")
CACHE = os.path.join(GAME, "cache")
LEVELS = os.path.join(GAME, "levels")
AMAX = os.path.join(GAME, "amax")
PLUGINS = os.path.join(AMAX, "plugins")
QUICKSTART_TARGET = os.path.join(PLUGINS, "quickstart", "target.lua")

# ours, in the game folder
REBORN_EXE = os.path.join(GAME, "Blur REBORN.exe")        # Blur.exe + 3 changes (pak check off, packs from rebrn\)
TESTER_EXE = os.path.join(GAME, "Blur Map Tester.exe")
PACK_DIR = os.path.join(GAME, "rebrn")                     # the built gamedata.pak / root.pak (Blur REBORN reads them)

if INSTALLED:
    DATA = os.path.join(GAME, APP_NAME)
    BASE = os.path.join(DATA, "base")                      # made once from the player's game (setup_game.py)
    UNPACKED = os.path.join(BASE, "unpacked", "gamedata")  # the few files the build reads, out of gamedata.pak
    GAMEDATA_ORIGINAL = os.path.join(BASE, "gamedata.pak.original")
    ROOT_ORIGINAL = os.path.join(BASE, "root.pak.original")
    ROOT_BASE = ROOT_ORIGINAL                              # the root pack our models / minimaps are added to
    MODEL_PAK = ROOT_ORIGINAL                              # where the game's models are read from
    LEVEL_TEMPLATE = os.path.join(BASE, "level_template")  # showroom level with its scenery hidden
    ADDON = os.path.join(BASE, "addon")                    # TimberStalker's BlurImportExport (downloaded)
    CATALOG = os.path.join(BASE, "catalog.json")
    MODEL_CACHE = os.path.join(BASE, "model_cache")
    WORK = os.path.join(DATA, "work")
    TRACKS = os.path.join(DATA, "maps")                    # the player's maps: world.json, pictures (never updated)
    LOGS = os.path.join(DATA, "logs")
else:
    PROJ = os.path.join(GAME, "_mod_project")
    DATA = PROJ
    BASE = PROJ
    UNPACKED = os.path.join(PROJ, "unpacked", "gamedata", "gamedata")
    GAMEDATA_ORIGINAL = os.path.join(PROJ, "backups", "gamedata.pak.original")
    ROOT_ORIGINAL = os.path.join(PROJ, "backups", "root.pak.original")
    ROOT_BASE = ROOT_ORIGINAL
    MODEL_PAK = ROOT_ORIGINAL
    LEVEL_TEMPLATE = os.path.join(PROJ, "work", "flat_earth", "level_template")
    ADDON = os.path.join(PROJ, "blender", "addon")
    CATALOG = os.path.join(APP, "library", "catalog.json")
    MODEL_CACHE = os.path.join(PROJ, "work", "model_cache")
    WORK = os.path.join(PROJ, "work")
    TRACKS = os.path.join(APP, "tracks")
    LOGS = os.path.join(PROJ, "logs")

FE_WORK = os.path.join(WORK, "flat_earth")                 # level files, per-location work folders
GAMEDATA_OUT = os.path.join(WORK, "gamedata_track.pak")
ROOT_OUT = os.path.join(WORK, "root_track.pak")
WORLD_FILE = os.path.join(TRACKS, "world.json")


def set_app_icon(root):
    """the MDPRO logo on every window of this program (title bar + taskbar), not Tk's feather. The exe carries it
    (release/icon.ico via --icon); from source the .ico itself"""
    ico = sys.executable if FROZEN else os.path.join(os.path.dirname(os.path.abspath(__file__)), "release", "icon.ico")
    try:
        # its own taskbar group (else a source run shows python's icon there)
        import ctypes
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("MDPRO." + APP_NAME.replace(" ", ""))
    except (AttributeError, OSError):
        pass
    try:
        root.iconbitmap(default=ico)          # default= : every Toplevel / dialog of this program too
    except Exception:                         # noqa: BLE001 - only looks
        pass


def game_running():
    """True while the GAME runs (Blur.exe / Blur REBORN.exe). Not a plain "blur" match: that also hit
    Blur.Map.Builder.exe / Blur.Map.Tester.exe themselves and every build said "close Blur first" (user 2026-10-21)"""
    import subprocess
    try:
        out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True,
                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)).stdout
    except OSError:
        return False
    names = {ln.split('","')[0].strip('"').lower() for ln in out.splitlines() if ln.strip()}
    return bool(names & {"blur.exe", os.path.basename(REBORN_EXE).lower()})


def ensure_dirs():
    for d in (WORK, FE_WORK, TRACKS, LOGS, PACK_DIR):
        os.makedirs(d, exist_ok=True)


def python_cmd(script, *args):
    """the command line that runs one of our .py scripts in its own process: python, or (in the exe) the exe itself
    with --run (bmb_main.py)"""
    if FROZEN:
        return [sys.executable, "--run", os.path.relpath(script, APP)] + list(args)
    return [sys.executable, "-B", "-u", script] + list(args)

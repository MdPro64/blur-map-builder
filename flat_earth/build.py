"""
Build "Flat Earth" (new location, folder Flat_Earth) with the sprint route "Home Run v1" (Flat_Earth_1), on top of
My First Track, into one gamedata pack.

  python build.py            -> _mod_project/work/gamedata_track.pak (+ the level folder files in work/flat_earth)

Game data (gamedata pack):
  amax.bin        City "Flat_Earth"  = clone of ShowRoom (obj 412): not dev-only, own name, folder Flat_Earth, bounds,
                                      location-wide lists -> the empty list 6992B639
                  Route "Flat_Earth_1" = clone of JPN_MHaruna_1 (obj 395, a sprint): our City, own name, thumbnail
                                      name; Haruna's physical objects (3ED4CF3F) and trigger zones (DE7D2096)
                                      -> the empty list 6992B639
  multiplayer.bin route added to the custom-race list
  cityandroutenames.loc  "Flat Earth", "Home Run v1"
  levels/flat_earth/actorpack.xt, levels/flat_earth/flat_earth_1.aid (gen_actorpack.py / gen_aid.py)
Level folder (game folder levels\\flat_earth): copies of levels\\showroom + our flat ground (physics/write_flat.py).
"""
import math
import os
import struct
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.dirname(HERE)
sys.path[:0] = [HERE, MB] + [os.path.join(MB, p) for p in ("kslf", "pak", "aid", "physics", "car")]
import paths               # noqa: E402
import build_track as B     # noqa: E402
import dump                 # noqa: E402
import gen_actorpack        # noqa: E402
import gen_aid              # noqa: E402
import road as road_mod     # noqa: E402
import road_layout          # noqa: E402
import road_mesh            # noqa: E402
import kslf                 # noqa: E402
import kslf_write           # noqa: E402
import loc                  # noqa: E402
import minimap              # noqa: E402
import network              # noqa: E402
import surface              # noqa: E402
import lighting             # noqa: E402

CITY_NAME, CITY_FOLDER = "Flat_Earth", "flat_earth"
ROUTE_NAME = "Flat_Earth_1"
CITY_UID, ROUTE_UID = 0xFFFFFF20, 0xFFFFFF21            # above My First Track's uids (FFFFFF01, lists FFFFFF10-15)
CITY_LOC, ROUTE_LOC = 0xF00D0010, 0xF00D0011
BLOOM_LOC = 0xF00D0B10                                   # common.loc "Bloom" (the Bloom plugin's menu row)
BLOOM_ROW = False       # the Bloom plugin's "Bloom" title in ui.bin / common.loc - plugin REMOVED 2026-10-20 (its row
                        # broke the Graphics screen's Accept; user: nothing that changes the game for other players)
UI_ADV_VSYNC_TITLE = 0xF14E4                             # ui.bin: text_id of SDR_VSync in Options_AdvancedGraphics
LOCATION_TITLE, ROUTE_TITLE = "Flat Earth", "Home Run v1"
SRC_CITY_OBJ, SRC_ROUTE_OBJ = 805, 395                   # LA River (was ShowRoom 412: a dev/menu city - the race camera never attached), JPN_MHaruna_1
LIGHT_CITY_OBJ = 2226                                    # USA_Amboy: desert sky (805 LA River day, 1994 Barcelona night)
AUDIO_VISTA_ID = 12                                      # Haruna's (with its ambience: some sound was heard)
# TEST RESULT: with docks proxy copies (audio.bin FFFFFF60-65) + vista 9 there was NO sound at all -> not used now
USE_DOCKS_SOUNDS = False
# sounds: copies of LA docks AudioEntityProxies (audio.bin) under our uids (an actorpack can only add uids above its
# highest one); crowds use the game's own proxies (Haruna's actorpack already has their rows)
AUDIO = {                       # name: (source proxy uid in audio.bin, our uid or None = use the source uid)
    "docks": ("5596DAD9", 0xFFFFFF60),        # Ambience_LA_Docks
    "boathorn": ("E4715A71", 0xFFFFFF61),
    "siren": ("3EE46070", 0xFFFFFF62),
    "loading": ("548B3B5B", 0xFFFFFF63),
    "trucks": ("4A1F4BD6", 0xFFFFFF64),
    "crane": ("1CC2FF02", 0xFFFFFF65),
    "crowd_s": ("5526BBF3", None), "crowd_m": ("2B6AB5B6", None), "crowd_l": ("582CC295", None),
    "haruna": ("3D1D4428", None),             # Haruna's level ambience
}
PICKUP_LANES = 5
INTRO_CAM_UID = 0xF1D60000                              # our 5 AnimatedCameraSequence copies (short stage)
INTRO_STAGE_TIME = 0.0                                   # s: 0 = no fly-by (as the game's own Amboy 3 / Haruna / Russian Hill routes)
INTRO_MODE = "own"      # "none" = NO intro camera sequence (empty links, as the game's dev routes) - BROKE the start + countdown cameras (user 2026-10-20);
                        # "short" = the copies above (StageTime 0: still one camera slide in game, 2026-10-20);
                        # "own" = every map its own fly-by at its start line (intro_cam.py: vista + pack .cycle files)
INTRO_CYCLE_UID = 0xF1D70000                             # "own": map k's CycleResourceDefs (+16k: vista, pack); its
                                                         # AnimatedCameraSequences = INTRO_CAM_UID + 16k (vista, pack)
INTRO_CYCLE_ALIAS_OF = "animations/cameras/pack_lar6.cycle"   # a game .cycle in root.pak (new files start as its alias)


def intro_cycle_name(m, shot):
    """the CycleResourceDef name (root.pak path without .cycle) of map m's "vista" / "pack" start camera"""
    return "animations/cameras/fe_%s_%s" % (m["route_name"].lower(), shot)
CAMERA_ROUTE_OBJ = 3081                                  # USA_LA_River_6: its start camera sequences
EMPTY_LIST = "6992B639"
GLOBAL_LIST = "60AD2B34"                                 # every city lists it: DirectorActor, DynamicCameraActor...
DROP_LISTS = ()                                          # (Haruna's lists 3ED4CF3F / DE7D2096 now serve our road
                                                         # tiles / lamp posts, see gen_actorpack)
# The map sits inside LA River's area (its level graphics, all scenery hidden, give the daylight that lights our
# objects): road middle line z = OZ, from x = OX - ROAD_LEN / 2 to OX + ROAD_LEN / 2, floor at GROUND_Y
OX, OZ = 790.0, 2160.0
GROUND_Y = 1.0
# the road's SHAPE comes from map_builder/tracks/flat_earth_road.json (road_designer.py); default = the straight road
ROAD_DEF = road_mod.load()
ROAD_LEN, ROAD_WIDTH = 3000.0, float(ROAD_DEF.get("width", 60.0))
PIECE_STEP, PIECE_LEN = 48.0, 56.0                       # road pieces every 48 m, 56 m long (overlap hides bend joins)
ROAD = dict(x0=OX - ROAD_LEN / 2, z0=OZ - ROAD_WIDTH / 2, length=ROAD_LEN, y=GROUND_Y, width=ROAD_WIDTH)
PICKUP_STEP = 150.0                                      # one row of 3 power-ups every 150 m (start..finish only)
LIGHT_LEVEL = "usa_la_river"                             # level graphics source (scenery hidden)
WORK = paths.FE_WORK

# visible ground: our own model (model/road_tile.py) shown by 144 DynamicObjActors
TILE_MODEL = "models\\objects\\flat_earth\\roadtile01.model"
TILE_MODEL_ALIAS_OF = "models\\objects\\usa_la_river\\misc\\dynamic_cardboardbox01.model"
GLOW_SOURCE = "\\models\\objects\\usa_la_downtown\\misc\\sign_wetfloor.model"   # model/cpmodel.py edits it
GLOW_DIFF, GLOW_MAT = "sign_wetfloor_diff", "sign_wetfloor_ipr0"
ROAD_GREY, ROAD_GLOW, LINE_GLOW = 105, 95, 130                 # albedo grey, material green (glow) 0-255
ROAD_SHINE = 30                                                # material red (shine; 200 = wet look + bloom)
MODEL_UID, TILE_DEF_UID = 0xFFFFFF30, 0xFFFFFF31
SRC_GAMEMODEL, SRC_DYNDEF = "1F3F42A6", "01ADFCF8"       # DynamicWaterLevel: PhysType None, Alignment None
TILE_SIZE, TILE_TOP_BELOW = 128.0, 0.02                  # tile top 2 cm under the collision floor
TILE_UV = TILE_SIZE / 5.25                               # texture repeats (every 5.25 m)
TILE_DRAW_DISTANCE = 5000.0
ROOT_BASE = paths.ROOT_BASE      # the game's original root.pak (was work/root_mod3.pak = it + the old test track)
ROOT_OUT = paths.ROOT_OUT
FIRST_TRACK = False              # the old test route "My First Track" (LA River 7) + "MY River": not any more
FRONTEND_BPR = "\\ui\\data\\ui_frontend.bpr"
THUMBNAIL_PNG = os.path.join(paths.TRACKS, "flat_earth_1_thumbnail.png")   # menu picture (the user's, 512x256)


TEST_DEF_UID = 0xFFFFFF32                                # DIAGNOSTIC: same def, but a shipped model (container)
TEST_MODEL_FILE = "models\\objects\\USA_LA_LongBeachDocks\\containers\\ContainerDarkRed.model"


# start / finish line: a checkered strip across both tile rows + red containers at both ends
LINE_MODEL = "models\\objects\\flat_earth\\startline01.model"
LINE_MODEL_UID, LINE_DEF_UID = 0xFFFFFF33, 0xFFFFFF34
LINE_W, LINE_LEN = 3.0, ROAD_WIDTH + 8.0
LINE_TOP_BELOW = -0.03                                   # (negative = above the floor)
TILE_COLUMNS = 25                                        # 1 row x 25 tiles (OX +-1600, OZ +-64)
LAMP_SIDE_Z, LAMP_STEP = ROAD_WIDTH / 2 + 10, 50.0
# roadside objects (Haruna's spare lamp-post actors show these; amax ElementObjectRecord clones, own uids)
ROADSIDE = {                    # name: (source ElementObjectRecord uid, our uid)
    "barrier": ("52428956", 0xFFFFFF40),      # SPA_Barcelona_Gracia Construction\ConcreteBarrier
    "billboard": ("EF4052EE", 0xFFFFFF41),    # uk_london_hackney misc\standingbillboard
    "flashing": ("5C3279E4", 0xFFFFFF42),     # Shared gameplay\FlashingSign
    "light": ("D40A6188", 0xFFFFFF43),        # shared Construction\StandingLight
    "barrel": ("EBA0FFD7", 0xFFFFFF44),       # jpn_shutoko RoadWorks\BarrelA        (obstacles on the road)
    "rwbarrier": ("E7A4B0AD", 0xFFFFFF45),    # jpn_shutoko RoadWorks\RWBarrierA
    "cone": ("A099EA98", 0xFFFFFF46),         # jpn_shutoko RoadWorks\ConeC
    "lamp": ("9D39678A", 0xFFFFFF47),         # Haruna's lamp post (own copy: longer draw distance)
}
ROADSIDE_DRAW_DISTANCE = 1500.0                 # PhysicalObject.DrawDistance in our copies (game: 200-400 m)
BARRIER_STEP, BILLBOARD_STEP = 45.0, 300.0
EDGE_BARRIERS = False                           # concrete barriers along both road edges (user 2026-10-15: remove)
OBSTACLE_GROUPS = 12                            # groups of barrels / barriers / cones on the road
# big scenery to hide the horizon (DynamicObjActors, no collision): name, model file (when the game has no GameModel
# for it: we make one with our_model_uid), its GameModel uid in amax, our DynamicObjDef uid, lift (m)
BIG = [
    ("ferris", "models\\specialfx\\ferriswheel.model", None, 0xFFFFFF50, 0xFFFFFF51, 0.0),
    ("tanker", None, "411EF6CC", None, 0xFFFFFF52, 0.0),        # 333 m ship (LA docks)
    ("cruise", None, "2A9D443A", None, 0xFFFFFF53, 7.8),        # 263 m cruise ship (hull 7.8 m below its base)
    ("crane", None, "FD1DE76F", None, 0xFFFFFF54, 0.0),         # 132 m x 71 m gantry crane
    ("radio", None, "FF1D13E4", None, 0xFFFFFF55, 0.0),         # 62 m radio mast
]
# TALL BUILDINGS (Surface / MAP panel "Tall buildings"): our own skyscraper models (a box + a generated glass and
# window facade, some windows lit), placed far from the road: (name, width x, depth z, height, seed)
TOWERS = [("tower_a", 40.0, 40.0, 140.0, 1), ("tower_b", 55.0, 35.0, 95.0, 2), ("tower_c", 30.0, 30.0, 200.0, 3),
          ("tower_d", 70.0, 45.0, 60.0, 4),
          # more designs (user 2026-10-20: "more buildings and skyscrapers")
          ("tower_e", 50.0, 50.0, 180.0, 5), ("tower_f", 60.0, 30.0, 150.0, 6), ("tower_g", 36.0, 36.0, 230.0, 7),
          ("tower_h", 90.0, 24.0, 75.0, 8), ("tower_i", 70.0, 70.0, 110.0, 9), ("tower_j", 64.0, 64.0, 90.0, 10),
          ("tower_k", 22.0, 22.0, 170.0, 11), ("tower_l", 110.0, 60.0, 40.0, 12)]
# per design: shape (tower_parts) + facade: frame colour, window colour, lit share, window grid (cols, rows per 16 m),
# ribbon = horizontal window bands, lit_colour = the glowing windows
TOWER_STYLE = {
    "tower_a": dict(shape="box", frame=(24, 30, 40), win=(52, 70, 92), lit=0.35),
    "tower_b": dict(shape="box", frame=(24, 30, 40), win=(52, 70, 92), lit=0.35),
    "tower_c": dict(shape="box", frame=(24, 30, 40), win=(52, 70, 92), lit=0.35),
    "tower_d": dict(shape="box", frame=(24, 30, 40), win=(52, 70, 92), lit=0.35),
    "tower_e": dict(shape="setback", frame=(28, 44, 66), win=(70, 110, 160), lit=0.30),           # blue glass
    "tower_f": dict(shape="twin", frame=(175, 175, 168), win=(40, 50, 62), lit=0.25, ribbon=True),  # white offices
    "tower_g": dict(shape="spire", frame=(10, 12, 16), win=(20, 26, 34), lit=0.30, lit_col=(80, 230, 255),
                    cols=8, rows=2),                                                               # dark glass, cyan
    "tower_h": dict(shape="slab", frame=(120, 62, 46), win=(54, 48, 44), lit=0.45, cols=6, rows=5),  # brick flats
    "tower_i": dict(shape="stepped", frame=(30, 60, 44), win=(70, 130, 96), lit=0.25),              # green glass
    "tower_j": dict(shape="lshape", frame=(150, 120, 60), win=(110, 92, 52), lit=0.30, ribbon=True),  # gold glass
    "tower_k": dict(shape="box", frame=(200, 200, 205), win=(60, 80, 110), lit=0.30, cols=2, rows=6),  # slim needle
    "tower_l": dict(shape="box", frame=(90, 92, 96), win=(36, 40, 46), lit=0.20, ribbon=True),       # low wide block
}
TOWER_UID = 0xFFFFFF76                          # model uid TOWER_UID + 2k, DynamicObjDef + 2k + 1 (76..7D: free between the
#                                               road-piece widths 64..75 and the extra maps 80..) - the first 4 designs
TOWER_UID2 = 0xF1E10000                         # designs 5..: any uid works now (kslf_write.clone_object_any)


def tower_uids(k):
    """(GameModel uid, DynamicObjDef uid) of tower design k"""
    if k < 4:
        return TOWER_UID + 2 * k, TOWER_UID + 2 * k + 1
    return TOWER_UID2 + 2 * (k - 4), TOWER_UID2 + 2 * (k - 4) + 1


def tower_parts(name):
    """the boxes (x0, x1, z0, z1, y0, y1) a tower design is made of (model space: centred, ground at y 0)"""
    _, wx, dz, h, _ = next(t for t in TOWERS if t[0] == name)
    X, Z = wx / 2, dz / 2
    shape = TOWER_STYLE.get(name, {}).get("shape", "box")
    if shape == "setback":                       # wide base, narrower middle, slim top
        return [(-X, X, -Z, Z, 0, h * .55), (-X * .75, X * .75, -Z * .75, Z * .75, h * .55, h * .82),
                (-X * .5, X * .5, -Z * .5, Z * .5, h * .82, h)]
    if shape == "twin":                          # a low podium + two towers with a gap
        g = X * .12
        return [(-X, X, -Z, Z, 0, h * .18), (-X, -g, -Z * .85, Z * .85, h * .18, h),
                (g, X, -Z * .85, Z * .85, h * .18, h * .88)]
    if shape == "spire":                         # a tower, a crown and a thin spire
        return [(-X, X, -Z, Z, 0, h * .78), (-X * .6, X * .6, -Z * .6, Z * .6, h * .78, h * .86),
                (-1.5, 1.5, -1.5, 1.5, h * .86, h)]
    if shape == "slab":                          # one long thin block
        return [(-X, X, -Z, Z, 0, h)]
    if shape == "stepped":                       # four tiers
        return [(-X * f, X * f, -Z * f, Z * f, h * a, h * b)
                for f, a, b in ((1.0, 0, .35), (.8, .35, .6), (.6, .6, .82), (.4, .82, 1.0))]
    if shape == "lshape":                        # two wings
        return [(-X, X, -Z, -Z * .1, 0, h), (-X, -X * .1, -Z * .1, Z, 0, h * .7)]
    return [(-X, X, -Z, Z, 0, h)]


BIG += [(name, "models\\objects\\flat_earth\\%s.model" % name, None, tower_uids(k)[0], tower_uids(k)[1], 0.0)
        for k, (name, _, _, _, _) in enumerate(TOWERS)]
road_layout.OBJECT_BOX.update({name: (-wx / 2, wx / 2, 0, h, -dz / 2, dz / 2) for name, wx, dz, h, _ in TOWERS})

# START / FINISH ARCH (the game has no such model to reuse - its start "text" is spawned by code): our own gantry
# over the line - two pillars outside the walls, a beam with a glowing sign. Model x = across the road (pillar
# centres at +-ARCH_W / 2 before scaling; road_layout scales x to the road's width), y up, z = along the road (the
# sign faces the cars on its +z side). (name, sign text, seed)
ARCHES = [("arch_start", "START", 21), ("arch_finish", "FINISH", 22), ("arch_lap", "START  FINISH", 23)]
ARCH_W, ARCH_PILLAR, ARCH_Y0, ARCH_Y1, ARCH_DEPTH = 40.0, 2.0, 9.0, 12.5, 2.0
ARCH_UID = 0xF1E20000                            # model ARCH_UID + 2k, DynamicObjDef + 2k + 1


def arch_parts():
    """the boxes (x0, x1, z0, z1, y0, y1) of the arch (model space, before the width scaling)"""
    X, P, D = ARCH_W / 2, ARCH_PILLAR / 2, ARCH_DEPTH / 2
    return [(-X - P, -X + P, -P, P, 0.0, ARCH_Y1), (X - P, X + P, -P, P, 0.0, ARCH_Y1),
            (-X - P, X + P, -D, D, ARCH_Y0, ARCH_Y1)]


BIG += [(name, "models\\objects\\flat_earth\\%s.model" % name, None, ARCH_UID + 2 * k, ARCH_UID + 2 * k + 1, 0.0)
        for k, (name, _, _) in enumerate(ARCHES)]
road_layout.OBJECT_BOX.update({name: (-ARCH_W / 2 - ARCH_PILLAR / 2, ARCH_W / 2 + ARCH_PILLAR / 2, 0, ARCH_Y1,
                                      -ARCH_DEPTH / 2, ARCH_DEPTH / 2) for name, _, _ in ARCHES})
road_layout.FIXED_KINDS.update(name for name, _, _ in ARCHES)        # it belongs to the line: moves with it
TOWER_Z = 300.0                                 # from the road middle (behind the ships / cranes)
TOWER_STEP = 240.0                              # one every ... m along each side
SHIP_Z, CRANE_Z = 110.0, 175.0                  # distance of the ship wall / crane row from the road middle
CRANES_PER_SIDE = 8
# obstacles that stop a car: a shipping container with FIXED physics in each obstacle group
BLOCK_DEF_UID = 0xFFFFFF56                       # DynamicObjDef: container model, PhysType Fixed
# glowing walls along both road edges (our model: a vertical quad, one per side, the whole road long)
WALL_MODEL = "models\\objects\\flat_earth\\edgewall01.model"
WALL_MODEL_UID, WALL_DEF_UID = 0xFFFFFF57, 0xFFFFFF58
WALL_H = 1.2
# glowing chevron panels on the outside wall of bends (own model: a dark vertical panel with white arrows, like the
# game's corner arrows), placed by road_layout.chevron_panels in the tile list
CHEVRON_MODEL = "models\\objects\\flat_earth\\chevron01.model"
CHEVRON_MODEL_UID, CHEVRON_DEF_UID = 0xFFFFFF59, 0xFFFFFF5A
CHEVRON_LEN, CHEVRON_H, CHEVRON_COUNT = 16.0, 2.5, 4          # metres long / high, arrows per panel
WALL_COLOUR, WALL_GLOW = (0, 220, 255), 255


def tile_spots(start_x, finish_x):
    """2 rows x 49 road tiles (OX +-1568, OZ +-64), the checkered start / finish strips and a red container at
    each end of them. (A spot's 4th value = the DynamicObjDef uid; default TILE_DEF_UID.)"""
    x0 = OX - TILE_COLUMNS * TILE_SIZE / 2
    spots = [(x0 + TILE_SIZE / 2 + TILE_SIZE * i, GROUND_Y - TILE_TOP_BELOW, OZ) for i in range(TILE_COLUMNS)]
    for x in (start_x, finish_x):
        spots.append((x, GROUND_Y - LINE_TOP_BELOW, OZ, LINE_DEF_UID))
        spots += [(x, GROUND_Y, OZ + side * (LINE_LEN / 2 + 4), TEST_DEF_UID) for side in (-1, 1)]
    spots += big_spots(start_x, finish_x)
    assert len(spots) <= 144, len(spots)
    return spots


def lamp_spots(start_x, finish_x):
    """Haruna's 356 lamp-post actors: lamp posts every LAMP_STEP m on both sides (turned to face the road), concrete
    barriers along both road edges, standing billboards further out, flashing signs + standing lights at the start
    and finish lines (spot = x, y, z, turn[, ElementObjectRecord uid])"""
    half = ROAD_LEN / 2 - 10
    spots = []
    x = OX - half
    lamp = ROADSIDE["lamp"][1]
    while x <= OX + half:
        spots.append((x, GROUND_Y, OZ + LAMP_SIDE_Z, math.pi, lamp))
        spots.append((x, GROUND_Y, OZ - LAMP_SIDE_Z, 0.0, lamp))
        x += LAMP_STEP
    edge = ROAD_WIDTH / 2 + 6                  # outside the solid edge wall (z 31..33)
    x = OX - half + BARRIER_STEP / 2
    while x <= OX + half:
        for side in (-1, 1):
            spots.append((x, GROUND_Y, OZ + side * edge, 0.0, ROADSIDE["barrier"][1]))
        x += BARRIER_STEP
    x = OX - half + BILLBOARD_STEP / 2
    while x <= OX + half:
        spots.append((x, GROUND_Y, OZ + edge + 14, math.pi, ROADSIDE["billboard"][1]))
        spots.append((x + BILLBOARD_STEP / 2, GROUND_Y, OZ - edge - 14, 0.0, ROADSIDE["billboard"][1]))
        x += BILLBOARD_STEP
    for lx in (start_x, finish_x):
        for side in (-1, 1):
            spots.append((lx - 6, GROUND_Y, OZ + side * (edge + 4), math.pi if side > 0 else 0.0,
                          ROADSIDE["flashing"][1]))
            spots.append((lx + 6, GROUND_Y, OZ + side * (edge + 4), math.pi if side > 0 else 0.0,
                          ROADSIDE["light"][1]))
    # obstacles: groups between the power-up rows, alternating left / middle / right of the road
    for gx, lane in obstacle_groups(start_x, finish_x):
        spots.append((gx, GROUND_Y, OZ + lane, math.pi / 2, ROADSIDE["rwbarrier"][1]))
        for k, (dx, dz) in enumerate(((-3, -3), (-3, 3), (3, -5), (3, 5))):
            spots.append((gx + dx, GROUND_Y, OZ + lane + dz, 0.0,
                          ROADSIDE["barrel"][1] if k < 2 else ROADSIDE["cone"][1]))
    assert len(spots) <= 356, len(spots)
    return spots


def big_spots(start_x, finish_x):
    """the horizon wall: ships end to end on both sides (turned to run along the road), gantry cranes behind them,
    a ferris wheel at the start and a radio mast at the finish (tile-list spots: x, y, z, def uid, turn)"""
    defs = {b[0]: b for b in BIG}
    spots = []
    for side in (-1, 1):
        x = OX - ROAD_LEN / 2 - 150
        k = 0 if side < 0 else 1
        while x < OX + ROAD_LEN / 2 + 150:
            name = ("tanker", "cruise")[k % 2]
            length = 333.0 if name == "tanker" else 263.0
            spots.append((x + length / 2, GROUND_Y + defs[name][5], OZ + side * SHIP_Z, defs[name][4], math.pi / 2))
            x += length + 10
            k += 1
        for i in range(CRANES_PER_SIDE):
            cx = OX - ROAD_LEN / 2 + 400 + i * (ROAD_LEN - 800) / (CRANES_PER_SIDE - 1) + (0 if side < 0 else 250)
            spots.append((cx, GROUND_Y, OZ + side * CRANE_Z, defs["crane"][4], 0.0))
    spots.append((start_x - 60, GROUND_Y, OZ + 70, defs["ferris"][4], 0.0))
    spots.append((finish_x + 60, GROUND_Y, OZ - 70, defs["radio"][4], 0.0))
    # the glowing edge walls (model centred on x: one per side, as long as the road)
    for side in (-1, 1):
        spots.append((OX, GROUND_Y, OZ + side * (ROAD_WIDTH / 2 + 1), WALL_DEF_UID, 0.0))
    # road-block containers (their solid boxes are in the collision floor: see block_boxes)
    for cx, cz in block_centres(start_x, finish_x):
        # turned 90 deg the container (model: x -13.7..0, z 0..2.4 from its corner) covers x +0..2.4, z +0..13.7
        spots.append((cx - CONTAINER[2] / 2, GROUND_Y, cz - CONTAINER[0] / 2, BLOCK_DEF_UID, math.pi / 2))
    return spots


CONTAINER = (13.7, 2.9, 2.4)                    # length, height, width of ContainerDarkRed.model


def block_centres(start_x, finish_x):
    """2 containers per obstacle group: one in the group's lane 12 m after its barrels, one in the next lane 40 m on"""
    out = []
    for g, (gx, lane) in enumerate(obstacle_groups(start_x, finish_x)):
        other = (-1, 0, 1)[(g + 1) % 3] * ROAD_WIDTH / 4
        out += [(gx + 12, OZ + lane), (gx + 40, OZ + other)]
    return out


def block_boxes(start_x, finish_x):
    """solid boxes for the collision floor: the road-block containers + the two edge walls"""
    boxes = []
    for cx, cz in block_centres(start_x, finish_x):
        boxes.append((cx - CONTAINER[2] / 2, cx + CONTAINER[2] / 2, GROUND_Y, GROUND_Y + CONTAINER[1],
                      cz - CONTAINER[0] / 2, cz + CONTAINER[0] / 2))
    for side in (-1, 1):
        z = OZ + side * (ROAD_WIDTH / 2 + 1)
        out = side * 2.0                         # thick (2 m, outwards from the glowing wall), 3 m high
        boxes.append((OX - ROAD_LEN / 2, OX + ROAD_LEN / 2, GROUND_Y, GROUND_Y + 3.0, min(z, z + out), max(z, z + out)))
    return boxes


def obstacle_groups(start_x, finish_x):
    """(x, lane offset) of each obstacle group: between the power-up rows, alternating left / middle / right"""
    span = finish_x - start_x
    return [(start_x + span * (g + 1) / (OBSTACLE_GROUPS + 1) + PICKUP_STEP / 2, (-1, 0, 1)[g % 3] * ROAD_WIDTH / 4)
            for g in range(OBSTACLE_GROUPS)]


def audio_spots(start_x, finish_x):
    """15 AudioActors: docks ambience, ship horns, siren, loading, trucks and cranes along the ship wall; crowds
    right behind the barriers at the start and finish lines (spot = x, y, z, AudioEntityProxy uid)"""
    def uid(name):
        src, ours = AUDIO[name]
        return ours or int(src, 16)
    y = GROUND_Y + 2
    mid = (start_x + finish_x) / 2
    side = ROAD_WIDTH / 2 + 3
    if USE_DOCKS_SOUNDS:
        spots = [(mid, y, OZ, uid("docks")),
                 (start_x + 400, y, OZ + SHIP_Z - 25, uid("boathorn")), (finish_x - 500, y, OZ - SHIP_Z + 25, uid("boathorn")),
                 (mid + 200, y, OZ + SHIP_Z - 25, uid("siren")),
                 (start_x + 700, y, OZ - CRANE_Z + 40, uid("loading")), (finish_x - 800, y, OZ + CRANE_Z - 40, uid("loading")),
                 (mid - 300, y, OZ - SHIP_Z + 25, uid("trucks")),
                 (mid - 600, y, OZ + CRANE_Z - 40, uid("crane")), (mid + 700, y, OZ - CRANE_Z + 40, uid("crane"))]
    else:
        spots = [(mid, y, OZ, uid("haruna"))]
        for x in (start_x + (finish_x - start_x) / 3, start_x + 2 * (finish_x - start_x) / 3):
            spots += [(x, y, OZ + side, uid("crowd_m")), (x, y, OZ - side, uid("crowd_m"))]
    # crowds on both sides of each line; the start ones 90 m AFTER the line: a sound seems to start when a car
    # ENTERS its range (crowds at the finish were heard, the ones around the grid never)
    for lx in (start_x + 90, finish_x):
        spots += [(lx, y, OZ + side, uid("crowd_l")), (lx, y, OZ - side, uid("crowd_l")),
                  (lx - 25, y, OZ - side, uid("crowd_m")), (lx + 25, y, OZ + side, uid("crowd_s")),
                  (lx - 25, y, OZ + side, uid("crowd_s"))]
    return spots[:15]


def audio_spots_old(start_x, finish_x):
    """Haruna's 15 AudioActors (crowds, flares): crowds at the start and finish lines, the rest along the road"""
    side = ROAD_WIDTH / 2 + 4                 # just behind the barriers
    spots = []
    for lx in (start_x, finish_x):            # 3 per side at each line
        for dx in (-30, 0, 30):
            spots += [(lx + dx, GROUND_Y + 1, OZ + side), (lx + dx, GROUND_Y + 1, OZ - side)]
    n = 3
    for k in range(n):
        x = start_x + (finish_x - start_x) * (k + 1) / (n + 1)
        spots.append((x, GROUND_Y, OZ + (side if k % 2 else -side)))
    return spots


def _tmp(data):
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".bin")
    f.write(data)
    f.close()
    return f.name


def _edit(data, obj_index, fn):
    p_in, p_out = _tmp(data), _tmp(b"")
    try:
        return kslf_write.edit_object(p_in, p_out, obj_index, fn)
    finally:
        os.unlink(p_in)
        os.unlink(p_out)


def _swap_lists(mapping):
    """edit fn: replace LevelActorList references (by uid) inside the object's list arrays"""
    def fn(ctx):
        for bi, (t, cnt, kind) in enumerate(ctx.rows):
            if ctx.types[t]["name"] == "LevelActorList" and kind == 2:
                for e in range(cnt):
                    at = ctx.blocks[bi] + 4 * e
                    v = struct.unpack_from("<I", ctx.data, at)[0]
                    if v in mapping:
                        struct.pack_into("<I", ctx.data, at, mapping[v])
    return fn


# road-piece models: one per width bucket (road width + 8 m across), GameModel + DynamicObjDef each
TILE_WIDTHS = (30, 40, 50, 60, 70, 80, 90, 100)
TILE_VARIANT_BASE = 0xFFFFFF66                            # model uid = base + 2k, def uid = base + 2k + 1
TILE_DEFS = {b: TILE_VARIANT_BASE + 2 * k + 1 for k, b in enumerate(TILE_WIDTHS)}


def tile_variant_file(b):
    return "models\\objects\\flat_earth\\roadtile_w%d.model" % b


def _world():
    import world
    return world.assign_ids(world.load())


def _roads_of(loc):
    return [road_mod.for_map(m) for m in loc["maps"]]


def _bounds_of(loc, margin):
    xs0, xs1, zs0, zs1 = [], [], [], []
    for m in loc["maps"]:
        b = road_mod.for_map(m).bounds(margin)
        xs0.append(b[0])
        xs1.append(b[1])
        zs0.append(b[2])
        zs1.append(b[3])
    return min(xs0), max(xs1), min(zs0), max(zs1)


def build_amax(amax, wld=None, list_maps=None):
    """every location (City) and map (Route) of the world + the shared models"""
    wld = wld or _world()
    # LIGHTING tab: our own LightingSettings copies first (they may move uid rows), then read everything else
    amax, loc_env = lighting.prepare(amax, wld, dump, kslf_write, _tmp)
    A = dump.File(_tmp(amax))
    ref = {"%08X" % u: i for i, (u, t, oi) in enumerate(A.refs)}
    la = A.O[LIGHT_CITY_OBJ]["blocks"][0]["offset"]
    light = amax[la + 52:la + 132]          # Environment + Flashback settings .. environmentFarClip (refs = same file)
    la6 = A.O[CAMERA_ROUTE_OBJ]["blocks"][0]["offset"]
    assert A.top(A.O[CAMERA_ROUTE_OBJ])["Name"] == "USA_LA_River_6"
    cam_refs = amax[la6 + 88:la6 + 108]
    src_lists = {ref[x["uid"]] for k in ("ActorLists", "MultiplayerActorLists") for x in A.top(A.O[SRC_CITY_OBJ])[k]
                 if x["uid"] != GLOBAL_LIST}

    feel_uids = wld.get("_feel_uids") or {}            # our own grip tables (surface.feel_table) per grip level

    # INTRO CAMERAS: LA River 6's fly-bys are animations in LA River's world (on our maps they flew around somewhere
    # random). Our routes get copies of its 5 AnimatedCameraSequences with StageTime INTRO_STAGE_TIME: the race
    # starts at once on the normal camera behind the car, at the start line.
    cam_srcs = [A.top(A.O[CAMERA_ROUTE_OBJ])[k]["object"] for k in ("VistaCameraSequence", "PackCameraSequence",
                "PackCameraSequenceMP", "PackCameraSequenceSingle", "PackCameraSequenceBoss")]
    cam_uids = []                                      # "short": the 5 shared copies
    map_cams = {}                                      # "own": route uid -> the 5 sequence uids of that map
    if INTRO_MODE == "own":
        # every map: 2 CycleResourceDefs (its own .cycle files, made by build_root / intro_cam.py) + 2
        # AnimatedCameraSequences (vista: LA River 6's Vista one, pack: its PackMP one) -> the route's 5 links
        import intro_cam
        import schema
        types = schema.load_schema(kslf.Kslf(amax))
        tn = {t["name"]: i for i, t in enumerate(types)}
        fn_off = _field_offset(types, tn["CycleResourceDef"], "FilenameMinusExt")
        anim_off = _field_offset(types, tn["AnimatedCameraSequence"], "Animation")
        stage_off = _field_offset(types, tn["AnimatedCameraSequence"], "StageTime")
        cyc_src = A.top(A.O[cam_srcs[2]])["Animation"]["object"]          # Pack_LAR6's CycleResourceDef
        all_maps = [m for loc in wld["locations"] for m in loc["maps"]]
        for k, m in enumerate(all_maps):
            seqs = []
            for j, (shot, seq_src, stage) in enumerate((("vista", cam_srcs[0], intro_cam.VISTA_STAGE),
                                                        ("pack", cam_srcs[2], intro_cam.PACK_STAGE))):
                cyc_uid, seq_uid = INTRO_CYCLE_UID + 16 * k + j, INTRO_CAM_UID + 16 * k + j

                def cyc_patch(data, pool, blocks, name=intro_cycle_name(m, shot)):
                    struct.pack_into("<I", data, blocks[0] + fn_off, len(pool))
                    pool += name.encode() + b"\0"
                _, amax = kslf_write.clone_object_any(cyc_src, None, cyc_uid, cyc_patch, data_in=amax)
                _, amax = kslf_write.clone_object_any(seq_src, None, seq_uid, None, data_in=amax)
                seqs.append((seq_uid, cyc_uid, stage))
            map_cams[m["route_uid"]] = [seqs[0][0]] + [seqs[1][0]] * 4
            for seq_uid, cyc_uid, stage in seqs:             # (rows only now: the inserts above moved them)
                seq_obj = next(oi for u, t, oi in dump.File(_tmp(amax)).refs if u == seq_uid)

                def seq_patch(ctx, cyc_uid=cyc_uid, stage=stage):
                    k4 = kslf.Kslf(bytes(amax))
                    o4, n4 = k4.sections[4]
                    row = next(i for i in range(n4) if struct.unpack_from("<I", amax, o4 + 8 * i)[0] == cyc_uid)
                    struct.pack_into("<I", ctx.data, ctx.blocks[0] + anim_off, row)       # Animation
                    struct.pack_into("<f", ctx.data, ctx.blocks[0] + stage_off, stage)    # StageTime
                amax = _edit(amax, seq_obj, seq_patch)
    else:
        for k, src in enumerate(cam_srcs):
            uid = INTRO_CAM_UID + k

            def cam_patch(data, pool, blocks):
                struct.pack_into("<f", data, blocks[0] + 12, INTRO_STAGE_TIME)        # StageTime
            _, amax = kslf_write.clone_object_any(src, None, uid, cam_patch, data_in=amax)
            cam_uids.append(uid)
    A = dump.File(_tmp(amax))
    ref = {"%08X" % u: i for i, (u, t, oi) in enumerate(A.refs)}

    def cam_refs_now(amax, m=None):
        """the 5 camera refs (section-4 rows of our sequences: the shared copies, or map m's own) as the route
        field bytes"""
        k4 = kslf.Kslf(amax)
        o4, n4 = k4.sections[4]
        rows = {struct.unpack_from("<I", amax, o4 + 8 * i)[0]: i for i in range(n4)}
        uids = map_cams[m["route_uid"]] if (m is not None and m["route_uid"] in map_cams) else cam_uids
        return struct.pack("<5I", *[rows[u] for u in uids])

    def row_of(amax, uid):
        k = kslf.Kslf(amax)
        o4, n4 = k.sections[4]
        return next(i for i in range(n4) if struct.unpack_from("<I", amax, o4 + 8 * i)[0] == uid)

    def add_city(amax, loc):
        feel_uid = feel_uids.get(surface.loc_feel(wld, loc))      # this location's grip table, or None (game's)
        surf_row = row_of(amax, feel_uid) if feel_uid else None
        if surf_row is not None:
            import schema
            types = schema.load_schema(kslf.Kslf(amax))
            city_t = next(i for i, t in enumerate(types) if t["name"] == "City")
            surf_off = _field_offset(types, city_t, "surfaceMappings")

        def city_patch(data, pool, blocks):
            b = blocks[0]
            if surf_row is not None:                                      # surfaceMappings = OUR grip table
                struct.pack_into("<I", data, b + surf_off, surf_row)
            struct.pack_into("<i", data, b + 16, 15)                      # Index
            struct.pack_into("<I", data, b + 20, loc["loc_id"])           # LocString
            data[b + 24] = 0                                              # DevOnly = no
            struct.pack_into("<I", data, b + 28, len(pool))               # IconName (an existing menu icon)
            pool += b"la_river\0"
            struct.pack_into("<I", data, b + 32, len(pool))               # ActorListFolder
            pool += loc["city_name"].encode() + b"\0"
            # boundsMin / boundsMax (x, z): 16:9 = the area of the in-race minimap (minimap.py)
            x0, x1, z0, z1 = minimap.city_bounds(_roads_of(loc))
            struct.pack_into("<4f", data, b + 140, x0, z0, x1, z1)
            data[b + 52:b + 132] = loc_env.get(wld["locations"].index(loc), light)
        n = len(dump.File(_tmp(amax)).O)
        _, amax = kslf_write.clone_object(None, None, SRC_CITY_OBJ, loc["city_name"], loc["city_uid"], city_patch,
                                          data_in=amax)
        # the source city's own lists -> empty, EXCEPT the global one (race director, cameras, start scripts)
        return _edit(amax, n, _swap_lists({r: ref[EMPTY_LIST] for r in src_lists}))

    def add_route(amax, loc, m):
        cams = cam_refs_now(amax, m)
        A2 = dump.File(_tmp(amax))
        city_ref = next(i for i, (u, t, oi) in enumerate(A2.refs) if u == loc["city_uid"])

        # the game loads levels\<city>\<city>_<RouteNumber>.aid (the race path: AI line, checkpoints, resets) -
        # every map needs its own number (all at 1 = map 2 raced on map 1's path: cars reset all the time)
        number = int(m["route_name"].rsplit("_", 1)[1])

        def route_patch(data, pool, blocks):
            b = blocks[0]
            struct.pack_into("<i", data, b + 16, number)                  # Index (menu order)
            struct.pack_into("<I", data, b + 20, m["loc_id"])             # LocString
            struct.pack_into("<I", data, b + 24, city_ref)                # City
            struct.pack_into("<i", data, b + 28, number)                  # RouteNumber -> <city>_<number>.aid
            struct.pack_into("<I", data, b + 36, AUDIO_VISTA_ID)          # AudioVistaID
            struct.pack_into("<I", data, b + 32, len(pool))               # Thumbnail (looked up by name)
            # intro cameras: "none" = empty links (0xFFFFFFFF), as the game's own _0 / OBJTest routes - the short
            # copies (StageTime 0) still showed one camera slide at a random spot (user, 2026-10-20)
            data[b + 88:b + 108] = b"\xff" * 20 if INTRO_MODE == "none" else cams
            if m.get("laps"):                                             # a lap track: as LA River 6 (circuit)
                struct.pack_into("<I", data, b + 76, 0)                   # OnlyAllowSingleLap = no
                struct.pack_into("<i", data, b + 84, 10)                  # CircuitType 10 (the menu's "circuit")
            pool += m["route_name"].encode() + b"\0"
        n = len(A2.O)
        _, amax = kslf_write.clone_object(None, None, SRC_ROUTE_OBJ, m["route_name"], m["route_uid"], route_patch,
                                          data_in=amax)
        return _edit(amax, n, _swap_lists({ref[u]: ref[EMPTY_LIST] for u in DROP_LISTS}))

    locs = wld["locations"]
    # 1. location 0 + its first map (the original Flat Earth ids FFFFFF20 / 21)
    amax = add_city(amax, locs[0])
    amax = add_route(amax, locs[0], locs[0]["maps"][0])
    # 2. shared models (as before)
    amax = _shared_models(amax)
    # 3. road-piece width models
    A = dump.File(_tmp(amax))
    obj = {"%08X" % u: oi for u, t, oi in A.refs if oi != 0xFFFF}
    for k, b in enumerate(TILE_WIDTHS):
        def gm_patch(data, pool, blocks, b=b):
            struct.pack_into("<I", data, blocks[0], len(pool))
            pool += tile_variant_file(b).encode() + b"\0"
        mu = TILE_VARIANT_BASE + 2 * k
        _, amax = kslf_write.clone_object(None, None, obj[SRC_GAMEMODEL], None, mu, gm_patch, data_in=amax)
        A = dump.File(_tmp(amax))
        gm_ref = next(i for i, (u, t, oi) in enumerate(A.refs) if u == mu)

        def def_patch(data, pool, blocks, gm_ref=gm_ref):
            b0 = blocks[0]
            struct.pack_into("<I", data, b0 + 0, gm_ref)                  # Model
            struct.pack_into("<f", data, b0 + 12, TILE_DRAW_DISTANCE)     # DrawDistance
            struct.pack_into("<I", data, b0 + 24, 1)                     # DisplayInMirror (the rear-view mirror shows it too)
            struct.pack_into("<I", data, b0 + 32, 0)                      # PhysType None
            struct.pack_into("<I", data, b0 + 68, 0)                      # Alignment None
        _, amax = kslf_write.clone_object(None, None, obj[SRC_DYNDEF], None, mu + 1, def_patch, data_in=amax)
    # 3b. the seamless road: every map's road chunk models (uids anywhere: road_mesh.uids)
    if ROAD_MESH:
        amax = _road_chunk_models(amax, wld)
    # 3c. the user's scenery objects from the library: a DynamicObjDef (no physics) for each model used
    amax = _scenery_defs(amax, wld)
    # 3d. the player's imported models (.glb): a GameModel + DynamicObjDef per model piece (model_import.uids)
    amax = _import_defs(amax, wld)
    # 4. every other location / map (ids from world.EXTRA_BASE, rising in file order)
    for li, loc in enumerate(locs):
        if li:
            amax = add_city(amax, loc)
        for mi, m in enumerate(loc["maps"]):
            if li or mi:
                amax = add_route(amax, loc, m)
    # 5. each extra map's own actor lists (ids from world.LIST_BASE, rising): swap them into its route
    for li, loc in enumerate(locs):
        for mi, m in enumerate(loc["maps"]):
            mapping = (list_maps or {}).get((li, mi))
            if not mapping:
                continue
            A = dump.File(_tmp(amax))
            route_obj = next(oi for u, t, oi in A.refs if u == m["route_uid"])
            refs_now = {"%08X" % u: i for i, (u, t, oi) in enumerate(A.refs)}

            def swap(ctx, mapping=mapping, refs_now=refs_now):
                new = {}
                for old, uid in sorted(mapping.items(), key=lambda kv: kv[1]):
                    new[refs_now[old]] = ctx.add_uid(uid, "LevelActorList")
                for bi, (t, cnt, kind) in enumerate(ctx.rows):
                    if ctx.types[t]["name"] == "LevelActorList" and kind == 2:
                        for e in range(cnt):
                            at = ctx.blocks[bi] + 4 * e
                            v = struct.unpack_from("<I", ctx.data, at)[0]
                            if v in new:
                                struct.pack_into("<I", ctx.data, at, new[v])
            amax = _edit(amax, route_obj, swap)
    amax = _tower_defs(amax)                      # LAST: their uids are above every other one made here
    return amax


def _road_chunk_models(amax, wld):
    """GameModel + DynamicObjDef (no physics, far draw distance) for every road chunk of every map"""
    fe = sys.modules[__name__]
    A = dump.File(_tmp(amax))
    obj = {"%08X" % u: oi for u, t, oi in A.refs if oi != 0xFFFF}
    for loc in wld["locations"]:
        for m in loc["maps"]:
            for k in range(road_mesh.model_count(fe, m, map_road(m))):
                mu, du = road_mesh.uids(m, k)

                def gm_patch(data, pool, blocks, name=road_mesh.model_file(m, k)):
                    struct.pack_into("<I", data, blocks[0], len(pool))
                    pool += name.encode() + b"\0"
                _, amax = kslf_write.clone_object_any(obj[SRC_GAMEMODEL], None, mu, gm_patch, data_in=amax)
                k4 = kslf.Kslf(amax)
                o4, n4 = k4.sections[4]
                gm_ref = next(i for i in range(n4) if struct.unpack_from("<I", amax, o4 + 8 * i)[0] == mu)

                def def_patch(data, pool, blocks, gm_ref=gm_ref):
                    b0 = blocks[0]
                    struct.pack_into("<I", data, b0 + 0, gm_ref)                  # Model
                    struct.pack_into("<f", data, b0 + 12, TILE_DRAW_DISTANCE)     # DrawDistance
                    struct.pack_into("<I", data, b0 + 24, 1)                     # DisplayInMirror (the rear-view mirror shows it too)
                    struct.pack_into("<I", data, b0 + 32, 0)                      # PhysType None
                    struct.pack_into("<I", data, b0 + 68, 0)                      # Alignment None
                _, amax = kslf_write.clone_object_any(obj[SRC_DYNDEF], None, du, def_patch, data_in=amax)
    return amax


def _import_defs(amax, wld):
    """GameModel + DynamicObjDef (no physics: Solid = a box in the collision floor, far draw distance) of every
    piece of every imported model placed in the world"""
    import model_import
    used = model_import.used(wld)
    if not used:
        return amax
    A = dump.File(_tmp(amax))
    obj = {"%08X" % u: oi for u, t, oi in A.refs if oi != 0xFFFF}
    for e in used:
        for k in range(e["pieces"]):
            mu, du = model_import.uids(e["slot"], k)

            def gm_patch(data, pool, blocks, name=model_import.model_file(e["slot"], k)):
                struct.pack_into("<I", data, blocks[0], len(pool))
                pool += name.encode() + b"\0"
            _, amax = kslf_write.clone_object_any(obj[SRC_GAMEMODEL], None, mu, gm_patch, data_in=amax)
            k4 = kslf.Kslf(amax)
            o4, n4 = k4.sections[4]
            gm_ref = next(i for i in range(n4) if struct.unpack_from("<I", amax, o4 + 8 * i)[0] == mu)

            def def_patch(data, pool, blocks, gm_ref=gm_ref):
                b0 = blocks[0]
                struct.pack_into("<I", data, b0 + 0, gm_ref)                  # Model
                struct.pack_into("<f", data, b0 + 12, TILE_DRAW_DISTANCE)     # DrawDistance
                struct.pack_into("<I", data, b0 + 24, 1)                      # DisplayInMirror
                struct.pack_into("<I", data, b0 + 32, 0)                      # PhysType None
                struct.pack_into("<I", data, b0 + 68, 0)                      # Alignment None
            _, amax = kslf_write.clone_object_any(obj[SRC_DYNDEF], None, du, def_patch, data_in=amax)
    return amax


def _scenery_defs(amax, wld):
    """a DynamicObjDef (PhysType None, far draw distance) showing each scenery model the user placed
    (road_layout.scenery_def_uid); the GameModels are the game's own (already in amax)"""
    used = road_layout.scenery_models(wld)
    if not used:
        return amax
    A = dump.File(_tmp(amax))
    obj = {"%08X" % u: oi for u, t, oi in A.refs if oi != 0xFFFF}
    have = {u for u, t, oi in A.refs}
    for gm in used:
        du = road_layout.scenery_def_uid(gm)
        assert du not in have, "scenery def uid %08X already used (model %s)" % (du, gm)
        have.add(du)
        k4 = kslf.Kslf(amax)
        o4, n4 = k4.sections[4]
        gm_ref = next(i for i in range(n4) if struct.unpack_from("<I", amax, o4 + 8 * i)[0] == int(gm, 16))

        def def_patch(data, pool, blocks, gm_ref=gm_ref):
            b0 = blocks[0]
            struct.pack_into("<I", data, b0 + 0, gm_ref)                  # Model
            struct.pack_into("<f", data, b0 + 12, TILE_DRAW_DISTANCE)     # DrawDistance
            struct.pack_into("<I", data, b0 + 24, 1)                     # DisplayInMirror (the rear-view mirror shows it too)
            struct.pack_into("<I", data, b0 + 32, 0)                      # PhysType None
            struct.pack_into("<I", data, b0 + 68, 0)                      # Alignment None
        _, amax = kslf_write.clone_object_any(obj[SRC_DYNDEF], None, du, def_patch, data_in=amax)
    return amax


def _tower_defs(amax):
    """GameModel + DynamicObjDef of the TOWERS skyscrapers and the start / finish ARCHES (no physics, far draw
    distance, in the mirror)"""
    A = dump.File(_tmp(amax))
    obj = {"%08X" % u: oi for u, t, oi in A.refs if oi != 0xFFFF}
    for name, fname, gm_uid, our_gm, def_uid, lift in BIG:
        if not name.startswith(("tower_", "arch_")):
            continue

        def gm_patch(data, pool, blocks, fname=fname):
            struct.pack_into("<I", data, blocks[0], len(pool))
            pool += fname.encode() + b"\0"
        _, amax = kslf_write.clone_object_any(obj[SRC_GAMEMODEL], None, our_gm, gm_patch, data_in=amax)
        k4 = kslf.Kslf(amax)
        o4, n4 = k4.sections[4]
        gm_ref = next(i for i in range(n4) if struct.unpack_from("<I", amax, o4 + 8 * i)[0] == our_gm)

        def def_patch(data, pool, blocks, gm_ref=gm_ref):
            b0 = blocks[0]
            struct.pack_into("<I", data, b0 + 0, gm_ref)                  # Model
            struct.pack_into("<f", data, b0 + 12, TILE_DRAW_DISTANCE)     # DrawDistance
            struct.pack_into("<I", data, b0 + 24, 1)                      # DisplayInMirror
            struct.pack_into("<I", data, b0 + 32, 0)                      # PhysType None
            struct.pack_into("<I", data, b0 + 68, 0)                      # Alignment None
        _, amax = kslf_write.clone_object_any(obj[SRC_DYNDEF], None, def_uid, def_patch, data_in=amax)
    return amax


def _shared_models(amax):
    """the shared models / defs of every Flat Earth map (uids FFFFFF30-58, as the original build made them)"""
    # road tile model: GameModel (file name) + DynamicObjDef (how to show it)
    def model_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0], len(pool))            # Filename
        pool += TILE_MODEL.encode() + b"\0"
    A = dump.File(_tmp(amax))
    obj = {"%08X" % u: oi for u, t, oi in A.refs if oi != 0xFFFF}
    _, amax = kslf_write.clone_object(None, None, obj[SRC_GAMEMODEL], None, MODEL_UID, model_patch, data_in=amax)
    A = dump.File(_tmp(amax))
    model_ref = next(i for i, (u, t, oi) in enumerate(A.refs) if u == MODEL_UID)

    def def_patch(data, pool, blocks):
        b = blocks[0]
        struct.pack_into("<I", data, b + 0, model_ref)                # Model
        struct.pack_into("<f", data, b + 12, TILE_DRAW_DISTANCE)      # DrawDistance
        struct.pack_into("<I", data, b + 24, 1)                      # DisplayInMirror (the rear-view mirror shows it too)
        struct.pack_into("<I", data, b + 32, 0)                       # PhysType None
        struct.pack_into("<I", data, b + 68, 0)                       # Alignment None
    _, amax = kslf_write.clone_object(None, None, obj[SRC_DYNDEF], None, TILE_DEF_UID, def_patch, data_in=amax)
    test_ref = next(i for i, (u, t, oi) in enumerate(A.refs)
                    if oi != 0xFFFF and A.T[t]["name"] == "GameModel"
                    and A.top(A.O[oi])["Filename"].lower() == TEST_MODEL_FILE.lower())

    def test_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0] + 24, 1)        # DisplayInMirror (the rear-view mirror)
        def_patch(data, pool, blocks)
        struct.pack_into("<I", data, blocks[0], test_ref)
    _, amax = kslf_write.clone_object(None, None, obj[SRC_DYNDEF], None, TEST_DEF_UID, test_patch, data_in=amax)

    def line_model_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0], len(pool))
        pool += LINE_MODEL.encode() + b"\0"
    _, amax = kslf_write.clone_object(None, None, obj[SRC_GAMEMODEL], None, LINE_MODEL_UID, line_model_patch,
                                      data_in=amax)
    A = dump.File(_tmp(amax))
    line_ref = next(i for i, (u, t, oi) in enumerate(A.refs) if u == LINE_MODEL_UID)

    def line_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0] + 24, 1)        # DisplayInMirror (the rear-view mirror)
        def_patch(data, pool, blocks)
        struct.pack_into("<I", data, blocks[0], line_ref)
    _, amax = kslf_write.clone_object(None, None, obj[SRC_DYNDEF], None, LINE_DEF_UID, line_patch, data_in=amax)
    for src, uid in sorted(ROADSIDE.values(), key=lambda v: v[1]):
        A = dump.File(_tmp(amax))
        new_obj = len(A.O)
        _, amax = kslf_write.clone_object(None, None, obj[src], None, uid, None, data_in=amax)
        amax = _edit(amax, new_obj, _draw_distance(ROADSIDE_DRAW_DISTANCE))
    def add_big(amax, entries):
        for name, fname, gm_uid, our_gm, def_uid, lift in entries:
          if gm_uid is None:
              def gm_patch(data, pool, blocks, fname=fname):
                  struct.pack_into("<I", data, blocks[0], len(pool))
                  pool += fname.encode() + b"\0"
              _, amax = kslf_write.clone_object_any(obj[SRC_GAMEMODEL], None, our_gm, gm_patch, data_in=amax)
              gm_uid = "%08X" % our_gm
          A = dump.File(_tmp(amax))
          gm_ref = next(i for i, (u, t, oi) in enumerate(A.refs) if "%08X" % u == gm_uid)

          def big_patch(data, pool, blocks, gm_ref=gm_ref):
              struct.pack_into("<I", data, blocks[0] + 24, 1)        # DisplayInMirror (the rear-view mirror)
              def_patch(data, pool, blocks)
              struct.pack_into("<I", data, blocks[0], gm_ref)
          _, amax = kslf_write.clone_object_any(obj[SRC_DYNDEF], None, def_uid, big_patch, data_in=amax)
        return amax
    # the skyscrapers (uids above the others below) are made LAST: objects are appended in uid order
    amax = add_big(amax, [b for b in BIG if not b[0].startswith(("tower_", "arch_"))])
    A = dump.File(_tmp(amax))
    box_ref = next(i for i, (u, t, oi) in enumerate(A.refs)
                   if oi != 0xFFFF and A.T[t]["name"] == "GameModel"
                   and A.top(A.O[oi])["Filename"].lower() == TEST_MODEL_FILE.lower())

    def block_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0] + 24, 1)        # DisplayInMirror (the rear-view mirror)
        def_patch(data, pool, blocks)
        struct.pack_into("<I", data, blocks[0], box_ref)
    _, amax = kslf_write.clone_object(None, None, obj[SRC_DYNDEF], None, BLOCK_DEF_UID, block_patch, data_in=amax)

    def wall_model_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0], len(pool))
        pool += WALL_MODEL.encode() + b"\0"
    _, amax = kslf_write.clone_object(None, None, obj[SRC_GAMEMODEL], None, WALL_MODEL_UID, wall_model_patch,
                                      data_in=amax)
    A = dump.File(_tmp(amax))
    wall_ref = next(i for i, (u, t, oi) in enumerate(A.refs) if u == WALL_MODEL_UID)

    def wall_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0] + 24, 1)        # DisplayInMirror (the rear-view mirror)
        def_patch(data, pool, blocks)
        struct.pack_into("<I", data, blocks[0], wall_ref)
    _, amax = kslf_write.clone_object(None, None, obj[SRC_DYNDEF], None, WALL_DEF_UID, wall_patch, data_in=amax)

    def chevron_model_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0], len(pool))
        pool += CHEVRON_MODEL.encode() + b"\0"
    _, amax = kslf_write.clone_object(None, None, obj[SRC_GAMEMODEL], None, CHEVRON_MODEL_UID, chevron_model_patch,
                                      data_in=amax)
    A = dump.File(_tmp(amax))
    chev_ref = next(i for i, (u, t, oi) in enumerate(A.refs) if u == CHEVRON_MODEL_UID)

    def chevron_patch(data, pool, blocks):
        struct.pack_into("<I", data, blocks[0] + 24, 1)        # DisplayInMirror (the rear-view mirror)
        def_patch(data, pool, blocks)
        struct.pack_into("<I", data, blocks[0], chev_ref)
    _, amax = kslf_write.clone_object(None, None, obj[SRC_DYNDEF], None, CHEVRON_DEF_UID, chevron_patch, data_in=amax)
    return amax



def _field_offset(types, t, name):
    """offset of field `name` in type t (searching its bases too)"""
    for f in types[t]["fields"]:
        if f["name"] == name:
            return f["offset"]
    for b, off in types[t]["bases"]:
        o = _field_offset(types, b, name)
        if o is not None:
            return off + o
    return None


def _draw_distance(dist):
    """edit fn: every PhysicalObject in the object gets DrawDistance >= dist (0 = the game's default: kept)"""
    def fn(ctx):
        for bi, (t, cnt, kind) in enumerate(ctx.rows):
            if kind != 0 or ctx.types[t]["name"] != "PhysicalObject":
                continue
            off = _field_offset(ctx.types, t, "DrawDistance")
            for e in range(cnt):
                at = ctx.blocks[bi] + e * ctx.types[t]["size"] + off
                v = struct.unpack_from("<f", ctx.data, at)[0]
                if 0 < v < dist:
                    struct.pack_into("<f", ctx.data, at, dist)
    return fn


def road_texture(w, h, width, cpmodel):
    """road piece texture for a road `width` m wide (+ 8 m): grey, a bright glowing cyan line on both edges, red /
    white kerb stripes outside it (where the solid wall stands: you can see where the road ends), dashed middle line.
    v = across (image rows), u = along (the texture repeats every 14 m: 128 columns)."""
    diff, mat = bytearray(cpmodel.noise(w, h, ROAD_GREY, 12)), bytearray(cpmodel.solid(w, h, (ROAD_SHINE, ROAD_GLOW, 0)))
    across = width + 8.0
    row = across / h                                           # metres per image row
    for y in range(h):
        metres = (y + 0.5) / h * across - across / 2          # from the road middle (v = image rows)
        out = abs(metres) - width / 2                          # metres outside the road edge (< 0 = on the road)
        edge = -max(1.0, row * 0.6) < out < max(1.0, row * 0.6)
        kerb = out >= max(1.0, row * 0.6)
        for x in range(w):
            i = 3 * (y * w + x)
            if edge:
                diff[i:i + 3] = bytes(WALL_COLOUR)
                mat[i:i + 3] = bytes((ROAD_SHINE, 255, 0))
            elif kerb:
                red = (x * 8 // w) % 2 == 0                     # 8 stripes per 14 m
                diff[i:i + 3] = bytes((230, 30, 30) if red else (240, 240, 240))
                mat[i:i + 3] = bytes((ROAD_SHINE, 200, 0))
            elif y == h // 2 and x < w // 2:                     # middle line: 1 texture row, 7 m dashes
                diff[i:i + 3] = bytes((235, 235, 235))
                mat[i:i + 3] = bytes((ROAD_SHINE, 160, 0))
    return bytes(diff), bytes(mat)


# TrackMania-style road piece: flat road + a raised edge on both sides that curves up into a low wall
BANK = ((0.0, 0.0), (0.9, 0.45), (1.6, 1.2), (2.2, 2.0))     # (metres outside the edge, height) - the raised edge
# the raised side walls: dark graphite (was near-white 225,228,235 glow 110 - the power-ups were hard to see)
BANK_COLOUR, BANK_GLOW = (34, 38, 48), 35
ROAD_STYLE = "trackmania"                                   # "flat" = the old flat piece with kerbs
ROAD_MESH = True        # trackmania style as a few long models that follow the road exactly (road_mesh.py);
                        # False = the old 56 m straight pieces (edges did not line up on bends, overlaps flickered)


def road_profile(width):
    """[(z across, y, v)] of a road piece `width` m wide: banks on both sides, v = arc length across / total"""
    e = width / 2
    pts = [(-(e + o), hgt) for o, hgt in reversed(BANK)] + [(e + o, hgt) for o, hgt in BANK]
    arc = [0.0]
    for (z0, y0), (z1, y1) in zip(pts, pts[1:]):
        arc.append(arc[-1] + math.hypot(z1 - z0, y1 - y0))
    return [(z, y, a / arc[-1]) for (z, y), a in zip(pts, arc)]


def road_texture_profile(w, h, width):
    """texture for road_profile(width): u = columns (along, repeats every 14 m), v = rows (across the profile).
    Road grey with a dashed middle line; a glowing cyan line where the road meets the bank; the bank white with a
    glowing blue band along its top (TrackMania look)."""
    import cpmodel
    prof = road_profile(width)
    v_edge_l, v_edge_r = prof[len(BANK) - 1][2], prof[len(BANK)][2]
    v_mid = (v_edge_l + v_edge_r) / 2
    diff = bytearray(cpmodel.noise(w, h, ROAD_GREY, 12))
    mat = bytearray(cpmodel.solid(w, h, (ROAD_SHINE, ROAD_GLOW, 0)))
    row_v = 1.0 / h
    for y in range(h):
        v = (y + 0.5) / h
        on_bank = v < v_edge_l or v > v_edge_r
        to_edge = min(abs(v - v_edge_l), abs(v - v_edge_r))
        top = min(v, 1 - v) < 1.6 * row_v                     # the top of the bank
        for x in range(w):
            i = 3 * (y * w + x)
            if top:
                diff[i:i + 3] = bytes((40, 120, 255))
                mat[i:i + 3] = bytes((ROAD_SHINE, 255, 0))
            elif to_edge < 0.8 * row_v:                       # road edge line
                diff[i:i + 3] = bytes(WALL_COLOUR)
                mat[i:i + 3] = bytes((ROAD_SHINE, 255, 0))
            elif on_bank:
                diff[i:i + 3] = bytes(BANK_COLOUR)
                mat[i:i + 3] = bytes((ROAD_SHINE, BANK_GLOW, 0))
            elif abs(v - v_mid) < 0.5 * row_v and x < w // 2:  # middle line: 7 m dashes
                diff[i:i + 3] = bytes((235, 235, 235))
                mat[i:i + 3] = bytes((ROAD_SHINE, 160, 0))
    return bytes(diff), bytes(mat)


def road_piece_model(base, w, h, width, cpmodel):
    """the road piece model for a road `width` m wide (ROAD_STYLE)"""
    if ROAD_STYLE == "trackmania":
        dv, mv = road_texture_profile(w, h, width)
        model, _ = cpmodel.make_profile(base, PIECE_LEN, road_profile(width), PIECE_LEN / 14.0,
                                        {GLOW_DIFF: dv, GLOW_MAT: mv})
        return model
    dv, mv = road_texture(w, h, width, cpmodel)
    model, _ = cpmodel.make_quad(base, PIECE_LEN, width + 8.0, PIECE_LEN / 14.0, 1, {GLOW_DIFF: dv, GLOW_MAT: mv})
    return model


def road_chunk_files(src, cpmodel, wld, log=print):
    """{file name: model} of every map's seamless road chunks (road_mesh.py)"""
    fe = sys.modules[__name__]
    base = src.read(src.find(road_mesh.SOURCE))
    tex = cpmodel.parse(base)["textures"]
    files = {}
    for loc in wld["locations"]:
        for m in loc["maps"]:
            k = 0
            main = map_road(m)
            net = road_mesh.network_chunks(fe, m, main)
            ss = surface.settings(m)
            variants = 3 if ss["variety"] else 1          # varied asphalt: a different pattern on every 3rd model
            for ri, (road, cs) in enumerate(net):
                encs = []
                for var in range(variants):
                    enc = {}
                    for name, pick in ((road_mesh.DIFF, 0), (road_mesh.MAT, 1)):
                        _, w, h, mips = tex[name]
                        pic = (road_texture_profile(w, h, road.width) if getattr(road, "kind", "") == "divider"
                               else surface.road_texture(fe, w, h, road.width, m, var))
                        enc[name] = cpmodel.encode_mips(pic[pick], w, h, mips)
                    encs.append(enc)
                for ci, c in enumerate(cs):
                    var = ci % len(encs)
                    model, _ = cpmodel.make_mesh(base, c["verts"], c["strip"], encs[var])
                    # one texture id per road (+ pattern): its chunks share the same pictures
                    files[road_mesh.model_file(m, k)] = cpmodel.new_texture_ids(
                        model, road_mesh.texture_salt(m, ri) ^ (var << 20))
                    k += 1
            # painted surfaces: flat textured models (one texture per surface kind)
            over = road_mesh.overlay_chunks(fe, m, main)
            enc_of = {}
            for c in over:
                if c["key"] not in enc_of:
                    enc = {}
                    for name, pick in ((road_mesh.DIFF, 0), (road_mesh.MAT, 1)):
                        _, w, h, mips = tex[name]
                        enc[name] = cpmodel.encode_mips(surface.material_texture(c["key"], w, h)[pick], w, h, mips)
                    enc_of[c["key"]] = enc
                model, _ = cpmodel.make_mesh(base, c["verts"], c["strip"], enc_of[c["key"]])
                files[road_mesh.model_file(m, k)] = cpmodel.new_texture_ids(model, surface.texture_salt(m, c["key"]))
                k += 1
            if over:
                log("%s: painted surfaces = %d model(s) (%s)" % (m["name"], len(over),
                                                                ", ".join(sorted(set(c["key"] for c in over)))))
            allc = [c for _, cs in net for c in cs]
            log("%s: seamless road = %d models (%d road(s); %d vertices, %d indices in the biggest)" % (
                m["name"], len(allc), len(net), max(len(c["verts"]) for c in allc), max(len(c["strip"]) for c in allc)))
    return files


CHEVRON_LIFT = 1.8        # the arrows start 1.8 m above the road: over the 2 m raised edge (BANK) seen from the car
CHEVRON_STROKE = 0.9      # arrow stroke thickness (m, along the panel)


def chevron_mesh(src, cpmodel):
    """the chevron panel WITHOUT a panel: only the arrows ('>' x CHEVRON_COUNT, pointing +x), each two slanted
    strips, both sides, solid glowing white (see-through between them - no alpha needed). Built on the seamless road's
    source model (one strip mesh) with cpmodel.make_mesh."""
    base = src.read(src.find(road_mesh.SOURCE))
    tex = cpmodel.parse(base)["textures"]
    verts, strip = [], []

    def quad(a, b, c, d):
        """a b c d around the quad (x, y): both sides, joined to the strip with degenerate triangles"""
        for order in ((a, b, d, c), (b, a, c, d)):
            k = len(verts)
            for (x, y) in order:
                verts.append((x, y, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0))
            idx = [k, k + 1, k + 2, k + 3]
            if strip:
                strip.extend([strip[-1], idx[0]])
                if len(strip) % 2:                      # keep the winding of the next triangle
                    strip.append(idx[0])
            strip.extend(idx)
    cell = CHEVRON_LEN / CHEVRON_COUNT
    y0, y1 = CHEVRON_LIFT, CHEVRON_LIFT + CHEVRON_H
    ym = (y0 + y1) / 2
    t = CHEVRON_STROKE
    for k in range(CHEVRON_COUNT):
        x0 = -CHEVRON_LEN / 2 + k * cell + cell * 0.15
        tip = x0 + cell * 0.55
        quad((x0, y1), (x0 + t, y1), (tip + t, ym), (tip, ym))      # upper stroke: top-left -> tip
        quad((tip, ym), (tip + t, ym), (x0 + t, y0), (x0, y0))      # lower stroke: tip -> bottom-left
    enc = {}
    for name, rgb in ((road_mesh.DIFF, (250, 250, 250)), (road_mesh.MAT, (ROAD_SHINE, 255, 0))):
        _, w, h, mips = tex[name]
        enc[name] = cpmodel.encode_mips(cpmodel.solid(w, h, rgb), w, h, mips)
    model, _ = cpmodel.make_mesh(base, verts, strip, enc)
    return cpmodel.new_texture_ids(model, 0x0F1A7E04)


def chevron_texture(w, h):
    """one arrow '>' (pointing to +u = the model's +x) on a dark panel; the model repeats it CHEVRON_COUNT times.
    White arrow = full glow, the panel = dark and dim."""
    from PIL import Image, ImageDraw
    cols, rows = w, h                  # u = columns (along the panel), v = rows (up)
    im = Image.new("RGB", (cols, rows), (24, 26, 32))
    gl = Image.new("RGB", (cols, rows), (ROAD_SHINE, 25, 0))
    pts = [(0.12, 0.10), (0.44, 0.10), (0.86, 0.5), (0.44, 0.90), (0.12, 0.90), (0.54, 0.5)]
    poly = [(u * cols, v * rows) for u, v in pts]
    ImageDraw.Draw(im).polygon(poly, fill=(250, 250, 250))
    ImageDraw.Draw(gl).polygon(poly, fill=(ROAD_SHINE, 255, 0))
    return im.tobytes(), gl.tobytes()


def thumbnail_png(loc, m, path):
    """the map's menu picture (512 x 256): the road shape from above + the names"""
    from PIL import Image, ImageDraw, ImageFont
    r = road_mod.for_map(m)
    net = network.get(m, r)
    W, H = 512, 256
    im = Image.new("RGB", (W, H), (14, 18, 28))
    d = ImageDraw.Draw(im)
    for k in range(0, H, 16):
        d.line([(0, k), (W, k)], fill=(20, 26, 40))
    bb = [rr.bounds(r.width) for rr in [r] + [br for _, br in net.branches]]
    x0, x1 = min(q[0] for q in bb), max(q[1] for q in bb)
    z0, z1 = min(q[2] for q in bb), max(q[3] for q in bb)
    sc = min((W - 40) / max(x1 - x0, 1), (H - 70) / max(z1 - z0, 1))
    ox, oz = (W - (x1 - x0) * sc) / 2, 50 + (H - 70 - (z1 - z0) * sc) / 2

    def P(x, z):
        return ox + (x - x0) * sc, oz + (z1 - z) * sc
    pts = [P(x, z) for x, z in r.centre]
    if r.closed:
        pts.append(pts[0])
    wpx = max(3, int(r.width * sc))
    branch_pts = [([P(x, z) for x, z in br.centre], max(3, int(br.width * sc))) for _, br in net.branches]
    for bp, bw in branch_pts:                       # branches: the same look, under the main road's middle
        d.line(bp, fill=(0, 140, 170), width=bw + 4)
    d.line(pts, fill=(0, 140, 170), width=wpx + 4)
    for bp, bw in branch_pts:
        d.line(bp, fill=(70, 74, 82), width=bw)
    d.line(pts, fill=(70, 74, 82), width=wpx)
    for dv in net.dividers:                          # lane split walls
        d.line([P(x, z) for x, z in dv.centre], fill=(255, 176, 32), width=2)
    for i, h in enumerate(r.height):
        if h > 0.5 and i % 3 == 0:
            x, y = pts[i]
            d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(255, 200, 60))
    d.ellipse([pts[0][0] - 6, pts[0][1] - 6, pts[0][0] + 6, pts[0][1] + 6], fill=(60, 230, 90))
    d.ellipse([pts[-1][0] - 6, pts[-1][1] - 6, pts[-1][0] + 6, pts[-1][1] + 6], fill=(255, 255, 255))
    try:
        big = ImageFont.truetype("arialbd.ttf", 26)
        small = ImageFont.truetype("arial.ttf", 16)
    except OSError:
        big = small = ImageFont.load_default()
    d.text((14, 8), m["name"], fill=(255, 255, 255), font=big)
    d.text((16, 36), "%s  -  %.1f km" % (loc["name"], r.length / 1000), fill=(120, 200, 230), font=small)
    im.save(path)
    return path


def tower_models(src, cpmodel):
    """{file: model} of the TOWERS skyscrapers: the boxes of tower_parts (4 sides + roof each) made from the road
    chunks' source model; texture = a generated facade per design (TOWER_STYLE: frame / window colours, window grid
    or ribbon bands, some windows lit = glow) repeating every 16 m"""
    import numpy as np
    base = src.read(src.find(road_mesh.SOURCE))
    tex = cpmodel.parse(base)["textures"]
    out = {}
    for name, wx, dz, h, seed in TOWERS:
        st = TOWER_STYLE.get(name, {})
        rng = np.random.default_rng(seed)
        _, tw, th, tm = tex[road_mesh.DIFF]
        _, mw, mh, mm = tex[road_mesh.MAT]
        cols, rows = st.get("cols", 4), st.get("rows", 4)
        frame, win = st.get("frame", (24, 30, 40)), st.get("win", (52, 70, 92))
        lit_col = st.get("lit_col", (250, 214, 140))
        ribbon = st.get("ribbon", False)
        lit = rng.random((rows, cols)) < st.get("lit", 0.35)
        # facade: cols x rows windows per texture (16 m), frames between them (ribbon: one long band per floor);
        # the material: shine (glass) + glow of the lit windows
        img = np.zeros((th, tw, 3), np.uint8)
        img[:] = frame
        mat = np.zeros((mh, mw, 3), np.uint8)
        mat[:, :, 0] = 60
        for i in range(rows):
            for j in range(cols):
                fx0, fx1 = (j + (0.0 if ribbon else 0.125)) / cols, (j + (1.0 if ribbon else 0.875)) / cols
                fy0, fy1 = (i + 0.2) / rows, (i + 0.8) / rows
                img[int(fy0 * th):int(fy1 * th), int(fx0 * tw):int(fx1 * tw)] = lit_col if lit[i, j] else win
                if lit[i, j]:
                    mat[int(fy0 * mh):int(fy1 * mh), int(fx0 * mw):int(fx1 * mw), 1] = 255
        enc = {road_mesh.DIFF: cpmodel.encode_mips(img.tobytes(), tw, th, tm),
               road_mesh.MAT: cpmodel.encode_mips(mat.tobytes(), mw, mh, mm)}
        verts, tris = [], []

        def quad(p0, p1, p2, p3, nrm, uw, vh, v0=0.0):
            b = len(verts)
            for p, (u, v) in zip((p0, p1, p2, p3), ((0, v0 + vh), (uw, v0 + vh), (uw, v0), (0, v0))):
                verts.append((p[0], p[1], p[2], nrm[0], nrm[1], nrm[2], u, v))
            # winding: a triangle (a, b, c) faces along cross(b - a, c - a) in game (road chunks, checked in game);
            # (b, b+1, b+2) faced INTO the box (the user saw the sides facing the track missing) -> reversed
            tris.extend([(b, b + 2, b + 1), (b, b + 3, b + 2)])
        rep = 16.0
        for x0, x1, z0, z1, y0, y1 in tower_parts(name):
            for (ax, az, bx, bz, nrm) in ((x0, z0, x1, z0, (0, 0, -1)), (x1, z0, x1, z1, (1, 0, 0)),
                                          (x1, z1, x0, z1, (0, 0, 1)), (x0, z1, x0, z0, (-1, 0, 0))):
                span = math.hypot(bx - ax, bz - az)
                # v from the ground up (the floors of stacked parts line up)
                quad((ax, y0, az), (bx, y0, bz), (bx, y1, bz), (ax, y1, az), nrm, span / rep, (y1 - y0) / rep,
                     -y1 / rep)
            quad((x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1), (0, 1, 0), 0.02, 0.02)   # roof (frame)
        strip = []
        for a, b, c in tris:
            if strip:
                strip += [strip[-1], a]
                if len(strip) % 2:
                    strip.append(a)
            strip += [a, b, c]
        model, _ = cpmodel.make_mesh(base, verts, strip, enc)
        out["models\\objects\\flat_earth\\%s.model" % name] = cpmodel.new_texture_ids(model, 0x0F4D0000 + seed)
    return out


def arch_texture(text, tw, th, mw, mh):
    """(diffuse RGB bytes tw x th, material RGB bytes mw x mh) of an arch: horizontal bands (u repeats across):
    rows 0-47/256 the sign (text, glowing), 48-79 a checker strip, 80-159 pillar metal + a cyan light stripe,
    160-255 the beam's dark sides + a cyan line. Material: R = shine, G = glow."""
    import numpy as np
    from PIL import Image, ImageDraw, ImageFont

    def band(a, b, n):
        return int(a / 256 * n), int(b / 256 * n)
    img = np.zeros((th, tw, 3), np.uint8)
    mat = np.zeros((mh, mw, 3), np.uint8)
    mat[:, :, 0] = 40
    # sign: dark blue panel, white glowing letters, thin cyan frame
    y0, y1 = band(0, 48, th)
    img[y0:y1] = (14, 20, 36)
    sign = Image.new("L", (tw, y1 - y0), 0)
    d = ImageDraw.Draw(sign)
    size = y1 - y0 - 10
    while size > 8:
        try:
            font = ImageFont.truetype("arialbd.ttf", size)
        except OSError:
            font = ImageFont.load_default()
            break
        box = d.textbbox((0, 0), text, font=font)
        if box[2] - box[0] <= tw - 16:
            break
        size -= 2
    box = d.textbbox((0, 0), text, font=font)
    d.text(((tw - (box[2] - box[0])) / 2 - box[0], (y1 - y0 - (box[3] - box[1])) / 2 - box[1]), text, fill=255,
           font=font)
    letters = np.array(sign) > 128
    img[y0:y1][letters] = (255, 255, 255)
    img[y0:y0 + 2] = img[y1 - 2:y1] = (0, 220, 255)
    m0, m1 = band(0, 48, mh)
    mletters = np.array(sign.resize((mw, m1 - m0))) > 128
    mat[m0:m1, :, 1][mletters] = 255
    mat[m0:m0 + 1, :, 1] = mat[m1 - 1:m1, :, 1] = 255
    # checker strip: 2 rows of squares (16 across the texture)
    y0, y1 = band(48, 80, th)
    sq = (y1 - y0) // 2
    for r in range(2):
        for k in range(tw // sq):
            white = (r + k) % 2 == 0
            img[y0 + r * sq:y0 + (r + 1) * sq, k * sq:(k + 1) * sq] = (235, 235, 235) if white else (16, 16, 16)
    m0, m1 = band(48, 80, mh)
    msq = max((m1 - m0) // 2, 1)
    for r in range(2):
        for k in range(mw // msq):
            if (r + k) % 2 == 0:
                mat[m0 + r * msq:m0 + (r + 1) * msq, k * msq:(k + 1) * msq, 1] = 150
    # pillars: grey metal, a glowing cyan stripe down the middle
    y0, y1 = band(80, 160, th)
    img[y0:y1] = (70, 76, 88)
    img[y0:y1, tw * 7 // 16:tw * 9 // 16] = (0, 220, 255)
    m0, m1 = band(80, 160, mh)
    mat[m0:m1, mw * 7 // 16:mw * 9 // 16, 1] = 255
    mat[m0:m1, :, 0] = 90
    # beam sides / ends / top: dark, a cyan line
    y0, y1 = band(160, 256, th)
    img[y0:y1] = (20, 24, 34)
    ly0, ly1 = band(236, 244, th)
    img[ly0:ly1] = (0, 220, 255)
    m0, m1 = band(236, 244, mh)
    mat[m0:max(m1, m0 + 1), :, 1] = 255
    return img.tobytes(), mat.tobytes()


def arch_models(src, cpmodel):
    """{file: model} of the ARCHES (start / finish gantry, arch_parts) made from the road chunks' source model.
    The sign faces both ways and reads left to right from each side (model +x = the road's side direction = the
    driver's LEFT when coming from +z)."""
    base = src.read(src.find(road_mesh.SOURCE))
    tex = cpmodel.parse(base)["textures"]
    _, tw, th, tm = tex[road_mesh.DIFF]
    _, mw, mh, mm = tex[road_mesh.MAT]
    V_SIGN, V_CHECK, V_PILLAR, V_BEAM = (0.0, 48 / 256), (48 / 256, 80 / 256), (80 / 256, 160 / 256), (160 / 256, 1.0)
    out = {}
    for name, text, seed in ARCHES:
        dv, mv = arch_texture(text, tw, th, mw, mh)
        enc = {road_mesh.DIFF: cpmodel.encode_mips(dv, tw, th, tm), road_mesh.MAT: cpmodel.encode_mips(mv, mw, mh, mm)}
        verts, tris = [], []

        def quad(p, nrm, uv):
            """4 corners (any order around the quad) + their (u, v); wound to face along nrm"""
            b = len(verts)
            for (x, y, z), (u, v) in zip(p, uv):
                verts.append((x, y, z, nrm[0], nrm[1], nrm[2], u, v))
            for t in ((b, b + 1, b + 2), (b, b + 2, b + 3)):
                A, B, C = (verts[i][:3] for i in t)
                cr = ((B[1] - A[1]) * (C[2] - A[2]) - (B[2] - A[2]) * (C[1] - A[1]),
                      (B[2] - A[2]) * (C[0] - A[0]) - (B[0] - A[0]) * (C[2] - A[2]),
                      (B[0] - A[0]) * (C[1] - A[1]) - (B[1] - A[1]) * (C[0] - A[0]))
                tris.append(t if sum(c * n for c, n in zip(cr, nrm)) > 0 else (t[0], t[2], t[1]))

        def box(x0, x1, z0, z1, y0, y1, vb, u_rep=4.0):
            """4 sides + top, texture band vb, u in metres / u_rep"""
            va, vz = vb
            for (ax, az, bx, bz, n) in ((x0, z0, x1, z0, (0, 0, -1)), (x1, z0, x1, z1, (1, 0, 0)),
                                        (x1, z1, x0, z1, (0, 0, 1)), (x0, z1, x0, z0, (-1, 0, 0))):
                uw = math.hypot(bx - ax, bz - az) / u_rep
                quad([(ax, y0, az), (bx, y0, bz), (bx, y1, bz), (ax, y1, az)], n,
                     [(0, vz), (uw, vz), (uw, va), (0, va)])
            quad([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], (0, 1, 0),
                 [(0, vz), (0.5, vz), (0.5, va), (0, va)])
        X, P, D = ARCH_W / 2, ARCH_PILLAR / 2, ARCH_DEPTH / 2
        for sx in (-1, 1):                                   # pillars
            box(sx * X - P, sx * X + P, -P, P, 0.0, ARCH_Y0, V_PILLAR, u_rep=2 * P)
        # the beam: top, bottom, ends dark; front + back = checker | SIGN | checker
        y0, y1 = ARCH_Y0, ARCH_Y1
        bx0, bx1 = -X - P, X + P
        quad([(bx0, y1, -D), (bx1, y1, -D), (bx1, y1, D), (bx0, y1, D)], (0, 1, 0), [(0, 1), (8, 1), (8, V_BEAM[0]), (0, V_BEAM[0])])
        quad([(bx0, y0, -D), (bx1, y0, -D), (bx1, y0, D), (bx0, y0, D)], (0, -1, 0), [(0, 1), (8, 1), (8, V_BEAM[0]), (0, V_BEAM[0])])
        for sx in (-1, 1):
            xe = bx1 if sx > 0 else bx0
            quad([(xe, y0, -D), (xe, y0, D), (xe, y1, D), (xe, y1, -D)], (sx, 0, 0), [(0, 1), (1, 1), (1, V_BEAM[0]), (0, V_BEAM[0])])
        h = y1 - y0
        half_sign = h * (256 / 48) / 2                       # the sign keeps the texture's shape (no stretching)
        sq_m = h / 2                                          # checker squares as tall as half the beam
        for fz, n in ((D, (0, 0, 1)), (-D, (0, 0, -1))):
            # reading left to right: from +z the driver's right is -x; from -z it is +x
            right = -1 if fz > 0 else 1
            segs = [(-right * (X + P), -right * half_sign, "check"), (-right * half_sign, right * half_sign, "sign"),
                    (right * half_sign, right * (X + P), "check")]
            for xa, xb, kind in segs:                       # xa = the reader's left end, xb = the right end
                if kind == "sign":
                    uv = [(0, V_SIGN[1]), (1, V_SIGN[1]), (1, V_SIGN[0]), (0, V_SIGN[0])]
                else:
                    uw = abs(xb - xa) / (sq_m * 16 / 1)       # 16 squares across the texture
                    uv = [(0, V_CHECK[1]), (uw, V_CHECK[1]), (uw, V_CHECK[0]), (0, V_CHECK[0])]
                quad([(xa, y0, fz), (xb, y0, fz), (xb, y1, fz), (xa, y1, fz)], n, uv)
        strip = []
        for a, b, c in tris:
            if strip:
                strip += [strip[-1], a]
                if len(strip) % 2:
                    strip.append(a)
            strip += [a, b, c]
        model, _ = cpmodel.make_mesh(base, verts, strip, enc)
        out["models\\objects\\flat_earth\\%s.model" % name] = cpmodel.new_texture_ids(model, 0x0F4E0000 + seed)
    return out


def build_root(log=print, wld=None):
    """root.pak (on top of the minimap/thumbnail pack): + our road / line / wall models, the road-piece width
    models and every map's menu picture"""
    sys.path.insert(0, os.path.join(MB, "model"))
    import pak
    import cpmodel
    wld = wld or _world()
    os.makedirs(WORK, exist_ok=True)
    # source: a model whose shader has a material texture (GREEN = glow): the level has no light for our objects
    src = pak.Pak(paths.MODEL_PAK)
    base = src.read(src.find(GLOW_SOURCE))
    tex = cpmodel.parse(base)["textures"]
    (w, h) = tex[GLOW_DIFF][1:3]
    tile = road_piece_model(base, w, h, ROAD_WIDTH, cpmodel)
    cell = 8
    chk = b"".join(bytes([235 if (x // cell + y // cell) % 2 else 20] * 3) for y in range(h) for x in range(w))
    line, _ = cpmodel.make_quad(base, LINE_W, LINE_LEN, 1, 21, {
        GLOW_DIFF: chk, GLOW_MAT: cpmodel.solid(w, h, (ROAD_SHINE, LINE_GLOW, 0))})
    wall, _ = cpmodel.make_quad(base, ROAD_LEN, WALL_H, ROAD_LEN / 4, 1, {
        GLOW_DIFF: cpmodel.solid(w, h, WALL_COLOUR), GLOW_MAT: cpmodel.solid(w, h, (ROAD_SHINE, WALL_GLOW, 0))},
        vertical=True)
    tile = cpmodel.new_texture_ids(tile, 0x0F1A7E01)
    line = cpmodel.new_texture_ids(line, 0x0F1A7E02)
    wall = cpmodel.new_texture_ids(wall, 0x0F1A7E03)
    chevron = chevron_mesh(src, cpmodel)            # arrows only (see-through), lifted over the raised edge
    files = {TILE_MODEL: tile, LINE_MODEL: line, WALL_MODEL: wall, CHEVRON_MODEL: chevron}
    files.update(tower_models(src, cpmodel))
    files.update(arch_models(src, cpmodel))
    for k, b in enumerate(TILE_WIDTHS):
        model = road_piece_model(base, w, h, b, cpmodel)
        files[tile_variant_file(b)] = cpmodel.new_texture_ids(model, 0x0F1A7E10 + k)
    if ROAD_MESH:
        files.update(road_chunk_files(src, cpmodel, wld, log))
    import model_import                             # the player's imported models (.glb)
    files.update(model_import.build_files(src.read(src.find(road_mesh.SOURCE)), cpmodel, wld, road_mesh.DIFF,
                                          road_mesh.MAT, log))
    tmp = ROOT_OUT + ".%d.tmp" % os.getpid()           # own temporary file (two builds at once broke the shared one)
    # in-race minimap of every location: picture + route shapes (by city name)
    base_pak = pak.Pak(ROOT_BASE)
    src_ipf = base_pak.read(base_pak.find(minimap.SRC_GAME_IPF))
    src_tex = base_pak.read(base_pak.find(minimap.SRC_TEXTURE))
    aliases = {name: TILE_MODEL_ALIAS_OF for name in files}
    for loc in wld["locations"]:
        roads = _roads_of(loc)
        b = minimap.city_bounds(roads)
        routes = []
        for m, r in zip(loc["maps"], roads):
            own = None
            mm = m.get("minimap") or {}
            if not mm.get("auto", True) and mm.get("image") and os.path.exists(mm["image"]):
                own = minimap.image_polys(b, mm["image"])           # the map's own picture, traced
                log("minimap of %s: own picture %s (%d shapes)" % (m["name"], mm["image"], len(own)))
            branches = [br for _, br in network.get(m, r).branches]
            if own is None and branches:                            # the route ribbon + its branches
                own = minimap.route_polys(b, r)
                for br in branches:
                    own += minimap.route_polys(b, br)
            routes.append((m["route_name"], r, r.dist[gen_aid.race_lines(r, m)[0]], own))
        pic = roads + [br for m, r in zip(loc["maps"], roads) for _, br in network.get(m, r).branches]
        for name, data, like in ((minimap.game_ipf_name(loc["city_name"]), minimap.game_ipf(src_ipf, b, routes),
                                  minimap.SRC_GAME_IPF),
                                 (minimap.texture_name(loc["city_name"]), minimap.texture(src_tex, minimap.picture(b, pic)),
                                  minimap.SRC_TEXTURE)):
            files[name] = data
            if base_pak.find(name) is None:
                aliases[name] = like
    # every map's own start cameras (intro_cam.py): a fly-in over its start line + a shot along its grid
    if INTRO_MODE == "own":
        import intro_cam
        for loc in wld["locations"]:
            for m, r in zip(loc["maps"], _roads_of(loc)):
                s0 = r.dist[gen_aid.race_lines(r, m)[0]]
                for shot, data in intro_cam.cycles_for_map(r, s0, GROUND_Y, m).items():
                    name = intro_cycle_name(m, shot) + ".cycle"
                    files[name] = data
                    aliases[name] = INTRO_CYCLE_ALIAS_OF
    import car_build
    aliases.update(car_build.model_aliases(wld, B.U))                     # custom cars: their models by their names
    files.update(car_build.model_files(wld, log))                              # (+ their own body model)
    files.update(car_build.icon_files(wld, log))                               # (+ their mini icons)
    pak.add_aliases(ROOT_BASE, tmp, aliases)
    # menu pictures: ui_frontend.bpr entries named like each route's Thumbnail
    sys.path.insert(0, os.path.join(MB, "ui"))
    import bpr
    frontend = base_pak.read(base_pak.find(FRONTEND_BPR))
    for li, loc in enumerate(wld["locations"]):
        for mi, m in enumerate(loc["maps"]):
            pic = m.get("picture")
            if not pic and li == 0 and mi == 0 and os.path.exists(THUMBNAIL_PNG):
                pic = THUMBNAIL_PNG
            if not pic or not os.path.exists(pic):
                pic = thumbnail_png(loc, m, os.path.join(WORK, "thumb_%s.png" % m["route_name"].lower()))
            frontend = bpr.set_picture(frontend, m["route_name"], pic)
    frontend = car_build.menu_pictures(frontend, wld)                         # custom cars: menu pictures
    files[FRONTEND_BPR] = frontend
    pak.replace_files(tmp, ROOT_OUT, files)
    os.remove(tmp)
    log("root pack: %s (%d files, %d menu pictures, minimaps for %d location(s))" % (ROOT_OUT, len(files) - 1,
                                                          sum(len(l["maps"]) for l in wld["locations"]), len(wld["locations"])))
    return ROOT_OUT



UI_CONFIG_OBJ = 95                   # ui.bin: the UiXtConfig object
MAP_SHAPES_FIELD = 576               # its MapShapes array (sz8 names of the minimap .ipf files to load)


def add_map_shapes(ui, names):
    """ui.bin with `names` added to UiXtConfig.MapShapes (the game loads ui/data/ipf/minimap/<name>_game.ipf for them)"""
    def fn(ctx):
        blk, first, cnt = struct.unpack_from("<HHI", ctx.data, MAP_SHAPES_FIELD)
        assert ctx.rows[blk][1] == first + cnt, "MapShapes is not the last list of its block"
        have = set()
        for k in range(cnt):
            off = struct.unpack_from("<I", ctx.data, ctx.blocks[blk] + 4 * (first + k))[0]
            have.add(bytes(ctx.pool[off:ctx.pool.index(b"\0", off)]).decode().lower())
        new = []
        for n in names:
            if n.lower() not in have:
                new.append(struct.pack("<I", len(ctx.pool)))
                ctx.pool += n.lower().encode() + b"\0"
                have.add(n.lower())
        if new:
            ctx.pool_changed = True
            ctx.insert(blk, first + cnt, b"".join(new))
            struct.pack_into("<HHI", ctx.data, MAP_SHAPES_FIELD, blk, first, cnt + len(new))
    src, dst = _tmp(ui), _tmp(b"")
    out = kslf_write.edit_object(src, dst, UI_CONFIG_OBJ, fn)
    os.unlink(src)
    os.unlink(dst)
    return out


def build_audio(data):
    A = dump.File(_tmp(data))
    obj = {"%08X" % u: oi for u, t, oi in A.refs if oi != 0xFFFF}
    for src, ours in sorted((v for v in AUDIO.values() if v[1]), key=lambda v: v[1]):
        _, data = kslf_write.clone_object(None, None, obj[src], None, ours, None, data_in=data)
    return data


def build_multiplayer(mp, wld=None):
    wld = wld or _world()
    uids = [m["route_uid"] for loc in wld["locations"] for m in loc["maps"]]

    def add_route(c):
        ROUTES, ARENAS = 160, 168
        blk, first, cnt = struct.unpack_from("<HHI", c.data, ROUTES)
        ablk, afirst, acnt = struct.unpack_from("<HHI", c.data, ARENAS)
        refs = [c.add_uid(u, "Route") for u in uids]
        c.insert(blk, first + cnt, b"".join(struct.pack("<I", r) for r in refs))
        struct.pack_into("<HHI", c.data, ROUTES, blk, first, cnt + len(refs))
        struct.pack_into("<HHI", c.data, ARENAS, ablk, afirst + len(refs), acnt)
    return _edit(mp, 585, add_route)



PHYSICS_FRAME = os.path.join(paths.LEVELS, "showroom", "Level.levelphysics")
PHYSICS_GAME = os.path.join(paths.LEVELS, "flat_earth", "level.levelphysics")


def build_physics(boxes, log, pieces=(), bounds=None, decks=(), out=None, game=None, patches=()):
    sys.path.insert(0, os.path.join(MB, "physics"))
    import write_flat
    x0, x1, z0, z1 = bounds or (OX - 2000, OX + 2000, OZ - 2000, OZ + 2000)
    data = write_flat.build_flat(PHYSICS_FRAME, GROUND_Y, x0, x1, z0, z1, boxes, pieces, decks, patches)
    out = out or os.path.join(WORK, "level.levelphysics")
    game = game or PHYSICS_GAME
    open(out, "wb").write(data)
    if DRY:
        log("collision floor (dry run): %s" % out)
        return
    try:
        open(game, "wb").write(data)
        log("collision floor: %d solid pieces + %d bridge decks -> %s" % (len(boxes) + len(pieces), len(decks), game))
    except OSError as e:
        log("collision floor written to %s, but NOT copied into the game (%s) - close Blur and build again"
            % (out, e))


LEVEL_TEMPLATE = paths.LEVEL_TEMPLATE    # showroom level, scenery hidden (setup_game.make_level_template)
DRY = bool(os.environ.get("FE_DRY"))              # test builds: nothing is written into the game folder


def map_road(m):
    return road_mod.for_map(m)


def build_level_files(log, wld=None):
    """per location: the actorpack (all its maps), the collision floor (all its maps' walls / containers / decks),
    the level folder; per map: the race file (.aid). Returns ({location index: actorpack bytes},
    {(location, map): aid bytes}, {(location, map): list mapping})"""
    wld = wld or _world()
    fe = sys.modules[__name__]
    packs, aids, lists = {}, {}, {}
    for li, loc in enumerate(wld["locations"]):
        work = os.path.join(WORK, loc["folder"])
        os.makedirs(work, exist_ok=True)
        specs, pieces, decks, bounds = [], [], [], None
        patches = []
        for mi, m in enumerate(loc["maps"]):
            road = map_road(m)
            lines = gen_aid.race_lines(road, m)               # the map's start / finish (movable in the designer)
            net = network.get(m, road)                        # branches + lane splits: alternative AI routes
            for p in net.problems:
                log("%s / %s: %s" % (loc["name"], m["name"], p))
            alts, avoid = net.alt_routes(GROUND_Y), net.busy_ranges()
            _, info = gen_aid.build_path(road, GROUND_Y, lines=lines, alts=alts, avoid=avoid)
            corridor = road_layout.ai_corridor(fe, road, info, m)
            alts = net.alt_routes(GROUND_Y, corridor)         # they start ON the main AI line (the junction)
            band = net.logic_band(alts)                       # the Logic line covers the branches (no reset there)
            d_aid, info = gen_aid.build_path(road, GROUND_Y, corridor=corridor, lines=lines, alts=alts, avoid=avoid,
                                             band=band)
            if alts:
                log("%s / %s: %d branch(es), %d lane split(s) -> %d alternative AI route(s)"
                    % (loc["name"], m["name"], len(net.branches), len(net.splits), len(alts)))
            aids[(li, mi)] = d_aid
            open(os.path.join(work, m["route_name"].lower() + ".aid"), "wb").write(d_aid)
            specs.append({"start_line": info["start_line"], "finish_line": info["finish_line"],
                          "start_angle": info["start_angle"], "width": road.width,
                          "tiles": road_layout.tile_spots(fe, road, info, m), "tile_def_uid": TILE_DEF_UID,
                          "lamps": road_layout.lamp_spots(fe, road, info, m),
                          "audio": road_layout.audio_spots(fe, road, info),
                          "pickup_spots": road_layout.pickup_spots(fe, road, info, m),
                          "list_uids": m.get("list_uids")})
            p, dk = road_layout.collision_pieces(fe, road, info, m)
            pieces += p
            decks += dk
            sp = surface.physics_patches(fe, m, net)          # dirt / water ... (the game's own grip)
            patches += sp
            if sp:
                mats = {}
                for _, mat in sp:
                    mats[mat] = mats.get(mat, 0) + 1
                log("%s / %s: surface patches %s" % (loc["name"], m["name"],
                                                     ", ".join("%s x%d" % (k2, v) for k2, v in sorted(mats.items()))))
            b = road.bounds(1500.0)
            bounds = b if bounds is None else (min(bounds[0], b[0]), max(bounds[1], b[1]),
                                               min(bounds[2], b[2]), max(bounds[3], b[3]))
            log("%s / %s: road %.0f m (%d points, tightest turn %.0f m, %d bridge(s), steepest %.0f %%), %d power-ups"
                % (loc["name"], m["name"], road.length, info["points"], road.min_radius(), len(road.crossings),
                   100 * road.max_grade(), len(specs[-1]["pickup_spots"])))
        ap_path = os.path.join(work, "actorpack.xt")
        maps_lists = gen_actorpack.build_location(ap_path, specs)
        for mi, mp in enumerate(maps_lists):
            if mp:
                lists[(li, mi)] = mp
        packs[li] = open(ap_path, "rb").read()
        # the level folder: the template's graphics files + this location's collision floor
        game_dir = os.path.join(paths.LEVELS, loc["folder"]) if not DRY else os.path.join(work, "level_folder")
        os.makedirs(game_dir, exist_ok=True)
        for f in ("level.level", "level.levelrender", "level.levelstream"):
            if DRY:
                break
            dst = os.path.join(game_dir, f)
            if not os.path.exists(dst):
                import shutil
                shutil.copy(os.path.join(LEVEL_TEMPLATE, f), dst)
        if not DRY:
            # LIGHTING tab: "Light on placed objects" -> every light sample of this location's level
            lv = lighting.effective(loc)[1]          # untouched Lighting tab: Amboy day + objects lit up
            if lv.get("object_light"):
                roads = [road_mod.for_map(mm)
                         for mm in loc["maps"]]
                n = lighting.write_level_light(os.path.join(game_dir, "level.levelrender"), lv, roads, GROUND_Y)
                log("%s: object light %s -> %d level light points along the roads" % (loc["name"], lv["object_light"], n))
        build_physics((), log, pieces, bounds, decks, os.path.join(work, "level.levelphysics"),
                      os.path.join(game_dir, "level.levelphysics"), patches)
    return packs, aids, lists



def add_flat_earth(parts, log):
    wld = _world()
    packs, aids, lists = build_level_files(log, wld)
    f = parts["files"]
    log("database: %d location(s), %d map(s)" % (len(wld["locations"]), sum(len(l["maps"]) for l in wld["locations"])))
    # how the surfaces DRIVE: our own copy of LA River's grip table (Surface tab: Like the game / Strong / Extreme)
    rp = f.get("gamedata/xt/racingphysics.bin") or open(os.path.join(B.U, "xt", "racingphysics.bin"), "rb").read()
    # (per LOCATION now: one table per grip level in use; "game" = the cities keep LA River's own)
    wld["_feel_uids"] = {}
    for feel in sorted({surface.loc_feel(wld, lc) for lc in wld["locations"]}):
        rp, uid = surface.feel_table(rp, feel)
        if uid:
            wld["_feel_uids"][feel] = uid
            log("surface grip: %s (our own grip table %08X) for %s" % (
                feel, uid, ", ".join(lc["name"] for lc in wld["locations"] if surface.loc_feel(wld, lc) == feel)))
    if wld["_feel_uids"]:
        f["gamedata/xt/racingphysics.bin"] = rp
    amax = f["gamedata/xt/amax.bin"]
    for feel, uid in sorted(wld["_feel_uids"].items(), key=lambda kv: kv[1]):
        amax, _ = kslf_write.insert_uid(amax, uid, "RP_SurfaceMappingList")
    f["gamedata/xt/amax.bin"] = build_amax(amax, wld, lists)
    f["gamedata/xt/multiplayer.bin"] = build_multiplayer(f["gamedata/xt/multiplayer.bin"], wld)
    names = []
    for lc in wld["locations"]:
        names.append((lc["loc_id"], "CRN_%s" % lc["city_name"].upper(), lc["name"]))
        for m in lc["maps"]:
            names.append((m["loc_id"], "CRN_%s" % m["route_name"].upper(), m["name"]))
    loc_in = _tmp(f["gamedata/xt/loc/cityandroutenames.loc"])
    loc_out = _tmp(b"")
    f["gamedata/xt/loc/cityandroutenames.loc"] = loc.write(loc_in, loc_out, add=names)
    os.unlink(loc_in)
    os.unlink(loc_out)
    # the player's own cars (Custom car tab)
    import car_build
    car_build.add_cars(f, wld, B.U, log)
    if USE_DOCKS_SOUNDS:
        f["gamedata/xt/audio.bin"] = build_audio(open(os.path.join(B.U, "xt", "audio.bin"), "rb").read())
    # Bloom plugin row: it copies the (unused) Advanced Graphics screen's Vsync row - give that copy the title "Bloom"
    ui = bytearray(open(os.path.join(B.U, "xt", "ui.bin"), "rb").read())
    assert struct.unpack_from("<I", ui, UI_ADV_VSYNC_TITLE)[0] == 0x0A559F98, "ui.bin changed?"
    if BLOOM_ROW:
        struct.pack_into("<I", ui, UI_ADV_VSYNC_TITLE, BLOOM_LOC)
    # in-race minimap: the game only loads the minimap shapes of the cities named in UiXtConfig.MapShapes
    f["gamedata/xt/ui.bin"] = add_map_shapes(bytes(ui), [lc["city_name"].lower() for lc in wld["locations"]])
    loc_out = _tmp(b"")
    f["gamedata/xt/loc/common.loc"] = loc.write(os.path.join(B.U, "xt", "loc", "common.loc"), loc_out,
                                                add=[(BLOOM_LOC, "CMN_OPT_GRAPHICS_BLOOM", "Bloom")] if BLOOM_ROW else [])
    os.unlink(loc_out)
    for li, l in enumerate(wld["locations"]):
        ap = "gamedata/levels/%s/actorpack.xt" % l["folder"]
        parts["aliases"][ap] = "gamedata/levels/showroom/actorpack.xt"      # new files: alias first, then replace
        f[ap] = packs[li]
        for mi, m in enumerate(l["maps"]):
            ai = "gamedata/levels/%s/%s.aid" % (l["folder"], m["route_name"].lower())
            parts["aliases"][ai] = "gamedata/levels/jpn_mharuna/jpn_mharuna_1.aid"
            f[ai] = aids[(li, mi)]


if __name__ == "__main__":
    os.makedirs(WORK, exist_ok=True)
    out = B.build(B.load_settings(), extra=[add_flat_earth], first_track=FIRST_TRACK)
    print("pack:", out)
    if "--no-root" not in sys.argv:
        print("pack:", build_root())

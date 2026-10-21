"""Flat Earth Track Designer: locations, maps, roads with curves / 90-degree corners / bridges, per-point width and
height, power-up rows and single power-ups, undo / redo, hotkeys, checks, auto-save, one-click build.

Start: "Open road designer.bat". Press F1 (or H) in the window for every key. The world is saved in
map_builder\\tracks\\world.json (auto-save: world.autosave.json). "Build + install" runs flat_earth\\build.py, copies the
packs to _mod_project\\mod_packs and, when Blur is closed, into the game.
"""
import copy
import json
import queue
import math
import os
import shutil
import subprocess
import sys
import threading
import time
import tkinter as tk

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blur_theme  # noqa: E402
blur_theme.install()          # Blur look: themed, rounded widgets everywhere (before any window is made)
import winsound
from tkinter import filedialog, messagebox, simpledialog, ttk

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [os.path.join(HERE, "flat_earth")]
import road as road_mod   # noqa: E402
import world as world_mod  # noqa: E402
import minimap as minimap_mod  # noqa: E402
import road_layout  # noqa: E402
import gen_aid  # noqa: E402
import surface as surface_mod  # noqa: E402
import lighting as lighting_mod  # noqa: E402
sys.path.insert(0, HERE)
from tkinter import colorchooser  # noqa: E402
import view3d  # noqa: E402
import designer_net  # noqa: E402
import designer_surface  # noqa: E402
import designer_share  # noqa: E402

import paths  # noqa: E402
GAME = paths.GAME
AUTOSAVE = os.path.join(paths.TRACKS, "world.autosave.json")
BUILD_TIME_FILE = os.path.join(paths.TRACKS, "last_build_seconds.txt")     # for the build progress bar
BUILD_LOG = os.path.join(paths.LOGS, "last_build.txt")


def signed32(v):
    v = int(v) & 0xFFFFFFFF
    return v - 0x100000000 if v >= 0x80000000 else v


def play_sound(ok):
    """Windows' own success / error sound (asynchronous)"""
    try:
        winsound.PlaySound("SystemAsterisk" if ok else "SystemHand", winsound.SND_ALIAS | winsound.SND_ASYNC)
    except RuntimeError:
        winsound.MessageBeep(winsound.MB_OK if ok else winsound.MB_ICONHAND)


def center_on_screen(win):
    """put a window in the middle of the screen"""
    win.update_idletasks()
    w, h = win.winfo_reqwidth(), win.winfo_reqheight()
    win.geometry("+%d+%d" % (max(0, (win.winfo_screenwidth() - w) // 2), max(0, (win.winfo_screenheight() - h) // 2)))


def bring_to_front(win):
    """put a window in front of ALL windows (also other programs, e.g. the game) and give it the keyboard.
    Windows only lets the foreground program take the focus, so: stay on top + the Alt-key trick +
    SetForegroundWindow."""
    try:
        win.update_idletasks()
        win.deiconify()
        win.lift()
        win.attributes("-topmost", True)
        import ctypes
        user32 = ctypes.windll.user32
        hwnd = user32.GetParent(win.winfo_id()) or win.winfo_id()
        user32.keybd_event(0x12, 0, 0, 0)            # Alt down / up: lets this program take the foreground
        user32.keybd_event(0x12, 0, 2, 0)
        user32.ShowWindow(hwnd, 5)
        user32.SetForegroundWindow(hwnd)
        win.focus_force()
    except Exception:
        pass
UNDO_STEPS = 30
W_PREV, H_PREV = 960, 540
SNAP = 10.0
MIN_RADIUS = 35.0
MIN_LEN, MAX_LEN = 900.0, 5300.0
LINE_STEP = 20.0                         # Start / Finish -/+ buttons (m); drag the lines for any spot
PICKUP_STEP, LANES, MAX_PICKUPS = 150.0, 5, 109
TYPES = list(world_mod.PICKUP_TYPES)    # Shunt, Barge, Shock, Mine, Bolt, Nitro, Shield, Repair
TYPE_COLOURS = {"Shunt": "#ff4040", "Barge": "#ff9f1a", "Shock": "#3aa0ff", "Mine": "#ffe14a", "Bolt": "#b070ff",
                "Nitro": "#40e0ff", "Shield": "#40ff70", "Repair": "#ffffff"}
AUTO_PATTERN = ("Nitro", "Shunt", "Shield", "Barge", "Bolt", "Shock", "Mine", "Repair")
TOOLS = [("select", "Select", "S"), ("move", "Move", "M"), ("add", "Add point", "A"), ("insert", "Insert", "I"),
         ("row", "Power-up row", "R"), ("single", "Single power-up", "P"), ("erase", "Erase", "E"),
         ("branch", "Branch", "B"), ("split", "Lane split", "L"), ("measure", "Measure", "D")]
ICON = {"select": "\u2B09", "move": "\u2725", "add": "\u271A", "insert": "\u2913", "row": "\u2022\u2022\u2022",
        "single": "\u25CF", "erase": "\u2716", "undo": "\u21B6", "redo": "\u21B7", "fit": "\u26F6", "help": "\u2754",
        "save": "\u2714", "build": "\u25B6", "new_map": "\u271A", "new_loc": "\u2302", "dup": "\u29C9",
        "rename": "\u270E", "delete": "\u2716", "left": "\u21B0", "right": "\u21B1", "corner": "\u25E2",
        "reverse": "\u21C4", "mirror": "\u21C5", "auto": "\u26A1", "clear": "\u232B", "map": "\u25B8",
        "loc": "\u25A3", "branch": "\u21B1", "split": "\u2016", "measure": "\u21D4"}
WALL, WALL_GLOW = "#28e6ff", "#0d5566"
UI_FONT = ("Segoe UI Symbol", 9)
BUILD_TEXT = ICON["build"] + " Build + install (Ctrl+B)"
# THEME: deep blues; light colours only for what matters (the selected tab / tool, Build, Play, headings)
# (the Blur look: blur_theme.py - one blue for selected / active, orange only for Build)
BG, PANEL, FG, ACCENT = blur_theme.BG, blur_theme.PANEL, blur_theme.TEXT, blur_theme.ORANGE
BAR, BAR2 = blur_theme.PANEL, blur_theme.PANEL2          # top bar (tabs), tool bar (row 2)
BTN, BTN_HI, SEL = blur_theme.GLASS, blur_theme.GLASS_HI, blur_theme.BLUE   # buttons, hover, the selected tab / tool
PLAY, DANGER, LIST_BG = blur_theme.BLUE, blur_theme.DANGER, blur_theme.FIELD
HEAD, LIGHT = blur_theme.HEAD, "#ffffff"          # headings

CREDITS = """BLUR MAP BUILDER  by MDPRO   -   version %s   (bugs / crash reports: %s on Discord)
Blur (c) 2010 Bizarre Creations / Activision - this is a free fan tool, it needs your own copy of the game.
Amax by the Amax team.   Model reader: BlurImportExport by TimberStalker (github.com/TimberStalker).
Made with Python, numpy, Pillow, PyOpenGL and pyopengltk."""

HELP = """MOUSE
  wheel: zoom      middle-drag or right-drag on empty ground: move the map
TOOLS
  S  Select: click a point / power-up to edit it on the right
  M  Move: drag points and power-ups
  A  Add point at the END of the road        I  Insert a point into the road
  R  Power-up ROW (5 across the road)        P  Single power-up (type = the chosen one)
  E  Erase: click a point or power-up to delete it
  B  BRANCH: click ON the road where it leaves, click its points beside the road, click ON the road where it
     joins again (Esc = cancel). The AI takes it too. Move tool: drag its points / its round handles
  L  LANE SPLIT: click ON the road where it starts, then where it ends: a wall splits the road into 2 lanes
     (3 lanes: on the right). Drag its round handles to move the ends.  Select a branch / split + Delete = remove
  1-8  power-up type: 1 Shunt 2 Barge 3 Shock 4 Mine 5 Bolt 6 Nitro 7 Shield 8 Repair (also changes the selected one)
SELECTED POINT
  W / Q  road wider / narrower here      PgUp / PgDn  higher / lower (bridge, ramp)
  C  corner on/off (sharp turn)          T / Y  90-degree turn LEFT / RIGHT here
     (a corner is never tighter than half the road + 10 m: the inside wall would fold)
  arrows  nudge 5 m (Shift: 1 m)         Delete  delete      Esc  deselect
ROAD
  V  reverse the direction    X  mirror    G  snap to a 10 m grid    F  fit the view
  Auto bridges (right panel): where the road crosses itself the later part rises onto a 9 m deck
WORLD
  Ctrl+N new map    Ctrl+L new location    Ctrl+D duplicate map    F2 rename
  Ctrl+Z undo    Ctrl+Y (or Ctrl+Shift+Z) redo   (30 steps)
  Ctrl+S save    Ctrl+B build + install    F1 / H this help
VIEW
  F3 (or the 2D / 3D / Collision buttons at the top left)  switch views
  3D view (Blender keys): middle-drag turn, Shift+middle-drag move, Ctrl+middle-drag or wheel zoom,
     Alt+left-drag = middle-drag, numpad 7 top / 1 front / 3 side (Ctrl = opposite), 5 flat, 2 4 6 8 turn, Home all
  Collision view: green = drivable ground, red = solid (white ticks = the side that pushes cars away),
     yellow = bridge decks
  The 3D view shows what the build will place (same layout code); it updates after every change
  AI PATH tab: the yellow line = where the AI cars drive, the band = their lane.  "Auto AI path" = made for you
     (the whole road; this is also what you get if you never open the tab).  "Racing line" = cuts the inside of the
     bends.  "Pen" on: drag along the road to draw the line yourself - drawing over a part replaces only that part.
     Pen off: drag moves the map.  AI lane width = how wide the AI's lane is around the line.
  3D OBJECTS: click = select (yellow box + handles, buttons Move / Rotate / Scale / Hide / Reset on top).
     Drag a handle: red = X, green = Y, blue = up; white dot = move on the ground / scale everything;
     rings = rotate (red X / green Y tilt, blue Z turn).  Scale handles stretch the object along its own axes.
     Panel at the bottom left: location / rotation / scale - type a value + Enter.  Orange dots = the road's
     points: click one, drag its arrows (blue = height: a ramp / bridge), or type x / y / height / width.  Keys: G move, R turn (Ctrl = 15 degree steps), S scale (ships, cranes,
     panels...), X or Delete hide.  While moving: X / Y = only along one ground axis, Z = up/down.
     Click / Enter = OK, right-click / Esc = cancel.  Alt+G / Alt+R / Alt+S = back to how it was placed,
     Alt+H = show hidden objects again.  Ctrl+Z undoes.  Save + Build puts the changes in the game."""


class Designer(designer_net.NetTools, designer_surface.SurfaceTools, designer_share.ShareTools):
    def __init__(self, root):
        self.root = root
        root.title("Blur Map Builder by MDPRO %s - Track Designer" % paths.VERSION)
        root.configure(bg=BG)
        self.world = self.load_world()
        self.cur = (0, 0)
        self.sel = None            # selected point index
        self.sel_pick = None       # selected power-up index (in the map's list)
        self.tool = tk.StringVar(value="select")
        self.ptype = tk.StringVar(value="Nitro")
        self.snap = tk.BooleanVar(value=False)
        self.undo, self.redo = [], []
        self.scale, self.ox, self.oz = 0.25, 0.0, 0.0
        self.drag = None
        self.pan = None
        self.building = False
        self.road = None
        self.problems = []
        self.net_init()
        self.surf_init()
        self._ui()
        self._keys()
        self.refresh_tree()
        root.after(80, self.fit)
        root.after(500, lambda: self.write_quickstart(False))     # Blur starts normally unless Play > LAN race

    # ================================================================================================ data
    def load_world(self):
        w = world_mod.load()
        if os.path.exists(AUTOSAVE) and (not os.path.exists(world_mod.WORLD_FILE)
                                         or os.path.getmtime(AUTOSAVE) > os.path.getmtime(world_mod.WORLD_FILE) + 2):
            # the question belongs to the start-up picture (shown on top of it; the main window is still hidden -
            # a question without a visible owner opened behind other programs)
            splash = getattr(self.root, "_splash", None)
            if splash:
                splash.attributes("-topmost", False)      # (on top it covered the question)
            ask = messagebox.askyesno("Auto-save", "There are unsaved changes from last time (auto-save). Open them?",
                                      parent=splash or self.root)
            if ask:
                w = json.load(open(AUTOSAVE))
        return w

    @property
    def loc(self):
        return self.world["locations"][self.cur[0]]

    @property
    def map(self):
        return self.loc["maps"][self.cur[1]]

    def points(self):
        pts = self.map["points"]
        for k, p in enumerate(pts):                 # upgrade [x, z] pairs to dicts
            if not isinstance(p, dict):
                pts[k] = {"x": float(p[0]), "z": float(p[1])}
        return pts

    def make_road(self, m=None):
        m = m or self.map
        return road_mod.for_map(m)

    ROAD_KEYS = ("points", "width", "auto_bridges", "laps")

    def cached_road(self, m):
        """the map's Road, made again only when its shape changed (pan / zoom / select redraw fast)"""
        key = json.dumps([m.get(k) for k in self.ROAD_KEYS], sort_keys=True)
        cache = self.__dict__.setdefault("_road_cache", {})
        hit = cache.get(id(m))
        if hit and hit[0] == key and hit[2] is m:
            return hit[1]
        r = self.make_road(m)
        if len(cache) > 32:
            cache.clear()
        cache[id(m)] = (key, r, m)
        return r

    def change(self):
        """call BEFORE modifying the world: remembers the old state for undo"""
        self.undo.append(json.dumps(self.world))
        del self.undo[:-UNDO_STEPS]
        self.redo.clear()

    def changed(self):
        """call AFTER modifying: redraw + auto-save"""
        try:
            json.dump(self.world, open(AUTOSAVE, "w"))
        except OSError:
            pass
        self.redraw()
        if self.view_mode.get() == "light":
            self.light_build()

    def do_undo(self, e=None):
        if self.undo:
            self.redo.append(json.dumps(self.world))
            self.world = json.loads(self.undo.pop())
            self._clamp_cur()
            self.refresh_tree()
            self.changed()
            self.say("Undo (%d more)" % len(self.undo))

    def do_redo(self, e=None):
        if self.redo:
            self.undo.append(json.dumps(self.world))
            self.world = json.loads(self.redo.pop())
            self._clamp_cur()
            self.refresh_tree()
            self.changed()
            self.say("Redo (%d more)" % len(self.redo))

    def _clamp_cur(self):
        li = min(self.cur[0], len(self.world["locations"]) - 1)
        mi = min(self.cur[1], len(self.world["locations"][li]["maps"]) - 1)
        self.cur = (li, mi)
        if self.sel is not None and self.sel >= len(self.map["points"]):
            self.sel = None
        self.sel_pick = None

    # ================================================================================================ UI
    def _ui(self):
        r = self.root
        bar = tk.Frame(r, bg=BAR)
        bar.pack(side="top", fill="x")
        # right side FIRST: Save / Play / Build + install always keep their place, however narrow the window
        self.build_btn = tk.Button(bar, text=BUILD_TEXT, command=self.build, bg=ACCENT, fg="white", relief="flat",
                                   activebackground="#2fd67a", font=("Segoe UI Symbol", 10, "bold"), padx=10)
        self.build_btn.pack(side="right", padx=(2, 6), pady=4)
        tk.Button(bar, text="\u25B6 Play (F5)", command=lambda: self.play_dialog(), bg=PLAY, fg="white", relief="flat",
                  activebackground="#45b8f0", font=("Segoe UI Symbol", 10, "bold"), padx=10).pack(side="right", padx=2, pady=4)
        tk.Button(bar, text=ICON["save"] + " Save (Ctrl+S)", command=self.save, bg=BTN, fg=FG, relief="flat",
                  activebackground=BTN_HI, font=UI_FONT, padx=8).pack(side="right", padx=2, pady=4)
        self.view_mode = tk.StringVar(value="2d")
        for val, txt in (("2d", "\u25A6 2D map"), ("3d", "\u25B2 3D"), ("collision", "\u26A0 Collision"),
                         ("ai", "\u27BF AI path"), ("light", "\u2600 Lighting"), ("surface", "\u25A9 Surface")):
            tk.Radiobutton(bar, text=txt, variable=self.view_mode, value=val, indicatoron=False, bg=BTN,
                           fg="white", selectcolor=SEL, activebackground=BTN_HI, relief="flat", padx=12, pady=4,
                           command=self.on_view, font=("Segoe UI Symbol", 10, "bold")).pack(side="left", padx=1, pady=4)
        # row 2: one tool bar per tab (on_view shows the right one)
        self.toolrow = tk.Frame(r, bg=BAR2)
        self.toolrow.pack(side="top", fill="x")
        self.bars = {}

        def group(parent, title):
            g = tk.Frame(parent, bg=BAR2)
            g.pack(side="left", padx=(6, 4), pady=3)
            tk.Label(g, text=title, bg=BAR2, fg=HEAD, font=("Segoe UI", 7, "bold")).pack(side="top", anchor="w")
            row = tk.Frame(g, bg=BAR2)
            row.pack(side="top")
            return row

        def tool_btn(parent, key, label, hk):
            b = tk.Radiobutton(parent, text="%s %s (%s)" % (ICON[key], label, hk), variable=self.tool, value=key,
                               indicatoron=False, bg=BTN, fg=FG, selectcolor=SEL, activebackground=BTN_HI,
                               relief="flat", padx=7, pady=3, command=self.on_tool, font=UI_FONT)
            b.pack(side="left", padx=1)
            self.tool_buttons[key] = b

        def common(parent):
            """Undo / Redo / Fit / Help (right side of every tool bar)"""
            for txt, cmd in ((ICON["help"] + " Help", self.help), (ICON["fit"] + " Fit", self.fit),
                             (ICON["redo"] + " Redo (Ctrl+Y)", self.do_redo),
                             (ICON["undo"] + " Undo (Ctrl+Z)", self.do_undo)):
                tk.Button(parent, text=txt, command=cmd, bg=BTN, fg=FG, relief="flat", activebackground=BTN_HI,
                          font=UI_FONT, padx=6).pack(side="right", padx=1, pady=6)
        self.tool_buttons = {}
        tools = {key: (label, hk) for key, label, hk in TOOLS}
        # 2D map
        b2 = tk.Frame(self.toolrow, bg=BAR2)
        common(b2)
        tk.Checkbutton(b2, text="Snap (G)", variable=self.snap, bg=BAR2, fg=FG, selectcolor=BTN,
                       activebackground=BAR2, activeforeground=FG).pack(side="right", padx=6)
        g = group(b2, "ROAD")
        for key in ("select", "move", "add", "insert", "erase"):
            tool_btn(g, key, *tools[key])
        g = group(b2, "BRANCHES & LANES")
        for key in ("branch", "split"):
            tool_btn(g, key, *tools[key])
        g = group(b2, "MEASURE")
        tool_btn(g, "measure", *tools["measure"])
        g = group(b2, "POWER-UPS")
        for key in ("row", "single"):
            tool_btn(g, key, *tools[key])
        tk.Label(g, text=" type", bg=BAR2, fg=FG).pack(side="left")
        self.type_menu = tk.OptionMenu(g, self.ptype, *TYPES, command=lambda v: self.set_type(v))
        self.type_menu.config(bg=BTN, fg=FG, activebackground=BTN_HI, highlightthickness=0, relief="flat")
        self.type_menu.pack(side="left", padx=2)
        self.bars["2d"] = b2
        # AI path: its own tools (+ branches / lanes, which work there too)
        self.ai_bar = tk.Frame(self.toolrow, bg=BAR2)
        common(self.ai_bar)
        self.bars["ai"] = self.ai_bar
        # 3D / Collision / Lighting: what you can do there
        for mode, text in (("3d", "3D VIEW - click an object: move / turn / scale it (buttons appear on top);  middle-drag "
                                  "turn, Shift+middle move, wheel zoom, numpad 7 / 1 / 3 views, Home = all"),
                           ("collision", "COLLISION - green = drivable, red = solid walls (white ticks = the side that "
                                         "pushes cars away), yellow = bridge decks;  same mouse as the 3D view"),
                           ("light", "LIGHTING of the whole location - pick a city as the start, change what you like, "
                                     "then Build + install")):
            f = tk.Frame(self.toolrow, bg=BAR2)
            common(f)
            tk.Label(f, text=text, bg=BAR2, fg=HEAD, font=("Segoe UI", 9), anchor="w").pack(side="left", padx=8, pady=8)
            self.bars[mode] = f
        sf = self.surf_bar(self.toolrow, BTN, BTN_HI, SEL, FG, HEAD, UI_FONT)     # SURFACE tab: brush + road type
        common(sf)
        self.bars["surface"] = sf
        self.bars["2d"].pack(fill="x")

        body = tk.Frame(r, bg=BG)
        body.pack(fill="both", expand=True)
        # left: locations + maps
        left = tk.Frame(body, bg=PANEL, width=210)
        left.pack(side="left", fill="y")
        left.pack_propagate(False)
        tk.Label(left, text="LOCATIONS & MAPS", bg=PANEL, fg=HEAD, font=("Segoe UI", 9, "bold")).pack(anchor="w", padx=6, pady=(6, 2))
        self.tree = tk.Listbox(left, bg=LIST_BG, fg=FG, selectbackground=SEL, activestyle="none",
                               font=("Segoe UI Symbol", 10), highlightthickness=0, borderwidth=0)
        self.tree.pack(fill="both", expand=True, padx=6)
        self.tree.bind("<<ListboxSelect>>", self.on_tree)
        for txt, cmd, col in ((ICON["new_map"] + "  New map (Ctrl+N)", self.new_map, BTN),
                              (ICON["new_loc"] + "  New location (Ctrl+L)", self.new_location, BTN),
                              (ICON["dup"] + "  Duplicate map (Ctrl+D)", self.dup_map, BTN),
                              (ICON["rename"] + "  Rename (F2)", self.rename, BTN),
                              (ICON["delete"] + "  Delete map", self.delete_map, DANGER),
                              (ICON["delete"] + "  Delete location", self.delete_location, DANGER),
                              ("⇩  Import map (.blurmap)", self.import_map_ui, BTN),
                              ("⇧  Export this map", self.export_map_ui, BTN)):
            tk.Button(left, text=txt, command=cmd, bg=col, fg=FG, anchor="w", font=UI_FONT).pack(fill="x", padx=6, pady=1)
        tk.Label(left, text="", bg=PANEL).pack(pady=2)
        # right: properties, checks, stats
        right = tk.Frame(body, bg=PANEL, width=320)
        right.pack(side="right", fill="y")
        right.pack_propagate(False)
        self.props = tk.Frame(right, bg=PANEL)
        self.props.pack(fill="x", padx=6, pady=6)
        tk.Label(right, text="CHECKS", bg=PANEL, fg=HEAD, font=("Segoe UI", 9, "bold")).pack(anchor="w", padx=6)
        self.checks = tk.Text(right, height=12, bg=LIST_BG, fg=FG, font=("Consolas", 9), wrap="word",
                              borderwidth=0, highlightthickness=0)
        self.checks.pack(fill="both", expand=True, padx=6, pady=(0, 6))
        self.checks.tag_config("ok", foreground="#58d06a")
        self.checks.tag_config("bad", foreground="#ff6060")
        self.checks.tag_config("warn", foreground="#ffd23b")
        self.checks.tag_config("info", foreground="#9aa8b8")
        # canvas
        self.c = tk.Canvas(body, bg=BG, highlightthickness=0, width=980, height=720)
        self.c.pack(fill="both", expand=True)
        self.body = body
        # AI path tab: its tool bar (shown above the map in that tab)
        self.ai_pen = tk.BooleanVar(value=False)
        self.ai_lane = tk.DoubleVar(value=road_layout.AI_LANE)
        self.ai_stroke = None
        tk.Label(self.ai_bar, text=" AI PATH ", bg=BAR2, fg="#ffe27a", font=("Segoe UI", 9, "bold")).pack(side="left")
        for txt, cmd in (("⟳ Auto AI path", self.ai_auto), ("\U0001F3C1 Racing line", self.ai_racing)):
            tk.Button(self.ai_bar, text=txt, command=cmd, bg=BTN, fg="white",
                      font=("Segoe UI Symbol", 9)).pack(side="left", padx=2, pady=2)
        tk.Checkbutton(self.ai_bar, text="✎ Pen (draw)", variable=self.ai_pen, indicatoron=False, bg=BTN,
                       fg="white", selectcolor="#c08a1a", activebackground=BTN_HI, padx=8, pady=2,
                       font=("Segoe UI Symbol", 9, "bold"),
                       command=lambda: self.say("Pen: drag along the road to draw where the AI should drive" if
                                                self.ai_pen.get() else "Pen off: drag = move the map")).pack(side="left", padx=6)
        tk.Label(self.ai_bar, text="AI lane width (m):", bg=BAR2, fg=FG).pack(side="left", padx=(10, 2))
        sp = tk.Spinbox(self.ai_bar, from_=6, to=40, increment=2, width=4, textvariable=self.ai_lane,
                        command=self.ai_lane_changed)
        sp.bind("<Return>", lambda e: self.ai_lane_changed())
        sp.pack(side="left")
        for key in ("branch", "split"):                    # these two work on the AI tab too
            label, hk = {k: (l, h) for k, l, h in TOOLS}[key]
            tk.Radiobutton(self.ai_bar, text="%s %s (%s)" % (ICON[key], label, hk), variable=self.tool, value=key,
                           indicatoron=False, bg=BTN, fg=FG, selectcolor=SEL, activebackground=BTN_HI, relief="flat",
                           padx=7, pady=3, command=self.on_tool, font=UI_FONT).pack(side="left", padx=(8 if key == "branch" else 1, 1))
        self.ai_state = tk.Label(self.ai_bar, text="", bg=BAR2, fg="#9aa8b8")
        self.ai_state.pack(side="left", padx=10)
        self.light_panel = tk.Frame(body, bg=BG)                           # LIGHTING tab (light_build)
        self.v3d = view3d.View3D(body, bg=BG, fg=FG, panel=PANEL)        # shown by the 3D button / F3
        self.v3d.on_edit = self.on_3d_edit
        self.v3d.on_point = self.on_3d_point
        self.v3d.on_pickups = self.on_3d_pickups
        self.v3d.on_objects = self.on_3d_objects
        self.v3d.on_start_cams = self.on_3d_start_cams
        self._v3d_job = None
        self.status = tk.Label(r, anchor="w", bg=PANEL, fg=FG)
        self.status.pack(side="bottom", fill="x")
        c = self.c
        c.bind("<Configure>", self.on_canvas_size)
        c.bind("<MouseWheel>", self.on_wheel)
        c.bind("<ButtonPress-1>", self.on_down)
        c.bind("<B1-Motion>", self.on_move)
        c.bind("<ButtonRelease-1>", self.on_up)
        c.bind("<ButtonPress-2>", self.pan_start)
        c.bind("<B2-Motion>", self.pan_move)
        c.bind("<ButtonPress-3>", self.on_right)
        c.bind("<B3-Motion>", lambda e: self.on_move(e) if self.surf_stroke else self.pan_move(e))
        c.bind("<ButtonRelease-3>", lambda e: self.surf_up() if self.surf_stroke else None)
        c.bind("<Motion>", self.on_hover)

    def _keys(self):
        r = self.root

        def k(seq, fn, typing_ok=False):
            def h(e):
                if not typing_ok and isinstance(r.focus_get(), (tk.Entry, tk.Text, tk.Spinbox)):
                    return None
                if not typing_ok and self.view_mode.get() not in ("2d", "ai", "surface"):
                    return None          # 3D / Collision view: its own keys (G R S X ...), the road stays as it is
                fn(e)
                return "break"
            r.bind(seq, h)
        for key, _, hk in TOOLS:
            k("<%s>" % hk.lower(), lambda e, key=key: self.set_tool(key))
            k("<%s>" % hk, lambda e, key=key: self.set_tool(key))
        for n, t in enumerate(TYPES):
            k("<Key-%d>" % (n + 1), lambda e, t=t: self.set_type(t))
        k("<w>", lambda e: self.adj("w", +5))
        k("<q>", lambda e: self.adj("w", -5))
        k("<Prior>", lambda e: self.adj("h", +1))
        k("<Next>", lambda e: self.adj("h", -1))
        k("<c>", lambda e: self.toggle_corner())
        k("<t>", lambda e: self.turn90(1))
        k("<y>", lambda e: self.turn90(-1))
        k("<v>", lambda e: self.reverse())
        k("<x>", lambda e: self.mirror())
        k("<g>", lambda e: (self.snap.set(not self.snap.get()), self.say("Snap %s" % ("on" if self.snap.get() else "off"))))
        k("<f>", lambda e: self.fit())
        k("<h>", lambda e: self.help())
        k("<F1>", lambda e: self.help(), True)
        k("<F2>", lambda e: self.rename(), True)
        k("<F3>", lambda e: self.toggle_view(), True)
        k("<Delete>", lambda e: self.delete_selected())
        k("<BackSpace>", lambda e: self.delete_selected())
        k("<Escape>", lambda e: self.deselect(), True)
        k("<Return>", lambda e: self.measure_finish())
        for key, dx, dz in (("Left", -1, 0), ("Right", 1, 0), ("Up", 0, 1), ("Down", 0, -1)):
            k("<%s>" % key, lambda e, dx=dx, dz=dz: self.nudge(dx * 5, dz * 5))
            k("<Shift-%s>" % key, lambda e, dx=dx, dz=dz: self.nudge(dx, dz))
        k("<Control-z>", self.do_undo, True)
        k("<Control-y>", self.do_redo, True)
        k("<Control-Y>", self.do_redo, True)
        # capital Z = Shift (Ctrl+Shift+Z = redo) OR Caps Lock on (then Ctrl+Z must still undo)
        k("<Control-Z>", lambda e: self.do_redo() if e.state & 0x1 else self.do_undo(), True)
        k("<Control-s>", lambda e: self.save(), True)
        k("<Control-b>", lambda e: self.build(), True)
        k("<F5>", lambda e: self.play_dialog(), True)
        k("<Control-n>", lambda e: self.new_map(), True)
        k("<Control-l>", lambda e: self.new_location(), True)
        k("<Control-d>", lambda e: self.dup_map(), True)

    # ================================================================================================ 2D / 3D
    def toggle_view(self):
        order = ["2d", "3d", "collision", "ai", "light", "surface"]
        self.view_mode.set(order[(order.index(self.view_mode.get()) + 1) % len(order)])
        self.on_view()

    def show_toolbar(self):
        """row 2 = only the tools of the current tab"""
        for f in self.bars.values():
            f.pack_forget()
        self.bars.get(self.view_mode.get(), self.bars["2d"]).pack(fill="x")

    def on_view(self):
        self.show_toolbar()
        if self.view_mode.get() == "light":
            self.v3d.show(False)
            self.v3d.pack_forget()
            self.c.pack_forget()
            self.light_panel.pack(fill="both", expand=True)
            self.light_build()
            self.say("LIGHTING of the location '%s': pick a game city as the start, change what you like, Build + "
                     "install, look in game" % self.loc["name"])
            return
        self.light_panel.pack_forget()
        if self.view_mode.get() == "surface":
            self.v3d.show(False)
            self.v3d.pack_forget()
            self.c.pack(fill="both", expand=True)
            self.surf_refresh()
            self.redraw()
            self.say("SURFACE: left drag = paint %s, right drag = remove, middle drag = move the map, wheel = zoom.  "
                     "Whole road: type / sides / varied asphalt on the bar" % surface_mod.SURFACES[self.surf_kind.get()][0])
            return
        if self.view_mode.get() == "ai":
            self.v3d.show(False)
            self.v3d.pack_forget()
            self.c.pack_forget()
            self.c.pack(fill="both", expand=True)
            self.ai_refresh_bar()
            self.redraw()
            self.say("AI PATH: the yellow line is where the AI cars drive (the band = their lane). Auto = made for "
                     "you; Pen = draw it yourself (drag on the road; drawing over a part replaces that part)")
            return
        if self.view_mode.get() in ("3d", "collision"):
            self.c.pack_forget()
            self.v3d.pack(fill="both", expand=True)
            self.update_3d(now=True)
            self.v3d.show(True)
            self.say("%s: middle-drag turn, Shift+middle move, wheel zoom, numpad 7/1/3 views, 5 flat, Home all  "
                     "(F3 = next view)" % ("COLLISION view" if self.view_mode.get() == "collision" else "3D view"))
        else:
            self.v3d.show(False)
            self.v3d.pack_forget()
            self.c.pack(fill="both", expand=True)
            self.redraw()
            self.on_tool()

    def on_3d_edit(self, edits):
        """the 3D view moved / turned / scaled / hid an object: store it in the map (undo, auto-save)"""
        self.change()
        if edits:
            self.map["edits"] = edits
        else:
            self.map.pop("edits", None)
        self.changed()
        self.update_3d(now=True)
        n = len(edits)
        self.say("%d object change(s) in this map - Ctrl+Z undoes, Save + Build puts them in the game" % n)

    def on_3d_point(self, i, values):
        """the 3D view moved a track point (x / z / h height) or changed its width (w)"""
        pts = self.points()
        if not 0 <= i < len(pts):
            return
        self.change()
        pts[i].update(values)
        self.sel = i
        self.changed()
        self.update_3d(now=True)
        self.say("Track point %d: x %.0f  z %.0f  height %.1f m  width %.0f m - Ctrl+Z undoes" % (
            i + 1, pts[i]["x"], pts[i]["z"], pts[i].get("h") or 0.0, pts[i].get("w") or self.map.get("width", 60)))

    def on_3d_objects(self, objects, edits):
        """the 3D view placed / deleted a library object: the map's object list + edits (undo, auto-save)"""
        self.change()
        for key, value in (("objects", objects), ("edits", edits)):
            if value:
                self.map[key] = value
            else:
                self.map.pop(key, None)
        self.changed()
        self.update_3d(now=True)
        self.say("%d placed object(s) in this map - Ctrl+Z undoes, Build + install puts them in the game"
                 % len(objects))

    def on_3d_pickups(self, items):
        """the 3D view moved / retyped / deleted a power-up: the map gets the whole list (automatic rows -> a list)"""
        was_auto = self.map.get("pickups") is None
        self.change()
        self.map["pickups"] = items
        self.sel_pick = None
        self.changed()
        self.update_3d(now=True)
        self.say("Power-ups: %d rows / singles%s - Ctrl+Z undoes" % (
            len(items), " (the automatic rows are now your own list)" if was_auto else ""))

    def on_3d_start_cams(self, cams):
        """the 3D view's START CAMERA views changed (flat_earth/intro_cam.py; no views = the automatic shots)"""
        self.change()
        if cams:
            self.map["start_cams"] = cams
        else:
            self.map.pop("start_cams", None)
        self.changed()
        self.say("Start camera: %s - Build + install to see it in game, Ctrl+Z undoes" % ", ".join(
            "%s %d view(s)" % (k, len(v)) for k, v in cams.items()) if cams else
            "Start camera: automatic - Build + install to see it in game")

    # ================================================================================================ AI path
    def ai_refresh_bar(self):
        p = self.map.get("ai_path") or {}
        own = p.get("mode") == "drawn"
        if own:
            self.ai_lane.set(p.get("lane", road_layout.AI_LANE))
        self.ai_state.config(text="now: YOUR OWN line (drawn / racing line)" if own else
                             "now: AUTOMATIC (the whole road; made at every build)", fg="#f0e080" if own else "#9aa8b8")

    def ai_auto(self):
        if (self.map.get("ai_path") or {}).get("mode") != "drawn":
            self.say("The AI path is already automatic")
            return
        self.change()
        self.map.pop("ai_path", None)
        self.changed()
        self.ai_refresh_bar()
        self.say("AI path: automatic again (the AI uses the whole road) - Ctrl+Z undoes")

    def ai_racing(self):
        if self.road is None:
            return
        self.change()
        lane = float(self.ai_lane.get())
        self.map["ai_path"] = {"mode": "drawn", "lane": lane, "points": road_layout.racing_line(self.road, lane)}
        self.changed()
        self.ai_refresh_bar()
        self.say("Racing line made: the AI cuts the inside of the bends. Use the Pen to change any part of it.")

    def ai_lane_changed(self):
        p = self.map.get("ai_path") or {}
        try:
            lane = max(6.0, min(40.0, float(self.ai_lane.get())))
        except (tk.TclError, ValueError):
            return
        if p.get("mode") == "drawn" and abs(p.get("lane", road_layout.AI_LANE) - lane) > 1e-6:
            self.change()
            p["lane"] = lane
            self.changed()

    def ai_pen_down(self, e):
        self.ai_stroke = [self.to_world(e.x, e.y)]

    def ai_pen_move(self, e):
        if self.ai_stroke is None:
            return
        x, z = self.to_world(e.x, e.y)
        lx, lz = self.ai_stroke[-1]
        if math.hypot(x - lx, z - lz) >= 2.0:
            self.ai_stroke.append((x, z))
            a, b = self.to_screen(lx, lz), self.to_screen(x, z)
            self.c.create_line(*a, *b, fill="#ff9f1a", width=3, capstyle="round", tags="stroke")

    def ai_pen_up(self, e):
        stroke, self.ai_stroke = self.ai_stroke, None
        if not stroke or len(stroke) < 3 or self.road is None:
            self.redraw()
            return
        self.change()
        lane = float(self.ai_lane.get())
        pts = road_layout.merge_ai_stroke(self.road, self.map, stroke)
        self.map["ai_path"] = {"mode": "drawn", "lane": lane, "points": pts}
        self.changed()
        self.ai_refresh_bar()
        self.say("AI path drawn (this part replaced) - Ctrl+Z undoes, Auto AI path = back to automatic")

    def draw_ai_path(self, r):
        """AI path tab: the AI's lane (band) and its middle line, as the build will write it"""
        c = self.c
        self.ai_refresh_bar()
        try:
            os.environ.setdefault("FE_DRY", "1")
            import build as fe
            import gen_aid
            _, info = gen_aid.build_path(r, fe.GROUND_Y, lines=gen_aid.race_lines(r, self.map))
            cor = road_layout.ai_corridor(fe, r, info, self.map)
        except Exception as ex:                  # noqa: BLE001 - shown
            c.create_text(10, 30, anchor="nw", fill="#ff6060", text="AI path: %s" % ex)
            return
        lo = [r.at(r.dist[i], cor[i][0])[:2] for i in range(len(r))]
        hi = [r.at(r.dist[i], cor[i][1])[:2] for i in range(len(r))]
        mid = [r.at(r.dist[i], (cor[i][0] + cor[i][1]) / 2)[:2] for i in range(len(r))]
        for i in range(0, len(r) - 1, 2):
            j = min(i + 2, len(r) - 1)
            poly = []
            for q in (lo[i], lo[j], hi[j], hi[i]):
                poly += self.to_screen(*q)
            c.create_polygon(*poly, fill="#3d4a24", outline="", stipple="gray50")
        for line in (lo, hi):
            flat = []
            for q in line[::2] + [line[-1]]:
                flat += self.to_screen(*q)
            c.create_line(*flat, fill="#8a9a40", width=1)
        flat = []
        for q in mid[::2] + [mid[-1]]:
            flat += self.to_screen(*q)
        c.create_line(*flat, fill="#ffe14a", width=3, arrow="last", arrowshape=(12, 14, 5))

    def update_3d(self, now=False):
        """rebuild the 3D scene of the current map (after changes: once things are quiet for 0.4 s)"""
        if self.view_mode.get() not in ("3d", "collision"):
            return
        if self._v3d_job:
            self.root.after_cancel(self._v3d_job)
            self._v3d_job = None
        if now:
            self.v3d.set_map(self.map, self.view_mode.get())
        else:
            self._v3d_job = self.root.after(400, lambda: self.update_3d(now=True))

    def say(self, text, colour=FG):
        self.status.config(text=text, fg=colour)

    def set_tool(self, key):
        self.tool.set(key)
        self.on_tool()

    def on_tool(self):
        self.net_draft = None
        if self.tool.get() != "measure":
            self.measure = None
        hints = {"select": "click a point or power-up to edit it on the right",
                 "move": "drag points and power-ups",
                 "add": "click to add a point at the end of the road",
                 "insert": "click near the road to insert a point there (near a branch: into the branch)",
                 "row": "click on the road (or a branch) to place a row of power-ups",
                 "single": "click to place one %s power-up (1-8 = type)" % self.ptype.get(),
                 "erase": "click a point or power-up to delete it",
                 "branch": "click ON the road where the branch leaves, then its points beside the road, then ON the "
                           "road where it joins again",
                 "split": "click ON the road where the lane split starts, then where it ends",
                 "measure": "click A, then B: the straight distance and, on the same road, the driving distance"}
        self.say("%s: %s" % (dict((t[0], t[1]) for t in TOOLS)[self.tool.get()], hints[self.tool.get()]))

    # ================================================================================================ tree
    def refresh_tree(self):
        self.tree.delete(0, "end")
        self.tree_rows = []
        for li, loc in enumerate(self.world["locations"]):
            self.tree.insert("end", "%s %s" % (ICON["loc"], loc["name"]))
            self.tree.itemconfig("end", fg=HEAD)
            self.tree_rows.append((li, None))
            for mi, m in enumerate(loc["maps"]):
                self.tree.insert("end", "     %s %s" % (ICON["map"], m["name"]))
                self.tree_rows.append((li, mi))
        for row, (li, mi) in enumerate(self.tree_rows):
            if (li, mi) == self.cur:
                self.tree.selection_clear(0, "end")
                self.tree.selection_set(row)

    def on_tree(self, e=None):
        self.net_clear_sel()
        self.net_draft = None
        self.root.after(10, self.surf_refresh)
        sel = self.tree.curselection()
        if not sel:
            return
        li, mi = self.tree_rows[sel[0]]
        if mi is None:
            mi = 0
        if (li, mi) != self.cur:
            self.cur = (li, mi)
            self.sel = self.sel_pick = None
            self.refresh_tree()
            self.fit()
            if self.view_mode.get() == "light":
                self.light_build()

    def total_maps(self):
        return sum(len(l["maps"]) for l in self.world["locations"])

    def _free_spot(self, loc):
        """a straight 1500 m road away from the location's other roads"""
        zs = [p["z"] if isinstance(p, dict) else p[1] for m in loc["maps"] for p in m["points"]]
        xs = [p["x"] if isinstance(p, dict) else p[0] for m in loc["maps"] for p in m["points"]]
        if not zs:
            return [{"x": 0.0, "z": 0.0}, {"x": 1500.0, "z": 0.0}]
        z = max(zs) + 400
        x = min(xs)
        return [{"x": x, "z": z}, {"x": x + 1500, "z": z}]

    def new_map(self):
        if self.total_maps() >= world_mod.MAX_MAPS:
            messagebox.showwarning("Too many maps", "At most %d maps in total." % world_mod.MAX_MAPS)
            return
        name = simpledialog.askstring("New map", "Name of the new map:", parent=self.root)
        if not name:
            return
        self.change()
        loc = self.loc
        loc["maps"].append({"id": world_mod.slug(name), "name": name, "width": 60.0, "auto_bridges": True,
                            "obstacles": False, "points": self._free_spot(loc),
                            "scenery": dict(road_layout.NEW_MAP_SCENERY)})
        self.cur = (self.cur[0], len(loc["maps"]) - 1)
        self.sel = self.sel_pick = None
        self.refresh_tree()
        self.changed()
        self.fit()

    def new_location(self):
        if self.total_maps() >= world_mod.MAX_MAPS:
            messagebox.showwarning("Too many maps", "At most %d maps in total." % world_mod.MAX_MAPS)
            return
        name = simpledialog.askstring("New location", "Name of the new location (a new world):", parent=self.root)
        if not name:
            return
        ids = {l["id"] for l in self.world["locations"]}
        lid = world_mod.slug(name)
        while lid in ids or lid in ("flat_earth",):
            lid += "_2"
        self.change()
        self.world["locations"].append({"id": lid, "name": name, "maps": [
            {"id": lid + "_1", "name": "%s 1" % name, "width": 60.0, "auto_bridges": True, "obstacles": False,
             "points": [{"x": 0.0, "z": 0.0}, {"x": 1500.0, "z": 0.0}],
             "scenery": dict(road_layout.NEW_MAP_SCENERY)}]})
        self.cur = (len(self.world["locations"]) - 1, 0)
        self.sel = self.sel_pick = None
        self.refresh_tree()
        self.changed()
        self.fit()

    def dup_map(self):
        if self.total_maps() >= world_mod.MAX_MAPS:
            messagebox.showwarning("Too many maps", "At most %d maps in total." % world_mod.MAX_MAPS)
            return
        self.change()
        m = copy.deepcopy(self.map)
        m["name"] += " copy"
        m["id"] = world_mod.slug(m["name"])
        for k in [k for k in m if k in ("route_name", "route_uid", "loc_id", "list_uids")]:
            del m[k]
        for p in self.points_of(m):
            p["z"] += 300
        for it in m.get("pickups") or []:
            it["z"] += 300
        self.loc["maps"].append(m)
        self.cur = (self.cur[0], len(self.loc["maps"]) - 1)
        self.refresh_tree()
        self.changed()
        self.fit()

    def points_of(self, m):
        for k, p in enumerate(m["points"]):
            if not isinstance(p, dict):
                m["points"][k] = {"x": float(p[0]), "z": float(p[1])}
        return m["points"]

    def rename(self):
        li, mi = self.cur
        what = simpledialog.askstring("Rename", "1 = this map (%s)\n2 = this location (%s)" % (self.map["name"], self.loc["name"]),
                                      initialvalue="1", parent=self.root)
        if what not in ("1", "2"):
            return
        target = self.map if what == "1" else self.loc
        name = simpledialog.askstring("Rename", "New name:", initialvalue=target["name"], parent=self.root)
        if name:
            self.change()
            target["name"] = name
            self.refresh_tree()
            self.changed()

    def delete_map(self):
        if len(self.loc["maps"]) <= 1:
            messagebox.showinfo("Delete map", "A location needs at least one map (delete the location instead).")
            return
        if self.cur == (0, 0):
            messagebox.showinfo("Delete map", "The first map of the first location can't be deleted (it holds the "
                                              "original Flat Earth ids) - change its road instead.")
            return
        if messagebox.askyesno("Delete map", "Delete the map '%s'?" % self.map["name"]):
            self.change()
            del self.loc["maps"][self.cur[1]]
            self.cur = (self.cur[0], 0)
            self.refresh_tree()
            self.changed()
            self.fit()
            self.say("Map deleted here - press 'Build + install' to remove it from the game too", "#ffd23b")

    def delete_location(self):
        if len(self.world["locations"]) <= 1:
            messagebox.showinfo("Delete location", "This is the only location - make another one first.")
            return
        extra = ""
        if self.cur[0] == 0:             # the first location has the game's fixed slot (folder flat_earth, ids
            #                              FFFFFF20 / 21): the next one takes it over at the next build
            extra = ("\n\nIt is the first location: '%s' becomes the first one and takes over its place in the game."
                     % self.world["locations"][1]["name"])
        if messagebox.askyesno("Delete location", "Delete the location '%s' and its %d map(s)?%s"
                               % (self.loc["name"], len(self.loc["maps"]), extra)):
            self.change()
            del self.world["locations"][self.cur[0]]
            self.cur = (0, 0)
            self.refresh_tree()
            self.changed()
            self.fit()
            self.say("Location deleted here - press 'Build + install' to remove it from the game too", "#ffd23b")

    # ================================================================================================ view
    def on_canvas_size(self, e):
        self._cwh = (e.width, e.height)
        self.redraw()

    def canvas_size(self):
        """the 2D canvas' size (kept from its last resize: asking Tk for it thousands of times a redraw was slow)"""
        wh = getattr(self, "_cwh", None)
        return wh if wh else (self.c.winfo_width(), self.c.winfo_height())

    def to_screen(self, x, z):
        w, h = self.canvas_size()
        return w / 2 + (x - self.ox) * self.scale, h / 2 - (z - self.oz) * self.scale

    def to_world(self, sx, sy):
        w, h = self.canvas_size()
        return self.ox + (sx - w / 2) / self.scale, self.oz - (sy - h / 2) / self.scale

    def fit(self):
        pts = self.points()
        xs = [p["x"] for p in pts]
        zs = [p["z"] for p in pts]
        self.ox, self.oz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
        w, h = self.canvas_size()
        w, h = max(w, 200), max(h, 200)
        self.scale = 0.8 * min(w / (max(xs) - min(xs) + 500), h / (max(zs) - min(zs) + 500))
        self.redraw()

    def on_wheel(self, e):
        x, z = self.to_world(e.x, e.y)
        self.scale *= 1.2 if e.delta > 0 else 1 / 1.2
        nx, nz = self.to_world(e.x, e.y)
        self.ox += x - nx
        self.oz += z - nz
        self.redraw_soon()

    def pan_start(self, e):
        self.pan = (e.x, e.y, self.ox, self.oz)

    def pan_move(self, e):
        if self.pan:
            x0, y0, ox, oz = self.pan
            self.ox = ox - (e.x - x0) / self.scale
            self.oz = oz + (e.y - y0) / self.scale
            self.redraw_soon()

    # ================================================================================================ power-ups
    def pickup_items(self, create=False):
        """the map's power-up list (None = automatic rows); create=True turns the automatic rows into a list"""
        m = self.map
        if m.get("pickups") is None and create:
            m["pickups"] = self.auto_rows(self.road or self.make_road())
        return m.get("pickups")

    def auto_rows(self, r):
        """the automatic rows (as the build makes them: road_layout.auto_pickups) - main road + branches"""
        import network
        si, fi = gen_aid.race_lines(r, self.map)
        s0, s1 = r.dist[si], r.dist[fi]
        if r.closed:                    # a lap track: rows all the way round
            s1 = s0 + r.length
        return network.get(self.map, r).auto_rows(s0, s1, road_layout.pickup_step(self.map),
                                                  MAX_PICKUPS // road_layout.row_size(self.map))

    def pickup_dots(self, r):
        """[(item index, x, z, type)] for drawing"""
        items = self.map.get("pickups")
        auto = items is None
        if auto:
            items = self.auto_rows(r)
        import network
        net = network.get(self.map, r)
        main = r
        dots, k = [], 0
        for n, it in enumerate(items):
            r = net.pickup_road(it) if it.get("bi") is not None else main     # a row on a branch
            s, _ = r.project(it["x"], it["z"])
            if r is main and it.get("kind") != "single":
                s = net.clear_of_splits(s)                # rows never stand in a lane split
            if it.get("kind") == "single":
                dots.append((None if auto else n, it["x"], it["z"], it.get("type") or AUTO_PATTERN[k % 8]))
                k += 1
                continue
            w = r.w_at(s)
            lanes = road_layout.row_size(self.map, it)
            for li in range(lanes):
                x, z, _ = r.at(s, (li - (lanes - 1) / 2) * w / (lanes + 1))
                dots.append((None if auto else n, x, z, it.get("type") or AUTO_PATTERN[k % 8]))
                k += 1
        return dots

    def set_type(self, t):
        self.ptype.set(t)
        items = self.map.get("pickups")
        if self.sel_pick is not None and items and self.sel_pick < len(items):
            self.change()
            items[self.sel_pick]["type"] = t
            self.changed()
            self.say("Power-up type: %s" % t)
        else:
            self.say("New power-ups: %s" % t)
        self.build_props()

    # ================================================================================================ drawing
    def redraw_soon(self):
        """a light redraw once the waiting mouse events are done: fast mouse moves share one redraw (the picture
        never lags behind the mouse)"""
        if not getattr(self, "_redraw_job", None):
            self._redraw_job = self.root.after_idle(self._redraw_light)

    def _redraw_light(self):
        self._redraw_job = None
        self.redraw(light=True)

    def redraw(self, light=False):
        """the 2D picture; light=True (mouse drag / pan / zoom / hover): only the picture - the checks, the right
        panel and the 3D view are left as they are (they follow at the next full redraw, e.g. when the mouse is let go)"""
        if not light and getattr(self, "_redraw_job", None):
            self.root.after_cancel(self._redraw_job)       # this full redraw replaces a waiting light one
            self._redraw_job = None
        c = self.c
        c.delete("all")
        w, h = self.canvas_size()
        x0, z1 = self.to_world(0, 0)
        x1, z0 = self.to_world(w, h)
        step = 100.0 if self.scale > 0.08 else 500.0
        gx = math.floor(x0 / step) * step
        while gx < x1:
            sx, _ = self.to_screen(gx, 0)
            c.create_line(sx, 0, sx, h, fill="#16233a")
            gx += step
        gz = math.floor(z0 / step) * step
        while gz < z1:
            _, sy = self.to_screen(0, gz)
            c.create_line(0, sy, w, sy, fill="#16233a")
            gz += step
        # other maps of this location (ghosts)
        others = []
        for mi, m in enumerate(self.loc["maps"]):
            if mi == self.cur[1]:
                continue
            try:
                r = self.cached_road(m)
            except Exception:
                continue
            others.append((m, r))
            flat = []
            for x, z in r.centre[::2]:
                flat += self.to_screen(x, z)
            if len(flat) >= 4:
                c.create_line(*flat, fill="#3a4250", width=max(2, int(r.width * self.scale)))
                c.create_text(*self.to_screen(*r.centre[0]), text=m["name"], fill="#5c6878", anchor="sw")
        try:
            self.road = r = self.cached_road(self.map)
        except Exception as ex:
            self.road = None
            self.problems = [str(ex)]
            self.show_checks([])
            return
        n = len(r)
        self.net = None
        try:
            import network
            if self.map.get("branches") or self.map.get("splits"):
                self.net = network.get(self.map, r)            # cached: the same for pickups / chevrons / checks
        except Exception as ex:                       # noqa: BLE001 - shown in the checks
            self.problems = [str(ex)]
        self.net_draw_surfaces(c)                     # branch surfaces first: the main road covers their ends
        # the road between its two (loop-free) edges, coloured by height
        el, er = r.edge(-1), r.edge(1)
        seg = 3
        for i in range(0, n - 1, seg):
            j = min(i + seg, n - 1)
            hh = r.height[i]
            col = "#%02x%02x%02x" % (min(255, 62 + int(hh * 18)), min(255, 66 + int(hh * 14)), 74)
            poly = []
            for q in (el[i], el[j], er[j], er[i]):
                poly += self.to_screen(*q)
            c.create_polygon(*poly, fill=col, outline=col)
        # the solid walls (1 m outside the edge): a glow + a bright line (left out where a branch joins)
        for side in (-1, 1):
            cut = self.net_main_cut(side)
            if cut:
                self._net_wall(c, r, side, cut)
                continue
            wall = r.edge(side, 1.0)
            flat = []
            for x, z in wall[::2] + [wall[-1]]:
                flat += self.to_screen(x, z)
            c.create_line(*flat, fill=WALL_GLOW, width=max(6, int(4 * self.scale) + 6), capstyle="round",
                          joinstyle="round")
            c.create_line(*flat, fill=WALL, width=max(2, int(1.2 * self.scale) + 2), joinstyle="round")
        self.net_draw(c, r)
        if self.view_mode.get() == "surface":
            self.surf_draw(c, r)
        flat = []
        for x, z in r.centre:
            flat += self.to_screen(x, z)
        c.create_line(*flat, fill="#c8c8c8", dash=(6, 6))
        # glowing chevron panels on the outside wall of bends (as the build places them: the tile list has room for
        # about 144 - road pieces - lines - road blocks - horizon things)
        room = 144 - int(math.ceil(r.length / 48.0)) - 6 - (24 if self.map.get("obstacles", False) else 0) - road_layout.BIG_MIN
        clean = self.view_mode.get() == "surface"     # Surface tab: only the surfaces, power-ups and the start /
        #                                                 finish line (no labels / handles / points; user 2026-10-20)
        for cs, lat in (road_layout.chevron_panels(r, room, m=self.map) if not clean else ()):
            x0, z0, a = r.at(cs - road_layout.PANEL_LEN / 2, lat)
            x1, z1, _ = r.at(cs + road_layout.PANEL_LEN / 2, lat)
            c.create_line(*self.to_screen(x0, z0), *self.to_screen(x1, z1), fill="#f4f4f4", width=4, arrow="last",
                          arrowshape=(7, 8, 4))
        # start / finish / checkpoints
        si, fi = gen_aid.race_lines(r, self.map)
        if r.closed:            # a lap track: one start / finish line, the splits a third and two thirds round
            marks = ((si, "#35e05a", "START"), ((si + n // 3) % n, "#ffd23b", "split 1"),
                     ((si + 2 * n // 3) % n, "#ffd23b", "split 2"))
        else:
            marks = ((si, "#35e05a", "START"), (int(si + (fi - si) / 3), "#ffd23b", "split 1"),
                     (int(si + 2 * (fi - si) / 3), "#ffd23b", "split 2"), (fi, "#ffffff", "FINISH"))
        if r.closed or 0 < si < fi < n:
            for i, colour, label in marks:
                if clean and label.startswith("split"):
                    continue
                if r.closed and label == "START":
                    label = "START / FINISH (%d laps)" % self.map.get("laps", 3)
                half = r.width_at[i] / 2
                a = r.at(r.dist[i], -half - 4)
                b = r.at(r.dist[i], half + 4)
                big = self.drag and self.drag[0] == label.lower()
                c.create_line(*self.to_screen(a[0], a[1]), *self.to_screen(b[0], b[1]), fill=colour, width=6 if big else 3)
                c.create_text(*self.to_screen(b[0], b[1]), text=label, fill=colour, anchor="w", font=("Segoe UI", 9, "bold"))
                if label in ("START", "FINISH") or label.startswith("START /"):   # drag handle in the middle
                    hx, hy = self.to_screen(*r.centre[i])
                    c.create_rectangle(hx - 6, hy - 6, hx + 6, hy + 6, fill=colour, outline="#000")
            mx, mz, a = r.at(r.dist[si] + 60)
            c.create_line(*self.to_screen(mx, mz), *self.to_screen(mx + 50 * math.sin(a), mz + 50 * math.cos(a)),
                          fill="#35e05a", width=3, arrow="last")
        # bridges
        for j, i, flat_len in (r.crossings if not clean else ()):
            x, z = r.centre[j]
            sx, sy = self.to_screen(x, z)
            ok = abs(r.height[j] - r.height[i]) >= road_mod.BRIDGE_HEIGHT - 2
            c.create_oval(sx - 12, sy - 12, sx + 12, sy + 12, outline="#ffd23b" if ok else "#ff4040", width=2)
            c.create_text(sx + 14, sy - 14, text="BRIDGE %.0f m" % r.height[j] if ok else "CROSSING!",
                          fill="#ffd23b" if ok else "#ff4040", anchor="w", font=("Segoe UI", 8, "bold"))
        # tight turns
        for i in (range(1, n - 1) if not clean else ()):
            da = (r.heading[i + 1] - r.heading[i - 1] + math.pi) % (2 * math.pi) - math.pi
            if abs(da) > 1e-6 and 2 * road_mod.STEP / abs(da) < MIN_RADIUS:
                sx, sy = self.to_screen(*r.centre[i])
                c.create_oval(sx - 3, sy - 3, sx + 3, sy + 3, outline="#ff4040")
        # power-ups
        for idx, x, z, t in self.pickup_dots(r):
            sx, sy = self.to_screen(x, z)
            big = idx is not None and idx == self.sel_pick
            rad = 6 if big else 4
            c.create_oval(sx - rad, sy - rad, sx + rad, sy + rad, fill=TYPE_COLOURS.get(t, "#ccc"),
                          outline="#ffffff" if big else "#000000", width=2 if big else 1)
        if self.view_mode.get() == "ai":                # AI path tab: the AI's lane + line on top
            self.draw_ai_path(r)
        # control points
        pts = self.points()
        for k, p in enumerate(pts if not clean else ()):
            sx, sy = self.to_screen(p["x"], p["z"])
            col = "#35e05a" if k == 0 else ("#ffffff" if k == len(pts) - 1 else "#ffb020")
            if p.get("corner"):
                c.create_rectangle(sx - 7, sy - 7, sx + 7, sy + 7, fill=col, outline="#000")
            else:
                c.create_oval(sx - 7, sy - 7, sx + 7, sy + 7, fill=col, outline="#000")
            if k == self.sel:
                c.create_oval(sx - 12, sy - 12, sx + 12, sy + 12, outline="#7fd0ff", width=2)
            tag = str(k + 1)
            if p.get("h"):
                tag += "  ^%.0fm" % p["h"]
            if p.get("w"):
                tag += "  w%.0f" % p["w"]
            c.create_text(sx, sy - 15, text=tag, fill="#cfd6df", font=("Segoe UI", 8))
        self.measure_draw(c)
        if not light:
            self.show_checks(others)
            self.props_if_changed()
            self.update_3d()
        c.create_text(10, 10, anchor="nw", fill="#7f8a96", font=("Segoe UI", 9),
                      text="%s  >  %s      F1 = all keys      grid = %.0f m" % (self.loc["name"], self.map["name"], step))

    # ================================================================================================ checks
    def show_checks(self, others):
        r = self.road
        lines = []
        prob = []

        def check(ok, text):
            if ok == "warn":                          # a design hint (yellow): not an error
                lines.append(("warn", "WARN " + text))
                return
            lines.append(("ok" if ok else "bad", ("OK   " if ok else "FIX  ") + text))
            if not ok:
                prob.append(text)
        if r is None:
            for p in self.problems:
                check(False, p)
        else:
            check(MIN_LEN <= r.length <= MAX_LEN, "length %.0f m (%.0f..%.0f)" % (r.length, MIN_LEN, MAX_LEN))
            rad = r.min_radius()
            check(rad >= MIN_RADIUS, "tightest turn radius %.0f m (min %.0f)" % (rad, MIN_RADIUS))
            ub = r.unbridged_crossings()
            check(not ub, "crossings: %d, all bridged" % len(r.crossings) if not ub else
                  "%d crossing(s) without a bridge (turn Auto bridges on / raise a point)" % len(ub))
            g = r.max_grade()
            check(g <= 0.12, "steepest ramp %.1f %% (max 12 %%)" % (100 * g))
            dots = self.pickup_dots(r)
            check(len(dots) <= MAX_PICKUPS, "power-ups %d (max %d)" % (len(dots), MAX_PICKUPS))
            clash = None
            for m, o in others:
                for x, z in r.centre[::4]:
                    if o.near_road(x, z, (r.width + o.width) / 2 + 6):
                        clash = m["name"]
                        break
                if clash:
                    break
            check(clash is None, "no overlap with other maps here" if clash is None else
                  "overlaps the map '%s' (same location: their walls block each other)" % clash)
            check(self.total_maps() <= world_mod.MAX_MAPS, "maps in total: %d (max %d)" % (self.total_maps(), world_mod.MAX_MAPS))
            for ok, text in self.net_checks() + self.surf_checks():
                check(ok, text)
            lines.append(("info", ""))
            mins = r.length / (150 / 3.6) / 60
            lines.append(("info", "Length %.2f km, ~%d:%02d at 150 km/h" % (r.length / 1000, int(mins), int(mins * 60) % 60)))
            lines.append(("info", "Width %.0f..%.0f m, highest %.1f m, %d bridge(s)" % (
                min(r.width_at), max(r.width_at), max(r.height), len(r.crossings))))
            lines.append(("info", "Corners: %d, points: %d" % (sum(1 for p in self.points() if p.get("corner")),
                                                               len(self.points()))))
        self.problems = prob
        self.checks.config(state="normal")
        self.checks.delete("1.0", "end")
        for tag, text in lines:
            self.checks.insert("end", text + "\n", tag)
        self.checks.config(state="disabled")

    # ================================================================================================ properties
    def props_if_changed(self):
        """build_props only when something it shows changed (the map or the selection): re-making the whole panel
        took ~0.1 s at every redraw"""
        try:
            key = (id(self.world), self.cur, self.sel, self.sel_pick, getattr(self, "sel_branch", None),
                   getattr(self, "sel_bpt", None), getattr(self, "sel_split", None),
                   json.dumps(self.map, sort_keys=True, default=str))
        except (TypeError, ValueError):
            key = None
        if key is None or key != getattr(self, "_props_key", None):
            self.build_props()
            self._props_key = key

    def build_props(self):
        f = self.props
        for wdg in f.winfo_children():
            wdg.destroy()

        def head(t):
            tk.Label(f, text=t, bg=PANEL, fg=HEAD, font=("Segoe UI", 9, "bold")).pack(anchor="w", pady=(4, 0))

        def row(label, value, minus, plus):
            fr = tk.Frame(f, bg=PANEL)
            fr.pack(fill="x")
            tk.Label(fr, text=label, bg=PANEL, fg=FG, width=13, anchor="w").pack(side="left")
            tk.Button(fr, text="-", width=2, command=minus, bg=BTN, fg=FG).pack(side="left")
            tk.Label(fr, text=value, bg=PANEL, fg="white", width=8).pack(side="left")
            tk.Button(fr, text="+", width=2, command=plus, bg=BTN, fg=FG).pack(side="left")
        m = self.map
        pts = self.points()
        if self.sel is not None and self.sel < len(pts):
            p = pts[self.sel]
            head("POINT %d of %d" % (self.sel + 1, len(pts)))
            tk.Label(f, text="x %.0f   z %.0f" % (p["x"], p["z"]), bg=PANEL, fg=FG).pack(anchor="w")
            row("Width (Q/W)", "%.0f m" % (p.get("w") or m.get("width", 60)), lambda: self.adj("w", -5), lambda: self.adj("w", 5))
            row("Height (PgDn/Up)", "%.0f m" % (p.get("h") or 0), lambda: self.adj("h", -1), lambda: self.adj("h", 1))
            row("Corner radius", "%s" % ("%.0f m" % p["corner"] if p.get("corner") else "smooth"),
                lambda: self.adj("corner", -5), lambda: self.adj("corner", 5))
            used = self.road.points[self.sel].get("corner_used") if self.road and self.sel < len(self.road.points) else None
            if p.get("corner") and used is not None and used < p["corner"] - 0.5:
                tk.Label(f, text="only %.0f m fits here (the points next to it are too close)" % used, bg=PANEL,
                         fg="#ffd23b").pack(anchor="w")
            fr = tk.Frame(f, bg=PANEL)
            fr.pack(fill="x", pady=2)
            tk.Button(fr, text=ICON["left"] + " 90\u00B0 left (T)", command=lambda: self.turn90(1), bg=BTN, fg=FG,
                      font=UI_FONT).pack(side="left")
            tk.Button(fr, text=ICON["right"] + " 90\u00B0 right (Y)", command=lambda: self.turn90(-1), bg=BTN,
                      fg=FG, font=UI_FONT).pack(side="left", padx=2)
            fr = tk.Frame(f, bg=PANEL)
            fr.pack(fill="x", pady=2)
            tk.Button(fr, text=ICON["corner"] + " Corner (C)", command=self.toggle_corner, bg=BTN, fg=FG,
                      font=UI_FONT).pack(side="left")
            tk.Button(fr, text=ICON["delete"] + " Delete", command=self.delete_selected, bg=DANGER, fg=FG,
                      font=UI_FONT).pack(side="left", padx=2)
        elif self.sel_pick is not None and m.get("pickups") and self.sel_pick < len(m["pickups"]):
            it = m["pickups"][self.sel_pick]
            head("POWER-UP %s" % ("ROW" if it.get("kind") == "row" else ""))
            tk.Label(f, text="type: %s (keys 1-8 or the Type menu)" % (it.get("type") or "mixed"), bg=PANEL, fg=FG).pack(anchor="w")
            fr = tk.Frame(f, bg=PANEL)
            fr.pack(fill="x", pady=2)
            if it.get("kind") == "row":
                tk.Button(fr, text="Mixed types", command=self.mixed_row, bg=BTN, fg=FG).pack(side="left")
                row("Power-ups in this row", "%d" % road_layout.row_size(m, it), lambda: self.row_n(-1),
                    lambda: self.row_n(1))
            tk.Button(fr, text="Delete", command=self.delete_selected, bg=DANGER, fg=FG).pack(side="left", padx=2)
        self.net_props(f, head, row)
        head("MAP: %s" % m["name"])
        row("Default width", "%.0f m" % m.get("width", 60), lambda: self.map_width(-5), lambda: self.map_width(5))
        if self.road:
            si, fi = gen_aid.race_lines(self.road, m)
            row("Start at", "%.0f m" % self.road.dist[si], lambda: self.move_line("start", -LINE_STEP),
                lambda: self.move_line("start", LINE_STEP))
            if not self.road.closed:
                row("Finish at", "%.0f m" % self.road.dist[fi], lambda: self.move_line("finish", -LINE_STEP),
                    lambda: self.move_line("finish", LINE_STEP))
            fr = tk.Frame(f, bg=PANEL)
            fr.pack(fill="x")
            tk.Label(fr, text="(or drag the squares on the lines)", bg=PANEL, fg="#7f8a96").pack(side="left")
            if m.get("start_m") is not None or m.get("finish_m") is not None:
                tk.Button(fr, text="Reset", command=self.reset_lines, bg=BTN, fg=FG).pack(side="left", padx=4)
        # LAP TRACK: the road's end joins its start (closed loop), start = finish, raced for a number of laps
        self.v_laps = tk.BooleanVar(value=bool(m.get("laps")))
        fr = tk.Frame(f, bg=PANEL)
        fr.pack(fill="x", pady=(4, 0))
        tk.Checkbutton(fr, text="⟳ Lap track (the end joins the start)", variable=self.v_laps, bg=PANEL,
                       fg="#7fd0ff", selectcolor=BG, activebackground=PANEL, font=UI_FONT,
                       command=self.toggle_laps).pack(side="left")
        if m.get("laps"):
            row("Laps", "%d" % m["laps"], lambda: self.set_laps(-1), lambda: self.set_laps(1))
        self.v_bridges = tk.BooleanVar(value=m.get("auto_bridges", True))
        self.v_obst = tk.BooleanVar(value=m.get("obstacles", False))
        tk.Checkbutton(f, text="Auto bridges where the road crosses", variable=self.v_bridges, bg=PANEL, fg=FG,
                       selectcolor=BG, activebackground=PANEL, command=lambda: self.map_flag("auto_bridges", self.v_bridges)).pack(anchor="w")
        tk.Checkbutton(f, text="Obstacles (containers, barrels)", variable=self.v_obst, bg=PANEL, fg=FG,
                       selectcolor=BG, activebackground=PANEL, command=lambda: self.map_flag("obstacles", self.v_obst)).pack(anchor="w")
        fr = tk.Frame(f, bg=PANEL)
        fr.pack(fill="x", pady=2)
        auto = m.get("pickups") is None
        tk.Label(fr, text="Power-ups: %s" % ("automatic" if auto else "%d items" % len(m["pickups"])), bg=PANEL, fg=FG).pack(side="left")
        row("Rows every", "%.0f m" % road_layout.pickup_step(m), lambda: self.map_num("pickup_step", -25, 50, 600, 150),
            lambda: self.map_num("pickup_step", 25, 50, 600, 150))
        row("Power-ups per row", "%d" % road_layout.row_size(m), lambda: self.map_num("pickup_per_row", -1, 1, 9, 5),
            lambda: self.map_num("pickup_per_row", 1, 1, 9, 5))
        fr = tk.Frame(f, bg=PANEL)
        fr.pack(fill="x")
        tk.Button(fr, text=ICON["auto"] + " Auto rows", command=self.pickups_auto, bg=BTN, fg=FG,
                  font=UI_FONT).pack(side="left")
        tk.Button(fr, text=ICON["clear"] + " Clear all", command=self.pickups_clear, bg=BTN, fg=FG,
                  font=UI_FONT).pack(side="left", padx=2)
        fr = tk.Frame(f, bg=PANEL)
        fr.pack(fill="x", pady=2)
        tk.Button(fr, text=ICON["reverse"] + " Reverse (V)", command=self.reverse, bg=BTN, fg=FG,
                  font=UI_FONT).pack(side="left")
        tk.Button(fr, text=ICON["mirror"] + " Mirror (X)", command=self.mirror, bg=BTN, fg=FG,
                  font=UI_FONT).pack(side="left", padx=2)
        # minimap (in race): automatic from the road, or the map's own picture
        head("AUTO SCENERY (made at every build)")
        sc = tk.Frame(f, bg=PANEL)
        sc.pack(fill="x")
        for k, (key, label) in enumerate(road_layout.SCENERY):
            v = tk.BooleanVar(value=road_layout.scenery_on(m, key))
            tk.Checkbutton(sc, text=label, variable=v, bg=PANEL, fg=FG, selectcolor=BG, activebackground=PANEL,
                           activeforeground=FG, command=lambda key=key, v=v: self.scenery_set(key, v.get())).grid(
                row=k // 2, column=k % 2, sticky="w")
        head("MINIMAP")
        mm = m.get("minimap") or {}
        self.v_mm_auto = tk.BooleanVar(value=mm.get("auto", True))
        tk.Checkbutton(f, text="Auto minimap from the road", variable=self.v_mm_auto, bg=PANEL, fg=FG, selectcolor=BG,
                       activebackground=PANEL, command=self.minimap_auto).pack(anchor="w")
        if not mm.get("auto", True):
            name = os.path.basename(mm.get("image") or "") or "(no picture picked)"
            tk.Label(f, text="own picture: " + name, bg=PANEL, fg="#ffd23b" if mm.get("image") else "#ff6060",
                     wraplength=260, justify="left").pack(anchor="w")
        fr = tk.Frame(f, bg=PANEL)
        fr.pack(fill="x", pady=2)
        tk.Button(fr, text=ICON["save"] + " Export road picture", command=self.minimap_export, bg=BTN, fg=FG,
                  font=UI_FONT).pack(side="left")
        tk.Button(fr, text=ICON["map"] + " Pick own", command=self.minimap_pick, bg=BTN, fg=FG,
                  font=UI_FONT).pack(side="left", padx=2)
        tk.Button(fr, text=ICON["fit"] + " Preview", command=self.minimap_preview, bg=BTN, fg=FG,
                  font=UI_FONT).pack(side="left")

    # ================================================================================================ lighting
    # Per LOCATION (all its maps share the level): loc["lighting"] = {"preset": name, "values": {field: [floats]}}.
    # The game's own tracks have Sun strength 0 and get all their light from light samples stored in the level
    # (Baked); our levels have none -> placed models look black. "Light up placed objects" turns Sun + Sky on.
    LIGHT_UP = lighting_mod.LIGHT_UP          # writes light into the level's light samples (sun / sky did nothing)

    def light_data(self):
        # a location whose Lighting tab was never touched shows (and gets) Amboy day + placed objects lit up
        return lighting_mod.effective(self.loc)

    def light_value(self, key):
        name, vals = self.light_data()
        return list(vals.get(key) or lighting_mod.presets()[name][key])

    def light_set(self, key=None, value=None, preset=None, reset=False, many=None):
        self.change()
        name, vals = self.light_data()
        vals = dict(vals)
        if preset:
            name, vals = preset, {}
        if reset:
            vals = {}
        if key:
            vals[key] = [round(float(x), 5) for x in value]
        for k, v in (many or {}).items():
            vals[k] = list(v)
        self.loc["lighting"] = {"preset": name, "values": vals}
        self.changed()                                  # (rebuilds this tab)

    def light_build(self):
        f = self.light_panel
        for w in f.winfo_children():
            w.destroy()
        presets = lighting_mod.presets()
        name, vals = self.light_data()
        top = tk.Frame(f, bg=BG)
        top.pack(fill="x", padx=12, pady=(10, 4))
        tk.Label(top, text="☀ LIGHTING  -  location: %s  (all its maps)" % self.loc["name"], bg=BG, fg="#ffd23b",
                 font=("Segoe UI Symbol", 12, "bold")).pack(side="left")
        bar = tk.Frame(f, bg=BG)
        bar.pack(fill="x", padx=12, pady=4)
        tk.Label(bar, text="Start from the lighting of:", bg=BG, fg=FG).pack(side="left")
        pv = tk.StringVar(value=name)
        om = tk.OptionMenu(bar, pv, *lighting_mod.PRESETS, command=lambda v: self.light_set(preset=v))
        om.config(bg=BTN, fg=FG, highlightthickness=0, width=26)
        om.pack(side="left", padx=6)
        tk.Button(bar, text="☀ Light up placed objects", bg="#7a5a10", fg="white",
                  font=("Segoe UI Symbol", 9, "bold"), command=lambda: self.light_set(many=self.LIGHT_UP)).pack(side="left", padx=6)
        tk.Button(bar, text="↺ Reset to the preset", bg=BTN, fg=FG, font=UI_FONT,
                  command=lambda: self.light_set(reset=True)).pack(side="left", padx=2)
        tk.Label(f, text="Blur's own tracks have the Sun at 0 and get their light from light samples stored in the level "
                         "(Baked). Our level has none where the track is, so placed game models looked black: "
                         "'Light on placed objects' writes light into all of the level's samples (Sun / Sky don't "
                         "reach objects). Changes need Build + install; values you changed are marked *.",
                 bg=BG, fg="#9aa8b8", wraplength=1000, justify="left").pack(anchor="w", padx=12, pady=(0, 6))
        body = tk.Frame(f, bg=BG)
        body.pack(fill="both", expand=True, padx=12)
        rows = tk.Frame(body, bg=BG)
        rows.pack(side="left", fill="y")
        self.light_prev = tk.Canvas(body, width=420, height=300, bg="#000", highlightthickness=1,
                                    highlightbackground="#3a4250")
        self.light_prev.pack(side="left", anchor="n", padx=16)
        fields = [(k, f[0], f[1], f[2]) for k, f in lighting_mod.SAMPLE_FIELDS.items()]
        fields += [(k, f[1], f[2], f[3]) for k, f in lighting_mod.FIELDS.items()]
        for key, n, label, mx in fields:
            v = self.light_value(key)
            r = tk.Frame(rows, bg=BG)
            r.pack(fill="x", pady=1)
            tk.Label(r, text=("* " if key in vals else "   ") + label, bg=BG, fg="#ffd23b" if key in vals else FG,
                     width=34, anchor="w").pack(side="left")
            if n == 4:
                rgb = v[:3]
                peak = max(max(rgb), 1e-6)
                col = "#%02x%02x%02x" % tuple(int(255 * min(1.0, c / peak)) for c in rgb)
                tk.Button(r, text="  ", bg=col, activebackground=col, width=3, relief="ridge",
                          command=lambda k=key: self.light_colour(k)).pack(side="left", padx=4)
            if key == "fog_colour":
                continue
            idx = 3 if n == 4 else 0
            var = tk.DoubleVar(value=v[idx])
            sc = tk.Scale(r, from_=0, to=max(mx, v[idx]), resolution=mx / 200.0, orient="horizontal", length=260,
                          variable=var, showvalue=True, bg=BG, fg=FG, troughcolor=BTN, highlightthickness=0,
                          command=lambda _v, k=key, i=idx, var=var: self.light_preview(k, i, var.get()))
            sc.pack(side="left")
            sc.bind("<ButtonRelease-1>", lambda e, k=key, i=idx, var=var: self._light_commit(k, i, var.get()))
        self.light_preview()

    def _light_commit(self, key, i, value):
        v = self.light_value(key)
        if abs(v[i] - value) > 1e-9:
            v[i] = value
            self.light_set(key, v)

    def light_colour(self, key):
        v = self.light_value(key)
        peak = max(max(v[:3]), 1e-6)
        start = "#%02x%02x%02x" % tuple(int(255 * min(1.0, c / peak)) for c in v[:3])
        got = colorchooser.askcolor(color=start, parent=self.root, title=lighting_mod.FIELDS[key][2])
        if got and got[0]:
            scale = peak if key != "fog_colour" else 1.0
            v[:3] = [c / 255.0 * scale for c in got[0]]
            self.light_set(key, v)

    def light_preview(self, key=None, i=None, value=None):
        """a rough idea only: sky colour, ground and a grey box lit by sun + sky + emissive (the game also adds the
        level's baked light and the exposure)"""
        cv = getattr(self, "light_prev", None)
        if cv is None or not cv.winfo_exists():
            return
        vals = {k: self.light_value(k) for k in list(lighting_mod.FIELDS) + list(lighting_mod.SAMPLE_FIELDS)}
        if key is not None:
            vals[key][i] = value
        cv.delete("all")

        def hexc(rgb):
            return "#%02x%02x%02x" % tuple(max(0, min(255, int(255 * c))) for c in rgb)
        sun, sky, emi = vals["sun"], vals["sky"], vals["emissive"]
        sky_rgb = [c * min(1.0, 0.25 + vals["sky_intensity"][0] * 3) for c in sky[:3]]
        fog = vals["fog_colour"][:3]
        for y in range(0, 180, 6):
            t = y / 180.0
            cv.create_rectangle(0, y, 420, y + 6, outline="", fill=hexc([s * (1 - t) * 0.8 + f * t * 3 for s, f in zip(sky_rgb, fog)]))
        cv.create_rectangle(0, 180, 420, 300, outline="", fill="#3a3e44")
        ol, up = vals["object_light"], vals["object_light_up"][0]
        amb = [c * ol[3] * 3.0 * (1 - 0.5 * up) for c in ol[:3]]          # level light samples (what lights objects)
        direct = [c * ol[3] * 3.0 * up for c in ol[:3]]
        glow = [c * emi[3] * 0.0 for c in emi[:3]]
        base = 0.55
        top_face = [base * (a + d + g) for a, d, g in zip(amb, direct, glow)]
        side1 = [base * (a + d * 0.55 + g) for a, d, g in zip(amb, direct, glow)]
        side2 = [base * (a + d * 0.15 + g) for a, d, g in zip(amb, direct, glow)]
        cv.create_polygon(150, 150, 230, 130, 290, 150, 210, 170, fill=hexc(top_face), outline="#000")
        cv.create_polygon(150, 150, 210, 170, 210, 250, 150, 230, fill=hexc(side1), outline="#000")
        cv.create_polygon(210, 170, 290, 150, 290, 230, 210, 250, fill=hexc(side2), outline="#000")
        cv.create_text(10, 10, anchor="nw", fill="#ffffff", font=("Segoe UI", 9),
                       text="rough preview of a placed object (light on placed objects)")
        cv.create_text(10, 282, anchor="nw", fill="#9aa8b8", font=("Segoe UI", 8),
                       text="object light %.2f   exposure %.2f   bloom %.2f" % (ol[3],
                                                                                  vals["middle_grey"][0], vals["bloom"][0]))

    # ================================================================================================ minimap
    def loc_bounds(self):
        roads = [self.make_road(m) for m in self.loc["maps"]]
        return minimap_mod.city_bounds(roads), roads

    def minimap_auto(self):
        self.change()
        mm = dict(self.map.get("minimap") or {})
        mm["auto"] = bool(self.v_mm_auto.get())
        self.map["minimap"] = mm
        self.changed()
        if not mm["auto"] and not mm.get("image"):
            self.say("Pick a picture for the minimap (tip: Export road picture, paint on it, then Pick own)", "#ffd23b")

    def minimap_export(self):
        os.makedirs(os.path.join(paths.TRACKS, "minimaps"), exist_ok=True)
        path = filedialog.asksaveasfilename(parent=self.root, title="Save the road picture",
                                            initialdir=os.path.join(paths.TRACKS, "minimaps"),
                                            initialfile="%s.png" % world_mod.slug(self.map["name"]),
                                            defaultextension=".png", filetypes=[("PNG picture", "*.png")])
        if not path:
            return
        b, roads = self.loc_bounds()
        minimap_mod.export_png(path, b, roads, highlight=self.cur[1])
        self.say("Saved %s: WHITE = shown on the minimap. Paint on it (Paint is fine), then 'Pick own'." % path,
                 "#58d06a")

    def minimap_pick(self):
        path = filedialog.askopenfilename(parent=self.root, title="Pick the minimap picture",
                                          initialdir=os.path.join(paths.TRACKS, "minimaps"),
                                          filetypes=[("Pictures", "*.png *.jpg *.jpeg *.bmp"), ("All files", "*.*")])
        if not path:
            return
        self.change()
        self.map["minimap"] = {"auto": False, "image": os.path.normpath(path)}
        self.changed()
        if not os.path.exists(path + ".json"):
            self.say("This picture was not exported here: it is stretched over the whole location area (16:9)",
                     "#ffd23b")

    def minimap_preview(self):
        """what the in-race minimap will show (all routes of the location in grey, this one in white)"""
        b, roads = self.loc_bounds()
        top = tk.Toplevel(self.root)
        top.title("Minimap preview - %s" % self.map["name"])
        cv = tk.Canvas(top, width=W_PREV, height=H_PREV, bg="#05080c", highlightthickness=0)
        cv.pack()
        for k, (m, r) in enumerate(zip(self.loc["maps"], roads)):
            mm = m.get("minimap") or {}
            polys = None
            if not mm.get("auto", True) and mm.get("image") and os.path.exists(mm["image"]):
                polys = minimap_mod.image_polys(b, mm["image"])
            polys = polys or minimap_mod.route_polys(b, r)
            col = "#ffffff" if k == self.cur[1] else "#3a4250"
            for p in polys:
                cv.create_polygon(*[c * W_PREV for uv in p for c in uv], fill=col, outline=col)
            if k == self.cur[1]:
                for p in minimap_mod.start_polys(b, r, r.dist[gen_aid.race_lines(r, m)[0]]):
                    cv.create_polygon(*[c * W_PREV for uv in p for c in uv], fill="#35e05a", outline="#35e05a")
        tk.Label(top, text="White = this map's route, green = start line. The cars move on it in the race.",
                 bg=PANEL, fg=FG).pack(fill="x")
        top.bind("<Escape>", lambda e: top.destroy())

    # ================================================================================================ edits
    def adj(self, key, delta):
        if self.sel is None:
            self.say("Select a point first (S, then click a point)")
            return
        self.change()
        p = self.points()[self.sel]
        if key == "w":
            p["w"] = max(30.0, min(100.0, (p.get("w") or self.map.get("width", 60.0)) + delta))
        elif key == "h":
            p["h"] = max(0.0, min(30.0, (p.get("h") or 0.0) + delta))
        elif key == "corner":
            v = (p.get("corner") or 0.0) + delta
            p["corner"] = 0.0 if v < road_mod.MIN_CORNER - 0.1 else min(v, 300.0)
            if delta > 0 and not p.get("corner"):
                p["corner"] = road_mod.MIN_CORNER
        self.changed()

    def map_width(self, d):
        self.change()
        self.map["width"] = max(30.0, min(100.0, self.map.get("width", 60.0) + d))
        self.changed()

    def map_num(self, key, d, lo, hi, default):
        """a number setting of the map (power-up rows every .. m, power-ups per row)"""
        self.change()
        self.map[key] = max(lo, min(hi, (self.map.get(key) or default) + d))
        self.changed()
        self.say("%s: %s (the automatic power-up rows follow)" % (key.replace("_", " "), self.map[key]))

    def row_n(self, d):
        """the selected power-up row: how many power-ups across it"""
        items = self.map.get("pickups")
        if not items or self.sel_pick is None:
            return
        self.change()
        it = items[self.sel_pick]
        it["n"] = max(1, min(9, road_layout.row_size(self.map, it) + d))
        self.changed()

    def scenery_set(self, key, on):
        """turn an automatic scenery kind (ships, cranes, lamps ...) on / off for this map"""
        self.change()
        self.map.setdefault("scenery", {})[key] = bool(on)
        self.changed()
        self.say("%s %s - Build + install to see it in game" % (dict(road_layout.SCENERY)[key], "ON" if on else "OFF"))

    def map_flag(self, key, var):
        self.change()
        self.map[key] = bool(var.get())
        self.changed()

    def toggle_corner(self):
        if self.sel is None:
            self.say("Select a point first")
            return
        self.change()
        p = self.points()[self.sel]
        p["corner"] = 0.0 if p.get("corner") else 40.0
        self.changed()

    def turn90(self, side=1):
        """make the selected point a 90-degree corner: side 1 = LEFT, -1 = RIGHT. The REST of the road (every later
        point, its power-ups and drawn AI line) turns round the point as one piece, so the road leaves at a right
        angle and keeps its shape after it (a new point 300 m away at the end of the road); the point becomes a
        corner."""
        pts = self.points()
        k = self.sel
        if k is None or k == 0:
            self.say("Select a point (not the first one) for a 90-degree turn")
            return
        p, a = pts[k], pts[k - 1]
        ux, uz = p["x"] - a["x"], p["z"] - a["z"]
        if math.hypot(ux, uz) < 1e-6:
            self.say("This point sits on the one before it - move it first")
            return
        self.change()
        lx, lz = -uz * side, ux * side                   # (-uz, ux) = left on the screen (z points up)
        if k + 1 < len(pts):
            b = pts[k + 1]
            # turn the tail by the angle between where it goes now and the right angle
            ang = math.atan2(lx * (b["z"] - p["z"]) - lz * (b["x"] - p["x"]), lx * (b["x"] - p["x"]) + lz * (b["z"] - p["z"]))
            ca, sa = math.cos(-ang), math.sin(-ang)

            def turn(x, z):
                dx, dz = x - p["x"], z - p["z"]
                return p["x"] + dx * ca - dz * sa, p["z"] + dx * sa + dz * ca
            s_k = self.road.project(p["x"], p["z"])[0] if self.road else None
            for q in pts[k + 1:]:
                q["x"], q["z"] = turn(q["x"], q["z"])
            if s_k is not None:                           # things placed on the turned part go with it
                for it in self.map.get("pickups") or []:
                    if self.road.project(it["x"], it["z"])[0] > s_k + 1:
                        it["x"], it["z"] = (round(v, 1) for v in turn(it["x"], it["z"]))
                ai = self.map.get("ai_path")
                if isinstance(ai, dict) and ai.get("points"):
                    for q in ai["points"]:
                        if self.road.project(q[0], q[1])[0] > s_k + 1:
                            q[0], q[1] = turn(q[0], q[1])
        else:
            lu = math.hypot(lx, lz)
            pts.append({"x": p["x"] + lx / lu * 300, "z": p["z"] + lz / lu * 300})
        p["corner"] = p.get("corner") or 40.0
        self.changed()
        self.say("90-degree %s turn at point %d" % ("LEFT" if side > 0 else "RIGHT", k + 1))

    def reverse(self):
        self.change()
        m = self.map
        if self.road and (m.get("start_m") is not None or m.get("finish_m") is not None):
            si, fi = gen_aid.race_lines(self.road, m)     # the same stretch of road, driven the other way
            m["start_m"], m["finish_m"] = self.road.length - self.road.dist[fi], self.road.length - self.road.dist[si]
        self.map["points"].reverse()
        self.sel = None
        self.changed()
        self.say("Direction reversed (start <-> finish)")

    def mirror(self):
        self.change()
        pts = self.points()
        cz = sum(p["z"] for p in pts) / len(pts)
        for p in pts:
            p["z"] = 2 * cz - p["z"]
        for it in self.map.get("pickups") or []:
            it["z"] = 2 * cz - it["z"]
        for o in self.map.get("objects") or []:       # library objects: mirrored place, mirrored facing
            o["z"] = 2 * cz - o["z"]
            o["turn"] = math.pi - o.get("turn", 0.0)
        self.changed()
        self.say("Mirrored")

    def pickups_auto(self):
        self.change()
        self.map["pickups"] = None
        self.sel_pick = None
        self.changed()
        self.say("Automatic power-up rows")

    def pickups_clear(self):
        self.change()
        self.map["pickups"] = []
        self.sel_pick = None
        self.changed()
        self.say("All power-ups removed (add rows with R, single ones with P)")

    def mixed_row(self):
        items = self.map.get("pickups")
        if self.sel_pick is not None and items:
            self.change()
            items[self.sel_pick].pop("type", None)
            self.changed()

    def on_right(self, e):
        """right click: Surface tab = remove paint (drag); Measure tool = clear the line; otherwise move the map"""
        if self.view_mode.get() == "surface":
            self.surf_down(*self.to_world(e.x, e.y), erase=True)
            return
        if self.tool.get() == "measure" and self.measure:
            self.measure = None
            self.say("Measure cleared (right click)")
            self.redraw()
            return
        self.pan_start(e)

    def deselect(self):
        if self.net_cancel():
            self.redraw()
            return
        if self.measure:
            self.measure = None
            self.say("Measure cleared")
            self.redraw()
            return
        self.sel = self.sel_pick = None
        self.net_clear_sel()
        self.redraw()

    def delete_selected(self):
        if self.tool.get() == "measure" and self.measure_back():
            return                                      # Measure tool: Backspace / Delete = the last point
        pts = self.points()
        if self.sel is None and self.sel_pick is None and self.net_delete():
            return
        if self.sel is not None:
            if len(pts) <= 2:
                self.say("A road needs at least 2 points")
                return
            self.change()
            del pts[self.sel]
            self.sel = None
            self.changed()
        elif self.sel_pick is not None and self.map.get("pickups"):
            self.change()
            del self.map["pickups"][self.sel_pick]
            self.sel_pick = None
            self.changed()

    def nudge(self, dx, dz):
        if self.sel is None:
            return
        self.change()
        p = self.points()[self.sel]
        p["x"] += dx
        p["z"] += dz
        self.changed()

    # ================================================================================================ mouse
    def hit_point(self, sx, sy):
        best = None
        for k, p in enumerate(self.points()):
            px, py = self.to_screen(p["x"], p["z"])
            d = (px - sx) ** 2 + (py - sy) ** 2
            if d < 160 and (best is None or d < best[0]):
                best = (d, k)
        return best[1] if best else None

    def hit_line(self, sx, sy):
        """'start' / 'finish' if (sx, sy) is on that line's handle (the square in the middle of the road)"""
        if not self.road:
            return None
        si, fi = gen_aid.race_lines(self.road, self.map)
        for kind, i in ((("start", si),) if self.road.closed else (("start", si), ("finish", fi))):
            px, py = self.to_screen(*self.road.centre[i])
            if (px - sx) ** 2 + (py - sy) ** 2 < 150:
                return kind
        return None

    def set_line(self, kind, s):
        """put the start or finish line `s` m along the road (kept where the grid / gates fit, >= 300 m apart)"""
        m = self.map
        if self.road.closed:            # a lap track: one start / finish line, anywhere round the loop
            s = round((s % self.road.length) / road_mod.STEP) * road_mod.STEP
            m["start_m"] = s
            m.pop("finish_m", None)
            self.say("START / FINISH at %.0f m of the %.0f m loop" % (s, self.road.length), "#35e05a")
            return
        m[kind + "_m"] = round(s / road_mod.STEP) * road_mod.STEP
        si, fi = gen_aid.race_lines(self.road, m)          # what the build will use
        m["start_m"], m["finish_m"] = self.road.dist[si], self.road.dist[fi]
        if m.get("pickups") is None:
            self.say("START at %.0f m, FINISH at %.0f m (race %.0f m); the automatic power-ups follow"
                     % (m["start_m"], m["finish_m"], m["finish_m"] - m["start_m"]), "#35e05a")

    def move_line(self, kind, delta):
        if not self.road:
            return
        self.change()
        si, fi = gen_aid.race_lines(self.road, self.map)
        self.set_line(kind, self.road.dist[si if kind == "start" else fi] + delta)
        self.changed()

    def toggle_laps(self):
        """Lap track on / off: on joins the road's last point back to its first (a closed loop, start = finish)"""
        self.change()
        m = self.map
        if self.v_laps.get():
            m["laps"] = m.get("laps_last", 3)
            m.pop("finish_m", None)
            self.say("Lap track: the end now joins the start, %d laps (change with Laps - / +)" % m["laps"], "#7fd0ff")
        else:
            m["laps_last"] = m.pop("laps", 3)
            self.say("Sprint again: start and finish lines")
        m.pop("start_m", None)
        self.changed()

    def set_laps(self, delta):
        self.change()
        self.map["laps"] = max(1, min(10, self.map.get("laps", 3) + delta))
        self.changed()
        self.say("%d laps" % self.map["laps"], "#7fd0ff")

    def reset_lines(self):
        self.change()
        self.map.pop("start_m", None)
        self.map.pop("finish_m", None)
        self.changed()
        self.say("Start / finish back to the standard spots")

    def hit_pickup(self, sx, sy):
        if not self.road or self.map.get("pickups") is None:
            return None
        best = None
        for idx, x, z, t in self.pickup_dots(self.road):
            px, py = self.to_screen(x, z)
            d = (px - sx) ** 2 + (py - sy) ** 2
            if d < 100 and (best is None or d < best[0]):
                best = (d, idx)
        return best[1] if best else None

    def world_xy(self, e):
        x, z = self.to_world(e.x, e.y)
        if self.snap.get():
            x, z = round(x / SNAP) * SNAP, round(z / SNAP) * SNAP
        return x, z

    def on_down(self, e):
        if self.view_mode.get() == "surface":         # SURFACE tab: the brush
            self.surf_down(*self.to_world(e.x, e.y))
            return
        if self.view_mode.get() == "ai":                # AI path tab: pen, the branch / lane split tools, or move the map
            if self.ai_pen.get():
                self.ai_pen_down(e)
            elif self.tool.get() in ("branch", "split"):
                self.net_click(self.tool.get(), *self.world_xy(e))
            else:
                self.pan_start(e)
            return
        tool = self.tool.get()
        x, z = self.world_xy(e)
        if tool in ("branch", "split"):
            self.net_click(tool, x, z)
            return
        if tool == "measure":
            self.measure_click(*self.to_world(e.x, e.y))
            return
        k = self.hit_point(e.x, e.y)
        pk = self.hit_pickup(e.x, e.y) if k is None else None
        pts = self.points()
        line = self.hit_line(e.x, e.y) if k is None and pk is None and tool in ("select", "move") else None
        nh = self.net_hit(e.x, e.y) if k is None and pk is None and not line and tool in ("select", "move", "erase") \
            else None
        if nh:
            self.sel = self.sel_pick = None
            self.net_select(nh)
            if tool in ("move", "select") and nh[0] not in ("branch", "split"):   # branch points / handles: drag
                self.change()
                self.drag = ("net", nh)
            elif tool == "erase":
                if nh[0] != "bpt":
                    self.sel_bpt = None
                self.net_delete()
                return
            self.redraw()
            return
        if line:
            self.change()
            self.drag = (line, None)
            self.say("Drag the %s line along the road" % line.upper(), "#35e05a")
            self.redraw()
        elif tool == "select":
            self.net_clear_sel()
            self.sel, self.sel_pick = k, (pk if k is None else None)
            if k is None and pk is None:
                self.pan_start(e)
            self.redraw()
        elif tool == "move":
            if k is not None or pk is not None:
                self.change()
                self.drag = ("point", k) if k is not None else ("pick", pk)
                self.sel, self.sel_pick = k, pk
            else:
                self.pan_start(e)
        elif tool == "add":
            self.change()
            pts.append({"x": x, "z": z})
            self.sel = len(pts) - 1
            self.changed()
        elif tool == "insert":
            near_main = self.road is not None and abs(self.road.project(x, z)[1]) <= self.road.w_at(
                self.road.project(x, z)[0]) / 2 + 30
            if (self.sel_branch is not None or not near_main) and self.net_insert(x, z):
                return                                  # a new point in a branch
            best = min(range(len(pts) - 1), key=lambda i: self.seg_dist(pts, i, x, z))
            self.change()
            pts.insert(best + 1, {"x": x, "z": z})
            self.sel = best + 1
            self.changed()
        elif tool in ("row", "single"):
            if self.road is None:
                return
            s, lat = self.road.project(x, z)
            on_branch = self.net_branch_at(x, z) if tool == "row" else None
            if tool == "row" and on_branch is None and abs(lat) > self.road.w_at(s) / 2 + 20:
                self.say("Click ON the road (or a branch) to place a row")
                return
            self.change()
            items = self.pickup_items(create=True)
            if tool == "row" and on_branch is not None:
                bi, br = on_branch
                bs, _ = br.project(x, z)
                rx, rz, _ = br.at(bs)
                items.append({"kind": "row", "x": round(rx, 1), "z": round(rz, 1), "type": self.ptype.get(), "bi": bi})
            elif tool == "row":
                rx, rz, _ = self.road.at(s)
                items.append({"kind": "row", "x": round(rx, 1), "z": round(rz, 1), "type": self.ptype.get()})
            else:
                items.append({"kind": "single", "x": round(x, 1), "z": round(z, 1), "type": self.ptype.get()})
            self.sel_pick = len(items) - 1
            self.sel = None
            self.changed()
        elif tool == "erase":
            if k is not None:
                self.sel, self.sel_pick = k, None
                self.delete_selected()
            elif pk is not None:
                self.sel, self.sel_pick = None, pk
                self.delete_selected()

    def seg_dist(self, pts, i, x, z):
        ax, az, bx, bz = pts[i]["x"], pts[i]["z"], pts[i + 1]["x"], pts[i + 1]["z"]
        dx, dz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((x - ax) * dx + (z - az) * dz) / max(dx * dx + dz * dz, 1e-9)))
        return (ax + dx * t - x) ** 2 + (az + dz * t - z) ** 2

    def on_move(self, e):
        if self.surf_stroke:
            self.surf_move(*self.to_world(e.x, e.y))
            return
        if self.ai_stroke is not None:
            self.ai_pen_move(e)
            return
        if self.drag:
            kind, i = self.drag
            x, z = self.world_xy(e)
            if kind == "net":
                if self.road:
                    self.net_drag(i[0], i[1], x, z)
            elif kind in ("start", "finish"):
                if self.road:
                    self.set_line(kind, self.road.project(*self.to_world(e.x, e.y))[0])
            elif kind == "point":
                p = self.points()[i]
                p["x"], p["z"] = x, z
            else:
                it = self.map["pickups"][i]
                it["x"], it["z"] = round(x, 1), round(z, 1)
            self.redraw_soon()          # the checks / panel / 3D follow when the mouse is let go
        elif self.pan:
            self.pan_move(e)

    def on_up(self, e):
        if self.surf_stroke:
            self.surf_up()
            return
        if self.ai_stroke is not None:
            self.ai_pen_up(e)
            return
        if self.drag:
            self.drag = None
            self.changed()
        self.pan = None

    def on_hover(self, e):
        x, z = self.to_world(e.x, e.y)
        if self.view_mode.get() == "surface":           # the brush circle follows the mouse
            self.surf_mouse = (x, z)
            self.surf_draw_cursor()
            return
        if self.net_draft:                        # the branch being drawn follows the mouse
            self.mouse_xy = (e.x, e.y)
            self.redraw_soon()
            return
        if self.measure and not self.measure.get("done"):      # the measuring line follows the mouse
            self.mouse_world = (x, z)
            self.redraw_soon()
            return
        if self.road:
            s, lat = self.road.project(x, z)
            self.say("x %.0f  z %.0f   |   %.0f m along the road, %.0f m from its middle, height %.1f m   |   %s"
                     % (x, z, s, lat, self.road.h_at(s), dict((t[0], t[1]) for t in TOOLS)[self.tool.get()]), "#9aa8b8")

    # ================================================================================================ save / build
    def save(self):
        world_mod.save(self.world)
        try:
            os.remove(AUTOSAVE)
        except OSError:
            pass
        self.say("Saved %s" % os.path.normpath(world_mod.WORLD_FILE), "#58d06a")

    def help(self):
        top = tk.Toplevel(self.root)
        top.title("Keys / About")
        top.configure(bg=PANEL)
        tk.Label(top, text=HELP, justify="left", bg=PANEL, fg=FG, font=("Consolas", 10)).pack(padx=12, pady=12)
        tk.Label(top, text=CREDITS % (paths.VERSION, paths.DISCORD), justify="left", bg=PANEL, fg="#7fd0ff",
                 font=("Segoe UI", 9)).pack(padx=12, pady=(0, 8), anchor="w")
        tk.Button(top, text="Close", command=top.destroy).pack(pady=(0, 10))
        top.bind("<Escape>", lambda e: top.destroy())

    def build(self):
        if self.building:
            self.say("A build is already running", "#ff6060")
            return
        bad = []
        for li, loc in enumerate(self.world["locations"]):
            for mi, m in enumerate(loc["maps"]):
                try:
                    r = self.make_road(m)
                    if r.unbridged_crossings():
                        bad.append("%s / %s: crossing without a bridge" % (loc["name"], m["name"]))
                    if not MIN_LEN <= r.length <= MAX_LEN:
                        bad.append("%s / %s: length %.0f m" % (loc["name"], m["name"], r.length))
                except Exception as ex:
                    bad.append("%s / %s: %s" % (loc["name"], m["name"], ex))
        if bad and not messagebox.askyesno("Problems", "\n".join(bad) + "\n\nBuild anyway?"):
            return
        self.save()
        self.building = True
        self.build_btn.config(state="disabled", text="Building...")
        self.log_win = tk.Toplevel(self.root)
        self.log_win.title("Building")
        self.log_win.configure(bg=PANEL)
        head = tk.Frame(self.log_win, bg=PANEL)
        head.pack(fill="x", padx=10, pady=(10, 4))
        self.build_stage = tk.Label(head, text="Starting the build...", bg=PANEL, fg="white",
                                    font=("Segoe UI", 11, "bold"), anchor="w")
        self.build_stage.pack(side="left")
        self.build_pct_label = tk.Label(head, text="0 %", bg=PANEL, fg="#ffd23b", font=("Segoe UI", 11, "bold"))
        self.build_pct_label.pack(side="right")
        style = ttk.Style(self.log_win)
        style.theme_use("clam")
        style.configure("Build.Horizontal.TProgressbar", troughcolor=LIST_BG, background="#2d9d4d",
                        lightcolor="#3fbf63", darkcolor="#1f7a3a", bordercolor=LIST_BG)
        style.configure("Fail.Horizontal.TProgressbar", troughcolor=LIST_BG, background="#c03030",
                        lightcolor="#e04040", darkcolor="#902020", bordercolor=LIST_BG)
        self.build_bar = ttk.Progressbar(self.log_win, length=760, maximum=100, mode="determinate",
                                         style="Build.Horizontal.TProgressbar")
        self.build_bar.pack(fill="x", padx=10, pady=(0, 8))
        self.log_text = tk.Text(self.log_win, width=110, height=20, bg="#0a1424", fg="#4fc3ff", font=("Consolas", 9),
                                insertbackground="#4fc3ff", relief="flat", highlightthickness=1,
                                highlightbackground="#1c3556", padx=8, pady=6)
        self.log_text.tag_config("out", foreground="#4fc3ff")
        self.log_text.tag_config("err", foreground="#ff6b6b")
        self.log_text.pack(fill="both", expand=True, padx=10, pady=(0, 10))
        self.build_q = queue.Queue()
        # progress = the build's printed stage or the time against the last build, whichever is further
        self.build_t0 = time.time()
        self.build_stage_pct = 0.0
        self.build_maps = sum(len(l["maps"]) for l in self.world["locations"])
        self.build_maps_done = 0
        try:
            self.build_expected = max(10.0, float(open(BUILD_TIME_FILE).read()))
        except (OSError, ValueError):
            self.build_expected = 60.0
        self._progress_tick()
        threading.Thread(target=self._build, daemon=True).start()

    # build output line -> (progress %, stage text); maps are counted on the "road" lines
    BUILD_STAGES = (("start line at", 5, "Preparing the race lines"), ("building actor lists", 8, "Placing objects"),
                    ("building game database", 12, "Building the game database"),
                    ("database:", 50, "Game database done"), ("writing the pack", 55, "Writing gamedata.pak"),
                    ("built ", 66, "gamedata.pak written"), ("seamless road", 72, "Making the road models"),
                    ("root pack:", 92, "root.pak written (models, pictures, minimaps)"),
                    ("installing the packs", 96, "Installing into the game"),
                    ("pack: ", 0, None))

    def _stage_from_line(self, text):
        low = text.lower()
        if ": road " in low and " m (" in low:                       # "<location> / <map>: road 2015 m (...)"
            self.build_maps_done += 1
            pct = 12 + 36 * self.build_maps_done / max(1, self.build_maps)
            return pct, "Level files: map %d of %d" % (self.build_maps_done, self.build_maps)
        if "object light" in low:
            return None, "Lighting: light points along the roads"
        if "collision floor" in low:
            return None, "Collision floor (walls, bridges)"
        for key, pct, label in self.BUILD_STAGES:
            if low.startswith(key) or key in low:
                return (pct or None), label
        return None, None

    def _progress_tick(self):
        """every 100 ms on the window side: show the build's new lines (the build thread only fills build_q -
        touching tk from that thread froze the old window at 95 %), move the bar, finish when the thread says so"""
        done = None
        try:
            while True:
                item = self.build_q.get_nowait()
                if item[0] == "line":
                    self._show_line(item[1])
                else:
                    done = item
        except queue.Empty:
            pass
        if done:
            self._build_done(done[1], done[2])
            return
        if not self.building:
            return
        if self.log_win.winfo_exists():
            by_time = min(95.0, 95.0 * (time.time() - self.build_t0) / self.build_expected)
            pct = max(self.build_stage_pct, by_time)
            self.build_bar["value"] = pct
            self.build_pct_label.config(text="%d %%" % pct)
        self.root.after(100, self._progress_tick)

    def _build_finished(self, ok):
        if not self.log_win.winfo_exists():
            return
        self.build_bar.configure(style="Build.Horizontal.TProgressbar" if ok else "Fail.Horizontal.TProgressbar")
        self.build_bar["value"] = 100
        self.build_pct_label.config(text="100 %" if ok else "FAILED", fg="#58d06a" if ok else "#ff6060")
        self.build_stage.config(text="Built and installed" if ok else "The build failed (see below)")

    def _log(self, text):
        """(build thread) a line for the build window"""
        self.build_q.put(("line", text))

    def _show_line(self, text):
        """(window side) add a build line to the log and move the stage / bar"""
        if self.log_win.winfo_exists():
            tag = "err" if ("error" in text.lower() or "traceback" in text.lower() or "failed" in text.lower()) else "out"
            self.log_text.insert("end", text + "\n", tag)
            self.log_text.see("end")
        pct, stage = self._stage_from_line(text)
        if pct:
            self.build_stage_pct = max(self.build_stage_pct, pct)
        if stage and self.log_win.winfo_exists():
            self.build_stage.config(text=stage)

    def _build(self):
        ok, msg = False, ""
        try:
            if paths.game_running():
                self._log("Blur is running: the level files can't be replaced. Close Blur and build again.")
                raise RuntimeError("close Blur first")
            t0 = time.time()
            # a REAL build: without FE_DRY (the 3D view / AI tab set it in this process for their previews - the
            # build inherited it and skipped copying the collision floor into the game: walls / bridges gone)
            env = {k: v for k, v in os.environ.items() if k != "FE_DRY"}
            p = subprocess.Popen(paths.python_cmd(os.path.join(HERE, "flat_earth", "build.py")),
                                 cwd=os.path.join(HERE, "flat_earth"), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                 text=True, env=env, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            out = []
            for line in p.stdout:
                out.append(line)
                self._log(line.rstrip())
            p.wait()
            paths.ensure_dirs()
            open(BUILD_LOG, "w").write("".join(out))
            if p.returncode != 0:
                raise RuntimeError("build failed (see the window / %s)" % BUILD_LOG)
            try:
                open(BUILD_TIME_FILE, "w").write("%.1f" % (time.time() - t0))     # for the next progress bar
            except OSError:
                pass
            # Blur REBORN.exe reads its packs from rebrn\ (the game's own cache\ packs stay as they are)
            self._log("Installing the packs for Blur REBORN (rebrn\\)...")
            os.makedirs(paths.PACK_DIR, exist_ok=True)
            for src, name in ((paths.GAMEDATA_OUT, "gamedata.pak"), (paths.ROOT_OUT, "root.pak")):
                dst = os.path.join(paths.PACK_DIR, name)
                shutil.copyfile(src, dst + ".new")      # new file, then REPLACE: rebrn\ may hold hard links to the
                os.replace(dst + ".new", dst)           # game's original packs - never write into those
            ok, msg = True, "Built and installed in %d s." % (time.time() - t0)
        except Exception as ex:
            import traceback
            msg = str(ex) or ex.__class__.__name__
            self._log(traceback.format_exc().rstrip())
        finally:
            self._log(msg)
            self.build_q.put(("done", ok, msg))          # the window side (_progress_tick) finishes the build

    def _build_done(self, ok, msg):
        self.building = False
        self.build_btn.config(state="normal", text=BUILD_TEXT)
        self.say(msg, "#58d06a" if ok else "#ff6060")
        self._build_finished(ok)
        play_sound(ok)
        if ok:
            self.write_quickstart(False)            # "Blur Map Tester.exe" tests the map built last
            if self.log_win.winfo_exists():         # success: the build window closes itself (the log stays in
                self.log_win.after(1200, self.log_win.destroy)          # tracks\last_road_build.txt)
            self.play_dialog(msg)
        else:
            self.fail_dialog(msg)

    def fail_dialog(self, msg):
        top = tk.Toplevel(self.root)
        top.title("Build failed")
        top.configure(bg=PANEL)
        tk.Label(top, text="✖  The build failed", bg=PANEL, fg="#ff6060",
                 font=("Segoe UI Symbol", 13, "bold")).pack(padx=16, pady=(14, 4))
        tk.Label(top, text=msg, bg=PANEL, fg=FG, wraplength=520, justify="left").pack(padx=16, pady=4)
        tk.Label(top, text="The full log is in the Building window and in " + BUILD_LOG,
                 bg=PANEL, fg="#9aa8b8").pack(padx=16)
        tk.Button(top, text="OK", width=12, bg=DANGER, fg="white", command=top.destroy).pack(pady=(8, 14))
        top.bind("<Escape>", lambda e: top.destroy())
        top.bind("<Return>", lambda e: top.destroy())
        center_on_screen(top)
        bring_to_front(top)

    # ================================================================================================ play
    QUICKSTART = paths.QUICKSTART_TARGET

    def write_quickstart(self, on):
        """the Amax QuickStart plugin's target: custom LAN races start on the current map (on = False: off)"""
        def lua_list(items):
            return "{" + ", ".join('"%s"' % str(s).replace("\\", "\\\\").replace('"', '\\"') for s in items if s) + "}"
        loc, m = self.loc, self.map
        world_mod.assign_ids(self.world)                   # city_name / route_name as the build makes them
        text = ("-- written by the track designer (%s): the map the QuickStart plugin opens a custom LAN race on\n"
                "return {\n\tenabled = %s,\n\tauto = true,\n\tcity_names = %s,\n\troute_names = %s,\n"
                "\tcity_uid = %d,\n\troute_uid = %d,\n\tlaps = %s,\n}\n"
                % (time.strftime("%Y-%m-%d %H:%M"), "true" if on else "false",
                   lua_list([loc["name"], loc.get("city_name")]), lua_list([m["name"], m.get("route_name")]),
                   signed32(loc.get("city_uid", 0)), signed32(m.get("route_uid", 0)),
                   "%d" % m["laps"] if m.get("laps") else "nil"))
        # the game's custom race lists use the uids as SIGNED 32-bit ids (our FFFFFF20 = -224): small numbers.
        # (the unsigned uids ~4.29e9 crashed the game's Lua at start - lua5.1.dll trap)
        try:
            os.makedirs(os.path.dirname(self.QUICKSTART), exist_ok=True)
            with open(self.QUICKSTART, "w", encoding="utf-8", newline="\n") as f:      # no BOM (the game's Lua)
                f.write(text)
            return True
        except OSError as ex:
            self.say("could not write the QuickStart target: %s" % ex, "#ff6060")
            return False

    def start_game(self, lan):
        if paths.game_running():
            messagebox.showinfo("Blur is running", "Blur is already running - close it first, then press Play again.")
            return
        if not os.path.exists(paths.REBORN_EXE):
            messagebox.showinfo("Blur REBORN", "Blur REBORN.exe is not there yet - start the map builder again (its "
                                "first-start setup makes it).")
            return
        if not os.path.exists(os.path.join(paths.PACK_DIR, "root.pak")):
            messagebox.showinfo("Build first", "Press Build + install once first: Blur REBORN plays the built maps.")
            return
        self.write_quickstart(lan)
        subprocess.Popen([paths.REBORN_EXE], cwd=GAME)
        if lan:
            self.say("Starting Blur: Multiplayer > LAN > Host game (Y) - the race is set to %s / %s"
                     % (self.loc["name"], self.map["name"]), "#58d06a")
            self.quickstart_off_later()

    def quickstart_off_later(self, seen=None, waited=0):
        """the custom LAN race is ONE launch only: when the game that was started for it CLOSES, the target goes
        back to off - Blur started any other way starts normally. (A timer was too early: the plugin reads the
        target after the intro / login, which can take minutes.) The designer also turns it off when it opens."""
        running = paths.game_running()
        if running and seen is None:
            seen = time.time()
        if (seen is not None and not running) or (seen is None and waited > 600):
            self.write_quickstart(False)        # the game closed (or never started within 10 minutes)
            return
        self.root.after(3000, lambda: self.quickstart_off_later(seen, waited + 3))

    def play_dialog(self, msg=""):
        """after a build (or the Play button): start the game normally or straight onto this map in a LAN race"""
        top = tk.Toplevel(self.root)
        top.title("Play")
        top.configure(bg=PANEL)
        top.transient(self.root)
        if msg:
            tk.Label(top, text=msg, bg=PANEL, fg="#58d06a", wraplength=460, justify="left").pack(padx=14, pady=(12, 4))
        tk.Label(top, text="Start the game?", bg=PANEL, fg=FG, font=("Segoe UI", 11, "bold")).pack(padx=14, pady=4)

        def go(lan):
            top.destroy()
            if lan is not None:
                self.start_game(lan)
        tk.Button(top, text="▶  Custom LAN race on  %s  >  %s" % (self.loc["name"], self.map["name"]),
                  bg=ACCENT, fg="white", font=("Segoe UI Symbol", 10, "bold"), anchor="w",
                  command=lambda: go(True)).pack(fill="x", padx=14, pady=3)
        tk.Label(top, text="   in game: Multiplayer > LAN > Host game (Y): the lobby is already on this map",
                 bg=PANEL, fg="#9aa8b8").pack(anchor="w", padx=14)
        tk.Button(top, text="▶  Start the game normally", bg=BTN, fg=FG, font=UI_FONT, anchor="w",
                  command=lambda: go(False)).pack(fill="x", padx=14, pady=3)
        tk.Button(top, text="Not now", bg=BTN, fg=FG, font=UI_FONT,
                  command=lambda: go(None)).pack(fill="x", padx=14, pady=(3, 12))
        top.bind("<Escape>", lambda e: go(None))
        center_on_screen(top)
        bring_to_front(top)


CREDIT = "BLUR MAP BUILDER  BY MDPRO"
SPLASH_MS = 2500      # the start-up picture stays at least this long (a click closes it sooner)


def show_splash(root):
    """the start-up picture (credits; built into ui\\brand.py and checked there) in the middle of the screen while
    the designer loads; returns a function that closes it (after SPLASH_MS) and shows the main window"""
    pic = None
    try:
        from PIL import ImageTk
        from ui import brand
        im = brand.picture()              # None if brand.py was changed
        if im is not None:
            pic = ImageTk.PhotoImage(im, master=root)
    except Exception:      # noqa: BLE001 - a missing / changed picture: the credit as text instead
        pic = None
    top = tk.Toplevel(root, bg=BG)
    top.overrideredirect(True)
    top.attributes("-topmost", True)
    if pic is not None:
        tk.Label(top, image=pic, bd=0, bg=BG).pack()
    else:
        tk.Label(top, text=CREDIT, bg="#3a1d7a", fg="#ffffff", font=("Segoe UI", 28, "bold italic"),
                 width=34, height=5).pack()
    status = tk.Label(top, text="Loading...", bg=BG, fg=FG, anchor="w", font=("Segoe UI", 9))
    status.pack(fill="x", padx=10, pady=3)
    top.pic = pic
    root._splash = top         # load_world hides it while it asks a question
    center_on_screen(top)
    top.update()
    t0 = time.time()
    state = {"done": False, "on_show": None}

    def close(_e=None):
        if state["done"]:
            return
        state["done"] = True
        root._splash = None
        top.destroy()
        root.deiconify()
        root.lift()
        root.focus_force()
        if state["on_show"]:
            root.update_idletasks()
            state["on_show"]()

    def finish(on_show=None):
        state["on_show"] = on_show
        status.config(text="Ready")
        wait = max(0, SPLASH_MS - int((time.time() - t0) * 1000))
        top.after(wait, close)
    top.bind("<Button-1>", close)
    return finish


if __name__ == "__main__":
    root = tk.Tk()
    root.withdraw()
    splash_done = show_splash(root)
    root.geometry("1500x880")
    designer = Designer(root)
    splash_done(designer.fit)       # zoom to the map once the window has its real size
    root.mainloop()

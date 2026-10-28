"""
PATH SCATTER for the 3D view (view3d.View3D): draw a path on the ground, pick a few library objects, and they are
placed along it - spacing, which side, random turn / size / spread, a fill chance - with a live preview (pink
footprints). "Add" puts them all in the map's object list in one go (one Ctrl+Z takes them all away again). After
that they are normal placed objects: select, move, Ctrl+D, Delete as any other.
"""
import math
import random
import tkinter as tk

import numpy as np

import road_layout
from road_layout import actor_axes

BG, FG, DIM, HEAD = "#20242b", "#d8dee6", "#6f7c8a", "#8fb6e0"
SIDES = ("middle", "left", "right", "both sides")


def _num(var, default, lo=None, hi=None):
    try:
        v = float(str(var.get()).replace(",", "."))
    except (ValueError, tk.TclError):
        v = default
    if lo is not None:
        v = max(lo, v)
    if hi is not None:
        v = min(hi, v)
    return v


def path_spots(pts, items, opt, seed):
    """where the objects go: [{"id", "x", "y", "z", "turn" (radians), "scale"}] along the path pts [(x, y, z)].
    opt: spacing, side ("middle" / "left" / "right" / "both sides"), side_dist, fill (%), align (bool), turn (deg),
    turn_rand (deg, +-), size_min, size_max, spread (m, +-), order ("random" / "in turn")"""
    if len(pts) < 2 or not items:
        return []
    rng = random.Random(seed)
    p = np.array(pts, np.float64)
    seg = np.linalg.norm(p[1:, [0, 2]] - p[:-1, [0, 2]], axis=1)
    cum = np.r_[0.0, np.cumsum(seg)]
    total = float(cum[-1])
    if total < 1e-6:
        return []
    step = max(0.5, opt["spacing"])
    signs = {"middle": (0,), "left": (1,), "right": (-1,), "both sides": (1, -1)}[opt["side"]]
    out, n = [], 0
    for s in np.arange(0.0, total + 1e-6, step):
        i = min(int(np.searchsorted(cum, s, side="right")) - 1, len(seg) - 1)
        while i > 0 and seg[i] < 1e-9:
            i -= 1
        t = (s - cum[i]) / seg[i] if seg[i] > 1e-9 else 0.0
        pos = p[i] + (p[i + 1] - p[i]) * t
        d = (p[i + 1] - p[i])[[0, 2]] / max(seg[i], 1e-9)          # (dx, dz) along the path
        left = np.array((-d[1], d[0]))                               # (seen from above, walking the path; checked
        #                                                              in the 3D view's top view)
        for sign in signs:
            if rng.random() * 100.0 >= opt["fill"]:
                continue
            a = rng.uniform(-opt["spread"], opt["spread"])
            b = rng.uniform(-opt["spread"], opt["spread"])
            xz = pos[[0, 2]] + d * a + left * (sign * opt["side_dist"] + b)
            turn = math.atan2(d[0], d[1]) if opt["align"] else 0.0  # (model z -> along the path)
            if opt["align"] and sign < 0:
                turn += math.pi                                      # the right side faces the other way (mirrored)
            turn += math.radians(opt["turn"] + rng.uniform(-opt["turn_rand"], opt["turn_rand"]))
            turn = (turn + math.pi) % (2 * math.pi) - math.pi
            lo, hi = sorted((opt["size_min"], opt["size_max"]))
            item = items[n % len(items)] if opt["order"] == "in turn" else rng.choice(items)
            n += 1
            out.append({"id": item, "x": float(xz[0]), "y": float(pos[1]), "z": float(xz[1]), "turn": turn,
                        "scale": rng.uniform(lo, hi)})
    return out


class ScatterTools:
    """mixed into View3D: self.m, self.scene, self._point_under, self._set_objects, self.edits, self.refresh ..."""
    sc = None                  # the open tool: {"win", "pts", "drawing", "items", "seed", vars...}

    def scatter_open(self):
        if self.sc is not None:
            self.sc["win"].lift()
            return
        if self.m is None or self.mode != "3d":
            return
        self._stop_placing()
        self.select(None)
        win = tk.Toplevel(self)
        win.title("Path scatter")
        win.configure(bg=BG)
        win.transient(self.winfo_toplevel())
        win.resizable(False, False)
        win.protocol("WM_DELETE_WINDOW", self.scatter_close)
        sc = self.sc = {"win": win, "pts": [], "drawing": False, "items": [], "seed": 1}
        v = {k: tk.StringVar(value=d) for k, d in (("spacing", "10"), ("side_dist", "0"), ("fill", "100"),
                                                     ("turn", "0"), ("turn_rand", "0"), ("size_min", "1"),
                                                     ("size_max", "1"), ("spread", "0"))}
        v["side"] = tk.StringVar(value="middle")
        v["order"] = tk.StringVar(value="random")
        v["align"] = tk.BooleanVar(value=True)
        sc["vars"] = v

        def head(text):
            tk.Label(win, text=text, bg=BG, fg=HEAD, font=("Segoe UI", 9, "bold")).pack(anchor="w", padx=8, pady=(8, 1))

        def row():
            f = tk.Frame(win, bg=BG)
            f.pack(fill="x", padx=8, pady=1)
            return f

        def field(f, label, key, width=6):
            tk.Label(f, text=label, bg=BG, fg=FG, font=("Segoe UI", 9)).pack(side="left")
            tk.Entry(f, textvariable=v[key], width=width, bg="#1a1e24", fg=FG, insertbackground=FG, relief="flat",
                     justify="right").pack(side="left", padx=(3, 10))

        def button(f, text, cmd, bg="#2c3440"):
            b = tk.Button(f, text=text, command=cmd, bg=bg, fg="white", relief="flat", font=("Segoe UI Symbol", 9),
                          padx=6)
            b.pack(side="left", padx=(0, 4))
            return b

        head("1  PATH")
        f = row()
        sc["draw_btn"] = button(f, "✎ Draw path", self._sc_draw_toggle, "#1e9bff")
        button(f, "↶ Last point", self._sc_pop)
        button(f, "✖ Clear", self._sc_clear)
        sc["path_label"] = tk.Label(win, text="", bg=BG, fg=DIM, font=("Segoe UI", 8), justify="left")
        sc["path_label"].pack(anchor="w", padx=8)

        head("2  OBJECTS")
        tk.Label(win, text="Click objects in the OBJECT LIBRARY (left) to add them here", bg=BG, fg=DIM,
                 font=("Segoe UI", 8)).pack(anchor="w", padx=8)
        sc["list"] = tk.Listbox(win, height=5, width=46, bg="#08111f", fg=FG, selectbackground="#1e9bff",
                                relief="flat", highlightthickness=0, activestyle="none")
        sc["list"].pack(fill="x", padx=8, pady=2)
        f = row()
        button(f, "✖ Remove", self._sc_remove)
        for val in ("random", "in turn"):
            tk.Radiobutton(f, text=val, variable=v["order"], value=val, bg=BG, fg=FG, selectcolor="#1a1e24",
                           activebackground=BG, activeforeground=FG).pack(side="left")

        head("3  LAYOUT")
        f = row()
        field(f, "Every (m)", "spacing")
        field(f, "Fill (%)", "fill")
        f = row()
        tk.Label(f, text="Side", bg=BG, fg=FG, font=("Segoe UI", 9)).pack(side="left")
        om = tk.OptionMenu(f, v["side"], *SIDES)
        om.config(bg="#1a1e24", fg=FG, highlightthickness=0, activebackground="#2c3440", width=10, relief="flat")
        om.pack(side="left", padx=(3, 10))
        field(f, "Distance from path (m)", "side_dist")

        head("4  RANDOM")
        f = row()
        tk.Checkbutton(f, text="Face along the path", variable=v["align"], bg=BG, fg=FG, selectcolor="#1a1e24",
                       activebackground=BG, activeforeground=FG).pack(side="left")
        field(f, "  + turn (°)", "turn", 5)
        f = row()
        field(f, "Random turn ± (°)", "turn_rand", 5)
        field(f, "Spread ± (m)", "spread", 5)
        f = row()
        field(f, "Size from", "size_min", 5)
        field(f, "to", "size_max", 5)
        button(f, "🎲 New random", self._sc_reroll)
        tk.Label(win, text="(breakable objects keep their size)", bg=BG, fg=DIM, font=("Segoe UI", 8)).pack(
            anchor="w", padx=8)

        f = row()
        f.pack_configure(pady=(10, 8))
        sc["add_btn"] = button(f, "✔ Add", self.scatter_apply, "#1f9d55")
        button(f, "Close", self.scatter_close, "#4a2a2a")
        sc["info"] = tk.Label(win, text="", bg=BG, fg="#f0e080", font=("Segoe UI", 9), wraplength=330, justify="left")
        sc["info"].pack(anchor="w", padx=8, pady=(0, 8))

        for var in v.values():
            var.trace_add("write", lambda *a: self._sc_update())
        top = self.winfo_toplevel()
        win.update_idletasks()
        win.geometry("+%d+%d" % (top.winfo_rootx() + top.winfo_width() - win.winfo_reqwidth() - 300,
                                 top.winfo_rooty() + 120))
        self._sc_draw_toggle(True)

    def scatter_close(self):
        if self.sc is None:
            return
        try:
            self.sc["win"].destroy()
        except tk.TclError:
            pass
        self.sc = None
        if self.gl is not None:
            self.gl.config(cursor="")
        self.refresh()

    # ---- the path (clicks in the 3D view while "Draw path" is on)
    def _sc_drawing(self):
        return self.sc is not None and self.sc["drawing"]

    def _sc_draw_toggle(self, on=None):
        sc = self.sc
        sc["drawing"] = (not sc["drawing"]) if on is None else on
        sc["draw_btn"].config(text="✔ Done drawing" if sc["drawing"] else "✎ Draw path",
                              bg="#c08a1a" if sc["drawing"] else "#1e9bff")
        if self.gl is not None:
            self.gl.config(cursor="pencil" if sc["drawing"] else "")
            if sc["drawing"]:
                self.gl.focus_set()
        self._sc_update()

    def _sc_click(self, mx, my):
        p = self._point_under(mx, my)
        if p is not None:
            self.sc["pts"].append(tuple(float(c) for c in p[:3]))
            self._sc_update()

    def _sc_pop(self):
        if self.sc and self.sc["pts"]:
            self.sc["pts"].pop()
            self._sc_update()

    def _sc_clear(self):
        self.sc["pts"] = []
        self._sc_update()

    # ---- objects
    def _sc_lib_add(self, e):
        self.sc["items"].append(e["id"])
        self.sc["list"].insert("end", "%s   (%s)" % (e["name"], road_layout.KIND_TEXT.get(e["kind"], e["kind"])))
        self._sc_update()

    def _sc_remove(self):
        s = self.sc["list"].curselection()
        i = s[0] if s else len(self.sc["items"]) - 1
        if i >= 0:
            self.sc["items"].pop(i)
            self.sc["list"].delete(i)
            self._sc_update()

    def _sc_reroll(self):
        self.sc["seed"] += 1
        self._sc_update()

    # ---- preview + add
    def _sc_options(self):
        v = self.sc["vars"]
        return {"spacing": _num(v["spacing"], 10, 0.5), "side": v["side"].get(), "side_dist": _num(v["side_dist"], 0),
                "fill": _num(v["fill"], 100, 0, 100), "align": bool(v["align"].get()), "turn": _num(v["turn"], 0),
                "turn_rand": _num(v["turn_rand"], 0, 0, 180), "size_min": _num(v["size_min"], 1, 0.05, 50),
                "size_max": _num(v["size_max"], 1, 0.05, 50), "spread": _num(v["spread"], 0, 0),
                "order": v["order"].get()}

    def _sc_update(self):
        sc = self.sc
        if sc is None:
            return
        pts = sc["pts"]
        length = sum(math.hypot(b[0] - a[0], b[2] - a[2]) for a, b in zip(pts, pts[1:]))
        sc["path_label"].config(text="%d point(s), %.0f m%s" % (
            len(pts), length, "   -   click on the ground in the 3D view to add points; Backspace = remove the last, "
                              "Enter / right-click = done" if sc["drawing"] else ""))
        sc["spots"] = path_spots(pts, sc["items"], self._sc_options(), sc["seed"])
        tiles_free, lamps_free = self._sc_room()
        cat = road_layout.catalog()
        for spot in sc["spots"]:                       # the game's lists have fixed room: the rest can't show
            e = cat.get(spot["id"]) or {}
            if e.get("kind") == "breakable":
                spot["fits"] = lamps_free > 0
                lamps_free -= spot["fits"]
            else:
                need = int(e.get("pieces") or 1) if e.get("kind") == "import" else 1
                spot["fits"] = need <= tiles_free
                tiles_free -= need if spot["fits"] else 0
        n = sum(1 for spot in sc["spots"] if spot["fits"])
        lost = len(sc["spots"]) - n
        sc["add_btn"].config(text="✔ Add %d object%s" % (n, "" if n == 1 else "s"), state="normal" if n else "disabled")
        if len(pts) < 2:
            sc["info"].config(text="Draw a path: at least 2 points.")
        elif not sc["items"]:
            sc["info"].config(text="Now pick objects in the library on the left.")
        elif lost:
            sc["info"].config(text="Pink = where they go. RED = %d that do NOT fit: a map has 144 places for big "
                                   "objects (road pieces use some; each of your own models needs one place per piece). "
                                   "Only the pink ones are added - fewer copies (bigger Every / lower Fill) or smaller "
                                   "models help." % lost)
        else:
            sc["info"].config(text="Pink = where they go. Change the numbers, then Add.")
        self.refresh()

    def _sc_room(self):
        """(places left in the tile list, in the breakable objects' list) for more placed objects"""
        objs = (self.m or {}).get("objects") or []
        cat = road_layout.catalog()
        breakables = sum(1 for o in objs if (cat.get(o.get("id")) or {}).get("kind") == "breakable")
        return (self.scene or {}).get("tile_free", 144), road_layout.LAMP_PLACED_MAX - breakables

    def scatter_apply(self):
        sc = self.sc
        spots = sc.get("spots") or []
        if not spots or self.m is None:
            return
        g = self.scene.get("ground", 0.0) if self.scene else 0.0
        cat = road_layout.catalog()
        objs = [dict(o) for o in self.m.get("objects") or []]
        edits = self.edits()
        spots = [s for s in spots if s["fits"]]
        group = self._new_group_name("Scatter")        # all of one Add = one group (click its line = all of them)
        self._outl_open.add("group:" + group)
        for s in spots:
            objs.append({"id": s["id"], "x": round(s["x"], 2), "z": round(s["z"], 2), "h": round(max(0.0, s["y"] - g), 2),
                         "turn": round(s["turn"], 4), "group": group})
            if abs(s["scale"] - 1.0) > 1e-3 and (cat.get(s["id"]) or {}).get("kind") != "breakable":
                edits["obj:%d" % (len(objs) - 1)] = {"scale": round(s["scale"], 3)}
        sc["pts"] = []
        sc["seed"] += 1
        self._sc_draw_toggle(True)
        sc["info"].config(text="Added %d objects (Ctrl+Z takes them all away). Draw the next path, or Close."
                          % len(spots))
        self._set_objects(objs, edits)

    def _sc_draw_gl(self):
        """the path (cyan) and the preview footprints (pink), seen through everything"""
        sc = self.sc
        if sc is None:
            return
        from OpenGL import GL as gl
        gl.glDisable(gl.GL_DEPTH_TEST)
        pts = sc["pts"]
        if pts:
            gl.glLineWidth(3.0)
            gl.glColor4f(.2, .9, 1, 1)
            gl.glBegin(gl.GL_LINE_STRIP)
            for p in pts:
                gl.glVertex3f(p[0], p[1] + .3, p[2])
            gl.glEnd()
            gl.glPointSize(9.0)
            gl.glBegin(gl.GL_POINTS)
            for p in pts:
                gl.glVertex3f(p[0], p[1] + .3, p[2])
            gl.glEnd()
        cat = road_layout.catalog()
        gl.glLineWidth(1.5)
        for s in sc.get("spots") or []:
            gl.glColor4f(*((1, .35, .85, 1) if s.get("fits", True) else (1, .1, .1, 1)))
            e = cat.get(s["id"]) or {}
            if e.get("sized"):
                (x0, _, z0), (x1, _, z1) = e["lo"], e["hi"]
            else:
                x0, z0, x1, z1 = -1.0, -1.0, 1.0, 1.0
            k = s["scale"] if e.get("kind") != "breakable" else 1.0
            ax, _, az = actor_axes(s["turn"])
            gl.glBegin(gl.GL_LINE_LOOP)
            for a, b in ((x0, z0), (x1, z0), (x1, z1), (x0, z1)):
                gl.glVertex3f(s["x"] + (ax[0] * a + az[0] * b) * k, s["y"] + .25, s["z"] + (ax[2] * a + az[2] * b) * k)
            gl.glEnd()
            gl.glBegin(gl.GL_LINES)                     # the front (model z): which way it faces
            gl.glVertex3f(s["x"], s["y"] + .25, s["z"])
            gl.glVertex3f(s["x"] + az[0] * z1 * k, s["y"] + .25, s["z"] + az[2] * z1 * k)
            gl.glEnd()
        gl.glLineWidth(1.0)
        gl.glPointSize(1.0)
        gl.glEnable(gl.GL_DEPTH_TEST)

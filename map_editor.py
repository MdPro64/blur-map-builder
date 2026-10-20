"""Map editor for "My First Track" (LA River, route USA_LA_River_7).

Shows the whole track from above (race route, AI lines, start/checkpoints, start grid, power-ups, flares,
objects) and lets you change it, then builds the game files:
  - "Start line" tool: click on the blue race route = the start line goes there (checkpoints follow)
  - "Power-ups" tool: drag a power-up to move it; right-click it to change its type
  - track name / location name boxes
  - "Save" writes tracks/my_first_track.json; "Build + install" builds the pack (build_track.py), copies it to
    _mod_project/mod_packs and, if Blur is closed, into the game. Then play with "Play modded Blur.bat".
Mouse: wheel = zoom, right-drag (empty space) or middle-drag = move the map.
Works without the game running (it reads the unpacked game files)."""
import os
import shutil
import subprocess
import sys
import threading
import tkinter as tk
from tkinter import messagebox

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [os.path.join(HERE, p) for p in ("kslf", "aid")]
import aid          # noqa: E402
import build_track as B   # noqa: E402
import dump         # noqa: E402

PICKUP_COLORS = {"Shunt": "#ff4040", "Barge": "#ff9f1a", "Shock": "#3aa0ff", "Mine": "#ffe14a",
                 "Bolt": "#b070ff", "Nos": "#40e0ff", "Shield": "#40ff70", "Repair": "#ffffff"}


class Data:
    """everything the editor draws, read from the unpacked game files"""

    def __init__(self):
        self.d6 = open(os.path.join(B.U, "levels", B.CITY, B.SRC_ROUTE + ".aid"), "rb").read()
        self.spl = {s[0]: s for s in aid.splines(self.d6)}
        self.L = self.spl["LOGIC"]
        self.m = self.L[3] - 1
        self.lines = {}
        for name, s in self.spl.items():
            self.lines[name] = [B.logic_xz(self.d6, s, i) for i in range(s[3])]
        self.width = [aid.point(self.d6, self.L[4] + i * aid.POINT)[3] for i in range(self.m)]
        self.frames = [B.frame(self.d6, self.L, i) for i in range(self.m)]
        P = dump.File(os.path.join(B.U, "levels", B.CITY, "actorpack.xt"))
        u2o = {"%08X" % u: oi for u, t, oi in P.refs if oi != 0xFFFF}

        def actors(uid):
            return [a for a in P.top(P.O[u2o[uid]])["LevelActors"] if isinstance(a, dict) and "WorldMatrix" in a]
        self.pickups = {}                    # list uid -> [(name, x, z, type uid)]
        for lst in B.PICKUP_LISTS:
            self.pickups[lst] = [(a["Name"].split("_")[0], a["WorldMatrix"]["aw"]["vx"], a["WorldMatrix"]["aw"]["vz"],
                                  a["PickupType"]["uid"]) for a in actors(lst)]
        self.types = {}
        for lst in self.pickups.values():
            for name, _, _, t in lst:
                self.types.setdefault(name, t)
        self.type_name = {t: n for n, t in self.types.items()}
        self.objects = []                    # grey dots: every placed object of the level (shows its shape)
        for oi, o in enumerate(P.O):
            for a in P.top(o)["LevelActors"]:
                if isinstance(a, dict) and a.get("_type") in ("PhysicalObjectActor", "DynamicObjActor"):
                    w = a["WorldMatrix"]["aw"]
                    self.objects.append((w["vx"], w["vz"]))
        self.grid = []                       # start grid of the last build (if any)
        built = os.path.join(B.W, "actorpack.xt")
        if os.path.exists(built):
            try:
                N = dump.File(built)
                n2 = {"%08X" % u: oi for u, t, oi in N.refs if oi != 0xFFFF}
                for lst in ("FFFFFF11",):
                    for a in N.top(N.O[n2[lst]])["LevelActors"]:
                        if a.get("_type") == "StartPosition":
                            self.grid.append((a["WorldMatrix"]["aw"]["vx"], a["WorldMatrix"]["aw"]["vz"]))
            except Exception:
                self.grid = []

    def line_ends(self, i):
        """the two ends of a gate line across the road at Logic point i (the point is the road edge)"""
        x, z, a = self.frames[i % self.m]
        w = self.width[i % self.m]
        side = B.road_side_sign(self)
        return (x, z), (x + side * w * (B.math.cos(a)), z - side * w * B.math.sin(a))


class Editor:
    def __init__(self, root):
        self.root = root
        root.title("Blur map editor - My First Track")
        self.data = Data()
        self.cfg = B.load_settings()
        self.cfg.setdefault("pickups", {})
        self.show_list = "5AAC9F7D"          # multiplayer power-ups (custom races are multiplayer)
        self.scale, self.ox, self.oz = 0.5, 0.0, 0.0
        self.tool = tk.StringVar(value="start")
        self.drag = None
        self.layers = {k: tk.BooleanVar(value=True) for k in
                       ("Race route", "AI lines", "Objects", "Power-ups", "Start grid", "Checkpoints")}
        bar = tk.Frame(root)
        bar.pack(side="top", fill="x")
        tk.Label(bar, text="Tool:").pack(side="left")
        tk.Radiobutton(bar, text="Start line", variable=self.tool, value="start").pack(side="left")
        tk.Radiobutton(bar, text="Power-ups", variable=self.tool, value="pickup").pack(side="left")
        tk.Label(bar, text="  Track name:").pack(side="left")
        self.name = tk.Entry(bar, width=18)
        self.name.insert(0, self.cfg.get("name", "My First Track"))
        self.name.pack(side="left")
        tk.Label(bar, text=" Location:").pack(side="left")
        self.locname = tk.Entry(bar, width=12)
        self.locname.insert(0, self.cfg.get("location_name", "MY River"))
        self.locname.pack(side="left")
        tk.Button(bar, text="Reset power-ups", command=self.reset_pickups).pack(side="left", padx=4)
        tk.Button(bar, text="Save", command=self.save).pack(side="left", padx=4)
        tk.Button(bar, text="Build + install", command=self.build, bg="#2d7d2d", fg="white").pack(side="left", padx=4)
        lay = tk.Frame(root)
        lay.pack(side="top", fill="x")
        tk.Label(lay, text="Show:").pack(side="left")
        for k, v in self.layers.items():
            tk.Checkbutton(lay, text=k, variable=v, command=self.redraw).pack(side="left")
        self.status = tk.Label(root, anchor="w", text="")
        self.status.pack(side="bottom", fill="x")
        self.c = tk.Canvas(root, width=1100, height=750, bg="#15181d", highlightthickness=0)
        self.c.pack(fill="both", expand=True)
        self.c.bind("<Configure>", lambda e: self.redraw())
        self.c.bind("<MouseWheel>", self.on_wheel)
        self.c.bind("<ButtonPress-1>", self.on_down)
        self.c.bind("<B1-Motion>", self.on_move)
        self.c.bind("<ButtonRelease-1>", self.on_up)
        self.c.bind("<ButtonPress-2>", self.pan_start)
        self.c.bind("<B2-Motion>", self.pan_move)
        self.c.bind("<ButtonPress-3>", self.on_right)
        self.c.bind("<B3-Motion>", self.pan_move)
        self.c.bind("<Motion>", self.on_hover)
        xs = [p[0] for p in self.data.lines["LOGIC"]]
        zs = [p[1] for p in self.data.lines["LOGIC"]]
        self.ox, self.oz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
        self.root.after(50, self.fit)

    # ---- view ----
    def fit(self):
        xs = [p[0] for p in self.data.lines["LOGIC"]]
        zs = [p[1] for p in self.data.lines["LOGIC"]]
        w, h = max(self.c.winfo_width(), 200), max(self.c.winfo_height(), 200)
        self.scale = 0.9 * min(w / (max(xs) - min(xs) + 1), h / (max(zs) - min(zs) + 1))
        self.redraw()

    def to_screen(self, x, z):
        w, h = self.c.winfo_width(), self.c.winfo_height()
        return w / 2 + (x - self.ox) * self.scale, h / 2 - (z - self.oz) * self.scale

    def to_world(self, sx, sy):
        w, h = self.c.winfo_width(), self.c.winfo_height()
        return self.ox + (sx - w / 2) / self.scale, self.oz - (sy - h / 2) / self.scale

    def on_wheel(self, e):
        x, z = self.to_world(e.x, e.y)
        self.scale *= 1.2 if e.delta > 0 else 1 / 1.2
        nx, nz = self.to_world(e.x, e.y)
        self.ox += x - nx
        self.oz += z - nz
        self.redraw()

    def pan_start(self, e):
        self.pan = (e.x, e.y, self.ox, self.oz)

    def pan_move(self, e):
        if getattr(self, "pan", None):
            x0, y0, ox, oz = self.pan
            self.ox = ox - (e.x - x0) / self.scale
            self.oz = oz + (e.y - y0) / self.scale
            self.redraw()

    # ---- drawing ----
    def poly(self, pts, color, width=1, tag=None):
        flat = []
        for x, z in pts:
            flat += self.to_screen(x, z)
        if len(flat) >= 4:
            self.c.create_line(*flat, fill=color, width=width, tags=tag)

    def pickup_list(self):
        """current power-up positions of the shown list (with the edits applied)"""
        edits = self.cfg["pickups"].get(self.show_list, {})
        out = []
        for i, (name, x, z, t) in enumerate(self.data.pickups[self.show_list]):
            ed = edits.get(str(i), {})
            t = ed.get("type", t)
            out.append((self.data.type_name.get(t, name), ed.get("x", x), ed.get("z", z), t, bool(ed)))
        return out

    def redraw(self):
        c, d = self.c, self.data
        c.delete("all")
        if self.layers["Objects"].get():
            for x, z in d.objects:
                sx, sy = self.to_screen(x, z)
                c.create_rectangle(sx, sy, sx + 1, sy + 1, outline="#444a52")
        if self.layers["AI lines"].get():
            for name, pts in d.lines.items():
                if name not in ("LOGIC", "Start_Finish", "Inter_One", "Inter_Two"):
                    self.poly(pts, "#3f8f4f" if name == "MainLoop" else "#2f5f3f", 2 if name == "MainLoop" else 1)
        if self.layers["Race route"].get():
            self.poly(d.lines["LOGIC"], "#3a7bff", 2)
            # the road: the route points are its edge, it runs `width` m to one side
            side = road_side(d)
            edge2 = [(x + side * w * B.math.cos(a), z - side * w * B.math.sin(a))
                     for (x, z, a), w in zip(d.frames, d.width)]
            self.poly(edge2 + edge2[:1], "#24407a", 1)
        start = int(self.cfg.get("start", 699)) % d.m
        if self.layers["Checkpoints"].get():
            for off, color, label in ((0, "#ff3b3b", "START"), (B.SPLIT1, "#ffd23b", "split 1"),
                                      (B.SPLIT2, "#ffd23b", "split 2")):
                i = (start + off) % d.m
                x, z, a = d.frames[i]
                side = road_side(d)
                w = d.width[i]
                p2 = (x + side * w * B.math.cos(a), z - side * w * B.math.sin(a))
                self.poly([(x, z), p2], color, 4)
                sx, sy = self.to_screen(*p2)
                c.create_text(sx + 6, sy, text=label, fill=color, anchor="w", font=("Segoe UI", 9, "bold"))
            # race direction arrow at the start
            x, z, a = d.frames[start]
            mx, mz = x + road_side(d) * d.width[start] / 2 * B.math.cos(a), z - road_side(d) * d.width[start] / 2 * B.math.sin(a)
            ax, az = mx + 40 * B.math.sin(a), mz + 40 * B.math.cos(a)
            c.create_line(*self.to_screen(mx, mz), *self.to_screen(ax, az), fill="#ff3b3b", width=3, arrow="last")
        if self.layers["Start grid"].get() and d.grid and self.cfg.get("built_start") == start:
            for x, z in d.grid:
                sx, sy = self.to_screen(x, z)
                c.create_rectangle(sx - 3, sy - 3, sx + 3, sy + 3, outline="#ffffff")
        if self.layers["Power-ups"].get():
            for i, (name, x, z, t, edited) in enumerate(self.pickup_list()):
                sx, sy = self.to_screen(x, z)
                r = 6 if edited else 4
                c.create_oval(sx - r, sy - r, sx + r, sy + r, fill=PICKUP_COLORS.get(name, "#cccccc"),
                              outline="#ffffff" if edited else "#000000", tags=("pickup", "p%d" % i))
        c.create_text(10, 10, anchor="nw", fill="#cfd6df", font=("Segoe UI", 9), text=(
            "blue = race route (edge of the road), green = AI lines, red = START, yellow = checkpoints, "
            "dots = power-ups (big = changed), white squares = start grid of the last build"))
        self.status.config(text="Start line at route point %d.  Tool: %s.  Wheel = zoom, right/middle-drag = move map."
                           % (start, "click the blue route to put the start there" if self.tool.get() == "start"
                              else "drag a power-up to move it, right-click it to change its type"))

    # ---- editing ----
    def nearest_pickup(self, sx, sy):
        best = None
        for i, (name, x, z, t, e) in enumerate(self.pickup_list()):
            px, py = self.to_screen(x, z)
            dd = (px - sx) ** 2 + (py - sy) ** 2
            if dd < 120 and (best is None or dd < best[0]):
                best = (dd, i)
        return best[1] if best else None

    def on_down(self, e):
        if self.tool.get() == "start":
            x, z = self.to_world(e.x, e.y)
            d = self.data
            i = min(range(d.m), key=lambda k: (d.frames[k][0] - x) ** 2 + (d.frames[k][1] - z) ** 2)
            fx, fz, _ = d.frames[i]
            if (fx - x) ** 2 + (fz - z) ** 2 > (40 / self.scale) ** 2 + 60 ** 2:
                self.status.config(text="Click closer to the blue race route.")
                return
            self.cfg["start"] = i
            self.cfg.pop("ground_y", None)   # the builder takes the road height from the AI line
            self.redraw()
        else:
            self.drag = self.nearest_pickup(e.x, e.y)

    def on_move(self, e):
        if self.drag is not None:
            x, z = self.to_world(e.x, e.y)
            ed = self.cfg["pickups"].setdefault(self.show_list, {}).setdefault(str(self.drag), {})
            ed["x"], ed["z"] = x, z
            self.redraw()

    def on_up(self, e):
        self.drag = None

    def on_right(self, e):
        self.pan = None
        if self.tool.get() == "pickup":
            i = self.nearest_pickup(e.x, e.y)
            if i is not None:
                menu = tk.Menu(self.root, tearoff=0)
                for name, t in sorted(self.data.types.items()):
                    menu.add_command(label=name, command=lambda t=t, i=i: self.set_type(i, t))
                menu.tk_popup(e.x_root, e.y_root)
                return
        self.pan_start(e)

    def set_type(self, i, t):
        self.cfg["pickups"].setdefault(self.show_list, {}).setdefault(str(i), {})["type"] = t
        self.redraw()

    def on_hover(self, e):
        if self.tool.get() == "pickup":
            i = self.nearest_pickup(e.x, e.y)
            if i is not None:
                name, x, z, t, ed = self.pickup_list()[i]
                self.status.config(text="Power-up %d: %s at (%.0f, %.0f)%s" % (i, name, x, z, "  (changed)" if ed else ""))

    def reset_pickups(self):
        self.cfg["pickups"] = {}
        self.redraw()

    def save(self):
        self.cfg["name"] = self.name.get().strip() or "My First Track"
        self.cfg["location_name"] = self.locname.get().strip() or "MY River"
        # the game uses the multiplayer list in custom races; keep single player the same
        self.cfg["pickups"]["ECFB5B8B"] = self.cfg["pickups"].get("5AAC9F7D", {})
        B.save_settings(self.cfg)
        self.status.config(text="Saved " + B.TRACK_FILE)

    def build(self):
        self.save()
        self.status.config(text="Building... (about a minute)")
        threading.Thread(target=self._build, daemon=True).start()

    def _build(self):
        try:
            out = B.build(self.cfg, log=lambda m: self.root.after(0, lambda m=m: self.status.config(text=m)))
            packs = os.path.join(B.PROJ, "mod_packs")
            shutil.copy(out, os.path.join(packs, "gamedata.pak"))
            running = "blur" in subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
            if not running:
                game = os.path.dirname(B.PROJ)
                shutil.copy(os.path.join(packs, "gamedata.pak"), os.path.join(game, "cache", "gamedata.pak"))
                shutil.copy(os.path.join(packs, "root.pak"), os.path.join(game, "cache", "root.pak"))
            self.cfg["built_start"] = int(self.cfg["start"]) % self.data.m
            B.save_settings(self.cfg)
            msg = ("Built and installed. Start the game with 'Play modded Blur.bat'." if not running else
                   "Built. Blur is running: close it, then use 'Play modded Blur.bat' (it installs the new pack).")
            self.root.after(0, lambda: (self.reload_grid(), self.status.config(text=msg), messagebox.showinfo("Done", msg)))
        except Exception:
            import traceback
            msg = traceback.format_exc()
            open(os.path.join(HERE, "tracks", "last_build_error.txt"), "w").write(msg)
            self.root.after(0, lambda msg=msg: messagebox.showerror(
                "Build failed", msg.strip().splitlines()[-1] +
                "\n\n(details: map_builder\\tracks\\last_build_error.txt)"))

    def reload_grid(self):
        self.data = Data()
        self.redraw()


def road_side(d):
    """+1 or -1: on which side of the route points the road lies (route 6's START text is mid-road)"""
    if not hasattr(d, "_side"):
        sx, sz, sa = d.frames[B.ROUTE6_LINE]
        t = B.START_TEXT
        d._side = 1.0 if (t[0] - sx) * B.math.cos(sa) - (t[1] - sz) * B.math.sin(sa) > 0 else -1.0
    return d._side


B.road_side_sign = road_side

if __name__ == "__main__":
    root = tk.Tk()
    Editor(root)
    root.mainloop()

"""CUSTOM CAR tab: the player's own cars (world["cars"]). Each is a copy of a game car of the same class with its
own name, icon, paint and stats. The stats can never beat the class: every stat stays inside the range of the
class's real cars, and all of them together inside the class's point budget (cars_data.limits)."""
import os
import re
import shutil
import threading
import tkinter as tk
from tkinter import colorchooser, filedialog, ttk

import paths

import blur_theme as T
import cars_data as cd

BARS_W, ROW_H = 520, 46


def internal_name(name):
    """the car's name inside the game files (the game finds its model by it): letters, digits, _"""
    return "BMB_" + (re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_") or "car")


class CarPanel(tk.Frame):
    def __init__(self, parent, designer):
        super().__init__(parent, bg=T.BG)
        self.d = designer
        self.sel = 0
        self.loading = False
        # ---- left: the list of custom cars
        left = tk.Frame(self, bg=T.PANEL, width=240)
        left.pack(side="left", fill="y")
        left.pack_propagate(False)
        tk.Label(left, text="MY CARS", bg=T.PANEL, fg=T.HEAD, font=("Segoe UI", 9, "bold")).pack(anchor="w", padx=10, pady=(10, 4))
        row = tk.Frame(left, bg=T.PANEL)
        row.pack(fill="x", padx=10, pady=(0, 8))
        tk.Button(row, text="+ New", command=self.add).pack(side="left")
        tk.Button(row, text="Copy", command=self.copy).pack(side="left", padx=4)
        tk.Button(row, text="Delete", command=self.delete).pack(side="left")
        self.listbox = tk.Listbox(left, bg=T.FIELD, fg=T.TEXT, selectbackground=T.BLUE, relief="flat",
                                  highlightthickness=0, font=("Segoe UI", 10), activestyle="none", exportselection=False)
        self.listbox.pack(fill="both", expand=True, padx=10)
        self.listbox.bind("<<ListboxSelect>>", lambda e: self._pick())
        # ---- right: the selected car
        self.right = tk.Frame(self, bg=T.BG)
        self.right.pack(side="left", fill="both", expand=True, padx=16, pady=10)
        r = self.right
        top = tk.Frame(r, bg=T.BG)
        top.pack(fill="x")
        tk.Label(top, text="Name", bg=T.BG, fg=T.MUTED).grid(row=0, column=0, sticky="w")
        self.name = tk.StringVar()
        e = tk.Entry(top, textvariable=self.name, width=24, font=("Segoe UI", 11))
        e.grid(row=1, column=0, sticky="w", padx=(0, 16))
        self.name.trace_add("write", lambda *a: self._set("name", self.name.get().strip()[:24] or "My car"))
        tk.Label(top, text="Class", bg=T.BG, fg=T.MUTED).grid(row=0, column=1, sticky="w")
        cls = tk.Frame(top, bg=T.BG)
        cls.grid(row=1, column=1, sticky="w", padx=(0, 16))
        self.cls = tk.IntVar()
        for i, c in enumerate(cd.CLASSES):
            tk.Radiobutton(cls, text=" %s " % c, variable=self.cls, value=i, indicatoron=False,
                           command=self._class_changed, font=("Segoe UI", 10, "bold")).pack(side="left", padx=1)
        tk.Label(top, text="Body (a game car of this class)", bg=T.BG, fg=T.MUTED).grid(row=0, column=2, sticky="w")
        self.base = ttk.Combobox(top, state="readonly", width=34)
        self.base.grid(row=1, column=2, sticky="w", padx=(0, 16))
        self.base.bind("<<ComboboxSelected>>", lambda e: self._base_changed())
        tk.Label(top, text="Menu icon", bg=T.BG, fg=T.MUTED).grid(row=0, column=3, sticky="w")
        self.icon = ttk.Combobox(top, state="readonly", width=30, values=sorted({c["icon"] for c in cd.game_cars()}))
        self.icon.grid(row=1, column=3, sticky="w")
        self.icon.bind("<<ComboboxSelected>>", lambda e: self._set("icon", self.icon.get()))
        paint = tk.Frame(r, bg=T.BG)
        paint.pack(fill="x", pady=(12, 0))
        tk.Label(paint, text="Paint", bg=T.BG, fg=T.MUTED).pack(side="left")
        self.paint_btns = []
        for i in range(2):
            sw = tk.Canvas(paint, width=34, height=22, highlightthickness=1, highlightbackground=T.EDGE,
                           cursor="hand2")
            sw.pack(side="left", padx=(8, 2))
            sw.bind("<Button-1>", lambda e, i=i: self._paint(i))
            tk.Button(paint, text="colour %d" % (i + 1), command=lambda i=i: self._paint(i)).pack(side="left")
            self.paint_btns.append(sw)
        tk.Label(paint, text="Wheels", bg=T.BG, fg=T.MUTED).pack(side="left", padx=(24, 4))
        self.wheels = ttk.Combobox(paint, state="readonly", width=34, values=[c["name"] for c in cd.game_cars()])
        self.wheels.pack(side="left")
        self.wheels.bind("<<ComboboxSelected>>", lambda e: self._set("wheels", self.wheels.get()))
        tk.Button(paint, text="⇩ Import body (.glb)", command=self._import_body).pack(side="left", padx=(24, 4))
        self.body_btn = tk.Button(paint, text="Use the game body", command=lambda: self._set("body_glb", None))
        self.body_btn.pack(side="left")
        # ---- stats: one slider per stat + the bar chart against the class
        tk.Label(r, text="STATS  (each one stays inside the class's range; all together inside its point budget)",
                 bg=T.BG, fg=T.HEAD, font=("Segoe UI", 9, "bold")).pack(anchor="w", pady=(16, 2))
        self.points = tk.Label(r, text="", bg=T.BG, fg=T.TEXT, font=("Segoe UI", 11, "bold"))
        self.points.pack(anchor="w")
        body = tk.Frame(r, bg=T.BG)
        body.pack(fill="x", pady=6)
        self.sliders = {}
        for i, (key, _, label) in enumerate(cd.STATS):
            tk.Label(body, text=label, bg=T.BG, fg=T.TEXT, width=13, anchor="w").grid(row=i, column=0, sticky="w")
            tk.Button(body, text="−", width=2, command=lambda k=key: self._step(k, -1)).grid(row=i, column=1)
            v = tk.Label(body, text="", bg=T.BG, fg=T.ORANGE, width=3, font=("Segoe UI", 12, "bold"))
            v.grid(row=i, column=2)
            tk.Button(body, text="+", width=2, command=lambda k=key: self._step(k, 1)).grid(row=i, column=3)
            self.sliders[key] = v
        self.chart = tk.Canvas(body, width=BARS_W, height=ROW_H * len(cd.STATS), bg=T.BG, highlightthickness=0,
                               cursor="hand2")
        self.chart.bind("<Button-1>", self._chart_click)
        self.chart.bind("<B1-Motion>", self._chart_click)
        self.chart.grid(row=0, column=4, rowspan=len(cd.STATS), padx=(20, 0))
        tk.Label(r, text="grey ticks = the class's cars,  blue line = class average,  box = class range,  orange = "
                         "yours  (click or drag on a bar)", bg=T.BG, fg=T.MUTED, font=("Segoe UI", 8)).pack(anchor="w")
        self.top_speed = tk.Label(r, text="", bg=T.BG, fg=T.MUTED)
        self.top_speed.pack(anchor="w", pady=(4, 0))
        tk.Label(r, text="Build + install puts your cars in the game (car select: their class).  The game finds a "
                         "car's 3D model by its body car.", bg=T.BG, fg=T.MUTED).pack(anchor="w", pady=(14, 0))
        # ---- the car in 3D (drag = turn it)
        self.view = tk.Canvas(r, bg=T.BG, highlightthickness=0, cursor="fleur")
        self.view.pack(fill="both", expand=True, pady=(8, 0))
        self.view.bind("<Configure>", lambda e: self._draw(True))
        self.view.bind("<B1-Motion>", self._turn)
        self.view.bind("<ButtonPress-1>", lambda e: setattr(self, "_drag_x", e.x))
        self.view.bind("<ButtonRelease-1>", lambda e: self._draw(True))
        self.yaw = -0.7
        self._parts_key = self._parts = None
        self._job = 0
        self._img = None

    # ------------------------------------------------------------------------------------------------- data
    def cars(self):
        return self.d.world.setdefault("cars", [])

    def car(self):
        cars = self.cars()
        return cars[self.sel] if 0 <= self.sel < len(cars) else None

    def _edit(self, fn):
        if self.loading or self.car() is None:
            return
        self.d.change()
        fn(self.car())
        self.d.changed()

    def _set(self, key, value):
        if self.car() is not None and self.car().get(key) != value:
            self._edit(lambda c: c.__setitem__(key, value))
            if key in ("wheels", "body_glb"):
                self._load()
            if key == "name":
                self.listbox.delete(self.sel)
                self.listbox.insert(self.sel, self._label(self.car()))
                self.listbox.selection_set(self.sel)

    # ------------------------------------------------------------------------------------------------- list
    def _label(self, c):
        return "%s   [%s]" % (c["name"], cd.CLASSES[c["class"]])

    def refresh(self):
        """(re)load everything from the world (tab shown, undo)"""
        cars = self.cars()
        self.listbox.delete(0, "end")
        for c in cars:
            self.listbox.insert("end", self._label(c))
        self.sel = min(self.sel, len(cars) - 1)
        state = "normal" if cars else "disabled"
        for w in self._all(self.right):
            try:
                w.configure(state="readonly" if isinstance(w, ttk.Combobox) and cars else state)
            except tk.TclError:
                pass
        if not cars:
            self.points.configure(text="No custom car yet: press + New")
            self.chart.delete("all")
            return
        self.listbox.selection_set(self.sel)
        self._load()

    def _all(self, w):
        for c in w.winfo_children():
            yield c
            yield from self._all(c)

    def _pick(self):
        s = self.listbox.curselection()
        if s:
            self.sel = s[0]
            self._load()

    def add(self):
        self.d.change()
        car = cd.new_car()
        names = {c["name"] for c in self.cars()}
        n = 1
        while car["name"] in names:
            n += 1
            car["name"] = "My car %d" % n
        self.cars().append(car)
        self.sel = len(self.cars()) - 1
        self.d.changed()
        self.refresh()

    def copy(self):
        if self.car() is None:
            return
        import copy as cp
        self.d.change()
        c = cp.deepcopy(self.car())
        c["name"] = (c["name"] + " copy")[:24]
        self.cars().append(c)
        self.sel = len(self.cars()) - 1
        self.d.changed()
        self.refresh()

    def delete(self):
        if self.car() is None:
            return
        self.d.change()
        del self.cars()[self.sel]
        self.d.changed()
        self.refresh()

    # ------------------------------------------------------------------------------------------------- editing
    def _load(self):
        c = self.car()
        self.loading = True
        self.name.set(c["name"])
        self.cls.set(c["class"])
        self.base.configure(values=[x["name"] for x in cd.class_cars(c["class"])])
        self.base.set(c["base"])
        self.icon.set(c["icon"])
        self.wheels.set(c.get("wheels") or c["base"])
        self.body_btn.configure(state="normal" if c.get("body_glb") else "disabled")
        for i, b in enumerate(self.paint_btns):
            b.configure(bg=c["paint"][i])
        self.loading = False
        self._show_stats()
        self._draw(True)

    def _class_changed(self):
        cls = self.cls.get()
        if self.car() is None or cls == self.car()["class"]:
            return
        base = cd.class_cars(cls)[0]

        def fn(c):
            c["class"], c["base"] = cls, base["name"]
            c["stats"] = {k: base[k] for k, _, _ in cd.STATS}
            cd.fair(c)
        self._edit(fn)
        self.listbox.delete(self.sel)
        self.listbox.insert(self.sel, self._label(self.car()))
        self.listbox.selection_set(self.sel)
        self._load()

    def _base_changed(self):
        """a new body: start from that car's own stats"""
        base = next(x for x in cd.game_cars() if x["name"] == self.base.get())

        def fn(c):
            c["base"], c["icon"] = base["name"], base["icon"]
            c["stats"] = {k: base[k] for k, _, _ in cd.STATS}
            cd.fair(c)
        self._edit(fn)
        self._load()

    def _paint(self, i):
        if self.car() is None:
            return
        col = colorchooser.askcolor(self.car()["paint"][i], title="Paint colour %d" % (i + 1))[1]
        if col:
            self._edit(lambda c: c["paint"].__setitem__(i, col))
            self._load()

    def _stat(self, key, value):
        """a stat changed (- / + or the chart): it stops at the class's range and where the points run out"""
        c = self.car()
        if self.loading or c is None or c["stats"][key] == value:
            return
        lim = cd.limits(c["class"])
        room = lim["budget"] - (cd.total(c) - c["stats"][key])
        want = value
        value = max(lim[key][0], min(value, room, lim[key][1]))
        if value != c["stats"][key]:
            self._edit(lambda c: c["stats"].__setitem__(key, value))
        if value < want:
            self.d.say("No points left: lower another stat first (the class's budget is %d)" % lim["budget"]
                       if value == room else "That is the most any class %s car has" % cd.CLASSES[c["class"]])
        elif value > want:
            self.d.say("That is the least any class %s car has" % cd.CLASSES[c["class"]])
        self._show_stats()

    def _step(self, key, d):
        if self.car() is not None:
            self._stat(key, self.car()["stats"][key] + d)

    def _chart_click(self, e):
        if self.car() is None:
            return
        i = int(e.y // ROW_H)
        if 0 <= i < len(cd.STATS):
            x0, x1 = 10, BARS_W - 60
            self._stat(cd.STATS[i][0], int(round((e.x - x0) * 10.0 / (x1 - x0))))

    def _show_stats(self):
        c = self.car()
        lim = cd.limits(c["class"])
        left = lim["budget"] - cd.total(c)
        self.points.configure(text="Points used: %d / %d   (%d left)" % (cd.total(c), lim["budget"], left),
                              fg=T.ORANGE if left == 0 else T.TEXT)
        self.top_speed.configure(text="Top speed about %d mph  (class %s: %d - %d mph)" % (
            cd.top_speed(c["class"], c["stats"]["speed"]), cd.CLASSES[c["class"]], *lim["mph"]))
        # the chart: per stat the class's range (band), every real car (ticks), the class average, OUR car
        cv = self.chart
        cv.delete("all")
        x0, x1 = 10, BARS_W - 60
        X = lambda v: x0 + (x1 - x0) * v / 10.0
        cars = cd.class_cars(c["class"])
        for i, (key, _, label) in enumerate(cd.STATS):
            y = i * ROW_H + ROW_H / 2
            cv.create_rectangle(X(0), y - 3, X(10), y + 3, fill=T.FIELD, outline="")
            cv.create_rectangle(X(lim[key][0]), y - 7, X(lim[key][1]), y + 7, fill=T.GLASS, outline=T.EDGE)
            for g in cars:
                cv.create_line(X(g[key]), y - 9, X(g[key]), y + 9, fill=T.MUTED)
            avg = sum(g[key] for g in cars) / len(cars)
            cv.create_line(X(avg), y - 12, X(avg), y + 12, fill=T.HEAD, width=2)
            v = c["stats"][key]
            self.sliders[key].configure(text=str(v))
            cv.create_rectangle(X(0), y - 4, X(v), y + 4, fill=T.ORANGE, outline="")
            cv.create_oval(X(v) - 7, y - 7, X(v) + 7, y + 7, fill=T.ORANGE, outline="white")

    # ------------------------------------------------------------------------------------------------- 3D view
    def _import_body(self):
        """a .glb body (one model: no damage parts - a damaged car only smokes). Kept next to the maps."""
        if self.car() is None:
            return
        path = filedialog.askopenfilename(title="Car body (.glb)", filetypes=[("glTF binary", "*.glb")])
        if not path:
            return
        import car_model
        try:
            car_model.read_glb_body(path)
        except Exception as e:                       # noqa: BLE001 - say why
            self.d.say("This .glb can't be used: %s" % e)
            return
        folder = os.path.join(paths.TRACKS, "cars")
        os.makedirs(folder, exist_ok=True)
        name = os.path.basename(path)
        shutil.copy2(path, os.path.join(folder, name))
        self._set("body_glb", name)
        self.d.say("Body imported: %s (the 3D view shows it; in game the car still uses the body car until car "
                   "import is finished)" % name)

    def _turn(self, e):
        self.yaw += (e.x - getattr(self, "_drag_x", e.x)) * 0.012
        self._drag_x = e.x
        self._draw(False)

    def _draw(self, full):
        """render in the background (one at a time; the newest request wins); full = sharp, else quick"""
        c = self.car()
        if c is None or not self.winfo_ismapped():
            self.view.delete("all")
            return
        w, h = self.view.winfo_width(), self.view.winfo_height()
        if w < 50 or h < 50:
            return
        key = (c["base"], c.get("wheels") or c["base"], c.get("body_glb"))
        paint = tuple(int(c["paint"][0][i:i + 2], 16) for i in (1, 3, 5))
        self._want = (key, paint, self.yaw, min(w, h), full, (w, h))
        if not getattr(self, "_busy", False):
            self._busy = True
            threading.Thread(target=self._render, daemon=True).start()
            self.after(15, self._poll)

    def _render(self):
        import car_model
        while True:
            want = self._want
            key, paint, yaw, size, full, wh = want
            try:
                if key != self._parts_key:
                    base, wheels, glb = key
                    body = car_model.read_game_car(base)
                    whl = car_model.read_game_car(wheels) if wheels != base else None
                    if glb:
                        own = car_model.read_glb_body(os.path.join(paths.TRACKS, "cars", glb))
                        body = dict({k: v for k, v in body.items() if k.split("|")[0] in ("tyre", "rim", "centre")},
                                    **own)
                    self._parts, self._parts_key = car_model.assemble(body, whl), key
                img = car_model.picture(self._parts, paint, max(60, size), yaw, smooth=full)
            except Exception as e:                   # noqa: BLE001 - shown in the view
                img = "This car can't be shown: %s" % e
            if want is self._want:
                self._result = (img, wh)
                self._busy = False
                return

    def _poll(self):
        if getattr(self, "_busy", False):
            self.after(15, self._poll)
            return
        res, self._result = getattr(self, "_result", None), None
        if res is None:
            return
        img, (w, h) = res
        self.view.delete("all")
        if isinstance(img, str):
            self.view.create_text(w / 2, h / 2, text=img, fill=T.MUTED)
            return
        from PIL import ImageTk
        side = min(w, h)
        if img.size[0] != side:
            img = img.resize((side, side))
        self._img = ImageTk.PhotoImage(img)
        self.view.create_image(w / 2, h / 2, image=self._img)
        self.view.create_text(10, h - 8, anchor="sw", fill=T.MUTED, font=("Segoe UI", 8),
                              text="drag = turn the car")

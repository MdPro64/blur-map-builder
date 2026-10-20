"""
Track designer: the SURFACE tab (next to Lighting) - paint dirt / water / gravel ... with a brush, set the road type,
dirt sides and varied asphalt. Mixed into road_designer.Designer. Data + build: flat_earth/surface.py.

Left drag = paint the chosen surface, right drag = remove paint (back to the road's own type), middle drag = move
the map, wheel = zoom.
"""
import tkinter as tk

import network
import surface

BG2 = "#122036"


class SurfaceTools:
    def surf_init(self):
        self.surf_kind = tk.StringVar(value="dirt")
        self.surf_size = tk.DoubleVar(value=6.0)
        self.surf_stroke = None          # "paint" / "erase" while a mouse button is down
        self.surf_mouse = None           # (x, z) world, for the brush circle
        self.surf_road = tk.StringVar(value="Asphalt")
        self.surf_side_type = tk.StringVar(value="Dirt")
        self.surf_side_key = tk.StringVar(value="dirt")
        self.surf_variety = tk.BooleanVar(value=False)
        self.surf_feel = tk.StringVar(value="Strong")

    # ---- the tool bar (row 2 of the Surface tab) ----
    def surf_bar(self, parent, BTN, BTN_HI, SEL, FG, HEAD, UI_FONT):
        f = tk.Frame(parent, bg=BG2)
        cols = tk.Frame(f, bg=BG2)                       # two lines: brush on top, whole-road settings below
        cols.pack(side="left")
        top = tk.Frame(cols, bg=BG2)
        top.pack(side="top", anchor="w")
        bottom = tk.Frame(cols, bg=BG2)
        bottom.pack(side="top", anchor="w")
        note = tk.Frame(top, bg="#1c3a5c", highlightbackground="#8fd3ff", highlightthickness=1)
        note.pack(side="left", padx=(6, 2), pady=4)
        tk.Label(note, text="LEFT click = add", bg="#1c3a5c", fg="#7dffa8",
                 font=("Segoe UI Symbol", 9, "bold")).pack(anchor="w", padx=6)
        tk.Label(note, text="RIGHT click = delete", bg="#1c3a5c", fg="#ff9a9a",
                 font=("Segoe UI Symbol", 9, "bold")).pack(anchor="w", padx=6)
        g = tk.Frame(top, bg=BG2)
        g.pack(side="left", padx=(6, 4), pady=3)
        tk.Label(g, text="BRUSH", bg=BG2, fg=HEAD, font=("Segoe UI", 7, "bold")).pack(anchor="w")
        row = tk.Frame(g, bg=BG2)
        row.pack()
        short = {"roughdirt": "Rough", "sand": "Sand", "water": "Water", "deepwater": "Deep water", "tarmac": "Asphalt"}
        for key in surface.ORDER:
            label, _, col = surface.SURFACES[key][:3]
            label = short.get(key, label)
            tk.Radiobutton(row, text=label, variable=self.surf_kind, value=key, indicatoron=False, bg=col, fg="white",
                           selectcolor=SEL, activebackground=BTN_HI, relief="flat", padx=6, pady=3, font=UI_FONT,
                           command=lambda: self.say("Brush: %s - left drag paints, right drag removes" %
                                                    surface.SURFACES[self.surf_kind.get()][0])).pack(side="left", padx=1)
        tk.Label(row, text="  size", bg=BG2, fg=FG).pack(side="left")
        sp = tk.Spinbox(row, from_=1, to=40, increment=1, width=3, textvariable=self.surf_size)
        sp.pack(side="left", padx=2)
        tk.Label(row, text="m", bg=BG2, fg=FG).pack(side="left")
        g = tk.Frame(bottom, bg=BG2)
        g.pack(side="left", padx=(6, 4), pady=(0, 3))
        tk.Label(g, text="WHOLE ROAD", bg=BG2, fg=HEAD, font=("Segoe UI", 7, "bold")).pack(anchor="w")
        row = tk.Frame(g, bg=BG2)
        row.pack()
        labels = {k: surface.SURFACES[k][0] for k in surface.SURFACES}
        tk.Label(row, text="type", bg=BG2, fg=FG).pack(side="left")
        self._surf_key = {v: k for k, v in labels.items()}
        om = tk.OptionMenu(row, self.surf_road, *[labels[k] for k in labels if k != "deepwater"],
                           command=lambda v: self.surf_set("road", self._surf_key[v]))
        om.config(bg=BTN, fg=FG, activebackground=BTN_HI, highlightthickness=0, relief="flat", width=9)
        om.pack(side="left", padx=2)
        self.surf_road_menu = om
        tk.Label(row, text="  sides", bg=BG2, fg=FG).pack(side="left")
        tk.Button(row, text="-", width=2, bg=BTN, fg=FG, relief="flat", command=lambda: self.surf_sides(-1)).pack(side="left")
        self.surf_sides_lbl = tk.Label(row, text="off", width=5, bg=BG2, fg="white")
        self.surf_sides_lbl.pack(side="left")
        tk.Button(row, text="+", width=2, bg=BTN, fg=FG, relief="flat", command=lambda: self.surf_sides(1)).pack(side="left")
        short = {"roughdirt": "Rough", "sand": "Sand", "water": "Water", "deepwater": "Deep water"}
        for key in ("dirt", "roughdirt", "gravel", "grass", "sand", "water", "deepwater"):   # the sides (as the brush)
            tk.Radiobutton(row, text=short.get(key, surface.SURFACES[key][0]), variable=self.surf_side_key, value=key,
                           indicatoron=False, bg=surface.SURFACES[key][2], fg="white", selectcolor=SEL,
                           activebackground=BTN_HI, relief="flat", padx=5, pady=2, font=UI_FONT,
                           command=lambda: self.surf_set("side_type", self.surf_side_key.get())).pack(side="left", padx=1)
        tk.Checkbutton(row, text="Varied asphalt", variable=self.surf_variety, bg=BG2, fg=FG,
                       selectcolor=BTN, activebackground=BG2, activeforeground=FG,
                       command=lambda: self.surf_set("variety", bool(self.surf_variety.get()))).pack(side="left", padx=8)
        tk.Button(row, text="Clear paint", bg="#7a2a3a", fg="white", relief="flat",
                  command=self.surf_clear).pack(side="left", padx=4)
        g = tk.Frame(bottom, bg=BG2)
        g.pack(side="left", padx=(10, 4), pady=(0, 3))
        tk.Label(g, text="GRIP (this location)", bg=BG2, fg=HEAD, font=("Segoe UI", 7, "bold")).pack(anchor="w")
        names = dict(surface.FEEL_NAMES)
        self._feel_key = {v: k for k, v in names.items()}
        om3 = tk.OptionMenu(g, self.surf_feel, *names.values(), command=lambda v: self.surf_feel_set(self._feel_key[v]))
        om3.config(bg=BTN, fg=FG, activebackground=BTN_HI, highlightthickness=0, relief="flat", width=10)
        om3.pack()
        return f

    def surf_refresh(self):
        """the tool bar shows the current map's settings"""
        s = surface.settings(self.map)
        self.surf_road.set(surface.SURFACES[s["road"]][0])           # the menus show the names
        self.surf_side_type.set(surface.SURFACES[s["side_type"]][0])
        self.surf_side_key.set(s["side_type"])
        self.surf_variety.set(bool(s["variety"]))
        self.surf_feel.set(dict(surface.FEEL_NAMES).get(surface.loc_feel(self.world, self.loc), "Strong"))
        if hasattr(self, "surf_sides_lbl"):
            self.surf_sides_lbl.config(text=("%.0f m" % s["sides"]) if s["sides"] else "off")

    # ---- settings ----
    def surf_set(self, key, value):
        self.change()
        self.map.setdefault("surface", {})[key] = value
        self.changed()
        self.surf_refresh()                              # (the sides width label etc. at once)
        names = {"road": "Road type", "side_type": "Sides", "variety": "Varied asphalt"}
        shown = surface.SURFACES[value][0] if isinstance(value, str) and value in surface.SURFACES else (
            "ON" if value is True else "OFF" if value is False else value)
        self.say("%s: %s (Build + install to drive on it)" % (names.get(key, key), shown), "#58d06a")

    def surf_feel_set(self, key):
        """how strongly dirt / grass / water ... change the driving - per LOCATION (all its maps; it goes with an
        exported map); a location that never set one uses the old all-maps value world["surface_feel"]"""
        self.change()
        self.loc["surface_feel"] = key
        self.changed()
        self.say("Grip of the surfaces: %s (all maps of %s) - Build + install to feel it" % (
            dict(surface.FEEL_NAMES)[key], self.loc["name"]), "#58d06a")

    def surf_sides(self, d):
        s = surface.settings(self.map)
        v = max(0.0, min(15.0, float(s["sides"]) + d))
        self.surf_set("sides", v)

    def surf_clear(self):
        if not surface.settings(self.map)["paint"]:
            return
        self.change()
        self.map.setdefault("surface", {})["paint"] = {}
        self.changed()
        self.say("All paint removed from this map")

    # ---- the brush ----
    def surf_apply(self, x, z, erase):
        try:
            size = max(1.0, float(self.surf_size.get()))
        except (tk.TclError, ValueError):
            size = 6.0
        return surface.brush(self.map, x, z, size / 2, None if erase else self.surf_kind.get())

    def surf_down(self, x, z, erase=False):
        self.change()
        self.surf_stroke = "erase" if erase else "paint"
        self.surf_apply(x, z, erase)
        self._surf_quick(x, z)

    def surf_move(self, x, z):
        self.surf_mouse = (x, z)
        if self.surf_stroke:
            if self.surf_apply(x, z, self.surf_stroke == "erase"):
                self._surf_quick(x, z)               # just the brush spot now; everything is redrawn on release
        self.surf_draw_cursor()

    def _surf_quick(self, x, z):
        """draw the brush spot (fast, while dragging; the full shapes come on release)"""
        try:
            rad = max(1.0, float(self.surf_size.get())) / 2
        except (tk.TclError, ValueError):
            rad = 3.0
        erase = self.surf_stroke == "erase"
        col = "#3e4250" if erase else surface.SURFACES[self.surf_kind.get()][2]
        a = self.to_screen(x - rad, z + rad)
        b = self.to_screen(x + rad, z - rad)
        self.c.create_oval(a[0], a[1], b[0], b[1], fill=col, outline="")

    def surf_up(self):
        if self.surf_stroke:
            self.surf_stroke = None
            self.changed()                           # (redraws everything)
            n = len(surface.settings(self.map)["paint"])
            self.say("Painted %d cells of %.0f m (%.0f m2) - Build + install to drive on it" %
                     (n, surface.CELL, n * surface.CELL * surface.CELL), "#58d06a")

    # ---- drawing (redraw, Surface tab) ----
    def surf_draw(self, c, r):
        """the Surface tab: the road type (whole road), the painted shapes (smooth outlines, as in game), the sides
        on top (the paint never covers them)"""
        net = self.net or network.get(self.map, r)
        st = surface.settings(self.map)
        roads = net.drivable()
        if st["road"] != "tarmac":                    # the whole road in its type's colour
            col = surface.SURFACES[st["road"]][2]
            for rr in roads:
                self._surf_band(c, rr, -1, 1, col)
        for key, (rects_, polys) in surface.paint_shapes(self.map, net).items():
            col = surface.SURFACES[key][2]
            for x0, z0, x1, z1 in rects_:
                a = self.to_screen(x0, z1)
                b = self.to_screen(x1, z0)
                c.create_rectangle(a[0], a[1], b[0] + 1, b[1] + 1, fill=col, outline="")
            for poly in polys:
                flat = []
                for x, z in poly:
                    flat += self.to_screen(x, z)
                c.create_polygon(*flat, fill=col, outline=col)
        if st["sides"] > 0:                           # the sides: smooth strips along both edges, on top
            col = surface.SURFACES[st["side_type"]][2]
            for rr in roads:
                for side in (-1, 1):
                    self._surf_strip(c, rr, side, float(st["sides"]), col)
        self.surf_draw_cursor()

    def _surf_band(self, c, road, a, b, col):
        """the road between its two edges in one colour"""
        el, er = road.edge(-1), road.edge(1)
        for i in range(0, len(road) - 1, 2):
            j = min(i + 2, len(road) - 1)
            flat = []
            for q in (el[i], el[j], er[j], er[i]):
                flat += self.to_screen(*q)
            c.create_polygon(*flat, fill=col, outline=col)

    def _surf_strip(self, c, road, side, width, col):
        """a strip `width` m wide along the inside of one road edge - not where the wall is cut (a branch leaves /
        joins there) and, for a branch, not on the main road"""
        net = self.net or network.get(self.map, self.road)
        cut = net.wall_mask(road)[0 if side < 0 else 1]
        fac = surface.side_factor(net, road, 0 if side < 0 else 1)      # narrowing smoothly towards an opening
        outer = road.edge(side)
        n = len(road)
        inner = []
        for i in range(n):
            cx, cz = road.centre[i]
            sx, sz = road.side(i)
            lat = side * max(road.width_at[i] / 2 - width * fac[i], 0.0)
            inner.append((cx + sx * lat, cz + sz * lat))
        clip = net.clip(road)
        for i in range(n - 1):
            j = i + 1
            if cut[i] or cut[j]:
                continue
            pts = (outer[i], outer[j], inner[j], inner[i])
            if clip:
                pts = (clip(i, *outer[i]), clip(j, *outer[j]), clip(j, *inner[j]), clip(i, *inner[i]))
            flat = []
            for q in pts:
                flat += self.to_screen(*q)
            c.create_polygon(*flat, fill=col, outline=col)

    def surf_draw_cursor(self):
        c = self.c
        items = c.find_withtag("surfcursor")
        if self.surf_mouse is None or self.view_mode.get() != "surface":
            c.delete("surfcursor")
            return
        x, z = self.surf_mouse
        try:
            rad = max(1.0, float(self.surf_size.get())) / 2
        except (tk.TclError, ValueError):
            rad = 3.0
        # on screen at least 6 pixels round: zoomed far out a small brush was 1-3 pixels, its inner circle (2 pixels
        # smaller) got a NEGATIVE size and Windows left dotted trails behind it when the mouse moved (user 2026-10-20)
        cx, cy = self.to_screen(x, z)
        pr = max(6, int(round(rad * self.scale)))
        x0, y0, x1, y1 = int(round(cx)) - pr, int(round(cy)) - pr, int(round(cx)) + pr, int(round(cy)) + pr
        col = "#ff6060" if self.surf_stroke == "erase" else surface.SURFACES[self.surf_kind.get()][2]
        if len(items) == 2:
            c.coords(items[0], x0, y0, x1, y1)
            c.coords(items[1], x0 + 2, y0 + 2, x1 - 2, y1 - 2)
            c.itemconfig(items[1], outline=col)
            c.tag_raise("surfcursor")
        else:
            c.delete("surfcursor")
            c.create_oval(x0, y0, x1, y1, outline="#ffffff", width=2, tags="surfcursor")
            c.create_oval(x0 + 2, y0 + 2, x1 - 2, y1 - 2, outline=col, width=2, tags="surfcursor")

    def surf_checks(self):
        """[(ok, text)] for the CHECKS box"""
        s = surface.settings(self.map)
        if surface.is_default(self.map):
            return []
        parts = []
        if s["road"] != "tarmac":
            parts.append("road %s" % surface.SURFACES[s["road"]][0])
        if s["sides"]:
            parts.append("%.0f m %s sides" % (s["sides"], surface.SURFACES[s["side_type"]][0].lower()))
        if s["variety"]:
            parts.append("varied asphalt")
        if s["paint"]:
            parts.append("%d m2 painted" % (len(s["paint"]) * surface.CELL * surface.CELL))
        return [(True, "surface: " + ", ".join(parts))]

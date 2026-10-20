"""
Track designer: BRANCHES and LANE SPLITS (2D tab). Mixed into road_designer.Designer.

Branch tool (B): click ON the road where the branch leaves, click its points beside the road, then click ON the road
where it joins again (Esc = cancel). Split tool (L): click ON the road where the lane split starts, then where it
ends. Select (S) / Move (M): click a branch's points or its road to select it, drag the points or the round handles
(where it leaves / joins, where a split starts / ends). Delete = delete the selected point / branch / split.
The data: m["branches"], m["splits"] (flat_earth/network.py).
"""
import math
import tkinter as tk

import network

BRANCH_COL, BRANCH_EDGE, SPLIT_COL = "#4f6a5a", "#5cf0a0", "#ffb020"
HANDLE = 8
MEASURE_COL = "#ff8af0"
MEASURE_MAX = 50
BALANCE_SHORT, BALANCE_LONG = 0.80, 1.35     # a branch shorter than 80 % / longer than 135 % of the main road it skips


class NetTools:
    def net_init(self):
        self.sel_branch = None        # selected branch index
        self.sel_bpt = None           # selected point of that branch
        self.sel_split = None         # selected lane split index
        self.net_draft = None         # a branch / split being drawn: {"kind", "from_m", "points"}
        self.net = None
        self.measure = None           # MEASURE tool: {"pts": [(x, z)...] (<= MEASURE_MAX), "done": bool}
        self.mouse_world = None

    # ---- data ----
    def branches(self):
        return self.map.setdefault("branches", [])

    def splits(self):
        return self.map.setdefault("splits", [])

    def net_clear_sel(self):
        self.sel_branch = self.sel_bpt = self.sel_split = None

    def net_on_road(self, x, z, extra=10.0):
        """(s, lat) if (x, z) is on the main road (or within `extra` m of its edge), else None"""
        if not self.road:
            return None
        s, lat = self.road.project(x, z)
        return (s, lat) if abs(lat) <= self.road.w_at(s) / 2 + extra else None

    def _span_to(self, s0, s1):
        """the end of a stretch that starts at s0 and ends where the user clicked (s1): a lap track wraps round"""
        if self.road.closed and s1 <= s0:
            s1 += self.road.length
        return s1

    # ---- the tools ----
    def net_click(self, tool, x, z):
        hit = self.net_on_road(x, z)
        d = self.net_draft
        if tool == "branch":
            if d is None:
                if not hit:
                    self.say("Branch: first click ON the road where the branch should leave it", "#ffb020")
                    return
                self.net_draft = {"kind": "branch", "from_m": hit[0], "points": []}
                self.say("Branch: now click its points BESIDE the road, then click ON the road where it joins "
                         "again (Esc = cancel)", "#5cf0a0")
            elif hit and d["points"] and abs(hit[1]) <= self.road.w_at(hit[0]) / 2:
                s1 = self._span_to(d["from_m"], hit[0])
                if not self.road.closed and s1 < d["from_m"]:
                    d["from_m"], s1 = s1, d["from_m"]          # drawn backwards on a sprint: swap the ends
                    d["points"].reverse()
                if s1 - d["from_m"] < network.MIN_BRANCH_GAP:
                    self.say("The branch must join at least %.0f m after it leaves" % network.MIN_BRANCH_GAP, "#ff6060")
                    return
                self.change()
                self.branches().append({"from_m": round(d["from_m"], 1), "to_m": round(s1, 1),
                                        "width": network.BRANCH_WIDTH, "points": d["points"]})
                self.net_draft = None
                self.net_clear_sel()
                self.sel_branch = len(self.branches()) - 1
                self.changed()
                self.say("Branch %d made: %.0f m to %.0f m along the road. Drag its points (Move tool), "
                         "change its width on the right" % (self.sel_branch + 1, d["from_m"], s1), "#5cf0a0")
                return
            elif hit and abs(hit[1]) <= self.road.w_at(hit[0]) / 2:
                self.say("Click at least one point BESIDE the road first (where the branch goes)", "#ffb020")
                return
            else:
                d["points"].append({"x": round(x, 1), "z": round(z, 1)})
                self.say("Branch: %d point(s) - more points, or click ON the road where it joins (Esc = cancel)"
                         % len(d["points"]), "#5cf0a0")
            self.redraw()
        elif tool == "split":
            if not hit:
                self.say("Lane split: click ON the road", "#ffb020")
                return
            if d is None:
                self.net_draft = {"kind": "split", "from_m": hit[0], "points": []}
                self.say("Lane split: now click ON the road where it ends (Esc = cancel)", SPLIT_COL)
                self.redraw()
                return
            s0, s1 = d["from_m"], self._span_to(d["from_m"], hit[0])
            if not self.road.closed and s1 < s0:
                s0, s1 = s1, s0
            if s1 - s0 < network.SPLIT_MIN:
                self.say("A lane split must be at least %.0f m long" % network.SPLIT_MIN, "#ff6060")
                return
            self.change()
            self.splits().append({"from_m": round(s0, 1), "to_m": round(s1, 1), "lanes": 2})
            self.net_draft = None
            self.net_clear_sel()
            self.sel_split = len(self.splits()) - 1
            self.changed()
            self.say("Lane split %d: 2 lanes from %.0f m to %.0f m (3 lanes: on the right)" % (self.sel_split + 1, s0, s1),
                     SPLIT_COL)

    # ---- MEASURE tool (D): a path of up to MEASURE_MAX points ----
    def measure_click(self, x, z):
        """every click adds a point (up to MEASURE_MAX); Enter = finish (the next click starts a new path),
        Backspace = take the last point away, Esc = clear"""
        m = self.measure
        if m is None or m.get("done"):
            self.measure = m = {"pts": [], "done": False}
        if len(m["pts"]) >= MEASURE_MAX:
            self.say("Measure: %d points is the most - Enter = finish, Backspace = take one away" % MEASURE_MAX,
                     "#ffd23b")
            return
        m["pts"].append((x, z))
        if len(m["pts"]) >= MEASURE_MAX:
            m["done"] = True
        self.measure_say()
        self.redraw()

    def measure_finish(self):
        """Enter: stop the line following the mouse"""
        if self.measure and self.measure["pts"]:
            self.measure["done"] = True
            self.measure_say()
            self.redraw()
            return True
        return False

    def measure_back(self):
        """Backspace / Delete: take the last point away"""
        m = self.measure
        if not m or not m["pts"]:
            return False
        m["pts"].pop()
        m["done"] = False
        if not m["pts"]:
            self.measure = None
        self.measure_say()
        self.redraw()
        return True

    def measure_say(self):
        m = self.measure
        if not m or not m["pts"]:
            self.say("Measure: click the first point", MEASURE_COL)
            return
        pts = m["pts"]
        if len(pts) == 1:
            self.say("Measure: click more points (up to %d)  -  Enter = finish, Backspace = undo a point, Esc = clear"
                     % MEASURE_MAX, MEASURE_COL)
        else:
            self.say("Measure %d points: %s%s" % (len(pts), self.measure_text(pts).replace("\n", "   "),
                                                  "" if m["done"] else "   (Enter = finish)"), MEASURE_COL)

    def _on_which_road(self, x, z):
        """(Road, s) of the road (main or a branch) (x, z) is on, or None"""
        if not self.road:
            return None
        if self.net is not None:
            hit = self.net_branch_at(x, z)
            if hit:
                bi, br = hit
                return br, br.project(x, z)[0]
        s, lat = self.road.project(x, z)
        if abs(lat) <= self.road.w_at(s) / 2 + 2:
            return self.road, s
        return None

    def measure_text(self, pts):
        """path length (straight lines between the points) + the driving distance when every point is on the
        same road (a lap track: each step in the driving direction) + first-to-last straight"""
        path = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts, pts[1:]))
        text = "path %.0f m" % path
        on = [self._on_which_road(*q) for q in pts]
        if all(on) and all(o[0] is on[0][0] for o in on):
            road = on[0][0]
            drive = 0.0
            for a, b in zip(on, on[1:]):
                d = b[1] - a[1]
                drive += d % road.length if road.closed else abs(d)
            text += "\nalong the road %.0f m (%s%s)" % (drive, "main road" if road is self.road else "branch",
                                                        ", driving direction" if road.closed else "")
        elif any(on):
            text += "\n(all points on the same road = the driving distance too)"
        if len(pts) > 2:
            text += "\nfirst to last, straight %.0f m" % math.hypot(pts[-1][0] - pts[0][0], pts[-1][1] - pts[0][1])
        return text

    def measure_draw(self, c):
        m = self.measure
        if not m or not m["pts"]:
            return
        pts = list(m["pts"])
        if not m["done"] and self.mouse_world:
            pts.append(self.mouse_world)                     # the next point follows the mouse
        scr = [self.to_screen(*q) for q in pts]
        if len(scr) >= 2:
            flat = [v for q in scr for v in q]
            c.create_line(*flat, fill=MEASURE_COL, width=2, dash=(8, 4))
        for k, (px, py) in enumerate(scr[:len(m["pts"])]):
            c.create_oval(px - 5, py - 5, px + 5, py + 5, fill=MEASURE_COL, outline="#000")
            if k == 0 or k == len(m["pts"]) - 1:
                c.create_text(px - 8, py - 8, text="A" if k == 0 else str(k + 1), fill=MEASURE_COL, anchor="se",
                              font=("Segoe UI", 9, "bold"))
        if len(pts) < 2:
            return
        lines = self.measure_text(pts).split("\n")
        mx, my = scr[-1]
        w = max(len(t) for t in lines) * 7 + 12
        c.create_rectangle(mx + 12, my - 6, mx + 12 + w, my + 16 * len(lines) + 4, fill="#0b1a2e", outline=MEASURE_COL)
        for k, t in enumerate(lines):
            c.create_text(mx + 18, my + k * 16, text=t, fill="#ffffff" if k == 0 else MEASURE_COL, anchor="nw",
                          font=("Segoe UI", 9, "bold" if k == 0 else "normal"))

    def net_cancel(self):
        if self.net_draft:
            self.net_draft = None
            self.say("Cancelled")
            return True
        return False

    # ---- hit tests (screen pixels) ----
    def net_handles(self):
        """[(kind, index, x, z)]: the round handles of branches (leave / join) and splits (start / end)"""
        out = []
        if not self.road:
            return out
        for bi, b in enumerate(self.map.get("branches") or []):
            s0, s1 = network.branch_span(self.road, b)
            for kind, s in (("bfrom", s0), ("bto", s1)):
                x, z, _ = self.road.at(s)
                out.append((kind, bi, x, z))
        for si, sp in enumerate(self.map.get("splits") or []):
            s0, s1 = network.split_span(self.road, sp)
            for kind, s in (("sfrom", s0), ("sto", s1)):
                x, z, _ = self.road.at(s)
                out.append((kind, si, x, z))
        return out

    def net_hit(self, sx, sy):
        """what is under the mouse - the NEAREST of: a branch point ("bpt", (branch, point)), a round handle
        ("bfrom" / "bto" / "sfrom" / "sto", index), a lane divider or its label ("split", index); else the branch
        road itself ("branch", index) or None"""
        best = None

        def offer(d2, hit):
            nonlocal best
            if d2 < 160 and (best is None or d2 < best[0]):
                best = (d2, hit)
        for bi, b in enumerate(self.map.get("branches") or []):
            for pi, p in enumerate(b.get("points") or []):
                px, py = self.to_screen(p["x"], p["z"])
                offer((px - sx) ** 2 + (py - sy) ** 2, ("bpt", (bi, pi)))
        for kind, i, x, z in self.net_handles():
            px, py = self.to_screen(x, z)
            offer((px - sx) ** 2 + (py - sy) ** 2 + 20, (kind, i))      # a point beats a handle on the same spot
        if self.net is not None:
            for si, (sp, ds) in enumerate(self.net.splits):
                for d in ds:
                    for x, z in d.centre[::2]:
                        px, py = self.to_screen(x, z)
                        offer((px - sx) ** 2 + (py - sy) ** 2 + 10, ("split", si))
                s0, s1 = network.split_span(self.road, sp)
                lx, lz, _ = self.road.at((s0 + s1) / 2, self.road.w_at((s0 + s1) / 2) / 2 + 14)
                px, py = self.to_screen(lx, lz)
                offer(max(0.0, (px + 25 - sx) ** 2 / 9 + (py - sy) ** 2 - 40), ("split", si))   # its "N LANES" label
        if best:
            return best[1]
        if self.net is None:
            return None
        x, z = self.to_world(sx, sy)
        hit = self.net_branch_at(x, z)
        return ("branch", hit[0]) if hit else None

    def net_insert(self, x, z):
        """Insert tool on / near a branch: a new point in the branch where the click is. True if done"""
        if not self.road:
            return False
        sx, sy = self.to_screen(x, z)
        best = None
        for bi, b in enumerate(self.map.get("branches") or []):
            s0, s1 = network.branch_span(self.road, b)
            chain = [self.road.at(s0)[:2]] + [(p["x"], p["z"]) for p in b["points"]] + [self.road.at(s1)[:2]]
            for k in range(len(chain) - 1):
                (ax, ay), (bx, by) = self.to_screen(*chain[k]), self.to_screen(*chain[k + 1])
                dx, dy = bx - ax, by - ay
                t = max(0.0, min(1.0, ((sx - ax) * dx + (sy - ay) * dy) / max(dx * dx + dy * dy, 1e-9)))
                d2 = (ax + dx * t - sx) ** 2 + (ay + dy * t - sy) ** 2
                if best is None or d2 < best[0]:
                    best = (d2, bi, k)
        if best is None or best[0] > 40 ** 2:
            return False
        _, bi, k = best
        self.change()
        self.branches()[bi]["points"].insert(k, {"x": round(x, 1), "z": round(z, 1)})
        self.net_clear_sel()
        self.sel_branch, self.sel_bpt = bi, k
        self.changed()
        self.say("Point added to branch %d - drag it with the Move tool, Delete removes it" % (bi + 1), BRANCH_EDGE)
        return True

    def net_branch_at(self, x, z):
        """(index, Road) of the branch whose surface (x, z) is on (off the main road), or None"""
        if self.net is None:
            return None
        areas = self.net.areas()
        if areas[id(self.net.main)].depth(x, z) > 0:
            return None
        for bi, (b, r) in enumerate(self.net.branches):
            if areas[id(r)].depth(x, z) > 0:
                return bi, r
        return None

    def net_select(self, hit):
        self.net_clear_sel()
        kind, i = hit
        if kind == "bpt":
            self.sel_branch, self.sel_bpt = i
        elif kind in ("bfrom", "bto", "branch"):
            self.sel_branch = i
        elif kind in ("sfrom", "sto", "split"):
            self.sel_split = i

    def net_drag(self, kind, i, x, z):
        """drag a branch point / handle (mouse at world x, z)"""
        if kind == "bpt":
            bi, pi = i
            p = self.branches()[bi]["points"][pi]
            p["x"], p["z"] = round(x, 1), round(z, 1)
            return
        s, _ = self.road.project(x, z)
        if kind in ("bfrom", "bto"):
            b = self.branches()[i]
            key, other = ("from_m", "to_m") if kind == "bfrom" else ("to_m", "from_m")
        else:
            b = self.splits()[i]
            key, other = ("from_m", "to_m") if kind == "sfrom" else ("to_m", "from_m")
        if self.road.closed:            # a lap track: start in 0..length, the end after it (wrapping round)
            L = self.road.length
            f, t = b.get("from_m", 0.0) % L, b.get("to_m", 0.0) % L
            if key == "from_m":
                f = s
            else:
                t = s
            b["from_m"], b["to_m"] = round(f, 1), round(self._span_to(f, t), 1)
            return
        b[key] = round(s, 1)

    def net_delete(self):
        """Delete key: the selected branch point (the last one: the whole branch) / branch / split. True if done"""
        if self.sel_branch is not None and self.sel_branch < len(self.branches()):
            self.change()
            b = self.branches()[self.sel_branch]
            if self.sel_bpt is not None and len(b["points"]) > 1:
                del b["points"][self.sel_bpt]
                self.say("Branch point deleted")
            else:
                bi = self.sel_branch
                del self.branches()[bi]
                items = self.map.get("pickups")
                if items:                            # its power-up rows go too; later branches move down one
                    keep = []
                    for it in items:
                        if it.get("bi") == bi:
                            continue
                        if it.get("bi") is not None and it["bi"] > bi:
                            it["bi"] -= 1
                        keep.append(it)
                    items[:] = keep
                self.say("Branch deleted")
            self.net_clear_sel()
            self.changed()
            return True
        if self.sel_split is not None and self.sel_split < len(self.splits()):
            self.change()
            del self.splits()[self.sel_split]
            self.net_clear_sel()
            self.changed()
            self.say("Lane split deleted")
            return True
        return False

    # ---- property panel ----
    def net_props(self, f, head, row):
        if self.sel_branch is not None and self.sel_branch < len(self.map.get("branches") or []):
            b = self.branches()[self.sel_branch]
            head("BRANCH %d (%d points)" % (self.sel_branch + 1, len(b["points"])))
            s0, s1 = network.branch_span(self.road, b) if self.road else (b["from_m"], b["to_m"])
            row("Width", "%.0f m" % b.get("width", network.BRANCH_WIDTH), lambda: self.net_adj("width", -2),
                lambda: self.net_adj("width", 2))
            if self.net is not None and self.sel_branch < len(self.net.branches):
                _, blen, mlen, ratio = self.net.balance()[self.sel_branch]
                col = "#ffd23b" if not BALANCE_SHORT <= ratio <= BALANCE_LONG else "#58d06a"
                tk.Label(f, text="Length %.0f m  vs  main road %.0f m  (%+.0f %%)" % (blen, mlen, (ratio - 1) * 100),
                         bg=f["bg"], fg=col).pack(anchor="w")
            row("Leaves at", "%.0f m" % s0, lambda: self.net_adj("from_m", -20), lambda: self.net_adj("from_m", 20))
            row("Joins at", "%.0f m" % s1, lambda: self.net_adj("to_m", -20), lambda: self.net_adj("to_m", 20))
            if self.sel_bpt is not None and self.sel_bpt < len(b["points"]):
                p = b["points"][self.sel_bpt]
                row("Point %d height" % (self.sel_bpt + 1), "%.0f m" % (p.get("h") or 0),
                    lambda: self.net_adj("h", -1), lambda: self.net_adj("h", 1))
            tk.Label(f, text="Move tool: drag its points / the round handles", bg=f["bg"], fg="#7f8a96").pack(anchor="w")
            tk.Button(f, text="✖ Delete branch", command=self.net_delete_whole, bg="#7a2a3a", fg="white").pack(anchor="w", pady=2)
        elif self.sel_split is not None and self.sel_split < len(self.map.get("splits") or []):
            sp = self.splits()[self.sel_split]
            head("LANE SPLIT %d" % (self.sel_split + 1))
            s0, s1 = network.split_span(self.road, sp) if self.road else (sp["from_m"], sp["to_m"])
            fr = tk.Frame(f, bg=f["bg"])
            fr.pack(fill="x", pady=2)
            tk.Label(fr, text="Lanes", bg=f["bg"], fg="#d8dee6", width=14, anchor="w").pack(side="left")
            for k in (2, 3):
                on = network.lane_count(sp) == k
                tk.Button(fr, text="%d lanes" % k, bg="#3d8ee8" if on else "#243a60", fg="white",
                          command=lambda k=k: self.net_lanes(k)).pack(side="left", padx=1)
            row("Starts at", "%.0f m" % s0, lambda: self.net_adj("from_m", -20), lambda: self.net_adj("from_m", 20))
            row("Ends at", "%.0f m" % s1, lambda: self.net_adj("to_m", -20), lambda: self.net_adj("to_m", 20))
            tk.Button(f, text="✖ Delete split", command=self.net_delete_whole, bg="#7a2a3a", fg="white").pack(anchor="w", pady=2)

    def net_delete_whole(self):
        self.sel_bpt = None
        self.net_delete()

    def net_adj(self, key, d):
        self.change()
        if self.sel_branch is not None:
            b = self.branches()[self.sel_branch]
            if key == "h":
                p = b["points"][self.sel_bpt]
                p["h"] = max(0.0, (p.get("h") or 0) + d)
            elif key == "width":
                b["width"] = max(14.0, min(80.0, b.get("width", network.BRANCH_WIDTH) + d))
            else:
                b[key] = b.get(key, 0.0) + d
        elif self.sel_split is not None:
            sp = self.splits()[self.sel_split]
            sp[key] = sp.get(key, 0.0) + d
        self.changed()

    def net_lanes(self, k):
        self.change()
        self.splits()[self.sel_split]["lanes"] = k
        self.changed()

    # ---- drawing ----
    def net_draw_surfaces(self, c):
        """the branch surfaces (called by redraw BEFORE the main road: the main road covers their ends - the same
        picture as the build's surface cut at the main road's edge, without working that cut out at every redraw)"""
        net = self.net
        if net is None:
            return
        for bi, (b, br) in enumerate(net.branches):
            el, er = br.edge(-1), br.edge(1)
            fill = "#5a7a66" if bi == self.sel_branch else BRANCH_COL
            for i in range(0, len(br) - 1, 2):
                j = min(i + 2, len(br) - 1)
                poly = []
                for q in (el[i], el[j], er[j], er[i]):
                    poly += self.to_screen(*q)
                c.create_polygon(*poly, fill=fill, outline="")

    def net_draw(self, c, r):
        """branch walls + labels, lane dividers, handles, the branch being drawn (called by redraw after the main
        road)"""
        net = self.net                # made by redraw (network.Network of this map)
        clean = self.view_mode.get() == "surface"     # Surface tab: no labels / handles (user 2026-10-20)
        if net is not None:
            for bi, (b, br) in enumerate(net.branches):
                mask = net.wall_mask(br)
                for si, side in enumerate((-1, 1)):
                    self._net_wall(c, br, side, mask[si])
                mx, mz = br.centre[len(br) // 2]
                if not clean:
                    c.create_text(*self.to_screen(mx, mz), text="BRANCH %d" % (bi + 1), fill=BRANCH_EDGE,
                                  font=("Segoe UI", 9, "bold"))
            for sp, ds in net.splits:
                for d in ds:
                    flat = []
                    for x, z in d.centre[::2] + [d.centre[-1]]:
                        flat += self.to_screen(x, z)
                    if len(flat) >= 4:
                        c.create_line(*flat, fill=SPLIT_COL, width=max(3, int(3 * self.scale) + 2), capstyle="round")
            for si, (sp, ds) in enumerate(net.splits if not clean else ()):
                s0, s1 = network.split_span(r, sp)
                x, z, _ = r.at((s0 + s1) / 2, r.w_at((s0 + s1) / 2) / 2 + 14)
                c.create_text(*self.to_screen(x, z), text="%d LANES" % network.lane_count(sp), anchor="w",
                              fill=SPLIT_COL if si != self.sel_split else "#ffffff", font=("Segoe UI", 9, "bold"))
        if net is not None and self.view_mode.get() == "ai":     # AI tab: where the AI can go off the main line
            try:
                for a in net.alt_routes(0.0):
                    flat = []
                    for p in a["points"][::2]:
                        flat += self.to_screen(p[0] - p[5] * p[3] / 2, p[2] + p[4] * p[3] / 2)   # middle of its lane
                    if len(flat) >= 4:
                        c.create_line(*flat, fill="#ff9a20", width=2, dash=(6, 3))
            except Exception:                         # noqa: BLE001 - only a picture
                pass
        if clean:
            return
        # handles
        for kind, i, x, z in self.net_handles():
            sx, sy = self.to_screen(x, z)
            col = BRANCH_EDGE if kind[0] == "b" else SPLIT_COL
            sel = (kind[0] == "b" and i == self.sel_branch) or (kind[0] == "s" and i == self.sel_split)
            c.create_oval(sx - HANDLE, sy - HANDLE, sx + HANDLE, sy + HANDLE, fill=col, outline="#ffffff" if sel else "#000",
                          width=2 if sel else 1)
        # branch points
        for bi, b in enumerate(self.map.get("branches") or []):
            for pi, p in enumerate(b.get("points") or []):
                sx, sy = self.to_screen(p["x"], p["z"])
                sel = bi == self.sel_branch and pi == self.sel_bpt
                c.create_rectangle(sx - 6, sy - 6, sx + 6, sy + 6, fill=BRANCH_EDGE, outline="#ffffff" if sel else "#000",
                                   width=2 if sel else 1)
                if p.get("h"):
                    c.create_text(sx, sy - 13, text="^%.0fm" % p["h"], fill="#cfd6df", font=("Segoe UI", 8))
        # the branch / split being drawn
        d = self.net_draft
        if d and self.road:
            x, z, _ = self.road.at(d["from_m"])
            flat = list(self.to_screen(x, z))
            for p in d["points"]:
                flat += self.to_screen(p["x"], p["z"])
            if getattr(self, "mouse_xy", None):
                flat += list(self.mouse_xy)
            if len(flat) >= 4:
                c.create_line(*flat, fill=BRANCH_EDGE if d["kind"] == "branch" else SPLIT_COL, width=3, dash=(8, 4))
            sx, sy = self.to_screen(x, z)
            c.create_oval(sx - HANDLE, sy - HANDLE, sx + HANDLE, sy + HANDLE, fill="#ffffff", outline="#000")

    def _net_wall(self, c, road, side, cut):
        """a road's wall line (glow + bright line) without its cut parts - the build's exact pieces (the main and
        the branch walls meet in a V at the forks / merges)"""
        runs = self.net.wall_runs(road, side) if self.net is not None else []
        for run in runs:
            flat = []
            for x, z in run:
                flat += self.to_screen(x, z)
            if len(flat) >= 4:
                c.create_line(*flat, fill="#0d5566", width=max(6, int(4 * self.scale) + 6), capstyle="round",
                              joinstyle="round")
                c.create_line(*flat, fill="#28e6ff", width=max(2, int(1.2 * self.scale) + 2), joinstyle="round")

    def net_main_cut(self, side):
        """the main road's cut wall parts (side -1 / +1), or None when nothing is cut"""
        if self.net is None or not self.net.branches:
            return None
        return self.net.wall_mask(self.net.main)[0 if side < 0 else 1]

    def net_checks(self):
        """[(ok, text)] for the CHECKS box"""
        out = []
        if self.net is None:
            return out
        for p in self.net.problems:
            out.append((False, p))
        r = self.road
        import gen_aid
        si, fi = gen_aid.race_lines(r, self.map)
        busy = self.net.busy_ranges()
        for idx, name in ((si, "START"),) + (() if r.closed else ((fi, "FINISH"),)):
            for a, b in busy:
                hit = (r.closed and (idx - a) % len(r) <= (b - a)) or (not r.closed and a <= idx <= b)
                if hit:
                    out.append((False, "The %s line is at a branch / lane split: move one of them" % name))
                    break
        for bi, blen, mlen, ratio in self.net.balance():   # is a branch a fair choice?
            pct = (ratio - 1.0) * 100
            if ratio < BALANCE_SHORT:
                out.append(("warn", "branch %d is much SHORTER than the main road it skips: %.0f m vs %.0f m (%.0f %%) "
                                    "- a big shortcut, everyone will take it" % (bi + 1, blen, mlen, pct)))
            elif ratio > BALANCE_LONG:
                out.append(("warn", "branch %d is much LONGER than the main road it skips: %.0f m vs %.0f m (+%.0f %%) "
                                    "- nobody will take it" % (bi + 1, blen, mlen, pct)))
            else:
                out.append((True, "branch %d balanced: %.0f m vs main %.0f m (%+.0f %%)" % (bi + 1, blen, mlen, pct)))
        for bi, (b, br) in enumerate(self.net.branches):
            if getattr(br, "lowered", 0.0) > 0.5:
                out.append((True, "branch %d: heights eased to max 10 %% slope (up to %.1f m lower/higher than "
                                  "set) - its ends stay level with the road" % (bi + 1, br.lowered)))
        if self.net.branches or self.net.splits:
            out.append((True, "%d branch(es), %d lane split(s): the AI takes them too" % (len(self.net.branches),
                                                                                      len(self.net.splits))))
        return out

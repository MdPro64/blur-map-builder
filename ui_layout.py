"""
THE WINDOW'S LAYOUT - panels the player can resize, bars that wrap on a narrow window, saved layouts.

  Layouts     the sizes of every panel + the window size; kept in maps\\ui_layouts.json with the player's saved ones
  sash()      the thin bar next to a panel: drag = resize, double-click = back to its default size
  flow()      a row of buttons (pack) that wraps onto more lines when the window is too narrow for it
  wrap_grid() the same for a row made with grid
  Scroll      a frame whose content scrolls when the window is too low for it
"""
import json
import os
import tkinter as tk

import paths

DEFAULT = "Default"
WINDOW = (1500, 880)
MIN_WINDOW = (960, 600)
MIN_PANEL = 110                         # px: a panel the player drags never gets smaller than this ...
MIN_CENTRE = 320                        # ... and the map / 3D view always keeps this much of the window
SASH = 5
SASH_BG, SASH_HI = "#1c2838", "#1e9bff"
SCROLL_W, MIN_THUMB = 9, 24
TROUGH, THUMB, THUMB_HI = "#0b1420", "#5ab4ff", "#8ce6ff"


def sash(parent, axis="x"):
    """the bar itself (Layouts.add makes it work); axis x = drag left / right"""
    size = {"width": SASH} if axis == "x" else {"height": SASH}
    return tk.Frame(parent, bg=SASH_BG, cursor="sb_h_double_arrow" if axis == "x" else "sb_v_double_arrow", **size)


class Layouts:
    def __init__(self, root):
        self.root = root
        self.file = os.path.join(paths.TRACKS, "ui_layouts.json")
        self.panels = {}                # name: {"widget", "axis", "default", "sign"}
        self.sizes = {}                 # what the player set; fit() may show less on a small window
        self.saved = {}
        self.current = DEFAULT
        self._window = None
        self._zoomed = False
        self._save_job = None
        try:
            d = json.load(open(self.file, encoding="utf-8"))
            self.sizes, self.saved, self.current = d.get("sizes", {}), d.get("saved", {}), d.get("current", DEFAULT)
            self._window, self._zoomed = d.get("window"), d.get("zoomed", False)
        except (OSError, ValueError, AttributeError):
            pass
        if self.current != DEFAULT and self.current not in self.saved:
            self.current = DEFAULT
        root.minsize(*MIN_WINDOW)
        root.bind("<Configure>", self._root_size, add="+")

    # ---- panels
    def add(self, name, widget, bar, axis, default, sign=1):
        """widget = the panel with a fixed width (x) / height (y); bar = its sash; sign = +1: dragging right / down
        makes it bigger, -1: left / up"""
        self.panels[name] = {"widget": widget, "axis": axis, "default": default, "sign": sign}
        bar.bind("<ButtonPress-1>", lambda e: self._grab(e, name))
        bar.bind("<B1-Motion>", lambda e: self._drag(e, name))
        bar.bind("<ButtonRelease-1>", lambda e: self._changed())
        bar.bind("<Double-Button-1>", lambda e: self._one_default(name))
        bar.bind("<Enter>", lambda e: bar.configure(bg=SASH_HI))
        bar.bind("<Leave>", lambda e: bar.configure(bg=SASH_BG))
        widget.bind("<Map>", lambda e: self.root.after_idle(self.fit), add="+")      # a tab shows / hides panels
        widget.bind("<Unmap>", lambda e: self.root.after_idle(self.fit), add="+")
        self._show(name, self.size(name))

    def size(self, name):
        return int(self.sizes.get(name, self.panels[name]["default"]))

    def _show(self, name, px):
        p = self.panels[name]
        key = "width" if p["axis"] == "x" else "height"
        if int(p["widget"].cget(key)) != int(px):
            p["widget"].configure(**{key: int(px)})

    def _grab(self, e, name):
        w = self.panels[name]["widget"]
        self._start = (e.x_root, e.y_root, w.winfo_width() if self.panels[name]["axis"] == "x" else w.winfo_height())

    def _drag(self, e, name):
        p = self.panels[name]
        d = e.x_root - self._start[0] if p["axis"] == "x" else e.y_root - self._start[1]
        self.sizes[name] = max(MIN_PANEL, self._start[2] + p["sign"] * d)
        self.fit()

    def _one_default(self, name):
        self.sizes.pop(name, None)
        self.fit()
        self._changed()

    def fit(self):
        """the sizes as far as this window holds them: a small window shrinks the panels, a bigger one gives them
        back (the player's sizes stay stored)"""
        W, H = self.root.winfo_width(), self.root.winfo_height()
        if W < 50:
            return
        shown = [n for n, p in self.panels.items() if p["axis"] == "x" and p["widget"].winfo_ismapped()]
        total = sum(self.size(n) for n in shown)
        k = min(1.0, max(0, W - MIN_CENTRE) / total) if total else 1.0
        for n, p in self.panels.items():
            if p["axis"] == "x":
                self._show(n, max(60, self.size(n) * k) if n in shown else self.size(n))
            else:
                self._show(n, min(self.size(n), max(MIN_PANEL, H - 330)))

    def _root_size(self, e):
        if e.widget is self.root:
            self.fit()
            self._changed()

    # ---- saved layouts
    def names(self):
        return [DEFAULT] + sorted(self.saved)

    def _state(self):
        r = self.root
        return {"sizes": dict(self.sizes), "window": [r.winfo_width(), r.winfo_height()], "zoomed": r.state() == "zoomed"}

    def _apply(self, state):
        self.sizes = dict(state.get("sizes", {}))
        r = self.root
        if state.get("zoomed"):
            r.state("zoomed")
        else:
            r.state("normal")
            r.geometry("%dx%d" % tuple(state.get("window") or WINDOW))
        r.update_idletasks()
        self.fit()
        self._changed()

    def restore(self):
        """the start of the program: the layout of the last time"""
        if self._window:
            self._apply({"sizes": self.sizes, "window": self._window, "zoomed": self._zoomed})

    def load(self, name):
        self.current = name if name in self.saved else DEFAULT
        self._apply(self.saved.get(self.current, {}))

    def reset(self):
        """back to how the current layout was saved (Default: how the program comes)"""
        self.load(self.current)

    def save_as(self, name):
        self.saved[name] = self._state()
        self.current = name
        self._write()

    def delete(self, name):
        self.saved.pop(name, None)
        if self.current == name:
            self.current = DEFAULT
        self._write()

    def _changed(self):
        if self._save_job:
            self.root.after_cancel(self._save_job)
        self._save_job = self.root.after(800, self._write)

    def _write(self):
        self._save_job = None
        s = self._state()
        try:
            os.makedirs(os.path.dirname(self.file), exist_ok=True)
            with open(self.file + ".tmp", "w", encoding="utf-8") as f:
                json.dump({"current": self.current, "sizes": self.sizes, "window": s["window"], "zoomed": s["zoomed"],
                           "saved": self.saved}, f, indent=1)
            os.replace(self.file + ".tmp", self.file)
        except OSError:
            pass                        # only the layout: the next change tries again


def _pad(v):
    return tuple(int(str(x)) for x in v) if isinstance(v, (tuple, list)) else (int(str(v)), int(str(v)))


def flow(frame):
    """frame's buttons, packed left / right, wrap onto more lines instead of being cut off by a narrow window.
    The right side stays on the first line. Children are placed from now on: add every child before calling this"""
    items = []
    for w in frame.pack_slaves():
        i = w.pack_info()
        items.append((w, i.get("side"), _pad(i.get("padx", 0)), _pad(i.get("pady", 0))))
        w.pack_forget()
    frame.pack_propagate(False)
    job = []

    def lay():
        job.clear()
        W = frame.winfo_width()
        if W < 50:
            return
        y, row_h, x, end = 0, 0, 0, W
        for w, side, px, py in items:                     # first line, from the right
            if side != "right":
                continue
            need = w.winfo_reqwidth() + sum(px)
            if end - need < 0 and end < W:
                y, row_h, end = y + row_h, 0, W
            end -= need
            w.place(x=end + px[0], y=y + py[0])
            row_h = max(row_h, w.winfo_reqheight() + sum(py))
        if y > 0:                                         # the right side took several lines: the rest goes below
            y, row_h, end = y + row_h, 0, W
        for w, side, px, py in items:
            if side == "right":
                continue
            need = w.winfo_reqwidth() + sum(px)
            if x + need > end and (x > 0 or end < W):
                y, row_h, x, end = y + row_h, 0, 0, W
            w.place(x=x + px[0], y=y + py[0])
            x += need
            row_h = max(row_h, w.winfo_reqheight() + sum(py))
        if y + row_h != int(frame.cget("height")):
            frame.configure(height=y + row_h)

    def later(e=None):
        if not job:
            job.append(frame.after_idle(lay))

    frame.bind("<Configure>", later, add="+")
    for w, *_ in items:
        w.bind("<Configure>", later, add="+")
    frame.configure(height=max((t[0].winfo_reqheight() + sum(t[3]) for t in items), default=1))
    return lay


def wrap_grid(frame):
    """frame's columns (made with grid, label over field) wrap into more rows of columns on a narrow window"""
    cells = [(w, w.grid_info()) for w in frame.grid_slaves()]
    cols = sorted({int(i["column"]) for _, i in cells})
    rows = 1 + max(int(i["row"]) + int(i["rowspan"]) - 1 for _, i in cells)
    state = {"spots": None}

    def lay(e=None):
        W = frame.winfo_width()
        if W < 50:
            return
        spots, x, line, col = {}, 0, 0, 0
        for c in cols:
            need = max(w.winfo_reqwidth() + sum(_pad(i["padx"])) for w, i in cells if int(i["column"]) == c)
            if x + need > W and x > 0:
                x, line, col = 0, line + 1, 0
            spots[c] = (line, col)
            x += need
            col += 1
        if spots != state["spots"]:
            state["spots"] = spots
            for w, i in cells:
                ln, cl = spots[int(i["column"])]
                w.grid(row=ln * rows + int(i["row"]), column=cl)

    frame.bind("<Configure>", lay, add="+")


class ScrollBar(tk.Canvas):
    """a slim dark scroll bar (Windows draws tk's own white, whatever its colours say): command = a view's yview"""

    def __init__(self, parent, command, bg=TROUGH):
        super().__init__(parent, bg=bg, width=SCROLL_W, highlightthickness=0, borderwidth=0, cursor="arrow")
        self.command = command
        self.span = (0.0, 1.0)
        self.thumb = self.create_rectangle(0, 0, 0, 0, fill=THUMB, width=0)
        self.bind("<Configure>", lambda e: self._draw())
        self.bind("<ButtonPress-1>", self._press)
        self.bind("<B1-Motion>", self._move)
        self.bind("<Enter>", lambda e: self.itemconfigure(self.thumb, fill=THUMB_HI))
        self.bind("<Leave>", lambda e: self.itemconfigure(self.thumb, fill=THUMB))

    def set(self, lo, hi):
        self.span = (float(lo), float(hi))
        self._draw()

    def _draw(self):
        H = self.winfo_height()
        lo, hi = self.span
        y0 = lo * H
        self.coords(self.thumb, 2, y0, self.winfo_width() - 2, max(hi * H, y0 + MIN_THUMB))

    def _press(self, e):
        lo, hi = self.span
        at = e.y / max(1, self.winfo_height())
        if not lo <= at <= hi:                         # beside the thumb: jump there
            self.command("moveto", at - (hi - lo) / 2)
            lo = self.span[0]
        self._grip = at - lo

    def _move(self, e):
        self.command("moveto", e.y / max(1, self.winfo_height()) - self._grip)


class Scroll(tk.Frame):
    """.inner = the frame to fill; a scroll bar shows when the content is higher than the window has room for"""

    def __init__(self, parent, bg):
        super().__init__(parent, bg=bg)
        self.cv = tk.Canvas(self, bg=bg, highlightthickness=0, borderwidth=0, width=1, height=1)
        self.bar = ScrollBar(self, self.cv.yview)
        self.cv.configure(yscrollcommand=self.bar.set)
        self.cv.pack(side="left", fill="both", expand=True)
        self.inner = tk.Frame(self.cv, bg=bg)
        self.item = self.cv.create_window(0, 0, anchor="nw", window=self.inner)
        self.inner.bind("<Configure>", lambda e: self._size())
        self.cv.bind("<Configure>", lambda e: self._size())
        self.bind("<Enter>", lambda e: self.cv.bind_all("<MouseWheel>", self._wheel))
        self.bind("<Leave>", lambda e: self.cv.unbind_all("<MouseWheel>"))

    def _size(self):
        H = self.cv.winfo_height()
        need = self.inner.winfo_reqheight()
        self.cv.itemconfigure(self.item, width=self.cv.winfo_width(), height=max(H, need))
        self.cv.configure(scrollregion=(0, 0, 1, max(H, need)))
        if need > H and not self.bar.winfo_manager():
            self.bar.pack(side="right", fill="y", before=self.cv)
        elif need <= H and self.bar.winfo_manager():
            self.bar.pack_forget()

    def _wheel(self, e):
        if self.inner.winfo_reqheight() > self.cv.winfo_height():
            self.cv.yview_scroll(-1 if e.delta > 0 else 1, "units")

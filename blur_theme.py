"""
BLUR LOOK for the track designer (tkinter): near-black navy background, dark glass panels, ONE blue for what is
selected / active, orange only for Build (Blur's highlight colour), white text, rounded buttons with a soft glow
edge and a hover shade.

install() swaps tkinter's widget classes for themed ones BEFORE the designer builds its window, so every button,
tab, toggle, label, frame, entry and list the designer (and its 2D / 3D / Surface / AI parts) makes gets the look -
without touching hundreds of lines. Old hard-coded colours are folded into the few theme colours (REMAP); colours
that MEAN something (surface types, power-up types) are kept as they are.
"""
import tkinter as tk
import tkinter.font as tkfont

# ---- the palette (Blur's menus) ------------------------------------------------------------------------------
BG = "#060a14"            # map / window background
PANEL = "#0b1426"         # side panels, bars
PANEL2 = "#0e1a31"        # second bar row
GLASS = "#13233f"         # a button
GLASS_HI = "#1b3358"      # a button under the mouse
EDGE = "#2a4a78"          # a button's glow edge
BLUE = "#1e9bff"          # selected tab / tool / toggle, Play
BLUE_HI = "#45b2ff"
ORANGE = "#ff8a1e"        # Build (the one warm colour)
DANGER = "#3a1624"        # delete buttons: dark red glass ...
DANGER_EDGE = "#a8384f"   # ... with a red edge
TEXT = "#e9f2ff"
MUTED = "#8095b6"
HEAD = "#4fc3ff"          # headings
FIELD = "#08111f"         # entries, lists, text boxes
RADIUS = 9

# old colours -> theme colours (backgrounds of buttons / bars / panels, a few text colours)
REMAP = {
    "#0e1726": BG, "#15223a": PANEL, "#1a2b48": PANEL, "#122036": PANEL2, "#243a60": GLASS, "#30507f": GLASS_HI,
    "#3d8ee8": BLUE, "#22b866": ORANGE, "#1aa3e8": BLUE, "#7a2a3a": DANGER, "#0f1b2e": FIELD, "#16233a": PANEL,
    "#1a1e24": FIELD, "#20242b": PANEL, "#2a2f1a": PANEL2, "#3a4228": GLASS, "#c08a1a": BLUE, "#4a5430": GLASS_HI,
    "#3a3428": GLASS, "#4a2a2a": DANGER, "#1c3a5c": GLASS, "#2a3038": GLASS, "#2c3440": GLASS_HI, "#3b5f8a": BLUE,
    "#2d7d9a": BLUE, "#2a3a50": GLASS, "#1d2228": PANEL, "#262a30": FIELD, "#4a5060": EDGE, "#7a5a10": BLUE,
    # text
    "#dbe6f5": TEXT, "#d8dee6": TEXT, "#8fd3ff": HEAD, "#9aa8b8": MUTED, "#7f8a96": MUTED, "#6f7c8a": MUTED,
    "#8fb6e0": HEAD, "#cfd6df": TEXT,
}
# button backgrounds that are a ROLE (everything else - surface / power-up colours - keeps its own colour)
ROLE = {GLASS: "glass", GLASS_HI: "glass", PANEL: "glass", PANEL2: "glass", BG: "glass", FIELD: "glass",
        BLUE: "blue", ORANGE: "orange", DANGER: "danger"}

_orig = {}


def _map(c):
    if isinstance(c, str) and c.lower() in REMAP:
        return REMAP[c.lower()]
    return c


def _hex(c):
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4))


def _mix(a, b, t):
    a, b = _hex(a), _hex(b)
    return "#%02x%02x%02x" % tuple(int(x + (y - x) * t) for x, y in zip(a, b))


def _parent_bg(master):
    try:
        return master.cget("bg")
    except (tk.TclError, AttributeError):
        return BG


_cache = {}


def _pill(w, h, fill, edge, glow=False):
    """a rounded rectangle picture (transparent corners) for a button of w x h pixels"""
    key = (w, h, fill, edge, glow)
    if key in _cache:
        return _cache[key]
    from PIL import Image, ImageDraw, ImageTk
    s = 3                                                    # drawn 3x bigger, then shrunk: smooth corners
    im = Image.new("RGBA", (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = min(RADIUS, h // 2) * s
    d.rounded_rectangle([0, 0, w * s - 1, h * s - 1], radius=r, fill=_hex(fill) + (255,),
                        outline=_hex(edge) + (255,), width=(2 if glow else 1) * s)
    # a soft light along the top (glass)
    top = Image.new("RGBA", (w * s, h * s), (0, 0, 0, 0))
    ImageDraw.Draw(top).rounded_rectangle([s, s, w * s - 1 - s, h * s // 2], radius=r, fill=(255, 255, 255, 14))
    im = Image.alpha_composite(im, top)
    im = im.resize((w, h), Image.LANCZOS)
    photo = ImageTk.PhotoImage(im)
    _cache[key] = photo
    return photo


def _colours(role, own):
    """(fill, edge, hover fill, selected fill, selected edge) of a button role"""
    if role == "blue":
        return BLUE, BLUE_HI, BLUE_HI, BLUE, "#bfe4ff"
    if role == "orange":
        return ORANGE, "#ffb466", "#ff9f45", ORANGE, "#ffd9a8"
    if role == "danger":
        return DANGER, DANGER_EDGE, "#4d1d30", DANGER, "#ff8098"
    if role == "own":                                        # a meaningful colour (surface / power-up type)
        return own, _mix(own, "#ffffff", 0.35), _mix(own, "#ffffff", 0.15), own, "#ffffff"
    return GLASS, EDGE, GLASS_HI, BLUE, BLUE_HI


class _Skin:
    """rounded pictures behind a button's text: normal / hover / selected, re-made when the size or text changes"""

    def _skin_init(self, own_bg, kind):
        self._own = own_bg if isinstance(own_bg, str) and own_bg.startswith("#") and len(own_bg) == 7 else GLASS
        self._role = ROLE.get(self._own, "own")
        self._kind = kind
        self._size = None
        self._stretch = 0          # picture width given by the layout (fill="x"), 0 = fits the text
        self._text_w = 0
        self._hover = False
        try:
            self._wchars = int(self._orig_cls.cget(self, "width") or 0)      # width in characters (text buttons)
        except (tk.TclError, ValueError):
            self._wchars = 0
        self._sel_role = None
        base = self._orig_cls
        base.configure(self, relief="flat", bd=0, highlightthickness=0, padx=10, pady=3, compound="center",
                       cursor="hand2", bg=_parent_bg(self.master), activebackground=_parent_bg(self.master),
                       fg=TEXT if self._role != "own" else self._text_on(self._own), activeforeground=TEXT)
        if kind != "button":
            base.configure(self, selectcolor=_parent_bg(self.master), offrelief="flat", overrelief="flat")
        self.bind("<Enter>", lambda e: self._set_hover(True), add="+")
        self.bind("<Leave>", lambda e: self._set_hover(False), add="+")
        self.bind("<Configure>", self._on_size, add="+")
        self.after_idle(self._reskin)

    @staticmethod
    def _text_on(col):
        r, g, b = _hex(col)
        return "#0a0f18" if (0.3 * r + 0.59 * g + 0.11 * b) > 165 else TEXT

    def _set_hover(self, on):
        """only swaps the colour - the size stays exactly as it is"""
        self._hover = on
        self._reskin(force=True)

    def _on_size(self, e):
        """stretched by its layout (fill="x"): the picture follows the real width; its own picture never makes it grow"""
        if not self._size:
            return
        extra = max(0, self.winfo_reqwidth() - self._size[0])      # borders around the picture
        target = e.width - extra
        if self._stretch:
            if abs(target - self._size[0]) > 2:
                self._stretch = target if target > self._text_w + 2 else 0
                self._reskin()
        elif target > self._size[0] + 2 and e.width > self.winfo_reqwidth() + 2:
            self._stretch = target
            self._reskin()

    def _reskin(self, force=False):
        try:
            base = self._orig_cls
            f = tkfont.Font(font=self.cget("font"))
            text = self.cget("text") or " "
            lines = text.split("\n")
            tw = max(f.measure(t) for t in lines) + 16
            th = f.metrics("linespace") * len(lines) + 7
            wchars = self._wchars
            if wchars:
                tw = max(tw, wchars * f.measure("0") + 16)
            self._text_w = tw
            w = max(tw, self._stretch)
            h = max(th, 20)
            fill, edge, hover, sfill, sedge = _colours(self._role, self._own)
            norm = _pill(w, h, hover if self._hover else fill, edge)
            base.configure(self, image=norm, padx=0, pady=0, width=0, height=0)
            if self._kind != "button":
                sel = _pill(w, h, BLUE if self._role in ("glass",) else sfill, sedge, glow=True)
                base.configure(self, selectimage=sel)
                if self._role == "glass":
                    base.configure(self, fg=TEXT)
            self._size = (w, h)
        except tk.TclError:
            pass

    def configure(self, cnf=None, **kw):
        if cnf:
            kw = dict(cnf, **kw)
        if not kw:
            return self._orig_cls.configure(self)
        restyle = False
        if "bg" in kw or "background" in kw:
            own = _map(kw.pop("bg", kw.pop("background", None)))
            self._own = own if isinstance(own, str) and own.startswith("#") and len(own) == 7 else self._own
            self._role = ROLE.get(self._own, "own")
            restyle = True
        for k in ("relief", "bd", "borderwidth", "highlightthickness", "selectcolor", "activebackground", "padx",
                  "pady", "indicatoron"):
            kw.pop(k, None)
        if "fg" in kw:
            kw["fg"] = _map(kw["fg"])
        if "width" in kw:
            try:
                self._wchars = int(kw.pop("width") or 0)
            except (TypeError, ValueError):
                pass
            restyle = True
        if "text" in kw or "font" in kw:
            restyle = True
        out = self._orig_cls.configure(self, **kw) if kw else None
        if restyle:
            self._reskin()
        return out

    config = configure


def _clean(kw):
    """theme colours for the plain options of any widget"""
    for k in ("bg", "background", "fg", "foreground", "activebackground", "highlightbackground",
              "disabledbackground", "insertbackground", "selectbackground", "troughcolor"):
        if k in kw:
            kw[k] = _map(kw[k])
    return kw


def install():
    """swap tkinter's classes for the themed ones (call once, before the designer window is built)"""
    if _orig:
        return
    for name in ("Button", "Radiobutton", "Checkbutton", "Label", "Frame", "Entry", "Spinbox", "Listbox", "Text",
                 "Canvas", "OptionMenu", "Scale", "LabelFrame", "Toplevel"):
        _orig[name] = getattr(tk, name)

    def skinned(name, kind):
        base = _orig[name]

        class Themed(_Skin, base):
            _orig_cls = base

            def __init__(self, master=None, cnf=None, **kw):
                kw = _clean(dict(cnf or {}, **kw))
                plain = kind != "button" and kw.get("indicatoron", True) not in (False, 0)
                if plain:                                    # a normal check box / radio: just colours
                    kw.setdefault("bg", _parent_bg(master))
                    kw["selectcolor"] = FIELD
                    kw.setdefault("activebackground", _parent_bg(master))
                    base.__init__(self, master, **kw)
                    self._plain = True
                    return
                self._plain = False
                own = kw.pop("bg", kw.pop("background", GLASS))
                for k in ("relief", "bd", "borderwidth", "selectcolor", "activebackground"):
                    kw.pop(k, None)
                base.__init__(self, master, **kw)
                self._skin_init(own, kind)

            def configure(self, cnf=None, **kw):
                if getattr(self, "_plain", True):
                    return base.configure(self, cnf, **_clean(kw))
                return _Skin.configure(self, cnf, **kw)
            config = configure
        Themed.__name__ = name
        return Themed

    tk.Button = skinned("Button", "button")
    tk.Radiobutton = skinned("Radiobutton", "radio")
    tk.Checkbutton = skinned("Checkbutton", "check")

    def plain(name, extra):
        base = _orig[name]

        class Themed(base):
            def __init__(self, master=None, cnf=None, **kw):
                kw = _clean(dict(cnf or {}, **kw))
                for k, v in extra.items():
                    kw.setdefault(k, v)
                base.__init__(self, master, **kw)

            def configure(self, cnf=None, **kw):
                return base.configure(self, cnf, **_clean(kw))
            config = configure
        Themed.__name__ = name
        return Themed
    field = {"bg": FIELD, "fg": TEXT, "insertbackground": TEXT, "relief": "flat", "highlightthickness": 1,
             "highlightbackground": EDGE, "highlightcolor": BLUE}
    tk.Label = plain("Label", {})
    tk.Frame = plain("Frame", {})
    tk.LabelFrame = plain("LabelFrame", {})
    tk.Toplevel = plain("Toplevel", {"bg": PANEL})
    tk.Canvas = plain("Canvas", {})
    tk.Entry = plain("Entry", field)
    tk.Spinbox = plain("Spinbox", dict(field, buttonbackground=GLASS))
    tk.Text = plain("Text", dict(field, highlightthickness=0))
    tk.Listbox = plain("Listbox", {"bg": FIELD, "fg": TEXT, "relief": "flat", "highlightthickness": 0,
                                   "selectbackground": BLUE, "selectforeground": "#ffffff"})
    tk.Scale = plain("Scale", {"bg": PANEL, "fg": TEXT, "troughcolor": FIELD, "highlightthickness": 0, "bd": 0,
                               "activebackground": BLUE})

    base_om = _orig["OptionMenu"]

    class OptionMenu(base_om):
        def __init__(self, master, variable, value, *values, **kw):
            base_om.__init__(self, master, variable, value, *values, **kw)
            base_om.configure(self, bg=GLASS, fg=TEXT, activebackground=GLASS_HI, activeforeground=TEXT,
                              relief="flat", bd=0, highlightthickness=1, highlightbackground=EDGE, padx=8)
            self["menu"].configure(bg=PANEL, fg=TEXT, activebackground=BLUE, activeforeground="#ffffff", bd=0,
                                   relief="flat")

        def configure(self, cnf=None, **kw):
            return base_om.configure(self, cnf, **_clean(kw))
        config = configure
    tk.OptionMenu = OptionMenu

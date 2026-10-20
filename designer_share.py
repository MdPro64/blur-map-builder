"""
Track designer: SHARE maps with other players - export a map to a .blurmap file, import one.

A .blurmap is a small zip: map.json (the map's design exactly as world.json keeps it, without the game ids this PC
gives it: they are made again on the other PC) + info.json (format, name, location, date, and "settings": the
location's LIGHTING and GRIP - they live outside the map, so without them the map would look / drive differently on
another PC) + the map's own pictures (minimap picture, menu picture) when it has any. The game files are never sent: the other player presses
Build + install and their PC makes them (about a minute).

Import: as ITS OWN new location with the maker's lighting + grip (exactly as made - recommended), or into the
CURRENT location (then it uses that location's lighting + grip); renamed "(2)" etc. if a name is taken; its
pictures are unpacked to tracks/imported/<map id>/. A .blurmap from someone else is only read as data: JSON + pictures, nothing in it is
run; file names inside the zip are never used as paths.
"""
import copy
import datetime
import json
import os
import zipfile
from tkinter import filedialog, messagebox

import surface
import world as world_mod

FORMAT = 1
EXT = ".blurmap"
GAME_IDS = ("route_name", "route_uid", "loc_id", "list_uids")       # world.assign_ids makes them on every PC
MAX_JSON = 8 * 1024 * 1024                                           # bytes: refuse anything absurd
PICTURES = (".png", ".jpg", ".jpeg", ".bmp", ".gif")


def _pictures(m):
    """[(key path, file)] of the map's own picture files that exist on this PC"""
    out = []
    mm = m.get("minimap") or {}
    if mm.get("image") and os.path.isfile(mm["image"]):
        out.append((("minimap", "image"), mm["image"]))
    if m.get("picture") and os.path.isfile(m["picture"]):
        out.append((("picture",), m["picture"]))
    return out


def export_map(m, loc_name, path, settings=None):
    """write map m to `path` (.blurmap) with its location's settings ({"lighting", "surface_feel"}). Returns the
    number of pictures packed."""
    data = copy.deepcopy(m)
    for k in GAME_IDS:
        data.pop(k, None)
    pics = _pictures(m)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for n, (keys, src) in enumerate(pics):
            name = "picture_%d%s" % (n, os.path.splitext(src)[1].lower())
            z.write(src, name)
            target = data
            for k in keys[:-1]:
                target = target[k]
            target[keys[-1]] = name                 # inside the file: the picture's name in the zip
        z.writestr("map.json", json.dumps(data, indent=1))
        z.writestr("info.json", json.dumps({"format": FORMAT, "name": m.get("name", "map"), "location": loc_name,
                                            "made": datetime.datetime.now().strftime("%Y-%m-%d %H:%M"),
                                            "app": "Blur map builder (road designer)",
                                            "settings": settings or {}}, indent=1))
    return len(pics)


def read_map(path):
    """(map dict, info dict, {picture name in zip: bytes}) from a .blurmap; raises ValueError if it is not one"""
    try:
        z = zipfile.ZipFile(path)
    except zipfile.BadZipFile:
        raise ValueError("this is not a .blurmap file")
    with z:
        names = set(z.namelist())
        if "map.json" not in names:
            raise ValueError("no map inside (map.json missing)")
        for nm in ("map.json", "info.json"):
            if nm in names and z.getinfo(nm).file_size > MAX_JSON:
                raise ValueError("%s is far too big" % nm)
        m = json.loads(z.read("map.json").decode("utf-8"))
        info = json.loads(z.read("info.json").decode("utf-8")) if "info.json" in names else {}
        if not isinstance(m, dict) or not isinstance(m.get("points"), list) or len(m["points"]) < 2:
            raise ValueError("the map has no road in it")
        if int(info.get("format", FORMAT)) > FORMAT:
            raise ValueError("made by a newer map builder (format %s) - update yours" % info.get("format"))
        pics = {}
        for nm in names:
            if nm.startswith("picture_") and "/" not in nm and "\\" not in nm and nm.lower().endswith(PICTURES) \
                    and z.getinfo(nm).file_size < 32 * 1024 * 1024:
                pics[nm] = z.read(nm)
    return m, info, pics


class ShareTools:
    def export_map_ui(self, e=None):
        m = self.map
        path = filedialog.asksaveasfilename(parent=self.root, title="Export the map for another player",
                                            defaultextension=EXT, initialfile=world_mod.slug(m["name"]) + EXT,
                                            filetypes=[("Blur map", "*" + EXT)])
        if not path:
            return
        try:
            n = export_map(m, self.loc["name"], path, {
                "lighting": copy.deepcopy(self.loc.get("lighting")),        # None = untouched (Amboy day, lit)
                "surface_feel": surface.loc_feel(self.world, self.loc)})
        except OSError as ex:
            messagebox.showerror("Export", "Could not write the file:\n%s" % ex)
            return
        size = os.path.getsize(path)
        self.say("Exported '%s' with its lighting + grip -> %s (%.1f KB%s). Send this file; the other player uses "
                 "Import map." % (m["name"], path, size / 1024, ", %d picture(s)" % n if n else ""), "#58d06a")

    def import_map_ui(self, e=None):
        path = filedialog.askopenfilename(parent=self.root, title="Import a map from another player",
                                          filetypes=[("Blur map", "*" + EXT), ("All files", "*.*")])
        if not path:
            return
        if self.total_maps() >= world_mod.MAX_MAPS:
            messagebox.showwarning("Too many maps", "At most %d maps in total - delete one first." % world_mod.MAX_MAPS)
            return
        try:
            m, info, pics = read_map(path)
        except (ValueError, OSError, UnicodeDecodeError, json.JSONDecodeError) as ex:
            messagebox.showerror("Import", "Could not import this file:\n%s" % ex)
            return
        st = info.get("settings") if isinstance(info.get("settings"), dict) else {}
        feel = st.get("surface_feel") if st.get("surface_feel") in dict(surface.FEEL_NAMES) else None
        own = messagebox.askyesnocancel(
            "Import map",
            "Import '%s' as its OWN location, with the maker's lighting and grip?\n\n"
            "Yes = exactly as it was made (recommended)\n"
            "No = into '%s' (it then uses that location's lighting and grip)" % (m.get("name", "map"), self.loc["name"]),
            parent=self.root)
        if own is None:
            return
        for k in GAME_IDS:
            m.pop(k, None)
        names = {mm["name"] for loc in self.world["locations"] for mm in loc["maps"]}
        base = str(m.get("name") or info.get("name") or "imported map")[:40]
        name, k = base, 2
        while name in names:
            name = "%s (%d)" % (base, k)
            k += 1
        m["name"] = name
        ids = {mm.get("id") for mm in self.loc["maps"]}
        mid, k = world_mod.slug(name), 2
        while mid in ids:
            mid = "%s_%d" % (world_mod.slug(name), k)
            k += 1
        m["id"] = mid
        # its pictures: unpacked next to the world file (names made here, never taken from the zip)
        folder = os.path.join(os.path.dirname(world_mod.WORLD_FILE), "imported", mid)
        saved = {}
        for zname, raw in pics.items():
            os.makedirs(folder, exist_ok=True)
            dst = os.path.join(folder, "%s%s" % (os.path.splitext(zname)[0], os.path.splitext(zname)[1].lower()))
            with open(dst, "wb") as f:
                f.write(raw)
            saved[zname] = dst
        mm = m.get("minimap") or {}
        if mm.get("image"):
            mm["image"] = saved.get(mm["image"], "")
            if not mm["image"]:
                mm["auto"] = True
        if m.get("picture"):
            m["picture"] = saved.get(m["picture"])
            if not m["picture"]:
                m.pop("picture")
        self.change()
        if own:                                            # a new location with the maker's settings
            lnames = {l["name"] for l in self.world["locations"]}
            lbase = str(info.get("location") or m["name"])[:40]
            lname, k = lbase, 2
            while lname in lnames:
                lname = "%s (%d)" % (lbase, k)
                k += 1
            lids = {l["id"] for l in self.world["locations"]}
            lid, k = world_mod.slug(lname), 2
            while lid in lids or lid == "flat_earth":
                lid = "%s_%d" % (world_mod.slug(lname), k)
                k += 1
            newloc = {"id": lid, "name": lname, "maps": []}
            if isinstance(st.get("lighting"), dict):
                newloc["lighting"] = copy.deepcopy(st["lighting"])
            if feel:
                newloc["surface_feel"] = feel
            self.world["locations"].append(newloc)
            self.cur = (len(self.world["locations"]) - 1, 0)
        self.loc["maps"].append(m)
        self.cur = (self.cur[0], len(self.loc["maps"]) - 1)
        self.refresh_tree()
        self.changed()
        self.fit()
        who = " (from %s, %s)" % (info.get("location"), info.get("made")) if info.get("made") else ""
        self.say("Imported '%s' into %s%s - press Build + install to race it" % (name, self.loc["name"], who),
                 "#58d06a")

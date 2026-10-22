"""GUIDE PICTURES, step 2. Usage: python release/guide/annotate_guide.py <capture folder> docs/guide
annotated screenshots of the map builder: numbered badges on the controls + a legend in Blur's colours"""
import json
import os
import re
import sys

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

SRC = sys.argv[1]          # screenshots + widget lists (capture_guide.py)
OUT = sys.argv[2]

# Blur's menu palette (blur_theme.py)
BG, PANEL, EDGE = "#060a14", "#0b1426", "#2a4a78"
BLUE, ORANGE, HEAD, TEXT, MUTED = "#1e9bff", "#ff8a1e", "#4fc3ff", "#e9f2ff", "#8095b6"
F = r"C:\Windows\Fonts"


def font(name, size):
    return ImageFont.truetype(os.path.join(F, name), size)


F_TITLE, F_SUB = font("seguibl.ttf", 34), font("segoeui.ttf", 17)
F_NAME, F_DESC, F_BADGE = font("segoeuib.ttf", 18), font("segoeui.ttf", 16), font("segoeuib.ttf", 15)
F_NOTE = font("segoeuii.ttf", 16)

_cache = {}


def widgets(shot):
    if shot not in _cache:
        _cache[shot] = json.load(open(os.path.join(SRC, shot + ".json"), encoding="utf-8"))
    return _cache[shot]


def _plain(t):
    return re.sub(r"^[^A-Za-z0-9*(]+", "", t).strip().lower()


def w(shot, text, cls=None, nth=0, exact=False):
    """the box of the control whose text starts with `text` (icons ignored)"""
    hits = [x for x in widgets(shot) if (_plain(x["text"]) == text.lower() if exact else
                                         _plain(x["text"]).startswith(text.lower())) and (cls is None or x["cls"] == cls)
            and x["box"][2] > x["box"][0]]
    if len(hits) <= nth:
        raise KeyError("%s: no '%s'" % (shot, text))
    return tuple(hits[nth]["box"])


def raw(shot, text):
    """the box of the control whose text is exactly `text` (icon-only buttons)"""
    return tuple(next(x["box"] for x in widgets(shot) if x["text"] == text))


def row(shot, label, x1=None, nth=0):
    """a label + everything on its line to the right of it (the - value + of a setting row, a slider ...)"""
    x0, y0, lx1, y1 = w(shot, label, "Label", nth)
    cy = (y0 + y1) / 2
    xs = [b for b in (tuple(x["box"]) for x in widgets(shot)) if b[1] <= cy <= b[3] and b[0] >= lx1 - 2
          and (x1 is None or b[2] <= x1) and b[3] - b[1] < 60]
    box = (x0, min([y0] + [b[1] for b in xs]), max([lx1] + [b[2] for b in xs]), max([y1] + [b[3] for b in xs]))
    return box


def union(*boxes):
    return (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))


def wrap(text, fnt, width):
    out, line = [], ""
    for word in text.split():
        t = (line + " " + word).strip()
        if fnt.getlength(t) <= width:
            line = t
        else:
            out.append(line)
            line = word
    if line:
        out.append(line)
    return out


def badge(d, cx, cy, n, r=13):
    d.ellipse((cx - r - 2, cy - r - 2, cx + r + 2, cy + r + 2), fill=BG)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=ORANGE)
    s = str(n)
    d.text((cx, cy - 1), s, font=F_BADGE, fill=BG, anchor="mm")


def make(name, shot, title, sub, items, crop=None, scale=1.0, note=None, legend_w=900, place="left",
         margin=(0, 0), below=False):
    img = Image.open(os.path.join(SRC, shot + ".png")).convert("RGB")
    crop = crop or (0, 0, img.width, img.height)
    img = img.crop(crop)
    if scale != 1.0:
        img = img.resize((round(img.width * scale), round(img.height * scale)), Image.LANCZOS)

    def tr(b):
        return (round((b[0] - crop[0]) * scale), round((b[1] - crop[1]) * scale),
                round((b[2] - crop[0]) * scale), round((b[3] - crop[1]) * scale))
    mt, ml = margin                                  # empty room above / left of the shot for badges
    if mt or ml:
        big_img = Image.new("RGB", (img.width + ml, img.height + mt), BG)
        big_img.paste(img, (ml, mt))
        img = big_img
    boxes = [tuple(v + (ml if i % 2 == 0 else mt) for i, v in enumerate(tr(it[0]))) for it in items]
    # dim everything, the explained controls stay bright
    dim = ImageEnhance.Brightness(img).enhance(0.38)
    for b in boxes:
        b2 = (max(0, b[0] - 3), max(0, b[1] - 3), min(img.width, b[2] + 3), min(img.height, b[3] + 3))
        dim.paste(img.crop(b2), b2[:2])
    shot_img = dim
    d = ImageDraw.Draw(shot_img)
    for b in boxes:
        d.rounded_rectangle((b[0] - 3, b[1] - 3, b[2] + 3, b[3] + 3), radius=6, outline=BLUE, width=2)
    for n, (b, it) in enumerate(zip(boxes, items), 1):
        big = (b[2] - b[0]) * (b[3] - b[1]) > 60000
        where = it[3] if len(it) > 3 else ("inside" if big else place)
        if where == "inside":                         # an area: the badge inside, top left
            cx, cy = b[0] + 28, b[1] + 40
        elif where == "top":                          # just above the control (toolbars)
            cx, cy = b[0] + 16, b[1] - 12
        elif where == "right":
            cx, cy = b[2] + 18, (b[1] + b[3]) / 2
        elif where == "inside_right":
            cx, cy = b[2] - 28, b[1] + 28
        elif where == "bottom":
            cx, cy = b[0] + 16, b[3] + 12
        else:                                         # just left of the control (lists, panels)
            cx, cy = b[0] - 18, (b[1] + 14 if b[3] - b[1] > 40 else (b[1] + b[3]) / 2)
        cx = min(max(cx, 15), shot_img.width - 15)
        cy = min(max(cy, 15), shot_img.height - 15)
        badge(d, cx, cy, n)

    # legend
    pad, gap = 22, 10
    entries = []
    for n, it in enumerate(items, 1):
        nm, desc = it[1], it[2]
        cols = None
        entries.append((n, nm, desc))

    def layout(ncol):
        cw = (legend_w - pad * (ncol + 1)) // ncol
        tw = cw - 62
        hs = []
        for n, nm, desc in entries:
            lines = wrap(nm, F_NAME, tw)
            dl = wrap(desc, F_DESC, tw)
            hs.append((lines, dl, 14 + 24 * len(lines) + 21 * len(dl) + 12))
        per = -(-len(entries) // ncol)
        colh = [sum(h[2] + gap for h in hs[c * per:(c + 1) * per]) for c in range(ncol)]
        return cw, tw, hs, per, max(colh)
    if below:
        legend_w = shot_img.width
    best = min((layout(c) + (c,) for c in ((4,) if below else (1, 2))),
               key=lambda L: max(L[4], 0) + (0 if L[5] == 1 else 60))
    cw, tw, hs, per, colh, ncol = best
    head_h = 110
    note_lines = wrap(note, F_NOTE, legend_w + shot_img.width - 2 * pad) if note else []
    note_h = 30 * len(note_lines) + (16 if note_lines else 0)
    if below:
        H = head_h + shot_img.height + pad + colh + note_h + pad
        W = shot_img.width + 2 * pad
    else:
        H = head_h + max(shot_img.height, colh + pad) + note_h + pad
        W = shot_img.width + legend_w + pad
    can = Image.new("RGB", (W, H), BG)
    cd = ImageDraw.Draw(can)
    # header: title + orange bar (Blur)
    cd.rectangle((0, 0, W, 6), fill=ORANGE)
    cd.text((pad, 22), title, font=F_TITLE, fill=TEXT)
    cd.text((pad + 2, 70), sub, font=F_SUB, fill=MUTED)
    cd.text((W - pad, 30), "BLUR MAP BUILDER", font=F_NAME, fill=HEAD, anchor="ra")
    cd.text((W - pad, 56), "by MDPRO", font=F_DESC, fill=MUTED, anchor="ra")
    y_img = head_h
    can.paste(shot_img, (pad, y_img))
    cd.rectangle((pad - 1, y_img - 1, pad + shot_img.width, y_img + shot_img.height), outline=EDGE)
    lx = pad + shot_img.width + pad
    ly = y_img
    if below:
        lx, ly = 0, y_img + shot_img.height + pad
    for c in range(ncol):
        x = lx + pad + c * (cw + pad) if below else lx + c * (cw + pad)
        y = ly
        for k in range(c * per, min((c + 1) * per, len(entries))):
            n, nm, desc = entries[k]
            lines, dl, h = hs[k]
            cd.rounded_rectangle((x, y, x + cw, y + h), radius=8, fill=PANEL, outline=EDGE)
            badge(cd, x + 26, y + 26, n)
            ty = y + 12
            for ln in lines:
                cd.text((x + 50, ty), ln, font=F_NAME, fill=HEAD)
                ty += 24
            for ln in dl:
                cd.text((x + 50, ty), ln, font=F_DESC, fill=TEXT)
                ty += 21
            y += h + gap
    if note_lines:
        y = H - pad - 30 * len(note_lines)
        for ln in note_lines:
            cd.text((pad, y), ln, font=F_NOTE, fill=MUTED)
            y += 30
    can.save(os.path.join(OUT, name + ".png"), optimize=True)
    print("made", name, can.size, "%d items, %d col" % (len(items), ncol))


S1, SP, SU, S3, SC, SA, SL, SS = ("1_2d_map", "2_2d_point", "2b_2d_powerup", "3_3d", "4_collision", "5_ai",
                                  "6_lighting", "7_surface")
SUP, SUD, SSRC, SIM = "8_update", "8b_update_download", "9_update_source", "10_import"
os.makedirs(OUT, exist_ok=True)

make("01_top_bar_and_tools", S1, "1. Top bar and 2D tools",
     "The tabs, the buttons that are always there, and the tools of the 2D map tab", [
         (w(S1, "2D map"), "2D map", "Draw and edit the road from above."),
         (w(S1, "3D", "Radiobutton"), "3D", "See the map in 3D, place game objects, set the start camera."),
         (w(S1, "Collision"), "Collision", "What the car hits: green = drivable, red = walls, yellow = bridge decks."),
         (w(S1, "AI path"), "AI path", "Where the AI cars drive."),
         (w(S1, "Lighting"), "Lighting", "Light, fog, sky and glow of the whole location."),
         (w(S1, "Surface"), "Surface", "Paint dirt, grass, water... on the road and set the grip."),
         (raw(S1, "\u2699"), "Update source", "Where updates come from (picture 14). Normally MDPRO's GitHub page."),
         (w(S1, "Up to date"), "Check for updates", "Light green = you have the newest version. Orange = a new "
                                                    "version: click it to update (pictures 12 - 13)."),
         (w(S1, "Save"), "Save (Ctrl+S)", "Saves your maps. Every change is also auto-saved."),
         (w(S1, "Play"), "Play (F5)", "Starts Blur REBORN: a normal start, or a quick LAN race on this map."),
         (w(S1, "Build"), "Build + install (Ctrl+B)", "Turns all your maps into game files and installs them for "
                                                      "Blur REBORN. Do this before Play."),
         (w(S1, "Select"), "Select (S)", "Click a point or power-up to edit it in the right panel."),
         (w(S1, "Move"), "Move (M)", "Drag points and power-ups."),
         (w(S1, "Add point"), "Add point (A)", "Click to add a point at the end of the road."),
         (w(S1, "Insert"), "Insert (I)", "Click near the road to add a point in between (also on branches)."),
         (w(S1, "Erase"), "Erase (E)", "Click a point or power-up to delete it."),
         (w(S1, "Branch", "Radiobutton"), "Branch (B)", "A second road that leaves and joins the main road: click "
                                                         "on the road, then its points, then on the road again."),
         (w(S1, "Lane split", "Radiobutton"), "Lane split (L)", "Splits the road into lanes with a divider: click "
                                                                 "where it starts, then where it ends."),
         (w(S1, "Measure", "Radiobutton"), "Measure (D)", "Click A, then B: the straight distance and the driving distance."),
         (w(S1, "Power-up row"), "Power-up row (R)", "Click on the road to place a row of power-ups across it."),
         (w(S1, "Single power-up"), "Single power-up (P)", "Click to place one power-up."),
         (union(w(S1, "type", "Label"), w(S1, "Nitro")), "Power-up type", "Which power-up you place (keys 1-8)."),
         (w(S1, "Snap"), "Snap (G)", "Points snap to a 10 m grid."),
         (w(S1, "Undo"), "Undo (Ctrl+Z)", "Takes back the last change (30 steps)."),
         (w(S1, "Redo"), "Redo (Ctrl+Y)", "Puts it back again."),
         (w(S1, "Fit"), "Fit (F)", "Zooms so the whole map fits."),
         (w(S1, "Help"), "Help (F1)", "Every key, plus the credits."),
     ], crop=(0, 0, 1600, 82), scale=1.25, place="top", margin=(34, 0), below=True,
     note="The right panel is explained in pictures 3, 4 and 5 - it changes with what you select.")

make("02_maps_panel", S1, "2. Locations, maps and the map view",
     "The left panel: your worlds (locations) and the maps in them - and the map itself", [
         ((6, 111, 204, 230), "Locations and maps", "A location is a world with its own lighting and grip; its "
                                                    "maps are the tracks in it. Click a map to edit it."),
         (w(S1, "New map"), "New map (Ctrl+N)", "A new map in this location."),
         (w(S1, "New location"), "New location (Ctrl+L)", "A new world, with its own lighting and grip."),
         (w(S1, "Duplicate map"), "Duplicate map (Ctrl+D)", "A copy of this map to try things on."),
         (w(S1, "Rename"), "Rename (F2)", "Rename the map or the location."),
         (w(S1, "Delete map"), "Delete map", "Deletes this map."),
         (w(S1, "Delete location"), "Delete location", "Deletes the location and every map in it."),
         (w(S1, "Import map"), "Import map (.blurmap)", "Adds a map someone shared with you."),
         (w(S1, "Export this map"), "Export this map", "Saves this map as a .blurmap file to share (its lighting "
                                                      "and grip go with it)."),
         ((210, 82, 1280, 979), "The map", "Left = use the tool, right-drag or middle-drag = move the map, wheel "
                                           "= zoom. Drag the squares on START / FINISH to move them."),
         (w(S1, "Select: click"), "Status line", "Tells you what the tool does and what just happened."),
     ], crop=(0, 80, 1280, 1000), margin=(0, 34), legend_w=900)

make("03_sidebar_map", S1, "3. Right panel - the map",
     "What you see when nothing is selected: the settings of the whole map", [
         (row(S1, "Default width"), "Default width", "Road width wherever a point has no width of its own."),
         (row(S1, "Start at"), "Start at", "Where the start line is, in metres along the road (or drag the green "
                                           "square on the map)."),
         (row(S1, "Finish at"), "Finish at", "Where the finish line is (sprints only)."),
         (w(S1, "Lap track"), "Lap track", "Joins the end of the road to its start: a circuit raced in laps "
                                           "(Laps - / + appears)."),
         (w(S1, "Auto bridges"), "Auto bridges", "Where the road crosses itself, the later part goes up onto a "
                                                 "bridge."),
         (w(S1, "Obstacles"), "Obstacles", "Containers and barrels on the road."),
         (row(S1, "Rows every"), "Rows every", "Space between the automatic power-up rows."),
         (row(S1, "Power-ups per row"), "Power-ups per row", "How many power-ups in each row (1-9)."),
         (w(S1, "Auto rows"), "Auto rows", "Places power-up rows along the whole road."),
         (w(S1, "Clear all"), "Clear all", "Removes every power-up of this map.", "right"),
         (w(S1, "Reverse"), "Reverse (V)", "The map is driven the other way."),
         (w(S1, "Mirror"), "Mirror (X)", "Flips the map left / right.", "right"),
         (union(w(S1, "Ships"), w(S1, "Start / finish arch"), w(S1, "Arrow boards")), "Auto scenery",
          "What every build places by itself: ships, cranes, ferris wheel, lamps, billboards, arrow boards, "
          "start / finish lights and arch, tall buildings. Tick what you want.", "left"),
         (w(S1, "Auto minimap"), "Auto minimap", "The minimap in the race is made from the road shape."),
         (w(S1, "Export road picture"), "Export road picture", "Saves the minimap picture so you can paint on it."),
         (w(S1, "Pick own"), "Pick own", "Uses your own picture as the minimap.", "bottom"),
         (w(S1, "Preview"), "Preview", "Shows the minimap picture.", "right"),
         ((1286, 628, 1594, 820), "Checks", "Before you build: green = OK, yellow = warning, red = must fix. "
                                            "Below: length, lap time, width, bridges.", "left"),
     ], crop=(1180, 80, 1600, 840), scale=1.35, legend_w=860)

make("04_sidebar_point", SP, "4. Right panel - a point selected",
     "Click a road point with Select (S): its settings appear at the top, the map settings stay below", [
         (union(w(SP, "POINT 3"), w(SP, "x -117")), "Point", "Which point of the road, and where it is."),
         (row(SP, "Width (Q/W)"), "Width (Q / W)", "The road width at this point."),
         (row(SP, "Height (PgDn/Up)"), "Height (PgDn / PgUp)", "Lifts the road here: ramps and bridges."),
         (row(SP, "Corner radius"), "Corner radius", "For a sharp corner: how tight it is."),
         (w(SP, "90° left"), "90° left (T)", "Makes a 90 degree left turn at this point."),
         (w(SP, "90° right"), "90° right (Y)", "Makes a 90 degree right turn at this point.", "right"),
         (w(SP, "Corner (C)"), "Corner (C)", "Makes this point a sharp corner, or smooth again."),
         (w(SP, "Delete", "Button", exact=True), "Delete", "Deletes this point.", "right"),
     ], crop=(1180, 80, 1600, 400), scale=1.6, legend_w=760)

make("05_sidebar_powerup", SU, "5. Right panel - a power-up selected",
     "Click a power-up or a row with Select (S): its settings appear at the top", [
         (union(w(SU, "POWER-UP ROW", "Label"), w(SU, "type: mixed")), "Power-up / row", "Its type. Change it with keys 1-8 or "
                                                                            "the type menu on the tool bar."),
         (w(SU, "Mixed types"), "Mixed types", "The row gets different power-ups."),
         (w(SU, "Delete", "Button", exact=True), "Delete", "Deletes the power-up or the whole row.", "right"),
         (row(SU, "Power-ups in this"), "Power-ups in this row", "How many power-ups this row has."),
     ], crop=(1180, 80, 1600, 330), scale=1.6, legend_w=700)

make("06_3d", S3, "6. The 3D tab",
     "See your map in 3D, place the game's own objects and set the camera before the race", [
         (w(S3, "Top view"), "Top view", "Look straight down."),
         (w(S3, "Reset", "Button"), "Reset", "Camera back to the whole map."),
         (w(S3, "Drive"), "Drive", "The camera drives the road from start to finish."),
         (w(S3, "Stop"), "Stop", "Stops the drive."),
         (w(S3, "Fly-in"), "Start camera: Fly-in", "The shot that flies over the track before the race.", "bottom"),
         (w(S3, "Grid shot"), "Start camera: Grid shot", "The shot of the cars on the start grid.", "bottom"),
         (w(S3, "Add view"), "Add view", "Adds what the 3D view shows now as a camera point of this shot.", "bottom"),
         (w(S3, "Remove last"), "Remove last", "Removes the last camera point.", "bottom"),
         (w(S3, "Play", "Button", 1), "Play", "Plays the start camera, as in the game.", "bottom"),
         (w(S3, "Auto", "Button", exact=True), "Auto", "Back to the automatic start cameras.", "bottom"),
         (w(S3, "Import 3D model"), "Import 3D model (.glb)", "Your own model with its textures (picture 11). It "
                                                                "joins the library under \"Your models\".", "left"),
         (w(S3, "PY_VAR", "Entry"), "Search", "Find an object by name.", "left"),
         ((216, 230, 510, 780), "Object library", "Your models and the game's objects. Hover one = its 3D picture; "
                                                  "pick one, then click in the view to place it. Scenery is look-only "
                                                  "(tick Solid = a wall).", "left"),
         (union(w(S3, "Bake 3D pictures"), w(S3, "Makes a picture")), "Bake 3D pictures",
          "Makes the hover picture of every object at once (a few minutes, one time). Without it a picture "
          "appears about a second after you hover.", "left"),
         (union(w(S3, "REAL SHAPES"), w(S3, "On: placed")), "Real shapes",
          "On: the game objects you placed show as their real models in the 3D view (read once, then fast). "
          "Off: boxes. A few models can't be read - they stay boxes.", "left"),
         ((512, 128, 1280, 979), "3D view", "Middle-drag = turn, Shift+middle = move, wheel = zoom. Click an "
                                            "object: G move, R turn, S scale, X hide. Orange dots = road points.", "inside_right"),
     ], place="top", note="The right panel works the same as in the 2D tab (pictures 3 - 5).")

make("07_collision", SC, "7. The Collision tab",
     "What the car will hit in the game - check it before you build", [
         (w(SC, "Top view"), "Top view", "Look straight down."),
         (w(SC, "Reset", "Button"), "Reset", "Camera back to the whole map."),
         (w(SC, "Drive"), "Drive", "The camera drives the road."),
         (w(SC, "Stop"), "Stop", "Stops the drive."),
         ((210, 100, 1280, 979), "Collision view", "Green = drivable ground, red = solid walls (the white ticks "
                                                   "show the side that pushes cars away), yellow = bridge decks."),
     ], place="top")

make("08_ai_path", SA, "8. The AI path tab",
     "Where the AI cars drive: the yellow line, with their lane around it", [
         (w(SA, "Auto AI path"), "Auto AI path", "Made for you along the whole road. This is what you get if you "
                                                 "never open this tab."),
         (w(SA, "Racing line"), "Racing line", "A line that cuts the inside of the bends."),
         (w(SA, "Pen"), "Pen (draw)", "Drag along the road to draw the line yourself. Drawing over a part "
                                      "replaces only that part."),
         (row(SA, "AI lane width"), "AI lane width", "How wide the AI's lane is around the line."),
         (w(SA, "Branch", "Radiobutton"), "Branch (B)", "Same as in the 2D tab."),
         (w(SA, "Lane split", "Radiobutton"), "Lane split (L)", "Same as in the 2D tab."),
         (w(SA, "now:"), "Which line now", "Tells you which AI line this map uses."),
         ((420, 370, 1060, 670), "The AI line", "Yellow line = where the AI drives, the band = its lane."),
     ], crop=(0, 0, 1280, 1000), place="bottom")

make("09_lighting", SL, "9. The Lighting tab",
     "The light of the whole location (all its maps). Values you changed are marked *", [
         (union(w(SL, "Start from the lighting"), w(SL, "Barcelona")), "Start from the lighting of",
          "Pick a city of the game as the starting point."),
         (w(SL, "Light up placed objects"), "Light up placed objects", "Lights the objects you placed (without "
                                                                        "it they look black).", "top"),
         (w(SL, "Reset to the preset"), "Reset to the preset", "Undoes your changes back to the city you picked.", "top"),
         (row(SL, "* Light on placed", x1=775), "Light on placed objects", "Colour (click the swatch) and strength."),
         (row(SL, "*    ... coming", x1=775), "... coming from above", "0 = light from all round."),
         (row(SL, "Sun colour", x1=775), "Sun", "Colour and strength of the sun."),
         (row(SL, "Sky (ambient)", x1=775), "Sky (ambient)", "The soft light from the sky."),
         (row(SL, "Baked light", x1=775), "Baked light", "The light stored in the level."),
         (row(SL, "Emissive", x1=775), "Emissive (glow)", "How bright glowing parts are."),
         (row(SL, "Fog colour", x1=775), "Fog colour", ""),
         (union(row(SL, "Distance fog", x1=775), row(SL, "Height fog", x1=775)), "Fog density", "Fog far away, and fog low "
                                                                                 "near the ground."),
         (union(row(SL, "Sky picture rotation", x1=775), row(SL, "Sky picture brightness", x1=775)), "Sky picture",
          "Turn the sky picture, and how bright it is."),
         (row(SL, "Exposure (middle", x1=775), "Exposure", "The overall brightness."),
         (union(row(SL, "Exposure min", x1=775), row(SL, "Exposure max", x1=775)), "Exposure min / max", "The limits of the "
                                                                                        "automatic brightness."),
         (union(row(SL, "* Bloom strength", x1=775), row(SL, "* Bloom threshold", x1=775)), "Bloom", "The glow around bright "
                                                                                      "things: strength, and from how bright."),
         ((784, 185, 1206, 487), "Preview", "A rough preview of a placed object."),
     ], crop=(210, 70, 1215, 760), scale=1.15, margin=(0, 30), note="Build + install, then look in the game.")

make("10_surface", SS, "10. The Surface tab",
     "Paint what the road is made of, and how much it changes the driving", [
         (union(w(SS, "LEFT click"), w(SS, "RIGHT click")), "Mouse", "Left click / drag = paint, right = remove.", "left"),
         (union(w(SS, "Dirt", "Radiobutton"), w(SS, "Asphalt", "Radiobutton")), "Brush",
          "What you paint: dirt, rough, gravel, grass, sand, water, deep water or asphalt."),
         (row(SS, "size"), "Size", "Brush size in metres."),
         (union(w(SS, "type", "Label"), w(SS, "Asphalt", "Menubutton")), "Whole road: type",
          "What the whole road is made of.", "left"),
         (row(SS, "sides", x1=260), "Sides", "A strip along both sides of the road (off = none)."),
         (union(w(SS, "Dirt", "Radiobutton", 1), w(SS, "Deep water", "Radiobutton", 1)), "Side type",
          "What the side strips are made of."),
         (w(SS, "Varied asphalt"), "Varied asphalt", "Patches of different asphalt, so it looks natural."),
         (w(SS, "Clear paint"), "Clear paint", "Removes all the paint of this map."),
         (union(w(SS, "GRIP"), w(SS, "Like the game")), "Grip (this location)",
          "How much the surfaces change the driving: Like the game, Strong or Extreme."),
         ((440, 400, 1060, 700), "The map", "Painted parts show in their colour."),
     ], crop=(0, 30, 1280, 760), scale=1.15, place="top", margin=(0, 34))

make("11_import_model", SIM, "11. Import your own 3D model (.glb)",
     "3D tab > Import 3D model: the map builder checks the model before it joins your library", [
         (union(w(SIM, "IMPORT 3D MODEL"), w(SIM, "24 triangles   ")), "The model",
          "Its name, triangles, size in metres, materials and texture size."),
         (union(w(SIM, "24 triangles - fine"), w(SIM, "Collision: none")), "The checks",
          "Green = fine, blue = good to know, yellow = may cause lag (over 10,000 triangles), red = too big "
          "(over 50,000: simplify it first, e.g. Blender's Decimate). Textures are shown at 256 x 256 in the game."),
         (w(SIM, "Collision: none"), "Collision", "None: cars drive through it. Tick Solid in the 3D tab to make "
                                                  "it a wall - a simple BOX around the whole model, not its shape."),
         (w(SIM, "Import + place"), "Import + place", "It joins \"Your models\" (kept with your maps); click in "
                                                      "the view to place it. G / R / S move, turn, scale it.", "bottom"),
         (w(SIM, "Cancel", "Button"), "Cancel", "Nothing is imported.", "right"),
     ], scale=1.4, legend_w=760,
     note="Make a .glb in Blender: File > Export > glTF 2.0, Format: glTF Binary (.glb).")

make("12_update", SUP, "12. A new version",
     "When a new version is out, this window opens at the start (or click the orange update button)", [
         (union(w(SUP, "Blur Map Builder v"), w(SUP, "you have")), "Versions", "The new version and the one you have."),
         (w(SUP, "", "Text"), "What's new", "What the new version changes.", "inside_right"),
         (w(SUP, "Update now"), "Update now", "Downloads and installs it. Your maps are kept.", "bottom"),
         (w(SUP, "Later", "Button"), "Later", "Not now - the orange button stays until you update.", "right"),
     ], scale=1.4, legend_w=640)

make("13_update_download", SUD, "13. Updating",
     "The download - then the map builder restarts by itself", [
         (w(SUD, "Downloading"), "Downloading", "Which file (there are two: the map builder and the map tester)."),
         (union(w(SUD, "", "Canvas"), w(SUD, "19.4 MB")), "Progress", "How much is done, and how fast."),
         (w(SUD, "Please don't close"), "Don't close it", "When it is done it says \"restarting\" and the new "
                                                          "version opens by itself."),
     ], scale=1.4, legend_w=640,
     note="Download failed? It says why, nothing was changed - press Try again.")

make("14_update_source", SSRC, "14. Update source",
     "The gear next to Check for updates: where the map builder gets its updates", [
         (union(w(SSRC, "github.com/", "Label"), w(SSRC, "PY_VAR", "Entry")), "GitHub page",
          "owner/name or the github.com link. Normally MDPRO's - if MDPRO stops making updates, put the page of "
          "whoever carries on here."),
         (w(SSRC, "Test", "Button", exact=True), "Test", "Checks that page has a release with the map builder "
                                                         "programs.", "bottom"),
         (w(SSRC, "Save", "Button", exact=True), "Save", "Updates come from that page from now on.", "bottom"),
         (w(SSRC, "MDPRO's"), "MDPRO's (default)", "Back to MDPRO's page.", "bottom"),
         (w(SSRC, "Only use a page"), "Trust", "Only use a page you trust: the map builder downloads and runs "
                                               "programs from it."),
     ], scale=1.4, legend_w=640)

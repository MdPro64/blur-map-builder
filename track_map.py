"""
Blur live track map - draws the loaded track's routes from above, with your car on it.

Colours:  grey = racing-line pieces   white = main race route (Logic)
          green = start   red = finish   yellow = split checkpoints   blue = junctions
          cyan arrows = start grid (where cars line up)
          orange dot = your car        pink dots = spots saved with F5
Buttons:  "Save track" writes every route to tracks/<time>.json (for the map builder)

Route recorder: press F7 in the game to start recording, drive, F7 again to stop.
  A point is taken every 4 m (like the game's own routes) and drawn in pink.
  "Save my route" writes it to my_routes/; saved routes for the same track are shown in purple.

How the routes are found (see _mod_project/notes/addresses.md "Track data"):
  world = [0x11B3778];  T = [[[world + 0xD24 + 0x18]] + 0x10]   (TrackData, type 0x20)
  splines = [T+8] (array), count = [T+0x10]
  spline: +0x1C flags, +0x48 points, +0x4C count; point = 11 floats (x, z, side x, side z, ...)
"""
import json
import os
import struct
import sys
import time
import tkinter as tk

HERE = os.path.dirname(os.path.abspath(sys.executable if getattr(sys, "frozen", False) else __file__))
sys.path.insert(0, os.path.join(HERE, "..", "trainer"))
import blur_trainer as bt  # noqa: E402

TRACK_DIR = os.path.join(HERE, "tracks")
ROUTE_DIR = os.path.join(HERE, "my_routes")
VK_F7 = 0x76
STEP = 4.0             # metres between recorded points (same as the game's routes)
LOOP_DISTANCE = 30.0   # finishing this close to the start makes it a loop
POSITIONS = os.path.join(HERE, "..", "saved_positions.jsonl")
POINT_SIZE = 0x2C
KINDS = [(0x20, "logic", "#ffffff", 3), (0x02, "start", "#34c759", 4), (0x04, "finish", "#ff3b30", 4),
         (0x08, "split1", "#ffd60a", 4), (0x10, "split2", "#ffd60a", 4), (0x40, "junction", "#0a84ff", 2)]
RACING = ("racing", "#636366", 2)
SIZE = 760


def find_track_data(g):
    world = g.u32(0x11B3778)
    if not world:
        return 0
    sentinel = g.u32(world + 0x924 + 0x20 * 0x20 + 0x18)
    node = g.u32(sentinel)
    if not node or node == sentinel:
        return 0
    return g.u32(node + 0x10)


GRID_SIZE = 0x44   # one start slot: direction at +0x1C, position at +0x34
GRID_COUNT = 0x1A90


def find_grid(g):
    """Start grid object = [[racer + 0x48] + 0x50] (see FUN_0049c4b0); 0 outside a race."""
    racer = bt.find_racer(g)
    container = g.u32(racer + 0x48) if racer else 0
    return g.u32(container + 0x50) if container else 0


def read_grid(g):
    """List of start slots: {pos: [x, y, z], dir: [x, y, z]}."""
    grid = find_grid(g)
    n = g.u32(grid + GRID_COUNT) if grid else 0
    out = []
    for k in range(n if n <= 64 else 0):
        e = grid + k * GRID_SIZE
        out.append({"pos": list(struct.unpack("<3f", g.read(e + 0x34, 12))),
                    "dir": list(struct.unpack("<3f", g.read(e + 0x1C, 12)))})
    return out


def kind_of(flags):
    for bit, name, colour, width in KINDS:
        if flags & bit:
            return name, colour, width
    return RACING


def read_track(g):
    """List of {kind, flags, points[(x, z, side_x, side_z, a, b, c)]}, or [] outside a race."""
    t = find_track_data(g)
    if not t:
        return []
    items, count = g.u32(t + 8), g.u32(t + 0x10)
    if not items or not 0 < count < 1000:
        return []
    out = []
    for i in range(count):
        s = g.u32(items + 4 * i)
        head = g.read(s, 0x50) if s else None
        if not head:
            continue
        flags = head[0x1C]
        ptr, n = struct.unpack_from("<II", head, 0x48)
        if not ptr or not 0 < n < 20000:
            continue
        raw = g.read(ptr, n * POINT_SIZE)
        if not raw:
            continue
        pts = [struct.unpack_from("<7f", raw, k * POINT_SIZE) for k in range(n)]
        out.append({"kind": kind_of(flags)[0], "flags": flags, "points": pts})
    return out


def track_id(track):
    """A short name for the loaded track, so saved routes can be matched to it."""
    logic = [s for s in track if s["kind"] == "logic"]
    main = logic[0]["points"] if logic else track[0]["points"]
    return "track_%d_%d_%d" % (round(main[0][0]), round(main[0][1]), len(main))


def side_of(dx, dz):
    """Unit vector across the road, for a travel direction (same convention as the game's points)."""
    n = (dx * dx + dz * dz) ** 0.5 or 1.0
    return -dz / n, dx / n


class TrackMap:
    def __init__(self, g):
        self.g = g
        self.track = []
        self.view = None
        self.keys = bt.Keys()
        self.recording = False
        self.route = []          # recorded points: [x, z, side_x, side_z, width, y, y]
        self.root = tk.Tk()
        self.root.title("Blur track map")
        self.root.configure(bg="#1c1c1e")
        bar = tk.Frame(self.root, bg="#1c1c1e")
        bar.pack(fill="x")
        tk.Button(bar, text="Reload track", command=self.load).pack(side="left", padx=6, pady=6)
        tk.Button(bar, text="Save track", command=self.save).pack(side="left", padx=6, pady=6)
        tk.Button(bar, text="Save my route", command=self.save_route).pack(side="left", padx=6, pady=6)
        tk.Button(bar, text="Clear my route", command=self.clear_route).pack(side="left", padx=6, pady=6)
        self.status = tk.Label(bar, text="", fg="#f2f2f2", bg="#1c1c1e")
        self.status.pack(side="left", padx=10)
        self.canvas = tk.Canvas(self.root, width=SIZE, height=SIZE, bg="#000000", highlightthickness=0)
        self.canvas.pack()
        self.load()
        self.tick()

    def load(self):
        self.track = read_track(self.g)
        if not self.track:
            self.status.config(text="No track found - are you in a race?")
            self.view = None
            return
        xs = [p[0] for s in self.track for p in s["points"]]
        zs = [p[1] for s in self.track for p in s["points"]]
        span = max(max(xs) - min(xs), max(zs) - min(zs)) or 1.0
        self.view = (min(xs), min(zs), (SIZE - 40) / span)
        n = sum(len(s["points"]) for s in self.track)
        self.status.config(text="%d routes, %d points" % (len(self.track), n))
        self.draw_track()

    def to_screen(self, x, z):
        x0, z0, scale = self.view
        return 20 + (x - x0) * scale, SIZE - 20 - (z - z0) * scale

    def draw_track(self):
        c = self.canvas
        c.delete("all")
        order = sorted(self.track, key=lambda s: s["kind"] != "racing")  # racing pieces underneath
        for s in order:
            _, colour, width = kind_of(s["flags"])
            coords = []
            for p in s["points"]:
                coords += self.to_screen(p[0], p[1])
            if len(coords) >= 4:
                c.create_line(*coords, fill=colour, width=width)
        try:
            with open(POSITIONS) as f:
                for line in f:
                    x, _, z = json.loads(line)["pos"]
                    sx, sy = self.to_screen(x, z)
                    c.create_oval(sx - 3, sy - 3, sx + 3, sy + 3, fill="#ff2d55", outline="")
        except (OSError, ValueError, KeyError):
            pass
        for slot in read_grid(self.g):          # start grid: small cyan arrows
            x, _, z = slot["pos"]
            dx, _, dz = slot["dir"]
            sx, sy = self.to_screen(x, z)
            ex, ey = self.to_screen(x + dx * 12, z + dz * 12)
            c.create_line(sx, sy, ex, ey, fill="#64d2ff", width=2, arrow="last")
        for route in self.saved_routes():
            self.draw_route(route["points"], "#bf5af2", "saved")
        self.draw_route(self.route, "#ff2d55", "rec")

    def draw_route(self, points, colour, tag):
        self.canvas.delete(tag) if tag == "rec" else None
        coords = []
        for p in points:
            coords += self.to_screen(p[0], p[1])
        if len(coords) >= 4:
            self.canvas.create_line(*coords, fill=colour, width=4, tags=tag)

    def saved_routes(self):
        if not self.track or not os.path.isdir(ROUTE_DIR):
            return []
        tid, out = track_id(self.track), []
        for name in sorted(os.listdir(ROUTE_DIR)):
            try:
                with open(os.path.join(ROUTE_DIR, name)) as f:
                    route = json.load(f)
                if route.get("track") == tid:
                    out.append(route)
            except (OSError, ValueError):
                pass
        return out

    def toggle_recording(self):
        self.recording = not self.recording
        if self.recording:
            self.route = []
            self.draw_route(self.route, "#ff2d55", "rec")
            bt.beep(1200)
        else:
            bt.beep(600)
            bt.beep(600)
        self.show_route_status()

    def show_route_status(self):
        if self.recording:
            self.status.config(text="RECORDING (F7 to stop): %d points, %.0f m" % (len(self.route), len(self.route) * STEP))
        elif self.route:
            self.status.config(text="Recorded %d points (%.0f m)%s - press Save my route"
                               % (len(self.route), len(self.route) * STEP, ", loop" if self.is_loop() else ""))

    def is_loop(self):
        if len(self.route) < 10:
            return False
        (x0, z0), (x1, z1) = self.route[0][:2], self.route[-1][:2]
        return ((x1 - x0) ** 2 + (z1 - z0) ** 2) ** 0.5 < LOOP_DISTANCE

    def record(self, pose):
        x, y, z = pose["pos"]
        if not self.route:
            sx, sz = side_of(pose["rot"][6], pose["rot"][8])
            self.route.append([x, z, sx, sz, 0.0, y, y])
            return
        px, pz, py = self.route[-1][0], self.route[-1][1], self.route[-1][5]
        dx, dz, dy = x - px, z - pz, y - py
        dist = (dx * dx + dz * dz) ** 0.5
        if dist < STEP:
            return
        sx, sz = side_of(dx, dz)
        if len(self.route) == 1:            # the first point gets the same direction
            self.route[0][2:4] = [sx, sz]
        # fast cars move more than 4 m between checks: fill in evenly spaced points
        for k in range(1, int(dist // STEP) + 1):
            t = k * STEP / dist
            self.route.append([px + dx * t, pz + dz * t, sx, sz, 0.0, py + dy * t, py + dy * t])
        self.draw_route(self.route, "#ff2d55", "rec")
        self.show_route_status()

    def save_route(self):
        if self.recording or len(self.route) < 2:
            self.status.config(text="Record a route first (F7 in the game).")
            return
        os.makedirs(ROUTE_DIR, exist_ok=True)
        name = time.strftime("route_%Y%m%d_%H%M%S.json")
        with open(os.path.join(ROUTE_DIR, name), "w") as f:
            json.dump({"track": track_id(self.track), "loop": self.is_loop(), "step": STEP,
                       "point_fields": ["x", "z", "side_x", "side_z", "width", "y", "y"],
                       "points": self.route}, f)
        self.route = []
        self.draw_track()
        self.status.config(text="Saved my_routes\\" + name)

    def clear_route(self):
        self.recording = False
        self.route = []
        self.canvas.delete("rec")
        self.status.config(text="Route cleared.")

    def save(self):
        if not self.track:
            return
        os.makedirs(TRACK_DIR, exist_ok=True)
        path = os.path.join(TRACK_DIR, time.strftime("track_%Y%m%d_%H%M%S.json"))
        with open(path, "w") as f:
            json.dump({"point_fields": ["x", "z", "side_x", "side_z", "a", "b", "c"],
                       "routes": self.track, "start_grid": read_grid(self.g)}, f)
        self.status.config(text="Saved " + os.path.basename(path))

    def tick(self):
        if not self.g.alive():
            self.root.destroy()
            return
        self.canvas.delete("car")
        if self.keys.pressed(VK_F7) and self.g.has_focus() and self.view:
            self.toggle_recording()
        if self.view:
            phys = bt.find_physics(self.g)
            pose = bt.read_pose(self.g, phys) if phys else None
            if pose and self.recording:
                self.record(pose)
            if pose:
                x, _, z = pose["pos"]
                sx, sy = self.to_screen(x, z)
                fx, fz = pose["rot"][6], pose["rot"][8]
                ex, ey = self.to_screen(x + fx * 25, z + fz * 25)
                self.canvas.create_line(sx, sy, ex, ey, fill="#ff9500", width=2, tags="car")
                self.canvas.create_oval(sx - 6, sy - 6, sx + 6, sy + 6, fill="#ff9500", outline="", tags="car")
        self.root.after(100, self.tick)


if __name__ == "__main__":
    TrackMap(bt.Game(wait=True)).root.mainloop()

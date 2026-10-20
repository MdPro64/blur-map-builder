import copy, math, os, sys
MB = r"E:\blur - bak\map_builder"
sys.path[:0] = [MB, os.path.join(MB, "flat_earth")]
import road as R
import road_designer as D


class Stub:
    def __init__(self, pts, pickups=None):
        self.map = {"points": pts, "width": 60.0, "pickups": pickups}
        self.road = R.Road(pts, 60.0, False)
        self.sel = None
        self.msg = ""

    def points(self):
        return self.map["points"]

    def change(self):
        pass

    def changed(self):
        self.road = R.Road(self.map["points"], 60.0, False)

    def say(self, t, *a):
        self.msg = t


def heading(a, b):
    return math.degrees(math.atan2(b["x"] - a["x"], b["z"] - a["z"]))


def check(name, pts, ops, pickups=None):
    s = Stub(copy.deepcopy(pts), copy.deepcopy(pickups))
    before = [math.hypot(b["x"] - a["x"], b["z"] - a["z"]) for a, b in zip(s.points(), s.points()[1:])]
    for k, side in ops:
        s.sel = k
        D.Designer.turn90(s, side)
        p = s.points()
        tin, tout = heading(p[k - 1], p[k]), heading(p[k], p[k + 1])
        rel = (tout - tin + 180) % 360 - 180          # + = clockwise (right) in x/z with z up
        print("  %s point %d %s: turn %+.1f deg %s" % (name, k + 1, "LEFT " if side > 0 else "RIGHT",
              rel, "OK" if abs(abs(rel) - 90) < 0.01 and (rel < 0) == (side > 0) else "WRONG"))
    after = [math.hypot(b["x"] - a["x"], b["z"] - a["z"]) for a, b in zip(s.points(), s.points()[1:])]
    keep = all(abs(x - y) < 0.01 for x, y in zip(before, after))
    r = s.road
    print("  %s: segment lengths kept %s, min radius %.0f, length %.0f, %s" % (name, keep, r.min_radius(), r.length,
          "pickups %s" % s.map["pickups"] if pickups else ""))


P = lambda x, z: {"x": x, "z": z}
line = [P(0, 0), P(400, 0), P(800, 100), P(1200, 0), P(1600, 0)]
check("line L at 2", line, [(1, 1)])
check("line R at 2", line, [(1, -1)])
check("line R at 2, L at 3, R at 4", line, [(1, -1), (2, 1), (3, -1)])
check("line L at 2 then R at 2 (redo)", line, [(1, 1), (1, -1)])
check("diagonal R at 3", [P(0, 0), P(300, 300), P(600, 500), P(900, 900)], [(2, -1)])
check("last point L", [P(0, 0), P(500, 0)], [(1, 1)])
check("with pickups", line, [(1, 1)], [{"kind": "row", "x": 200.0, "z": 0.0}, {"kind": "row", "x": 1400.0, "z": 0.0}])

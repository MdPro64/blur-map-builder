import copy, os, sys
MB = r"E:\blur - bak\map_builder"
sys.path[:0] = [MB, os.path.join(MB, "flat_earth")]
import road as R
import road_designer as D
import gen_aid

pts = [{"x": 0, "z": 0}, {"x": 1000, "z": 0}, {"x": 2000, "z": 300}]


class Stub:
    to_screen = lambda self, x, z: (x / 2, -z / 2)      # 0.5 px per m

    def __init__(self):
        self.map = {"points": copy.deepcopy(pts), "width": 60.0, "pickups": None}
        self.road = R.Road(self.map["points"], 60.0)
        self.msg = ""

    def points(self):
        return self.map["points"]

    def change(self):
        pass

    def changed(self):
        self.road = R.Road(self.map["points"], 60.0)

    def say(self, t, *a):
        self.msg = t


for name in ("hit_line", "set_line", "move_line", "reset_lines", "reverse"):
    setattr(Stub, name, getattr(D.Designer, name))
s = Stub()
L = s.road.length
show = lambda tag: print("%-28s start %5.0f  finish %5.0f   (road %.0f m)  %s" % (
    tag, *(s.road.dist[i] for i in gen_aid.race_lines(s.road, s.map)), L, s.msg))
show("default")
s.move_line("start", 20); show("start +20")
s.move_line("finish", -20); show("finish -20")
for _ in range(200):
    s.move_line("start", 20)
show("start +20 x200 (pushes finish)")
s.reset_lines(); show("reset")
si, fi = gen_aid.race_lines(s.road, s.map)
hx, hy = s.to_screen(*s.road.centre[si])
print("hit on start handle:", s.hit_line(hx + 3, hy - 2), " hit far away:", s.hit_line(hx + 60, hy))
s.set_line("start", 523.0); s.set_line("finish", 1700.0); show("dragged to 523 / 1700")
a = gen_aid.race_lines(s.road, s.map)
xy = (s.road.centre[a[0]], s.road.centre[a[1]])
s.reverse(); show("reversed")
b = gen_aid.race_lines(s.road, s.map)
print("same stretch after reverse: start now at old finish %.0f m away, finish at old start %.0f m away" % (
    ((s.road.centre[b[0]][0] - xy[1][0]) ** 2 + (s.road.centre[b[0]][1] - xy[1][1]) ** 2) ** .5,
    ((s.road.centre[b[1]][0] - xy[0][0]) ** 2 + (s.road.centre[b[1]][1] - xy[0][1]) ** 2) ** .5))

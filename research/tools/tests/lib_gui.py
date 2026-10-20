"""drive the 3D view's library like a user would (a copy of map 1 in memory; nothing saved)"""
import copy, os, sys, time
import tkinter as tk
MB = r"E:\blur - bak\map_builder"
sys.path[:0] = [MB, os.path.join(MB, "flat_earth")]
os.chdir(MB)
import view3d, world as world_mod
from PIL import ImageGrab

OUT = os.path.dirname(os.path.abspath(__file__))
root = tk.Tk()
root.geometry("1500x850+20+20")
v = view3d.View3D(root)
v.pack(fill="both", expand=True)
m = copy.deepcopy(world_mod.load()["locations"][0]["maps"][0])
m.pop("objects", None)
log = []


def shot(name):
    root.update()
    time.sleep(0.4)
    root.update()
    x, y = root.winfo_rootx(), root.winfo_rooty()
    ImageGrab.grab((x, y, x + root.winfo_width(), y + root.winfo_height())).save(os.path.join(OUT, name))


def step1():
    v.set_map(m, "3d")
    v.show(True)
    root.after(800, step2)


def step2():
    shot("lib_1_tree.png")
    v.lib_search.set("cement")
    root.update()
    leaf = [i for i in v.lib_tree.get_children(v.lib_tree.get_children(v.lib_tree.get_children(
        v.lib_tree.get_children("")[0])[0])[0])]
    log.append("search 'cement' -> top groups %s, first leaf %s" % (
        [v.lib_tree.item(i, "text") for i in v.lib_tree.get_children("")], leaf[:1]))
    v.lib_tree.selection_set(leaf[0])
    root.update()
    log.append("placing: %s" % (v.placing or {}).get("name"))
    # look at the start of the road from close by, then click the middle of the view
    import build as fe, gen_aid
    road = fe.map_road(m)
    si, _ = gen_aid.race_lines(road, m)
    x, z, a = road.at(road.dist[si] + 40, 50)
    v.target = [x, fe.GROUND_Y, z]
    v.dist, v.pitch = 120.0, 0.5
    v.refresh()
    root.after(500, step3)


def step3():
    w, h = v.gl.winfo_width(), v.gl.winfo_height()
    v._left_press(type("E", (), {"x": w // 2, "y": h // 2})())
    v._left_press(type("E", (), {"x": w // 2 + 120, "y": h // 2})())
    log.append("objects after 2 clicks: %s" % m.get("objects"))
    log.append("selected: %s" % v.sel)
    v._escape()                                  # stop placing
    v.select("obj:0")
    p = v._pick("obj:0")
    log.append("pick obj:0 label: %s" % (p or {}).get("label"))
    v._solid.set(True)
    v._set_solid(p)
    log.append("edits obj:0 after Solid: %s" % m["edits"].get("obj:0"))
    v.select("obj:0")
    root.after(600, step4)


def step4():
    shot("lib_2_placed.png")
    v.set_map(m, "collision")
    root.after(600, step5)


def step5():
    shot("lib_3_collision.png")
    v.set_map(m, "3d")
    v.select("obj:0")
    v._hide()                                    # X = delete the first one
    log.append("after delete obj:0: objects %s, edits obj keys %s" % (
        [o["id"] for o in m.get("objects") or []], [k for k in m["edits"] if k.startswith("obj")]))
    root.after(300, lambda: (open(os.path.join(OUT, "lib_gui.txt"), "w").write("\n".join(log)), root.destroy()))


root.after(300, step1)
root.mainloop()
print("\n".join(log))

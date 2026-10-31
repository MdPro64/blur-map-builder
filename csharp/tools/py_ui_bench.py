"""the Python designer's speed on the scratch copy (nothing is written to a game folder): start, 2D redraw, road made again, 3D scene"""
import json
import os
import statistics
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MB = os.path.join(os.path.dirname(ROOT), "map_builder")
os.environ.update(BMB_GAME=os.path.join(ROOT, "scratch", "game"), BMB_INSTALLED="1", FE_DRY="1")
sys.dont_write_bytecode = True
sys.path.insert(0, MB)
t0 = time.perf_counter()
import tkinter as tk  # noqa: E402
import road_designer as rd  # noqa: E402

root = tk.Tk()
d = rd.Designer(root)
root.update()
t_start = time.perf_counter() - t0
d.fit()
root.update()


def frames(n, fn):
    ts = []
    for i in range(n):
        t = time.perf_counter()
        fn(i)
        root.update()
        ts.append((time.perf_counter() - t) * 1000)
    return round(statistics.mean(ts), 1), round(max(ts), 1)


out = {"start_s": round(t_start, 2)}
print("started", out, flush=True)
out["2d_light_redraw_ms (mean, worst)"] = frames(30, lambda i: d.redraw(light=True))


def drag(i):
    d.points()[3]["x"] += 0.5
    d.redraw(light=True)


out["2d_drag_point_ms"] = frames(30, drag)
out["2d_full_redraw_ms"] = frames(10, lambda i: d.redraw())
print(out, flush=True)
d.view_mode.set("3d")
t = time.perf_counter()
d.on_view()
for _ in range(60):
    root.update()
    time.sleep(0.05)
out["3d_open_and_settle_s (3 s of waiting taken off)"] = round(time.perf_counter() - t - 3.0, 2)
v = d.v3d
for name in ("build_scene", "rebuild", "refresh"):
    if hasattr(v, name):
        out["3d_has"] = name
try:
    t = time.perf_counter()
    if hasattr(v, "build_scene"):
        v.build_scene(d.map) if v.build_scene.__code__.co_argcount > 1 else v.build_scene()
    out["3d_scene_ms"] = round((time.perf_counter() - t) * 1000, 1)
except Exception as e:  # noqa: BLE001
    out["3d_scene_error"] = repr(e)
def orbit(i):
    v.yaw += 0.01
    v.refresh()


out["3d_orbit_ms"] = frames(40, orbit)
t = time.perf_counter()
d.update_3d(now=True)
root.update()
out["3d_scene_after_edit_ms"] = round((time.perf_counter() - t) * 1000, 1)
print(json.dumps(out, indent=1))
os._exit(0)

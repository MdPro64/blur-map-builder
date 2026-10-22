"""GUIDE PICTURES, step 1 (step 2: annotate_guide.py). Usage: python release/guide/capture_guide.py <folder>
open the designer on a COPY of the maps, show every tab (+ a selected point), save screenshot + widget list"""
import ctypes, json, os, shutil, sys, time
ctypes.windll.shcore.SetProcessDpiAwareness(2)
MB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))      # map_builder/
OUT = sys.argv[1]
sys.path[:0] = [MB] + [os.path.join(MB, p) for p in ("flat_earth", "pak", "level", "kslf")]
import paths
tmp = os.path.join(OUT, "_tracks")
shutil.rmtree(tmp, ignore_errors=True)
shutil.copytree(os.path.join(MB, "tracks"), tmp)
for f in ("world.autosave.json",):
    if os.path.exists(os.path.join(tmp, f)):
        os.remove(os.path.join(tmp, f))
paths.TRACKS = tmp
paths.WORLD_FILE = os.path.join(tmp, "world.json")
paths.QUICKSTART_TARGET = os.path.join(OUT, "_target.lua")      # don't touch the game's
import runpy, tkinter as tk
from PIL import ImageGrab
sys.argv = [os.path.join(MB, "road_designer.py")]
os.chdir(MB)
rd = runpy.run_path(os.path.join(MB, "road_designer.py"), run_name="road_designer")
rd_paths = rd["paths"]
root = tk.Tk()
root.geometry("1600x1000+0+0")
d = rd["Designer"](root)
d.QUICKSTART = paths.QUICKSTART_TARGET
root.attributes("-topmost", True)

INTER = ("Button", "Radiobutton", "Checkbutton", "Menubutton", "Spinbox", "Scale", "Entry", "Listbox", "Text",
         "Canvas")

def widgets(top=None):
    top = top or root
    out = []
    def walk(w):
        for c in w.winfo_children():
            if not c.winfo_ismapped() or (isinstance(c, tk.Toplevel) and top is root):
                continue
            cls = c.winfo_class()
            if cls in INTER or cls == "Label":
                x, y = c.winfo_rootx() - top.winfo_rootx(), c.winfo_rooty() - top.winfo_rooty()
                wd, ht = c.winfo_width(), c.winfo_height()
                try:
                    txt = c.cget("text")
                except tk.TclError:
                    txt = ""
                out.append({"cls": cls, "text": str(txt), "box": [x, y, x + wd, y + ht], "path": str(c)})
            walk(c)
    walk(top)
    return out

def shot(name, settle=1.5):
    root.update()
    t = time.time()
    while time.time() - t < settle:
        root.update(); time.sleep(0.05)
    x, y = root.winfo_rootx(), root.winfo_rooty()
    img = ImageGrab.grab((x, y, x + root.winfo_width(), y + root.winfo_height()), all_screens=True)
    img.save(os.path.join(OUT, name + ".png"))
    json.dump(widgets(), open(os.path.join(OUT, name + ".json"), "w"), indent=1)
    print("saved", name, img.size, flush=True)

def shot_win(win, name, settle=1.0):
    """a window of its own (update / import windows) + its widget list"""
    t = time.time()
    while time.time() - t < settle:
        root.update(); time.sleep(0.05)
    win.lift()
    root.update()
    x, y = win.winfo_rootx(), win.winfo_rooty()
    img = ImageGrab.grab((x, y, x + win.winfo_width(), y + win.winfo_height()), all_screens=True)
    img.save(os.path.join(OUT, name + ".png"))
    json.dump(widgets(win), open(os.path.join(OUT, name + ".json"), "w"), indent=1)
    print("saved", name, img.size, flush=True)

# no internet in the pictures: the newest version = this one (green button); a made-up next version for the window
import updater, update_window
updater.SOURCE_FILE = os.path.join(OUT, "_update_source.json")
NEXT = "v1.0.%d" % (int(paths.VERSION.split(".")[-1]) + 1)
NEXT_INFO = (NEXT, {n: "x" for n in updater.ASSETS}, "## What's new\n* New: something new in the map builder\n"
             "* Fix: something that was wrong\n\nYour maps are kept.")
updater.latest = lambda source=None: ("v" + paths.VERSION, {}, "")
d.check_updates()


def extra_windows():
    """the update window (asking + downloading), the update source window, the .glb import window"""
    import threading
    hold = threading.Event()

    def fake_install(info, progress):
        for got in range(0, 21_000_000, 700_000):
            progress("Blur Map Builder.exe", got, 46_800_000)
            time.sleep(0.03)
        hold.wait()
        return "stopped"
    uw = update_window.UpdateWindow(root, NEXT_INFO, install=fake_install, restart=lambda: None)
    shot_win(uw.win, "8_update")
    uw.start()
    shot_win(uw.win, "8b_update_download", 2.0)
    hold.set()
    root.update(); time.sleep(0.3); root.update()
    uw.busy = False
    uw.win.destroy()
    sw = update_window.source_window(root)
    shot_win(sw, "9_update_source")
    sw.destroy()
    # import the sample crate (into the COPY of the maps) through the real window
    from tkinter import filedialog
    import model_import
    sample = os.path.join(MB, "samples", "MDPRO_test_crate.glb")
    filedialog.askopenfilename = lambda **k: sample
    tab("3d")
    v = d.v3d

    def grab_import():
        tops = [t for t in root.winfo_children() if isinstance(t, tk.Toplevel) and t.winfo_ismapped()]
        win = tops[-1]
        shot_win(win, "10_import")

        def find(widget):
            for c in widget.winfo_children():
                if isinstance(c, tk.Button) and "Import + place" in str(c.cget("text")):
                    return c
                r_ = find(c)
                if r_:
                    return r_
        find(win).invoke()
    root.after(1500, grab_import)
    v._import_model()
    v._stop_placing()


def tab(v):
    d.view_mode.set(v); d.on_view()

def run():
    tab("2d"); d.fit(); shot("1_2d_map", 2.5)
    d.sel = 2; d.build_props(); d.redraw()
    shot("2_2d_point", 1.5)
    d.deselect()
    d.sel_pick = 0; d.build_props(); d.redraw(); shot("2b_2d_powerup", 1.5)
    d.deselect()
    tab("3d"); shot("3_3d", 6)
    tab("collision"); shot("4_collision", 5)
    tab("ai"); shot("5_ai", 3)
    tab("light"); shot("6_lighting", 2)
    tab("surface"); shot("7_surface", 2)
    tab("2d")
    extra_windows()
    tab("2d")
    root.destroy()

root.after(1500, run)
root.mainloop()

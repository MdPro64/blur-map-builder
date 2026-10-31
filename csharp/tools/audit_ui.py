"""Every text the Python window shows (buttons, labels, messages) that the C# source does not hold. A check for forgotten tools.
usage: python audit_ui.py        (only reads)"""
import glob
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
PY = os.path.join(HERE, "..", "..", "map_builder")
FILES = ["road_designer.py", "designer_net.py", "designer_surface.py", "designer_share.py", "view3d.py", "view3d_scatter.py", "car/car_tab.py", "bmb_main.py", "map_tester.py"]
cs = ""
for f in glob.glob(os.path.join(HERE, "..", "src", "*", "*.cs")):
    cs += open(f, encoding="utf8").read()
norm = lambda s: re.sub(r"[^a-z0-9]+", " ", s.lower()).strip()
csn = norm(cs)
SKIP = ("segoe", "consolas", "#", "<", "gl_", "__", "utf", ".py", "\\", "/", "%s %s", "win", "hand", "sb_")
for name in FILES:
    src = open(os.path.join(PY, name), encoding="utf8").read()
    miss = []
    for m in re.finditer(r'(?:text=|say\(|head\(|config\(text=|title\(|Button\(\w+, text=|askstring\(|showinfo\(|showerror\(|showwarning\(|askyesno\(|row\()\s*(?:%s)?\s*"((?:[^"\\]|\\.){4,})"', src):
        t = m.group(1)
        if any(t.lower().startswith(s) for s in SKIP):
            continue
        parts = [p for p in re.split(r"%[-+.0-9]*[a-z]|\\n|\\u[0-9A-Fa-f]{4}", t) if len(norm(p)) >= 4]
        if parts and not all(norm(p) in csn for p in parts):
            miss.append(t[:90])
    print("%-28s %d missing" % (name, len(miss)))
    for t in dict.fromkeys(miss):
        print("     ", t)

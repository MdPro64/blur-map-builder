"""Compare the C# format core with the Python one on the real game files (read only).
  python tools\\compare_core.py          (run from map_builder_csharp, needs bin built)
Everything that differs is printed; the last line says how many checks passed. Outputs go to scratch\\core."""
import hashlib
import io
import json
import os
import subprocess
import sys
import time
import contextlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MB = os.path.join(os.path.dirname(ROOT), "map_builder")
GAME = r"J:\blur - user 1"
BASE = os.path.join(GAME, "Blur Map Builder", "base")
os.environ["BMB_GAME"] = os.path.join(ROOT, "scratch", "no_game")   # nothing of paths.py may point at the game
sys.path[:0] = [MB] + [os.path.join(MB, p) for p in ("pak", "kslf", "aid", "physics", "tex", "model")]
import pak          # noqa: E402
import dump         # noqa: E402
import dxt          # noqa: E402
import cpmodel      # noqa: E402
import write_flat   # noqa: E402
import levelphysics as lp   # noqa: E402

cpmodel.ADDON = os.path.join(BASE, "addon")
cpmodel.limit_memory(6000)
OUT = os.path.join(ROOT, "scratch", "core")
os.makedirs(OUT, exist_ok=True)
CLI = os.path.join(ROOT, "src", "Bmb.Cli", "bin", "Release", "net10.0", "bmbcli.exe")
ok = bad = 0
T = {}


def check(name, good, detail=""):
    global ok, bad
    print(("OK  " if good else "BAD ") + name, detail)
    ok += bool(good)
    bad += not good


def cli(*a):
    t = time.perf_counter()
    r = subprocess.run([CLI] + list(a), capture_output=True, text=True)
    T[a[0] + " " + os.path.basename(a[1]) if len(a) > 1 else a[0]] = time.perf_counter() - t
    if r.returncode:
        print(r.stderr[-2000:])
    return r.stdout


def sha(b):
    return hashlib.sha1(b).hexdigest()


# 1. packs
for path in (os.path.join(BASE, "gamedata.pak.original"), os.path.join(BASE, "root.pak.original")):
    o = os.path.join(OUT, os.path.basename(path) + ".txt")
    cli("pak-list", path, o)
    t = time.perf_counter()
    p = pak.Pak(path)
    want = {}
    for e in p.files:
        if e["offset"] != pak.NO_DATA:
            want[e["name"]] = "%d\t%08X\t%s" % (e["size"], e["crc"], sha(p.read(e)))
    T["python pak " + os.path.basename(path)] = time.perf_counter() - t
    got = dict(ln.rstrip("\n").split("\t", 1) for ln in open(o, encoding="latin1"))
    diff = [k for k in want if got.get(k) != want[k]]
    check("pak " + os.path.basename(path), not diff and len(got) == len(want), "%d files, %d differ %s" % (len(want), len(diff), diff[:3]))

# 2. KSLF dump of every .bin in the unpacked set
unp = os.path.join(BASE, "unpacked")
n = 0
for dirp, _, files in os.walk(unp):
    for f in files:
        if not (f.endswith(".bin") or f.endswith(".xt")):
            continue
        path = os.path.join(dirp, f)
        if open(path, "rb").read(4) != b"KSLF":
            continue
        t = time.perf_counter()
        F = dump.File(path)
        want = [{"type": o["blocks"][0]["name"], "value": F.top(o)} for o in F.O]
        T["python dump " + f] = time.perf_counter() - t
        o = os.path.join(OUT, f + ".json")
        cli("kslf-dump", path, o)
        got = json.load(open(o))
        same = json.loads(json.dumps(want, default=str)) == got
        check("kslf " + f, same, "%d objects" % len(want))
        n += 1

# 3. every model's layout against the add-on reader (a sample: the reader is slow)
o = os.path.join(OUT, "models.txt")
cli("model-parse", os.path.join(BASE, "root.pak.original"), o)
got = dict(ln.rstrip("\n").split("\t", 1) for ln in open(o, encoding="latin1"))
names = sorted(got)[::12]
sdiff = fail_same = compared = 0
t = time.perf_counter()
todo, res = list(names), {}
import queue, threading  # noqa: E402
worker = os.path.join(ROOT, "tools", "model_worker_py.py")
while todo:
    w = subprocess.Popen([sys.executable, worker] + todo[:40], stdout=subprocess.PIPE, text=True)
    q = queue.Queue()
    threading.Thread(target=lambda: [q.put(ln) for ln in w.stdout] and None or q.put(None), daemon=True).start()
    cur = None
    while True:
        try:
            ln = q.get(timeout=8)
        except queue.Empty:
            w.kill()
            res[cur] = None           # hung: stays unread, as the Python program treats it
            break
        if ln is None:
            break
        f = ln.rstrip(chr(10)).split(chr(9))
        if f[0] == "S":
            cur = f[1]
        else:
            res[f[1]] = json.loads(f[2]) if f[0] == "R" else None
    todo = [n for n in todo if n not in res]
for name in names:
    w = res.get(name)
    if w is None:
        fail_same += got[name].startswith("ERR")
        if not got[name].startswith("ERR"):
            print("   python fails, C# reads:", name)
        continue
    if got[name].startswith("ERR"):
        print("   C# fails:", name, got[name])
        sdiff += 1
        continue
    compared += 1
    g = json.loads(got[name])
    if any(g[k] != w[k] for k in w if k != "bbox") or any(abs(a - b) > 1e-4 for a, b in zip(g["bbox"], w["bbox"])):
        sdiff += 1
        print("   differs:", name)
T["python model parse (%d)" % len(names)] = time.perf_counter() - t
check("model layouts", sdiff == 0, "%d compared, %d differ, %d fail in both" % (compared, sdiff, fail_same))

# 4. small things
wf = os.path.join(GAME, "Blur Map Builder", "maps", "world.json")
o = os.path.join(OUT, "world_rt.json")
cli("json-roundtrip", wf, o)
check("world.json byte round trip", json.dumps(json.load(open(wf)), indent=1) == open(o).read())

import random  # noqa: E402
r = random.Random(1234)
want = [repr(r.random()) for _ in range(10)] + [str(r.randint(-40, 1000)) for _ in range(10)]
check("Python random", cli("random", "1234").split() == want)
for w, h, s in ((64, 64, 7), (130, 66, 9)):
    px = cpmodel.noise(w, h, 100, 60, s)
    enc = dxt.dxt1_encode(px, w, h)
    check("dxt %dx%d" % (w, h), cli("dxt", str(w), str(h), str(s)).split() ==
          [sha(enc), sha(dxt.dxt1_decode(enc, w, h)), sha(cpmodel.encode_mips(px, w, h, 3))])

lv = os.path.join(GAME, "levels", "showroom", "Level.levelphysics")
m = lp.read_mesh(lv)
head = "%d %d %d %d %d %d" % (m["version"], len(m["verts"]), len(m["nodes"]), len(m["tris_raw"]), len(m["groups"]), m["stream_pos"])
want = write_flat.build_flat(lv, 1.0, -500, 500, -400, 400, [(0, 10, 1, 4, 0, 20), (30, 44, 1, 4, 30, 33)],
                             [write_flat.turned_box(5, 6, 1, 3, 4, 2, 0.7),
                              write_flat.wall_strip([(0.0, 0.0), (5.0, 1.0), (9.0, 4.0)], 1.0, 3.5, [(0.0, 1.0), (0.2, 0.98), (0.5, 0.86)])],
                             [write_flat.deck_strip([(0, 2, 0), (5, 3, 0)], [(0, 2, 4), (5, 3, 4)])],
                             [(write_flat.deck_strip([(8, 2, 0), (9, 3, 0)], [(8, 2, 4), (9, 3, 4)]), 4)])
g = cli("phys", lv).split("\n")
check("physics read", g[0] == head, g[0])
check("physics write", g[1].split()[0] == sha(want), g[1])

print("\nseconds:")
for k, v in T.items():
    print("  %-46s %8.2f" % (k, v))
print("\n%d passed, %d failed" % (ok, bad))

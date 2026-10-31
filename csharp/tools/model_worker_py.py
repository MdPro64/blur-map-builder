"""one worker of compare_core.py: reads models with the add-on reader, memory capped, one JSON line per model"""
import json, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MB = os.path.join(os.path.dirname(ROOT), "map_builder")
BASE = r"J:\blur - user 1\Blur Map Builder\base"
os.environ["BMB_GAME"] = os.path.join(ROOT, "scratch", "no_game")
sys.path[:0] = [MB] + [os.path.join(MB, p) for p in ("pak", "model", "tex")]
import pak, cpmodel
cpmodel.ADDON = os.path.join(BASE, "addon")
cpmodel.limit_memory(1024)
p = pak.Pak(os.path.join(BASE, "root.pak.original"))
for name in sys.argv[1:]:
    print("S\t" + name, flush=True)
    try:
        P = cpmodel.parse(p.read(p.find(name)))
        w = {k: P[k] for k in ("vstart", "vcount", "stride", "fstart", "fcount")}
        w.update(elements=[list(x) for x in P["elements"]], meshes=[list(x) for x in P["meshes"]], bbox=list(P["bbox"]))
        print("R\t" + name + "\t" + json.dumps(w), flush=True)
    except BaseException as ex:
        print("E\t" + name + "\t" + type(ex).__name__, flush=True)

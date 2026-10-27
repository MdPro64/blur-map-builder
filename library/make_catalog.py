"""
Make the object library (library/catalog.json) for the road designer's side bar: every object of the game that can
be placed on a Flat Earth map, sorted into groups.

Sources: the game's own database (gamedata/xt/amax.bin, unpacked) and the model files (root.pak):
  * ElementObjectRecord  -> "breakable": smash / knock over (lamp posts, bins, fences...); placed as a
                            PhysicalObjectActor in the lamp list (356 per location)
  * DynamicObjDef        -> "animated" (has AnimationEvents) or "movable" (PhysType 2 = cars push it around);
                            placed as a DynamicObjActor in the tile list (144 per location)
  * GameModel            -> "scenery": the model alone, no physics (the build makes it a DynamicObjDef with
                            PhysType None); the designer's "Solid" box adds a wall box in the collision floor instead
Each entry: {"id", "kind", "name", "loc", "cat", "model", "uid" (hex), "lo", "hi" (model box, x / y up / z),
"sized" (False = the size could not be read: 2 m box)}.

usage: python make_catalog.py        (about 5 minutes; reads every model once)
"""
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.join(HERE, "..")
sys.path[:0] = [os.path.join(MB, p) for p in ("kslf", "pak")] + [MB]
import dump  # noqa: E402
import pak   # noqa: E402

import paths  # noqa: E402
paths_mod = paths   # (model_sizes has a local called paths)
AMAX = os.path.join(paths.UNPACKED, "xt", "amax.bin")
ROOT = paths.MODEL_PAK
CACHE = paths.MODEL_CACHE
OUT = paths.CATALOG

KINDS = ("breakable", "movable", "animated", "scenery")
LOCATIONS = {
    "shared": "Shared (all cities)", "usa_la_river": "LA River", "usa_la_downtown": "LA Downtown",
    "usa_la_longbeachdocks": "LA Long Beach docks", "usa_la_observatory": "LA Observatory", "usa_amboy": "Amboy",
    "usa_ny_dumbo": "New York Dumbo", "usa_sanfran_russianhill": "San Francisco Russian Hill",
    "usa_sanfran_sausalito": "San Francisco Sausalito", "uk_brighton": "Brighton", "uk_london_hackney": "London Hackney",
    "spa_barcelona_gracia": "Barcelona Gracia", "spa_barcelona_oval": "Barcelona Oval", "jpn_shutoko": "Tokyo Shutoko",
    "jpn_mountaintrack": "Mount Haruna",
}
DEFAULT_BOX = ([-1.0, 0.0, -1.0], [1.0, 2.0, 1.0])


def pretty(s):
    s = s.replace("_", " ").strip()
    out = ""
    for i, ch in enumerate(s):                     # CamelCase -> Camel Case
        if i and ch.isupper() and s[i - 1].islower():
            out += " "
        out += ch
    return out[:1].upper() + out[1:]


def place_of(model):
    """(location, category) from models\\objects\\<location>\\<category>\\...\\name.model"""
    p = model.replace("/", "\\").split("\\")
    low = [x.lower() for x in p]
    if len(p) < 4 or low[0] != "models" or low[1] != "objects":
        return None
    loc = LOCATIONS.get(low[2], pretty(p[2]))
    cat = pretty(p[3]) if len(p) > 4 else "Misc"
    if low[2] == "shared" and len(p) > 4:          # shared\US\x.model: the sub folder is a region, not a category
        cat = {"us": "USA", "uk": "UK", "jpn": "Japan", "spa": "Spain"}.get(low[3], pretty(p[3]))
    return loc, cat


BATCH, WORKERS, START_SECONDS = 100, 4, 30
log_fn = print


def model_sizes(files):
    """{model file (lower case): (lo, hi)} - extracted to CACHE, read in batches; a model that hangs the reader is
    skipped (default box)"""
    os.makedirs(CACHE, exist_ok=True)
    R = pak.Pak(ROOT)
    paths = {}
    for fn in files:
        e = R.find("\\" + fn.lower())
        if e is None:
            continue
        p = os.path.join(CACHE, fn.lower().replace("\\", "__").replace("/", "__"))
        if not os.path.exists(p):
            open(p, "wb").write(R.read(e))
        paths[p] = fn.lower()
    R.close()                                  # (the workers read the extracted files: root.pak not needed any more)
    sizes, todo = {}, list(paths)
    worker = os.path.join(HERE, "model_worker.py")
    import threading

    def run_batch(batch):
        """(results {file: line json}, files not read) - a worker that stops answering is killed: the first unread
        model hung the reader"""
        proc = subprocess.Popen(paths_mod.python_cmd(worker, *batch), stdout=subprocess.PIPE, text=True,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        lines, t0 = [], time.time()

        def read(proc=proc):
            for line in proc.stdout:
                lines.append(line)
        th = threading.Thread(target=read, daemon=True)
        th.start()
        seen, last = 0, t0
        while proc.poll() is None:              # quiet too long: START_SECONDS before the first model (the exe
            time.sleep(0.1)                     # unpacks itself first: ~5 s), then 3 s per model
            if len(lines) != seen:
                seen, last = len(lines), time.time()
            if time.time() - last > (START_SECONDS if not seen else 3.0):
                proc.kill()
                break
        th.join(2)
        got = {}
        for line in lines:
            j = json.loads(line)
            got[j["file"]] = j
        return got, [f for f in batch if f not in got]

    total = len(todo)
    lock = threading.Lock()

    def worker_loop():
        while True:
            with lock:
                if not todo:
                    return
                batch = todo[:BATCH]
                del todo[:BATCH]
            got, left = run_batch(batch)
            with lock:
                for f, j in got.items():
                    if "lo" in j and all(abs(v) < 1e5 for v in j["lo"] + j["hi"]):
                        sizes[paths[f]] = (j["lo"], j["hi"])
                if left:                         # the first unread one hung: skip it, the rest again
                    log_fn("  skipped (reader hangs): %s" % paths[left[0]])
                    todo[:0] = left[1:]
                done[0] += len(got)
                log_fn("  models read: %d / %d" % (min(done[0], total), total))
    done = [0]
    threads = [threading.Thread(target=worker_loop) for _ in range(WORKERS)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    return sizes


def main(log=print):
    global log_fn
    log_fn = log
    A = dump.File(AMAX)
    uid_of = {oi: "%08X" % u for u, t, oi in A.refs if oi != 0xFFFF}
    models, defs, recs = {}, [], []
    for i, o in enumerate(A.O):
        tn = o["blocks"][0]["name"]
        if tn == "GameModel":
            models[uid_of[i]] = A.top(o)["Filename"]
        elif tn == "DynamicObjDef":
            defs.append((uid_of[i], A.top(o)))
        elif tn == "ElementObjectRecord":
            recs.append((uid_of[i], A.top(o)))
    files = sorted({f for f in models.values() if place_of(f)})
    log("%d models, %d object defs, %d breakable records; reading model sizes..." % (len(files), len(defs), len(recs)))
    sizes = model_sizes(files)
    out = []

    def add(kind, uid, model, name, extra=None):
        where = place_of(model or "")
        if where is None:
            return
        lo, hi = sizes.get(model.lower(), DEFAULT_BOX)
        e = {"id": "%s:%s" % (kind, uid), "kind": kind, "name": name, "loc": where[0], "cat": where[1],
             "model": model, "uid": uid, "lo": [round(v, 3) for v in lo], "hi": [round(v, 3) for v in hi],
             "sized": model.lower() in sizes}
        e.update(extra or {})
        out.append(e)
    for uid, top in recs:
        m = top.get("Model") or {}
        model = models.get(m.get("uid"))
        objs = top.get("objects") or []
        name = (objs[0].get("Name") if objs else None) or os.path.splitext(os.path.basename(model or "?"))[0]
        add("breakable", uid, model, pretty(name[2:] if name[:2] in ("O_", "M_", "S_") else name))
    for uid, top in defs:
        model = models.get((top.get("Model") or {}).get("uid"))
        if not model:
            continue
        anim = bool(top.get("AnimationEvents"))
        if not anim and top.get("PhysType") != 2:
            continue                                    # fixed / none: the scenery entry of its model covers it
        add("animated" if anim else "movable", uid, model, pretty(os.path.splitext(os.path.basename(model))[0]),
            {"weight": top.get("Weight")})
    for uid, model in models.items():
        add("scenery", uid, model, pretty(os.path.splitext(os.path.basename(model))[0]))
    # one entry per (kind, model): several records may show the same model
    seen, uniq = set(), []
    for e in sorted(out, key=lambda e: (KINDS.index(e["kind"]), e["loc"], e["cat"], e["name"].lower(), e["uid"])):
        k = (e["kind"], e["model"].lower())
        if k not in seen:
            seen.add(k)
            uniq.append(e)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump({"made": time.strftime("%Y-%m-%d %H:%M"), "objects": uniq}, open(OUT, "w"), indent=0)
    for kind in KINDS:
        n = [e for e in uniq if e["kind"] == kind]
        print("%-10s %4d objects (%d with a measured size)" % (kind, len(n), sum(e["sized"] for e in n)))
    print("written", os.path.normpath(OUT))


if __name__ == "__main__":
    main()

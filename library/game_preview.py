"""
REAL SHAPES of the game's objects in the 3D tab (the "Real shapes" tick; off = the fast boxes). The models come from the
player's own game: Blur Map Builder\\base\\model_cache (made by the object library at the first start) or root.pak.

Reading a model uses the BlurImportExport reader (slow, and it hangs on a few models) - so a WORKER process does it
(python game_preview.py OUT_DIR MODEL...; one json line per model) and writes a small preview into
<model_cache>\\preview\\: <name>.npz (triangles: position + uv, per texture) + <name>_<k>.png (the texture, <= 256 px,
read from the texture's own smaller mip level). The 3D tab reads those (fast) and asks the worker for the missing
ones in the background; a model that fails or hangs is noted (failed.json) and stays a box.

Model layout (checked on 25 models): all elements at 0 (no transforms); "O_x" = the whole object, "S_x" = its
pieces - the "Damaged" / "Debris" pieces are left out (they overlap the whole one); a model with several _diff
textures: its meshes take them in turn (the container porter: 2 detail levels x [cab, cab alphas, trailer]).
"""
import contextlib
import io
import json
import os
import struct
import subprocess
import sys
import threading
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [os.path.join(HERE, ".."), os.path.join(HERE, "..", "model"), os.path.join(HERE, "..", "tex"),
                os.path.join(HERE, "..", "pak")]
import paths  # noqa: E402

TEX = 256                          # preview texture size (the largest mip level not above it)
PICTURE_SECONDS = 5.0              # a hover picture that takes longer: no picture (the worker; in the map builder 1.5)
START_SECONDS, MODEL_SECONDS = 15.0, 8.0   # a worker silent this long (start / per model) is stopped: model skipped
DXT1, DXT5 = 0x31545844, 0x35545844
SKIP_PARTS = ("dam", "debris", "broken")


def folder():
    return os.path.join(paths.MODEL_CACHE, "preview")


def thumb_path(model):
    """the object library's hover picture of a game model (thumb.py)"""
    return os.path.join(folder(), "thumbs", key_of(model) + ".png")


def key_of(model):
    return model.lower().lstrip("\\").replace("\\", "__").replace("/", "__")


# ---------------------------------------------------------------------------------------------- worker side
def _model_bytes(model):
    p = os.path.join(paths.MODEL_CACHE, key_of(model))
    if os.path.exists(p):
        return open(p, "rb").read()
    import pak
    R = pak.Pak(paths.MODEL_PAK)
    e = R.find("\\" + model.lower().lstrip("\\"))
    if e is None:
        raise FileNotFoundError(model)
    return R.read(e)


def _texture(t):
    """RGB picture (PIL) of a texture's largest mip level <= TEX"""
    from PIL import Image
    import dxt
    w, h, mips, fmt, data = t["width"], t["height"], max(1, t["mipmaps"]), t["dxt"], t["data"]
    block = 16 if fmt == DXT5 else 8
    if fmt not in (DXT1, DXT5):
        raise ValueError("texture format %08X" % fmt)
    off = 0
    for _ in range(mips - 1):
        if max(w, h) <= TEX:
            break
        off += max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * block
        w, h = max(1, w // 2), max(1, h // 2)
    n = max(1, (w + 3) // 4) * max(1, (h + 3) // 4)
    raw = data[off:off + n * block]
    if fmt == DXT5:                                   # the colour half of each block is a DXT1 block
        raw = b"".join(raw[i + 8:i + 16] for i in range(0, len(raw), 16))
    bw, bh = max(4, w), max(4, h)
    px = dxt.dxt1_decode(raw, bw, bh)
    return Image.frombytes("RGB", (bw, bh), px).crop((0, 0, w, h))


def extract(model, out):
    """write <out>\\<key>.npz + pngs for one game model; returns the number of triangles"""
    import cpmodel
    data = _model_bytes(model)
    import tempfile
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".model")
    f.write(data)
    f.close()
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            M = cpmodel._reader().read_cpmodel_data(None, f.name)
    finally:
        os.unlink(f.name)
    diffs = [i for i, t in enumerate(M["textures"]) if t["name"].lower().endswith("_diff")] or \
        [i for i, t in enumerate(M["textures"]) if t["name"] != "DefaultColour"][:1]
    names = [e["name"] if isinstance(e, dict) else "" for e in M["elements"]]
    # vertex streams by their element types (a mesh names its definition)
    def types(defn):
        return tuple(d["type"] for d in defn if d.get("prefix", 0) == 0)
    streams = {types(vs["definition"]): vs for vs in M["vertex_streams"]}
    groups = {}                                       # texture slot -> [triangles (n, 3, 5)]
    shown = 0
    for mi, mesh in enumerate(M["meshes"]):
        el = names[mesh["object_index"]].lower() if mesh["object_index"] < len(names) else ""
        if any(s in el for s in SKIP_PARTS):
            continue
        vs = streams.get(types(M["vert_definitions"][mesh["definition"]]))
        if vs is None:
            continue
        fs = M["face_streams"][mesh["face_stream_index"]]
        stride, off_pos, off_uv, uv_type = vs["bytes"], None, None, None
        o = 0
        for d in vs["definition"]:
            if d["type"] == 6 and off_pos is None:
                off_pos = o
            elif d["type"] in (8, 9) and off_uv is None:
                off_uv, uv_type = o, d["type"]
            o += {6: 12, 0xB: 4, 8: 4, 9: 8, 0xA: 4}.get(d["type"], 4)
        if off_pos is None:
            continue
        d1, d2 = mesh["data1"][0], mesh["data2"][0]
        base = d2["vOffset"] // stride                 # absolute vertex = base + index in the face stream
        idx = np.frombuffer(data, np.uint16, fs["count"], fs["start"])
        faces = idx[d1["face_offset"]:d1["face_offset"] + d1["face_count"]].astype(np.int64)
        if mesh["face_type"] == 1:                     # strip -> triangles (degenerate ones dropped)
            if len(faces) < 3:
                continue
            tri = np.stack([faces[:-2], faces[1:-1], faces[2:]], axis=1)
        else:
            tri = faces[:len(faces) // 3 * 3].reshape(-1, 3)
        tri = tri[(tri[:, 0] != tri[:, 1]) & (tri[:, 1] != tri[:, 2]) & (tri[:, 0] != tri[:, 2])]
        if not len(tri):
            continue
        v = (tri + base).reshape(-1)
        if v.max() >= vs["count"]:
            continue
        rows = np.frombuffer(data, np.uint8, vs["count"] * stride, vs["start"]).reshape(vs["count"], stride)
        pos = rows[v, off_pos:off_pos + 12].copy().view(np.float32).reshape(-1, 3)
        if off_uv is not None:
            uv = rows[v, off_uv:off_uv + 4].copy().view(np.float16).reshape(-1, 2).astype(np.float32)
        else:
            uv = np.zeros((len(v), 2), np.float32)
        slot = mi % len(diffs) if diffs else -1
        groups.setdefault(slot, []).append(np.c_[pos, uv].reshape(-1, 3, 5))
        shown += len(tri)
    if not groups:
        raise ValueError("no triangles")
    k = key_of(model)
    arrays, meta = {}, []
    for gi, (slot, parts) in enumerate(sorted(groups.items())):
        a = np.concatenate(parts).astype(np.float32)
        arrays["g%d" % gi] = a
        png = None
        if slot >= 0:
            try:
                png = "%s_%d.png" % (k, gi)
                _texture(M["textures"][diffs[slot]]).save(os.path.join(out, png))
            except Exception:                         # noqa: BLE001 - grey then
                png = None
        meta.append(png)
    arrays["meta"] = np.array(json.dumps(meta))
    tmp = os.path.join(out, k + ".tmp.npz")
    np.savez_compressed(tmp, **arrays)
    os.replace(tmp, os.path.join(out, k + ".npz"))
    return shown


def make_thumb(model, seconds=PICTURE_SECONDS):
    """the hover picture from the model's preview (made first if missing); too slow = no picture (ValueError)"""
    import thumb
    p = os.path.join(folder(), key_of(model) + ".npz")
    if not os.path.exists(p):
        extract(model, folder())
    mats = Loader._load(None, p)
    img = thumb.render(mats or [], seconds=seconds)
    if img is None:
        raise ValueError("no picture (no triangles, or slower than %g s)" % seconds)
    os.makedirs(os.path.dirname(thumb_path(model)), exist_ok=True)
    img.save(thumb_path(model) + ".tmp.png")
    os.replace(thumb_path(model) + ".tmp.png", thumb_path(model))


def worker_main(argv):
    import cpmodel
    cpmodel.limit_memory()                            # a runaway model: MemoryError (reported), not gigabytes
    thumbs = argv[:1] == ["--thumbs"]                 # also the hover pictures (Bake 3D pictures)
    argv = argv[1:] if thumbs else argv
    out = argv[0]
    os.makedirs(out, exist_ok=True)
    for model in argv[1:]:
        try:
            if thumbs:
                make_thumb(model)
                n = 0
            else:
                n = extract(model, out)
            print(json.dumps({"model": model, "tris": n}), flush=True)
        except Exception as ex:                       # noqa: BLE001 - reported: stays a box
            import traceback
            tb = traceback.extract_tb(ex.__traceback__)[-1]
            print(json.dumps({"model": model, "error": "%s at %s:%d" % (repr(ex)[:120], os.path.basename(tb.filename),
                                                                         tb.lineno)}), flush=True)


# ---------------------------------------------------------------------------------------------- the 3D tab's side
class Loader:
    """previews for the 3D tab: get(model) = [{key, image, pos, uv, glow}] or None (then it is read in the
    background; changed() tells the tab to draw again)"""

    def __init__(self):
        self.mem = {}
        self.queue = []
        self.busy = False
        self.new = False
        self.lock = threading.Lock()
        try:
            self.failed = set(json.load(open(os.path.join(folder(), "failed.json"))))
        except (OSError, ValueError):
            self.failed = set()

    def get(self, model):
        if not model:
            return None
        k = key_of(model)
        if k in self.mem:
            return self.mem[k]
        p = os.path.join(folder(), k + ".npz")
        if os.path.exists(p):
            self.mem[k] = Loader._load(None, p)
            return self.mem[k]
        if k not in self.failed:
            with self.lock:
                if model not in self.queue:
                    self.queue.append(model)
            self._start()
        return None

    @staticmethod
    def _load(_, p):                                  # (a static method: also used by the worker)
        from PIL import Image
        try:
            z = np.load(p)
            meta = json.loads(str(z["meta"]))
            out = []
            for gi, png in enumerate(meta):
                a = z["g%d" % gi].reshape(-1, 5)
                img = None
                if png and os.path.exists(os.path.join(folder(), png)):
                    img = Image.open(os.path.join(folder(), png)).convert("RGB")
                    w, h = (1 << max(0, (n - 1).bit_length()) for n in img.size)
                    if (w, h) != img.size:
                        img = img.resize((w, h))
                if img is None:
                    img = Image.new("RGB", (4, 4), (150, 155, 165))
                out.append({"key": "%s|%d|%s" % (p, gi, os.path.getmtime(p)), "image": img,
                            "pos": np.ascontiguousarray(a[:, :3]), "uv": np.ascontiguousarray(a[:, 3:5]),
                            "nrm": None, "glow": 0.0})
            return out
        except Exception:                             # noqa: BLE001 - a broken preview: read it again next time
            return None

    def _start(self):
        with self.lock:
            if self.busy or not self.queue:
                return
            self.busy = True
        threading.Thread(target=self._run, daemon=True).start()

    def _run(self):
        """read the queued models in a worker (a few per run); one that hangs the reader is killed + noted"""
        try:
            while True:
                with self.lock:
                    batch = self.queue[:8]
                    del self.queue[:8]
                if not batch:
                    return
                os.makedirs(folder(), exist_ok=True)
                proc = subprocess.Popen(paths.python_cmd(os.path.abspath(__file__), folder(), *batch),
                                        stdout=subprocess.PIPE, text=True,
                                        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
                lines = []
                th = threading.Thread(target=lambda: lines.extend(proc.stdout), daemon=True)
                th.start()
                seen, last = 0, time.time()
                while proc.poll() is None:
                    time.sleep(0.1)
                    if len(lines) != seen:
                        seen, last = len(lines), time.time()
                        self.new = True
                    if time.time() - last > (START_SECONDS if not seen else MODEL_SECONDS):
                        proc.kill()
                        break
                th.join(2)
                done = set()
                for line in lines:
                    try:
                        j = json.loads(line)
                    except ValueError:
                        continue
                    done.add(j["model"])
                    if "error" in j:
                        self.failed.add(key_of(j["model"]))
                left = [m for m in batch if m not in done]
                if left:                              # the first unread one hung the reader
                    self.failed.add(key_of(left[0]))
                    with self.lock:
                        self.queue[:0] = left[1:]
                try:
                    json.dump(sorted(self.failed), open(os.path.join(folder(), "failed.json"), "w"))
                except OSError:
                    pass
                self.new = True
        finally:
            with self.lock:
                self.busy = False

    def note_failed(self, model):
        """a model that can't be read (stays a box, no hover picture) - kept in failed.json"""
        with self.lock:
            self.failed.add(key_of(model))
            try:
                json.dump(sorted(self.failed), open(os.path.join(folder(), "failed.json"), "w"))
            except OSError:
                pass

    def changed(self):
        """True once after new previews arrived"""
        if self.new:
            self.new = False
            return True
        return False

    def waiting(self):
        return self.busy or bool(self.queue)


class Baker:
    """Bake 3D pictures: the hover picture of every object of the library, made by WORKERS workers in the
    background (a model that hangs the reader is skipped and noted, as in the Loader)"""
    WORKERS, BATCH = 4, 25

    def __init__(self, models):
        known_bad = loader().failed
        self.todo = [m for m in dict.fromkeys(models) if m and not os.path.exists(thumb_path(m))
                     and key_of(m) not in known_bad]
        self.total = len(self.todo)
        self.done = 0
        self.failed = 0
        self.stop = False
        self.lock = threading.Lock()
        self.threads = [threading.Thread(target=self._run, daemon=True) for _ in range(self.WORKERS)]
        for t in self.threads:
            t.start()

    def running(self):
        return any(t.is_alive() for t in self.threads)

    def _run(self):
        while not self.stop:
            with self.lock:
                batch = self.todo[:self.BATCH]
                del self.todo[:self.BATCH]
            if not batch:
                return
            os.makedirs(folder(), exist_ok=True)
            proc = subprocess.Popen(paths.python_cmd(os.path.abspath(__file__), "--thumbs", folder(), *batch),
                                    stdout=subprocess.PIPE, text=True,
                                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            lines = []
            th = threading.Thread(target=lambda: lines.extend(proc.stdout), daemon=True)
            th.start()
            seen, last = 0, time.time()
            while proc.poll() is None:
                time.sleep(0.1)
                if len(lines) != seen:
                    with self.lock:
                        self.done += len(lines) - seen
                    seen, last = len(lines), time.time()
                if self.stop or time.time() - last > (START_SECONDS if not seen else MODEL_SECONDS):
                    proc.kill()
                    break
            th.join(2)
            with self.lock:
                self.done += len(lines) - seen
            got = set()
            for line in lines:
                try:
                    j = json.loads(line)
                except ValueError:
                    continue
                got.add(j["model"])
                if "error" in j:
                    with self.lock:
                        self.failed += 1
                    loader().note_failed(j["model"])
            left = [m for m in batch if m not in got]
            if left and not self.stop:                # the first unread one hung the reader: skip it
                with self.lock:
                    self.failed += 1
                    self.done += 1
                    self.todo[:0] = left[1:]
                loader().note_failed(left[0])


_loader = None


def loader():
    global _loader
    if _loader is None:
        _loader = Loader()
    return _loader


if __name__ == "__main__":
    worker_main(sys.argv[1:])

"""SIMPLE 3D VIEW of a car for the Custom car tab: a game car's body (+ the wheels of any game car, or an imported
.glb body) as flat-coloured parts, drawn with the object library's picture renderer (library/thumb.py).

Car models (models\\vehicles\\<name>.model) differ from scenery: positions are 4 half floats (x, up, forward, 0),
every part is an element ("e_exterior", "e_Tyre_FrontLeft", ...) placed by its own matrix (x, forward, up), and each mesh has 3 detail levels (data1[0] = the best). The parts are kept per kind (paint, dark,
glass, light, tyre, rim) and per wheel corner, so another car's wheels can be put on the body.
A read car is kept in WORK/car_preview/<name>.npz."""
import contextlib
import io
import json
import os
import sys
import tempfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.dirname(HERE)
for p in (MB, os.path.join(MB, "library"), os.path.join(MB, "model"), os.path.join(MB, "flat_earth")):
    if p not in sys.path:
        sys.path.insert(0, p)
import paths

CORNERS = ("frontleft", "frontright", "backleft", "backright")
SKIP = ("dam", "debris", "broken", "collision", "shadow", "smash")
COLOURS = {"dark": (38, 40, 46), "glass": (70, 95, 120), "light": (225, 225, 210), "tyre": (24, 24, 26),
           "rim": (175, 180, 190)}


def folder():
    return os.path.join(paths.WORK, "car_preview")


def model_path(name):
    return "models\\vehicles\\%s.model" % name.lower()


def _kind(el):
    el = el.lower()
    if "tyre" in el:
        return "tyre"
    if "alloyrim" in el or "brake" in el or "hubcap" in el or "rim" in el:
        return "rim"
    if "glass" in el or "window" in el or "screen" in el:
        return "glass"
    if "light" in el or "lamp" in el:
        return "light"
    if any(s in el for s in ("interior", "steering", "gearstick", "signalstalk", "wiper", "mirror_centre",
                             "exhaust", "grille", "driver")):
        return "dark"
    return "paint"


def _corner(el):
    el = el.lower().replace("_", "")
    return next((c for c in CORNERS if c in el), None)


def read_game_car(name):
    """{"<kind>[|corner]": triangles (n, 3, 3)} + "centre|<corner>" (3,) wheel centres; cached"""
    p = os.path.join(folder(), name.lower() + ".npz")
    if os.path.exists(p):
        z = np.load(p)
        return {k: z[k] for k in z.files}
    import game_preview
    import cpmodel
    data = game_preview._model_bytes(model_path(name))
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".model")
    f.write(data)
    f.close()
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            M = cpmodel._reader().read_cpmodel_data(None, f.name)
    finally:
        os.unlink(f.name)
    els = M["elements"]

    def world(i, depth=0):                          # element position in vertex axes (x, up, forward)
        e = els[i]
        if not isinstance(e, dict) or depth > 20:
            return np.zeros(3)
        x, fwd, up = e["matrix"]["position"]
        return np.array([x, up, fwd])               # (a part's own matrix already holds its parent's place)

    def types(defn):
        return tuple(d["type"] for d in defn if d.get("prefix", 0) == 0)
    streams = {types(vs["definition"]): vs for vs in M["vertex_streams"]}
    out = {}
    for mesh in M["meshes"]:
        oi = mesh["object_index"]
        el = els[oi]["name"] if oi < len(els) and isinstance(els[oi], dict) else ""
        if any(s in el.lower() for s in SKIP):
            continue
        vs = streams.get(types(M["vert_definitions"][mesh["definition"]]))
        if vs is None or vs["definition"][0]["type"] != 9:
            continue
        stride = vs["bytes"]
        fs = M["face_streams"][mesh["face_stream_index"]]
        d1, d2 = mesh["data1"][0], mesh["data2"][0]
        base = d2["vOffset"] // stride
        idx = np.frombuffer(data, np.uint16, fs["count"], fs["start"])
        faces = idx[d1["face_offset"]:d1["face_offset"] + d1["face_count"]].astype(np.int64)
        if mesh["face_type"] == 1:
            if len(faces) < 3:
                continue
            tri = np.stack([faces[:-2], faces[1:-1], faces[2:]], axis=1)
            tri[1::2] = tri[1::2, ::-1]                     # strips flip every other triangle
        else:
            tri = faces[:len(faces) // 3 * 3].reshape(-1, 3)
        tri = tri[(tri[:, 0] != tri[:, 1]) & (tri[:, 1] != tri[:, 2]) & (tri[:, 0] != tri[:, 2])]
        v = (tri + base).reshape(-1)
        if not len(tri) or v.max() >= vs["count"]:
            continue
        rows = np.frombuffer(data, np.uint8, vs["count"] * stride, vs["start"]).reshape(vs["count"], stride)
        pos = rows[v, 0:6].copy().view(np.float16).reshape(-1, 3).astype(np.float32) + world(oi)
        kind, corner = _kind(el), _corner(el)
        key = kind + ("|" + corner if corner and kind in ("tyre", "rim") else "")
        out.setdefault(key, []).append(pos.reshape(-1, 3, 3))
    out = {k: np.concatenate(v) for k, v in out.items()}
    for c in CORNERS:                                       # wheel centre = middle of its tyre
        t = out.get("tyre|" + c)
        if t is not None:
            pts = t.reshape(-1, 3)
            out["centre|" + c] = (pts.min(0) + pts.max(0)) / 2
    os.makedirs(folder(), exist_ok=True)
    np.savez_compressed(p + ".tmp.npz", **out)
    os.replace(p + ".tmp.npz", p)
    return out


def read_glb_body(path):
    """an imported body (.glb): {"paint": triangles} in the same axes, standing on the ground, centred"""
    import model_import
    g = model_import.read_glb(path)
    parts, lo, hi = model_import.game_space(g)
    tris = [p["pos"][p["tri"]] for p in parts]
    return {"paint": np.concatenate(tris).astype(np.float32)}


def assemble(body, wheels=None):
    """{kind: triangles}: the body's parts; with `wheels` (another car's parts) its tyres + rims moved onto the
    body's wheel centres"""
    out = {}
    for k, t in body.items():
        if k.startswith("centre|") or (wheels is not None and k.split("|")[0] in ("tyre", "rim")):
            continue
        out.setdefault(k.split("|")[0], []).append(t)
    if wheels is not None:
        for c in CORNERS:
            at = body.get("centre|" + c)
            src = wheels.get("centre|" + c)
            if at is None or src is None:
                continue
            for kind in ("tyre", "rim"):
                t = wheels.get(kind + "|" + c)
                if t is not None:
                    out.setdefault(kind, []).append(t - src + at)
    return {k: np.concatenate(v) for k, v in out.items()}


def mats(parts, paint):
    """the renderer's material list: one flat colour per kind (paint = the car's colour 1)"""
    from PIL import Image
    out = []
    for kind, t in sorted(parts.items()):
        col = paint if kind == "paint" else COLOURS[kind]
        pos = (t * np.array([1.0, 1.0, -1.0], np.float32)).reshape(-1, 3)    # forward = the renderer's -z
        out.append({"key": "car|%s|%s" % (kind, col), "image": Image.new("RGB", (4, 4), col),
                    "pos": np.ascontiguousarray(pos, np.float32), "uv": np.zeros((len(pos), 2), np.float32),
                    "nrm": None, "glow": 0.0})
    return out


def picture(parts, paint, size, yaw, pitch=0.35):
    """PIL picture of the car turned by yaw (radians)"""
    import thumb
    old = thumb.YAW, thumb.PITCH
    thumb.YAW, thumb.PITCH = yaw, pitch
    try:
        return thumb.render(mats(parts, paint), size=size, bg=(6, 10, 20))
    finally:
        thumb.YAW, thumb.PITCH = old


if __name__ == "__main__":
    import time
    t = time.time()
    b = read_game_car(sys.argv[1] if len(sys.argv) > 1 else "Dodge_Challenger_Tuner")
    print({k: v.shape for k, v in b.items()}, "%.1fs" % (time.time() - t))
    w = read_game_car(sys.argv[2]) if len(sys.argv) > 2 and sys.argv[2] else None
    t = time.time()
    img = picture(assemble(b, w), (30, 155, 255), 420, -0.7)
    print("render %.2fs" % (time.time() - t))
    img.save(os.path.join(sys.argv[3] if len(sys.argv) > 3 else ".", "car_preview.png"))

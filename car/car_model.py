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
        try:
            z = np.load(p)
            return {k: z[k] for k in z.files}
        except (OSError, ValueError, EOFError):            # a broken cache file: read the car again
            os.remove(p)
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


_glb = {}


def read_glb_body(path):
    """an imported body (.glb, one model - no damage parts): {"glb": triangles, "glb_rgb": their colours (its own
    textures / material colours: the paint colours do not apply)}, in game axes, standing on the ground, centred"""
    key = (path, os.path.getmtime(path))
    if key in _glb:
        return _glb[key]
    import model_import
    g = model_import.read_glb(path)
    parts, lo, hi = model_import.game_space(g)
    tris, rgb = [], []
    for p in parts:
        t = p["pos"][p["tri"]]
        mat = g["materials"][p["mat"]] if 0 <= p["mat"] < len(g["materials"]) else None
        col = np.array([int(255 * max(0.0, min(1.0, c))) for c in (mat["colour"][:3] if mat else (0.7, 0.7, 0.7))])
        if mat is not None and mat["image"] is not None and p.get("uv") is not None and len(p["uv"]):
            img = np.asarray(mat["image"].convert("RGB"))
            uv = p["uv"][p["tri"]].mean(1) % 1.0                      # each triangle: the texture at its middle
            px = img[(uv[:, 1] * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)]
            c = px.astype(np.float32) * (col / 255.0)
        else:
            c = np.repeat(col[None].astype(np.float32), len(t), 0)
        tris.append(t)
        rgb.append(c)
    out = {"glb": np.concatenate(tris).astype(np.float32), "glb_rgb": np.concatenate(rgb).astype(np.float32)}
    _glb.clear()
    _glb[key] = out
    return out


def fit_glb(glb, body, fit):
    """the imported body placed on the game body's wheels: first scaled to the game body's length and centred
    on it, then the player's fine-tuning fit = {"size" (x), "fwd", "up" (m), "turn" (degrees, around up)}"""
    t = glb["glb"].astype(np.float64)
    a = np.radians(fit.get("turn", 0))
    x, z = t[..., 0] * np.cos(a) - t[..., 2] * np.sin(a), t[..., 0] * np.sin(a) + t[..., 2] * np.cos(a)
    t = np.stack([x, t[..., 1], z], -1)
    ref = np.concatenate([v.reshape(-1, 3) for k, v in body.items() if k == "paint"])
    lo, hi = t.reshape(-1, 3).min(0), t.reshape(-1, 3).max(0)
    rlo, rhi = ref.min(0), ref.max(0)
    k = (rhi[2] - rlo[2]) / max(1e-6, hi[2] - lo[2]) * fit.get("size", 1.0)
    t = (t - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * k
    t += [(rlo[0] + rhi[0]) / 2, rlo[1] + fit.get("up", 0.0), (rlo[2] + rhi[2]) / 2 + fit.get("fwd", 0.0)]
    return {"glb": t.astype(np.float32), "glb_rgb": glb["glb_rgb"]}


def assemble(body, wheels=None, fit=None):
    """{kind: triangles}: the body's parts with the tyres + rims of `wheels` (another car's parts; None = its
    own) on the body's wheel centres, moved apart by fit {"track": side to side, "wb": front to back} (m)"""
    fit = fit or {}
    src = wheels if wheels is not None else body
    out = {}
    for k, t in body.items():
        if k.startswith("centre|") or (k.split("|")[0] in ("tyre", "rim") and "|" in k):
            continue
        if wheels is not None and k == "rim":
            continue
        out.setdefault(k.split("|")[0], []).append(t)
    for c in CORNERS:
        at, frm = body.get("centre|" + c), src.get("centre|" + c)
        if at is None or frm is None:
            continue
        at = at + [np.sign(at[0]) * fit.get("track", 0.0) / 2, 0.0, np.sign(at[2]) * fit.get("wb", 0.0) / 2]
        for kind in ("tyre", "rim"):
            t = src.get(kind + "|" + c)
            if t is not None:
                out.setdefault(kind, []).append(t - frm + at)
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


def picture(parts, paint, size, yaw, pitch=-0.35, smooth=True):
    """PIL picture of the car turned by yaw (radians): flat shaded triangles, far ones first (painter's
    algorithm, numpy + PIL - fast enough to turn the car with the mouse); smooth = drawn 2x and shrunk"""
    from PIL import Image, ImageDraw
    k = 2 if smooth else 1
    S = size * k
    kinds = sorted(k for k in parts if k != "glb_rgb")
    tris = np.concatenate([parts[n] for n in kinds]).astype(np.float64)
    cols = np.concatenate([parts["glb_rgb"] if n == "glb" else
                           np.repeat([paint if n == "paint" else COLOURS[n]], len(parts[n]), 0) for n in kinds])
    cy, sy, cp, sp = np.cos(yaw), np.sin(yaw), np.cos(pitch), np.sin(pitch)
    x, y, z = tris[..., 0], tris[..., 1], tris[..., 2]
    x1, z1 = x * cy - z * sy, x * sy + z * cy                 # turn around the up axis
    y2, z2 = y * cp + z1 * sp, z1 * cp - y * sp              # tilt: look down at the car
    pts = np.stack([x1, y2, z2], -1)
    n = np.cross(pts[:, 1] - pts[:, 0], pts[:, 2] - pts[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
    light = np.abs(n @ (np.array([0.35, 0.75, -0.55]) / np.linalg.norm([0.35, 0.75, -0.55])))
    shade = (0.35 + 0.65 * light)[:, None]
    rgb = np.clip(cols * shade, 0, 255).astype(np.uint8)
    lo, hi = pts[..., :2].reshape(-1, 2).min(0), pts[..., :2].reshape(-1, 2).max(0)
    scale = 0.9 * S / max(hi - lo)
    mid = (lo + hi) / 2
    sx = (pts[..., 0] - mid[0]) * scale + S / 2
    sy2 = S / 2 - (pts[..., 1] - mid[1]) * scale
    order = np.argsort(pts[..., 2].mean(1))                   # far (small z) first ...
    order = order[n[order, 2] >= 0]                           # ... and only the sides facing us
    img = Image.new("RGB", (S, S), (6, 10, 20))
    d = ImageDraw.Draw(img)
    xy = np.stack([sx, sy2], -1)
    area = np.abs((xy[order, 1, 0] - xy[order, 0, 0]) * (xy[order, 2, 1] - xy[order, 0, 1]) -
                  (xy[order, 2, 0] - xy[order, 0, 0]) * (xy[order, 1, 1] - xy[order, 0, 1]))
    order = order[area >= (0.1 if smooth else 3.0)]            # turning with the mouse: tiny triangles left out
    for i in order:
        d.polygon([tuple(p) for p in xy[i]], fill=tuple(rgb[i]))
    return img.resize((size, size), Image.LANCZOS) if k > 1 else img


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

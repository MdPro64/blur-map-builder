"""
IMPORTED 3D MODELS (3D tab, object library "Your models"): a .glb file (glTF 2.0 binary = mesh + its textures in ONE
file; Blender: File > Export > glTF 2.0, format "glTF Binary") becomes pieces of our own models
(cpmodel.make_mesh on road_mesh.SOURCE: up to 3232 vertices / 4861 strip indices and ONE 256 x 256 texture each; a big
model is cut into several pieces, each material gets its own pieces). It is placed like a library object
(m["objects"] id "import:<file>", key obj:<n>: move / turn / scale / hide, Solid = its box as a wall).

Files (with the player's maps): imports\\<name>.glb + imports\\<name>.glb.json (what the import found: box, size,
pieces, slot = its uid block).
Axes: glTF is right-handed, +y up; the game mirrors one axis (the BlurImportExport reader swaps y / z): z -> -z and
every triangle's winding reversed. The model stands on its lowest point, centred on its box (x / z).
"""
import json
import math
import os
import shutil
import struct
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import paths  # noqa: E402

KIND = "import"
ID_PREFIX = "import:"
VCAP, ICAP = 3232, 4861                # one piece: the source model's vertex / strip index room
GAME_TEX = 256                         # the source model's texture (256 x 256): every picture is shown at this size
WARN_TRIS = 10_000                     # more: a warning (may lag)
MAX_TRIS = 50_000                      # more: refused (simplify it first)
MAX_PIECES = 60                        # pieces of one model (the tile list has 144 places per map)
WARN_PIECES = 20
IMPORT_UID = 0xF1E30000                # + slot * 256: model = + 2k, DynamicObjDef = + 2k + 1 (k < 128)
SHINE = 25                             # material red (shine), as our other objects
PREVIEW_TRIS = 6000                    # what the 3D tab draws of one model


def folder():
    return os.path.join(paths.TRACKS, "imports")


class ImportError_(Exception):
    """a file we can't use (the text says why, for the player)"""


# ---------------------------------------------------------------------------------------------- reading a .glb
_CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
_NORM = {5120: 127.0, 5121: 255.0, 5122: 32767.0, 5123: 65535.0}


def _chunks(path):
    data = open(path, "rb").read()
    if len(data) < 20 or data[:4] != b"glTF":
        raise ImportError_("Not a .glb file. In Blender: File > Export > glTF 2.0, Format: glTF Binary (.glb).")
    if struct.unpack_from("<I", data, 4)[0] != 2:
        raise ImportError_("Only glTF 2.0 .glb files are supported.")
    off, js, binary = 12, None, b""
    while off + 8 <= len(data):
        n, typ = struct.unpack_from("<II", data, off)
        body = data[off + 8:off + 8 + n]
        if typ == 0x4E4F534A:
            js = json.loads(body.decode("utf-8"))
        elif typ == 0x004E4942:
            binary = body
        off += 8 + n
    if js is None:
        raise ImportError_("The .glb file has no scene description (broken file?).")
    return js, binary


def _accessor(js, binary, i, normalized_float=False):
    a = js["accessors"][i]
    n, ct, nc = a["count"], a["componentType"], _NC[a["type"]]
    dt = np.dtype(_CT[ct])
    if "bufferView" not in a:
        out = np.zeros((n, nc), dt)
    else:
        bv = js["bufferViews"][a["bufferView"]]
        if bv.get("buffer", 0) != 0:
            raise ImportError_("The model's data is in a separate file - export as glTF Binary (.glb).")
        start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride") or dt.itemsize * nc
        if stride == dt.itemsize * nc:
            out = np.frombuffer(binary, dt, n * nc, start).reshape(n, nc)
        else:
            rows = np.frombuffer(binary, np.uint8, stride * (n - 1) + dt.itemsize * nc, start)
            out = np.lib.stride_tricks.as_strided(rows, (n, dt.itemsize * nc), (stride, 1)).copy().view(dt)
            out = out.reshape(n, nc)
    if "sparse" in a:
        raise ImportError_("This .glb uses 'sparse' data (morph / shape keys) - apply them in Blender first.")
    if normalized_float and ct in _NORM:
        return out.astype(np.float64) / _NORM[ct]
    return out.astype(np.float64) if ct == 5126 else out


def _node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], np.float64).reshape(4, 4).T
    t = node.get("translation", (0, 0, 0))
    x, y, z, w = node.get("rotation", (0, 0, 0, 1))
    s = node.get("scale", (1, 1, 1))
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * np.array(s, np.float64)
    m[:3, 3] = t
    return m


def _image(js, binary, tex_index):
    """PIL image of a texture, or (None, why)"""
    from PIL import Image
    import io
    tex = js.get("textures", [])[tex_index]
    src = tex.get("source")
    if src is None:
        return None, "its texture format is not supported (KTX2 / Basis / WebP) - plain colour used"
    im = js["images"][src]
    if "bufferView" not in im:
        return None, "its picture is a separate file - export as glTF Binary (.glb) to include it"
    bv = js["bufferViews"][im["bufferView"]]
    raw = binary[bv.get("byteOffset", 0):bv.get("byteOffset", 0) + bv["byteLength"]]
    try:
        img = Image.open(io.BytesIO(raw))
        img.load()
    except Exception:                                   # noqa: BLE001 - reported as a note
        return None, "its picture could not be read (%s) - plain colour used" % im.get("mimeType", "?")
    return img, None


def read_glb(path):
    """{"parts": [{"pos", "nrm", "uv", "tri", "mat"}], "materials": [{"name", "image", "colour", "glow", "note"}]}
    in glTF space (node transforms applied)"""
    js, binary = _chunks(path)
    need = set(js.get("extensionsRequired", []))
    if need & {"KHR_draco_mesh_compression", "EXT_meshopt_compression"}:
        raise ImportError_("This .glb is compressed (Draco / meshopt). In Blender export it again with "
                           "'Compression' switched off.")
    materials, mat_of = [], {}

    def material(mi):
        if mi in mat_of:
            return mat_of[mi]
        m = js.get("materials", [])[mi] if mi is not None and mi < len(js.get("materials", [])) else {}
        pbr = m.get("pbrMetallicRoughness", {})
        f = pbr.get("baseColorFactor", (1, 1, 1, 1))
        img, note = None, None
        if "baseColorTexture" in pbr:
            img, note = _image(js, binary, pbr["baseColorTexture"]["index"])
        em = m.get("emissiveFactor", (0, 0, 0))
        glow = max(em) if ("emissiveTexture" in m or max(em) > 0) else 0.0
        materials.append({"name": m.get("name") or "material %d" % len(materials), "image": img,
                          "colour": tuple(float(c) for c in f[:3]), "glow": float(min(1.0, glow)), "note": note})
        mat_of[mi] = len(materials) - 1
        return mat_of[mi]

    parts = []
    skipped = set()

    def visit(ni, parent):
        node = js["nodes"][ni]
        world = parent @ _node_matrix(node)
        if "mesh" in node:
            for prim in js["meshes"][node["mesh"]]["primitives"]:
                mode = prim.get("mode", 4)
                if mode != 4:
                    skipped.add("lines / points" if mode < 4 else "strips / fans")
                    continue
                at = prim["attributes"]
                if "POSITION" not in at:
                    continue
                pos = _accessor(js, binary, at["POSITION"]).astype(np.float64)
                n = len(pos)
                tri = (_accessor(js, binary, prim["indices"]).reshape(-1).astype(np.int64)
                       if "indices" in prim else np.arange(n))
                tri = tri[:len(tri) // 3 * 3].reshape(-1, 3)
                uv = (_accessor(js, binary, at["TEXCOORD_0"], normalized_float=True).astype(np.float64)
                      if "TEXCOORD_0" in at else np.zeros((n, 2)))
                p4 = np.c_[pos, np.ones(n)] @ world.T
                pos_w = p4[:, :3]
                if "NORMAL" in at:
                    nrm = _accessor(js, binary, at["NORMAL"]).astype(np.float64)
                    nm = np.linalg.inv(world[:3, :3]).T
                    nrm = nrm @ nm.T
                else:
                    nrm = np.zeros((n, 3))
                    a, b, c = pos_w[tri[:, 0]], pos_w[tri[:, 1]], pos_w[tri[:, 2]]
                    fn = np.cross(b - a, c - a)
                    for k in range(3):
                        np.add.at(nrm, tri[:, k], fn)
                nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
                if np.linalg.det(world[:3, :3]) < 0:        # a mirrored node: its triangles face the other way
                    tri = tri[:, [0, 2, 1]]
                parts.append({"pos": pos_w, "nrm": nrm, "uv": uv[:, :2], "tri": tri,
                              "mat": material(prim.get("material"))})
        for c in node.get("children", []):
            visit(c, world)

    scene = js.get("scenes", [{}])[js.get("scene", 0)] if js.get("scenes") else {"nodes": list(range(len(
        js.get("nodes", []))))}
    for ni in scene.get("nodes", []):
        visit(ni, np.eye(4))
    if not parts:
        raise ImportError_("No triangles found in this .glb (only %s?)." % (", ".join(sorted(skipped)) or "empty"))
    return {"parts": parts, "materials": materials, "skipped": sorted(skipped)}


# ---------------------------------------------------------------------------------------------- to game space
def game_space(g):
    """the parts in the game's axes (z mirrored, winding reversed), standing on y = 0, centred on x / z; also
    (lo, hi) of the box"""
    allp = np.concatenate([p["pos"] for p in g["parts"]])
    lo, hi = allp.min(axis=0), allp.max(axis=0)
    shift = np.array([-(lo[0] + hi[0]) / 2, -lo[1], -(lo[2] + hi[2]) / 2])
    out = []
    for p in g["parts"]:
        pos = (p["pos"] + shift) * np.array([1.0, 1.0, -1.0])
        nrm = p["nrm"] * np.array([1.0, 1.0, -1.0])
        out.append(dict(p, pos=pos, nrm=nrm, tri=p["tri"][:, [0, 2, 1]]))
    glo = np.array([lo[0] + shift[0], 0.0, -(hi[2] + shift[2])])
    ghi = np.array([hi[0] + shift[0], hi[1] + shift[1], -(lo[2] + shift[2])])
    return out, glo, ghi


def pieces(parts):
    """[{"mat", "verts": [(x, y, z, nx, ny, nz, u, v)], "strip"}] - each fits one model (VCAP / ICAP); the
    triangles of one material go into its own pieces"""
    out = []
    by_mat = {}
    for p in parts:
        by_mat.setdefault(p["mat"], []).append(p)
    for mat in sorted(by_mat):
        cur = None
        for p in by_mat[mat]:
            pos, nrm, uv = p["pos"], p["nrm"], p["uv"]
            vmap = {}                                    # this part's vertex -> the current piece's
            for a, b, c in p["tri"].tolist():
                if a == b or b == c or a == c:
                    continue
                new = len({a, b, c} - vmap.keys())
                join = 2 + len(cur["strip"]) % 2 if cur and cur["strip"] else 0
                if cur is None or len(cur["verts"]) + new > VCAP or len(cur["strip"]) + join + 3 > ICAP:
                    cur = {"mat": mat, "verts": [], "strip": []}
                    out.append(cur)
                    vmap = {}
                idx = []
                for v in (a, b, c):
                    if v not in vmap:
                        vmap[v] = len(cur["verts"])
                        cur["verts"].append((pos[v][0], pos[v][1], pos[v][2], nrm[v][0], nrm[v][1], nrm[v][2],
                                             uv[v][0], uv[v][1]))
                    idx.append(vmap[v])
                st = cur["strip"]
                if st:
                    st += [st[-1], idx[0]]                   # degenerate join
                    if len(st) % 2:
                        st.append(idx[0])                    # the new triangle starts at an even place
                st += idx
    return out


# ---------------------------------------------------------------------------------------------- checking
def analyse(path):
    """what the import window shows: {"name", "tris", "verts", "size" (x, y, z m), "materials", "textures"
    [(w, h)], "pieces", "notes" [(level "ok" / "info" / "warn" / "stop", text)], "lo", "hi"}"""
    g = read_glb(path)
    parts, lo, hi = game_space(g)
    tris = int(sum(len(p["tri"]) for p in parts))
    verts = int(sum(len(p["pos"]) for p in parts))
    size = (hi - lo).tolist()
    texs = [m["image"].size for m in g["materials"] if m["image"] is not None]
    notes = []
    if tris > MAX_TRIS:
        notes.append(("stop", "%d triangles - too many (most for one model: %d). Simplify it first (Blender: "
                              "Decimate modifier)." % (tris, MAX_TRIS)))
        n_pieces = None
    else:
        n_pieces = len(pieces(parts))
        if tris > WARN_TRIS:
            notes.append(("warn", "%d triangles - a big model: it may make the game lag. Under %d is best."
                                  % (tris, WARN_TRIS)))
        else:
            notes.append(("ok", "%d triangles - fine." % tris))
        if n_pieces > MAX_PIECES:
            notes.append(("stop", "Needs %d model pieces (most: %d). Simplify it or use fewer materials."
                                  % (n_pieces, MAX_PIECES)))
        elif n_pieces > WARN_PIECES:
            notes.append(("warn", "Needs %d model pieces. A map has room for about 144 objects in all (road "
                                  "pieces and scenery included)." % n_pieces))
    big = [t for t in texs if max(t) > GAME_TEX]
    if big:
        w, h = max(big, key=lambda t: t[0] * t[1])
        notes.append(("warn" if max(w, h) > 2048 else "info",
                      "Texture %d x %d: shown at %d x %d in the game (softer)." % (w, h, GAME_TEX, GAME_TEX)))
    for m in g["materials"]:
        if m["note"]:
            notes.append(("info", "Material '%s': %s." % (m["name"], m["note"])))
        elif m["image"] is None:
            notes.append(("info", "Material '%s' has no texture - shown in its plain colour." % m["name"]))
    if g["skipped"]:
        notes.append(("info", "Left out: %s (only triangles are used)." % ", ".join(g["skipped"])))
    biggest = max(size)
    if biggest > 800:
        notes.append(("warn", "It is %.0f m big - maybe made in centimetres? Scale it down in the 3D tab (S)."
                              % biggest))
    elif biggest < 0.3:
        notes.append(("warn", "It is only %.2f m big - maybe made in other units? Scale it up in the 3D tab (S)."
                              % biggest))
    notes.append(("info", "Collision: none (cars drive through it). Tick 'Solid' in the 3D tab to make it a wall - "
                          "a simple BOX around the whole model, not its exact shape (don't use it as a road / ramp)."))
    return {"name": os.path.splitext(os.path.basename(path))[0], "tris": tris, "verts": verts, "size": size,
            "materials": len(g["materials"]), "textures": texs, "pieces": n_pieces, "notes": notes,
            "lo": lo.tolist(), "hi": hi.tolist()}


def blocked(info):
    return any(level == "stop" for level, _ in info["notes"])


# ---------------------------------------------------------------------------------------------- the library
def _sidecars():
    d = folder()
    out = []
    try:
        names = sorted(os.listdir(d))
    except OSError:
        return out
    for f in names:
        if f.lower().endswith(".glb.json"):
            try:
                out.append(json.load(open(os.path.join(d, f), encoding="utf-8")))
            except (OSError, ValueError):
                pass
    return out


def entries():
    """catalog entries (road_layout.catalog) of every imported model"""
    out = []
    for s in _sidecars():
        if not os.path.exists(os.path.join(folder(), s["file"])):
            continue
        out.append({"id": ID_PREFIX + s["file"], "kind": KIND, "name": s["name"], "loc": "Your models",
                    "cat": "Imported", "model": s["file"], "uid": "%08X" % uids(s["slot"], 0)[0], "lo": s["lo"],
                    "hi": s["hi"], "sized": True, "slot": s["slot"], "pieces": s["pieces"], "tris": s["tris"]})
    return out


def add(path, info=None):
    """copy the .glb next to the maps (a file of the same name is replaced: its slot kept) + the .json; returns the
    catalog id"""
    info = info or analyse(path)
    if blocked(info):
        raise ImportError_(next(t for lv, t in info["notes"] if lv == "stop"))
    os.makedirs(folder(), exist_ok=True)
    name = "".join(c if c.isalnum() or c in "-_" else "_" for c in info["name"])[:40] or "model"
    fname = name + ".glb"
    side = os.path.join(folder(), fname + ".json")
    old = None
    if os.path.exists(side):
        try:
            old = json.load(open(side, encoding="utf-8"))
        except (OSError, ValueError):
            old = None
    used = {s["slot"] for s in _sidecars() if s["file"] != fname}
    slot = old["slot"] if old and old.get("slot") not in used else next(i for i in range(256) if i not in used)
    dst = os.path.join(folder(), fname)
    if os.path.abspath(path) != os.path.abspath(dst):
        shutil.copyfile(path, dst)
    json.dump({"file": fname, "name": info["name"], "slot": slot, "lo": info["lo"], "hi": info["hi"],
               "tris": info["tris"], "pieces": info["pieces"], "materials": info["materials"],
               "source": os.path.abspath(path)}, open(side, "w", encoding="utf-8"), indent=1)
    return ID_PREFIX + fname


def uids(slot, k):
    """(GameModel uid, DynamicObjDef uid) of piece k of the model in this slot"""
    assert 0 <= slot < 256 and k < 128, (slot, k)
    b = IMPORT_UID + slot * 256
    return b + 2 * k, b + 2 * k + 1


def model_file(slot, k):
    return "models\\objects\\flat_earth\\imp_%02x_%03d.model" % (slot, k)


def used(wld):
    """the catalog entries of the imported models placed in any map"""
    ids = {o.get("id") for loc in wld["locations"] for m in loc["maps"] for o in m.get("objects") or []}
    return [e for e in entries() if e["id"] in ids]


# ---------------------------------------------------------------------------------------------- the build
def textures(mat, w, h, mw, mh):
    """(diffuse RGB bytes w x h, material RGB bytes mw x mh) of one material"""
    from PIL import Image
    col = tuple(int(round(255 * max(0.0, min(1.0, c)))) for c in mat["colour"])
    if mat["image"] is not None:
        img = mat["image"].convert("RGB").resize((w, h), Image.LANCZOS)
        if col != (255, 255, 255):
            a = np.asarray(img, np.float64) * (np.array(col) / 255.0)
            img = Image.fromarray(a.clip(0, 255).astype(np.uint8))
        diff = img.tobytes()
    else:
        diff = bytes(col) * (w * h)
    glow = int(round(255 * mat["glow"]))
    return diff, bytes((SHINE, glow, 0)) * (mw * mh)


def build_files(base, cpmodel, wld, diff_name, mat_name, log=print):
    """{model file: bytes} of every imported model placed in the world (base = road_mesh.SOURCE's model)"""
    tex = cpmodel.parse(base)["textures"]
    _, tw, th, tm = tex[diff_name]
    _, mw, mh, mm = tex[mat_name]
    files = {}
    for e in used(wld):
        g = read_glb(os.path.join(folder(), e["model"]))
        parts, _, _ = game_space(g)
        ps = pieces(parts)
        assert len(ps) == e["pieces"], "%s changed since it was imported - import it again" % e["model"]
        enc = {}
        for k, p in enumerate(ps):
            if p["mat"] not in enc:
                d, mt = textures(g["materials"][p["mat"]], tw, th, mw, mh)
                enc[p["mat"]] = {diff_name: cpmodel.encode_mips(d, tw, th, tm),
                                 mat_name: cpmodel.encode_mips(mt, mw, mh, mm)}
            model, _ = cpmodel.make_mesh(base, p["verts"], p["strip"], enc[p["mat"]])
            # one texture id per model + material (its pieces share the picture)
            files[model_file(e["slot"], k)] = cpmodel.new_texture_ids(
                model, 0x0F5A0000 | (e["slot"] << 8) | (p["mat"] & 0xFF))
        log("imported model %s: %d triangles in %d piece(s)" % (e["name"], e["tris"], len(ps)))
    return files


# ---------------------------------------------------------------------------------------------- 3D tab preview
_preview = {}
PREVIEW_TEX = GAME_TEX                 # the 3D tab shows the texture as sharp as the game does


def preview(file):
    """the 3D tab's view of an imported model, ALL its triangles with their textures: [{"key" (texture key),
    "image" (PIL RGB, power of two), "pos" (n * 3, 3), "nrm", "uv"}] - one entry per material, model space (game
    axes); [] if it can't be read"""
    path = os.path.join(folder(), file)
    try:
        stamp = os.path.getmtime(path)
    except OSError:
        return []
    key = (path, stamp)
    if key in _preview:
        return _preview[key]
    try:
        g = read_glb(path)
        parts, _, _ = game_space(g)
    except Exception:                                    # noqa: BLE001 - the 3D tab then shows the box only
        _preview[key] = []
        return []
    from PIL import Image
    out = []
    for mi, mat in enumerate(g["materials"]):
        mine = [p for p in parts if p["mat"] == mi]
        if not mine:
            continue
        col = tuple(int(round(255 * max(0.0, min(1.0, c)))) for c in mat["colour"])
        if mat["image"] is not None:
            img = mat["image"].convert("RGB")
            w, h = (min(PREVIEW_TEX, 1 << max(0, (n - 1).bit_length())) for n in img.size)
            img = img.resize((w, h), Image.LANCZOS)
            if col != (255, 255, 255):
                img = Image.fromarray((np.asarray(img, np.float64) * (np.array(col) / 255.0)).clip(0, 255)
                                      .astype(np.uint8))
        else:
            img = Image.new("RGB", (4, 4), col)
        pos = np.concatenate([p["pos"][p["tri"].reshape(-1)] for p in mine]).astype(np.float32)
        nrm = np.concatenate([p["nrm"][p["tri"].reshape(-1)] for p in mine]).astype(np.float32)
        uv = np.concatenate([p["uv"][p["tri"].reshape(-1)] for p in mine]).astype(np.float32)
        out.append({"key": "%s|%s|%d" % (path, stamp, mi), "image": img, "pos": pos, "nrm": nrm, "uv": uv,
                    "glow": mat["glow"]})
    _preview[key] = out
    return out

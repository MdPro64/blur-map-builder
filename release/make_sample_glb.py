"""
TEST MODEL for the 3D tab's "Import model (.glb)": samples/MDPRO_test_crate.glb - a 4 m crate, each side its own
label (FRONT / BACK / LEFT / RIGHT / TOP / BOTTOM) + an arrow pointing to the reader's right, so a mirrored or
inside-out model is seen at once in the game; a glowing cyan bar on top (2nd material, no texture, emissive).
Usage: python release/make_sample_glb.py
"""
import io
import json
import os
import struct

import numpy as np
from PIL import Image, ImageDraw, ImageFont

MB = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(MB, "samples", "MDPRO_test_crate.glb")
TILE = 256
FACES = [  # label, outward normal, right (as seen from outside), up, tile colour
    ("FRONT", (0, 0, 1), (1, 0, 0), (0, 1, 0), (255, 138, 30)),
    ("BACK", (0, 0, -1), (-1, 0, 0), (0, 1, 0), (30, 155, 255)),
    ("RIGHT", (1, 0, 0), (0, 0, -1), (0, 1, 0), (88, 208, 106)),
    ("LEFT", (-1, 0, 0), (0, 0, 1), (0, 1, 0), (200, 80, 220)),
    ("TOP", (0, 1, 0), (1, 0, 0), (0, 0, -1), (240, 220, 60)),
    ("BOTTOM", (0, -1, 0), (1, 0, 0), (0, 0, 1), (150, 150, 150)),
]


def font(size):
    for f in ("arialbd.ttf", "segoeuib.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def atlas():
    """3 x 2 tiles, one per face"""
    img = Image.new("RGB", (3 * TILE, 2 * TILE), (6, 10, 20))
    d = ImageDraw.Draw(img)
    big, small = font(54), font(26)
    for i, (label, _, _, _, col) in enumerate(FACES):
        x0, y0 = (i % 3) * TILE, (i // 3) * TILE
        d.rectangle((x0 + 6, y0 + 6, x0 + TILE - 7, y0 + TILE - 7), fill=(11, 20, 38), outline=col, width=12)
        d.text((x0 + TILE / 2, y0 + 70), label, font=big, fill=col, anchor="mm")
        d.text((x0 + TILE / 2, y0 + 120), "MDPRO TEST", font=small, fill=(233, 242, 255), anchor="mm")
        # arrow to the reader's right
        ay = y0 + 182
        d.line((x0 + 50, ay, x0 + 190, ay), fill=(233, 242, 255), width=14)
        d.polygon([(x0 + 206, ay), (x0 + 172, ay - 26), (x0 + 172, ay + 26)], fill=(233, 242, 255))
        d.text((x0 + TILE / 2, y0 + 222), "this way = right", font=font(18), fill=(128, 149, 182), anchor="mm")
    return img


def box(c, half, faces, tiles=None):
    """vertices (pos, nrm, uv) + triangles of a box: centre c, half sizes; tiles = uv rectangle per face"""
    pos, nrm, uv, tri = [], [], [], []
    for i, (_, n, r, u, _) in enumerate(faces):
        n, r, u = np.array(n, float), np.array(r, float), np.array(u, float)
        h = np.array(half)
        b = len(pos)
        for s, t in ((-1, -1), (1, -1), (1, 1), (-1, 1)):            # anticlockwise seen from outside
            pos.append(np.array(c) + (n + s * r + t * u) * h)
            nrm.append(n)
            if tiles:
                tu0, tv0, tu1, tv1 = tiles[i]
                uv.append((tu0 + (s + 1) / 2 * (tu1 - tu0), tv0 + (1 - (t + 1) / 2) * (tv1 - tv0)))
            else:
                uv.append((0.5, 0.5))
        tri += [(b, b + 1, b + 2), (b, b + 2, b + 3)]
    return np.array(pos, np.float32), np.array(nrm, np.float32), np.array(uv, np.float32), np.array(tri, np.uint16)


def main():
    tiles = [((i % 3) / 3, (i // 3) / 2, (i % 3 + 1) / 3, (i // 3 + 1) / 2) for i in range(6)]
    pad = 2 / (3 * TILE)                                              # keep away from the tile border (mipmaps)
    tiles = [(a + pad, b + pad, c - pad, d - pad) for a, b, c, d in tiles]
    crate = box((0, 2, 0), (2, 2, 2), FACES, tiles)
    bar = box((0, 4.25, 0), (1.6, 0.25, 0.25), FACES)
    png = io.BytesIO()
    atlas().save(png, "PNG")
    blobs, views, accs = [], [], []

    def add(arr, target=None):
        data = arr.tobytes()
        off = sum(len(b) for b in blobs)
        blobs.append(data + b"\0" * (-len(data) % 4))
        views.append(dict({"buffer": 0, "byteOffset": off, "byteLength": len(data)},
                          **({"target": target} if target else {})))
        return len(views) - 1

    def acc(arr, typ, ct, target, minmax=False):
        v = add(arr, target)
        a = {"bufferView": v, "componentType": ct, "count": len(arr), "type": typ}
        if minmax:
            a["min"], a["max"] = arr.min(axis=0).tolist(), arr.max(axis=0).tolist()
        accs.append(a)
        return len(accs) - 1

    prims = []
    for (p, n, uv, t), mat in ((crate, 0), (bar, 1)):
        prims.append({"attributes": {"POSITION": acc(p, "VEC3", 5126, 34962, True), "NORMAL": acc(n, "VEC3", 5126, 34962),
                                     "TEXCOORD_0": acc(uv, "VEC2", 5126, 34962)},
                      "indices": acc(t.reshape(-1), "SCALAR", 5123, 34963), "material": mat})
    img_view = add(np.frombuffer(png.getvalue(), np.uint8))
    js = {"asset": {"version": "2.0", "generator": "Blur Map Builder sample (MDPRO)"},
          "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0, "name": "MDPRO_test_crate"}],
          "meshes": [{"name": "crate", "primitives": prims}],
          "materials": [{"name": "crate labels", "pbrMetallicRoughness": {"baseColorTexture": {"index": 0},
                                                                          "metallicFactor": 0, "roughnessFactor": 1}},
                        {"name": "glow bar", "pbrMetallicRoughness": {"baseColorFactor": [0.12, 0.85, 1.0, 1.0],
                                                                      "metallicFactor": 0},
                         "emissiveFactor": [0.12, 0.85, 1.0]}],
          "textures": [{"source": 0, "sampler": 0}], "samplers": [{"wrapS": 10497, "wrapT": 10497}],
          "images": [{"bufferView": img_view, "mimeType": "image/png"}],
          "accessors": accs, "bufferViews": views}
    binary = b"".join(blobs)
    js["buffers"] = [{"byteLength": len(binary)}]
    jb = json.dumps(js, separators=(",", ":")).encode()
    jb += b" " * (-len(jb) % 4)
    total = 12 + 8 + len(jb) + 8 + len(binary)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<II", len(jb), 0x4E4F534A) + jb)
        f.write(struct.pack("<II", len(binary), 0x004E4942) + binary)
    atlas().save(os.path.splitext(OUT)[0] + "_texture.png")
    print("wrote", OUT, total, "bytes")


if __name__ == "__main__":
    main()

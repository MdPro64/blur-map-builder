"""CUSTOM SKY (Lighting tab "Custom sky (HDRI)"): the player's 360 panorama as the location's sky picture.

The game's skies (root.pak graphics\\textures\\skies\\<name>.texture, picked by LightingSettings.skySettings.texture,
amax record +112) are all the same: 0x54-byte header + 2048 x 512 DXT5, one level. The picture is the UPPER HALF of
an equirectangular panorama (top row = straight up, bottom row = the horizon), stored as colour x brightness: every
pixel's brightest channel is 255 and alpha holds its brightness (the sun = the highest alpha) - measured on all 19
game skies, 2026-10-27.

Our sky: the panorama (2:1, .hdr / .exr / .png / .jpg) -> top half -> gamma -> brightness = brightest channel
(scaled so the brightest 1 % - the sun - just reaches 1) -> DXT5. The location's lighting record gets a new sky NAME
of the same length as its preset's ("t02c" -> "z000"), so its string pool keeps its size (lighting.prepare edits
the preset's own record in place)."""
import io
import os
import re
import struct

import numpy as np

W, H = 2048, 512
SKY_DIR = "graphics/textures/skies/"
SKY_OFF = 112                     # LightingSettings.skySettings.texture (sz8)


def _read_hdr(path):
    """Radiance .hdr (RGBE, flat or new run-length scanlines) -> float32 (h, w, 3)"""
    d = open(path, "rb").read()
    at = 0
    while True:                                    # header lines up to an empty one, then the size line
        end = d.index(b"\n", at)
        line = d[at:end].strip()
        at = end + 1
        if not line:
            break
    end = d.index(b"\n", at)
    m = re.match(rb"([-+])Y (\d+) ([-+])X (\d+)", d[at:end].strip())
    h, w = int(m.group(2)), int(m.group(4))
    at = end + 1
    out = np.zeros((h, w, 4), np.uint8)
    for y in range(h):
        if w >= 8 and w < 32768 and d[at] == 2 and d[at + 1] == 2 and (d[at + 2] << 8 | d[at + 3]) == w:
            at += 4
            for c in range(4):
                x = 0
                while x < w:
                    n = d[at]
                    at += 1
                    if n > 128:
                        n -= 128
                        out[y, x:x + n, c] = d[at]
                        at += 1
                    else:
                        out[y, x:x + n, c] = np.frombuffer(d, np.uint8, n, at)
                        at += n
                    x += n
        else:
            out[y] = np.frombuffer(d, np.uint8, w * 4, at).reshape(w, 4)
            at += w * 4
    e = out[..., 3].astype(np.int32)
    k = np.where(e > 0, np.ldexp(1.0, e - 136), 0.0)       # (mantissa + 0.5) / 256 * 2^(e - 128)
    img = (out[..., :3].astype(np.float32) + 0.5) * k[..., None]
    if m.group(1) == b"+":
        img = img[::-1]
    if m.group(3) == b"-":
        img = img[:, ::-1]
    return img.astype(np.float32)


def read_panorama(path):
    """the picture as linear light, float32 (h, w, 3)"""
    ext = os.path.splitext(path)[1].lower()
    if ext == ".hdr":
        return _read_hdr(path)
    if ext == ".exr":
        os.environ.setdefault("OPENCV_IO_ENABLE_OPENEXR", "1")
        try:
            import cv2
        except ImportError:
            raise ValueError(".exr needs OpenCV (pip install opencv-python) - or save the sky as .hdr")
        img = cv2.imread(path, cv2.IMREAD_UNCHANGED)
        if img is None:
            raise ValueError("cannot read %s" % path)
        return np.ascontiguousarray(img[..., 2::-1]).astype(np.float32)
    from PIL import Image
    img = np.asarray(Image.open(path).convert("RGB"), np.float32) / 255.0
    return img ** 2.2


def sky_rgba(path):
    """the game sky picture (RGBA uint8, 2048 x 512): colour x brightness of the panorama's upper half"""
    from PIL import Image
    pano = read_panorama(path)
    top = pano[:pano.shape[0] // 2]
    top = np.stack([np.asarray(Image.fromarray(top[..., c]).resize((W, H), Image.BILINEAR)) for c in range(3)], -1)
    g = np.maximum(top, 0) ** (1 / 2.2)
    peak = g.max(-1)
    g /= max(float(np.percentile(peak, 99.0)), 1e-6)             # the brightest 1 % (the sun) -> full brightness
    a = np.clip(g.max(-1), 1 / 255.0, 1.0)
    rgb = np.clip(g / a[..., None], 0, 1)
    return np.dstack([rgb * 255, a * 255]).round().astype(np.uint8)


def texture(header, path):
    """the .texture file: the game sky's 0x54-byte header + our DXT5 picture"""
    from PIL import Image
    buf = io.BytesIO()
    Image.fromarray(sky_rgba(path), "RGBA").save(buf, "DDS", pixel_format="DXT5")
    data = buf.getvalue()[128:128 + W * H]
    assert len(data) == W * H, len(data)
    return header[:0x54] + data


def preview(path, width=420):
    """small RGB picture of what the sky picture will hold (colour x brightness)"""
    from PIL import Image
    a = sky_rgba(path).astype(np.float32) / 255.0
    rgb = a[..., :3] * a[..., 3:] ** 0.6
    im = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")
    return im.resize((width, width * H // W), Image.LANCZOS)


def preset_preview(city_obj, width=420):
    """small RGB picture of a game city's sky (lighting.PRESETS city object -> its sky texture)"""
    import sys
    from PIL import Image
    here = os.path.dirname(os.path.abspath(__file__))
    for p in (os.path.join(here, ".."), os.path.join(here, "..", "kslf"), os.path.join(here, "..", "pak")):
        if p not in sys.path:
            sys.path.insert(0, p)
    import dump
    import lighting
    import pak
    import paths
    F = dump.File(os.path.join(paths.UNPACKED, "xt", "amax.bin"))
    old = record_sky(F.d, F.O[lighting.ls_object(F, city_obj)])[0]
    R = pak.Pak(paths.MODEL_PAK)
    d = R.read(R.find("\\" + old.replace("/", "\\")))
    hdr = struct.pack("<4sI", b"DDS ", 124) + struct.pack("<6I", 0x1007 | 0x80000, H, W, W * H, 0, 1) + b"\0" * 44
    hdr += struct.pack("<2I4s5I", 32, 4, b"DXT5", 0, 0, 0, 0, 0) + struct.pack("<5I", 0x1000, 0, 0, 0, 0)
    a = np.asarray(Image.open(io.BytesIO(hdr + d[0x54:0x54 + W * H])).convert("RGBA"), np.float32) / 255.0
    rgb = a[..., :3] * a[..., 3:] ** 0.6
    im = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB")
    return im.resize((width, width * H // W), Image.LANCZOS)


def own_name(old, li):
    """our sky path for location li, as long as the preset's (old = "graphics/textures/skies/t02c.texture")"""
    stem = old[len(SKY_DIR):-len(".texture")]
    return SKY_DIR + ("z%0*d" % (len(stem) - 1, li))[:len(stem)] + ".texture"


def record_sky(amax, obj):
    """(the sky path of a LightingSettings object, its pool offset in the file, the pool) - obj from dump.File.O"""
    b = obj["blocks"][0]["offset"]
    at = struct.unpack_from("<I", amax, b + SKY_OFF)[0]
    pool = obj["pool"]
    return bytes(pool[at:pool.index(b"\0", at)]).decode("latin1"), obj["data"] + obj["dsize"], pool


def set_record_sky(amax, obj, new):
    """amax with the object's sky path replaced by `new` (same length: the pool is re-scrambled in place)"""
    import kslf
    old, at, pool = record_sky(amax, obj)
    assert len(new) == len(old), (old, new)
    plain = bytearray(pool)
    i = struct.unpack_from("<I", amax, obj["blocks"][0]["offset"] + SKY_OFF)[0]
    plain[i:i + len(new)] = new.encode("latin1")
    out = bytearray(amax)
    out[at:at + len(plain)] = kslf.scramble(bytes(plain))
    return bytes(out)

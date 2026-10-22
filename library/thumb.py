"""
3D PICTURES of objects for the object library (hover an object = its picture): a small software renderer (numpy +
PIL, no OpenGL): textured triangles, z-buffer, sun light, seen from the front-left above like the 3D tab, drawn 2x
bigger and shrunk (smooth edges). Input = the 3D tab's material list ({"pos" (n*3, 3) model space, "uv", "image"}).
The view is the 3D tab's (game z drawn mirrored), so signs read the same way as there and in the game.
"""
import math
import time

import numpy as np

BG = (11, 20, 38)
SIZE = 168
YAW, PITCH = math.radians(-35.0), math.radians(24.0)
SUN = np.array((0.45, 0.8, 0.4)) / np.linalg.norm((0.45, 0.8, 0.4))


def render(mats, size=SIZE, bg=BG, seconds=None):
    """PIL picture (size x size) of the model, or None if it has no triangles or takes longer than `seconds`
    (then there is simply no picture)"""
    end = time.time() + seconds if seconds else None
    from PIL import Image
    mats = [m for m in mats if m is not None and len(m["pos"])]
    if not mats:
        return None
    ss = 2
    W = size * ss
    allp = np.concatenate([m["pos"] for m in mats]).astype(np.float64)
    allp[:, 2] *= -1                                     # as the 3D tab draws it
    lo, hi = allp.min(axis=0), allp.max(axis=0)
    c = (lo + hi) / 2
    cy, sy, cp, sp = math.cos(YAW), math.sin(YAW), math.cos(PITCH), math.sin(PITCH)
    # camera axes: right, up, toward the viewer (orthographic)
    right = np.array((cy, 0.0, -sy))
    fwd = np.array((sy * cp, sp, cy * cp))
    up = np.cross(fwd, right)
    R = np.stack([right, up, fwd])

    def proj(p):
        q = (p - c) @ R.T
        return q
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
    qc = proj(corners)
    span = max(qc[:, 0].max() - qc[:, 0].min(), qc[:, 1].max() - qc[:, 1].min(), 1e-6)
    scale = W * 0.86 / span
    ox = W / 2 - (qc[:, 0].max() + qc[:, 0].min()) / 2 * scale
    oy = W / 2 + (qc[:, 1].max() + qc[:, 1].min()) / 2 * scale
    img = np.empty((W, W, 3), np.float32)
    img[:] = bg
    zbuf = np.full((W, W), -np.inf, np.float32)
    for m in mats:
        p = m["pos"].astype(np.float64).copy()
        p[:, 2] *= -1
        q = proj(p).reshape(-1, 3, 3)
        sx = q[:, :, 0] * scale + ox
        sy_ = oy - q[:, :, 1] * scale
        dz = q[:, :, 2]
        uv = m["uv"].reshape(-1, 3, 2).astype(np.float64)
        tex = np.asarray(m["image"].convert("RGB"), np.float32)
        th, tw = tex.shape[:2]
        t3 = p.reshape(-1, 3, 3)
        n = np.cross(t3[:, 1] - t3[:, 0], t3[:, 2] - t3[:, 0])
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
        light = 0.42 + 0.58 * np.abs(n @ SUN)
        for i in range(len(q)):
            if end is not None and i % 64 == 0 and time.time() > end:
                return None
            x0, y0, x1, y1, x2, y2 = sx[i, 0], sy_[i, 0], sx[i, 1], sy_[i, 1], sx[i, 2], sy_[i, 2]
            area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
            if abs(area) < 1e-9:
                continue
            bx0, bx1 = max(int(min(x0, x1, x2)), 0), min(int(max(x0, x1, x2)) + 1, W - 1)
            by0, by1 = max(int(min(y0, y1, y2)), 0), min(int(max(y0, y1, y2)) + 1, W - 1)
            if bx0 > bx1 or by0 > by1:
                continue
            ys, xs = np.mgrid[by0:by1 + 1, bx0:bx1 + 1]
            px, py = xs + 0.5, ys + 0.5
            w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) / area
            w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) / area
            w2 = 1.0 - w0 - w1
            inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not inside.any():
                continue
            z = w0 * dz[i, 0] + w1 * dz[i, 1] + w2 * dz[i, 2]
            yy, xx = ys[inside], xs[inside]
            zz = z[inside]
            closer = zz > zbuf[yy, xx]
            if not closer.any():
                continue
            yy, xx, zz = yy[closer], xx[closer], zz[closer]
            a, b, g = w0[inside][closer], w1[inside][closer], w2[inside][closer]
            u = a * uv[i, 0, 0] + b * uv[i, 1, 0] + g * uv[i, 2, 0]
            v = a * uv[i, 0, 1] + b * uv[i, 1, 1] + g * uv[i, 2, 1]
            tx = (np.mod(u, 1.0) * tw).astype(int).clip(0, tw - 1)
            ty = (np.mod(v, 1.0) * th).astype(int).clip(0, th - 1)
            zbuf[yy, xx] = zz
            img[yy, xx] = tex[ty, tx] * light[i]
    out = Image.fromarray(img.clip(0, 255).astype(np.uint8))
    return out.resize((size, size), Image.LANCZOS)

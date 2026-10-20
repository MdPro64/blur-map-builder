"""
OUR START CAMERAS (the fly-by before the countdown): .cycle camera animations made for each map.

The game's intro cameras (Route VistaCameraSequence / PackCameraSequence* -> AnimatedCameraSequence {Animation,
StageTime ...} -> CycleResourceDef "animations/cameras/<name>" -> root.pak <name>.cycle) are animations in WORLD
coordinates (x, y up, z), made for their own track: on our maps LA River 6's flew somewhere random, and with no
sequence at all the start + countdown cameras broke (2026-10-20). So every map gets its own two: a VISTA shot (flies
in high over the start line) and a PACK shot (low, along the grid).

.cycle (version 15) - the common 3-bone layout of the game's camera files (AftkRoot, camera, camera.fov), decoded
2026-10-20 from the game's files + the exe:
  header (0xCD bytes, TEMPLATE_HEAD: two identity transforms, u32 frames at 0x5C, f32 last frame at 0x60, f32 fps
  30 at 0x64, the 3 bone names, the track layout (camera position 0x41, rotation 0x83, fov 0x40), the root's
  constant position (0,0,0) + rotation), then u16 2 (chunks), the chunk table 10 x u16 {0, B + 0x30, 0, 0, 0,
  last frame, B, last frame, 0, 0} (B = table_size(): the room the game makes for the keys), u16 0x30, data:
    first frame: position (3 f32), fov (f32), rotation (6 bytes)
    u32 position keys, u16 fov keys, u16 rotation keys, u32 0
    3 frame lists: u8 count + u8 frame numbers (1 .. last-1) - positions, fovs, rotations
    the position keys (3 f32 each), the fov keys (f32), the rotation keys (6 bytes)
    last frame: position, fov, rotation
  Rotation = 48-bit quaternion (exe 0x829010): V = w0 << 32 | w1 << 16 | w2 of the three u16; bit 0 = sign of the
  largest part, bits 1-2 = its index (order x, y, z, w), bits 3 / 18 / 33 = the other three (15 bits:
  (v * 2 / 32767 - 1) * sqrt(0.5)) in slots SLOTS[index]. The camera looks along its +z, +y up. fov = radians.
"""
import math
import struct

TEMPLATE_HEAD = bytes.fromhex(
    "0f000000000000000000000000000000000000000000803f0000000000000000000000000000803f0000803f0000803f00000000"
    "00000000000000000000803f0000000000000000000000000000803f0000803f0000803f00000000690000000000d0420000f041"
    "0300000000000303000000080000004166746b526f6f740600000063616d6572610a00000063616d6572612e666f760200030001"
    "00000001000100000001000100000000000100000041008300400012000000000000000000000000000000fe7ffdfffeff")
FPS = 30.0
R = 0.70710677
SLOTS = ((1, 2, 3), (2, 3, 0), (3, 0, 1), (0, 1, 2))

# the two shots (frames at 30 fps; StageTime = how long the game shows each, as LA River 6's)
VISTA_FRAMES, VISTA_STAGE = 180, 4.5
PACK_FRAMES, PACK_STAGE = 105, 3.5
FOV = 0.9                   # radians (the game's pack shots: 0.895)


def quat_decode(rb):
    w0, w1, w2 = struct.unpack("<3H", rb)
    v = (w0 << 32) | (w1 << 16) | w2
    sign, idx = v & 1, (v >> 1) & 3
    vals = [(((v >> sh) & 0x7FFF) * 2.0 / 32767 - 1.0) * R for sh in (3, 18, 33)]
    q = [0.0] * 4
    for s, x in zip(SLOTS[idx], vals):
        q[s] = x
    big = math.sqrt(max(0.0, 1.0 - sum(x * x for x in vals)))
    q[idx] = -big if sign else big
    return q


def quat_encode(q):
    """6 bytes of quaternion q = (x, y, z, w)"""
    n = math.sqrt(sum(x * x for x in q)) or 1.0
    q = [x / n for x in q]
    idx = max(range(4), key=lambda k: abs(q[k]))
    v = (1 if q[idx] < 0 else 0) | (idx << 1)
    for s, sh in zip(SLOTS[idx], (3, 18, 33)):
        x = max(-R, min(R, q[s]))
        v |= max(0, min(0x7FFF, int(round((x / R + 1.0) * 32767 / 2.0)))) << sh
    return struct.pack("<3H", (v >> 32) & 0xFFFF, (v >> 16) & 0xFFFF, v & 0xFFFF)


def look_quat(eye, target):
    """(x, y, z, w) turning the camera's +z towards target, no roll (+y stays up)"""
    fx, fy, fz = (target[k] - eye[k] for k in range(3))
    ln = math.sqrt(fx * fx + fy * fy + fz * fz) or 1.0
    yaw = math.atan2(fx, fz)
    pitch = math.asin(max(-1.0, min(1.0, fy / ln)))
    # q = yaw about y, then -pitch about x (turning +z up towards +y)
    cy, sy = math.cos(yaw / 2), math.sin(yaw / 2)
    cp, sp = math.cos(-pitch / 2), math.sin(-pitch / 2)
    # (w, x, y, z) of qy * qx
    w = cy * cp
    x = cy * sp
    y = sy * cp
    z = -sy * sp
    return (x, y, z, w)


def rotate(q, v):
    """vector v turned by quaternion q = (x, y, z, w)"""
    x, y, z, w = q
    tx = 2 * (y * v[2] - z * v[1]); ty = 2 * (z * v[0] - x * v[2]); tz = 2 * (x * v[1] - y * v[0])
    return (v[0] + w * tx + (y * tz - z * ty), v[1] + w * ty + (z * tx - x * tz), v[2] + w * tz + (x * ty - y * tx))


def table_size(npos, nfov, nrot):
    """the chunk table's size value B (the room the game makes for the keys; matches all 99 game files)"""
    return (37 + 13 * npos + 5 * nfov + (6 * nrot + 3) // 4 * 4 + nrot + 15) // 16 * 16


def write_cycle(frames, rot_every=2, fov_keys=(), fps=FPS):
    """.cycle bytes of a camera animation: frames = [(position (x, y, z), quaternion (x, y, z, w), fov)] one per
    frame (2..255). Positions keyed every frame, rotations every `rot_every` frames, fovs at `fov_keys` frames."""
    n = len(frames)
    assert 2 <= n <= 255, n
    last = n - 1
    inner = list(range(1, last))
    pos_f = inner
    rot_f = [f for f in inner if f % rot_every == 0]
    fov_f = [f for f in fov_keys if 0 < f < last] or [last // 2]
    body = bytearray()

    def full(f):
        p, q, fov = frames[f]
        return struct.pack("<3ff", p[0], p[1], p[2], fov) + quat_encode(q)
    body += full(0)
    body += struct.pack("<IHHI", len(pos_f), len(fov_f), len(rot_f), 0)
    for lst in (pos_f, fov_f, rot_f):
        body += bytes([len(lst)] + lst)
    for f in pos_f:
        body += struct.pack("<3f", *frames[f][0])
    for f in fov_f:
        body += struct.pack("<f", frames[f][2])
    for f in rot_f:
        body += quat_encode(frames[f][1])
    body += full(last)
    b = table_size(len(pos_f), len(fov_f), len(rot_f))
    head = bytearray(TEMPLATE_HEAD)
    struct.pack_into("<Iff", head, 0x5C, n, float(last), fps)
    table = struct.pack("<H10HH", 2, 0, b + 0x30, 0, 0, 0, last, b, last, 0, 0, 0x30)
    return bytes(head) + table + bytes(body)


# ---------------------------------------------------------------------------------------------- the two shots
def _smooth(t):
    return t * t * (3 - 2 * t)


def _road_point(road, s, lat=0.0, up=0.0, ground_y=0.0):
    """world (x, y, z) of road distance s (kept on the road; a lap track wraps), lat m sideways, up m above it"""
    if getattr(road, "closed", False):
        s %= road.length
    else:
        s = max(0.0, min(road.length, s))
    x, z, _ = road.at(s, lat)
    return (x, ground_y + road.h_at(s) + up, z)


def vista_frames(road, s0, ground_y, side=1):
    """the fly-in: from high above the road 120 m past the start line down to beside the line, looking at the
    middle of the grid (s0 = the start line's distance along the road)"""
    w = road.w_at(s0)
    out = []
    for f in range(VISTA_FRAMES):
        t = _smooth(f / (VISTA_FRAMES - 1))
        s = s0 + 120.0 - 135.0 * t
        lat = side * (w / 2 + 25.0 - 12.0 * t)
        up = 34.0 - 24.0 * t
        eye = _road_point(road, s, lat, up, ground_y)
        target = _road_point(road, s0 - 30.0 - 15.0 * t, 0.0, 1.0, ground_y)
        out.append((eye, look_quat(eye, target), FOV))
    return out


def pack_frames(road, s0, ground_y, side=-1):
    """low along the grid: from just in front of the line back along the cars, looking across them"""
    w = road.w_at(s0)
    lat = side * min(w / 2 - 3.0, 14.0)
    out = []
    for f in range(PACK_FRAMES):
        t = _smooth(f / (PACK_FRAMES - 1))
        eye = _road_point(road, s0 + 4.0 - 34.0 * t, lat, 1.7, ground_y)
        target = _road_point(road, s0 - 22.0 - 30.0 * t, -lat * 0.3, 0.8, ground_y)
        out.append((eye, look_quat(eye, target), FOV))
    return out


# ---------------------------------------------------------------------------------------------- the user's own
# m["start_cams"] = {"vista": [view, ...], "pack": [view, ...]}, view = {"eye": [x, y, z], "look": [x, y, z]} (world,
# set in the designer's 3D tab: "Add view" = the 3D view's camera there). A shot without views = the automatic one.
SHOTS = (("vista", "Fly-in", VISTA_FRAMES, VISTA_STAGE), ("pack", "Grid shot", PACK_FRAMES, PACK_STAGE))
MAX_VIEWS = 12


def _catmull(p0, p1, p2, p3, t):
    t2, t3 = t * t, t * t * t
    return tuple(0.5 * (2 * p1[k] + (p2[k] - p0[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2 +
                        (3 * p1[k] - p0[k] - 3 * p2[k] + p3[k]) * t3) for k in range(3))


def keyed_frames(views, n):
    """n frames flying smoothly through the user's views (spread evenly over the shot; one view = a still shot)"""
    eyes = [tuple(v["eye"]) for v in views]
    looks = [tuple(v["look"]) for v in views]
    out = []
    for f in range(n):
        if len(views) == 1:
            eye, look = eyes[0], looks[0]
        else:
            u = _smooth(f / (n - 1)) * (len(views) - 1)
            i = min(int(u), len(views) - 2)
            t = u - i

            def at(ps):
                return _catmull(ps[max(i - 1, 0)], ps[i], ps[i + 1], ps[min(i + 2, len(ps) - 1)], t)
            eye, look = at(eyes), at(looks)
        out.append((eye, look_quat(eye, look), FOV))
    return out


def shot_frames(road, s0, ground_y, m=None):
    """{"vista": frames, "pack": frames} of one map: the user's views (m["start_cams"]) or the automatic shots"""
    own = (m or {}).get("start_cams") or {}
    auto = {"vista": vista_frames, "pack": pack_frames}
    out = {}
    for shot, _, n, _ in SHOTS:
        views = [v for v in own.get(shot) or [] if v.get("eye") and v.get("look")]
        out[shot] = keyed_frames(views, n) if views else auto[shot](road, s0, ground_y)
    return out


def play_frames(road, s0, ground_y, m=None):
    """what the game shows before the countdown: the fly-in for its stage time, then the grid shot"""
    fr = shot_frames(road, s0, ground_y, m)
    return fr["vista"][:int(VISTA_STAGE * FPS)] + fr["pack"][:int(PACK_STAGE * FPS)]


def cycles_for_map(road, s0, ground_y, m=None):
    """{"vista": bytes, "pack": bytes} of one map"""
    return {shot: write_cycle(frames) for shot, frames in shot_frames(road, s0, ground_y, m).items()}

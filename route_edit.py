"""
Experiments that change the loaded track's routes in the game's memory (nothing on disk changes).

  python route_edit.py info              list the routes of the loaded track
  python route_edit.py reverse-logic     flip the main race route backwards (backup saved first)
  python route_edit.py reverse-all       flip every route, then turn the car around
  python route_edit.py turn-around       Hand of God the car to face the other way
  python route_edit.py grid-here         move the start grid to where your car is (lost on restart!)
  python route_edit.py grid-watch [--key] put the start grid at your car (or the F9 spot) on the next restart
  python route_edit.py start-here        F9 = move the start line in front of your car + grid behind it
  python route_edit.py restore           put back everything from the last backup

Backups go to map_builder/backups/<track>.json (only the bytes we changed).
"""
import json
import os
import struct
import sys

import track_map as tm

HERE = tm.HERE
BACKUP_DIR = os.path.join(HERE, "backups")


def splines(g):
    t = tm.find_track_data(g)
    if not t:
        sys.exit("No track found - are you in a race?")
    items, count = g.u32(t + 8), g.u32(t + 0x10)
    for i in range(count):
        s = g.u32(items + 4 * i)
        head = g.read(s, 0x50) if s else None
        if head:
            ptr, n = struct.unpack_from("<II", head, 0x48)
            yield i, s, head[0x1C], ptr, n


def backup_path(g):
    return os.path.join(BACKUP_DIR, tm.track_id(tm.read_track(g)) + ".json")


def save_backup(g, chunks):
    """chunks: list of (address, bytes) we are about to overwrite."""
    os.makedirs(BACKUP_DIR, exist_ok=True)
    path = backup_path(g)
    if os.path.exists(path):
        print("A backup already exists for this track (keeping the original one).")
        return
    with open(path, "w") as f:
        json.dump([[a, data.hex()] for a, data in chunks], f)
    print("Backup saved:", path)


def cmd_info(g):
    for i, s, flags, ptr, n in splines(g):
        print("%2d  spline 0x%08X  flags %02X %-9s points %5d at 0x%08X" % (i, s, flags, tm.kind_of(flags)[0], n, ptr))


def cmd_reverse_logic(g):
    for i, s, flags, ptr, n in splines(g):
        if flags & 0x20:
            raw = g.read(ptr, n * tm.POINT_SIZE)
            save_backup(g, [(ptr, raw)])
            g.write(ptr, reverse_points(raw, n))
            print("Main route (%d points) is now reversed in memory." % n)
            return
    print("No main route found.")


def reverse_points(raw, n):
    pts = [list(struct.unpack_from("<11f", raw, k * tm.POINT_SIZE)) for k in range(n)]
    pts.reverse()
    for p in pts:                      # left and right swap when you drive the other way
        p[2], p[3] = -p[2], -p[3]
    return b"".join(struct.pack("<11f", *p) for p in pts)


PROGRESS_HELPER = 0x1134D88   # [helper+0x2C] = race-progress system, racers at [sys+0x4C]


def trackers(g):
    """Each racer's route tracker (it remembers the nearest point on every route + the last good spot)."""
    sysobj = g.u32(PROGRESS_HELPER + 0x2C)
    first, last = g.u32(sysobj + 0x4C), g.u32(sysobj + 0x50)
    for a in range(first, last, 4) if first and last and last - first < 0x400 else []:
        racer = g.u32(a)
        t = g.u32(racer + 0x34) if racer else 0
        if t:
            yield t


def flip_trackers(g, counts):
    """After reversing routes: point i becomes n-1-i in every tracker, and the remembered direction flips.
    counts = {spline address: number of points}. Doing it twice undoes it."""
    for t in trackers(g):
        for arr_off in (0x80, 0x8C):
            arr, n = g.u32(t + arr_off), g.u32(t + arr_off + 4)
            for k in range(n if n < 1000 else 0):
                e = arr + k * 0x24
                spline, idx = g.u32(e), struct.unpack("<h", g.read(e + 0x20, 2))[0]
                if spline in counts and 0 <= idx < counts[spline]:
                    g.write(e + 0x20, struct.pack("<h", counts[spline] - 1 - idx))
        node = g.u32(t + 8)
        if node:
            g.write(t + 0x28, struct.pack("<i", struct.unpack("<h", g.read(node + 0x20, 2))[0]))
        for off in (0x18, 0x20, 0x5C, 0x64):        # cached facing x / z, and its copy
            v = struct.unpack("<f", g.read(t + off, 4))[0]
            g.write(t + off, struct.pack("<f", -v))
        print("Tracker 0x%08X updated." % t)


def cmd_reverse_all(g):
    """Flip every route of the track (points only; the per-point AI lists at +0x60 are left alone)."""
    chunks, counts = [], {}
    for i, s, flags, ptr, n in splines(g):
        raw = g.read(ptr, n * tm.POINT_SIZE)
        if raw:
            chunks.append((ptr, raw))
            counts[s] = n
    save_backup(g, chunks)
    for ptr, raw in chunks:
        g.write(ptr, reverse_points(raw, len(raw) // tm.POINT_SIZE))
    flip_trackers(g, counts)
    print("All %d routes are now reversed in memory." % len(chunks))
    cmd_turn_around(g)


def cmd_turn_around(g):
    """Use the game's Hand of God to put the car where it is, facing the other way."""
    import hog
    phys = tm.bt.find_physics(g)
    pose = tm.bt.read_pose(g, phys) if phys else None
    if not pose:
        sys.exit("Can't find your car.")
    r = pose["rot"]
    flipped = [-r[0], r[1], -r[2], r[3], r[4], r[5], -r[6], r[7], -r[8]]
    hog.reset_car_to(g, tm.bt.find_racer(g), pose["pos"], flipped)
    print("Car turned around.")


def cmd_grid_here(g):
    """Move the whole start grid to where the car is: 2 columns, rows 8 m apart, facing the car's way.
    Takes effect when the race is restarted. Start slot = 0x44 bytes: +0 index, +4 rotation rows
    (side, up, forward), +0x28 position, +0x34 position copy."""
    grid = tm.find_grid(g)
    n = g.u32(grid + tm.GRID_COUNT) if grid else 0
    pose = tm.bt.read_pose(g, tm.bt.find_physics(g))
    if not n or not pose:
        sys.exit("Can't find the start grid or your car - are you in a race?")
    raw = g.read(grid, n * tm.GRID_SIZE)
    save_backup(g, [(grid, raw)])
    x, y, z = pose["pos"]
    fx, fz = pose["rot"][6], pose["rot"][8]
    length = (fx * fx + fz * fz) ** 0.5 or 1.0
    fx, fz = fx / length, fz / length
    sx, sz = fz, -fx                        # sideways, same convention as the game's slots
    for k in range(n):
        row, col = k // 2, k % 2
        back, across = 8.0 * row, (-3.0 if col == 0 else 3.0)
        px, pz = x - fx * back + sx * across, z - fz * back + sz * across
        slot = struct.pack("<I9f3f3f", k, sx, 0.0, sz, 0.0, 1.0, 0.0, fx, 0.0, fz,
                           px, y + 0.5, pz, px, y + 0.5, pz)
        g.write(grid + k * tm.GRID_SIZE, slot)
    print("Start grid (%d slots) moved to your car. Restart the race to try it." % n)


def grid_layout(pose, n):
    """n start transforms (9 rotation floats + position) in 2 columns behind the car."""
    x, y, z = pose["pos"]
    fx, fz = pose["rot"][6], pose["rot"][8]
    length = (fx * fx + fz * fz) ** 0.5 or 1.0
    fx, fz = fx / length, fz / length
    sx, sz = fz, -fx
    out = []
    for k in range(n):
        back, across = 8.0 * (k // 2), (-3.0 if k % 2 == 0 else 3.0)
        px, pz = x - fx * back + sx * across, z - fz * back + sz * across
        out.append((sx, 0.0, sz, 0.0, 1.0, 0.0, fx, 0.0, fz, px, y + 0.5, pz))
    return out


def move_start_line(g, pose, ahead=8.0):
    """Rewrite the 'start' spline (flags 0x02) as a straight piece starting `ahead` m in front of the
    car and running forward. Keeps its point count. Returns (address, original bytes) for backup."""
    for i, s, flags, ptr, n in splines(g):
        if flags & 0x02 and not flags & 0x20:
            break
    else:
        sys.exit("This track has no start piece.")
    head_raw = g.read(s, 0x18)
    raw = g.read(ptr, n * tm.POINT_SIZE)
    first = struct.unpack_from("<11f", raw, 0)
    x, y, z = pose["pos"]
    fx, fz = pose["rot"][6], pose["rot"][8]
    length = (fx * fx + fz * fz) ** 0.5 or 1.0
    fx, fz = fx / length, fz / length
    sx, sz = tm.side_of(fx, fz)
    pts, xs, zs = [], [], []
    for k in range(n):
        d = ahead + k * tm.STEP
        px, pz = x + fx * d, z + fz * d
        xs.append(px)
        zs.append(pz)
        # keep the original point's last 4 fields: field 8 is a pointer the game fills in at load
        # time (FUN_00b0a120 reads [field8]+0x5C); writing 0 there crashed the game
        tail = raw[k * tm.POINT_SIZE + 28:(k + 1) * tm.POINT_SIZE]
        pts.append(struct.pack("<7f", px, pz, sx, sz, first[4], y + 5.0, y + 5.0) + tail)
    margin = first[4]
    bbox = struct.pack("<6f", min(xs) - margin, y - 5.0, min(zs) - margin, max(xs) + margin, y + 15.0, max(zs) + margin)
    g.write(ptr, b"".join(pts))
    g.write(s, bbox)
    print("Start line moved in front of your car (%d points)." % n)
    return [(ptr, raw), (s, head_raw)]


def rotate_logic(g, pose, ahead=8.0):
    """Renumber the main race route (Logic) so point 0 is the point nearest to `ahead` m in front of
    the car. The road keeps its shape and direction. The per-point lists (+0x54 links, 12 bytes each;
    +0x60.. 8 groups x 11 byte-channels) and every racer's tracker index are shifted the same way."""
    for i, s, flags, ptr, n in splines(g):
        if flags & 0x20:
            break
    else:
        sys.exit("No main route found.")
    raw = g.read(ptr, n * tm.POINT_SIZE)
    pts = [raw[k * tm.POINT_SIZE:(k + 1) * tm.POINT_SIZE] for k in range(n)]
    x, _, z = pose["pos"]
    fx, fz = pose["rot"][6], pose["rot"][8]
    tx, tz = x + fx * ahead, z + fz * ahead
    xy = [struct.unpack_from("<2f", p) for p in pts]
    loop = (xy[0][0] - xy[-1][0]) ** 2 + (xy[0][1] - xy[-1][1]) ** 2 < 1.0   # last point repeats the first
    m = n - 1 if loop else n
    shift = min(range(m), key=lambda k: (xy[k][0] - tx) ** 2 + (xy[k][1] - tz) ** 2)

    def rotated(items):
        body = items[:m]
        body = body[shift:] + body[:shift]
        return body + ([body[0]] if loop else items[m:])

    g.write(ptr, b"".join(rotated(pts)))
    links, nlinks = g.u32(s + 0x54), g.u32(s + 0x58)
    if links and nlinks == n:
        lraw = g.read(links, n * 12)
        g.write(links, b"".join(rotated([lraw[k * 12:(k + 1) * 12] for k in range(n)])))
    for grp in range(8):
        arr, cnt = g.u32(s + 0x60 + grp * 0xC), g.u32(s + 0x64 + grp * 0xC)
        for c in range(cnt if cnt < 32 else 0):
            chan, clen = g.u32(arr + c * 0xC), g.u32(arr + c * 0xC + 4)
            if chan and clen == n:
                craw = g.read(chan, n)
                g.write(chan, bytes(rotated(list(craw))))
    for t in trackers(g):                      # every racer: point i becomes (i - shift) mod m
        for arr_off in (0x80, 0x8C):
            arr, cnt = g.u32(t + arr_off), g.u32(t + arr_off + 4)
            for k in range(cnt if cnt < 1000 else 0):
                e = arr + k * 0x24
                idx = struct.unpack("<h", g.read(e + 0x20, 2))[0]
                if g.u32(e) == s and 0 <= idx < n:
                    g.write(e + 0x20, struct.pack("<h", (idx - shift) % m))
        logic_idx = struct.unpack("<i", g.read(t + 0x2C, 4))[0]
        if 0 <= logic_idx < n:
            g.write(t + 0x2C, struct.pack("<i", (logic_idx - shift) % m))
        node = g.u32(t + 8)
        if node and g.u32(node) == s:
            g.write(t + 0x28, struct.pack("<i", struct.unpack("<h", g.read(node + 0x20, 2))[0]))
    print("Main route renumbered: old point %d is now point 0 (%d points, %s)." % (shift, n, "loop" if loop else "not a loop"))


def place_gate_on_route(g, flag, center):
    """Rewrite the gate piece with this flag (0x02 start, 0x08 split 1, 0x10 split 2) so it lies on the
    main route, centred on Logic point `center`. Copies the route's x, z, side and width, keeps the
    piece's own heights and its last 4 fields (field 8 is a runtime pointer). The game's gate test
    (FUN_00b0a460) uses the piece's point 1 as a line across the road. Returns backup chunks."""
    logic = [x for x in splines(g) if x[2] & 0x20][0]
    _, _, _, lptr, ln = logic
    lraw = g.read(lptr, ln * tm.POINT_SIZE)
    lpts = [struct.unpack_from("<11f", lraw, k * tm.POINT_SIZE) for k in range(ln)]
    loop = (lpts[0][0] - lpts[-1][0]) ** 2 + (lpts[0][1] - lpts[-1][1]) ** 2 < 1.0
    m = ln - 1 if loop else ln
    for i, s, flags, ptr, n in splines(g):
        if flags & flag and not flags & 0x20:
            break
    else:
        return []
    head_raw, raw = g.read(s, 0x18), g.read(ptr, n * tm.POINT_SIZE)
    new, xs, zs = [], [], []
    for k in range(n):
        src = lpts[(center - 1 + k) % m] if loop else lpts[min(max(center - 1 + k, 0), m - 1)]
        own = struct.unpack_from("<7f", raw, k * tm.POINT_SIZE)
        new.append(struct.pack("<7f", src[0], src[1], src[2], src[3], src[4], own[5], own[6])
                   + raw[k * tm.POINT_SIZE + 28:(k + 1) * tm.POINT_SIZE])
        xs.append(src[0])
        zs.append(src[1])
    old_min_y, old_max_y = struct.unpack_from("<f", head_raw, 4)[0], struct.unpack_from("<f", head_raw, 16)[0]
    margin = max(p[4] for p in lpts[:1]) + 10
    g.write(ptr, b"".join(new))
    g.write(s, struct.pack("<6f", min(xs) - margin, old_min_y, min(zs) - margin,
                           max(xs) + margin, old_max_y, max(zs) + margin))
    return [(ptr, raw), (s, head_raw)]


def place_gates(g, pose):
    """Start gate at the Logic point nearest the car (+8 m), split 1 and split 2 at 1/3 and 2/3 of a lap
    after it, and the lap line (T+0x14) on the start point."""
    import lap_line
    t, pts = lap_line.logic_points(g)
    x, _, z = pose["pos"]
    fx, fz = pose["rot"][6], pose["rot"][8]
    start = lap_line.nearest_point(pts, x + fx * 8, z + fz * 8)
    loop = (pts[0][0] - pts[-1][0]) ** 2 + (pts[0][1] - pts[-1][1]) ** 2 < 1.0
    m = len(pts) - 1 if loop else len(pts)
    chunks = place_gate_on_route(g, 0x02, start)
    chunks += place_gate_on_route(g, 0x08, (start + m // 3) % m)
    chunks += place_gate_on_route(g, 0x10, (start + 2 * m // 3) % m)
    split1, split2 = (start + m // 3) % m, (start + 2 * m // 3) % m
    g.write(t + lap_line.LINE, struct.pack("<i", start))
    # the tracker only wakes a gate piece up through the Logic route's per-point links, and keeps it awake
    # while the car's Logic index is <= the gate's index in TrackData (+0x14 start, +0x24 split 1, +0x2C split 2)
    g.write(t + SPLIT1_IDX, struct.pack("<i", split1))
    g.write(t + SPLIT2_IDX, struct.pack("<i", split2))
    for flag, center in ((0x02, start), (0x08, split1), (0x10, split2)):
        relink_gate(g, flag, center)
    print("Gates placed: start at route point %d, split 1 at %d, split 2 at %d (of %d)."
          % (start, split1, split2, m), flush=True)
    return chunks


SPLIT1_IDX, SPLIT2_IDX = 0x24, 0x2C     # TrackData: Logic point index of each split gate (start = +0x14)
LINK_LIST = 0x54                        # spline: per point {ptr to (spline, point idx) pairs, capacity, count}


def relink_gate(g, flag, center):
    """Move the Logic route's links to the gate piece with this flag: drop the old ones, then add
    (piece, k) on Logic point center-1+k (FUN_00b08660 wakes a sleeping piece only through these links).
    Only uses free capacity in the game's own lists, so nothing is reallocated."""
    all_sp = list(splines(g))
    logic = [x for x in all_sp if x[2] & 0x20][0]
    gate = [x for x in all_sp if x[2] & flag and not x[2] & 0x20][0]
    _, ls, _, _, ln = logic
    _, gs, _, _, gn = gate
    arr = g.u32(ls + LINK_LIST)
    lists = [struct.unpack("<3I", g.read(arr + 12 * k, 12)) for k in range(ln)]
    removed = 0
    for k, (p, cap, c) in enumerate(lists):
        if not c:
            continue
        pairs = [struct.unpack_from("<Ii", g.read(p, 8 * c), 8 * j) for j in range(c)]
        keep = [q for q in pairs if q[0] != gs]
        if len(keep) != c:
            removed += c - len(keep)
            g.write(p, b"".join(struct.pack("<Ii", *q) for q in keep))
            g.write(arr + 12 * k + 8, struct.pack("<I", len(keep)))
            lists[k] = (p, cap, len(keep))
    m = ln - 1                                    # lap track: the last point repeats point 0
    added, skipped = 0, 0
    for k in range(gn):
        pt = (center - 1 + k) % m
        p, cap, c = lists[pt]
        if p and c < cap:
            g.write(p + 8 * c, struct.pack("<Ii", gs, k))
            g.write(arr + 12 * pt + 8, struct.pack("<I", c + 1))
            added += 1
        else:
            skipped += 1
    # a full list everywhere: on 3 points around the gate line, give up the list's LAST link (a racing
    # piece that the neighbouring points also link) for the gate. One link is enough to wake the gate.
    if not added:
        for k in (0, 1, 2):
            pt = (center - 1 + k) % m
            p, cap, c = lists[pt]
            if p and c >= 2:
                g.write(p + 8 * (c - 1), struct.pack("<Ii", gs, k))
                added += 1
    print("  gate %02X: removed %d old links, added %d new (%d points had no free slot)"
          % (flag, removed, added, skipped), flush=True)


def cmd_start_here(g):
    """F9 in the game: move the start line in front of the car, then on restart put the grid behind it."""
    pose = wait_f9(g)
    if watch_and_set_grid(g, pose):
        import time
        time.sleep(1.0)                     # a restart reloads the routes: wait until loading settles
        # start gate, split 1, split 2 and the lap line (T+0x14) all follow the new start, so the
        # lap checklist (split 1 -> split 2 -> start) works around the new line
        place_gates(g, pose)


def watch_and_set_grid(g, pose):
    """Wait for the race to restart and overwrite the freshly rebuilt start grid with 2 columns behind
    `pose`, before the cars are placed on it. (Editing the grid's sources does NOT work: they are
    re-created on restart, and moving them left stuck lights around the map.)"""
    import time
    old = tm.find_grid(g)
    full = g.u32(old + tm.GRID_COUNT)          # wait until the rebuilt grid has this many slots
    print("Start spot saved. Now restart the race (waiting up to 2 minutes)...", flush=True)
    end = time.time() + 120
    for k, r in enumerate(grid_layout(pose, full)):   # put ours in now: a restart will visibly undo it
        g.write(old + k * tm.GRID_SIZE, struct.pack("<I12f", k, *r) + struct.pack("<3f", *r[9:]))
    ours = g.read(old + 0x28, 12)
    emptied = False
    while time.time() < end:
        grid = tm.find_grid(g)
        n = g.u32(grid + tm.GRID_COUNT) if grid else 0
        if grid != old or n < full or (grid and g.read(grid + 0x28, 12) != ours):
            emptied = True                # the restart cleared, replaced or refilled the grid
        if emptied and grid and n >= full:
            for k, r in enumerate(grid_layout(pose, n)):
                g.write(grid + k * tm.GRID_SIZE, struct.pack("<I12f", k, *r) + struct.pack("<3f", *r[9:]))
            print("Rebuilt grid found and overwritten (%d slots)." % n)
            return True
        time.sleep(0.0002)
    print("No restart seen.")
    return False


def wait_f9(g):
    import time
    keys = tm.bt.Keys()
    print("Drive to your start spot and press F9 in the game...", flush=True)
    while not (keys.pressed(0x78) and g.has_focus()):
        time.sleep(0.01)
    tm.bt.beep(1200)
    pose = tm.bt.read_pose(g, tm.bt.find_physics(g))
    if not pose:
        sys.exit("Can't find your car - are you in a race?")
    return pose


def cmd_grid_watch(g):
    """Pick a start spot (your car now, or F9 with --key), then put the grid there on the next restart."""
    pose = wait_f9(g) if "--key" in sys.argv else tm.bt.read_pose(g, tm.bt.find_physics(g))
    watch_and_set_grid(g, pose)


def cmd_restore(g):
    # find the backup by memory address (a reversed route has a different first point / track id)
    all_splines = list(splines(g))
    loaded = {ptr for _, _, _, ptr, _ in all_splines} | {s for _, s, _, _, _ in all_splines}
    for name in os.listdir(BACKUP_DIR) if os.path.isdir(BACKUP_DIR) else []:
        path = os.path.join(BACKUP_DIR, name)
        with open(path) as f:
            chunks = json.load(f)
        if all(addr in loaded for addr, _ in chunks):
            for addr, data in chunks:
                g.write(addr, bytes.fromhex(data))
            if len(chunks) == len(all_splines):   # a reverse-all: flip the trackers back as well
                flip_trackers(g, {s: n for _, s, _, ptr, n in splines(g) if ptr in {a for a, _ in chunks}})
            os.remove(path)
            print("Restored the original route.")
            return
    sys.exit("No backup for the loaded track.")


if __name__ == "__main__":
    cmds = {"info": cmd_info, "reverse-logic": cmd_reverse_logic, "reverse-all": cmd_reverse_all,
            "turn-around": cmd_turn_around, "grid-here": cmd_grid_here,
            "grid-watch": cmd_grid_watch, "start-here": cmd_start_here,
            "restore": cmd_restore}
    if len(sys.argv) < 2 or sys.argv[1] not in cmds:
        sys.exit(__doc__)
    cmds[sys.argv[1]](tm.bt.Game())


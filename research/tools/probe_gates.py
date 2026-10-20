"""Read-only: show the gate pieces (start/split1/split2) and what YOUR tracker knows about them."""
import struct, sys
sys.path.insert(0, r"E:\blur - bak\map_builder")
import track_map as tm, route_edit as re_
g = tm.bt.Game()
names = {0x02: "start", 0x08: "split1", 0x10: "split2", 0x20: "LOGIC"}
sp = {}
for i, s, flags, ptr, n in re_.splines(g):
    sp[s] = (i, flags, ptr, n)
    if flags & 0x3A:
        p1 = struct.unpack("<11f", g.read(ptr + tm.POINT_SIZE, tm.POINT_SIZE))
        bbox = struct.unpack("<6f", g.read(s, 0x18))
        print("spline %2d %08X flags %02X %-6s n=%4d pt1=(%.0f,%.0f) side=(%.2f,%.2f) w=%.1f bbox x %.0f..%.0f z %.0f..%.0f"
              % (i, s, flags, names.get(flags & 0x3A, "?"), n, p1[0], p1[1], p1[2], p1[3], p1[4], bbox[0], bbox[3], bbox[2], bbox[5]))
t = next(re_.trackers(g))
print("tracker %08X  +28=%d +2C=%d" % (t, struct.unpack("<h", g.read(t + 0x28, 2))[0], g.u32(t + 0x2C)))
for off, lab in [(0x3C, "Logic"), (0x40, "Start"), (0x44, "Split1"), (0x48, "Split2"), (0x4C, "Finish")]:
    print("  node %s +%X = %08X" % (lab, off, g.u32(t + off)))
for arr, cnt in [(0x80, 0x84), (0x8C, 0x90)]:
    base, c = g.u32(t + arr), g.u32(t + cnt)
    print(" array +%X count %d at %08X" % (arr, c, base))
    for k in range(min(c, 64)):
        e = g.read(base + k * 0x24, 0x24)
        s = struct.unpack_from("<I", e, 0)[0]
        idx = struct.unpack_from("<h", e, 0x20)[0]
        info = sp.get(s)
        fl = info[1] if info else -1
        if fl & 0x3A or k < 3:
            print("   [%2d] %08X spl#%s flags %02X idx %d active %d raw %s" % (k, base + k*0x24, info[0] if info else "?", fl & 0xFF, idx, e[0x23], e[4:0x20].hex()))

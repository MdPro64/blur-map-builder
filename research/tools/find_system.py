"""Walk Blur's system registry (FUN_00afe500) and list the objects registered under a type id.

world = [0x11B3778] (a pointer); per-type bucket at world + 0x2C4 + type*0x20 (a list of nodes);
each node's +0xC indexes a second table at world + 0x924 + idx*0x20 (a map: key -> object at node+0x10).
usage: python find_system.py [type_hex]   (default 1A = TrackDataSystem)
"""
import struct, sys
sys.path.insert(0, r"E:\blur - bak\trainer")
import blur_trainer as b

g = b.Game()
u = g.u32
TYPE = int(sys.argv[1], 16) if len(sys.argv) > 1 else 0x1A
world = u(0x11B3778)
print("world object 0x%08X" % world)
bucket = world + 0x2C4 + TYPE * 0x20
print("bucket @0x%08X:" % bucket, " ".join("%08X" % u(bucket + 4 * i) for i in range(8)))


def walk_list(head, limit=50):
    """MSVC std::list: head node -> [next, prev, value...]."""
    out, node = [], u(head)
    while node and node != head and len(out) < limit:
        out.append(node)
        node = u(node)
    return out


def walk_tree(header, limit=200):
    """MSVC std::map: header node; root = [header+4]; nodes [left, parent, right, ..., isnil byte]."""
    out, stack, seen = [], [u(header + 4)], set()
    while stack and len(out) < limit:
        n = stack.pop()
        if not n or n == header or n in seen:
            continue
        seen.add(n)
        out.append(n)
        stack += [u(n), u(n + 8)]
    return out


# try the bucket words as possible list heads
for i in range(8):
    head = u(bucket + 4 * i)
    if not head:
        continue
    nodes = walk_list(head)
    if 0 < len(nodes) < 50:
        print(" word %d looks like a list with %d nodes" % (i, len(nodes)))
        for n in nodes:
            idx = u(n + 8 + 0xC) if False else None
            vals = [u(n + 4 * k) for k in range(8)]
            print("   node %08X: %s" % (n, " ".join("%08X" % v for v in vals)))


"""Compare two finished builds file by file (the packs + the level files). Numbers inside models may differ in the last
bit of a float; those are counted as "close".  python tools\\compare_build.py DIR_A DIR_B
Each DIR holds gamedata_track.pak, root_track.pak and optionally levels\\ and flat_earth\\ (see run_py_build.sh)."""
import hashlib
import os
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.environ["BMB_GAME"] = os.path.join(ROOT, "scratch", "no_game")
sys.dont_write_bytecode = True
sys.path[:0] = [os.path.join(os.path.dirname(ROOT), "map_builder", "pak")]
import pak  # noqa: E402


def how_close(a, b):
    """(bytes that differ, u32 words that differ by more than a float rounding step)"""
    if len(a) != len(b):
        return None
    diff = sum(1 for x, y in zip(a, b) if x != y) if len(a) < 2000000 else -1
    bad = 0
    if diff:
        n = len(a) // 4
        for (x,), (y,) in zip(struct.iter_unpack("<f", a[:n * 4]), struct.iter_unpack("<f", b[:n * 4])):
            if x != y and not (x != x and y != y) and abs(x - y) > 1e-3 * max(abs(x), abs(y), 1.0):
                bad += 1
    return diff, bad


def packs(a, b, name, show=25):
    pa, pb = pak.Pak(os.path.join(a, name)), pak.Pak(os.path.join(b, name))
    fa = {e["name"].lower(): e for e in pa.files}
    fb = {e["name"].lower(): e for e in pb.files}
    only_a, only_b = sorted(set(fa) - set(fb)), sorted(set(fb) - set(fa))
    same = stored_only = 0
    rows = []
    for n in sorted(set(fa) & set(fb)):
        ea, eb = fa[n], fb[n]
        if ea["offset"] == eb["offset"] and ea["size"] == eb["size"] and ea["crc"] == eb["crc"] and ea["zsize"] == eb["zsize"]:
            same += 1
            continue
        da, db = pa.read(ea), pb.read(eb)
        if da == db:
            stored_only += 1
            continue
        rows.append((n, len(da or b""), len(db or b""), how_close(da, db)))
    print("%s: %d files in A, %d in B; %d identical, %d same data (other compression), %d differ" % (
        name, len(fa), len(fb), same, stored_only, len(rows)))
    if only_a or only_b:
        print("   only in A:", only_a[:10], "only in B:", only_b[:10])
    for r in rows[:show]:
        print("   ", r)
    return rows


def plain(a, b):
    for rel in sorted({os.path.relpath(os.path.join(d, f), a) for d, _, fs in os.walk(a) for f in fs if not f.endswith(".pak")}):
        pa, pb = os.path.join(a, rel), os.path.join(b, rel)
        if not os.path.exists(pb):
            print("   missing in B:", rel)
            continue
        da, db = open(pa, "rb").read(), open(pb, "rb").read()
        print("   %-46s %s" % (rel, "identical" if da == db else "DIFFERENT %s" % (how_close(da, db),)))


if __name__ == "__main__":
    A, B = sys.argv[1:3]
    packs(A, B, "gamedata_track.pak")
    packs(A, B, "root_track.pak")
    plain(A, B)

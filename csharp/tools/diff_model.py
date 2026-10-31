"""how far two builds' copies of a model are apart: python tools/diff_model.py DIR_A DIR_B name"""
import os, struct, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.environ["BMB_GAME"] = os.path.join(ROOT, "scratch", "no_game")
sys.path.insert(0, os.path.join(os.path.dirname(ROOT), "map_builder", "pak"))
import pak
A, B, name = sys.argv[1:4]
pa, pb = pak.Pak(os.path.join(A, "root_track.pak")), pak.Pak(os.path.join(B, "root_track.pak"))
a, b = pa.read(pa.find(name)), pb.read(pb.find(name))
d = [i for i in range(len(a)) if a[i] != b[i]]
print(len(d), "bytes differ, at", d[:24])
words = sorted({i // 2 * 2 for i in d})
print([(hex(i), struct.unpack_from("<e", a, i)[0], struct.unpack_from("<e", b, i)[0], a[i], b[i]) for i in words[:12]])

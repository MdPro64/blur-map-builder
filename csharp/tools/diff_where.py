"""is a model difference inside its textures, its vertices or elsewhere? python tools/diff_where.py DIR_A DIR_B name"""
import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MB = os.path.join(os.path.dirname(ROOT), "map_builder")
os.environ["BMB_GAME"] = os.path.join(ROOT, "scratch", "no_game")
sys.path[:0] = [MB] + [os.path.join(MB, p) for p in ("pak", "model", "tex")]
import pak, cpmodel
cpmodel.ADDON = r"J:\blur - user 1\Blur Map Builder\base\addon"
A, B, name = sys.argv[1:4]
pa, pb = pak.Pak(os.path.join(A, "root_track.pak")), pak.Pak(os.path.join(B, "root_track.pak"))
a, b = pa.read(pa.find(name)), pb.read(pb.find(name))
P = cpmodel.parse(a)
tex = {n: (o, o + cpmodel.texture_bytes(w, h, m), w, h, m) for n, (o, w, h, m) in P["textures"].items()}
print("vertices", P["vstart"], P["vstart"] + P["vcount"] * P["stride"], "faces", P["fstart"], P["fstart"] + 2 * P["fcount"], tex)
d = [i for i in range(len(a)) if a[i] != b[i]]
for n, (s, e, w, h, m) in tex.items():
    print(n, sum(s <= i < e for i in d), "bytes differ of", e - s)
print("outside textures:", sum(not any(s <= i < e for s, e, *_ in tex.values()) for i in d))
import struct
st, vs = P["stride"], P["vstart"]
print(P["elements"], st)
for v in sorted({(i - vs) // st for i in d if vs <= i < vs + P["vcount"] * st})[:8]:
    at = vs + v * st
    print(v, struct.unpack_from("<3f", a, at), a[at + 12:at + st].hex(), "|", struct.unpack_from("<3f", b, at), b[at + 12:at + st].hex())

import os, sys, struct
MB = r"E:\blur - bak\map_builder"
sys.path.insert(0, os.path.join(MB, "pak")); sys.path.insert(0, os.path.join(MB, "flat_earth"))
import pak
from PIL import Image, ImageDraw

p = pak.Pak(r"E:\blur - bak\cache\root.pak")
print("names api:", [a for a in dir(p) if not a.startswith("_")])
i = p.find("\\ui\\data\\ipf\\minimap\\flat_earth_game.ipf")
print("ipf index", i)
d = p.read(i)
print("size", len(d))

# walk nodes
pos = 8
def node(pos):
    kind, flags, nl = struct.unpack_from("<III", d, pos)
    name = d[pos + 12:pos + 12 + nl].decode(errors="replace")
    return kind, flags, name, pos + 12 + nl
im = Image.new("RGB", (1280, 720)); dr = ImageDraw.Draw(im)
k, f, n, pos = node(pos); pos += 4
k, f, n, pos = node(pos); cnt = struct.unpack_from("<I", d, pos)[0]; pos += 4
print("children", cnt)
# bounds node: skip to fixed size via known layout? find shapes by scanning names
import re
for m in re.finditer(rb"\x01\x00\x00\x00\x02\x00\x00\x00(.)\x00\x00\x00", d):
    nl = m.group(1)[0]
    s = m.start()
    name = d[s + 12:s + 12 + nl]
    if not name.isascii() or name == b"bounds":
        continue
    q = s + 12 + nl + 34 + 24 + 16
    npoly = struct.unpack_from("<I", d, q)[0]; q += 4
    print(name, "polys", npoly)
    for _ in range(npoly):
        nv = struct.unpack_from("<I", d, q)[0]; q += 4
        pts = [struct.unpack_from("<2f", d, q + 24 * j) for j in range(nv)]; q += 24 * nv
        dr.polygon([(u * 1280, v * 1280) for u, v in pts], fill=(255, 255, 255) if not name.endswith(b"_alt") else (255, 0, 0))
im.save(os.path.join(os.path.dirname(__file__), "installed_minimap.png"))

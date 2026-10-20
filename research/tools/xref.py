"""Find code that references a string in Blur.exe (live process memory), and disassemble around it."""
import struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
sys.path.insert(0, r"E:\blur - bak\trainer")
import blur_trainer as b

g = b.Game()
img = g.read(g.mod_base, g.mod_size) or b""
if not img:
    # read page by page if the whole image can't be read at once
    chunks = []
    for off in range(0, g.mod_size, 0x1000):
        chunks.append(g.read(g.mod_base + off, 0x1000) or b"\0" * 0x1000)
    img = b"".join(chunks)
md = Cs(CS_ARCH_X86, CS_MODE_32)
for text in sys.argv[1:]:
    needle = text.encode() + b"\0"
    pos = img.find(needle)
    while pos != -1:
        va = g.mod_base + pos
        print("\n=== %r at 0x%08X" % (text, va))
        ref = struct.pack("<I", va)
        r = img.find(ref)
        while r != -1:
            site = g.mod_base + r
            print("--- referenced at 0x%08X" % site)
            start = site - 0x60
            for ins in md.disasm(img[start - g.mod_base: start - g.mod_base + 0xA0], start):
                print("   %08X  %-8s %s%s" % (ins.address, ins.mnemonic, ins.op_str,
                                            "   <==" if ins.address <= site < ins.address + ins.size else ""))
            r = img.find(ref, r + 1)
        pos = img.find(needle, pos + 1)


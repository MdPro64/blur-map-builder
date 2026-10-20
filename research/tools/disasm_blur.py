import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
sys.path.insert(0, r"E:\blur - bak\trainer")
import blur_trainer as b

g = b.Game()
start = int(sys.argv[1], 16)
length = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x100
mark = int(sys.argv[3], 16) if len(sys.argv) > 3 else -1
code = g.read(start, length)
md = Cs(CS_ARCH_X86, CS_MODE_32)
for ins in md.disasm(code, start):
    flag = " <==" if ins.address + ins.size == mark or ins.address == mark else ""
    print("%08X  %-8s %s%s" % (ins.address, ins.mnemonic, ins.op_str, flag))


"""Disassemble Blur.exe straight from the file on disk (no running game needed).
usage: python disasm_file.py START LEN [MARK]   (hex numbers, no 0x; addresses as loaded at 0x400000)
Also importable: read_va(va, size) gives the bytes at a virtual address."""
import struct
import sys

from capstone import Cs, CS_ARCH_X86, CS_MODE_32

EXE = r"E:\blur - bak\Blur.exe"
_data = open(EXE, "rb").read()
_pe = struct.unpack_from("<I", _data, 0x3C)[0]
_nsec = struct.unpack_from("<H", _data, _pe + 6)[0]
_opt = struct.unpack_from("<H", _data, _pe + 20)[0]
_secs = []
for i in range(_nsec):
    s = _pe + 24 + _opt + 40 * i
    vsize, va, rsize, raw = struct.unpack_from("<IIII", _data, s + 8)
    _secs.append((0x400000 + va, max(vsize, rsize), raw, rsize))


def read_va(va, size):
    for start, span, raw, rsize in _secs:
        if start <= va < start + span:
            off = va - start
            return _data[raw + off:raw + min(off + size, rsize)]
    return b""


def u32(va):
    return struct.unpack("<I", read_va(va, 4))[0]


if __name__ == "__main__":
    start = int(sys.argv[1], 16)
    length = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x100
    mark = int(sys.argv[3], 16) if len(sys.argv) > 3 else -1
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    for ins in md.disasm(read_va(start, length), start):
        flag = " <==" if ins.address == mark else ""
        print("%08X  %-8s %s%s" % (ins.address, ins.mnemonic, ins.op_str, flag))

"""Shared helpers: enumerate Blur's writable memory and read it into numpy arrays."""
import ctypes, sys
from ctypes import wintypes
import numpy as np
sys.path.insert(0, r"E:\blur - bak\trainer")
import blur_trainer as b

k32 = ctypes.WinDLL("kernel32", use_last_error=True)


class MBI(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_uint64), ("AllocationBase", ctypes.c_uint64),
                ("AllocationProtect", wintypes.DWORD), ("PartitionId", wintypes.WORD),
                ("RegionSize", ctypes.c_uint64), ("State", wintypes.DWORD),
                ("Protect", wintypes.DWORD), ("Type", wintypes.DWORD)]


k32.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(MBI), ctypes.c_size_t]
k32.VirtualQueryEx.restype = ctypes.c_size_t
k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


def regions(g):
    out, addr, mbi = [], 0x10000, MBI()
    while addr < 0x100000000:
        if not k32.VirtualQueryEx(g.h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi)):
            break
        if mbi.State == 0x1000 and mbi.Protect & 0xCC and not mbi.Protect & 0x100 and mbi.Type != 0x40000:
            out.append((mbi.BaseAddress, mbi.RegionSize))
        addr = mbi.BaseAddress + mbi.RegionSize
    return out


def read_array(g, base, size, dtype=np.float32):
    a = np.empty(size // np.dtype(dtype).itemsize, dtype)
    n = ctypes.c_size_t()
    ok = k32.ReadProcessMemory(g.h, ctypes.c_void_p(base), ctypes.c_void_p(a.ctypes.data), a.nbytes, ctypes.byref(n))
    return a if ok and n.value == a.nbytes else None


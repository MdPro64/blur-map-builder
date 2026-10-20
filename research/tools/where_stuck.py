"""READ-ONLY: where is a frozen Blur stuck?  python -B where_stuck.py [samples]
Samples every thread of Blur_mod.exe (or Blur.exe) a few times: the code address it is running (EIP) and the
return addresses found on its stack (= the functions that called it). Writes the report to where_stuck.txt.
Nothing in the game is changed (each thread is paused for a moment to read its registers)."""
import ctypes
import ctypes.wintypes as W
import collections
import os
import subprocess
import sys
import time

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
PROCESS_ALL = 0x0010 | 0x0400            # VM_READ | QUERY_INFORMATION
THREAD_ACC = 0x0002 | 0x0008 | 0x0040    # SUSPEND_RESUME | GET_CONTEXT | QUERY_INFORMATION
TH32CS_SNAPTHREAD = 0x4
WOW64_CONTEXT_CONTROL = 0x00010001
WOW64_CONTEXT_INTEGER = 0x00010002


class THREADENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", W.DWORD), ("cntUsage", W.DWORD), ("th32ThreadID", W.DWORD),
                ("th32OwnerProcessID", W.DWORD), ("tpBasePri", W.LONG), ("tpDeltaPri", W.LONG), ("dwFlags", W.DWORD)]


class WOW64_CONTEXT(ctypes.Structure):
    _fields_ = [("ContextFlags", W.DWORD), ("Dr", W.DWORD * 6), ("FloatSave", ctypes.c_byte * 112),
                ("SegGs", W.DWORD), ("SegFs", W.DWORD), ("SegEs", W.DWORD), ("SegDs", W.DWORD),
                ("Edi", W.DWORD), ("Esi", W.DWORD), ("Ebx", W.DWORD), ("Edx", W.DWORD), ("Ecx", W.DWORD),
                ("Eax", W.DWORD), ("Ebp", W.DWORD), ("Eip", W.DWORD), ("SegCs", W.DWORD), ("EFlags", W.DWORD),
                ("Esp", W.DWORD), ("SegSs", W.DWORD), ("Ext", ctypes.c_byte * 512)]


def find_pid():
    out = subprocess.run(["tasklist", "/fo", "csv", "/nh"], capture_output=True, text=True).stdout
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if parts and parts[0].lower() in ("blur_mod.exe", "blur.exe"):
            return int(parts[1]), parts[0]
    sys.exit("Blur is not running.")


def threads(pid):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    te = THREADENTRY32()
    te.dwSize = ctypes.sizeof(te)
    out = []
    ok = k32.Thread32First(snap, ctypes.byref(te))
    while ok:
        if te.th32OwnerProcessID == pid:
            out.append(te.th32ThreadID)
        ok = k32.Thread32Next(snap, ctypes.byref(te))
    k32.CloseHandle(snap)
    return out


def read(hp, addr, n):
    buf = ctypes.create_string_buffer(n)
    got = ctypes.c_size_t()
    if not k32.ReadProcessMemory(hp, ctypes.c_void_p(addr), buf, n, ctypes.byref(got)):
        return b""
    return buf.raw[:got.value]


def main():
    samples = int(sys.argv[1]) if len(sys.argv) > 1 else 20
    pid, name = find_pid()
    hp = k32.OpenProcess(PROCESS_ALL, False, pid)
    lo, hi = 0x400000, 0x1400000              # the game's own code (Blur_mod.exe image)
    eips = collections.defaultdict(collections.Counter)
    stacks = collections.defaultdict(collections.Counter)
    for s in range(samples):
        for tid in threads(pid):
            ht = k32.OpenThread(THREAD_ACC, False, tid)
            if not ht:
                continue
            ctx = WOW64_CONTEXT()
            ctx.ContextFlags = WOW64_CONTEXT_CONTROL | WOW64_CONTEXT_INTEGER
            k32.Wow64SuspendThread(ht)
            ok = k32.Wow64GetThreadContext(ht, ctypes.byref(ctx))
            k32.ResumeThread(ht)
            k32.CloseHandle(ht)
            if not ok:
                continue
            eips[tid][ctx.Eip] += 1
            raw = read(hp, ctx.Esp, 0x2000)
            rets = [int.from_bytes(raw[i:i + 4], "little") for i in range(0, len(raw) - 3, 4)]
            rets = [r for r in rets if lo <= r < hi][:24]
            stacks[tid][tuple(rets[:12])] += 1
        time.sleep(0.15)
    lines = ["%s pid %d, %d samples" % (name, pid, samples)]
    for tid in sorted(eips, key=lambda t: -max(eips[t].values())):
        lines.append("thread %d: EIP %s" % (tid, ", ".join("%08X x%d" % (a, c) for a, c in eips[tid].most_common(8))))
        for st, c in stacks[tid].most_common(2):
            lines.append("    stack x%d: %s" % (c, " ".join("%08X" % a for a in st)))
    text = "\n".join(lines)
    print(text)
    open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "where_stuck.txt"), "w").write(text + "\n")


if __name__ == "__main__":
    main()

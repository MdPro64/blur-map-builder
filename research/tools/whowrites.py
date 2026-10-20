"""Hardware write-breakpoint on a few addresses in Blur; report who writes them and where the data came from."""
import ctypes, struct, sys, time
from ctypes import wintypes
import numpy as np
sys.path.insert(0, r"E:\blur - bak\trainer")
import blur_trainer as b

k32 = b.k32
DBG_CONTINUE, DBG_NOT_HANDLED = 0x00010002, 0x80010001
SINGLE_STEPS = (0x80000004, 0x4000001E)


class FLOATSAVE(ctypes.Structure):
    _fields_ = [("a", wintypes.DWORD * 7), ("RegisterArea", ctypes.c_ubyte * 80), ("Cr0NpxState", wintypes.DWORD)]


class WOW64_CONTEXT(ctypes.Structure):
    _fields_ = [("ContextFlags", wintypes.DWORD), ("Dr0", wintypes.DWORD), ("Dr1", wintypes.DWORD),
                ("Dr2", wintypes.DWORD), ("Dr3", wintypes.DWORD), ("Dr6", wintypes.DWORD), ("Dr7", wintypes.DWORD),
                ("FloatSave", FLOATSAVE), ("SegGs", wintypes.DWORD), ("SegFs", wintypes.DWORD),
                ("SegEs", wintypes.DWORD), ("SegDs", wintypes.DWORD), ("Edi", wintypes.DWORD), ("Esi", wintypes.DWORD),
                ("Ebx", wintypes.DWORD), ("Edx", wintypes.DWORD), ("Ecx", wintypes.DWORD), ("Eax", wintypes.DWORD),
                ("Ebp", wintypes.DWORD), ("Eip", wintypes.DWORD), ("SegCs", wintypes.DWORD), ("EFlags", wintypes.DWORD),
                ("Esp", wintypes.DWORD), ("SegSs", wintypes.DWORD), ("ExtendedRegisters", ctypes.c_ubyte * 512)]


class DEBUG_EVENT(ctypes.Structure):
    _fields_ = [("code", wintypes.DWORD), ("pid", wintypes.DWORD), ("tid", wintypes.DWORD),
                ("pad", wintypes.DWORD), ("u", ctypes.c_ubyte * 160)]


assert ctypes.sizeof(WOW64_CONTEXT) == 716, ctypes.sizeof(WOW64_CONTEXT)
assert ctypes.sizeof(DEBUG_EVENT) == 176

k32.DebugActiveProcess.argtypes = [wintypes.DWORD]
k32.DebugActiveProcessStop.argtypes = [wintypes.DWORD]
k32.DebugSetProcessKillOnExit.argtypes = [wintypes.BOOL]
k32.WaitForDebugEvent.argtypes = [ctypes.POINTER(DEBUG_EVENT), wintypes.DWORD]
k32.ContinueDebugEvent.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.DWORD]
k32.Wow64GetThreadContext.argtypes = [wintypes.HANDLE, ctypes.POINTER(WOW64_CONTEXT)]
k32.Wow64SetThreadContext.argtypes = [wintypes.HANDLE, ctypes.POINTER(WOW64_CONTEXT)]
k32.SuspendThread.argtypes = [wintypes.HANDLE]
k32.SuspendThread.restype = wintypes.DWORD
k32.ResumeThread.argtypes = [wintypes.HANDLE]
k32.ResumeThread.restype = wintypes.DWORD

g = b.Game()
watch = [int(a, 16) for a in sys.argv[1:5] if not a.startswith("--")]
X, Y, Z = struct.unpack("<3f", g.read(0x10BD62C, 12))
print("watching", ["0x%08X" % a for a in watch], "car at", X, Y, Z)

dr7 = 0
for i in range(len(watch)):
    dr7 |= (1 << (2 * i)) | ((0b11 if "--rw" in sys.argv else 0b01) << (16 + 4 * i)) | (0b11 << (18 + 4 * i))

threads = {}


def set_drs(h, enable):
    ctx = WOW64_CONTEXT()
    ctx.ContextFlags = 0x10010
    if not k32.Wow64GetThreadContext(h, ctypes.byref(ctx)):
        return False
    for i, a in enumerate(watch):
        setattr(ctx, "Dr%d" % i, a if enable else 0)
    ctx.Dr7 = dr7 if enable else 0
    ctx.Dr6 = 0
    return bool(k32.Wow64SetThreadContext(h, ctypes.byref(ctx)))


def find_source(regs):
    """Look near every register for a copy of the car position."""
    out = []
    for name, v in regs.items():
        if name in ("Eip",) or v < 0x10000:
            continue
        base = (v & ~3) - 0x400
        data = g.read(base, 0x800)
        if not data:
            continue
        f = np.frombuffer(data, np.float32)
        with np.errstate(all="ignore"):
            m = (np.abs(f[:-2] - X) < 1.5) & (np.abs(f[1:-1] - Y) < 1.5) & (np.abs(f[2:] - Z) < 1.5)
        for i in np.nonzero(m)[0]:
            out.append("%s+0x%X -> 0x%08X" % (name, base + 4 * int(i) - v, base + 4 * int(i)) if base + 4 * int(i) >= v
                       else "%s-0x%X -> 0x%08X" % (name, v - base - 4 * int(i), base + 4 * int(i)))
    return out


if not k32.DebugActiveProcess(g.pid):
    sys.exit("attach failed %d" % ctypes.get_last_error())
k32.DebugSetProcessKillOnExit(False)
hits = []
ev = DEBUG_EVENT()
armed = False
t_end = None
try:
    while True:
        if not k32.WaitForDebugEvent(ctypes.byref(ev), 500):
            if t_end and time.time() > t_end:
                break
            continue
        status = DBG_CONTINUE
        u = bytes(ev.u)
        if ev.code == 3:  # create process
            hfile, hproc, hthr = struct.unpack_from("<QQQ", u, 0)
            threads[ev.tid] = hthr
            if hfile:
                k32.CloseHandle(hfile)
        elif ev.code == 2:  # create thread
            threads[ev.tid] = struct.unpack_from("<Q", u, 0)[0]
            if armed:
                set_drs(threads[ev.tid], True)
        elif ev.code == 4:
            threads.pop(ev.tid, None)
        elif ev.code == 6:
            hfile = struct.unpack_from("<Q", u, 0)[0]
            if hfile:
                k32.CloseHandle(hfile)
        elif ev.code == 1:
            exc = struct.unpack_from("<I", u, 0)[0]
            if exc in (0x80000003, 0x4000001F) and not armed:
                for h in threads.values():
                    set_drs(h, True)
                armed = True
                t_end = time.time() + (12.0 if "--rw" in sys.argv else 3.0)
                print("armed on %d threads" % len(threads))
            elif exc in SINGLE_STEPS and ev.tid in threads:
                ctx = WOW64_CONTEXT()
                ctx.ContextFlags = 0x10013
                k32.Wow64GetThreadContext(threads[ev.tid], ctypes.byref(ctx))
                regs = {n: getattr(ctx, n) for n in ("Eax", "Ebx", "Ecx", "Edx", "Esi", "Edi", "Ebp", "Esp", "Eip")}
                which = [i for i in range(4) if ctx.Dr6 & (1 << i)]
                only_car = "--only-car" in sys.argv
                wrote = g.read_f32(watch[which[0]]) if which else float("nan")
                if len(hits) < 40 and (not only_car or abs(wrote - X) < 3):
                    code = g.read(regs["Eip"] - 16, 16)
                    hits.append((ev.tid, which, regs, code.hex() if code else "", find_source(regs)))
                ctx.ContextFlags = 0x10010
                ctx.Dr6 = 0
                k32.Wow64SetThreadContext(threads[ev.tid], ctypes.byref(ctx))
            elif exc in (0x80000003, 0x4000001F):
                pass
            else:
                status = DBG_NOT_HANDLED
        k32.ContinueDebugEvent(ev.pid, ev.tid, status)
        if t_end and time.time() > t_end:
            break
finally:
    for h in threads.values():
        k32.SuspendThread(h)
        set_drs(h, False)
        k32.ResumeThread(h)
    # drain any breakpoint hits that were already queued
    quiet = time.time() + 0.5
    while time.time() < quiet:
        if k32.WaitForDebugEvent(ctypes.byref(ev), 100):
            exc = struct.unpack_from("<I", bytes(ev.u), 0)[0] if ev.code == 1 else 0
            st = DBG_NOT_HANDLED if ev.code == 1 and exc not in SINGLE_STEPS + (0x80000003, 0x4000001F) else DBG_CONTINUE
            k32.ContinueDebugEvent(ev.pid, ev.tid, st)
            quiet = time.time() + 0.3
    print("detach:", bool(k32.DebugActiveProcessStop(g.pid)))

seen = set()
for tid, which, regs, code, src in hits:
    key = (tuple(which), regs["Eip"])
    if key in seen:
        continue
    seen.add(key)
    print("\nwatch%s written by EIP=0x%08X (Blur.exe+0x%X) thread %d" % (which, regs["Eip"], regs["Eip"] - g.mod_base, tid))
    print("  regs:", " ".join("%s=%08X" % kv for kv in regs.items()))
    print("  code before:", code)
    print("  car-position copies near registers:", src[:10])
print("\ntotal hits:", len(hits))



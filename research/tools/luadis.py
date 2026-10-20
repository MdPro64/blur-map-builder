"""Lua 5.1 bytecode ("\\x1bLuaQ") disassembler, enough to read Blur's UI scripts (they are zlib blocks inside
Blur.exe; extracted to work/lua_scripts/<exe offset>.luac).  python luadis.py FILE.luac [function-name filter]"""
import struct
import sys

OPS = ["MOVE", "LOADK", "LOADBOOL", "LOADNIL", "GETUPVAL", "GETGLOBAL", "GETTABLE", "SETGLOBAL", "SETUPVAL",
       "SETTABLE", "NEWTABLE", "SELF", "ADD", "SUB", "MUL", "DIV", "MOD", "POW", "UNM", "NOT", "LEN", "CONCAT", "JMP",
       "EQ", "LT", "LE", "TEST", "TESTSET", "CALL", "TAILCALL", "RETURN", "FORLOOP", "FORPREP", "TFORLOOP",
       "SETLIST", "CLOSE", "CLOSURE", "VARARG"]


class R:
    def __init__(self, b):
        self.b, self.p = b, 0

    def u8(self):
        self.p += 1
        return self.b[self.p - 1]

    def i32(self):
        self.p += 4
        return struct.unpack_from("<i", self.b, self.p - 4)[0]

    def f64(self):
        self.p += 8
        return struct.unpack_from("<d", self.b, self.p - 8)[0]

    def s(self):
        n = struct.unpack_from("<I", self.b, self.p)[0]
        self.p += 4
        v = self.b[self.p:self.p + n - 1].decode("latin1") if n else None
        self.p += n
        return v


def func(r):
    f = {"source": r.s(), "line": r.i32(), "last": r.i32()}
    f["nups"], f["nparams"], f["vararg"], f["maxstack"] = r.u8(), r.u8(), r.u8(), r.u8()
    f["code"] = [r.i32() & 0xFFFFFFFF for _ in range(r.i32())]
    ks = []
    for _ in range(r.i32()):
        t = r.u8()
        ks.append(None if t == 0 else bool(r.u8()) if t == 1 else r.f64() if t == 3 else r.s())
    f["k"] = ks
    f["protos"] = [func(r) for _ in range(r.i32())]
    n = r.i32()                                                # line info (read the count BEFORE moving p)
    r.p += 4 * n
    f["locals"] = []
    for _ in range(r.i32()):
        f["locals"].append(r.s())
        r.i32()
        r.i32()
    f["upvals"] = [r.s() for _ in range(r.i32())]
    return f


def load(b):
    assert b[:4] == b"\x1bLua"
    r = R(b)
    r.p = 12
    return func(r)


def rk(f, x):
    return repr(f["k"][x - 256]) if x >= 256 else "r%d" % x


def dis(f, name="main", indent=""):
    print("%sfunction %s (params %d, upvals %s)" % (indent, name, f["nparams"], f["upvals"]))
    names = {}
    for pc, ins in enumerate(f["code"]):
        op, a = ins & 0x3F, (ins >> 6) & 0xFF
        c, b = (ins >> 14) & 0x1FF, (ins >> 23) & 0x1FF
        bx = ins >> 14
        sbx = bx - 131071
        o = OPS[op] if op < len(OPS) else "?%d" % op
        if o in ("LOADK", "GETGLOBAL", "SETGLOBAL"):
            arg = "r%d %r" % (a, f["k"][bx])
        elif o in ("JMP", "FORLOOP", "FORPREP"):
            arg = "-> %d" % (pc + 1 + sbx)
        elif o == "CLOSURE":
            arg = "r%d proto %d" % (a, bx)
        elif o in ("GETTABLE", "SELF"):
            arg = "r%d = r%d[%s]" % (a, b, rk(f, c))
        elif o == "SETTABLE":
            arg = "r%d[%s] = %s" % (a, rk(f, b), rk(f, c))
        elif o in ("EQ", "LT", "LE", "ADD", "SUB", "MUL", "DIV"):
            arg = "%d %s %s" % (a, rk(f, b), rk(f, c))
        else:
            arg = "%d %d %d" % (a, b, c)
        print("%s  %4d %-10s %s" % (indent, pc, o, arg))
        if o == "CLOSURE":
            names[bx] = pc
    for i, p in enumerate(f["protos"]):
        # name = the SETGLOBAL / SETTABLE key right after its CLOSURE
        nm = "proto%d" % i
        if i in names:
            nxt = f["code"][names[i] + 1] if names[i] + 1 < len(f["code"]) else 0
            if nxt & 0x3F == 7:
                nm = f["k"][nxt >> 14]
            elif nxt & 0x3F == 9 and ((nxt >> 23) & 0x1FF) >= 256:
                nm = f["k"][((nxt >> 23) & 0x1FF) - 256]
        dis(p, nm, indent + "    ")


if __name__ == "__main__":
    main = load(open(sys.argv[1], "rb").read())
    dis(main)

"""Write KSLF files: clone a top-level object (e.g. a Route) under a new name + uid.
The clone copies the object's data blocks, object-table rows and string pool; its Name string is replaced.
Section 5 row = {uid-table row, object-table row count, data size, pool size}. Its uid is appended at the END of section 4 (the uid table is sorted, so the new uid must be larger than
all existing ones); nothing else needs renumbering. Sections 4-7 are rebuilt, the header offsets updated."""
import struct

import kslf
import schema


def rebuild(k, s4, s5, s6, s7):
    """New file bytes: header + sections 0-3 unchanged, then the given raw bytes for sections 4-7."""
    d = k.data
    out = bytearray(d[:k.sections[4][0]])
    counts = [len(s4) // 8, len(s5) // 16, len(s6) // 6, len(s7)]
    for i, raw in enumerate((s4, s5, s6, s7)):
        while len(out) % 4:
            out += b"\0"
        struct.pack_into("<II", out, 8 + 8 * (4 + i), len(out), counts[i])
        out += raw
    return bytes(out)


def clone_object(path_in, path_out, src_index, new_name, new_uid=None, patch=None, data_in=None):
    """Clone top-level object `src_index` as `new_name`. `patch(data_bytearray, block_offsets)` may edit
    the copied data (offsets are relative to the start of the copied data). Returns the new uid."""
    d = data_in if data_in is not None else open(path_in, "rb").read()
    k = kslf.Kslf(d)
    types = schema.load_schema(k)
    objs = kslf.Objects(k, types).objects
    (o4, n4), (o5, n5), (o6, n6), (o7, n7) = k.sections[4:8]
    s4 = bytearray(d[o4:o4 + 8 * n4])
    s5 = bytearray(d[o5:o5 + 16 * n5])
    s6 = bytearray(d[o6:o6 + 6 * n6])
    s7 = bytearray(d[o7:o7 + n7])

    src = objs[src_index]
    # section 5 row = {row in the uid table (section 4), object-table rows, data size, pool size};
    # the object-table rows of object i start after the rows of objects 0..i-1
    _, nrows, dsize, ssize = struct.unpack_from("<4I", d, o5 + 16 * src_index)
    first_row = sum(struct.unpack_from("<I", d, o5 + 16 * i + 4)[0] for i in range(src_index))
    data = bytearray(d[src["data"]:src["data"] + dsize])
    pool = bytearray(src["pool"])

    # new Name (objects derived from AmaxGameDataObject, Name at +12): append to the pool and point at it.
    # new_name=None for nameless objects (e.g. LevelActorList)
    b0 = src["blocks"][0]
    if new_name is not None:
        name_field = b0["offset"] - src["data"] + 12
        struct.pack_into("<I", data, name_field, len(pool))
        pool += new_name.encode("latin1") + b"\0"
    if patch:
        patch(data, pool, [b["offset"] - src["data"] for b in src["blocks"]])
    while len(pool) % 4:
        pool += b"\0"

    # uid above every existing uid; section 4 row = {uid, type, object index}
    last_uid = struct.unpack_from("<I", s4, 8 * (n4 - 1))[0]
    uid = new_uid if new_uid is not None else last_uid + 1
    assert uid > last_uid and uid <= 0xFFFFFFFF
    s4 += struct.pack("<IHH", uid, b0["type"], n5)
    s5 += struct.pack("<4I", n4, nrows, len(data), len(pool))
    s6 += d[o6 + 6 * first_row:o6 + 6 * (first_row + nrows)]
    s7 += data + kslf.scramble(bytes(pool))
    out = rebuild(k, bytes(s4), bytes(s5), bytes(s6), bytes(s7))
    if path_out:
        open(path_out, "wb").write(out)
    return uid if data_in is None else (uid, out)


def edit_object(path_in, path_out, obj_index, fn):
    """Let fn(ctx) edit one top-level object, then rebuild the file. ctx has:
    data (bytearray of the object's data), rows (list of [type, count, kind] object-table rows),
    pool (bytearray, decoded string pool), blocks (list of block start offsets inside data, recomputed by
    ctx.insert), insert(block, pos, raw) to insert elements into a block, add_uid(uid, type_name) ->
    new section-4 index (appended; uid must be above all existing ones), types."""
    d = open(path_in, "rb").read()
    k = kslf.Kslf(d)
    types = schema.load_schema(k)
    objs = kslf.Objects(k, types).objects
    (o4, n4), (o5, n5), (o6, n6), (o7, n7) = k.sections[4:8]
    s4 = bytearray(d[o4:o4 + 8 * n4])
    rows_all = [list(struct.unpack_from("<3H", d, o6 + 6 * i)) for i in range(n6)]
    s5rows = [list(struct.unpack_from("<4I", d, o5 + 16 * i)) for i in range(n5)]
    first = sum(r[1] for r in s5rows[:obj_index])
    obj = objs[obj_index]
    uidrow, nrows, dsize, ssize = s5rows[obj_index]

    class Ctx:
        pass
    ctx = Ctx()
    ctx.types = types
    ctx.data = bytearray(d[obj["data"]:obj["data"] + dsize])
    ctx.rows = [list(r) for r in rows_all[first:first + nrows]]
    ctx.pool = bytearray(obj["pool"])
    ctx.pool_changed = False

    def block_offsets():
        out, p = [], 0
        for t, cnt, kind in ctx.rows:
            out.append(p)
            size = types[t]["size"] if kind == 0 else 4
            p = (p + size * cnt + 3) & ~3
        return out, p
    ctx.blocks = block_offsets()[0]

    def insert(block, pos, raw):
        t, cnt, kind = ctx.rows[block]
        size = types[t]["size"] if kind == 0 else 4
        assert len(raw) % size == 0
        old_offsets, old_end = block_offsets()
        at = old_offsets[block] + pos * size
        old_block_end = old_offsets[block] + size * cnt
        padded_old = (old_block_end + 3) & ~3
        ctx.rows[block][1] += len(raw) // size
        new_block_end = old_block_end + len(raw)
        padded_new = (new_block_end + 3) & ~3
        # insert the elements, then fix the padding after the block
        ctx.data[at:at] = raw
        tail_start = new_block_end
        cur_pad = padded_old + len(raw) - new_block_end
        need_pad = padded_new - new_block_end
        if cur_pad != need_pad:
            del ctx.data[tail_start:tail_start + cur_pad]
            ctx.data[tail_start:tail_start] = b"\0" * need_pad
        ctx.blocks = block_offsets()[0]
    ctx.insert = insert

    def add_uid(uid, type_name):
        """section-4 index of uid: its row if it is already there, else a new row (uid above all others)"""
        nonlocal s4
        cnt = len(s4) // 8
        for i in range(cnt):
            if struct.unpack_from("<I", s4, 8 * i)[0] == uid:
                return i
        last = struct.unpack_from("<I", s4, 8 * (cnt - 1))[0]
        assert uid > last, "uid must be above %08X" % last
        t = next(i for i, ty in enumerate(types) if ty["name"] == type_name)
        s4 += struct.pack("<IHH", uid, t, 0xFFFF)
        return cnt
    ctx.add_uid = add_uid

    fn(ctx)
    assert block_offsets()[1] == len(ctx.data), (block_offsets()[1], len(ctx.data))

    pool_raw = obj["pool_raw"]
    if ctx.pool_changed:
        pool = bytes(ctx.pool)
        while len(pool) % 4:
            pool += b"\0"
        pool_raw = kslf.scramble(pool)
    s5rows[obj_index] = [uidrow, len(ctx.rows), len(ctx.data), len(pool_raw)]
    rows_all[first:first + nrows] = ctx.rows
    s5 = b"".join(struct.pack("<4I", *r) for r in s5rows)
    s6 = b"".join(struct.pack("<3H", *r) for r in rows_all)
    s7 = d[o7:obj["data"]] + bytes(ctx.data) + pool_raw + d[obj["data"] + dsize + ssize:o7 + n7]
    out = rebuild(k, bytes(s4), s5, s6, s7)
    open(path_out, "wb").write(out)
    return out


# ---------------------------------------------------------------------------------------------------------------
# uids ANYWHERE: section 4 must stay sorted; a row put in the middle shifts the index of every later row, so every
# reference (u32 index into section 4) and every object's own uid-row number (section 5, first u32) is renumbered.

def _all_fields(types, t):
    out = []
    for b, off in types[t]["bases"]:
        out += [dict(f, offset=f["offset"] + off) for f in _all_fields(types, b)]
    return out + types[t]["fields"]


def ref_slots(data):
    """absolute offsets of every reference slot (u32 index into section 4) in the file's object data"""
    k = kslf.Kslf(data)
    types = schema.load_schema(k)
    objs = kslf.Objects(k, types).objects
    slots = []

    def walk(obj, t, at, depth=0):
        if t >= len(types) or depth > 40:
            return
        for f in _all_fields(types, t):
            a, fl = at + f["offset"], f["flags"]
            if fl == 5:
                slots.append(a)
            elif fl & 0x100:
                if fl & 0xF != 5:
                    continue
                blk, first, cnt = struct.unpack_from("<HHI", data, a)
                if blk == 0xFFFF or cnt == 0 or blk >= len(obj["blocks"]):
                    continue
                b = obj["blocks"][blk]
                if b["kind"] != 0:
                    slots.extend(b["offset"] + 4 * j for j in range(first, first + cnt))
            elif fl in (0, 3) and f["type"] < len(types) and (types[f["type"]]["fields"] or types[f["type"]]["bases"]):
                if types[f["type"]]["kind"] != 1:          # not an enum: an inline struct
                    walk(obj, f["type"], a, depth + 1)
    for obj in objs:
        for b in obj["blocks"]:
            if b["kind"] == 0:                              # every struct element of every block (owned objects too)
                for e in range(b["count"]):
                    walk(obj, b["type"], b["offset"] + e * b["size"])
    return sorted(set(slots))


def _move_last_row(data):
    """the LAST section-4 row moves to its sorted place; all references renumbered. Returns (data, its index)"""
    d = bytearray(data)
    k = kslf.Kslf(bytes(d))
    o4, n4 = k.sections[4]
    o5, n5 = k.sections[5]
    last = n4 - 1
    uid = struct.unpack_from("<I", d, o4 + 8 * last)[0]
    pos = 0
    while pos < last and struct.unpack_from("<I", d, o4 + 8 * pos)[0] < uid:
        pos += 1
    if pos == last:
        return bytes(d), last
    assert struct.unpack_from("<I", d, o4 + 8 * pos)[0] != uid, "uid %08X is already there" % uid

    def remap(v):
        if v == last:
            return pos
        if pos <= v < last:
            return v + 1
        return v
    for a in ref_slots(bytes(d)):
        v = struct.unpack_from("<I", d, a)[0]
        if v != 0xFFFFFFFF and v < n4:
            struct.pack_into("<I", d, a, remap(v))
    for i in range(n5):
        v = struct.unpack_from("<I", d, o5 + 16 * i)[0]
        struct.pack_into("<I", d, o5 + 16 * i, remap(v))
    row = bytes(d[o4 + 8 * last:o4 + 8 * last + 8])
    d[o4 + 8 * (pos + 1):o4 + 8 * (last + 1)] = d[o4 + 8 * pos:o4 + 8 * last]
    d[o4 + 8 * pos:o4 + 8 * pos + 8] = row
    return bytes(d), pos


def insert_uid(data, uid, type_name, obj=0xFFFF):
    """add a section-4 row {uid, type, object index (0xFFFF = defined in another file)} at its sorted place
    (any uid not used yet). Returns (data, row index). An existing uid returns its row unchanged."""
    k = kslf.Kslf(data)
    types = schema.load_schema(k)
    o4, n4 = k.sections[4]
    for i in range(n4):
        if struct.unpack_from("<I", data, o4 + 8 * i)[0] == uid:
            return data, i
    t = next(i for i, ty in enumerate(types) if ty["name"] == type_name)
    s4 = bytearray(data[o4:o4 + 8 * n4]) + struct.pack("<IHH", uid, t, obj)
    (o5, n5), (o6, n6), (o7, n7) = k.sections[5:8]
    out = rebuild(k, bytes(s4), data[o5:o5 + 16 * n5], data[o6:o6 + 6 * n6], data[o7:o7 + n7])
    return _move_last_row(out)


def clone_object_any(src_index, new_name, new_uid, patch=None, data_in=None):
    """clone_object with ANY new uid (not only above the highest one): appended, then moved to its sorted place.
    Returns (uid, data)."""
    k = kslf.Kslf(data_in)
    o4, n4 = k.sections[4]
    last = struct.unpack_from("<I", data_in, o4 + 8 * (n4 - 1))[0]
    if new_uid > last:
        return clone_object(None, None, src_index, new_name, new_uid, patch, data_in=data_in)
    # clone under a temporary uid above everything, then give the row its real uid and move it into place
    tmp = 0xFFFFFFFF
    assert last < tmp, "the top uid is taken"
    _, d = clone_object(None, None, src_index, new_name, tmp, patch, data_in=data_in)
    d = bytearray(d)
    o4, n4 = kslf.Kslf(bytes(d)).sections[4]
    for i in range(n4):
        assert struct.unpack_from("<I", d, o4 + 8 * i)[0] != new_uid, "uid %08X is already there" % new_uid
    struct.pack_into("<I", d, o4 + 8 * (n4 - 1), new_uid)
    d, _ = _move_last_row(bytes(d))
    return new_uid, d

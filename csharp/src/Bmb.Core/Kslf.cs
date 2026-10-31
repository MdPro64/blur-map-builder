// Blur KSLF (.bin / actorpack.xt): reader, schema, object dump, writer.
// Ports of kslf/kslf.py, schema.py, dump.py, kslf_write.py - see those for the format notes.
// The Python build re-parses the whole file (through a temp file) after every edit; here parsing is cheap and lazy.
namespace Bmb.Core;

public sealed class FieldDef { public string Name = ""; public int Type, Offset, Flags; }
public sealed class TypeDef
{
    public int Index, Kind, Size, Unk; public string Name = "";
    public List<FieldDef> Fields = new(); public List<(int Base, int Off)> Bases = new();
    public List<FieldDef>? All;      // with the base classes' fields, bases first
}
public sealed class Block { public int Type, Count, Kind, Offset, Size; public string Name = ""; }
public sealed class KObject
{
    public uint Id; public List<Block> Blocks = new(); public int Data, DSize, PoolAt, PoolSize;
    internal byte[]? File; byte[]? _pool;
    public ReadOnlySpan<byte> PoolRaw => File.AsSpan(PoolAt, PoolSize);
    public byte[] Pool => _pool ??= Kslf.Descramble(PoolRaw);
}

public sealed class Kslf
{
    public readonly byte[] Data;
    public readonly uint Version;
    public readonly (int Off, int N)[] Sections = new (int, int)[8];
    public readonly byte[] StringsBlob;
    List<TypeDef>? _types;
    List<KObject>? _objects;
    (uint Uid, int Type, int Obj)[]? _refs;

    public Kslf(byte[] data)
    {
        if (data.Length < 72 || B.U32(data, 0) != 0x464C534B) throw new InvalidDataException("not a KSLF file");
        Data = data;
        Version = B.U32(data, 4);
        for (int i = 0; i < 8; i++) Sections[i] = ((int)B.U32(data, 8 + 8 * i), (int)B.U32(data, 12 + 8 * i));
        var (off, size) = Sections[3];
        var raw = data.AsSpan(off, size);
        StringsBlob = Version == 2 ? Descramble(raw) : raw.ToArray();
    }

    public static byte[] Descramble(ReadOnlySpan<byte> b)
    {
        var o = new byte[b.Length];
        byte k = 0xCC;
        for (int i = 0; i < b.Length; i++)
        {
            byte v = (byte)(k - (b[i] ^ 0xB3));
            o[i] = v;
            k = (byte)(k + v - i);
        }
        return o;
    }

    public static byte[] Scramble(ReadOnlySpan<byte> plain)
    {
        var o = new byte[plain.Length];
        byte k = 0xCC;
        for (int i = 0; i < plain.Length; i++)
        {
            byte v = plain[i];
            o[i] = (byte)((byte)(k - v) ^ 0xB3);
            k = (byte)(k + v - i);
        }
        return o;
    }

    public List<TypeDef> Types => _types ??= LoadSchema();

    List<TypeDef> LoadSchema()
    {
        byte[] d = Data, S = StringsBlob;
        var (t0, tn) = Sections[0]; var (b0, _) = Sections[1]; var (f0, _) = Sections[2];
        var types = new List<TypeDef>(tn);
        int fi = 0, bi = 0;
        for (int i = 0; i < tn; i++)
        {
            int a = t0 + 12 * i;
            int nameOff = B.U16(d, a), nf = B.U16(d, a + 2), nb = B.U16(d, a + 4);
            var t = new TypeDef { Index = i, Name = B.Sz(S, nameOff), Kind = B.U16(d, a + 6), Unk = B.U16(d, a + 8), Size = B.U16(d, a + 10) };
            for (int j = 0; j < nf; j++)
            {
                int f = f0 + 8 * (fi + j);
                t.Fields.Add(new FieldDef { Name = B.Sz(S, B.U16(d, f)), Type = B.U16(d, f + 2), Offset = B.U16(d, f + 4), Flags = B.U16(d, f + 6) });
            }
            for (int j = 0; j < nb; j++) t.Bases.Add((B.U16(d, b0 + 4 * (bi + j)), B.U16(d, b0 + 4 * (bi + j) + 2)));
            fi += nf; bi += nb;
            types.Add(t);
        }
        return types;
    }

    public int TypeIndex(string name) => Types.FindIndex(t => t.Name == name);

    public List<FieldDef> AllFields(int t)
    {
        var ty = Types[t];
        if (ty.All != null) return ty.All;
        var o = new List<FieldDef>();
        foreach (var (b, off) in ty.Bases)
            foreach (var f in AllFields(b)) o.Add(new FieldDef { Name = f.Name, Type = f.Type, Offset = f.Offset + off, Flags = f.Flags });
        o.AddRange(ty.Fields);
        return ty.All = o;
    }

    public int FieldOffset(int t, string name)
    {
        foreach (var f in AllFields(t)) if (f.Name == name) return f.Offset;
        throw new KeyNotFoundException(name);
    }

    public List<KObject> Objects => _objects ??= LoadObjects();

    List<KObject> LoadObjects()
    {
        var d = Data; var types = Types;
        var (o5, n5) = Sections[5]; var (o6, _) = Sections[6]; var (o7, _) = Sections[7];
        var list = new List<KObject>(n5);
        int pos = o7, row = 0;
        for (int i = 0; i < n5; i++)
        {
            int a = o5 + 16 * i;
            uint oid = B.U32(d, a); int nrows = (int)B.U32(d, a + 4), dsize = (int)B.U32(d, a + 8), ssize = (int)B.U32(d, a + 12);
            var o = new KObject { Id = oid, Data = pos, DSize = dsize, PoolAt = pos + dsize, PoolSize = ssize, File = d };
            int p = pos;
            for (int r = row; r < row + nrows; r++)
            {
                int ra = o6 + 6 * r;
                int t = B.U16(d, ra), cnt = B.U16(d, ra + 2), kind = B.U16(d, ra + 4);
                int size = kind == 0 ? types[t].Size : 4;
                o.Blocks.Add(new Block { Type = t, Name = types[t].Name, Count = cnt, Kind = kind, Offset = p, Size = size });
                p = (p + size * cnt + 3) & ~3;
            }
            list.Add(o);
            pos += dsize + ssize;
            row += nrows;
        }
        return list;
    }

    /// section 4: uid table rows {uid, type, object index or 0xFFFF}
    public (uint Uid, int Type, int Obj)[] Refs
    {
        get
        {
            if (_refs != null) return _refs;
            var (o4, n4) = Sections[4];
            var r = new (uint, int, int)[n4];
            for (int i = 0; i < n4; i++) r[i] = (B.U32(Data, o4 + 8 * i), B.U16(Data, o4 + 8 * i + 4), B.U16(Data, o4 + 8 * i + 6));
            return _refs = r;
        }
    }

    public int RowOf(uint uid)
    {
        var (o4, n4) = Sections[4];
        for (int i = 0; i < n4; i++) if (B.U32(Data, o4 + 8 * i) == uid) return i;
        throw new KeyNotFoundException(uid.ToString("X8"));
    }

    public int ObjOf(uint uid)
    {
        foreach (var r in Refs) if (r.Uid == uid) return r.Obj;
        throw new KeyNotFoundException(uid.ToString("X8"));
    }

    // ---- dump.py: objects as plain values (Dictionary / List / string / numbers) ---------------------------------
    string? Str(KObject o, uint off)
    {
        var p = o.Pool;
        if (off == 0xFFFFFFFF || off >= p.Length) return null;
        return B.Sz(p, (int)off);
    }

    object? Ref(uint i)
    {
        if (i == 0xFFFFFFFF || i >= Refs.Length) return null;
        var (uid, t, oi) = Refs[(int)i];
        return new Dictionary<string, object?> { ["uid"] = uid.ToString("X8"), ["type"] = Types[t].Name, ["object"] = oi == 0xFFFF ? null : (long)oi };
    }

    object? Value(KObject obj, int t, int at, int depth)
    {
        var d = Data;
        if (t >= Types.Count) return "0x" + B.U32(d, at).ToString("X8");
        var ty = Types[t];
        switch (ty.Name)
        {
            case "sz8": return Str(obj, B.U32(d, at));
            case "i32": return (long)B.I32(d, at);
            case "u32": case "bool": case "LocId": return (long)B.U32(d, at);
            case "f32": return (double)B.F32(d, at);
            case "u8": return (long)d[at];
            case "i8": return (long)(sbyte)d[at];
            case "i16": return (long)B.I16(d, at);
            case "u16": return (long)B.U16(d, at);
            case "u64": return B.U64(d, at);
            case "f64": return B.F64(d, at);
        }
        if (ty.Fields.Count > 0 || ty.Bases.Count > 0) return Struct(obj, t, at, depth + 1);
        return "0x" + B.U32(d, at).ToString("X8");
    }

    public Dictionary<string, object?> Struct(KObject obj, int t, int at, int depth = 0)
    {
        var d = Data;
        var o = new Dictionary<string, object?>();
        foreach (var f in AllFields(t))
        {
            int a = at + f.Offset, fl = f.Flags;
            if (fl == 5) o[f.Name] = Ref(B.U32(d, a));
            else if (fl == 4) o[f.Name] = Owned(obj, B.U32(d, a), depth);
            else if (fl == 1) o[f.Name] = (long)B.U32(d, a);
            else if ((fl & 0x100) != 0)
            {
                int blk = B.U16(d, a), first = B.U16(d, a + 2); uint cnt = B.U32(d, a + 4);
                var items = new List<object?>();
                o[f.Name] = items;
                if (blk == 0xFFFF || cnt == 0) continue;
                var b = obj.Blocks[blk];
                for (int j = first; j < first + cnt; j++)
                {
                    int ea = b.Offset + j * b.Size;
                    if (b.Kind == 0) items.Add(depth < 6 ? Value(obj, b.Type, ea, depth) : "...");
                    else if ((fl & 0xF) == 4) items.Add(Owned(obj, B.U32(d, ea), depth));
                    else items.Add(Ref(B.U32(d, ea)));
                }
            }
            else o[f.Name] = depth < 6 ? Value(obj, f.Type, a, depth) : "...";
        }
        return o;
    }

    object? Owned(KObject obj, uint v, int depth)
    {
        if (v == 0xFFFFFFFF) return null;
        int blk = (int)(v & 0xFFFF), idx = (int)(v >> 16);
        if (blk >= obj.Blocks.Count) return "0x" + v.ToString("X8");
        var b = obj.Blocks[blk];
        if (depth >= 6) return "<" + b.Name + ">";
        var val = Struct(obj, b.Type, b.Offset + idx * b.Size, depth + 1);
        var o = new Dictionary<string, object?> { ["_type"] = b.Name };
        foreach (var kv in val) o[kv.Key] = kv.Value;
        return o;
    }

    public Dictionary<string, object?> Top(KObject obj)
    {
        var b = obj.Blocks[0];
        return Struct(obj, b.Type, b.Offset);
    }

    public string? Name(KObject o)
    {
        foreach (var f in AllFields(o.Blocks[0].Type))
            if (f.Name == "Name" && f.Flags != 5 && f.Flags != 4 && (f.Flags & 0x100) == 0 && Types[f.Type].Name == "sz8")
                return Str(o, B.U32(Data, o.Blocks[0].Offset + f.Offset));
        return null;
    }
}

public delegate void ClonePatch(ByteBuf data, ByteBuf pool, int[] blocks);

public sealed class EditCtx
{
    public List<TypeDef> Types = null!;
    public ByteBuf Data = null!;
    public List<int[]> Rows = null!;      // [type, count, kind]
    public ByteBuf Pool = null!;
    public bool PoolChanged;
    public int[] Blocks = null!;
    internal ByteBuf S4 = null!;
    int Size(int[] r) => r[2] == 0 ? Types[r[0]].Size : 4;

    internal (int[] Off, int End) Offsets()
    {
        var o = new int[Rows.Count]; int p = 0;
        for (int i = 0; i < Rows.Count; i++) { o[i] = p; p = (p + Size(Rows[i]) * Rows[i][1] + 3) & ~3; }
        return (o, p);
    }

    public void Insert(int block, int pos, ReadOnlySpan<byte> raw)
    {
        int size = Size(Rows[block]), cnt = Rows[block][1];
        if (raw.Length % size != 0) throw new ArgumentException("not whole elements");
        var (oldOff, _) = Offsets();
        int at = oldOff[block] + pos * size;
        int oldEnd = oldOff[block] + size * cnt, paddedOld = (oldEnd + 3) & ~3;
        Rows[block][1] += raw.Length / size;
        int newEnd = oldEnd + raw.Length, paddedNew = (newEnd + 3) & ~3;
        Data.Insert(at, raw);
        int curPad = paddedOld + raw.Length - newEnd, needPad = paddedNew - newEnd;
        if (curPad != needPad)
        {
            Data.Remove(newEnd, curPad);
            Data.Insert(newEnd, new byte[needPad]);
        }
        Blocks = Offsets().Off;
    }

    /// section-4 index of uid: its row if it is already there, else a new row (uid above all others)
    public int AddUid(uint uid, string typeName)
    {
        int cnt = S4.Len / 8;
        for (int i = 0; i < cnt; i++) if (S4.U32(8 * i) == uid) return i;
        uint last = S4.U32(8 * (cnt - 1));
        if (uid <= last) throw new InvalidOperationException($"uid must be above {last:X8}");
        int t = Types.FindIndex(ty => ty.Name == typeName);
        S4.AddU32(uid).AddU16((ushort)t).AddU16(0xFFFF);
        return cnt;
    }
}

public static class KslfWrite
{
    /// New file bytes: header + sections 0-3 unchanged, then the given raw bytes for sections 4-7.
    static byte[] Rebuild(Kslf k, ReadOnlySpan<byte> s4, ReadOnlySpan<byte> s5, ReadOnlySpan<byte> s6, ReadOnlySpan<byte> s7)
    {
        var o = new ByteBuf(k.Sections[4].Off + s4.Length + s5.Length + s6.Length + s7.Length + 16);
        o.Add(k.Data.AsSpan(0, k.Sections[4].Off));
        void One(int i, ReadOnlySpan<byte> raw, int count, ByteBuf b)
        {
            b.AlignZero(4);
            b.Put(8 + 8 * (4 + i), (uint)b.Len); b.Put(12 + 8 * (4 + i), (uint)count);
            b.Add(raw);
        }
        One(0, s4, s4.Length / 8, o); One(1, s5, s5.Length / 16, o); One(2, s6, s6.Length / 6, o); One(3, s7, s7.Length, o);
        return o.ToArray();
    }

    /// Clone top-level object srcIndex as newName under newUid (above every uid in the file). Returns the new file.
    public static byte[] CloneObject(byte[] d, int srcIndex, string? newName, uint? newUid, ClonePatch? patch, out uint uid)
    {
        var k = new Kslf(d);
        var (o4, n4) = k.Sections[4]; var (o5, n5) = k.Sections[5]; var (o6, n6) = k.Sections[6]; var (o7, n7) = k.Sections[7];
        var src = k.Objects[srcIndex];
        int nrows = (int)B.U32(d, o5 + 16 * srcIndex + 4), dsize = src.DSize;
        int firstRow = 0;
        for (int i = 0; i < srcIndex; i++) firstRow += (int)B.U32(d, o5 + 16 * i + 4);
        var data = new ByteBuf(d.AsSpan(src.Data, dsize));
        var pool = new ByteBuf(src.Pool);
        var b0 = src.Blocks[0];
        if (newName != null)
        {
            data.Put(b0.Offset - src.Data + 12, (uint)pool.Len);
            pool.AddSz(newName);
        }
        patch?.Invoke(data, pool, src.Blocks.Select(b => b.Offset - src.Data).ToArray());
        pool.AlignZero(4);
        uint last = B.U32(d, o4 + 8 * (n4 - 1));
        uid = newUid ?? last + 1;
        if (uid <= last) throw new InvalidOperationException("uid not above the last one");
        var s4 = new ByteBuf(d.AsSpan(o4, 8 * n4)); s4.AddU32(uid).AddU16((ushort)b0.Type).AddU16((ushort)n5);
        var s5 = new ByteBuf(d.AsSpan(o5, 16 * n5)); s5.AddU32((uint)n4).AddU32((uint)nrows).AddU32((uint)data.Len).AddU32((uint)pool.Len);
        var s6 = new ByteBuf(d.AsSpan(o6, 6 * n6)); s6.Add(d.AsSpan(o6 + 6 * firstRow, 6 * nrows));
        var s7 = new ByteBuf(n7 + data.Len + pool.Len);
        s7.Add(d.AsSpan(o7, n7)).Add(data.Span).Add(Kslf.Scramble(pool.Span));
        return Rebuild(k, s4.Span, s5.Span, s6.Span, s7.Span);
    }

    public static byte[] EditObject(byte[] d, int objIndex, Action<EditCtx> fn)
    {
        var k = new Kslf(d);
        var types = k.Types;
        var (o4, n4) = k.Sections[4]; var (o5, n5) = k.Sections[5]; var (o6, n6) = k.Sections[6]; var (o7, n7) = k.Sections[7];
        var rowsAll = new List<int[]>(n6);
        for (int i = 0; i < n6; i++) rowsAll.Add(new int[] { B.U16(d, o6 + 6 * i), B.U16(d, o6 + 6 * i + 2), B.U16(d, o6 + 6 * i + 4) });
        var s5rows = new uint[n5][];
        for (int i = 0; i < n5; i++) s5rows[i] = new[] { B.U32(d, o5 + 16 * i), B.U32(d, o5 + 16 * i + 4), B.U32(d, o5 + 16 * i + 8), B.U32(d, o5 + 16 * i + 12) };
        int first = 0;
        for (int i = 0; i < objIndex; i++) first += (int)s5rows[i][1];
        var obj = k.Objects[objIndex];
        int nrows = (int)s5rows[objIndex][1], dsize = obj.DSize, ssize = obj.PoolSize;
        var ctx = new EditCtx
        {
            Types = types, Data = new ByteBuf(d.AsSpan(obj.Data, dsize)),
            Rows = rowsAll.GetRange(first, nrows).Select(r => (int[])r.Clone()).ToList(),
            Pool = new ByteBuf(obj.Pool), S4 = new ByteBuf(d.AsSpan(o4, 8 * n4)),
        };
        ctx.Blocks = ctx.Offsets().Off;
        fn(ctx);
        if (ctx.Offsets().End != ctx.Data.Len) throw new InvalidOperationException("object size does not match its blocks");
        byte[] poolRaw;
        if (ctx.PoolChanged) { ctx.Pool.AlignZero(4); poolRaw = Kslf.Scramble(ctx.Pool.Span); }
        else poolRaw = obj.PoolRaw.ToArray();
        s5rows[objIndex] = new[] { s5rows[objIndex][0], (uint)ctx.Rows.Count, (uint)ctx.Data.Len, (uint)poolRaw.Length };
        rowsAll.RemoveRange(first, nrows); rowsAll.InsertRange(first, ctx.Rows);
        var s5 = new byte[16 * n5];
        for (int i = 0; i < n5; i++) for (int j = 0; j < 4; j++) B.Put(s5, 16 * i + 4 * j, s5rows[i][j]);
        var s6 = new byte[6 * rowsAll.Count];
        for (int i = 0; i < rowsAll.Count; i++) for (int j = 0; j < 3; j++) B.Put(s6, 6 * i + 2 * j, (ushort)rowsAll[i][j]);
        var s7 = new ByteBuf(n7 + ctx.Data.Len);
        s7.Add(d.AsSpan(o7, obj.Data - o7)).Add(ctx.Data.Span).Add(poolRaw).Add(d.AsSpan(obj.Data + dsize + ssize, o7 + n7 - (obj.Data + dsize + ssize)));
        return Rebuild(k, ctx.S4.Span, s5, s6, s7.Span);
    }

    /// absolute offsets of every reference slot (u32 index into section 4) in the file's object data
    public static int[] RefSlots(Kslf k)
    {
        var data = k.Data; var types = k.Types;
        var slots = new HashSet<int>();
        // per type: the reference layout, worked out once (the Python walk redoes it for every element)
        void Walk(KObject obj, int t, int at, int depth)
        {
            if (t >= types.Count || depth > 40) return;
            foreach (var f in k.AllFields(t))
            {
                int a = at + f.Offset, fl = f.Flags;
                if (fl == 5) slots.Add(a);
                else if ((fl & 0x100) != 0)
                {
                    if ((fl & 0xF) != 5) continue;
                    int blk = B.U16(data, a), first = B.U16(data, a + 2); uint cnt = B.U32(data, a + 4);
                    if (blk == 0xFFFF || cnt == 0 || blk >= obj.Blocks.Count) continue;
                    var b = obj.Blocks[blk];
                    if (b.Kind != 0) for (int j = first; j < first + cnt; j++) slots.Add(b.Offset + 4 * j);
                }
                else if ((fl == 0 || fl == 3) && f.Type < types.Count && (types[f.Type].Fields.Count > 0 || types[f.Type].Bases.Count > 0))
                {
                    if (types[f.Type].Kind != 1) Walk(obj, f.Type, a, depth + 1);
                }
            }
        }
        foreach (var obj in k.Objects)
            foreach (var b in obj.Blocks)
                if (b.Kind == 0) for (int e = 0; e < b.Count; e++) Walk(obj, b.Type, b.Offset + e * b.Size, 0);
        var arr = slots.ToArray(); Array.Sort(arr);
        return arr;
    }

    /// the LAST section-4 row moves to its sorted place; all references renumbered. Returns its index.
    static byte[] MoveLastRow(byte[] data, out int pos)
    {
        var d = (byte[])data.Clone();
        var k = new Kslf(data);
        var (o4, n4) = k.Sections[4]; var (o5, n5) = k.Sections[5];
        int last = n4 - 1;
        uint uid = B.U32(d, o4 + 8 * last);
        pos = 0;
        while (pos < last && B.U32(d, o4 + 8 * pos) < uid) pos++;
        if (pos == last) return d;
        if (B.U32(d, o4 + 8 * pos) == uid) throw new InvalidOperationException($"uid {uid:X8} is already there");
        int p0 = pos;
        uint Remap(uint v) => v == last ? (uint)p0 : (p0 <= v && v < last) ? v + 1 : v;
        foreach (int a in RefSlots(k))
        {
            uint v = B.U32(d, a);
            if (v != 0xFFFFFFFF && v < n4) B.Put(d, a, Remap(v));
        }
        for (int i = 0; i < n5; i++) B.Put(d, o5 + 16 * i, Remap(B.U32(d, o5 + 16 * i)));
        var row = d.AsSpan(o4 + 8 * last, 8).ToArray();
        Buffer.BlockCopy(d, o4 + 8 * pos, d, o4 + 8 * (pos + 1), 8 * (last - pos));
        row.CopyTo(d, o4 + 8 * pos);
        return d;
    }

    /// add a section-4 row at its sorted place (any uid not used yet); an existing uid returns its row unchanged
    public static byte[] InsertUid(byte[] data, uint uid, string typeName, out int row, int obj = 0xFFFF)
    {
        var k = new Kslf(data);
        var (o4, n4) = k.Sections[4];
        for (int i = 0; i < n4; i++) if (B.U32(data, o4 + 8 * i) == uid) { row = i; return data; }
        int t = k.TypeIndex(typeName);
        var s4 = new ByteBuf(data.AsSpan(o4, 8 * n4)); s4.AddU32(uid).AddU16((ushort)t).AddU16((ushort)obj);
        var (o5, n5) = k.Sections[5]; var (o6, n6) = k.Sections[6]; var (o7, n7) = k.Sections[7];
        var o = Rebuild(k, s4.Span, data.AsSpan(o5, 16 * n5), data.AsSpan(o6, 6 * n6), data.AsSpan(o7, n7));
        return MoveLastRow(o, out row);
    }

    /// clone with ANY new uid (not only above the highest one): appended, then moved to its sorted place
    public static byte[] CloneObjectAny(byte[] dataIn, int srcIndex, string? newName, uint newUid, ClonePatch? patch)
    {
        var k = new Kslf(dataIn);
        var (o4, n4) = k.Sections[4];
        uint last = B.U32(dataIn, o4 + 8 * (n4 - 1));
        if (newUid > last) return CloneObject(dataIn, srcIndex, newName, newUid, patch, out _);
        const uint tmp = 0xFFFFFFFF;
        if (last >= tmp) throw new InvalidOperationException("the top uid is taken");
        var d = CloneObject(dataIn, srcIndex, newName, tmp, patch, out _);
        var k2 = new Kslf(d);
        (o4, n4) = k2.Sections[4];
        for (int i = 0; i < n4; i++) if (B.U32(d, o4 + 8 * i) == newUid) throw new InvalidOperationException($"uid {newUid:X8} is already there");
        B.Put(d, o4 + 8 * (n4 - 1), newUid);
        return MoveLastRow(d, out _);
    }
}

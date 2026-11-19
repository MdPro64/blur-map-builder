// KSLF objects from value trees, and new types copied in from another KSLF file.
// The tree is the same shape Kslf.Top gives: a Dictionary per struct, "_type" on owned (pointed-to) objects,
// {"uid": "X8", "type": name} for references, lists for arrays. A Dictionary used twice (same instance) is
// written once and pointed at twice (actors that point at each other, e.g. a trigger and its target).
namespace Bmb.Core;

public static class KslfBuild
{
    // ---- read a whole object (no depth limit, shared objects stay shared) --------------------------------------
    public static Dictionary<string, object?> ReadTree(Kslf k, int objIndex) => ReadTree(k, objIndex, out _);

    /// byBlock: every pointed-to object by (block, element), e.g. the k-th DynamicObjActor of the list
    public static Dictionary<string, object?> ReadTree(Kslf k, int objIndex, out Dictionary<(int Blk, int Idx), Dictionary<string, object?>> byBlock)
    {
        var obj = k.Objects[objIndex];
        var cache = byBlock = new Dictionary<(int, int), Dictionary<string, object?>>();
        var d = k.Data;
        object? Ref(uint i)
        {
            if (i == 0xFFFFFFFF || i >= k.Refs.Length) return null;
            var (uid, t, _) = k.Refs[(int)i];
            return new Dictionary<string, object?> { ["uid"] = uid.ToString("X8"), ["type"] = k.Types[t].Name };
        }
        object? Owned(uint v)
        {
            if (v == 0xFFFFFFFF) return null;
            int blk = (int)(v & 0xFFFF), idx = (int)(v >> 16);
            if (cache.TryGetValue((blk, idx), out var c)) return c;
            var b = obj.Blocks[blk];
            var o = new Dictionary<string, object?> { ["_type"] = b.Name };
            cache[(blk, idx)] = o;
            Fill(o, b.Type, b.Offset + idx * b.Size);
            return o;
        }
        object? Value(int t, int at)
        {
            if (t >= k.Types.Count) return "0x" + B.U32(d, at).ToString("X8");
            var ty = k.Types[t];
            switch (ty.Name)
            {
                case "sz8": { uint off = B.U32(d, at); var p = obj.Pool; return off == 0xFFFFFFFF || off >= p.Length ? null : B.Sz(p, (int)off); }
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
            if (ty.Fields.Count > 0 || ty.Bases.Count > 0) { var o = new Dictionary<string, object?>(); Fill(o, t, at); return o; }
            return "0x" + B.U32(d, at).ToString("X8");
        }
        void Fill(Dictionary<string, object?> o, int t, int at)
        {
            foreach (var f in k.AllFields(t))
            {
                int a = at + f.Offset, fl = f.Flags;
                if (fl == 5) o[f.Name] = Ref(B.U32(d, a));
                else if (fl == 4) o[f.Name] = Owned(B.U32(d, a));
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
                        if (b.Kind == 0) items.Add(Value(b.Type, ea));
                        else if ((fl & 0xF) == 4) items.Add(Owned(B.U32(d, ea)));
                        else items.Add(Ref(B.U32(d, ea)));
                    }
                }
                else o[f.Name] = Value(f.Type, a);
            }
        }
        var top = new Dictionary<string, object?> { ["_type"] = obj.Blocks[0].Name };
        cache[(0, 0)] = top;
        Fill(top, obj.Blocks[0].Type, obj.Blocks[0].Offset);
        return top;
    }

    // ---- write an object ---------------------------------------------------------------------------------------
    sealed class Blk { public int Type, Kind, Size; public List<byte[]> Elems = new(); }

    /// (rows [type, count, kind], data, plain pool) of a new object whose top is `top` (its "_type" = block 0's type).
    /// rowOf: section-4 row of a referenced uid.
    public static (List<int[]> Rows, byte[] Data, byte[] Pool) Write(Kslf k, Dictionary<string, object?> top, Func<uint, int> rowOf)
    {
        var types = k.Types;
        var blocks = new List<Blk>();
        var pool = new ByteBuf(64);
        var strAt = new Dictionary<string, int>();
        var placed = new Dictionary<Dictionary<string, object?>, uint>(ReferenceEqualityComparer.Instance);
        var fills = new Queue<Action>();
        int TypeOf(string name) { int t = k.TypeIndex(name); if (t < 0) throw new InvalidDataException($"type {name} is not in the file's schema"); return t; }
        Blk BlockFor(int t, int kind, out int bi)
        {
            bi = blocks.FindIndex(b => b.Type == t && b.Kind == kind);
            if (bi >= 0) return blocks[bi];
            var nb = new Blk { Type = t, Kind = kind, Size = kind == 0 ? types[t].Size : 4 };
            blocks.Add(nb); bi = blocks.Count - 1;
            return nb;
        }
        uint Str(string? s)
        {
            if (s == null) return 0xFFFFFFFF;
            if (strAt.TryGetValue(s, out int at)) return (uint)at;
            at = pool.Len; pool.AddSz(s); strAt[s] = at; return (uint)at;
        }
        uint Place(Dictionary<string, object?>? o)
        {
            if (o == null) return 0xFFFFFFFF;
            if (placed.TryGetValue(o, out uint p)) return p;
            int t = TypeOf((string)o["_type"]!);
            var b = BlockFor(t, 0, out int bi);
            var e = new byte[b.Size]; b.Elems.Add(e);
            p = (uint)bi | ((uint)(b.Elems.Count - 1) << 16);
            placed[o] = p;
            fills.Enqueue(() => FillStruct(e, 0, t, o));
            return p;
        }
        uint RefRow(object? v) => v is Dictionary<string, object?> r && r.GetValueOrDefault("uid") is string u ? (uint)rowOf(Convert.ToUInt32(u, 16)) : 0xFFFFFFFF;
        void PutScalar(byte[] e, int a, int t, object? v)
        {
            var ty = types[t];
            switch (ty.Name)
            {
                case "sz8": B.Put(e, a, Str(v as string)); return;
                case "i32": B.Put(e, a, (int)Convert.ToInt64(v ?? 0L)); return;
                case "u32": case "bool": case "LocId": B.Put(e, a, (uint)Convert.ToInt64(v ?? 0L)); return;
                case "f32": B.Put(e, a, (float)Convert.ToDouble(v ?? 0.0)); return;
                case "u8": e[a] = (byte)Convert.ToInt64(v ?? 0L); return;
                case "i8": e[a] = (byte)(sbyte)Convert.ToInt64(v ?? 0L); return;
                case "i16": B.Put(e, a, (ushort)(short)Convert.ToInt64(v ?? 0L)); return;
                case "u16": B.Put(e, a, (ushort)Convert.ToInt64(v ?? 0L)); return;
                case "u64": B.Put(e, a, Convert.ToUInt64(v ?? 0UL)); return;
                case "f64": B.Put(e, a, BitConverter.DoubleToUInt64Bits(Convert.ToDouble(v ?? 0.0))); return;
            }
            if (ty.Fields.Count > 0 || ty.Bases.Count > 0) { FillStruct(e, a, t, v as Dictionary<string, object?> ?? new()); return; }
            B.Put(e, a, v is string s && s.StartsWith("0x") ? Convert.ToUInt32(s[2..], 16) : 0u);
        }
        void FillStruct(byte[] e, int at, int t, Dictionary<string, object?> o)
        {
            foreach (var f in k.AllFields(t))
            {
                int a = at + f.Offset, fl = f.Flags;
                var v = o.GetValueOrDefault(f.Name);
                if (fl == 5) B.Put(e, a, RefRow(v));
                else if (fl == 4) B.Put(e, a, Place(v as Dictionary<string, object?>));
                else if (fl == 1) B.Put(e, a, (uint)Convert.ToInt64(v ?? 0L));
                else if ((fl & 0x100) != 0)
                {
                    var items = (v as System.Collections.IList)?.Cast<object?>().ToList() ?? new List<object?>();
                    if (items.Count == 0) { B.Put(e, a, 0xFFFFFFFFu); B.Put(e, a + 4, 0u); continue; }
                    int kind = (fl & 0xF) == 4 ? 1 : (fl & 0xF) == 5 ? 2 : 0;
                    var b = BlockFor(f.Type, kind, out int bi);
                    int first = b.Elems.Count;
                    var slots = items.Select(_ => new byte[b.Size]).ToList();
                    b.Elems.AddRange(slots);
                    B.Put(e, a, (ushort)bi); B.Put(e, a + 2, (ushort)first); B.Put(e, a + 4, (uint)items.Count);
                    for (int j = 0; j < items.Count; j++)
                    {
                        var s = slots[j]; var it = items[j];
                        if (kind == 1) B.Put(s, 0, Place(it as Dictionary<string, object?>));
                        else if (kind == 2) B.Put(s, 0, RefRow(it));
                        else { int ft = f.Type; fills.Enqueue(() => PutScalar(s, 0, ft, it)); }
                    }
                }
                else PutScalar(e, a, f.Type, v);
            }
        }
        Place(top);
        while (fills.Count > 0) fills.Dequeue()();
        var data = new ByteBuf(1024); var rows = new List<int[]>();
        foreach (var b in blocks)
        {
            foreach (var el in b.Elems) data.Add(el);
            data.AlignZero(4);
            rows.Add(new[] { b.Type, b.Elems.Count, b.Kind });
        }
        pool.AlignZero(4);
        return (rows, data.ToArray(), pool.ToArray());
    }

    // ---- whole-file rewrite (sections in order, as the game writes them) ----------------------------------------
    static byte[] Assemble(uint version, byte[][] sec, int[] counts)
    {
        var o = new ByteBuf(72 + sec.Sum(s => s.Length) + 64);
        o.AddU32(0x464C534B).AddU32(version);
        for (int i = 0; i < 8; i++) o.AddU32(0).AddU32(0);
        for (int i = 0; i < 8; i++)
        {
            o.AlignZero(4);
            o.Put(8 + 8 * i, (uint)o.Len); o.Put(12 + 8 * i, (uint)counts[i]);
            o.Add(sec[i]);
        }
        return o.ToArray();
    }

    static byte[] Sec(Kslf k, int i, int elem) { var (off, n) = k.Sections[i]; return k.Data.AsSpan(off, elem * n).ToArray(); }

    /// a copy of `file` whose schema also has the named types of `donor` (and every type they need). Types already in
    /// the file (same name) are kept as they are; new ones go at the end, so nothing in the file changes meaning.
    public static byte[] AddTypes(byte[] file, byte[] donor, IEnumerable<string> names)
    {
        var K = new Kslf(file); var D = new Kslf(donor);
        var have = new Dictionary<string, int>();
        for (int i = 0; i < K.Types.Count; i++) have.TryAdd(K.Types[i].Name, i);
        var order = new List<int>(); var seen = new HashSet<int>();
        void Need(int t)
        {
            if (t < 0 || t >= D.Types.Count || !seen.Add(t)) return;
            if (have.ContainsKey(D.Types[t].Name)) return;
            foreach (var (b, _) in D.Types[t].Bases) Need(b);
            foreach (var f in D.Types[t].Fields) Need(f.Type);
            order.Add(t);
        }
        foreach (var n in names) { int t = D.TypeIndex(n); if (t < 0) throw new InvalidDataException($"donor has no type {n}"); Need(t); }
        if (order.Count == 0) return file;
        int baseN = K.Types.Count;
        var map = new Dictionary<int, int>();
        for (int i = 0; i < order.Count; i++) map[order[i]] = baseN + i;
        int Map(int dt) => dt >= D.Types.Count ? dt : map.TryGetValue(dt, out int m) ? m : have[D.Types[dt].Name];
        var strs = new ByteBuf(K.StringsBlob); var sAt = new Dictionary<string, int>();
        ushort S(string s)
        {
            if (!sAt.TryGetValue(s, out int at)) { at = strs.Len; strs.AddSz(s); sAt[s] = at; }
            if (at > 0xFFFF) throw new InvalidDataException("schema strings too long");
            return (ushort)at;
        }
        var s0 = new ByteBuf(Sec(K, 0, 12)); var s1 = new ByteBuf(Sec(K, 1, 4)); var s2 = new ByteBuf(Sec(K, 2, 8));
        foreach (int dt in order)
        {
            var ty = D.Types[dt];
            s0.AddU16(S(ty.Name)).AddU16((ushort)ty.Fields.Count).AddU16((ushort)ty.Bases.Count).AddU16((ushort)ty.Kind).AddU16((ushort)ty.Unk).AddU16((ushort)ty.Size);
            foreach (var f in ty.Fields) s2.AddU16(S(f.Name)).AddU16((ushort)Map(f.Type)).AddU16((ushort)f.Offset).AddU16((ushort)f.Flags);
            foreach (var (b, off) in ty.Bases) s1.AddU16((ushort)Map(b)).AddU16((ushort)off);
        }
        strs.AlignZero(4);
        var s3 = K.Version == 2 ? Kslf.Scramble(strs.Span) : strs.ToArray();
        var sec = new[] { s0.ToArray(), s1.ToArray(), s2.ToArray(), s3, Sec(K, 4, 8), Sec(K, 5, 16), Sec(K, 6, 6), Sec(K, 7, 1) };
        var counts = new[] { s0.Len / 12, s1.Len / 4, s2.Len / 8, s3.Length, K.Sections[4].N, K.Sections[5].N, K.Sections[6].N, K.Sections[7].N };
        return Assemble(K.Version, sec, counts);
    }

    /// a copy of `file` where top-level object `objIndex` is rewritten from `top` (same uid; referenced uids must be listed)
    public static byte[] ReplaceObject(byte[] file, int objIndex, Dictionary<string, object?> top)
    {
        var K = new Kslf(file);
        var rowOf = new Dictionary<uint, int>();
        for (int i = 0; i < K.Refs.Length; i++) rowOf[K.Refs[i].Uid] = i;
        var (rows, data, pool) = Write(K, top, u => rowOf.TryGetValue(u, out int r) ? r : throw new KeyNotFoundException($"uid {u:X8} is not listed"));
        var (o5, n5) = K.Sections[5]; var (o6, n6) = K.Sections[6];
        var s5 = Sec(K, 5, 16);
        int first = 0;
        for (int i = 0; i < objIndex; i++) first += (int)B.U32(s5, 16 * i + 4);
        int oldRows = (int)B.U32(s5, 16 * objIndex + 4);
        B.Put(s5, 16 * objIndex + 4, (uint)rows.Count); B.Put(s5, 16 * objIndex + 8, (uint)data.Length); B.Put(s5, 16 * objIndex + 12, (uint)pool.Length);
        var s6 = new ByteBuf(6 * (n6 + rows.Count));
        s6.Add(K.Data.AsSpan(o6, 6 * first));
        foreach (var r in rows) s6.AddU16((ushort)r[0]).AddU16((ushort)r[1]).AddU16((ushort)r[2]);
        s6.Add(K.Data.AsSpan(o6 + 6 * (first + oldRows), 6 * (n6 - first - oldRows)));
        var ob = K.Objects[objIndex]; var (o7, n7) = K.Sections[7];
        var s7 = new ByteBuf(n7 + data.Length);
        s7.Add(K.Data.AsSpan(o7, ob.Data - o7)).Add(data).Add(Kslf.Scramble(pool));
        int after = ob.Data + ob.DSize + ob.PoolSize;
        s7.Add(K.Data.AsSpan(after, o7 + n7 - after));
        var sec = new[] { Sec(K, 0, 12), Sec(K, 1, 4), Sec(K, 2, 8), Sec(K, 3, 1), Sec(K, 4, 8), s5, s6.ToArray(), s7.ToArray() };
        var counts = new[] { K.Sections[0].N, K.Sections[1].N, K.Sections[2].N, K.Sections[3].N, K.Sections[4].N, n5, s6.Len / 6, s7.Len };
        return Assemble(K.Version, sec, counts);
    }

    /// a copy of `file` with one more top-level object (built from `top`) under `uid`. Referenced uids that the file does
    /// not list yet are added first (as references of `refType(uid)`).
    public static byte[] AddObject(byte[] file, Dictionary<string, object?> top, uint uid, Func<uint, string>? refType = null)
    {
        // every referenced uid must have a section-4 row
        var refs = new List<(uint, string)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object? v)
        {
            if (v is Dictionary<string, object?> d)
            {
                if (!seen.Add(d)) return;
                if (d.Count == 2 && d.GetValueOrDefault("uid") is string u && d.GetValueOrDefault("type") is string tn) refs.Add((Convert.ToUInt32(u, 16), tn));
                foreach (var x in d.Values) Walk(x);
            }
            else if (v is System.Collections.IList l) foreach (var x in l) Walk(x);
        }
        Walk(top);
        foreach (var (u, tn) in refs) file = KslfWrite.InsertUid(file, u, refType?.Invoke(u) ?? tn, out _);
        var K = new Kslf(file);
        var rowOf = new Dictionary<uint, int>();
        for (int i = 0; i < K.Refs.Length; i++) rowOf[K.Refs[i].Uid] = i;
        if (rowOf.ContainsKey(uid)) throw new InvalidOperationException($"uid {uid:X8} is already used");
        var (rows, data, pool) = Write(K, top, u => rowOf[u]);
        int n4 = K.Sections[4].N, n5 = K.Sections[5].N;
        var s4 = new ByteBuf(Sec(K, 4, 8)); s4.AddU32(0xFFFFFFFF).AddU16((ushort)rows[0][0]).AddU16((ushort)n5);
        var s5 = new ByteBuf(Sec(K, 5, 16)); s5.AddU32((uint)n4).AddU32((uint)rows.Count).AddU32((uint)data.Length).AddU32((uint)pool.Length);
        var s6 = new ByteBuf(Sec(K, 6, 6)); foreach (var r in rows) s6.AddU16((ushort)r[0]).AddU16((ushort)r[1]).AddU16((ushort)r[2]);
        var s7 = new ByteBuf(Sec(K, 7, 1)); s7.Add(data).Add(Kslf.Scramble(pool));
        if (B.U32(Sec(K, 4, 8), 8 * (n4 - 1)) == 0xFFFFFFFF) throw new InvalidOperationException("the top uid is taken");
        var sec = new[] { Sec(K, 0, 12), Sec(K, 1, 4), Sec(K, 2, 8), Sec(K, 3, 1), s4.ToArray(), s5.ToArray(), s6.ToArray(), s7.ToArray() };
        var counts = new[] { K.Sections[0].N, K.Sections[1].N, K.Sections[2].N, K.Sections[3].N, n4 + 1, n5 + 1, s6.Len / 6, s7.Len };
        var o = Assemble(K.Version, sec, counts);
        var k2 = new Kslf(o);
        B.Put(o, k2.Sections[4].Off + 8 * n4, uid);
        return KslfWrite.MoveLastRowPublic(o);
    }
}

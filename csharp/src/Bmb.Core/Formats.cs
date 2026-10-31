// Smaller formats: .loc text, .aid race layout, DXT1, .bpr picture bundle. Ports of kslf/loc.py, aid/aid.py, tex/dxt.py, ui/bpr.py.
using System.Text;
using System.Text.RegularExpressions;

namespace Bmb.Core;

public static class Loc
{
    const int Base = 0x84;

    public static List<(string Name, int Off, int Size)> Blocks(byte[] d)
    {
        var o = new List<(string, int, int)> { ("keys", Base + (int)B.U32(d, 8), (int)B.U32(d, 12)) };
        int n = (int)B.U32(d, 0x14);
        for (int i = 0; i < n; i++)
        {
            int e = 0x1C + 0x18 * i;
            string code = B.Latin1.GetString(d, e, 4).TrimEnd('\0');
            o.Add((code, (int)B.U32(d, e + 0x10), (int)B.U32(d, e + 0x14)));
        }
        return o;
    }

    static Dictionary<uint, string> ReadBlock(byte[] d, int b, bool wide)
    {
        int n = (int)B.U32(d, b), hdr = (int)B.U32(d, b + 4);
        var t = new Dictionary<uint, string>(n);
        for (int j = 0; j < n; j++)
        {
            uint h = B.U32(d, b + hdr + 8 * j); int p = b + (int)B.U32(d, b + hdr + 8 * j + 4);
            if (wide)
            {
                int q = p;
                while (d[q] != 0 || d[q + 1] != 0) q += 2;
                t[h] = Encoding.Unicode.GetString(d, p, q - p);
            }
            else t[h] = B.Sz(d, p);
        }
        return t;
    }

    public static Dictionary<string, Dictionary<uint, string>> Read(byte[] d)
    {
        var o = new Dictionary<string, Dictionary<uint, string>>();
        foreach (var (name, b, _) in Blocks(d)) o[name] = ReadBlock(d, b, name != "keys");
        return o;
    }

    static byte[] Block(Dictionary<uint, string> entries, bool wide, List<uint> order)
    {
        int n = order.Count, bas = 8 + 8 * n;
        var table = new ByteBuf(n * 8); var strings = new ByteBuf(n * 16);
        foreach (uint h in order)
        {
            table.AddU32(h).AddU32((uint)(bas + strings.Len));
            if (wide) strings.Add(Encoding.Unicode.GetBytes(entries[h])).Add((byte)0).Add((byte)0);
            else strings.AddSz(entries[h]);
        }
        var o = new ByteBuf(table.Len + strings.Len + 8);
        return o.AddU32((uint)n).AddU32(8).Add(table.Span).Add(strings.Span).ToArray();
    }

    /// Rebuild a .loc. add = (hash, key, text); change = {hash: text}.
    public static byte[] Write(byte[] d, IEnumerable<(uint Hash, string Key, string Text)>? add, IEnumerable<KeyValuePair<uint, string>>? change)
    {
        var bl = Blocks(d);
        int kb = bl[0].Off;
        int n = (int)B.U32(d, kb);
        var keys = new List<(uint H, string K)>();
        for (int j = 0; j < n; j++)
            keys.Add((B.U32(d, kb + 8 + 8 * j), B.Sz(d, kb + (int)B.U32(d, kb + 8 + 8 * j + 4))));
        var codes = bl.Skip(1).Select(b => b.Name).ToList();
        var langs = new Dictionary<string, Dictionary<uint, string>>();
        foreach (var b in bl.Skip(1)) langs[b.Name] = ReadBlock(d, b.Off, true);
        foreach (var (h, key, text) in add ?? Array.Empty<(uint, string, string)>())
        {
            if (langs[codes[0]].ContainsKey(h)) throw new InvalidOperationException("hash already used");
            keys.Add((h, key));
            foreach (var c in codes) langs[c][h] = text;
        }
        foreach (var kv in change ?? Array.Empty<KeyValuePair<uint, string>>())
            foreach (var c in codes) langs[c][kv.Key] = kv.Value;
        keys = keys.OrderBy(x => x.K, StringComparer.Ordinal).ToList();
        var keyDict = new Dictionary<uint, string>();
        foreach (var (h, k) in keys) keyDict[h] = k;
        var keyblock = Block(keyDict, false, keys.Select(x => x.H).ToList());
        var o = new ByteBuf(d.AsSpan(0, Base + (int)B.U32(d, 8)));
        o.Add(keyblock);
        o.Put(12, (uint)(o.Len - 0x10));
        for (int i = 0; i < codes.Count; i++)
        {
            int start = o.Len;
            var ord = langs[codes[i]].Keys.OrderBy(x => x).ToList();
            o.Add(Block(langs[codes[i]], true, ord));
            o.Put(0x1C + 0x18 * i + 0x10, (uint)start); o.Put(0x1C + 0x18 * i + 0x14, (uint)(o.Len - start));
        }
        return o.ToArray();
    }
}

public sealed record AidSpline(string Name, int Rec, uint Flags, int Count, int First);

public static class Aid
{
    public const int Point = 0x143;
    static readonly Regex Rx = new(@"([\x03-\x20])\x00\x00\x00([A-Za-z][A-Za-z0-9_]{2,31})\x00{8}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<AidSpline> Splines(byte[] d)
    {
        var o = new List<AidSpline>();
        string s = B.Latin1.GetString(d);
        foreach (Match m in Rx.Matches(s))
        {
            if (m.Groups[2].Length != m.Groups[1].Value[0]) continue;
            int end = m.Index + m.Length;
            o.Add(new AidSpline(m.Groups[2].Value, m.Index, B.U32(d, end), (int)B.U32(d, end + 4), end + 8));
        }
        return o;
    }

    public static float[] PointAt(ReadOnlySpan<byte> d, int at)
    {
        var o = new float[11];
        for (int i = 0; i < 11; i++) o[i] = B.F32(d, at + 4 * i);
        return o;
    }

    static List<(uint, string)>[]? ParseLinks(byte[] d, int s, int n, out int end)
    {
        var lists = new List<(uint, string)>[n]; int a = s; end = 0;
        for (int i = 0; i < n; i++)
        {
            if (a + 4 > d.Length) return null;
            uint c = B.U32(d, a); a += 4;
            if (c > 64) return null;
            var cur = new List<(uint, string)>();
            for (int j = 0; j < c; j++)
            {
                if (a + 8 > d.Length) return null;
                uint idx = B.U32(d, a), ln = B.U32(d, a + 4);
                if (ln < 1 || ln > 32 || a + 8 + ln > d.Length) return null;
                for (int k = 0; k < ln; k++) { byte x = d[a + 8 + k]; if (x < 48 || x >= 123) return null; }
                cur.Add((idx, B.Latin1.GetString(d, a + 8, (int)ln)));
                a += 8 + (int)ln;
            }
            lists[i] = cur;
        }
        end = a;
        return lists;
    }

    public static (int Start, List<(uint, string)>[] Lists) LinkTable(byte[] d, AidSpline logic)
    {
        int n = logic.Count;
        for (int s = logic.First + n * Point; s < d.Length; s++)
        {
            var r = ParseLinks(d, s, n, out int end);
            if (r != null && end == d.Length) return (s, r);
        }
        throw new InvalidDataException("link table not found");
    }

    public static byte[] WriteLinks(IEnumerable<List<(uint Idx, string Name)>> lists)
    {
        var o = new ByteBuf(4096);
        foreach (var cur in lists)
        {
            o.AddU32((uint)cur.Count);
            foreach (var (idx, name) in cur) o.AddU32(idx).AddU32((uint)name.Length).Add(B.Bytes(name));
        }
        return o.ToArray();
    }
}

public static class Dxt
{
    public static byte[] Dxt1Decode(ReadOnlySpan<byte> data, int w, int h)
    {
        var px = new byte[w * h * 3];
        Span<byte> pal = stackalloc byte[12];
        int i = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
            {
                int c0 = B.U16(data, i), c1 = B.U16(data, i + 2); uint bits = B.U32(data, i + 4);
                i += 8;
                Pal(c0, c1, pal, c0 > c1);
                for (int k = 0; k < 16; k++)
                {
                    int x = bx + k % 4, y = by + k / 4;
                    if (x < w && y < h)
                    {
                        int j = (int)(bits >> (2 * k) & 3) * 3, o = (y * w + x) * 3;
                        px[o] = pal[j]; px[o + 1] = pal[j + 1]; px[o + 2] = pal[j + 2];
                    }
                }
            }
        return px;
    }

    static void Pal(int c0, int c1, Span<byte> p, bool four)
    {
        p[0] = (byte)((c0 >> 11 & 31) * 255 / 31); p[1] = (byte)((c0 >> 5 & 63) * 255 / 63); p[2] = (byte)((c0 & 31) * 255 / 31);
        p[3] = (byte)((c1 >> 11 & 31) * 255 / 31); p[4] = (byte)((c1 >> 5 & 63) * 255 / 63); p[5] = (byte)((c1 & 31) * 255 / 31);
        for (int t = 0; t < 3; t++)
        {
            if (four) { p[6 + t] = (byte)((2 * p[t] + p[3 + t]) / 3); p[9 + t] = (byte)((p[t] + 2 * p[3 + t]) / 3); }
            else { p[6 + t] = (byte)((p[t] + p[3 + t]) / 2); p[9 + t] = 0; }
        }
    }

    static int To565(int r, int g, int b) => (r * 31 / 255) << 11 | (g * 63 / 255) << 5 | (b * 31 / 255);

    /// simple DXT1 encoder: same choices as tex/dxt.py (byte-identical output), each row of blocks on its own core
    public static byte[] Dxt1Encode(ReadOnlySpan<byte> px0, int w, int h)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        var o = new byte[bw * bh * 8];
        byte[] src = px0.ToArray();
        Parallel.For(0, bh, row =>
        {
            Span<byte> pal = stackalloc byte[12];
            Span<int> block = stackalloc int[48];
            for (int col = 0; col < bw; col++)
            {
                int bx = col * 4, by = row * 4, at = (row * bw + col) * 8;
                int loK = int.MaxValue, hiK = int.MinValue, lo = 0, hi = 0;
                for (int k = 0; k < 16; k++)
                {
                    int x = Math.Min(bx + k % 4, w - 1), y = Math.Min(by + k / 4, h - 1), s = (y * w + x) * 3;
                    int r = src[s], g = src[s + 1], b = src[s + 2];
                    block[k * 3] = r; block[k * 3 + 1] = g; block[k * 3 + 2] = b;
                    int key = r * 3 + g * 6 + b;
                    if (key < loK) { loK = key; lo = k; }       // first of equals, as min()
                    if (key > hiK) { hiK = key; hi = k; }       // first of equals, as max()
                }
                int c0 = To565(block[hi * 3], block[hi * 3 + 1], block[hi * 3 + 2]);
                int c1 = To565(block[lo * 3], block[lo * 3 + 1], block[lo * 3 + 2]);
                if (c0 < c1) (c0, c1) = (c1, c0);
                B.Put(o, at, (ushort)c0); B.Put(o, at + 2, (ushort)c1);
                if (c0 == c1) { B.Put(o, at + 4, 0u); continue; }
                Pal(c0, c1, pal, true);
                uint bits = 0;
                for (int k = 0; k < 16; k++)
                {
                    int best = 0, bd = int.MaxValue;
                    for (int j = 0; j < 4; j++)
                    {
                        int dr = block[k * 3] - pal[j * 3], dg = block[k * 3 + 1] - pal[j * 3 + 1], db = block[k * 3 + 2] - pal[j * 3 + 2];
                        int dist = dr * dr + dg * dg + db * db;
                        if (dist < bd) { bd = dist; best = j; }
                    }
                    bits |= (uint)best << (2 * k);
                }
                B.Put(o, at + 4, bits);
            }
        });
        return o;
    }
}

public sealed class Bpr
{
    public List<int[]> Recs = new();     // the 11 fields of a 28-byte record
    public ByteBuf Names = null!, Data = null!;
    public byte[] Tail = null!;
    public byte V1, V2;
    const int RecSize = 28;

    public static Bpr Parse(byte[] d)
    {
        if (B.U32(d, 0) != 0x425A5232) throw new InvalidDataException("not a bpr");
        var b = new Bpr { V1 = d[4], V2 = d[5] };
        int rn = B.U16(d, 6), rec = (int)B.U32(d, 8), dataSize = (int)B.U32(d, 12);
        int p = 0x14;
        while (true)
        {
            var r = new int[] { B.U16(d, p), B.U16(d, p + 2), (int)B.U32(d, p + 4), B.U16(d, p + 8), B.U16(d, p + 10), (int)B.U32(d, p + 12),
                                (int)B.U32(d, p + 16), B.U16(d, p + 20), B.U16(d, p + 22), B.U16(d, p + 24), B.U16(d, p + 26) };
            b.Recs.Add(r);
            if (r[0] == 0) break;
            p += r[0];
        }
        int namesAt = 0x14 + rec, dataAt = 0x14 + rn;
        b.Names = new ByteBuf(d.AsSpan(namesAt, dataAt - namesAt));
        b.Data = new ByteBuf(d.AsSpan(dataAt, dataSize));
        b.Tail = d.AsSpan(dataAt + dataSize).ToArray();
        return b;
    }

    public string NameOf(int[] r) => B.Sz(Names.Span, r[1]);

    public byte[] Write()
    {
        var raw = new ByteBuf(Recs.Count * RecSize);
        for (int i = 0; i < Recs.Count; i++)
        {
            var r = Recs[i];
            raw.AddU16((ushort)(i < Recs.Count - 1 ? RecSize : 0)).AddU16((ushort)r[1]).AddU32((uint)r[2]).AddU16((ushort)r[3]).AddU16((ushort)r[4])
               .AddU32((uint)r[5]).AddU32((uint)r[6]).AddU16((ushort)r[7]).AddU16((ushort)r[8]).AddU16((ushort)r[9]).AddU16((ushort)r[10]);
        }
        int rn = raw.Len + Names.Len;
        if (rn >= 0x10000) throw new InvalidOperationException("records + names too big for the u16 header field");
        var o = new ByteBuf(0x14 + rn + Data.Len + Tail.Length);
        o.Add(B.Bytes("2RZB")).Add(V1).Add(V2).AddU16((ushort)rn).AddU32((uint)raw.Len).AddU32((uint)Data.Len).AddU32(0);
        return o.Add(raw.Span).Add(Names.Span).Add(Data.Span).Add(Tail).ToArray();
    }

    /// a picture from ready DXT1 bytes (replace or add)
    public static byte[] SetPicture(byte[] bpr, string name, byte[] pic)
    {
        var b = Parse(bpr);
        foreach (var r in b.Recs)
            if (b.NameOf(r) == name) { r[2] = b.Data.Len; r[5] = pic.Length; b.Data.Add(pic); return b.Write(); }
        int nameOff = b.Names.Len;
        b.Names.AddSz(name);
        var rec = new[] { RecSize, nameOff, b.Data.Len, 1, 0, pic.Length, 0, 1, 2, 512, 256 };
        b.Data.Add(pic);
        b.Recs.Insert(b.Recs.Count - 1, rec);
        return b.Write();
    }

    public static byte[] AliasPicture(byte[] bpr, string name, string like)
    {
        var b = Parse(bpr);
        var names = b.Recs.Select(b.NameOf).ToList();
        if (names.Contains(name)) return bpr;
        var src = b.Recs[names.IndexOf(like)];
        int nameOff = b.Names.Len;
        b.Names.AddSz(name);
        var rec = (int[])src.Clone(); rec[0] = RecSize; rec[1] = nameOff;
        b.Recs.Insert(b.Recs.Count - 1, rec);
        return b.Write();
    }
}

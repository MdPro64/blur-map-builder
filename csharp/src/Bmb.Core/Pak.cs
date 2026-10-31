// Blur PAK files (cache/*.pak, magic "2KAP"). Port of pak/pak.py - see that file for the layout notes.
using System.IO.Compression;
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;

namespace Bmb.Core;

public sealed class PakEntry
{
    public int At; public uint Hash, NameOff, Unk, Size; public ulong Time, Offset; public uint Crc, ZSize; public string Name = "";
}

public sealed unsafe class Pak : IDisposable
{
    public const ulong NoData = 0xFFFFFFFFFFFFFFFF;
    static readonly byte[] Key = B.Bytes("VXo40j3@$%\\%`x");

    public readonly string Path;
    readonly MemoryMappedFile _map;
    readonly MemoryMappedViewAccessor _view;
    readonly byte* _p;
    public readonly long FileSize;
    public readonly uint Version, Align;
    public readonly int ESize, NSizeAt, NamesOff, DirOff, DirEnd;
    public readonly byte[] Names;
    public readonly List<PakEntry> Files = new();
    readonly Dictionary<uint, PakEntry> _byHash = new();

    public ReadOnlySpan<byte> Head(int n) => new(_p, n);
    public ReadOnlySpan<byte> Range(long at, int n) => new(_p + at, n);

    public Pak(string path)
    {
        Path = path;
        FileSize = new FileInfo(path).Length;
        // the file MAPPED, not read (as the Python version): Windows pages in only what is used
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        _map = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
        _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _p);
        var d = new ReadOnlySpan<byte>(_p, (int)Math.Min(FileSize, int.MaxValue));
        if (B.U32(d, 0) != 0x50414B32) throw new InvalidDataException("not a pak: " + path);
        Version = B.U32(d, 4); Align = B.U32(d, 8);
        ESize = d[0x11] == 1 ? 0x40 : 0x28;
        int pos = 0x38;
        uint nsize = B.U32(d, pos);
        if (nsize == 0) { pos = (int)Align; nsize = B.U32(d, pos); }
        NSizeAt = pos; NamesOff = pos + 4;
        Names = Xor(d.Slice(NamesOff, (int)nsize));
        DirOff = (int)((NamesOff + nsize + Align - 1) / Align * Align);
        uint count = B.U32(d, DirOff), nbytes = B.U32(d, DirOff + 4);
        if (nbytes != count * ESize) throw new InvalidDataException("bad directory");
        for (int i = 0; i < count; i++)
        {
            int a = DirOff + 8 + ESize * i;
            var e = new PakEntry
            {
                At = a, Hash = B.U32(d, a), NameOff = B.U32(d, a + 4), Unk = B.U32(d, a + 8), Size = B.U32(d, a + 12),
                Time = B.U64(d, a + 16), Offset = B.U64(d, a + 24), Crc = B.U32(d, a + 32), ZSize = B.U32(d, a + 36),
            };
            e.Name = B.Sz(Names, (int)e.NameOff);
            Files.Add(e);
            _byHash.TryAdd(e.Hash, e);
        }
        DirEnd = DirOff + 8 + ESize * (int)count;
    }

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose(); _map.Dispose();
    }

    public PakEntry? Find(string name) => _byHash.GetValueOrDefault(NameHash(name));

    public ReadOnlySpan<byte> Stored(PakEntry e) => new(_p + (long)(e.Offset * Align), (int)e.ZSize);

    public byte[]? Read(PakEntry e)
    {
        if (e.Offset == NoData) return null;
        var raw = Stored(e);
        var o = new byte[e.Size];
        if (ESize == 0x40)
        {
            fixed (byte* src = raw)
            {
                using var ms = new UnmanagedMemoryStream(src, raw.Length);
                using var z = new ZLibStream(ms, CompressionMode.Decompress);
                ReadFull(z, o);
            }
            return o;
        }
        Unchunk(raw, o);
        return o;
    }

    public byte[]? Read(string name) { var e = Find(name); return e == null ? null : Read(e); }

    static void ReadFull(Stream s, Span<byte> o)
    {
        int got = 0;
        while (got < o.Length) { int n = s.Read(o[got..]); if (n <= 0) break; got += n; }
    }

    /// 0x28-entry packs: u32 chunk size, then per chunk u32 length + raw deflate data
    static void Unchunk(ReadOnlySpan<byte> raw, byte[] o)
    {
        int at = 4, w = 0;
        while (at < raw.Length && w < o.Length)
        {
            int n = (int)B.U32(raw, at);
            fixed (byte* src = raw)
            {
                using var ms = new UnmanagedMemoryStream(src + at + 4, n);
                using var z = new DeflateStream(ms, CompressionMode.Decompress);
                while (w < o.Length) { int g = z.Read(o, w, o.Length - w); if (g <= 0) break; w += g; }
            }
            at += 4 + n;
        }
    }

    public static byte[] Chunk(ReadOnlySpan<byte> data, int csize = 0x10000)
    {
        var o = new ByteBuf(data.Length / 2 + 64);
        o.AddU32((uint)csize);
        for (int i = 0; i < data.Length; i += csize)
        {
            var z = Deflate(data.Slice(i, Math.Min(csize, data.Length - i)));
            o.AddU32((uint)z.Length).Add(z);
        }
        return o.ToArray();
    }

    public static byte[] Deflate(ReadOnlySpan<byte> d)
    {
        using var ms = new MemoryStream(d.Length / 2 + 64);
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, true)) z.Write(d);
        return ms.ToArray();
    }

    public static byte[] ZLib(ReadOnlySpan<byte> d)
    {
        using var ms = new MemoryStream(d.Length / 2 + 64);
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) z.Write(d);
        return ms.ToArray();
    }

    /// What the game checks: sum of ~crc32 over each 32 KB block of the uncompressed data
    public static uint GameCrc(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 0x8000)
            sum += Crc32Of(data.Slice(i, Math.Min(0x8000, data.Length - i))) ^ 0xFFFFFFFF;
        return sum;
    }

    public static uint Crc32Of(ReadOnlySpan<byte> d) => System.IO.Hashing.Crc32.HashToUInt32(d);

    public static byte[] Xor(ReadOnlySpan<byte> b)
    {
        var o = new byte[b.Length];
        for (int i = 0; i < b.Length; i++) o[i] = (byte)(b[i] ^ Key[i % Key.Length]);
        return o;
    }

    public static uint NameHash(string name)
    {
        name = "\\" + name.Replace('/', '\\').TrimStart('\\');
        return Crc32Of(B.Bytes(name.ToLowerInvariant())) ^ 0xFFFFFFFF;
    }

    /// Copy pak src to dst, giving each {pak path: bytes} new contents. Compression runs on all cores.
    public static long ReplaceFiles(string src, string dst, IReadOnlyList<KeyValuePair<string, byte[]>> newFiles, bool check = true)
    {
        using var p = new Pak(src);
        var comp = new byte[newFiles.Count][];
        var ents = new PakEntry[newFiles.Count];
        for (int i = 0; i < newFiles.Count; i++)
            ents[i] = p.Find(newFiles[i].Key) ?? throw new KeyNotFoundException(newFiles[i].Key);
        bool z = p.ESize == 0x40;
        Parallel.For(0, newFiles.Count, i => comp[i] = z ? ZLib(newFiles[i].Value) : Chunk(newFiles[i].Value));
        File.Copy(src, dst, true);
        long len;
        using (var fs = new FileStream(dst, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1 << 20))
        {
            len = fs.Length;
            uint al = p.Align;
            var u = new byte[8];
            for (int i = 0; i < newFiles.Count; i++)
            {
                long pos = (len + al - 1) / al * al;
                fs.Position = len;
                for (long k = len; k < pos; k++) fs.WriteByte(0);
                fs.Write(comp[i]);
                len = pos + comp[i].Length;
                var e = ents[i];
                var data = newFiles[i].Value;
                void W32(int at, uint v) { fs.Position = at; B.Put(u, 0, v); fs.Write(u[..4]); }
                W32(e.At + 12, (uint)data.Length);
                fs.Position = e.At + 24; B.Put(u, 0, (ulong)(pos / al)); fs.Write(u);
                W32(e.At + 32, GameCrc(data));
                W32(e.At + 36, (uint)comp[i].Length);
                if (z) { fs.Position = e.At + 44; fs.Write(SHA1.HashData(comp[i])); }
            }
            long end = (len + al - 1) / al * al;
            fs.Position = len;
            for (long k = len; k < end; k++) fs.WriteByte(0);
            len = end;
        }
        if (check)
        {
            using var c = new Pak(dst);
            foreach (var kv in newFiles)
                if (!c.Read(c.Find(kv.Key)!)!.AsSpan().SequenceEqual(kv.Value)) throw new InvalidDataException("pak check: " + kv.Key);
        }
        return len;
    }

    /// Copy pak src to dst with extra entries {new path: existing path} that point at the SAME stored data
    public static long AddAliases(string src, string dst, IReadOnlyList<KeyValuePair<string, string>> aliases)
    {
        byte[] entriesData; long len;
        using var p = new Pak(src);
        byte[] d = File.ReadAllBytes(src);
        var names = new ByteBuf(p.Names);
        var entries = new List<byte[]>();
        foreach (var e in p.Files) entries.Add(d.AsSpan(e.At, p.ESize).ToArray());
        foreach (var (new0, old) in aliases)
        {
            string nw = "\\" + new0.Replace('/', '\\').TrimStart('\\');
            var e = p.Find(old) ?? throw new KeyNotFoundException(old);
            if (p.Find(nw) != null) throw new InvalidOperationException("already there: " + nw);
            var ent = d.AsSpan(e.At, p.ESize).ToArray();
            B.Put(ent, 0, NameHash(nw)); B.Put(ent, 4, (uint)names.Len);
            names.AddSz(nw);
            entries.Add(ent);
        }
        entries = entries.OrderBy(b => B.U32(b, 0)).ToList();     // stable, as Python's sort
        if (entries.Select(b => B.U32(b, 0)).Distinct().Count() != entries.Count) throw new InvalidOperationException("hash collision");
        long al = p.Align;
        long firstData = p.Files.Where(f => f.Offset != NoData).Min(f => (long)f.Offset) * al;
        long dirOff = Math.Max(p.DirOff, (p.NamesOff + names.Len + al - 1) / al * al);
        long end = dirOff + 8 + p.ESize * entries.Count;
        long shift = Math.Max(0, (end + al - 1) / al * al - firstData);
        if (dirOff != p.DirOff || shift != 0)
        {
            if (p.NSizeAt != al || B.U32(d, 0x24) != p.DirOff - al || B.U32(d, 0x28) != firstData - p.DirOff)
                throw new InvalidDataException("unknown pack layout");
            foreach (var ent in entries)
            {
                ulong off = B.U64(ent, 24);
                if (off != NoData) B.Put(ent, 24, off + (ulong)(shift / al));
            }
            var nd = new byte[d.Length + shift];
            d.AsSpan(0, p.NamesOff).CopyTo(nd);
            d.AsSpan((int)firstData).CopyTo(nd.AsSpan((int)(firstData + shift)));
            d = nd;
            B.Put(d, 0x24, (uint)(dirOff - al)); B.Put(d, 0x28, (uint)(firstData + shift - dirOff));
        }
        B.Put(d, p.NSizeAt, (uint)names.Len);
        Xor(names.Span).CopyTo(d, p.NamesOff);
        B.Put(d, (int)dirOff, (uint)entries.Count); B.Put(d, (int)dirOff + 4, (uint)(p.ESize * entries.Count));
        entriesData = entries.SelectMany(x => x).ToArray();
        entriesData.CopyTo(d, dirOff + 8);
        File.WriteAllBytes(dst, d);
        len = d.Length;
        using var c = new Pak(dst);
        foreach (var (nw, old) in aliases)
        {
            var a = c.Find(nw); var b = c.Find(old);
            if (a == null || b == null || a.Offset != b.Offset || a.Size != b.Size) throw new InvalidDataException("alias check: " + nw);
        }
        return len;
    }
}

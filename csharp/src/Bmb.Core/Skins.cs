// CAR SKINS for the game's own cars: a new body picture written straight into Blur REBORN's root.pak (rebrn\). Port of car/skin_data.py
// (its Texmod part is left out). Blur.exe and the cache\ packs are never changed.
// Only cars whose body wears a full picture can be skinned: Boss / Legend / livery / Rat cars. Plain-paint cars have no body picture.
// Storage: <data>\skins\  skins.json {car: {picture: {"on": bool}}}, <car>\<picture>.png, installed.json (what rebrn\root.pak holds now).
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Bmb.Core;

public sealed class SkinCar { public string Car = "", Body = ""; public List<string> Pictures = new(); }
public sealed record SkinLevel(int W, int H, string Fmt, int At, int Size, int Mips);

/// a picture with alpha: R G B A per pixel
public sealed class Rgba
{
    public int W, H; public byte[] Px;
    public Rgba(int w, int h, byte[]? px = null) { W = w; H = h; Px = px ?? new byte[w * h * 4]; }

    public static Rgba Load(string path)
    {
        using var f = File.OpenRead(path);
        using var b0 = new Bitmap(f);
        return FromBitmap(b0);
    }

    public static Rgba FromBitmap(Bitmap b0)
    {
        using var b = new Bitmap(b0.Width, b0.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b)) g.DrawImage(b0, 0, 0, b0.Width, b0.Height);
        var o = new Rgba(b.Width, b.Height);
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var row = new byte[b.Width * 4];
        for (int y = 0; y < b.Height; y++)
        {
            Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, row.Length);
            for (int x = 0; x < b.Width; x++) { int s = 4 * x, t = (y * b.Width + x) * 4; o.Px[t] = row[s + 2]; o.Px[t + 1] = row[s + 1]; o.Px[t + 2] = row[s]; o.Px[t + 3] = row[s + 3]; }
        }
        b.UnlockBits(d);
        return o;
    }

    public Bitmap ToBitmap()
    {
        var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        var d = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new byte[W * 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++) { int s = (y * W + x) * 4, t = 4 * x; row[t] = Px[s + 2]; row[t + 1] = Px[s + 1]; row[t + 2] = Px[s]; row[t + 3] = Px[s + 3]; }
            Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, row.Length);
        }
        b.UnlockBits(d);
        return b;
    }

    public void Save(string path) { using var b = ToBitmap(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); b.Save(path, ImageFormat.Png); }

    public Rgba Resize(int w, int h)
    {
        if (w == W && h == H) return this;
        using var src = ToBitmap();
        using var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var wrap = new ImageAttributes(); wrap.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, W, H, GraphicsUnit.Pixel, wrap);
        }
        return FromBitmap(dst);
    }

    /// the next smaller mip size: each pixel the mean of 2 x 2
    public Rgba Half()
    {
        int w = Math.Max(1, W / 2), h = Math.Max(1, H / 2);
        var o = new Rgba(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++)
                {
                    int x0 = Math.Min(2 * x, W - 1), x1 = Math.Min(2 * x + 1, W - 1), y0 = Math.Min(2 * y, H - 1), y1 = Math.Min(2 * y + 1, H - 1);
                    o.Px[(y * w + x) * 4 + c] = (byte)((Px[(y0 * W + x0) * 4 + c] + Px[(y0 * W + x1) * 4 + c] + Px[(y1 * W + x0) * 4 + c] + Px[(y1 * W + x1) * 4 + c] + 2) / 4);
                }
        return o;
    }
}

public static class Skins
{
    const string Veh = "\\models\\vehicles\\";
    static readonly Regex LevelRx = new("\\x1c\\x00\\x00\\x00(....)(....)\\x01\\x00\\x00\\x00(....)(DXT1|DXT5)\\x00\\x00\\x00\\x00\\x03\\x00\\x00\\x00RA\\x00\\x00(....)", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex NameRx = new("([A-Za-z0-9_]{3,})RA\\x01", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly HashSet<string> Shared = new() { "carbonfibre", "paintdent", "tyrewall", "tyretred", "breakdisc", "smashedglass", "underside_small", "tyrewall_nrm", "smashedglass_nrm", "underside_small_nrm", "tyretred_bump_nrm" };
    static readonly Regex BodyHint = new(@"liv|legend\d|rust|ratcol|decal_01|viper1024|stripe|^drift$|^text01$", RegexOptions.Compiled);
    static readonly Regex NotBody = new(@"nrm|nmr|bump|underside|interior|dials|light|scratch|glass|alpha|grill|carbon|badge|tyre|_\d\d_|^\d\d_|cali|nyc|uk_|spain|jap|yokohama|shinagawa|^defaultcolour$", RegexOptions.Compiled);

    public static string Folder => Path.Combine(Paths.Data, "skins");
    public static string ModelPath(string car) => Veh + car + ".model";
    static Pak? _pak; static readonly object _lock = new();

    public static byte[] ModelBytes(string car) { lock (_lock) { _pak ??= new Pak(Paths.RootOriginal); return _pak.Read(ModelPath(car)) ?? throw new FileNotFoundException(car); } }

    /// every stored size of a picture
    public static List<SkinLevel> Levels(byte[] data, string pic)
    {
        string s = B.Latin1.GetString(data);
        var names = NameRx.Matches(s).Select(m => (m.Index, Name: m.Groups[1].Value)).ToList();
        var o = new List<SkinLevel>();
        for (int k = 0; k < names.Count; k++)
        {
            if (names[k].Name != pic) continue;
            int a = names[k].Index, end = k + 1 < names.Count ? names[k + 1].Index : data.Length;
            for (var m = LevelRx.Match(s, a, end - a); m.Success; m = m.NextMatch())
                o.Add(new SkinLevel((int)B.U32(data, m.Groups[1].Index), (int)B.U32(data, m.Groups[2].Index), m.Groups[4].Value, m.Index + m.Length + 20, (int)B.U32(data, m.Groups[5].Index), (int)B.U32(data, m.Groups[3].Index)));
        }
        return o;
    }

    static SkinCar? Scan(string car, byte[] data)
    {
        var fx = CpModel.Read(data).FxFiles;
        if (!fx.Any(f => f.Contains("paint", StringComparison.OrdinalIgnoreCase))) return null;
        var pics = new Dictionary<string, int>();
        foreach (var n in NameRx.Matches(B.Latin1.GetString(data)).Select(m => m.Groups[1].Value).Distinct())
        {
            var lv = Levels(data, n);
            if (lv.Count > 0 && !Shared.Contains(n)) pics[n] = lv.Max(l => l.W * l.H);
        }
        var hinted = pics.Keys.Where(n => BodyHint.IsMatch(n.ToLowerInvariant()) && !NotBody.IsMatch(n.ToLowerInvariant())).ToList();
        if (fx.Any(f => f.StartsWith("veh_paint_a", StringComparison.OrdinalIgnoreCase)) && !hinted.Any(n => pics[n] >= 512 * 512)) return null;
        if (pics.Count == 0) return null;
        var other = pics.Keys.Where(n => !NotBody.IsMatch(n.ToLowerInvariant())).ToList();
        var pool = hinted.Count > 0 ? hinted : other.Count > 0 ? other : pics.Keys.ToList();
        return new SkinCar { Car = car, Pictures = pics.OrderByDescending(p => p.Value).Select(p => p.Key).ToList(), Body = pool.OrderByDescending(n => pics[n]).First() };
    }

    /// the skinnable cars (cached in work\skin_cars.json until root.pak.original changes)
    public static List<SkinCar> Cars(Action<string>? log = null)
    {
        Directory.CreateDirectory(Paths.Work);
        string cache = Path.Combine(Paths.Work, "skin_cars.json");
        var fi = new FileInfo(Paths.RootOriginal);
        string stamp = $"{fi.Length} {fi.LastWriteTimeUtc.Ticks}";
        try
        {
            var d = (JObj)Json.Load(cache)!;
            if (d.Str("stamp") == stamp)
                return d.Objs("cars").Select(c => new SkinCar { Car = c.Str("car")!, Body = c.Str("body")!, Pictures = c.Arr("pictures")!.Select(p => (string)p!).ToList() }).ToList();
        }
        catch (Exception) { }
        log?.Invoke("Looking for cars with a full body picture (once)...");
        List<string> names;
        lock (_lock) { _pak ??= new Pak(Paths.RootOriginal); names = _pak.Files.Select(f => f.Name).Where(n => n.StartsWith(Veh) && n.EndsWith(".model") && n.Count(c => c == '\\') == 3).Select(n => n[Veh.Length..^6]).ToList(); }
        var found = new System.Collections.Concurrent.ConcurrentBag<SkinCar>();
        Parallel.ForEach(names, car => { try { if (Scan(car, ModelBytes(car)) is { } c) found.Add(c); } catch (Exception) { } });
        var o = found.OrderBy(c => c.Car, StringComparer.Ordinal).ToList();
        Json.Save(new JObj { ["stamp"] = stamp, ["cars"] = o.Select(c => (object?)new JObj { ["car"] = c.Car, ["body"] = c.Body, ["pictures"] = c.Pictures.Select(p => (object?)p).ToList() }).ToList() }, cache);
        return o;
    }

    public static string Nice(string car)
    {
        var roman = new HashSet<string> { "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x" };
        return string.Join(" ", car.Split('_').Where(w => w.Length > 0).Select(w => roman.Contains(w) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    static int Blocks(int w, int h) => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4);

    /// the game's own picture at its biggest stored size
    public static Rgba Picture(string car, string pic)
    {
        var data = ModelBytes(car);
        var l = Levels(data, pic).OrderByDescending(q => q.W * q.H).First();
        int bw = Math.Max(4, l.W), bh = Math.Max(4, l.H);
        byte[] px;
        if (l.Fmt == "DXT5") px = Previews.Dxt5Decode(data.AsSpan(l.At, Blocks(l.W, l.H) * 16), bw, bh);
        else
        {
            var rgb = Dxt.Dxt1Decode(data.AsSpan(l.At, Blocks(l.W, l.H) * 8), bw, bh);
            px = new byte[bw * bh * 4];
            for (int i = 0; i < bw * bh; i++) { px[4 * i] = rgb[3 * i]; px[4 * i + 1] = rgb[3 * i + 1]; px[4 * i + 2] = rgb[3 * i + 2]; px[4 * i + 3] = 255; }
        }
        var o = new Rgba(l.W, l.H);
        for (int y = 0; y < l.H; y++) Array.Copy(px, y * bw * 4, o.Px, y * l.W * 4, l.W * 4);
        return o;
    }

    /// the car's painted triangles where they sit on the picture: yellow lines on a clear picture (or on `over`)
    public static Rgba UvMap(string car, string pic, int w = 0, int h = 0, Rgba? over = null)
    {
        var data = ModelBytes(car);
        if (w == 0) { var l = Levels(data, pic).OrderByDescending(q => q.W * q.H).First(); w = l.W; h = l.H; }
        var M = CpModel.Read(data);
        using var bmp = over != null ? over.Resize(w, h).ToBitmap() : new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var pen = new Pen(Color.FromArgb(255, 255, 230, 0), Math.Max(1f, w / 1024f)))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var mesh in M.MeshInfos)
            {
                if (mesh.Material < 0 || mesh.Material >= M.FxFiles.Count || !M.FxFiles[mesh.Material].Contains("paint", StringComparison.OrdinalIgnoreCase)) continue;
                if (mesh.Definition >= M.VertDefs.Count || mesh.FaceStream >= M.FStreams.Count || mesh.Data1.Count == 0 || mesh.VOffset.Count == 0) continue;
                var types = M.VertDefs[mesh.Definition].Where(d => d.Prefix == 0).Select(d => d.Type).ToList();
                var vs = M.VStreams.FirstOrDefault(s => s.Def.Select(d => d.Type).SequenceEqual(types));
                if (vs == null) continue;
                int off = 0, uvAt = -1;
                foreach (var (t, ch, sub) in vs.Def) { if (ch == 5 && sub == 0 && uvAt < 0) uvAt = off; off += t == 6 ? 12 : t == 9 ? 8 : 4; }
                if (uvAt < 0) continue;
                var fs = M.FStreams[mesh.FaceStream];
                var (fo, fc, _, _) = mesh.Data1[0];
                int vbase = mesh.VOffset[0] / vs.Bytes;
                var idx = new int[fc];
                for (int i = 0; i < fc; i++) idx[i] = B.U16(data, fs.Start + 2 * (fo + i));
                var tris = new List<(int, int, int)>();
                if (mesh.FaceType == 1) for (int i = 0; i + 2 < fc; i++) tris.Add((idx[i], idx[i + 1], idx[i + 2]));
                else for (int i = 0; i + 2 < fc; i += 3) tris.Add((idx[i], idx[i + 1], idx[i + 2]));
                PointF? Uv(int v)
                {
                    v += vbase;
                    if (v < 0 || v >= vs.Count) return null;
                    int at = vs.Start + v * vs.Bytes + uvAt;
                    float u = (float)BitConverter.UInt16BitsToHalf(B.U16(data, at)), q = (float)BitConverter.UInt16BitsToHalf(B.U16(data, at + 2));
                    if (u < -0.01f || u > 1.01f || q < -0.01f || q > 1.01f) return null;
                    return new PointF(u * w, q * h);
                }
                foreach (var (a, b, c) in tris)
                {
                    if (a == b || b == c || a == c) continue;
                    if (Uv(a) is { } pa && Uv(b) is { } pb && Uv(c) is { } pc) g.DrawPolygon(pen, new[] { pa, pb, pc });
                }
            }
        }
        return Rgba.FromBitmap(bmp);
    }

    // ---- the player's skins --------------------------------------------------------------------------------------------------
    static JObj LoadJson(string name) { try { return (JObj)Json.Load(Path.Combine(Folder, name))!; } catch (Exception) { return new JObj(); } }
    static void SaveJson(string name, JObj d) { Directory.CreateDirectory(Folder); string p = Path.Combine(Folder, name); Json.Save(d, p + ".tmp"); File.Move(p + ".tmp", p, true); }

    /// {car: {picture: {"on": bool}}}
    public static JObj All() => LoadJson("skins.json");
    public static string SkinFile(string car, string pic) => Path.Combine(Folder, car, pic + ".png");
    public static bool IsOn(string car, string pic) => All().Obj(car)?.Obj(pic)?.Truthy("on") == true;
    public static bool Has(string car, string pic) => All().Obj(car)?.ContainsKey(pic) == true;

    public static void Set(string car, string pic, string imagePath)
    {
        var img = Rgba.Load(imagePath);
        if (img.W < 16 || img.H < 16) throw new InvalidDataException("the picture is too small");
        img.Save(SkinFile(car, pic));
        var s = All();
        if (s.Obj(car) == null) s[car] = new JObj();
        s.Obj(car)![pic] = new JObj { ["on"] = true };
        SaveJson("skins.json", s);
    }

    public static void Turn(string car, string pic, bool on)
    {
        var s = All();
        if (s.Obj(car)?.Obj(pic) is { } e) { e["on"] = on; SaveJson("skins.json", s); }
    }

    public static void TurnAllOff()
    {
        var s = All();
        foreach (var c in s.Values.OfType<JObj>()) foreach (var e in c.Values.OfType<JObj>()) e["on"] = false;
        SaveJson("skins.json", s);
    }

    public static void Remove(string car, string pic)
    {
        var s = All();
        s.Obj(car)?.Remove(pic);
        if (s.Obj(car) is { Count: 0 }) s.Remove(car);
        SaveJson("skins.json", s);
        try { File.Delete(SkinFile(car, pic)); } catch (IOException) { }
    }

    /// {car: [pictures]} of the skins that are on (and whose picture is there)
    public static Dictionary<string, List<string>> OnNow()
    {
        var o = new Dictionary<string, List<string>>();
        foreach (var (car, v) in All())
            if (v is JObj pics)
                foreach (var (pic, e) in pics)
                    if (e is JObj j && j.Truthy("on") && File.Exists(SkinFile(car, pic))) { if (!o.TryGetValue(car, out var l)) o[car] = l = new(); l.Add(pic); }
        return o;
    }

    // ---- writing -----------------------------------------------------------------------------------------------------------
    /// a picture as DXT1 / DXT5 with all its mip sizes (block colours along the main colour line: much closer than min/max)
    /// The stored length says how many sizes the game keeps (the small pictures hold their mips, the big ones only the top): `size` = that length
    public static byte[] Encode(Rgba img, string fmt, int mips, int size = 0)
    {
        int block = fmt == "DXT5" ? 16 : 8;
        var o = new List<byte>();
        var cur = img;
        for (int i = 0; i < Math.Max(1, mips); i++)
        {
            if (i > 0) cur = cur.Half();
            o.AddRange(EncodeOne(cur, fmt == "DXT5", block));
            if (size > 0 && o.Count >= size) break;
        }
        return o.ToArray();
    }

    static byte[] EncodeOne(Rgba p, bool dxt5, int block)
    {
        int bw = Math.Max(1, (p.W + 3) / 4), bh = Math.Max(1, (p.H + 3) / 4);
        var o = new byte[bw * bh * block];
        Parallel.For(0, bh, row =>
        {
            Span<float> c = stackalloc float[48]; Span<int> a = stackalloc int[16]; Span<int> pal = stackalloc int[12]; Span<int> ap = stackalloc int[8];
            for (int col = 0; col < bw; col++)
            {
                int at = (row * bw + col) * block;
                float mr = 0, mg = 0, mb = 0;
                for (int k = 0; k < 16; k++)
                {
                    int x = Math.Min(col * 4 + k % 4, p.W - 1), y = Math.Min(row * 4 + k / 4, p.H - 1), s = (y * p.W + x) * 4;
                    c[3 * k] = p.Px[s]; c[3 * k + 1] = p.Px[s + 1]; c[3 * k + 2] = p.Px[s + 2]; a[k] = p.Px[s + 3];
                    mr += c[3 * k]; mg += c[3 * k + 1]; mb += c[3 * k + 2];
                }
                mr /= 16; mg /= 16; mb /= 16;
                if (dxt5)
                {
                    int a0 = 0, a1 = 255;
                    for (int k = 0; k < 16; k++) { a0 = Math.Max(a0, a[k]); a1 = Math.Min(a1, a[k]); }
                    o[at] = (byte)a0; o[at + 1] = (byte)a1;
                    ap[0] = a0; ap[1] = a1;
                    if (a0 > a1) for (int k = 1; k < 7; k++) ap[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                    else for (int k = 2; k < 8; k++) ap[k] = a0;
                    ulong bits = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        int best = 0, bd = 999;
                        for (int j = 0; j < 8; j++) { int dd = Math.Abs(ap[j] - a[k]); if (dd < bd) { bd = dd; best = j; } }
                        bits |= (ulong)best << (3 * k);
                    }
                    for (int k = 0; k < 6; k++) o[at + 2 + k] = (byte)(bits >> (8 * k));
                    at += 8;
                }
                // the main direction of the colours (a few power steps on their spread)
                float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                for (int k = 0; k < 16; k++)
                {
                    float r = c[3 * k] - mr, g = c[3 * k + 1] - mg, b = c[3 * k + 2] - mb;
                    xx += r * r; xy += r * g; xz += r * b; yy += g * g; yz += g * b; zz += b * b;
                }
                float vx = 1, vy = 1, vz = 1;
                for (int it = 0; it < 6; it++)
                {
                    float nx = xx * vx + xy * vy + xz * vz, ny = xy * vx + yy * vy + yz * vz, nz = xz * vx + yz * vy + zz * vz;
                    float l = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (l < 1e-6f) { vx = vy = vz = 0; break; }
                    vx = nx / l; vy = ny / l; vz = nz / l;
                }
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < 16; k++) { float t = (c[3 * k] - mr) * vx + (c[3 * k + 1] - mg) * vy + (c[3 * k + 2] - mb) * vz; lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
                int R(float v) => Math.Clamp((int)MathF.Round(v), 0, 255);
                int c0 = To565(R(mr + vx * hi), R(mg + vy * hi), R(mb + vz * hi)), c1 = To565(R(mr + vx * lo), R(mg + vy * lo), R(mb + vz * lo));
                if (c0 < c1) (c0, c1) = (c1, c0);
                B.Put(o, at, (ushort)c0); B.Put(o, at + 2, (ushort)c1);
                if (c0 == c1) { B.Put(o, at + 4, 0u); continue; }
                Rgb(c0, pal, 0); Rgb(c1, pal, 3);
                for (int t = 0; t < 3; t++) { pal[6 + t] = (2 * pal[t] + pal[3 + t]) / 3; pal[9 + t] = (pal[t] + 2 * pal[3 + t]) / 3; }
                uint cb = 0;
                for (int k = 0; k < 16; k++)
                {
                    int best = 0; float bd = float.MaxValue;
                    for (int j = 0; j < 4; j++) { float dr = c[3 * k] - pal[3 * j], dg = c[3 * k + 1] - pal[3 * j + 1], db = c[3 * k + 2] - pal[3 * j + 2], dd = dr * dr + dg * dg + db * db; if (dd < bd) { bd = dd; best = j; } }
                    cb |= (uint)best << (2 * k);
                }
                B.Put(o, at + 4, cb);
            }
        });
        return o;
    }

    static int To565(int r, int g, int b) => (r * 31 + 127) / 255 << 11 | (g * 63 + 127) / 255 << 5 | (b * 31 + 127) / 255;
    static void Rgb(int c, Span<int> p, int at) { p[at] = (c >> 11 & 31) * 255 / 31; p[at + 1] = (c >> 5 & 63) * 255 / 63; p[at + 2] = (c & 31) * 255 / 31; }

    /// a copy of the model with the skins written over its pictures, every stored size, same byte length
    public static byte[] Apply(byte[] model, string car, IEnumerable<string> pics, Action<string>? log = null)
    {
        var o = (byte[])model.Clone();
        foreach (var pic in pics)
        {
            var img = Rgba.Load(SkinFile(car, pic));
            var lv = Levels(model, pic);
            int done = 0;
            foreach (var l in lv)
            {
                var enc = Encode(img.Resize(l.W, l.H), l.Fmt, l.Mips, l.Size);
                if (enc.Length != l.Size) { log?.Invoke($"   {Nice(car)} {pic} {l.W} x {l.H}: unexpected size, left as it is"); continue; }
                enc.CopyTo(o, l.At);
                done++;
            }
            log?.Invoke($"   skin {Nice(car)}: {pic}, {done} of {lv.Count} sizes");
        }
        return o;
    }

    /// the map build: the models of every car with a skin on, written over what the build already changed. Also notes them as installed
    public static List<KeyValuePair<string, byte[]>> BuildFiles(IList<KeyValuePair<string, byte[]>> files, Func<string, byte[]> read, Action<string> log)
    {
        var o = new List<KeyValuePair<string, byte[]>>();
        var on = OnNow();
        foreach (var (car, pics) in on)
        {
            string path = ModelPath(car);
            var bas = files.LastOrDefault(f => f.Key.Equals(path, StringComparison.OrdinalIgnoreCase)).Value ?? read(path);
            o.Add(new(path, Apply(bas, car, pics, log)));
        }
        if (on.Count > 0) log($"car skins: {on.Count} car(s) with your skin");
        var inst = LoadJson("installed.json"); inst["cars"] = on.Keys.Select(k => (object?)k).ToList();
        SaveJson("installed.json", inst);
        return o;
    }

    /// "Build skins": only the skinned cars' models in rebrn\root.pak change (skins that are off go back to the game's picture).
    /// The rest of that pack stays as the last map build left it. Returns (cars with a skin, cars put back)
    public static (int On, int Back) Install(Action<string> log)
    {
        string pack = Path.Combine(Paths.PackDir, "root.pak");
        if (!File.Exists(pack)) throw new FileNotFoundException("Blur REBORN's root.pak is not there yet: press Build + install once first.");
        var on = OnNow();
        var before = LoadJson("installed.json").Arr("cars")?.Select(c => (string)c!).ToHashSet() ?? new HashSet<string>();
        var files = new List<KeyValuePair<string, byte[]>>();
        foreach (var (car, pics) in on) files.Add(new(ModelPath(car), Apply(ModelBytes(car), car, pics, log)));
        int back = 0;
        foreach (var car in before.Where(c => !on.ContainsKey(c))) { files.Add(new(ModelPath(car), ModelBytes(car))); back++; log($"   {Nice(car)}: the game's own picture again"); }
        if (files.Count > 0)
        {
            string tmp = pack + ".skins.tmp";
            Pak.ReplaceFiles(pack, tmp, files);
            File.Move(tmp, pack, true);
        }
        var inst = LoadJson("installed.json"); inst["cars"] = on.Keys.Select(k => (object?)k).ToList();
        SaveJson("installed.json", inst);
        InstallNormal(on, log);
        return (on.Count, back);
    }

    // ---- normal Blur (opt-in): its cache root.pak = the player's original + the skins; turned off = the original back, byte for byte
    public static bool Normal
    {
        get => LoadJson("settings.json").Truthy("normal_blur");
        set { var s = LoadJson("settings.json"); s["normal_blur"] = value; SaveJson("settings.json", s); }
    }
    public static bool InNormalNow => LoadJson("installed.json").Truthy("normal");
    public static string NormalPack => Path.Combine(Paths.Cache, "root.pak");

    static void InstallNormal(Dictionary<string, List<string>> on, Action<string> log)
    {
        bool want = Normal && on.Count > 0;
        if (!want && !InNormalNow) return;
        string pack = NormalPack, tmp = pack + ".skins.tmp";
        Directory.CreateDirectory(Paths.Cache);
        if (want)
        {
            var files = on.Select(kv => new KeyValuePair<string, byte[]>(ModelPath(kv.Key), Apply(ModelBytes(kv.Key), kv.Key, kv.Value))).ToList();
            Pak.ReplaceFiles(Paths.RootOriginal, tmp, files);
            log($"normal Blur: {on.Count} car(s) with your skin in {pack}");
        }
        else
        {
            File.Copy(Paths.RootOriginal, tmp, true);
            log($"normal Blur: the game's own {pack} is back");
        }
        File.Move(tmp, pack, true);
        var inst = LoadJson("installed.json"); inst["normal"] = want;
        SaveJson("installed.json", inst);
    }
}

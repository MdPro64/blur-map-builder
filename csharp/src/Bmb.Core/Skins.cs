// CAR SKINS for the game's own cars: a new body picture written straight into Blur REBORN's root.pak (rebrn\). Port of car/skin_data.py
// (its Texmod part is left out). Blur.exe and the cache\ packs are never changed.
// Cars whose body wears a full picture (Boss / Legend / livery / Rat cars) get the skin in that picture. Plain-paint cars (no body
// picture) first get a picture slot (PlainSkin.AddSlot): the skin goes into their scratch picture, which has the body's layout.
// Storage: <data>\skins\  skins.json {car: {picture: {"on": bool}}}, <car>\<picture>.png, installed.json (what rebrn\root.pak holds now).
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Bmb.Core;

public sealed class SkinCar { public string Car = "", Body = ""; public List<string> Pictures = new(); public bool Plain, Decal; }
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

    /// the picture laid over one colour (no see-through left)
    public Rgba OnColour(byte r, byte g, byte b)
    {
        var o = new Rgba(W, H);
        for (int i = 0; i < W * H; i++)
        {
            int a = Px[4 * i + 3];
            o.Px[4 * i] = (byte)((Px[4 * i] * a + r * (255 - a) + 127) / 255); o.Px[4 * i + 1] = (byte)((Px[4 * i + 1] * a + g * (255 - a) + 127) / 255);
            o.Px[4 * i + 2] = (byte)((Px[4 * i + 2] * a + b * (255 - a) + 127) / 255); o.Px[4 * i + 3] = 255;
        }
        return o;
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

    /// cars whose body picture the scan cannot find (car -> picture, "NAME#DXT5" = only that format)
    static readonly Dictionary<string, string> BodyFix = new();


    public static string Folder => Path.Combine(Paths.Data, "skins");
    public static string ModelPath(string car) => Veh + car + ".model";
    static Pak? _pak; static readonly object _lock = new();

    public static byte[] ModelBytes(string car) { lock (_lock) { _pak ??= new Pak(Paths.RootOriginal); return _pak.Read(ModelPath(car)) ?? throw new FileNotFoundException(car); } }

    /// every stored size of a picture ("NAME#DXT5": only its sizes in that format)
    public static List<SkinLevel> Levels(byte[] data, string pic)
    {
        int hash = pic.IndexOf('#');
        if (hash >= 0) { string fmt = pic[(hash + 1)..]; return Levels(data, pic[..hash]).Where(l => l.Fmt == fmt).ToList(); }
        string s = B.Latin1.GetString(data);
        var names = NameRx.Matches(s).Select(m => (m.Index, Name: m.Groups[1].Value)).ToList();
        List<SkinLevel> Under(int k)
        {
            var l = new List<SkinLevel>();
            int a = names[k].Index, end = k + 1 < names.Count ? names[k + 1].Index : data.Length;
            for (var m = LevelRx.Match(s, a, end - a); m.Success; m = m.NextMatch())
                l.Add(new SkinLevel((int)B.U32(data, m.Groups[1].Index), (int)B.U32(data, m.Groups[2].Index), m.Groups[4].Value, m.Index + m.Length + 20, (int)B.U32(data, m.Groups[5].Index), (int)B.U32(data, m.Groups[3].Index)));
            return l;
        }
        var o = new List<SkinLevel>();
        for (int k = 0; k < names.Count; k++)
        {
            if (names[k].Name != pic) continue;
            var own = Under(k);
            o.AddRange(own);
            // a picture with only its smallest size under its name: its bigger sizes are stored under the NEXT name, as the
            // first of each pair of same-size records (that name's own picture is the second). Ford GT: fordgt_livery1 (16x16)
            // + its stripes 32..512 under fordgt_rgb_scratch. Tested in game 2026-11-06 (Ford GT, Challenger Tuner).
            if (own.Count == 1 && k + 1 < names.Count)
            {
                var next = Under(k + 1);
                for (int i = 1; i < next.Count; i++)
                    if (next[i].W == next[i - 1].W && next[i].H == next[i - 1].H && (i < 2 || next[i - 2].W != next[i].W)) o.Add(next[i - 1]);
            }
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
        // cars whose body picture has a misleading name (tested in game)
        if (BodyFix.TryGetValue(car, out var fixedPic) && Levels(data, fixedPic).Count > 0)
            return new SkinCar { Car = car, Pictures = new[] { fixedPic }.Concat(pics.OrderByDescending(p => p.Value).Select(p => p.Key).Where(n => n != fixedPic.Split('#')[0])).ToList(), Body = fixedPic };
        return new SkinCar { Car = car, Pictures = pics.OrderByDescending(p => p.Value).Select(p => p.Key).ToList(), Body = pool.OrderByDescending(n => pics[n]).First() };
    }

    /// most of the livery-painted body sits on one tiny spot of its picture (the livery is only decals)
    static bool DecalLivery(byte[] data)
    {
        var parts = BodyMeshes(data, CpModel.Read(data), plain: false);
        int tiny = 0, all = 0;
        foreach (var m in parts)
            foreach (var (a, b, c) in m.Tris)
            {
                var pa = m.Uv(data, a); var pb = m.Uv(data, b); var pc = m.Uv(data, c);
                float w = Math.Max(pa.U, Math.Max(pb.U, pc.U)) - Math.Min(pa.U, Math.Min(pb.U, pc.U)), h = Math.Max(pa.V, Math.Max(pb.V, pc.V)) - Math.Min(pa.V, Math.Min(pb.V, pc.V));
                all++; if (w < 0.002f && h < 0.002f) tiny++;
            }
        return all > 0 && tiny > all / 2;
    }

    /// more of the painted body is plain paint (veh_Paint_a...) than livery paint?
    static bool MostlyPlain(byte[] data)
    {
        var M = CpModel.Read(data);
        int plainTris = 0, liveryTris = 0;
        foreach (var m in M.MeshInfos)
        {
            if (m.Material < 0 || m.Material >= M.FxFiles.Count || m.Data1.Count == 0) continue;
            string fx = M.FxFiles[m.Material];
            if (!fx.Contains("paint", StringComparison.OrdinalIgnoreCase)) continue;
            if (fx.StartsWith("veh_paint_a", StringComparison.OrdinalIgnoreCase)) plainTris += m.Data1[0].FaceCount; else liveryTris += m.Data1[0].FaceCount;
        }
        return plainTris > liveryTris;
    }

    /// the skinnable cars (cached in work\skin_cars.json until root.pak.original changes)
    public static List<SkinCar> Cars(Action<string>? log = null)
    {
        Directory.CreateDirectory(Paths.Work);
        string cache = Path.Combine(Paths.Work, "skin_cars.json");
        var fi = new FileInfo(Paths.RootOriginal);
        string stamp = $"{fi.Length} {fi.LastWriteTimeUtc.Ticks} plain7";
        try
        {
            var d = (JObj)Json.Load(cache)!;
            if (d.Str("stamp") == stamp)
                return d.Objs("cars").Select(c => new SkinCar { Car = c.Str("car")!, Body = c.Str("body")!, Pictures = c.Arr("pictures")!.Select(p => (string)p!).ToList(), Plain = c.Truthy("plain"), Decal = c.Truthy("decal") }).ToList();
        }
        catch (Exception) { }
        log?.Invoke("Looking for cars with a full body picture (once)...");
        List<string> names;
        lock (_lock) { _pak ??= new Pak(Paths.RootOriginal); names = _pak.Files.Select(f => f.Name).Where(n => n.StartsWith(Veh) && n.EndsWith(".model") && n.Count(c => c == '\\') == 3).Select(n => n[Veh.Length..^6]).ToList(); }
        var found = new System.Collections.Concurrent.ConcurrentBag<SkinCar>();
        Parallel.ForEach(names, car =>
        {
            try
            {
                var data = ModelBytes(car);
                var sc = Scan(car, data);
                // a "livery" that is only decals on a mostly plain body: the car is better as a plain-paint car (the whole body)
                if (sc != null && MostlyPlain(data) && PlainSkin.ScratchPicture(data) is { } p0 && PlainSkin.CanAdd(data)) sc = new SkinCar { Car = car, Body = p0, Pictures = new() { p0 }, Plain = true };
                // a "livery" that puts the body on one tiny spot of a decal picture: a whole-body skin in the scratch picture instead
                else if (sc != null && DecalLivery(data) && PlainSkin.ScratchPicture(data) is { } p1 && PlainSkin.CanAdd(data, true)) sc = new SkinCar { Car = car, Body = p1, Pictures = new() { p1 }, Plain = true, Decal = true };
                if (sc is { } c) found.Add(c);
                else if (PlainSkin.ScratchPicture(data) is { } pic && PlainSkin.CanAdd(data)) found.Add(new SkinCar { Car = car, Body = pic, Pictures = new() { pic }, Plain = true });
            }
            catch (Exception) { }
        });
        var o = found.OrderBy(c => c.Car, StringComparer.Ordinal).ToList();
        Json.Save(new JObj { ["stamp"] = stamp, ["cars"] = o.Select(c => (object?)new JObj { ["car"] = c.Car, ["body"] = c.Body, ["pictures"] = c.Pictures.Select(p => (object?)p).ToList(), ["plain"] = c.Plain, ["decal"] = c.Decal }).ToList() }, cache);
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

    /// one painted mesh: its vertex block, first vertex, where its UV sits (Pair 0 = the first two numbers = the livery's UV,
    /// 2 = the last two = the scratch / dent UV), the vertices it uses and its triangles (strip joins with no area left out)
    public sealed record BodyMesh(VStreamInfo Vs, int VBase, int UvAt, int Pair, int[] Verts, List<(int A, int B, int C)> Tris)
    {
        public float[] Ratio = Array.Empty<float>();
        /// where the skin puts this piece: u' = u * S + Ou, v' = v * S + Ov (moved off a piece it lay on top of)
        public float S = 1, Ou, Ov;
        public (float U, float V) Uv(byte[] d, int v)
        {
            int at = Vs.Start + (VBase + v) * Vs.Bytes + UvAt + 2 * Pair;
            return ((float)BitConverter.UInt16BitsToHalf(B.U16(d, at)) * S + Ou, (float)BitConverter.UInt16BitsToHalf(B.U16(d, at + 2)) * S + Ov);
        }
    }

    static float Half(byte[] d, int at) => (float)BitConverter.UInt16BitsToHalf(B.U16(d, at));

    /// the painted meshes of a model. Several vertex blocks can share one layout: each mesh gets the block where its vertices
    /// make sense. plain = a plain-paint body: its UV is the scratch UV (the last two numbers) when it has one
    public static List<BodyMesh> BodyMeshes(byte[] data, ModelInfo M, bool plain)
    {
        var o = new List<BodyMesh>();
        foreach (var mesh in M.MeshInfos)
        {
            if (mesh.Material < 0 || mesh.Material >= M.FxFiles.Count || !M.FxFiles[mesh.Material].Contains("paint", StringComparison.OrdinalIgnoreCase)) continue;
            // a livery car: only the meshes that wear the livery (a plain paint part never shows it)
            if (!plain && M.FxFiles[mesh.Material].StartsWith("veh_paint_a", StringComparison.OrdinalIgnoreCase)) continue;
            if (mesh.Definition >= M.VertDefs.Count || mesh.FaceStream >= M.FStreams.Count || mesh.Data1.Count == 0 || mesh.VOffset.Count == 0) continue;
            var types = M.VertDefs[mesh.Definition].Where(d => d.Prefix == 0).Select(d => d.Type).ToList();
            var (fo, fc, _, _) = mesh.Data1[0];
            // the mesh's triangle list: the one it names, unless its triangles do not fit there (then the one they fit in)
            var fs = M.FStreams[mesh.FaceStream];
            if (fo + fc > fs.Count) fs = M.FStreams.FirstOrDefault(f => fo + fc <= f.Count) ?? fs;
            if (fo < 0 || fc <= 0 || fo + fc > fs.Count || fs.Start + 2 * (fo + fc) > data.Length) continue;
            var idx = new int[fc];
            for (int i = 0; i < fc; i++) idx[i] = B.U16(data, fs.Start + 2 * (fo + i));
            var raw = new List<(int, int, int)>();
            if (mesh.FaceType == 1) for (int i = 0; i + 2 < fc; i++) raw.Add((idx[i], idx[i + 1], idx[i + 2]));
            else for (int i = 0; i + 2 < fc; i += 3) raw.Add((idx[i], idx[i + 1], idx[i + 2]));
            raw.RemoveAll(t => t.Item1 == t.Item2 || t.Item2 == t.Item3 || t.Item1 == t.Item3);
            var used = idx.Distinct().ToArray();
            BodyMesh? best = null; double bestScore = double.MinValue;
            foreach (var (vs, vbase) in M.VStreams.Select(b => (b, b.Bytes > 0 && mesh.VOffset[0] % b.Bytes == 0 ? mesh.VOffset[0] / b.Bytes : -1)))
            {
                if (vbase < 0 || !vs.Def.Select(d => d.Type).SequenceEqual(types) || vs.Bytes <= 0) continue;
                if (used.Length == 0 || vbase + used.Max() >= vs.Count) continue;
                int off = 0, uvAt = -1, posAt = -1, posType = 0;
                foreach (var (t, ch, sub) in vs.Def)
                {
                    if (ch == 5 && sub == 0 && t == 9 && uvAt < 0) uvAt = off;
                    if (ch == 0 && sub == 0 && posAt < 0) { posAt = off; posType = t; }
                    off += t == 6 ? 12 : t == 9 ? 8 : 4;
                }
                if (uvAt < 0 || posAt < 0) continue;
                int At(int v) => vs.Start + (vbase + v) * vs.Bytes;
                // which UV: the scratch UV (last two) for a plain body when it has one, else the first two
                int pair = 0;
                if (plain)
                {
                    float lo = 9, hi = -9;
                    foreach (int v in used) { float z = Half(data, At(v) + uvAt + 4); lo = Math.Min(lo, z); hi = Math.Max(hi, z); }
                    if (hi - lo > 0.01f) pair = 2;
                }
                int inside = 0;
                foreach (int v in used)
                {
                    float u = Half(data, At(v) + uvAt + 2 * pair), q = Half(data, At(v) + uvAt + 2 * pair + 2);
                    if (u >= -0.01f && u <= 1.01f && q >= -0.01f && q <= 1.01f && (u != 0 || q != 0)) inside++;
                }
                // the triangles on the car: short edges = the right block
                (float X, float Y, float Z) P(int v)
                {
                    int a = At(v) + posAt;
                    return posType == 6 ? (BitConverter.ToSingle(data, a), BitConverter.ToSingle(data, a + 4), BitConverter.ToSingle(data, a + 8)) : (Half(data, a), Half(data, a + 2), Half(data, a + 4));
                }
                var tris = new List<(int, int, int)>(); var edges = new List<float>(); float[] rat = Array.Empty<float>();
                foreach (var (a, b, c) in raw)
                {
                    var pa = P(a); var pb = P(b); var pc = P(c);
                    float ux = pb.X - pa.X, uy = pb.Y - pa.Y, uz = pb.Z - pa.Z, wx = pc.X - pa.X, wy = pc.Y - pa.Y, wz = pc.Z - pa.Z;
                    float cx = uy * wz - uz * wy, cy = uz * wx - ux * wz, cz = ux * wy - uy * wx;
                    if (!(cx * cx + cy * cy + cz * cz >= 1e-10f)) continue;             // a strip join: no area on the car
                    tris.Add((a, b, c)); edges.Add(MathF.Sqrt(ux * ux + uy * uy + uz * uz));
                }
                if (tris.Count == 0) continue;
                // triangles far bigger on the picture than on the car join two pieces: leave them out (they only draw long lines)
                {
                    var ratio = new List<(float R, int I)>();
                    for (int t = 0; t < tris.Count; t++)
                    {
                        var (a, b, c) = tris[t];
                        float U(int v, int k) => Half(data, At(v) + uvAt + 2 * pair + 2 * k);
                        float uvL = MathF.Sqrt(MathF.Pow(U(a, 0) - U(b, 0), 2) + MathF.Pow(U(a, 1) - U(b, 1), 2)) + MathF.Sqrt(MathF.Pow(U(b, 0) - U(c, 0), 2) + MathF.Pow(U(b, 1) - U(c, 1), 2)) + MathF.Sqrt(MathF.Pow(U(c, 0) - U(a, 0), 2) + MathF.Pow(U(c, 1) - U(a, 1), 2));
                        var pa = P(a); var pb = P(b); var pc = P(c);
                        float L3(( float X, float Y, float Z) x, (float X, float Y, float Z) y) => MathF.Sqrt((x.X - y.X) * (x.X - y.X) + (x.Y - y.Y) * (x.Y - y.Y) + (x.Z - y.Z) * (x.Z - y.Z));
                        float carL = L3(pa, pb) + L3(pb, pc) + L3(pc, pa);
                        // squashed flat on the picture although a normal triangle on the car (a part seen edge-on: the underside)
                        float uvA = MathF.Abs((U(b, 0) - U(a, 0)) * (U(c, 1) - U(a, 1)) - (U(c, 0) - U(a, 0)) * (U(b, 1) - U(a, 1))) / 2;
                        float ux3 = pb.X - pa.X, uy3 = pb.Y - pa.Y, uz3 = pb.Z - pa.Z, wx3 = pc.X - pa.X, wy3 = pc.Y - pa.Y, wz3 = pc.Z - pa.Z;
                        float cx3 = uy3 * wz3 - uz3 * wy3, cy3 = uz3 * wx3 - ux3 * wz3, cz3 = ux3 * wy3 - uy3 * wx3;
                        float carA = MathF.Sqrt(cx3 * cx3 + cy3 * cy3 + cz3 * cz3) / 2;
                        ratio.Add((carL > 1e-6f ? uvL / carL : 0, t));
                    }
                    var sorted = ratio.Select(q => q.R).Where(q => q > 0).OrderBy(q => q).ToList();
                    if (sorted.Count > 8)
                    {
                        float medR = sorted[sorted.Count / 2];
                        var keep = ratio.Where(q => q.R <= medR * 6 || q.R == 0).ToList();
                        if (keep.Count > 0) ratio = keep;
                    }
                    rat = ratio.Select(q => q.R).ToArray(); tris = ratio.Select(q => tris[q.I]).ToList();
                }
                edges.Sort();
                float med = edges[edges.Count / 2];
                double score = (double)inside / used.Length - Math.Min(1.0, med);       // most UVs on the picture, small triangles
                if (score > bestScore) { bestScore = score; best = new BodyMesh(vs, vbase, uvAt, pair, used, tris) { Ratio = rat }; }
            }
            if (best != null) o.Add(best);
        }
        // across the whole car: pieces (and triangles) far more stretched on the picture than the body is (an underside or a
        // round part laid over the whole picture) are left out of the drawing
        var all = o.SelectMany(m => m.Ratio).Where(r => r > 0).OrderBy(r => r).ToList();
        if (all.Count > 20)
        {
            float med = all[all.Count / 2];
            var kept = new List<BodyMesh>();
            foreach (var m in o)
            {
                var rs = m.Ratio.Where(r => r > 0).OrderBy(r => r).ToList();
                if (rs.Count > 0 && rs[rs.Count / 2] > med * 4) continue;               // the whole piece is stretched
                float U(int v, int k) => Half(data, m.Vs.Start + (m.VBase + v) * m.Vs.Bytes + m.UvAt + 2 * m.Pair + 2 * k);
                bool Huge((int A, int B, int C) t)                                           // one triangle over most of the picture
                {
                    float u0 = Math.Min(U(t.A, 0), Math.Min(U(t.B, 0), U(t.C, 0))), u1 = Math.Max(U(t.A, 0), Math.Max(U(t.B, 0), U(t.C, 0)));
                    float v0 = Math.Min(U(t.A, 1), Math.Min(U(t.B, 1), U(t.C, 1))), v1 = Math.Max(U(t.A, 1), Math.Max(U(t.B, 1), U(t.C, 1)));
                    if (u1 - u0 > 0.6f || v1 - v0 > 0.6f) return true;
                    // a long sliver (almost no area on the picture): the lip under the body, laid along the picture's edges
                    float ax = U(t.A, 0), ay = U(t.A, 1), bx = U(t.B, 0), by = U(t.B, 1), cx = U(t.C, 0), cy = U(t.C, 1);
                    float area = Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay)) / 2;
                    float per = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) + MathF.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by)) + MathF.Sqrt((ax - cx) * (ax - cx) + (ay - cy) * (ay - cy));
                    return per > 0.12f && area < per * per * 0.006f;
                }
                var tris = m.Tris.Where((t, i) => (i >= m.Ratio.Length || m.Ratio[i] <= med * 8) && !Huge(t)).ToList();
                if (tris.Count > 0) kept.Add(m with { Tris = tris });
            }
            o = kept;
        }
        if (plain) Unoverlap(data, o);
        return o;
    }

    /// a plain body's skin layout: pieces the game laid on top of other pieces (a side skirt over the doors...) move to an empty
    /// place of the picture (smaller if they must). Pieces sharing points with another piece stay. The scratch marks keep the
    /// game's layout (they read the other UV numbers)
    public static Action<string>? UnoverlapLog;
    static void Unoverlap(byte[] data, List<BodyMesh> parts)
    {
        const int N = 128;
        bool[,] Mask(BodyMesh m)
        {
            var mk = new bool[N, N];
            foreach (var (a, b, c) in m.Tris)
            {
                var pa = m.Uv(data, a); var pb = m.Uv(data, b); var pc = m.Uv(data, c);
                float x0 = Math.Min(pa.U, Math.Min(pb.U, pc.U)) * N, x1 = Math.Max(pa.U, Math.Max(pb.U, pc.U)) * N;
                float y0 = Math.Min(pa.V, Math.Min(pb.V, pc.V)) * N, y1 = Math.Max(pa.V, Math.Max(pb.V, pc.V)) * N;
                for (int y = Math.Max(0, (int)y0); y <= Math.Min(N - 1, (int)y1); y++)
                    for (int x = Math.Max(0, (int)x0); x <= Math.Min(N - 1, (int)x1); x++)
                    {
                        // the cell's middle inside the triangle?
                        float px = (x + 0.5f) / N, py = (y + 0.5f) / N;
                        float d1 = (px - pb.U) * (pa.V - pb.V) - (pa.U - pb.U) * (py - pb.V), d2 = (px - pc.U) * (pb.V - pc.V) - (pb.U - pc.U) * (py - pc.V), d3 = (px - pa.U) * (pc.V - pa.V) - (pc.U - pa.U) * (py - pa.V);
                        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                        if (!(neg && pos)) mk[y, x] = true;
                    }
                // thin triangles: their edges
                foreach (var (p, q) in new[] { (pa, pb), (pb, pc), (pc, pa) })
                {
                    int steps = (int)(Math.Max(Math.Abs(p.U - q.U), Math.Abs(p.V - q.V)) * N * 2) + 1;
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = (float)k / steps; int x = (int)((p.U + (q.U - p.U) * t) * N), y = (int)((p.V + (q.V - p.V) * t) * N);
                        if (x >= 0 && x < N && y >= 0 && y < N) mk[y, x] = true;
                    }
                }
            }
            return mk;
        }
        int Count(bool[,] mk) { int n = 0; foreach (bool q in mk) if (q) n++; return n; }
        // who shares points with whom
        var keys = parts.Select(m => new HashSet<long>(m.Verts.Select(v => ((long)m.Vs.Start << 24) + m.VBase + v))).ToList();
        var masks = parts.Select(Mask).ToList();
        var order = Enumerable.Range(0, parts.Count).OrderByDescending(i => parts[i].Tris.Count).ToList();   // detailed pieces keep their place
        var placed = new bool[N, N];
        foreach (int i in order)
        {
            var m = masks[i];
            int area = Count(m), hit = 0;
            for (int y = 1; y < N - 1; y++) for (int x = 1; x < N - 1; x++)
                if (m[y, x] && placed[y, x] && placed[y - 1, x] && placed[y + 1, x] && placed[y, x - 1] && placed[y, x + 1]) hit++;   // inside the others, not their edge
            bool shares = Enumerable.Range(0, parts.Count).Any(j => j != i && keys[j].Overlaps(keys[i]));
            UnoverlapLog?.Invoke($"part {i}: area {area} cells, on others {hit}, shares {shares}");
            if (area > 0 && hit > area * 0.15 && !shares)
            {
                // the piece's box, then the first empty place for it (top-left first), smaller if it does not fit
                int bx0 = N, by0 = N, bx1 = -1, by1 = -1;
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) if (m[y, x]) { bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y); by1 = Math.Max(by1, y); }
                bool done = false;
                for (float sc = 1f; sc >= 0.3f && !done; sc *= 0.8f)
                {
                    int w = (int)Math.Ceiling((bx1 - bx0 + 1) * sc), h = (int)Math.Ceiling((by1 - by0 + 1) * sc);
                    for (int ty = 0; ty + h <= N && !done; ty++)
                        for (int tx = 0; tx + w <= N && !done; tx++)
                        {
                            bool free = true;
                            for (int y = 0; y < N && free; y++)
                                for (int x = 0; x < N && free; x++)
                                    if (m[y, x])
                                    {
                                        int nx = tx + (int)((x - bx0) * sc), ny = ty + (int)((y - by0) * sc);
                                        for (int yy = Math.Max(0, ny - 1); yy <= Math.Min(N - 1, ny + 1) && free; yy++)
                                            for (int xx = Math.Max(0, nx - 1); xx <= Math.Min(N - 1, nx + 1) && free; xx++)
                                                if (placed[yy, xx]) free = false;
                                    }
                            if (!free) continue;
                            var p = parts[i];
                            float s0 = p.S;
                            p.S = s0 * sc; p.Ou = p.Ou * sc + (float)tx / N - (float)bx0 / N * sc; p.Ov = p.Ov * sc + (float)ty / N - (float)by0 / N * sc;
                            m = Mask(p); done = true;
                        }
                }
            }
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) if (m[y, x]) placed[y, x] = true;
        }
    }

    /// the car's painted triangles where they sit on the picture: yellow lines on a clear picture (or on `over`).
    /// mask: the painted parts filled solid white on a clear picture (a selection for Photoshop / GIMP)
    public static Rgba UvMap(string car, string pic, int w = 0, int h = 0, Rgba? over = null, bool mask = false)
    {
        var data = ModelBytes(car);
        if (w == 0) { var l = Levels(data, pic).OrderByDescending(q => q.W * q.H).First(); w = l.W; h = l.H; }
        var M = CpModel.Read(data);
        using var bmp = over != null ? over.Resize(w, h).ToBitmap() : new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var pen = new Pen(mask ? Color.White : Color.FromArgb(255, 255, 230, 0), Math.Max(1f, w / 1024f)))
        using (var fill = new SolidBrush(Color.White))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var m in BodyMeshes(data, M, IsPlain(car)))
            {
                PointF? Uv(int v)
                {
                    var (u, q) = m.Uv(data, v);
                    if (u < -0.01f || u > 1.01f || q < -0.01f || q > 1.01f) return null;
                    return new PointF(u * w, q * h);
                }
                foreach (var (a, b, c) in m.Tris)
                    if (Uv(a) is { } pa && Uv(b) is { } pb && Uv(c) is { } pc)
                    {
                        if (mask) g.FillPolygon(fill, new[] { pa, pb, pc });                  // the mask: the painted parts solid white
                        g.DrawPolygon(pen, new[] { pa, pb, pc });
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

    /// is this a plain-paint car (its skin needs a picture slot first)?
    public static bool IsPlain(string car) { try { return Cars().Any(c => c.Car == car && c.Plain); } catch (Exception) { return false; } }
    public static bool IsDecal(string car) { try { return Cars().Any(c => c.Car == car && c.Decal); } catch (Exception) { return false; } }

    /// a copy of the model with the skins written over its pictures, every stored size (same byte length; a plain-paint car first
    /// gets its picture slot, a little longer)
    public static byte[] Apply(byte[] model, string car, IEnumerable<string> pics, Action<string>? log = null)
    {
        bool plain = IsPlain(car);
        if (plain) model = PlainSkin.AddSlot(model, IsDecal(car)) ?? throw new InvalidDataException(Nice(car) + ": no picture slot could be added");
        var o = (byte[])model.Clone();
        foreach (var pic in pics)
        {
            var img = Rgba.Load(SkinFile(car, pic));
            if (plain) img = img.OnColour(200, 200, 200);          // its picture keeps no see-through: clear parts become light grey
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
    public static (int On, int Back) Install(Action<string> log, Action<string>? step = null)
    {
        string pack = Path.Combine(Paths.PackDir, "root.pak");
        if (!File.Exists(pack)) throw new FileNotFoundException("Blur REBORN's root.pak is not there yet: press Build + install once first.");
        var on = OnNow();
        var before = LoadJson("installed.json").Arr("cars")?.Select(c => (string)c!).ToHashSet() ?? new HashSet<string>();
        var files = new List<KeyValuePair<string, byte[]>>();
        int k = 0;
        foreach (var (car, pics) in on) { step?.Invoke($"Preparing {Nice(car)} ({++k} of {on.Count})"); files.Add(new(ModelPath(car), Apply(ModelBytes(car), car, pics, log))); }
        var made = files.ToDictionary(f => f.Key, f => f.Value);
        int back = 0;
        foreach (var car in before.Where(c => !on.ContainsKey(c))) { files.Add(new(ModelPath(car), ModelBytes(car))); back++; log($"   {Nice(car)}: the game's own picture again"); }
        if (files.Count > 0)
        {
            string tmp = pack + ".skins.tmp";
            step?.Invoke("Writing Blur REBORN's pack...");
            Pak.ReplaceFiles(pack, tmp, files);
            File.Move(tmp, pack, true);
        }
        var inst = LoadJson("installed.json"); inst["cars"] = on.Keys.Select(k => (object?)k).ToList();
        SaveJson("installed.json", inst);
        InstallNormal(on, log, step, made);
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

    static void InstallNormal(Dictionary<string, List<string>> on, Action<string> log, Action<string>? step = null, Dictionary<string, byte[]>? made = null)
    {
        bool want = Normal && on.Count > 0;
        if (!want && !InNormalNow) return;
        string pack = NormalPack, tmp = pack + ".skins.tmp";
        Directory.CreateDirectory(Paths.Cache);
        if (want)
        {
            // the same skinned cars as Blur REBORN's (made once)
            var files = on.Select(kv => new KeyValuePair<string, byte[]>(ModelPath(kv.Key), made != null && made.TryGetValue(ModelPath(kv.Key), out var m) ? m : Apply(ModelBytes(kv.Key), kv.Key, kv.Value))).ToList();
            step?.Invoke("Writing normal Blur's pack...");
            Pak.ReplaceFiles(Paths.RootOriginal, tmp, files);
            log($"normal Blur: {on.Count} car(s) with your skin in {pack}");
        }
        else
        {
            step?.Invoke("Putting normal Blur's own pack back...");
            File.Copy(Paths.RootOriginal, tmp, true);
            log($"normal Blur: the game's own {pack} is back");
        }
        File.Move(tmp, pack, true);
        var inst = LoadJson("installed.json"); inst["normal"] = want;
        SaveJson("installed.json", inst);
    }
}

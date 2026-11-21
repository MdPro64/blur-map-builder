// Custom cars in the build. Ports of car/car_build.py, car_glb.py, car_icon.py, cars_data.py and the reading half of car_model.py.
using System.Drawing;
using System.Text.RegularExpressions;

namespace Bmb.Core;

public sealed class GameCar { public string Name = "", Icon = ""; public int Class; public bool Pick; public long Mph; public Dictionary<string, long> Stats = new(); }

public static class CarsData
{
    public static readonly (string Key, string Field)[] Stats = { ("speed", "UiSpeed"), ("acceleration", "UiAcceleration"), ("drift", "UiDrift"), ("grip", "UiGrip"), ("strength", "UiStrength") };
    static List<GameCar>? _cars;

    public static List<GameCar> Cars()
    {
        if (_cars != null) return _cars;
        var d = File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin"));
        var k = new Kslf(d);
        var mp = new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "multiplayer.bin")));
        var cfg = mp.Objects.First(o => mp.Types[o.Blocks[0].Type].Name == "MultiplayerConfig");
        int off = mp.Types[cfg.Blocks[0].Type].Fields.First(f => f.Name == "vehicles").Offset;
        int at = cfg.Blocks[0].Offset + off, blk = B.U16(mp.Data, at), first = B.U16(mp.Data, at + 2); int cnt = (int)B.U32(mp.Data, at + 4);
        var picks = new HashSet<uint>();
        for (int j = 0; j < cnt; j++) picks.Add(mp.Refs[B.U32(mp.Data, cfg.Blocks[blk].Offset + 4 * (first + j))].Uid);
        var uidOf = new Dictionary<int, uint>();
        foreach (var (u, t, oi) in k.Refs) if (k.Types[t].Name == "AmaxVehicle") uidOf[oi] = u;
        var o2 = new List<GameCar>();
        for (int oi = 0; oi < k.Objects.Count; oi++)
        {
            var o = k.Objects[oi]; var b = o.Blocks[0];
            if (k.Types[b.Type].Name != "AmaxVehicle") continue;
            var f = k.AllFields(b.Type).GroupBy(q => q.Name).ToDictionary(g => g.Key, g => g.Last());
            long Num(string name)
            {
                var q = f[name]; string tn = q.Type < k.Types.Count ? k.Types[q.Type].Name : "";
                return tn == "f32" ? (long)B.F32(d, b.Offset + q.Offset) : tn == "u8" ? d[b.Offset + q.Offset] : B.I32(d, b.Offset + q.Offset);
            }
            string Text(string name) => B.Sz(o.Pool, (int)B.U32(d, b.Offset + f[name].Offset));
            var c = new GameCar { Name = Text("Name"), Class = (int)Num("class"), Pick = uidOf.TryGetValue(oi, out uint uid) && picks.Contains(uid), Icon = Text("UiIconName"), Mph = Num("UiMaxSpeed") };
            foreach (var (key, field) in Stats) c.Stats[key] = Num(field);
            o2.Add(c);
        }
        var seen = new HashSet<string>();
        return _cars = o2.OrderBy(c => c.Class).ThenBy(c => c.Name, StringComparer.Ordinal).Where(c => seen.Add(c.Name)).ToList();
    }

    public static List<GameCar> ClassCars(int cls) => Cars().Where(c => c.Pick && c.Class == cls).ToList();
    public static (long Lo, long Hi) Limit(int cls, string key) { var c = ClassCars(cls); return (c.Min(q => q.Stats[key]), c.Max(q => q.Stats[key])); }

    public static long TopSpeed(int cls, long speed)
    {
        var c = ClassCars(cls);
        var (lo, hi) = Limit(cls, "speed");
        double t = hi == lo ? 0.5 : (double)(speed - lo) / (hi - lo);
        long m0 = c.Min(q => q.Mph), m1 = c.Max(q => q.Mph);
        return Py.Round(m0 + t * (m1 - m0));
    }
}

/// one KSLF file with fields by dotted path (car_build.Db)
sealed class Db
{
    public readonly byte[] Data; public readonly Kslf K;
    public Db(byte[] d) { Data = d; K = new Kslf(d); }
    public string TName(int i) => K.Types[K.Objects[i].Blocks[0].Type].Name;

    public (int Off, string Tn) Field(int t, string path)
    {
        int off = 0;
        foreach (var part in path.Split('.'))
        {
            var f = K.AllFields(t).First(q => q.Name == part);
            off += f.Offset; t = f.Type;
        }
        return (off, K.Types[t].Name);
    }

    public double Get(int i, string path)
    {
        var b = K.Objects[i].Blocks[0];
        var (off, tn) = Field(b.Type, path);
        int a = b.Offset + off;
        return tn switch
        {
            "i32" => B.I32(Data, a), "f32" => B.F32(Data, a), "u8" => Data[a], "u16" => B.U16(Data, a), "i16" => B.I16(Data, a), _ => B.U32(Data, a),
        };
    }

    public int ObjOfUid(uint uid) => K.Refs.First(r => r.Uid == uid).Obj;
    public int RowOfUid(uint uid) => Array.FindIndex(K.Refs, r => r.Uid == uid);
    public uint RefUid(int i, string path) => K.Refs[(int)Get(i, path)].Uid;
    public string Name(int i) { var o = K.Objects[i]; return B.Sz(o.Pool, (int)B.U32(Data, o.Blocks[0].Offset + 12)); }

    public Dictionary<string, int> Cars()
    {
        var o = new Dictionary<string, int>();
        for (int i = 0; i < K.Objects.Count; i++) if (TName(i) == "AmaxVehicle") o[Name(i)] = i;
        return o;
    }

    public void Set(ByteBuf data, int at, int t, string path, double v)
    {
        var (off, tn) = Field(t, path);
        int a = at + off;
        switch (tn)
        {
            case "f32": data.Put(a, (float)v); break;
            case "i32": data.Put(a, (int)v); break;
            case "u8": data[a] = (byte)v; break;
            case "u16": case "i16": data.Put(a, (ushort)(int)v); break;
            default: data.Put(a, (uint)(long)v); break;
        }
    }
}

/// the game car's parts (car_model.read_game_car)
sealed class GameParts
{
    public Dictionary<string, List<float[]>> Tris = new();     // kind[|corner]: x y z per corner, 9 floats a triangle
    public Dictionary<string, double[]> Centre = new();
}

public static class CarModel
{
    static readonly string[] Corners = { "frontleft", "frontright", "backleft", "backright" }, Skip = { "dam", "debris", "broken", "collision", "shadow", "smash" };
    public const int Atlas = 1024, Swatch = 32, Edge = 4, ClearSpot = 64;
    public const double InteriorBoost = 1;      // TEST, 1 = off
    public const double ClearUv = 1 - ClearSpot / 2.0 / Atlas;      // middle of the glass corner
    public static string ModelPath(string name) => $"models\\vehicles\\{name.ToLowerInvariant()}.model";
    public static byte[] ModelBytes(Pak root, string model) => root.Read("\\" + model.ToLowerInvariant().TrimStart('\\')) ?? throw new FileNotFoundException(model);

    public static string Kind(string el)
    {
        el = el.ToLowerInvariant();
        if (el.Contains("tyre")) return "tyre";
        if (el.Contains("brakelight")) return "light";     // "brake" alone means a wheel's disc / caliper
        if (el.Contains("alloyrim") || el.Contains("brake") || el.Contains("hubcap") || el.Contains("rim")) return "rim";
        if (el.Contains("glass") || el.Contains("window") || el.Contains("screen")) return "glass";
        if (el.Contains("light") || el.Contains("lamp")) return "light";
        if (new[] { "interior", "steering", "gearstick", "signalstalk", "wiper", "mirror_centre", "exhaust", "grille", "driver" }.Any(el.Contains)) return "dark";
        return "paint";
    }

    /// an imported body's glass is see-through (its own clear glass) unless the car says no (Custom car tab: a body
    /// without an interior keeps the game's dark windows)
    public static bool ClearGlass(JObj car) => !car.ContainsKey("clear_glass") || car.Truthy("clear_glass");
    /// the body's paint parts take the player's paint colour (the game's own paint), decals are drawn over it; off = every colour baked into the picture
    public static bool PaintBody(JObj car) => car.Truthy("paint_body");
    /// decals in the game's alpha part (not working in game yet): off; decals are laid over the body colour in the body picture instead
    public static bool DecalAlpha(JObj car) => car.Truthy("decal_alpha");      // OFF unless a car asks for it (2026-11-21: the user wants imported cars to keep their own colours)

    public static string? Corner(string el) { el = el.ToLowerInvariant().Replace("_", ""); return Corners.FirstOrDefault(el.Contains); }
    static string TypesKey(IEnumerable<int> t) => string.Join(",", t);

    internal static GameParts ReadGameCar(Pak root, string name)
    {
        var data = ModelBytes(root, ModelPath(name));
        var M = CpModel.Read(data);
        var streams = new Dictionary<string, VStreamInfo>();
        foreach (var vs in M.VStreams) streams[TypesKey(vs.Def.Select(d => d.Type))] = vs;
        var o = new GameParts();
        foreach (var mesh in M.MeshInfos)
        {
            int oi = mesh.Object;
            string el = oi < M.AllElements.Count && oi >= 0 ? M.AllElements[oi].Name : "";
            string low = el.ToLowerInvariant();
            if (Skip.Any(low.Contains)) continue;
            if (!streams.TryGetValue(TypesKey(M.VertDefs[mesh.Definition].Where(d => d.Prefix == 0).Select(d => d.Type)), out var vs) || vs.Def[0].Type != 9) continue;
            int stride = vs.Bytes;
            var fs = M.FStreams[mesh.FaceStream];
            var (fo, fc, _, _) = mesh.Data1[0];
            int bas = mesh.VOffset[0] / stride;
            var tri = new List<int>();
            if (mesh.FaceType == 1)
            {
                if (fc < 3) continue;
                for (int i = 0; i + 2 < fc; i++)
                {
                    int a = B.U16(data, fs.Start + 2 * (fo + i)), b = B.U16(data, fs.Start + 2 * (fo + i + 1)), c = B.U16(data, fs.Start + 2 * (fo + i + 2));
                    if (i % 2 != 0) (a, c) = (c, a);
                    if (a != b && b != c && a != c) tri.AddRange(new[] { a, b, c });
                }
            }
            else
                for (int i = 0; i + 2 < fc; i += 3)
                {
                    int a = B.U16(data, fs.Start + 2 * (fo + i)), b = B.U16(data, fs.Start + 2 * (fo + i + 1)), c = B.U16(data, fs.Start + 2 * (fo + i + 2));
                    if (a != b && b != c && a != c) tri.AddRange(new[] { a, b, c });
                }
            if (tri.Count == 0 || tri.Max() + bas >= vs.Count) continue;
            var e = oi < M.AllElements.Count ? M.AllElements[oi] : null;
            double wx = e?.M[9] ?? 0, wy = e?.M[10] ?? 0, wz = e?.M[11] ?? 0;
            var pos = new float[tri.Count * 3];
            for (int i = 0; i < tri.Count; i++)
            {
                int at = vs.Start + stride * (tri[i] + bas);
                pos[3 * i] = (float)((double)(float)BitConverter.ToHalf(data, at) + wx);
                pos[3 * i + 1] = (float)((double)(float)BitConverter.ToHalf(data, at + 2) + wy);
                pos[3 * i + 2] = (float)((double)(float)BitConverter.ToHalf(data, at + 4) + wz);
            }
            string kind = Kind(el); var corner = Corner(el);
            string key = kind + (corner != null && kind is "tyre" or "rim" && corner != null ? "|" + corner : "");
            if (!o.Tris.TryGetValue(key, out var l)) o.Tris[key] = l = new();
            l.Add(pos);
        }
        foreach (var c in Corners)
            if (o.Tris.TryGetValue("tyre|" + c, out var t))
            {
                double[] lo = { 1e30, 1e30, 1e30 }, hi = { -1e30, -1e30, -1e30 };
                foreach (var a in t) for (int i = 0; i < a.Length; i++) { lo[i % 3] = Math.Min(lo[i % 3], a[i]); hi[i % 3] = Math.Max(hi[i % 3], a[i]); }
                o.Centre[c] = new[] { (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2 };
            }
        return o;
    }

    static double Srgb(double v) { v = Math.Max(0, Math.Min(1, v)); return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055; }

    public sealed class Body
    {
        public float[] Tri = null!, Uv = null!; public Pic? Image; public bool[] Glass = Array.Empty<bool>();
        /// per triangle: 0 trim, 1 the body's paint (colour-only paint material), 2 decal (a picture with see-through parts)
        public byte[] Kind = Array.Empty<byte>();
        /// per triangle: 1 = not shiny (interior, plastic ...): its vertices get a low ambient-occlusion byte, so little reflection
        public byte[] Matte = Array.Empty<byte>();
        /// the decals' own picture (R G B A) and the uv of every triangle in it (only the decal triangles' are used)
        public Rgba? DecalImage; public float[] DecalUv = Array.Empty<float>();
    }     // 9 floats / 6 floats / 1 per triangle

    public static Body ReadGlbBody(string path)
    {
        var g = ModelImport.ReadGlb(path);
        var (parts, _, _) = ModelImport.GameSpace(g);
        var trisL = new List<double[]>(); var uvsL = new List<double[]>();
        var looks = new List<(GlbMat? M, Pic? Img, double[] C)>();
        var glassL = new List<bool>(); var kindL = new List<byte>(); var matteL = new List<byte>(); var mats = new List<GlbMat?>();
        var decalPic = DecalOnBody(g, parts);
        // TEST (2026-11-21): the cabin looks pure black in game - interior pictures x InteriorBoost, to see if it is only too dark
        var boosted = new Dictionary<GlbMat, Pic>();
        if (InteriorBoost > 1)
            foreach (var m in g.Materials.Where(q => q.Image != null && q.Name.Contains("interior", StringComparison.OrdinalIgnoreCase)))
            {
                var px = new byte[m.Image!.Px.Length];
                for (int i = 0; i < px.Length; i++) px[i] = (byte)Math.Min(255, m.Image.Px[i] * InteriorBoost);
                boosted[m] = new Pic(m.Image.W, m.Image.H, px);
            }
        foreach (var p in parts)
        {
            int nt = p.Tri.Length / 3;
            var t = new double[nt * 9]; var uv = new double[nt * 6];
            for (int i = 0; i < p.Tri.Length; i++)
            {
                int v = p.Tri[i];
                t[3 * i] = p.Pos[3 * v]; t[3 * i + 1] = p.Pos[3 * v + 1]; t[3 * i + 2] = p.Pos[3 * v + 2];
                uv[2 * i] = p.Uv[2 * v]; uv[2 * i + 1] = p.Uv[2 * v + 1];
            }
            var mat = p.Mat >= 0 && p.Mat < g.Materials.Count ? g.Materials[p.Mat] : null;
            looks.Add(mat != null && mat.Image != null ? (mat, decalPic.TryGetValue(p, out var dp) ? dp : boosted.TryGetValue(mat, out var bp) ? bp : mat.Image, mat.Colour[..3]) : (mat, null, mat != null ? mat.Colour[..3] : new[] { 0.7, 0.7, 0.7 }));
            trisL.Add(t); uvsL.Add(uv);
            for (int i = 0; i < nt; i++) { matteL.Add((byte)(mat?.Matte == true ? 1 : 0)); glassL.Add(mat?.Glass == true); kindL.Add((byte)(mat?.Paint == true ? 1 : mat?.Decal == true ? 2 : 0)); }
            mats.Add(mat);
        }
        var (uvs, image) = OneTexture(uvsL, looks);
        int n = trisL.Sum(t => t.Length);
        var tri = new float[n]; var outUv = new float[n / 3 * 2];
        int a = 0, b = 0;
        for (int k = 0; k < trisL.Count; k++)
        {
            foreach (double v in trisL[k]) tri[a++] = (float)v;
            foreach (double v in uvs[k]) outUv[b++] = (float)v;
        }
        var (decUvs, decImg) = DecalTiles(uvsL, mats);
        var decUv = new float[outUv.Length]; int d = 0;
        foreach (var u in decUvs) foreach (double v in u) decUv[d++] = (float)v;
        return new Body { Tri = tri, Uv = outUv, Image = image, Glass = glassL.ToArray(), Kind = kindL.ToArray(), Matte = matteL.ToArray(), DecalImage = decImg, DecalUv = decUv };
    }

    /// each decal picture (badge, sticker: a picture with see-through parts) laid over the colour of the body parts nearest to it, so
    /// its see-through parts show the body colour instead of a solid box (the game's pictures for body parts have no see-through layer)
    static Dictionary<GlbPart, Pic> DecalOnBody(Glb g, List<GlbPart> parts)
    {
        var o = new Dictionary<GlbPart, Pic>();
        var avg = new Dictionary<Pic, double[]>();
        double[] Mean(Pic p)
        {
            if (avg.TryGetValue(p, out var h)) return h;
            double r = 0, gg = 0, b = 0; int n = 0;
            for (int i = 0; i + 2 < p.Px.Length; i += 3 * 5) { r += p.Px[i]; gg += p.Px[i + 1]; b += p.Px[i + 2]; n++; }
            return avg[p] = new[] { r / Math.Max(1, n), gg / Math.Max(1, n), b / Math.Max(1, n) };
        }
        double[] ColourOf(GlbMat m)
        {
            if (m.Image != null) { var h = Mean(m.Image); return new[] { h[0] * Math.Min(1, m.Colour[0]), h[1] * Math.Min(1, m.Colour[1]), h[2] * Math.Min(1, m.Colour[2]) }; }
            return new[] { 255 * Srgb(m.Colour[0]), 255 * Srgb(m.Colour[1]), 255 * Srgb(m.Colour[2]) };
        }
        var cents = new List<(double X, double Y, double Z, GlbMat M)>();
        foreach (var p in parts)
        {
            var m = p.Mat >= 0 && p.Mat < g.Materials.Count ? g.Materials[p.Mat] : null;
            if (m == null || m.Decal || m.Glass) continue;
            for (int t = 0; t + 2 < p.Tri.Length; t += 3)
            {
                double x = 0, y = 0, z = 0;
                for (int k = 0; k < 3; k++) { int v = p.Tri[t + k]; x += p.Pos[3 * v]; y += p.Pos[3 * v + 1]; z += p.Pos[3 * v + 2]; }
                cents.Add((x / 3, y / 3, z / 3, m));
            }
        }
        foreach (var p in parts)
        {
            var m = p.Mat >= 0 && p.Mat < g.Materials.Count ? g.Materials[p.Mat] : null;
            if (m == null || !m.Decal || cents.Count == 0) continue;
            double cx = 0, cy = 0, cz = 0; int n = 0;
            foreach (int v in p.Tri) { cx += p.Pos[3 * v]; cy += p.Pos[3 * v + 1]; cz += p.Pos[3 * v + 2]; n++; }
            if (n == 0) continue;
            cx /= n; cy /= n; cz /= n;
            var near = cents.OrderBy(c => (c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy) + (c.Z - cz) * (c.Z - cz)).Take(12).ToList();
            var bg = new double[3];
            foreach (var c in near) { var col = ColourOf(c.M); for (int q = 0; q < 3; q++) bg[q] += col[q] / near.Count; }
            var src = m.Alpha!; var rgb = new byte[src.W * src.H * 3];
            for (int i2 = 0; i2 < src.W * src.H; i2++)
            {
                double a = src.Px[4 * i2 + 3] / 255.0 * Math.Max(0, Math.Min(1, m.A));
                for (int q = 0; q < 3; q++) rgb[3 * i2 + q] = (byte)Math.Max(0, Math.Min(255, src.Px[4 * i2 + q] * a + bg[q] * (1 - a)));
            }
            o[p] = new Pic(src.W, src.H, rgb);
        }
        return o;
    }

    /// the decal pictures (badges, stickers: pictures with see-through parts) side by side in one picture with their
    /// see-through layer, below ClearSpot rows kept clear; each decal triangle's uv in it
    static (List<double[]>, Rgba?) DecalTiles(List<double[]> uvs, List<GlbMat?> mats)
    {
        static string K(GlbMat m) => string.Join(",", m.Colour.Select(v => v.ToString("R"))) + "," + m.A.ToString("R");
        var tiles = new List<GlbMat>();
        foreach (var m in mats) if (m is { Decal: true } && !tiles.Any(t => t.Alpha == m.Alpha && K(t) == K(m))) tiles.Add(m);
        if (tiles.Count == 0) return (uvs.Select(u => new double[u.Length]).ToList(), null);
        int k = (int)Math.Ceiling(Math.Sqrt(tiles.Count)), w = Atlas / k, h = (Atlas - ClearSpot) / k;
        var atlas = new Rgba(Atlas, Atlas);
        for (int i = 0; i < Atlas * Atlas; i++) { atlas.Px[4 * i] = 128; atlas.Px[4 * i + 1] = 128; atlas.Px[4 * i + 2] = 128; }
        for (int n = 0; n < tiles.Count; n++)
        {
            var m = tiles[n]; var r = m.Alpha!.Resize(w, h); int ox = n % k * w, oy = n / k * h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = (y * w + x) * 4, t = ((y + oy) * Atlas + x + ox) * 4;
                    for (int q = 0; q < 3; q++) atlas.Px[t + q] = (byte)(float)(r.Px[s + q] * (float)Math.Max(0, Math.Min(1, m.Colour[q])));
                    atlas.Px[t + 3] = (byte)(int)(r.Px[s + 3] * Math.Max(0, Math.Min(1, m.A)));
                }
        }
        var o = new List<double[]>();
        for (int p = 0; p < uvs.Count; p++)
        {
            var uv = uvs[p]; var r = new double[uv.Length];
            if (mats[p] is { Decal: true } mm)
            {
                int n = tiles.FindIndex(t => t.Alpha == mm.Alpha && K(t) == K(mm));
                for (int t = 0; t < r.Length; t += 6)
                    for (int ax = 0; ax < 2; ax++)
                    {
                        double mn = Math.Floor(Math.Min(uv[t + ax], Math.Min(uv[t + 2 + ax], uv[t + 4 + ax])));
                        for (int q = 0; q < 3; q++)
                        {
                            double v = Math.Max(0, Math.Min(1, uv[t + 2 * q + ax] - mn));
                            r[t + 2 * q + ax] = (ax == 0 ? v * (w - 2 * Edge) + (n % k * w + Edge) : v * (h - 2 * Edge) + (n / k * h + Edge)) / Atlas;
                        }
                    }
            }
            o.Add(r);
        }
        return (o, atlas);
    }

    static (List<double[]>, Pic?) OneTexture(List<double[]> uvs, List<(GlbMat? M, Pic? Img, double[] C)> looks)
    {
        bool glass = looks.Any(l => l.M?.Glass == true);
        if (!glass && looks.Select(l => l.Img).Distinct().Count() == 1 && looks[0].Img != null) return (uvs, looks[0].Img);
        static string K(double[] c) => string.Join(",", c.Select(v => v.ToString("R")));
        var tiles = new List<(Pic I, double[] C)>();
        foreach (var (m, i, c) in looks)
            if (m?.Glass != true && i != null && !tiles.Any(t => t.I == i && K(t.C) == K(c))) tiles.Add((i, c));
        var plain = looks.Where(l => l.M?.Glass != true && l.Img == null).Select(l => l.C).GroupBy(K).Select(g => g.First()).OrderBy(c => c[0]).ThenBy(c => c[1]).ThenBy(c => c[2]).ToList();
        // glass gets its own corner (bottom right, ClearSpot square): the game's glass shader ADDS the picture's alpha to the
        // glass's own, so a solid picture makes solid glass. The build makes this corner see-through (DXT1 clear blocks).
        int perRow = (Atlas - (glass ? ClearSpot : 0)) / Swatch, strip = Math.Max((plain.Count + perRow - 1) / perRow * Swatch, glass ? ClearSpot : 0);
        int k = tiles.Count > 0 ? (int)Math.Ceiling(Math.Sqrt(tiles.Count)) : 1;
        int w = Atlas / k, h = (Atlas - strip) / k;
        var atlas = new Pic(Atlas, Atlas);
        for (int n = 0; n < tiles.Count; n++)
        {
            var (img, c) = tiles[n];
            var r = img.ResizeLanczos(w, h);
            int ox = n % k * w, oy = n / k * h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    for (int q = 0; q < 3; q++)
                        atlas.Px[((y + oy) * Atlas + x + ox) * 3 + q] = (byte)(float)(r.Px[(y * w + x) * 3 + q] * (float)Math.Max(0, Math.Min(1, c[q])));
        }
        for (int n = 0; n < plain.Count; n++)
        {
            int x0 = n % perRow * Swatch, y0 = Atlas - strip + n / perRow * Swatch;
            var c = plain[n];
            for (int y = y0; y < y0 + Swatch; y++)
                for (int x = x0; x < x0 + Swatch; x++)
                    for (int q = 0; q < 3; q++) atlas.Px[(y * Atlas + x) * 3 + q] = (byte)(int)(255 * Srgb(c[q]));
        }
        if (glass)
        {
            // the glass colour shows when the corner stays solid (See-through glass off): dark tinted windows
            var gc = looks.First(l => l.M?.Glass == true).C;
            for (int y = Atlas - ClearSpot; y < Atlas; y++)
                for (int x = Atlas - ClearSpot; x < Atlas; x++)
                    for (int q = 0; q < 3; q++) atlas.Px[(y * Atlas + x) * 3 + q] = (byte)(int)(255 * Srgb(gc[q]));
        }
        var o = new List<double[]>();
        for (int p = 0; p < uvs.Count; p++)
        {
            var uv = uvs[p]; var (gm, img, c) = looks[p];
            var r = new double[uv.Length];
            if (gm?.Glass == true)
                Array.Fill(r, ClearUv);
            else if (img == null)
            {
                int n = plain.FindIndex(q => K(q) == K(c));
                double sx = (n % perRow + 0.5) * Swatch / Atlas, sy = (Atlas - strip + (n / perRow + 0.5) * Swatch) / Atlas;
                for (int i = 0; i < r.Length; i += 2) { r[i] = sx; r[i + 1] = sy; }
            }
            else
            {
                int n = tiles.FindIndex(t => t.I == img && K(t.C) == K(c));
                for (int t = 0; t < r.Length; t += 6)
                    for (int ax = 0; ax < 2; ax++)
                    {
                        double mn = Math.Floor(Math.Min(uv[t + ax], Math.Min(uv[t + 2 + ax], uv[t + 4 + ax])));
                        for (int q = 0; q < 3; q++)
                        {
                            double v = Math.Max(0, Math.Min(1, uv[t + 2 * q + ax] - mn));
                            r[t + 2 * q + ax] = (ax == 0 ? v * (w - 2 * Edge) + (n % k * w + Edge) : v * (h - 2 * Edge) + (n / k * h + Edge)) / Atlas;
                        }
                    }
            }
            o.Add(r);
        }
        return (o, atlas);
    }

    internal static float[] FitGlb(Body glb, GameParts body, JObj fit)
    {
        int n = glb.Tri.Length;
        var t = new double[n];
        double a = fit.Num("turn") * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
        for (int i = 0; i < n; i += 3)
        {
            double x = glb.Tri[i], y = glb.Tri[i + 1], z = glb.Tri[i + 2];
            t[i] = x * ca - z * sa; t[i + 1] = y; t[i + 2] = x * sa + z * ca;
        }
        double[] lo = { 1e30, 1e30, 1e30 }, hi = { -1e30, -1e30, -1e30 }, rlo = { 1e30, 1e30, 1e30 }, rhi = { -1e30, -1e30, -1e30 };
        for (int i = 0; i < n; i++) { lo[i % 3] = Math.Min(lo[i % 3], t[i]); hi[i % 3] = Math.Max(hi[i % 3], t[i]); }
        foreach (var arr in body.Tris["paint"]) for (int i = 0; i < arr.Length; i++) { rlo[i % 3] = Math.Min(rlo[i % 3], arr[i]); rhi[i % 3] = Math.Max(rhi[i % 3], arr[i]); }
        double k = (rhi[2] - rlo[2]) / Math.Max(1e-6, hi[2] - lo[2]) * (fit.ContainsKey("size") ? fit.Num("size") : 1.0);
        double c0 = (lo[0] + hi[0]) / 2, c1 = lo[1], c2 = (lo[2] + hi[2]) / 2, d0 = (rlo[0] + rhi[0]) / 2, d1 = rlo[1] + fit.Num("up"), d2 = (rlo[2] + rhi[2]) / 2 + fit.Num("fwd");
        var o = new float[n];
        for (int i = 0; i < n; i += 3)
        {
            o[i] = (float)((t[i] - c0) * k + d0); o[i + 1] = (float)((t[i + 1] - c1) * k + d1); o[i + 2] = (float)((t[i + 2] - c2) * k + d2);
        }
        return o;
    }

    /// all triangles of the car as the side view sees them (car_parts): (x y z ...), and the tyres on their own
    /// the game car an imported body is fitted around: the replaced car (its wheels are where the game puts them), else the body car
    public static string FitCar(JObj car) => car.Truthy("body_glb") ? CarGlb.Container : car.Str("base")!;

    /// the fit of an imported body; a replaced car keeps its own wheel spots (its handling places them), so no wheel spacing
    public static JObj FitOf(JObj car)
    {
        if (!car.Truthy("body_glb")) return new JObj();
        return car.Obj("glb_fit") ?? new JObj();
    }

    internal static (List<float[]> All, List<float[]> Tyres) CarParts(Pak root, JObj car)
    {
        string bas = car.Str("base")!, wheels = car.Truthy("wheels") ? car.Str("wheels")! : bas;
        var body = ReadGameCar(root, bas);
        var whl = wheels != bas ? ReadGameCar(root, wheels) : null;
        var fit = FitOf(car);
        var refCar = car.Truthy("body_glb") ? ReadGameCar(root, FitCar(car)) : body;     // where the wheels are (an imported body: its container's)
        var all = new List<float[]>(); var tyres = new List<float[]>();
        if (car.Truthy("body_glb")) all.Add(FitGlb(ReadGlbBody(Path.Combine(Paths.Tracks, "cars", car.Str("body_glb")!)), refCar, fit));
        else foreach (var (k, v) in body.Tris) if (!k.Contains('|') && !(whl != null && k == "rim") && (k.Split('|')[0] is not ("tyre"))) all.AddRange(v);
        var src = whl ?? body;
        foreach (var c in Corners)
        {
            if (!refCar.Centre.TryGetValue(c, out var at) || !src.Centre.TryGetValue(c, out var frm)) continue;
            double[] to = { at[0] + Math.Sign(at[0]) * CarGlb.Track(fit, c.StartsWith("back")) / 2, at[1], at[2] + Math.Sign(at[2]) * fit.Num("wb") / 2 };
            foreach (var kind in new[] { "tyre", "rim" })
                if (src.Tris.TryGetValue(kind + "|" + c, out var l))
                    foreach (var arr in l)
                    {
                        var o = new float[arr.Length];
                        for (int i = 0; i < arr.Length; i++) o[i] = (float)(arr[i] - frm[i % 3] + to[i % 3]);
                        all.Add(o);
                        if (kind == "tyre") tyres.Add(o);
                    }
        }
        return (all, tyres);
    }
}

public static class CarGlb
{
    const string TrimFx = "veh_default_b_c_f";
    static readonly string[] Still = { "e_exterior", "e_interior" };
    public static readonly string[] GlbBody = { "Opel_AstraExtreme_Stock", "Bowler_Nemesis_Stock", "Nissan_Navara_Rat", "Nissan_Navara_OffRoad" };
    /// the game model every imported body is written into: the one with the most room (about 14,600 triangles), whatever the class.
    /// (GlbBody stays the class's start car for a new car's handling.)
    public const string Container = "Bowler_Nemesis_Stock";

    /// how much wider (m) the front / rear wheels sit than the body car's: fit["track"] = front, fit["track_r"] = rear
    /// (a car from before the rear had its own number: the rear follows the front)
    public static double Track(JObj fit, bool rear) => rear && fit.ContainsKey("track_r") ? fit.Num("track_r") : fit.Num("track");
    static readonly Dictionary<int, int> TypeSize = new() { [6] = 12, [0xB] = 4, [8] = 4, [9] = 8, [0xA] = 4 };
    static readonly double[] BuriedBox = { -0.01, -20.0, -0.01, 0.01, -19.99, 0.01 };
    static readonly Regex Level = new("\\x1c\\x00\\x00\\x00(....)(....)\\x01\\x00\\x00\\x00(....)DXT1\\x00\\x00\\x00\\x00\\x03\\x00\\x00\\x00RA\\x00\\x00(....)", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex PicName = new("([A-Za-z0-9_]{3,})RA\\x01", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static (byte, byte, byte, byte) LightBytes(double nx, double ny, double nz)
    {
        static byte C(double v) => (byte)Math.Max(0, Math.Min(255, Py.Round(v)));
        return (C(71 + 15 * ny), C(119 - 37 * ny), C(127 + 45 * nz), C(127 - 47 * nx));
    }

    /// triangles -> one index strip (greedy walks over shared edges, joined by degenerate triangles)
    public static List<int> Stripify(IList<(int A, int B, int C)> tris)
    {
        var edge = new Dictionary<(int, int), List<int>>();
        void E(int a, int b, int i) { if (!edge.TryGetValue((a, b), out var l)) edge[(a, b)] = l = new(); l.Add(i); }
        for (int i = 0; i < tris.Count; i++) { var (a, b, c) = tris[i]; E(a, b, i); E(b, c, i); E(c, a, i); }
        var used = new bool[tris.Count];
        var none = new List<int>();
        List<int> Of(int a, int b) => edge.TryGetValue((a, b), out var l) ? l : none;
        int FreeNb(int i)
        {
            var (a, b, c) = tris[i]; int n = 0;
            foreach (int j in Of(b, a)) if (!used[j] && j != i) n++;
            foreach (int j in Of(c, b)) if (!used[j] && j != i) n++;
            foreach (int j in Of(a, c)) if (!used[j] && j != i) n++;
            return n;
        }
        (List<int>, List<int>) Walk(int f0, int f1, int f2, HashSet<int> taken)
        {
            var s = new List<int> { f0, f1, f2 }; var got = new List<int>();
            while (true)
            {
                int x, y;
                if (s.Count % 2 != 0) { x = s[^2]; y = s[^1]; } else { x = s[^1]; y = s[^2]; }
                int nxt = -1;
                foreach (int j in Of(y, x)) if (!used[j] && !taken.Contains(j)) { nxt = j; break; }
                if (nxt < 0) return (s, got);
                taken.Add(nxt); got.Add(nxt);
                var (a, b, c) = tris[nxt];
                s.Add(a != x && a != y ? a : b != x && b != y ? b : c);
            }
        }
        var heap = new PriorityQueue<int, (int, int)>();
        for (int i = 0; i < tris.Count; i++) heap.Enqueue(i, (FreeNb(i), i));
        var o = new List<int>();
        while (heap.TryDequeue(out int start, out var pr))
        {
            if (used[start]) continue;
            int fn = FreeNb(start);
            if (pr.Item1 != fn) { heap.Enqueue(start, (fn, start)); continue; }
            var (a, b, c) = tris[start];
            List<int>? bestS = null, bestG = null;
            foreach (var (p, q, r) in new[] { (a, b, c), (b, c, a), (c, a, b) })
            {
                var (s0, g0) = Walk(p, q, r, new HashSet<int> { start });
                if (bestS == null || g0.Count > bestG!.Count) { bestS = s0; bestG = g0; }
            }
            used[start] = true;
            var touched = new HashSet<int> { start };
            foreach (int j in bestG!) { used[j] = true; touched.Add(j); }
            foreach (int j in touched)
            {
                var (x, y, z) = tris[j];
                foreach (var (e0, e1) in new[] { (y, x), (z, y), (x, z) })
                    foreach (int n in Of(e0, e1)) if (!used[n]) heap.Enqueue(n, (FreeNb(n), n));
            }
            if (o.Count > 0) { o.Add(o[^1]); o.Add(bestS![0]); if (o.Count % 2 != 0) o.Add(bestS[0]); }
            o.AddRange(bestS!);
        }
        return o;
    }

    /// one point per (position, texture spot): np.unique over the rounded rows
    internal static (double[] Pts, double[] Uvs, List<(int, int, int)> Faces) WeldUv(double[] tri, double[] uv)
    {
        var (p, u, f, _) = WeldUvAtt(tri, uv, null);
        return (p, u, f);
    }

    /// WeldUv that also carries one value per input point (att, one per triangle corner) to the welded points
    internal static (double[] Pts, double[] Uvs, List<(int, int, int)> Faces, double[] Att) WeldUvAtt(double[] tri, double[] uv, double[]? att)
    {
        int n = tri.Length / 3;
        var keys = new long[n][];
        for (int i = 0; i < n; i++)
            keys[i] = new[] { (long)Math.Round(tri[3 * i] / 1e-4), (long)Math.Round(tri[3 * i + 1] / 1e-4), (long)Math.Round(tri[3 * i + 2] / 1e-4), (long)Math.Round(uv[2 * i] / 1e-5), (long)Math.Round(uv[2 * i + 1] / 1e-5) };
        var idx = Enumerable.Range(0, n).ToArray();
        Comparison<int> cmp = (a, b) =>
        {
            for (int k = 0; k < 5; k++) { int c = keys[a][k].CompareTo(keys[b][k]); if (c != 0) return c; }
            return a.CompareTo(b);
        };
        Array.Sort(idx, cmp);
        var inv = new int[n]; var firsts = new List<int>();
        for (int i = 0; i < n; i++)
        {
            bool nw = i == 0;
            if (!nw) for (int k = 0; k < 5; k++) if (keys[idx[i]][k] != keys[idx[i - 1]][k]) { nw = true; break; }
            if (nw) firsts.Add(idx[i]);
            inv[idx[i]] = firsts.Count - 1;
        }
        var pts = new double[firsts.Count * 3]; var uvs = new double[firsts.Count * 2];
        for (int i = 0; i < firsts.Count; i++)
        {
            int f = firsts[i];
            pts[3 * i] = tri[3 * f]; pts[3 * i + 1] = tri[3 * f + 1]; pts[3 * i + 2] = tri[3 * f + 2]; uvs[2 * i] = uv[2 * f]; uvs[2 * i + 1] = uv[2 * f + 1];
        }
        var faces = new List<(int, int, int)>();
        for (int i = 0; i + 2 < n; i += 3) { int a = inv[i], b = inv[i + 1], c = inv[i + 2]; if (a != b && b != c && a != c) faces.Add((a, b, c)); }
        var attOut = new double[firsts.Count];
        for (int i = 0; i < firsts.Count; i++) attOut[i] = att != null ? att[firsts[i]] : 1.0;
        return (pts, uvs, faces, attOut);
    }

    internal sealed record MeshRow(MeshInfo M, string El, int S) { public VStreamInfo Vs(ModelInfo m) => m.VStreams[S]; }

    static string TKey(IEnumerable<int> t) => string.Join(",", t);

    internal static (List<MeshRow> Meshes, List<(MeshInfo M, int S)> Rooms) Rooms(ModelInfo M)
    {
        var fx = M.FxFiles.Select(f => f.ToLowerInvariant()).ToList();
        var sidx = new Dictionary<string, int>();
        for (int i = 0; i < M.VStreams.Count; i++) sidx[TKey(M.VStreams[i].Def.Select(d => d.Type))] = i;
        var meshes = new List<MeshRow>();
        foreach (var m in M.MeshInfos)
            meshes.Add(new MeshRow(m, m.Object < M.AllElements.Count ? M.AllElements[m.Object].Name : "", sidx.GetValueOrDefault(TKey(M.VertDefs[m.Definition].Where(d => d.Prefix == 0).Select(d => d.Type)), -1)));
        var rooms = new List<(MeshInfo, int)>();
        foreach (var r in meshes)
        {
            if (r.S < 0 || !Still.Contains(r.El.ToLowerInvariant()) || !fx[r.M.Material].StartsWith(TrimFx)) continue;
            var defs = M.VStreams[r.S].Def;
            if (defs[0].Type == 9 && defs.Any(d => d.Type == 11 && d.Channel == 3) && defs.Any(d => d.Type == 9 && d.Channel == 5)) rooms.Add((r.M, r.S));
        }
        rooms = rooms.OrderBy(r => Array.IndexOf(Still, M.AllElements[r.Item1.Object].Name.ToLowerInvariant())).ThenBy(r => -r.Item1.Data1[0].VCount).ToList();
        return (meshes, rooms);
    }

    /// look only (bmbcli carmeshes)
    public static (List<(MeshInfo M, string El, int S)> Meshes, List<(MeshInfo M, int S)> Rooms) RoomsOf(ModelInfo M)
    {
        var (m, r) = Rooms(M);
        return (m.Select(q => (q.M, q.El, q.S)).ToList(), r);
    }

    internal static int IndexRoom(MeshInfo m)
    {
        var d = m.Data1;
        int n = d[0].FaceCount;
        for (int i = 0; i + 1 < d.Count; i++)
        {
            if (d[i + 1].FaceOff != d[i].FaceOff + d[i].FaceCount) break;
            n += d[i + 1].FaceCount;
        }
        return n;
    }

    static int Replace(byte[] d, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int end)
    {
        int n = 0, i = 0;
        while (i + a.Length <= end)
        {
            int k = d.AsSpan(i, end - i).IndexOf(a);
            if (k < 0) break;
            b.CopyTo(d.AsSpan(i + k)); n++; i += k + a.Length;
        }
        return n;
    }

    static byte[] Box6(double[] v) { var o = new byte[24]; for (int i = 0; i < 6; i++) B.Put(o, 4 * i, (float)v[i]); return o; }

    public static byte[] GlbModel(Pak root, JObj car, string glbPath, Action<string> log)
    {
        var data = (byte[])CarModel.ModelBytes(root, CarModel.ModelPath(Container)).Clone();
        var M = CpModel.Read(data);
        GlassElement = null; GlassFrom = null; AlphaElement = null;
        var (meshes, rooms) = Rooms(M);
        int vroom = rooms.Sum(r => r.M.Data1[0].VCount), iroom = rooms.Sum(r => IndexRoom(r.M));
        var raw = CarModel.ReadGlbBody(glbPath);
        var fitted = CarModel.FitGlb(raw, CarModel.ReadGameCar(root, CarModel.FitCar(car)), CarModel.FitOf(car));
        // three groups of triangles: glass (see-through or named glass in the .glb), the body's paint + decals, and the rest (trim)
        int nt = fitted.Length / 9;
        bool IsGlass(int t) => t < raw.Glass.Length && raw.Glass[t];
        (double[] Tri, double[] Uv, double[] Shine) Pick(Func<int, bool> sel, bool paint = false)
        {
            var ts = Enumerable.Range(0, nt).Where(sel).ToList();
            var tr = new double[ts.Count * 9]; var u = new double[ts.Count * 6]; var sh = new double[ts.Count * 3];
            for (int i = 0; i < ts.Count; i++)
            {
                int t = ts[i];
                for (int k = 0; k < 3; k++) sh[3 * i + k] = t < raw.Matte.Length && raw.Matte[t] == 1 ? 0.0 : 1.0;
                for (int k = 0; k < 9; k++) tr[9 * i + k] = fitted[9 * t + k];
                // paint triangles look at the clear spot of the livery picture (the paint shows), decals at their own tile
                for (int k = 0; k < 6; k++) u[6 * i + k] = !paint ? raw.Uv[6 * t + k] : raw.Kind[t] == 2 ? raw.DecalUv[6 * t + k] : CarModel.ClearUv;
            }
            return (tr, u, sh);
        }
        int nGlass = Enumerable.Range(0, nt).Count(IsGlass);
        // the glass goes into the game's own headlight-cover glass meshes (user test 2026-11-21: a trim part switched to glass is drawn
        // black whatever its values, these draw as real see-through glass) when they have room for it; else the old way
        var coverMeshes = new List<MeshRow>();
        if (nGlass > 0 && CarBuild.GlassCover && CarModel.ClearGlass(car))
        {
            var cm = meshes.Where(r => r.S >= 0 && r.El.StartsWith("e_headlightcover", StringComparison.OrdinalIgnoreCase) && M.FxFiles[r.M.Material].StartsWith("veh_glass", StringComparison.OrdinalIgnoreCase)).ToList();
            var (gt0, gu0, _) = Pick(IsGlass);
            int need = WeldUv(gt0, new double[gu0.Length]).Pts.Length / 3, have = cm.Sum(r => r.M.Data1[0].VCount);
            if (cm.Count > 0 && need <= have * 0.9) coverMeshes = cm;
            else log($"   (glass: {need} points do not fit the game's {have}-point light-cover glass - the old glass part is used)");
        }
        bool useCovers = coverMeshes.Count > 0;
        var glassRoom = nGlass > 0 && !useCovers ? GlassRoom(data, M, meshes, rooms, nGlass, log) : null;
        // TEST: with GlassTestDrop the glass triangles on the left side (x < 0) are left out, the right side keeps its glass
        bool DropSel(int t) => CarBuild.GlassTestDrop && glassRoom != null && IsGlass(t) && (fitted[9 * t] + fitted[9 * t + 3] + fitted[9 * t + 6]) / 3 < 0;
        bool AnyGlass(int t) => (glassRoom != null || useCovers) && IsGlass(t);
        bool CoverSel(int t) => useCovers && IsGlass(t);
        bool GlassSel(int t) => AnyGlass(t) && !DropSel(t) && !CoverSel(t);
        // TEST 2026-11-21: no window glass at all (glass triangles are simply left out) - is the black cabin the glass?
        var bodyRooms = glassRoom == null ? rooms : rooms.Where(r => r.M != glassRoom.Value.M).ToList();
        // the body's paint: those triangles go into parts that use the game's paint material, so the player's paint colour shows;
        // decals (pictures with see-through parts) sit on the same parts, drawn over the paint
        bool PaintSel(int t) => !GlassSel(t) && t < raw.Kind.Length && (CarBuild.Diag ? raw.Kind[t] == 1 : raw.Kind[t] != 0);
        bool paintPlaced = false; double[] ppts = Array.Empty<double>();
        int nPaint = CarModel.PaintBody(car) ? Enumerable.Range(0, nt).Count(PaintSel) : 0;
        if (nPaint > 0)
        {
            var pm = meshes.FirstOrDefault(r => r.S >= 0 && r.El.Equals("e_exterior", StringComparison.OrdinalIgnoreCase) && M.FxFiles[r.M.Material].StartsWith("veh_paint_b_c", StringComparison.OrdinalIgnoreCase));
            var lv = Skins.Levels(data, PaintPic);
            if (pm == null || lv.Count == 0) log("   (paint: this model has no paint part or livery picture - the body keeps its colours baked in)");
            else
            {
                var prooms = bodyRooms.Where(r => r.S == pm.S).ToList();
                var (tri, uv, _) = Pick(PaintSel, true);
                var (pp, pu, pf) = WeldUv(tri, uv);
                var trial = (byte[])data.Clone(); var used = new List<(MeshInfo M, int S)>();
                int n = Place(trial, M, meshes, prooms, pp, pu, pf, clear: true, usedRooms: used);
                if (n >= pf.Count)
                {
                    data = trial; paintPlaced = true; ppts = pp;
                    foreach (var r in used) B.Put(data, r.M.MaterialAt, (uint)pm.M.Material);
                    bodyRooms = bodyRooms.Where(r => !used.Any(u => u.M == r.M)).ToList();
                    int decals = Enumerable.Range(0, nt).Count(t => PaintSel(t) && raw.Kind[t] == 2);
                    log($"   paint: {pf.Count} triangles ({decals} of the .glb's are decals) in {used.Count} parts with the game's paint - they take the player's paint colour");
                    PutPaintPicture(data, raw, lv, log);
                }
                else log($"   paint: the body has room for only {n} of {pf.Count} paint triangles - they stay trim with their colours baked in (make the paint panels lighter, or turn 'Paintable body' off)");
            }
        }
        // decals without the paint feature: their own small part with the game's alpha material (see-through picture), so a badge
        // shows its shape instead of a solid box
        bool DecalSel(int t) => !GlassSel(t) && t < raw.Kind.Length && raw.Kind[t] == 2;
        bool decalPlaced = false; double[] dpts = Array.Empty<double>();
        int nDecal = CarModel.DecalAlpha(car) && (!paintPlaced || CarBuild.Diag) ? Enumerable.Range(0, nt).Count(DecalSel) : 0;
        if (nDecal > 0 && raw.DecalImage != null)
        {
            var am = meshes.FirstOrDefault(r => r.S >= 0 && M.FxFiles[r.M.Material].StartsWith("veh_alpha_c", StringComparison.OrdinalIgnoreCase));
            var lv = Skins.Levels(data, AlphaPic);
            if (am == null || lv.Count == 0) log("   (decals: this model has no alpha part or picture - they stay solid in the body picture)");
            else
            {
                var (tri, uv, _) = Pick(DecalSel, true);
                var (dp, du, df) = WeldUv(tri, uv);
                var cands = bodyRooms.Where(r => r.S == am.S).OrderBy(r => r.M.Data1[0].VCount).ToList();
                foreach (var one in cands.Select(c => new List<(MeshInfo M, int S)> { c }).Append(cands))
                {
                    var trial = (byte[])data.Clone(); var used = new List<(MeshInfo M, int S)>();
                    int n = Place(trial, M, meshes, one, dp, du, df, clear: !paintPlaced, usedRooms: used);
                    if (n < df.Count) continue;
                    data = trial; decalPlaced = true; dpts = dp; AlphaElement = am.M.Object < M.AllElements.Count ? M.AllElements[am.M.Object].Name : null;
                    foreach (var r in used) { B.Put(data, r.M.MaterialAt, (uint)am.M.Material); B.Put(data, r.M.MaterialAt + 16, (ushort)am.M.Object); CopyMeshHeader(data, r.M, am.M); }
                    bodyRooms = bodyRooms.Where(r => !used.Any(u => u.M == r.M)).ToList();
                    log($"   decals: {df.Count} triangles in {used.Count} part(s) with the game's alpha material (see-through picture)");
                    PutAlphaPicture(data, raw, lv, log);
                    break;
                }
                if (!decalPlaced) log("   (decals: no small part had room for them - they stay solid in the body picture)");
            }
        }
        bool cleared = paintPlaced || decalPlaced;
        bool TrimSel(int t) => !AnyGlass(t) && !(paintPlaced && PaintSel(t)) && !(decalPlaced && DecalSel(t));
        var (bodyTri, bodyUv, bodyShine) = Pick(TrimSel);
        var (pts, uvs, faces, shine) = WeldUvAtt(bodyTri, bodyUv, bodyShine);
        int placed = faces.Count == 0 ? 0 : Place(data, M, meshes, bodyRooms, pts, uvs, faces, clear: !cleared, shine: shine);
        log($"   imported body: {faces.Count} triangles, {placed} placed in {bodyRooms.Count} trim parts (room {vroom} points, {iroom} indices)");
        if (glassRoom != null && !decalPlaced)
        {
            var lvA = Skins.Levels(data, AlphaPic);
            if (lvA.Count > 0) { PutAlphaPicture(data, raw, lvA, log); log("   (the windows read this picture too: its bottom right corner is see-through)"); }
        }
        if (paintPlaced) pts = pts.Concat(ppts).ToArray();
        if (decalPlaced) pts = pts.Concat(dpts).ToArray();
        CoverUsed.Clear();
        if (coverMeshes.Count > 0 && Enumerable.Range(0, nt).Any(CoverSel))
        {
            var (ctri, cuv, _) = Pick(CoverSel);
            var (cp, cu, cf) = WeldUv(ctri, new double[cuv.Length]);     // the covers look at one texel (uv 0,0), like the game's own
            var used = new List<(MeshInfo M, int S)>();
            int cn = Place(data, M, meshes, coverMeshes.OrderByDescending(r => r.M.Data1[0].VCount).Select(r => (r.M, r.S)).ToList(), cp, cu, cf, clear: false, usedRooms: used);
            foreach (var u in used) CoverUsed.Add(u.M);
            log($"   glass: {cf.Count} triangles in the game's own headlight-cover glass meshes (real see-through pass): {cn} placed in {used.Count} meshes{(cn < cf.Count ? $" - {cf.Count - cn} did not fit (holes in the glass)" : "")}");
            pts = pts.Concat(cp).ToArray();
        }
        if (glassRoom != null)
        {
            var (glassTri, glassUv, _) = Pick(GlassSel);
            var (gp, gu, gf) = WeldUv(glassTri, glassUv);
            int gplaced = Place(data, M, meshes, new() { glassRoom.Value }, gp, gu, gf, clear: false, copyFrom: GlassFrom);
            log($"   glass points take the real windscreen's damage coordinates ({GlassFrom?.Count ?? 0} windscreen points) + our picture's glass corner");
            log($"   glass: {gf.Count} triangles, {gplaced} placed in a part with the game's windscreen glass (see-through){(gplaced < gf.Count ? $" - {gf.Count - gplaced} did not fit (holes in the glass)" : "")}");
            pts = pts.Concat(gp).ToArray();
        }
        if (placed < faces.Count)
            log($"   WARNING: the body model ({Container}) has room for only part of this body - {faces.Count - placed} of its {faces.Count} triangles are missing (holes). Make the .glb smaller (Blender: Decimate) - the Custom car tab says to about how many triangles.");
        // wheel spots, only for the boxes
        var extra = new List<double[]>();
        var spots = new HashSet<(float, float, float)>();
        foreach (var e in M.AllElements)
            if (CarModel.Corner(e.Name) != null && (CarModel.Kind(e.Name) is "tyre" or "rim" || e.Name.ToLowerInvariant().StartsWith("e_wheel_"))) spots.Add((e.M[9], e.M[11], e.M[10]));
        foreach (var (x, fwd, up) in spots)
            foreach (var d in new[] { new[] { -.2, -.4, -.4 }, new[] { .2, .4, .4 } }) extra.Add(new[] { x + d[0], up + d[1], fwd + d[2] });
        HideLights(data, M, meshes, log);
        GrowBoxes(data, M, pts, extra, log);
        PutTexture(data, raw.Image, log, clear: glassRoom != null && CarModel.ClearGlass(car));
        if (CarBuild.Diag) DiagPictures(data, log);
        return data;
    }

    /// the wheel car's tyres + rim rings copied into this car's model (the wheels file only holds the rim's face). Every corner keeps
    /// its room: a tyre with more points than the room uses one of its lower detail levels. Returns the corners done.
    public static Action<string>? Why;

    sealed record WheelPart(VStreamInfo Vs, int Base, int[] Strip);

    /// where each part of a vertex lies in a stream: type, channel, sub -> offset
    static List<(int T, int Ch, int Sub, int Off, int Size)> Layout2(VStreamInfo v)
    {
        var o = new List<(int, int, int, int, int)>(); int at = 0;
        foreach (var (t, ch, sub) in v.Def) { int n = TypeSize.GetValueOrDefault(t, 4); o.Add((t, ch, sub, at, n)); at += n; }
        return o;
    }

    /// one vertex written in the target's format: what both have is copied, a colour the source lacks is white, the rest stays 0
    static void CopyVertex(byte[] sd, VStreamInfo sv, int si, byte[] d, VStreamInfo dv, int di)
    {
        var sl = Layout2(sv); var dl = Layout2(dv);
        int sa = sv.Start + sv.Bytes * si, da = dv.Start + dv.Bytes * di;
        d.AsSpan(da, dv.Bytes).Clear();
        var used = new HashSet<int>();
        foreach (var (t, ch, sub, off, size) in dl)
        {
            int k = sl.FindIndex(q => q.T == t && q.Ch == ch && q.Sub == sub);
            if (k < 0 || used.Contains(k)) k = sl.FindIndex(q => q.T == t && q.Ch == ch);
            if (k >= 0 && !used.Contains(k)) { used.Add(k); Array.Copy(sd, sa + sl[k].Off, d, da + off, Math.Min(size, sl[k].Size)); }
            else if (t == 8) d.AsSpan(da + off, size).Fill(255);
        }
    }

    static List<(int A, int B, int C)> StripTris(int[] st)
    {
        var o = new List<(int, int, int)>();
        for (int i = 0; i + 2 < st.Length; i++)
        {
            int a = st[i], b = st[i + 1], c = st[i + 2];
            if (a != b && b != c && a != c) o.Add(i % 2 == 0 ? (a, b, c) : (c, b, a));
        }
        return o;
    }

    /// the wheel car's tyres + rim rings copied into this car's model (the wheels file only holds the rim's face). Each corner keeps its
    /// room: if the parts don't fit, a lower detail level is used, and if even that is too big its points are merged. Returns the corners done.
    public static int SwapWheels(byte[] data, byte[] orig, Pak root, string wheelCar, Action<string> log)
    {
        var M = CpModel.Read(orig);
        var sd = CarModel.ModelBytes(root, CarModel.ModelPath(wheelCar));
        var S = CpModel.Read(sd);
        var (dm, _) = Rooms(M); var (sm, _) = Rooms(S);
        var lods = LodRecords(orig, M);
        var index = new Dictionary<MeshInfo, int>();
        for (int i = 0; i < M.MeshInfos.Count; i++) index[M.MeshInfos[i]] = i;
        static bool Is(string el, string kind, string corner) { el = el.ToLowerInvariant().Replace("_", ""); return el.Contains(kind) && el.Contains(corner); }
        static int Top(MeshRow r) => r.M.Data1.Max(d => d.VCount);
        var corners = new[] { "frontleft", "frontright", "backleft", "backright" };
        int done = 0, less = 0;
        foreach (var corner in corners)
        {
            bool good = true;
            foreach (var kind in new[] { "tyre", "alloyrim" })
            {
                var dst = dm.Where(r => Is(r.El, kind, corner) && r.S >= 0 && r.M.FaceType == 1).OrderByDescending(Top).ToList();
                var src = sm.Where(r => Is(r.El, kind, corner) && r.S >= 0 && r.M.FaceType == 1 && S.VStreams[r.S].Def[0].Type == 9 && r.M.Data1.Any(d => d.FaceCount >= 3)).OrderByDescending(Top).ToList();
                if (dst.Count == 0 || src.Count == 0 || dst.Any(r => M.VStreams[r.S].Def[0].Type != 9)) { good = false; Why?.Invoke($"{kind} parts {dst.Count} vs {src.Count}"); continue; }
                // each target part: which wheel parts go into it (a third part joins the last one), its room
                var into = dst.Select(_ => new List<MeshRow>()).ToList();
                for (int i = 0; i < src.Count; i++) into[Math.Min(i, dst.Count - 1)].Add(src[i]);
                var rooms = dst.Select(r => IndexRoom(r.M)).ToArray();
                var lo = dst.Select(r => r.M.Data1.Min(d => d.VOff)).ToArray(); var hi = dst.Select(r => r.M.Data1.Max(d => d.VOff + d.VCount)).ToArray();
                bool shared = dst.Select(r => (r.S, r.M.VOffset[0])).Distinct().Count() == 1;
                int pool = shared ? hi.Max() - lo.Min() : 0, pool0 = pool, slices = 96;
                int levels = src.Max(r => r.M.Data1.Count(d => d.FaceCount >= 3));
                List<WheelPart> Level(MeshRow r, int l)
                {
                    var ds = r.M.Data1.Where(d => d.FaceCount >= 3).ToList(); var d = ds[Math.Min(l, ds.Count - 1)];
                    var fs = S.FStreams[r.M.FaceStream]; var strip = new int[d.FaceCount];
                    for (int i = 0; i < strip.Length; i++) strip[i] = B.U16(sd, fs.Start + 2 * (d.FaceOff + i));
                    return new() { new WheelPart(S.VStreams[r.S], r.M.VOffset[0] / r.Vs(S).Bytes, strip) };
                }
                int Need(int p, int l) => into[p].Sum(r => Level(r, l)[0].Strip.Distinct().Count());
                int Idx(int p, int l) => into[p].Sum(r => Level(r, l)[0].Strip.Length) + 3 * (into[p].Count - 1);
                bool Fits(int l)
                {
                    for (int p = 0; p < dst.Count; p++) if (Idx(p, l) > rooms[p] || (!shared && Need(p, l) > hi[p] - lo[p])) return false;
                    return !shared || Enumerable.Range(0, dst.Count).Sum(p => Need(p, l)) <= pool;
                }
                const int level = 0;                                 // always the best detail: too many points = fewer slices round the wheel
                bool reduce = !Fits(0);
                if (reduce) less++;
                int sum = Enumerable.Range(0, dst.Count).Sum(p => Need(p, level)), off = lo.Min();
                for (int p = 0; p < dst.Count; p++)
                {
                    var r = dst[p]; var dv = M.VStreams[r.S]; int vbase = r.M.VOffset[0] / dv.Bytes;
                    int budget = shared ? (reduce ? Math.Max(1, pool0 * Need(p, level) / Math.Max(sum, 1)) : pool) : hi[p] - lo[p];
                    if (!shared) off = lo[p];
                    if (into[p].Count == 0)
                    {
                        // the wheel car has fewer parts: this one draws nothing
                        var d9 = r.M.Data1[0]; int f9 = M.FStreams[r.M.FaceStream].Start + 2 * d9.FaceOff;
                        for (int i = 0; i < rooms[p]; i++) B.Put(data, f9 + 2 * i, (ushort)lo[p]);
                        continue;
                    }
                    // the part's points: (source stream, index) -> ours
                    var pts = new List<(VStreamInfo Vs, int I)>(); var ids = new Dictionary<(VStreamInfo, int), int>();
                    var tris = new List<(int A, int B, int C)>(); int[]? plain = null;
                    foreach (var q in into[p])
                    {
                        var w = Level(q, level)[0];
                        int Id(int v) { var k = (w.Vs, w.Base + v); if (!ids.TryGetValue(k, out int n)) { n = pts.Count; ids[k] = n; pts.Add(k); } return n; }
                        var st = w.Strip.Select(Id).ToArray();
                        if (into[p].Count == 1) plain = st;
                        tris.AddRange(StripTris(st));
                    }
                    if (pts.Count > budget || plain == null || plain.Length > rooms[p])
                    {
                        if (plain == null && pts.Count <= budget) plain = Stripify(tris).ToArray();
                        if (pts.Count > budget || plain.Length > rooms[p])
                        {
                            // a wheel is one slice repeated round the axle (model x): fewer slices, the same profile
                            var pos = pts.Select(q => { int at = q.Vs.Start + q.Vs.Bytes * q.I; return (X: (double)BitConverter.ToHalf(sd, at), Y: (double)BitConverter.ToHalf(sd, at + 2), Z: (double)BitConverter.ToHalf(sd, at + 4)); }).ToList();
                            double cy = (pos.Min(q => q.Y) + pos.Max(q => q.Y)) / 2, cz = (pos.Min(q => q.Z) + pos.Max(q => q.Z)) / 2;
                            var srcPts = pts; var srcTris = tris; plain = null; int bestK = 0;
                            foreach (double row in new[] { 0.004, 0.006, 0.008, 0.012, 0.03, 0.06 })
                            for (int k = slices; k >= 6 && k > bestK && bestK < 12; k = k * 9 / 10)
                            {
                                double da = 2 * Math.PI / k;
                                var cells = new Dictionary<(int, long, long), (int I, double Off)>(); var key = new (int, long, long)[srcPts.Count];
                                for (int i = 0; i < srcPts.Count; i++)
                                {
                                    double y = pos[i].Y - cy, z = pos[i].Z - cz, ang = Math.Atan2(z, y) + Math.PI, r0 = Math.Sqrt(y * y + z * z);
                                    int c = (int)Math.Round(ang / da) % k; double dist = Math.Abs(ang - Math.Round(ang / da) * da);
                                    key[i] = (c, (long)Math.Round(r0 / row), (long)Math.Round(pos[i].X / row));
                                    if (!cells.TryGetValue(key[i], out var best) || dist < best.Off) cells[key[i]] = (i, dist);
                                }
                                var keep = new List<(VStreamInfo Vs, int I)>(); var ids2 = new Dictionary<(int, long, long), int>();
                                foreach (var (kk, v) in cells) { ids2[kk] = keep.Count; keep.Add(srcPts[v.I]); }
                                var t2 = srcTris.Select(t => (ids2[key[t.A]], ids2[key[t.B]], ids2[key[t.C]])).Where(t => t.Item1 != t.Item2 && t.Item2 != t.Item3 && t.Item1 != t.Item3).Distinct().ToList();
                                if (t2.Count == 0 || keep.Count > budget) continue;
                                var st2 = Stripify(t2).ToArray();
                                if (st2.Length > rooms[p]) continue;
                                pts = keep; tris = t2; plain = st2; bestK = k;
                                break;
                            }
                            if (bestK > 0) slices = bestK;                // the tread as many slices as the wall: nothing sticks out
                        }
                    }
                    if (plain == null || pts.Count > budget || plain.Length > rooms[p]) { good = false; Why?.Invoke($"{kind} no fit v{pts.Count}/{budget} i{plain?.Length}/{rooms[p]}"); if (shared) off += 0; continue; }
                    for (int j = 0; j < pts.Count; j++) CopyVertex(sd, pts[j].Vs, pts[j].I, data, dv, vbase + off + j);
                    var d0 = r.M.Data1[0];
                    int fa = M.FStreams[r.M.FaceStream].Start + 2 * d0.FaceOff, room = rooms[p];
                    for (int i = 0; i < room; i++) B.Put(data, fa + 2 * i, (ushort)(off + plain[Math.Min(i, plain.Length - 1)]));
                    int ix = index[r.M];
                    for (int lod = 0; lod < r.M.Data1.Count; lod++)
                        if (lods.TryGetValue((ix, lod), out int at)) { B.Put(data, at, (uint)d0.FaceOff); B.Put(data, at + 4, (uint)room); B.Put(data, at + 8, (uint)off); B.Put(data, at + 12, (uint)pts.Count); }
                    if (shared) { off += pts.Count; pool -= pts.Count; }
                }
            }
            if (good) done++;
        }
        int head = M.VStreams.Min(v => v.Start);
        foreach (var e in M.AllElements)
            if (corners.Any(c => Is(e.Name, "tyre", c) || Is(e.Name, "alloyrim", c)))
            {
                var b = e.Box;
                Replace(data, Box6(b), Box6(new[] { b[0] - .12, b[1] - .12, b[2] - .12, b[3] + .12, b[4] + .12, b[5] + .12 }), head);
            }
        log($"   tyres + rims of {wheelCar}: {done} of 4 corners{(less > 0 ? " (less detailed: the body car has less room for them)" : "")}");
        if (done < 4) log($"   WARNING: {wheelCar}'s tyres don't fit this body car on {4 - done} corner(s): those keep the body car's tyres");
        return done;
    }

    static void HideLights(byte[] d, ModelInfo M, List<MeshRow> meshes, Action<string> log)
    {
        int moved = 0;
        foreach (var (m, el, s) in meshes)
        {
            if (s < 0 || CarModel.Kind(el) != "light" || CoverUsed.Contains(m)) continue;
            var vs = M.VStreams[s];
            int stride = vs.Bytes, kind = vs.Def[0].Type, vbase = m.VOffset[0] / stride;
            foreach (var d1 in m.Data1)
                for (int j = 0; j < d1.VCount; j++)
                {
                    int k = vbase + d1.VOff + j;
                    if (k < 0 || k >= vs.Count) continue;
                    int at = vs.Start + stride * k;
                    if (kind == 9) { B.Put(d, at, (Half)0.0); B.Put(d, at + 2, (Half)(-20.0)); B.Put(d, at + 4, (Half)0.0); B.Put(d, at + 6, (Half)0.0); }
                    else if (kind == 6) { B.Put(d, at, 0f); B.Put(d, at + 4, -20f); B.Put(d, at + 8, 0f); }
                    moved++;
                }
        }
        // each light's box is in the file twice: in the element list (before the vertex data) and in the part list at the end
        // (after it). The game puts the glowing brake / rear light trails at the centre of that second one, so both go under
        // the ground, or the old car's light trails float behind the new body.
        int head = M.VStreams.Min(v => v.Start), tail = M.VStreams.Max(v => v.Start + v.Count * v.Bytes), done = 0;
        var nw = Box6(BuriedBox);
        foreach (var e in M.AllElements)
        {
            if (CarModel.Kind(e.Name) != "light" || CoverUsed.Any(c => c.Object < M.AllElements.Count && M.AllElements[c.Object] == e)) continue;
            var old = Box6(e.Box);
            if (old.AsSpan().SequenceEqual(nw)) continue;
            done += Replace(d, old, nw, head);
            // the second copy's numbers are rounded a little differently: matched within 1 mm
            for (int i = tail; i + 24 <= d.Length; i++)
            {
                int k = 0;
                while (k < 6 && Math.Abs(BitConverter.ToSingle(d, i + 4 * k) - e.Box[k]) < 0.001) k++;
                if (k == 6) { nw.CopyTo(d, i); done++; i += 20; }
            }
        }
        log($"   the body car's lights hidden ({moved} points + {done} boxes put under the ground)");
    }

    static void GrowBoxes(byte[] d, ModelInfo M, double[] pts, List<double[]> extra, Action<string> log)
    {
        double[] lo = { 1e30, 1e30, 1e30 }, hi = { -1e30, -1e30, -1e30 };
        for (int i = 0; i < pts.Length; i++) { lo[i % 3] = Math.Min(lo[i % 3], pts[i]); hi[i % 3] = Math.Max(hi[i % 3], pts[i]); }
        foreach (var p in extra) for (int i = 0; i < 3; i++) { lo[i] = Math.Min(lo[i], p[i]); hi[i] = Math.Max(hi[i], p[i]); }
        for (int i = 0; i < 3; i++) { lo[i] -= 0.02; hi[i] += 0.02; }
        // the glass's element too (its box held only the old windscreen: the new glass would be cut off / not drawn)
        var boxes = M.AllElements.Where(e => Still.Contains(e.Name.ToLowerInvariant()) || (GlassElement != null && e.Name == GlassElement) || (AlphaElement != null && e.Name == AlphaElement) || CoverUsed.Any(c => c.Object < M.AllElements.Count && M.AllElements[c.Object] == e)).Select(e => e.Box).Concat(M.ModelBoxes);
        int done = 0;
        foreach (var old in boxes)
        {
            var nw = new[] { Math.Min(old[0], lo[0]), Math.Min(old[1], lo[1]), Math.Min(old[2], lo[2]), Math.Max(old[3], hi[0]), Math.Max(old[4], hi[1]), Math.Max(old[5], hi[2]) };
            if (old.SequenceEqual(nw) || old[0] > old[3]) continue;
            done += Replace(d, Box6(old), Box6(nw), d.Length);
        }
        log($"   boxes grown to hold the body: {done}");
    }

    /// the glass corner (bottom right of the picture) made see-through in every mip level: DXT1 blocks with both colours
    /// equal use the 3-colour mode, where index 3 = clear (alpha 0). Levels where the corner is under one block stay solid.
    static void ClearCorner(byte[] enc, int w, int h, int mips)
    {
        int at = 0;
        foreach (var (lw, lh) in mips > 0 ? CpModel.MipSizes(w, h, mips) : new() { (w, h) })
        {
            int bw = Math.Max(lw / 4, 1), bh = Math.Max(lh / 4, 1);
            int sx = lw * CarModel.ClearSpot / CarModel.Atlas / 4, sy = lh * CarModel.ClearSpot / CarModel.Atlas / 4;   // corner size in blocks
            for (int by = bh - sy; by < bh && sx > 0; by++)
                for (int bx = bw - sx; bx < bw; bx++)
                {
                    int b = at + (by * bw + bx) * 8;
                    for (int i = 0; i < 4; i++) enc[b + i] = 0;
                    for (int i = 4; i < 8; i++) enc[b + i] = 0xFF;
                }
            at += bw * bh * 8;
        }
    }

    static void PutTexture(byte[] data, Pic? img, Action<string> log, bool clear = false)
    {
        if (img == null) { log("   (the .glb has no texture: the body shows the car's trim picture)"); return; }
        string s = B.Latin1.GetString(data);
        var names = PicName.Matches(s).Select(m => (m.Index, Name: m.Groups[1].Value)).ToList();
        foreach (var suffix in new[] { "_1", "_2" })
        {
            var pick = names.Where(n => n.Name.ToLowerInvariant().EndsWith(suffix)).ToList();
            if (pick.Count == 0) { log($"   (no picture <car>{suffix} found)"); continue; }
            var (a, name) = pick[0];
            int end = names.Where(n => n.Index > a).Select(n => n.Index).DefaultIfEmpty(data.Length).First();
            var sizes = new List<int>();
            for (var m = Level.Match(s, a, end - a); m.Success; m = m.NextMatch())
            {
                int w = (int)B.U32(data, m.Groups[1].Index), h = (int)B.U32(data, m.Groups[2].Index), mips = (int)B.U32(data, m.Groups[3].Index), size = (int)B.U32(data, m.Groups[4].Index);
                var px = img.ResizeLanczos(w, h).Px;
                bool chain = size == CpModel.TextureBytes(w, h, mips);
                var enc = chain ? CpModel.EncodeMips(px, w, h, mips) : Dxt.Dxt1Encode(px, w, h);
                if (enc.Length != size) { log($"   (picture {name} {w} x {h}: unexpected size, left as it is)"); continue; }
                if (clear) ClearCorner(enc, w, h, chain ? mips : 0);
                enc.CopyTo(data, m.Index + m.Length + 20);
                sizes.Add(w);
            }
            log($"   the .glb's texture -> {name} ({string.Join(", ", sizes)}){(clear ? ", glass corner see-through" : "")}");
        }
    }

    static Dictionary<(int, int), int> LodRecords(byte[] data, ModelInfo M)
    {
        var o = new Dictionary<(int, int), int>();
        int start = 0;
        var rec = new byte[20]; rec[0] = (byte)'R'; rec[1] = (byte)'A';
        for (int i = 0; i < M.MeshInfos.Count; i++)
            for (int lod = 0; lod < M.MeshInfos[i].Data1.Count; lod++)
            {
                var d1 = M.MeshInfos[i].Data1[lod];
                B.Put(rec, 4, d1.FaceOff); B.Put(rec, 8, d1.FaceCount); B.Put(rec, 12, d1.VOff); B.Put(rec, 16, d1.VCount);
                int at = data.AsSpan(start).IndexOf(rec);
                if (at >= 0) { o[(i, lod)] = start + at + 4; start = start + at + 4; }
            }
        return o;
    }

    /// a trim part for the imported glass: the game's windscreen material has no picture in it (veh_Glass without "_a"), so
    /// it is see-through by the view angle alone (the game's glass shader: alpha = max..min by N.V, + the picture's alpha
    /// only when it has one; our body picture has none, so glass in a part WITH a picture came out solid black).
    /// One trim part with the windscreen's vertex layout gets the windscreen's material: the smallest that holds the glass.
    /// the Bowler's livery picture (RGBA, 1024): the game's paint shader mixes the player's paint with its colour by its see-through layer
    /// DIAGNOSTIC: each picture of the car in its own solid colour
    static void DiagPictures(byte[] data, Action<string> log)
    {
        var colours = new (string Pic, byte R, byte G, byte B)[] { ("bowler_alpha", 0, 0, 255) };
        foreach (var (pic, r, g, b) in colours)
        {
            var lv = Skins.Levels(data, pic); int done = 0;
            foreach (var l in lv)
            {
                var img = new Rgba(l.W, l.H);
                for (int i = 0; i < l.W * l.H; i++) { img.Px[4 * i] = r; img.Px[4 * i + 1] = g; img.Px[4 * i + 2] = b; img.Px[4 * i + 3] = 255; }
                var enc = Skins.Encode(img, l.Fmt, l.Mips, l.Size);
                if (enc.Length == l.Size) { enc.CopyTo(data, l.At); done++; }
            }
            log($"   DIAGNOSTIC: {pic} -> solid ({r},{g},{b}), {done} of {lv.Count} sizes");
        }
    }

    const string PaintPic = "bowler_team_alpha";
    /// the Bowler's alpha-blended picture (RGBA, 512): the game's "alpha" material reads it - its boot badge. Decals (badges, stickers) go here.
    const string AlphaPic = "bowler_alpha";

    /// writes the decal picture over the alpha picture, every stored size. The alpha material adds its picture to what is behind it
    /// (source + background x (1 - alpha)), so the colours are laid out premultiplied (colour x alpha); see-through texels are black.
    static void PutAlphaPicture(byte[] data, CarModel.Body raw, List<SkinLevel> lv, Action<string> log)
    {
        var src = raw.DecalImage ?? new Rgba(CarModel.Atlas, CarModel.Atlas); var img = new Rgba(src.W, src.H);
        for (int i = 0; i < src.W * src.H; i++)
        {
            int a = src.Px[4 * i + 3];
            for (int q = 0; q < 3; q++) img.Px[4 * i + q] = (byte)(src.Px[4 * i + q] * a / 255);
            img.Px[4 * i + 3] = (byte)a;
        }
        int done = 0;
        foreach (var l in lv)
        {
            var enc = Skins.Encode(img.Resize(l.W, l.H), l.Fmt, l.Mips, l.Size);
            if (enc.Length != l.Size) { log($"   (decal picture {l.W} x {l.H}: unexpected size, left as it is)"); continue; }
            enc.CopyTo(data, l.At); done++;
        }
        log($"   the decal picture ({AlphaPic}): {done} of {lv.Count} sizes written");
    }

    /// writes the decal picture over the livery picture, every stored size; everything outside the decals is see-through (the paint shows)
    static void PutPaintPicture(byte[] data, CarModel.Body raw, List<SkinLevel> lv, Action<string> log)
    {
        var img = raw.DecalImage;
        if (img == null)
        {
            img = new Rgba(CarModel.Atlas, CarModel.Atlas);
            for (int i = 0; i < img.W * img.H; i++) { img.Px[4 * i] = 128; img.Px[4 * i + 1] = 128; img.Px[4 * i + 2] = 128; }
        }
        int done = 0;
        foreach (var l in lv)
        {
            var enc = Skins.Encode(img.Resize(l.W, l.H), l.Fmt, l.Mips, l.Size);
            if (enc.Length != l.Size) { log($"   (paint picture {l.W} x {l.H}: unexpected size, left as it is)"); continue; }
            enc.CopyTo(data, l.At); done++;
        }
        log($"   the paint picture ({PaintPic}): {done} of {lv.Count} sizes written{(raw.DecalImage != null ? " with the decals" : ", all see-through")}");
    }

    [ThreadStatic] static string? GlassElement, AlphaElement;
    [ThreadStatic] static HashSet<MeshInfo>? _coverUsed;
    static HashSet<MeshInfo> CoverUsed => _coverUsed ??= new();
    [ThreadStatic] static List<(double X, double Y, double Z, byte[] Uv, byte[] Last)>? GlassFrom;

    /// a mesh record's 56 bytes after its element: the render instance number (which pictures + exposed settings the mesh reads),
    /// the draw-order flags... copied from a real mesh of the kind we want, so our part is that kind of mesh in every way
    static void CopyMeshHeader(byte[] d, MeshInfo dst, MeshInfo src) => Array.Copy(d, src.MaterialAt + 20, d, dst.MaterialAt + 20, 56);

    static (MeshInfo M, int S)? GlassRoom(byte[] data, ModelInfo M, List<MeshRow> meshes, List<(MeshInfo M, int S)> rooms, int glassTris, Action<string> log)
    {
        var fx = M.FxFiles.Select(f => f.ToLowerInvariant()).ToList();
        var win = meshes.FirstOrDefault(r => r.S >= 0 && fx[r.M.Material].StartsWith("veh_glass") && !fx[r.M.Material].Contains("_a_"));
        if (win == null) { log("   (no picture-free glass in the body model: the glass stays in the body)"); return null; }
        var same = rooms.Where(r => r.S == win.S).OrderBy(r => r.M.Data1[0].VCount).ToList();
        if (same.Count == 0) { log("   (no trim part with the windscreen's layout: the glass stays in the body)"); return null; }
        int need = glassTris * 3;              // points before welding: a safe upper guess
        var pick = same.FirstOrDefault(r => r.M.Data1[0].VCount >= need);
        if (pick.M == null) pick = same[^1];
        B.Put(data, pick.M.MaterialAt, (uint)win.M.Material);
        // ... and belongs to the windscreen's element: the game gives glass its settings (g_vRenderSettings = alpha min / max,
        // reflection) only on elements named e_window* / e_headlightcover*. In e_interior the glass got none: it drew
        // black, and the first load could crash. Mesh record: material, definition, face type, face stream, element (u16).
        B.Put(data, pick.M.MaterialAt + 16, (ushort)win.M.Object);
        CopyMeshHeader(data, pick.M, win.M);
        GlassElement = win.M.Object < M.AllElements.Count ? M.AllElements[win.M.Object].Name : null;
        GlassFrom = WindowPoints(data, M, win);
        log($"   glass part: a trim part ({pick.M.Data1[0].VCount} points) now uses the windscreen's glass ({M.FxFiles[win.M.Material]}) and belongs to {GlassElement}");
        return pick;
    }

    /// the real windscreen's points (before anything is changed): position, its 8 uv bytes (texture + damage texture
    /// coordinates) and its last 4 bytes. Glass points copy these from the nearest one: the glass shader mixes the car's
    /// scratch / damage texture into the glass colour AND its alpha, read at those coordinates - with our body picture's
    /// coordinates the glass read a wrong spot and came out dark and solid whatever the glass settings said.
    static List<(double X, double Y, double Z, byte[] Uv, byte[] Last)> WindowPoints(byte[] data, ModelInfo M, MeshRow win)
    {
        var o = new List<(double, double, double, byte[], byte[])>();
        var vs = M.VStreams[win.S]; int stride = vs.Bytes, vb = win.M.VOffset[0] / stride + win.M.Data1[0].VOff;
        int uvAt = -1, at0 = 0;
        foreach (var (t, ch, _) in vs.Def) { if (t == 9 && ch == 5 && uvAt < 0) uvAt = at0; at0 += TypeSize.GetValueOrDefault(t, 4); }
        if (uvAt < 0 || vs.Def[0].Type != 9) return o;
        for (int k = 0; k < win.M.Data1[0].VCount; k++)
        {
            int a = vs.Start + stride * (vb + k);
            o.Add(((double)BitConverter.ToHalf(data, a), (double)BitConverter.ToHalf(data, a + 2), (double)BitConverter.ToHalf(data, a + 4),
                   data.AsSpan(a + uvAt, 8).ToArray(), data.AsSpan(a + stride - 4, 4).ToArray()));
        }
        return o;
    }

    static int Place(byte[] data, ModelInfo M, List<MeshRow> meshes, List<(MeshInfo M, int S)> rooms, double[] pts, double[] uvs, List<(int A, int B, int C)> faces, bool clear = true,
                     List<(double X, double Y, double Z, byte[] Uv, byte[] Last)>? copyFrom = null, List<(MeshInfo M, int S)>? usedRooms = null, double[]? shine = null)
    {
        int np = pts.Length / 3;
        var keys = new (long, long, long)[np];
        for (int i = 0; i < np; i++) keys[i] = ((long)Math.Round(pts[3 * i] / 1e-4), (long)Math.Round(pts[3 * i + 1] / 1e-4), (long)Math.Round(pts[3 * i + 2] / 1e-4));
        var uniq = keys.Distinct().OrderBy(k => k).Select((k, i) => (k, i)).ToDictionary(q => q.k, q => q.i);
        var same = keys.Select(k => uniq[k]).ToArray();
        var acc = new double[uniq.Count * 3];
        foreach (var (a, b, c) in faces)
        {
            double ux = pts[3 * b] - pts[3 * a], uy = pts[3 * b + 1] - pts[3 * a + 1], uz = pts[3 * b + 2] - pts[3 * a + 2];
            double vx = pts[3 * c] - pts[3 * a], vy = pts[3 * c + 1] - pts[3 * a + 1], vz = pts[3 * c + 2] - pts[3 * a + 2];
            double fx = uy * vz - uz * vy, fy = uz * vx - ux * vz, fz = ux * vy - uy * vx;
            foreach (int v in new[] { a, b, c }) { int s = same[v]; acc[3 * s] += fx; acc[3 * s + 1] += fy; acc[3 * s + 2] += fz; }
        }
        var nrm = new double[np * 3];
        for (int i = 0; i < np; i++)
        {
            int s = same[i];
            double x = acc[3 * s], y = acc[3 * s + 1], z = acc[3 * s + 2], l = Math.Sqrt(x * x + y * y + z * z) + 1e-9;
            nrm[3 * i] = x / l; nrm[3 * i + 1] = y / l; nrm[3 * i + 2] = z / l;
        }
        var walk = Stripify(faces);
        var seen = new HashSet<int>(); var order = new List<int>();
        var lookup = new Dictionary<(int, int, int), int>();
        static (int, int, int) Sort3(int a, int b, int c) { if (a > b) (a, b) = (b, a); if (b > c) (b, c) = (c, b); if (a > b) (a, b) = (b, a); return (a, b, c); }
        for (int i = 0; i < faces.Count; i++) lookup[Sort3(faces[i].A, faces[i].B, faces[i].C)] = i;
        for (int i = 0; i + 2 < walk.Count; i++)
        {
            var k = Sort3(walk[i], walk[i + 1], walk[i + 2]);
            if (lookup.TryGetValue(k, out int j) && k.Item1 != k.Item2 && k.Item2 != k.Item3 && seen.Add(j)) order.Add(j);
        }
        for (int j = 0; j < faces.Count; j++) if (!seen.Contains(j)) order.Add(j);
        var lods = LodRecords(data, M);
        var index = new Dictionary<MeshInfo, int>();
        for (int i = 0; i < M.MeshInfos.Count; i++) index[M.MeshInfos[i]] = i;
        if (clear)
        foreach (var (m, el, _) in meshes)
            if (CarModel.Kind(el) is not ("tyre" or "rim"))
                foreach (var d1 in m.Data1)
                    if (d1.FaceCount > 0)
                    {
                        int at = M.FStreams[m.FaceStream].Start + 2 * d1.FaceOff;
                        for (int i = 0; i < d1.FaceCount; i++) B.Put(data, at + 2 * i, (ushort)d1.VOff);
                    }
        int pos = 0;
        foreach (var (m, s) in rooms)
        {
            if (pos >= order.Count) break;
            var vs = M.VStreams[s];
            var d1 = m.Data1[0];
            int room = IndexRoom(m), stride = vs.Bytes;
            var offs = new Dictionary<(int, int, int), int>();
            int o0 = 0;
            foreach (var (t, ch, sub) in vs.Def)
            {
                offs.TryAdd((t, ch, ch == 5 && t == 11 ? sub : -1), o0);
                o0 += TypeSize.GetValueOrDefault(t, 4);
            }
            var verts = new Dictionary<int, int>(); int n = 0;
            while (pos + n < order.Count)
            {
                var (a, b, c) = faces[order[pos + n]];
                int nw = 0; var fresh = new List<int>();
                foreach (int v in new[] { a, b, c }) if (!verts.ContainsKey(v)) { fresh.Add(v); nw++; }
                if (verts.Count + nw > d1.VCount) break;
                foreach (int v in fresh) if (!verts.ContainsKey(v)) verts[v] = verts.Count;
                n++;
            }
            Dictionary<int, int> local = new(); List<int> strip = new();
            while (n > 0)
            {
                var take = order.GetRange(pos, n);
                var used = new HashSet<int>();
                foreach (int i in take) { var (a, b, c) = faces[i]; used.Add(a); used.Add(b); used.Add(c); }
                local = used.OrderBy(v => verts[v]).Select((v, j) => (v, j)).ToDictionary(q => q.v, q => q.j);
                strip = Stripify(take.Select(i => (d1.VOff + local[faces[i].A], d1.VOff + local[faces[i].B], d1.VOff + local[faces[i].C])).ToList());
                if (strip.Count <= room) break;
                n = (int)(n * room / (double)strip.Count * 0.97);
            }
            if (n == 0) continue;
            usedRooms?.Add((m, s));
            int vbase = m.VOffset[0] / stride;
            foreach (var (v, j) in local)
            {
                int at = vs.Start + stride * (vbase + d1.VOff + j);
                B.Put(data, at, (Half)pts[3 * v]); B.Put(data, at + 2, (Half)pts[3 * v + 1]); B.Put(data, at + 4, (Half)pts[3 * v + 2]); B.Put(data, at + 6, (Half)0.0);
                double nx = nrm[3 * v], ny = nrm[3 * v + 1], nz = nrm[3 * v + 2];
                if (offs.TryGetValue((11, 3, -1), out int a3))
                {
                    data[at + a3] = (byte)Math.Max(0, Math.Min(254, Py.Round(127 + 127 * nx))); data[at + a3 + 1] = (byte)Math.Max(0, Math.Min(254, Py.Round(127 + 127 * ny)));
                    data[at + a3 + 2] = (byte)Math.Max(0, Math.Min(254, Py.Round(127 + 127 * nz))); data[at + a3 + 3] = 0;
                }
                foreach (var k in new[] { (11, 5, 2), (11, 5, 3) })
                    if (offs.TryGetValue(k, out int a4)) { data[at + a4] = 127; data[at + a4 + 1] = 127; data[at + a4 + 2] = 127; data[at + a4 + 3] = 0; }
                if (offs.TryGetValue((11, 9, -1), out int a9))
                {
                    var (l0, l1, l2, l3) = LightBytes(nx, ny, nz);
                    // matte parts (interior, plastic): the first byte is the ambient occlusion the game multiplies the reflection by; the
                    // game's own interiors keep it near 10 (shiny parts 60-100)
                    if (shine != null && v < shine.Length) l0 = (byte)Math.Max(6, Math.Min(255, Py.Round(10 + (l0 - 10) * shine[v])));
                    data[at + a9] = l0; data[at + a9 + 1] = l1; data[at + a9 + 2] = l2; data[at + a9 + 3] = l3;
                }
                if (offs.TryGetValue((9, 5, -1), out int a5))
                {
                    B.Put(data, at + a5, (Half)uvs[2 * v]); B.Put(data, at + a5 + 2, (Half)uvs[2 * v + 1]); B.Put(data, at + a5 + 4, (Half)0.0); B.Put(data, at + a5 + 6, (Half)0.0);
                }
                if (copyFrom is { Count: > 0 })
                {
                    // glass: the nearest real windscreen point's uv (texture + damage coordinates) and last 4 bytes
                    double px = pts[3 * v], py = pts[3 * v + 1], pz = pts[3 * v + 2], best = double.MaxValue; int bi = 0;
                    for (int q = 0; q < copyFrom.Count; q++)
                    {
                        double dx = copyFrom[q].X - px, dy = copyFrom[q].Y - py, dz = copyFrom[q].Z - pz, dd = dx * dx + dy * dy + dz * dz;
                        if (dd < best) { best = dd; bi = q; }
                    }
                    // only the damage half (zw): the picture half (xy) stays on our see-through glass corner
                    if (offs.TryGetValue((9, 5, -1), out int u5)) copyFrom[bi].Uv.AsSpan(4, 4).CopyTo(data.AsSpan(at + u5 + 4));
                    copyFrom[bi].Last.CopyTo(data, at + stride - 4);
                }
            }
            int fa = M.FStreams[m.FaceStream].Start + 2 * d1.FaceOff;
            for (int i = 0; i < room; i++) B.Put(data, fa + 2 * i, (ushort)(i < strip.Count ? strip[i] : strip[^1]));
            int ix = index[m];
            for (int lod = 0; lod < m.Data1.Count; lod++)
                if (lods.TryGetValue((ix, lod), out int at))
                {
                    B.Put(data, at, (uint)d1.FaceOff); B.Put(data, at + 4, (uint)room); B.Put(data, at + 8, (uint)d1.VOff); B.Put(data, at + 12, (uint)d1.VCount);
                }
            pos += n;
        }
        return pos;
    }
}

/// where the exhaust flames come out: the car record's exhaustEmitters (a list of points in the car model's own space, x = side,
/// y = up, z = forward). A custom car copies its body car's record, so its flames sit where that car's pipes are; car["exhaust"]
/// = [[x, y, z], ...] puts them where this car's pipes are (any number of pipes; added ones copy the last game one's settings).
public static class CarExhaust
{
    static Db? _db;
    static readonly object _lock = new();

    /// a game car's flame points (empty if it has none or can't be read)
    public static List<double[]> Game(string car)
    {
        try
        {
            lock (_lock)
            {
                _db ??= new Db(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
                return _db.Cars().TryGetValue(car, out int i) ? Read(_db, i) : new();
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException or InvalidOperationException) { return new(); }
    }

    static (int Field, int Pos) Offsets(Db db, int obj)
    {
        var b0 = db.K.Objects[obj].Blocks[0];
        int fOff = db.Field(b0.Type, "exhaustEmitters").Off;
        int blk = B.U16(db.Data, b0.Offset + fOff);
        if (blk == 0xFFFF) return (fOff, -1);
        int et = db.K.Objects[obj].Blocks[blk].Type;
        var pf = db.K.AllFields(et).First(f => f.Name == "position");
        return (fOff, pf.Offset + db.K.FieldOffset(pf.Type, "vx"));
    }

    internal static List<double[]> Read(Db db, int obj)
    {
        var o = new List<double[]>();
        var (fOff, pos) = Offsets(db, obj);
        if (pos < 0) return o;
        var ob = db.K.Objects[obj]; int a = ob.Blocks[0].Offset + fOff;
        int blk = B.U16(db.Data, a), first = B.U16(db.Data, a + 2), cnt = (int)B.U32(db.Data, a + 4);
        var eb = ob.Blocks[blk];
        for (int j = first; j < first + cnt; j++)
        {
            int e = eb.Offset + j * eb.Size + pos;
            o.Add(new double[] { B.F32(db.Data, e), B.F32(db.Data, e + 4), B.F32(db.Data, e + 8) });
        }
        return o;
    }

    /// the record (object obj of amax.bin) with these flame points instead of its own
    public static byte[] Write(byte[] amaxD, int obj, List<double[]> pts)
    {
        var db = new Db(amaxD);
        var (fOff, pos) = Offsets(db, obj);
        if (pos < 0 || pts.Count == 0) throw new InvalidDataException("this car has no exhaust flames to move");
        return KslfWrite.EditObject(amaxD, obj, c =>
        {
            int a = c.Blocks[0] + fOff;
            int blk = B.U16(c.Data.A, a), first = B.U16(c.Data.A, a + 2), cnt = (int)B.U32(c.Data.A, a + 4);
            int size = c.Types[c.Rows[blk][0]].Size;
            var items = new List<byte[]>();
            for (int j = 0; j < pts.Count; j++)
            {
                var e = c.Data.A.AsSpan(c.Blocks[blk] + (first + Math.Min(j, cnt - 1)) * size, size).ToArray();
                for (int k = 0; k < 3; k++) B.Put(e, pos + 4 * k, (float)pts[j][k]);
                items.Add(e);
            }
            if (pts.Count <= cnt)
                for (int j = 0; j < pts.Count; j++) items[j].CopyTo(c.Data.A, c.Blocks[blk] + (first + j) * size);
            else
            {
                first = c.Rows[blk][1];
                c.Insert(blk, first, items.SelectMany(x => x).ToArray());
                a = c.Blocks[0] + fOff;
            }
            c.Data.Put(a + 2, (ushort)first); c.Data.Put(a + 4, (uint)pts.Count);
        });
    }

    /// (look only) the flame points of the cars whose names start with one of these
    public static List<string> Dump(byte[] amax, string[] starts)
    {
        var db = new Db(amax);
        return db.Cars().Where(c => starts.Any(c.Key.StartsWith)).Select(c => $"{c.Key}: " + string.Join("  ", Read(db, c.Value).Select(p => $"({p[0]:0.000}, {p[1]:0.000}, {p[2]:0.000})"))).ToList();
    }

    /// how many pipes a car may have: a replacer as many as the game car it replaces, a new car 2
    public static int Max(JObj car) => car.Truthy("replace") ? Math.Max(1, Game(car.Str("replace")!).Count) : 2;

    /// where this car's flames come out: its own spots, else the game car's (the replaced car, else the body car); never more than Max
    public static List<double[]> Points(JObj car)
    {
        var p = Of(car) ?? Game(car.Truthy("replace") ? car.Str("replace")! : car.Str("base")!);
        return p.Take(Max(car)).ToList();
    }

    /// the points to write in the build, or null to leave the record's own (no own spots and not more than Max of them)
    public static List<double[]>? ToWrite(JObj car, int gameCount) => Of(car) != null || gameCount > Max(car) ? Points(car) : null;

    /// the points a car's JSON asks for (null = the game's)
    public static List<double[]>? Of(JObj car)
    {
        var arr = car.Arr("exhaust");
        if (arr == null || arr.Count == 0) return null;
        var o = new List<double[]>();
        foreach (var p in arr)
            if (p is List<object?> l && l.Count >= 3) o.Add(new[] { Convert.ToDouble(l[0]), Convert.ToDouble(l[1]), Convert.ToDouble(l[2]) });
        return o.Count > 0 ? o : null;
    }
}

/// a custom car's own maker logo (shown in the garage / car select instead of the body car's maker's). The picture goes into the
/// menu pack (\ui\data\ui_frontend.bpr) like the game's logos: 256 x 128 + 128 x 64, DXT5 (with see-through). The car gets a copy
/// of its maker record (AmaxManufacturer) whose UiTexture names that picture; the maker's name stays.
public static class CarLogo
{
    public const string Folder = "logos";                          // maps\cars\logos\ (next to the imported bodies)
    public const int W = 256, H = 128;
    public static string FilePath(string file) => Path.Combine(Paths.Tracks, "cars", Folder, file);
    public static string TexName(int car) => $"man_bmb_{car}";

    /// the picture fitted (whole, centred, see-through around it) into w x h, as RGBA bytes
    public static byte[] Rgba(string path, int w, int h)
    {
        using var src = new Bitmap(path);
        using var bm = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            double k = Math.Min((double)w / src.Width, (double)h / src.Height);
            int dw = Math.Max(1, (int)Math.Round(src.Width * k)), dh = Math.Max(1, (int)Math.Round(src.Height * k));
            g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
        }
        var o = new byte[w * h * 4];
        var d = bm.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, row.Length);
                for (int x = 0; x < w; x++) { int a = 4 * (y * w + x); o[a] = row[4 * x + 2]; o[a + 1] = row[4 * x + 1]; o[a + 2] = row[4 * x]; o[a + 3] = row[4 * x + 3]; }
            }
        }
        finally { bm.UnlockBits(d); }
        return o;
    }

    /// (look only) each car whose name starts with one of these: its maker record and the logo picture it names
    public static List<string> Dump(byte[] amax, string[] starts)
    {
        var db = new Db(amax); var o = new List<string>();
        foreach (var (n, i) in db.Cars().Where(c => starts.Any(c.Key.StartsWith)))
        {
            int mo = db.ObjOfUid(db.RefUid(i, "Manufacturer"));
            o.Add($"{n}: maker {db.Name(mo)} -> {db.K.Top(db.K.Objects[mo])["UiTexture"]}");
        }
        return o;
    }

    /// the game's logo picture data: 256 x 128 then 128 x 64, DXT5
    public static byte[] Picture(string path) => B.Concat(Sky.Dxt5(Rgba(path, W, H), W, H), Sky.Dxt5(Rgba(path, W / 2, H / 2), W / 2, H / 2));
}

public static class CarIcon
{
    public const string Auto = "auto"; const int Wide = 480;
    public static readonly string[] Sheets = "abcd".Select(c => $"\\ui\\data\\ipf\\car_icons_{c}.ipf").ToArray();
    static readonly byte[] Head = Convert.FromHexString("010000000000803f0000803f0000803f0e0010000f000f001000110000000000ff00");

    static int Skip(byte[] d, int p)
    {
        uint kind = B.U32(d, p); int nl = (int)B.U32(d, p + 8), q = p + 12 + nl;
        if (kind == 3)
        {
            int n = (int)B.U32(d, q); q += 4;
            for (int i = 0; i < n; i++) q = Skip(d, q);
            return q;
        }
        q += 34 + 24 + 16;
        foreach (int item in new[] { 24, 2 })
        {
            int n = (int)B.U32(d, q); q += 4;
            for (int i = 0; i < n; i++) { int k = (int)B.U32(d, q); q += 4 + k * item; }
        }
        return q + 4;
    }

    static (int At, List<(string Name, int A, int B)> Kids) Top(byte[] d)
    {
        int at = 20 + (int)B.U32(d, 16), n = (int)B.U32(d, at), p = at + 4;
        var o = new List<(string, int, int)>();
        for (int i = 0; i < n; i++)
        {
            int q = Skip(d, p), nl = (int)B.U32(d, p + 8);
            o.Add((B.Latin1.GetString(d, p + 12, nl), p, q));
            p = q;
        }
        return (at, o);
    }

    static Dictionary<string, byte[]>? _shapes;
    public static Dictionary<string, byte[]> GameShapes(Pak root)
    {
        if (_shapes != null) return _shapes;
        var o = new Dictionary<string, byte[]>();
        foreach (var path in Sheets)
        {
            var d = root.Read(path)!;
            foreach (var (name, a, b) in Top(d).Kids) o.TryAdd(name.ToLowerInvariant(), d[a..b]);
        }
        return _shapes = o;
    }

    static byte[] Renamed(byte[] node, string name)
    {
        int nl = (int)B.U32(node, 8);
        var o = new ByteBuf(node.Length + 32);
        o.Add(node.AsSpan(0, 8)).AddU32((uint)name.Length).Add(B.Bytes(name)).Add(node.AsSpan(12 + nl));
        return o.ToArray();
    }

    public static (bool[] Px, int W, int H) SideMask(Pak root, JObj car)
    {
        var (all, tyres) = CarModel.CarParts(root, car);
        double f1 = double.MinValue, f0 = double.MaxValue, u0 = double.MaxValue, u1 = double.MinValue;
        foreach (var a in all) for (int i = 0; i < a.Length; i += 3) { f1 = Math.Max(f1, a[i + 2]); f0 = Math.Min(f0, a[i + 2]); u0 = Math.Min(u0, a[i + 1]); u1 = Math.Max(u1, a[i + 1]); }
        double L = f1 - f0, sc = (Wide - 1) / L;
        int h = (int)Math.Ceiling((u1 - u0) * sc) + 1;
        using var bm = new Bitmap(Wide, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(Color.Black);
            var pt = new PointF[3];
            foreach (var a in all)
                for (int i = 0; i + 8 < a.Length; i += 9)
                {
                    for (int k = 0; k < 3; k++) pt[k] = new PointF((float)((f1 - a[i + 3 * k + 2]) * sc), (float)((u1 - a[i + 3 * k + 1]) * sc));
                    g.FillPolygon(Brushes.White, pt);
                }
            if (tyres.Count > 0)
                foreach (bool front in new[] { true, false })
                {
                    double zmin = 1e30, zmax = -1e30, ymin = 1e30, ymax = -1e30;
                    foreach (var a in tyres)
                        for (int i = 0; i < a.Length; i += 3)
                            if ((a[i + 2] > f1 - L / 2) == front) { zmin = Math.Min(zmin, a[i + 2]); zmax = Math.Max(zmax, a[i + 2]); ymin = Math.Min(ymin, a[i + 1]); ymax = Math.Max(ymax, a[i + 1]); }
                    if (zmin > zmax) continue;
                    double cx = (f1 - (zmin + zmax) / 2) * sc, cy = (u1 - (ymin + ymax) / 2) * sc, r = (ymax - ymin) / 2 * sc, gp = Math.Max(2.0, 0.012 * Wide);
                    g.FillEllipse(Brushes.Black, (float)(cx - r - gp), (float)(cy - r - gp), (float)(2 * (r + gp)), (float)(2 * (r + gp)));
                    g.FillEllipse(Brushes.White, (float)(cx - r), (float)(cy - r), (float)(2 * r), (float)(2 * r));
                }
        }
        var p = Pic.FromBitmap(bm);
        var px = new bool[Wide * h];
        for (int i = 0; i < px.Length; i++) px[i] = p.Px[3 * i] > 127;
        return (px, Wide, h);
    }

    static byte[] MaskShape(string name, bool[] px, int w, int h)
    {
        var rects = new List<(int, int, int, int)>();
        var open = new Dictionary<(int, int), int>();
        for (int y = 0; y <= h; y++)
        {
            var runs = new List<(int, int)>();
            if (y < h)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x]) { int x0 = x; while (x < w && px[y * w + x]) x++; runs.Add((x0, x)); }
            var nxt = new Dictionary<(int, int), int>();
            foreach (var run in runs) { if (open.Remove(run, out int y0)) nxt[run] = y0; else nxt[run] = y; }
            foreach (var ((x0, x1), y0) in open) rects.Add((x0, y0, x1, y));
            open = nxt;
        }
        double k = 1.0 / w;
        var o = new ByteBuf(rects.Count * 120 + 200);
        o.AddU32(1).AddU32(1).AddU32((uint)name.Length).Add(B.Bytes(name)).Add(Head);
        foreach (double v in new[] { 0.0, 0.0, 0.0, 1.0, h * k, 0.0, 0, 0, 0, 0 }) o.AddF32((float)v);
        o.AddU32((uint)rects.Count);
        foreach (var (x0, y0, x1, y1) in rects)
        {
            o.AddU32(4);
            foreach (var (u, v) in new[] { (x0 * k, y0 * k), (x1 * k, y0 * k), (x1 * k, y1 * k), (x0 * k, y1 * k) })
                o.AddF32((float)u).AddF32((float)v).AddF32(0).AddF32(0).AddF32(1).AddF32(0);
        }
        o.AddU32((uint)rects.Count);
        foreach (var _ in rects) { o.AddU32(6); foreach (int i in new[] { 3, 0, 1, 1, 2, 3 }) o.AddU16((ushort)i); }
        return o.AddU32(0).ToArray();
    }

    public static string Choice(Pak root, JObj car)
    {
        string? c = car.Str("mini_icon");
        if (!string.IsNullOrEmpty(c) && (c == Auto || GameShapes(root).ContainsKey(c.ToLowerInvariant()))) return c == Auto ? c : c.ToLowerInvariant();
        return car.Truthy("body_glb") ? Auto : car.Str("base")!.ToLowerInvariant();
    }

    public static IEnumerable<KeyValuePair<string, byte[]>> Files(Pak root, List<JObj> cars, List<string> names, Action<string> log)
    {
        var o = new List<KeyValuePair<string, byte[]>>();
        foreach (var sheet in cars.Select(c => Sheets[(int)c.Int("class")]).Distinct().OrderBy(s => s, StringComparer.Ordinal))
        {
            var d = (byte[])root.Read(sheet)!.Clone();
            var (at, kids) = Top(d);
            var have = kids.Select(k => k.Name.ToLowerInvariant()).ToHashSet();
            var add = new ByteBuf(); int count = kids.Count;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i]; string name = names[i];
                if (Sheets[(int)car.Int("class")] != sheet || have.Contains(name.ToLowerInvariant())) continue;
                string c = Choice(root, car);
                if (c == Auto) { var (px, w, h) = SideMask(root, car); add.Add(MaskShape(name.ToLowerInvariant(), px, w, h)); }
                else add.Add(Renamed(GameShapes(root)[c], name.ToLowerInvariant()));
                count++;
                log($"   mini icon {name} (class {"ABCD"[(int)car.Int("class")]}): {(c == Auto ? "side view" : "like " + c)}");
            }
            B.Put(d, at, (uint)count);
            o.Add(new(sheet, B.Concat(d, add.ToArray())));
        }
        return o;
    }
}

public static partial class CarBuild
{
    const uint LogoUid0 = 0xCA4F0000, Loc0 = 0x4D440000; const double DriverDown = 1.5;
    static readonly (string Stat, string[] Fields)[] Trend =
    {
        ("speed", new[] { "friction.airFriction", "gears.finalRatio" }), ("acceleration", new[] { "engine.engineTorque_Rpm.yScale" }),
        ("drift", new[] { "steering.slideRecovery", "steering.steerSpringDrift", "brakes.handbrakeForce", "tyres.rearTyres.transverseGripScale", "tyres.rearTyres.combinedSlipForwards_Mph.y0", "tyres.rearTyres.combinedSlipForwards_Mph.y1" }),
        ("grip", new[] { "tyres.frontTyres.grip.yScale", "tyres.rearTyres.grip.yScale", "tyres.frontTyres.transverseGripScale", "friction.downforce" }),
    };

    static List<JObj> WorldCars(JObj wld)
    {
        var o = new List<JObj>();
        foreach (var c0 in wld.Objs("cars"))
        {
            var c = new JObj();
            foreach (var kv in c0) c[kv.Key] = kv.Value;
            if (c.Truthy("body_glb")) c["base"] = CarGlb.GlbBody[(int)c.Int("class")];
            o.Add(c);
        }
        return o;
    }

    static List<string> InternalNames(List<JObj> cars)
    {
        var o = new List<string>(); var used = new HashSet<string>();
        foreach (var c in cars)
        {
            string n = "BMB_" + Regex.Replace(c.Str("name")!, "[^A-Za-z0-9]+", "_").Trim('_');
            if (n == "BMB_") n = "BMB_car";
            while (!used.Add(n.ToLowerInvariant())) n += "_2";
            o.Add(n);
        }
        return o;
    }

    static double Fit(double[] xs, double[] ys)
    {
        double mx = xs.Sum() / xs.Length, my = ys.Sum() / ys.Length, sxx = xs.Sum(x => (x - mx) * (x - mx));
        return sxx == 0 ? 0.0 : xs.Zip(ys).Sum(q => (q.First - mx) * (q.Second - my)) / sxx;
    }

    static (Dictionary<string, double>, Dictionary<string, double>) Handling(JObj car, Db amax, Db rp)
    {
        var names = amax.Cars();
        int cls = (int)car.Int("class");
        var clsCars = CarsData.ClassCars(cls).Where(c => names.ContainsKey(c.Name)).ToList();
        var rpi = clsCars.ToDictionary(c => c.Name, c => rp.ObjOfUid(amax.RefUid(names[c.Name], "dynamics")));
        var rpOut = new Dictionary<string, double>(); var carOut = new Dictionary<string, double>();
        var stats = car.Obj("stats")!;
        double Place(string stat, double[] ys, double slope)
        {
            var (lo, hi) = CarsData.Limit(cls, stat);
            double t = hi == lo ? 0.5 : (stats.Num(stat) - lo) / (double)(hi - lo);
            return ys.Min() + (slope >= 0 ? t : 1 - t) * (ys.Max() - ys.Min());
        }
        foreach (var (stat, fields) in Trend)
        {
            var xs = clsCars.Select(c => (double)c.Stats[stat]).ToArray();
            foreach (var f in fields)
            {
                bool perKg = f == "engine.engineTorque_Rpm.yScale";
                var ys = clsCars.Select(c => rp.Get(rpi[c.Name], f) / (perKg ? rp.Get(rpi[c.Name], "mass.mass") : 1.0)).ToArray();
                rpOut[f] = Place(stat, ys, Fit(xs, ys)) * (perKg ? rp.Get(rpi[car.Str("base")!], "mass.mass") : 1.0);
            }
        }
        {
            var xs = clsCars.Select(c => (double)c.Stats["strength"]).ToArray();
            foreach (var f in new[] { "HealthGroup", "weightClass" })
            {
                var ys = clsCars.Select(c => amax.Get(names[c.Name], f)).ToArray();
                carOut[f] = Py.Round(Place("strength", ys, Fit(xs, ys)));
            }
        }
        return (rpOut, carOut);
    }

    static void OwnName(ByteBuf data, ByteBuf pool, int[] blocks, string text) { data.Put(blocks[0], (uint)pool.Len); pool.AddSz(text); }

    // ---- clear glass for an imported body (vehicles.bin) -----------------------------------------------------------
    /// every car's windows share the game's glass materials (near-black tint, 10-30 % solid head-on, 75-100 % at an angle):
    /// an imported body's see-through glass looked black. A custom car with an imported body gets its own material set:
    /// a copy of the game's default set (AmaxVehicleDefaults.materials) whose windows (part codes 23 Window, 24
    /// WindowInterior) use a clear copy of the window glass. Uids: GlassUid0 + 16 * car (+0 glass, +1..5 parts, +8 the set).
    public const uint GlassUid0 = 0xCA500000, DefaultMaterialSet = 0xC2D82C72;
    // VEH_PartCode in vehicles.bin: 23 = Window, 24 = WindowInterior (35-37 are the light covers - the first try changed those)
    static readonly int[] WindowParts = { 23, 24, 35, 36, 37 };
    public const double ClearMinAlpha = 0.0, ClearMaxAlpha = 0.10, ClearMinReflect = 0.02, ClearMaxReflect = 0.15;      // user test 2026-11-21: our glass record DOES apply (blue test); 0.03-0.35 still looked black over the dim cabin
    /// TEMPORARY (2026-11-19): one test build with red, mostly solid clear glass - does the game use our glass record at all? Set back to false.
    public const bool GlassTestRed = false;
    /// TEMPORARY (2026-11-21) DIAGNOSTIC: every picture of the car solid in its own colour (which picture does each part read?) and
    /// our window glass bright white, alpha .9 (does our glass record apply?). Set back to false.
    public const bool Diag = false;
    /// TEMPORARY DIAGNOSTIC (2026-11-21): the game's OWN window glass (every car) red, alpha .7-.9, while our clone keeps the clear values:
    /// our windows red = they use the game's glass (our override is ignored); dark / clear = they use our clone. Set back to false.
    public const bool DefaultGlassRed = false;
    /// TEMPORARY (2026-11-19): one test build with the clear glass fully invisible (0 solid, 0 reflection) - is the grey from our values? Set back to false.
    public const bool GlassTestInvisible = false;
    /// TEMPORARY (2026-11-21): leave the window glass out completely. Set back to false.
    public const bool GlassTestDrop = false;
    /// TEMPORARY A/B (2026-11-21): the right half of the glass (x >= 0) goes into the game's own headlight-cover glass meshes (a native glass pass), the left half stays in the trim part that was switched to glass. Set back to false (or make it the real way).
    public const bool GlassCover = true;

    /// adds the car's clear-glass material set to vehicles.bin; returns its uid (0 = the game's set is not as expected: nothing added)
    static uint ClearGlassSet(ref byte[] vehD, int car, Action<string> log)
    {
        uint g = GlassUid0 + 16u * (uint)car, setUid = g + 8;
        var veh = new Db(vehD);
        int so = veh.RowOfUid(DefaultMaterialSet) is int sr && sr >= 0 ? veh.K.Refs[sr].Obj : -1;
        if (so < 0 || so == 0xFFFF) { log("   (clear glass: the game's material set was not found - the glass stays the game's)"); return 0; }
        var sb = veh.K.Objects[so].Blocks[0];
        int a0 = sb.Offset + veh.Field(sb.Type, "defaults").Off;
        int blk = B.U16(vehD, a0), first = B.U16(vehD, a0 + 2), cnt = (int)B.U32(vehD, a0 + 4);
        int listAt = veh.K.Objects[so].Blocks[blk].Offset + 4 * first;
        var instOf = new Dictionary<int, (int Obj, int Index)>();
        for (int j = 0; j < cnt; j++)
        {
            int row = (int)B.U32(vehD, listAt + 4 * j), obj = veh.K.Refs[row].Obj;
            int part = (int)veh.Get(obj, "part.partCode");
            if (WindowParts.Contains(part) && !instOf.ContainsKey(part)) instOf[part] = (obj, j);
        }
        if (!instOf.ContainsKey(23)) { log("   (clear glass: the game's set has no window glass - the glass stays the game's)"); return 0; }
        uint origGlassUid = veh.RefUid(instOf[23].Obj, "material");
        int glassObj = veh.ObjOfUid(origGlassUid);
        int gt = veh.K.Objects[glassObj].Blocks[0].Type;
        var v0 = veh;
        vehD = KslfWrite.CloneObjectAny(vehD, glassObj, null, g, (data, pool, blocks) =>
        {
            v0.Set(data, blocks[0], gt, "minAlpha", GlassTestRed ? 0.7 : ClearMinAlpha); v0.Set(data, blocks[0], gt, "maxAlpha", GlassTestRed ? 0.9 : ClearMaxAlpha);
            v0.Set(data, blocks[0], gt, "minReflectivity", ClearMinReflect); v0.Set(data, blocks[0], gt, "maxReflectivity", ClearMaxReflect);
            if (DefaultGlassRed) { v0.Set(data, blocks[0], gt, "tint.r", 0.0); v0.Set(data, blocks[0], gt, "tint.g", 0.0); v0.Set(data, blocks[0], gt, "tint.b", 1.0); v0.Set(data, blocks[0], gt, "minAlpha", 0.5); v0.Set(data, blocks[0], gt, "maxAlpha", 0.5); }
            if (GlassTestInvisible) foreach (var f in new[] { "minAlpha", "maxAlpha", "minReflectivity", "maxReflectivity" }) v0.Set(data, blocks[0], gt, f, 0.0);
            if (GlassTestRed) { v0.Set(data, blocks[0], gt, "tint.r", 1.0); v0.Set(data, blocks[0], gt, "tint.g", 0.0); v0.Set(data, blocks[0], gt, "tint.b", 0.0); }
        });
        if (GlassTestRed) log("   TEST BUILD: the clear glass is RED (to see if the game uses it) - not for players");
        if (GlassTestInvisible) log("   TEST BUILD: the clear glass is fully INVISIBLE (to see where the grey comes from) - not for players");
        var vg = new Db(vehD); int go = vg.ObjOfUid(origGlassUid); int gtt = vg.K.Objects[go].Blocks[0].Type;
        if ((GlassTestRed || DefaultGlassRed) && vg.Get(go, "tint.r") < 0.9)
        {
            // TEMPORARY test 2: the game's own window glass (all cars) red too - do these records drive the look at all?
            vehD = KslfWrite.EditObject(vehD, go, ctx => { vg.Set(ctx.Data, ctx.Blocks[0], gtt, "tint.r", 1.0); vg.Set(ctx.Data, ctx.Blocks[0], gtt, "tint.g", 0.0); vg.Set(ctx.Data, ctx.Blocks[0], gtt, "tint.b", 0.0); vg.Set(ctx.Data, ctx.Blocks[0], gtt, "minAlpha", 0.7); vg.Set(ctx.Data, ctx.Blocks[0], gtt, "maxAlpha", 0.9); });
            log("   TEST BUILD: the game's own window glass is RED too (every car)");
        }
        var instUid = new Dictionary<int, uint>();
        for (int k = 0; k < WindowParts.Length; k++)
        {
            int part = WindowParts[k];
            if (!instOf.TryGetValue(part, out var src)) continue;
            veh = new Db(vehD);
            int io = veh.ObjOfUid(veh.K.Refs[(int)B.U32(vehD, veh.K.Objects[veh.ObjOfUid(DefaultMaterialSet)].Blocks[blk].Offset + 4 * (first + src.Index))].Uid);
            int it = veh.K.Objects[io].Blocks[0].Type, gRow = veh.RowOfUid(g);
            var v1 = veh;
            vehD = KslfWrite.CloneObjectAny(vehD, io, null, g + 1 + (uint)k, (data, pool, blocks) => v1.Set(data, blocks[0], it, "material", gRow));
            instUid[part] = g + 1 + (uint)k;
        }
        veh = new Db(vehD);
        int so2 = veh.ObjOfUid(DefaultMaterialSet);
        var rows = instUid.ToDictionary(kv => instOf[kv.Key].Index, kv => veh.RowOfUid(kv.Value));
        int fOff = veh.Field(veh.K.Objects[so2].Blocks[0].Type, "defaults").Off;
        vehD = KslfWrite.CloneObjectAny(vehD, so2, null, setUid, (data, pool, blocks) =>
        {
            int b0 = blocks[0] + fOff, bb = B.U16(data.A, b0), ff = B.U16(data.A, b0 + 2);
            foreach (var (j, r) in rows) data.Put(blocks[bb] + 4 * (ff + j), (uint)r);
        });
        log($"   clear glass: own material set (windows {ClearMinAlpha * 100:0}-{ClearMaxAlpha * 100:0} % solid instead of the game's dark glass)");
        return setUid;
    }

    public static void AddCars(Dictionary<string, byte[]> files, JObj wld, Action<string> log)
    {
        var cars = WorldCars(wld);
        if (cars.Count == 0) return;
        byte[] Rd(string n) => files.TryGetValue("gamedata/xt/" + n, out var d) ? d : File.ReadAllBytes(Path.Combine(new[] { Paths.Unpacked, "xt" }.Concat(n.Split('/')).ToArray()));
        byte[] amaxD = Rd("amax.bin"), rpD = Rd("racingphysics.bin"), vehD = Rd("vehicles.bin"), mpD = Rd("multiplayer.bin"), vloc = Rd("loc/vehicles.loc");
        byte[]? audD = null, entD = null;          // audio.bin / audioentities.bin, once a car has its own sound
        Pak? rootSrc = null;
        var names = InternalNames(cars);
        var added = new List<uint>();
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var netUids = CarNetId.Uids(cars, amaxD, rpD, vehD);      // each car's lasting number (the same on every PC: online play)
        for (int i = 0; i < cars.Count; i++)
        {
            var car = cars[i]; string name = names[i];
            uint carUid = netUids[i], dynUid = carUid + 1, paintUid = carUid + 2, wheelUid = carUid + 3;
            var picks = CarsData.ClassCars((int)car.Int("class")).Select(x => x.Name).ToList();
            if (!picks.Contains(car.Str("base")!))
            {
                var c2 = new JObj(); foreach (var kv in car) c2[kv.Key] = kv.Value;
                c2["base"] = picks[0]; car = c2;
            }
            string bas = car.Str("base")!, wheels = car.Truthy("wheels") ? car.Str("wheels")! : bas;
            Db amax = new(amaxD), rp = new(rpD);
            int bi = amax.Cars()[bas];
            var (rpVals, carVals) = Handling(car, amax, rp);
            int rb = rp.ObjOfUid(amax.RefUid(bi, "dynamics")), ri = rb, rt = rp.K.Objects[ri].Blocks[0].Type;
            // a replacer: the replaced car's own handling, but the wheels where this car has them (the game puts each wheel where
            // the physics geometry says), so the whole look - body, tyres and their spots - is this car's
            string? replaceOf = Replaces(car);
            bool replacing = replaceOf != null && amax.Cars().ContainsKey(replaceOf);
            if (replacing) { ri = rp.ObjOfUid(amax.RefUid(amax.Cars()[replaceOf!], "dynamics")); rpVals = new(); }
            bool glbBody = car.Truthy("body_glb");
            if (glbBody) rb = rp.ObjOfUid(amax.RefUid(amax.Cars()[CarGlb.Container], "dynamics"));     // its wheels sit where the container's do
            var fit = car.Truthy("body_glb") ? car.Obj("glb_fit") ?? new JObj() : new JObj();
            foreach (var (f, d) in new[] { ("geometry.wheelBase", fit.Num("wb")), ("geometry.trackWidthFront", CarGlb.Track(fit, false)), ("geometry.trackWidthRear", CarGlb.Track(fit, true)) })
                if (d != 0 || replacing || glbBody) rpVals[f] = rp.Get(rb, f) + d;
            rpD = KslfWrite.CloneObjectAny(rpD, ri, null, dynUid, (data, pool, blocks) =>
            {
                foreach (var (f, v) in rpVals) rp.Set(data, blocks[0], rt, f, v);
                OwnName(data, pool, blocks, name + "_physics");
            });
            int vt = amax.K.Objects[bi].Blocks[0].Type;
            int pOff = amax.Field(vt, "baseVehicle.paintJobs").Off, at = amax.K.Objects[bi].Blocks[0].Offset + pOff;
            int blk = B.U16(amaxD, at), first = B.U16(amaxD, at + 2);
            uint srcPaint = amax.K.Refs[B.U32(amaxD, amax.K.Objects[bi].Blocks[blk].Offset + 4 * first)].Uid;
            Db veh = new(vehD);
            int pi = veh.ObjOfUid(srcPaint), pt = veh.K.Objects[pi].Blocks[0].Type, cOff = veh.Field(pt, "colours").Off;
            var cols = car.Arr("paint")!.Select(h => { var s = (string)h!; return new[] { Convert.ToInt32(s.Substring(1, 2), 16) / 255.0, Convert.ToInt32(s.Substring(3, 2), 16) / 255.0, Convert.ToInt32(s.Substring(5, 2), 16) / 255.0 }; }).ToList();
            if (car.Truthy("body_glb")) cols = new() { new[] { 1.0, 1.0, 1.0 }, new[] { 1.0, 1.0, 1.0 } };
            vehD = KslfWrite.CloneObjectAny(vehD, pi, null, paintUid, (data, pool, blocks) =>
            {
                int a0 = blocks[0] + cOff;
                int cb = B.U16(data.A, a0), cf = B.U16(data.A, a0 + 2), cc = (int)B.U32(data.A, a0 + 4);
                for (int j = 0; j < cc; j++) for (int q = 0; q < 3; q++) data.Put(blocks[cb] + 12 * (cf + j) + 4 * q, (float)cols[Math.Min(j, 1)][q]);
                OwnName(data, pool, blocks, name + "_paint");
            });
            uint glassSet = glbBody && CarModel.ClearGlass(car) ? ClearGlassSet(ref vehD, i, log) : 0;
            amaxD = KslfWrite.InsertUid(amaxD, dynUid, "RP_CarDynamics", out _);
            amaxD = KslfWrite.InsertUid(amaxD, paintUid, "VEH_PaintJob", out _);
            if (glassSet != 0) amaxD = KslfWrite.InsertUid(amaxD, glassSet, "VEH_MaterialDefaults", out _);
            amax = new Db(amaxD);
            bi = amax.Cars()[bas];
            int wm = amax.ObjOfUid(amax.RefUid(amax.Cars().GetValueOrDefault(wheels, bi), "wheelModel"));
            int fnOff = amax.Field(amax.K.Objects[wm].Blocks[0].Type, "fileName").Off;
            amaxD = KslfWrite.CloneObjectAny(amaxD, wm, null, wheelUid, (data, pool, blocks) => { data.Put(blocks[0] + fnOff, (uint)pool.Len); pool.AddSz(name); });
            // an own logo: a copy of the maker record (of the car it replaces, else of the body car) naming the car's logo picture
            uint manUid = LogoUid0 + (uint)i;
            bool logo = LogoFile(car) != null;
            if (logo)
            {
                amax = new Db(amaxD);
                int of = amax.Cars()[replacing ? replaceOf! : bas];
                int mo = amax.ObjOfUid(amax.RefUid(of, "Manufacturer"));
                int texOff = amax.Field(amax.K.Objects[mo].Blocks[0].Type, "UiTexture").Off;
                amaxD = KslfWrite.CloneObjectAny(amaxD, mo, name + "_logo", manUid, (data, pool, blocks) => { data.Put(blocks[0] + texOff, (uint)pool.Len); pool.AddSz(CarLogo.TexName(i)); });
                log($"   own logo {car.Str("logo")} (instead of the {amax.Name(mo)} logo)");
            }
            amax = new Db(amaxD);
            bi = amax.Cars()[bas];
            int wi = amax.Cars().GetValueOrDefault(wheels, bi);
            int dynRow = amax.RowOfUid(dynUid), paintRow = amax.RowOfUid(paintUid);   // (read again below when an own sound adds a row)
            var sets = new Dictionary<string, double>(carVals);
            foreach (var f in new[] { "wheelScaleFront", "wheelScaleRear", "wheelDepthFront", "wheelDepthRear" }) sets[f] = amax.Get(wi, f);
            sets["wheelModel"] = amax.RowOfUid(wheelUid);
            if (logo) sets["Manufacturer"] = amax.RowOfUid(manUid);
            if (glassSet != 0) sets["baseVehicle.materialOverrides"] = amax.RowOfUid(glassSet);
            var s2 = car.Obj("stats")!;
            long mph = CarsData.TopSpeed((int)car.Int("class"), s2.Int("speed"));
            sets["LocString"] = Loc0 + (uint)i; sets["dynamics"] = dynRow; sets["UiSpeed"] = s2.Int("speed"); sets["UiAcceleration"] = s2.Int("acceleration"); sets["UiDrift"] = s2.Int("drift");
            sets["UiGrip"] = s2.Int("grip"); sets["UiStrength"] = s2.Int("strength"); sets["UiMaxSpeed"] = mph; sets["GameplayTopSpeed"] = mph; sets["BossAbilitySlot"] = 0; sets["class"] = car.Int("class");
            if (car.Truthy("body_glb")) sets["DriverOffset.vy"] = amax.Get(amax.Cars()[CarGlb.Container], "DriverOffset.vy") - DriverDown;
            // the sound: another game car's engine + wheel sound fields, copied byte for byte (references, numbers, flags)
            string? sound = car.Truthy("sound") ? car.Str("sound") : null;
            int si = sound != null && sound != bas && amax.Cars().TryGetValue(sound, out int sIdx) ? sIdx : -1;
            var soundBytes = new List<(int Off, byte[] Bytes)>();
            if (si >= 0)
                foreach (var f in CarSound.Fields)
                {
                    try
                    {
                        var (off, tn) = amax.Field(vt, f);
                        int size = tn is "bool" or "u8" ? 1 : tn is "u16" or "i16" ? 2 : 4, a = amax.K.Objects[si].Blocks[0].Offset + off;
                        if (a + size <= amax.Data.Length) soundBytes.Add((off, amax.Data.AsSpan(a, size).ToArray()));
                    }
                    catch (InvalidOperationException) { }     // a field this game's record does not have
                }
            // its own engine sound: copies of the engine's sound entries (the root pack gets the bank + .dat copies), the car points at it
            int ownRow = -1, aiRow = -1, wheelRow = -1, aiWheelRow = -1;
            if (HasOwnSounds(car))
            {
                try
                {
                    rootSrc ??= new Pak(Paths.RootOriginal);
                    var kinds = ExtraSoundFiles(car).Keys.Select(k => OwnSound.Kinds.First(q => q.Key == k).Bank).ToHashSet();
                    var plans = OwnSound.MakePlans(EngineCar(car), i, rootSrc, kinds.Contains("common"));
                    var wplans = kinds.Contains("wheel") ? OwnSound.MakeWheelPlans(EngineCar(car), i, rootSrc) : new();
                    int baseRow = (int)amax.Get(bi, "vehicleAudioEntity");
                    string ptype = amax.K.Types[amax.K.Refs[baseRow].Type].Name;
                    foreach (var plan in plans.Concat(wplans))
                    {
                        (audD, entD) = OwnSound.AddEntries(plan, audD ?? Rd("audio.bin"), entD ?? Rd("audioentities.bin"));
                        amaxD = KslfWrite.InsertUid(amaxD, plan.NewProxy, ptype, out _);
                    }
                    amax = new Db(amaxD); bi = amax.Cars()[bas];
                    ownRow = amax.RowOfUid(plans[0].NewProxy);
                    // the AI version: its own copy when the engine has one, else the full copy
                    aiRow = plans.Count > 1 ? amax.RowOfUid(plans[1].NewProxy) : ownRow;
                    if (wplans.Count > 0) { wheelRow = amax.RowOfUid(wplans[0].NewProxy); aiWheelRow = wplans.Count > 1 ? amax.RowOfUid(wplans[1].NewProxy) : wheelRow; }
                    // the rows of the copied sound fields may have moved with the new row: read them again
                    if (si >= 0)
                    {
                        si = amax.Cars()[sound!]; soundBytes.Clear();
                        foreach (var f in CarSound.Fields)
                        {
                            try
                            {
                                var (off, tn) = amax.Field(vt, f);
                                int size = tn is "bool" or "u8" ? 1 : tn is "u16" or "i16" ? 2 : 4, a = amax.K.Objects[si].Blocks[0].Offset + off;
                                if (a + size <= amax.Data.Length) soundBytes.Add((off, amax.Data.AsSpan(a, size).ToArray()));
                            }
                            catch (InvalidOperationException) { }
                        }
                    }
                    sets["dynamics"] = amax.RowOfUid(dynUid); sets["wheelModel"] = amax.RowOfUid(wheelUid); paintRow = amax.RowOfUid(paintUid); if (logo) sets["Manufacturer"] = amax.RowOfUid(manUid);
                    if (glassSet != 0) sets["baseVehicle.materialOverrides"] = amax.RowOfUid(glassSet);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException) { log($"   own sound not used ({e.Message})"); ownRow = aiRow = wheelRow = aiWheelRow = -1; }
            }
            int iconOff = amax.Field(vt, "UiIconName").Off;
            var amaxNow = amax;
            // a car replacer: the game car keeps its record (name, handling, stats, class, paints - the same online), and gets this
            // car's wheels and sound here; its body comes with the root pack (ModelFiles). No new car is added.
            if (Replaces(car) is { } target)
            {
                if (!amax.Cars().TryGetValue(target, out int ti)) { log($"custom car {car.Str("name")}: the car to replace, {target}, was not found - skipped"); continue; }
                if (!replaced.Add(target)) log($"   two of your cars replace the {target}: the last one wins");
                var keep = new[] { "dynamics", "wheelScaleFront", "wheelScaleRear", "wheelDepthFront", "wheelDepthRear", "DriverOffset.vy", "Manufacturer", "baseVehicle.materialOverrides" };
                amaxD = KslfWrite.EditObject(amaxD, ti, ctx =>
                {
                    foreach (var f in keep) if (sets.TryGetValue(f, out double v)) amaxNow.Set(ctx.Data, ctx.Blocks[0], vt, f, v);
                    foreach (var (off, bytes) in soundBytes) for (int k = 0; k < bytes.Length; k++) ctx.Data[ctx.Blocks[0] + off + k] = bytes[k];
                    if (ownRow >= 0) { amaxNow.Set(ctx.Data, ctx.Blocks[0], vt, "vehicleAudioEntity", ownRow); amaxNow.Set(ctx.Data, ctx.Blocks[0], vt, "aiVehicleAudioEntity", aiRow); }
                    if (wheelRow >= 0) { amaxNow.Set(ctx.Data, ctx.Blocks[0], vt, "dryWheelAudioEntity", wheelRow); amaxNow.Set(ctx.Data, ctx.Blocks[0], vt, "aiDryWheelAudioEntity", aiWheelRow); }
                });
                if (CarExhaust.ToWrite(car, CarExhaust.Game(target).Count) is { Count: > 0 } exR)
                {
                    try { amaxD = CarExhaust.Write(amaxD, new Db(amaxD).Cars()[target], exR); log($"   own exhaust flames: {exR.Count} pipe(s)"); }
                    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException) { log($"   exhaust flames not moved ({e.Message})"); }
                }
                log($"custom car {car.Str("name")} replaces the {target}: body {bas}, wheels {wheels} (where this car has them), sound {(ownRow >= 0 ? "own" : si >= 0 ? sound : "the " + target + "'s")}; the rest of the handling and the stats stay the {target}'s");
                continue;
            }
            amaxD = KslfWrite.CloneObjectAny(amaxD, bi, name, carUid, (data, pool, blocks) =>
            {
                foreach (var (f, v) in sets) amaxNow.Set(data, blocks[0], vt, f, v);
                foreach (var (off, bytes) in soundBytes) for (int k = 0; k < bytes.Length; k++) data[blocks[0] + off + k] = bytes[k];
                if (ownRow >= 0) { amaxNow.Set(data, blocks[0], vt, "vehicleAudioEntity", ownRow); amaxNow.Set(data, blocks[0], vt, "aiVehicleAudioEntity", aiRow); }
                if (wheelRow >= 0) { amaxNow.Set(data, blocks[0], vt, "dryWheelAudioEntity", wheelRow); amaxNow.Set(data, blocks[0], vt, "aiDryWheelAudioEntity", aiWheelRow); }
                data.Put(blocks[0] + iconOff, (uint)pool.Len); pool.AddSz(name);
                int a0 = blocks[0] + pOff, pb = B.U16(data.A, a0), pf = B.U16(data.A, a0 + 2);
                data.Put(blocks[pb] + 4 * pf, (uint)paintRow);
                data.Put(a0 + 4, 1u);
            });
            added.Add(carUid);
            if (CarExhaust.ToWrite(car, CarExhaust.Game(bas).Count) is { Count: > 0 } ex)
            {
                try { amaxD = CarExhaust.Write(amaxD, new Db(amaxD).ObjOfUid(carUid), ex); log($"   own exhaust flames: {ex.Count} pipe(s)"); }
                catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException) { log($"   exhaust flames not moved ({e.Message})"); }
            }
            log($"custom car {car.Str("name")} ({name}, class {"ABCD"[(int)car.Int("class")]}, body {bas}, wheels {wheels}, sound {(si >= 0 ? sound : bas)}): {mph} mph");
            if (sound != null && sound != bas && si < 0) log($"   the sound car {sound} was not found - the body car's sound stays");
            if (car.Truthy("body_glb")) log($"   (imported body {car.Str("body_glb")} on the wheels of {bas} - written with the root pack)");
        }
        rootSrc?.Dispose();
        if (audD != null) files["gamedata/xt/audio.bin"] = audD;
        if (entD != null) files["gamedata/xt/audioentities.bin"] = entD;
        files["gamedata/xt/racingphysics.bin"] = rpD;
        files["gamedata/xt/vehicles.bin"] = vehD;
        files["gamedata/xt/amax.bin"] = amaxD;
        files["gamedata/xt/multiplayer.bin"] = CarLists(mpD, added);
        files["gamedata/xt/loc/vehicles.loc"] = Loc.Write(vloc, cars.Select((c, i) => (Loc0 + (uint)i, $"VEH_BMB_{i}", c.Str("name")!)), null);
    }

    static byte[] CarLists(byte[] mpD, List<uint> uids)
    {
        foreach (uint u in uids) mpD = KslfWrite.InsertUid(mpD, u, "AmaxVehicle", out _);
        var mp = new Db(mpD);
        int mi = Enumerable.Range(0, mp.K.Objects.Count).First(i => mp.TName(i) == "MultiplayerConfig");
        int mt = mp.K.Objects[mi].Blocks[0].Type;
        int vehOff = mp.Field(mt, "vehicles").Off, lvlOff = mp.Field(mt, "leveling_data").Off, lvlT = mp.K.TypeIndex("MultiplayerLevel"), unlOff = mp.Field(lvlT, "VehicleUnlocks").Off;
        return KslfWrite.EditObject(mpD, mi, c =>
        {
            var refs = new ByteBuf();
            foreach (uint u in uids) refs.AddU32((uint)c.AddUid(u, "AmaxVehicle"));
            void Extend(Func<int> where)
            {
                int at = where();
                int blk = B.U16(c.Data.A, at), first = B.U16(c.Data.A, at + 2), cnt = (int)B.U32(c.Data.A, at + 4);
                byte[] old;
                if (blk == 0xFFFF)
                {
                    blk = c.Rows.FindIndex(r => c.Types[r[0]].Name == "AmaxVehicle" && r[2] == 2);
                    old = Array.Empty<byte>();
                }
                else old = c.Data.A.AsSpan(c.Blocks[blk] + 4 * first, 4 * cnt).ToArray();
                int end = c.Rows[blk][1];
                c.Insert(blk, end, B.Concat(old, refs.ToArray()));
                int w = where();
                c.Data.Put(w, (ushort)blk); c.Data.Put(w + 2, (ushort)end); c.Data.Put(w + 4, (uint)(cnt + uids.Count));
            }
            Extend(() => vehOff);
            Extend(() =>
            {
                int lblk = B.U16(c.Data.A, lvlOff), lfirst = B.U16(c.Data.A, lvlOff + 2);
                return c.Blocks[lblk] + lfirst * mp.K.Types[lvlT].Size + unlOff;
            });
        });
    }

    /// the rim's face of a game car: its wheels file's triangles at the size the game draws it (x y z, 9 floats a triangle), and how deep it sits
    public static (float[] Tri, double Depth) WheelFace(Pak root, string car)
    {
        var amax = AmaxOrig();
        var d = CarModel.ModelBytes(root, $"models\\vehicles\\wheels\\{WheelFile(amax, car).ToLowerInvariant()}.model");
        var M = CpModel.Read(d);
        var (rows, _) = CarGlb.Rooms(M);
        int wi = amax.Cars()[car]; double k = amax.Get(wi, "wheelScaleFront");
        var o = new List<float>();
        foreach (var r in rows)
        {
            if (r.S < 0 || r.El.Contains("blur", StringComparison.OrdinalIgnoreCase) || M.VStreams[r.S].Def[0].Type != 9 || r.M.Data1.Count == 0) continue;
            var vs = M.VStreams[r.S]; var fs = M.FStreams[r.M.FaceStream]; var d1 = r.M.Data1[0]; int bas = r.M.VOffset[0] / vs.Bytes;
            int Idx(int i) => B.U16(d, fs.Start + 2 * (d1.FaceOff + i));
            for (int i = 0; i + 2 < d1.FaceCount; i++)
            {
                int a = Idx(i), b = Idx(i + 1), c = Idx(i + 2);
                if (a == b || b == c || a == c) continue;
                foreach (int v in new[] { a, b, c }) { int at = vs.Start + vs.Bytes * (bas + v); for (int q = 0; q < 3; q++) o.Add((float)(k * (double)BitConverter.ToHalf(d, at + 2 * q))); }
            }
        }
        return (o.ToArray(), amax.Get(wi, "wheelDepthFront"));
    }

    public static string WheelFileIn(byte[] amax, string car) => WheelFile(new Db(amax), car);
    static string TryWheelFile(Db amax, string car) { try { return WheelFile(amax, car); } catch (Exception) { return ""; } }
    static string WheelFile(Db amax, string car)
    {
        int wi = amax.ObjOfUid(amax.RefUid(amax.Cars()[car], "wheelModel"));
        var o = amax.K.Objects[wi];
        int off = amax.Field(o.Blocks[0].Type, "fileName").Off;
        return B.Sz(o.Pool, (int)B.U32(amax.Data, o.Blocks[0].Offset + off));
    }

    static Db AmaxOrig() => new(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));

    public static IEnumerable<KeyValuePair<string, string>> ModelAliases(JObj wld)
    {
        var cars = WorldCars(wld);
        var o = new List<KeyValuePair<string, string>>();
        if (cars.Count == 0) return o;
        var amax = AmaxOrig(); var names = InternalNames(cars);
        const string folder = "\\models\\vehicles\\";
        for (int i = 0; i < cars.Count; i++)
        {
            var car = cars[i]; string name = names[i].ToLowerInvariant();
            o.Add(new(folder + name + ".model", folder + car.Str("base")!.ToLowerInvariant() + ".model"));
            o.Add(new(folder + "wheels\\" + name + ".model", folder + "wheels\\" + WheelFile(amax, car.Truthy("wheels") ? car.Str("wheels")! : car.Str("base")!).ToLowerInvariant() + ".model"));
        }
        return o;
    }

    /// the game car a custom car replaces (its "replace"), or null for a new car
    public static string? Replaces(JObj car) => car.Truthy("replace") ? car.Str("replace") : null;

    public static IEnumerable<KeyValuePair<string, byte[]>> ModelFiles(JObj wld, Pak src, Action<string> log)
    {
        var cars = WorldCars(wld);
        var o = new List<KeyValuePair<string, byte[]>>();
        if (cars.Count == 0) return o;
        var names = InternalNames(cars); var amax = AmaxOrig();
        for (int i = 0; i < cars.Count; i++)
        {
            var car = cars[i]; string name = names[i].ToLowerInvariant();
            string bas = car.Str("base")!;
            string wheelCar = car.Truthy("wheels") ? car.Str("wheels")! : bas, modelCar = car.Truthy("body_glb") ? CarGlb.Container : bas;
            bool other = !wheelCar.Equals(modelCar, StringComparison.OrdinalIgnoreCase) && (car.Truthy("wheels") || car.Truthy("body_glb"));
            byte[]? body = car.Truthy("body_glb") ? CarGlb.GlbModel(src, car, Path.Combine(Paths.Tracks, "cars", car.Str("body_glb")!), log) : other ? (byte[])CarModel.ModelBytes(src, CarModel.ModelPath(bas)).Clone() : null;
            if (other) CarGlb.SwapWheels(body!, CarModel.ModelBytes(src, CarModel.ModelPath(modelCar)), src, wheelCar, log);
            if (Replaces(car) is { } target)
            {
                // a replacer: the game car's own model file gets this body (the same file name, so its record is not changed)
                if (body == null && !target.Equals(bas, StringComparison.OrdinalIgnoreCase)) body = (byte[])CarModel.ModelBytes(src, CarModel.ModelPath(bas)).Clone();
                if (body != null) o.Add(new("\\" + CarModel.ModelPath(target), body));
                // its own wheel file too (when no other car uses it): whatever loads the wheels by that name gets these wheels
                string tw = WheelFile(amax, target), cw = WheelFile(amax, car.Truthy("wheels") ? car.Str("wheels")! : bas);
                // (the game shows a car's wheels from its own file; a Legend / boss version that shares the file gets them too)
                var sharing = amax.Cars().Keys.Where(k => !k.Equals(target, StringComparison.OrdinalIgnoreCase) && TryWheelFile(amax, k).Equals(tw, StringComparison.OrdinalIgnoreCase)).ToList();
                if (sharing.Count > 0) log($"   the {target}'s wheel file is shared with {string.Join(", ", sharing)}: they get these wheels too");
                if (!tw.Equals(cw, StringComparison.OrdinalIgnoreCase))
                    o.Add(new($"\\models\\vehicles\\wheels\\{tw.ToLowerInvariant()}.model", CarModel.ModelBytes(src, $"models\\vehicles\\wheels\\{cw.ToLowerInvariant()}.model")));
            }
            else if (body != null) o.Add(new("\\models\\vehicles\\" + name + ".model", body));
            string w = WheelFile(amax, car.Truthy("wheels") ? car.Str("wheels")! : car.Str("base")!).ToLowerInvariant();
            o.Add(new("\\models\\vehicles\\wheels\\" + name + ".model", CarModel.ModelBytes(src, $"models\\vehicles\\wheels\\{w}.model")));
        }
        return o;
    }

    /// the game car whose engine a custom car's sound is made from (its sound car, else its body car of its class)
    static string EngineCar(JObj car)
    {
        var picks = CarsData.ClassCars((int)car.Int("class")).Select(x => x.Name).ToList();
        string bas = picks.Contains(car.Str("base")!) ? car.Str("base")! : picks[0];
        return car.Truthy("sound") ? car.Str("sound")! : bas;
    }

    static string? OwnSoundFile(JObj car) => car.Truthy("sound_own") && File.Exists(OwnSound.FilePath(car.Str("sound_own")!)) ? OwnSound.FilePath(car.Str("sound_own")!) : null;

    /// the other sound kinds this car changes whose file is there (kind key -> path)
    public static Dictionary<string, string> ExtraSoundFiles(JObj car)
    {
        var o = new Dictionary<string, string>();
        var sd = car.Obj("sounds");
        if (sd == null) return o;
        foreach (var k in OwnSound.Kinds)
            if (sd.Truthy(k.Key) && File.Exists(OwnSound.FilePath(sd.Str(k.Key)!))) o[k.Key] = OwnSound.FilePath(sd.Str(k.Key)!);
        return o;
    }

    static bool HasOwnSounds(JObj car) => OwnSoundFile(car) != null || ExtraSoundFiles(car).Count > 0;

    /// all of a car's own sounds read (a file that can't be read is left out, with a line in the log)
    static OwnSound.Sounds ReadOwnSounds(JObj car, Action<string>? log)
    {
        var s = new OwnSound.Sounds { Rpm = car.Num("sound_rpm", OwnSound.DefaultRpm) };
        if (OwnSoundFile(car) is { } f) s.Engine = OwnSound.ReadWav(f);
        foreach (var (k, path) in ExtraSoundFiles(car))
            try { s.Extra[k] = OwnSound.ReadWav(path); }
            catch (Exception e) when (e is IOException or InvalidDataException) { log?.Invoke($"custom car {car.Str("name")}: {Path.GetFileName(path)} not used ({e.Message})"); }
        return s;
    }

    /// the new names of the own-sound files, each first made as a copy of the game file it is made from (filled in later)
    public static IEnumerable<KeyValuePair<string, string>> SoundAliases(JObj wld, Pak src)
    {
        var cars = WorldCars(wld);
        var o = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < cars.Count; i++)
        {
            if (!HasOwnSounds(cars[i])) continue;
            try
            {
                var kinds = ExtraSoundFiles(cars[i]).Keys.Select(k => OwnSound.Kinds.First(q => q.Key == k).Bank).ToHashSet();
                bool common = kinds.Contains("common"), first = true;
                var plans = OwnSound.MakePlans(EngineCar(cars[i]), i, src, common);
                if (kinds.Contains("wheel")) plans.AddRange(OwnSound.MakeWheelPlans(EngineCar(cars[i]), i, src));
                foreach (var plan in plans)
                {
                    o.Add(new(@"\audio\" + plan.NewBank.ToLowerInvariant() + ".baf", @"\audio\" + plan.Bank.ToLowerInvariant() + ".baf"));
                    if (common && first) o.Add(new(@"\audio\" + plan.NewCommon.ToLowerInvariant() + ".baf", @"\audio\" + OwnSound.CommonBank.ToLowerInvariant() + ".baf"));
                    first = false;
                    foreach (var d in plan.Dats) o.Add(new(@"\audio\dat\" + d.NewFile.ToLowerInvariant(), @"\audio\dat\" + d.OldFile.ToLowerInvariant()));
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException) { }
        }
        // a file shared by the full and the AI copy (e.g. the gear sounds' .dat) only once
        return o.GroupBy(q => q.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    /// the root pack's files of the custom cars with their own engine sound: a bank copy with the player's sound + its .dat copies
    public static IEnumerable<KeyValuePair<string, byte[]>> SoundFiles(JObj wld, Pak src, Action<string> log)
    {
        var cars = WorldCars(wld);
        var o = new List<KeyValuePair<string, byte[]>>();
        string? list = null;            // the game's bank list, with the copies' lines added
        for (int i = 0; i < cars.Count; i++)
        {
            var car = cars[i];
            if (car.Truthy("sound_own") && OwnSoundFile(car) == null) log($"custom car {car.Str("name")}: its sound file {car.Str("sound_own")} is missing - the game engine sound stays");
            if (!HasOwnSounds(car)) continue;
            try
            {
                var snd = ReadOwnSounds(car, log);
                if (!snd.Any) continue;
                var plans = OwnSound.MakePlans(EngineCar(car), i, src, snd.Common);
                if (snd.Wheel) plans.AddRange(OwnSound.MakeWheelPlans(EngineCar(car), i, src));
                foreach (var plan in plans)
                {
                    var made = OwnSound.RootFiles(plan, src, snd).ToList();
                    list = OwnSound.AddToBankList(list ?? B.Latin1.GetString(src.Read(OwnSound.BankList) ?? throw new FileNotFoundException(OwnSound.BankList)), plan, snd.Common);
                    o.AddRange(made);
                    string what = string.Join(", ", (snd.Engine != null ? new[] { "engine " + car.Str("sound_own") } : Array.Empty<string>()).Concat(snd.Extra.Keys.Select(k => OwnSound.Kinds.First(q => q.Key == k).Label.ToLowerInvariant())));
                    log($"custom car {car.Str("name")}: own sounds ({what}) on the {plan.Engine} engine (bank {plan.NewBank}{(snd.Common ? ", " + plan.NewCommon : "")})");
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException) { log($"custom car {car.Str("name")}: own sound not made ({e.Message}) - the game sound stays"); }
        }
        if (list != null) o.Add(new(OwnSound.BankList, B.Latin1.GetBytes(list)));
        return o;
    }

    public static IEnumerable<KeyValuePair<string, byte[]>> IconFiles(JObj wld, Pak src, Action<string> log)
    {
        var cars = WorldCars(wld);
        return cars.Count == 0 ? Array.Empty<KeyValuePair<string, byte[]>>() : CarIcon.Files(src, cars, InternalNames(cars), log);
    }

    public static byte[] MenuPictures(byte[] frontend, JObj wld)
    {
        var cars = WorldCars(wld); var names = InternalNames(cars);
        for (int i = 0; i < cars.Count; i++) frontend = Bpr.AliasPicture(frontend, names[i], cars[i].Str("icon")!);
        for (int i = 0; i < cars.Count; i++)
            if (LogoFile(cars[i]) is { } logo)
            {
                try { frontend = Bpr.SetPicture(frontend, CarLogo.TexName(i), CarLogo.Picture(logo), 5, 2, CarLogo.W, CarLogo.H); }
                catch (Exception e) when (e is ArgumentException or IOException or OutOfMemoryException) { }   // a broken picture: AddCars already left the maker as it was
            }
        return frontend;
    }

    /// the car's own logo picture, when it has one and the file is there
    static string? LogoFile(JObj car)
    {
        if (!car.Truthy("logo")) return null;
        string p = CarLogo.FilePath(car.Str("logo")!);
        if (!File.Exists(p)) return null;
        try { using var b = new Bitmap(p); return p; }
        catch (Exception e) when (e is ArgumentException or IOException or OutOfMemoryException) { return null; }
    }
}

/// a custom car's sound: the engine + wheel sound of another game car (the sound fields of its AmaxVehicle record)
public static class CarSound
{
    public static readonly string[] Fields =
    {
        "vehicleAudioEntity", "aiVehicleAudioEntity", "aiVehicleAudioEntity_PS3", "aiBossBattleVehicleAudioEntity_PS3",
        "dryWheelAudioEntity", "wetWheelAudioEntity", "aiDryWheelAudioEntity", "aiWetWheelAudioEntity",
        "AudioRPMLimit", "AudioGarageID", "AudioForcedInduction", "AudioEngineType", "AudioExhaustType",
    };

    /// (look only) the sound fields of some cars: type, value, and for a reference what it points at
    public static List<string> Dump(IEnumerable<string> cars, byte[]? amax = null)
    {
        var o = new List<string>();
        var db = new Db(amax ?? File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
        var all = db.Cars();
        cars = cars.SelectMany(c => c.EndsWith("*") ? all.Keys.Where(k => k.StartsWith(c[..^1], StringComparison.Ordinal)) : new[] { c }).ToList();
        foreach (var c in cars)
        {
            if (!all.TryGetValue(c, out int i)) { o.Add(c + ": not found"); continue; }
            int t = db.K.Objects[i].Blocks[0].Type;
            o.Add(c + ":");
            foreach (var f in Fields.Concat(new[] { "sharedAudioEntity", "aiSharedAudioEntity", "dynamics", "wheelModel" }))
            {
                try
                {
                    var (off, tn) = db.Field(t, f);
                    double v = db.Get(i, f);
                    string extra = "";
                    if (tn != "f32" && tn != "i32" && tn != "u8" && tn != "u16" && tn != "i16" && tn != "u32" && v >= 0 && v < db.K.Refs.Length)
                    {
                        var r = db.K.Refs[(int)v];
                        extra = r.Obj >= 0 && r.Obj < db.K.Objects.Count ? $" -> {db.TName(r.Obj)} {db.Name(r.Obj)}" : $" -> uid {r.Uid:X8}";
                    }
                    o.Add($"   {f} [{tn}] = {v}{extra}");
                }
                catch (Exception e) { o.Add($"   {f}: {e.GetType().Name}"); }
            }
        }
        return o;
    }
}

/// the game's sound banks (\audio\NAME.baf): a 'BANK' header, one 'WAVE' header per sound, then the sound data.
/// The car sounds are 4-bit ADPCM (VAG style: 33-byte blocks = a filter/scale byte + 64 samples), 48 kHz mono.
public sealed class SoundBank
{
    public sealed class Wave { public string Name = ""; public int Off, Size, Rate, Samples, Format, Channels = 1; }
    public string Name = "";
    public readonly List<Wave> Waves = new();
    public byte[] Data = Array.Empty<byte>();

    public static SoundBank Read(byte[] d)
    {
        if (d.Length < 52 || B.Latin1.GetString(d, 0, 4) != "BANK") throw new InvalidDataException("not a sound bank");
        var o = new SoundBank { Data = d, Name = B.Sz(d, 18) };
        int at = (int)B.U32(d, 4), n = (int)B.U32(d, 12);
        for (int i = 0; i < n && at + 12 <= d.Length; i++)
        {
            if (B.Latin1.GetString(d, at, 4) != "WAVE") break;
            int size = (int)B.U32(d, at + 4);
            var w = new Wave { Format = (int)B.U32(d, at + 8), Name = B.Sz(d, at + 12), Off = (int)B.U32(d, at + 44), Size = (int)B.U32(d, at + 48) };
            if (w.Format == 7) { w.Rate = (int)B.U32(d, at + 64); w.Samples = (int)B.U32(d, at + 68); w.Channels = Math.Max(1, (int)d[at + 75]); }   // +60 = ADPCM header size, +64 rate, +68 samples, +75 channels
            o.Waves.Add(w);
            at += size;
        }
        return o;
    }

    static readonly float[,] F = { { 0, 0 }, { 60 / 64f, 0 }, { 115 / 64f, -52 / 64f }, { 98 / 64f, -55 / 64f }, { 122 / 64f, -60 / 64f } };

    /// a sound as floats (-1..1); null if its format is not the 4-bit ADPCM
    public float[]? Decode(Wave w)
    {
        if (w.Format != 7 || w.Off + w.Size > Data.Length) return null;
        var o = new float[w.Samples]; int k = 0; float p1 = 0, p2 = 0;
        for (int b = w.Off; b + 33 <= w.Off + w.Size && k < o.Length; b += 33 * w.Channels)
        {
            int h = Data[b], fi = Math.Min(h >> 4, 4); float sc = MathF.Pow(2, -(15 + (h & 15))) * 4096f, a = F[fi, 0], c = F[fi, 1];
            for (int i = 1; i <= 32 && k < o.Length; i++)
            {
                int by = Data[b + i];
                foreach (int nib in new[] { by & 15, by >> 4 })
                {
                    float s = (nib >= 8 ? nib - 16 : nib) * sc + p1 * a + p2 * c;
                    p2 = p1; p1 = s;
                    if (k < o.Length) o[k++] = s;
                }
            }
        }
        return o;
    }
}

public static partial class CarSoundPreview
{
    static Dictionary<string, string>? _engine;
    static readonly object _lock = new();

    /// every game car -> its engine sound's name (Dodge_V10, BMW_i6 ...), read from amax / audio / audioentities
    public static Dictionary<string, string> Engines()
    {
        lock (_lock)
        {
            if (_engine != null) return _engine;
            var o = new Dictionary<string, string>();
            string xt = Path.Combine(Paths.Unpacked, "xt");
            var amax = new Db(File.ReadAllBytes(Path.Combine(xt, "amax.bin")));
            var au = new Db(File.ReadAllBytes(Path.Combine(xt, "audio.bin")));
            var en = new Db(File.ReadAllBytes(Path.Combine(xt, "audioentities.bin")));
            foreach (var (car, i) in amax.Cars())
            {
                try
                {
                    uint proxy = amax.RefUid(i, "vehicleAudioEntity");
                    int pi = au.ObjOfUid(proxy);
                    uint def = au.RefUid(pi, "EntityDef");
                    int di = en.ObjOfUid(def);
                    var b = en.K.Objects[di].Blocks[0];
                    int off = en.Field(b.Type, "name").Off;
                    o[car] = B.Sz(en.K.Objects[di].Pool, (int)B.U32(en.Data, b.Offset + off));
                }
                catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException) { }
            }
            return _engine = o;
        }
    }

    /// the uid of a game car's sound entry (its vehicleAudioEntity)
    public static uint ProxyOf(string car, string field = "vehicleAudioEntity")
    {
        var amax = new Db(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
        return amax.RefUid(amax.Cars()[car], field);
    }

    static readonly Dictionary<string, string> _bank = new();

    /// the bank an engine sound plays from: its entity (audioentities.bin) points at audio objects (audio.bin), each names a .dat
    /// file (\audio\dat\), and the engine's .dat names its bank (\audio\NAME.baf) before its sounds
    public static string BankOf(string engine, Pak root)
    {
        lock (_bank)
        {
            if (_bank.TryGetValue(engine, out var hit)) return hit;
            string bank = engine;
            try
            {
                string xt = Path.Combine(Paths.Unpacked, "xt");
                var en = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audioentities.bin")));
                var au = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audio.bin")));
                Dictionary<string, object?>? def = null;
                foreach (var o in en.Objects)
                {
                    if (o.Blocks[0].Name != "AudioEntityDef") continue;
                    var top = en.Top(o);
                    if (top.TryGetValue("name", out var n) && n as string == engine) { def = top; break; }
                }
                var uids = new List<uint>();
                void Walk(object? v)
                {
                    switch (v)
                    {
                        case Dictionary<string, object?> d:
                            if (d.TryGetValue("type", out var t) && t as string == "AudioObject" && d.TryGetValue("uid", out var u) && u is string us) uids.Add(Convert.ToUInt32(us, 16));
                            foreach (var x in d.Values) Walk(x);
                            break;
                        case System.Collections.IEnumerable e when v is not string:
                            foreach (var x in e) Walk(x);
                            break;
                    }
                }
                if (def != null) Walk(def);
                var files = new List<string>();
                foreach (uint u in uids.Distinct())
                {
                    try { if (au.Top(au.Objects[au.ObjOf(u)]).TryGetValue("filename", out var f) && f is string fs) files.Add(fs); }
                    catch (Exception x) when (x is InvalidOperationException or ArgumentException or IndexOutOfRangeException) { }
                }
                foreach (var file in files.OrderBy(f => f.Contains("_engine", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                {
                    byte[]? dat;
                    try { dat = root.Read(@"\audio\dat\" + file.ToLowerInvariant()); }
                    catch (Exception x) when (x is IOException or ArgumentException) { continue; }
                    if (dat == null) continue;
                    string self = Path.GetFileNameWithoutExtension(file);
                    var m = System.Text.RegularExpressions.Regex.Matches(B.Latin1.GetString(dat), "[A-Za-z0-9_]{4,}").Select(q => q.Value)
                        .FirstOrDefault(q => !q.Equals(self, StringComparison.OrdinalIgnoreCase) && !q.Equals("Car_Common", StringComparison.OrdinalIgnoreCase) && root.Find(@"\audio\" + q.ToLowerInvariant() + ".baf") != null);
                    if (m != null) { bank = m; break; }
                }
            }
            catch (Exception x) when (x is IOException or InvalidOperationException or IndexOutOfRangeException or ArgumentException or FormatException) { }
            return _bank[engine] = bank;
        }
    }

    public static string Nice(string engine) => engine.Replace("_AI", "").Replace("_", " ").Replace(" BB", " big block").Replace(" i", " inline-").Replace("Race ", "race ");

    /// a short rev-up of a car's engine: every rev step of the bank (engine + exhaust) for a moment, cross-faded, then off the gas.
    /// A 16-bit 48 kHz mono WAV file.
    /// the rev preview of the player's own sound on a game car's engine (what the build will make)
    public static byte[] OwnRevWav(string engineCar, string wav, double rpm)
    {
        string engine = Engines().TryGetValue(engineCar, out var e) ? e : throw new InvalidDataException("no engine sound for " + engineCar);
        using var root = new Pak(Paths.RootOriginal);
        var bank = root.Read(@"\audio\" + BankOf(engine, root).ToLowerInvariant() + ".baf") ?? throw new FileNotFoundException(engine);
        return RevWav(SoundBank.Read(OwnSound.MakeBank(bank, "preview", OwnSound.ReadWav(wav), rpm)));
    }

    public static byte[] RevWav(string car)
    {
        string engine = Engines().TryGetValue(car, out var e) ? e : throw new InvalidDataException("no engine sound for " + car);
        SoundBank bank;
        using (var root = new Pak(Paths.RootOriginal)) bank = SoundBank.Read(root.Read(@"\audio\" + BankOf(engine, root).ToLowerInvariant() + ".baf") ?? throw new FileNotFoundException(engine));
        return RevWav(bank);
    }

    /// the rev preview of a bank (a game bank or a custom one)
    public static byte[] RevWav(SoundBank bank)
    {
        var rx = new System.Text.RegularExpressions.Regex(@"_(Engine|Exhaust)_(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var steps = bank.Waves.Select(w => (W: w, M: rx.Match(w.Name))).Where(q => q.M.Success)
            .GroupBy(q => int.Parse(q.M.Groups[2].Value)).OrderBy(g => g.Key).ToList();
        int rate = 48000, seg = rate * 45 / 100, fade = rate * 6 / 100;
        var outp = new List<float>();
        float[] Loop(float[] s, int n) { var r = new float[n]; for (int i = 0; i < n; i++) r[i] = s.Length > 0 ? s[i % s.Length] : 0; return r; }
        void Add(float[] s)
        {
            int start = Math.Max(0, outp.Count - fade);
            for (int i = 0; i < s.Length; i++)
            {
                if (start + i < outp.Count) { float t = (float)i / fade; outp[start + i] = outp[start + i] * (1 - t) + s[i] * t; }
                else outp.Add(s[i]);
            }
        }
        foreach (var g in steps)
        {
            var mix = new float[seg + fade];
            foreach (var (w, _) in g) { var s = bank.Decode(w); if (s == null) continue; var l = Loop(s, mix.Length); for (int i = 0; i < mix.Length; i++) mix[i] += l[i]; }
            Add(mix);
        }
        foreach (var w in bank.Waves.Where(w => w.Name.EndsWith("_decel", StringComparison.OrdinalIgnoreCase)))
            if (bank.Decode(w) is { } s) { var l = Loop(s, rate); for (int i = 0; i < l.Length; i++) l[i] *= 1 - (float)i / l.Length; Add(l); break; }
        float peak = Math.Max(1e-4f, outp.Count == 0 ? 0 : outp.Max(MathF.Abs)), k = 0.8f / peak;
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        bw.Write(B.Latin1.GetBytes("RIFF")); bw.Write(36 + outp.Count * 2); bw.Write(B.Latin1.GetBytes("WAVEfmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16); bw.Write(B.Latin1.GetBytes("data")); bw.Write(outp.Count * 2);
        foreach (var v in outp) bw.Write((short)Math.Clamp(v * k * 32767, -32768, 32767));
        bw.Flush();
        return ms.ToArray();
    }
}

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
    public const int Atlas = 1024, Swatch = 32, Edge = 4;
    public static string ModelPath(string name) => $"models\\vehicles\\{name.ToLowerInvariant()}.model";
    public static byte[] ModelBytes(Pak root, string model) => root.Read("\\" + model.ToLowerInvariant().TrimStart('\\')) ?? throw new FileNotFoundException(model);

    public static string Kind(string el)
    {
        el = el.ToLowerInvariant();
        if (el.Contains("tyre")) return "tyre";
        if (el.Contains("alloyrim") || el.Contains("brake") || el.Contains("hubcap") || el.Contains("rim")) return "rim";
        if (el.Contains("glass") || el.Contains("window") || el.Contains("screen")) return "glass";
        if (el.Contains("light") || el.Contains("lamp")) return "light";
        if (new[] { "interior", "steering", "gearstick", "signalstalk", "wiper", "mirror_centre", "exhaust", "grille", "driver" }.Any(el.Contains)) return "dark";
        return "paint";
    }

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

    public sealed class Body { public float[] Tri = null!, Uv = null!; public Pic? Image; }     // 9 floats / 6 floats per triangle

    public static Body ReadGlbBody(string path)
    {
        var g = ModelImport.ReadGlb(path);
        var (parts, _, _) = ModelImport.GameSpace(g);
        var trisL = new List<double[]>(); var uvsL = new List<double[]>();
        var looks = new List<(GlbMat? M, Pic? Img, double[] C)>();
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
            looks.Add(mat != null && mat.Image != null ? (mat, mat.Image, mat.Colour[..3]) : (mat, null, mat != null ? mat.Colour[..3] : new[] { 0.7, 0.7, 0.7 }));
            trisL.Add(t); uvsL.Add(uv);
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
        return new Body { Tri = tri, Uv = outUv, Image = image };
    }

    static (List<double[]>, Pic?) OneTexture(List<double[]> uvs, List<(GlbMat? M, Pic? Img, double[] C)> looks)
    {
        if (looks.Select(l => l.Img).Distinct().Count() == 1 && looks[0].Img != null) return (uvs, looks[0].Img);
        static string K(double[] c) => string.Join(",", c.Select(v => v.ToString("R")));
        var tiles = new List<(Pic I, double[] C)>();
        foreach (var (_, i, c) in looks)
            if (i != null && !tiles.Any(t => t.I == i && K(t.C) == K(c))) tiles.Add((i, c));
        var plain = looks.Where(l => l.Img == null).Select(l => l.C).GroupBy(K).Select(g => g.First()).OrderBy(c => c[0]).ThenBy(c => c[1]).ThenBy(c => c[2]).ToList();
        int perRow = Atlas / Swatch, strip = (plain.Count + perRow - 1) / perRow * Swatch;
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
        var o = new List<double[]>();
        for (int p = 0; p < uvs.Count; p++)
        {
            var uv = uvs[p]; var (_, img, c) = looks[p];
            var r = new double[uv.Length];
            if (img == null)
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
    internal static (List<float[]> All, List<float[]> Tyres) CarParts(Pak root, JObj car)
    {
        string bas = car.Str("base")!, wheels = car.Truthy("wheels") ? car.Str("wheels")! : bas;
        var body = ReadGameCar(root, bas);
        var whl = wheels != bas ? ReadGameCar(root, wheels) : null;
        var fit = car.Obj("glb_fit") ?? new JObj();
        var all = new List<float[]>(); var tyres = new List<float[]>();
        if (car.Truthy("body_glb")) all.Add(FitGlb(ReadGlbBody(Path.Combine(Paths.Tracks, "cars", car.Str("body_glb")!)), body, fit));
        else foreach (var (k, v) in body.Tris) if (!k.Contains('|') && !(whl != null && k == "rim") && (k.Split('|')[0] is not ("tyre"))) all.AddRange(v);
        var src = whl ?? body;
        foreach (var c in Corners)
        {
            if (!body.Centre.TryGetValue(c, out var at) || !src.Centre.TryGetValue(c, out var frm)) continue;
            double[] to = { at[0] + Math.Sign(at[0]) * fit.Num("track") / 2, at[1], at[2] + Math.Sign(at[2]) * fit.Num("wb") / 2 };
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
        return (pts, uvs, faces);
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
        var data = (byte[])CarModel.ModelBytes(root, CarModel.ModelPath(car.Str("base")!)).Clone();
        var M = CpModel.Read(data);
        var (meshes, rooms) = Rooms(M);
        int vroom = rooms.Sum(r => r.M.Data1[0].VCount), iroom = rooms.Sum(r => IndexRoom(r.M));
        var raw = CarModel.ReadGlbBody(glbPath);
        var fitted = CarModel.FitGlb(raw, CarModel.ReadGameCar(root, car.Str("base")!), car.Obj("glb_fit") ?? new JObj());
        var tri = fitted.Select(v => (double)v).ToArray(); var uv = raw.Uv.Select(v => (double)v).ToArray();
        var (pts, uvs, faces) = WeldUv(tri, uv);
        int placed = Place(data, M, meshes, rooms, pts, uvs, faces);
        log($"   imported body: {faces.Count} triangles, {placed} placed in {rooms.Count} trim parts (room {vroom} points, {iroom} indices)");
        if (placed < faces.Count)
            log($"   WARNING: the class's body car {car.Str("base")} has room for only part of this body - {faces.Count - placed} of its {faces.Count} triangles are missing (holes). Make the .glb smaller (Blender: Decimate) - the Custom car tab says to about how many triangles.");
        // wheel spots, only for the boxes
        var extra = new List<double[]>();
        var spots = new HashSet<(float, float, float)>();
        foreach (var e in M.AllElements)
            if (CarModel.Corner(e.Name) != null && (CarModel.Kind(e.Name) is "tyre" or "rim" || e.Name.ToLowerInvariant().StartsWith("e_wheel_"))) spots.Add((e.M[9], e.M[11], e.M[10]));
        foreach (var (x, fwd, up) in spots)
            foreach (var d in new[] { new[] { -.2, -.4, -.4 }, new[] { .2, .4, .4 } }) extra.Add(new[] { x + d[0], up + d[1], fwd + d[2] });
        HideLights(data, M, meshes, log);
        GrowBoxes(data, M, pts, extra, log);
        PutTexture(data, raw.Image, log);
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
            if (s < 0 || CarModel.Kind(el) != "light") continue;
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
        int head = M.VStreams.Min(v => v.Start), done = 0;
        var nw = Box6(BuriedBox);
        foreach (var e in M.AllElements)
        {
            if (CarModel.Kind(e.Name) != "light") continue;
            var old = Box6(e.Box);
            if (!old.AsSpan().SequenceEqual(nw)) done += Replace(d, old, nw, head);
        }
        log($"   the body car's lights hidden ({moved} points + {done} boxes put under the ground)");
    }

    static void GrowBoxes(byte[] d, ModelInfo M, double[] pts, List<double[]> extra, Action<string> log)
    {
        double[] lo = { 1e30, 1e30, 1e30 }, hi = { -1e30, -1e30, -1e30 };
        for (int i = 0; i < pts.Length; i++) { lo[i % 3] = Math.Min(lo[i % 3], pts[i]); hi[i % 3] = Math.Max(hi[i % 3], pts[i]); }
        foreach (var p in extra) for (int i = 0; i < 3; i++) { lo[i] = Math.Min(lo[i], p[i]); hi[i] = Math.Max(hi[i], p[i]); }
        for (int i = 0; i < 3; i++) { lo[i] -= 0.02; hi[i] += 0.02; }
        var boxes = M.AllElements.Where(e => Still.Contains(e.Name.ToLowerInvariant())).Select(e => e.Box).Concat(M.ModelBoxes);
        int done = 0;
        foreach (var old in boxes)
        {
            var nw = new[] { Math.Min(old[0], lo[0]), Math.Min(old[1], lo[1]), Math.Min(old[2], lo[2]), Math.Max(old[3], hi[0]), Math.Max(old[4], hi[1]), Math.Max(old[5], hi[2]) };
            if (old.SequenceEqual(nw) || old[0] > old[3]) continue;
            done += Replace(d, Box6(old), Box6(nw), d.Length);
        }
        log($"   boxes grown to hold the body: {done}");
    }

    static void PutTexture(byte[] data, Pic? img, Action<string> log)
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
                var enc = size == CpModel.TextureBytes(w, h, mips) ? CpModel.EncodeMips(px, w, h, mips) : Dxt.Dxt1Encode(px, w, h);
                if (enc.Length != size) { log($"   (picture {name} {w} x {h}: unexpected size, left as it is)"); continue; }
                enc.CopyTo(data, m.Index + m.Length + 20);
                sizes.Add(w);
            }
            log($"   the .glb's texture -> {name} ({string.Join(", ", sizes)})");
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

    static int Place(byte[] data, ModelInfo M, List<MeshRow> meshes, List<(MeshInfo M, int S)> rooms, double[] pts, double[] uvs, List<(int A, int B, int C)> faces)
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
                    data[at + a9] = l0; data[at + a9 + 1] = l1; data[at + a9 + 2] = l2; data[at + a9 + 3] = l3;
                }
                if (offs.TryGetValue((9, 5, -1), out int a5))
                {
                    B.Put(data, at + a5, (Half)uvs[2 * v]); B.Put(data, at + a5 + 2, (Half)uvs[2 * v + 1]); B.Put(data, at + a5 + 4, (Half)0.0); B.Put(data, at + a5 + 6, (Half)0.0);
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
    const uint Uid0 = 0xCA400000, Loc0 = 0x4D440000; const double DriverDown = 1.5;
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

    public static void AddCars(Dictionary<string, byte[]> files, JObj wld, Action<string> log)
    {
        var cars = WorldCars(wld);
        if (cars.Count == 0) return;
        byte[] Rd(string n) => files.TryGetValue("gamedata/xt/" + n, out var d) ? d : File.ReadAllBytes(Path.Combine(new[] { Paths.Unpacked, "xt" }.Concat(n.Split('/')).ToArray()));
        byte[] amaxD = Rd("amax.bin"), rpD = Rd("racingphysics.bin"), vehD = Rd("vehicles.bin"), mpD = Rd("multiplayer.bin"), vloc = Rd("loc/vehicles.loc");
        var names = InternalNames(cars);
        var added = new List<uint>();
        for (int i = 0; i < cars.Count; i++)
        {
            var car = cars[i]; string name = names[i];
            uint carUid = Uid0 + 4 * (uint)i, dynUid = carUid + 1, paintUid = carUid + 2, wheelUid = carUid + 3;
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
            int ri = rp.ObjOfUid(amax.RefUid(bi, "dynamics")), rt = rp.K.Objects[ri].Blocks[0].Type;
            var fit = car.Truthy("body_glb") ? car.Obj("glb_fit") ?? new JObj() : new JObj();
            foreach (var (f, d) in new[] { ("geometry.wheelBase", fit.Num("wb")), ("geometry.trackWidthFront", fit.Num("track")), ("geometry.trackWidthRear", fit.Num("track")) })
                if (d != 0) rpVals[f] = rp.Get(ri, f) + d;
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
            amaxD = KslfWrite.InsertUid(amaxD, dynUid, "RP_CarDynamics", out _);
            amaxD = KslfWrite.InsertUid(amaxD, paintUid, "VEH_PaintJob", out _);
            amax = new Db(amaxD);
            bi = amax.Cars()[bas];
            int wm = amax.ObjOfUid(amax.RefUid(amax.Cars().GetValueOrDefault(wheels, bi), "wheelModel"));
            int fnOff = amax.Field(amax.K.Objects[wm].Blocks[0].Type, "fileName").Off;
            amaxD = KslfWrite.CloneObjectAny(amaxD, wm, null, wheelUid, (data, pool, blocks) => { data.Put(blocks[0] + fnOff, (uint)pool.Len); pool.AddSz(name); });
            amax = new Db(amaxD);
            bi = amax.Cars()[bas];
            int wi = amax.Cars().GetValueOrDefault(wheels, bi);
            int dynRow = amax.RowOfUid(dynUid), paintRow = amax.RowOfUid(paintUid);
            var sets = new Dictionary<string, double>(carVals);
            foreach (var f in new[] { "wheelScaleFront", "wheelScaleRear", "wheelDepthFront", "wheelDepthRear" }) sets[f] = amax.Get(wi, f);
            sets["wheelModel"] = amax.RowOfUid(wheelUid);
            var s2 = car.Obj("stats")!;
            long mph = CarsData.TopSpeed((int)car.Int("class"), s2.Int("speed"));
            sets["LocString"] = Loc0 + (uint)i; sets["dynamics"] = dynRow; sets["UiSpeed"] = s2.Int("speed"); sets["UiAcceleration"] = s2.Int("acceleration"); sets["UiDrift"] = s2.Int("drift");
            sets["UiGrip"] = s2.Int("grip"); sets["UiStrength"] = s2.Int("strength"); sets["UiMaxSpeed"] = mph; sets["GameplayTopSpeed"] = mph; sets["BossAbilitySlot"] = 0; sets["class"] = car.Int("class");
            if (car.Truthy("body_glb")) sets["DriverOffset.vy"] = amax.Get(bi, "DriverOffset.vy") - DriverDown;
            int iconOff = amax.Field(vt, "UiIconName").Off;
            var amaxNow = amax;
            amaxD = KslfWrite.CloneObjectAny(amaxD, bi, name, carUid, (data, pool, blocks) =>
            {
                foreach (var (f, v) in sets) amaxNow.Set(data, blocks[0], vt, f, v);
                data.Put(blocks[0] + iconOff, (uint)pool.Len); pool.AddSz(name);
                int a0 = blocks[0] + pOff, pb = B.U16(data.A, a0), pf = B.U16(data.A, a0 + 2);
                data.Put(blocks[pb] + 4 * pf, (uint)paintRow);
                data.Put(a0 + 4, 1u);
            });
            added.Add(carUid);
            log($"custom car {car.Str("name")} ({name}, class {"ABCD"[(int)car.Int("class")]}, body {bas}, wheels {wheels}): {mph} mph");
            if (car.Truthy("body_glb")) log($"   (imported body {car.Str("body_glb")} on the wheels of {bas} - written with the root pack)");
        }
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
            bool other = car.Truthy("wheels") && car.Str("wheels") != bas;
            byte[]? body = car.Truthy("body_glb") ? CarGlb.GlbModel(src, car, Path.Combine(Paths.Tracks, "cars", car.Str("body_glb")!), log) : other ? (byte[])CarModel.ModelBytes(src, CarModel.ModelPath(bas)).Clone() : null;
            if (other) CarGlb.SwapWheels(body!, CarModel.ModelBytes(src, CarModel.ModelPath(bas)), src, car.Str("wheels")!, log);
            if (body != null) o.Add(new("\\models\\vehicles\\" + name + ".model", body));
            string w = WheelFile(amax, car.Truthy("wheels") ? car.Str("wheels")! : car.Str("base")!).ToLowerInvariant();
            o.Add(new("\\models\\vehicles\\wheels\\" + name + ".model", CarModel.ModelBytes(src, $"models\\vehicles\\wheels\\{w}.model")));
        }
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
        return frontend;
    }
}

// What the window needs from the core: lighting values, previews, cars' numbers, sharing maps, updates, the Play target.
// Ports of parts of lighting.py, sky.py, minimap.py, cars_data.py, car_model.py, car_icon.py, car_glb.py, designer_share.py, updater.py, view3d_scatter.py, model_import.py, library/thumb.py, library/game_preview.py.
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Bmb.Core;

public static class UiText
{
    public static readonly (string Key, string Name)[] FeelNames = { ("game", "Like the game"), ("strong", "Strong"), ("extreme", "Extreme") };
    public static readonly Dictionary<string, string> KindText = new()
    {
        ["scenery"] = "look only - tick Solid to make it a wall", ["breakable"] = "breaks / falls when hit", ["movable"] = "cars push it around", ["animated"] = "animated object",
        ["import"] = "your own model (.glb) - look only, tick Solid to make it a box wall",
        ["ramp"] = "jump ramp - drive over it. S scales length / width / height, R turns it",
    };
    public const int MaxMaps = 12;
    public const string GithubRepo = "MdPro64/blur-map-builder", Discord = "mdproii";
}

// ---- lighting ---------------------------------------------------------------------------------------------------------
public static class LightUi
{
    public static readonly Dictionary<string, (int N, string Label, double Max, double[] Default)> SampleFields = new()
    {
        ["object_light"] = (4, "Light on placed objects (colour + strength)", 0.6, new[] { 1.0, 0.97, 0.92, 0.0 }),
        ["object_light_up"] = (1, "   ... coming from above (0 = all round)", 1.0, new[] { 0.6 }),
    };
    static readonly Dictionary<string, Dictionary<string, double[]>> _presets = new();

    /// every value of a preset (the game's + the two of ours)
    public static Dictionary<string, double[]> Preset(string name)
    {
        lock (_presets)
        {
            if (!_presets.TryGetValue(name, out var p))
            {
                p = Lighting.PresetValues(name);
                foreach (var (k, f) in SampleFields) p[k] = (double[])f.Default.Clone();
                _presets[name] = p;
            }
            return p;
        }
    }

    public static double[] Value(JObj loc, string key)
    {
        var (name, vals) = Lighting.Effective(loc);
        return vals.Arr(key) is { Count: > 0 } a ? a.Select(Json.D).ToArray() : (double[])Preset(name)[key].Clone();
    }

    /// the object in the sky's cities (a game city's sky texture as a small picture)
    public static Pic PresetSky(string preset, int width)
    {
        var F = new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
        string old = Sky.RecordSky(F.Data, F.Objects[Lighting.LsObject(F, Lighting.Presets.GetValueOrDefault(preset, Lighting.Presets[Lighting.DefaultPreset]))]).Path;
        using var R = new Pak(Paths.RootOriginal);
        var d = R.Read("\\" + old.Replace('/', '\\')) ?? throw new FileNotFoundException(old);
        const int W = 2048, H = 512;
        var rgba = Previews.Dxt5Decode(d.AsSpan(0x54, W * H), W, H);
        var px = new byte[W * H * 3];
        Parallel.For(0, W * H, i =>
        {
            float a = MathF.Pow(rgba[4 * i + 3] / 255f, 0.6f);
            for (int c = 0; c < 3; c++) px[3 * i + c] = (byte)Math.Min(255f, rgba[4 * i + c] * a);
        });
        return new Pic(W, H, px).ResizeLanczos(width, width * H / W);
    }

    /// a panorama of your own, whole, its top half bright (sky.preview); note = not 2:1
    public static (Pic Pic, string? Note) OwnSky(string path, int width)
    {
        var (pano, w, h) = Sky.ReadPanorama(path);
        var g = new float[pano.Length]; var peak = new float[w * h];
        for (int i = 0; i < pano.Length; i++) g[i] = MathF.Pow(Math.Max(pano[i], 0), 1 / 2.2f);
        for (int i = 0; i < peak.Length; i++) peak[i] = Math.Max(g[3 * i], Math.Max(g[3 * i + 1], g[3 * i + 2]));
        Array.Sort(peak);
        float scale = Math.Max(peak[(int)(0.99 * (peak.Length - 1))], 1e-6f);
        var px = new byte[pano.Length];
        for (int i = 0; i < px.Length; i++) px[i] = (byte)(Math.Clamp(g[i] / scale, 0, 1) * 255);
        int ph = Math.Max(1, (int)Math.Round((double)width * h / w));
        var p = new Pic(w, h, px).ResizeLanczos(width, ph);
        for (int i = ph / 2 * width * 3; i < p.Px.Length; i++) p.Px[i] = (byte)(p.Px[i] * 0.35);
        return (p, Math.Abs((double)w / h - 2.0) > 0.05 ? $"not 2:1 ({w} x {h}) - a 360 panorama is twice as wide as high" : null);
    }
}

// ---- minimap ---------------------------------------------------------------------------------------------------------
public static class MinimapUi
{
    public static void ExportPng(string path, (double X0, double X1, double Z0, double Z1) b, List<Road> roads, int highlight)
    {
        const int W = Minimap.W, H = Minimap.H;
        using var bm = new Bitmap(W, H);
        using var g = Graphics.FromImage(bm);
        g.Clear(Color.Black);
        double sc = W / (b.X1 - b.X0);
        for (int k = 0; k < roads.Count; k++)
        {
            var r = roads[k];
            var pts = new List<PointF>();
            for (int i = 0; i < r.Count; i += 2) pts.Add(new PointF((float)((r.Centre[i].X - b.X0) / (b.X1 - b.X0) * W), (float)((b.Z1 - r.Centre[i].Z) / (b.X1 - b.X0) * W)));
            using var pen = new Pen(k == highlight ? Color.White : Color.FromArgb(70, 70, 70), Math.Max(6, (int)(r.Width * sc))) { LineJoin = LineJoin.Round };
            if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
        }
        bm.Save(path, ImageFormat.Png);
        File.WriteAllText(path + ".json", Json.Dumps(new JObj { ["bounds"] = new List<object?> { b.X0, b.X1, b.Z0, b.Z1 } }, -1));
    }
}

// ---- the cars ----------------------------------------------------------------------------------------------------------
public static class CarUi
{
    public const string Classes = "ABCD";
    public static readonly (string Key, string Label)[] StatRows = { ("speed", "Top speed"), ("acceleration", "Acceleration"), ("drift", "Drift"), ("grip", "Grip"), ("strength", "Strength") };
    public static List<GameCar> Pickable() => CarsData.Cars().Where(c => c.Pick).ToList();
    public static long Budget(int cls) { var cars = CarsData.ClassCars(cls); return Py.Round(cars.Average(c => (double)StatRows.Sum(s => c.Stats[s.Key]))); }
    public static (long Lo, long Hi) Mph(int cls) { var c = CarsData.ClassCars(cls); return (c.Min(q => q.Mph), c.Max(q => q.Mph)); }
    public static long Total(JObj car) => StatRows.Sum(s => car.Obj("stats")!.Int(s.Key));

    public static JObj Fair(JObj car)
    {
        int cls = (int)car.Int("class"); var st = car.Obj("stats")!;
        long Get(string k) => st.Int(k);
        foreach (var (k, _) in StatRows)
        {
            var (lo, hi) = CarsData.Limit(cls, k);
            st[k] = Math.Max(lo, Math.Min(hi, st.ContainsKey(k) ? Get(k) : lo));
        }
        long budget = Budget(cls);
        foreach (var (k, _) in StatRows.Reverse())
        {
            var (lo, _) = CarsData.Limit(cls, k);
            while (Total(car) > budget && Get(k) > lo) st[k] = Get(k) - 1;
        }
        return car;
    }

    public static JObj NewCar(int cls = 1)
    {
        var src = CarsData.ClassCars(cls)[0];
        var stats = new JObj();
        foreach (var (k, _) in StatRows) stats[k] = src.Stats[k];
        return Fair(new JObj { ["name"] = "My car", ["class"] = (long)cls, ["base"] = src.Name, ["icon"] = src.Icon, ["stats"] = stats, ["paint"] = new List<object?> { "#1e9bff", "#0b1426" } });
    }

    public static JObj StatsOf(GameCar c) { var s = new JObj(); foreach (var (k, _) in StatRows) s[k] = c.Stats[k]; return s; }

    public static string InternalName(string name) => "BMB_" + (Regex.Replace(name, "[^A-Za-z0-9]+", "_").Trim('_') is { Length: > 0 } s ? s : "car");

    static readonly object _lock = new();
    static Pak? _root;
    static Pak Root => _root ??= new Pak(Paths.RootOriginal);

    /// (points, indices) a body car has for an imported .glb
    static readonly Dictionary<string, (int V, int I)> _room = new();
    public static (int V, int I) Room(string bas)
    {
        lock (_lock)
        {
            if (_room.TryGetValue(bas, out var r)) return r;
            var M = CpModel.Read(CarModel.ModelBytes(Root, CarModel.ModelPath(bas)));
            var (_, rooms) = CarGlb.Rooms(M);
            return _room[bas] = (rooms.Sum(q => q.M.Data1[0].VCount), rooms.Sum(q => CarGlb.IndexRoom(q.M)));
        }
    }

    static readonly Dictionary<string, (int P, int T)> _need = new();
    public static (int P, int T) Need(string glb)
    {
        string key = glb + "|" + File.GetLastWriteTimeUtc(glb).Ticks;
        lock (_lock)
        {
            if (_need.TryGetValue(key, out var n)) return n;
            var raw = CarModel.ReadGlbBody(glb);
            var (pts, _, faces) = CarGlb.WeldUv(raw.Tri.Select(v => (double)v).ToArray(), raw.Uv.Select(v => (double)v).ToArray());
            return _need[key] = (pts.Length / 3, faces.Count);
        }
    }

    public static bool Fits(string bas, string glb) { var (v, i) = Room(bas); var (p, t) = Need(glb); return v >= 1.1 * p && i >= 2.3 * t; }
    public const int GlbMaxTris = 10_000;

    public static string Mini(JObj car) { lock (_lock) return CarIcon.Choice(Root, car); }
    public static List<string> MiniShapes() { lock (_lock) return CarIcon.GameShapes(Root).Keys.OrderBy(s => s, StringComparer.Ordinal).ToList(); }

    /// the mini icon, blue on the page's colour (car_icon.preview)
    public static Bitmap MiniPicture(JObj car, int width, Color bg)
    {
        bool[] px; int w, h;
        lock (_lock)
        {
            string c = CarIcon.Choice(Root, car);
            if (c == CarIcon.Auto) (px, w, h) = CarIcon.SideMask(Root, car);
            else (px, w, h) = DrawShape(CarIcon.GameShapes(Root)[c]);
        }
        int oh = Math.Max(1, h * width / w);
        var bm = new Bitmap(width, oh);
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < width; x++)
            {
                int on = 0, sx = x * w / width, sy = y * h / oh, ex = Math.Max(sx + 1, (x + 1) * w / width), ey = Math.Max(sy + 1, (y + 1) * h / oh), n = 0;
                for (int q = sy; q < ey; q++) for (int p = sx; p < ex; p++) { n++; if (px[q * w + p]) on++; }
                double a = (double)on / n;
                bm.SetPixel(x, y, Color.FromArgb((int)(bg.R + (90 - bg.R) * a), (int)(bg.G + (180 - bg.G) * a), (int)(bg.B + (255 - bg.B) * a)));
            }
        return bm;
    }

    static (bool[], int, int) DrawShape(byte[] node, int width = 480)
    {
        int nl = (int)B.U32(node, 8);
        if (B.U32(node, 0) == 3) return DrawShape(node[(12 + nl + 4)..], width);
        int q = 12 + nl + 34;
        float bb4 = B.F32(node, q + 16);
        q += 40;
        int h = Math.Max(1, (int)Math.Ceiling(bb4 * width) + 1);
        using var bm = new Bitmap(width, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(Color.Black);
            int n = (int)B.U32(node, q); q += 4;
            for (int i = 0; i < n; i++)
            {
                int k = (int)B.U32(node, q); var pts = new PointF[k];
                for (int j = 0; j < k; j++) pts[j] = new PointF(B.F32(node, q + 4 + 24 * j) * width, B.F32(node, q + 8 + 24 * j) * width);
                q += 4 + 24 * k;
                if (k >= 3) g.FillPolygon(Brushes.White, pts);
            }
        }
        var p = Pic.FromBitmap(bm);
        var px = new bool[width * h];
        for (int i = 0; i < px.Length; i++) px[i] = p.Px[3 * i] > 127;
        return (px, width, h);
    }

    // ---- the car's picture ------------------------------------------------------------------------------------------
    public sealed class Parts { public Dictionary<string, float[]> Kinds = new(); public byte[]? GlbRgb; public float[]? Smooth; }
    static readonly Dictionary<string, byte[]> Colours = new() { ["dark"] = new byte[] { 38, 40, 46 }, ["glass"] = new byte[] { 70, 95, 120 }, ["light"] = new byte[] { 225, 225, 210 }, ["tyre"] = new byte[] { 24, 24, 26 }, ["rim"] = new byte[] { 175, 180, 190 } };
    static readonly Dictionary<string, double> Gloss = new() { ["paint"] = 0.4, ["glb"] = 0.35, ["glass"] = 0.6, ["rim"] = 0.4, ["light"] = 0.3, ["tyre"] = 0.03 };
    static readonly Dictionary<string, GameParts> _game = new();

    static GameParts Game(string name) { lock (_lock) { if (!_game.TryGetValue(name, out var g)) _game[name] = g = CarModel.ReadGameCar(Root, name); return g; } }
    static float[] Join(List<float[]> l) { var o = new float[l.Sum(a => a.Length)]; int at = 0; foreach (var a in l) { a.CopyTo(o, at); at += a.Length; } return o; }

    /// car_model.car_parts: {kind: triangles} as the view shows it
    public static Parts CarParts(JObj car)
    {
        string bas = car.Str("base")!, wheels = car.Truthy("wheels") ? car.Str("wheels")! : bas;
        var body = Game(bas); var whl = wheels != bas ? Game(wheels) : null;
        var fit = car.Obj("glb_fit") ?? new JObj();
        var outp = new Dictionary<string, List<float[]>>(); var p = new Parts();
        void Add(string k, float[] t) { if (!outp.TryGetValue(k, out var l)) outp[k] = l = new(); l.Add(t); }
        if (car.Truthy("body_glb"))
        {
            string glb = Path.Combine(Paths.Tracks, "cars", car.Str("body_glb")!);
            Add("glb", CarModel.FitGlb(CarModel.ReadGlbBody(glb), body, fit));
            p.GlbRgb = GlbColours(glb);
        }
        else foreach (var (k, l) in body.Tris)
                if (!(k.Contains('|') && k.Split('|')[0] is "tyre" or "rim") && !(whl != null && k == "rim") && k.Split('|')[0] is not ("tyre"))
                    foreach (var a in l) Add(k.Split('|')[0], a);
        var src = whl ?? body;
        foreach (var c in new[] { "frontleft", "frontright", "backleft", "backright" })
        {
            if (!body.Centre.TryGetValue(c, out var at) || !src.Centre.TryGetValue(c, out var frm)) continue;
            double[] to = { at[0] + Math.Sign(at[0]) * fit.Num("track") / 2, at[1], at[2] + Math.Sign(at[2]) * fit.Num("wb") / 2 };
            foreach (var kind in new[] { "tyre", "rim" })
                if (src.Tris.TryGetValue(kind + "|" + c, out var l))
                    foreach (var a in l)
                    {
                        var o = new float[a.Length];
                        for (int i = 0; i < a.Length; i++) o[i] = (float)(a[i] - frm[i % 3] + to[i % 3]);
                        Add(kind, o);
                    }
        }
        foreach (var (k, l) in outp) p.Kinds[k] = Join(l);
        return p;
    }

    static readonly Dictionary<string, Bitmap?> _wheels = new();

    /// one wheel of a game car as the build makes it (tyre, rim ring, rim face), turned 45 degrees: for the Wheels menu
    public static Bitmap? WheelPicture(string car, int size = 168)
    {
        lock (_wheels) if (_wheels.TryGetValue(car, out var hit)) return hit;
        Bitmap? bm = null;
        try
        {
            var g = Game(car);
            float[] face = Array.Empty<float>(); double depth = 0;
            try { lock (_lock) (face, depth) = CarBuild.WheelFace(Root, car); } catch (Exception e) when (e is not OutOfMemoryException) { }
            var parts = new List<(float[] T, byte[] C, bool Local)>();
            string corner = new[] { "frontleft", "frontright", "backleft", "backright" }.FirstOrDefault(q => g.Tris.ContainsKey("tyre|" + q)) ?? "frontleft";
            foreach (var (key, col) in new[] { ("tyre|" + corner, new byte[] { 34, 34, 37 }), ("rim|" + corner, new byte[] { 150, 155, 165 }) })
                if (g.Tris.TryGetValue(key, out var l)) parts.Add((Join(l), col, false));
            var c = g.Centre.GetValueOrDefault(corner) ?? new double[3];
            const double A = Math.PI / 4, Yaw = -35 * Math.PI / 180;
            double az = Math.Atan2(Math.Sin(Yaw), Math.Cos(Yaw)) + A, nx = Math.Sin(az), nz = Math.Cos(az), tx = Math.Cos(az), tz = -Math.Sin(az);
            float[] Turn(float[] t, bool local)
            {
                var o = new float[t.Length];
                for (int i = 0; i < t.Length; i += 3)
                {
                    double x = local ? t[i] + depth : t[i] - c[0], y = local ? t[i + 1] : t[i + 1] - c[1], z = local ? t[i + 2] : t[i + 2] - c[2];
                    o[i] = (float)(-x * nx + z * tx); o[i + 1] = (float)y; o[i + 2] = (float)-(-x * nz + z * tz);
                }
                return o;
            }
            var mats = new List<PreviewMat>();
            PreviewMat Mat(float[] t, byte[] col)
            {
                var im = new Pic(4, 4); for (int i = 0; i < 48; i++) im.Px[i] = col[i % 3];
                return new PreviewMat { Key = "", Image = im, Pos = t, Uv = new float[t.Length / 3 * 2] };
            }
            foreach (var (t, col, _) in parts) mats.Add(Mat(Turn(t, false), col));
            if (face.Length > 0) mats.Add(Mat(Turn(face, true), new byte[] { 200, 205, 215 }));
            var pic = Previews.Render(mats, size);
            bm = pic != null ? Previews.ToBitmap(pic) : null;
        }
        catch (Exception) { bm = null; }
        lock (_wheels) _wheels[car] = bm;
        return bm;
    }

    static byte[] GlbColours(string path)
    {
        var g = ModelImport.ReadGlb(path);
        var (parts, _, _) = ModelImport.GameSpace(g);
        var o = new List<byte>();
        foreach (var part in parts)
        {
            var mat = part.Mat >= 0 && part.Mat < g.Materials.Count ? g.Materials[part.Mat] : null;
            double[] col = mat != null ? mat.Colour : new[] { 0.7, 0.7, 0.7 };
            for (int t = 0; t + 2 < part.Tri.Length; t += 3)
            {
                if (mat?.Image != null)
                {
                    double u = 0, v = 0;
                    for (int q = 0; q < 3; q++) { u += part.Uv[2 * part.Tri[t + q]]; v += part.Uv[2 * part.Tri[t + q] + 1]; }
                    u = Py.Mod(u / 3, 1.0); v = Py.Mod(v / 3, 1.0);
                    var im = mat.Image; int at = ((int)(v * (im.H - 1)) * im.W + (int)(u * (im.W - 1))) * 3;
                    for (int c = 0; c < 3; c++) o.Add((byte)(im.Px[at + c] * Math.Clamp(col[c], 0, 1)));
                }
                else for (int c = 0; c < 3; c++) { double s = Math.Clamp(col[c], 0, 1); o.Add((byte)(255 * (s <= 0.0031308 ? 12.92 * s : 1.055 * Math.Pow(s, 1 / 2.4) - 0.055))); }
            }
        }
        return o.ToArray();
    }

    static float[]? _env;
    static float[] Env()
    {
        if (_env != null) return _env;
        const int h = 128, w = 256;
        var e = new float[h * w * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double el = 1 - 2.0 * y / (h - 1), az = -Math.PI + 2 * Math.PI * x / w;
                double[] c;
                if (el >= 0) { double k = Math.Pow(Math.Clamp(1 - el, 0, 1), 3); c = new[] { 38 + k * 120, 86 + k * 105, 150 + k * 80 }; }
                else { double k = Math.Pow(Math.Clamp(el + 1, 0, 1), 8); c = new[] { 34 + k * 60, 32 + k * 56, 30 + k * 50 }; }
                foreach (var (mid, width, gain) in new[] { (0.9, 0.7, 330.0), (-2.2, 0.45, 240.0) })
                {
                    double d = Py.Mod(az - mid + Math.PI, 2 * Math.PI) - Math.PI, glow = Math.Exp(-Math.Pow(d / width, 4)) * Math.Exp(-Math.Pow((el - 0.62) / 0.2, 4));
                    for (int q = 0; q < 3; q++) c[q] += glow * gain;
                }
                for (int q = 0; q < 3; q++) e[(y * w + x) * 3 + q] = (float)Math.Clamp(c[q], 0, 255);
            }
        return _env = e;
    }

    static float[] SmoothNormals(float[] t)
    {
        int nt = t.Length / 9;
        var map = new Dictionary<(long, long, long), int>(nt);
        var spot = new int[nt * 3]; var sum = new List<double>();
        for (int i = 0; i < nt * 3; i++)
        {
            var key = ((long)Math.Round(t[3 * i] * 500f), (long)Math.Round(t[3 * i + 1] * 500f), (long)Math.Round(t[3 * i + 2] * 500f));
            if (!map.TryGetValue(key, out int s)) { s = map.Count; map[key] = s; sum.Add(0); sum.Add(0); sum.Add(0); }
            spot[i] = s;
        }
        var tot = sum.ToArray();
        for (int i = 0; i < nt; i++)
        {
            int a = 9 * i;
            double ux = t[a + 3] - t[a], uy = t[a + 4] - t[a + 1], uz = t[a + 5] - t[a + 2], vx = t[a + 6] - t[a], vy = t[a + 7] - t[a + 1], vz = t[a + 8] - t[a + 2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            for (int q = 0; q < 3; q++) { int s = spot[3 * i + q] * 3; tot[s] += nx; tot[s + 1] += ny; tot[s + 2] += nz; }
        }
        for (int i = 0; i < tot.Length; i += 3) { double l = Math.Sqrt(tot[i] * tot[i] + tot[i + 1] * tot[i + 1] + tot[i + 2] * tot[i + 2]) + 1e-12; tot[i] /= l; tot[i + 1] /= l; tot[i + 2] /= l; }
        var o = new float[nt * 3];
        for (int i = 0; i < nt; i++)
        {
            double x = 0, y = 0, z = 0;
            for (int q = 0; q < 3; q++) { int s = spot[3 * i + q] * 3; x += tot[s]; y += tot[s + 1]; z += tot[s + 2]; }
            double l = Math.Sqrt(x * x + y * y + z * z) + 1e-12;
            o[3 * i] = (float)(x / l); o[3 * i + 1] = (float)(y / l); o[3 * i + 2] = (float)(z / l);
        }
        return o;
    }

    /// car_model.picture, with a depth buffer instead of the painter's sort
    public static unsafe Bitmap Picture(Parts parts, byte[] paint, int size, double yaw, bool smooth, double pitch = -0.35)
    {
        int k = smooth ? 2 : 1, S = size * k;
        var kinds = parts.Kinds.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var tris = kinds.SelectMany(n => parts.Kinds[n]).ToArray();
        int nt = tris.Length / 9;
        var cols = new byte[nt * 3]; var gloss = new float[nt];
        int ti = 0;
        foreach (var n in kinds)
        {
            int c = parts.Kinds[n].Length / 9;
            for (int i = 0; i < c; i++, ti++)
            {
                for (int q = 0; q < 3; q++) cols[3 * ti + q] = n == "glb" ? parts.GlbRgb![3 * i + q] : n == "paint" ? paint[q] : Colours[n][q];
                gloss[ti] = (float)Gloss.GetValueOrDefault(n, 0.1);
            }
        }
        parts.Smooth ??= SmoothNormals(tris);
        var sn = parts.Smooth;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        var pts = new float[tris.Length];
        double lo0 = 1e30, hi0 = -1e30, lo1 = 1e30, hi1 = -1e30, blox = 1e30, bhix = -1e30, bloy = 1e30, bloz = 1e30, bhiz = -1e30;
        for (int i = 0; i < tris.Length; i += 3)
        {
            double x = tris[i], y = tris[i + 1], z = tris[i + 2], x1 = x * cy - z * sy, z1 = x * sy + z * cy, y2 = y * cp + z1 * sp, z2 = z1 * cp - y * sp;
            pts[i] = (float)x1; pts[i + 1] = (float)y2; pts[i + 2] = (float)z2;
            lo0 = Math.Min(lo0, x1); hi0 = Math.Max(hi0, x1); lo1 = Math.Min(lo1, y2); hi1 = Math.Max(hi1, y2);
            blox = Math.Min(blox, x); bhix = Math.Max(bhix, x); bloy = Math.Min(bloy, y); bloz = Math.Min(bloz, z); bhiz = Math.Max(bhiz, z);
        }
        double L = bhiz - bloz, W = bhix - blox, fz = double.MinValue;
        foreach (var n in new[] { "tyre", "rim" }) if (parts.Kinds.TryGetValue(n, out var a)) for (int i = 2; i < a.Length; i += 3) fz = Math.Max(fz, a[i]);
        if (fz == double.MinValue) fz = bhiz - 0.2 * L;
        double[][] arr = { new[] { 0, bloy, fz }, new[] { 0, bloy, bhiz + 0.3 * L }, new[] { -0.25 * W, bloy, bhiz + 0.15 * L }, new[] { 0.25 * W, bloy, bhiz + 0.15 * L } };
        var a2 = new (double X, double Y, double Z)[4];
        for (int i = 0; i < 4; i++)
        {
            double ax = arr[i][0] * cy - arr[i][2] * sy, az = arr[i][0] * sy + arr[i][2] * cy;
            a2[i] = (ax, arr[i][1] * cp + az * sp, az);
            lo0 = Math.Min(lo0, a2[i].X); hi0 = Math.Max(hi0, a2[i].X); lo1 = Math.Min(lo1, a2[i].Y); hi1 = Math.Max(hi1, a2[i].Y);
        }
        double scale = 0.9 * S / Math.Max(hi0 - lo0, hi1 - lo1), mx = (lo0 + hi0) / 2, my = (lo1 + hi1) / 2;
        double lx = 0.35, ly = 0.75, lz = -0.55, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz); lx /= ll; ly /= ll; lz /= ll;
        var env = Env();
        var buf = new int[S * S]; var zb = new float[S * S];
        Array.Fill(zb, float.NegativeInfinity);
        for (int t = 0; t < nt; t++)
        {
            int a = 9 * t;
            float x0 = pts[a], y0 = pts[a + 1], z0 = pts[a + 2], x1 = pts[a + 3], y1 = pts[a + 4], z1 = pts[a + 5], x2 = pts[a + 6], y2 = pts[a + 7], z2 = pts[a + 8];
            double ux = x1 - x0, uy = y1 - y0, uz = z1 - z0, vx = x2 - x0, vy = y2 - y0, vz = z2 - z0;
            double fx = uy * vz - uz * vy, fy = uz * vx - ux * vz, ff = ux * vy - uy * vx;
            if (ff < 0) continue;
            double fl = Math.Sqrt(fx * fx + fy * fy + ff * ff) + 1e-9; fx /= fl; fy /= fl; ff /= fl;
            double s0 = sn[3 * t], s1 = sn[3 * t + 1], s2 = sn[3 * t + 2];
            double nx = s0 * cy - s2 * sy, nz0 = s0 * sy + s2 * cy, nx1 = nx, ny1 = s1 * cp + nz0 * sp, nz1 = nz0 * cp - s1 * sp;
            double shade = 0.35 + 0.65 * Math.Abs(nx1 * lx + ny1 * ly + nz1 * lz);
            double sg = nx1 * fx + ny1 * fy + nz1 * ff < 0 ? -1 : 1;
            double rnx = nx1 * sg, rny = ny1 * sg, rnz = nz1 * sg, nzc = Math.Clamp(rnz, 0, 1), ry = 2 * nzc * rny, rx = 2 * nzc * rnx, rz = 2 * nzc * nzc - 1;
            int row = Math.Clamp((int)((1 - ry) / 2 * 127), 0, 127), col = (int)((Math.Atan2(rx, rz) + Math.PI) / (2 * Math.PI) * 255);
            double gl = gloss[t], gleam = gl + (1 - gl) * Math.Pow(1 - nzc, 5);
            int rr = 0, gg = 0, bb = 0;
            for (int q = 0; q < 3; q++)
            {
                double v = Math.Clamp(cols[3 * t + q] * shade * (1 - gleam) + env[(row * 256 + col) * 3 + q] * gleam, 0, 255);
                if (q == 0) rr = (int)v; else if (q == 1) gg = (int)v; else bb = (int)v;
            }
            int colr = (255 << 24) | (rr << 16) | (gg << 8) | bb;
            double X0 = (x0 - mx) * scale + S / 2.0, Y0 = S / 2.0 - (y0 - my) * scale, X1 = (x1 - mx) * scale + S / 2.0, Y1 = S / 2.0 - (y1 - my) * scale, X2 = (x2 - mx) * scale + S / 2.0, Y2 = S / 2.0 - (y2 - my) * scale;
            double area = (X1 - X0) * (Y2 - Y0) - (X2 - X0) * (Y1 - Y0);
            if (Math.Abs(area) < (smooth ? 1e-6 : 1e-3)) continue;
            int bx0 = Math.Max((int)Math.Min(X0, Math.Min(X1, X2)), 0), bx1 = Math.Min((int)Math.Max(X0, Math.Max(X1, X2)) + 1, S - 1), by0 = Math.Max((int)Math.Min(Y0, Math.Min(Y1, Y2)), 0), by1 = Math.Min((int)Math.Max(Y0, Math.Max(Y1, Y2)) + 1, S - 1);
            for (int py = by0; py <= by1; py++)
                for (int px = bx0; px <= bx1; px++)
                {
                    double pxc = px + 0.5, pyc = py + 0.5;
                    double w0 = ((X1 - pxc) * (Y2 - pyc) - (X2 - pxc) * (Y1 - pyc)) / area, w1 = ((X2 - pxc) * (Y0 - pyc) - (X0 - pxc) * (Y2 - pyc)) / area, w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float zz = (float)(w0 * z0 + w1 * z1 + w2 * z2);
                    int at = py * S + px;
                    if (zz > zb[at]) { zb[at] = zz; buf[at] = colr; }
                }
        }
        var bm = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(Color.FromArgb(6, 10, 20));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            PointF A(int i) => new((float)((a2[i].X - mx) * scale + S / 2.0), (float)(S / 2.0 - (a2[i].Y - my) * scale));
            void Front()
            {
                using var pen = new Pen(Color.FromArgb(255, 70, 60), Math.Max(2, S / 110)) { LineJoin = LineJoin.Round };
                g.DrawLine(pen, A(0), A(1)); g.DrawLines(pen, new[] { A(2), A(1), A(3) });
                using var f = new Font("Segoe UI", Math.Max(10, S / 28), FontStyle.Bold, GraphicsUnit.Pixel);
                using var br = new SolidBrush(Color.FromArgb(255, 70, 60));
                var c = new PointF((A(0).X + A(1).X) / 2, (A(0).Y + A(1).Y) / 2 - S * 0.04f);
                var sz = g.MeasureString("FRONT", f);
                g.DrawString("FRONT", f, br, c.X - sz.Width / 2, c.Y - sz.Height / 2);
            }
            bool towards = a2[1].Z > a2[0].Z;
            if (!towards) Front();
            var d = bm.LockBits(new Rectangle(0, 0, S, S), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            for (int y = 0; y < S; y++) { var row = (int*)((byte*)d.Scan0 + y * d.Stride); for (int x = 0; x < S; x++) if (buf[y * S + x] != 0) row[x] = buf[y * S + x]; }
            bm.UnlockBits(d);
            if (towards) Front();
        }
        if (k == 1) return bm;
        var small = new Bitmap(size, size);
        using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(bm, 0, 0, size, size); }
        bm.Dispose();
        return small;
    }
}

// ---- previews of models ------------------------------------------------------------------------------------------------
public sealed class PreviewMat { public string Key = ""; public Pic Image = null!; public float[] Pos = null!, Uv = null!; public float[]? Nrm; public double Glow; }

public static class Previews
{
    const int Tex = 256; const uint DXT1 = 0x31545844, DXT5 = 0x35545844;
    static readonly string[] SkipParts = { "dam", "debris", "broken" };
    static readonly ConcurrentDictionary<string, List<PreviewMat>?> _mem = new();
    static readonly ConcurrentDictionary<string, bool> _busy = new();
    static Pak? _root;
    static readonly object _lock = new();
    public static event Action? Arrived;

    public static string Folder => Environment.GetEnvironmentVariable("BMB_PREVIEW") ?? Path.Combine(Paths.ModelCache, "preview");
    public static string KeyOf(string model) => model.ToLowerInvariant().TrimStart('\\').Replace("\\", "__").Replace("/", "__");
    public static string ThumbPath(string model) => Path.Combine(Folder, "thumbs", KeyOf(model) + ".png");

    static byte[]? ModelBytes(string model)
    {
        string p = Path.Combine(Paths.ModelCache, KeyOf(model));
        if (File.Exists(p)) return File.ReadAllBytes(p);
        lock (_lock) { _root ??= new Pak(Paths.RootOriginal); return _root.Read("\\" + model.ToLowerInvariant().TrimStart('\\')); }
    }

    /// RGB + A of a DXT5 picture
    public static byte[] Dxt5Decode(ReadOnlySpan<byte> d, int w, int h)
    {
        var o = new byte[w * h * 4];
        var arr = d.ToArray();
        Parallel.For(0, (h + 3) / 4, row =>
        {
            Span<byte> a = stackalloc byte[8]; Span<int> pal = stackalloc int[12];
            for (int col = 0; col < (w + 3) / 4; col++)
            {
                int at = (row * ((w + 3) / 4) + col) * 16;
                int a0 = arr[at], a1 = arr[at + 1];
                a[0] = (byte)a0; a[1] = (byte)a1;
                if (a0 > a1) for (int i = 1; i < 7; i++) a[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                else { for (int i = 1; i < 5; i++) a[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5); a[6] = 0; a[7] = 255; }
                ulong ab = 0; for (int i = 0; i < 6; i++) ab |= (ulong)arr[at + 2 + i] << (8 * i);
                int c0 = B.U16(arr, at + 8), c1 = B.U16(arr, at + 10); uint bits = B.U32(arr, at + 12);
                pal[0] = (c0 >> 11 & 31) * 255 / 31; pal[1] = (c0 >> 5 & 63) * 255 / 63; pal[2] = (c0 & 31) * 255 / 31;
                pal[3] = (c1 >> 11 & 31) * 255 / 31; pal[4] = (c1 >> 5 & 63) * 255 / 63; pal[5] = (c1 & 31) * 255 / 31;
                for (int t = 0; t < 3; t++) { pal[6 + t] = (2 * pal[t] + pal[3 + t]) / 3; pal[9 + t] = (pal[t] + 2 * pal[3 + t]) / 3; }
                for (int k = 0; k < 16; k++)
                {
                    int x = col * 4 + k % 4, y = row * 4 + k / 4;
                    if (x >= w || y >= h) continue;
                    int j = (int)(bits >> (2 * k) & 3) * 3, q = (y * w + x) * 4;
                    o[q] = (byte)pal[j]; o[q + 1] = (byte)pal[j + 1]; o[q + 2] = (byte)pal[j + 2]; o[q + 3] = a[(int)(ab >> (3 * k) & 7)];
                }
            }
        });
        return o;
    }

    static Pic? Texture(byte[] data, TexInfo t) => ReadTexture(data, t, Tex);

    /// one picture of a game model (DXT1 / DXT5, colour only), its first stored size at most max wide
    public static Pic? ReadTexture(byte[] data, TexInfo t, int max)
    {
        if (t.Dxt != DXT1 && t.Dxt != DXT5) return null;
        int w = t.W, h = t.H, mips = Math.Max(1, t.Mips), block = t.Dxt == DXT5 ? 16 : 8, off = t.Off;
        for (int i = 0; i < mips - 1; i++)
        {
            if (Math.Max(w, h) <= max) break;
            off += Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * block;
            w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
        }
        int n = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4), bw = Math.Max(4, w), bh = Math.Max(4, h);
        if (off + n * block > data.Length) return null;
        byte[] raw;
        if (t.Dxt == DXT5) { raw = new byte[n * 8]; for (int i = 0; i < n; i++) Array.Copy(data, off + 16 * i + 8, raw, 8 * i, 8); }
        else raw = data.AsSpan(off, n * 8).ToArray();
        var px = Dxt.Dxt1Decode(raw, bw, bh);
        if (bw == w && bh == h) return new Pic(w, h, px);
        var p = new Pic(w, h);
        for (int y = 0; y < h; y++) Array.Copy(px, y * bw * 3, p.Px, y * w * 3, w * 3);
        return p;
    }

    static Pic PowerOfTwo(Pic p)
    {
        int Up(int v) { int n = 1; while (n < v) n *= 2; return n; }
        int w = Up(p.W), h = Up(p.H);
        return w == p.W && h == p.H ? p : p.ResizeLanczos(w, h);
    }

    /// game_preview.extract, without temporary files
    static List<PreviewMat>? Extract(string model)
    {
        var data = ModelBytes(model);
        if (data == null) return null;
        var M = CpModel.Read(data);
        var diffs = M.TexList.Where(t => t.Name.EndsWith("_diff", StringComparison.OrdinalIgnoreCase)).ToList();
        if (diffs.Count == 0) diffs = M.TexList.Where(t => t.Name != "DefaultColour").Take(1).ToList();
        var streams = new Dictionary<string, VStreamInfo>();
        foreach (var vs in M.VStreams) streams[string.Join(",", vs.Def.Select(d => d.Type))] = vs;
        var groups = new SortedDictionary<int, List<float>>();
        for (int mi = 0; mi < M.MeshInfos.Count; mi++)
        {
            var mesh = M.MeshInfos[mi];
            string el = mesh.Object >= 0 && mesh.Object < M.AllElements.Count ? M.AllElements[mesh.Object].Name.ToLowerInvariant() : "";
            if (SkipParts.Any(el.Contains)) continue;
            if (!streams.TryGetValue(string.Join(",", M.VertDefs[mesh.Definition].Where(d => d.Prefix == 0).Select(d => d.Type)), out var vs)) continue;
            var fs = M.FStreams[mesh.FaceStream];
            int stride = vs.Bytes, offPos = -1, offUv = -1, uvType = 0, o = 0;
            foreach (var (t, _, _) in vs.Def)
            {
                if (t == 6 && offPos < 0) offPos = o;
                else if (t is 8 or 9 && offUv < 0) { offUv = o; uvType = t; }
                o += t switch { 6 => 12, 0xB => 4, 8 => 4, 9 => 8, 0xA => 4, _ => 4 };
            }
            if (offPos < 0 || mesh.Data1.Count == 0) continue;
            var (fo, fc, _, _) = mesh.Data1[0];
            int bas = mesh.VOffset[0] / stride;
            var tri = new List<int>();
            int Idx(int i) => B.U16(data, fs.Start + 2 * i);
            if (mesh.FaceType == 1)
            {
                if (fc < 3) continue;
                for (int i = 0; i + 2 < fc; i++) { int a = Idx(fo + i), b = Idx(fo + i + 1), c = Idx(fo + i + 2); if (a != b && b != c && a != c) { tri.Add(a); tri.Add(b); tri.Add(c); } }
            }
            else for (int i = 0; i + 2 < fc; i += 3) { int a = Idx(fo + i), b = Idx(fo + i + 1), c = Idx(fo + i + 2); if (a != b && b != c && a != c) { tri.Add(a); tri.Add(b); tri.Add(c); } }
            if (tri.Count == 0 || tri.Max() + bas >= vs.Count) continue;
            int slot = diffs.Count > 0 ? mi % diffs.Count : -1;
            if (!groups.TryGetValue(slot, out var g)) groups[slot] = g = new();
            foreach (int v0 in tri)
            {
                int at = vs.Start + stride * (v0 + bas);
                g.Add(B.F32(data, at + offPos)); g.Add(B.F32(data, at + offPos + 4)); g.Add(B.F32(data, at + offPos + 8));
                if (offUv >= 0) { g.Add((float)BitConverter.ToHalf(data, at + offUv)); g.Add((float)BitConverter.ToHalf(data, at + offUv + 2)); }
                else { g.Add(0); g.Add(0); }
            }
        }
        if (groups.Count == 0) return null;
        var outp = new List<PreviewMat>();
        int gi = 0;
        foreach (var (slot, l) in groups)
        {
            var a = l.ToArray(); int nv = a.Length / 5;
            var pos = new float[nv * 3]; var uv = new float[nv * 2];
            for (int i = 0; i < nv; i++) { pos[3 * i] = a[5 * i]; pos[3 * i + 1] = a[5 * i + 1]; pos[3 * i + 2] = a[5 * i + 2]; uv[2 * i] = a[5 * i + 3]; uv[2 * i + 1] = a[5 * i + 4]; }
            Pic? img = null;
            if (slot >= 0) try { img = Texture(data, diffs[slot]); } catch (Exception) { img = null; }
            img = img != null ? PowerOfTwo(img) : new Pic(4, 4, Enumerable.Repeat(new byte[] { 150, 155, 165 }, 16).SelectMany(q => q).ToArray());
            outp.Add(new PreviewMat { Key = model + "|" + gi, Image = img, Pos = pos, Uv = uv });
            gi++;
        }
        return outp;
    }

    /// a model's preview, or null: then it is read on another thread (Arrived tells the view)
    public static List<PreviewMat>? Get(string model, bool wait = false)
    {
        string k = KeyOf(model);
        if (_mem.TryGetValue(k, out var m)) return m;
        if (wait) { var v = Try(model); _mem[k] = v; return v; }
        if (_busy.TryAdd(k, true))
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _mem[k] = Try(model);
                _busy.TryRemove(k, out bool _);
                Arrived?.Invoke();
            });
        return null;
    }

    public static bool Failed(string model) => _mem.TryGetValue(KeyOf(model), out var m) && m == null;
    public static bool Waiting => !_busy.IsEmpty;
    static List<PreviewMat>? Try(string model) { try { return Extract(model); } catch (Exception) { return null; } }

    // ---- imported models (model_import.preview)
    static readonly Dictionary<string, List<PreviewMat>> _imp = new();
    public static List<PreviewMat> Imported(string file)
    {
        string path = Path.Combine(ModelImport.Folder, file);
        if (!File.Exists(path)) return new();
        string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        lock (_imp)
        {
            if (_imp.TryGetValue(key, out var hit)) return hit;
            var o = new List<PreviewMat>();
            try
            {
                var g = ModelImport.ReadGlb(path);
                var (parts, _, _) = ModelImport.GameSpace(g);
                for (int mi = 0; mi < g.Materials.Count; mi++)
                {
                    var mat = g.Materials[mi]; var mine = parts.Where(p => p.Mat == mi).ToList();
                    if (mine.Count == 0) continue;
                    var col = mat.Colour.Select(c => (byte)Py.Round(255 * Math.Clamp(c, 0, 1))).ToArray();
                    Pic img;
                    if (mat.Image != null)
                    {
                        int Up(int v) { int n = 1; while (n < v) n *= 2; return Math.Min(ModelImport.GameTex, n); }
                        img = mat.Image.ResizeLanczos(Up(mat.Image.W), Up(mat.Image.H));
                        if (col[0] != 255 || col[1] != 255 || col[2] != 255) for (int i = 0; i < img.Px.Length; i++) img.Px[i] = (byte)(img.Px[i] * col[i % 3] / 255.0);
                    }
                    else { img = new Pic(4, 4); for (int i = 0; i < 48; i++) img.Px[i] = col[i % 3]; }
                    int n = mine.Sum(p => p.Tri.Length);
                    var pos = new float[n * 3]; var nrm = new float[n * 3]; var uv = new float[n * 2];
                    int at = 0;
                    foreach (var p in mine)
                        foreach (int v in p.Tri)
                        {
                            for (int q = 0; q < 3; q++) { pos[3 * at + q] = (float)p.Pos[3 * v + q]; nrm[3 * at + q] = (float)p.Nrm[3 * v + q]; }
                            uv[2 * at] = (float)p.Uv[2 * v]; uv[2 * at + 1] = (float)p.Uv[2 * v + 1];
                            at++;
                        }
                    o.Add(new PreviewMat { Key = key + "|" + mi, Image = img, Pos = pos, Nrm = nrm, Uv = uv, Glow = mat.Glow });
                }
            }
            catch (Exception) { o = new(); }
            return _imp[key] = o;
        }
    }

    // ---- the hover picture (library/thumb.py)
    public static Pic? Render(List<PreviewMat> mats0, int size = 168, double seconds = 0)
    {
        var mats = mats0.Where(m => m.Pos.Length > 0).ToList();
        if (mats.Count == 0) return null;
        var end = seconds > 0 ? DateTime.UtcNow.AddSeconds(seconds) : DateTime.MaxValue;
        const int ss = 2; int W = size * ss;
        double[] lo = { 1e30, 1e30, 1e30 }, hi = { -1e30, -1e30, -1e30 };
        foreach (var m in mats)
            for (int i = 0; i < m.Pos.Length; i += 3)
            {
                double x = m.Pos[i], y = m.Pos[i + 1], z = -m.Pos[i + 2];
                lo[0] = Math.Min(lo[0], x); hi[0] = Math.Max(hi[0], x); lo[1] = Math.Min(lo[1], y); hi[1] = Math.Max(hi[1], y); lo[2] = Math.Min(lo[2], z); hi[2] = Math.Max(hi[2], z);
            }
        double[] c = { (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2 };
        double Y = -35.0 * Math.PI / 180, Pt = 24.0 * Math.PI / 180, cy = Math.Cos(Y), sy = Math.Sin(Y), cp = Math.Cos(Pt), sp = Math.Sin(Pt);
        double[] right = { cy, 0, -sy }, fwd = { sy * cp, sp, cy * cp };
        double[] up = { fwd[1] * right[2] - fwd[2] * right[1], fwd[2] * right[0] - fwd[0] * right[2], fwd[0] * right[1] - fwd[1] * right[0] };
        (double, double, double) Proj(double x, double y, double z) { x -= c[0]; y -= c[1]; z -= c[2]; return (x * right[0] + y * right[1] + z * right[2], x * up[0] + y * up[1] + z * up[2], x * fwd[0] + y * fwd[1] + z * fwd[2]); }
        double q0 = 1e30, q1 = -1e30, q2 = 1e30, q3 = -1e30;
        foreach (double x in new[] { lo[0], hi[0] }) foreach (double y in new[] { lo[1], hi[1] }) foreach (double z in new[] { lo[2], hi[2] })
                { var (a, b, _) = Proj(x, y, z); q0 = Math.Min(q0, a); q1 = Math.Max(q1, a); q2 = Math.Min(q2, b); q3 = Math.Max(q3, b); }
        double span = Math.Max(Math.Max(q1 - q0, q3 - q2), 1e-6), scale = W * 0.86 / span, ox = W / 2.0 - (q1 + q0) / 2 * scale, oy = W / 2.0 + (q3 + q2) / 2 * scale;
        var img = new byte[W * W * 3]; for (int i = 0; i < img.Length; i += 3) { img[i] = 11; img[i + 1] = 20; img[i + 2] = 38; }
        var zb = new float[W * W]; Array.Fill(zb, float.NegativeInfinity);
        double sl = Math.Sqrt(0.45 * 0.45 + 0.8 * 0.8 + 0.4 * 0.4), lx = 0.45 / sl, ly = 0.8 / sl, lz = 0.4 / sl;
        foreach (var m in mats)
        {
            var tex = m.Image; int nv = m.Pos.Length / 3;
            for (int t = 0; t + 2 < nv; t += 3)
            {
                if (t % 192 == 0 && DateTime.UtcNow > end) return null;
                double ax = m.Pos[3 * t], ay = m.Pos[3 * t + 1], az = -m.Pos[3 * t + 2], bx = m.Pos[3 * t + 3], by = m.Pos[3 * t + 4], bz = -m.Pos[3 * t + 5], cx = m.Pos[3 * t + 6], cyy = m.Pos[3 * t + 7], cz = -m.Pos[3 * t + 8];
                var (x0p, y0p, z0p) = Proj(ax, ay, az); var (x1p, y1p, z1p) = Proj(bx, by, bz); var (x2p, y2p, z2p) = Proj(cx, cyy, cz);
                double x0 = x0p * scale + ox, y0 = oy - y0p * scale, x1 = x1p * scale + ox, y1 = oy - y1p * scale, x2 = x2p * scale + ox, y2 = oy - y2p * scale;
                double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (Math.Abs(area) < 1e-9) continue;
                double nx = (by - ay) * (cz - az) - (bz - az) * (cyy - ay), ny = (bz - az) * (cx - ax) - (bx - ax) * (cz - az), nz = (bx - ax) * (cyy - ay) - (by - ay) * (cx - ax), nl = Math.Max(Math.Sqrt(nx * nx + ny * ny + nz * nz), 1e-12);
                double light = 0.42 + 0.58 * Math.Abs((nx * lx + ny * ly + nz * lz) / nl);
                int bx0 = Math.Max((int)Math.Min(x0, Math.Min(x1, x2)), 0), bx1 = Math.Min((int)Math.Max(x0, Math.Max(x1, x2)) + 1, W - 1), by0 = Math.Max((int)Math.Min(y0, Math.Min(y1, y2)), 0), by1 = Math.Min((int)Math.Max(y0, Math.Max(y1, y2)) + 1, W - 1);
                float u0 = m.Uv[2 * t], v0 = m.Uv[2 * t + 1], u1 = m.Uv[2 * t + 2], v1 = m.Uv[2 * t + 3], u2 = m.Uv[2 * t + 4], v2 = m.Uv[2 * t + 5];
                for (int py = by0; py <= by1; py++)
                    for (int px = bx0; px <= bx1; px++)
                    {
                        double xc = px + 0.5, yc = py + 0.5, w0 = ((x1 - xc) * (y2 - yc) - (x2 - xc) * (y1 - yc)) / area, w1 = ((x2 - xc) * (y0 - yc) - (x0 - xc) * (y2 - yc)) / area, w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float z = (float)(w0 * z0p + w1 * z1p + w2 * z2p);
                        int at = py * W + px;
                        if (z <= zb[at]) continue;
                        zb[at] = z;
                        double uu = w0 * u0 + w1 * u1 + w2 * u2, vv = w0 * v0 + w1 * v1 + w2 * v2;
                        int tx = Math.Clamp((int)(Py.Mod(uu, 1.0) * tex.W), 0, tex.W - 1), ty = Math.Clamp((int)(Py.Mod(vv, 1.0) * tex.H), 0, tex.H - 1), s = (ty * tex.W + tx) * 3;
                        for (int q = 0; q < 3; q++) img[3 * at + q] = (byte)Math.Min(255.0, tex.Px[s + q] * light);
                    }
            }
        }
        return new Pic(W, W, img).ResizeLanczos(size, size);
    }

    public static void SavePng(Pic p, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bm = ToBitmap(p);
        bm.Save(path + ".tmp.png", ImageFormat.Png);
        File.Move(path + ".tmp.png", path, true);
    }

    public static unsafe Bitmap ToBitmap(Pic p)
    {
        var bm = new Bitmap(p.W, p.H, PixelFormat.Format32bppArgb);
        var d = bm.LockBits(new Rectangle(0, 0, p.W, p.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < p.H; y++)
        {
            byte* r = (byte*)d.Scan0 + y * d.Stride;
            for (int x = 0; x < p.W; x++) { int o = (y * p.W + x) * 3; r[4 * x] = p.Px[o + 2]; r[4 * x + 1] = p.Px[o + 1]; r[4 * x + 2] = p.Px[o]; r[4 * x + 3] = 255; }
        }
        bm.UnlockBits(d);
        return bm;
    }

    /// Bake 3D pictures: one thumbnail per model on all cores. done(n, failed); stop() ends it
    public static (int Done, int Failed, int Total) Bake(IEnumerable<string> models, Action<int, int, int> progress, Func<bool> stop)
    {
        var todo = models.Where(m => !string.IsNullOrEmpty(m)).Distinct().Where(m => !File.Exists(ThumbPath(m))).ToList();
        int done = 0, failed = 0;
        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) }, (m, st) =>
        {
            if (stop()) { st.Stop(); return; }
            try
            {
                var mats = Extract(m);
                var img = mats == null ? null : Render(mats, seconds: 5);
                if (img != null) SavePng(img, ThumbPath(m)); else Interlocked.Increment(ref failed);
            }
            catch (Exception) { Interlocked.Increment(ref failed); }
            progress(Interlocked.Increment(ref done), failed, todo.Count);
        });
        return (done, failed, todo.Count);
    }
}

// ---- importing a .glb ----------------------------------------------------------------------------------------------------
public sealed class ImportInfo
{
    public string Name = ""; public int Tris, Verts, Materials; public double[] Size = new double[3], Lo = new double[3], Hi = new double[3]; public List<(int W, int H)> Textures = new();
    public int? Pieces; public List<(string Level, string Text)> Notes = new();
    public bool Blocked => Notes.Any(n => n.Level == "stop");
}

public static class ImportUi
{
    const int WarnTris = 10_000, MaxTris = 50_000, MaxPieces = 60, WarnPieces = 20;

    public static ImportInfo Analyse(string path)
    {
        var g = ModelImport.ReadGlb(path);
        var (parts, lo, hi) = ModelImport.GameSpace(g);
        var i = new ImportInfo { Name = Path.GetFileNameWithoutExtension(path), Tris = parts.Sum(p => p.Tri.Length / 3), Verts = parts.Sum(p => p.N), Materials = g.Materials.Count, Lo = lo, Hi = hi };
        i.Size = new[] { hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2] };
        i.Textures = g.Materials.Where(m => m.Image != null).Select(m => (m.Image!.W, m.Image.H)).ToList();
        if (i.Tris > MaxTris) i.Notes.Add(("stop", $"{i.Tris} triangles - too many (most for one model: {MaxTris}). Simplify it first (Blender: Decimate modifier)."));
        else
        {
            i.Pieces = ModelImport.Pieces(parts).Count;
            if (i.Tris > WarnTris) i.Notes.Add(("warn", $"{i.Tris} triangles - a big model: it may make the game lag. Under {WarnTris} is best."));
            else i.Notes.Add(("ok", $"{i.Tris} triangles - fine."));
            if (i.Pieces > MaxPieces) i.Notes.Add(("stop", $"Needs {i.Pieces} model pieces (most: {MaxPieces}). Simplify it or use fewer materials."));
            else if (i.Pieces > WarnPieces) i.Notes.Add(("warn", $"Needs {i.Pieces} model pieces. A map has room for about 144 objects in all (road pieces and scenery included)."));
        }
        var big = i.Textures.Where(t => Math.Max(t.W, t.H) > ModelImport.GameTex).ToList();
        if (big.Count > 0)
        {
            var (w, h) = big.OrderByDescending(t => t.W * t.H).First();
            i.Notes.Add((Math.Max(w, h) > 2048 ? "warn" : "info", $"Texture {w} x {h}: shown at {ModelImport.GameTex} x {ModelImport.GameTex} in the game (softer)."));
        }
        foreach (var m in g.Materials)
        {
            if (m.Note != null) i.Notes.Add(("info", $"Material '{m.Name}': {m.Note}."));
            else if (m.Image == null) i.Notes.Add(("info", $"Material '{m.Name}' has no texture - shown in its plain colour."));
        }
        if (g.Skipped.Count > 0) i.Notes.Add(("info", $"Left out: {string.Join(", ", g.Skipped)} (only triangles are used)."));
        double biggest = i.Size.Max();
        if (biggest > 800) i.Notes.Add(("warn", $"It is {biggest:0} m big - maybe made in centimetres? Scale it down in the 3D tab (S)."));
        else if (biggest < 0.3) i.Notes.Add(("warn", $"It is only {biggest:0.00} m big - maybe made in other units? Scale it up in the 3D tab (S)."));
        i.Notes.Add(("info", "Collision: none (cars drive through it). Tick 'Solid' in the 3D tab to make it a wall - a simple BOX around the whole model, not its exact shape (don't use it as a road / ramp)."));
        return i;
    }

    /// copy the .glb next to the maps + its .json; returns the catalog id
    public static string Add(string path, ImportInfo info)
    {
        if (info.Blocked) throw new ImportProblem(info.Notes.First(n => n.Level == "stop").Text);
        Directory.CreateDirectory(ModelImport.Folder);
        string name = new string(info.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        name = name[..Math.Min(40, name.Length)]; if (name == "") name = "model";
        string fname = name + ".glb", side = Path.Combine(ModelImport.Folder, fname + ".json");
        JObj? old = null;
        if (File.Exists(side)) try { old = (JObj)Json.Load(side)!; } catch (Exception) { }
        var used = Directory.GetFiles(ModelImport.Folder, "*.glb.json").Where(f => !string.Equals(Path.GetFileName(f), fname + ".json", StringComparison.OrdinalIgnoreCase)).Select(f => { try { return ((JObj)Json.Load(f)!).Int("slot"); } catch (Exception) { return -1L; } }).ToHashSet();
        long slot = old != null && old.ContainsKey("slot") && !used.Contains(old.Int("slot")) ? old.Int("slot") : Enumerable.Range(0, 256).Select(i => (long)i).First(i => !used.Contains(i));
        string dst = Path.Combine(ModelImport.Folder, fname);
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dst, true);
        List<object?> L(double[] v) => v.Select(q => (object?)q).ToList();
        Json.Save(new JObj { ["file"] = fname, ["name"] = info.Name, ["slot"] = slot, ["lo"] = L(info.Lo), ["hi"] = L(info.Hi), ["tris"] = (long)info.Tris, ["pieces"] = (long)(info.Pieces ?? 0), ["materials"] = (long)info.Materials, ["source"] = Path.GetFullPath(path) }, side);
        return "import:" + fname;
    }
}

// ---- .blurmap --------------------------------------------------------------------------------------------------------
public sealed class BlurMapFile { public JObj Map = new(), Info = new(); public Dictionary<string, byte[]> Pics = new(); }

public static class Share
{
    public const string Ext = ".blurmap"; const int Format = 1, MaxJson = 8 * 1024 * 1024;
    public static readonly string[] GameIds = { "route_name", "route_uid", "loc_id", "list_uids" };
    static readonly string[] Pictures = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    public static JObj Copy(JObj o) => (JObj)Json.Parse(Json.Dumps(o, -1))!;

    public static int Export(JObj m, string locName, string path, JObj settings)
    {
        var data = Copy(m);
        foreach (var k in GameIds) data.Remove(k);
        var pics = new List<(Action<string> Set, string Src)>();
        if (data.Obj("minimap") is { } mm && mm.Truthy("image") && File.Exists(mm.Str("image"))) pics.Add((v => mm["image"] = v, mm.Str("image")!));
        if (data.Truthy("picture") && File.Exists(data.Str("picture"))) pics.Add((v => data["picture"] = v, data.Str("picture")!));
        if (File.Exists(path)) File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        for (int n = 0; n < pics.Count; n++)
        {
            string name = $"picture_{n}{Path.GetExtension(pics[n].Src).ToLowerInvariant()}";
            z.CreateEntryFromFile(pics[n].Src, name);
            pics[n].Set(name);
        }
        void Text(string name, string s) { using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false)); w.Write(s); }
        Text("map.json", Json.Dumps(data, 1));
        Text("info.json", Json.Dumps(new JObj { ["format"] = (long)Format, ["name"] = m.Str("name") ?? "map", ["location"] = locName, ["made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["app"] = "Blur map builder (road designer)", ["settings"] = settings }, 1));
        return pics.Count;
    }

    public static BlurMapFile Read(string path)
    {
        ZipArchive z;
        try { z = ZipFile.OpenRead(path); } catch (InvalidDataException) { throw new ImportProblem("this is not a .blurmap file"); }
        using (z)
        {
            var mapE = z.GetEntry("map.json") ?? throw new ImportProblem("no map inside (map.json missing)");
            var infoE = z.GetEntry("info.json");
            foreach (var e in new[] { mapE, infoE }) if (e != null && e.Length > MaxJson) throw new ImportProblem(e.Name + " is far too big");
            string Txt(ZipArchiveEntry e) { using var r = new StreamReader(e.Open(), Encoding.UTF8); return r.ReadToEnd(); }
            var o = new BlurMapFile();
            if (Json.Parse(Txt(mapE)) is not JObj m || m.Arr("points") is not { Count: >= 2 }) throw new ImportProblem("the map has no road in it");
            o.Map = m;
            if (infoE != null) o.Info = Json.Parse(Txt(infoE)) as JObj ?? new JObj();
            if (o.Info.Int("format", Format) > Format) throw new ImportProblem($"made by a newer map builder (format {o.Info.Int("format")}) - update yours");
            foreach (var e in z.Entries)
                if (e.FullName.StartsWith("picture_") && !e.FullName.Contains('/') && !e.FullName.Contains('\\') && Pictures.Any(p => e.FullName.EndsWith(p, StringComparison.OrdinalIgnoreCase)) && e.Length < 32 * 1024 * 1024)
                {
                    using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms);
                    o.Pics[e.FullName] = ms.ToArray();
                }
            return o;
        }
    }
}

// ---- updates, Play ------------------------------------------------------------------------------------------------------
public static class Updates
{
    static string SourceFile => Path.Combine(Paths.Data, "update_source.json");

    public static string? ParseRepo(string? text)
    {
        var m = Regex.Match(text ?? "", @"^\s*(?:https?://)?(?:www\.)?(?:github\.com/)?([A-Za-z0-9-]+)/([A-Za-z0-9._-]+?)(?:\.git)?(?:/.*)?\s*$");
        return m.Success ? m.Groups[1].Value + "/" + m.Groups[2].Value : null;
    }

    public static string Repo()
    {
        try { if (ParseRepo(((JObj)Json.Load(SourceFile)!).Str("repo")) is { } r) return r; } catch (Exception) { }
        return UiText.GithubRepo;
    }

    public static string SetRepo(string? text)
    {
        var r = text != null ? ParseRepo(text) : null;
        if (r == null || r.Equals(UiText.GithubRepo, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(SourceFile); } catch (IOException) { } return UiText.GithubRepo; }
        Json.Save(new JObj { ["repo"] = r }, SourceFile);
        return r;
    }

    public static int[] Ver(string? tag) => Regex.Matches(tag ?? "", @"\d+").Take(3).Select(m => int.Parse(m.Value)).ToArray();

    public static bool Newer(string? tag)
    {
        var a = Ver(tag); var b = Ver(Paths.Version);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// what a release must carry (GitHub turns spaces into dots: "Blur.Map.Builder.exe" - matched to these either way)
    public static readonly string[] Programs = { "Blur Map Builder.exe", "Blur Map Tester.exe" };
    static string Key(string? name) => (name ?? "").ToLowerInvariant().Replace(" ", "").Replace(".", "");
    public static bool Installable => Environment.ProcessPath is { } p && !File.Exists(Path.ChangeExtension(p, ".dll"));   // the one-file program, not a build folder

    public static async Task<(string Tag, string Url, string Notes, Dictionary<string, string> Files)?> Latest()
    {
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("BlurMapBuilder");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var rel = (JObj)Json.Parse(await c.GetStringAsync(Environment.GetEnvironmentVariable("BMB_UPDATE_API") ?? $"https://api.github.com/repos/{Repo()}/releases/latest"))!;
            var files = new Dictionary<string, string>();
            foreach (var a in rel.Objs("assets"))
                foreach (var n in Programs)
                    if (Key(a.Str("name")) == Key(n) && a.Str("browser_download_url") is { } url) files[n] = url;
            return (rel.Str("tag_name") ?? "", rel.Str("html_url") ?? "", (rel.Str("body") ?? "").Trim(), files);
        }
        catch (Exception) { return null; }
    }

    /// the file a program has here: the download keeps GitHub's dotted name unless the player renamed it - update THAT one, not a second copy next to it
    static string LocalFile(string name, string self)
    {
        string dir = Path.GetDirectoryName(self)!;
        if (name == Programs[0]) return self;
        return Directory.GetFiles(dir, "*.exe").FirstOrDefault(f => Key(Path.GetFileName(f)) == Key(name)) ?? Path.Combine(dir, name);
    }

    /// downloads the programs next to the old ones, then swaps them (a running exe can be renamed; the last version stays as .old). An error text or null
    public static async Task<string?> Install(Dictionary<string, string> files, Action<string, long, long>? progress = null)
    {
        if (!Installable) return "Updates install into the finished program only (this is a build folder).";
        if (files.Count == 0) return "The release has no program files.";
        string self = Environment.ProcessPath!;
        using var c = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("BlurMapBuilder");
        var got = new List<string>();
        foreach (var (name, url) in files)
        {
            string cur = LocalFile(name, self), tmp = cur + ".new";
            try
            {
                using var r = await c.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                r.EnsureSuccessStatusCode();
                long total = r.Content.Headers.ContentLength ?? 0, done = 0;
                var buf = new byte[1 << 16];
                using (var i = await r.Content.ReadAsStreamAsync())
                using (var o = File.Create(tmp))
                    for (int n; (n = await i.ReadAsync(buf)) > 0;) { await o.WriteAsync(buf.AsMemory(0, n)); done += n; progress?.Invoke(name, done, total); }
                if (total > 0 && done != total) throw new IOException("the download stopped early");
            }
            catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
            {
                return $"Download failed - check your internet connection and try again. Nothing was changed.\n({name}: {e.Message})";
            }
            using (var f = File.OpenRead(tmp))
                if (f.Length < 1_000_000 || f.ReadByte() != 'M' || f.ReadByte() != 'Z') { f.Close(); File.Delete(tmp); return $"The downloaded {name} is not a program - update stopped, nothing changed."; }
            got.Add(cur);
        }
        foreach (var cur in got)
            try
            {
                if (File.Exists(cur + ".old")) File.Delete(cur + ".old");
                if (File.Exists(cur)) File.Move(cur, cur + ".old");
                File.Move(cur + ".new", cur);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"Could not replace {Path.GetFileName(cur)}: {e.Message}"; }
        return null;
    }
}

public static class Play
{
    public static string QuickstartTarget => Path.Combine(Paths.Game, "amax", "plugins", "quickstart", "target.lua");
    public static string RebornExe => Path.Combine(Paths.Game, "Blur REBORN.exe");

    static int Signed32(long v) => unchecked((int)(uint)(v & 0xFFFFFFFF));
    static string LuaList(IEnumerable<string?> items) => "{" + string.Join(", ", items.Where(s => !string.IsNullOrEmpty(s)).Select(s => "\"" + s!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "}";

    /// the Amax QuickStart plugin's target: custom LAN races start on this map. Returns an error text or null
    static string FastFile => Path.Combine(Paths.Data, "play.json");
    /// fast start: the QuickStart plugin skips the logo movies, the attract movie and "press start" (on by default)
    public static bool Fast
    {
        get { try { var o = (JObj)Json.Load(FastFile)!; return !o.ContainsKey("fast") || o.Truthy("fast"); } catch (Exception) { return true; } }
        set { try { Directory.CreateDirectory(Paths.Data); File.WriteAllText(FastFile, Json.Dumps(new JObj { ["fast"] = value }, -1)); } catch (IOException) { } }
    }

    public static string? WriteQuickstart(JObj world, JObj loc, JObj m, bool on)
    {
        try { Setup.RefreshPlugin(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        World.AssignIds(world);
        string text = $"-- written by the track designer ({DateTime.Now:yyyy-MM-dd HH:mm}): the map the QuickStart plugin opens a custom LAN race on\n" +
            $"return {{\n\tenabled = {(on ? "true" : "false")},\n\tfast = {(Fast ? "true" : "false")},\n\tauto = true,\n\tcity_names = {LuaList(new[] { loc.Str("name"), loc.Str("city_name") })},\n\troute_names = {LuaList(new[] { m.Str("name"), m.Str("route_name") })},\n" +
            $"\tcity_uid = {Signed32(loc.Int("city_uid"))},\n\troute_uid = {Signed32(m.Int("route_uid"))},\n\tlaps = {(m.Truthy("laps") ? m.Int("laps") : 1)},\n}}\n";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(QuickstartTarget)!);
            File.WriteAllText(QuickstartTarget, text, new UTF8Encoding(false));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    public static bool GameRunning() => System.Diagnostics.Process.GetProcessesByName("Blur").Length > 0 || System.Diagnostics.Process.GetProcessesByName("Blur REBORN").Length > 0;
}

// ---- Path scatter ------------------------------------------------------------------------------------------------------
public sealed class ScatterOpt { public double Spacing = 10, SideDist, Fill = 100, Turn, TurnRand, SizeMin = 1, SizeMax = 1, Spread; public string Side = "middle", Order = "random"; public bool Align = true; }
public sealed class ScatterSpot { public string Id = ""; public double X, Y, Z, Turn, Scale; public bool Fits = true; }

public static class Scatter
{
    public static List<ScatterSpot> PathSpots(List<double[]> pts, List<string> items, ScatterOpt opt, int seed)
    {
        var o = new List<ScatterSpot>();
        if (pts.Count < 2 || items.Count == 0) return o;
        var rng = new PyRandom(seed);
        var seg = new double[pts.Count - 1]; var cum = new double[pts.Count];
        for (int i = 0; i < seg.Length; i++) { seg[i] = Math.Sqrt(Math.Pow(pts[i + 1][0] - pts[i][0], 2) + Math.Pow(pts[i + 1][2] - pts[i][2], 2)); cum[i + 1] = cum[i] + seg[i]; }
        double total = cum[^1];
        if (total < 1e-6) return o;
        double step = Math.Max(0.5, opt.Spacing);
        int[] signs = opt.Side switch { "middle" => new[] { 0 }, "left" => new[] { 1 }, "right" => new[] { -1 }, _ => new[] { 1, -1 } };
        int n = 0, steps = (int)Math.Floor((total + 1e-6) / step);
        for (int q = 0; q <= steps; q++)
        {
            double s = q * step;
            int i = 0;
            while (i + 1 < cum.Length && cum[i + 1] <= s) i++;
            i = Math.Min(i, seg.Length - 1);
            while (i > 0 && seg[i] < 1e-9) i--;
            double t = seg[i] > 1e-9 ? (s - cum[i]) / seg[i] : 0.0;
            double px = pts[i][0] + (pts[i + 1][0] - pts[i][0]) * t, py = pts[i][1] + (pts[i + 1][1] - pts[i][1]) * t, pz = pts[i][2] + (pts[i + 1][2] - pts[i][2]) * t;
            double dx = (pts[i + 1][0] - pts[i][0]) / Math.Max(seg[i], 1e-9), dz = (pts[i + 1][2] - pts[i][2]) / Math.Max(seg[i], 1e-9), lx = -dz, lz = dx;
            foreach (int sign in signs)
            {
                if (rng.Random() * 100.0 >= opt.Fill) continue;
                double a = rng.Uniform(-opt.Spread, opt.Spread), b = rng.Uniform(-opt.Spread, opt.Spread), k = sign * opt.SideDist + b;
                double turn = opt.Align ? Math.Atan2(dx, dz) : 0.0;
                if (opt.Align && sign < 0) turn += Math.PI;
                turn += (opt.Turn + rng.Uniform(-opt.TurnRand, opt.TurnRand)) * Math.PI / 180;
                turn = Py.Mod(turn + Math.PI, 2 * Math.PI) - Math.PI;
                double lo = Math.Min(opt.SizeMin, opt.SizeMax), hi = Math.Max(opt.SizeMin, opt.SizeMax);
                string item = opt.Order == "in turn" ? items[n % items.Count] : items[rng.RandInt(0, items.Count - 1)];
                n++;
                o.Add(new ScatterSpot { Id = item, X = px + dx * a + lx * k, Y = py, Z = pz + dz * a + lz * k, Turn = turn, Scale = rng.Uniform(lo, hi) });
            }
        }
        return o;
    }
}

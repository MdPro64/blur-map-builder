// The Lighting tab's preview: the game's Dodge Challenger Tuner (GameCar.cs; a simple car made in code if the game files cannot
// be read) on a ground plane, drawn by a small software rasterizer. The paint has a clear coat (fresnel) that mirrors the location's sky picture; sun, sky light, the light on placed objects,
// baked light (on the ground), lamp glow, fog, exposure and bloom follow the sliders. Drag = turn the camera.
// It is a rough look, not the game's renderer, but it follows how the game lights a scene: exposure, bloom, fog, car paint.
using System.Drawing.Imaging;
using System.Numerics;
using Bmb.Core;

namespace Bmb.App;

/// the sky picture as linear light: the top half of the panorama, row 0 = straight up, the last row = the horizon
public sealed class SkyEnv
{
    public const int W = 1024, H = 256;
    public readonly float[] Px = new float[W * H * 3];       // sharp (mirror reflections, the background)
    public readonly float[] Blur = new float[32 * 8 * 3];     // very soft (glossy reflections)
    public readonly Vector3[] Cube = new Vector3[6];          // light from the sky on a surface facing +X -X +Y -Y +Z -Z (sky only, ground not in)
    public Vector3 SunDir = Vector3.Normalize(new Vector3(0.6f, 0.65f, 0.45f));   // in the picture's own space (before the rotation)

    static readonly Dictionary<string, SkyEnv> _cache = new();

    public static SkyEnv Get(string key, Func<SkyEnv> make)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var e)) return e;
            try { e = make(); } catch (Exception x) when (x is not OutOfMemoryException) { e = Plain(); }
            e.Finish();
            return _cache[key] = e;
        }
    }

    /// a game city's sky texture (DXT5, 2048 x 512, the colour divided by alpha: as Sky.Texture writes it)
    public static SkyEnv FromPreset(string preset)
    {
        var F = new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
        string old = Sky.RecordSky(F.Data, F.Objects[Lighting.LsObject(F, Lighting.Presets.GetValueOrDefault(preset, Lighting.Presets[Lighting.DefaultPreset]))]).Path;
        using var R = new Pak(Paths.RootOriginal);
        var d = R.Read("\\" + old.Replace('/', '\\')) ?? throw new FileNotFoundException(old);
        const int SW = 2048, SH = 512;
        var rgba = Previews.Dxt5Decode(d.AsSpan(0x54, SW * SH), SW, SH);
        var e = new SkyEnv();
        int fx = SW / W, fy = SH / H;
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                float r = 0, g = 0, b = 0;
                for (int j = 0; j < fy; j++)
                    for (int i = 0; i < fx; i++)
                    {
                        int s = 4 * ((y * fy + j) * SW + x * fx + i);
                        float a = rgba[s + 3] / 255f;
                        float a2 = a * a; r += rgba[s] / 255f * a2; g += rgba[s + 1] / 255f * a2; b += rgba[s + 2] / 255f * a2;   // as the game's sky: colour x alpha x alpha
                    }
                float k = 1f / (fx * fy); int t = 3 * (y * W + x);
                e.Px[t] = r * k; e.Px[t + 1] = g * k; e.Px[t + 2] = b * k;
            }
        });
        return e;
    }

    /// your own panorama: its top half, made as bright as the build makes it (99 % of it under 1)
    public static SkyEnv FromFile(string path)
    {
        var (pano, pw, ph0) = Sky.ReadPanorama(path);
        int ph = Math.Max(1, ph0 / 2);
        var e = new SkyEnv();
        var acc = new double[W * H * 3]; var n = new int[W * H];
        for (int y = 0; y < ph; y++)
        {
            int ty = Math.Min(H - 1, y * H / ph);
            for (int x = 0; x < pw; x++)
            {
                int tx = Math.Min(W - 1, x * W / pw), s = 3 * (y * pw + x), t = ty * W + tx;
                acc[3 * t] += Math.Max(0, pano[s]); acc[3 * t + 1] += Math.Max(0, pano[s + 1]); acc[3 * t + 2] += Math.Max(0, pano[s + 2]); n[t]++;
            }
        }
        var gam = new float[W * H * 3]; var peak = new float[W * H];
        for (int i = 0; i < W * H; i++)
        {
            for (int c = 0; c < 3; c++) gam[3 * i + c] = n[i] > 0 ? MathF.Pow((float)(acc[3 * i + c] / n[i]), 1 / 2.2f) : 0;
            peak[i] = Math.Max(gam[3 * i], Math.Max(gam[3 * i + 1], gam[3 * i + 2]));
        }
        Array.Sort(peak);
        float scale = Math.Max(peak[(int)(0.99 * (peak.Length - 1))], 1e-6f);
        // as the build stores it (colour / alpha, alpha = the brightest channel) and the game draws it (colour x alpha x alpha)
        for (int i = 0; i < W * H; i++)
        {
            float a = Math.Clamp(Math.Max(gam[3 * i], Math.Max(gam[3 * i + 1], gam[3 * i + 2])) / scale, 1 / 255f, 1);
            for (int c = 0; c < 3; c++) e.Px[3 * i + c] = Math.Clamp(gam[3 * i + c] / scale, 0, 1) * a;
        }
        return e;
    }

    /// no picture could be read: a plain blue sky
    public static SkyEnv Plain()
    {
        var e = new SkyEnv();
        for (int y = 0; y < H; y++)
        {
            float t = (y + 0.5f) / H;
            for (int x = 0; x < W; x++) { int i = 3 * (y * W + x); e.Px[i] = 0.10f + 0.35f * t; e.Px[i + 1] = 0.22f + 0.35f * t; e.Px[i + 2] = 0.55f + 0.25f * t; }
        }
        return e;
    }

    public static Vector3 Dir(float u, float v)
    {
        float a = u * 2 * MathF.PI, el = (1 - v) * MathF.PI / 2;
        return new Vector3(MathF.Cos(el) * MathF.Cos(a), MathF.Sin(el), MathF.Cos(el) * MathF.Sin(a));
    }

    void Finish()
    {
        // soft copy
        int bx = W / 32, by = H / 8;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 32; x++)
                for (int c = 0; c < 3; c++)
                {
                    float s = 0;
                    for (int j = 0; j < by; j++) for (int i = 0; i < bx; i++) s += Px[3 * ((y * by + j) * W + x * bx + i) + c];
                    Blur[3 * (y * 32 + x) + c] = s / (bx * by);
                }
        for (int pass = 0; pass < 2; pass++)
        {
            var o = new float[Blur.Length];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 32; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        float s = 0; int k = 0;
                        for (int j = -1; j <= 1; j++)
                        {
                            int yy = y + j; if (yy < 0 || yy > 7) continue;
                            for (int i = -1; i <= 1; i++) { s += Blur[3 * (yy * 32 + (x + i + 32) % 32) + c]; k++; }
                        }
                        o[3 * (y * 32 + x) + c] = s / k;
                    }
            Array.Copy(o, Blur, o.Length);
        }
        // light on the 6 faces of a cube (cosine weighted, divided by pi: diffuse = albedo * Cube)
        var axes = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        float best = -1, mean = 0; Vector3 bestDir = SunDir;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var d = Dir((x + 0.5f) / W, (y + 0.5f) / H);
                float dw = 2 * MathF.PI / W * (MathF.PI / 2 / H) * MathF.Sqrt(Math.Max(0, 1 - d.Y * d.Y));
                int i = 3 * (y * W + x);
                var L = new Vector3(Px[i], Px[i + 1], Px[i + 2]);
                for (int a = 0; a < 6; a++) Cube[a] += L * (Math.Max(0, Vector3.Dot(axes[a], d)) * dw / MathF.PI);
                float lum = L.X * 0.3f + L.Y * 0.6f + L.Z * 0.1f;
                mean += lum / (W * H);
                if (lum > best && d.Y > 0.05f) { best = lum; bestDir = d; }
            }
        // a clear bright spot = the sun of the picture
        if (best > 3 * mean && best > 0.2f) SunDir = Vector3.Normalize(new Vector3(bestDir.X, Math.Max(bestDir.Y, 0.25f), bestDir.Z));
    }
}

/// the car: a smooth body, a cabin with windows, 4 wheels. Built once.
static class PreviewCar
{
    // parts: 0 body, 1 cabin, 2 tyre, 3 inner side of a wheel, 10..13 the outer face of wheel 0..3
    public static readonly List<Vector3> P = new(), N = new();
    public static readonly List<int> Part = new();          // per vertex
    static readonly List<int> Grp = new();                  // which piece a vertex was built in (for turning the normals out)
    static int _grp;
    public static readonly List<int> Tri = new();           // 3 per triangle
    public static readonly Vector3[] Wheels = new Vector3[4];
    public const float Lh = 2.2f, Wh = 0.93f, WheelR = 0.33f, WheelW = 0.24f;
    public static int Triangles => Tri.Count / 3;

    static PreviewCar() { Build(); }

    static float SPow(float v, float e) => MathF.Sign(v) * MathF.Pow(MathF.Abs(v), e);
    static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    static void Grid(List<Vector3[]> rings, int part, bool closedAround)
    {
        int b = P.Count, n = rings[0].Length; _grp++;
        foreach (var r in rings) foreach (var p in r) { P.Add(p); N.Add(Vector3.Zero); Part.Add(part); Grp.Add(_grp); }
        int m = closedAround ? n : n - 1;
        for (int s = 0; s + 1 < rings.Count; s++)
            for (int j = 0; j < m; j++)
            {
                int a = b + s * n + j, c = b + s * n + (j + 1) % n, d = a + n, e = c + n;
                Tri.AddRange(new[] { a, d, c }); Tri.AddRange(new[] { c, d, e });
            }
    }

    static void Build()
    {
        float[] wx = { 1.36f, 1.36f, -1.30f, -1.30f }; float[] wz = { 0.80f, -0.80f, 0.80f, -0.80f };
        // --- body: sections along x (cosine spacing: more near the ends), each a rounded shape that closes to a point at the nose / tail
        const int Ns = 30, Nr = 24;
        var rings = new List<Vector3[]>();
        for (int s = 0; s < Ns; s++)
        {
            float x = -Lh * MathF.Cos(MathF.PI * s / (Ns - 1)), e = Math.Min(1, MathF.Abs(x) / Lh);
            float k = MathF.Pow(Math.Max(0, 1 - MathF.Pow(e, 3.5f)), 1 / 3.5f), g = MathF.Pow(Math.Max(0, 1 - MathF.Pow(e, 6)), 1 / 6f);
            float c = 0.64f;
            float top = 0.94f - 0.17f * Smooth(0.5f, 2.2f, x) - 0.08f * Smooth(-1.7f, -2.2f, x);
            float bot = 0.24f + 0.06f * Smooth(1.7f, 2.2f, x) + 0.07f * Smooth(-1.8f, -2.2f, x);
            for (int w = 0; w < 4; w += 2)
            {
                float dx = x - wx[w];
                if (MathF.Abs(dx) < 0.43f) bot = Math.Max(bot, WheelR + MathF.Sqrt(0.43f * 0.43f - dx * dx) - 0.02f);
            }
            bot = Math.Min(bot, c - 0.03f);
            var ring = new Vector3[Nr];
            for (int j = 0; j < Nr; j++)
            {
                float th = 2 * MathF.PI * j / Nr, cs = MathF.Cos(th), sn = MathF.Sin(th);
                float y = sn >= 0 ? c + (top - c) * g * SPow(sn, 0.55f) : c - (c - bot) * g * SPow(-sn, 0.45f);
                ring[j] = new Vector3(x, y, Wh * k * SPow(cs, 0.5f));
            }
            rings.Add(ring);
        }
        Grid(rings, 0, true);
        // --- cabin: a rounded box with a sloped windscreen; its bottom sits inside the body
        const int Nl = 9, Nc = 28; float yb = 0.80f, yt = 1.40f;
        rings = new List<Vector3[]>();
        for (int l = 0; l < Nl; l++)
        {
            float ph = MathF.PI / 2 * l / (Nl - 1), t = MathF.Pow(MathF.Sin(ph), 0.45f), sc = l == Nl - 1 ? 0 : MathF.Pow(MathF.Cos(ph), 0.45f);
            float xf = 0.78f - 0.80f * t, xbk = -1.55f + 0.40f * t, a = (xf - xbk) / 2, cx = (xf + xbk) / 2, bw = 0.80f - 0.13f * t;
            var ring = new Vector3[Nc];
            for (int j = 0; j < Nc; j++)
            {
                float th = 2 * MathF.PI * j / Nc;
                ring[j] = new Vector3(cx + a * sc * SPow(MathF.Cos(th), 0.4f), yb + (yt - yb) * t, bw * sc * SPow(MathF.Sin(th), 0.4f));
            }
            rings.Add(ring);
        }
        Grid(rings, 1, true);
        // --- wheels: a tyre with round shoulders, a flat outer face (the rim is drawn on it) and an inner face
        const int Nw = 18;
        for (int w = 0; w < 4; w++)
        {
            var cen = new Vector3(wx[w], WheelR, wz[w]); Wheels[w] = cen;
            float side = MathF.Sign(wz[w]);
            (float Off, float R)[] prof = { (-0.12f, 0.27f), (-0.115f, 0.315f), (-0.09f, 0.33f), (0.09f, 0.33f), (0.115f, 0.315f), (0.12f, 0.27f) };
            Vector3 At(float off, float r, int j) { float a = 2 * MathF.PI * j / Nw; return cen + new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), off * side); }
            rings = prof.Select(q => Enumerable.Range(0, Nw).Select(j => At(q.Off, q.R, j)).ToArray()).ToList();
            Grid(rings, 2, true);
            foreach (var (off, part) in new[] { (0.12f, 10 + w), (-0.12f, 3) })
            {
                int c0 = P.Count; _grp++;
                P.Add(cen + new Vector3(0, 0, off * side)); N.Add(Vector3.Zero); Part.Add(part); Grp.Add(_grp);
                for (int j = 0; j < Nw; j++) { P.Add(At(off, 0.27f, j)); N.Add(Vector3.Zero); Part.Add(part); Grp.Add(_grp); }
                for (int j = 0; j < Nw; j++)
                {
                    int a = c0 + 1 + j, b = c0 + 1 + (j + 1) % Nw;
                    if ((off > 0) == (side > 0)) Tri.AddRange(new[] { c0, b, a }); else Tri.AddRange(new[] { c0, a, b });
                }
            }
        }
        // smooth normals (vertices are shared only inside one part, so the parts keep hard edges)
        for (int i = 0; i < Tri.Count; i += 3)
        {
            var fn = Vector3.Cross(P[Tri[i + 1]] - P[Tri[i]], P[Tri[i + 2]] - P[Tri[i]]);
            for (int k = 0; k < 3; k++) N[Tri[i + k]] += fn;
        }
        // each piece's normals point out of it (a vote over the piece: mirrored wheels are wound the other way)
        Vector3 Inside(int i)
        {
            var p = P[i];
            if (Part[i] == 0) return new Vector3(p.X * 0.5f, 0.6f, 0);
            if (Part[i] == 1) return new Vector3(-0.4f + (p.X + 0.4f) * 0.5f, 0.9f, 0);
            var w = Wheels.OrderBy(q => Vector3.DistanceSquared(q, p)).First();
            return Part[i] == 2 ? new Vector3(w.X, w.Y, p.Z) : w;
        }
        var vote = new Dictionary<int, float>();
        for (int i = 0; i < P.Count; i++)
        {
            N[i] = N[i].LengthSquared() > 1e-12f ? Vector3.Normalize(N[i]) : Vector3.UnitY;
            vote[Grp[i]] = vote.GetValueOrDefault(Grp[i]) + MathF.Sign(Vector3.Dot(N[i], P[i] - Inside(i)));
        }
        for (int i = 0; i < P.Count; i++) if (vote[Grp[i]] < 0) N[i] = -N[i];
        // triangles: wind them so their face points the way of their normals (back faces are skipped when drawing)
        for (int i = 0; i < Tri.Count; i += 3)
        {
            var fn = Vector3.Cross(P[Tri[i + 1]] - P[Tri[i]], P[Tri[i + 2]] - P[Tri[i]]);
            if (Vector3.Dot(fn, N[Tri[i]] + N[Tri[i + 1]] + N[Tri[i + 2]]) < 0) (Tri[i + 1], Tri[i + 2]) = (Tri[i + 2], Tri[i + 1]);
        }
    }
}

/// values of a city's lighting record the tab does not show: fog scale + height fog shape, the sky's own fog, the 4 glow sizes
public static class LightHidden
{
    static readonly Dictionary<string, float[]> _c = new();

    /// 48 floats of the record (offsets 0..188), or null
    public static float[]? Of(string? preset)
    {
        preset ??= Lighting.DefaultPreset;
        lock (_c)
        {
            if (_c.TryGetValue(preset, out var hit)) return hit;
            try
            {
                var F = new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
                int b = F.Objects[Lighting.LsObject(F, Lighting.Presets.GetValueOrDefault(preset, Lighting.Presets[Lighting.DefaultPreset]))].Blocks[0].Offset;
                var o = new float[48];
                for (int i = 0; i < 48; i++) o[i] = B.F32(F.Data, b + 4 * i);
                return _c[preset] = o;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return null; }
        }
    }
}

/// what the sliders say, in the preview's units. The game's shaders use the RGB of the sun, sky, baked and emissive colours
/// (the brightness is in the colour itself); the light on objects is ours: colour x strength, partly from above.
public sealed class LightLook
{
    public Vector3 Sun, SkyAmb, ObjAll, ObjUp, Baked, Emissive, Fog, SkyFog;
    public float SkyScale, Rot, FogDensity, HeightFog, MiddleGrey, ExpMin, ExpMax, Bloom, BloomThr;
    public float HFogHeight = 5, HFogFalloff = 1, SkyFogCurve = 16;
    public float[] BloomLevels = { 0.5f, 0.5f, 0.75f, 0.25f };

    static Vector3 Rgb(double[] v, float k = 1) => new Vector3((float)v[0], (float)v[1], (float)v[2]) * k;

    public static LightLook From(IReadOnlyDictionary<string, double[]> v, string? preset = null)
    {
        var o = new LightLook();
        double[] G(string k, int n) => v.TryGetValue(k, out var a) && a.Length >= n ? a : new double[n];
        var sn = G("sun", 4); var sk = G("sky", 4);
        // sun + sky light = colour x strength (every game city has strength 0: no sun); baked + emissive = their colour
        o.Sun = Rgb(sn, (float)sn[3]); o.SkyAmb = Rgb(sk, (float)sk[3]);
        o.Baked = Rgb(G("baked", 3)); o.Emissive = Rgb(G("emissive", 3));
        var ol = G("object_light", 4); float up = (float)G("object_light_up", 1)[0];
        o.ObjAll = Rgb(ol, (float)ol[3]); o.ObjUp = o.ObjAll * up;
        o.Fog = Rgb(G("fog_colour", 3)); o.SkyFog = o.Fog;
        float depthScale = 1, heightScale = 1;
        if (LightHidden.Of(preset) is { } h)
        {
            depthScale = h[22]; o.HFogHeight = h[23]; o.HFogFalloff = h[25]; heightScale = h[26];
            o.SkyFog = new Vector3(h[31], h[32], h[33]); o.SkyFogCurve = h[35];
            o.BloomLevels = new[] { h[42], h[43], h[44], h[45] };
        }
        o.FogDensity = (float)G("fog_density", 1)[0] * depthScale; o.HeightFog = (float)G("height_fog", 1)[0] * heightScale;
        o.SkyScale = (float)G("sky_intensity", 1)[0]; o.Rot = (float)G("sky_rotation", 1)[0] * 2 * MathF.PI;   // a part of a whole turn
        o.MiddleGrey = Math.Max((float)G("middle_grey", 1)[0], 1e-4f); o.ExpMin = (float)G("exposure_min", 1)[0]; o.ExpMax = (float)G("exposure_max", 1)[0];
        if (o.ExpMax < o.ExpMin) (o.ExpMin, o.ExpMax) = (o.ExpMax, o.ExpMin);
        o.Bloom = (float)G("bloom", 1)[0]; o.BloomThr = (float)G("bloom_threshold", 1)[0];
        return o;
    }
}

/// the preview's ground picture: the game's asphalt (tarmacplain02, the Surface tab's tarmac) as linear colour with smaller
/// copies (mip levels), its mean brightness set to a plain road's; the wet patches come from the game's cloudy mask (uv_distort_04)
static class Asphalt
{
    const float Tile = 3f, MaskTile = 30f, Mean = 0.2f;
    static List<(int W, float[] Px)>? _col, _mask;
    static readonly object _lock = new();

    static List<(int W, float[] Px)> Levels(float[] px, int w, int ch)
    {
        var o = new List<(int, float[])> { (w, px) };
        while (w > 1)
        {
            int n = w / 2; var q = new float[n * n * ch]; var p = o[^1].Item2;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    for (int c = 0; c < ch; c++)
                        q[(y * n + x) * ch + c] = 0.25f * (p[((2 * y) * w + 2 * x) * ch + c] + p[((2 * y) * w + 2 * x + 1) * ch + c] + p[((2 * y + 1) * w + 2 * x) * ch + c] + p[((2 * y + 1) * w + 2 * x + 1) * ch + c]);
            o.Add((n, q)); w = n;
        }
        return o;
    }

    static void Load()
    {
        lock (_lock)
        {
            if (_col != null) return;
            const int S = 256;
            // asphalt: the game's picture, or a grey grain made here
            var pic = GameTex.Get("tarmacplain02_diff", 512);
            var col = new float[S * S * 3];
            if (pic != null)
            {
                var p = pic.W == S && pic.H == S ? pic : pic.ResizeLanczos(S, S);
                for (int i = 0; i < col.Length; i++) { float v = p.Px[i] / 255f; col[i] = v * v * (0.31f * v + 0.69f); }
            }
            else
            {
                var rnd = new Random(5);
                for (int i = 0; i < S * S; i++) { float v = 0.18f + 0.08f * (float)rnd.NextDouble(); col[3 * i] = col[3 * i + 1] = col[3 * i + 2] = v; }
            }
            double sum = 0; for (int i = 0; i < col.Length; i++) sum += col[i];
            float k = Mean / (float)Math.Max(sum / col.Length, 1e-4);
            for (int i = 0; i < col.Length; i++) col[i] *= k;
            // wet patches: the cloudy mask, or soft blobs made here
            var m = GameTex.Get("uv_distort_04", 512);
            var mask = new float[S * S];
            if (m != null)
            {
                var p = m.W == S && m.H == S ? m : m.ResizeLanczos(S, S);
                for (int i = 0; i < S * S; i++) mask[i] = (p.Px[3 * i] + p.Px[3 * i + 1] + p.Px[3 * i + 2]) / 765f;
            }
            else
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) mask[y * S + x] = 0.5f + 0.25f * MathF.Sin(x * 0.07f) * MathF.Sin(y * 0.05f + 1) + 0.25f * MathF.Sin(x * 0.031f + y * 0.043f);
            // keep the wettest ~20 % of it
            var sorted = (float[])mask.Clone(); Array.Sort(sorted);
            float lo = sorted[(int)(0.74f * (sorted.Length - 1))], hi = sorted[(int)(0.84f * (sorted.Length - 1))];
            for (int i = 0; i < mask.Length; i++) mask[i] = Math.Clamp((mask[i] - lo) / Math.Max(hi - lo, 1e-4f), 0, 1);
            _mask = Levels(mask, S, 1);
            _col = Levels(col, S, 3);
        }
    }

    static float Pick(List<(int W, float[] Px)> lv, float tile, float lod) => Math.Clamp(MathF.Log2(Math.Max(lod, 1e-6f) * lv[0].W / tile), 0, lv.Count - 1);

    static Vector3 Sample(List<(int W, float[] Px)> lv, int level, float u, float v, int ch)
    {
        var (w, px) = lv[level];
        float fx = (u - MathF.Floor(u)) * w - 0.5f, fy = (v - MathF.Floor(v)) * w - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy); float tx = fx - x0, ty = fy - y0;
        Vector3 P(int x, int y) { x = (x % w + w) % w; y = (y % w + w) % w; int i = (y * w + x) * ch; return ch == 3 ? new Vector3(px[i], px[i + 1], px[i + 2]) : new Vector3(px[i]); }
        return Vector3.Lerp(Vector3.Lerp(P(x0, y0), P(x0 + 1, y0), tx), Vector3.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), tx), ty);
    }

    static Vector3 Trilinear(List<(int W, float[] Px)> lv, float tile, float x, float z, float lod, int ch)
    {
        float l = Pick(lv, tile, lod); int l0 = (int)l, l1 = Math.Min(l0 + 1, lv.Count - 1); float t = l - l0;
        var a = Sample(lv, l0, x / tile, z / tile, ch);
        return t < 0.01f ? a : Vector3.Lerp(a, Sample(lv, l1, x / tile, z / tile, ch), t);
    }

    public static Vector3 Colour(float x, float z, float lod) { if (_col == null) Load(); return Trilinear(_col!, Tile, x, z, lod, 3); }
    public static float Wet(float x, float z, float lod) { if (_mask == null) Load(); return Trilinear(_mask!, MaskTile, x + 7, z - 3, lod, 1).X; }
}

/// a ring of simple city blocks 250-700 m around the car (the preview's stand-in for the buildings of a map): they take the
/// fog colour with distance like placed objects do in game, their windows glow with the emissive colour (Lamps and windows glow)
static class City
{
    const int Bins = 120;                              // 3 degrees each
    static readonly float[] R = new float[Bins], Hgt = new float[Bins];
    static readonly uint[] Seed = new uint[Bins];

    static City()
    {
        var rnd = new Random(11);
        for (int i = 0; i < Bins; i++)
        {
            bool gap = rnd.NextDouble() < 0.12;
            R[i] = 250 + 450 * (float)rnd.NextDouble();
            Hgt[i] = gap ? 0 : 25 + 150 * MathF.Pow((float)rnd.NextDouble(), 1.6f);
            Seed[i] = (uint)rnd.Next();
        }
    }

    static uint Hash(uint a, uint b, uint c) { uint h = a * 0x9E3779B1u ^ b * 0x85EBCA77u ^ c * 0xC2B2AE3Du; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; return h; }

    /// the first block a ray from o along d meets: distance and the point (x, y, z), its bin in hit.W
    public static bool Hit(Vector3 o, Vector3 d, out float t, out Vector4 hit)
    {
        t = 0; hit = default;
        float hl2 = d.X * d.X + d.Z * d.Z;
        if (hl2 < 1e-8f) return false;
        float az = MathF.Atan2(d.Z, d.X); if (az < 0) az += 2 * MathF.PI;
        int bin = Math.Min(Bins - 1, (int)(az / (2 * MathF.PI) * Bins));
        if (Hgt[bin] <= 0) return false;
        // the facade: a circle of radius R around the middle
        float b = o.X * d.X + o.Z * d.Z, c = o.X * o.X + o.Z * o.Z - R[bin] * R[bin];
        float disc = b * b - hl2 * c;
        if (disc < 0) return false;
        t = (-b + MathF.Sqrt(disc)) / hl2;
        float y = o.Y + d.Y * t;
        if (t <= 0 || y < 0 || y > Hgt[bin]) return false;
        var q = o + d * t;
        hit = new Vector4(q, bin);
        return true;
    }

    /// a facade: concrete lit like the ground (baked light), windows that glow (about 1 in 3), fogged by its distance.
    /// pix = metres one screen pixel covers there (far away the windows blend into an even glow)
    public static Vector3 Shade(CarRender.Frame f, Vector4 hit, Vector3 d, float t, float pix)
    {
        int bin = (int)hit.W;
        float az = MathF.Atan2(hit.Z, hit.X); if (az < 0) az += 2 * MathF.PI;
        float u = az * R[bin], v = hit.Y;
        const float CellW = 2.4f, CellH = 3.1f, Lit = 0.22f;
        uint sd = City.Seed[bin];
        float shade = 0.05f + 0.09f * ((sd >> 8) & 255) / 255f;
        var facing = Vector3.Normalize(new Vector3(-hit.X, 0, -hit.Z));      // the facade looks at the middle
        var wall = new Vector3(shade, shade * 1.03f, shade * 1.08f) * (f.L.Baked * 0.6f + f.L.SkyAmb + f.L.Sun * Math.Max(0, Vector3.Dot(facing, f.SunDir)));
        var glow = ((sd & 1) == 0 ? new Vector3(1f, 0.8f, 0.5f) : new Vector3(0.75f, 0.85f, 1f)) * f.L.Emissive * 0.6f;
        float k = Math.Clamp((pix - CellW * 0.25f) / (CellW * 0.5f), 0, 1);    // 0 = windows sharp, 1 = an even glow
        var even = wall * 0.75f + glow * (Lit * 0.2f);
        Vector3 c = even;
        if (k < 1)
        {
            int cx = (int)MathF.Floor(u / CellW), cy = (int)MathF.Floor(v / CellH);
            float fx = u / CellW - cx, fy = v / CellH - cy;
            bool window = fx > 0.25f && fx < 0.75f && fy > 0.3f && fy < 0.7f;
            bool on = (Hash((uint)cx, (uint)cy, City.Seed[bin]) & 0xFFFF) < Lit * 65536;
            var sharp = window ? (on ? glow : wall * 0.4f) : wall;
            c = Vector3.Lerp(sharp, even, k);
        }
        return Vector3.Lerp(f.L.Fog, c, MathF.Exp(-(f.L.FogDensity * t + 0)));
    }
}

/// draws the car scene into a bitmap
static class CarRender
{
    const float GroundAlbedo = 0.2f;

    internal sealed class Frame
    {
        public SkyEnv Env = null!; public LightLook L = null!; public CarMesh Car = null!;
        public Vector3 SunDir, Eye, Fw, Rt, Up; public float Cos, Sin, Focal; public int W, H;
        public Vector3 GroundIrr, GroundRad; public Vector3[] Cube = new Vector3[6];
        public float Refl = 1;      // the car's reflections: the game draws them from a picture of the scene that already has the exposure in it
    }

    public static List<string>? Prof;
    static float _lastE = -1;   // the last picture's exposure (the game's reflection picture is from the frame before, too)
    public static float Yaw = -0.75f, Pitch = 0.15f;   // the camera, kept between rebuilds of the tab

    // buffers kept between pictures (drawn on the window's thread only): no garbage while dragging
    static int[]? _tri, _mtri; static float[]? _depth, _b1, _b2, _hdr, _mdepth, _mb1, _mb2;
    static T[] Buf<T>(ref T[]? a, int n) { if (a == null || a.Length < n) a = new T[n]; return a; }

    public static Bitmap Render(int w, int h, int ss, SkyEnv env, LightLook L, CarMesh car, out double ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int W = w * ss, H = h * ss, Np = W * H;
        var f = new Frame { Env = env, L = L, Car = car, Refl = _lastE > 0 ? Math.Clamp(_lastE, L.ExpMin, Math.Max(L.ExpMax, L.ExpMin)) : (L.ExpMin + L.ExpMax) / 2, W = W, H = H, Cos = MathF.Cos(L.Rot), Sin = MathF.Sin(L.Rot) };
        f.SunDir = Rotate(f, env.SunDir);
        // camera
        var target = new Vector3(0, car.Height * 0.45f, 0); float dist = 2.3f * car.HalfLen;
        f.Eye = target + dist * new Vector3(MathF.Cos(Pitch) * MathF.Cos(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Sin(Yaw));
        f.Fw = Vector3.Normalize(target - f.Eye); f.Rt = Vector3.Normalize(Vector3.Cross(f.Fw, Vector3.UnitY)); f.Up = Vector3.Cross(f.Rt, f.Fw);
        f.Focal = H / 2f / MathF.Tan(42f * MathF.PI / 360f);   // a wider lens, like the game's chase camera
        // the light that falls on the ground (sky from above, sky light, baked) and the ground's own brightness
        // the ground is lit like the game's track: its baked light, the sky light, and the sun (with the car's shadow, below)
        f.GroundIrr = L.Baked * 0.6f + L.SkyAmb;
        f.GroundRad = GroundAlbedo * f.GroundIrr;
        for (int a = 0; a < 6; a++) f.Cube[a] = Rot6(env, f, a) * L.SkyScale + (a == 3 ? f.GroundRad : a == 2 ? Vector3.Zero : f.GroundRad * 0.5f);
        // the car into a buffer: which triangle + where on it, per pixel. mirror = the car upside down under the ground (what
        // the wet ground mirrors: a mirror on the ground plane shows the car at the same place on the screen)
        var tri = car.Tri; int nt = car.Triangles; var CP = car.P; int nv = CP.Length;
        var sx = new float[nv]; var sy = new float[nv]; var iz = new float[nv];
        const int Band = 16; int nb = (H + Band - 1) / Band;
        void Raster(bool mirror, int[] triId, float[] depth, float[] b1, float[] b2)
        {
            Array.Fill(triId, -1, 0, Np); Array.Clear(depth, 0, Np);
            for (int i = 0; i < nv; i++)
            {
                var q = mirror ? new Vector3(CP[i].X, -CP[i].Y, CP[i].Z) : CP[i];
                var v = q - f.Eye; float z = Vector3.Dot(v, f.Fw);
                iz[i] = 1 / Math.Max(z, 0.05f);
                sx[i] = W / 2f + Vector3.Dot(v, f.Rt) * iz[i] * f.Focal; sy[i] = H / 2f - Vector3.Dot(v, f.Up) * iz[i] * f.Focal;
            }
            bool cull = !car.Game;   // the game car is drawn from both sides
            var bins = new List<int>[nb]; for (int i = 0; i < nb; i++) bins[i] = new List<int>();
            for (int t = 0; t < nt; t++)
            {
                int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                float area = (sx[b] - sx[a]) * (sy[c] - sy[a]) - (sx[c] - sx[a]) * (sy[b] - sy[a]);
                if (mirror) area = -area;
                if ((area >= 0 && cull) || MathF.Abs(area) < 1e-9f) continue;   // back face (screen y goes down) / no area
                int y0 = Math.Max(0, (int)MathF.Floor(Math.Min(sy[a], Math.Min(sy[b], sy[c])))), y1 = Math.Min(H - 1, (int)MathF.Ceiling(Math.Max(sy[a], Math.Max(sy[b], sy[c]))));
                for (int k = y0 / Band; k <= y1 / Band && k < nb; k++) bins[k].Add(t);
            }
            Parallel.For(0, nb, k =>
            {
                int r0 = k * Band, r1 = Math.Min(H, r0 + Band);
                foreach (int t in bins[k])
                {
                    int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                    float ax = sx[a], ay = sy[a], bx = sx[b], by = sy[b], cx = sx[c], cy = sy[c];
                    float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay), inv = 1 / area;
                    int x0 = Math.Max(0, (int)MathF.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(W - 1, (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                    int y0 = Math.Max(r0, (int)MathF.Floor(Math.Min(ay, Math.Min(by, cy)))), y1 = Math.Min(r1 - 1, (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                    for (int y = y0; y <= y1; y++)
                    {
                        float py = y + 0.5f;
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f;
                            float w0 = ((bx - px) * (cy - py) - (cx - px) * (by - py)) * inv;
                            float w1 = ((cx - px) * (ay - py) - (ax - px) * (cy - py)) * inv;
                            float w2 = 1 - w0 - w1;
                            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                            float z = w0 * iz[a] + w1 * iz[b] + w2 * iz[c];
                            int p = y * W + x;
                            if (z <= depth[p]) continue;
                            depth[p] = z; triId[p] = t;
                            b1[p] = w1 * iz[b] / z; b2[p] = w2 * iz[c] / z;   // perspective correct
                        }
                    }
                }
            });
        }
        var triId = Buf(ref _tri, Np); var depth = Buf(ref _depth, Np); var b1 = Buf(ref _b1, Np); var b2 = Buf(ref _b2, Np);
        Raster(false, triId, depth, b1, b2);
        var mTri = Buf(ref _mtri, Np); var mDepth = Buf(ref _mdepth, Np); var mb1 = Buf(ref _mb1, Np); var mb2 = Buf(ref _mb2, Np);
        Raster(true, mTri, mDepth, mb1, mb2);
        float tanH = 1 / f.Focal;
        Vector3 Ray(int x, int y) => Vector3.Normalize(f.Fw + f.Rt * ((x + 0.5f - W / 2f) * tanH) - f.Up * ((y + 0.5f - H / 2f) * tanH));
        Vector3 CarAt(int t, float u1, float u2, Vector3 d)
        {
            int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2]; float u0 = 1 - u1 - u2;
            var pos = car.P[a] * u0 + car.P[b] * u1 + car.P[c] * u2;
            var n = Vector3.Normalize(car.N[a] * u0 + car.N[b] * u1 + car.N[c] * u2);
            return car.Game ? ShadeGame(f, pos, n, d, car.Mat[t], car.UV[a] * u0 + car.UV[b] * u1 + car.UV[c] * u2) : ShadeCar(f, pos, n, d, car.Mat[t]);
        }
        Prof?.Add("raster " + sw.Elapsed.TotalMilliseconds.ToString("0"));
        // shade
        var hdr = Buf(ref _hdr, Np * 3);
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                int p = y * W + x;
                var d = Ray(x, y);
                Vector3 col;
                if (triId[p] >= 0)
                {
                    int t = triId[p], a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                    float u1 = b1[p], u2 = b2[p], u0 = 1 - u1 - u2;
                    var pos = car.P[a] * u0 + car.P[b] * u1 + car.P[c] * u2;
                    col = Fogged(f, CarAt(t, u1, u2, d), Vector3.Distance(pos, f.Eye), pos.Y, false);
                }
                else if (d.Y < -1e-4f)
                {
                    float t = -f.Eye.Y / d.Y; var g = f.Eye + d * t;
                    // the car seen in the ground's mirror (the ray bounces up from the ground)
                    Vector3? mirror = null;
                    if (mTri[p] >= 0 && Asphalt.Wet(g.X, g.Z, 0.05f) > 0.01f) mirror = CarAt(mTri[p], mb1[p], mb2[p], Vector3.Reflect(d, Vector3.UnitY));
                    if (City.Hit(f.Eye, d, out float tb, out var hit) && tb < t) col = City.Shade(f, hit, d, tb, tb / f.Focal);
                    else
                    {
                        col = Ground(f, g, d, true, t / (f.Focal * Math.Max(-d.Y, 0.05f)), mirror);
                        col = Fogged(f, col, t, 0, true);
                    }
                }
                else col = Far(f, f.Eye, d, 1 / f.Focal);
                hdr[3 * p] = col.X; hdr[3 * p + 1] = col.Y; hdr[3 * p + 2] = col.Z;
            }
        });
        Prof?.Add("shade " + sw.Elapsed.TotalMilliseconds.ToString("0"));
        // exposure: middle grey / the picture's average (log) brightness, centre weighted, kept so the exposure stays between min and max
        double sum = 0, wsum = 0;
        for (int y = 0; y < H; y += 3)
            for (int x = 0; x < W; x += 3)
            {
                int p = y * W + x;
                float dx = (x + 0.5f) / W - 0.5f, dy = (y + 0.5f) / H - 0.5f, wt = Math.Clamp(1 - MathF.Sqrt(dx * dx + dy * dy) * 1.2f, 0, 1);
                float lum = 0.2125f * hdr[3 * p] + 0.7154f * hdr[3 * p + 1] + 0.0721f * hdr[3 * p + 2];
                sum += Math.Log(lum + 1e-4) * wt; wsum += wt;
            }
        float avg = (float)Math.Exp(sum / Math.Max(wsum, 1e-6));
        float E = L.MiddleGrey / (avg + 0.001f);
        if (L.ExpMax > 0) E = Math.Clamp(E, L.ExpMin, L.ExpMax);
        _lastE = E;
        Prof?.Add("expo " + sw.Elapsed.TotalMilliseconds.ToString("0"));
        // bloom: the bright part (soft: x / (offset + x), x 1.5), at a quarter size, blurred at 4 sizes (each twice the last) and
        // mixed with the city's 4 glow levels x Glow
        int q = 4 * ss, BW = (W + q - 1) / q, BH = (H + q - 1) / q;
        var bl = new float[BW * BH * 3];
        float bloomK = 0;
        if (L.Bloom > 0)
        {
            float off = Math.Max(2f, L.BloomThr);
            var bright = new float[bl.Length];
            Parallel.For(0, BH, by =>
            {
                for (int bx = 0; bx < BW; bx++)
                {
                    float r = 0, g = 0, b = 0; int k = 0;
                    for (int j = 0; j < q; j++)
                    {
                        int yy = by * q + j; if (yy >= H) break;
                        for (int i = 0; i < q; i++)
                        {
                            int xx = bx * q + i; if (xx >= W) break;
                            int s = 3 * (yy * W + xx);
                            float cr = hdr[s] * E, cg = hdr[s + 1] * E, cb = hdr[s + 2] * E, lum = 0.30f * cr + 0.59f * cg + 0.11f * cb;
                            float keep = lum > 1e-6f ? Math.Clamp(lum - L.BloomThr, 0, 1) / lum : 0;
                            cr *= keep; cg *= keep; cb *= keep;
                            r += cr / (off + cr); g += cg / (off + cg); b += cb / (off + cb); k++;
                        }
                    }
                    int o = 3 * (by * BW + bx); bright[o] = 1.5f * r / k; bright[o + 1] = 1.5f * g / k; bright[o + 2] = 1.5f * b / k;
                }
            });
            float px = BH / 180f;     // the game's quarter-size picture is 180 high (720p)
            for (int lv = 0; lv < 4; lv++)
            {
                float sc = L.Bloom * L.BloomLevels[lv];
                if (sc <= 0) continue;
                var a = (float[])bright.Clone();
                int rad = Math.Max(1, (int)MathF.Round(2 * (1 << lv) * px));
                for (int pass = 0; pass < 3; pass++) { BoxBlur(a, BW, BH, rad, true); BoxBlur(a, BW, BH, rad, false); }
                for (int i = 0; i < bl.Length; i++) bl[i] += a[i] * sc;
                bloomK = 1;
            }
        }
        Prof?.Add("bloom " + sw.Elapsed.TotalMilliseconds.ToString("0"));
        // straight (no curve): exposed colour + bloom, gamma 2.2; ss x ss pixels -> 1
        var bm = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        var data = bm.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        var outPx = new int[w * h];
        float inv2 = 1f / (ss * ss);
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0;
                for (int j = 0; j < ss; j++)
                    for (int i = 0; i < ss; i++)
                    {
                        int X = x * ss + i, Y = y * ss + j, s = 3 * (Y * W + X);
                        var bv = bloomK > 0 ? Bilinear(bl, BW, BH, (X + 0.5f) / q - 0.5f, (Y + 0.5f) / q - 0.5f) * bloomK : Vector3.Zero;
                        r += Math.Clamp(hdr[s] * E + bv.X, 0, 1); g += Math.Clamp(hdr[s + 1] * E + bv.Y, 0, 1); b += Math.Clamp(hdr[s + 2] * E + bv.Z, 0, 1);
                    }
                outPx[y * w + x] = Byte(r * inv2) << 16 | Byte(g * inv2) << 8 | Byte(b * inv2);
            }
        });
        System.Runtime.InteropServices.Marshal.Copy(outPx, 0, data.Scan0, outPx.Length);
        bm.UnlockBits(data);
        Prof?.Add("out " + sw.Elapsed.TotalMilliseconds.ToString("0"));
        ms = sw.Elapsed.TotalMilliseconds;
        return bm;
    }

    static Vector3 Bilinear(float[] a, int w, int h, float fx, float fy)
    {
        fx = Math.Clamp(fx, 0, w - 1); fy = Math.Clamp(fy, 0, h - 1);
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1); float tx = fx - x0, ty = fy - y0;
        Vector3 P(int x, int y) { int i = 3 * (y * w + x); return new Vector3(a[i], a[i + 1], a[i + 2]); }
        return Vector3.Lerp(Vector3.Lerp(P(x0, y0), P(x1, y0), tx), Vector3.Lerp(P(x0, y1), P(x1, y1), tx), ty);
    }

    static int Byte(float v) => (int)(Math.Clamp(MathF.Pow(Math.Max(v, 0), 1 / 2.2f), 0, 1) * 255 + 0.5f);

    static void BoxBlur(float[] a, int w, int h, int r, bool horiz)
    {
        var o = new float[a.Length];
        int n = horiz ? h : w, len = horiz ? w : h;
        Parallel.For(0, n, line =>
        {
            for (int c = 0; c < 3; c++)
            {
                float s = 0; int k = 0;
                int At(int i) => 3 * (horiz ? line * w + i : i * w + line) + c;
                for (int i = 0; i <= Math.Min(r, len - 1); i++) { s += a[At(i)]; k++; }
                for (int i = 0; i < len; i++)
                {
                    o[At(i)] = s / k;
                    int add = i + r + 1, del = i - r;
                    if (add < len) { s += a[At(add)]; k++; }
                    if (del >= 0) { s -= a[At(del)]; k--; }
                }
            }
        });
        Array.Copy(o, a, a.Length);
    }

    // the picture turns around the up axis by the sky rotation
    static Vector3 Rotate(Frame f, Vector3 d) => new(d.X * f.Cos - d.Z * f.Sin, d.Y, d.X * f.Sin + d.Z * f.Cos);
    static Vector3 Unrotate(Frame f, Vector3 d) => new(d.X * f.Cos + d.Z * f.Sin, d.Y, -d.X * f.Sin + d.Z * f.Cos);

    /// the cube light of the sky turned with the picture (faces X and Z mix; up and down stay)
    static Vector3 Rot6(SkyEnv e, Frame f, int a)
    {
        if (a == 2 || a == 3) return e.Cube[a];
        var axis = a switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 4 => Vector3.UnitZ, _ => -Vector3.UnitZ };
        var d = Unrotate(f, axis);
        Vector3 Pos(float v, int p, int m) => v >= 0 ? e.Cube[p] * (v * v) : e.Cube[m] * (v * v);
        return Pos(d.X, 0, 1) + Pos(d.Z, 4, 5);
    }

    static Vector3 Irr(Frame f, Vector3 n)
    {
        var c = f.Cube; var n2 = n * n;
        return n2.X * (n.X >= 0 ? c[0] : c[1]) + n2.Y * (n.Y >= 0 ? c[2] : c[3]) + n2.Z * (n.Z >= 0 ? c[4] : c[5]);
    }

    static Vector3 Sample(float[] px, int w, int h, Vector3 d)
    {
        float u = MathF.Atan2(d.Z, d.X) / (2 * MathF.PI); if (u < 0) u += 1;
        float v = 1 - MathF.Asin(Math.Clamp(d.Y, 0, 1)) / (MathF.PI / 2);
        float fx = u * w - 0.5f, fy = Math.Clamp(v * h - 0.5f, 0, h - 1);
        int x0 = (int)MathF.Floor(fx), y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
        int x1 = ((x0 + 1) % w + w) % w; x0 = (x0 % w + w) % w; int y1 = Math.Min(h - 1, y0 + 1);
        Vector3 P(int x, int y) { int i = 3 * (y * w + x); return new Vector3(px[i], px[i + 1], px[i + 2]); }
        return Vector3.Lerp(Vector3.Lerp(P(x0, y0), P(x1, y0), tx), Vector3.Lerp(P(x0, y1), P(x1, y1), tx), ty);
    }

    static Vector3 SkyL(Frame f, Vector3 d, bool soft = false)
    {
        var q = Unrotate(f, d);
        return (soft ? Sample(f.Env.Blur, 32, 8, q) : Sample(f.Env.Px, SkyEnv.W, SkyEnv.H, q)) * f.L.SkyScale;
    }

    /// how much height fog a ray from (eye height) to (point height), this long, goes through (fog under the layer's top)
    static float HeightFogAmount(Frame f, float wy, float ey, float dist)
    {
        float f0 = wy - f.L.HFogHeight, f1 = ey - f.L.HFogHeight, mn = Math.Min(f0, f1), mx = Math.Max(f0, f1);
        float ratio = mn - mx < -1e-6f ? Math.Clamp(mn / (mn - mx), 0, 1) : (mn < 0 ? 1 : 0);
        return ratio * dist * Math.Clamp(-mn * f.L.HFogFalloff, 0, 1) * f.L.HeightFog;
    }

    static Vector3 SkyFogged(Frame f, Vector3 d)
    {
        float v = 1 - MathF.Asin(Math.Clamp(d.Y, 0, 1)) / (MathF.PI / 2);   // 0 straight up, 1 at the horizon
        float fog = MathF.Pow(Math.Clamp(v, 0, 1), Math.Max(f.L.SkyFogCurve, 0.01f));
        return Vector3.Lerp(SkyL(f, d), f.L.SkyFog, fog);
    }

    static Vector3 Fogged(Frame f, Vector3 c, float dist, float y, bool height)
    {
        float tau = f.L.FogDensity * dist + (height ? HeightFogAmount(f, y, f.Eye.Y, dist) : 0);
        return Vector3.Lerp(f.L.Fog, c, MathF.Exp(-tau));
    }

    static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    /// distance from the car's outline seen from above (a rounded box), < 0 = under the car
    static float Footprint(Frame f, float x, float z, float grow)
    {
        float hx = f.Car.HalfLen - 0.2f + grow, hz = f.Car.HalfWid - 0.12f + grow, r = 0.45f + grow;
        float qx = MathF.Abs(x) - (hx - r), qz = MathF.Abs(z) - (hz - r);
        return MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qz, 0) * Math.Max(qz, 0)) + Math.Min(Math.Max(qx, qz), 0) - r;
    }

    /// the ground: asphalt-grey, darker under the car (soft), the sun's shadow (softer the further from the car), a faint sheen
    /// the ground: the game's asphalt picture (tiled every 3 m, smaller copies far away so it does not flicker), darker under the
    /// car (soft), wet patches here and there that mirror the sky (sharper and stronger at low angles), a faint sheen elsewhere.
    /// lod = how many metres one screen pixel covers there
    static Vector3 Ground(Frame f, Vector3 g, Vector3 d, bool sheen, float lod, Vector3? mirror = null)
    {
        float dd = Footprint(f, g.X, g.Z, 0);
        float ao = 0.12f + 0.88f * Smooth(-0.45f, 1.1f, dd);
        float wheel = 1;
        foreach (var w in f.Car.Wheels) { float r = MathF.Sqrt((g.X - w.X) * (g.X - w.X) + (g.Z - w.Z) * (g.Z - w.Z)); wheel *= 0.55f + 0.45f * Smooth(0.05f, 0.45f, r); }
        ao *= wheel;
        var alb = Asphalt.Colour(g.X, g.Z, lod);
        float wet = Asphalt.Wet(g.X, g.Z, lod);
        alb *= 1 - 0.55f * wet;                                     // wet asphalt is darker
        var sunLit = Vector3.Zero;
        if (f.L.Sun.LengthSquared() > 1e-8f && f.SunDir.Y > 0.02f)
        {
            float h = 0.75f, k = h / f.SunDir.Y;
            float ds = Footprint(f, g.X - f.SunDir.X * k, g.Z - f.SunDir.Z * k, -0.05f);
            float soft = 0.15f + 0.25f * Math.Min(3, k);
            sunLit = f.L.Sun * (f.SunDir.Y * Smooth(-soft, soft, ds));
        }
        var c = alb * (f.GroundIrr * ao + sunLit);
        if (!sheen) return c;
        float cv = Math.Max(0, -d.Y);
        float F = 0.02f + 0.30f * MathF.Pow(1 - cv, 5);
        float Fw = 0.03f + 0.97f * MathF.Pow(1 - cv, 5);              // water
        var r2 = Vector3.Reflect(d, Vector3.UnitY);
        var dry = c * (1 - F) + (mirror ?? SkyL(f, r2, true)) * (F * ao);
        if (wet <= 0) return dry;
        var water = c * (1 - Fw) + (mirror ?? Far(f, g, r2, lod * 0.5f)) * (Fw * (mirror != null ? 1 : 0.5f + 0.5f * ao));
        return Vector3.Lerp(dry, water, wet);
    }

    /// what a reflected ray sees: the sky, or the ground (with the car's shadow)
    /// what lies far along a ray that does not hit the ground: a building of the city ring (fogged) or the sky
    static Vector3 Far(Frame f, Vector3 o, Vector3 d, float pix)
    {
        if (City.Hit(o, d, out float t, out var hit)) return City.Shade(f, hit, d, t, t * pix);
        return SkyFogged(f, d);
    }

    static Vector3 Seen(Frame f, Vector3 pos, Vector3 r, bool soft)
    {
        if (r.Y >= 0) return City.Hit(pos, r, out float tb, out var hit) ? City.Shade(f, hit, r, tb, soft ? 8 : tb * 0.004f) : SkyL(f, r, soft);
        float t = -pos.Y / r.Y; var g = pos + r * t;
        var c = Ground(f, g, r, false, 0.2f);
        return Vector3.Lerp(f.L.Fog, c, MathF.Exp(-(f.L.FogDensity * t + HeightFogAmount(f, 0, pos.Y, t))));
    }

    /// the game car: its body picture for the paint, the other parts by their kind (glass, lamps, tyres, rims, trim)
    static Vector3 ShadeGame(Frame f, Vector3 p, Vector3 n, Vector3 d, int mat, Vector2 uv)
    {
        var L = f.L; var v = -d;
        float nv = Vector3.Dot(n, v);
        if (nv < 0) { n = -n; nv = -nv; }          // seen from its back side
        nv = Math.Max(nv, 0.01f);
        var r = Vector3.Reflect(d, n);
        float ao = (0.5f + 0.5f * Smooth(0.0f, 0.6f, p.Y)) * (n.Y < 0 ? 1 + 0.5f * n.Y : 1);
        Vector3 alb; float rMin, rMax; bool soft = false, matt = false; Vector3 lamp = Vector3.Zero;
        switch (mat)
        {
            case CarMesh.Paint: alb = f.Car.TexAt(uv); rMin = 0.05f; rMax = 0.6f; break;
            case CarMesh.PlainPaint: alb = f.Car.PaintColour; rMin = 0.05f; rMax = 0.6f; break;
            case CarMesh.Glass: alb = new Vector3(0.015f); rMin = 0.15f; rMax = 0.95f; break;
            case CarMesh.LampGlass: alb = new Vector3(0.05f); rMin = 0.2f; rMax = 0.9f; break;
            case CarMesh.HeadLamp: alb = new Vector3(0.7f); rMin = 0.5f; rMax = 0.9f; soft = true; lamp = new Vector3(1f, 0.96f, 0.88f); break;
            case CarMesh.RedLamp: alb = new Vector3(0.4f, 0.02f, 0.02f); rMin = 0.1f; rMax = 0.6f; lamp = new Vector3(0.9f, 0.04f, 0.02f); break;
            case CarMesh.AmberLamp: alb = new Vector3(0.5f, 0.25f, 0.02f); rMin = 0.1f; rMax = 0.6f; lamp = new Vector3(0.9f, 0.45f, 0.05f) * 0.6f; break;
            case CarMesh.Tyre: alb = new Vector3(0.03f); rMin = 0; rMax = 0.06f; matt = true; break;
            case CarMesh.Rim: alb = new Vector3(0.55f, 0.56f, 0.58f); rMin = 0.55f; rMax = 0.9f; soft = true; break;
            case CarMesh.Plate: alb = new Vector3(0.7f); rMin = 0.05f; rMax = 0.3f; break;
            case CarMesh.Disc: alb = new Vector3(0.3f); rMin = 0.2f; rMax = 0.5f; soft = true; break;
            case CarMesh.Caliper: alb = new Vector3(0.45f, 0.04f, 0.03f); rMin = 0.05f; rMax = 0.3f; break;
            default: alb = new Vector3(0.03f); rMin = 0.04f; rMax = 0.4f; soft = true; break;
        }
        var sh = Vector3.Clamp(L.ObjAll + L.ObjUp * (0.8f * n.Y + 0.075f * n.X - 0.075f * n.Z), Vector3.Zero, Vector3.One);
        var lighting = sh * L.Baked * ao;
        float refl = rMin + (rMax - rMin) * MathF.Pow(1 - nv, mat is CarMesh.Paint or CarMesh.PlainPaint or CarMesh.Glass ? 2.5f : 1f);
        float rimFade = Math.Clamp(nv / 0.1f, 0, 1) * 0.9f + 0.1f;
        var col = lighting * alb * (1 - refl);
        if (!matt) col += Seen(f, p, r, soft) * (refl * rimFade * ao * f.Refl);
        if (!matt) col += lighting * (Math.Clamp(n.Y * 0.75f + 0.25f, 0, 1) * MathF.Pow(1 - nv, 15));
        if (lamp != Vector3.Zero) col = Vector3.Max(col, lamp);
        return col;
    }

    static Vector3 ShadeCar(Frame f, Vector3 p, Vector3 n, Vector3 d, int part)
    {
        var L = f.L; var v = -d;
        float nv = Vector3.Dot(n, v);
        if (nv <= 0.01f) { n = Vector3.Normalize(n + v * (0.01f - nv)); nv = 0.01f; }
        var r = Vector3.Reflect(d, n);
        float ao = (0.45f + 0.55f * Smooth(0.05f, 0.75f, p.Y)) * (n.Y < 0 ? 1 + 0.6f * n.Y : 1);
        // material: colour, reflectivity facing you / at the edge, soft reflection, lamp
        Vector3 alb; float rMin, rMax; bool soft = false, matt = false; Vector3 lamp = Vector3.Zero;
        float az = MathF.Abs(p.Z);
        var paint = new Vector3(0.42f, 0.03f, 0.025f);
        var dark = new Vector3(0.03f, 0.03f, 0.033f);
        switch (part)
        {
            case 0:
                alb = paint; rMin = 0.06f; rMax = 0.85f;
                if (p.X > 1.92f && n.X > 0.25f && p.Y > 0.56f && p.Y < 0.72f && az > 0.42f && az < 0.84f) { alb = new Vector3(0.6f); rMin = 0.5f; rMax = 0.9f; lamp = new Vector3(1f, 0.96f, 0.88f) * 1.6f; }
                else if (p.X < -1.95f && n.X < -0.25f && p.Y > 0.64f && p.Y < 0.80f && az > 0.38f && az < 0.86f) { alb = new Vector3(0.35f, 0.01f, 0.01f); rMin = 0.1f; rMax = 0.6f; lamp = new Vector3(0.9f, 0.04f, 0.02f); }
                else if (p.X > 1.98f && n.X > 0.4f && p.Y > 0.33f && p.Y < 0.52f && az < 0.62f) { alb = dark; rMin = 0.04f; rMax = 0.3f; soft = true; }
                else if (p.Y < 0.31f) { alb = dark; rMin = 0.03f; rMax = 0.2f; soft = true; }
                break;
            case 1:
            {
                bool corner = MathF.Abs(n.X) > 0.33f && MathF.Abs(n.Z) > 0.33f;
                bool bPillar = MathF.Abs(n.Z) > 0.6f && MathF.Abs(p.X + 0.42f) < 0.07f;
                if (p.Y > 0.97f && p.Y < 1.30f && !corner && !bPillar) { alb = new Vector3(0.015f); rMin = 0.18f; rMax = 0.95f; }
                else { alb = paint; rMin = 0.06f; rMax = 0.85f; }
                break;
            }
            case 2: case 3: alb = dark; rMin = 0.0f; rMax = 0.08f; matt = true; break;
            default:
            {
                var w = PreviewCar.Wheels[part - 10];
                float dx = p.X - w.X, dy = p.Y - w.Y, rr = MathF.Sqrt(dx * dx + dy * dy), ang = MathF.Atan2(dy, dx);
                bool metal = rr < 0.075f || (rr > 0.225f && rr < 0.262f) || (rr < 0.23f && MathF.Cos(5 * ang) > 0.35f);
                if (rr >= 0.262f) { alb = dark; rMin = 0; rMax = 0.08f; matt = true; }
                else if (metal) { alb = new Vector3(0.5f, 0.51f, 0.53f); rMin = 0.55f; rMax = 0.9f; soft = true; }
                else { alb = new Vector3(0.01f); rMin = 0; rMax = 0.05f; matt = true; ao *= 0.4f; }
                break;
            }
        }
        // the light on placed objects (as light samples: overall + from above), at most 1, times the baked colour
        var sh = L.ObjAll + L.ObjUp * (0.8f * n.Y + 0.075f * n.X - 0.075f * n.Z);
        sh = Vector3.Clamp(sh, Vector3.Zero, Vector3.One);
        var lighting = sh * L.Baked * ao;
        // reflectivity from facing (low) to the edge (high); reflections fade right at the outline
        float refl = rMin + (rMax - rMin) * MathF.Pow(1 - nv, part == 0 || part == 1 ? 2.5f : 1f);
        float rimFade = Math.Clamp(nv / 0.1f, 0, 1) * 0.9f + 0.1f;
        var col = lighting * alb * (1 - refl);
        if (!matt) col += Seen(f, p, r, soft) * (refl * rimFade * ao * f.Refl);
        // rim light: the car's own light again at grazing angles, mostly on the top
        if (!matt) col += lighting * (Math.Clamp(n.Y * 0.75f + 0.25f, 0, 1) * MathF.Pow(1 - nv, 15));
        // lamps light themselves
        if (lamp != Vector3.Zero) col = Vector3.Max(col, lamp);
        return col;
    }
}

/// the preview box: shows the picture, drag to turn; a fast low picture while moving, the sharp one shortly after
public sealed class LightPreviewBox : Control
{
    Bitmap? _img; SkyEnv? _env; Dictionary<string, double[]>? _vals;
    readonly System.Windows.Forms.Timer _sharp = new() { Interval = 140 };
    Point _down; bool _drag; float _yaw0, _pitch0;
    public double LastMs;
    public string Caption = "";
    public string? Preset;

    public LightPreviewBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black; Cursor = Cursors.SizeAll;
        _sharp.Tick += (_, _) => { _sharp.Stop(); Draw(2); };
        Look.Tip(this, "Drag to turn around the car");
    }

    public void Show(SkyEnv env, Dictionary<string, double[]> vals, bool fast)
    {
        _env = env; _vals = vals;
        if (fast) { Draw(1); _sharp.Stop(); _sharp.Start(); } else Draw(2);
    }

    void Draw(int ss)
    {
        if (_env == null || _vals == null || Width < 8 || Height < 8 || IsDisposed) return;
        var bm = CarRender.Render(Width, Height, ss, _env, LightLook.From(_vals, Preset), CarMesh.Get(), out LastMs);
        var old = _img; _img = bm; old?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_img != null) g.DrawImageUnscaled(_img, 0, 0); else g.Clear(Color.Black);
        using var f = new Font("Segoe UI", 8.5f);
        using var sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        void Text(string s, float x, float y, Color c)
        {
            s = Lang.T(s);
            var sz = g.MeasureString(s, f);
            g.FillRectangle(sh, x - 3, y - 1, sz.Width + 4, sz.Height + 1);
            using var b = new SolidBrush(c); g.DrawString(s, f, b, x, y);
        }
        Text(Caption, 8, 6, Color.White);
        Text("drag to turn", 8, Height - 22, ColorTranslator.FromHtml("#b8c6d6"));
        base.OnPaint(e);
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _drag = true; _down = e.Location; _yaw0 = CarRender.Yaw; _pitch0 = CarRender.Pitch; } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag)
        {
            CarRender.Yaw = _yaw0 + (e.X - _down.X) * 0.01f;
            CarRender.Pitch = Math.Clamp(_pitch0 + (e.Y - _down.Y) * 0.006f, 0.03f, 1.2f);
            Draw(1);
            Update();
        }
        base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { if (_drag) { _drag = false; _sharp.Stop(); _sharp.Start(); } base.OnMouseUp(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _sharp.Dispose(); _img?.Dispose(); _img = null; }
        base.Dispose(disposing);
    }
}

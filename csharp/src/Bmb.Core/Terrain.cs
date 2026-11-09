// Hills (Landscape tab), jump ramps (3D tab library) and bumps (Surface tab). All three become extra ground models (the same
// kind as painted surfaces: Chunk -> RoadMesh model) plus drivable collision (patches) and solid sides (pieces).
//   map["land"]  = { "h": { "i,j": metres, ... }, "ground": "grass",         heights on an 8 m grid, always >= 0
//                    "auto_tex": true / false, "paint": { "i,j": surface } }  texture by slope, or painted 8 m cells
// The ground between the 8 m points is a smooth curve (cubic B-spline), drawn and built on a finer grid when there is room.
//   map["objects"][n] with a "ramp:..." library id (+ its "ramp" settings), moved / turned / scaled by map["edits"]["obj:n"]
//   map["surface"]["bumps"] = { "i,j": strength }  on the 2 m surface grid, + "bump_h" (m), "bump_size" (m)
// A map can hold 64 road models in all (RoadMesh.Uids): hills get coarser until they fit what the road leaves.
using System.Globalization;

namespace Bmb.Core;

public static class Terrain
{
    public const double Step = 8.0, RoadClear = 4.0, RoadBlend = 26.0, MaxHeight = 150.0, Lift = 0.03;
    public const int GridMax = 40, ModelCap = 64, HillCap = 40, BumpCap = 12;
    public static readonly string[] Grounds = { "grass", "drygrass", "dirt", "roughdirt", "gravel", "sand", "rock", "concrete" };
    /// what the Paint tool of the Landscape tab offers
    public static readonly string[] PaintKeys = { "grass", "drygrass", "dirt", "roughdirt", "gravel", "sand", "rock", "concrete", "tarmac" };

    static string K(int i, int j) => i.ToString(CultureInfo.InvariantCulture) + "," + j.ToString(CultureInfo.InvariantCulture);
    static (int, int) P(string k) { int c = k.IndexOf(','); return (int.Parse(k[..c], CultureInfo.InvariantCulture), int.Parse(k[(c + 1)..], CultureInfo.InvariantCulture)); }

    // ---- noise ---------------------------------------------------------------------------------------------------
    static double Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
            h ^= h >> 13; h *= 0x5BD1E995u; h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }
    /// smooth value noise, 0..1
    public static double Noise(double x, double z, int seed)
    {
        int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
        double fx = x - ix, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx); fz = fz * fz * (3 - 2 * fz);
        double a = Hash(ix, iz, seed), b = Hash(ix + 1, iz, seed), c = Hash(ix, iz + 1, seed), d = Hash(ix + 1, iz + 1, seed);
        return a + (b - a) * fx + (c - a) * fz + (a - b - c + d) * fx * fz;
    }
    /// a few octaves of noise, 0..1
    public static double Fbm(double x, double z, int seed, int octaves, double rough)
    {
        double v = 0, amp = 1, sum = 0, f = 1;
        for (int o = 0; o < octaves; o++) { v += Noise(x * f, z * f, seed + 17 * o) * amp; sum += amp; amp *= rough; f *= 2.03; }
        return v / sum;
    }
    static double Smooth(double e0, double e1, double x) { double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); }

    // ---- how far from the road -----------------------------------------------------------------------------------
    /// distance from a point to the nearest road edge (0 or less = on the road); far points get 1e9
    public sealed class RoadDist
    {
        readonly Dictionary<(int, int), List<(double X, double Z, double Hw, double H)>> _g = new();
        const double Cell = 32.0;
        public RoadDist(Network net)
        {
            foreach (var r in net.Drivable())
                for (int i = 0; i < r.Count; i++)
                {
                    var (x, z) = r.Centre[i];
                    var k = ((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell));
                    if (!_g.TryGetValue(k, out var l)) _g[k] = l = new();
                    l.Add((x, z, r.WidthAt[i] / 2, r.Height[i]));
                }
        }
        public double Dist(double x, double z, double reach = 64.0) => Near(x, z, reach).D;
        public (double D, double H) Near(double x, double z, double reach = 64.0)
        {
            int n = (int)Math.Ceiling(reach / Cell) + 1, gx = (int)Math.Floor(x / Cell), gz = (int)Math.Floor(z / Cell);
            double best = 1e9, bh = 0;
            for (int a = gx - n; a <= gx + n; a++)
                for (int b = gz - n; b <= gz + n; b++)
                    if (_g.TryGetValue((a, b), out var l))
                        foreach (var (px, pz, hw, h) in l)
                        {
                            double d = Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z)) - hw;
                            if (d < best) { best = d; bh = h; }
                        }
            return (best, bh);
        }
        /// 0 on and next to the road, rising to 1 further out: hills never cover the road
        public double Clear(double x, double z) => Smooth(RoadClear, RoadClear + RoadBlend, Dist(x, z, RoadClear + RoadBlend + 8));
    }

    // ---- landscape data -----------------------------------------------------------------------------------------
    public static JObj Land(JObj m)
    {
        if (m.Obj("land") is not { } l) m["land"] = l = new JObj();
        if (l.Obj("h") == null) l["h"] = new JObj();
        return l;
    }
    public static string Ground(JObj m) { var g = m.Obj("land")?.Str("ground"); return g != null && Surface.Surfaces.ContainsKey(g) ? g : "grass"; }
    public static Dictionary<(int, int), double> Heights(JObj m)
    {
        var o = new Dictionary<(int, int), double>();
        if (m.Obj("land")?.Obj("h") is { } h) foreach (var (k, v) in h) { double d = Json.D(v); if (d > 0.01) o[P(k)] = d; }
        return o;
    }
    public static bool HasLand(JObj m) => m.Obj("land")?.Obj("h") is { Count: > 0 };

    /// texture by slope (on unless switched off): flat = the ground, medium = dirt, steep = rock
    public static bool AutoTex(JObj m) { var l = m.Obj("land"); return l == null || !l.ContainsKey("auto_tex") || l.Truthy("auto_tex"); }
    public static Dictionary<(int, int), string> LandPaint(JObj m)
    {
        var o = new Dictionary<(int, int), string>();
        if (m.Obj("land")?.Obj("paint") is { } p) foreach (var (k, v) in p) if (v is string s && Surface.Surfaces.ContainsKey(s)) o[P(k)] = s;
        return o;
    }
    /// the Paint tool: the 8 m cells under the brush get key (null = back to automatic / the ground)
    public static int PaintLand(JObj m, double x, double z, double radius, string? key)
    {
        var land = Land(m);
        if (land.Obj("paint") == null) land["paint"] = new JObj();
        var p = land.Obj("paint")!;
        int n = 0;
        for (int i = (int)Math.Floor((x - radius) / Step); i <= (int)Math.Floor((x + radius) / Step); i++)
            for (int j = (int)Math.Floor((z - radius) / Step); j <= (int)Math.Floor((z + radius) / Step); j++)
            {
                if (Math.Sqrt(Math.Pow((i + 0.5) * Step - x, 2) + Math.Pow((j + 0.5) * Step - z, 2)) > radius) continue;
                string k = K(i, j);
                if (key == null) { if (p.Remove(k)) n++; }
                else if (p.Str(k) != key) { p[k] = key; n++; }
            }
        if (p.Count == 0) land.Remove("paint");
        return n;
    }

    /// the texture of the ground at (x, z) with this slope (ny = up part of the normal)
    public static string KeyAt(Dictionary<(int, int), string> paint, bool auto, string ground, double x, double z, double ny)
    {
        if (paint.Count > 0 && paint.TryGetValue(((int)Math.Floor(x / Step), (int)Math.Floor(z / Step)), out var k)) return k;
        if (!auto) return ground;
        double jit = 0.035 * (Noise(x / 45.0, z / 45.0, 3) - 0.5) * 2;
        if (ny < 0.74 + jit) return "rock";
        if (ny < 0.90 + jit) return ground is "grass" or "drygrass" ? "dirt" : ground == "rock" ? "roughdirt" : ground;
        return ground;
    }

    /// cubic B-spline through the 8 m heights (smooth, never above the highest point)
    public static double Spline(Dictionary<(int, int), double> hs, double x, double z)
    {
        double gx = x / Step, gz = z / Step; int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
        double fx = gx - i, fz = gz - j;
        Span<double> wx = stackalloc double[4], wz = stackalloc double[4];
        W(wx, fx); W(wz, fz);
        double v = 0;
        for (int b = 0; b < 4; b++)
        {
            double row = 0;
            for (int a = 0; a < 4; a++) if (hs.TryGetValue((i - 1 + a, j - 1 + b), out double h)) row += wx[a] * h;
            v += wz[b] * row;
        }
        return v;
        static void W(Span<double> w, double t) { double t2 = t * t, t3 = t2 * t; w[0] = (1 - t) * (1 - t) * (1 - t) / 6; w[1] = (3 * t3 - 6 * t2 + 4) / 6; w[2] = (-3 * t3 + 3 * t2 + 3 * t + 1) / 6; w[3] = t3 / 6; }
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Network, RoadDist> Dists = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Network, Dictionary<(long, long), double>> Clears = new();
    public static RoadDist DistOf(Network net) => Dists.GetValue(net, n => new RoadDist(n));
    /// Clear() kept per network (the road does not change while you paint hills)
    public static double ClearAt(Network net, double x, double z)
    {
        var c = Clears.GetValue(net, _ => new());
        var k = ((long)Math.Round(x * 4), (long)Math.Round(z * 4));
        lock (c) if (c.TryGetValue(k, out double v)) return v;
        double r = DistOf(net).Clear(x, z);
        lock (c) { if (c.Count > 400_000) c.Clear(); c[k] = r; }
        return r;
    }

    /// the hills' height at (x, z): the smooth curve, faded out next to the road
    public static double HillAt(Dictionary<(int, int), double> hs, Network net, double x, double z)
    {
        double r = Spline(hs, x, z);
        return r < 0.01 ? 0 : r * ClearAt(net, x, z);
    }

    /// the hills sampled on a grid of `step` metres: heights, and the texture of each quad that has ground
    public sealed class Sampled { public double Step; public Func<int, int, double> H = null!; public Dictionary<(int, int), string> Keys = new(); }
    public static Sampled Sample(JObj m, Network net, double step)
    {
        var hs = Heights(m);
        var o = new Sampled { Step = step };
        var hc = new Dictionary<(int, int), double>();
        double H(int i, int j)
        {
            if (hc.TryGetValue((i, j), out double v)) return v;
            return hc[(i, j)] = HillAt(hs, net, i * step, j * step);
        }
        o.H = H;
        if (hs.Count == 0) return o;
        var quads = new HashSet<(int, int)>();
        foreach (var (gi, gj) in hs.Keys)
        {
            int a0 = (int)Math.Floor((gi - 2) * Step / step), a1 = (int)Math.Ceiling((gi + 2) * Step / step);
            int b0 = (int)Math.Floor((gj - 2) * Step / step), b1 = (int)Math.Ceiling((gj + 2) * Step / step);
            for (int a = a0; a < a1; a++) for (int b = b0; b < b1; b++) quads.Add((a, b));
        }
        var paint = LandPaint(m); bool auto = AutoTex(m); string ground = Ground(m);
        // Auto hills start some metres from the road: the strip between is flat ground up to the road's kerb bank
        var verge = new HashSet<(int, int)>();
        if (m.Obj("land")?.Obj("auto") is { } au)
        {
            double vReach = au.Num("near") + 2 * Step;
            foreach (var r in net.Drivable())
                for (double d = 0; d <= r.Length; d += step * 0.7)
                    foreach (int side in new[] { -1, 1 })
                        for (double lat = r.WAt(d) / 2; lat <= r.WAt(d) / 2 + vReach; lat += step * 0.7)
                        {
                            var (x, z, _) = r.At(d, side * lat);
                            var q = ((int)Math.Floor(x / step), (int)Math.Floor(z / step));
                            verge.Add(q); quads.Add(q);
                        }
        }
        var nys = new Dictionary<(int, int), double>();
        foreach (var (i, j) in quads)
        {
            double h00 = H(i, j), h10 = H(i + 1, j), h01 = H(i, j + 1), h11 = H(i + 1, j + 1);
            // flat parts inside the hills' area stay (the ground runs up to the road), but not on or right next to the road
            if (h00 < 0.05 && h10 < 0.05 && h01 < 0.05 && h11 < 0.05 && ((!verge.Contains((i, j)) && Spline(hs, (i + 0.5) * step, (j + 0.5) * step) < 0.3) || DistOf(net).Dist((i + 0.5) * step, (j + 0.5) * step, 12) < 0.3 + step * 0.71)) continue;
            double gx = (h10 - h00 + h11 - h01) / (2 * step), gz = (h01 - h00 + h11 - h10) / (2 * step);
            nys[(i, j)] = 1 / Math.Sqrt(1 + gx * gx + gz * gz);
        }
        // the slope over about 12 m (not each small quad): whole areas get one texture, not specks
        int reach = Math.Max(1, (int)Math.Round(6.0 / step));
        foreach (var (i, j) in nys.Keys)
        {
            double sum = 0; int n = 0;
            for (int a = -reach; a <= reach; a++) for (int b = -reach; b <= reach; b++) if (nys.TryGetValue((i + a, j + b), out double v)) { sum += v; n++; }
            o.Keys[(i, j)] = KeyAt(paint, auto, ground, (i + 0.5) * step, (j + 0.5) * step, sum / n);
        }
        // tidy: a quad whose neighbours (5 of 8 or more) all have another texture takes theirs (painted cells stay)
        if (auto)
            for (int pass = 0; pass < 2; pass++)
            {
                var change = new List<((int, int), string)>();
                foreach (var ((i, j), k) in o.Keys)
                {
                    if (paint.Count > 0 && paint.ContainsKey(((int)Math.Floor((i + 0.5) * step / Step), (int)Math.Floor((j + 0.5) * step / Step)))) continue;
                    var count = new Dictionary<string, int>();
                    for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) if ((a != 0 || b != 0) && o.Keys.TryGetValue((i + a, j + b), out var nk)) count[nk] = count.GetValueOrDefault(nk) + 1;
                    var best = count.OrderByDescending(kv => kv.Value).FirstOrDefault();
                    if (best.Key != null && best.Key != k && best.Value >= 5) change.Add(((i, j), best.Key));
                }
                foreach (var (q, k) in change) o.Keys[q] = k;
            }
        // borders: grass next to dirt, dirt next to rock get the mixed picture of both
        if (auto)
        {
            var mixAt = new List<((int, int), string)>();
            foreach (var ((i, j), k) in o.Keys)
            {
                if (paint.Count > 0 && paint.ContainsKey(((int)Math.Floor((i + 0.5) * step / Step), (int)Math.Floor((j + 0.5) * step / Step)))) continue;
                string? want = k == "grass" ? "dirt" : k == "dirt" ? "rock" : null;
                if (want == null) continue;
                bool edge = false;
                for (int a = -1; a <= 1 && !edge; a++) for (int b = -1; b <= 1; b++) if (o.Keys.TryGetValue((i + a, j + b), out var nk) && nk == want) { edge = true; break; }
                if (edge) mixAt.Add(((i, j), k == "grass" ? "grassdirt" : "dirtrock"));
            }
            foreach (var (q, k) in mixAt) o.Keys[q] = k;
        }
        return o;
    }

    /// about how much ground the hills cover (m2), to skip grids that clearly do not fit
    static double Area(Dictionary<(int, int), double> hs) => hs.Count * Step * Step * 1.6;

    /// one dab of a landscape brush. tool: raise, lower, smooth, flatten, noise, erase. strength: metres per dab (raise /
    /// lower / noise) or 0..1 (smooth / flatten / erase). Returns how many grid points changed.
    public static int Brush(JObj m, string tool, double x, double z, double radius, double strength, double target = 0, int seed = 1)
    {
        var h = Land(m).Obj("h")!;
        int i0 = (int)Math.Floor((x - radius) / Step), i1 = (int)Math.Ceiling((x + radius) / Step);
        int j0 = (int)Math.Floor((z - radius) / Step), j1 = (int)Math.Ceiling((z + radius) / Step);
        double Get(int i, int j) => h.ContainsKey(K(i, j)) ? Json.D(h[K(i, j)]) : 0.0;
        var nw = new List<(string, double)>();
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                double d = Math.Sqrt(Math.Pow(i * Step - x, 2) + Math.Pow(j * Step - z, 2));
                if (d > radius) continue;
                double f = 1 - (d / radius) * (d / radius); f *= f;
                double old = Get(i, j), v = old;
                switch (tool)
                {
                    case "raise": v = old + strength * f; break;
                    case "lower": v = old - strength * f; break;
                    case "noise": v = old + (Noise(i * Step / 14.0, j * Step / 14.0, seed) - 0.45) * 2 * strength * f; break;
                    case "smooth":
                        double avg = (Get(i - 1, j) + Get(i + 1, j) + Get(i, j - 1) + Get(i, j + 1) + old) / 5;
                        v = old + (avg - old) * Math.Min(1, strength) * f; break;
                    case "flatten": v = old + (target - old) * Math.Min(1, strength) * f; break;
                    case "erase": v = old * (1 - Math.Min(1, strength) * f); break;
                }
                v = Math.Clamp(v, 0, MaxHeight);
                if (Math.Abs(v - old) > 1e-4) nw.Add((K(i, j), v));
            }
        foreach (var (k, v) in nw) { if (v < 0.05) h.Remove(k); else h[k] = Math.Round(v, 2); }
        return nw.Count;
    }

    public static double HeightAt(JObj m, double x, double z)
    {
        var h = m.Obj("land")?.Obj("h");
        if (h == null || h.Count == 0) return 0;
        return Spline(Heights(m), x, z);
    }

    /// the old straight-line height (kept for comparison)
    public static double HeightAtLinear(JObj m, double x, double z)
    {
        var h = m.Obj("land")?.Obj("h");
        if (h == null || h.Count == 0) return 0;
        double gx = x / Step, gz = z / Step; int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
        double fx = gx - i, fz = gz - j;
        double G(int a, int b) => h.ContainsKey(K(a, b)) ? Json.D(h[K(a, b)]) : 0.0;
        return G(i, j) * (1 - fx) * (1 - fz) + G(i + 1, j) * fx * (1 - fz) + G(i, j + 1) * (1 - fx) * fz + G(i + 1, j + 1) * fx * fz;
    }

    public sealed class AutoSet { public double Height = 35, Near = 30, Band = 220, Rough = 0.5; public int Seed = 1; public bool Keep; }

    /// hills all around the road: they start Near metres from the road edge, rise, and fade out Band metres further
    public static int Auto(JObj m, Network net, AutoSet s)
    {
        var land = Land(m);
        var h = land.Obj("h")!;
        if (!s.Keep) h.Clear();
        var rd = new RoadDist(net);
        double x0 = 1e9, x1 = -1e9, z0 = 1e9, z1 = -1e9;
        foreach (var r in net.Drivable()) { var b = r.Bounds(s.Near + s.Band + 20); x0 = Math.Min(x0, b.X0); x1 = Math.Max(x1, b.X1); z0 = Math.Min(z0, b.Z0); z1 = Math.Max(z1, b.Z1); }
        int n = 0;
        int oct = 2 + (int)Math.Round(3 * s.Rough);
        for (int i = (int)Math.Floor(x0 / Step); i <= (int)Math.Ceiling(x1 / Step); i++)
            for (int j = (int)Math.Floor(z0 / Step); j <= (int)Math.Ceiling(z1 / Step); j++)
            {
                double x = i * Step, z = j * Step;
                double d = rd.Dist(x, z, s.Near + s.Band + 40);
                if (d < s.Near || d > s.Near + s.Band) continue;
                double t = (d - s.Near) / s.Band;
                double env = Smooth(0, 0.3, t) * (1 - Smooth(0.7, 1.0, t));
                double shape = Fbm(x / 90.0, z / 90.0, s.Seed, oct, 0.35 + 0.35 * s.Rough);
                shape = Smooth(0.25, 0.85, shape);
                double v = s.Height * env * (0.25 + 0.75 * shape);
                if (v < 0.05) continue;
                string k = K(i, j);
                if (s.Keep && h.ContainsKey(k)) v = Math.Max(v, Json.D(h[k]));
                h[k] = Math.Round(Math.Min(v, MaxHeight), 2); n++;
            }
        land["auto"] = new JObj { ["height"] = s.Height, ["near"] = s.Near, ["band"] = s.Band, ["rough"] = s.Rough, ["seed"] = (long)s.Seed };
        return n;
    }

    // ---- jump ramps --------------------------------------------------------------------------------------------
    public static readonly string[] Shapes = { "kicker", "table" };
    public static IEnumerable<JObj> RampEntries()
    {
        JObj E(string id, string name, double w, double l, double h, string shape, double curve, double lip) => new JObj
        {
            ["id"] = "ramp:" + id, ["kind"] = "ramp", ["name"] = name, ["loc"] = "Built-in", ["cat"] = "Jump ramps", ["sized"] = true,
            ["lo"] = new List<object?> { -w / 2, 0.0, -l / 2 }, ["hi"] = new List<object?> { w / 2, h, l / 2 },
            ["shape"] = shape, ["curve"] = curve, ["lip"] = lip,
        };
        yield return E("kicker", "Kicker (short, steep)", 6, 7, 1.6, "kicker", 0.7, 0.0);
        yield return E("jump", "Jump ramp", 8, 16, 2.6, "kicker", 0.45, 1.0);
        yield return E("big", "Big jump ramp", 12, 30, 5.0, "kicker", 0.35, 2.0);
        yield return E("table", "Table top (up, flat, down)", 10, 40, 3.0, "table", 0.3, 12.0);
        yield return E("hump", "Speed hump (small table)", 10, 10, 0.6, "table", 0.0, 3.0);
    }
    public static bool IsRamp(JObj? catalogEntry) => catalogEntry?.Str("kind") == "ramp";

    public sealed class Ramp
    {
        public string Key = ""; public double X, Y, Z, Turn, Rx, Rz, W, L, H, Curve, Lip; public string Shape = "kicker", Surf = "tarmac"; public double[] K = { 1.0, 1.0, 1.0 };
        public double[][] A = Array.Empty<double[]>();
        public double[] World(double lx, double ly, double lz) => new[] { X + lx * A[0][0] + ly * A[1][0] + lz * A[2][0], Y + lx * A[0][1] + ly * A[1][1] + lz * A[2][1], Z + lx * A[0][2] + ly * A[1][2] + lz * A[2][2] };
        /// height of the driving surface at t (0 = front edge, 1 = back edge)
        public double Top(double t)
        {
            double lip = Math.Clamp(Lip, 0, L * 0.9) / L;
            double Up(double u) { u = Math.Clamp(u, 0, 1); return H * ((1 - Curve) * u + Curve * (1 - Math.Sqrt(Math.Max(0, 1 - u * u)))); }
            if (Shape == "table")
            {
                double up = (1 - lip) / 2;
                if (t <= up) return Up(t / up);
                if (t >= 1 - up) return Up((1 - t) / up);
                return H;
            }
            double rise = 1 - lip;
            return t >= rise ? H : Up(t / rise);
        }
    }

    /// the map's placed ramps (moved / turned / scaled by their edits)
    public static List<Ramp> Ramps(JObj m)
    {
        var o = new List<Ramp>();
        var objs = m.Objs("objects").ToList(); var edits = m.Obj("edits"); var cat = Layout.Catalog();
        for (int i = 0; i < objs.Count; i++)
        {
            var ob = objs[i];
            if (!cat.TryGetValue(ob.Str("id") ?? "", out var e) || !IsRamp(e)) continue;
            var ed = edits?.Obj("obj:" + i) ?? new JObj();
            if (ed.Truthy("hidden")) continue;
            double[] k = ed.ContainsKey("scale") ? (ed["scale"] is List<object?> sl ? new[] { Json.D(sl[0]), Json.D(sl[1]), Json.D(sl[2]) } : new[] { Json.D(ed["scale"]), Json.D(ed["scale"]), Json.D(ed["scale"]) }) : new[] { 1.0, 1.0, 1.0 };
            var lo = e.Arr("lo")!.Select(Json.D).ToArray(); var hi = e.Arr("hi")!.Select(Json.D).ToArray();
            var set = ob.Obj("ramp") ?? new JObj();
            var r = new Ramp
            {
                Key = "obj:" + i, X = ob.Num("x") + ed.Num("dx"), Y = ob.Num("h") + ed.Num("dy"), Z = ob.Num("z") + ed.Num("dz"),
                Turn = ob.Num("turn") + ed.Num("turn"), Rx = ed.Num("rx"), Rz = ed.Num("rz"),
                W = Math.Max(1, (hi[0] - lo[0]) * k[0]), H = Math.Max(0.1, (hi[1] - lo[1]) * k[1]), L = Math.Max(1, (hi[2] - lo[2]) * k[2]),
                Shape = set.Str("shape") ?? e.Str("shape") ?? "kicker",
                Curve = Math.Clamp(set.ContainsKey("curve") ? set.Num("curve") : e.Num("curve"), 0, 1),
                Lip = Math.Max(0, (set.ContainsKey("lip") ? set.Num("lip") : e.Num("lip")) * k[2]),
                Surf = set.Str("surface") is { } sk && Surface.Surfaces.ContainsKey(sk) ? sk : "tarmac",
            };
            r.A = Layout.ActorAxes3(r.Turn, r.Rx, r.Rz); r.K = k;
            o.Add(r);
        }
        return o;
    }

    // ---- mesh helpers ---------------------------------------------------------------------------------------------
    /// a triangle (3 own vertices, flat normal), wound so it faces out (n = the outward direction)
    static void Tri(Chunk ch, double[] a, double[] b, double[] c, double nx, double ny, double nz, bool sideUv = false)
    {
        double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
        double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
        if (cx * nx + cy * ny + cz * nz < 0) { (b, c) = (c, b); cx = -cx; cy = -cy; cz = -cz; }
        double l = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        if (l < 1e-9) return;
        cx /= l; cy /= l; cz /= l;
        int b0 = ch.Verts.Count;
        foreach (var q in new[] { a, b, c })
        {
            double u = sideUv ? (q[0] + q[2]) / Surface.TexM : q[0] / Surface.TexM, v = sideUv ? q[1] / Surface.TexM : q[2] / Surface.TexM;
            ch.Verts.Add(new[] { q[0] - ch.Anchor[0], q[1] + Surface.PatchLift, q[2] - ch.Anchor[2], cx, cy, cz, u, v });
        }
        Join(ch, b0);
        ch.Strip.Add(b0); ch.Strip.Add(b0 + 1); ch.Strip.Add(b0 + 2);
    }
    /// links the next strip run to the one before with degenerate triangles, keeping the next run on an even index
    static void Join(Chunk ch, int first)
    {
        if (ch.Strip.Count == 0) return;
        ch.Strip.Add(ch.Strip[^1]); ch.Strip.Add(first);
        if (ch.Strip.Count % 2 != 0) ch.Strip.Add(first);
    }

    public const int MaxVerts = 2000, MaxStrip = 3600, RunMax = 300;

    /// heights on a regular grid -> models (shared vertices, smooth normals, filled row by row up to MaxVerts / MaxStrip)
    /// + drivable decks (one strip per column run)
    static void Grid(Func<int, int, double> hgt, IEnumerable<(int I, int J)> quads0, double step, string key, double lift,
        List<Chunk>? chunks, List<(Piece P, int Mat)>? decks, int mat)
    {
        var quads = quads0.ToList();
        if (chunks != null)
        {
            var runs = new List<(int J, int I0, int I1)>();
            foreach (var row in quads.GroupBy(q => q.J).OrderBy(g => g.Key))
            {
                var cols = row.Select(q => q.I).OrderBy(i => i).ToList();
                int a = 0;
                while (a < cols.Count)
                {
                    int b = a;
                    while (b + 1 < cols.Count && cols[b + 1] == cols[b] + 1 && b - a < RunMax - 1) b++;
                    runs.Add((row.Key, cols[a], cols[b]));
                    a = b + 1;
                }
            }
            Chunk? ch = null; Dictionary<(int, int), int>? vid = null; double x0 = 0, z0 = 0;
            void Start(int i, int j) { x0 = i * step; z0 = j * step; ch = new Chunk { Key = key, Anchor = new[] { x0, Fe.GroundY, z0 } }; vid = new(); chunks.Add(ch); }
            int V(int i, int j)
            {
                if (vid!.TryGetValue((i, j), out int k)) return k;
                double y = hgt(i, j), x = i * step, z = j * step;
                double nx = hgt(i - 1, j) - hgt(i + 1, j), nz = hgt(i, j - 1) - hgt(i, j + 1), ny = 2 * step, l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                ch!.Verts.Add(new[] { x - x0, y + lift, z - z0, nx / l, ny / l, nz / l, x / Surface.TexM, z / Surface.TexM });
                return vid[(i, j)] = ch.Verts.Count - 1;
            }
            foreach (var (j, i0, i1) in runs)
            {
                int need = 2 * (i1 - i0 + 2);
                if (ch == null || ch.Verts.Count + need > MaxVerts || ch.Strip.Count + need + 3 > MaxStrip) Start(i0, j);
                var run = new List<int>();
                for (int i = i0; i <= i1 + 1; i++) { run.Add(V(i, j)); run.Add(V(i, j + 1)); }
                Join(ch!, run[0]);
                ch!.Strip.AddRange(run);
            }
        }
        if (decks != null)
            foreach (var col in quads.GroupBy(q => q.I).OrderBy(g => g.Key))
            {
                var rows = col.Select(q => q.J).OrderBy(j => j).ToList();
                int a = 0;
                while (a < rows.Count)
                {
                    int b = a;
                    while (b + 1 < rows.Count && rows[b + 1] == rows[b] + 1 && b - a < 100) b++;
                    int i = col.Key;
                    var left = new List<double[]>(); var right = new List<double[]>();
                    for (int j = rows[a]; j <= rows[b] + 1; j++)
                    {
                        left.Add(new[] { i * step, Fe.GroundY + hgt(i, j) + Surface.PatchLift, j * step });
                        right.Add(new[] { (i + 1) * step, Fe.GroundY + hgt(i + 1, j) + Surface.PatchLift, j * step });
                    }
                    decks.Add((Physics.DeckStrip(left, right), mat));
                    a = b + 1;
                }
            }
    }

    // ---- everything -----------------------------------------------------------------------------------------------
    public sealed class Result { public List<Chunk> Chunks = new(); public List<(Piece P, int Mat)> Decks = new(); public List<Piece> Walls = new(); public double HillStep, BumpStep; public int Hills, Bumps, Ramps; public List<string> HillKeys = new(); }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Network, Dictionary<string, Result>> Cache = new();

    /// the ramps, bumps and hills of a map. budget = how many models are left for them
    public static Result Make(JObj m, Network net, int budget)
    {
        string sig = budget + "|" + Json.Dumps(m["land"], -1) + "|" + Json.Dumps(m.Obj("surface")?["bumps"], -1)
            + "|" + Json.Dumps(m.Obj("surface")?["bump_h"], -1) + Json.Dumps(m.Obj("surface")?["bump_size"], -1) + "|" + RampSig(m);
        var c = Cache.GetOrCreateValue(net);
        lock (c)
        {
            if (!c.TryGetValue(sig, out var hit)) { if (c.Count > 4) c.Clear(); c[sig] = hit = Make0(m, net, budget); }
            return hit;
        }
    }

    static string RampSig(JObj m)
    {
        var cat = Layout.Catalog(); var sb = new System.Text.StringBuilder();
        var objs = m.Objs("objects").ToList(); var edits = m.Obj("edits");
        for (int i = 0; i < objs.Count; i++)
            if (cat.TryGetValue(objs[i].Str("id") ?? "", out var e) && IsRamp(e))
                sb.Append(i).Append(Json.Dumps(objs[i], -1)).Append(Json.Dumps(edits?["obj:" + i], -1));
        return sb.ToString();
    }

    static Result Make0(JObj m, Network net, int budget)
    {
        var o = new Result();
        // ramps
        foreach (var r in Ramps(m))
        {
            var ch = new Chunk { Key = r.Surf, Anchor = new[] { r.X, Fe.GroundY, r.Z } };
            int n = 14;
            double hw = r.W / 2, z0 = -r.L / 2;
            var tl = new List<double[]>(); var tr = new List<double[]>(); var bl = new List<double[]>(); var br = new List<double[]>();
            for (int s = 0; s <= n; s++)
            {
                double t = s / (double)n, lz = z0 + t * r.L, y = r.Top(t);
                tl.Add(r.World(-hw, y, lz)); tr.Add(r.World(hw, y, lz)); bl.Add(r.World(-hw, 0, lz)); br.Add(r.World(hw, 0, lz));
            }
            var up = r.A[1]; var side = r.A[0]; var fwd = r.A[2];
            for (int s = 0; s < n; s++)
            {
                // the driving surface
                double nx = up[0], ny = up[1], nz = up[2];
                Tri(ch, tl[s], tr[s], tr[s + 1], nx, ny, nz); Tri(ch, tl[s], tr[s + 1], tl[s + 1], nx, ny, nz);
                // the sides (only where the surface is above the ground)
                if (tl[s][1] - bl[s][1] > 0.02 || tl[s + 1][1] - bl[s + 1][1] > 0.02)
                {
                    Tri(ch, bl[s], tl[s], tl[s + 1], -side[0], -side[1], -side[2], true); Tri(ch, bl[s], tl[s + 1], bl[s + 1], -side[0], -side[1], -side[2], true);
                    Tri(ch, br[s], tr[s + 1], tr[s], side[0], side[1], side[2], true); Tri(ch, br[s], br[s + 1], tr[s + 1], side[0], side[1], side[2], true);
                }
            }
            if (r.Top(1) > 0.02) { Tri(ch, bl[n], br[n], tr[n], fwd[0], fwd[1], fwd[2], true); Tri(ch, bl[n], tr[n], tl[n], fwd[0], fwd[1], fwd[2], true); }
            o.Chunks.Add(ch);
            // collision: the surface you drive on + solid sides and back
            var deck = new Piece();
            for (int s = 0; s <= n; s++)
            {
                var a = (double[])tl[s].Clone(); var b = (double[])tr[s].Clone(); a[1] += Fe.GroundY + Surface.PatchLift; b[1] += Fe.GroundY + Surface.PatchLift;
                deck.Corners.Add(a); deck.Corners.Add(b);
            }
            for (int s = 0; s < n; s++) { int l0 = 2 * s, r0 = 2 * s + 1, l1 = 2 * s + 2, r1 = 2 * s + 3; deck.Faces.Add((l0, r0, r1, up[0], up[1], up[2])); deck.Faces.Add((l0, r1, l1, up[0], up[1], up[2])); }
            o.Decks.Add((deck, Surface.Surfaces[r.Surf].Mat));
            var wall = new Piece();
            double[] G(double[] q) { var w = (double[])q.Clone(); w[1] += Fe.GroundY; return w; }
            void Quad(double[] a, double[] b, double[] c2, double[] d, double[] nn)
            {
                int k = wall.Corners.Count; wall.Corners.Add(G(a)); wall.Corners.Add(G(b)); wall.Corners.Add(G(c2)); wall.Corners.Add(G(d));
                wall.Faces.Add((k, k + 1, k + 2, nn[0], nn[1], nn[2])); wall.Faces.Add((k, k + 2, k + 3, nn[0], nn[1], nn[2]));
            }
            var ns = new[] { -side[0], -side[1], -side[2] };
            for (int s = 0; s < n; s += 2)
            {
                int e = Math.Min(n, s + 2);
                if (tl[e][1] - bl[e][1] < 0.3 && tl[s][1] - bl[s][1] < 0.3) continue;
                Quad(bl[s], bl[e], tl[e], tl[s], ns); Quad(br[s], br[e], tr[e], tr[s], side);
            }
            if (r.Top(1) > 0.3) Quad(bl[n], br[n], tr[n], tl[n], fwd);
            if (wall.Corners.Count > 0) o.Walls.Add(wall);
            o.Ramps++;
        }
        budget -= o.Chunks.Count;
        var rd = DistOf(net);
        // bumps
        var bumps = m.Obj("surface")?.Obj("bumps");
        if (bumps is { Count: > 0 } && budget > 0)
        {
            double amp = m.Obj("surface")!.ContainsKey("bump_h") ? m.Obj("surface")!.Num("bump_h") : 0.3;
            double size = m.Obj("surface")!.ContainsKey("bump_size") ? m.Obj("surface")!.Num("bump_size") : 3.0;
            var st = new Dictionary<(int, int), double>();
            foreach (var (k, v) in bumps) st[P(k)] = Json.D(v);
            var hsB = Heights(m);
            var paint = Surface.PaintStrengths(m); var sset = Surface.Settings(m);
            var areas = net.Areas(); var roads = net.Drivable().ToList();
            (double H, bool OnRoad) Base(double x, double z)
            {
                double best = -1e9, v = 0; bool on = false;
                foreach (var r in roads)
                {
                    var (d, hh) = areas[r].SurfaceH(x, z);
                    if (d > -0.5 && d > best) { best = d; v = hh; on = true; }
                }
                return on ? (v, true) : (HillAt(hsB, net, x, z), false);
            }
            string KeyAt(double x, double z, bool on)
            {
                var c = Surface.CellOf(x, z);
                if (paint.TryGetValue(c, out var p) && p.St >= 0.5) return p.Key;
                return on ? sset.Road : HasLand(m) ? Ground(m) : "dirt";
            }
            double StAt(double x, double z)
            {
                double gx = x / Surface.CellM - 0.5, gz = z / Surface.CellM - 0.5; int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
                double fx = gx - i, fz = gz - j;
                double S(int a, int b) => st.GetValueOrDefault((a, b), 0.0);
                return S(i, j) * (1 - fx) * (1 - fz) + S(i + 1, j) * fx * (1 - fz) + S(i, j + 1) * (1 - fx) * fz + S(i + 1, j + 1) * fx * fz;
            }
            foreach (double bstep in new[] { 1.0, 2.0, 3.0, 4.0 })
            {
                double ratio = Surface.CellM / bstep;
                var quads = new HashSet<(int, int)>();
                foreach (var (ci, cj) in st.Keys)
                    for (int a = -1; a <= (int)Math.Ceiling(ratio); a++)
                        for (int b = -1; b <= (int)Math.Ceiling(ratio); b++)
                            quads.Add(((int)Math.Floor(ci * ratio) + a, (int)Math.Floor(cj * ratio) + b));
                var hc = new Dictionary<(int, int), double>(); var onc = new Dictionary<(int, int), bool>();
                double Hg(int i, int j)
                {
                    if (hc.TryGetValue((i, j), out double v)) return v;
                    double x = i * bstep, z = j * bstep;
                    var (bh, on) = Base(x, z);
                    double s = StAt(x, z);
                    double bump = s <= 0.01 ? 0 : amp * s * Smooth(0.3, 0.75, Fbm(x / size, z / size, 7, 2, 0.5));
                    onc[(i, j)] = on;
                    return hc[(i, j)] = bh + bump;
                }
                var keyOf = new Dictionary<(int, int), string>();
                foreach (var q in quads.ToList())
                {
                    double x = (q.Item1 + 0.5) * bstep, z = (q.Item2 + 0.5) * bstep;
                    if (StAt(x, z) <= 0.01) { quads.Remove(q); continue; }
                    keyOf[q] = KeyAt(x, z, Base(x, z).OnRoad);
                }
                var mesh = new List<Chunk>();
                foreach (var g in quads.GroupBy(q => keyOf[q])) Grid(Hg, g, bstep, g.Key, Surface.OverlayLift, mesh, null, 0);
                if (mesh.Count > Math.Min(BumpCap, budget) && bstep < 4.0) continue;
                var dk = new List<(Piece, int)>();
                foreach (var g in quads.GroupBy(q => keyOf[q])) Grid(Hg, g, bstep, g.Key, 0, null, dk, Surface.Surfaces[g.Key].Mat);
                if (mesh.Count > budget) mesh = mesh.Take(budget).ToList();
                o.Chunks.AddRange(mesh); o.Decks.AddRange(dk); o.Bumps = mesh.Count; o.BumpStep = bstep; budget -= mesh.Count;
                break;
            }
        }
        // hills: the finest grid whose models fit (4 m when there is room), one set of models per texture
        var hs = Heights(m);
        if (hs.Count > 0 && budget > 0)
        {
            int cap = Math.Min(HillCap, budget);
            double area = Area(hs);
            foreach (double step in new[] { 4.0, 6.0, 8.0, 12.0, 16.0, 24.0, 32.0, 48.0, 64.0 })
            {
                if (step < 64 && area / (step * step) > cap * MaxVerts * 0.8) continue;     // clearly too many points
                var sm = Sample(m, net, step);
                var groups = sm.Keys.GroupBy(kv => kv.Value).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                var mesh = new List<Chunk>();
                foreach (var g in groups) Grid(sm.H, g.Select(kv => kv.Key), step, g.Key, Lift, mesh, null, 0);
                if (mesh.Count > cap && step < 64) continue;
                var dk = new List<(Piece, int)>();
                foreach (var g in groups) Grid(sm.H, g.Select(kv => kv.Key), step, g.Key, 0, null, dk, Surface.Surfaces[g.Key].Mat);
                if (mesh.Count > budget) mesh = mesh.Take(budget).ToList();
                o.Chunks.AddRange(mesh); o.Decks.AddRange(dk); o.Hills = mesh.Count; o.HillStep = step;
                o.HillKeys = groups.Select(g => g.Key).ToList();
                break;
            }
        }
        return o;
    }

    /// models the map's road and painted surfaces already use (the rest is the budget for ramps, bumps and hills)
    public static int Budget(JObj m, Road main, Network net) =>
        ModelCap - RoadMesh.NetworkChunks(m, main).Sum(c => c.Cs.Count) - Surface.OverlayChunks(m, net).Count;

    // ---- bumps brush (Surface tab) --------------------------------------------------------------------------------
    public static int BumpBrush(JObj m, double x, double z, double radius, bool erase)
    {
        if (m.Obj("surface") == null) m["surface"] = new JObj();
        var surf = m.Obj("surface")!;
        if (surf.Obj("bumps") == null) surf["bumps"] = new JObj();
        var b = surf.Obj("bumps")!;
        double C = Surface.CellM, f = Math.Max(C, radius * 0.4), reach = radius + f / 2;
        var (i0, j0) = Surface.CellOf(x - reach, z - reach); var (i1, j1) = Surface.CellOf(x + reach, z + reach);
        int n = 0;
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                double d = Math.Sqrt(Math.Pow((i + 0.5) * C - x, 2) + Math.Pow((j + 0.5) * C - z, 2));
                double st = Math.Clamp((radius - d) / f + 0.5, 0, 1);
                if (st <= 0) continue;
                string k = K(i, j); double old = b.ContainsKey(k) ? Json.D(b[k]) : 0;
                double nw = erase ? Math.Min(old, 1 - st) : Math.Max(old, st);
                if (Math.Abs(nw - old) < 1e-6) continue;
                if (nw < 0.05) b.Remove(k); else b[k] = Math.Round(nw, 2);
                n++;
            }
        if (b.Count == 0) surf.Remove("bumps");
        return n;
    }

    /// Auto bumps: Height (m) and Size (m between bumps) go to bump_h / bump_size; bumpy stretches at most MaxLen long with
    /// at most MaxGap metres of smooth road between them, at random places (Seed). Whole = across the whole road, else one side
    public sealed class BumpSet { public double Height = 0.25, Size = 2.5, MaxLen = 25, MaxGap = 250; public int Seed = 1; public bool Keep, Whole; }

    public static int AutoBumps(JObj m, Network net, BumpSet s)
    {
        if (m.Obj("surface") == null) m["surface"] = new JObj();
        var surf = m.Obj("surface")!;
        if (!s.Keep || surf.Obj("bumps") == null) surf["bumps"] = new JObj();
        var b = surf.Obj("bumps")!;
        surf["bump_h"] = Math.Round(Math.Clamp(s.Height, 0.05, 2.0), 2);
        surf["bump_size"] = Math.Round(Math.Clamp(s.Size, 0.5, 20.0), 1);
        double maxLen = Math.Max(4, s.MaxLen), maxGap = Math.Max(10, s.MaxGap);
        var rng = new PyRandom(1000 + s.Seed);
        int patches = 0;
        foreach (var r in net.Drivable())
        {
            double pos = rng.Uniform(0.25, 1.0) * maxGap;
            while (pos < r.Length - 4)
            {
                double len = Math.Min(rng.Uniform(0.4, 1.0) * maxLen, r.Length - 2 - pos);
                if (len < 3) break;
                bool whole = s.Whole || rng.Random() < 0.35;
                int side = rng.Random() < 0.5 ? -1 : 1;
                for (double t = 0; t <= len; t += Surface.CellM / 2)
                {
                    double w = r.WAt(pos + t) / 2, end = Math.Min(t, len - t);
                    double lo = whole ? -w : side < 0 ? -w : 0, hi = whole ? w : side < 0 ? 0 : w;
                    double fade = Math.Clamp(end / 3.0 + 0.3, 0, 1);
                    for (double lat = lo; lat <= hi; lat += Surface.CellM / 2)
                    {
                        var (x, z, _) = r.At(pos + t, lat);
                        double edge = Math.Min(lat - lo, hi - lat);
                        double st = fade * Math.Clamp(edge / 2.0 + 0.4, 0, 1);
                        if (st < 0.05) continue;
                        var (i, j) = Surface.CellOf(x, z);
                        string k = K(i, j);
                        double old = b.ContainsKey(k) ? Json.D(b[k]) : 0;
                        if (st > old) b[k] = Math.Round(st, 2);
                    }
                }
                patches++;
                pos += len + rng.Uniform(0.35, 1.0) * maxGap;
            }
        }
        if (b.Count == 0) surf.Remove("bumps");
        return patches;
    }
}

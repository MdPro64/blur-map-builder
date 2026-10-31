// Road surfaces: what the cars drive on + what it looks like. Port of flat_earth/surface.py.
// np.random.default_rng is ported exactly (NumpyRng), so textures match, except the crack lines of "varied asphalt".
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Bmb.Core;

/// numpy's default_rng(seed): SeedSequence + PCG64
public sealed class NumpyRng
{
    UInt128 _state, _inc;
    static readonly UInt128 Mult = new(0x2360ED051FC65DA4, 0x4385DF649FCCF645);

    public NumpyRng(long seed)
    {
        const uint InitA = 0x43b0d7e5, MultA = 0x931e8875, InitB = 0x8b51f9dd, MultB = 0x58f38ded, L = 0xca01f9dd, R = 0x4973f715;
        var ent = seed > uint.MaxValue ? new[] { (uint)seed, (uint)(seed >> 32) } : new[] { (uint)seed };
        var pool = new uint[4];
        uint hc = InitA;
        uint HashMix(uint v) { v ^= hc; hc *= MultA; v *= hc; v ^= v >> 16; return v; }
        static uint Mix(uint x, uint y) { uint r = L * x - R * y; return r ^ (r >> 16); }
        for (int i = 0; i < 4; i++) pool[i] = HashMix(i < ent.Length ? pool[i] ^ ent[i] : pool[i]);
        for (int s = 0; s < 4; s++) for (int d = 0; d < 4; d++) if (s != d) pool[d] = Mix(pool[d], HashMix(pool[s]));
        var st = new uint[8];
        uint hb = InitB;
        for (int i = 0; i < 8; i++)
        {
            uint v = pool[i % 4];
            v ^= hb; hb *= MultB; v *= hb; v ^= v >> 16;
            st[i] = v;
        }
        ulong W(int i) => st[i] | ((ulong)st[i + 1] << 32);
        var init = new UInt128(W(0), W(2)); var seq = new UInt128(W(4), W(6));
        _state = 0; _inc = (seq << 1) | 1;
        Step(); _state += init; Step();
    }

    void Step() => _state = unchecked(_state * Mult + _inc);

    public ulong Next64()
    {
        Step();
        ulong hi = (ulong)(_state >> 64), lo = (ulong)_state;
        return ulong.RotateRight(hi ^ lo, (int)(_state >> 122));
    }

    public double Random() => (Next64() >> 11) * (1.0 / 9007199254740992.0);
    public double[] Fill(int n) { var o = new double[n]; for (int i = 0; i < n; i++) o[i] = Random(); return o; }
}

public static class Surface
{
    public sealed record Kind(string Label, int Mat, string Colour, byte[] Rgb, int Shine, int Glow);
    public static readonly Dictionary<string, Kind> Surfaces = new()
    {
        ["tarmac"] = new("Asphalt", 1, "#565c66", new byte[] { 88, 92, 98 }, 30, 60), ["dirt"] = new("Dirt", 4, "#8a6a42", new byte[] { 122, 92, 60 }, 8, 35),
        ["roughdirt"] = new("Rough dirt", 33, "#6c5234", new byte[] { 98, 74, 50 }, 6, 30), ["gravel"] = new("Gravel", 32, "#9a9286", new byte[] { 140, 134, 122 }, 10, 40),
        ["grass"] = new("Grass", 6, "#4f7d3a", new byte[] { 70, 108, 52 }, 6, 35), ["sand"] = new("Wet sand", 9, "#c2a878", new byte[] { 176, 152, 110 }, 40, 40),
        ["water"] = new("Water (shallow)", 34, "#3a78a8", new byte[] { 52, 92, 120 }, 200, 70), ["deepwater"] = new("Deep water", 22, "#1e4a7a", new byte[] { 28, 58, 92 }, 220, 60),
    };
    public static readonly string[] Order = { "dirt", "roughdirt", "gravel", "grass", "sand", "water", "deepwater", "tarmac" };
    public const double CellM = 2.0, StepM = 4.0, PatchLift = 0.02, OverlayLift = 0.07, TexM = 8.0, SideTaper = 25.0, LevelM = 3.0;
    const int MaxTris = 760;
    public const uint FeelSourceUid = 0xA5F91096;

    public sealed class Set { public string Road = "tarmac", SideType = "dirt"; public double Sides; public bool Variety; public JObj Paint = new(); }

    public static Set Settings(JObj? m)
    {
        var s = m?.Obj("surface");
        var o = new Set();
        if (s == null) return o;
        if (s.Str("road") is string r && Surfaces.ContainsKey(r)) o.Road = r;
        if (s.Str("side_type") is string t && Surfaces.ContainsKey(t)) o.SideType = t;
        o.Sides = s.Num("sides"); o.Variety = s.Truthy("variety");
        o.Paint = s.Obj("paint") ?? new JObj();
        return o;
    }

    public static bool IsDefault(JObj m) { var s = Settings(m); return s.Road == "tarmac" && s.Sides == 0 && !s.Variety && s.Paint.Count == 0; }
    public static (int, int) CellOf(double x, double z) => ((int)Math.Floor(x / CellM), (int)Math.Floor(z / CellM));
    static double Feather(double r) => Math.Max(1.5, 0.3 * r);

    public static (string? Key, double St) ParsePaint(string? v)
    {
        if (string.IsNullOrEmpty(v)) return (null, 0);
        int c = v.IndexOf(':');
        return c >= 0 ? (v[..c], double.Parse(v[(c + 1)..], CultureInfo.InvariantCulture)) : (v, 1.0);
    }
    static string Fmt(string key, double st) => st >= 0.995 ? key : key + ":" + st.ToString("0.00", CultureInfo.InvariantCulture);

    public static int Brush(JObj m, double x, double z, double radius, string? key)
    {
        if (m.Obj("surface") == null) m["surface"] = new JObj();
        var surf = m.Obj("surface")!;
        if (surf.Obj("paint") == null) surf["paint"] = new JObj();
        var paint = surf.Obj("paint")!;
        double f = Feather(radius), reach = radius + f / 2;
        var (i0, j0) = CellOf(x - reach, z - reach); var (i1, j1) = CellOf(x + reach, z + reach);
        int n = 0;
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                double d = Py.Hypot((i + 0.5) * CellM - x, (j + 0.5) * CellM - z);
                double st = Math.Max(0.0, Math.Min(1.0, (radius - d) / f + 0.5));
                if (st <= 0.0) continue;
                string k = i.ToString(CultureInfo.InvariantCulture) + "," + j.ToString(CultureInfo.InvariantCulture);
                var (oldKey, oldS) = ParsePaint(paint.Str(k));
                if (key == null)
                {
                    if (oldKey == null) continue;
                    double ns = Math.Min(oldS, 1.0 - st);
                    if (ns < 0.05) paint.Remove(k);
                    else if (ns < oldS) paint[k] = Fmt(oldKey, ns);
                    else continue;
                    n++;
                }
                else if (oldKey == key) { if (st > oldS + 1e-6) { paint[k] = Fmt(key, st); n++; } }
                else if (oldKey == null || st > oldS) { paint[k] = Fmt(key, st); n++; }
            }
        return n;
    }

    public static Dictionary<(int, int), (string Key, double St)> PaintStrengths(JObj m)
    {
        var o = new Dictionary<(int, int), (string, double)>();
        foreach (var kv in Settings(m).Paint)
        {
            var (key, st) = ParsePaint(kv.Value as string);
            if (key == null || !Surfaces.ContainsKey(key)) continue;
            var p = kv.Key.Split(',');
            o[(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture))] = (key, st);
        }
        return o;
    }

    static Dictionary<(int, int), string> Paint(JObj m) => PaintStrengths(m).Where(kv => kv.Value.St >= 0.5).ToDictionary(kv => kv.Key, kv => kv.Value.Key);

    static readonly ConditionalWeakTable<Network, Dictionary<string, (Dictionary<(int, int), string>, HashSet<(int, int)>)>> BaseCache = new();

    public static (Dictionary<(int, int), string> Out, HashSet<(int, int)> Sides) BaseCells(JObj m, Network net)
    {
        var s = Settings(m);
        string key = $"{net.Main.Count}|{net.Main.Length:F2}|{s.Road}|{s.Sides}|{s.SideType}";
        var c = BaseCache.GetOrCreateValue(net);
        lock (c)
        {
            if (!c.TryGetValue(key, out var hit)) c[key] = hit = BaseCells0(s, net);
            return (new(hit.Item1), new(hit.Item2));
        }
    }

    static (Dictionary<(int, int), string>, HashSet<(int, int)>) BaseCells0(Set s, Network net)
    {
        var o = new Dictionary<(int, int), string>(); var sides = new HashSet<(int, int)>();
        if (s.Road == "tarmac" && s.Sides == 0) return (o, sides);
        double side = s.Sides, step = CellM / 2;
        var mainArea = net.Branches.Count > 0 ? net.Areas()[net.Main] : null;
        foreach (var r in net.Drivable())
        {
            var cut = net.WallMask(r);
            var fac = new[] { SideFactor(net, r, 0), SideFactor(net, r, 1) };
            int n = (int)Math.Floor(r.Length / step) + 1;
            for (int q = 0; q < n; q++)
            {
                double sq = q * step;
                int i = r.IndexAt(sq);
                var (cx, cz, a) = r.At(sq);
                double sx = -Math.Cos(a), sz = Math.Sin(a), half = r.WAt(sq) / 2, k = -half;
                while (k <= half)
                {
                    double x = cx + sx * k, z = cz + sz * k;
                    if (r != net.Main && mainArea != null && mainArea.Depth(x, z) > 0) { k += step; continue; }
                    var c = CellOf(x, z);
                    int si = k < 0 ? 0 : 1;
                    if (side > 0 && Math.Abs(k) > half - side * fac[si][i] && !cut[si][i]) { o[c] = s.SideType; sides.Add(c); }
                    else o.TryAdd(c, s.Road);
                    k += step;
                }
            }
        }
        return (o, sides);
    }

    public static double[] SideFactor(Network net, Road road, int si)
    {
        var cut = net.WallMask(road)[si];
        int n = road.Count;
        if (!cut.Any(b => b)) return Enumerable.Repeat(1.0, n).ToArray();
        const long big = 1000000000;
        var dist = new long[n];
        for (int i = 0; i < n; i++) dist[i] = cut[i] ? 0 : big;
        for (int pass = 0; pass < 2; pass++)
        {
            int At(int k) => pass == 0 ? k : n - 1 - k;
            for (int t = 0; t < (road.Closed ? 2 : 1); t++)
            {
                int prev = -1;
                for (int k = 0; k < n; k++)
                {
                    int i = At(k);
                    if (prev >= 0) dist[i] = Math.Min(dist[i], dist[prev] + 1);
                    prev = i;
                }
                if (road.Closed) dist[At(0)] = Math.Min(dist[At(0)], dist[At(n - 1)] + 1);
            }
        }
        return dist.Select(d => { double t = Math.Min(1.0, d * StepM / SideTaper); return t * t * (3 - 2 * t); }).ToArray();
    }

    public static List<(Road R, int Side, List<int> Run)> SideGaps(JObj m, Network net)
    {
        var o = new List<(Road, int, List<int>)>();
        if (Settings(m).Sides == 0) return o;
        foreach (var r in net.Drivable())
        {
            var cut = net.WallMask(r);
            int si = 0;
            foreach (int side in new[] { -1, 1 })
            {
                var run = new List<int>();
                for (int i = 0; i < r.Count; i++)
                {
                    if (cut[si][i]) run.Add(i);
                    else if (run.Count > 0) { o.Add((r, side, run)); run = new(); }
                }
                if (run.Count > 0) o.Add((r, side, run));
                si++;
            }
        }
        return o;
    }

    /// the final surface of every cell that is not plain asphalt (2D view)
    public static Dictionary<(int, int), string> Grid(JObj m, Network net)
    {
        var (o, sides) = BaseCells(m, net);
        foreach (var (c, k) in Paint(m)) if (!sides.Contains(c)) o[c] = k;
        return o.Where(kv => kv.Value != "tarmac").ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    static Func<double, double, double> Heights(Network net)
    {
        var areas = net.Areas(); var roads = net.Drivable().ToList();
        return (x, z) =>
        {
            double best = 0, hh = 0;
            foreach (var r in roads)
            {
                var (d, rh) = areas[r].DepthH(x, z);
                if (d > -CellM && (d > best || best == 0.0)) { best = d; hh = rh; }
            }
            return hh;
        };
    }

    static List<(string Key, double X0, double Z0, double X1, double Z1, double Y)> Rects(Dictionary<(int, int), string> cells, Func<double, double, double> height)
    {
        var rows = new Dictionary<int, List<(int I, string K)>>();
        foreach (var ((i, j), k) in cells)
        {
            if (!rows.TryGetValue(j, out var l)) rows[j] = l = new();
            l.Add((i, k));
        }
        var runs = new Dictionary<(int, int, string, double), List<int>>();
        foreach (var (j, lst) in rows)
        {
            lst.Sort((a, b) => a.I != b.I ? a.I.CompareTo(b.I) : string.CompareOrdinal(a.K, b.K));
            int a2 = 0;
            while (a2 < lst.Count)
            {
                int b = a2;
                while (b + 1 < lst.Count && lst[b + 1].I == lst[b].I + 1 && lst[b + 1].K == lst[a2].K) b++;
                int i0 = lst[a2].I, i1 = lst[b].I;
                double y = Py.Round(height((i0 + i1 + 1) / 2.0 * CellM, (j + 0.5) * CellM) * 2) / 2.0;
                var key = (i0, i1, lst[a2].K, y);
                if (!runs.TryGetValue(key, out var l)) runs[key] = l = new();
                l.Add(j);
                a2 = b + 1;
            }
        }
        var o = new List<(string, double, double, double, double, double)>();
        foreach (var ((i0, i1, key, y), js) in runs)
        {
            js.Sort();
            int a = 0;
            while (a < js.Count)
            {
                int b = a;
                while (b + 1 < js.Count && js[b + 1] == js[b] + 1) b++;
                o.Add((key, i0 * CellM, js[a] * CellM, (i1 + 1) * CellM, (js[b] + 1) * CellM, y));
                a = b + 1;
            }
        }
        return o;
    }

    static (Dictionary<(int, int, int), string>, HashSet<(int, int, int)>, Dictionary<(int, int, int), double>) LevelBase(JObj m, Network net)
    {
        var s = Settings(m);
        var o = new Dictionary<(int, int, int), string>(); var sides = new HashSet<(int, int, int)>(); var hs = new Dictionary<(int, int, int), double>();
        if (s.Road == "tarmac" && s.Sides == 0) return (o, sides, hs);
        double side = s.Sides, step = CellM / 2;
        var mainArea = net.Branches.Count > 0 ? net.Areas()[net.Main] : null;
        foreach (var r in net.Drivable())
        {
            var cut = net.WallMask(r);
            var fac = new[] { SideFactor(net, r, 0), SideFactor(net, r, 1) };
            int n = (int)Math.Floor(r.Length / step) + 1;
            for (int q = 0; q < n; q++)
            {
                double sq = q * step;
                int i = r.IndexAt(sq);
                var (cx, cz, a) = r.At(sq);
                double sx = -Math.Cos(a), sz = Math.Sin(a), half = r.WAt(sq) / 2, h = r.HAt(sq), k = -half;
                int lev = (int)Py.Round(h / LevelM);
                while (k <= half)
                {
                    double x = cx + sx * k, z = cz + sz * k, hk = r.HAt(sq, k);
                    if (r != net.Main && mainArea != null && mainArea.Depth(x, z, h) > 0) { k += step; continue; }
                    var (ci, cj) = CellOf(x, z);
                    var c = (ci, cj, r.Tilted ? (int)Py.Round(hk / LevelM) : lev);
                    hs.TryAdd(c, hk);
                    int si = k < 0 ? 0 : 1;
                    if (side > 0 && Math.Abs(k) > half - side * fac[si][i] && !cut[si][i]) { o[c] = s.SideType; sides.Add(c); }
                    else o.TryAdd(c, s.Road);
                    k += step;
                }
            }
        }
        return (o, sides, hs);
    }

    public static List<(Piece P, int Mat)> PhysicsPatches(JObj m, Network net)
    {
        var o = new List<(Piece, int)>();
        if (IsDefault(m)) return o;
        var (cells0, sides, hs) = LevelBase(m, net);
        var levels = new Dictionary<(int, int), List<int>>();
        foreach (var (i, j, lev) in hs.Keys)
        {
            if (!levels.TryGetValue((i, j), out var l)) levels[(i, j)] = l = new();
            l.Add(lev);
        }
        foreach (var ((i, j), k) in Paint(m))
            foreach (int lev in levels.TryGetValue((i, j), out var ll) ? ll.ToArray() : new[] { 0 })
            {
                var c = (i, j, lev);
                if (!sides.Contains(c)) { cells0[c] = k; hs.TryAdd(c, 0.0); }
            }
        var cells = cells0.Where(kv => kv.Value != "tarmac").ToList();
        var areas = net.Areas(); var roads = net.Drivable().ToList();
        var vcache = new Dictionary<(int, int, int), double>();
        double Vh(int i, int j, int lev, double href)
        {
            if (vcache.TryGetValue((i, j, lev), out double v)) return v;
            double x = i * CellM, z = j * CellM, best = -1e9; double? got = null;
            foreach (var r in roads)
            {
                var (d, h) = areas[r].SurfaceH(x, z, href);
                if (d > -CellM * 1.5 && d > best) { best = d; got = h; }
            }
            v = got ?? (lev != 0 ? href : 0.0);
            return vcache[(i, j, lev)] = v;
        }
        var flat = new Dictionary<(int, int), List<(string K, double Y)>>();
        var slope = new Dictionary<(int, int, int), (string K, double Href)>();
        foreach (var ((i, j, lev), k) in cells)
        {
            double href = hs.GetValueOrDefault((i, j, lev), 0.0);
            double a = Vh(i, j, lev, href), b = Vh(i + 1, j, lev, href), c = Vh(i, j + 1, lev, href), d = Vh(i + 1, j + 1, lev, href);
            double mx = Math.Max(a, Math.Max(b, Math.Max(c, d))), mn = Math.Min(a, Math.Min(b, Math.Min(c, d)));
            if (mx - mn < 0.02)
            {
                if (!flat.TryGetValue((i, j), out var l)) flat[(i, j)] = l = new();
                l.Add((k, Py.RoundN((a + b + c + d) / 4, 2)));
            }
            else slope[(i, j, lev)] = (k, href);
        }
        var byKh = new Dictionary<(string, double), Dictionary<(int, int), string>>();
        foreach (var ((i, j), lst) in flat)
            foreach (var (k, y) in lst)
            {
                if (!byKh.TryGetValue((k, y), out var d)) byKh[(k, y)] = d = new();
                d[(i, j)] = k;
            }
        foreach (var ((_, y), cl) in byKh)
            foreach (var (key, x0, z0, x1, z1, _) in Rects(cl, (_, _) => 0.0))
            {
                double yy = Fe.GroundY + y + PatchLift;
                o.Add((Physics.DeckStrip(new[] { new[] { x0, yy, z0 }, new[] { x0, yy, z1 } }, new[] { new[] { x1, yy, z0 }, new[] { x1, yy, z1 } }), Surfaces[key].Mat));
            }
        var cols = new Dictionary<(int, int, string), List<(int J, double Href)>>();
        foreach (var ((i, j, lev), (k, href)) in slope)
        {
            if (!cols.TryGetValue((i, lev, k), out var l)) cols[(i, lev, k)] = l = new();
            l.Add((j, href));
        }
        foreach (var ((i, lev, k), lst0) in cols)
        {
            var lst = lst0.OrderBy(q => q.J).ThenBy(q => q.Href).ToList();
            int a = 0;
            while (a < lst.Count)
            {
                int b = a;
                while (b + 1 < lst.Count && lst[b + 1].J == lst[b].J + 1 && b - a < 120) b++;
                var left = new List<double[]>(); var right = new List<double[]>();
                for (int t = a; t < b + 2; t++)
                {
                    int j = lst[a].J + (t - a);
                    double href = lst[Math.Min(t, b)].Href, z = j * CellM;
                    left.Add(new[] { i * CellM, Fe.GroundY + Vh(i, j, lev, href) + PatchLift, z });
                    right.Add(new[] { (i + 1) * CellM, Fe.GroundY + Vh(i + 1, j, lev, href) + PatchLift, z });
                }
                o.Add((Physics.DeckStrip(left, right), Surfaces[k].Mat));
                a = b + 1;
            }
        }
        return o;
    }

    // ---- textures ------------------------------------------------------------------------------------------------
    static double[] TileNoise(int w, int h, int seed, double scale)
    {
        var a = new NumpyRng(seed).Fill(w * h);
        int k = Math.Max(1, (int)scale), span = 2 * k + 1;
        for (int t = 0; t < 3; t++)
        {
            var b = new double[w * h];
            Parallel.For(0, h, y =>
            {
                for (int s = -k; s <= k; s++)
                {
                    int yy = (int)Py.Mod(y - s, h);
                    for (int x = 0; x < w; x++) b[y * w + x] += a[yy * w + x];
                }
                for (int x = 0; x < w; x++) b[y * w + x] /= span;
            });
            var c = new double[w * h];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double v = 0;
                    for (int s = -k; s <= k; s++) v += b[y * w + (int)Py.Mod(x - s, w)];
                    c[y * w + x] = v / span;
                }
            });
            a = c;
        }
        double mn = a.Min();
        for (int i = 0; i < a.Length; i++) a[i] -= mn;
        double mx = Math.Max(a.Max(), 1e-9);
        for (int i = 0; i < a.Length; i++) a[i] /= mx;
        return a;
    }

    public static (byte[] Diff, byte[] Mat) MaterialTexture(string key, int w, int h, int seed = 1)
    {
        var sf = Surfaces[key];
        var n1 = TileNoise(w, h, seed, w / 32.0);
        var n2 = TileNoise(w, h, seed + 7, w / 128.0);
        var fine = new NumpyRng(seed + 3).Fill(w * h);
        var rgb = new byte[w * h * 3]; var mat = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x; double f;
                if (key is "water" or "deepwater") f = 0.85 + 0.25 * n1[i] + 0.06 * Math.Sin(n2[i] * 12.0);
                else if (key == "grass") f = 0.75 + 0.35 * n1[i] + 0.25 * (fine[i] - 0.5);
                else if (key == "gravel") f = 0.80 + 0.20 * n1[i] + 0.45 * (fine[i] - 0.5);
                else if (key == "sand") f = 0.90 + 0.15 * n1[i] + 0.06 * Math.Sin((double)y * 2 * Math.PI * 6 / h + n2[i] * 4);
                else f = 0.78 + 0.30 * n1[i] + 0.20 * (n2[i] - 0.5) + 0.22 * (fine[i] - 0.5);
                for (int c = 0; c < 3; c++) rgb[3 * i + c] = (byte)Math.Max(0, Math.Min(255, sf.Rgb[c] * f));
                mat[3 * i] = (byte)sf.Shine; mat[3 * i + 1] = (byte)sf.Glow;
            }
        return (rgb, mat);
    }

    public static (byte[] Diff, byte[] Mat) RoadTextureProfile(int w, int h, double width)
    {
        var prof = Fe.RoadProfile(width);
        int nb = Fe.Bank.Length;
        double vl = prof[nb - 1].V, vr = prof[nb].V, vMid = (vl + vr) / 2;
        var diff = CpModel.Noise(w, h, Fe.RoadGrey, 12);
        var mat = CpModel.Solid(w, h, Fe.RoadShine, Fe.RoadGlow, 0);
        double rowV = 1.0 / h;
        void Set(int i, byte a, byte b, byte c, byte g) { diff[i] = a; diff[i + 1] = b; diff[i + 2] = c; mat[i] = Fe.RoadShine; mat[i + 1] = g; mat[i + 2] = 0; }
        for (int y = 0; y < h; y++)
        {
            double v = (y + 0.5) / h;
            bool onBank = v < vl || v > vr;
            double toEdge = Math.Min(Math.Abs(v - vl), Math.Abs(v - vr));
            bool top = Math.Min(v, 1 - v) < 1.6 * rowV;
            for (int x = 0; x < w; x++)
            {
                int i = 3 * (y * w + x);
                if (top) Set(i, 40, 120, 255, 255);
                else if (toEdge < 0.8 * rowV) Set(i, Fe.WallColour[0], Fe.WallColour[1], Fe.WallColour[2], 255);
                else if (onBank) Set(i, Fe.BankColour[0], Fe.BankColour[1], Fe.BankColour[2], Fe.BankGlow);
                else if (Math.Abs(v - vMid) < 0.5 * rowV && x < w / 2) Set(i, 235, 235, 235, 160);
            }
        }
        return (diff, mat);
    }

    public static (byte[] Diff, byte[] Mat) RoadTexture(int w, int h, double width, JObj m, int variant = 0)
    {
        var (diff, mat) = RoadTextureProfile(w, h, width);
        var s = Settings(m);
        if (s.Road == "tarmac" && s.Sides == 0 && !s.Variety) return (diff, mat);
        var img = new double[w * h * 3];
        for (int i = 0; i < img.Length; i++) img[i] = diff[i];
        var mt = (byte[])mat.Clone();
        var keep = new bool[w * h];
        for (int i = 0; i < w * h; i++) keep[i] = Math.Min(diff[3 * i], Math.Min(diff[3 * i + 1], diff[3 * i + 2])) > 200;
        var prof = Fe.RoadProfile(width);
        int nb = Fe.Bank.Length;
        double vl = prof[nb - 1].V, vr = prof[nb].V;
        var rows = new double[h]; var across = new double[h]; var onRoad = new bool[h];
        for (int y = 0; y < h; y++)
        {
            rows[y] = (y + 0.5) / h;
            across[y] = (rows[y] - (vl + vr) / 2) / Math.Max(vr - vl, 1e-9) * width;
            onRoad[y] = rows[y] > vl + 1.0 / h && rows[y] < vr - 1.0 / h;
        }
        void Rows(string key, Func<int, bool> sel)
        {
            var (t, tm) = MaterialTexture(key, w, h, 3 + variant);
            for (int y = 0; y < h; y++)
                if (sel(y))
                    for (int i = 3 * y * w; i < 3 * (y + 1) * w; i++) { img[i] = t[i]; mt[i] = tm[i]; }
        }
        if (s.Road != "tarmac") Rows(s.Road, y => onRoad[y]);
        else if (s.Variety)
        {
            var blot = TileNoise(w, h, 21 + variant, w / 20.0);
            var patch = TileNoise(w, h, 31 + variant, w / 10.0);
            for (int y = 0; y < h; y++)
            {
                if (!onRoad[y]) continue;
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    double f = 0.80 + 0.40 * blot[p];
                    if (patch[p] > 0.78) f *= 0.72;
                    for (int c = 0; c < 3; c++) img[3 * p + c] *= f;
                }
            }
            // crack lines: same idea as _cracks, not the same random numbers (numpy's normal() is not ported)
            var r = new PyRandom(11 + variant);
            for (int c = 0; c < 18; c++)
            {
                int x = r.RandInt(0, w - 1), y = r.RandInt(0, h - 1), len = r.RandInt(20, 69);
                double ang = r.Random() * 2 * Math.PI;
                for (int t = 0; t < len; t++)
                {
                    ang += (r.Random() + r.Random() + r.Random() - 1.5) * 0.7;
                    x = (int)Py.Mod(x + (int)Py.Round(Math.Cos(ang) * 2), w);
                    y += (int)Py.Round(Math.Sin(ang) * 2);
                    if (y < 0 || y >= h) break;
                    if (onRoad[y]) for (int q = 0; q < 3; q++) img[3 * (y * w + x) + q] *= 0.45;
                }
            }
        }
        if (s.Road == "tarmac")
            for (int i = 0; i < w * h; i++) if (keep[i]) for (int c = 0; c < 3; c++) img[3 * i + c] = diff[3 * i + c];
        if (s.Sides > 0) Rows(s.SideType, y => onRoad[y] && Math.Abs(across[y]) > width / 2 - s.Sides);
        var o = new byte[img.Length];
        for (int i = 0; i < o.Length; i++) o[i] = (byte)Math.Max(0, Math.Min(255, img[i]));
        return (o, mt);
    }

    // ---- overlay models -----------------------------------------------------------------------------------------
    public static SortedDictionary<string, (List<(double, double, double, double)> Rects, List<List<(double X, double Z)>> Polys)> PaintShapes(JObj m, Network net)
    {
        var o = new SortedDictionary<string, (List<(double, double, double, double)>, List<List<(double, double)>>)>(StringComparer.Ordinal);
        var ps = PaintStrengths(m);
        if (ps.Count == 0) return o;
        var (_, sides) = BaseCells(m, net);
        foreach (string key in ps.Values.Select(v => v.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal))
        {
            var val = new Dictionary<(int, int), double>();
            foreach (var (c, (k, st)) in ps) if (k == key && !sides.Contains(c)) val[c] = st;
            var squares = new HashSet<(int, int)>();
            foreach (var (i, j) in val.Keys) foreach (int di in new[] { -1, 0 }) foreach (int dj in new[] { -1, 0 }) squares.Add((i + di, j + dj));
            var full = new List<(int, int)>(); var polys = new List<List<(double, double)>>();
            foreach (var (i, j) in squares.OrderBy(q => q.Item2).ThenBy(q => q.Item1))
            {
                var cs = new[] { (i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1) };
                var a = cs.Select(c => val.GetValueOrDefault(c, 0.0)).ToArray();
                var pos = cs.Select(c => ((c.Item1 + 0.5) * CellM, (c.Item2 + 0.5) * CellM)).ToArray();
                var inside = a.Select(v => v >= 0.5).ToArray();
                if (inside.All(b => b)) { full.Add((i, j)); continue; }
                if (!inside.Any(b => b)) continue;
                var poly = new List<(double, double)>();
                for (int k = 0; k < 4; k++)
                {
                    int n = (k + 1) % 4;
                    if (inside[k]) poly.Add(pos[k]);
                    if (inside[k] != inside[n])
                    {
                        double t = (0.5 - a[k]) / (a[n] - a[k]);
                        poly.Add((pos[k].Item1 + (pos[n].Item1 - pos[k].Item1) * t, pos[k].Item2 + (pos[n].Item2 - pos[k].Item2) * t));
                    }
                }
                if (poly.Count >= 3) polys.Add(poly);
            }
            var rows = new Dictionary<int, List<int>>();
            foreach (var (i, j) in full)
            {
                if (!rows.TryGetValue(j, out var l)) rows[j] = l = new();
                l.Add(i);
            }
            var runs = new Dictionary<(int, int), List<int>>();
            foreach (var (j, lst) in rows)
            {
                lst.Sort();
                int a = 0;
                while (a < lst.Count)
                {
                    int b = a;
                    while (b + 1 < lst.Count && lst[b + 1] == lst[b] + 1) b++;
                    if (!runs.TryGetValue((lst[a], lst[b]), out var l)) runs[(lst[a], lst[b])] = l = new();
                    l.Add(j);
                    a = b + 1;
                }
            }
            var rects = new List<(double, double, double, double)>();
            foreach (var ((i0, i1), js) in runs)
            {
                js.Sort();
                int a = 0;
                while (a < js.Count)
                {
                    int b = a;
                    while (b + 1 < js.Count && js[b + 1] == js[b] + 1) b++;
                    rects.Add(((i0 + 0.5) * CellM, (js[a] + 0.5) * CellM, (i1 + 1.5) * CellM, (js[b] + 1.5) * CellM));
                    a = b + 1;
                }
            }
            o[key] = (rects, polys);
        }
        return o;
    }

    static readonly ConditionalWeakTable<Network, Dictionary<string, List<Chunk>>> OverlayCache = new();

    public static List<Chunk> OverlayChunks(JObj m, Network net)
    {
        string sig = Json.Dumps(m["surface"], -1);
        var c = OverlayCache.GetOrCreateValue(net);
        lock (c)
        {
            if (!c.TryGetValue(sig, out var hit)) { if (c.Count > 4) c.Clear(); c[sig] = hit = Overlay0(m, net); }
            return hit;
        }
    }

    static List<Chunk> Overlay0(JObj m, Network net)
    {
        var o = new List<Chunk>();
        var shapes = PaintShapes(m, net);
        var gaps = SideGaps(m, net);
        if (shapes.Count == 0 && gaps.Count == 0) return o;
        var bas = BaseCells(m, net).Out;
        var hgt = Heights(net);
        var areas = net.Areas(); var roads = net.Drivable().ToList();
        double OnRoad(double x, double z, double href)
        {
            double best = -1e9, v = href;
            foreach (var r in roads)
            {
                var (d, h) = areas[r].SurfaceH(x, z, href);
                if (d > -CellM * 1.5 && d > best) { best = d; v = h; }
            }
            return v;
        }
        var st = Settings(m);
        List<(double X, double Z)[]>? gtris = null;
        if (gaps.Count > 0)
        {
            gtris = new();
            double S = st.Sides;
            foreach (var r in net.Drivable())
            {
                var clip = net.Clip(r);
                int n = r.Count; double W = r.Width;
                int si = 0;
                foreach (int side in new[] { -1, 1 })
                {
                    var fac = SideFactor(net, r, si);
                    var eOwn = r.Edge(side); var eOther = r.Edge(-side);
                    var pairs = Enumerable.Range(0, n - 1).Select(i => (i, i + 1)).ToList();
                    if (r.Closed) pairs.Add((n - 1, 0));
                    foreach (var (a, b) in pairs)
                    {
                        if (fac[a] >= 0.999 && fac[b] >= 0.999) continue;
                        var q = new List<((double, double) L, (double, double) H)>();
                        foreach (int i in new[] { a, b })
                        {
                            var (ex, ez) = eOwn[i]; var (ox, oz) = eOther[i];
                            if (clip != null) { (ex, ez) = clip(i, ex, ez); (ox, oz) = clip(i, ox, oz); }
                            double tLo = Math.Min(S / W, 0.5), tHi = Math.Min(S * fac[i] / W, 0.5);
                            q.Add(((ex + (ox - ex) * tLo, ez + (oz - ez) * tLo), (ex + (ox - ex) * tHi, ez + (oz - ez) * tHi)));
                        }
                        gtris.Add(new[] { q[0].L, q[1].L, q[1].H }); gtris.Add(new[] { q[0].L, q[1].H, q[0].H });
                    }
                    si++;
                }
            }
            if (!shapes.ContainsKey(st.Road)) shapes[st.Road] = (new(), new());
        }
        foreach (var (key, (rects, polys)) in shapes)
        {
            var tris = new List<(double X, double Z)[]>();
            var gapSet = new HashSet<(double X, double Z)[]>();
            if (key == st.Road && gtris != null) { tris.AddRange(gtris); gapSet.UnionWith(gtris); }
            foreach (var (x0, z0, x1, z1) in rects)
            {
                tris.Add(new[] { (x0, z0), (x1, z0), (x1, z1) });
                tris.Add(new[] { (x0, z0), (x1, z1), (x0, z1) });
            }
            foreach (var poly in polys)
                for (int k = 1; k < poly.Count - 1; k++) tris.Add(new[] { poly[0], poly[k], poly[k + 1] });
            if (key == "tarmac")
                tris = tris.Where(t => gapSet.Contains(t) || (bas.TryGetValue(CellOf(t[0].X, t[0].Z), out var b) && b != "tarmac")).ToList();
            tris = tris.OrderBy(t => Py.Round(t[0].Z / 300)).ThenBy(t => t[0].X).ToList();
            for (int a0 = 0; a0 < tris.Count; a0 += MaxTris)
            {
                var part = tris.GetRange(a0, Math.Min(MaxTris, tris.Count - a0));
                double xmin = double.MaxValue, xmax = double.MinValue, zmin = xmin, zmax = xmax;
                foreach (var t in part) foreach (var p in t) { xmin = Math.Min(xmin, p.X); xmax = Math.Max(xmax, p.X); zmin = Math.Min(zmin, p.Z); zmax = Math.Max(zmax, p.Z); }
                double cx = (xmin + xmax) / 2, cz = (zmin + zmax) / 2;
                var ch = new Chunk { Key = key, Anchor = new[] { cx, Fe.GroundY, cz } };
                foreach (var t in part)
                {
                    double ax = t[0].X, az = t[0].Z, bx = t[1].X, bz = t[1].Z, qx = t[2].X, qz = t[2].Z;
                    if (Math.Abs((bz - az) * (qx - ax) - (bx - ax) * (qz - az)) < 1e-6) continue;
                    double href = hgt(ax, az);
                    var pts = t.Select(p => new[] { p.X, OnRoad(p.X, p.Z, href) - Fe.TileTopBelow + OverlayLift, p.Z }).ToArray();
                    double cy = (pts[1][2] - pts[0][2]) * (pts[2][0] - pts[0][0]) - (pts[1][0] - pts[0][0]) * (pts[2][2] - pts[0][2]);
                    if (cy <= 0) (pts[1], pts[2]) = (pts[2], pts[1]);
                    int b0 = ch.Verts.Count;
                    foreach (var q in pts) ch.Verts.Add(new[] { q[0] - cx, q[1], q[2] - cz, 0.0, 1.0, 0.0, (q[0] - cx) / TexM, (q[2] - cz) / TexM });
                    if (ch.Strip.Count > 0)
                    {
                        ch.Strip.Add(ch.Strip[^1]); ch.Strip.Add(b0);
                        if (ch.Strip.Count % 2 != 0) ch.Strip.Add(b0);
                    }
                    ch.Strip.Add(b0); ch.Strip.Add(b0 + 1); ch.Strip.Add(b0 + 2);
                }
                o.Add(ch);
            }
        }
        return o;
    }

    public static uint TextureSalt(JObj m, string key, int variant = 0) =>
        0x0F3C0000 | (((uint)m.Int("route_uid") & 0xFF) << 8) | ((uint)Surfaces[key].Mat & 0x3F) << 2 | ((uint)variant & 3);

    // ---- how they drive ---------------------------------------------------------------------------------------------
    public static readonly Dictionary<string, uint> FeelUids = new() { ["strong"] = 0xF1D50001, ["extreme"] = 0xF1D50002 };
    static readonly Dictionary<string, Dictionary<string, double[]>> Feel = new()
    {
        ["strong"] = new()
        {
            ["Dirt"] = new[] { 0.72, 45.0, 4.0, 1.0, 0.92 }, ["Grass"] = new[] { 0.62, 40.0, 6.0, 1.2, 0.90 }, ["DirtLightRough"] = new[] { 0.80, 45.0, 3.0, 1.4, 0.94 },
            ["DirtHeavyRough"] = new[] { 0.70, 40.0, 9.0, 2.0, 0.90 }, ["WetSand"] = new[] { 0.60, 40.0, 10.0, 1.25, 0.90 }, ["Water"] = new[] { 0.55, 35.0, 12.0, 0.9, 0.85 },
            ["WaterDeep"] = new[] { 1.0, 1.0, 25.0, 2.0, 1.0 },
        },
        ["extreme"] = new()
        {
            ["Dirt"] = new[] { 0.55, 35.0, 8.0, 1.4, 0.85 }, ["Grass"] = new[] { 0.45, 30.0, 10.0, 1.5, 0.82 }, ["DirtLightRough"] = new[] { 0.65, 35.0, 6.0, 1.8, 0.88 },
            ["DirtHeavyRough"] = new[] { 0.55, 30.0, 16.0, 2.6, 0.82 }, ["WetSand"] = new[] { 0.45, 30.0, 18.0, 1.6, 0.82 }, ["Water"] = new[] { 0.40, 30.0, 20.0, 1.2, 0.78 },
            ["WaterDeep"] = new[] { 1.0, 1.0, 45.0, 2.5, 1.0 },
        },
    };

    public static string LocFeel(JObj wld, JObj loc) => loc.Truthy("surface_feel") ? loc.Str("surface_feel")! : wld.ContainsKey("surface_feel") ? wld.Str("surface_feel")! : "strong";

    public static (byte[] Data, uint? Uid) FeelTable(byte[] rp, string feel)
    {
        if (!Feel.TryGetValue(feel, out var vals)) return (rp, null);
        uint tableUid = FeelUids[feel];
        var k = new Kslf(rp);
        int src = k.ObjOf(FeelSourceUid);
        var top = k.Top(k.Objects[src]);
        var names = ((List<object?>)top["properties"]!).Select(p => (((Dictionary<string, object?>)((Dictionary<string, object?>)p!)["Surface"]!)["name"] as string ?? "").Trim()).ToList();
        var data = KslfWrite.CloneObjectAny(rp, src, null, tableUid, null);
        var k2 = new Kslf(data);
        int newObj = k2.ObjOf(tableUid), ts = k2.TypeIndex("RP_TrackSurface");
        var o = KslfWrite.EditObject(data, newObj, ctx =>
        {
            int b = ctx.Rows.FindIndex(r => r[0] == ts && r[2] == 0);
            int bas = ctx.Blocks[b];
            for (int e = 0; e < names.Count; e++)
                if (vals.TryGetValue(names[e], out var v))
                    for (int i = 0; i < 5; i++) ctx.Data.Put(bas + 36 * e + 12 + 4 * i, (float)v[i]);
        });
        return (o, tableUid);
    }
}

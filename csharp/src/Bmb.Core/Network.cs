// The road network of a map: main road + branches + lane splits. Port of flat_earth/network.py.
namespace Bmb.Core;

public sealed class Area
{
    public readonly Road R; readonly double _cell;
    readonly Dictionary<(int, int), List<(double X, double Z, double Fx, double Fz, double Hw, double Ph, int I)>> _grid = new();
    readonly int _reach; readonly (double X0, double X1, double Z0, double Z1) _bb;
    readonly Dictionary<(int, int), (double X, double Z, double Fx, double Fz, double Hw, double Ph, int I)[]> _near = new();
    const double Lim = Road.Step * 0.75;

    public Area(Road road, double cell = 40.0)
    {
        R = road; _cell = cell;
        for (int i = 0; i < road.Count; i++)
        {
            var (x, z) = road.Centre[i]; var (fx, fz) = road.Forward(i);
            var k = ((int)Math.Floor(x / cell), (int)Math.Floor(z / cell));
            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new();
            l.Add((x, z, fx, fz, road.WidthAt[i] / 2, road.Height[i], i));
        }
        double mw = road.WidthAt.Max();
        _reach = (int)Math.Ceiling(mw / 2 / cell) + 1;
        _bb = road.Bounds(mw / 2 + 10.0);
    }

    (double X, double Z, double Fx, double Fz, double Hw, double Ph, int I)[] Near(int gx, int gz)
    {
        lock (_near)
        {
            if (_near.TryGetValue((gx, gz), out var p)) return p;
            var l = new List<(double, double, double, double, double, double, int)>();
            for (int ax = gx - _reach; ax <= gx + _reach; ax++)
                for (int az = gz - _reach; az <= gz + _reach; az++)
                    if (_grid.TryGetValue((ax, az), out var c)) l.AddRange(c);
            return _near[(gx, gz)] = l.ToArray();
        }
    }

    public double Depth(double x, double z, double? h = null) => DepthH(x, z, h).Depth;

    public (double Depth, double H) DepthH(double x, double z, double? h = null)
    {
        if (!(_bb.X0 <= x && x <= _bb.X1 && _bb.Z0 <= z && z <= _bb.Z1)) return (-1e9, 0.0);
        double best = -1e9, bh = 0.0;
        foreach (var (cx, cz, fx, fz, hw, ph, i) in Near((int)Math.Floor(x / _cell), (int)Math.Floor(z / _cell)))
        {
            double dx = x - cx, dz = z - cz, along = dx * fx + dz * fz;
            if (along > Lim || along < -Lim) continue;
            double side = dz * fx - dx * fz, d = hw - (side >= 0 ? side : -side), sh = ph + side * R.TanOf(i);
            if (h != null && Math.Abs(sh - h.Value) > 3.0) continue;
            if (d > best) { best = d; bh = sh; }
        }
        return (best, bh);
    }

    public (double Depth, double H) SurfaceH(double x, double z, double? h = null)
    {
        if (!(_bb.X0 <= x && x <= _bb.X1 && _bb.Z0 <= z && z <= _bb.Z1)) return (-1e9, 0.0);
        double best = -1e9, ba = 0.0, bs = 0.0; int bi = -1;
        foreach (var (cx, cz, fx, fz, hw, ph, i) in Near((int)Math.Floor(x / _cell), (int)Math.Floor(z / _cell)))
        {
            double dx = x - cx, dz = z - cz, along = dx * fx + dz * fz;
            if (along > Lim || along < -Lim) continue;
            double side = dz * fx - dx * fz, d = hw - (side >= 0 ? side : -side);
            if (h != null && Math.Abs(ph + side * R.TanOf(i) - h.Value) > 3.0) continue;
            if (d > best) { best = d; bi = i; ba = along; bs = side; }
        }
        if (bi < 0) return (best, 0.0);
        int n = R.Height.Count, j = bi + (ba >= 0 ? 1 : -1);
        if (R.Closed) j = (int)Py.Mod(j, n);
        else if (j < 0 || j >= n) return (best, R.Surf(bi, bs));
        double t = Math.Min(1.0, Math.Abs(ba) / Road.Step);
        return (best, R.Surf(bi, bs) + (R.Surf(j, bs) - R.Surf(bi, bs)) * t);
    }
}

public sealed class AltRoute { public string Name = "", Junction = ""; public List<double[]> Points = new(); public List<int> Logic = new(); }

public sealed class Network
{
    public const double BranchLead = 40.0, BranchWidth = 30.0, MinBranchGap = 120.0, DividerW = 1.0, DividerHalf = 3.2, SplitEase = 70.0,
        SplitMin = 60.0, DividerNose = 24.0, SinkH = 0.3, HeightHold = 20.0, HeightEase = 40.0, MaxGrade = 0.10;
    public const int JunctionPoints = 16, AltOnMain = 12;
    const double Step = Road.Step;

    public JObj M; public Road Main;
    public List<(JObj B, Road R)> Branches = new();
    public List<(JObj Sp, List<Road> Ds)> Splits = new();
    public List<Road> Dividers = new();
    public List<string> Problems = new();
    Dictionary<Road, Area>? _areas;
    readonly Dictionary<Road, bool[][]> _masks = new();
    (Dictionary<Road, Dictionary<int, List<(double, double)>>> Cuts, HashSet<Road> Done)? _jn;
    public List<((double, double) P, Road Br, string Kind)> Tips = new();
    public string Sig = "";

    static (double T, double U, (double, double) Pt)? SegX((double X, double Z) p, (double X, double Z) q, (double X, double Z) a, (double X, double Z) b)
    {
        double rx = q.X - p.X, rz = q.Z - p.Z, sx = b.X - a.X, sz = b.Z - a.Z;
        double den = rx * sz - rz * sx;
        if (Math.Abs(den) < 1e-12) return null;
        double t = ((a.X - p.X) * sz - (a.Z - p.Z) * sx) / den, u = ((a.X - p.X) * rz - (a.Z - p.Z) * rx) / den;
        if (0.0 <= t && t <= 1.0 && 0.0 <= u && u <= 1.0) return (t, u, (p.X + rx * t, p.Z + rz * t));
        return null;
    }

    static int SideOf(Road main, JObj b)
    {
        var pts = b.Arr("points");
        if (pts == null || pts.Count == 0) return 1;
        var p = (JObj)pts[0]!;
        var (_, lat) = main.Project(p.Num("x"), p.Num("z"));
        return lat >= 0 ? 1 : -1;
    }

    public static (double, double) BranchSpan(Road main, JObj b)
    {
        double s0 = b.Num("from_m", 0.0), s1 = b.ContainsKey("to_m") ? b.Num("to_m") : s0 + 400.0;
        if (main.Closed)
        {
            s0 = Py.Mod(s0, main.Length);
            while (s1 <= s0) s1 += main.Length;
            s1 = Math.Min(s1, s0 + main.Length - 2 * BranchLead);
        }
        else
        {
            s0 = Math.Max(0.0, Math.Min(s0, main.Length - MinBranchGap - 10));
            s1 = Math.Max(s0 + MinBranchGap, Math.Min(s1, main.Length - 5));
        }
        return (s0, s1);
    }

    public static Road BranchRoad(Road main, JObj b)
    {
        int side = SideOf(main, b);
        double w = b.ContainsKey("width") ? b.Num("width") : BranchWidth;
        var (s0, s1) = BranchSpan(main, b);
        double lead = Math.Min(BranchLead, (s1 - s0) / 4);
        RoadPoint OnMain(double s)
        {
            double wl = Math.Min(w, Math.Max(8.0, main.WAt(s) - 6.0));
            double lat = side * Math.Max(main.WAt(s) / 2 - wl / 2 - 2.0, 0.0);
            var (x, z, _) = main.At(s, lat);
            return new RoadPoint { X = x, Z = z, W = wl, H = main.HAt(s) };
        }
        var ctl = new List<RoadPoint> { OnMain(s0), OnMain(s0 + lead / 2), OnMain(s0 + lead) };
        foreach (var p in b.Objs("points"))
            ctl.Add(new RoadPoint
            {
                X = p.Num("x"), Z = p.Num("z"), W = p.Truthy("w") ? p.Num("w") : w, H = p.Truthy("h") ? p.Num("h") : 0.0,
                Corner = p.Truthy("corner") ? p.Num("corner") : 0.0,
            });
        ctl.Add(OnMain(s1 - lead)); ctl.Add(OnMain(s1 - lead / 2)); ctl.Add(OnMain(s1));
        var r = new Road(ctl, w, false) { Kind = "branch", Span = (s0, s1), OutSide = side };
        LevelEnds(main, r);
        return r;
    }

    static void LevelEnds(Road main, Road r)
    {
        int n = r.Count;
        var ma = new Area(main);
        int hold = (int)(HeightHold / Step), ease = Math.Max(1, (int)(HeightEase / Step));
        double MainH(int i)
        {
            var (x, z) = r.Centre[i];
            var (d, h) = ma.DepthH(x, z);
            if (d < -1e8) { var (s, _) = main.Project(x, z); return main.HAt(s); }
            return h;
        }
        bool NearM(int i) { var (x, z) = r.Centre[i]; return ma.Depth(x, z) > -(r.WidthAt[i] / 2 + 3.0); }
        for (int pass = 0; pass < 2; pass++)
        {
            int At(int k) => pass == 0 ? k : n - 1 - k;
            int lastNear = 0;
            for (int k = 0; k < n; k++)
            {
                if (NearM(At(k))) lastNear = k;
                else if (k - lastNear > hold + ease) break;
            }
            for (int k = 0; k < Math.Min(n, lastNear + hold + ease + 1); k++)
            {
                int i = At(k);
                if (k <= lastNear + hold) r.Height[i] = MainH(i);
                else
                {
                    double t = (double)(k - lastNear - hold) / ease;
                    t = t * t * (3 - 2 * t);
                    r.Height[i] = MainH(i) + (r.Height[i] - MainH(i)) * t;
                }
            }
        }
        var pinned = new List<(int, double)>();
        for (int i = 0; i < n; i++) if (NearM(i)) pinned.Add((i, r.Height[i]));
        var want = new List<double>(r.Height);
        double g = MaxGrade * Step;
        for (int it = 0; it < 4; it++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 0) for (int i = 1; i < n; i++) r.Height[i] = Math.Max(r.Height[i - 1] - g, Math.Min(r.Height[i - 1] + g, r.Height[i]));
                else for (int i = n - 2; i >= 0; i--) r.Height[i] = Math.Max(r.Height[i + 1] - g, Math.Min(r.Height[i + 1] + g, r.Height[i]));
                foreach (var (i, h) in pinned) r.Height[i] = h;
            }
        }
        r.Lowered = 0;
        for (int i = 0; i < n; i++) r.Lowered = Math.Max(r.Lowered, Math.Abs(want[i] - r.Height[i]));
    }

    public static (double, double) SplitSpan(Road main, JObj sp)
    {
        double s0 = sp.Num("from_m", 0.0), s1 = sp.ContainsKey("to_m") ? sp.Num("to_m") : s0 + 200.0;
        if (main.Closed)
        {
            s0 = Py.Mod(s0, main.Length);
            while (s1 <= s0) s1 += main.Length;
            s1 = Math.Min(s1, s0 + main.Length - 50);
        }
        else
        {
            s0 = Math.Max(0.0, Math.Min(s0, main.Length - SplitMin));
            s1 = Math.Max(s0 + SplitMin, Math.Min(s1, main.Length));
        }
        return (s0, s1);
    }

    public static int LaneCount(JObj sp) => sp.Int("lanes", 2) >= 3 ? 3 : 2;

    public static List<double> DividerLats(Road main, double s, int lanes)
    {
        double w = main.WAt(s);
        var o = new List<double>();
        for (int k = 1; k < lanes; k++) o.Add(w * ((double)k / lanes - 0.5));
        return o;
    }

    public static List<(double Lo, double Hi)> LaneRanges(Road main, double s, int lanes)
    {
        double w = main.WAt(s);
        var cuts = new List<double> { -w / 2 }; cuts.AddRange(DividerLats(main, s, lanes)); cuts.Add(w / 2);
        var o = new List<(double, double)>();
        for (int k = 0; k < lanes; k++)
            o.Add((cuts[k] + (k > 0 ? DividerHalf : 0.0), cuts[k + 1] - (k < lanes - 1 ? DividerHalf : 0.0)));
        return o;
    }

    public static List<Road> DividerRoads(Road main, JObj sp)
    {
        var (s0, s1) = SplitSpan(main, sp);
        int lanes = LaneCount(sp);
        var o = new List<Road>();
        for (int k = 0; k < lanes - 1; k++)
        {
            var ctl = new List<RoadPoint>();
            double s = s0;
            while (true)
            {
                double lat = DividerLats(main, s, lanes)[k];
                var (x, z, _) = main.At(s, lat);
                ctl.Add(new RoadPoint { X = x, Z = z, W = DividerW, H = main.HAt(s) });
                if (s >= s1) break;
                s = Math.Min(s + 8.0, s1);
            }
            var r = new Road(ctl, DividerW, false) { Kind = "divider", Span = (s0, s1), Taper = new() };
            for (int i = 0; i < r.Count; i++)
            {
                double t = Math.Min(1.0, Math.Min(r.Dist[i] / DividerNose, (r.Length - r.Dist[i]) / DividerNose));
                t = Math.Max(0.0, t);
                r.Taper.Add(t * t * (3 - 2 * t));
            }
            o.Add(r);
        }
        return o;
    }

    public Network(JObj m, Road? main = null)
    {
        M = m;
        Main = main ?? Road.ForMap(m);
        Main.Kind = "main";
        int k = 0;
        foreach (var b in m.Objs("branches"))
        {
            k++;
            try { Branches.Add((b, BranchRoad(Main, b))); }
            catch (Exception ex) { Problems.Add($"branch {k}: {ex.Message}"); }
        }
        k = 0;
        foreach (var sp in m.Objs("splits"))
        {
            k++;
            try { var ds = DividerRoads(Main, sp); Splits.Add((sp, ds)); Dividers.AddRange(ds); }
            catch (Exception ex) { Problems.Add($"split {k}: {ex.Message}"); }
        }
    }

    public IEnumerable<Road> Roads() { yield return Main; foreach (var (_, r) in Branches) yield return r; foreach (var d in Dividers) yield return d; }
    public IEnumerable<Road> Drivable() { yield return Main; foreach (var (_, r) in Branches) yield return r; }

    public Dictionary<Road, Area> Areas()
    {
        if (_areas == null) { _areas = new(); foreach (var r in Drivable()) _areas[r] = new Area(r); }
        return _areas;
    }

    (Dictionary<Road, Dictionary<int, List<(double, double)>>> Cuts, HashSet<Road> Done) Junctions()
    {
        if (_jn != null) return _jn.Value;
        var cuts = new Dictionary<Road, Dictionary<int, List<(double, double)>>>();
        var done = new HashSet<Road>();
        var main = Main; int n = main.Count;
        void Add(Road road, int side, double a, double b)
        {
            if (!cuts.TryGetValue(road, out var c)) cuts[road] = c = new();
            if (!c.TryGetValue(side, out var l)) c[side] = l = new();
            l.Add((a, b));
        }
        foreach (var (b, br) in Branches)
        {
            int o = br.OutSide;
            var Mw = main.Edge(o, 1.0); var Bo = br.Edge(o, 1.0); var Bi = br.Edge(-o, 1.0);
            int j0 = (int)(br.Span.Item1 / Step), j1 = (int)Math.Ceiling(br.Span.Item2 / Step);
            List<int> Win(int lo, int hi)
            {
                var w = new List<int>();
                for (int k = lo; k < hi; k++)
                    if (main.Closed) w.Add((int)Py.Mod(k, n));
                    else if (0 <= k && k < n - 1) w.Add(k);
                return w;
            }
            (double Sm, double Sb, (double, double) Pt)? Cross(List<(double X, double Z)> B2, IEnumerable<int> order, List<int> win)
            {
                foreach (int k in order)
                {
                    var p = B2[k]; var q = B2[k + 1];
                    foreach (int mk in win)
                    {
                        int mk2 = main.Closed ? (mk + 1) % n : mk + 1;
                        var hit = SegX(p, q, Mw[mk], Mw[mk2]);
                        if (hit != null)
                        {
                            var (t, u, pt) = hit.Value;
                            double sm = main.Dist[mk] + u * (mk2 != 0 ? main.Dist[mk2] - main.Dist[mk] : main.Length - main.Dist[mk]);
                            double sb = br.Dist[k] + t * (br.Dist[k + 1] - br.Dist[k]);
                            return (sm, sb, pt);
                        }
                    }
                }
                return null;
            }
            int nb = br.Count;
            var fwd = Enumerable.Range(0, nb - 1).ToList(); var back = Enumerable.Range(0, nb - 1).Reverse().ToList();
            var p1 = Cross(Bo, fwd, Win(j0 - 30, j0 + 60));
            var p2 = Cross(Bi, fwd, Win(j0 - 30, j0 + 60));
            var p4 = Cross(Bo, back, Win(j1 - 60, j1 + 30));
            var p3 = Cross(Bi, back, Win(j1 - 60, j1 + 30));
            if (p1 == null || p2 == null || p3 == null || p4 == null || !(p1.Value.Sb < p3.Value.Sb && p2.Value.Sb < p4.Value.Sb)) continue;
            done.Add(br);
            Tips.Add((p2.Value.Pt, br, "fork")); Tips.Add((p3.Value.Pt, br, "merge"));
            Add(main, o, p1.Value.Sm, p2.Value.Sm);
            Add(main, o, p3.Value.Sm, p4.Value.Sm);
            Add(br, o, -1.0, p1.Value.Sb);
            Add(br, o, p4.Value.Sb, br.Length + 1.0);
            Add(br, -o, -1.0, p2.Value.Sb);
            Add(br, -o, p3.Value.Sb, br.Length + 1.0);
        }
        _jn = (cuts, done);
        return _jn.Value;
    }

    IEnumerable<(double, double)> CutsOf(Road road, int side)
    {
        var (cuts, _) = Junctions();
        if (cuts.TryGetValue(road, out var c) && c.TryGetValue(side, out var l)) return l;
        return Array.Empty<(double, double)>();
    }

    public Dictionary<int, List<double>> CutBounds(Road road)
    {
        var o = new Dictionary<int, List<double>>();
        int si = 0;
        foreach (int side in new[] { -1, 1 })
        {
            var l = new List<double>();
            foreach (var (a, b) in CutsOf(road, side))
                foreach (double e in new[] { a, b }) l.Add(road.Closed ? Py.Mod(e, road.Length) : e);
            l.Sort();
            o[si++] = l;
        }
        return o;
    }

    public bool InCut(Road road, int side, double sq)
    {
        foreach (var (a, b) in CutsOf(road, side))
        {
            if (road.Closed && b < a) { if (sq >= a || sq <= b) return true; }
            else if (a < sq && sq < b) return true;
        }
        return false;
    }

    public List<List<double[]>> WallRuns(Road road, int side, bool heights = false)
    {
        var edge0 = road.Edge(side, 1.0);
        var edge = new List<double[]>(edge0.Count);
        for (int i = 0; i < edge0.Count; i++)
            edge.Add(heights ? new[] { edge0[i].X, edge0[i].Z, road.Surf(i, side * (road.WidthAt[i] / 2 + 1)) } : new[] { edge0[i].X, edge0[i].Z });
        var mask = WallMask(road)[side < 0 ? 0 : 1];
        int n = road.Count;
        var (cuts, done) = Junctions();
        bool exact = done.Contains(road) || (road == Main && cuts.TryGetValue(road, out var mc) && mc.Count > 0);
        var idx = Enumerable.Range(0, n).ToList();
        if (road.Closed) idx.Add(0);
        var runs = new List<List<double[]>>(); var cur = new List<double[]>();
        for (int k = 0; k < idx.Count; k++)
        {
            int i = idx[k];
            if (mask[i])
            {
                if (cur.Count > 0)
                {
                    if (exact && k > 0) cur.Add(CutPoint(road, side, idx[k - 1], i, edge));
                    runs.Add(cur);
                }
                cur = new();
            }
            else
            {
                if (cur.Count == 0 && exact && k > 0 && mask[idx[k - 1]]) cur.Add(CutPoint(road, side, i, idx[k - 1], edge));
                cur.Add(edge[i]);
            }
        }
        if (cur.Count > 0) runs.Add(cur);
        return runs.Where(r => r.Count >= 2).ToList();
    }

    double[] CutPoint(Road road, int side, int iIn, int iOut, List<double[]> edge)
    {
        double si = road.Dist[iIn], so = road.Dist[iOut];
        if (road.Closed && Math.Abs(si - so) > road.Length / 2) so = so < si ? so + road.Length : so - road.Length;
        double? best = null;
        var shifts = road.Closed ? new[] { 0.0, road.Length, -road.Length } : new[] { 0.0 };
        foreach (var (a, b) in CutsOf(road, side))
            foreach (double e in new[] { a, b })
                foreach (double sh in shifts)
                {
                    double ee = e + sh;
                    if (Math.Min(si, so) - 1e-6 <= ee && ee <= Math.Max(si, so) + 1e-6) best = ee;
                }
        if (best == null || so == si) return edge[iIn];
        double t = (best.Value - si) / (so - si);
        var pa = edge[iIn]; var pb = edge[iOut];
        var o = new double[pa.Length];
        for (int k = 0; k < pa.Length; k++) o[k] = pa[k] + (pb[k] - pa[k]) * t;
        return o;
    }

    public bool[][] WallMask(Road road)
    {
        if (_masks.TryGetValue(road, out var m0)) return m0;
        int n = road.Count;
        var mask = new[] { new bool[n], new bool[n] };
        if (road.Kind != "divider" && Branches.Count > 0)
        {
            var (_, done) = Junctions();
            int si = 0;
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < n; i++) mask[si][i] = InCut(road, side, road.Dist[i]);
                si++;
            }
            var areas = Areas();
            var others = Drivable().Where(r => r != road && !done.Contains(r) && !(road.Kind == "branch" && done.Contains(road) && r == Main)).ToList();
            if (road.Kind == "main") others = Drivable().Where(r => r != road && !done.Contains(r)).ToList();
            si = 0;
            foreach (int side in new[] { -1, 1 })
            {
                var edge = road.Edge(side, 1.0);
                for (int i = 0; i < n; i++)
                {
                    if (mask[si][i]) continue;
                    var (x, z) = edge[i];
                    foreach (var o in others)
                    {
                        var (d, h) = areas[o].DepthH(x, z);
                        double need = road.Kind == "branch" && o.Kind == "main" ? -2.5 : 1.5;
                        if (d > need && Math.Abs(h - road.Height[i]) < 3.0) { mask[si][i] = true; break; }
                    }
                }
                si++;
            }
        }
        return _masks[road] = mask;
    }

    public Func<double, double, double>? Sink(Road road)
    {
        if (road.Kind != "branch") return null;
        var ma = Areas()[Main];
        return (x, z) => ma.Depth(x, z) > 0.3 ? -SinkH : 0.0;
    }

    public Func<int, double, double, (double, double)>? Clip(Road road)
    {
        if (road.Kind != "branch") return null;
        var ma = Areas()[Main];
        var outer = road.Edge(road.OutSide);
        return (i, x, z) =>
        {
            if (ma.Depth(x, z) <= -0.3) return (x, z);
            var (ox, oz) = outer[i];
            double dx = ox - x, dz = oz - z, ln = Py.Hypot(dx, dz);
            if (ln < 0.5)
            {
                var (sx, sz) = road.Side(i);
                dx = sx * road.OutSide; dz = sz * road.OutSide; ln = 1.0;
            }
            dx /= ln; dz /= ln;
            double b = ln;
            while (ma.Depth(x + dx * b, z + dz * b) > 0.0 && b < 200.0) b += 4.0;
            double a = 0.0;
            for (int k = 0; k < 18; k++)
            {
                double t = (a + b) / 2;
                if (ma.Depth(x + dx * t, z + dz * t) > 0.0) a = t; else b = t;
            }
            return (x + dx * b, z + dz * b);
        };
    }

    public double ClearOfSplits(double s, double margin = 12.0)
    {
        double L = Main.Length;
        foreach (var (sp, _) in Splits)
        {
            var (s0, s1) = SplitSpan(Main, sp);
            foreach (double sh in Main.Closed ? new[] { 0.0, L, -L } : new[] { 0.0 })
            {
                double t = s + sh;
                if (s0 - margin < t && t < s1 + margin) return (t - s0 < s1 - t ? s0 - margin : s1 + margin) - sh;
            }
        }
        return s;
    }

    public bool NearBranch(double x, double z, double margin = 0.0)
    {
        if (Branches.Count == 0) return false;
        var areas = Areas();
        return Branches.Any(b => areas[b.R].Depth(x, z) > -margin);
    }

    public (int Lanes, double W) SplitWeight(double s)
    {
        double L = Main.Length;
        foreach (var (sp, _) in Splits)
        {
            var (s0, s1) = SplitSpan(Main, sp);
            foreach (double shift in Main.Closed ? new[] { 0.0, L, -L } : new[] { 0.0 })
            {
                double t = s + shift;
                if (s0 <= t && t <= s1) return (LaneCount(sp), 1.0);
                if (s0 - SplitEase < t && t < s0) return (LaneCount(sp), (t - (s0 - SplitEase)) / SplitEase);
                if (s1 < t && t < s1 + 20) return (LaneCount(sp), 1.0 - (t - s1) / 20);
            }
        }
        return (0, 0.0);
    }

    public (double, double) MainLane(int i, double lo, double hi)
    {
        var (lanes, w) = SplitWeight(Main.Dist[i]);
        if (lanes == 0) return (lo, hi);
        var (a, b) = LaneRanges(Main, Main.Dist[i], lanes)[0];
        return (lo + (a - lo) * w, hi + (b - hi) * w);
    }

    public List<AltRoute> AltRoutes(double y, IList<(double Lo, double Hi)>? corridor = null, double margin = 2.5)
    {
        var main = Main; int n = main.Count;
        var o = new List<AltRoute>();
        double[] MainPt(int j)
        {
            int i = (int)Py.Mod(j, n);
            double half = main.WidthAt[i] / 2 - margin;
            var (lo, hi) = corridor != null ? corridor[i] : (-half, half);
            var (cx, cz) = main.Centre[i]; var (sx, sz) = main.Side(i); var (fx, fz) = main.Forward(i);
            return new[] { cx + sx * lo, y + main.Height[i], cz + sz * lo, Math.Max(hi - lo, 4.0), fx, fz };
        }
        foreach (var (sp, _) in Splits)
        {
            var (s0, s1) = SplitSpan(main, sp);
            int lanes = LaneCount(sp);
            int i0 = (int)Math.Floor((s0 - SplitEase) / Step), i1 = (int)Math.Ceiling((s1 + 20) / Step);
            if (!main.Closed) { i0 = Math.Max(i0, 0); i1 = Math.Min(i1, n - 1); }
            for (int lane = 1; lane < lanes; lane++)
            {
                var a = new AltRoute();
                for (int j = i0 - AltOnMain; j <= i1; j++)
                {
                    if (!main.Closed && !(0 <= j && j < n)) continue;
                    int i = (int)Py.Mod(j, n);
                    double s = main.Dist[i], half = main.WidthAt[i] / 2 - margin;
                    var (_, w) = SplitWeight(s);
                    if (j < i0) w = 0.0;
                    var (la, lb) = LaneRanges(main, s, lanes)[lane];
                    var (baseLo, baseHi) = corridor != null ? corridor[i] : (-half, half);
                    double lo = baseLo + (la - baseLo) * w, hi = baseHi + (lb - baseHi) * w;
                    var (cx, cz) = main.Centre[i]; var (sx, sz) = main.Side(i); var (fx, fz) = main.Forward(i);
                    a.Points.Add(new[] { cx + sx * lo, y + main.Height[i], cz + sz * lo, Math.Max(hi - lo, 4.0), fx, fz });
                    a.Logic.Add(j);
                }
                o.Add(a);
            }
        }
        foreach (var (_, r) in Branches)
        {
            var (s0, s1) = r.Span;
            double j0 = s0 / Step, j1 = s1 / Step;
            int nb = r.Count;
            var a = new AltRoute();
            int f0 = (int)Py.Round(j0);
            for (int j = f0 - AltOnMain; j < f0; j++)
                if (main.Closed || (0 <= j && j < n)) { a.Points.Add(MainPt(j)); a.Logic.Add(j); }
            for (int i = 0; i < nb; i++)
            {
                double half = r.WidthAt[i] / 2 - margin;
                var (cx, cz) = r.Centre[i]; var (sx, sz) = r.Side(i); var (fx, fz) = r.Forward(i);
                a.Points.Add(new[] { cx - sx * half, y + r.Height[i], cz - sz * half, 2 * half, fx, fz });
                a.Logic.Add((int)Py.Round(j0 + (j1 - j0) * i / Math.Max(nb - 1, 1)));
            }
            int t0 = (int)Py.Round(j1);
            for (int j = t0 + 1; j < t0 + 1 + AltOnMain / 2; j++)
                if (main.Closed || (0 <= j && j < n)) { a.Points.Add(MainPt(j)); a.Logic.Add(j); }
            o.Add(a);
        }
        for (int k = 0; k < o.Count; k++) { o[k].Name = $"Alt{k + 1}"; o[k].Junction = $"Junction{k + 1}"; }
        return o;
    }

    public Road PickupRoad(JObj it)
    {
        if (it["bi"] != null) { long bi = it.Int("bi"); if (0 <= bi && bi < Branches.Count) return Branches[(int)bi].R; }
        return Main;
    }

    public List<JObj> AutoRows(double s0, double s1, double step, int maxRows)
    {
        var extra = new List<JObj>();
        for (int bi = 0; bi < Branches.Count; bi++)
        {
            var r = Branches[bi].R;
            double usable = r.Length - 2 * BranchLead;
            int k = Math.Max(1, (int)Math.Floor(usable / step));
            for (int j = 0; j < k; j++)
            {
                var (x, z, _) = r.At(BranchLead + usable * (j + 0.5) / k);
                extra.Add(new JObj { ["kind"] = "row", ["x"] = Py.RoundN(x, 1), ["z"] = Py.RoundN(z, 1), ["bi"] = (long)bi });
            }
        }
        if (extra.Count > Math.Max(0, maxRows - 4)) extra = extra.GetRange(0, Math.Max(0, maxRows - 4));
        double span = s1 - s0;
        int rows = Math.Max(0, Math.Min((int)Math.Floor(span / step), maxRows - extra.Count));
        double first = step / 2, room = span - step;
        var o = new List<JObj>();
        for (int k = 0; k < rows; k++)
        {
            var (x, z, _) = Main.At(ClearOfSplits(s0 + first + room * k / Math.Max(rows - 1, 1)));
            o.Add(new JObj { ["kind"] = "row", ["x"] = Py.RoundN(x, 1), ["z"] = Py.RoundN(z, 1) });
        }
        o.AddRange(extra);
        return o;
    }

    public List<(double Lo, double Hi)> LogicBand(IList<AltRoute>? alts, double margin = 12.0, int spread = 10)
    {
        var main = Main; int n = main.Count;
        var lo = new double[n]; var hi = new double[n];
        for (int i = 0; i < n; i++) { lo[i] = -main.WidthAt[i] / 2; hi[i] = main.WidthAt[i] / 2; }
        if (Branches.Count == 0) return Enumerable.Range(0, n).Select(i => (lo[i], hi[i])).ToList();
        var needLo = (double[])lo.Clone(); var needHi = (double[])hi.Clone();
        foreach (var (_, r) in Branches)
        {
            var (s0, s1) = r.Span;
            double j0 = s0 / Step, j1 = s1 / Step;
            int nb = r.Count;
            for (int k = 0; k < nb; k += 2)
            {
                int guess = (int)Py.Round(j0 + (j1 - j0) * k / Math.Max(nb - 1, 1));
                foreach (int side in new[] { -1, 1 })
                {
                    var (ex, ez) = r.Edge(side, 2.0)[k];
                    double bestA = 0, bestLat = 0; int bestI = -1;
                    for (int jj = guess - 60; jj <= guess + 60; jj++)
                    {
                        if (!main.Closed && !(0 <= jj && jj < n)) continue;
                        int i = (int)Py.Mod(jj, n);
                        var (cx, cz) = main.Centre[i]; var (fx, fz) = main.Forward(i);
                        double along = (ex - cx) * fx + (ez - cz) * fz;
                        if (bestI < 0 || Math.Abs(along) < Math.Abs(bestA))
                        {
                            var (sx, sz) = main.Side(i);
                            bestA = along; bestI = i; bestLat = (ex - cx) * sx + (ez - cz) * sz;
                        }
                    }
                    if (bestI < 0 || Math.Abs(bestA) > 3 * Step) continue;
                    needLo[bestI] = Math.Min(needLo[bestI], bestLat - margin);
                    needHi[bestI] = Math.Max(needHi[bestI], bestLat + margin);
                }
            }
        }
        var o = new List<(double, double)>(n);
        for (int i = 0; i < n; i++)
        {
            double a = lo[i], b = hi[i];
            for (int d = -spread; d <= spread; d++)
            {
                int j = i + d;
                if (main.Closed) j = (int)Py.Mod(j, n);
                else if (!(0 <= j && j < n)) continue;
                a = Math.Min(a, needLo[j]); b = Math.Max(b, needHi[j]);
            }
            o.Add((a, b));
        }
        return o;
    }

    public List<(int Bi, double Len, double MainLen, double Ratio)> Balance()
    {
        var o = new List<(int, double, double, double)>();
        for (int bi = 0; bi < Branches.Count; bi++)
        {
            var r = Branches[bi].R; var (s0, s1) = r.Span;
            double ml = Math.Max(s1 - s0, 1.0);
            o.Add((bi, r.Length, ml, r.Length / ml));
        }
        return o;
    }

    public List<(int, int)> BusyRanges()
    {
        var o = new List<(int, int)>();
        foreach (var (_, r) in Branches) o.Add(((int)(r.Span.Item1 / Step) - 5, (int)Math.Ceiling(r.Span.Item2 / Step) + 5));
        foreach (var (sp, _) in Splits)
        {
            var (s0, s1) = SplitSpan(Main, sp);
            o.Add(((int)((s0 - SplitEase) / Step), (int)Math.Ceiling((s1 + 20) / Step)));
        }
        return o;
    }

    static readonly Dictionary<Road, Network> Cache = new();

    public static Network Get(JObj m, Road main)
    {
        string sig = Json.Dumps(m["branches"], -1) + Json.Dumps(m["splits"], -1);
        lock (Cache)
        {
            if (Cache.TryGetValue(main, out var c) && c.M == m && c.Sig == sig) return c;
            var net = new Network(m, main) { Sig = sig };
            if (Cache.Count > 16) Cache.Clear();
            Cache[main] = net;
            return net;
        }
    }
}

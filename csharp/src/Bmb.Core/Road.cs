// A road shape: a smooth curve through control points, sampled every STEP m. Port of flat_earth/road.py.
namespace Bmb.Core;

public sealed class RoadPoint { public double X, Z, W, H, Corner, CornerUsed, Tilt; public RoadPoint Copy() => (RoadPoint)MemberwiseClone(); }

public sealed class Road
{
    public const double Step = 4.0, BridgeHeight = 9.0, BridgeGrade = 0.07, MinCorner = 25.0, MaxTilt = 20.0, TiltEdgeGrade = 0.06;

    public double Width, Length;
    public List<RoadPoint> Points;
    public bool Closed;
    public List<(double X, double Z)> Centre = new();
    public List<double> WidthAt = new(), ManualH = new(), Heading = new(), Dist = new(), Height = new();
    // the tilt set on the points (degrees, + = the left side up), how much of it stays where the road must be flat, the slope across (metres up per metre to the left)
    public List<double> ManualTilt = new(), TiltKept = new(), Tan = new();
    public bool Tilted;
    List<double>? _plainH;
    public List<(int J, int I, double Flat)> Crossings = new();
    readonly Dictionary<(int, double), List<(double X, double Z)>> _edges = new();
    // set by the network
    public string Kind = ""; public (double, double) Span; public int OutSide; public double Lowered; public List<double>? Taper;

    public int Count => Centre.Count;

    public static RoadPoint NormPoint(object? p, double width)
    {
        if (p is JObj d)
        {
            return new RoadPoint
            {
                X = Json.D(d["x"]), Z = Json.D(d["z"]), W = Json.Truthy(d["w"]) ? Json.D(d["w"]) : width,
                H = Json.Truthy(d["h"]) ? Json.D(d["h"]) : 0.0, Corner = Json.Truthy(d["corner"]) ? Json.D(d["corner"]) : 0.0, Tilt = Json.Truthy(d["tilt"]) ? Json.D(d["tilt"]) : 0.0,
            };
        }
        var a = (List<object?>)p!;
        return new RoadPoint { X = Json.D(a[0]), Z = Json.D(a[1]), W = width };
    }

    public static Road ForMap(JObj m)
    {
        var pts = m.Arr("points")!;
        double w = m.Num("width", 60.0);
        var rp = new List<RoadPoint>(pts.Count);
        foreach (var p in pts) rp.Add(NormPoint(p, w));
        var r = new Road(rp, w, !m.ContainsKey("auto_bridges") || Json.Truthy(m["auto_bridges"]), m.Truthy("laps"));
        r.ApplyTilt(m);
        return r;
    }

    /// the tilt of the road's points. It stays flat at the start, the finish, branches, lane splits and bridges: those parts join level with something.
    /// The low side never goes under the ground: the road rises as far as that needs (the floor of the game's world is flat).
    void ApplyTilt(JObj m)
    {
        if (ManualTilt.All(t => t == 0)) return;
        var keep = new List<(double A, double B)>();
        double clear = Math.Max(Network.AltOnMain * Step, Network.SplitEase) + 20.0;
        foreach (var b in m.Objs("branches")) { var (a, e) = Network.BranchSpan(this, b); keep.Add((a - clear, e + clear)); }
        foreach (var sp in m.Objs("splits")) { var (a, e) = Network.SplitSpan(this, sp); keep.Add((a - clear, e + clear)); }
        var (si, fi) = GenAid.RaceLines(this, m);
        foreach (int i in Closed ? new[] { si } : new[] { si, fi }) keep.Add((Dist[i] - 160.0, Dist[i] + 110.0));
        foreach (var (j, i, flat) in Crossings) foreach (int c in new[] { j, i }) keep.Add((Dist[c] - flat, Dist[c] + flat));
        double Near(double s)
        {
            double best = 1e9;
            foreach (var (a, b) in keep)
                foreach (double sh in Closed ? new[] { 0.0, Length, -Length } : new[] { 0.0 })
                    best = Math.Min(best, s + sh < a ? a - s - sh : s + sh > b ? s + sh - b : 0.0);
            return best;
        }
        // a car feels every quick change of the slope: the edges climb gently (the limit), without corners (the smoothing), and the road's rise never follows
        // the tilt down through zero where it swaps sides. All that spreads along the road, so the flat parts keep this much clear space around them
        const int Reach = 7;
        const double Grown = 4 * Reach * Step;
        int n = Count;
        int At(int i) => Closed ? (int)Py.Mod(i, n) : Math.Max(0, Math.Min(n - 1, i));
        double[] Box(double[] a) { var o = new double[n]; for (int i = 0; i < n; i++) { double sum = 0; for (int k = -Reach; k <= Reach; k++) sum += a[At(i + k)]; o[i] = sum / (2 * Reach + 1); } return o; }
        var want = new double[n]; var tan = new double[n]; var pinned = new bool[n];
        for (int i = 0; i < n; i++)
        {
            want[i] = Math.Tan(Math.Max(-MaxTilt, Math.Min(MaxTilt, ManualTilt[i])) * Math.PI / 180);
            pinned[i] = Near(Dist[i]) < Grown;
            tan[i] = pinned[i] ? 0.0 : want[i];
        }
        for (int pass = 0; pass < 6; pass++)
        {
            for (int k = 1; k < n + (Closed ? 1 : 0); k++) { int i = At(k), p = At(k - 1); if (!pinned[i]) { double r = TiltEdgeGrade * Step / WidthAt[i]; tan[i] = Math.Max(tan[p] - r, Math.Min(tan[p] + r, tan[i])); } }
            for (int k = n - 2 + (Closed ? 1 : 0); k >= 0; k--) { int i = At(k), p = At(k + 1); if (!pinned[i]) { double r = TiltEdgeGrade * Step / WidthAt[i]; tan[i] = Math.Max(tan[p] - r, Math.Min(tan[p] + r, tan[i])); } }
        }
        tan = Box(Box(tan));
        var low = new double[n];                                   // how high the middle must be for the low side to stay above the ground
        for (int i = 0; i < n; i++) low[i] = (WidthAt[i] / 2 + 1) * Math.Abs(tan[i]);
        var top = new double[n];
        for (int i = 0; i < n; i++) for (int k = -Reach; k <= Reach; k++) top[i] = Math.Max(top[i], low[At(i + k)]);
        var rise = Box(top);
        _plainH = new List<double>(Height);
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(tan[i]) < 1e-6) tan[i] = 0.0;
            if (rise[i] < 1e-6) rise[i] = 0.0;
            Tan.Add(tan[i]); TiltKept.Add(want[i] == 0 ? 1.0 : Math.Min(1.0, Math.Abs(tan[i] / want[i])));
            Tilted |= tan[i] != 0;
            Height[i] = Math.Max(Height[i], rise[i]);
        }
    }

    static void Catmull((double, double) p0, (double, double) p1, (double, double) p2, (double, double) p3, int n, List<(double, double)> o)
    {
        static double Tj(double ti, (double, double) a, (double, double) b) =>
            ti + Math.Pow(Math.Max(Py.Hypot(b.Item1 - a.Item1, b.Item2 - a.Item2), 1e-6), 0.5);
        double t0 = 0.0, t1 = Tj(t0, p0, p1), t2 = Tj(t1, p1, p2), t3 = Tj(t2, p2, p3);
        for (int k = 0; k < n; k++)
        {
            double t = t1 + (t2 - t1) * k / n;
            double w = (t - t0) / (t1 - t0);
            double a1x = p0.Item1 + (p1.Item1 - p0.Item1) * w, a1z = p0.Item2 + (p1.Item2 - p0.Item2) * w;
            w = (t - t1) / (t2 - t1);
            double a2x = p1.Item1 + (p2.Item1 - p1.Item1) * w, a2z = p1.Item2 + (p2.Item2 - p1.Item2) * w;
            w = (t - t2) / (t3 - t2);
            double a3x = p2.Item1 + (p3.Item1 - p2.Item1) * w, a3z = p2.Item2 + (p3.Item2 - p2.Item2) * w;
            double b1x = a1x + (a2x - a1x) * (t - t0) / (t2 - t0), b1z = a1z + (a2z - a1z) * (t - t0) / (t2 - t0);
            double b2x = a2x + (a3x - a2x) * (t - t1) / (t3 - t1), b2z = a2z + (a3z - a2z) * (t - t1) / (t3 - t1);
            o.Add((b1x + (b2x - b1x) * (t - t1) / (t2 - t1), b1z + (b2z - b1z) * (t - t1) / (t2 - t1)));
        }
    }

    static List<(double X, double Z, double W, double H, double T, bool Arc)> WithCorners(List<RoadPoint> pts)
    {
        var o = new List<(double, double, double, double, double, bool)>();
        for (int k = 0; k < pts.Count; k++)
        {
            var p = pts[k];
            double r = p.Corner;
            if (0 < k && k < pts.Count - 1 && r > 0)
            {
                var a = pts[k - 1]; var b = pts[k + 1];
                double ux = p.X - a.X, uz = p.Z - a.Z, vx = b.X - p.X, vz = b.Z - p.Z;
                double lu = Py.Hypot(ux, uz), lv = Py.Hypot(vx, vz);
                if (lu > 1e-6 && lv > 1e-6)
                {
                    ux /= lu; uz /= lu; vx /= lv; vz /= lv;
                    double turn = Math.Atan2(ux * vz - uz * vx, ux * vx + uz * vz);
                    if (Math.Abs(turn) > 1e-3)
                    {
                        r = Math.Max(r, Math.Max(MinCorner, p.W / 2 + 10));
                        double d = Math.Min(r * Math.Tan(Math.Abs(turn) / 2), Math.Min(0.45 * lu, 0.45 * lv));
                        r = d / Math.Tan(Math.Abs(turn) / 2);
                        p.CornerUsed = r;
                        double t1x = p.X - ux * d, t1z = p.Z - uz * d;
                        int sgn = turn > 0 ? 1 : -1;
                        double nx = -uz * sgn, nz = ux * sgn;
                        double cx = t1x + nx * r, cz = t1z + nz * r;
                        double a0 = Math.Atan2(t1x - cx, t1z - cz);
                        int steps = Math.Max(2, (int)(Math.Abs(turn) * r / 2));
                        for (int i = 0; i <= steps; i++)
                        {
                            double ang = a0 - turn * i / steps;
                            o.Add((cx + r * Math.Sin(ang), cz + r * Math.Cos(ang), p.W, p.H, p.Tilt, true));
                        }
                        continue;
                    }
                }
            }
            o.Add((p.X, p.Z, p.W, p.H, p.Tilt, false));
        }
        return o;
    }

    public static List<(double X, double Z)> UnloopPublic(List<(double X, double Z)> pts, List<double> lat, List<(double X, double Z)> centre) => Unloop(pts, lat, centre);

    static List<(double X, double Z)> Unloop(List<(double X, double Z)> pts, List<double> lat, List<(double X, double Z)> centre, double cell = 30.0)
    {
        var grid = new Dictionary<(int, int), List<int>>();
        for (int k = 0; k < centre.Count; k++)
        {
            var key = ((int)Math.Floor(centre[k].X / cell), (int)Math.Floor(centre[k].Z / cell));
            if (!grid.TryGetValue(key, out var l)) grid[key] = l = new();
            l.Add(k);
        }
        int n = pts.Count;
        var good = new bool[n];
        bool any = false, all = true;
        for (int i = 0; i < n; i++)
        {
            var (x, z) = pts[i];
            double need = Math.Abs(lat[i]) - 1.0;
            int r = (int)Math.Floor(need / cell) + 1;
            int gx = (int)Math.Floor(x / cell), gz = (int)Math.Floor(z / cell);
            bool ok = true;
            for (int ax = gx - r; ax <= gx + r && ok; ax++)
                for (int az = gz - r; az <= gz + r && ok; az++)
                    if (grid.TryGetValue((ax, az), out var l))
                        foreach (int k in l)
                        {
                            double dx = centre[k].X - x, dz = centre[k].Z - z;
                            if (dx * dx + dz * dz < need * need) { ok = false; break; }
                        }
            good[i] = ok; any |= ok; all &= ok;
        }
        if (all || !any) return new List<(double, double)>(pts);
        var o = new List<(double X, double Z)>(pts);
        int ii = 0;
        while (ii < n)
        {
            if (good[ii]) { ii++; continue; }
            int j = ii;
            while (j < n && !good[j]) j++;
            int a = ii - 1, b = j;
            for (int m = ii; m < j; m++)
            {
                if (a < 0) o[m] = pts[b];
                else if (b >= n) o[m] = pts[a];
                else
                {
                    double t = (double)(m - a) / (b - a);
                    o[m] = (pts[a].X + (pts[b].X - pts[a].X) * t, pts[a].Z + (pts[b].Z - pts[a].Z) * t);
                }
            }
            ii = j;
        }
        return o;
    }

    public static double Smooth(double v0, double v1, double t) { t = t * t * (3 - 2 * t); return v0 + (v1 - v0) * t; }

    public Road(List<RoadPoint> pts, double width = 60.0, bool autoBridges = true, bool closed = false)
    {
        if (pts.Count < (closed ? 3 : 2)) throw new InvalidOperationException($"a road needs at least {(closed ? 3 : 2)} points");
        Width = width; Points = pts; Closed = closed;
        var used = pts;
        if (closed)
        {
            var first = pts[0].Copy(); first.Corner = 0;
            used = new List<RoadPoint> { first };
            used.AddRange(pts.Skip(1));
            used.Add(first.Copy());
        }
        var ctl = WithCorners(used);
        var P = ctl.Select(c => (c.X, c.Z)).ToList();
        var ext = new List<(double X, double Z)>(P.Count + 2);
        if (closed) { ext.Add(P[^2]); ext.AddRange(P); ext.Add(P[1]); }
        else
        {
            ext.Add((2 * P[0].X - P[1].X, 2 * P[0].Z - P[1].Z)); ext.AddRange(P);
            ext.Add((2 * P[^1].X - P[^2].X, 2 * P[^1].Z - P[^2].Z));
        }
        var dense = new List<(double X, double Z, double W, double H, double T)>();
        var xy = new List<(double, double)>();
        for (int i = 0; i < ctl.Count - 1; i++)
        {
            double seg = Py.Hypot(P[i + 1].X - P[i].X, P[i + 1].Z - P[i].Z);
            int n0 = Math.Max((int)seg, 2);
            xy.Clear();
            if (ctl[i].Arc && ctl[i + 1].Arc)
                for (int k = 0; k < n0; k++) xy.Add((P[i].X + (P[i + 1].X - P[i].X) * k / n0, P[i].Z + (P[i + 1].Z - P[i].Z) * k / n0));
            else
            {
                var p0 = ext[i]; var p3 = ext[i + 3];
                if (ctl[i].Arc && i > 0)
                {
                    double dx = P[i].X - P[i - 1].X, dz = P[i].Z - P[i - 1].Z;
                    double k = seg / Math.Max(Py.Hypot(dx, dz), 1e-9);
                    p0 = (P[i].X - dx * k, P[i].Z - dz * k);
                }
                if (ctl[i + 1].Arc && i + 2 < P.Count)
                {
                    double dx = P[i + 2].X - P[i + 1].X, dz = P[i + 2].Z - P[i + 1].Z;
                    double k = seg / Math.Max(Py.Hypot(dx, dz), 1e-9);
                    p3 = (P[i + 1].X + dx * k, P[i + 1].Z + dz * k);
                }
                Catmull(p0, ext[i + 1], ext[i + 2], p3, n0, xy);
            }
            for (int k = 0; k < xy.Count; k++)
            {
                double t = (double)k / n0;
                dense.Add((xy[k].Item1, xy[k].Item2, Smooth(ctl[i].W, ctl[i + 1].W, t), Smooth(ctl[i].H, ctl[i + 1].H, t), Smooth(ctl[i].T, ctl[i + 1].T, t)));
            }
        }
        dense.Add((P[^1].X, P[^1].Z, ctl[^1].W, ctl[^1].H, ctl[^1].T));
        var acc = new double[dense.Count];
        for (int i = 1; i < dense.Count; i++) acc[i] = acc[i - 1] + Py.Hypot(dense[i].X - dense[i - 1].X, dense[i].Z - dense[i - 1].Z);
        Length = acc[^1];
        int n = (int)Math.Floor(Length / Step) + 1, j = 0;
        for (int i = 0; i < n; i++)
        {
            double s = i * Step;
            while (j < acc.Length - 2 && acc[j + 1] < s) j++;
            double w = (s - acc[j]) / Math.Max(acc[j + 1] - acc[j], 1e-9);
            var a = dense[j]; var b = dense[j + 1];
            Centre.Add((a.X + (b.X - a.X) * w, a.Z + (b.Z - a.Z) * w));
            WidthAt.Add(a.W + (b.W - a.W) * w);
            ManualH.Add(a.H + (b.H - a.H) * w);
            ManualTilt.Add(a.T + (b.T - a.T) * w);
        }
        if (Closed && n > 2 && Py.Hypot(Centre[^1].X - Centre[0].X, Centre[^1].Z - Centre[0].Z) < Step * 0.5)
        {
            Centre.RemoveAt(n - 1); WidthAt.RemoveAt(n - 1); ManualH.RemoveAt(n - 1); ManualTilt.RemoveAt(n - 1);
            n--;
        }
        for (int i = 0; i < n; i++)
        {
            (double X, double Z) a, b;
            if (Closed) { a = Centre[(int)Py.Mod(i - 1, n)]; b = Centre[(i + 1) % n]; }
            else { a = Centre[Math.Max(i - 1, 0)]; b = Centre[Math.Min(i + 1, n - 1)]; }
            Heading.Add(Math.Atan2(b.X - a.X, b.Z - a.Z));
            Dist.Add(i * Step);
        }
        Crossings = FindCrossings();
        Height = new List<double>(ManualH);
        if (autoBridges) foreach (var (over, _, flat) in Crossings) Lift(over, flat);
    }

    List<(int, int, double)> FindCrossings()
    {
        int n = Count;
        var o = new List<(int J, int I, double Flat)>();
        const double cell = 60.0;
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < n; i += 2)
        {
            var key = ((int)Math.Floor(Centre[i].X / cell), (int)Math.Floor(Centre[i].Z / cell));
            if (!grid.TryGetValue(key, out var l)) grid[key] = l = new();
            l.Add(i);
        }
        for (int j = 0; j < n; j += 2)
        {
            var (x, z) = Centre[j];
            int cx = (int)Math.Floor(x / cell), cz = (int)Math.Floor(z / cell);
            double bestD = 0; int bestI = -1;
            for (int gx = cx - 1; gx <= cx + 1; gx++)
                for (int gz = cz - 1; gz <= cz + 1; gz++)
                {
                    if (!grid.TryGetValue((gx, gz), out var l)) continue;
                    foreach (int i in l)
                    {
                        int near = Math.Max(60, (int)((WidthAt[i] + WidthAt[j]) * 2 / Step));
                        if (j - i < near || (Closed && n - (j - i) < near)) continue;
                        double lim = (WidthAt[i] + WidthAt[j]) / 2 + 8;
                        double dx = x - Centre[i].X, dz = z - Centre[i].Z, dd = dx * dx + dz * dz;
                        if (dd < lim * lim && (bestI < 0 || dd < bestD)) { bestD = dd; bestI = i; }
                    }
                }
            if (bestI < 0) continue;
            int ib = bestI;
            if (o.Count > 0)
            {
                var (lj, li, _) = o[^1];
                double near = Py.Hypot(x - Centre[lj].X, z - Centre[lj].Z);
                if (near < 2.5 * Math.Max(WidthAt[ib], WidthAt[j]) && Math.Abs(li - ib) < 60) continue;
            }
            double cross = Math.Abs(Math.Sin(Heading[ib] - Heading[j]));
            double flat = (WidthAt[ib] / 2 + 14) / Math.Max(cross, 0.35);
            o.Add((j, ib, flat));
        }
        return o;
    }

    void Lift(int j, double flat)
    {
        double ramp = 1.5 * BridgeHeight / BridgeGrade, sj = Dist[j];
        for (int k = 0; k < Count; k++)
        {
            double d = Math.Abs(Dist[k] - sj), h;
            if (d <= flat) h = BridgeHeight;
            else if (d <= flat + ramp) h = Smooth(BridgeHeight, 0.0, (d - flat) / ramp);
            else continue;
            Height[k] = Math.Max(Height[k], h);
        }
    }

    public int IndexAt(double s)
    {
        if (Closed) return (int)Py.Mod(Py.Round(s / Step), Count);
        return Math.Max(0, Math.Min(Count - 1, (int)Py.Round(s / Step)));
    }

    (int I, int J, double W) Seg(double s)
    {
        int n = Count;
        if (Closed)
        {
            s = Py.Mod(s, Length);
            int i = Math.Min((int)Math.Floor(s / Step), n - 1);
            if (i == n - 1) return (i, 0, (s - i * Step) / Math.Max(Length - i * Step, 1e-9));
            return (i, i + 1, (s - i * Step) / Step);
        }
        s = Math.Max(0.0, Math.Min(Length, s));
        int ii = Math.Min((int)Math.Floor(s / Step), n - 2);
        return (ii, ii + 1, (s - ii * Step) / Step);
    }

    public (double X, double Z, double A) At(double s, double lateral = 0.0)
    {
        var (i, j, w) = Seg(s);
        var (x0, z0) = Centre[i]; var (x1, z1) = Centre[j];
        double x = x0 + (x1 - x0) * w, z = z0 + (z1 - z0) * w;
        double a0 = Heading[i], a1 = Heading[j];
        double da = Py.Mod(a1 - a0 + Math.PI, 2 * Math.PI) - Math.PI;
        double a = a0 + da * w;
        double sx = -Math.Cos(a), sz = Math.Sin(a);
        return (x + sx * lateral, z + sz * lateral, a);
    }

    public double HAt(double s) { var (i, j, w) = Seg(s); return Height[i] + (Height[j] - Height[i]) * w; }
    /// the road's surface `lat` metres to the left of its middle
    public double Surf(int i, double lat) => Tilted ? Height[i] + lat * Tan[i] : Height[i];
    public double HAt(double s, double lat) { if (!Tilted) return HAt(s); var (i, j, w) = Seg(s); double a = Surf(i, lat); return a + (Surf(j, lat) - a) * w; }
    public double TanOf(int i) => Tilted ? Tan[i] : 0.0;
    public double TiltDeg(double s) { if (!Tilted) return 0.0; var (i, j, w) = Seg(s); return Math.Atan(Tan[i] + (Tan[j] - Tan[i]) * w) * 180 / Math.PI; }
    /// the height without what the tilt added
    public double PlainH(int i) => _plainH != null ? _plainH[i] : Height[i];
    public double WAt(double s) { var (i, j, w) = Seg(s); return WidthAt[i] + (WidthAt[j] - WidthAt[i]) * w; }
    public double PitchAt(double s, double span = 8.0) => Math.Atan2(HAt(s + span / 2) - HAt(s - span / 2), span);
    public (double X, double Z) Forward(int i) { double a = Heading[i]; return (Math.Sin(a), Math.Cos(a)); }
    public (double X, double Z) Side(int i) { var (fx, fz) = Forward(i); return (-fz, fx); }

    public List<(double X, double Z)> Edge(int side, double extra = 0.0)
    {
        lock (_edges)
        {
            if (_edges.TryGetValue((side, extra), out var e)) return e;
            var raw = new List<(double, double)>(Count); var lats = new List<double>(Count);
            for (int i = 0; i < Count; i++)
            {
                var (cx, cz) = Centre[i]; var (sx, sz) = Side(i);
                double lat = side * (WidthAt[i] / 2 + extra);
                raw.Add((cx + sx * lat, cz + sz * lat));
                lats.Add(lat);
            }
            return _edges[(side, extra)] = Unloop(raw, lats, Centre);
        }
    }

    public bool OffRoad(double x, double z, double lat)
    {
        double need = Math.Abs(lat) - 2.0, c2 = need * need;
        for (int i = 0; i < Centre.Count; i += 2)
        {
            double dx = Centre[i].X - x, dz = Centre[i].Z - z;
            if (dx * dx + dz * dz < c2) return false;
        }
        return true;
    }

    public double MinRadius()
    {
        double best = 1e9;
        for (int i = 1; i < Count - 1; i++)
        {
            double da = Py.Mod(Heading[i + 1] - Heading[i - 1] + Math.PI, 2 * Math.PI) - Math.PI;
            if (Math.Abs(da) > 1e-6) best = Math.Min(best, 2 * Step / Math.Abs(da));
        }
        return best;
    }

    public double MaxGrade()
    {
        double best = 0;
        for (int i = 0; i < Count - 1; i++)
        {
            best = Math.Max(best, Math.Abs(Height[i + 1] - Height[i]) / Step);
            if (Tilted) foreach (int sd in new[] { -1, 1 }) best = Math.Max(best, Math.Abs(Surf(i + 1, sd * WidthAt[i + 1] / 2) - Surf(i, sd * WidthAt[i] / 2)) / Step);
        }
        return best;
    }

    public (double X0, double X1, double Z0, double Z1) Bounds(double margin = 0.0)
    {
        double x0 = double.MaxValue, x1 = double.MinValue, z0 = x0, z1 = x1;
        foreach (var (x, z) in Centre) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
        return (x0 - margin, x1 + margin, z0 - margin, z1 + margin);
    }

    public bool NearRoad(double x, double z, double clearance)
    {
        double c2 = clearance * clearance;
        for (int i = 0; i < Centre.Count; i += 2)
        {
            double dx = Centre[i].X - x, dz = Centre[i].Z - z;
            if (dx * dx + dz * dz < c2) return true;
        }
        return false;
    }

    public (double S, double Lat) Project(double x, double z)
    {
        int bi = 0; double bd = double.MaxValue;
        for (int k = 0; k < Count; k++)
        {
            double dx = Centre[k].X - x, dz = Centre[k].Z - z, d = dx * dx + dz * dz;
            if (d < bd) { bd = d; bi = k; }
        }
        var (sx, sz) = Side(bi);
        return (Dist[bi], (x - Centre[bi].X) * sx + (z - Centre[bi].Z) * sz);
    }

    public List<(int J, int I)> UnbridgedCrossings() =>
        Crossings.Where(c => Math.Abs(Height[c.J] - Height[c.I]) < BridgeHeight - 2).Select(c => (c.J, c.I)).ToList();
}

// Seamless road: the road + raised edges as a few long models. Port of flat_earth/road_mesh.py.
namespace Bmb.Core;

public sealed class Chunk { public double[] Anchor = new double[3]; public List<double[]> Verts = new(); public List<int> Strip = new(); public int First, Last; public string Key = ""; }
public sealed class NetChunks { public Road? R; public double Width; public string Kind = ""; public List<Chunk> Cs = new(); }

public static class RoadMesh
{
    public const string Source = "\\models\\objects\\shared\\us\\rh_telegraphpoles.model", Diff = "telegraphpoles_diff", Mat = "telegraphpoles_ipr0";
    public const double ChunkLen = 420.0, URepeat = 14.0, EdgeRampLen = 15.0, DividerLift = 0.08, EdgeJoin = 0.15;
    public const uint BaseUid = 0xF1A70000;
    const int EdgeChunk = 300;
    static readonly double EdgeRound = 25.0 * Math.PI / 180;

    public static (uint Model, uint Def) Uids(JObj m, int k)
    {
        uint b = BaseUid | (((uint)(m.ContainsKey("route_uid") ? m.Int("route_uid") : 0xFFFFFF21) & 0xFF) << 8);
        if (k >= 64) throw new InvalidOperationException("road too long for its uid block");
        return (b + 2 * (uint)k, b + 2 * (uint)k + 1);
    }

    public static string ModelFile(JObj m, int k) => $"models\\objects\\flat_earth\\road_{m.Int("route_uid") & 0xFF:x2}_{k:00}.model";

    static (double, double, double) Sub((double, double, double) a, (double, double, double) b) => (a.Item1 - b.Item1, a.Item2 - b.Item2, a.Item3 - b.Item3);
    static (double, double, double) Cross((double, double, double) a, (double, double, double) b) =>
        (a.Item2 * b.Item3 - a.Item3 * b.Item2, a.Item3 * b.Item1 - a.Item1 * b.Item3, a.Item1 * b.Item2 - a.Item2 * b.Item1);
    static (double, double, double) Unit((double, double, double) a)
    {
        double ln = Math.Sqrt(a.Item1 * a.Item1 + a.Item2 * a.Item2 + a.Item3 * a.Item3);
        return ln > 1e-9 ? (a.Item1 / ln, a.Item2 / ln, a.Item3 / ln) : (0.0, 1.0, 0.0);
    }

    static (double, double, double)[][] Rings(Road road, bool[][]? mask, Func<double, double, double>? sink, Func<int, double, double, (double, double)>? clip, double[][]? ramp)
    {
        var bank = Fe.Bank.ToArray();
        List<double>? taper = null;
        if (road.Kind == "divider")
        {
            double top = bank[^1].H;
            bank = bank.Select(b => (b.O, top - b.H)).ToArray();
            taper = road.Taper;
        }
        var order = new List<(int Side, double O, double H)>();
        for (int i = bank.Length - 1; i >= 0; i--) order.Add((-1, bank[i].O, bank[i].H));
        foreach (var (o, h) in bank) order.Add((1, o, h));
        var rings = new (double, double, double)[order.Count][];
        // each profile line on its own core (the Python loop is the slow part of a road)
        foreach (var (side, o0, _) in order) { road.Edge(side, o0 > 0 ? o0 : 0.0); road.Edge(side); }
        Parallel.For(0, order.Count, pi =>
        {
            var (side, o, h) = order[pi];
            var e = road.Edge(side, o > 0 ? o : 0.0); var flat = road.Edge(side);
            var cut = mask?[side < 0 ? 0 : 1];
            var ring = new (double, double, double)[road.Count];
            for (int i = 0; i < road.Count; i++)
            {
                double x, z, hh;
                if (cut != null && cut[i]) { x = flat[i].X; z = flat[i].Z; hh = 0.0; }
                else
                {
                    x = e[i].X; z = e[i].Z; hh = h;
                    if (ramp != null)
                    {
                        double f = ramp[side < 0 ? 0 : 1][i];
                        x = flat[i].X + (x - flat[i].X) * f; z = flat[i].Z + (z - flat[i].Z) * f; hh *= f;
                    }
                }
                if (taper != null)
                {
                    double t = taper[i]; var (cx, cz) = road.Centre[i];
                    x = cx + (x - cx) * t; z = cz + (z - cz) * t; hh *= Math.Min(1.0, 0.35 + 0.65 * t);
                }
                if (clip != null) (x, z) = clip(i, x, z);
                double y = road.Height[i] - Fe.TileTopBelow + hh + (taper != null ? DividerLift : 0.0);
                if (road.Tilted) { var (lx, lz) = road.Side(i); y += ((x - road.Centre[i].X) * lx + (z - road.Centre[i].Z) * lz) * road.Tan[i]; }
                if (sink != null) y += sink(x, z);
                ring[i] = (x, y, z);
            }
            rings[pi] = ring;
        });
        return rings;
    }

    struct Idx { public int I, Ia, Ib; public double T; public bool Cut; public static implicit operator Idx(int i) => new() { I = i }; }

    public static List<Chunk> Chunks(Road road, bool[][]? mask = null, Func<double, double, double>? sink = null, Func<int, double, double, (double, double)>? clip = null,
                                     Dictionary<int, List<double>>? bounds = null, double[][]? ramp = null)
    {
        var rings = Rings(road, mask, sink, clip, ramp);
        int P = rings.Length, n = road.Count, mid = P / 2 - 1;
        var vs = Fe.RoadProfile(road.Width).Select(v => v.V).ToArray();
        var cuts = new List<int> { 0 };
        while (cuts[^1] < n - 1)
        {
            int i = cuts[^1] + 1;
            while (i < n - 1 && road.Dist[i + 1] - road.Dist[cuts[^1]] <= ChunkLen) i++;
            cuts.Add(i);
        }
        var o = new List<Chunk>();
        for (int ci = 0; ci + 1 < cuts.Count; ci++)
        {
            int i0 = cuts[ci], i1 = cuts[ci + 1], im = (i0 + i1) / 2;
            var (ax, az) = road.Centre[im];
            double[] anchor = { ax, Fe.GroundY, az };
            double uc = URepeat * Py.Round(road.Dist[im] / URepeat);
            var idx = new List<Idx>(); var dists = new List<double>();
            for (int i = i0; i <= i1; i++) { idx.Add(i); dists.Add(road.Dist[i]); }
            if (road.Closed && i1 == n - 1) { idx.Add(0); dists.Add(road.Length); }
            if (mask != null && bounds != null) (idx, dists) = WithCuts(road, idx, dists, mask, bounds);
            bool cap = road.Kind == "divider";
            if (cap && i0 == 0) { idx.Insert(0, -1); dists.Insert(0, road.Dist[0]); }
            if (cap && i1 == n - 1) { idx.Add(-2); dists.Add(road.Dist[n - 1]); }
            var verts = new List<double[]>(P * idx.Count);
            for (int p = 0; p < P; p++)
                for (int q = 0; q < idx.Count; q++)
                {
                    var i = idx[q]; double di = dists[q];
                    if (!i.Cut && i.I < 0)
                    {
                        int e = i.I == -1 ? 0 : n - 1;
                        var (fx, fz) = road.Forward(e);
                        int sgn = i.I == -1 ? -1 : 1;
                        var (cx, cz) = road.Centre[e];
                        verts.Add(new[] { cx - anchor[0], road.Height[e] - Fe.TileTopBelow, cz - anchor[2], fx * sgn, 0.0, fz * sgn, (di - uc) / URepeat, vs[p] });
                        continue;
                    }
                    if (i.Cut)
                    {
                        (double, double, double) At(int qq)
                        {
                            var (x0, y0, z0) = rings[qq][i.Ia]; var (x1, y1, z1) = rings[qq][i.Ib];
                            return (x0 + (x1 - x0) * i.T, y0 + (y1 - y0) * i.T, z0 + (z1 - z0) * i.T);
                        }
                        var (x, y, z) = At(p);
                        var a = Sub(At(Math.Min(p + 1, P - 1)), At(Math.Max(p - 1, 0)));
                        var lng = Sub(rings[mid][i.Ib], rings[mid][i.Ia]);
                        var nrm = Unit(Cross(lng, a));
                        var up = Cross(lng, Sub(At(mid + 1), At(mid)));
                        if (up.Item2 < 0) nrm = (-nrm.Item1, -nrm.Item2, -nrm.Item3);
                        verts.Add(new[] { x - anchor[0], y, z - anchor[2], nrm.Item1, nrm.Item2, nrm.Item3, (di - uc) / URepeat, vs[p] });
                        continue;
                    }
                    {
                        int ii = i.I;
                        var (x, y, z) = rings[p][ii];
                        var a = Sub(rings[Math.Min(p + 1, P - 1)][ii], rings[Math.Max(p - 1, 0)][ii]);
                        int ib, ia;
                        if (road.Closed) { ib = (int)Py.Mod(ii - 1, n); ia = (ii + 1) % n; }
                        else { ib = Math.Max(ii - 1, 0); ia = Math.Min(ii + 1, n - 1); }
                        var lng = Sub(rings[mid][ia], rings[mid][ib]);
                        var nrm = Unit(Cross(lng, a));
                        var up = Cross(lng, Sub(rings[mid + 1][ii], rings[mid][ii]));
                        if (up.Item2 < 0) nrm = (-nrm.Item1, -nrm.Item2, -nrm.Item3);
                        verts.Add(new[] { x - anchor[0], y, z - anchor[2], nrm.Item1, nrm.Item2, nrm.Item3, (di - uc) / URepeat, vs[p] });
                    }
                }
            int N = idx.Count;
            int V(int p, int j) => p * N + j;
            (double, double, double) Pos(int k) => (verts[k][0], verts[k][1], verts[k][2]);
            double vote = 0.0;
            for (int j = 0; j < N - 1; j++)
                foreach (var (a, b, c) in new[] { (V(mid, j), V(mid + 1, j), V(mid, j + 1)), (V(mid + 1, j), V(mid + 1, j + 1), V(mid, j + 1)) })
                    vote += Cross(Sub(Pos(b), Pos(a)), Sub(Pos(c), Pos(a))).Item2;
            bool flip = vote < 0;
            o.Add(new Chunk { Anchor = anchor, Verts = verts, Strip = MakeStrip(P, N, flip), First = i0, Last = i1 });
        }
        return o;
    }

    static List<int> MakeStrip(int P, int N, bool flip)
    {
        var strip = new List<int>(2 * P * N);
        for (int p = 0; p < P - 1; p++)
        {
            int first = flip ? (p + 1) * N : p * N;
            if (strip.Count > 0) { strip.Add(strip[^1]); strip.Add(first); }
            for (int j = 0; j < N; j++)
            {
                int a = p * N + j, b = (p + 1) * N + j;
                if (flip) { strip.Add(b); strip.Add(a); } else { strip.Add(a); strip.Add(b); }
            }
        }
        return strip;
    }

    static (List<Idx>, List<double>) WithCuts(Road road, List<Idx> idx, List<double> dists, bool[][] mask, Dictionary<int, List<double>> bounds, double gap = 0.03)
    {
        var oi = new List<Idx>(); var od = new List<double>();
        for (int k = 0; k < idx.Count; k++)
        {
            int i = idx[k].I; double d = dists[k];
            oi.Add(idx[k]); od.Add(d);
            if (k + 1 == idx.Count) break;
            int j = idx[k + 1].I; double dj = dists[k + 1], seg = dj - d;
            if (seg <= 1e-6) continue;
            var marks = new List<(double F, int S)>();
            for (int si = 0; si < 2; si++)
            {
                if (mask[si][i] == mask[si][j]) continue;
                foreach (double b in bounds.GetValueOrDefault(si) ?? new())
                    foreach (double bb in road.Closed ? new[] { b, b + road.Length, b - road.Length } : new[] { b })
                        if (d < bb && bb < dj) marks.Add(((bb - d) / seg, si));
            }
            if (marks.Count == 0) continue;
            double eps = Math.Min(gap / seg, 0.25);
            var pts = new SortedSet<double>();
            foreach (var (f, _) in marks) foreach (int s in new[] { -1, 1 }) pts.Add(Math.Min(Math.Max(f + s * eps, 1e-4), 1 - 1e-4));
            foreach (double tt in pts) { oi.Add(new Idx { Cut = true, Ia = i, Ib = j, T = tt }); od.Add(d + seg * tt); }
        }
        return (oi, od);
    }

    public static double[][]? EdgeRamp(Road road, bool[][] mask, Dictionary<int, List<double>> bounds, double length = EdgeRampLen)
    {
        if (bounds[0].Count == 0 && bounds[1].Count == 0) return null;
        var o = new double[2][];
        for (int si = 0; si < 2; si++)
        {
            var bs = bounds[si]; var row = new double[road.Count];
            for (int i = 0; i < road.Count; i++)
            {
                if (mask[si][i] || bs.Count == 0) { row[i] = mask[si][i] ? 0.0 : 1.0; continue; }
                double s = road.Dist[i], d = bs.Min(b => Math.Abs(s - b));
                if (road.Closed) d = Math.Min(d, bs.Min(b => road.Length - Math.Abs(s - b)));
                double u = Math.Min(1.0, d / length);
                row[i] = u * u * (3 - 2 * u);
            }
            o[si] = row;
        }
        return o;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Network, List<NetChunks>> ChunkCache = new();

    public static List<NetChunks> NetworkChunks(JObj m, Road main)
    {
        var net = Network.Get(m, main);
        lock (ChunkCache) { if (ChunkCache.TryGetValue(net, out var c)) return c; }
        var o = new List<NetChunks>();
        bool useEdges = net.Branches.Count > 0;
        foreach (var r in net.Roads())
        {
            List<Chunk> cs;
            if (useEdges && r.Kind != "divider")
            {
                var all = new[] { Enumerable.Repeat(true, r.Count).ToArray(), Enumerable.Repeat(true, r.Count).ToArray() };
                cs = Chunks(r, all, net.Sink(r), net.Clip(r));
            }
            else
            {
                var b = net.CutBounds(r);
                cs = Chunks(r, net.WallMask(r), net.Sink(r), net.Clip(r), b, EdgeRamp(r, net.WallMask(r), b));
            }
            o.Add(new NetChunks { R = r, Width = r.Width, Kind = r.Kind, Cs = cs });
        }
        if (useEdges)
        {
            var cs = EdgeChunks(net, main);
            if (cs.Count > 0) o.Add(new NetChunks { Width = main.Width, Kind = "edge", Cs = cs });
        }
        lock (ChunkCache) { ChunkCache.AddOrUpdate(net, o); }
        return o;
    }

    public static List<(List<(double X, double Z, double H)> Chain, bool Loop)> EdgeChains(Network net)
    {
        var runs = new List<List<(double X, double Z, double H)>>();
        var areas = net.Areas();
        foreach (var r in net.Drivable())
        {
            var area = areas[r];
            foreach (int side in new[] { -1, 1 })
                foreach (var run in net.WallRuns(r, side, true))
                {
                    var pts = new List<(double X, double Z, double H)>();
                    foreach (var p in run)
                    {
                        if (pts.Count == 0) { pts.Add((p[0], p[1], p[2])); continue; }
                        double dx = p[0] - pts[^1].X, dz = p[1] - pts[^1].Z;
                        if (dx * dx + dz * dz > 0.01) pts.Add((p[0], p[1], p[2]));
                    }
                    if (pts.Count < 2) continue;
                    int k = pts.Count / 2;
                    var a = pts[Math.Max(k - 1, 0)]; var b = pts[Math.Min(k + 1, pts.Count - 1)];
                    double ddx = b.X - a.X, ddz = b.Z - a.Z, ln = Py.Hypot(ddx, ddz);
                    if (ln == 0) ln = 1.0;
                    double lx = -ddz / ln, lz = ddx / ln;
                    var (mx, mz, mh) = pts[k];
                    if (area.Depth(mx + lx * 1.5, mz + lz * 1.5, mh) < area.Depth(mx - lx * 1.5, mz - lz * 1.5, mh)) pts.Reverse();
                    runs.Add(pts);
                }
        }
        static bool Close((double X, double Z, double H) a, (double X, double Z, double H) b) =>
            (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z) < EdgeJoin * EdgeJoin;
        var chains = new List<(List<(double, double, double)>, bool)>();
        var used = new bool[runs.Count];
        for (int i0 = 0; i0 < runs.Count; i0++)
        {
            if (used[i0]) continue;
            int i = i0;
            var seen = new HashSet<int> { i };
            while (true)
            {
                int prev = -1;
                for (int j = 0; j < runs.Count; j++) if (!used[j] && !seen.Contains(j) && Close(runs[j][^1], runs[i][0])) { prev = j; break; }
                if (prev < 0) break;
                i = prev; seen.Add(i);
            }
            var chain = new List<(double X, double Z, double H)>(runs[i]);
            used[i] = true;
            while (true)
            {
                int nxt = -1;
                for (int j = 0; j < runs.Count; j++) if (!used[j] && Close(runs[j][0], chain[^1])) { nxt = j; break; }
                if (nxt < 0) break;
                chain.AddRange(runs[nxt].Skip(1));
                used[nxt] = true;
            }
            bool loop = chain.Count > 3 && Close(chain[0], chain[^1]);
            if (loop) chain.RemoveAt(chain.Count - 1);
            chains.Add((chain, loop));
        }
        return chains;
    }

    readonly record struct St(double X, double Z, double H, double Nx, double Nz, double Along, int Vi, bool Concave);

    static List<St> EdgeStations(List<(double X, double Z, double H)> chain, bool loop)
    {
        int n = chain.Count;
        var o = new List<St>();
        double along = 0.0;
        static (double, double, double) D((double X, double Z, double H) a, (double X, double Z, double H) b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, ln = Py.Hypot(dx, dz);
            if (ln == 0) ln = 1.0;
            return (dx / ln, dz / ln, ln);
        }
        for (int i = 0; i < n; i++)
        {
            var p = chain[i];
            bool hasA = i > 0 || loop, hasB = i < n - 1 || loop;
            if (i > 0) along += D(chain[i - 1], p).Item3;
            var din = hasA ? D(chain[i > 0 ? i - 1 : n - 1], p) : default;
            var dout = hasB ? D(p, chain[(i + 1) % n]) : default;
            if (!hasA || !hasB)
            {
                var dd = hasA ? din : dout;
                o.Add(new St(p.X, p.Z, p.H, dd.Item2, -dd.Item1, along, i, false));
                continue;
            }
            (double X, double Z) nIn = (din.Item2, -din.Item1), nOut = (dout.Item2, -dout.Item1);
            double ang = Math.Atan2(din.Item1 * dout.Item2 - din.Item2 * dout.Item1, din.Item1 * dout.Item1 + din.Item2 * dout.Item2);
            if (Math.Abs(ang) <= EdgeRound)
            {
                double mx = nIn.X + nOut.X, mz = nIn.Z + nOut.Z, ml = Py.Hypot(mx, mz);
                if (ml == 0) ml = 1.0;
                double scale = Math.Min(1.0 / Math.Max(Math.Cos(ang / 2), 0.5), 2.0);
                o.Add(new St(p.X, p.Z, p.H, mx / ml * scale, mz / ml * scale, along, i, false));
            }
            else
            {
                int steps = (int)Math.Ceiling(Math.Abs(ang) / (15.0 * Math.PI / 180));
                double a0 = Math.Atan2(nIn.Z, nIn.X);
                for (int s = 0; s <= steps; s++)
                {
                    double q = a0 + ang * s / steps;
                    o.Add(new St(p.X, p.Z, p.H, Math.Cos(q), Math.Sin(q), along + s * 1e-3, i, ang < 0));
                }
            }
        }
        if (loop && o.Count > 0)
        {
            var f = o[0];
            o.Add(new St(f.X, f.Z, f.H, f.Nx, f.Nz, along + D(chain[^1], chain[0]).Item3, 0, f.Concave));
        }
        return o;
    }

    static List<Chunk> EdgeChunks(Network net, Road main)
    {
        var bank = Fe.Bank; int nb = bank.Length;
        var prof = Fe.RoadProfile(main.Width);
        var profile = new (double Lat, double H, double V)[nb];
        for (int j = 0; j < nb; j++) profile[j] = (bank[j].O - 1.0, bank[j].H, prof[nb - 1 - j].V);
        int P = nb;
        var o = new List<Chunk>();
        var chains = EdgeChains(net);
        double reach = profile[^1].Lat;
        var grid = new Dictionary<(int, int), List<((double X, double Z, double H) A, (double X, double Z, double H) B, int Ci, int I)>>();
        for (int ci = 0; ci < chains.Count; ci++)
        {
            var (chain, loop) = chains[ci]; int n = chain.Count;
            for (int i = 0; i < (loop ? n : n - 1); i++)
            {
                var a = chain[i]; var b = chain[(i + 1) % n];
                for (int gx = (int)Math.Floor(Math.Min(a.X, b.X) / 4); gx <= (int)Math.Floor(Math.Max(a.X, b.X) / 4); gx++)
                    for (int gz = (int)Math.Floor(Math.Min(a.Z, b.Z) / 4); gz <= (int)Math.Floor(Math.Max(a.Z, b.Z) / 4); gz++)
                    {
                        if (!grid.TryGetValue((gx, gz), out var l)) grid[(gx, gz)] = l = new();
                        l.Add((a, b, ci, i));
                    }
            }
        }
        double Room(int ci, int vi, int nPts, bool loop, double x, double z, double nx, double nz)
        {
            double best = 2 * reach + 1.0;
            int own0 = loop ? (int)Py.Mod(vi - 1, nPts) : vi - 1, own1 = vi;
            var seen = new HashSet<(int, int)>();
            for (int gx = (int)Math.Floor((x - 4) / 4); gx <= (int)Math.Floor((x + 4) / 4); gx++)
                for (int gz = (int)Math.Floor((z - 4) / 4); gz <= (int)Math.Floor((z + 4) / 4); gz++)
                {
                    if (!grid.TryGetValue((gx, gz), out var l)) continue;
                    foreach (var (a, b, cj, sj) in l)
                    {
                        if (seen.Contains((cj, sj)) || (cj == ci && (sj == own0 || sj == own1))) continue;
                        seen.Add((cj, sj));
                        double ex = b.X - a.X, ez = b.Z - a.Z, den = nx * ez - nz * ex;
                        if (Math.Abs(den) < 1e-9) continue;
                        double wx = a.X - x, wz = a.Z - z;
                        double tt = (wx * ez - wz * ex) / den, u = (wx * nz - wz * nx) / den;
                        if (tt > 0.05 && -1e-6 <= u && u <= 1 + 1e-6) best = Math.Min(best, tt);
                    }
                }
            return Math.Min(reach, best / 2);
        }
        for (int ci = 0; ci < chains.Count; ci++)
        {
            var (chain, loop) = chains[ci];
            var st = EdgeStations(chain, loop);
            if (st.Count < 2) continue;
            var lim = new double[st.Count];
            for (int i = 0; i < st.Count; i++)
            {
                var q = st[i];
                double ln = Py.Hypot(q.Nx, q.Nz);
                if (ln == 0) ln = 1.0;
                lim[i] = q.Concave ? 0.0 : Room(ci, q.Vi, chain.Count, loop, q.X, q.Z, q.Nx / ln, q.Nz / ln) / ln;
            }
            for (int s0 = 0; s0 < st.Count - 1; s0 += EdgeChunk)
            {
                var part = st.GetRange(s0, Math.Min(s0 + EdgeChunk + 1, st.Count) - s0);
                if (part.Count < 2) continue;
                double ax = part.Sum(q => q.X) / part.Count, az = part.Sum(q => q.Z) / part.Count;
                double uc = URepeat * Py.Round(part[part.Count / 2].Along / URepeat);
                int N = part.Count;
                var pos = new (double, double, double)[P, N];
                for (int p = 0; p < P; p++)
                    for (int j = 0; j < N; j++)
                    {
                        var q = part[j]; double m = Math.Min(profile[p].Lat, lim[s0 + j]);
                        pos[p, j] = (q.X + q.Nx * m, q.H - Fe.TileTopBelow + profile[p].H, q.Z + q.Nz * m);
                    }
                var verts = new List<double[]>(P * N);
                for (int p = 0; p < P; p++)
                    for (int j = 0; j < N; j++)
                    {
                        var (x, y, z) = pos[p, j];
                        var a = Sub(pos[Math.Min(p + 1, P - 1), j], pos[Math.Max(p - 1, 0), j]);
                        var lng = Sub(pos[0, Math.Min(j + 1, N - 1)], pos[0, Math.Max(j - 1, 0)]);
                        var nrm = Unit(Cross(lng, a));
                        double nx = part[j].Nx, nz = part[j].Nz;
                        if (nrm.Item2 - (nrm.Item1 * nx + nrm.Item3 * nz) < 0) nrm = (-nrm.Item1, -nrm.Item2, -nrm.Item3);
                        verts.Add(new[] { x - ax, y, z - az, nrm.Item1, nrm.Item2, nrm.Item3, (part[j].Along - uc) / URepeat, profile[p].V });
                    }
                double vote = 0.0;
                (double, double, double) Pos(int k) => (verts[k][0], verts[k][1], verts[k][2]);
                for (int p = 0; p < P - 1; p++)
                    for (int j = 0; j < N - 1; j++)
                    {
                        var A = Pos(p * N + j);
                        var cr = Cross(Sub(Pos((p + 1) * N + j), A), Sub(Pos(p * N + j + 1), A));
                        vote += cr.Item2 - (cr.Item1 * part[j].Nx + cr.Item3 * part[j].Nz);
                    }
                o.Add(new Chunk { Anchor = new[] { ax, Fe.GroundY, az }, Verts = verts, Strip = MakeStrip(P, N, vote < 0), First = 0, Last = N - 1 });
            }
        }
        return o;
    }

    public static List<Chunk> OverlayChunks(JObj m, Road main) => Surface.OverlayChunks(m, Network.Get(m, main));

    public static int ModelCount(JObj m, Road main) => NetworkChunks(m, main).Sum(c => c.Cs.Count) + OverlayChunks(m, main).Count;

    public static uint TextureSalt(JObj m, int ri)
    {
        uint b = (uint)m.Int("route_uid") & 0xFF;
        return ri == 0 ? 0x0F1A7F00 | b : 0x0F2B0000 | (b << 8) | ((uint)ri & 0xFF);
    }

    public static List<TileSpot> Spots(JObj m, Road road)
    {
        var o = new List<TileSpot>(); int k = 0;
        foreach (var nc in NetworkChunks(m, road))
            foreach (var c in nc.Cs)
                o.Add(new TileSpot { X = c.Anchor[0], Y = c.Anchor[1], Z = c.Anchor[2], Uid = Uids(m, k++).Def });
        foreach (var c in OverlayChunks(m, road))
            o.Add(new TileSpot { X = c.Anchor[0], Y = c.Anchor[1], Z = c.Anchor[2], Uid = Uids(m, k++).Def });
        return o;
    }
}

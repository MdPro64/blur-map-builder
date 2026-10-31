// The race layout (.aid) along a road. Port of flat_earth/gen_aid.py.
namespace Bmb.Core;

public static class GenAid
{
    const double Step = 4.0;
    public const int GatePoints = 24, GateLead = 2, StartDefault = 40 + GateLead, FinishDefaultFromEnd = 40 + GatePoints - GateLead, JunctionPoints = 16;
    const uint JunctionFlags = 0x40;
    const double StartMinM = 110.0, MinRaceM = 300.0;
    static readonly double FinishEndMinM = (GatePoints + 2) * Step;

    static Dictionary<string, byte[]>? _lanes;
    public static Dictionary<string, byte[]> Lanes()
    {
        if (_lanes != null) return _lanes;
        var d = File.ReadAllBytes(Path.Combine(Paths.Unpacked, "levels", "jpn_mharuna", "jpn_mharuna_1.aid"));
        var rows = new Dictionary<string, byte[]>();
        foreach (var s in Aid.Splines(d))
        {
            int first = s.First + (s.Name == "Start" ? 4 : 0);
            rows[s.Name] = d.AsSpan(first + 44, Aid.Point - 44).ToArray();
        }
        return _lanes = rows;
    }

    static (double, double, double) Heights(string kind) => kind == "Main" ? (7.8, -2.9, 7.8) : (0.0, 0.0, 0.0);

    public static byte[] MakePoint(double x, double y, double z, double width, double fx, double fz, byte[] lane, string kind = "gate", double tan = 0.0)
    {
        double sx = -fz, sz = fx;
        var (h, h1, h2) = Heights(kind);
        var o = new byte[44 + lane.Length];
        // the banked width direction: across the road towards the higher `lat`, rising by `tan` per metre
        double c = 1 / Math.Sqrt(1 + tan * tan), up = tan == 0 ? 0.0 : tan * c;
        Span<float> v = stackalloc float[] { (float)x, (float)(y + h), (float)-z, (float)width, (float)sx, (float)-sz, (float)(sx * c), (float)up, (float)(-sz * c), (float)(y + h1), (float)(y + h2) };
        for (int i = 0; i < 11; i++) B.Put(o, 4 * i, v[i]);
        lane.CopyTo(o, 44);
        return o;
    }

    static void LaneSteps(List<byte[]> pts)
    {
        double[]? prevX = null, prevZ = null;
        foreach (var b in pts)
        {
            double x = B.F32(b, 0), z = B.F32(b, 8), w = B.F32(b, 12), sx = B.F32(b, 16), sz = B.F32(b, 20);
            int n = (int)B.U32(b, 44);
            var cx = new double[n]; var cz = new double[n];
            for (int k = 0; k < n; k++)
            {
                int at = 48 + 25 * k;
                double f = B.F32(b, at + 16);
                double lx = x + sx * w * f, lz = z + sz * w * f;
                double step = prevX == null ? 0.0 : Py.Hypot(lx - prevX[k], lz - prevZ![k]);
                B.Put(b, at + 4, (float)step);
                cx[k] = lx; cz[k] = lz;
            }
            prevX = cx; prevZ = cz;
        }
    }

    static byte[] Record(string name, uint flags, List<byte[]> points, string frm = "", string to = "")
    {
        var cp = points.Select(p => (byte[])p.Clone()).ToList();
        LaneSteps(cp);
        var o = new ByteBuf(cp.Count * Aid.Point + 64);
        foreach (var t in new[] { name, frm, to }) o.AddU32((uint)t.Length).Add(B.Bytes(t));
        o.AddU32(flags).AddU32((uint)cp.Count);
        foreach (var p in cp) o.Add(p);
        return o.ToArray();
    }

    static (List<byte[]>, List<byte[]>) AltRecords(IList<AltRoute>? alts, Dictionary<string, byte[]> lanes)
    {
        var ra = new List<byte[]>(); var rj = new List<byte[]>();
        foreach (var a in alts ?? Array.Empty<AltRoute>())
        {
            var P = a.Points.Select(p => MakePoint(p[0], p[1], p[2], p[3], p[4], p[5], lanes["Main"], "Main")).ToList();
            ra.Add(Record(a.Name, 0, P));
            rj.Add(Record(a.Junction, JunctionFlags, P.GetRange(0, Math.Min(JunctionPoints, P.Count)), "Main", a.Name));
        }
        return (ra, rj);
    }

    static void AltLinks(List<List<(uint, string)>> lists, IList<AltRoute>? alts, int n, bool closed)
    {
        foreach (var a in alts ?? Array.Empty<AltRoute>())
            for (int k = 0; k < a.Logic.Count; k++)
            {
                int p = a.Logic[k];
                foreach (int q0 in new[] { p - 1, p, p + 1 })
                {
                    int q = q0;
                    if (closed) q = (int)Py.Mod(q, n);
                    else if (!(0 <= q && q < lists.Count)) continue;
                    var targets = closed && q == 0 && lists.Count > n ? new[] { q, n } : new[] { q };
                    foreach (int t in targets)
                    {
                        lists[t].Add(((uint)k, a.Name));
                        if (k < JunctionPoints) lists[t].Add(((uint)k, a.Junction));
                    }
                }
            }
    }

    static (double X, double Z, double W, double Fx, double Fz, double Lat)[] BandPoints(Road road, IList<(double Lo, double Hi)>? ranges = null)
    {
        int n = road.Count;
        var L = new List<(double X, double Z)>(n); var R = new List<(double X, double Z)>(n); var la = new List<double>(n); var ra = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            var (lo, hi) = ranges != null ? ranges[i] : (-road.WidthAt[i] / 2, road.WidthAt[i] / 2);
            var (cx, cz) = road.Centre[i]; var (sx, sz) = road.Side(i);
            L.Add((cx + sx * lo, cz + sz * lo)); R.Add((cx + sx * hi, cz + sz * hi));
            la.Add(lo); ra.Add(hi);
        }
        L = Road.UnloopPublic(L, la, road.Centre); R = Road.UnloopPublic(R, ra, road.Centre);
        var o = new (double, double, double, double, double, double)[n];
        for (int i = 0; i < n; i++)
        {
            double dx = R[i].X - L[i].X, dz = R[i].Z - L[i].Z, w = Py.Hypot(dx, dz), sx, sz;
            if (w < 1.0) { (sx, sz) = road.Side(i); w = Math.Max(w, 1.0); }
            else { sx = dx / w; sz = dz / w; }
            o[i] = (L[i].X, L[i].Z, w, sz, -sx, la[i]);
        }
        return o;
    }

    static int ClearGate(int bas, IList<(int, int)>? avoid, int n, bool closed, int lo = 0, int? hi0 = null)
    {
        int hi = hi0 ?? n - GatePoints - 1;
        for (int t = 0; t < 12; t++)
        {
            int? hit = null;
            foreach (var (a, b) in avoid ?? Array.Empty<(int, int)>())
            {
                for (int k = bas; k < bas + GatePoints; k++)
                    if ((closed && Py.Mod(k - a, n) <= b - a) || (!closed && a <= k && k <= b)) { hit = b; break; }
                if (hit != null) break;
            }
            if (hit == null) return bas;
            bas = hit.Value + 2;
            if (closed) bas = (int)Py.Mod(bas, n);
            else if (bas > hi) return Math.Min(bas, hi);
        }
        return bas;
    }

    public static (int Si, int Fi) RaceLines(Road road, JObj? m)
    {
        int n = road.Count;
        if (road.Closed)
        {
            int s = m != null && m["start_m"] != null ? (int)Py.Mod(Py.Round(m.Num("start_m") / Step), n) : StartDefault % n;
            return (s, s);
        }
        int lo = (int)Math.Ceiling(StartMinM / Step), hi = n - 1 - (int)Math.Ceiling(FinishEndMinM / Step), gap = (int)(MinRaceM / Step);
        int si = m != null && m["start_m"] != null ? (int)Py.Round(m.Num("start_m") / Step) : StartDefault;
        int fi = m != null && m["finish_m"] != null ? (int)Py.Round(m.Num("finish_m") / Step) : n - FinishDefaultFromEnd;
        si = Math.Max(Math.Min(si, hi - gap), Math.Min(lo, StartDefault));
        fi = Math.Min(Math.Max(fi, si + gap), hi);
        if (fi <= si) { si = StartDefault; fi = n - FinishDefaultFromEnd; }
        return (si, fi);
    }

    static byte[] Finish(List<byte[]> recs, int count, List<List<(uint, string)>> lists)
    {
        var o = new ByteBuf(recs.Sum(r => r.Length) + 4096);
        o.AddF32(6.0f).AddU32((uint)recs.Count);
        foreach (var r in recs) o.Add(r);
        o.AddU32((uint)count).Add(Aid.WriteLinks(lists));
        return o.ToArray();
    }

    public static (byte[] Data, AidInfo Info) BuildPath(Road road, double y, IList<(double Lo, double Hi)>? corridor, (int Si, int Fi) lines, IList<AltRoute>? alts, IList<(int, int)>? avoid, IList<(double Lo, double Hi)>? band)
    {
        double[] splits = { 1.0 / 3, 2.0 / 3 };
        if (road.Closed) return BuildLoop(road, y, splits, corridor, lines, alts, avoid, band);
        int n = road.Count;
        int startAt = lines.Si - GateLead, finishFromEnd = n - GatePoints - (lines.Fi - GateLead);
        var lanes = Lanes();
        var H = road.Height;
        var roadBand = BandPoints(road); var aiBand = BandPoints(road, corridor); var logicBand = BandPoints(road, band);
        List<byte[]> Pts(string kind, int first, int count)
        {
            var o = new List<byte[]>(count);
            for (int k = 0; k < count; k++)
            {
                var (x, z, w, fx, fz, lat) = roadBand[first + k];
                o.Add(MakePoint(x, y + road.Surf(first + k, lat), z, w, fx, fz, lanes[kind], kind, road.TanOf(first + k)));
            }
            return o;
        }
        var splitAt = splits.Select(f => (int)(lines.Si + (lines.Fi - lines.Si) * f) - GateLead).ToArray();
        int fin = n - finishFromEnd - GatePoints;
        for (int i = 0; i < 2; i++) splitAt[i] = ClearGate(splitAt[i], avoid, n, false, startAt + GatePoints + 1, fin - GatePoints - 1);
        var gates = new (string Name, uint Flags, int Base)[] { ("Start", 2, startAt), ("InterOne", 8, splitAt[0]), ("InterTwo", 16, splitAt[1]), ("Finish", 4, fin) };
        var (ra, rj) = AltRecords(alts, lanes);
        var recs = new List<byte[]>(ra);
        var ai = new List<byte[]>(n);
        for (int i = 0; i < n; i++) { var (x, z, w, fx, fz, lat) = aiBand[i]; ai.Add(MakePoint(x, y + road.Surf(i, lat), z, w, fx, fz, lanes["Main"], "Main", road.TanOf(i))); }
        recs.Add(Record("Main", 0, ai));
        for (int g = 0; g < 3; g++) recs.Add(Record(gates[g].Name, gates[g].Flags, Pts(gates[g].Name, gates[g].Base, GatePoints)));
        var logic = new List<byte[]>(n);
        for (int i = 0; i < n; i++) { var (x, z, w, fx, fz, lat) = logicBand[i]; logic.Add(MakePoint(x, y + road.Surf(i, lat), z, w, fx, fz, lanes["Logic"], "Logic", road.TanOf(i))); }
        recs.Add(Record("Logic", 32, logic));
        recs.AddRange(rj);
        recs.Add(Record("Finish", gates[3].Flags, Pts("Finish", gates[3].Base, GatePoints)));
        var lists = new List<List<(uint, string)>>(n);
        for (int p = 0; p < n; p++)
        {
            var cur = new List<(uint, string)>();
            foreach (int k in new[] { p - 1, p, p + 1 }) if (0 <= k && k < n) cur.Add(((uint)k, "Main"));
            foreach (var (name, _, bas) in gates)
                for (int k = 0; k < GatePoints; k++) if (Math.Abs(p - (bas + k)) <= 1) cur.Add(((uint)k, name));
            lists.Add(cur);
        }
        AltLinks(lists, alts, n, false);
        int si = gates[0].Base + GateLead, fi = gates[3].Base + GateLead;
        var info = new AidInfo
        {
            Points = n, StartIndex = si, FinishIndex = fi, StartLine = new[] { road.Centre[si].X, y + H[si], road.Centre[si].Z }, StartAngle = road.Heading[si],
            FinishLine = new[] { road.Centre[fi].X, y + H[fi], road.Centre[fi].Z }, FinishAngle = road.Heading[fi],
        };
        foreach (var g in gates) info.Gates[g.Name] = g.Base;
        return (Finish(recs, n, lists), info);
    }

    static (byte[], AidInfo) BuildLoop(Road road, double y, double[] splits, IList<(double Lo, double Hi)>? corridor, (int Si, int Fi) lines, IList<AltRoute>? alts, IList<(int, int)>? avoid, IList<(double Lo, double Hi)>? band)
    {
        var lanes = Lanes();
        int n = road.Count;
        var H = road.Height;
        int si = (int)Py.Mod(lines.Si, n);
        var roadBand = BandPoints(road); var aiBand = BandPoints(road, corridor); var logicBand = BandPoints(road, band);
        byte[] Pt(string kind, (double X, double Z, double W, double Fx, double Fz, double Lat)[] b, int i)
        {
            i = (int)Py.Mod(i, n);
            var (x, z, w, fx, fz, lat) = b[i];
            return MakePoint(x, y + road.Surf(i, lat), z, w, fx, fz, lanes[kind], kind, road.TanOf(i));
        }
        var gates = new (string Name, uint Flags, int Base)[]
        {
            ("Start", 2, si - GateLead),
            ("InterOne", 8, ClearGate((int)Py.Mod(si + (int)(n * splits[0]) - GateLead, n), avoid, n, true)),
            ("InterTwo", 16, ClearGate((int)Py.Mod(si + (int)(n * splits[1]) - GateLead, n), avoid, n, true)),
        };
        var (ra, rj) = AltRecords(alts, lanes);
        var recs = new List<byte[]>(ra);
        recs.Add(Record("Main", 1, Enumerable.Range(0, n + 1).Select(i => Pt("Main", aiBand, i)).ToList()));
        foreach (var (name, flags, bas) in gates)
            recs.Add(Record(name, flags, Enumerable.Range(0, GatePoints).Select(k => Pt(name, roadBand, bas + k)).ToList()));
        recs.Add(Record("Logic", 0x21, Enumerable.Range(0, n + 1).Select(i => Pt("Logic", logicBand, i)).ToList()));
        recs.AddRange(rj);
        var lists = new List<List<(uint, string)>>();
        for (int p = 0; p <= n; p++)
        {
            var cur = new List<(uint, string)>();
            foreach (int k in new[] { p - 1, p, p + 1 }) cur.Add(((uint)Py.Mod(k, n), "Main"));
            foreach (var (name, _, bas) in gates)
                for (int k = 0; k < GatePoints; k++)
                {
                    long d = Py.Mod(p - (bas + k), n);
                    if (Math.Min(d, n - d) <= 1) cur.Add(((uint)k, name));
                }
            lists.Add(cur);
        }
        AltLinks(lists, alts, n, true);
        var line = new[] { road.Centre[si].X, y + H[si], road.Centre[si].Z };
        var info = new AidInfo { Points = n + 1, StartIndex = si, FinishIndex = si, StartLine = line, StartAngle = road.Heading[si], FinishLine = line, FinishAngle = road.Heading[si], Closed = true };
        foreach (var g in gates) info.Gates[g.Name] = (int)Py.Mod(g.Base, n);
        return (Finish(recs, n + 1, lists), info);
    }
}

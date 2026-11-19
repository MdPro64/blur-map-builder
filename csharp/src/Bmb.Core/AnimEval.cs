// Where an animated object is at a time: the 3D preview and the Animation sub-tab use this, and it follows what
// AnimExport writes for the game (same path curves, constant speed along each piece, the object facing the way it moves).
// The "anim" of an object (world.json): see AnimExport. Extra key here: "rkeys" = rotation keys [{"t", "angle" (degrees)}]
// (the Rotation row of the editor); without it each position key's "angle" is used.
namespace Bmb.Core;

public static class AnimEval
{
    public const double DefaultLength = 10.0;

    public static string Mode(JObj a) => a.Str("mode") ?? "path";

    public static List<double[]> PathPoints(JObj a) => (a.Arr("path") ?? new()).OfType<List<object?>>().Select(p => p.Select(Json.D).ToArray()).ToList();

    public static List<JObj> Keys(JObj a) => (a.Arr("keys") ?? new()).OfType<JObj>().OrderBy(k => k.Num("t")).ToList();
    public static List<JObj> RKeys(JObj a) => (a.Arr("rkeys") ?? new()).OfType<JObj>().OrderBy(k => k.Num("t")).ToList();

    /// the rotation (degrees, added to "facing the way it moves") at time t: rotation keys hold their value until the next one
    public static double AngleAt(JObj a, double t)
    {
        var rk = RKeys(a);
        if (rk.Count > 0)
        {
            double v = rk[0].Num("angle");
            foreach (var k in rk) if (k.Num("t") <= t + 1e-9) v = k.Num("angle");
            return v;
        }
        if (Mode(a) == "keys")
        {
            var ks = Keys(a); double v = ks.Count > 0 ? ks[0].Num("angle") : 0;
            foreach (var k in ks) if (k.Num("t") <= t + 1e-9) v = k.Num("angle");
            return v;
        }
        return a.Num("angle");
    }

    // ---- the curve the game drives along (Catmull-Rom x 0.6 tangents, the same as AnimExport.Spline)
    static double[] Tangent(IList<double[]> p, int i, bool closed)
    {
        int n = p.Count;
        double[] a = i > 0 ? p[i - 1] : closed ? p[n - 1] : p[i], b = i < n - 1 ? p[i + 1] : closed ? p[0] : p[i];
        double k = (i > 0 && i < n - 1) || closed ? 0.3 : 0.6;
        return new[] { (b[0] - a[0]) * k, (b[1] - a[1]) * k, (b[2] - a[2]) * k };
    }

    static double[] Hermite(double[] p0, double[] m0, double[] p1, double[] m1, double s)
    {
        double s2 = s * s, s3 = s2 * s, h00 = 2 * s3 - 3 * s2 + 1, h10 = s3 - 2 * s2 + s, h01 = -2 * s3 + 3 * s2, h11 = s3 - s2;
        return new[] { h00 * p0[0] + h10 * m0[0] + h01 * p1[0] + h11 * m1[0], h00 * p0[1] + h10 * m0[1] + h01 * p1[1] + h11 * m1[1], h00 * p0[2] + h10 * m0[2] + h01 * p1[2] + h11 * m1[2] };
    }

    /// the curve as many short straight pieces (16 per point gap); closed = back to the first point
    public static List<double[]> Curve(IList<double[]> p, bool closed, int per = 16)
    {
        var o = new List<double[]>();
        int n = p.Count;
        if (n == 0) return o;
        if (n == 1) { o.Add(p[0]); return o; }
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            int j = (i + 1) % n;
            var m0 = Tangent(p, i, closed); var m1 = Tangent(p, j, closed);
            for (int q = 0; q < per; q++) o.Add(Hermite(p[i], m0, p[j], m1, q / (double)per));
        }
        o.Add(closed ? p[0] : p[n - 1]);
        return o;
    }

    /// one piece between keys i and i+1 (its ends use the neighbours' directions, as AnimExport writes it)
    public static List<double[]> KeyPiece(IList<double[]> pts, int i, int per = 16)
    {
        var m0 = Tangent(pts, i, false); var m1 = Tangent(pts, i + 1, false);
        var o = new List<double[]>();
        for (int q = 0; q <= per; q++) o.Add(Hermite(pts[i], m0, pts[i + 1], m1, q / (double)per));
        return o;
    }

    public static double Length(IList<double[]> c) { double s = 0; for (int i = 1; i < c.Count; i++) s += Dist(c[i - 1], c[i]); return s; }
    static double Dist(double[] a, double[] b) => Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2));

    /// the point d metres along a polyline + the direction there (radians, our "turn": forward = (sin, cos))
    public static (double[] P, double Turn) Along(IList<double[]> c, double d)
    {
        if (c.Count == 1) return (c[0], 0);
        for (int i = 1; i < c.Count; i++)
        {
            double l = Dist(c[i - 1], c[i]);
            if (d <= l || i == c.Count - 1)
            {
                double f = l < 1e-9 ? 0 : Math.Clamp(d / l, 0, 1);
                var a = c[i - 1]; var b = c[i];
                return (new[] { a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f }, Math.Atan2(b[0] - a[0], b[2] - a[2]));
            }
            d -= l;
        }
        return (c[^1], 0);
    }

    /// how long one pass takes (seconds); spin: one turn
    public static double Duration(JObj a)
    {
        switch (Mode(a))
        {
            case "keys": { var ks = Keys(a); var rk = RKeys(a); double e = Math.Max(ks.Count > 0 ? ks[^1].Num("t") : 0, rk.Count > 0 ? rk[^1].Num("t") : 0); return Math.Max(e, 0.1); }
            case "spin": return 60.0 / Math.Max(1, Math.Abs(a.Num("rpm", 20)));
            default:
            {
                var p = PathPoints(a);
                return p.Count < 2 ? 0.1 : Length(Curve(p, a.Truthy("closed"))) / Math.Max(0.1, a.Num("speed", 20));
            }
        }
    }

    /// where the object is at time t (seconds since it started); rest = its placed position + turn
    public static (double[] P, double Turn) Pose(JObj a, double t, double[] rest, double restTurn)
    {
        double deg = Math.PI / 180;
        switch (Mode(a))
        {
            case "spin":
                return (rest, restTurn + 2 * Math.PI * a.Num("rpm", 20) / 60.0 * Math.Max(0, t));
            case "keys":
            {
                var ks = Keys(a);
                if (ks.Count == 0) return (rest, restTurn + AngleAt(a, t) * deg);
                var pts = ks.Select(k => new[] { k.Num("x"), k.Num("y"), k.Num("z") }).ToList();
                double total = ks[^1].Num("t");
                if (a.Truthy("loop") && total > 0.05 && t > total) t %= total;
                if (ks.Count == 1 || t <= ks[0].Num("t"))
                {
                    double dir = ks.Count > 1 ? Along(KeyPiece(pts, 0), 0.01).Turn : restTurn - AngleAt(a, 0) * deg;
                    return (pts[0], dir + AngleAt(a, t) * deg);
                }
                for (int i = 0; i + 1 < ks.Count; i++)
                {
                    double t0 = ks[i].Num("t"), t1 = ks[i + 1].Num("t");
                    if (t > t1 && i + 2 < ks.Count) continue;
                    var piece = KeyPiece(pts, i);
                    double len = Length(piece), f = t1 - t0 < 1e-6 ? 1 : Math.Clamp((t - t0) / (t1 - t0), 0, 1);
                    var (p, dir) = Along(piece, len * f);
                    if (f >= 1) dir = Along(piece, Math.Max(0, len - 0.01)).Turn;
                    // the game turns the piece's angle in when the piece starts (one value per piece)
                    return (p, dir + AngleAt(a, t0) * deg);
                }
                return (pts[^1], restTurn);
            }
            default:
            {
                var pts = PathPoints(a);
                if (pts.Count < 2) return (rest, restTurn);
                bool closed = a.Truthy("closed");
                var c = Curve(pts, closed);
                double len = Length(c), d = a.Num("speed", 20) * Math.Max(0, t);
                if (a.Truthy("loop") && len > 0.01) d %= len; else d = Math.Min(d, len);
                var (p, dir) = Along(c, d);
                if (d >= len - 1e-6) dir = Along(c, Math.Max(0, len - 0.01)).Turn;
                return (p, dir + a.Num("angle") * deg);
            }
        }
    }

    /// the keys the game gets: the position keys with the rotation (rkeys) at each key's time
    public static List<JObj> ExportKeys(JObj a)
    {
        var ks = Keys(a).Select(k => Share.Copy(k)).ToList();
        if (RKeys(a).Count > 0) foreach (var k in ks) k["angle"] = AngleAt(a, k.Num("t"));
        return ks;
    }
}

// Minimap, start cameras, lighting, sky, level light. Ports of flat_earth/minimap.py, intro_cam.py, lighting.py, sky.py, level/lstree.py.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Bmb.Core;

public static class Minimap
{
    public const int W = 1280, H = 720;
    const double Aspect = (double)H / W;
    public const string SrcGameIpf = "\\ui\\data\\ipf\\minimap\\usa_la_river_game.ipf", SrcTexture = "\\ui\\data\\minimap\\usa_la_river.texture";
    const int Bounds0 = 0x33, Bounds1 = 0x1D3, AltNode = 0x1D3, RouteNode = 0x58B;

    public static string GameIpfName(string city) => $"\\ui\\data\\ipf\\minimap\\{city.ToLowerInvariant()}_game.ipf";
    public static string TextureName(string city) => $"\\ui\\data\\minimap\\{city.ToLowerInvariant()}.texture";

    public static (double X0, double X1, double Z0, double Z1) CityBounds(IEnumerable<Road> roads, double margin = 1000.0)
    {
        var bs = roads.Select(r => r.Bounds()).ToList();
        double x0 = bs.Min(b => b.X0) - margin, x1 = bs.Max(b => b.X1) + margin, z0 = bs.Min(b => b.Z0) - margin, z1 = bs.Max(b => b.Z1) + margin;
        double w = x1 - x0, h = z1 - z0;
        if (h < w * Aspect) { double g = (w * Aspect - h) / 2; z0 -= g; z1 += g; }
        else { double g = (h / Aspect - w) / 2; x0 -= g; x1 += g; }
        return (x0, x1, z0, z1);
    }

    static (double U, double V) Uv((double X0, double X1, double Z0, double Z1) b, double x, double z) => ((x - b.X0) / (b.X1 - b.X0), (b.Z1 - z) / (b.X1 - b.X0));

    static byte[] Header(byte[] src, int node) => src.AsSpan(node + 12 + (int)B.U32(src, node + 8), 34).ToArray();

    static byte[] Node(string name, uint flags, uint kind = 1)
    {
        var o = new ByteBuf(); o.AddU32(kind).AddU32(flags).AddU32((uint)name.Length).Add(B.Bytes(name));
        return o.ToArray();
    }

    static byte[] FillShape(string name, byte[] header, List<List<(double U, double V)>> polys, double[]? extra = null)
    {
        var o = new ByteBuf(1024);
        o.Add(Node(name, 2)).Add(header);
        double u0 = polys.SelectMany(p => p).Min(q => q.U), v0 = polys.SelectMany(p => p).Min(q => q.V), u1 = polys.SelectMany(p => p).Max(q => q.U), v1 = polys.SelectMany(p => p).Max(q => q.V);
        foreach (double v in new[] { u0, v0, 0.0, u1, v1, 0.0 }) o.AddF32((float)v);
        foreach (double v in extra ?? new double[4]) o.AddF32((float)v);
        o.AddU32((uint)polys.Count);
        foreach (var p in polys)
        {
            o.AddU32((uint)p.Count);
            foreach (var (u, v) in p) { o.AddF32((float)u).AddF32((float)v).AddF32(0).AddF32(0).AddF32(1).AddF32(0); }
        }
        o.AddU32((uint)polys.Count);
        foreach (var p in polys)
        {
            var idx = new List<int>();
            if (p.Count == 4) idx.AddRange(new[] { 3, 0, 1, 1, 2, 3 });
            else for (int k = 1; k < p.Count - 1; k++) idx.AddRange(new[] { 0, k, k + 1 });
            o.AddU32((uint)idx.Count);
            foreach (int i in idx) o.AddU16((ushort)i);
        }
        o.AddU32(0);
        return o.ToArray();
    }

    static List<(double U, double V)> Ccw(List<(double U, double V)> p)
    {
        double a = 0;
        for (int k = 0; k < p.Count; k++) a += p[k].U * p[(k + 1) % p.Count].V - p[(k + 1) % p.Count].U * p[k].V;
        if (a > 0) return p;
        var r = new List<(double, double)>(p); r.Reverse();
        return r;
    }

    public static List<List<(double U, double V)>> RoutePolys((double X0, double X1, double Z0, double Z1) b, Road road, int every = 3)
    {
        double width = b.X1 - b.X0, minW = width * 0.010;
        var el = new List<(double, double)>(); var er = new List<(double, double)>();
        for (int i = 0; i < road.Count; i++)
        {
            double half = Math.Max(road.WidthAt[i], minW) / 2;
            var (cx, cz) = road.Centre[i]; var (sx, sz) = road.Side(i);
            el.Add(Uv(b, cx - sx * half, cz - sz * half)); er.Add(Uv(b, cx + sx * half, cz + sz * half));
        }
        var idx = new List<int>();
        for (int i = 0; i < road.Count; i += every) idx.Add(i);
        if (idx[^1] != road.Count - 1) idx.Add(road.Count - 1);
        var o = new List<List<(double, double)>>();
        for (int k = 0; k + 1 < idx.Count; k++) { int i = idx[k], j = idx[k + 1]; o.Add(Ccw(new() { el[i], el[j], er[j], er[i] })); }
        return o;
    }

    public static List<List<(double U, double V)>> StartPolys((double X0, double X1, double Z0, double Z1) b, Road road, double s)
    {
        double width = b.X1 - b.X0, t = width * 0.004, half = road.WAt(s) / 2 + width * 0.004;
        var pts = new List<(double, double)>();
        foreach (var (ds, lat) in new[] { (-t / 2, -half), (-t / 2, half), (t / 2, half), (t / 2, -half) })
        {
            var (x, z, _) = road.At(s + ds, lat);
            pts.Add(Uv(b, x, z));
        }
        return new() { Ccw(pts) };
    }

    public static byte[] GameIpf(byte[] src, (double X0, double X1, double Z0, double Z1) b, List<(string Name, Road R, double S0, List<List<(double U, double V)>>? Polys)> routes)
    {
        var routeHead = Header(src, RouteNode); var altHead = Header(src, AltNode);
        var kids = new List<byte[]> { src.AsSpan(Bounds0, Bounds1 - Bounds0).ToArray() };
        foreach (var (name, road, s0, polys) in routes)
        {
            var sp = StartPolys(b, road, s0);
            double u0 = sp[0].Min(q => q.U), u1 = sp[0].Max(q => q.U), v0 = sp[0].Min(q => q.V), v1 = sp[0].Max(q => q.V);
            kids.Add(FillShape(name.ToLowerInvariant() + "_alt", altHead, sp, new[] { u0, v0, u1 - u0, v1 - v0 }));
            kids.Add(FillShape(name.ToLowerInvariant(), routeHead, polys ?? RoutePolys(b, road)));
        }
        var o = new ByteBuf(4096);
        o.AddU32(0x100000).AddU32(1).Add(Node("game", 0, 3)).AddU32(1).Add(Node("<group>", 1, 3)).AddU32((uint)kids.Count);
        foreach (var k in kids) o.Add(k);
        return o.ToArray();
    }

    /// the 1280 x 720 background (drawn with GDI+: same look as the Python picture, not the same pixels)
    public static byte[] Picture((double X0, double X1, double Z0, double Z1) b, IEnumerable<Road> roads)
    {
        using var bm = new Bitmap(W, H);
        using var g = Graphics.FromImage(bm);
        g.Clear(Color.FromArgb(16, 22, 30));
        double sc = W / (b.X1 - b.X0);
        PointF Px(double x, double z) { var (u, v) = Uv(b, x, z); return new PointF((float)(u * W), (float)(v * W)); }
        using var grid = new Pen(Color.FromArgb(24, 32, 44));
        for (double q = Math.Floor(b.X0 / 250) * 250; q < b.X1; q += 250) g.DrawLine(grid, Px(q, b.Z0), Px(q, b.Z1));
        for (double q = Math.Floor(b.Z0 / 250) * 250; q < b.Z1; q += 250) g.DrawLine(grid, Px(b.X0, q), Px(b.X1, q));
        g.SmoothingMode = SmoothingMode.None;
        foreach (var r in roads)
        {
            var pts = new List<PointF>();
            for (int i = 0; i < r.Count; i += 2) pts.Add(Px(r.Centre[i].X, r.Centre[i].Z));
            pts.Add(Px(r.Centre[^1].X, r.Centre[^1].Z));
            int w = Math.Max(3, (int)(r.Width * sc));
            using var p1 = new Pen(Color.FromArgb(40, 200, 230), w + 4) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var p2 = new Pen(Color.FromArgb(70, 76, 86), w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(p1, pts.ToArray()); g.DrawLines(p2, pts.ToArray());
        }
        return Pic.FromBitmap(bm).Px;
    }

    public static byte[] Texture(byte[] srcTex, byte[] rgb)
    {
        if (B.U32(srcTex, 0x38) != W || B.U32(srcTex, 0x3C) != H) throw new InvalidDataException("not a 1280 x 720 texture");
        return B.Concat(srcTex.AsSpan(0, 0x54).ToArray(), Dxt.Dxt1Encode(rgb, W, H));
    }

    /// trace a picture into minimap polygons (m["minimap"] own picture)
    public static List<List<(double U, double V)>> ImagePolys((double X0, double X1, double Z0, double Z1) b, string path, int w = 640, int h = 360, int threshold = 128)
    {
        (double X0, double X1, double Z0, double Z1) ib = b;
        if (File.Exists(path + ".json"))
        {
            var a = ((JObj)Json.Load(path + ".json")!).Arr("bounds")!;
            ib = (Json.D(a[0]), Json.D(a[1]), Json.D(a[2]), Json.D(a[3]));
        }
        var pic = Pic.Load(path);
        // grey picture, resized (Pillow's default bicubic is not ported: nearest sample is enough for a traced shape)
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = ((y * pic.H / h) * pic.W + x * pic.W / w) * 3;
                px[y * w + x] = (pic.Px[o] * 299 + pic.Px[o + 1] * 587 + pic.Px[o + 2] * 114) / 1000;
            }
        double span = ib.X1 - ib.X0, zspan = ib.Z1 - ib.Z0;
        (double, double) World(double x, double y) => (ib.X0 + x / w * span, ib.Z1 - y / h * zspan);
        var rects = new List<(int, int, int, int)>();
        var open = new Dictionary<(int, int), int>();
        for (int y = 0; y <= h; y++)
        {
            var runs = new List<(int, int)>();
            if (y < h)
            {
                int x = 0;
                while (x < w)
                {
                    if (px[y * w + x] > threshold)
                    {
                        int x0 = x;
                        while (x < w && px[y * w + x] > threshold) x++;
                        runs.Add((x0, x));
                    }
                    else x++;
                }
            }
            var nxt = new Dictionary<(int, int), int>();
            foreach (var run in runs) { if (open.Remove(run, out int y0)) nxt[run] = y0; else nxt[run] = y; }
            foreach (var ((x0, x1), y0) in open) rects.Add((x0, y0, x1, y));
            open = nxt;
        }
        var polys = new List<List<(double, double)>>();
        foreach (var (x0, y0, x1, y1) in rects)
        {
            var c = new[] { World(x0, y0), World(x1, y0), World(x1, y1), World(x0, y1) };
            polys.Add(Ccw(c.Select(q => Uv(b, q.Item1, q.Item2)).ToList()));
        }
        return polys;
    }
}

public static class IntroCam
{
    static readonly byte[] TemplateHead = Convert.FromHexString(
        "0f000000000000000000000000000000000000000000803f0000000000000000000000000000803f0000803f0000803f00000000" +
        "00000000000000000000803f0000000000000000000000000000803f0000803f0000803f00000000690000000000d0420000f041" +
        "0300000000000303000000080000004166746b526f6f740600000063616d6572610a00000063616d6572612e666f760200030001" +
        "00000001000100000001000100000000000100000041008300400012000000000000000000000000000000fe7ffdfffeff");
    const double Fps = 30.0, R = 0.70710677, Fov = 0.9;
    public const int VistaFrames = 180, PackFrames = 105;
    public const double VistaStage = 4.5, PackStage = 3.5;
    static readonly int[][] Slots = { new[] { 1, 2, 3 }, new[] { 2, 3, 0 }, new[] { 3, 0, 1 }, new[] { 0, 1, 2 } };

    static byte[] QuatEncode(double[] q0)
    {
        double n = Math.Sqrt(q0.Sum(x => x * x));
        if (n == 0) n = 1.0;
        var q = q0.Select(x => x / n).ToArray();
        int idx = 0;
        for (int k = 1; k < 4; k++) if (Math.Abs(q[k]) > Math.Abs(q[idx])) idx = k;
        ulong v = (q[idx] < 0 ? 1UL : 0UL) | ((ulong)idx << 1);
        int[] sh = { 3, 18, 33 };
        for (int i = 0; i < 3; i++)
        {
            double x = Math.Max(-R, Math.Min(R, q[Slots[idx][i]]));
            v |= (ulong)Math.Max(0, Math.Min(0x7FFF, Py.Round((x / R + 1.0) * 32767 / 2.0))) << sh[i];
        }
        var o = new byte[6];
        B.Put(o, 0, (ushort)((v >> 32) & 0xFFFF)); B.Put(o, 2, (ushort)((v >> 16) & 0xFFFF)); B.Put(o, 4, (ushort)(v & 0xFFFF));
        return o;
    }

    static double[] LookQuat(double[] eye, double[] t)
    {
        double fx = t[0] - eye[0], fy = t[1] - eye[1], fz = t[2] - eye[2], ln = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        if (ln == 0) ln = 1.0;
        double yaw = Math.Atan2(fx, fz), pitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, fy / ln)));
        double cy = Math.Cos(yaw / 2), sy = Math.Sin(yaw / 2), cp = Math.Cos(-pitch / 2), sp = Math.Sin(-pitch / 2);
        return new[] { cy * sp, sy * cp, -sy * sp, cy * cp };
    }

    static int TableSize(int npos, int nfov, int nrot) => (37 + 13 * npos + 5 * nfov + (6 * nrot + 3) / 4 * 4 + nrot + 15) / 16 * 16;

    sealed record Frame(double[] P, double[] Q, double F);

    static byte[] WriteCycle(List<Frame> frames, int rotEvery = 2)
    {
        int n = frames.Count, last = n - 1;
        var posF = Enumerable.Range(1, last - 1).ToList();
        var rotF = posF.Where(f => f % rotEvery == 0).ToList();
        var fovF = new List<int> { last / 2 };
        var body = new ByteBuf(n * 16 + 64);
        void Full(int f) { var (p, q, fov) = frames[f]; body.AddF32((float)p[0]).AddF32((float)p[1]).AddF32((float)p[2]).AddF32((float)fov).Add(QuatEncode(q)); }
        Full(0);
        body.AddU32((uint)posF.Count).AddU16((ushort)fovF.Count).AddU16((ushort)rotF.Count).AddU32(0);
        foreach (var lst in new[] { posF, fovF, rotF }) { body.Add((byte)lst.Count); foreach (int f in lst) body.Add((byte)f); }
        foreach (int f in posF) foreach (double v in frames[f].P) body.AddF32((float)v);
        foreach (int f in fovF) body.AddF32((float)frames[f].F);
        foreach (int f in rotF) body.Add(QuatEncode(frames[f].Q));
        Full(last);
        int b = TableSize(posF.Count, fovF.Count, rotF.Count);
        var head = (byte[])TemplateHead.Clone();
        B.Put(head, 0x5C, (uint)n); B.Put(head, 0x60, (float)last); B.Put(head, 0x64, (float)Fps);
        var o = new ByteBuf(head.Length + 24 + body.Len);
        o.Add(head);
        foreach (int v in new[] { 2, 0, b + 0x30, 0, 0, 0, last, b, last, 0, 0, 0x30 }) o.AddU16((ushort)v);
        return o.Add(body.Span).ToArray();
    }

    public static double[] Rotate(double[] q, double[] v)
    {
        double x = q[0], y = q[1], z = q[2], w = q[3], tx = 2 * (y * v[2] - z * v[1]), ty = 2 * (z * v[0] - x * v[2]), tz = 2 * (x * v[1] - y * v[0]);
        return new[] { v[0] + w * tx + (y * tz - z * ty), v[1] + w * ty + (z * tx - x * tz), v[2] + w * tz + (x * ty - y * tx) };
    }

    public const int MaxViews = 12;

    /// what the game shows before the countdown: (eye, rotation, field of view) per frame
    public static List<(double[] Eye, double[] Q, double Fov)> PlayFrames(Road road, double s0, double gy, JObj m)
    {
        var own = m.Obj("start_cams");
        List<Frame> Shot(string shot, int n)
        {
            var views = own?.Objs(shot).Where(v => v.Truthy("eye") && v.Truthy("look")).ToList() ?? new();
            return views.Count > 0 ? Keyed(views, n) : shot == "vista" ? Vista(road, s0, gy) : Pack(road, s0, gy);
        }
        return Shot("vista", VistaFrames).Take((int)(VistaStage * Fps)).Concat(Shot("pack", PackFrames).Take((int)(PackStage * Fps))).Select(f => (f.P, f.Q, f.F)).ToList();
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    static double[] RoadPt(Road road, double s, double lat, double up, double groundY)
    {
        s = road.Closed ? Py.Mod(s, road.Length) : Math.Max(0.0, Math.Min(road.Length, s));
        var (x, z, _) = road.At(s, lat);
        return new[] { x, groundY + road.HAt(s, lat) + up, z };
    }

    static List<Frame> Vista(Road road, double s0, double gy, int side = 1)
    {
        double w = road.WAt(s0);
        var o = new List<Frame>();
        for (int f = 0; f < VistaFrames; f++)
        {
            double t = Smooth((double)f / (VistaFrames - 1));
            var eye = RoadPt(road, s0 + 120.0 - 135.0 * t, side * (w / 2 + 25.0 - 12.0 * t), 34.0 - 24.0 * t, gy);
            var target = RoadPt(road, s0 - 30.0 - 15.0 * t, 0.0, 1.0, gy);
            o.Add(new(eye, LookQuat(eye, target), Fov));
        }
        return o;
    }

    static List<Frame> Pack(Road road, double s0, double gy, int side = -1)
    {
        double w = road.WAt(s0), lat = side * Math.Min(w / 2 - 3.0, 14.0);
        var o = new List<Frame>();
        for (int f = 0; f < PackFrames; f++)
        {
            double t = Smooth((double)f / (PackFrames - 1));
            var eye = RoadPt(road, s0 + 4.0 - 34.0 * t, lat, 1.7, gy);
            var target = RoadPt(road, s0 - 22.0 - 30.0 * t, -lat * 0.3, 0.8, gy);
            o.Add(new(eye, LookQuat(eye, target), Fov));
        }
        return o;
    }

    static double[] Catmull(double[] p0, double[] p1, double[] p2, double[] p3, double t)
    {
        double t2 = t * t, t3 = t * t * t;
        var o = new double[3];
        for (int k = 0; k < 3; k++)
            o[k] = 0.5 * (2 * p1[k] + (p2[k] - p0[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2 + (3 * p1[k] - p0[k] - 3 * p2[k] + p3[k]) * t3);
        return o;
    }

    static List<Frame> Keyed(List<JObj> views, int n)
    {
        var eyes = views.Select(v => v.Arr("eye")!.Select(Json.D).ToArray()).ToList();
        var looks = views.Select(v => v.Arr("look")!.Select(Json.D).ToArray()).ToList();
        var o = new List<Frame>();
        for (int f = 0; f < n; f++)
        {
            double[] eye, look;
            if (views.Count == 1) { eye = eyes[0]; look = looks[0]; }
            else
            {
                double u = Smooth((double)f / (n - 1)) * (views.Count - 1);
                int i = Math.Min((int)u, views.Count - 2);
                double t = u - i;
                double[] At(List<double[]> ps) => Catmull(ps[Math.Max(i - 1, 0)], ps[i], ps[i + 1], ps[Math.Min(i + 2, ps.Count - 1)], t);
                eye = At(eyes); look = At(looks);
            }
            o.Add(new(eye, LookQuat(eye, look), Fov));
        }
        return o;
    }

    public static Dictionary<string, byte[]> CyclesForMap(Road road, double s0, double gy, JObj m)
    {
        var own = m.Obj("start_cams");
        var o = new Dictionary<string, byte[]>();
        foreach (var (shot, n) in new[] { ("vista", VistaFrames), ("pack", PackFrames) })
        {
            var views = own?.Objs(shot).Where(v => v.Truthy("eye") && v.Truthy("look")).ToList() ?? new();
            var fr = views.Count > 0 ? Keyed(views, n) : shot == "vista" ? Vista(road, s0, gy) : Pack(road, s0, gy);
            o[shot] = WriteCycle(fr);
        }
        return o;
    }
}

public static class LevelLight
{
    static (int, int, int) FindChunk(byte[] d, string name)
    {
        var key = new byte[8]; B.Bytes(name).CopyTo(key, 0);
        int o = d.AsSpan().IndexOf(key);
        if (o <= 0) throw new InvalidDataException(name);
        return (o, o + 16, (int)B.U32(d, o + 8));
    }

    static List<double[]> RoadPoints(List<Road> roads, int n, double gy)
    {
        double total = roads.Sum(r => r.Length), perSpot = 3 + 2 / 3.0, step = Math.Max(total * perSpot / n, 2.0);
        var pts = new List<double[]>();
        foreach (var r in roads)
        {
            double s = step / 2; int k = 0;
            while (s < r.Length)
            {
                double y = gy + r.HAt(s) + 1.5, w = r.WAt(s) / 2;
                var lats = new List<double> { 0.0, w + 20, -(w + 20) };
                if (k % 3 == 0) { lats.Add(w + 150); lats.Add(-(w + 150)); }
                foreach (double lat in lats)
                {
                    var (x, z, _) = r.At(s, lat);
                    pts.Add(new[] { x, Math.Abs(lat) < w + 30 ? y : gy + 1.5, z });
                }
                s += step; k++;
            }
        }
        if (pts.Count > n)
        {
            var o = new List<double[]>(n); int c = pts.Count;
            for (int i = 0; i < n; i++) o.Add(pts[(int)Py.Round((double)(c - 1) * i / (n - 1))]);
            pts = o;
        }
        while (pts.Count < n) { var p = pts[pts.Count % Math.Max(1, pts.Count)]; pts.Add(new[] { p[0], p[1] + 6.0, p[2] }); }
        return pts;
    }

    public static byte[]? Write(byte[] d, JObj values, List<Road> roads, double gy)
    {
        var col = values.Arr("object_light")?.Select(Json.D).ToArray() ?? new[] { 1.0, 0.97, 0.92, 0.0 };
        double up = values.Arr("object_light_up")?.Select(Json.D).First() ?? 0.6;
        if (col[3] <= 0) return null;
        var sh = new List<double>();
        for (int c = 0; c < 3; c++) { double s = col[3] * col[c]; sh.AddRange(new[] { 0.075 * up * s, 0.8 * up * s, -0.075 * up * s, s }); }
        var (_, b, size) = FindChunk(d, "LSTree");
        int end = b + size, nPlanes = (int)B.U32(d, b), p = b + 4 + 16 * nPlanes, nPts = (int)B.U32(d, p), q = p + 4 + 10 * nPts, nNodes = (int)B.U32(d, q);
        var pts = RoadPoints(roads, nPts, gy);
        var o = (byte[])d.Clone();
        var (_, sb, _) = FindChunk(d, "LSData");
        int nSamples = (int)B.U32(d, sb);
        for (int i = 0; i < nSamples; i++) for (int k = 0; k < 12; k++) B.Put(o, sb + 4 + 24 * i + 2 * k, (Half)sh[k]);
        int nInner = (nNodes - 1) / 2, nLeaves = nNodes - nInner;
        var planes = new List<double[]>(); var words = new List<uint>(); var ordered = new List<double[]>();
        void Build(List<double[]> P, int n)
        {
            if (n == 1) { words.AddRange(new[] { 0xFFFFFFFF, (uint)ordered.Count, (uint)P.Count }); ordered.AddRange(P); return; }
            int a = n / 2;
            int axis = P.Max(q2 => q2[0]) - P.Min(q2 => q2[0]) >= P.Max(q2 => q2[2]) - P.Min(q2 => q2[2]) ? 0 : 2;
            P = P.OrderBy(q2 => q2[axis]).ToList();
            int k = P.Count - Math.Max(1, (int)Py.Round((double)P.Count * a / n));
            k = Math.Min(Math.Max(k, 1), P.Count - 1);
            int lo = k, hi = k;
            while (hi < P.Count && P[hi - 1][axis] == P[hi][axis]) hi++;
            while (lo > 1 && P[lo - 1][axis] == P[lo][axis]) lo--;
            if (P[lo - 1][axis] != P[lo][axis] && (hi >= P.Count || k - lo <= hi - k)) k = lo;
            else if (hi < P.Count) k = hi;
            double v = (P[k - 1][axis] + P[k][axis]) / 2.0;
            planes.Add(axis == 0 ? new[] { 1.0, 0.0, 0.0, -v } : new[] { 0.0, 0.0, 1.0, -v });
            words.Add(1); words.Add((uint)planes.Count - 1);
            Build(P.GetRange(k, P.Count - k), a);
            Build(P.GetRange(0, k), n - a);
        }
        Build(pts.Select(q2 => new[] { (double)(Half)q2[0], (double)(Half)q2[1], (double)(Half)q2[2] }).ToList(), nLeaves);
        while (planes.Count < nPlanes) planes.Add(planes[^1]);
        for (int k = 0; k < nPlanes; k++) for (int c = 0; c < 4; c++) B.Put(o, b + 4 + 16 * k + 4 * c, (float)planes[k][c]);
        for (int k = 0; k < ordered.Count; k++)
        {
            B.Put(o, p + 4 + 10 * k, (uint)(k % nSamples));
            for (int c = 0; c < 3; c++) B.Put(o, p + 8 + 10 * k + 2 * c, (Half)ordered[k][c]);
        }
        for (int k = 0; k < words.Count; k++) B.Put(o, q + 4 + 4 * k, words[k]);
        return o;
    }
}

public static class Lighting
{
    public const uint LightUidBase = 0xF1C00000;
    public const string DefaultPreset = "Amboy (desert day)";
    public static readonly Dictionary<string, int> Presets = new()
    {
        ["Amboy (desert day)"] = 2226, ["LA River (day)"] = 805, ["LA Downtown"] = 2829, ["LA Observatory"] = 2149, ["Long Beach Docks"] = 3689, ["Barcelona Gracia (night)"] = 1994,
        ["Barcelona Oval"] = 221, ["Brighton"] = 526, ["London Hackney"] = 1116, ["Tokyo Shutoko"] = 696, ["Mount Haruna"] = 1929, ["NY Dumbo"] = 1996,
        ["San Francisco Russian Hill"] = 1513, ["San Francisco Sausalito"] = 1545,
    };
    public static readonly Dictionary<string, (int Off, int N, string Label, double Max)> Fields = new()
    {
        ["sun"] = (0, 4, "Sun colour + strength", 60.0), ["sky"] = (16, 4, "Sky (ambient) colour + strength", 4.0), ["baked"] = (32, 4, "Baked light colour + strength", 4.0),
        ["emissive"] = (48, 4, "Emissive (glow) colour + strength", 4.0), ["fog_colour"] = (64, 4, "Fog colour", 1.0), ["fog_density"] = (80, 1, "Distance fog density", 0.01),
        ["height_fog"] = (96, 1, "Height fog density", 0.01), ["sky_rotation"] = (116, 1, "Sky picture rotation", 6.3), ["sky_intensity"] = (120, 1, "Sky picture brightness", 0.2),
        ["middle_grey"] = (152, 1, "Exposure (middle grey)", 1.0), ["exposure_min"] = (156, 1, "Exposure min", 4.0), ["exposure_max"] = (160, 1, "Exposure max", 8.0),
        ["bloom"] = (164, 1, "Bloom strength (0 = no glow)", 1.5), ["bloom_threshold"] = (184, 1, "Bloom threshold (higher = less glow)", 4.0),
    };

    public static (string Preset, JObj Values) Effective(JObj loc)
    {
        var lt = loc.Obj("lighting");
        if (lt == null || lt.Count == 0)
        {
            var v = new JObj { ["object_light"] = new List<object?> { 1.0, 0.97, 0.92, 0.14 }, ["object_light_up"] = new List<object?> { 0.6 } };
            return (DefaultPreset, v);
        }
        return (lt.Truthy("preset") ? lt.Str("preset")! : DefaultPreset, lt.Obj("values") ?? new JObj());
    }

    public static int LsObject(Kslf k, int cityObj)
    {
        var top = k.Top(k.Objects[cityObj]);
        var env = (Dictionary<string, object?>)top["EnvironmentSettings"]!;
        var uid = (string)((Dictionary<string, object?>)env["lightingSettings"]!)["uid"]!;
        return k.ObjOf(Convert.ToUInt32(uid, 16));
    }

    /// the preset's own values (read from the unpacked game database)
    public static Dictionary<string, double[]> PresetValues(string preset)
    {
        var o = new Dictionary<string, double[]>();
        var F = new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
        int b = F.Objects[LsObject(F, Presets.GetValueOrDefault(preset, Presets[DefaultPreset]))].Blocks[0].Offset;
        foreach (var (k, f) in Fields)
        {
            var v = new double[f.N];
            for (int i = 0; i < f.N; i++) v[i] = B.F32(F.Data, b + f.Off + 4 * i);
            o[k] = v;
        }
        return o;
    }

    static int City(JObj lt) => Presets.TryGetValue(lt.Str("preset") ?? "", out int c) ? c : Presets[DefaultPreset];

    public static (byte[] Amax, Dictionary<int, byte[]> Env) Prepare(byte[] amax, JObj wld)
    {
        var o = new Dictionary<int, byte[]>();
        var edited = new Dictionary<int, string>();
        int li = -1;
        foreach (var loc in wld.Objs("locations"))
        {
            li++;
            var lt = loc.Obj("lighting");
            if (lt == null || lt.Count == 0) continue;
            int city = City(lt);
            var vals = new JObj();
            foreach (var kv in lt.Obj("values") ?? new JObj()) if (Fields.ContainsKey(kv.Key)) vals[kv.Key] = kv.Value;
            string sig = Json.Dumps(vals, -1);
            uint? uid = null;
            if (vals.Count > 0 && !edited.ContainsKey(city))
            {
                var A = new Kslf(amax);
                int b = A.Objects[LsObject(A, city)].Blocks[0].Offset;
                amax = (byte[])amax.Clone();
                foreach (var (k, v) in vals)
                {
                    var (off, n, _, _) = Fields[k];
                    var l = (List<object?>)v!;
                    for (int i = 0; i < Math.Min(n, l.Count); i++) B.Put(amax, b + off + 4 * i, (float)Json.D(l[i]));
                }
                edited[city] = sig;
            }
            else if (vals.Count > 0 && edited[city] != sig)
            {
                var A = new Kslf(amax);
                int src = LsObject(A, city);
                uid = LightUidBase + (uint)li;
                amax = KslfWrite.CloneObjectAny(amax, src, null, uid.Value, (data, pool, blocks) =>
                {
                    foreach (var (k, v) in vals)
                    {
                        var (off, n, _, _) = Fields[k];
                        var l = (List<object?>)v!;
                        for (int i = 0; i < Math.Min(n, l.Count); i++) data.Put(blocks[0] + off + 4 * i, (float)Json.D(l[i]));
                    }
                });
            }
            var A2 = new Kslf(amax);
            if (lt.Truthy("sky_image"))
            {
                int oi = uid != null ? A2.ObjOf(uid.Value) : LsObject(A2, city);
                var (old, _, _) = Sky.RecordSky(amax, A2.Objects[oi]);
                amax = Sky.SetRecordSky(amax, A2.Objects[oi], Sky.OwnName(old, li));
                A2 = new Kslf(amax);
            }
            int bb = A2.Objects[city].Blocks[0].Offset;
            var env = amax.AsSpan(bb + 52, 80).ToArray();
            if (uid != null) B.Put(env, 0, (uint)A2.RowOf(uid.Value));
            o[li] = env;
        }
        return (amax, o);
    }

    public static (List<KeyValuePair<string, byte[]>> Files, List<KeyValuePair<string, string>> Aliases) SkyFiles(JObj wld, string mapsDir, Func<string, byte[]> readRoot, Action<string> log)
    {
        var files = new List<KeyValuePair<string, byte[]>>(); var aliases = new List<KeyValuePair<string, string>>();
        Kslf? F = null; int li = -1;
        foreach (var loc in wld.Objs("locations"))
        {
            li++;
            var lt = loc.Obj("lighting");
            if (lt == null || !lt.Truthy("sky_image")) continue;
            string src = Path.Combine(mapsDir, lt.Str("sky_image")!);
            if (!File.Exists(src)) { log($"custom sky of {loc.Str("name")}: {src} is missing - the preset's sky stays"); continue; }
            F ??= new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
            var (old, _, _) = Sky.RecordSky(F.Data, F.Objects[LsObject(F, City(lt))]);
            string name = "\\" + Sky.OwnName(old, li).Replace('/', '\\'), bas = "\\" + old.Replace('/', '\\');
            files.Add(new(name, Sky.Texture(readRoot(bas), src)));
            aliases.Add(new(name, bas));
            log($"custom sky of {loc.Str("name")}: {lt.Str("sky_image")} -> {name}");
        }
        return (files, aliases);
    }
}

public static class Sky
{
    const int W = 2048, H = 512, SkyOff = 112;
    const string SkyDir = "graphics/textures/skies/";

    public static string OwnName(string old, int li)
    {
        string stem = old[SkyDir.Length..^".texture".Length];
        string s = "z" + li.ToString(CultureInfo.InvariantCulture).PadLeft(stem.Length - 1, '0');
        return SkyDir + s[..stem.Length] + ".texture";
    }

    public static (string Path, int At, byte[] Pool) RecordSky(byte[] amax, KObject obj)
    {
        int at = (int)B.U32(amax, obj.Blocks[0].Offset + SkyOff);
        return (B.Sz(obj.Pool, at), obj.Data + obj.DSize, obj.Pool);
    }

    public static byte[] SetRecordSky(byte[] amax, KObject obj, string nw)
    {
        var (old, at, pool) = RecordSky(amax, obj);
        if (nw.Length != old.Length) throw new InvalidOperationException("sky name length");
        var plain = (byte[])pool.Clone();
        B.Bytes(nw).CopyTo(plain, (int)B.U32(amax, obj.Blocks[0].Offset + SkyOff));
        var o = (byte[])amax.Clone();
        Kslf.Scramble(plain).CopyTo(o, at);
        return o;
    }

    /// linear light as float (w, h, rgb)
    public static (float[] Px, int W, int H) ReadPanorama(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".hdr") return ReadHdr(path);
        if (ext == ".exr") throw new InvalidOperationException(".exr is not supported in the C# version - save the sky as .hdr");
        var p = Pic.Load(path);
        var o = new float[p.W * p.H * 3];
        Parallel.For(0, o.Length, i => o[i] = MathF.Pow(p.Px[i] / 255f, 2.2f));
        return (o, p.W, p.H);
    }

    static (float[], int, int) ReadHdr(string path)
    {
        var d = File.ReadAllBytes(path);
        int at = 0;
        while (true)
        {
            int e0 = Array.IndexOf(d, (byte)'\n', at);
            string line = B.Latin1.GetString(d, at, e0 - at).Trim();
            at = e0 + 1;
            if (line.Length == 0) break;
        }
        int end = Array.IndexOf(d, (byte)'\n', at);
        var parts = B.Latin1.GetString(d, at, end - at).Trim().Split(' ');
        int h = int.Parse(parts[1]), w = int.Parse(parts[3]);
        bool flipY = parts[0] == "+Y", flipX = parts[2] == "-X";
        at = end + 1;
        var rows = new byte[h * w * 4];
        for (int y = 0; y < h; y++)
        {
            if (w >= 8 && w < 32768 && d[at] == 2 && d[at + 1] == 2 && (d[at + 2] << 8 | d[at + 3]) == w)
            {
                at += 4;
                for (int c = 0; c < 4; c++)
                {
                    int x = 0;
                    while (x < w)
                    {
                        int n = d[at++];
                        if (n > 128) { n -= 128; byte v = d[at++]; for (int k = 0; k < n; k++) rows[(y * w + x + k) * 4 + c] = v; }
                        else { for (int k = 0; k < n; k++) rows[(y * w + x + k) * 4 + c] = d[at + k]; at += n; }
                        x += n;
                    }
                }
            }
            else { Array.Copy(d, at, rows, y * w * 4, w * 4); at += w * 4; }
        }
        var o = new float[h * w * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4, yy = flipY ? h - 1 - y : y, xx = flipX ? w - 1 - x : x, t = (yy * w + xx) * 3;
                int e = rows[s + 3];
                double k = e > 0 ? Math.ScaleB(1.0, e - 136) : 0.0;
                for (int c = 0; c < 3; c++) o[t + c] = (float)((rows[s + c] + 0.5) * k);
            }
        return (o, w, h);
    }

    /// the 0x54-byte header + our DXT5 picture. (Pillow's BC3 coder is not ported: same picture, other bytes.)
    public static byte[] Texture(byte[] header, string path)
    {
        var (pano, pw, ph0) = ReadPanorama(path);
        int ph = ph0 / 2;
        var top = new float[W * H * 3];
        // bilinear resize of the upper half (triangle filter, wider when shrinking - as Pillow's BILINEAR)
        double sx = (double)pw / W, sy = (double)ph / H, fx = Math.Max(1, sx), fy = Math.Max(1, sy);
        var tmp = new float[W * ph * 3];
        Parallel.For(0, ph, y =>
        {
            for (int x = 0; x < W; x++)
            {
                double c = (x + 0.5) * sx;
                int i0 = Math.Max(0, (int)(c - fx + 0.5)), i1 = Math.Min(pw, (int)(c + fx + 0.5));
                double[] s = new double[3]; double ww = 0;
                for (int i = i0; i < i1; i++)
                {
                    double t = Math.Abs((i - c + 0.5) / fx), k = t < 1 ? 1 - t : 0;
                    ww += k;
                    for (int q = 0; q < 3; q++) s[q] += pano[(y * pw + i) * 3 + q] * k;
                }
                for (int q = 0; q < 3; q++) tmp[(y * W + x) * 3 + q] = (float)(s[q] / ww);
            }
        });
        Parallel.For(0, H, y =>
        {
            double c = (y + 0.5) * sy;
            int i0 = Math.Max(0, (int)(c - fy + 0.5)), i1 = Math.Min(ph, (int)(c + fy + 0.5));
            for (int x = 0; x < W * 3; x++)
            {
                double s = 0, ww = 0;
                for (int i = i0; i < i1; i++)
                {
                    double t = Math.Abs((i - c + 0.5) / fy), k = t < 1 ? 1 - t : 0;
                    ww += k; s += tmp[i * W * 3 + x] * k;
                }
                top[y * W * 3 + x] = (float)(s / ww);
            }
        });
        var peak = new float[W * H];
        for (int i = 0; i < top.Length; i++) top[i] = MathF.Pow(Math.Max(top[i], 0), 1 / 2.2f);
        for (int i = 0; i < peak.Length; i++) peak[i] = Math.Max(top[3 * i], Math.Max(top[3 * i + 1], top[3 * i + 2]));
        var sorted = (float[])peak.Clone(); Array.Sort(sorted);
        double pos = 0.99 * (sorted.Length - 1); int pi = (int)pos;
        double pc = sorted[pi] + (sorted[Math.Min(pi + 1, sorted.Length - 1)] - sorted[pi]) * (pos - pi);
        float scale = (float)Math.Max(pc, 1e-6);
        var rgba = new byte[W * H * 4];
        Parallel.For(0, W * H, i =>
        {
            float g0 = top[3 * i] / scale, g1 = top[3 * i + 1] / scale, g2 = top[3 * i + 2] / scale;
            float a = Math.Clamp(Math.Max(g0, Math.Max(g1, g2)), 1 / 255f, 1f);
            rgba[4 * i] = (byte)MathF.Round(Math.Clamp(g0 / a, 0, 1) * 255);
            rgba[4 * i + 1] = (byte)MathF.Round(Math.Clamp(g1 / a, 0, 1) * 255);
            rgba[4 * i + 2] = (byte)MathF.Round(Math.Clamp(g2 / a, 0, 1) * 255);
            rgba[4 * i + 3] = (byte)MathF.Round(a * 255);
        });
        return B.Concat(header.AsSpan(0, 0x54).ToArray(), Dxt5(rgba, W, H));
    }

    public static byte[] Dxt5(byte[] rgba, int w, int h)
    {
        int bw = w / 4, bh = h / 4;
        var o = new byte[bw * bh * 16];
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++) { rgb[3 * i] = rgba[4 * i]; rgb[3 * i + 1] = rgba[4 * i + 1]; rgb[3 * i + 2] = rgba[4 * i + 2]; }
        var col = Dxt.Dxt1Encode(rgb, w, h);
        Parallel.For(0, bh, row =>
        {
            Span<int> pal = stackalloc int[8];
            for (int c = 0; c < bw; c++)
            {
                int at = (row * bw + c) * 16, a0 = 0, a1 = 255;
                for (int k = 0; k < 16; k++) { int v = rgba[((row * 4 + k / 4) * w + c * 4 + k % 4) * 4 + 3]; a0 = Math.Max(a0, v); a1 = Math.Min(a1, v); }
                o[at] = (byte)a0; o[at + 1] = (byte)a1;
                pal[0] = a0; pal[1] = a1;
                if (a0 > a1) for (int k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                else for (int k = 2; k < 8; k++) pal[k] = a0;
                ulong bits = 0;
                for (int k = 0; k < 16; k++)
                {
                    int v = rgba[((row * 4 + k / 4) * w + c * 4 + k % 4) * 4 + 3], best = 0, bd = 999;
                    for (int j = 0; j < 8; j++) { int dd = Math.Abs(pal[j] - v); if (dd < bd) { bd = dd; best = j; } }
                    bits |= (ulong)best << (3 * k);
                }
                for (int k = 0; k < 6; k++) o[at + 2 + k] = (byte)(bits >> (8 * k));
                col.AsSpan((row * bw + c) * 8, 8).CopyTo(o.AsSpan(at + 8));
            }
        });
        return o;
    }
}

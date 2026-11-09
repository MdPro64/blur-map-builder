// The Landscape tab works in the 3D view: the brush paints on the hills you see (a ray marched over Terrain.HillAt),
// the hills are drawn live with their textures while you paint, and a ring under the mouse shows the brush.
// Ground models of the 3D tab (ramps, bumps, hills, painted surfaces) are drawn with their textures too.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class View3D
{
    public string LandTool = "raise", LandPaintKey = "rock"; public double LandSize = 80, LandStrength = 1.5;
    bool _landStroke, _landErase, _landDirty = true; double _landTarget; long _landLast; double[]? _landHover;
    readonly List<TexDraw> _landLive = new();
    public bool LandMode => SceneMode == "land";
    /// the Landscape tab's info line wants to know when a stroke ends
    public event Action? LandChanged;

    static readonly Dictionary<string, Pic> _surfPics = new();
    /// a surface's picture for the 3D view (the same picture the build puts in the game, 256 x 256)
    public static Pic SurfacePic(string key)
    {
        lock (_surfPics)
        {
            if (_surfPics.TryGetValue(key, out var p)) return p;
            var (d, _) = Surface.MaterialTexture(key, 256, 256);
            return _surfPics[key] = new Pic(256, 256, d);
        }
    }

    static float Light(double nx, double ny, double nz)
    {
        double l = Math.Max(Math.Sqrt(nx * nx + ny * ny + nz * nz), 1e-9);
        double d = (nx * Mesh.SunX + ny * Mesh.SunY + nz * Mesh.SunZ) / l / Math.Sqrt(Mesh.SunX * Mesh.SunX + Mesh.SunY * Mesh.SunY + Mesh.SunZ * Mesh.SunZ);
        return (float)(0.5 + 0.55 * Math.Max(0, d));
    }

    /// a ground model drawn with its texture (its own normals give the light)
    void ChunkTex(Chunk c, string owner = "")
    {
        var st = c.Strip; var v = c.Verts; var o = new List<float>(st.Count * 9);
        for (int i = 0; i + 2 < st.Count; i++)
        {
            int a0 = st[i], a1 = st[i + 1], a2 = st[i + 2];
            if (a0 == a1 || a1 == a2 || a0 == a2) continue;
            foreach (int q in new[] { a0, a1, a2 })
            {
                var p = v[q];
                float l = Light(p[3], p[4], p[5]);
                o.Add((float)(p[0] + c.Anchor[0])); o.Add((float)(p[1] + c.Anchor[1])); o.Add((float)(p[2] + c.Anchor[2]));
                o.Add(l); o.Add(l); o.Add(l); o.Add(1); o.Add((float)p[6]); o.Add((float)p[7]);
            }
        }
        if (o.Count > 0) _textured.Add(new TexDraw { Key = "surf:" + c.Key, Image = SurfacePic(c.Key), V = o.ToArray(), Owner = owner });
    }

    /// the hills of the map, sampled fresh (fast enough to follow the brush)
    void LandLiveBuild()
    {
        _landDirty = false;
        _landLive.Clear();
        var r = D.Road();
        if (r == null) return;
        var hs = Terrain.Heights(D.Map);
        if (hs.Count == 0) return;
        var net = Network.Get(D.Map, r);
        double area = hs.Count * Terrain.Step * Terrain.Step * 1.6;
        double step = area / 16 < 90_000 ? 4 : area / 36 < 90_000 ? 6 : 8;
        var sm = Terrain.Sample(D.Map, net, step);
        double g = Fe.GroundY;
        foreach (var grp in sm.Keys.GroupBy(kv => kv.Value))
        {
            var quads = grp.Select(kv => kv.Key).ToList();
            var o = new float[quads.Count * 6 * 9]; int at = 0;
            void Vx(int i, int j)
            {
                double y = sm.H(i, j), x = i * step, z = j * step;
                float l = Light(sm.H(i - 1, j) - sm.H(i + 1, j), 2 * step, sm.H(i, j - 1) - sm.H(i, j + 1));
                o[at] = (float)x; o[at + 1] = (float)(g + y + Terrain.Lift); o[at + 2] = (float)z;
                o[at + 3] = o[at + 4] = o[at + 5] = l; o[at + 6] = 1; o[at + 7] = (float)(x / Surface.TexM); o[at + 8] = (float)(z / Surface.TexM);
                at += 9;
            }
            foreach (var (i, j) in quads) { Vx(i, j); Vx(i + 1, j); Vx(i + 1, j + 1); Vx(i, j); Vx(i + 1, j + 1); Vx(i, j + 1); }
            _landLive.Add(new TexDraw { Key = "surf:" + grp.Key, Image = SurfacePic(grp.Key), V = o });
        }
    }

    double HillY(Network net, Dictionary<(int, int), double> hs, double x, double z) => Fe.GroundY + Terrain.HillAt(hs, net, x, z);

    /// where the mouse ray meets the hills (or the flat ground)
    double[]? LandHit(int mx, int my)
    {
        var ray = RayAt(mx, my); var r = D.Road();
        if (ray == null || r == null) return null;
        var (o, d) = ray.Value;
        if (d[1] > -1e-6) return null;
        var net = Network.Get(D.Map, r); var hs = Terrain.Heights(D.Map);
        double g = Fe.GroundY, top = g + Terrain.MaxHeight + 2;
        double t0 = o[1] > top ? (top - o[1]) / d[1] : 0, t1 = (g - o[1]) / d[1];
        double[] At(double t) => V(o[0] + d[0] * t, o[1] + d[1] * t, o[2] + d[2] * t);
        if (hs.Count > 0)
        {
            double step = Math.Max(0.5, (t1 - t0) / 600), prev = t0;
            double F(double t) { var p = At(t); return p[1] - HillY(net, hs, p[0], p[2]); }
            for (double t = t0; t <= t1; t += step)
            {
                if (F(t) <= 0)
                {
                    double a = prev, b = t;
                    for (int k = 0; k < 24; k++) { double mid = (a + b) / 2; if (F(mid) > 0) a = mid; else b = mid; }
                    return At(b);
                }
                prev = t;
            }
        }
        return At(t1);
    }

    void LandDown(MouseEventArgs e, bool erase)
    {
        var p = LandHit(e.X, e.Y);
        if (p == null) return;
        D.Snapshot();
        _landStroke = true; _landErase = erase; _landLast = 0;
        _landTarget = Terrain.HeightAt(D.Map, p[0], p[2]);
        LandDab(p[0], p[2]);
    }

    void LandDab(double x, double z)
    {
        long now = Environment.TickCount64;
        if (now - _landLast < 35) return;
        _landLast = now;
        double rad = Math.Max(4, LandSize / 2);
        if (LandTool == "paint") Terrain.PaintLand(D.Map, x, z, rad, _landErase ? null : LandPaintKey);
        else
        {
            string tool = _landErase ? "erase" : LandTool;
            double st = tool is "raise" or "lower" or "noise" ? LandStrength : Math.Min(1.0, LandStrength * 0.2);
            Terrain.Brush(D.Map, tool, x, z, rad, st, _landTarget);
        }
        _landDirty = true;
        G.Invalidate();
    }

    void LandMove(int mx, int my)
    {
        _landHover = LandHit(mx, my);
        if (_landStroke && _landHover != null) LandDab(_landHover[0], _landHover[2]);
        G.Invalidate();
    }

    void LandUp()
    {
        if (!_landStroke) return;
        _landStroke = false; _landErase = false;
        D.Touched();
        Edited?.Invoke();
        LandChanged?.Invoke();
        G.Invalidate();
    }

    /// the live hills, then the brush ring on top
    void LandDraw()
    {
        if (_landDirty) LandLiveBuild();
        if (_landLive.Count > 0) DrawTextured(_landLive);
    }

    void LandRing()
    {
        if (_landHover == null) return;
        var r = D.Road();
        if (r == null) return;
        var net = Network.Get(D.Map, r); var hs = Terrain.Heights(D.Map);
        double rad = Math.Max(4, LandSize / 2), cx = _landHover[0], cz = _landHover[2];
        var col = _landErase || LandTool is "lower" ? new Rgba(1, .38f, .38f) : LandTool switch
        {
            "smooth" => new Rgba(.35f, .7f, 1), "flatten" => new Rgba(1, .82f, .23f), "noise" => new Rgba(.75f, .5f, 1),
            "paint" => Rgba.Hex(Surface.Surfaces.TryGetValue(LandPaintKey, out var k) ? k.Colour : "#ffffff"), _ => new Rgba(.35f, .82f, .42f),
        };
        var b = new Batch();
        const int n = 64;
        double[] P(double rr, int q) { double a = 2 * Math.PI * q / n, x = cx + rr * Math.Cos(a), z = cz + rr * Math.Sin(a); return V(x, HillY(net, hs, x, z) + .6, z); }
        foreach (var (rr, c) in new[] { (rad, col), (rad * .98, new Rgba(1, 1, 1)) })
            for (int q = 0; q < n; q++) { var a = P(rr, q); var bb = P(rr, q + 1); b.Add((float)a[0], (float)a[1], (float)a[2], c); b.Add((float)bb[0], (float)bb[1], (float)bb[2], c); }
        double cy = HillY(net, hs, cx, cz) + .6;
        b.Add((float)(cx - 1.5), (float)cy, (float)cz, col); b.Add((float)(cx + 1.5), (float)cy, (float)cz, col);
        b.Add((float)cx, (float)cy, (float)(cz - 1.5), col); b.Add((float)cx, (float)cy, (float)(cz + 1.5), col);
        GL.glDisable(GL.DEPTH_TEST); GL.glLineWidth(2); b.Draw(GL.LINES); GL.glLineWidth(1); GL.glEnable(GL.DEPTH_TEST);
    }

    /// tests: one brush dab at a world point
    public void LandDabAt(double x, double z, bool erase = false)
    {
        D.Snapshot(); _landStroke = true; _landErase = erase; _landLast = 0; _landTarget = Terrain.HeightAt(D.Map, x, z);
        LandDab(x, z); LandUp();
    }
    public void LandHoverAt(double x, double z) { _landHover = V(x, 0, z); G.Invalidate(); }
}

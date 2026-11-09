// The Landscape tab in the 2D view: hills painted with brushes (Terrain.Brush), drawn as a height map. The road always stays
// clear: hills fade out next to it (Terrain.RoadDist.Clear), and the map shows them that way.
using System.Runtime.CompilerServices;
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MapView
{
    public string LandTool = "raise"; public double LandSize = 80, LandStrength = 1.5;
    bool _landStroke, _landErase; double _landTarget; long _lastDab;
    static readonly ConditionalWeakTable<Network, Terrain.RoadDist> _dists = new();
    static readonly ConditionalWeakTable<Network, Dictionary<(int, int), double>> _clears = new();

    double ClearAt(Network net, int i, int j)
    {
        var c = _clears.GetValue(net, _ => new());
        lock (c)
        {
            if (c.TryGetValue((i, j), out double v)) return v;
            return c[(i, j)] = _dists.GetValue(net, n => new Terrain.RoadDist(n)).Clear(i * Terrain.Step, j * Terrain.Step);
        }
    }

    static Rgba HeightColour(double h)
    {
        (double H, float R, float G, float B)[] stops = { (0, .16f, .32f, .17f), (8, .25f, .52f, .24f), (25, .55f, .5f, .28f), (55, .5f, .38f, .26f), (90, .82f, .82f, .8f) };
        for (int k = 1; k < stops.Length; k++)
            if (h <= stops[k].H)
            {
                var a = stops[k - 1]; var b = stops[k]; float t = (float)((h - a.H) / (b.H - a.H));
                return new Rgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
            }
        return new Rgba(.9f, .9f, .9f);
    }

    void LandDraw(Network net)
    {
        var hs = Terrain.Heights(D.Map);
        if (hs.Count == 0) return;
        double half = Terrain.Step / 2;
        foreach (var ((i, j), h) in hs)
        {
            double v = h * ClearAt(net, i, j);
            if (v < 0.05) continue;
            double x = i * Terrain.Step, z = j * Terrain.Step;
            var (a, b) = ToScreen(x - half, z + half); var (c, d) = ToScreen(x + half, z - half);
            _tri.Rect(a, b, c + 1, d + 1, HeightColour(v));
        }
    }

    void LandRing()
    {
        var (bx, bz) = ToWorld(_mouse.X, _mouse.Y);
        var (sx, sy) = ToScreen(bx, bz);
        double pr = Math.Max(6, Math.Round(LandSize / 2 * Scale));
        var col = _landErase || LandTool is "lower" or "erase" ? Rgba.Hex("#ff6060") : LandTool switch { "smooth" => Rgba.Hex("#5ab4ff"), "flatten" => Rgba.Hex("#ffd23b"), "noise" => Rgba.Hex("#c080ff"), _ => Rgba.Hex("#58d06a") };
        _top.Ring(sx, sy, pr, 2, White); _top.Ring(sx, sy, Math.Max(pr - 2, 2), 2, col);
    }

    void LandAt(MouseEventArgs e, bool erase)
    {
        D.Snapshot();
        _landStroke = true; _landErase = erase;
        var (x, z) = ToWorld(e.X, e.Y);
        _landTarget = Terrain.HeightAt(D.Map, x, z);
        _lastDab = 0;
        LandDab(x, z);
    }

    void LandDab(double x, double z)
    {
        long now = Environment.TickCount64;
        if (now - _lastDab < 30) return;
        _lastDab = now;
        string tool = _landErase ? "erase" : LandTool;
        double st = tool is "raise" or "lower" or "noise" ? LandStrength : Math.Min(1.0, LandStrength * 0.2);
        Terrain.Brush(D.Map, tool, x, z, Math.Max(4, LandSize / 2), st, _landTarget);
        G.Invalidate();
    }

    void LandUp()
    {
        _landStroke = false; _landErase = false; Commit();
        var hs = Terrain.Heights(D.Map);
        Tell(hs.Count == 0 ? "No hills on this map" : $"Hills: {hs.Count} points of {Terrain.Step:0} m, highest {hs.Values.Max():0} m - see them in the 3D tab, Build + install to drive on them", C("#58d06a"));
    }
}

public sealed partial class MapView
{
    public const string BumpColour = "#ff8c3a";

    /// one dab of the Surface brush: a surface type, or bumps
    void Dab(double x, double z)
    {
        double r = Math.Max(1.0, BrushSize) / 2;
        if (BrushKey == "bumps") Terrain.BumpBrush(D.Map, x, z, r, Erasing);
        else Surface.Brush(D.Map, x, z, r, Erasing ? null : BrushKey);
    }

    void BumpDraw()
    {
        var b = D.Map.Obj("surface")?.Obj("bumps");
        if (b == null || b.Count == 0) return;
        var c = Rgba.Hex(BumpColour); double C = Surface.CellM;
        foreach (var (k, v) in b)
        {
            int p = k.IndexOf(','); int i = int.Parse(k[..p]), j = int.Parse(k[(p + 1)..]);
            double st = Bmb.Core.Json.D(v);
            var col = new Rgba(c.R, c.G, c.B, (float)(0.35 + 0.5 * st));
            var (a, bb) = ToScreen(i * C, (j + 1) * C); var (cc, d) = ToScreen((i + 1) * C, j * C);
            // a chequer so the road still shows through
            if (((i + j) & 1) == 0) _tri.Rect(a, bb, cc + 1, d + 1, col);
            else { float mx = (a + cc) / 2, my = (bb + d) / 2; _tri.Rect(mx - 1, my - 1, mx + 1, my + 1, col); }
        }
    }
}

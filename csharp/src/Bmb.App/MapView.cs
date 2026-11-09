// The 2D map: drawing + the tools. Follows road_designer.py (redraw / on_down / on_move ...), designer_net.py and designer_surface.py. Drawn with OpenGL, so it stays smooth at any size.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MapView
{
    readonly Doc D; public readonly GlControl G;
    public double Scale = 0.15, Ox, Oz;     // pixels per metre, world point at the top left
    public string Tool = "select", Mode = "2d", PType = "Nitro";
    public bool Snap; public int Sel = -1, SelPick = -1, SelBranch = -1, SelBpt = -1, SelSplit = -1;
    public string BrushKey = "dirt"; public double BrushSize = 6; public bool Erasing; public bool AiPen; public double AiLane = Layout.AiLane;
    readonly Batch _tri = new(), _top = new();
    (string Kind, int I, int J)? _drag; (int X, int Y)? _pan; (double Ox, double Oz) _panStart;
    bool _stroke; List<(double X, double Z)>? _aiStroke; (int X, int Y) _mouse;
    public List<(double X, double Z)>? Measure; public bool MeasureDone; (double X, double Z)? _mouseWorld;
    sealed class NetDraft { public string Kind = ""; public double FromM; public List<JObj> Points = new(); }
    NetDraft? _draft;
    bool _lapSnap, _startHover;
    public event Action? Edited;                     // the document changed (full refresh of the panels)
    public event Action? Selected;                   // only the selection changed
    public event Action<string, Color?>? Say;

    const double SnapM = 10.0, LineStep = 20.0, MinRadius = 35.0; const int LapSnapPx = 20, MeasureMax = 50;
    public static readonly string[] Types = World.PickupTypes.Keys.ToArray();
    public static readonly Dictionary<string, (string Label, string Hk)> ToolInfo = new()
    {
        ["select"] = ("Select", "S"), ["move"] = ("Move", "M"), ["add"] = ("Add point", "A"), ["insert"] = ("Insert", "I"), ["row"] = ("Power-up row", "R"), ["single"] = ("Single power-up", "P"),
        ["erase"] = ("Erase", "E"), ["branch"] = ("Branch", "B"), ["split"] = ("Lane split", "L"), ["measure"] = ("Measure", "D"),
    };
    static readonly Dictionary<string, Rgba> TypeCol = new()
    {
        ["Shunt"] = Rgba.Hex("#ff4040"), ["Barge"] = Rgba.Hex("#ff9f1a"), ["Shock"] = Rgba.Hex("#3aa0ff"), ["Mine"] = Rgba.Hex("#ffe14a"), ["Bolt"] = Rgba.Hex("#b070ff"),
        ["Nitro"] = Rgba.Hex("#40e0ff"), ["Shield"] = Rgba.Hex("#40ff70"), ["Repair"] = Rgba.Hex("#ffffff"),
    };
    static readonly Rgba White = new(1, 1, 1), Black = new(0, 0, 0), Green = Rgba.Hex("#35e05a"), Yellow = Rgba.Hex("#ffd23b"), Red = Rgba.Hex("#ff4040"), Wall = Rgba.Hex("#28e6ff"), WallGlow = Rgba.Hex("#0d5566"),
        Grid = Rgba.Hex("#16233a"), BranchCol = Rgba.Hex("#4f6a5a"), BranchSel = Rgba.Hex("#5a7a66"), BranchEdge = Rgba.Hex("#5cf0a0"), SplitCol = Rgba.Hex("#ffb020"), MeasureCol = Rgba.Hex("#ff8af0");

    public MapView(Doc d, GlControl g)
    {
        D = d; G = g;
        g.MouseDown += Down; g.MouseMove += Move; g.MouseUp += Up; g.MouseWheel += Wheel;
    }

    void Tell(string s, Color? c = null) => Say?.Invoke(s, c);
    static Color C(string hex) => ColorTranslator.FromHtml(hex);

    public (float X, float Y) ToScreen(double x, double z) => ((float)((x - Ox) * Scale), (float)((Oz - z) * Scale));
    public (double X, double Z) ToWorld(double sx, double sy) => (Ox + sx / Scale, Oz - sy / Scale);

    public void Fit()
    {
        var pts = D.Points.Select(p => Road.NormPoint(p, 60)).ToList();
        if (pts.Count == 0) return;
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), z0 = pts.Min(p => p.Z), z1 = pts.Max(p => p.Z);
        double w = Math.Max(G.Width, 200), h = Math.Max(G.Height, 200);
        Scale = 0.8 * Math.Min(w / (x1 - x0 + 500), h / (z1 - z0 + 500));
        Ox = (x0 + x1) / 2 - w / 2 / Scale; Oz = (z0 + z1) / 2 + h / 2 / Scale;
    }

    (double X, double Z) WorldXY(MouseEventArgs e)
    {
        var (x, z) = ToWorld(e.X, e.Y);
        return Snap ? (Py.Round(x / SnapM) * SnapM, Py.Round(z / SnapM) * SnapM) : (x, z);
    }

    bool HasNet => (D.Map.Arr("branches")?.Count ?? 0) > 0 || (D.Map.Arr("splits")?.Count ?? 0) > 0;

    // ---- drawing ---------------------------------------------------------------------------------------------------
    public void Draw()
    {
        GL.glClearColor(0.043f, 0.078f, 0.125f, 1); GL.glClear(GL.COLOR_BUFFER_BIT);
        GL.glDisable(GL.DEPTH_TEST); GL.glEnable(GL.BLEND); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GlControl.Ortho2D(G.Width, G.Height);
        var t = _tri; t.Clear(); _top.Clear();
        int W = G.Width, H = G.Height;
        var (wx0, wz1) = ToWorld(0, 0); var (wx1, wz0) = ToWorld(W, H);
        double step = Scale > 0.08 ? 100.0 : 500.0;
        for (double gx = Math.Floor(wx0 / step) * step; gx < wx1; gx += step) { var (sx, _) = ToScreen(gx, 0); t.Rect(sx, 0, sx + 1, H, Grid); }
        for (double gz = Math.Floor(wz0 / step) * step; gz < wz1; gz += step) { var (_, sy) = ToScreen(0, gz); t.Rect(0, sy, W, sy + 1, Grid); }
        var labels = new List<(float X, float Y, string S, Rgba C)>();
        // other maps of this location
        int mi = -1;
        foreach (var m in D.Loc.Objs("maps"))
        {
            if (++mi == D.Mi) continue;
            var o = D.RoadOf(m);
            if (o == null) continue;
            Polyline(t, o.Centre, 2, Math.Max(2, o.Width * Scale), Rgba.Hex("#3a4250"));
            var (lx, ly) = ToScreen(o.Centre[0].X, o.Centre[0].Z);
            labels.Add((lx, ly, m.Str("name")!, Rgba.Hex("#5c6878")));
        }
        var r = D.Road();
        if (r == null) { Flush(); G.Text(10, 24, "The road can't be made (needs at least 2 points)", Red); return; }
        var net = D.Net()!;
        bool hasNet = HasNet, clean = Mode is "surface" or "land";
        if (Mode == "land") LandDraw(net);
        int bi = 0;
        foreach (var (_, br) in hasNet ? net.Branches : new()) RoadBand(t, br, bi++ == SelBranch ? BranchSel : BranchCol, 2);
        RoadBand(t, r, null, 3);
        // the solid walls
        foreach (var rr in net.Drivable())
            foreach (int side in new[] { -1, 1 })
                foreach (var run in net.WallRuns(rr, side))
                {
                    var pts = run.Select(p => (p[0], p[1])).ToList();
                    Polyline(t, pts, 1, Math.Max(6, (int)(4 * Scale) + 6), WallGlow);
                    Polyline(t, pts, 1, Math.Max(2, (int)(1.2 * Scale) + 2), Wall);
                }
        if (hasNet) NetDraw(r, net, labels, clean);
        if (Mode == "surface") { SurfDraw(r, net); BumpDraw(); }
        // the dashed centre line
        for (int i = 0; i < r.Count - 1; i += 4)
        {
            var (a, b2) = ToScreen(r.Centre[i].X, r.Centre[i].Z); var (c, d) = ToScreen(r.Centre[Math.Min(i + 2, r.Count - 1)].X, r.Centre[Math.Min(i + 2, r.Count - 1)].Z);
            t.Thick(a, b2, c, d, 1, Rgba.Hex("#c8c8c8"));
        }
        // chevron panels on the outside of bends, as the build places them
        if (!clean)
        {
            int room = 144 - (int)Math.Ceiling(r.Length / 48.0) - 6 - (D.Map.Truthy("obstacles") ? 24 : 0) - Layout.BigMin;
            foreach (var (cs, lat) in Layout.ChevronPanels(r, room, D.Map))
            {
                var a = r.At(cs - Layout.PanelLen / 2, lat); var b = r.At(cs + Layout.PanelLen / 2, lat);
                Arrow(t, a.X, a.Z, b.X, b.Z, 4, Rgba.Hex("#f4f4f4"), 8);
            }
        }
        // start / finish / checkpoints
        var (si, fi) = GenAid.RaceLines(r, D.Map);
        int n = r.Count;
        var marks = r.Closed
            ? new[] { (si, Green, "START"), ((si + n / 3) % n, Yellow, "split 1"), ((si + 2 * n / 3) % n, Yellow, "split 2") }
            : new[] { (si, Green, "START"), ((int)(si + (fi - si) / 3.0), Yellow, "split 1"), ((int)(si + 2 * (fi - si) / 3.0), Yellow, "split 2"), (fi, White, "FINISH") };
        if (r.Closed || (0 < si && si < fi && fi < n))
        {
            foreach (var (i, col, label0) in marks)
            {
                if (clean && label0.StartsWith("split")) continue;
                string label = r.Closed && label0 == "START" ? $"START / FINISH ({D.Map.Int("laps", 3)} laps)" : label0;
                double half = r.WidthAt[i] / 2;
                var a = r.At(r.Dist[i], -half - 4); var b = r.At(r.Dist[i], half + 4);
                var (ax, ay) = ToScreen(a.X, a.Z); var (bx, by) = ToScreen(b.X, b.Z);
                _top.Thick(ax, ay, bx, by, _drag?.Kind == label0.ToLowerInvariant() ? 6 : 3, col);
                labels.Add((bx + 3, by + 4, label, col));
                if (!label0.StartsWith("split"))
                {
                    var (hx, hy) = ToScreen(r.Centre[i].X, r.Centre[i].Z);
                    _top.Rect(hx - 7, hy - 7, hx + 7, hy + 7, Black); _top.Rect(hx - 6, hy - 6, hx + 6, hy + 6, col);
                }
            }
            var (mx, mz, ang) = r.At(r.Dist[si] + 60);
            Arrow(_top, mx, mz, mx + 50 * Math.Sin(ang), mz + 50 * Math.Cos(ang), 3, Green, 10);
        }
        if (!clean)
        {
            foreach (var (j, i, _) in r.Crossings)
            {
                var (sx, sy) = ToScreen(r.Centre[j].X, r.Centre[j].Z);
                bool ok = Math.Abs(r.Height[j] - r.Height[i]) >= Road.BridgeHeight - 2;
                _top.Ring(sx, sy, 12, 2, ok ? Yellow : Red);
                labels.Add((sx + 14, sy - 10, ok ? $"BRIDGE {r.Height[j]:0} m" : "CROSSING!", ok ? Yellow : Red));
            }
            // tight turns
            for (int i = 1; i < n - 1; i++)
            {
                double da = Py.Mod(r.Heading[i + 1] - r.Heading[i - 1] + Math.PI, 2 * Math.PI) - Math.PI;
                if (Math.Abs(da) > 1e-6 && 2 * Road.Step / Math.Abs(da) < MinRadius) { var (sx, sy) = ToScreen(r.Centre[i].X, r.Centre[i].Z); _top.Ring(sx, sy, 3, 1, Red, 8); }
            }
        }
        // the tilted parts: orange = the side that goes up, blue = the side that goes down
        if (!clean && r.Tilted)
        {
            var up = Rgba.Hex("#ff9a2e"); var down = Rgba.Hex("#39b6ff");
            var el = r.Edge(-1); var er = r.Edge(1);
            for (int i = 0; i + 1 < n; i++)
            {
                double tan = r.Tan[i];
                if (Math.Abs(tan) < 0.004) continue;
                var (ax, ay) = ToScreen(el[i].X, el[i].Z); var (bx, by) = ToScreen(el[i + 1].X, el[i + 1].Z);
                var (cx, cy) = ToScreen(er[i].X, er[i].Z); var (dx, dy) = ToScreen(er[i + 1].X, er[i + 1].Z);
                _top.Thick(ax, ay, bx, by, 4, tan < 0 ? up : down);
                _top.Thick(cx, cy, dx, dy, 4, tan > 0 ? up : down);
            }
        }
        // power-ups
        foreach (var (idx, x, z, ty) in Dots(r))
        {
            var (sx, sy) = ToScreen(x, z);
            bool big = idx >= 0 && idx == SelPick;
            _top.Disc(sx, sy, big ? 8 : 5, big ? White : Black); _top.Disc(sx, sy, big ? 6 : 4, TypeCol.GetValueOrDefault(ty, White));
        }
        if (Mode == "ai") DrawAi(r, net, hasNet);
        if (!clean)
        {
            var pts = D.Points; int k = 0;
            foreach (var po in pts)
            {
                var p = Road.NormPoint(po, r.Width);
                var (sx, sy) = ToScreen(p.X, p.Z);
                var col = k == 0 ? Green : k == pts.Count - 1 ? White : Rgba.Hex("#ffb020");
                if (po is JObj jc && jc.Truthy("corner")) { _top.Rect(sx - 8, sy - 8, sx + 8, sy + 8, Black); _top.Rect(sx - 7, sy - 7, sx + 7, sy + 7, col); }
                else { _top.Disc(sx, sy, 8, Black); _top.Disc(sx, sy, 7, col); }
                if (k == Sel) _top.Ring(sx, sy, 12, 2, Rgba.Hex("#7fd0ff"));
                string tag = (k + 1).ToString();
                if (po is JObj jo) { if (jo.Truthy("h")) tag += $"  ^{jo.Num("h"):0}m"; if (jo.Truthy("w")) tag += $"  w{jo.Num("w"):0}"; if (jo.Truthy("tilt")) tag += $"  tilt {Math.Abs(jo.Num("tilt")):0.#}°"; }
                labels.Add((sx - 6, sy - 12, tag, Rgba.Hex("#cfd6df")));
                k++;
            }
            // the start as the lap target
            if (pts.Count > 0 && (LapDrag() >= 0 || (_startHover && CanLap())))
            {
                var p0 = Road.NormPoint(pts[0], 60); var (sx, sy) = ToScreen(p0.X, p0.Z);
                if (_lapSnap) { _top.Ring(sx, sy, 20, 4, Green); labels.Add((sx - 50, sy + 34, "let go = LAP TRACK", Green)); }
                else
                {
                    _top.Ring(sx, sy, 14, 2, Green, 16);
                    if (_startHover && LapDrag() < 0) labels.Add((sx - 70, sy + 32, Tool == "add" ? "click = LAP TRACK" : "drag the end point here = LAP TRACK", Green));
                }
            }
        }
        if (Mode == "surface")
        {
            var (bx, bz) = ToWorld(_mouse.X, _mouse.Y);
            var (sx, sy) = ToScreen(bx, bz);
            double pr = Math.Max(6, Math.Round(Math.Max(1, BrushSize) / 2 * Scale));
            _top.Ring(sx, sy, pr, 2, White); _top.Ring(sx, sy, Math.Max(pr - 2, 2), 2, Erasing ? Rgba.Hex("#ff6060") : BrushKey == "bumps" ? Rgba.Hex(BumpColour) : Rgba.Hex(Surface.Surfaces[BrushKey].Colour));
        }
        if (Mode == "land") LandRing();
        MeasureDraw(labels);
        Flush();
        foreach (var (x, y, s, c) in labels) G.Text(x, y, s, c);
        G.Text(10, 18, $"{D.Loc.Str("name")}  >  {D.Map.Str("name")}      F1 = all keys      grid = {step:0} m", Rgba.Hex("#7f8a96"));
    }

    void Flush() { _tri.Draw(GL.TRIANGLES); _top.Draw(GL.TRIANGLES); }

    void Arrow(Batch t, double x0, double z0, double x1, double z1, double w, Rgba c, double head)
    {
        var (a, b) = ToScreen(x0, z0); var (e, f) = ToScreen(x1, z1);
        t.Thick(a, b, e, f, w, c);
        double dx = e - a, dy = f - b, l = Math.Sqrt(dx * dx + dy * dy);
        if (l < 1e-3) return;
        dx /= l; dy /= l;
        t.Tri(e + dx * head, f + dy * head, e - dy * head * .6, f + dx * head * .6, e + dy * head * .6, f - dx * head * .6, c);
    }

    void Polyline(Batch t, IReadOnlyList<(double X, double Z)> pts, int every, double width, Rgba c)
    {
        (float X, float Y)? last = null;
        for (int i = 0; i < pts.Count; i += every)
        {
            var s = ToScreen(pts[i].X, pts[i].Z);
            if (last != null) t.Thick(last.Value.X, last.Value.Y, s.X, s.Y, width, c);
            last = s;
        }
        var e = ToScreen(pts[^1].X, pts[^1].Z);
        if (last != null) t.Thick(last.Value.X, last.Value.Y, e.X, e.Y, width, c);
    }

    void RoadBand(Batch t, Road r, Rgba? fixedCol, int seg)
    {
        var el = r.Edge(-1); var er = r.Edge(1);
        int n = r.Count;
        void One(int i, int j)
        {
            double hh = r.Height[i];
            var col = fixedCol ?? new Rgba(Math.Min(255, 62 + (int)(hh * 18)) / 255f, Math.Min(255, 66 + (int)(hh * 14)) / 255f, 74 / 255f);
            var (a, b) = ToScreen(el[i].X, el[i].Z); var (c, d) = ToScreen(el[j].X, el[j].Z); var (e, f) = ToScreen(er[j].X, er[j].Z); var (g, h) = ToScreen(er[i].X, er[i].Z);
            t.Quad(a, b, c, d, e, f, g, h, col);
        }
        for (int i = 0; i < n - 1; i += seg) One(i, Math.Min(i + seg, n - 1));
        if (r.Closed) One(n - 1, 0);
    }

    // ---- branches + lanes (designer_net.net_draw)
    void NetDraw(Road r, Network net, List<(float, float, string, Rgba)> labels, bool clean)
    {
        int bi = 0;
        foreach (var (_, br) in net.Branches)
        {
            if (!clean) { var (mx, mz) = br.Centre[br.Count / 2]; var (sx, sy) = ToScreen(mx, mz); labels.Add((sx - 30, sy + 4, $"BRANCH {bi + 1}", BranchEdge)); }
            bi++;
        }
        foreach (var (_, ds) in net.Splits) foreach (var d in ds) Polyline(_tri, d.Centre, 2, Math.Max(3, (int)(3 * Scale) + 2), SplitCol);
        if (!clean)
        {
            int si = 0;
            foreach (var (sp, _) in net.Splits)
            {
                var (s0, s1) = Network.SplitSpan(r, sp);
                var (x, z, _) = r.At((s0 + s1) / 2, r.WAt((s0 + s1) / 2) / 2 + 14);
                var (sx, sy) = ToScreen(x, z);
                labels.Add((sx, sy + 4, $"{Network.LaneCount(sp)} LANES", si == SelSplit ? White : SplitCol));
                si++;
            }
        }
        if (clean) return;
        foreach (var (kind, i, x, z) in NetHandles())
        {
            var (sx, sy) = ToScreen(x, z);
            bool sel = (kind[0] == 'b' && i == SelBranch) || (kind[0] == 's' && i == SelSplit);
            _top.Disc(sx, sy, 9, sel ? White : Black); _top.Disc(sx, sy, 8, kind[0] == 'b' ? BranchEdge : SplitCol);
        }
        bi = 0;
        foreach (var b in D.Map.Objs("branches"))
        {
            int pi = 0;
            foreach (var p in b.Objs("points"))
            {
                var (sx, sy) = ToScreen(p.Num("x"), p.Num("z"));
                bool sel = bi == SelBranch && pi == SelBpt;
                _top.Rect(sx - 7, sy - 7, sx + 7, sy + 7, sel ? White : Black); _top.Rect(sx - 6, sy - 6, sx + 6, sy + 6, BranchEdge);
                if (p.Truthy("h")) labels.Add((sx - 10, sy - 10, $"^{p.Num("h"):0}m", Rgba.Hex("#cfd6df")));
                pi++;
            }
            bi++;
        }
    }

    void DraftDraw(Road r)
    {
        if (_draft == null) return;
        var (x, z, _) = r.At(_draft.FromM);
        var line = new List<(double X, double Z)> { (x, z) };
        foreach (var p in _draft.Points) line.Add((p.Num("x"), p.Num("z")));
        line.Add(ToWorld(_mouse.X, _mouse.Y));
        var col = _draft.Kind == "branch" ? BranchEdge : SplitCol;
        for (int i = 0; i + 1 < line.Count; i++) { var (a, b) = ToScreen(line[i].X, line[i].Z); var (c, d) = ToScreen(line[i + 1].X, line[i + 1].Z); Dashed(_top, a, b, c, d, 3, col, 8, 4); }
        var (sx, sy) = ToScreen(x, z);
        _top.Disc(sx, sy, 9, Black); _top.Disc(sx, sy, 8, White);
    }

    static void Dashed(Batch t, double x0, double y0, double x1, double y1, double w, Rgba c, double on, double off)
    {
        double dx = x1 - x0, dy = y1 - y0, l = Math.Sqrt(dx * dx + dy * dy);
        if (l < 1e-3) return;
        dx /= l; dy /= l;
        for (double s = 0; s < l; s += on + off) { double e = Math.Min(s + on, l); t.Thick(x0 + dx * s, y0 + dy * s, x0 + dx * e, y0 + dy * e, w, c); }
    }

    public List<(string Kind, int I, double X, double Z)> NetHandles()
    {
        var o = new List<(string, int, double, double)>();
        var r = D.Road();
        if (r == null) return o;
        int bi = 0;
        foreach (var b in D.Map.Objs("branches"))
        {
            var (s0, s1) = Network.BranchSpan(r, b);
            foreach (var (k, s) in new[] { ("bfrom", s0), ("bto", s1) }) { var (x, z, _) = r.At(s); o.Add((k, bi, x, z)); }
            bi++;
        }
        int si = 0;
        foreach (var sp in D.Map.Objs("splits"))
        {
            var (s0, s1) = Network.SplitSpan(r, sp);
            foreach (var (k, s) in new[] { ("sfrom", s0), ("sto", s1) }) { var (x, z, _) = r.At(s); o.Add((k, si, x, z)); }
            si++;
        }
        return o;
    }

    // ---- the Surface tab
    void SurfDraw(Road r, Network net)
    {
        var st = Surface.Settings(D.Map);
        var roads = net.Drivable().ToList();
        if (st.Road != "tarmac") { var col = Rgba.Hex(Surface.Surfaces[st.Road].Colour); foreach (var rr in roads) RoadBand(_tri, rr, col, 2); }
        foreach (var (key, (rects, polys)) in Surface.PaintShapes(D.Map, net))
        {
            var col = Rgba.Hex(Surface.Surfaces[key].Colour);
            foreach (var (x0, z0, x1, z1) in rects) { var (a, b) = ToScreen(x0, z1); var (c, d) = ToScreen(x1, z0); _tri.Rect(a, b, c + 1, d + 1, col); }
            foreach (var poly in polys)
            {
                var (ax, ay) = ToScreen(poly[0].X, poly[0].Z);
                for (int i = 1; i + 1 < poly.Count; i++) { var (bx, by) = ToScreen(poly[i].X, poly[i].Z); var (cx, cy) = ToScreen(poly[i + 1].X, poly[i + 1].Z); _tri.Tri(ax, ay, bx, by, cx, cy, col); }
            }
        }
        if (st.Sides > 0)
        {
            var col = Rgba.Hex(Surface.Surfaces[st.SideType].Colour);
            foreach (var rr in roads) foreach (int side in new[] { -1, 1 }) SurfStrip(net, rr, side, st.Sides, col);
        }
    }

    void SurfStrip(Network net, Road road, int side, double width, Rgba col)
    {
        var cut = net.WallMask(road)[side < 0 ? 0 : 1];
        var fac = Surface.SideFactor(net, road, side < 0 ? 0 : 1);
        var outer = road.Edge(side);
        int n = road.Count;
        var inner = new (double X, double Z)[n];
        for (int i = 0; i < n; i++)
        {
            var (cx, cz) = road.Centre[i]; var (sx, sz) = road.Side(i);
            double lat = side * Math.Max(road.WidthAt[i] / 2 - width * fac[i], 0.0);
            inner[i] = (cx + sx * lat, cz + sz * lat);
        }
        var clip = net.Clip(road);
        for (int i = 0; i < n - 1; i++)
        {
            int j = i + 1;
            if (cut[i] || cut[j]) continue;
            var (o0, o1, i1, i0) = (outer[i], outer[j], inner[j], inner[i]);
            if (clip != null) { o0 = clip(i, o0.X, o0.Z); o1 = clip(j, o1.X, o1.Z); i1 = clip(j, i1.X, i1.Z); i0 = clip(i, i0.X, i0.Z); }
            var (a, b) = ToScreen(o0.X, o0.Z); var (c, d) = ToScreen(o1.X, o1.Z); var (e, f) = ToScreen(i1.X, i1.Z); var (g, h) = ToScreen(i0.X, i0.Z);
            _tri.Quad(a, b, c, d, e, f, g, h, col);
        }
    }

    void DrawAi(Road r, Network net, bool hasNet)
    {
        var info = D.Info()!;
        var cor = Layout.AiCorridor(r, info, D.Map);
        (float, float)? la = null, lb = null, lc = null;
        void Seg(int i)
        {
            var (lo, hi) = cor[i];
            var a = r.At(r.Dist[i], lo); var b = r.At(r.Dist[i], hi); var c = r.At(r.Dist[i], (lo + hi) / 2);
            var pa = ToScreen(a.X, a.Z); var pb = ToScreen(b.X, b.Z); var pc = ToScreen(c.X, c.Z);
            if (la != null)
            {
                _tri.Quad(la.Value.Item1, la.Value.Item2, pa.X, pa.Y, pb.X, pb.Y, lb!.Value.Item1, lb.Value.Item2, Rgba.Hex("#3d4a24").WithA(0.55f));
                _top.Thick(la.Value.Item1, la.Value.Item2, pa.X, pa.Y, 1, Rgba.Hex("#8a9a40"));
                _top.Thick(lb!.Value.Item1, lb.Value.Item2, pb.X, pb.Y, 1, Rgba.Hex("#8a9a40"));
                _top.Thick(lc!.Value.Item1, lc.Value.Item2, pc.X, pc.Y, 3, Rgba.Hex("#ffe14a"));
            }
            la = (pa.X, pa.Y); lb = (pb.X, pb.Y); lc = (pc.X, pc.Y);
        }
        for (int i = 0; i < r.Count; i += 2) Seg(i);
        Seg(r.Count - 1);
        if (lc != null)
        {
            var e = r.At(r.Dist[r.Count - 1], (cor[r.Count - 1].Lo + cor[r.Count - 1].Hi) / 2); var e0 = r.At(r.Dist[r.Count - 3], (cor[r.Count - 3].Lo + cor[r.Count - 3].Hi) / 2);
            Arrow(_top, e0.X, e0.Z, e.X, e.Z, 3, Rgba.Hex("#ffe14a"), 12);
        }
        if (hasNet)
            try
            {
                foreach (var alt in net.AltRoutes(0.0))
                {
                    (float X, float Y)? last = null;
                    for (int i = 0; i < alt.Points.Count; i += 2)
                    {
                        var p = alt.Points[i];
                        var s = ToScreen(p[0] - p[5] * p[3] / 2, p[2] + p[4] * p[3] / 2);
                        if (last != null) Dashed(_top, last.Value.X, last.Value.Y, s.X, s.Y, 2, Rgba.Hex("#ff9a20"), 6, 3);
                        last = s;
                    }
                }
            }
            catch (Exception) { }
        if (_aiStroke != null) Polyline(_top, _aiStroke, 1, 3, Rgba.Hex("#ff9f1a"));
    }

    void MeasureDraw(List<(float, float, string, Rgba)> labels)
    {
        var r = D.Road();
        if (r != null) DraftDraw(r);
        if (Measure == null || Measure.Count == 0) return;
        var pts = new List<(double X, double Z)>(Measure);
        if (!MeasureDone && _mouseWorld != null) pts.Add(_mouseWorld.Value);
        var scr = pts.Select(q => ToScreen(q.X, q.Z)).ToList();
        for (int i = 0; i + 1 < scr.Count; i++) Dashed(_top, scr[i].X, scr[i].Y, scr[i + 1].X, scr[i + 1].Y, 2, MeasureCol, 8, 4);
        for (int k = 0; k < Measure.Count; k++)
        {
            _top.Disc(scr[k].X, scr[k].Y, 6, Black); _top.Disc(scr[k].X, scr[k].Y, 5, MeasureCol);
            if (k == 0 || k == Measure.Count - 1) labels.Add((scr[k].X - 16, scr[k].Y - 10, k == 0 ? "A" : (k + 1).ToString(), MeasureCol));
        }
        if (pts.Count < 2) return;
        var lines = MeasureText(pts).Split('\n');
        var (mx, my) = scr[^1];
        double w = lines.Max(t => t.Length) * 7 + 12;
        _top.Rect(mx + 11, my - 7, mx + 13 + w, my + 16 * lines.Length + 5, MeasureCol); _top.Rect(mx + 12, my - 6, mx + 12 + w, my + 16 * lines.Length + 4, Rgba.Hex("#0b1a2e"));
        for (int k = 0; k < lines.Length; k++) labels.Add((mx + 18, my + 9 + k * 16, lines[k], k == 0 ? White : MeasureCol));
    }

    public string MeasureText(List<(double X, double Z)> pts)
    {
        double path = 0;
        for (int i = 0; i + 1 < pts.Count; i++) path += Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Z - pts[i].Z, 2));
        string text = $"path {path:0} m";
        var on = pts.Select(q => OnWhichRoad(q.X, q.Z)).ToList();
        var main = D.Road();
        if (on.All(o => o != null) && on.All(o => o!.Value.R == on[0]!.Value.R))
        {
            var road = on[0]!.Value.R; double drive = 0;
            for (int i = 0; i + 1 < on.Count; i++) { double d = on[i + 1]!.Value.S - on[i]!.Value.S; drive += road.Closed ? Py.Mod(d, road.Length) : Math.Abs(d); }
            text += $"\nalong the road {drive:0} m ({(road == main ? "main road" : "branch")}{(road.Closed ? ", driving direction" : "")})";
        }
        else if (on.Any(o => o != null)) text += "\n(all points on the same road = the driving distance too)";
        if (pts.Count > 2) text += $"\nfirst to last, straight {Math.Sqrt(Math.Pow(pts[^1].X - pts[0].X, 2) + Math.Pow(pts[^1].Z - pts[0].Z, 2)):0} m";
        return text;
    }

    (Road R, double S)? OnWhichRoad(double x, double z)
    {
        var r = D.Road();
        if (r == null) return null;
        if (HasNet) { var hit = BranchAt(x, z); if (hit != null) return (hit.Value.R, hit.Value.R.Project(x, z).S); }
        var (s, lat) = r.Project(x, z);
        return Math.Abs(lat) <= r.WAt(s) / 2 + 2 ? (r, s) : null;
    }

    (int I, Road R)? BranchAt(double x, double z)
    {
        var net = D.Net();
        if (net == null || net.Branches.Count == 0) return null;
        var areas = net.Areas();
        if (areas[net.Main].Depth(x, z) > 0) return null;
        for (int i = 0; i < net.Branches.Count; i++) if (areas[net.Branches[i].R].Depth(x, z) > 0) return (i, net.Branches[i].R);
        return null;
    }

    public List<(int Idx, double X, double Z, string Type)> Dots(Road r)
    {
        var info = D.Info()!;
        var (spots, own) = Layout.PickupSpots(r, info, D.Map);
        bool mine = D.Map["pickups"] is List<object?>;
        var names = World.PickupTypes.ToDictionary(kv => kv.Value, kv => kv.Key);
        var o = new List<(int, double, double, string)>(spots.Count);
        for (int i = 0; i < spots.Count; i++) o.Add((mine ? own[i] : -1, spots[i].X, spots[i].Z, names.GetValueOrDefault(spots[i].Type ?? "", "?")));
        return o;
    }

    // ---- hits ---------------------------------------------------------------------------------------------------
    int HitPoint(int sx, int sy)
    {
        int best = -1; double bd = 160;
        for (int k = 0; k < D.Points.Count; k++)
        {
            var p = Road.NormPoint(D.Points[k], 60);
            var (x, y) = ToScreen(p.X, p.Z);
            double d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
            if (d < bd) { bd = d; best = k; }
        }
        return best;
    }

    string? HitLine(int sx, int sy)
    {
        var r = D.Road();
        if (r == null) return null;
        var (si, fi) = GenAid.RaceLines(r, D.Map);
        foreach (var (i, kind) in new[] { (si, "start"), (fi, "finish") })
        {
            if (kind == "finish" && r.Closed) continue;
            var (x, y) = ToScreen(r.Centre[i].X, r.Centre[i].Z);
            if ((x - sx) * (x - sx) + (y - sy) * (y - sy) < 150) return kind;
        }
        return null;
    }

    int HitPickup(int sx, int sy)
    {
        var r = D.Road();
        if (r == null || D.Map["pickups"] == null) return -1;
        int best = -1; double bd = 100;
        foreach (var (idx, x, z, _) in Dots(r))
        {
            if (idx < 0) continue;
            var (px, py) = ToScreen(x, z);
            double d = (px - sx) * (px - sx) + (py - sy) * (py - sy);
            if (d < bd) { bd = d; best = idx; }
        }
        return best;
    }

    (string Kind, int I, int J)? NetHit(int sx, int sy)
    {
        (string, int, int)? hit = null; double best = 160;
        void Offer(double d2, (string, int, int) h) { if (d2 < best) { best = d2; hit = h; } }
        int bi = 0;
        foreach (var b in D.Map.Objs("branches"))
        {
            int pi = 0;
            foreach (var p in b.Objs("points")) { var (px, py) = ToScreen(p.Num("x"), p.Num("z")); Offer((px - sx) * (px - sx) + (py - sy) * (py - sy), ("bpt", bi, pi)); pi++; }
            bi++;
        }
        foreach (var (kind, i, x, z) in NetHandles()) { var (px, py) = ToScreen(x, z); Offer((px - sx) * (px - sx) + (py - sy) * (py - sy) + 20, (kind, i, 0)); }
        var net = D.Net(); var r = D.Road();
        if (HasNet && net != null && r != null)
        {
            int si = 0;
            foreach (var (sp, ds) in net.Splits)
            {
                foreach (var d in ds)
                    for (int q = 0; q < d.Count; q += 2) { var (px, py) = ToScreen(d.Centre[q].X, d.Centre[q].Z); Offer((px - sx) * (px - sx) + (py - sy) * (py - sy) + 10, ("split", si, 0)); }
                var (s0, s1) = Network.SplitSpan(r, sp);
                var (lx, lz, _) = r.At((s0 + s1) / 2, r.WAt((s0 + s1) / 2) / 2 + 14);
                var (ax, ay) = ToScreen(lx, lz);
                Offer(Math.Max(0.0, (ax + 25 - sx) * (ax + 25 - sx) / 9 + (ay - sy) * (ay - sy) - 40), ("split", si, 0));
                si++;
            }
        }
        if (hit != null) return hit;
        if (!HasNet) return null;
        var (wx, wz) = ToWorld(sx, sy);
        var bh = BranchAt(wx, wz);
        return bh != null ? ("branch", bh.Value.I, 0) : null;
    }

    public void NetClearSel() { SelBranch = SelBpt = SelSplit = -1; }

    void NetSelect((string Kind, int I, int J) h)
    {
        NetClearSel();
        if (h.Kind == "bpt") { SelBranch = h.I; SelBpt = h.J; }
        else if (h.Kind is "bfrom" or "bto" or "branch") SelBranch = h.I;
        else if (h.Kind is "sfrom" or "sto" or "split") SelSplit = h.I;
    }

    double SegDist(int i, double x, double z)
    {
        var a = Road.NormPoint(D.Points[i], 60); var b = Road.NormPoint(D.Points[i + 1], 60);
        double dx = b.X - a.X, dz = b.Z - a.Z, t = Math.Max(0.0, Math.Min(1.0, ((x - a.X) * dx + (z - a.Z) * dz) / Math.Max(dx * dx + dz * dz, 1e-9)));
        return Math.Pow(a.X + dx * t - x, 2) + Math.Pow(a.Z + dz * t - z, 2);
    }

    // ---- mouse ----------------------------------------------------------------------------------------------------
    void Commit() { D.Touched(); Edited?.Invoke(); G.Invalidate(); }
    void Light() { D.Touched(); G.Invalidate(); }
    void Chosen() { Selected?.Invoke(); G.Invalidate(); }

    bool Off => Mode is "3d" or "collision" or "land" or "light" or "car" or "skins";

    void PanStart(MouseEventArgs e) { _pan = (e.X, e.Y); _panStart = (Ox, Oz); }

    void Down(object? _, MouseEventArgs e)
    {
        if (Off) return;
        if (e.Button == MouseButtons.Middle) { PanStart(e); return; }
        if (e.Button == MouseButtons.Right)
        {
            if (Mode == "surface") { BrushAt(e, true); return; }
            if (Mode == "land") { LandAt(e, true); return; }
            if (Tool == "measure" && Measure != null) { Measure = null; Tell("Measure cleared (right click)"); G.Invalidate(); return; }
            PanStart(e); return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (Mode == "surface") { BrushAt(e, false); return; }
        if (Mode == "land") { LandAt(e, false); return; }
        if (Mode == "ai")
        {
            if (AiPen) { _aiStroke = new() { ToWorld(e.X, e.Y) }; }
            else if (Tool is "branch" or "split") { var (ax, az) = WorldXY(e); NetClick(Tool, ax, az); }
            else PanStart(e);
            return;
        }
        var (x, z) = WorldXY(e);
        if (Tool is "branch" or "split") { NetClick(Tool, x, z); return; }
        if (Tool == "measure") { var w = ToWorld(e.X, e.Y); MeasureClick(w.X, w.Z); return; }
        int k = HitPoint(e.X, e.Y);
        int pk = k < 0 ? HitPickup(e.X, e.Y) : -1;
        string? line = k < 0 && pk < 0 && Tool is "select" or "move" && k < 0 && pk < 0 ? HitLine(e.X, e.Y) : null;
        var nh = k < 0 && pk < 0 && line == null && Tool is "select" or "move" or "erase" && k < 0 && pk < 0 && line == null ? NetHit(e.X, e.Y) : null;
        if (nh != null)
        {
            Sel = SelPick = -1;
            NetSelect(nh.Value);
            if (Tool is "move" or "select" && nh.Value.Kind is not ("branch" or "split")) { D.Snapshot(); _drag = ("net:" + nh.Value.Kind, nh.Value.I, nh.Value.J); }
            else if (Tool == "erase")
            {
                if (nh.Value.Kind != "bpt") SelBpt = -1;
                NetDelete();
                return;
            }
            Chosen();
            return;
        }
        if (line != null) { D.Snapshot(); _drag = (line, 0, 0); Tell($"Drag the {line.ToUpperInvariant()} line along the road", C("#35e05a")); G.Invalidate(); return; }
        var pts = D.PointsAsObjects();
        switch (Tool)
        {
            case "select":
                NetClearSel();
                Sel = k; SelPick = k < 0 ? pk : -1;
                if (k < 0 && pk < 0) PanStart(e);
                Chosen();
                break;
            case "move":
                if (k >= 0 || pk >= 0) { D.Snapshot(); _drag = k >= 0 ? ("point", k, 0) : ("pick", pk, 0); Sel = k; SelPick = pk; Selected?.Invoke(); }
                else PanStart(e);
                break;
            case "add":
                D.Snapshot();
                if (_startHover && CanLap()) { _startHover = false; Sel = -1; CloseLap(); Commit(); return; }
                pts.Add(new JObj { ["x"] = x, ["z"] = z });
                Sel = pts.Count - 1;
                Commit();
                break;
            case "insert":
                var road = D.Road();
                bool nearMain = false;
                if (road != null) { var (s, lat) = road.Project(x, z); nearMain = Math.Abs(lat) <= road.WAt(s) / 2 + 30; }
                if ((SelBranch >= 0 || !nearMain) && NetInsert(x, z)) return;
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < pts.Count - 1; i++) { double q = SegDist(i, x, z); if (q < bd) { bd = q; best = i; } }
                D.Snapshot();
                pts.Insert(best + 1, new JObj { ["x"] = x, ["z"] = z });
                Sel = best + 1;
                Commit();
                break;
            case "row":
            case "single":
                PlacePickup(x, z);
                break;
            case "erase":
                if (k >= 0) { Sel = k; SelPick = -1; DeleteSelected(); }
                else if (pk >= 0) { Sel = -1; SelPick = pk; DeleteSelected(); }
                break;
        }
    }

    public List<object?> PickupItems()
    {
        if (D.Map["pickups"] is not List<object?> l)
        {
            var r = D.Road()!;
            l = Layout.PickupItems(r, D.Info()!, D.Map).Select(o => (object?)o).ToList();
            D.Map["pickups"] = l;
        }
        return l;
    }

    void PlacePickup(double x, double z)
    {
        var r = D.Road();
        if (r == null) return;
        var (s, lat) = r.Project(x, z);
        var onBranch = Tool == "row" ? BranchAt(x, z) : null;
        if (Tool == "row" && onBranch == null && Math.Abs(lat) > r.WAt(s) / 2 + 20) { Tell("Click ON the road (or a branch) to place a row"); return; }
        D.Snapshot();
        var items = PickupItems();
        if (Tool == "row" && onBranch != null)
        {
            var (bi, br) = onBranch.Value;
            var (bs, _) = br.Project(x, z); var (rx, rz, _) = br.At(bs);
            items.Add(new JObj { ["kind"] = "row", ["x"] = Py.RoundN(rx, 1), ["z"] = Py.RoundN(rz, 1), ["type"] = PType, ["bi"] = (long)bi });
        }
        else if (Tool == "row") { var (rx, rz, _) = r.At(s); items.Add(new JObj { ["kind"] = "row", ["x"] = Py.RoundN(rx, 1), ["z"] = Py.RoundN(rz, 1), ["type"] = PType }); }
        else items.Add(new JObj { ["kind"] = "single", ["x"] = Py.RoundN(x, 1), ["z"] = Py.RoundN(z, 1), ["type"] = PType });
        SelPick = items.Count - 1; Sel = -1;
        Commit();
    }

    // ---- branches + splits (designer_net.net_click ...)
    (double S, double Lat)? OnRoad(double x, double z, double extra = 10.0)
    {
        var r = D.Road();
        if (r == null) return null;
        var (s, lat) = r.Project(x, z);
        return Math.Abs(lat) <= r.WAt(s) / 2 + extra ? (s, lat) : null;
    }

    double SpanTo(double s0, double s1) { var r = D.Road()!; return r.Closed && s1 <= s0 ? s1 + r.Length : s1; }

    List<object?> Branches() { if (D.Map.Arr("branches") == null) D.Map["branches"] = new List<object?>(); return D.Map.Arr("branches")!; }
    List<object?> Splits() { if (D.Map.Arr("splits") == null) D.Map["splits"] = new List<object?>(); return D.Map.Arr("splits")!; }

    public void NetClick(string tool, double x, double z)
    {
        var r = D.Road();
        var hit = OnRoad(x, z);
        var d = _draft;
        if (r == null) return;
        if (tool == "branch")
        {
            if (d == null)
            {
                if (hit == null) { Tell("Branch: first click ON the road where the branch should leave it", C("#ffb020")); return; }
                _draft = new NetDraft { Kind = "branch", FromM = hit.Value.S };
                Tell("Branch: now click its points BESIDE the road, then click ON the road where it joins again (Esc = cancel)", C("#5cf0a0"));
            }
            else if (hit != null && d.Points.Count > 0 && Math.Abs(hit.Value.Lat) <= r.WAt(hit.Value.S) / 2)
            {
                double s1 = SpanTo(d.FromM, hit.Value.S);
                if (!r.Closed && s1 < d.FromM) { (d.FromM, s1) = (s1, d.FromM); d.Points.Reverse(); }
                if (s1 - d.FromM < Network.MinBranchGap) { Tell($"The branch must join at least {Network.MinBranchGap:0} m after it leaves", C("#ff6060")); return; }
                D.Snapshot();
                Branches().Add(new JObj { ["from_m"] = Py.RoundN(d.FromM, 1), ["to_m"] = Py.RoundN(s1, 1), ["width"] = Network.BranchWidth, ["points"] = d.Points.Select(p => (object?)p).ToList() });
                _draft = null;
                NetClearSel();
                SelBranch = Branches().Count - 1;
                Commit();
                Tell($"Branch {SelBranch + 1} made: {d.FromM:0} m to {s1:0} m along the road. Drag its points (Move tool), change its width on the right", C("#5cf0a0"));
                return;
            }
            else if (hit != null && Math.Abs(hit.Value.Lat) <= r.WAt(hit.Value.S) / 2) { Tell("Click at least one point BESIDE the road first (where the branch goes)", C("#ffb020")); return; }
            else
            {
                d.Points.Add(new JObj { ["x"] = Py.RoundN(x, 1), ["z"] = Py.RoundN(z, 1) });
                Tell($"Branch: {d.Points.Count} point(s) - more points, or click ON the road where it joins (Esc = cancel)", C("#5cf0a0"));
            }
            G.Invalidate();
        }
        else
        {
            if (hit == null) { Tell("Lane split: click ON the road", C("#ffb020")); return; }
            if (d == null)
            {
                _draft = new NetDraft { Kind = "split", FromM = hit.Value.S };
                Tell("Lane split: now click ON the road where it ends (Esc = cancel)", C("#ffb020"));
                G.Invalidate(); return;
            }
            double s0 = d.FromM, s1 = SpanTo(d.FromM, hit.Value.S);
            if (!r.Closed && s1 < s0) (s0, s1) = (s1, s0);
            if (s1 - s0 < Network.SplitMin) { Tell($"A lane split must be at least {Network.SplitMin:0} m long", C("#ff6060")); return; }
            D.Snapshot();
            Splits().Add(new JObj { ["from_m"] = Py.RoundN(s0, 1), ["to_m"] = Py.RoundN(s1, 1), ["lanes"] = 2L });
            _draft = null;
            NetClearSel();
            SelSplit = Splits().Count - 1;
            Commit();
            Tell($"Lane split {SelSplit + 1}: 2 lanes from {s0:0} m to {s1:0} m (3 lanes: on the right)", C("#ffb020"));
        }
    }

    bool NetInsert(double x, double z)
    {
        var r = D.Road();
        if (r == null) return false;
        var (sx, sy) = ToScreen(x, z);
        (double D2, int Bi, int K)? best = null;
        int bi = 0;
        foreach (var b in D.Map.Objs("branches"))
        {
            var (s0, s1) = Network.BranchSpan(r, b);
            var chain = new List<(double X, double Z)>();
            var e0 = r.At(s0); chain.Add((e0.X, e0.Z));
            foreach (var p in b.Objs("points")) chain.Add((p.Num("x"), p.Num("z")));
            var e1 = r.At(s1); chain.Add((e1.X, e1.Z));
            for (int k = 0; k < chain.Count - 1; k++)
            {
                var (ax, ay) = ToScreen(chain[k].X, chain[k].Z); var (bx, by) = ToScreen(chain[k + 1].X, chain[k + 1].Z);
                double dx = bx - ax, dy = by - ay, t = Math.Max(0.0, Math.Min(1.0, ((sx - ax) * dx + (sy - ay) * dy) / Math.Max(dx * dx + dy * dy, 1e-9)));
                double d2 = Math.Pow(ax + dx * t - sx, 2) + Math.Pow(ay + dy * t - sy, 2);
                if (best == null || d2 < best.Value.D2) best = (d2, bi, k);
            }
            bi++;
        }
        if (best == null || best.Value.D2 > 40 * 40) return false;
        D.Snapshot();
        var br = (JObj)Branches()[best.Value.Bi]!;
        br.Arr("points")!.Insert(best.Value.K, new JObj { ["x"] = Py.RoundN(x, 1), ["z"] = Py.RoundN(z, 1) });
        NetClearSel();
        SelBranch = best.Value.Bi; SelBpt = best.Value.K;
        Commit();
        Tell($"Point added to branch {SelBranch + 1} - drag it with the Move tool, Delete removes it", C("#5cf0a0"));
        return true;
    }

    void NetDrag(string kind, int i, int j, double x, double z)
    {
        var r = D.Road();
        if (r == null) return;
        if (kind == "bpt")
        {
            var p = (JObj)((JObj)Branches()[i]!).Arr("points")![j]!;
            p["x"] = Py.RoundN(x, 1); p["z"] = Py.RoundN(z, 1);
            return;
        }
        var (s, _) = r.Project(x, z);
        var b = (JObj)(kind is "bfrom" or "bto" ? Branches()[i] : Splits()[i])!;
        string key = kind is "bfrom" or "sfrom" ? "from_m" : "to_m";
        if (r.Closed)
        {
            double L = r.Length, f = Py.Mod(b.Num("from_m"), L), t = Py.Mod(b.Num("to_m"), L);
            if (key == "from_m") f = s; else t = s;
            b["from_m"] = Py.RoundN(f, 1); b["to_m"] = Py.RoundN(SpanTo(f, t), 1);
            return;
        }
        b[key] = Py.RoundN(s, 1);
    }

    public bool NetDelete()
    {
        var bs = D.Map.Arr("branches"); var ss = D.Map.Arr("splits");
        if (SelBranch >= 0 && bs != null && SelBranch < bs.Count)
        {
            D.Snapshot();
            var b = (JObj)bs[SelBranch]!;
            if (SelBpt >= 0 && b.Arr("points")!.Count > 1) { b.Arr("points")!.RemoveAt(SelBpt); Tell("Branch point deleted"); }
            else
            {
                int bi = SelBranch;
                bs.RemoveAt(bi);
                if (D.Map.Arr("pickups") is { Count: > 0 } items)
                {
                    var keep = new List<object?>();
                    foreach (var it0 in items)
                    {
                        var it = (JObj)it0!;
                        if (it.ContainsKey("bi") && it["bi"] != null && it.Int("bi") == bi) continue;
                        if (it.ContainsKey("bi") && it["bi"] != null && it.Int("bi") > bi) it["bi"] = it.Int("bi") - 1;
                        keep.Add(it);
                    }
                    items.Clear(); items.AddRange(keep);
                }
                Tell("Branch deleted");
            }
            NetClearSel(); Commit();
            return true;
        }
        if (SelSplit >= 0 && ss != null && SelSplit < ss.Count)
        {
            D.Snapshot(); ss.RemoveAt(SelSplit); NetClearSel(); Commit();
            Tell("Lane split deleted");
            return true;
        }
        return false;
    }

    public void NetDeleteWhole() { SelBpt = -1; NetDelete(); }

    public void NetAdj(string key, double d)
    {
        D.Snapshot();
        if (SelBranch >= 0)
        {
            var b = (JObj)Branches()[SelBranch]!;
            if (key == "h") { var p = (JObj)b.Arr("points")![SelBpt]!; p["h"] = Math.Max(0.0, p.Num("h") + d); }
            else if (key == "width") b["width"] = Math.Max(14.0, Math.Min(80.0, b.Num("width", Network.BranchWidth) + d));
            else b[key] = b.Num(key) + d;
        }
        else if (SelSplit >= 0) { var sp = (JObj)Splits()[SelSplit]!; sp[key] = sp.Num(key) + d; }
        Commit();
    }

    public void NetLanes(int k) { D.Snapshot(); ((JObj)Splits()[SelSplit]!)["lanes"] = (long)k; Commit(); }

    // ---- measure
    void MeasureClick(double x, double z)
    {
        if (Measure == null || MeasureDone) { Measure = new(); MeasureDone = false; }
        if (Measure.Count >= MeasureMax) { Tell($"Measure: {MeasureMax} points is the most - Enter = finish, Backspace = take one away", C("#ffd23b")); return; }
        Measure.Add((x, z));
        if (Measure.Count >= MeasureMax) MeasureDone = true;
        MeasureSay();
        G.Invalidate();
    }

    public bool MeasureFinish() { if (Measure is { Count: > 0 }) { MeasureDone = true; MeasureSay(); G.Invalidate(); return true; } return false; }

    bool MeasureBack()
    {
        if (Measure == null || Measure.Count == 0) return false;
        Measure.RemoveAt(Measure.Count - 1);
        MeasureDone = false;
        if (Measure.Count == 0) Measure = null;
        MeasureSay();
        G.Invalidate();
        return true;
    }

    void MeasureSay()
    {
        var col = C("#ff8af0");
        if (Measure == null || Measure.Count == 0) { Tell("Measure: click the first point", col); return; }
        if (Measure.Count == 1) Tell($"Measure: click more points (up to {MeasureMax})  -  Enter = finish, Backspace = undo a point, Esc = clear", col);
        else Tell($"Measure {Measure.Count} points: {MeasureText(Measure).Replace("\n", "   ")}{(MeasureDone ? "" : "   (Enter = finish)")}", col);
    }

    void BrushAt(MouseEventArgs e, bool erase)
    {
        D.Snapshot();
        _stroke = true; Erasing = erase;
        var (x, z) = ToWorld(e.X, e.Y);
        Dab(x, z);
        G.Invalidate();
    }

    void Move(object? _, MouseEventArgs e)
    {
        if (Off) return;
        _mouse = (e.X, e.Y);
        if (_stroke) { var (x, z) = ToWorld(e.X, e.Y); Dab(x, z); G.Invalidate(); return; }
        if (_landStroke) { var (x, z) = ToWorld(e.X, e.Y); LandDab(x, z); return; }
        if (_aiStroke != null)
        {
            var w = ToWorld(e.X, e.Y); var l = _aiStroke[^1];
            if (Math.Sqrt(Math.Pow(w.X - l.X, 2) + Math.Pow(w.Z - l.Z, 2)) >= 2.0) { _aiStroke.Add(w); G.Invalidate(); }
            return;
        }
        if (_drag != null)
        {
            var (kind, i, j) = _drag.Value;
            var (x, z) = WorldXY(e);
            if (kind.StartsWith("net:")) { NetDrag(kind[4..], i, j, x, z); Light(); return; }
            if (kind is "start" or "finish") { var r = D.Road(); if (r != null) { var q = ToWorld(e.X, e.Y); SetLine(kind, r.Project(q.X, q.Z).S); Light(); } return; }
            if (kind == "point")
            {
                var p = (JObj)D.PointsAsObjects()[i]!;
                p["x"] = x; p["z"] = z;
                if (LapDrag() >= 0)
                {
                    var p0 = (JObj)D.Points[0]!;
                    var (sx, sy) = ToScreen(p0.Num("x"), p0.Num("z"));
                    _lapSnap = Math.Sqrt(Math.Pow(e.X - sx, 2) + Math.Pow(e.Y - sy, 2)) <= LapSnapPx;
                }
            }
            else { var it = (JObj)D.Map.Arr("pickups")![i]!; it["x"] = Py.RoundN(x, 1); it["z"] = Py.RoundN(z, 1); }
            Light();
        }
        else if (_pan != null)
        {
            Ox = _panStart.Ox - (e.X - _pan.Value.X) / Scale; Oz = _panStart.Oz + (e.Y - _pan.Value.Y) / Scale;
            G.Invalidate();
        }
        else Hover(e);
    }

    void Hover(MouseEventArgs e)
    {
        var (x, z) = ToWorld(e.X, e.Y);
        if (Mode is "surface" or "land") { G.Invalidate(); return; }
        if (_draft != null) { G.Invalidate(); return; }
        if (Measure != null && !MeasureDone) { _mouseWorld = (x, z); G.Invalidate(); return; }
        bool hover = false;
        if (CanLap() && D.Points.Count > 0)
        {
            var p0 = Road.NormPoint(D.Points[0], 60); var (sx, sy) = ToScreen(p0.X, p0.Z);
            hover = Math.Sqrt(Math.Pow(e.X - sx, 2) + Math.Pow(e.Y - sy, 2)) <= LapSnapPx;
        }
        if (hover != _startHover) { _startHover = hover; G.Invalidate(); }
        var r = D.Road();
        if (hover) Tell(Tool == "add" ? "Click = lap track (the road closes back to its start)" : "Drag the END point (white) onto this start point = lap track (the end joins the start)", C("#35e05a"));
        else if (r != null) { var (s, lat) = r.Project(x, z); Tell($"x {x:0}  z {z:0}   |   {s:0} m along the road, {lat:0} m from its middle, height {r.HAt(s):0.0} m   |   {ToolInfo[Tool].Label}", C("#9aa8b8")); }
    }

    void SetLine(string kind, double s)
    {
        var m = D.Map; var r = D.Road()!;
        if (r.Closed)
        {
            s = Py.Round(Py.Mod(s, r.Length) / Road.Step) * Road.Step;
            m["start_m"] = s; m.Remove("finish_m");
            Tell($"START / FINISH at {s:0} m of the {r.Length:0} m loop", C("#35e05a"));
            return;
        }
        m[kind + "_m"] = Py.Round(s / Road.Step) * Road.Step;
        var (si, fi) = GenAid.RaceLines(r, m);
        m["start_m"] = r.Dist[si]; m["finish_m"] = r.Dist[fi];
        if (m["pickups"] == null) Tell($"START at {m.Num("start_m"):0} m, FINISH at {m.Num("finish_m"):0} m (race {m.Num("finish_m") - m.Num("start_m"):0} m); the automatic power-ups follow", C("#35e05a"));
    }

    public void MoveLine(string kind, double delta)
    {
        var r = D.Road();
        if (r == null) return;
        D.Snapshot();
        var (si, fi) = GenAid.RaceLines(r, D.Map);
        SetLine(kind, r.Dist[kind == "start" ? si : fi] + delta);
        Commit();
    }

    void Up(object? _, MouseEventArgs e)
    {
        if (Off) return;
        if (_landStroke) { LandUp(); return; }
        if (_stroke)
        {
            bool wasBumps = BrushKey == "bumps";
            _stroke = false; Erasing = false; Commit();
            int nb = D.Map.Obj("surface")?.Obj("bumps")?.Count ?? 0;
            if (wasBumps) { Tell($"Bumps on {nb} cells of {Surface.CellM:0} m ({nb * Surface.CellM * Surface.CellM:0} m2) - Build + install to drive on them", C("#58d06a")); return; }
            int n = Surface.Settings(D.Map).Paint.Count;
            Tell($"Painted {n} cells of {Surface.CellM:0} m ({n * Surface.CellM * Surface.CellM:0} m2) - Build + install to drive on it", C("#58d06a"));
            return;
        }
        if (_aiStroke != null)
        {
            var st = _aiStroke; _aiStroke = null;
            var r = D.Road();
            if (r == null || st.Count < 3) { G.Invalidate(); return; }
            D.Snapshot();
            MergeAiStroke(r, st);
            Commit();
            Tell("AI path drawn (this part replaced) - Ctrl+Z undoes, Auto AI path = back to automatic");
            return;
        }
        if (_drag != null)
        {
            if (LapDrag() >= 0 && _lapSnap) { D.PointsAsObjects().RemoveAt(D.Points.Count - 1); Sel = -1; CloseLap(); }
            _lapSnap = false;
            _drag = null;
            Commit();
        }
        _pan = null;
    }

    bool CanLap() => Mode == "2d" && !D.Map.Truthy("laps") && D.Points.Count >= (Tool == "add" ? 3 : 4);

    void CloseLap()
    {
        var m = D.Map;
        m["laps"] = m.Int("laps_last", 3);
        m.Remove("finish_m"); m.Remove("start_m");
        Tell($"Lap track: the end now joins the start, {m.Int("laps")} laps (change with Laps - / +)", C("#35e05a"));
    }

    int LapDrag() => _drag == null || _drag.Value.Kind != "point" || !CanLap() ? -1 : _drag.Value.I == D.Points.Count - 1 ? _drag.Value.I : -1;

    void MergeAiStroke(Road r, List<(double X, double Z)> stroke, double blend = 25.0)
    {
        var old = Layout.AiProfile(r, D.Map) ?? Enumerable.Repeat(0.0, r.Count).ToList();
        var st = Layout.ProjectSeq(r, stroke).OrderBy(q => q.S).ToList();
        double s0 = st[0].S, s1 = st[^1].S;
        var nw = new List<double>(r.Count);
        double Interp(double s)
        {
            int k = st.FindIndex(q => q.S >= s);
            if (k <= 0) return st[0].Lat;
            var (a, la) = st[k - 1]; var (b, lb) = st[k];
            return la + (lb - la) * (s - a) / Math.Max(b - a, 1e-9);
        }
        for (int i = 0; i < r.Count; i++)
        {
            double s = r.Dist[i];
            if (s0 <= s && s <= s1) nw.Add(Interp(s));
            else if (s0 - blend < s && s < s0) { double t = (s0 - s) / blend; nw.Add(st[0].Lat * (1 - t) + old[i] * t); }
            else if (s1 < s && s < s1 + blend) { double t = (s - s1) / blend; nw.Add(st[^1].Lat * (1 - t) + old[i] * t); }
            else nw.Add(old[i]);
        }
        SetAi(Layout.AiLinePoints(r, nw));
    }

    public void SetAi(List<object?> pts) => D.Map["ai_path"] = new JObj { ["mode"] = "drawn", ["lane"] = AiLane, ["points"] = pts };

    void Wheel(object? _, MouseEventArgs e)
    {
        if (Off) return;
        double f = e.Delta > 0 ? 1.2 : 1 / 1.2;
        var (wx, wz) = ToWorld(e.X, e.Y);
        Scale *= f;
        Ox = wx - e.X / Scale; Oz = wz + e.Y / Scale;
        G.Invalidate();
    }

    // ---- edits used by the keys / panels ------------------------------------------------------------------------
    public void DeleteSelected()
    {
        if (Tool == "measure" && MeasureBack()) return;
        var pts = D.Points;
        if (Sel < 0 && SelPick < 0 && NetDelete()) return;
        if (Sel >= 0)
        {
            if (pts.Count <= 2) { Tell("A road needs at least 2 points"); return; }
            D.Snapshot(); pts.RemoveAt(Sel); Sel = -1; Commit();
        }
        else if (SelPick >= 0 && D.Map.Arr("pickups") is { Count: > 0 } l) { D.Snapshot(); l.RemoveAt(SelPick); SelPick = -1; Commit(); }
    }

    public void Escape()
    {
        if (_draft != null) { _draft = null; Tell("Cancelled"); G.Invalidate(); return; }
        if (Measure != null) { Measure = null; Tell("Measure cleared"); G.Invalidate(); return; }
        Sel = SelPick = -1; NetClearSel(); Selected?.Invoke(); G.Invalidate();
    }

    public void ToolChanged()
    {
        _draft = null;
        if (Tool != "measure") Measure = null;
    }

    JObj? PointObj() => Sel < 0 || Sel >= D.Points.Count ? null : (JObj)D.PointsAsObjects()[Sel]!;

    public void Adj(string key, double delta)
    {
        var p = PointObj();
        if (p == null) { Tell("Select a point first (S, then click a point)"); return; }
        D.Snapshot();
        double Or(string k, double d) => p.Truthy(k) ? p.Num(k) : d;
        if (key == "w") p["w"] = Math.Max(30.0, Math.Min(200.0, Or("w", D.Map.Num("width", 60.0)) + delta));
        else if (key == "h") p["h"] = Math.Max(0.0, Math.Min(30.0, Or("h", 0.0) + delta));
        else if (key == "tilt")
        {
            double v = Math.Max(-Road.MaxTilt, Math.Min(Road.MaxTilt, Or("tilt", 0.0) + delta));
            if (v == 0) p.Remove("tilt"); else p["tilt"] = v;
            Commit();
            Tell($"Point {Sel + 1}: {TiltText(v)}");
            return;
        }
        else
        {
            double v = Or("corner", 0.0) + delta;
            p["corner"] = v < Road.MinCorner - 0.1 ? 0.0 : Math.Min(v, 300.0);
            if (delta > 0 && !p.Truthy("corner")) p["corner"] = Road.MinCorner;
        }
        Commit();
    }

    public static string TiltText(double deg) => Math.Abs(deg) < 0.05 ? "flat" : $"{(deg > 0 ? "left" : "right")} side up {Math.Abs(deg):0.#}°";

    public static string TiltShort(double deg) => Math.Abs(deg) < 0.05 ? "flat" : $"{Math.Abs(deg):0.#}° {(deg > 0 ? "left" : "right")}";

    /// a good tilt for the bend at the selected point: the outside of the bend goes up
    public void AutoTilt()
    {
        var p = PointObj(); var r = D.Road();
        if (p == null || r == null) { Tell("Select a point first"); return; }
        var (s, _) = r.Project(p.Num("x"), p.Num("z"));
        int i = r.IndexAt(s), n = r.Count, a = r.Closed ? (int)Py.Mod(i - 6, n) : Math.Max(i - 6, 0), b = r.Closed ? (i + 6) % n : Math.Min(i + 6, n - 1);
        double da = Py.Mod(r.Heading[b] - r.Heading[a] + Math.PI, 2 * Math.PI) - Math.PI;
        double len = Math.Max(1, (r.Closed ? Py.Mod(b - a, n) : b - a) * Road.Step), radius = Math.Abs(da) > 1e-4 ? len / Math.Abs(da) : 1e9;
        double deg = radius > 800 ? 0.0 : Math.Round(Math.Max(3.0, Math.Min(12.0, 1500.0 / radius))) * Math.Sign(da);
        D.Snapshot();
        if (deg == 0) p.Remove("tilt"); else p["tilt"] = deg;
        Commit();
        Tell(deg == 0 ? $"Point {Sel + 1}: the road is nearly straight here, so it stays flat" : $"Point {Sel + 1}: bend of radius {radius:0} m, {TiltText(deg)}");
    }

    public void ToggleCorner()
    {
        var p = PointObj();
        if (p == null) { Tell("Select a point first"); return; }
        D.Snapshot();
        p["corner"] = p.Truthy("corner") ? 0.0 : 40.0;
        Commit();
    }

    public void Turn90(int side)
    {
        var pts = D.PointsAsObjects();
        int k = Sel;
        if (k <= 0) { Tell("Select a point (not the first one) for a 90-degree turn"); return; }
        var p = (JObj)pts[k]!; var a = (JObj)pts[k - 1]!;
        double px = p.Num("x"), pz = p.Num("z"), ux = px - a.Num("x"), uz = pz - a.Num("z");
        if (Math.Sqrt(ux * ux + uz * uz) < 1e-6) { Tell("This point sits on the one before it - move it first"); return; }
        var road = D.Road();
        D.Snapshot();
        double lx = -uz * side, lz = ux * side;
        if (k + 1 < pts.Count)
        {
            var b = (JObj)pts[k + 1]!;
            double dx0 = b.Num("x") - px, dz0 = b.Num("z") - pz;
            double ang = Math.Atan2(lx * dz0 - lz * dx0, lx * dx0 + lz * dz0), ca = Math.Cos(-ang), sa = Math.Sin(-ang);
            (double, double) T(double x, double z) { double dx = x - px, dz = z - pz; return (px + dx * ca - dz * sa, pz + dx * sa + dz * ca); }
            double? sk = road?.Project(px, pz).S;
            for (int i = k + 1; i < pts.Count; i++) { var q = (JObj)pts[i]!; var (nx, nz) = T(q.Num("x"), q.Num("z")); q["x"] = nx; q["z"] = nz; }
            if (sk != null)
            {
                foreach (var it in D.Map.Objs("pickups"))
                    if (road!.Project(it.Num("x"), it.Num("z")).S > sk + 1) { var (nx, nz) = T(it.Num("x"), it.Num("z")); it["x"] = Py.RoundN(nx, 1); it["z"] = Py.RoundN(nz, 1); }
                if (D.Map.Obj("ai_path")?.Arr("points") is { } ai)
                    foreach (var q0 in ai)
                    {
                        var q = (List<object?>)q0!;
                        if (road!.Project(Json.D(q[0]), Json.D(q[1])).S > sk + 1) { var (nx, nz) = T(Json.D(q[0]), Json.D(q[1])); q[0] = nx; q[1] = nz; }
                    }
            }
        }
        else
        {
            double lu = Math.Sqrt(lx * lx + lz * lz);
            pts.Add(new JObj { ["x"] = px + lx / lu * 300, ["z"] = pz + lz / lu * 300 });
        }
        if (!p.Truthy("corner")) p["corner"] = 40.0;
        Commit();
        Tell($"90-degree {(side > 0 ? "LEFT" : "RIGHT")} turn at point {k + 1}");
    }

    public void Reverse()
    {
        D.Snapshot();
        var m = D.Map; var r = D.Road();
        if (r != null && (m["start_m"] != null || m["finish_m"] != null))
        {
            var (si, fi) = GenAid.RaceLines(r, m);
            m["start_m"] = r.Length - r.Dist[fi]; m["finish_m"] = r.Length - r.Dist[si];
        }
        FlipTilts();
        D.Points.Reverse();
        Sel = -1;
        Commit();
        Tell("Direction reversed (start <-> finish)");
    }

    public void Mirror()
    {
        D.Snapshot();
        var pts = D.PointsAsObjects();
        double cz = pts.Sum(p => ((JObj)p!).Num("z")) / pts.Count;
        FlipTilts();
        foreach (var p in pts.OfType<JObj>()) p["z"] = 2 * cz - p.Num("z");
        foreach (var it in D.Map.Objs("pickups")) it["z"] = 2 * cz - it.Num("z");
        foreach (var o in D.Map.Objs("objects")) { o["z"] = 2 * cz - o.Num("z"); o["turn"] = Math.PI - o.Num("turn"); }
        Commit();
        Tell("Mirrored");
    }

    /// left and right swap when the road runs the other way or is mirrored
    void FlipTilts() { foreach (var p in D.Points.OfType<JObj>()) if (p.Truthy("tilt")) p["tilt"] = -p.Num("tilt"); }

    public void Nudge(double dx, double dz)
    {
        var p = PointObj();
        if (p == null) return;
        D.Snapshot(); p["x"] = p.Num("x") + dx; p["z"] = p.Num("z") + dz; Commit();
    }

    public void SetType(string t)
    {
        PType = t;
        var items = D.Map.Arr("pickups");
        if (SelPick >= 0 && items != null && SelPick < items.Count)
        {
            D.Snapshot(); ((JObj)items[SelPick]!)["type"] = t; Commit();
            Tell($"Power-up type: {t}");
        }
        else Tell($"New power-ups: {t}");
    }

    public void MixedRow()
    {
        if (SelPick >= 0 && D.Map.Arr("pickups") is { Count: > 0 } items) { D.Snapshot(); ((JObj)items[SelPick]!).Remove("type"); Commit(); }
    }

    public void RowN(int d)
    {
        if (SelPick < 0 || D.Map.Arr("pickups") is not { Count: > 0 } items) return;
        D.Snapshot();
        var it = (JObj)items[SelPick]!;
        it["n"] = (long)Math.Max(1, Math.Min(9, Layout.RowSize(D.Map, it) + d));
        Commit();
    }

    public void ResetSelection() { Sel = SelPick = -1; NetClearSel(); _draft = null; }
}

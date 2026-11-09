// The 3D + Collision tabs: the same layout code as the build, drawn with OpenGL. Follows view3d.py: scene, camera, selection, handles, panel.
// View3DPanels.cs holds the side bars (library, outliner, scatter).
using Bmb.Core;

namespace Bmb.App;

public sealed class Pick
{
    public string Key = "", Label = ""; public double[] Pos = new double[3]; public double Turn; public double[] Scale = { 1, 1, 1 }; public double[] Box = new double[6]; public bool Editable = true, Scalable = true;
    public double Rx, Rz; public int Point = -1, Pickup = -1; public bool Row, Multi; public string? PType; public double Width, Tilt, Lift; public Rgba Col;
}

/// triangles with a colour each (shaded by the sun when added) + lines; each owner's ranges are kept (Solo)
sealed class Mesh
{
    public readonly Batch V = new(), T = new(), L = new(), Shadow = new(), ShadowTex = new();
    public readonly Dictionary<string, List<(Batch B, int First, int N)>> Ranges = new();
    public double[] CurRot = { 0, 0 };
    string? _owner; readonly Dictionary<Batch, int> _start = new();
    public const double SunX = 0.6, SunY = 0.6, SunZ = 0.45;       // a low sun: a tilted road catches it differently, and shadows reach out
    static readonly double Sl = Math.Sqrt(SunX * SunX + SunY * SunY + SunZ * SunZ);

    public void Owner(string? key)
    {
        if (_owner != null)
            foreach (var b in new[] { V, T, L })
            {
                int s = _start.GetValueOrDefault(b, 0);
                if (b.N > s) { if (!Ranges.TryGetValue(_owner, out var l)) Ranges[_owner] = l = new(); l.Add((b, s, b.N - s)); }
            }
        _owner = key;
        foreach (var b in new[] { V, T, L }) _start[b] = b.N;
    }

    public void Tri(double[] a, double[] b, double[] c, double[] col, double alpha = 1, bool shade = true)
    {
        double k = 1;
        if (shade)
        {
            double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, ln = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (ln > 1e-9)
            {
                if (ny < 0) { nx = -nx; ny = -ny; nz = -nz; }                  // triangles come wound either way
                double d = (nx * SunX + ny * SunY + nz * SunZ) / Sl / ln;
                k = 0.34 + 0.86 * (ny / ln < 0.1 ? Math.Abs(d) : Math.Max(0, d));
            }
        }
        var q = new Rgba((float)(col[0] * k), (float)(col[1] * k), (float)(col[2] * k), (float)alpha);
        var t = alpha >= 1 ? V : T;
        t.Add((float)a[0], (float)a[1], (float)a[2], q); t.Add((float)b[0], (float)b[1], (float)b[2], q); t.Add((float)c[0], (float)c[1], (float)c[2], q);
    }

    public void Quad(double[] a, double[] b, double[] c, double[] d, double[] col, double alpha = 1, bool shade = true) { Tri(a, b, c, col, alpha, shade); Tri(a, c, d, col, alpha, shade); }

    public void Line(double[] a, double[] b, double[] col, double alpha = 1)
    {
        var q = new Rgba((float)col[0], (float)col[1], (float)col[2], (float)alpha);
        L.Add((float)a[0], (float)a[1], (float)a[2], q); L.Add((float)b[0], (float)b[1], (float)b[2], q);
    }

    /// box (sx, sy, sz), placed like an actor; origin null = centre of the bottom face, or (x0, x1, z0, z1) in model space
    public void Box(double[] c, double[] size, double turn, double[] col, double lift = 0, double[]? origin = null, double[]? k = null)
    {
        var A = Layout.ActorAxes3(turn, CurRot[0], CurRot[1]);
        double x0 = origin?[0] ?? -size[0] / 2, x1 = origin?[1] ?? size[0] / 2, z0 = origin?[2] ?? -size[2] / 2, z1 = origin?[3] ?? size[2] / 2;
        double kx = k?[0] ?? 1, ky = k?[1] ?? 1, kz = k?[2] ?? 1;
        x0 *= kx; x1 *= kx; z0 *= kz; z1 *= kz;
        double y0 = lift * ky, y1 = (lift + size[1]) * ky;
        double[] W(double lx, double ly, double lz) => new[] { c[0] + lx * A[0][0] + ly * A[1][0] + lz * A[2][0], c[1] + lx * A[0][1] + ly * A[1][1] + lz * A[2][1], c[2] + lx * A[0][2] + ly * A[1][2] + lz * A[2][2] };
        var p = new double[8][]; int i = 0;
        foreach (double y in new[] { y0, y1 }) foreach (double z in new[] { z0, z1 }) foreach (double x in new[] { x0, x1 }) p[i++] = W(x, y, z);
        foreach (var f in new[] { new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } }) Quad(p[f[0]], p[f[1]], p[f[2]], p[f[3]], col);
    }
}

public sealed class TexDraw { public string Key = ""; public Pic Image = null!; public float[] V = null!; public string Owner = ""; }

public sealed partial class View3D
{
    readonly MainForm _f; readonly Doc D; public readonly GlControl G;
    public double Yaw = -0.6, Pitch = 0.55, Dist = 2500; public double[] Target = { 0, 0, 0 };
    public bool Ortho; public string SceneMode = "3d"; public bool Active;
    public double? DriveT; public (DateTime T, List<(double[] Eye, double[] Q, double Fov)> F)? CamAnim;
    public string? Sel; public List<string> MultiSel = new(); public string? SoloKey; public HashSet<string> SoloSet = new();
    public string Gizmo = "move";
    public sealed class Op { public string Kind = ""; public string? Axis; public (int X, int Y) M0; public (double X, double Y)? Cen; public double[]? Hit0; public double[] Delta = { 0, 0, 0 }; public double DTurn; public double[] DScale = { 1, 1, 1 }; public double[] DRot = { 0, 0 }; public bool Drag; public double[] Centre = new double[3]; public double Len = 10; public double Turn0; public double[][] Own = null!; }
    public Op? Oper;
    Mesh? _mesh; public List<Pick> Picks = new(); public double Ground = Fe.GroundY; public int TileFree = 144; public List<JObj> PickupList = new(); public string Stats = ""; public int Tris; string? _sceneError;
    readonly List<(double X, double Y, double Z, double A)> _path = new();
    (double X0, double X1, double Z0, double Z1) _bounds = (-500, 500, -500, 500);
    List<TexDraw> _textured = new();
    bool _dirty = true, _placed; uint _list; bool _hasList;
    readonly Dictionary<string, uint> _tex = new();
    public bool RealShapes; string? _hot;
    double[] _fwd = new double[16], _inv = new double[16]; bool _mats; int _w = 1, _h = 1;
    (string Mode, int X, int Y)? _drag; double[]? _pivot;
    bool _shiftDown, _ctrlDown;
    public event Action? Edited;
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 16 };

    static readonly Dictionary<string, double[]> KindRgb = new() { ["scenery"] = new[] { .6, .66, .75 }, ["breakable"] = new[] { 1, .72, .3 }, ["movable"] = new[] { .35, .8, 1 }, ["animated"] = new[] { .8, .5, 1 }, ["import"] = new[] { .35, .95, .55 }, ["ramp"] = new[] { 1, .45, .2 } };
    static readonly Dictionary<string, double[]> TypeRgb = new() { ["Shunt"] = new[] { 1, .25, .25 }, ["Barge"] = new[] { 1, .62, .1 }, ["Shock"] = new[] { .23, .63, 1 }, ["Mine"] = new[] { 1, .88, .29 }, ["Bolt"] = new[] { .69, .44, 1 }, ["Nitro"] = new[] { .25, .88, 1 }, ["Shield"] = new[] { .25, 1, .44 }, ["Repair"] = new[] { 1.0, 1, 1 } };
    static readonly Dictionary<string, string> ObjectLabels = new()
    {
        ["tanker"] = "Tanker ship (LA docks, 333 m) - look only", ["cruise"] = "Cruise ship (263 m) - look only", ["crane"] = "Gantry crane (132 m) - look only", ["ferris"] = "Ferris wheel - look only", ["radio"] = "Radio mast - look only",
        ["chevron"] = "Chevron panel (bend arrows) - look only", ["line"] = "Start / finish strip (fixed: part of the road)", ["linebox"] = "Container at the line (fixed)", ["block"] = "Road-block container (fixed: obstacles)",
        ["lamp"] = "Lamp post - can be knocked down", ["barrier"] = "Concrete barrier - can be knocked down", ["billboard"] = "Billboard - can be knocked down", ["flashing"] = "Flashing sign - can be knocked down",
        ["light"] = "Standing light - can be knocked down", ["barrel"] = "Barrel - can be knocked down", ["rwbarrier"] = "Road-works barrier - can be knocked down", ["cone"] = "Cone - can be knocked down",
    };
    static readonly Dictionary<string, (double[] V, Rgba C)> AxesInfo = new() { ["x"] = (new[] { 1.0, 0, 0 }, new Rgba(.95f, .25f, .25f)), ["y"] = (new[] { 0, 0, 1.0 }, new Rgba(.4f, .85f, .3f)), ["z"] = (new[] { 0, 1.0, 0 }, new Rgba(.3f, .5f, 1f)) };

    public View3D(MainForm f, Doc d, GlControl g)
    {
        _f = f; D = d; G = g;
        g.MouseDown += Down; g.MouseMove += Move; g.MouseUp += Up; g.MouseWheel += Wheel;
        _anim.Tick += (_, _) => { if (DriveT != null || CamAnim != null) G.Invalidate(); else _anim.Stop(); };
        Previews.Arrived += () => { if (RealShapes && Active && _f.IsHandleCreated) _f.BeginInvoke(ShapesArrived); };
        RealShapes = Setting("real_shapes");
    }

    DateTime _shapeAt;
    void ShapesArrived()
    {
        if ((DateTime.UtcNow - _shapeAt).TotalMilliseconds < 500) { if (!Previews.Waiting) Dirty(); return; }
        _shapeAt = DateTime.UtcNow;
        Dirty();
    }

    static string SettingFile => Path.Combine(Paths.Data, "view3d.json");
    static bool Setting(string n) { try { return ((JObj)Json.Load(SettingFile)!).Truthy(n); } catch (Exception) { return false; } }
    static void SettingSet(string n, bool v) { JObj o; try { o = (JObj)Json.Load(SettingFile)!; } catch (Exception) { o = new JObj(); } o[n] = v; try { Directory.CreateDirectory(Paths.Data); File.WriteAllText(SettingFile, Json.Dumps(o, -1)); } catch (IOException) { } }

    public void SetMode(string m)
    {
        bool on = m is "3d" or "collision" or "land";
        Active = on;
        if (on) { if (SceneMode != m) _dirty = true; SceneMode = m; }
        else { Oper = null; DriveT = null; CamAnim = null; }
        ShowPanels(on, m);
        if (on) { Dirty(); Ensure(); if (!_placed) { Reset(); _placed = true; } CamInfo(); }
        else { PanelShow(null); HoverHide(); }
    }

    public void Dirty() { _dirty = true; _landDirty = true; G.Invalidate(); }

    public void Ensure() { if (_dirty) Rebuild(); }

    // ---- the scene (view3d.build_scene / build_collision)
    static double[] V(double x, double y, double z) => new[] { x, y, z };

    void PickAdd(string key, double[] pos, double turn, double[] box, double[] k, bool scalable = true, double rx = 0, double rz = 0, string? label = null)
    {
        string kind = key.Split(':')[0];
        Picks.Add(new Pick { Key = key, Label = label ?? ObjectLabels.GetValueOrDefault(kind, kind), Pos = pos, Turn = turn, Scale = k, Box = box, Editable = !Layout.IsFixed(key), Scalable = scalable, Rx = rx, Rz = rz });
    }

    void Rebuild()
    {
        _dirty = false;
        Picks = new(); _textured = new(); _path.Clear(); _sceneError = null;
        if (_hiddenOn != (D.Li, D.Mi)) { ViewHidden.Clear(); _hiddenOn = (D.Li, D.Mi); }
        var M = new Mesh();
        var r = D.Road();
        if (r == null) { _sceneError = "cannot show this road"; _mesh = M; Stats = _sceneError; Compile(M); AfterScene(); return; }
        try { if (SceneMode == "collision") BuildCollision(M, r); else BuildScene(M, r); }
        catch (Exception e) { _sceneError = e.Message; Stats = "cannot show this road: " + e.Message; }
        M.Owner(null);
        _mesh = M;
        Tris = (M.V.N + M.T.N) / 3;
        Compile(M);
        AfterScene();
    }

    void BuildCollision(Mesh M, Road road)
    {
        var m = D.Map; var info = D.Info()!;
        var (pieces, decks) = Layout.CollisionPieces(road, info, m);
        double g = Fe.GroundY;
        var (x0, x1, z0, z1) = road.Bounds(1500);
        M.Quad(V(x0, g, z0), V(x1, g, z0), V(x1, g, z1), V(x0, g, z1), V(.1, .35, .15), shade: false);
        for (double gx = Math.Floor(x0 / 100) * 100; gx < x1; gx += 100) M.Line(V(gx, g + .02, z0), V(gx, g + .02, z1), V(.15, .45, .2));
        for (double gz = Math.Floor(z0 / 100) * 100; gz < z1; gz += 100) M.Line(V(x0, g + .02, gz), V(x1, g + .02, gz), V(.15, .45, .2));
        foreach (int side in new[] { -1, 1 }) { var e = road.Edge(side); for (int i = 0; i + 1 < road.Count; i++) M.Line(V(e[i].X, g + .05, e[i].Z), V(e[i + 1].X, g + .05, e[i + 1].Z), V(.6, .7, .65)); }
        int walls = 0, boxes = 0;
        foreach (var p in pieces)
        {
            if (p.Corners.Count == 8) boxes++; else walls++;
            foreach (var f in p.Faces)
            {
                double[] a = p.Corners[f.A], b = p.Corners[f.B], c = p.Corners[f.C];
                M.Tri(a, b, c, V(.95, .18, .15), 0.55);
                double[] ctr = V((a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3, (a[2] + b[2] + c[2]) / 3);
                double ln = Math.Sqrt(f.Nx * f.Nx + f.Ny * f.Ny + f.Nz * f.Nz);
                if (ln > 0 && Math.Abs(f.Ny) < 0.5) M.Line(ctr, V(ctr[0] + f.Nx / ln * 1.5, ctr[1] + f.Ny / ln * 1.5, ctr[2] + f.Nz / ln * 1.5), V(1, 1, 1));
            }
        }
        foreach (var p in decks) foreach (var f in p.Faces) M.Tri(p.Corners[f.A], p.Corners[f.B], p.Corners[f.C], V(1, .85, .15), 0.8);
        for (int i = 0; i < road.Count; i += 2) _path.Add((road.Centre[i].X, g + road.Height[i], road.Centre[i].Z, road.Heading[i]));
        _bounds = road.Bounds(0);
        Ground = g;
        Stats = $"COLLISION of {m.Str("name") ?? "map"}: green = drivable ground, red = solid ({walls} wall pieces, {boxes} boxes; white ticks = the side that pushes cars away), yellow = bridge decks ({decks.Count})";
    }

    void AddChunk(Mesh M, Chunk c, double[] col)
    {
        var st = c.Strip; var v = c.Verts;
        for (int i = 0; i + 2 < st.Count; i++)
        {
            int a0 = st[i], a1 = st[i + 1], a2 = st[i + 2];
            if (a0 == a1 || a1 == a2 || a0 == a2) continue;
            double[] P(int q) => V(v[q][0] + c.Anchor[0], v[q][1] + c.Anchor[1], v[q][2] + c.Anchor[2]);
            M.Tri(P(a0), P(a1), P(a2), col);
        }
    }

    void BuildScene(Mesh M, Road road)
    {
        var m = D.Map; var info = D.Info()!; double g = Fe.GroundY;
        var (tiles, tileKeys) = Layout.TileSpotsK(road, info, m);
        TileFree = Layout.TileFree;
        var (lamps, lampKeys) = Layout.LampSpotsK(road, info, m);
        var (pickups, owner) = Layout.PickupSpots(road, info, m);
        PickupList = Layout.PickupItems(road, info, m).Select(Share.Copy).ToList();
        int n = road.Count, placedN = 0;
        var drawn = new HashSet<string>();
        var objs = m.Arr("objects") ?? new();
        void PlacedBox(string key, double[] pos, double turn, double[] k, double rx, double rz, bool scalable)
        {
            var cat = Layout.Catalog();
            var e = cat.GetValueOrDefault(((JObj)objs[int.Parse(key[4..])]!).Str("id") ?? "") ?? new JObj();
            var bx = Layout.ObjectBoxOf(m, key);
            if (bx == null || !drawn.Add(key)) return;
            M.CurRot = new[] { rx, rz };
            List<PreviewMat>? mats = null;
            if (e.Str("kind") == "import") mats = Previews.Imported(e.Str("model")!);
            else if (RealShapes && e.Truthy("model")) mats = Previews.Get(e.Str("model")!);
            if (mats is { Count: > 0 })
            {
                var A = Layout.ActorAxes3(turn, rx, rz);
                foreach (var mt in mats)
                {
                    int nv = mt.Pos.Length / 3;
                    var o = new float[nv * 9];
                    for (int i = 0; i < nv; i++)
                    {
                        double lx = mt.Pos[3 * i] * k[0], ly = mt.Pos[3 * i + 1] * k[1], lz = mt.Pos[3 * i + 2] * k[2];
                        for (int q = 0; q < 3; q++) o[9 * i + q] = (float)(lx * A[0][q] + ly * A[1][q] + lz * A[2][q] + pos[q]);
                    }
                    for (int i = 0; i + 2 < nv; i += 3)
                    {
                        double light = 1;
                        if (mt.Glow < 0.5)
                        {
                            double nx, ny, nz;
                            if (mt.Nrm != null) { double ax = mt.Nrm[3 * i] / k[0], ay = mt.Nrm[3 * i + 1] / k[1], az = mt.Nrm[3 * i + 2] / k[2]; nx = ax * A[0][0] + ay * A[1][0] + az * A[2][0]; ny = ax * A[0][1] + ay * A[1][1] + az * A[2][1]; nz = ax * A[0][2] + ay * A[1][2] + az * A[2][2]; }
                            else
                            {
                                double ux = o[9 * (i + 1)] - o[9 * i], uy = o[9 * (i + 1) + 1] - o[9 * i + 1], uz = o[9 * (i + 1) + 2] - o[9 * i + 2], vx = o[9 * (i + 2)] - o[9 * i], vy = o[9 * (i + 2) + 1] - o[9 * i + 1], vz = o[9 * (i + 2) + 2] - o[9 * i + 2];
                                nx = uy * vz - uz * vy; ny = uz * vx - ux * vz; nz = ux * vy - uy * vx;
                            }
                            double l = Math.Max(Math.Sqrt(nx * nx + ny * ny + nz * nz), 1e-9);
                            light = 0.45 + 0.55 * Math.Abs((nx * .45 + ny * .8 + nz * .35) / 1.0 / l);
                        }
                        for (int t = 0; t < 3; t++)
                        {
                            int at = 9 * (i + t), v0 = i + t;
                            o[at + 3] = o[at + 4] = o[at + 5] = (float)light; o[at + 6] = 1; o[at + 7] = mt.Uv[2 * v0]; o[at + 8] = mt.Uv[2 * v0 + 1];
                        }
                    }
                    _textured.Add(new TexDraw { Key = mt.Key, Image = mt.Image, V = o, Owner = key });
                }
            }
            else if (Terrain.IsRamp(e)) { }      // drawn as its real shape with the ground models
            else M.Box(pos, V(bx[1] - bx[0], Math.Max(bx[3] - bx[2], .05), bx[5] - bx[4]), turn, KindRgb.GetValueOrDefault(e.Str("kind") ?? "", V(.7, .7, .7)), lift: bx[2], origin: new[] { bx[0], bx[1], bx[4], bx[5] }, k: k);
            M.CurRot = new[] { 0.0, 0 };
            bool solid = m.Obj("edits")?.Obj(key)?.Truthy("solid") == true;
            PickAdd(key, pos, turn, bx, k, scalable, rx, rz, $"{e.Str("name") ?? "?"}  ({UiText.KindText.GetValueOrDefault(e.Str("kind") ?? "", "")}, {e.Str("loc")} / {e.Str("cat")}){(solid ? "  - SOLID" : "")}");
            placedN++;
        }
        foreach (var rp in Terrain.Ramps(m)) PlacedBox(rp.Key, V(rp.X, g + rp.Y, rp.Z), rp.Turn, rp.K, rp.Rx, rp.Rz, true);   // ramps: clickable (drawn as ground models)
        var (x0, x1, z0, z1) = road.Bounds(1500);
        M.Quad(V(x0, g - .3, z0), V(x1, g - .3, z0), V(x1, g - .3, z1), V(x0, g - .3, z1), V(.23, .27, .32), shade: false);
        for (double gx = Math.Floor(x0 / 100) * 100; gx < x1; gx += 100) M.Line(V(gx, g - .25, z0), V(gx, g - .25, z1), V(.3, .35, .4));
        for (double gz = Math.Floor(z0 / 100) * 100; gz < z1; gz += 100) M.Line(V(x0, g - .25, gz), V(x1, g - .25, gz), V(.3, .35, .4));
        var el = road.Edge(-1); var er = road.Edge(1);
        var ys = Enumerable.Range(0, n).Select(i => g + .03 + road.Height[i]).ToArray();
        var net = D.Net()!;
        double[] bankRgb = Fe.BankColour.Select(c => c / 255.0).ToArray(), roadRgb = { .36, .37, .4 };
        foreach (var nc in RoadMesh.NetworkChunks(m, road))
            foreach (var c in nc.Cs) AddChunk(M, c, nc.Kind == "edge" ? bankRgb : nc.Kind == "divider" ? bankRgb : roadRgb);
        foreach (int side in new[] { -1, 1 })
        {
            var e = road.Edge(side);
            double Sy(int i) => ys[i] + side * road.WidthAt[i] / 2 * road.TanOf(i);
            for (int i = 0; i < n - 1; i++) M.Line(V(e[i].X, Sy(i) + .05, e[i].Z), V(e[i + 1].X, Sy(i + 1) + .05, e[i + 1].Z), V(.16, .9, 1));
        }
        for (int i = 0; i < n - 1; i += 4) { int j = Math.Min(i + 2, n - 1); M.Line(V(road.Centre[i].X, ys[i] + .06, road.Centre[i].Z), V(road.Centre[j].X, ys[j] + .06, road.Centre[j].Z), V(.9, .9, .9)); }
        foreach (var (_, br) in net.Branches)
            foreach (int side in new[] { -1, 1 })
            {
                var ee = br.Edge(side);
                for (int i = 0; i + 1 < br.Count; i++) M.Line(V(ee[i].X, g + br.Height[i] + .08, ee[i].Z), V(ee[i + 1].X, g + br.Height[i + 1] + .08, ee[i + 1].Z), V(.16, .9, 1));
            }
        if (!Surface.IsDefault(m))
        {
            var st = Surface.Settings(m);
            void Strip(Road rr, Func<Road, int, double> lo, Func<Road, int, double> hi, double[] col, double lift, bool[]? cut = null)
            {
                for (int i = 0; i + 1 < rr.Count; i++)
                {
                    if (cut != null && (cut[i] || cut[i + 1])) continue;
                    double[] Q(int k, Func<Road, int, double> f) { var (cx, cz) = rr.Centre[k]; var (sx, sz) = rr.Side(k); double lat = f(rr, k); return V(cx + sx * lat, g + rr.Surf(k, lat) + lift, cz + sz * lat); }
                    M.Quad(Q(i, lo), Q(i + 1, lo), Q(i + 1, hi), Q(i, hi), col);
                }
            }
            double[] Rgb(string k) => Surface.Surfaces[k].Rgb.Select(c => c / 255.0).ToArray();
            foreach (var rr in net.Drivable())
            {
                if (st.Road != "tarmac") Strip(rr, (q, k) => -q.WidthAt[k] / 2, (q, k) => q.WidthAt[k] / 2, Rgb(st.Road), .06);
                if (st.Sides > 0)
                {
                    double sw = st.Sides; var col = Rgb(st.SideType); var mask = net.WallMask(rr);
                    var f0 = Surface.SideFactor(net, rr, 0); var f1 = Surface.SideFactor(net, rr, 1);
                    Strip(rr, (q, k) => -q.WidthAt[k] / 2, (q, k) => -q.WidthAt[k] / 2 + sw * f0[k], col, .08, mask[0]);
                    Strip(rr, (q, k) => q.WidthAt[k] / 2 - sw * f1[k], (q, k) => q.WidthAt[k] / 2, col, .08, mask[1]);
                }
            }
            foreach (var c in Surface.OverlayChunks(m, net)) ChunkTex(c);
        }
        // jump ramps, bumps and hills (the same models the build makes)
        // (the Landscape tab draws the hills live instead: LandDraw)
        var ter = Terrain.Make(m, net, Terrain.Budget(m, road, net));
        foreach (var c in LandMode ? ter.Chunks.Take(ter.Chunks.Count - ter.Hills) : ter.Chunks) ChunkTex(c);
        for (int i = 0; i < n - 1; i++)
        {
            if (road.Height[i] > 0.5 || road.Height[i + 1] > 0.5)
                foreach (var (e, sd) in new[] { (el, -1), (er, 1) })
                {
                    double y0 = ys[i] + sd * road.WidthAt[i] / 2 * road.TanOf(i), y1 = ys[i + 1] + sd * road.WidthAt[i + 1] / 2 * road.TanOf(i + 1);
                    M.Quad(V(e[i].X, y0 - 1.2, e[i].Z), V(e[i + 1].X, y1 - 1.2, e[i + 1].Z), V(e[i + 1].X, y1, e[i + 1].Z), V(e[i].X, y0, e[i].Z), V(.5, .5, .55));
                }
            if (road.Height[i] > 3 && i % 6 == 0)
                foreach (double lat in new[] { -road.WidthAt[i] / 3, road.WidthAt[i] / 3 })
                {
                    var (x, z, a) = road.At(road.Dist[i], lat);
                    M.Box(V(x, g, z), V(2, Math.Max(.1, ys[i] + lat * road.TanOf(i) - g - 1.2), 2), a, V(.55, .55, .6));
                }
        }
        // the tile list
        var (cL, cH, cW) = Fe.Container;
        for (int ti = 0; ti < tiles.Count; ti++)
        {
            var t = tiles[ti]; string key = tileKeys[ti], kind = key.Split(':')[0];
            double x = t.X, y = t.Y, z = t.Z, turn = t.Turn; var k = t.Scale; var pos = V(x, y, z);
            M.CurRot = new[] { t.Rx, t.Rz };
            M.Owner(key);
            if (Layout.ObjectBox.TryGetValue(kind, out var box)) PickAdd(key, pos, turn, box, k, true, t.Rx, t.Rz);
            if (kind == "obj") { PlacedBox(key, pos, turn, k, t.Rx, t.Rz, true); continue; }
            var A = Layout.ActorAxes3(turn, t.Rx, t.Rz);
            double[] W(double lx, double ly, double lz = 0) => V(x + lx * A[0][0] + ly * A[1][0] + lz * A[2][0], y + lx * A[0][1] + ly * A[1][1] + lz * A[2][1], z + lx * A[0][2] + ly * A[1][2] + lz * A[2][2]);
            if (kind == "line")
            {
                double c = Math.Cos(turn), s = Math.Sin(turn), ln = Fe.LineLen * k[2];
                const int cells = 12;
                for (int q = 0; q < cells; q++)
                    for (int j = 0; j < 2; j++)
                    {
                        double lz0 = -ln / 2 + ln * q / cells, lz1 = lz0 + ln / cells, lx0 = -Fe.LineW / 2 + Fe.LineW * j / 2, lx1 = lx0 + Fe.LineW / 2;
                        double[] P(double lx, double lz) => V(x + lx * c + lz * s, y + .08, z - lx * s + lz * c);
                        var col = (q + j) % 2 != 0 ? V(.95, .95, .95) : V(.05, .05, .05);
                        M.Quad(P(lx0, lz0), P(lx1, lz0), P(lx1, lz1), P(lx0, lz1), col, shade: false);
                    }
            }
            else if (kind is "linebox" or "block") M.Box(pos, V(cL, cH, cW), turn, kind == "block" ? V(.75, .12, .1) : V(.85, .2, .15), origin: new[] { -cL, 0, 0, cW }, k: k);
            else if (kind == "chevron")
            {
                double hl = Fe.ChevronLen / 2 * k[0], h = Fe.ChevronH * k[1];
                M.Quad(W(-hl, 0), W(hl, 0), W(hl, h), W(-hl, h), V(.08, .1, .14), shade: false);
                for (int q = 0; q < Fe.ChevronCount; q++)
                {
                    double cx0 = -hl + 2 * hl * (q + .25) / Fe.ChevronCount, cx1 = -hl + 2 * hl * (q + .75) / Fe.ChevronCount;
                    M.Tri(W(cx0, h * .15), W(cx1, h * .5), W(cx0, h * .85), V(1, 1, 1), shade: false);
                }
            }
            else if (kind is "tanker" or "cruise")
            {
                double ln = kind == "tanker" ? 333 : 263;
                M.Box(pos, V(36, 18, ln), turn, kind == "tanker" ? V(.3, .32, .36) : V(.85, .85, .9), lift: kind == "cruise" ? -7.8 : 0, k: k);
                M.Box(pos, V(30, 26, 40), turn, V(.8, .8, .8), lift: kind == "tanker" ? 10 : 2, origin: new[] { -15, 15, ln / 2 - 60, ln / 2 - 20 }, k: k);
            }
            else if (kind.StartsWith("arch_"))
            {
                double X = Fe.ArchW / 2, P = Fe.ArchPillar / 2, D2 = Fe.ArchDepth / 2;
                var parts = new[] { new[] { -X - P, -X + P, -P, P, 0, Fe.ArchY1 }, new[] { X - P, X + P, -P, P, 0, Fe.ArchY1 }, new[] { -X - P, X + P, -D2, D2, Fe.ArchY0, Fe.ArchY1 } };
                for (int i = 0; i < 3; i++) { var a = parts[i]; M.Box(pos, V(a[1] - a[0], a[5] - a[4], a[3] - a[2]), turn, i == 2 ? V(.08, .12, .2) : V(.3, .32, .36), lift: a[4], origin: new[] { a[0], a[1], a[2], a[3] }, k: k); }
            }
            else if (kind.StartsWith("tower_"))
            {
                var col = Build.TowerFrame(kind).Select(c => c / 255.0).ToArray();
                foreach (var (a, b, c, d, e, f) in Build.TowerParts(kind)) M.Box(pos, V(b - a, f - e, d - c), turn, col, lift: e, origin: new[] { a, b, c, d }, k: k);
            }
            else if (kind == "crane")
            {
                foreach (double lx in new[] { -60.0, 56.0 }) M.Box(pos, V(4, 71, 26), turn, V(.15, .35, .75), origin: new[] { lx, lx + 4, -13, 13 }, k: k);
                M.Box(pos, V(132, 6, 10), turn, V(.15, .35, .75), lift: 62, k: k);
            }
            else if (kind == "ferris")
            {
                var hub = W(0, 26 * k[1]); double[]? last = null;
                for (int q = 0; q <= 32; q++)
                {
                    double a = 2 * Math.PI * q / 32; var p = W(22 * k[0] * Math.Cos(a), 26 * k[1] + 22 * k[1] * Math.Sin(a));
                    if (last != null) { M.Line(last, p, V(1, .85, .4)); M.Line(hub, last, V(1, .85, .4), .5); }
                    last = p;
                }
                M.Box(pos, V(3, 26, 3), turn, V(.7, .7, .7), k: k);
            }
            else if (kind == "radio") for (int j = 0; j < 6; j++) M.Box(pos, V(6, 10, 6), turn, j % 2 != 0 ? V(.85, .15, .15) : V(.95, .95, .95), lift: 10 * j, k: k);
        }
        // the lamp list
        for (int li = 0; li < lamps.Count; li++)
        {
            var sp = lamps[li]; string key = lampKeys[li], kind = key.Split(':')[0]; var pos = V(sp.X, sp.Y, sp.Z); double turn = sp.Turn;
            M.CurRot = new[] { sp.Rx, sp.Rz };
            M.Owner(key);
            if (kind == "obj") { PlacedBox(key, pos, turn, new[] { 1.0, 1, 1 }, sp.Rx, sp.Rz, false); continue; }
            if (Layout.ObjectBox.TryGetValue(kind, out var box)) PickAdd(key, pos, turn, box, new[] { 1.0, 1, 1 }, false, sp.Rx, sp.Rz);
            switch (kind)
            {
                case "lamp": M.Box(pos, V(.4, 10, .4), turn, V(.6, .62, .65)); M.Box(pos, V(1.2, .5, 1.2), turn, V(1, .9, .55), lift: 10); break;
                case "barrier": M.Box(pos, V(3.6, 1, .8), turn, V(.62, .62, .6)); break;
                case "billboard":
                    foreach (double lx in new[] { -3.0, 3.0 }) M.Box(pos, V(.3, 4, .3), turn, V(.4, .4, .4), origin: new[] { lx - .15, lx + .15, -.15, .15 });
                    M.Box(pos, V(8, 4, .4), turn, V(.95, .55, .1), lift: 4); break;
                case "flashing": M.Box(pos, V(1.6, 2.2, .3), turn, V(1, .85, .1)); break;
                case "light": M.Box(pos, V(.5, 6, .5), turn, V(.5, .5, .5)); M.Box(pos, V(1.5, .8, .6), turn, V(1, 1, .9), lift: 6); break;
                case "barrel": M.Box(pos, V(.8, 1, .8), turn, V(1, .45, .05)); break;
                case "rwbarrier": M.Box(pos, V(2.4, 1.1, .4), turn, V(.95, .2, .2)); break;
                case "cone": M.Box(pos, V(.5, .8, .5), turn, V(1, .5, .05)); break;
            }
        }
        M.CurRot = new[] { 0.0, 0 };
        // power-ups
        var nameOf = World.PickupTypes.ToDictionary(kv => kv.Value, kv => kv.Key);
        var lane = new Dictionary<int, int>();
        for (int i = 0; i < pickups.Count; i++)
        {
            var p = pickups[i]; int ow = owner[i];
            var col = TypeRgb.GetValueOrDefault(nameOf.GetValueOrDefault(p.Type ?? "", ""), V(.8, .8, .8));
            M.Owner("pickup:" + ow);
            M.Box(V(p.X, p.Y + .3, p.Z), V(1.6, .12, 2.6), p.A, col);
            var it = PickupList[ow]; bool row = it.Str("kind") != "single";
            lane[ow] = lane.GetValueOrDefault(ow, -1) + 1;
            Picks.Add(new Pick { Key = $"pickup:{ow}:{lane[ow]}", Pos = V(p.X, p.Y + .3, p.Z), Turn = p.A, Box = new[] { -.8, .8, -.3, .5, -1.3, 1.3 }, Scalable = false, Pickup = ow, Row = row, PType = it.Str("type"), Label = $"Power-up {(row ? "row" : "single")}: {it.Str("type") ?? (row ? "mixed" : nameOf.GetValueOrDefault(p.Type ?? "", "?"))}  ({(row ? "row" : "item")} {ow + 1})" });
        }
        M.Owner(null);
        // the road's points
        var pts = m.Arr("points") ?? new();
        for (int i = 0; i < pts.Count; i++)
        {
            var pd = Road.NormPoint(pts[i], 60); double hgt = pts[i] is JObj j0 ? j0.Num("h") : 0, width = pts[i] is JObj j1 && j1.Truthy("w") ? j1.Num("w") : m.Num("width", 60);
            if (hgt > .05) M.Line(V(pd.X, g, pd.Z), V(pd.X, g + hgt, pd.Z), V(1, .7, .15), .6);
            int near = road.IndexAt(road.Project(pd.X, pd.Z).S);
            Picks.Add(new Pick { Key = "point:" + i, Label = $"Track point {i + 1} of {pts.Count}", Pos = V(pd.X, g + hgt, pd.Z), Box = new[] { -3.0, 3, 0, 1, -3, 3 }, Scalable = false, Point = i, Width = width, Turn = road.Heading[near], Tilt = pd.Tilt, Lift = Math.Max(0, road.Height[near] - hgt), Col = i == 0 ? new Rgba(.21f, .88f, .35f) : i == pts.Count - 1 ? new Rgba(1, 1, 1) : new Rgba(1, .69f, .13f) });
        }
        // red beacons: what the checks complain about
        int problems = 0; var marked = new List<(double X, double Z)>();
        void Beacon(double x, double y, double z)
        {
            if (marked.Any(q => Math.Sqrt(Math.Pow(x - q.X, 2) + Math.Pow(z - q.Z, 2)) < 40)) return;
            marked.Add((x, z));
            M.Box(V(x, y, z), V(3, 25, 3), 0, V(1, .15, .15)); M.Box(V(x, y, z), V(7, 3, 7), 0, V(1, .3, .3), lift: 25);
        }
        for (int i = 1; i < n - 1; i++)
        {
            double da = Py.Mod(road.Heading[i + 1] - road.Heading[i - 1] + Math.PI, 2 * Math.PI) - Math.PI;
            if (Math.Abs(da) > 1e-6 && 2 * 4.0 / Math.Abs(da) < 35) { Beacon(road.Centre[i].X, ys[i], road.Centre[i].Z); problems++; }
        }
        foreach (var (j, _) in road.UnbridgedCrossings()) { Beacon(road.Centre[j].X, ys[j], road.Centre[j].Z); problems++; }
        for (int i = 0; i < n - 1; i++) if (Math.Abs(road.Height[i + 1] - road.Height[i]) / 4.0 > .12) { Beacon(road.Centre[i].X, ys[i], road.Centre[i].Z); problems++; }
        // the drive path
        var (s0, s1) = Layout.LineS(road, info);
        for (double s = Math.Max(s0 - 60, 0); s <= Math.Min(s1 + 40, road.Length); s += 4) { var (x, z, a) = road.At(s); _path.Add((x, g + road.HAt(s), z, a)); }
        _bounds = road.Bounds(0);
        Ground = g;
        Stats = $"{m.Str("name") ?? "map"}: {road.Length:0} m, {pickups.Count} power-ups, {road.Crossings.Count} bridge(s), {(problems == 0 ? "no problems" : $"{marked.Count} PROBLEM SPOT(S) = red beacons")}";
        int nobj = objs.Count, hidden = Enumerable.Range(0, nobj).Count(i => m.Obj("edits")?.Obj("obj:" + i)?.Truthy("hidden") == true);
        if (nobj > 0)
        {
            Stats += $", {placedN} placed object(s)";
            if (placedN + hidden < nobj) Stats += $" - {nobj - placedN - hidden} DID NOT FIT (the game's object lists are full)";
        }
    }

    unsafe void Compile(Mesh M)
    {
        G.MakeCurrent();
        if (!_hasList) { _list = GL.glGenLists(1); _hasList = true; }
        GL.glNewList(_list, GL.COMPILE);
        if (Shadows && SceneMode == "3d") { MakeShadows(M); Shown(M, M.Shadow, GL.TRIANGLES, M.V); M.ShadowTex.Draw(GL.TRIANGLES); }
        Shown(M, M.V, GL.TRIANGLES); Shown(M, M.L, GL.LINES);
        if (_textured.Count > 0) DrawTextured(ViewHidden.Count == 0 ? _textured : _textured.Where(t => !ViewHidden.Contains(t.Owner ?? "")));
        GL.glEndList();
    }

    public bool Shadows = true;

    /// hard shadows: every triangle pressed flat onto the ground along the sun's direction. One shadow vertex per scene vertex, so the eye icons' ranges fit it too
    void MakeShadows(Mesh M)
    {
        double sl = Math.Sqrt(Mesh.SunX * Mesh.SunX + Mesh.SunY * Mesh.SunY + Mesh.SunZ * Mesh.SunZ);
        float gy = (float)(Fe.GroundY - .27), ax = (float)(Mesh.SunX / Mesh.SunY), az = (float)(Mesh.SunZ / Mesh.SunY);
        var dark = new Rgba(.035f, .04f, .05f);
        void Tri(Batch to, float[] v, int a, int b, int c)
        {
            float x0 = v[a], y0 = v[a + 1], z0 = v[a + 2], x1 = v[b], y1 = v[b + 1], z1 = v[b + 2], x2 = v[c], y2 = v[c + 1], z2 = v[c + 2];
            bool flat = Math.Max(y0, Math.Max(y1, y2)) < gy + .7f;             // the ground and the flat road cast nothing
            float k0 = flat ? 0 : Math.Max(0, y0 - gy), k1 = flat ? 0 : Math.Max(0, y1 - gy), k2 = flat ? 0 : Math.Max(0, y2 - gy);
            if (flat) { x1 = x2 = x0; z1 = z2 = z0; }
            to.Add(x0 - ax * k0, gy, z0 - az * k0, dark); to.Add(x1 - ax * k1, gy, z1 - az * k1, dark); to.Add(x2 - ax * k2, gy, z2 - az * k2, dark);
        }
        M.Shadow.Clear(); M.ShadowTex.Clear();
        var vv = M.V.V;
        for (int t = 0; t + 2 < M.V.N; t += 3) Tri(M.Shadow, vv, 7 * t, 7 * (t + 1), 7 * (t + 2));
        foreach (var td in _textured)
        {
            if (ViewHidden.Contains(td.Owner ?? "")) continue;
            int nv = td.V.Length / 9;
            for (int t = 0; t + 2 < nv; t += 3) Tri(M.ShadowTex, td.V, 9 * t, 9 * (t + 1), 9 * (t + 2));
        }
    }

    public void ToggleShadows(bool on) { Shadows = on; if (_mesh != null) Compile(_mesh); G.Invalidate(); }

    /// a batch without what the eye icons hide
    void Shown(Mesh M, Batch b, uint mode, Batch? rangesOf = null)
    {
        if (ViewHidden.Count == 0) { b.Draw(mode); return; }
        var cut = ViewHidden.SelectMany(k => M.Ranges.GetValueOrDefault(k) ?? new()).Where(r => r.B == (rangesOf ?? b)).OrderBy(r => r.First).ToList();
        int at = 0;
        foreach (var r in cut) { if (r.First > at) b.DrawRange(mode, at, r.First - at); at = Math.Max(at, r.First + r.N); }
        if (b.N > at) b.DrawRange(mode, at, b.N - at);
    }

    unsafe void DrawTextured(IEnumerable<TexDraw> ts)
    {
        GL.glEnable(GL.TEXTURE_2D);
        GL.glEnableClientState(GL.VERTEX_ARRAY); GL.glEnableClientState(GL.COLOR_ARRAY); GL.glEnableClientState(GL.TEXTURE_COORD_ARRAY);
        foreach (var t in ts)
        {
            GL.glBindTexture(GL.TEXTURE_2D, Texture(t));
            fixed (float* p = t.V)
            {
                GL.glVertexPointer(3, GL.FLOAT, 36, p); GL.glColorPointer(4, GL.FLOAT, 36, p + 3); GL.glTexCoordPointer(2, GL.FLOAT, 36, p + 7);
                GL.glDrawArrays(GL.TRIANGLES, 0, t.V.Length / 9);
            }
        }
        GL.glDisableClientState(GL.TEXTURE_COORD_ARRAY); GL.glDisableClientState(GL.VERTEX_ARRAY); GL.glDisableClientState(GL.COLOR_ARRAY);
        GL.glBindTexture(GL.TEXTURE_2D, 0);
        GL.glDisable(GL.TEXTURE_2D);
    }

    unsafe uint Texture(TexDraw t)
    {
        if (_tex.TryGetValue(t.Key, out uint id)) return id;
        GL.glGenTextures(1, &id);
        GL.glBindTexture(GL.TEXTURE_2D, id);
        GL.glPixelStorei(GL.UNPACK_ALIGNMENT, 1);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, GL.REPEAT); GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, GL.REPEAT);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, GL.LINEAR); GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, GL.LINEAR_MIPMAP_LINEAR);
        fixed (byte* p = t.Image.Px) GL.gluBuild2DMipmaps(GL.TEXTURE_2D, (int)GL.RGB, t.Image.W, t.Image.H, GL.RGB, GL.UNSIGNED_BYTE, p);
        return _tex[t.Key] = id;
    }

    void AfterScene()
    {
        var nobj = D.Map.Arr("objects")?.Count ?? 0;
        MultiSel = MultiSel.Where(k => (k.StartsWith("obj:") && int.Parse(k[4..]) < nobj) || PickOf(k) != null).ToList();
        FillOutliner();
        if (!_placed && Active) { Reset(); _placed = true; }
        Info(_sceneError != null ? Stats : RealShapes && Previews.Waiting ? "Reading the game's models... (once - then they are fast)" : $"{Stats}   ({Tris:N0} triangles)", _sceneError != null ? Theme.Bad : RealShapes && Previews.Waiting ? YellowInfo : null);
        if (PickOf(Sel) == null) Select(null, true); else Select(Sel, true);
    }

    // ---- objects
    public const string MultiKey = "__group__";
    public Pick? PickOf(string? key)
    {
        if (key == MultiKey) return MultiPick();
        return key == null ? null : Picks.FirstOrDefault(p => p.Key == key);
    }

    List<Pick> Members() { var o = new List<Pick>(); foreach (var k in MultiSel) { var q = Picks.FirstOrDefault(p => p.Key == k); if (q != null && q.Editable && q.Point < 0 && q.Pickup < 0) o.Add(q); } return o; }

    Pick? MultiPick()
    {
        var ms = Members();
        if (ms.Count < 2) return null;
        var cs = ms.SelectMany(q => BoxCorners(q)).ToList();
        double lox = cs.Min(c => c[0]), hix = cs.Max(c => c[0]), loy = cs.Min(c => c[1]), hiy = cs.Max(c => c[1]), loz = cs.Min(c => c[2]), hiz = cs.Max(c => c[2]), cx = (lox + hix) / 2, cz = (loz + hiz) / 2;
        return new Pick { Key = MultiKey, Label = $"{ms.Count} objects", Pos = V(cx, loy, cz), Box = new[] { lox - cx, hix - cx, 0, hiy - loy, loz - cz, hiz - cz }, Multi = true };
    }

    static double[][] AxesOf(Pick p, double dt = 0, double[]? dr = null) => Layout.ActorAxes3(p.Turn + dt, p.Rx + (dr?[0] ?? 0), p.Rz + (dr?[1] ?? 0));

    public static List<double[]> BoxCorners(Pick p, double[]? delta = null, double dturn = 0, double[]? dscale = null, double[]? drot = null)
    {
        double[] b = p.Box; double[] k = { p.Scale[0] * (dscale?[0] ?? 1), p.Scale[1] * (dscale?[1] ?? 1), p.Scale[2] * (dscale?[2] ?? 1) };
        var A = AxesOf(p, dturn, drot); var o = new double[3];
        for (int i = 0; i < 3; i++) o[i] = p.Pos[i] + (delta?[i] ?? 0);
        var r = new List<double[]>(8);
        foreach (double ly in new[] { b[2], b[3] }) foreach (double lz in new[] { b[4], b[5] }) foreach (double lx in new[] { b[0], b[1] })
                    r.Add(V(o[0] + lx * k[0] * A[0][0] + ly * k[1] * A[1][0] + lz * k[2] * A[2][0], o[1] + lx * k[0] * A[0][1] + ly * k[1] * A[1][1] + lz * k[2] * A[2][1], o[2] + lx * k[0] * A[0][2] + ly * k[1] * A[1][2] + lz * k[2] * A[2][2]));
        return r;
    }

    static double? RayHit(Pick p, double[] origin, double[] dir)
    {
        var b = p.Box; var k = p.Scale; const double grow = .8;
        double[] lo = { Math.Min(b[0] * k[0], -grow), b[2] * k[1] - grow * .5, Math.Min(b[4] * k[2], -grow) }, hi = { Math.Max(b[1] * k[0], grow), b[3] * k[1] + grow * .5, Math.Max(b[5] * k[2], grow) };
        var A = AxesOf(p);
        double[] rel = { origin[0] - p.Pos[0], origin[1] - p.Pos[1], origin[2] - p.Pos[2] }, o = new double[3], d = new double[3];
        for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++) { o[j] += rel[i] * A[j][i]; d[j] += dir[i] * A[j][i]; }
        double t0 = 0, t1 = 1e12;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(d[i]) < 1e-12) { if (!(lo[i] <= o[i] && o[i] <= hi[i])) return null; continue; }
            double a = (lo[i] - o[i]) / d[i], c = (hi[i] - o[i]) / d[i];
            if (a > c) (a, c) = (c, a);
            t0 = Math.Max(t0, a); t1 = Math.Min(t1, c);
            if (t0 > t1) return null;
        }
        return t0;
    }

    (double[] D, double T, double S) MemberChange(Pick g, Pick q, Op op)
    {
        double s = op.DScale[0], t = op.DTurn;
        double[] rel = { q.Pos[0] - g.Pos[0], q.Pos[1] - g.Pos[1], q.Pos[2] - g.Pos[2] }, nw = { rel[0] * s, rel[1] * s, rel[2] * s };
        double c = Math.Cos(t), si = Math.Sin(t);
        nw = V(nw[0] * c + nw[2] * si, nw[1], -nw[0] * si + nw[2] * c);
        return (V(nw[0] - rel[0] + op.Delta[0], nw[1] - rel[1] + op.Delta[1], nw[2] - rel[2] + op.Delta[2]), t, q.Scalable ? s : 1);
    }

    public JObj EditsCopy() => D.Map.Obj("edits") != null ? Share.Copy(D.Map.Obj("edits")!) : new JObj();
    JObj EditOf(JObj e, string k) => e.Obj(k) ?? new JObj();
    static double[] KVec(object? k) => k is List<object?> l ? l.Select(Json.D).ToArray() : k == null ? new[] { 1.0, 1, 1 } : new[] { Json.D(k), Json.D(k), Json.D(k) };
    static object KObj(double[] v) => v.Max() - v.Min() < 1e-9 ? v[0] : v.Select(x => (object?)x).ToList();

    public void Select(string? key, bool keepMulti = false)
    {
        if (key == MultiKey) { SetMulti(MultiSel); return; }
        if (!keepMulti) MultiSel = new();
        Sel = key;
        var p = PickOf(key);
        if (p == null) { Sel = null; SelBar(false, ""); }
        else
        {
            var e = D.Map.Obj("edits")?.Obj(key!) ?? new JObj();
            var ch = new List<string>();
            if (e.Truthy("dx") || e.Truthy("dy") || e.Truthy("dz")) ch.Add($"moved {Math.Sqrt(Math.Pow(e.Num("dx"), 2) + Math.Pow(e.Num("dy"), 2) + Math.Pow(e.Num("dz"), 2)):0} m");
            if (e.Truthy("turn")) ch.Add($"turned {e.Num("turn") * 180 / Math.PI:+0;-0;+0}°");
            var k = KVec(e["scale"]);
            if (k.Max(v => Math.Abs(v - 1)) > 1e-6) ch.Add(k.Max() - k.Min() < 1e-9 ? $"scale x{k[0]:0.00}" : $"scale x{k[0]:0.00} / y{k[2]:0.00} / z{k[1]:0.00}");
            string how = !p.Editable ? "part of the road layout - it moves with the road" : $"drag a handle, or G / R / {(p.Scalable ? "S / " : "")}X keys   Esc = deselect";
            SelBar(true, $"{p.Label}   [{key}]   {(ch.Count > 0 ? string.Join(", ", ch) : "as placed")}   |   {how}");
        }
        if (SoloKey != null)
        {
            if (p == null || p.Point >= 0) SoloOff();
            else if (!SoloSet.Contains(SoloOf(p))) SoloOn(p);
        }
        OutlinerShow(p);
        PanelShow(p);
        G.Invalidate();
    }

    public void SetMulti(List<string> keys, bool sync = true)
    {
        Oper = null;
        if (SoloKey != null && !keys.All(SoloSet.Contains)) SoloOff();
        MultiSel = keys.ToList();
        var g = MultiPick();
        Sel = g != null ? MultiKey : null;
        if (g != null) SelBar(true, $"{g.Label} selected   |   drag a handle, or G move / R turn / S size (all of them together)   Delete, Ctrl+G group, Ctrl+D copy   Esc = none");
        else SelBar(false, "");
        PanelShow(null);
        if (sync) OutlinerShow(null);
        Info($"{keys.Count} selected - Delete = delete them, Ctrl+G = group, Ctrl+D = copy, Esc = none", YellowInfo);
        G.Invalidate();
    }

    static readonly Color YellowInfo = ColorTranslator.FromHtml("#f0e080");

    // ---- Solo
    public static string SoloOf(Pick p) => p.Pickup >= 0 ? "pickup:" + p.Pickup : p.Key;
    (double[] T, double D, double Y, double P, bool O)? _soloCam;

    public void ToggleSolo()
    {
        if (SoloKey != null) { SoloOff(); return; }
        var p = PickOf(Sel);
        if (p == null || p.Point >= 0 || p.Multi) { Info("Solo: select ONE object first (a track point has no shape)", YellowInfo); return; }
        _soloCam = ((double[])Target.Clone(), Dist, Yaw, Pitch, Ortho);
        SoloOn(p);
    }

    void SoloOn(Pick p)
    {
        SoloShow(SoloOf(p), new() { SoloOf(p) }, p.Label);
    }

    /// `key` = what is in solo (an object, a group, a whole list), `owners` = the objects shown
    void SoloShow(string key, HashSet<string> owners, string label)
    {
        var cs = Picks.Where(q => owners.Contains(SoloOf(q))).SelectMany(q => BoxCorners(q)).ToList();
        if (cs.Count == 0) return;
        SoloKey = key; SoloSet = owners;
        double[] lo = { cs.Min(c => c[0]), cs.Min(c => c[1]), cs.Min(c => c[2]) }, hi = { cs.Max(c => c[0]), cs.Max(c => c[1]), cs.Max(c => c[2]) };
        Target = V((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
        Dist = Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2])) * 2.2 + 4;
        DriveT = null; CamAnim = null;
        SoloButton(true);
        _outlTree?.Invalidate();
        Info($"SOLO: only {label}   ( / or Esc = show everything )", YellowInfo);
        G.Invalidate();
    }

    void SoloOff()
    {
        SoloKey = null; SoloSet = new();
        _outlTree?.Invalidate();
        if (_soloCam != null) { (Target, Dist, Yaw, Pitch, Ortho) = (_soloCam.Value.T.ToArray(), _soloCam.Value.D, _soloCam.Value.Y, _soloCam.Value.P, _soloCam.Value.O); }
        SoloButton(false);
        Info("");
        G.Invalidate();
    }

    // ---- handles
    (double[] C, double Ln, List<(string Name, double[] End)> Axes, Dictionary<string, (List<double[]> Ring, double[] N)> Rings)? Handles()
    {
        var p = PickOf(Sel);
        if (p == null || !p.Editable || !_mats || SceneMode != "3d") return null;
        if (Gizmo == "scale" && !p.Scalable) return null;
        if ((p.Pickup >= 0 && Gizmo != "move") || (p.Point >= 0 && Gizmo == "scale")) return null;
        var op = Oper;
        double[] d = op?.Kind == "move" ? op.Delta : new double[3];
        double[] c;
        if (p.Point >= 0 || p.Pickup >= 0) c = V(p.Pos[0] + d[0], p.Pos[1] + p.Lift + d[1], p.Pos[2] + d[2]);
        else { var cs = BoxCorners(p); c = V(p.Pos[0] + d[0], (cs[0][1] + cs[7][1]) / 2 + d[1], p.Pos[2] + d[2]); }
        var s = Screen3(c);
        if (s == null) return null;
        var a = Unproject(s.Value.X, s.Value.Y, s.Value.Z); var b = Unproject(s.Value.X + 90, s.Value.Y, s.Value.Z);
        if (a == null || b == null) return null;
        double ln = Dist3(a, b), dt = op?.DTurn ?? 0;
        var dr = op?.DRot ?? new double[2];
        var A = AxesOf(p, dt, dr);
        var own = new Dictionary<string, double[]> { ["x"] = A[0], ["y"] = A[2], ["z"] = A[1] };
        var axes = new List<(string, double[])>();
        foreach (var (name, (v, _)) in AxesInfo)
        {
            if (name == "z" && p.Pickup >= 0) continue;
            var dir = Gizmo == "scale" ? own[name] : v;
            axes.Add((name, V(c[0] + ln * dir[0], c[1] + ln * dir[1], c[2] + ln * dir[2])));
        }
        var normals = new Dictionary<string, double[]> { ["z"] = V(0, 1, 0), ["y"] = Layout.ActorAxes3(p.Turn + dt, 0, 0)[2], ["x"] = A[0] };
        if (p.Point >= 0) normals = new() { ["x"] = V(Math.Sin(p.Turn), 0, Math.Cos(p.Turn)) };       // a road point has one ring: around the road, that is its tilt
        var rings = new Dictionary<string, (List<double[]>, double[])>();
        foreach (var (name, nv) in normals)
        {
            var u = Cross(nv, Math.Abs(nv[1]) < .9 ? V(0, 1, 0) : V(1, 0, 0));
            double ul = Len(u); u = V(u[0] / ul, u[1] / ul, u[2] / ul);
            var v = Cross(nv, u);
            var ring = new List<double[]>(49);
            for (int i = 0; i <= 48; i++)
            {
                double q = 2 * Math.PI * i / 48, cq = Math.Cos(q) * .85 * ln, sq = Math.Sin(q) * .85 * ln;
                ring.Add(V(c[0] + cq * u[0] + sq * v[0], c[1] + cq * u[1] + sq * v[1], c[2] + cq * u[2] + sq * v[2]));
            }
            rings[name] = (ring, nv);
        }
        if (p.Multi) { rings = new() { ["z"] = rings["z"] }; if (Gizmo == "scale") axes = new(); }
        return (c, ln, axes, rings);
    }

    static double[] Cross(double[] a, double[] b) => V(a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]);
    static double Len(double[] a) => Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
    static double Dist3(double[] a, double[] b) => Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2));

    public void LookAt(double x, double y, double z, double heading) { Target = V(x, y, z); Dist = 170; Pitch = .2; Yaw = heading + .35; }

    /// the robot: a place on the selected road point's ring
    public (int X, int Y)? TiltRingPoint()
    {
        var h = Handles();
        if (h == null || !h.Value.Rings.TryGetValue("x", out var r)) return null;
        var s = Screen(r.Ring[6]);
        return s == null ? null : ((int)s.Value.X, (int)s.Value.Y);
    }

    string? HandleAt(int mx, int my)
    {
        var h = Handles();
        if (h == null) return null;
        var (c, _, axes, rings) = h.Value;
        var sc = Screen(c);
        if (sc == null) return null;
        double SegD((double X, double Y) a, (double X, double Y) b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y, t = Math.Max(0, Math.Min(1, ((mx - a.X) * vx + (my - a.Y) * vy) / Math.Max(vx * vx + vy * vy, 1e-9)));
            return Math.Sqrt(Math.Pow(mx - a.X - t * vx, 2) + Math.Pow(my - a.Y - t * vy, 2));
        }
        if (Math.Sqrt(Math.Pow(mx - sc.Value.X, 2) + Math.Pow(my - sc.Value.Y, 2)) < 10 && Gizmo != "rotate") return "centre";
        string? best = null; double bd = double.MaxValue;
        if (Gizmo == "rotate")
        {
            foreach (var (name, (ring, _)) in rings)
            {
                var pts = ring.Select(Screen).ToList();
                if (pts.Any(q => q == null)) continue;
                double dd = double.MaxValue;
                for (int i = 0; i + 1 < pts.Count; i++) dd = Math.Min(dd, SegD(pts[i]!.Value, pts[i + 1]!.Value));
                if (dd < 8 && dd < bd) { bd = dd; best = name; }
            }
            return best;
        }
        foreach (var (name, end) in axes)
        {
            var se = Screen(end);
            if (se == null) continue;
            double dd = SegD(sc.Value, se.Value);
            if (dd < 9 && dd < bd) { bd = dd; best = name; }
        }
        return best;
    }

    void DrawHandles()
    {
        var h = Handles();
        if (h == null) return;
        var (c, ln, axes, rings) = h.Value;
        var active = Oper != null ? Oper.Axis : _hot;
        var b = new Batch(); var tri = new Batch();
        GL.glDisable(GL.DEPTH_TEST);
        Rgba Col(string n, Rgba bas) => n == active ? new Rgba(1, .9f, .2f) : bas;
        void Seg(Batch q, double[] a, double[] e, Rgba col) { q.Add((float)a[0], (float)a[1], (float)a[2], col); q.Add((float)e[0], (float)e[1], (float)e[2], col); }
        if (Gizmo == "rotate")
        {
            var s0 = Screen3(c);
            if (s0 != null)
            {
                double[]? last = null;
                for (int i = 0; i <= 48; i++)
                {
                    double q = 2 * Math.PI * i / 48;
                    var w = Unproject(s0.Value.X + .9 * 90 * Math.Cos(q), s0.Value.Y + .9 * 90 * Math.Sin(q), s0.Value.Z);
                    if (w != null && last != null) Seg(b, last, w, new Rgba(.8f, .8f, .8f, .35f));
                    last = w;
                }
            }
            GL.glLineWidth(1.5f); b.Draw(GL.LINES); b.Clear();
            var sel = PickOf(Sel);
            if (sel is { Point: >= 0 })
            {
                double deg = PointTiltNow(sel), tan = Math.Tan(deg * Math.PI / 180), half = sel.Width / 2, lx = -Math.Cos(sel.Turn), lz = Math.Sin(sel.Turn);
                Seg(b, V(c[0] - lx * half, c[1] - half * tan + .3, c[2] - lz * half), V(c[0] + lx * half, c[1] + half * tan + .3, c[2] + lz * half), new Rgba(1, .95f, .3f));
                GL.glLineWidth(4); b.Draw(GL.LINES); b.Clear();
            }
            foreach (var (name, (ring, _)) in rings)
            {
                for (int i = 0; i + 1 < ring.Count; i++) Seg(b, ring[i], ring[i + 1], Col(name, AxesInfo[name].C));
                GL.glLineWidth(name == active ? 4 : 2.5f); b.Draw(GL.LINES); b.Clear();
            }
        }
        else
        {
            foreach (var (name, end) in axes)
            {
                var col = Col(name, AxesInfo[name].C);
                Seg(b, c, end, col);
                GL.glLineWidth(name == active ? 4 : 2.5f); b.Draw(GL.LINES); b.Clear();
                double[] d = V((end[0] - c[0]) / ln, (end[1] - c[1]) / ln, (end[2] - c[2]) / ln);
                var side = Math.Abs(d[1]) < .9 ? Cross(d, V(0, 1, 0)) : V(1, 0, 0); double sl = Len(side); side = V(side[0] / sl, side[1] / sl, side[2] / sl);
                var up = Cross(d, side); double r = ln * .07;
                void T(double[] p) => tri.Add((float)p[0], (float)p[1], (float)p[2], col);
                double[] Off(double[] o, double a, double[] x, double q, double[] y, double w = 0, double[]? z = null) => V(o[0] + a * x[0] + q * y[0] + w * (z?[0] ?? 0), o[1] + a * x[1] + q * y[1] + w * (z?[1] ?? 0), o[2] + a * x[2] + q * y[2] + w * (z?[2] ?? 0));
                if (Gizmo == "move")
                {
                    var tip = V(end[0] + d[0] * ln * .22, end[1] + d[1] * ln * .22, end[2] + d[2] * ln * .22);
                    for (int i = 0; i < 8; i++)
                    {
                        double a0 = 2 * Math.PI * i / 8, a1 = 2 * Math.PI * (i + 1) / 8;
                        T(Off(end, r * Math.Cos(a0), side, r * Math.Sin(a0), up)); T(Off(end, r * Math.Cos(a1), side, r * Math.Sin(a1), up)); T(tip);
                    }
                }
                else
                {
                    var cube = new List<double[]>();
                    foreach (int sz in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) foreach (int sx in new[] { -1, 1 }) cube.Add(Off(end, r * sx, side, r * sy, up, r * sz, d));
                    foreach (var f in new[] { new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 } })
                        foreach (int i in new[] { f[0], f[1], f[2], f[0], f[2], f[3] }) T(cube[i]);
                }
            }
            tri.Draw(GL.TRIANGLES);
            var pts = new Batch();
            var cc = Col("centre", new Rgba(.95f, .95f, .95f));
            pts.Add((float)c[0], (float)c[1], (float)c[2], cc);
            GL.glPointSize(active == "centre" ? 11 : 8); pts.Draw(GL.POINTS); GL.glPointSize(1);
        }
        GL.glLineWidth(1);
        GL.glEnable(GL.DEPTH_TEST);
    }

    // ---- camera + maths
    static double[] MatMul(double[] a, double[] b)
    {
        var o = new double[16];
        for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) for (int k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k];
        return o;
    }

    static double[] Perspective(double fovDeg, double asp, double n, double f)
    {
        double t = 1 / Math.Tan(fovDeg * Math.PI / 360); var p = new double[16];
        p[0] = t / asp; p[5] = t; p[10] = (f + n) / (n - f); p[11] = -1; p[14] = 2 * f * n / (n - f);
        return p;
    }

    static double[] OrthoM(double l, double r, double b, double t, double n, double f)
    {
        var p = new double[16];
        p[0] = 2 / (r - l); p[5] = 2 / (t - b); p[10] = -2 / (f - n); p[12] = -(r + l) / (r - l); p[13] = -(t + b) / (t - b); p[14] = -(f + n) / (f - n); p[15] = 1;
        return p;
    }

    static double[] LookAt(double[] e, double[] c, double[] up)
    {
        double[] f = { c[0] - e[0], c[1] - e[1], c[2] - e[2] }; double fl = Len(f); f = V(f[0] / fl, f[1] / fl, f[2] / fl);
        var s = Cross(f, up); double sl = Math.Max(Len(s), 1e-12); s = V(s[0] / sl, s[1] / sl, s[2] / sl);
        var u = Cross(s, f);
        return new[] { s[0], u[0], -f[0], 0, s[1], u[1], -f[1], 0, s[2], u[2], -f[2], 0, -(s[0] * e[0] + s[1] * e[1] + s[2] * e[2]), -(u[0] * e[0] + u[1] * e[1] + u[2] * e[2]), f[0] * e[0] + f[1] * e[1] + f[2] * e[2], 1 };
    }

    static double[]? Invert(double[] m)
    {
        var a = new double[4, 8];
        for (int r = 0; r < 4; r++) { for (int c = 0; c < 4; c++) a[r, c] = m[c * 4 + r]; a[r, r + 4] = 1; }
        for (int i = 0; i < 4; i++)
        {
            int p = i; for (int r = i + 1; r < 4; r++) if (Math.Abs(a[r, i]) > Math.Abs(a[p, i])) p = r;
            if (Math.Abs(a[p, i]) < 1e-30) return null;
            if (p != i) for (int c = 0; c < 8; c++) (a[i, c], a[p, c]) = (a[p, c], a[i, c]);
            double d = a[i, i]; for (int c = 0; c < 8; c++) a[i, c] /= d;
            for (int r = 0; r < 4; r++) if (r != i) { double k = a[r, i]; if (k != 0) for (int c = 0; c < 8; c++) a[r, c] -= k * a[i, c]; }
        }
        var o = new double[16];
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) o[c * 4 + r] = a[r, c + 4];
        return o;
    }

    static double[] Xf(double[] m, double x, double y, double z, double w) => new[] { m[0] * x + m[4] * y + m[8] * z + m[12] * w, m[1] * x + m[5] * y + m[9] * z + m[13] * w, m[2] * x + m[6] * y + m[10] * z + m[14] * w, m[3] * x + m[7] * y + m[11] * z + m[15] * w };

    (double[] Origin, double[] Dir)? RayAt(int mx, int my)
    {
        if (!_mats) return null;
        double nx = 2.0 * mx / Math.Max(_w, 1) - 1, ny = 1 - 2.0 * my / Math.Max(_h, 1);
        var a = Xf(_inv, nx, ny, -1, 1); var b = Xf(_inv, nx, ny, 1, 1);
        double[] p = V(a[0] / a[3], a[1] / a[3], a[2] / a[3]), q = V(b[0] / b[3], b[1] / b[3], b[2] / b[3]), d = V(q[0] - p[0], q[1] - p[1], q[2] - p[2]);
        double l = Len(d);
        return (p, V(d[0] / l, d[1] / l, d[2] / l));
    }

    public (double X, double Y)? Screen(double[] p) { var s = Screen3(p); return s == null ? null : (s.Value.X, s.Value.Y); }
    (double X, double Y, double Z)? Screen3(double[] p)
    {
        if (!_mats) return null;
        var c = Xf(_fwd, p[0], p[1], p[2], 1);
        if (c[3] < 1e-9) return null;
        return ((c[0] / c[3] + 1) * _w / 2, (1 - c[1] / c[3]) * _h / 2, c[2] / c[3]);
    }

    double[]? Unproject(double x, double y, double z)
    {
        if (!_mats) return null;
        var a = Xf(_inv, 2.0 * x / _w - 1, 1 - 2.0 * y / _h, z, 1);
        return Math.Abs(a[3]) > 1e-12 ? V(a[0] / a[3], a[1] / a[3], a[2] / a[3]) : null;
    }

    public void Reset()
    {
        var (x0, x1, z0, z1) = _bounds;
        Target = V((x0 + x1) / 2, 0, (z0 + z1) / 2);
        Dist = Math.Max(x1 - x0, z1 - z0) * 1.1 + 200;
        Yaw = -0.6; Pitch = 0.55; DriveT = null; CamAnim = null;
        G.Invalidate();
    }

    public void Top() { Reset(); Yaw = 0; Pitch = 1.55; G.Invalidate(); }
    public void StartDrive() { if (_path.Count > 0) { DriveT = Now; _anim.Start(); } }
    public void StopDrive() { DriveT = null; CamAnim = null; }
    static double Now => Environment.TickCount64 / 1000.0;
    public void ViewAxis(string w)
    {
        DriveT = null;
        (Yaw, Pitch) = w switch { "top" => (0.0, 1.55), "bottom" => (0.0, -1.55), "front" => (0.0, 0.0), "back" => (Math.PI, 0.0), "right" => (Math.PI / 2, 0.0), _ => (-Math.PI / 2, 0.0) };
        G.Invalidate();
    }
    void OrbitStep(int dx, int dy) { Yaw -= dx * Math.PI / 12; Pitch = Math.Min(1.55, Math.Max(-1.55, Pitch + dy * Math.PI / 12)); G.Invalidate(); }
    void ZoomStep(double k) { Dist = Math.Min(40000, Math.Max(5, Dist * k)); G.Invalidate(); }

    double[] Eye() { double cp = Math.Cos(Pitch); return V(Target[0] + Dist * cp * Math.Sin(Yaw), Target[1] + Dist * Math.Sin(Pitch), Target[2] + Dist * cp * Math.Cos(Yaw)); }
    double[] ViewDir() { double cp = Math.Cos(Pitch); return V(-cp * Math.Sin(Yaw), -Math.Sin(Pitch), -cp * Math.Cos(Yaw)); }
    void KeepEye(double depth) { var e = Eye(); var d = ViewDir(); depth = Math.Max(.5, depth); Target = V(e[0] + d[0] * depth, e[1] + d[1] * depth, e[2] + d[2] * depth); Dist = depth; }
    double DepthOf(double[] p) { var e = Eye(); var d = ViewDir(); return (p[0] - e[0]) * d[0] + (p[1] - e[1]) * d[1] + (p[2] - e[2]) * d[2]; }

    // ---- drawing
    bool _drawOverlays = true;
    public unsafe void Draw()
    {
        Ensure();
        int w = Math.Max(G.Width, 1), h = Math.Max(G.Height, 1);
        _w = w; _h = h;
        GL.glClearColor(.07f, .09f, .12f, 1); GL.glClear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
        GL.glEnable(GL.DEPTH_TEST); GL.glDepthMask(1); GL.glEnable(GL.BLEND); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        double aspect = (double)w / h;
        (double[] Eye, double[] Q, double Fov)? cam = null;
        if (CamAnim != null)
        {
            int f = (int)((DateTime.UtcNow - CamAnim.Value.T).TotalSeconds * 30);
            if (f < CamAnim.Value.F.Count) cam = CamAnim.Value.F[f]; else CamAnim = null;
        }
        double[] P = cam != null ? Perspective(cam.Value.Fov * 180 / Math.PI, aspect, .5, 60000) : Ortho && DriveT == null ? OrthoM(-Dist * .45 * aspect, Dist * .45 * aspect, -Dist * .45, Dist * .45, -60000, 60000) : Perspective(55, aspect, Math.Clamp(Dist / 100, 1, 20), 60000);
        double[] Mv;
        if (cam != null)
        {
            var (eye, q, _) = cam.Value;
            var fw = IntroCam.Rotate(q, V(0, 0, 1)); var up = IntroCam.Rotate(q, V(0, 1, 0));
            Mv = LookAt(V(eye[0], eye[1], -eye[2]), V(eye[0] + fw[0], eye[1] + fw[1], -(eye[2] + fw[2])), V(up[0], up[1], -up[2]));
        }
        else if (DriveT != null && _path.Count > 0)
        {
            int i = (int)((Now - DriveT.Value) * 14.0);
            if (i >= _path.Count - 6) { DriveT = null; i = _path.Count - 6; }
            i = Math.Max(i, 0);
            var (x, y, z, a) = _path[i]; var (lx, ly, lz, _) = _path[Math.Min(i + 6, _path.Count - 1)];
            Mv = LookAt(V(x - 14 * Math.Sin(a), y + 6, -(z - 14 * Math.Cos(a))), V(lx, ly + 2, -lz), V(0, 1, 0));
        }
        else
        {
            var e = Eye();
            var up = Math.Abs(Pitch) < 1.5 ? V(0, 1, 0) : V(Math.Sin(Yaw) * (Pitch > 0 ? 1 : -1), 0, -Math.Cos(Yaw) * (Pitch > 0 ? 1 : -1));
            Mv = LookAt(V(e[0], e[1], -e[2]), V(Target[0], Target[1], -Target[2]), up);
        }
        Mv = MatMul(Mv, new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1 });
        _fwd = MatMul(P, Mv);
        var inv = Invert(_fwd); _mats = inv != null; if (inv != null) _inv = inv;
        var fl = _fwd.Select(v => (float)v).ToArray();
        GL.glMatrixMode(GL.PROJECTION);
        fixed (float* p = fl) GL.glLoadMatrixf(p);
        GL.glMatrixMode(GL.MODELVIEW); GL.glLoadIdentity();
        var M = _mesh;
        if (M != null)
        {
            if (SoloKey != null)
            {
                var rs = SoloSet.SelectMany(k => M.Ranges.GetValueOrDefault(k) ?? new()).ToList();
                foreach (var (b, first, n) in rs) if (b == M.V) b.DrawRange(GL.TRIANGLES, first, n); else if (b == M.L) b.DrawRange(GL.LINES, first, n);
                DrawTextured(_textured.Where(t => SoloSet.Contains(t.Owner ?? "")));
                GL.glDepthMask(0); foreach (var (b, first, n) in rs) if (b == M.T) b.DrawRange(GL.TRIANGLES, first, n); GL.glDepthMask(1);
            }
            else
            {
                if (_hasList) GL.glCallList(_list);
                GL.glDepthMask(0); Shown(M, M.T, GL.TRIANGLES); GL.glDepthMask(1);
            }
        }
        if (LandMode) LandDraw();
        if (!_drawOverlays) return;
        if (LandMode) LandRing();
        var sel = PickOf(Sel); var op = Oper;
        if (SceneMode == "3d" && SoloKey == null)
        {
            GL.glDisable(GL.DEPTH_TEST);
            var pts = new Batch();
            foreach (var q in Picks)
                if (q.Point >= 0 && !ViewHidden.Contains(q.Key))
                {
                    var d = q == sel && op != null ? op.Delta : new double[3];
                    pts.Add((float)(q.Pos[0] + d[0]), (float)(q.Pos[1] + q.Lift + d[1] + .5), (float)(q.Pos[2] + d[2]), q == sel ? new Rgba(1, .95f, .2f) : q.Col);
                }
            GL.glPointSize(9); pts.Draw(GL.POINTS); GL.glPointSize(1);
            GL.glEnable(GL.DEPTH_TEST);
        }
        ScatterDraw();
        var movable = sel is { Multi: true } ? Members().Select(q => q.Key).ToHashSet() : new HashSet<string>();
        foreach (var k in MultiSel)
        {
            var q = PickOf(k);
            if (q == null || q.Point >= 0) continue;
            if (op != null && movable.Contains(k))
            {
                var (d, t, s) = MemberChange(sel!, q, op);
                BoxLines(BoxCorners(q), new Rgba(.6f, .6f, .3f, .5f));
                BoxLines(BoxCorners(q, d, t, new[] { s, s, s }), new Rgba(1, .9f, .1f));
            }
            else BoxLines(BoxCorners(q), new Rgba(1, .9f, .1f));
        }
        if (sel != null)
        {
            var group = sel.Pickup >= 0 ? Picks.Where(q => q.Pickup == sel.Pickup).ToList() : sel.Point >= 0 ? new List<Pick>() : new List<Pick> { sel };
            foreach (var q in group)
            {
                var cs = BoxCorners(q, op?.Delta, op?.DTurn ?? 0, op?.DScale, op?.DRot);
                if (op != null && !q.Multi) BoxLines(BoxCorners(q), new Rgba(.6f, .6f, .3f, .5f));
                BoxLines(cs, q.Multi ? new Rgba(.45f, .8f, 1, .8f) : new Rgba(1, .9f, .1f));
            }
            DrawHandles();
        }
    }

    static void BoxLines(List<double[]> cs, Rgba col)
    {
        GL.glDisable(GL.DEPTH_TEST);
        var b = new Batch();
        foreach (var (a, c) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) })
        { b.Add((float)cs[a][0], (float)cs[a][1], (float)cs[a][2], col); b.Add((float)cs[c][0], (float)cs[c][1], (float)cs[c][2], col); }
        GL.glLineWidth(2); b.Draw(GL.LINES); GL.glLineWidth(1);
        GL.glEnable(GL.DEPTH_TEST);
    }

    double[]? GroundHit(int mx, int my, double y)
    {
        var r = RayAt(mx, my);
        if (r == null || Math.Abs(r.Value.Dir[1]) < 1e-6) return null;
        double t = (y - r.Value.Origin[1]) / r.Value.Dir[1];
        if (t < 0) return null;
        return V(r.Value.Origin[0] + t * r.Value.Dir[0], r.Value.Origin[1] + t * r.Value.Dir[1], r.Value.Origin[2] + t * r.Value.Dir[2]);
    }

    /// the world point under the mouse (what is drawn there), or the ground
    public double[]? PointUnder(int mx, int my)
    {
        if (_mesh == null) return null;
        float z = 1;
        try
        {
            G.MakeCurrent();
            GL.glViewport(0, 0, Math.Max(G.Width, 1), Math.Max(G.Height, 1));
            _drawOverlays = false; Draw(); _drawOverlays = true;
            z = G.ReadDepth(mx, G.Height - 1 - my);
        }
        catch (Exception) { z = 1; _drawOverlays = true; }
        if (z < 0.999999f) return Unproject(mx, my, 2.0 * z - 1);
        return GroundHit(mx, my, Ground);
    }

    // ---- mouse
    void Down(object? _, MouseEventArgs e)
    {
        if (!Active) return;
        var mods = Control.ModifierKeys;
        _shiftDown = (mods & Keys.Shift) != 0; _ctrlDown = (mods & Keys.Control) != 0;
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (mods & Keys.Alt) != 0)) { MidPress(e); return; }
        if (LandMode) { if (e.Button is MouseButtons.Left or MouseButtons.Right) LandDown(e, e.Button == MouseButtons.Right); return; }
        if (e.Button == MouseButtons.Right)
        {
            if (ScatterDrawing) ScatterDrawToggle(false);
            else if (Placing != null) StopPlacing();
            else CancelOp();
            return;
        }
        if (e.Button == MouseButtons.Left) LeftPress(e.X, e.Y, _shiftDown);
    }

    void MidPress(MouseEventArgs e)
    {
        string mode = _shiftDown ? "move" : _ctrlDown ? "zoom" : "turn";
        _drag = (mode, e.X, e.Y);
        DriveT = null; CamAnim = null;
        _pivot = mode == "turn" ? PointUnder(e.X, e.Y) : null;
        if (mode == "move" && !Ortho)
        {
            var p = PointUnder(e.X, e.Y);
            if (p != null && DepthOf(p) > .5) KeepEye(DepthOf(p));
        }
    }

    public void LeftPress(int x, int y, bool shift = false)
    {
        if (ScatterDrawing && SceneMode == "3d") { ScatterClick(x, y); return; }
        if (Placing != null && SceneMode == "3d" && Oper == null) { PlaceAt(x, y); return; }
        if (Oper != null) { ConfirmOp(); return; }
        var h = HandleAt(x, y);
        if (h != null)
        {
            string kind = Gizmo switch { "move" => "move", "rotate" => "turn", _ => "scale" };
            StartOp(kind, h is "centre" ? null : h, (x, y), true);
            return;
        }
        if (SoloKey != null) return;
        foreach (var p in Picks)
            if (p.Point >= 0 && !ViewHidden.Contains(p.Key))
            {
                var s = Screen(V(p.Pos[0], p.Pos[1] + p.Lift + .5, p.Pos[2]));
                if (s != null && Math.Sqrt(Math.Pow(s.Value.X - x, 2) + Math.Pow(s.Value.Y - y, 2)) < 10) { if (Gizmo == "scale") Gizmo = "move"; GizmoButtons(); Select(p.Key); return; }
            }
        var r = RayAt(x, y);
        if (r == null) return;
        string? best = null; double bt = double.MaxValue;
        foreach (var p in Picks)
        {
            if (p.Point >= 0 || ViewHidden.Contains(SoloOf(p))) continue;
            var t = RayHit(p, r.Value.Origin, r.Value.Dir);
            if (t != null && t < bt) { bt = t.Value; best = p.Key; }
        }
        if (shift && best != null)
        {
            var cur = MultiSel.Count > 0 ? MultiSel.ToList() : Sel != null ? new List<string> { Sel } : new();
            if (cur.Contains(best)) cur.Remove(best); else cur.Add(best);
            if (cur.Count == 1) Select(cur[0]); else if (cur.Count > 0) SetMulti(cur); else Select(null);
            return;
        }
        Select(best);
    }

    void StartOp(string kind, string? axis, (int X, int Y) m, bool drag)
    {
        var p = PickOf(Sel);
        if (p == null || SceneMode != "3d" || Oper != null) return;
        if (!p.Editable) { Select(Sel); return; }
        if (kind == "scale" && !p.Scalable) { SelText($"{p.Label}: cannot be scaled{(p.Point >= 0 ? "" : " (the game's physics would not follow)")} - move / rotate only"); return; }
        if (p.Point >= 0 && kind == "scale") { SelText("Track point: G moves it, R tilts the road here. Width: the panel below."); return; }
        if (p.Pickup >= 0 && kind != "move") { SelText("Power-up: move only (G, or drag the arrows). Type: the panel below."); return; }
        var h = Handles();
        var cen = h != null ? Screen(h.Value.C) : Screen(p.Pos);
        if (kind == "turn" && axis == null) axis = p.Point >= 0 ? "x" : "z";
        Oper = new Op { Kind = kind, Axis = axis, M0 = m, Cen = cen, Hit0 = GroundHit(m.X, m.Y, p.Pos[1]), Drag = drag, Centre = h?.C ?? (double[])p.Pos.Clone(), Len = h?.Ln ?? 10, Turn0 = p.Turn, Own = AxesOf(p) };
        OpText();
        G.Invalidate();
    }

    void KeyOp(string kind)
    {
        Gizmo = kind switch { "move" => "move", "turn" => "rotate", _ => "scale" };
        GizmoButtons();
        var q = G.PointToClient(Cursor.Position);
        StartOp(kind, null, (q.X, q.Y), false);
    }

    void AxisKey(string a)
    {
        var op = Oper;
        if (op == null) return;
        if (Sel == MultiKey && op.Kind != "move") return;
        if (PickOf(Sel) is { Point: >= 0 } && op.Kind == "turn") return;
        if (op.Kind is "move" or "scale") { op.Axis = op.Axis == a ? null : a; OpText(); }
        else if (!op.Drag) { op.Axis = a; op.DTurn = 0; op.DRot = new double[2]; OpText(); G.Invalidate(); }
    }

    ((double X, double Y) C, (double X, double Y) V)? AxisScreen(Op op, string name)
    {
        var A = op.Own;
        var v = op.Kind == "move" ? AxesInfo[name].V : name == "x" ? A[0] : name == "y" ? A[2] : A[1];
        var a = Screen(op.Centre); var b = Screen(V(op.Centre[0] + op.Len * v[0], op.Centre[1] + op.Len * v[1], op.Centre[2] + op.Len * v[2]));
        if (a == null || b == null) return null;
        return (a.Value, (b.Value.X - a.Value.X, b.Value.Y - a.Value.Y));
    }

    public void HoverMotion(int x, int y, bool ctrl)
    {
        var op = Oper;
        if (op == null)
        {
            var hot = HandleAt(x, y);
            if (hot != _hot) { _hot = hot; G.Invalidate(); }
            return;
        }
        var p = PickOf(Sel);
        if (p == null) return;
        if (op.Kind is "move" or "scale" && op.Axis != null)
        {
            var ax = AxisScreen(op, op.Axis);
            if (ax != null)
            {
                var ((cx, cy), (vx, vy)) = ax.Value;
                double vv = Math.Max(vx * vx + vy * vy, 1e-6);
                if (op.Kind == "move")
                {
                    double amt = ((x - op.M0.X) * vx + (y - op.M0.Y) * vy) / vv * op.Len;
                    var v = AxesInfo[op.Axis].V;
                    op.Delta = V(v[0] * amt, v[1] * amt, v[2] * amt);
                }
                else
                {
                    double s0 = ((op.M0.X - cx) * vx + (op.M0.Y - cy) * vy) / vv, s1 = ((x - cx) * vx + (y - cy) * vy) / vv;
                    double f = Math.Abs(s0) > 1e-3 ? Math.Max(.05, s1 / s0) : 1;
                    var k = new[] { 1.0, 1, 1 };
                    k[op.Axis == "x" ? 0 : op.Axis == "z" ? 1 : 2] = f;
                    op.DScale = k;
                }
            }
        }
        else if (op.Kind == "move")
        {
            var h = GroundHit(x, y, p.Pos[1]);
            if (h != null && op.Hit0 != null) op.Delta = V(h[0] - op.Hit0[0], 0, h[2] - op.Hit0[2]);
        }
        else if (op.Kind == "scale" && op.Drag) { double s = Math.Max(.05, 1 + ((x - op.M0.X) - (y - op.M0.Y)) / 150.0); op.DScale = V(s, s, s); }
        else if (op.Cen != null)
        {
            var (cx, cy) = op.Cen.Value;
            if (op.Kind == "turn")
            {
                double a0 = Math.Atan2(op.M0.Y - cy, op.M0.X - cx), a1 = Math.Atan2(y - cy, x - cx), ang = Py.Mod(a1 - a0 + Math.PI, 2 * Math.PI) - Math.PI;
                if (ctrl) ang = Math.Round(ang / (Math.PI / 12)) * (Math.PI / 12);
                var nv = p.Point >= 0 ? V(Math.Sin(p.Turn), 0, Math.Cos(p.Turn)) : op.Axis == "z" ? V(0, 1, 0) : op.Axis == "y" ? Layout.ActorAxes3(op.Turn0, 0, 0)[2] : op.Own[0];
                var ray = RayAt((int)cx, (int)cy);
                if (ray != null && nv[0] * ray.Value.Dir[0] + nv[1] * ray.Value.Dir[1] + nv[2] * ray.Value.Dir[2] > 0) ang = -ang;
                if (op.Axis == "z") op.DTurn = ang;
                else if (op.Axis == "x") op.DRot = new[] { ang, 0 };
                else op.DRot = new[] { 0, ang };
            }
            else
            {
                double d0 = Math.Sqrt(Math.Pow(op.M0.X - cx, 2) + Math.Pow(op.M0.Y - cy, 2)), s = Math.Max(.05, Math.Sqrt(Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2)) / Math.Max(d0, 1));
                op.DScale = V(s, s, s);
            }
        }
        OpText();
        G.Invalidate();
    }

    void OpText()
    {
        var op = Oper!; string txt;
        if (op.Kind == "move")
        {
            txt = $"MOVE  x {op.Delta[0]:+0.0;-0.0;+0.0}  z {op.Delta[2]:+0.0;-0.0;+0.0}  up {op.Delta[1]:+0.0;-0.0;+0.0} m";
            txt += op.Axis != null ? "   (only along: " + (op.Axis == "x" ? "world X" : op.Axis == "y" ? "world Z (Blender Y)" : "up/down") + ")" : "   X / Y = only along one axis, Z = up/down";
        }
        else if (op.Kind == "turn")
        {
            double a = op.Axis == "z" ? op.DTurn : op.Axis == "x" ? op.DRot[0] : op.DRot[1];
            if (PickOf(Sel) is { Point: >= 0 } tp) txt = $"TILT THE ROAD HERE  {MapView.TiltText(PointTiltNow(tp))}   (Ctrl = 15° steps, most {Road.MaxTilt:0}°)";
            else txt = $"ROTATE {(op.Axis == "x" ? "X (red)" : op.Axis == "y" ? "Y (green)" : "Z (blue, up)")}  {a * 180 / Math.PI:+0;-0;+0}°   (Ctrl = 15° steps{(op.Drag ? "" : "; X / Y / Z = other axis")})";
        }
        else
        {
            var k = op.DScale;
            txt = k.Max() - k.Min() < 1e-9 ? $"SCALE  x{k[0]:0.00}" : $"SCALE  x{k[0]:0.00}  y{k[2]:0.00}  z{k[1]:0.00}";
            if (!op.Drag) txt += "   (X / Y / Z = only along one axis)";
        }
        SelText($"{txt}   |   {(op.Drag ? "let go = OK   (Esc = cancel)" : "click / Enter = OK   right-click / Esc = cancel")}");
    }

    void CancelOp() { if (Oper != null) { Oper = null; Select(Sel); } }

    /// the point's tilt, with the ring being dragged. A turn around the road's direction lowers its left side
    double PointTiltNow(Pick p) => Math.Max(-Road.MaxTilt, Math.Min(Road.MaxTilt, Math.Round(p.Tilt - (Oper?.Kind == "turn" ? Oper.DRot[0] * 180 / Math.PI : 0), 1)));

    void Esc()
    {
        if (ScatterDrawing) ScatterDrawToggle(false);
        else if (Placing != null) StopPlacing();
        else if (Oper != null) CancelOp();
        else if (SoloKey != null) SoloOff();
        else Select(null);
    }

    // ---- storing the changes
    void Store(string key, JObj e) { var n = EditsCopy(); n[key] = e; StoreAll(n); }

    public void StoreAll(JObj nw)
    {
        var o = new JObj();
        foreach (var (key, ev) in nw)
        {
            var e = (JObj)ev!; var c = new JObj();
            foreach (var (k, v) in e)
            {
                if (k is "dx" or "dy" or "dz" or "turn" or "rx" or "rz" && Math.Abs(Json.D(v)) < 1e-6) continue;
                if (k == "scale" && KVec(v).Max(x => Math.Abs(x - 1)) < 1e-6) continue;
                if (k is "hidden" or "solid" && !Json.Truthy(v)) continue;
                c[k] = v;
            }
            if (c.Count > 0) o[key] = c;
        }
        D.Snapshot();
        if (o.Count > 0) D.Map["edits"] = o; else D.Map.Remove("edits");
        D.Touched(); Dirty(); Edited?.Invoke();
        Ensure();
        _f.Say($"{o.Count} object change(s) in this map - Ctrl+Z undoes, Save + Build puts them in the game");
    }

    public void SetPoint(int i, Dictionary<string, double> values)
    {
        var pts = D.PointsAsObjects();
        if (i < 0 || i >= pts.Count) return;
        D.Snapshot();
        var p = (JObj)pts[i]!;
        foreach (var (k, v) in values) p[k] = Py.RoundN(v, 3);
        D.Touched(); Dirty(); Edited?.Invoke();
        Ensure();
        _f.Say($"Track point {i + 1}: x {p.Num("x"):0}  z {p.Num("z"):0}  height {p.Num("h"):0.0} m  width {(p.Truthy("w") ? p.Num("w") : D.Map.Num("width", 60)):0} m  {MapView.TiltText(p.Num("tilt"))} - Ctrl+Z undoes");
    }

    public void SetPickups(List<JObj> items)
    {
        bool auto = D.Map["pickups"] == null;
        D.Snapshot();
        D.Map["pickups"] = items.Select(q => (object?)q).ToList();
        D.Touched(); Dirty(); Edited?.Invoke();
        Ensure();
        _f.Say($"Power-ups: {items.Count} rows / singles{(auto ? " (the automatic rows are now your own list)" : "")} - Ctrl+Z undoes");
    }

    public void SetObjects(List<object?> objs, JObj edits)
    {
        D.Snapshot();
        if (objs.Count > 0) D.Map["objects"] = objs; else D.Map.Remove("objects");
        if (edits.Count > 0) D.Map["edits"] = edits; else D.Map.Remove("edits");
        D.Touched(); Dirty(); Edited?.Invoke();
        Ensure();
        _f.Say($"{objs.Count} placed object(s) in this map - Ctrl+Z undoes, Build + install puts them in the game");
    }

    List<JObj> ItemsCopy() => PickupList.Select(Share.Copy).ToList();

    void ConfirmOp()
    {
        if (ScatterDrawing && Oper == null) { ScatterDrawToggle(false); return; }
        var op = Oper; string? key = Sel;
        if (op == null || key == null) return;
        Oper = null;
        var p = PickOf(key);
        if (p is { Multi: true })
        {
            var nw = EditsCopy();
            foreach (var q in Members())
            {
                var (d, t, s) = MemberChange(p, q, op);
                var e = EditOf(nw, q.Key);
                e["dx"] = e.Num("dx") + d[0]; e["dy"] = e.Num("dy") + d[1]; e["dz"] = e.Num("dz") + d[2]; e["turn"] = e.Num("turn") + t;
                var k0 = KVec(e["scale"]);
                e["scale"] = KObj(k0.Select(v => v * s).ToArray());
                nw[q.Key] = e;
            }
            StoreAll(nw);
            return;
        }
        if (p is { Pickup: >= 0 })
        {
            var d = op.Delta;
            if (Math.Abs(d[0]) > 1e-6 || Math.Abs(d[2]) > 1e-6)
            {
                var items = ItemsCopy(); var it = items[p.Pickup];
                it["x"] = Py.RoundN(it.Num("x") + d[0], 1); it["z"] = Py.RoundN(it.Num("z") + d[2], 1);
                SetPickups(items);
            }
            else Select(key);
            return;
        }
        if (p is { Point: >= 0 })
        {
            var d = op.Delta; var vals = new Dictionary<string, double>();
            if (op.Kind == "turn") { double t = Math.Max(-Road.MaxTilt, Math.Min(Road.MaxTilt, Math.Round(p.Tilt - op.DRot[0] * 180 / Math.PI, 1))); if (t != p.Tilt) vals["tilt"] = t; }
            if (Math.Abs(d[0]) > 1e-6 || Math.Abs(d[2]) > 1e-6) { vals["x"] = p.Pos[0] + d[0]; vals["z"] = p.Pos[2] + d[2]; }
            if (Math.Abs(d[1]) > 1e-6) vals["h"] = Math.Max(0, p.Pos[1] - Ground + d[1]);
            if (vals.Count > 0) SetPoint(p.Point, vals); else Select(key);
            return;
        }
        var ed = EditOf(EditsCopy(), key);
        ed["dx"] = ed.Num("dx") + op.Delta[0]; ed["dy"] = ed.Num("dy") + op.Delta[1]; ed["dz"] = ed.Num("dz") + op.Delta[2];
        ed["turn"] = ed.Num("turn") + op.DTurn; ed["rx"] = ed.Num("rx") + op.DRot[0]; ed["rz"] = ed.Num("rz") + op.DRot[1];
        var k1 = KVec(ed["scale"]);
        ed["scale"] = KObj(k1.Select((v, i) => v * op.DScale[i]).ToArray());
        Store(key, ed);
    }

    void DeletePickup(Pick p) { var items = ItemsCopy(); items.RemoveAt(p.Pickup); Sel = null; SetPickups(items); }

    void PickupType(Pick p, string name)
    {
        var items = ItemsCopy();
        if (name == "mixed") items[p.Pickup].Remove("type"); else items[p.Pickup]["type"] = name;
        SetPickups(items);
    }

    public void Hide()
    {
        var p = PickOf(Sel);
        if (p is { Multi: true }) { DeleteSelection(); return; }
        if (p is { Pickup: >= 0 } && SceneMode == "3d") { DeletePickup(p); return; }
        if (p == null || !p.Editable || SceneMode != "3d" || p.Point >= 0) return;
        if (p.Key.StartsWith("obj:")) { Sel = null; var (o, e) = Without(new HashSet<int> { int.Parse(p.Key[4..]) }, EditsCopy()); SetObjects(o, e); return; }
        var ed = EditOf(EditsCopy(), p.Key);
        ed["hidden"] = true;
        Sel = null;
        Store(p.Key, ed);
    }

    void ResetObject()
    {
        var p = PickOf(Sel);
        if (p == null || p.Point >= 0 || p.Pickup >= 0) return;
        JObj Keep(JObj e) { var o = new JObj(); foreach (var k in new[] { "hidden", "solid" }) if (e.ContainsKey(k)) o[k] = e[k]; return o; }
        var nw = EditsCopy();
        if (p.Multi) foreach (var q in Members()) nw[q.Key] = Keep(EditOf(nw, q.Key));
        else nw[p.Key] = Keep(EditOf(nw, p.Key));
        StoreAll(nw);
    }

    void Clear(string what)
    {
        var p = PickOf(Sel);
        if (p == null || Oper != null) return;
        string[] keys = what == "move" ? new[] { "dx", "dy", "dz" } : what == "turn" ? new[] { "turn", "rx", "rz" } : new[] { "scale" };
        var nw = EditsCopy();
        if (p.Multi) foreach (var q in Members()) { var e = EditOf(nw, q.Key); foreach (var k in keys) e.Remove(k); nw[q.Key] = e; }
        else { if (p.Point >= 0 || p.Pickup >= 0) return; var e = EditOf(nw, p.Key); foreach (var k in keys) e.Remove(k); nw[p.Key] = e; }
        StoreAll(nw);
    }

    public void UnhideAll() { var nw = EditsCopy(); foreach (var e in nw.Values.OfType<JObj>()) e.Remove("hidden"); StoreAll(nw); }

    (List<object?>, JObj) Without(HashSet<int> gone, JObj edits)
    {
        var old = D.Map.Arr("objects") ?? new();
        var keep = Enumerable.Range(0, old.Count).Where(i => !gone.Contains(i)).ToList();
        var moved = keep.Select((j, n) => (j, n)).ToDictionary(q => q.j, q => q.n);
        var hid = ViewHidden.ToList(); ViewHidden.Clear();
        foreach (var k in hid)
            if (!k.StartsWith("obj:")) ViewHidden.Add(k);
            else if (moved.TryGetValue(int.Parse(k[4..]), out int to)) ViewHidden.Add("obj:" + to);
        var nw = new JObj();
        foreach (var (key, e) in edits)
        {
            string k = key;
            if (key.StartsWith("obj:")) { int j = int.Parse(key[4..]); if (!moved.TryGetValue(j, out int n)) continue; k = "obj:" + n; }
            nw[k] = e;
        }
        return (keep.Select(i => (object?)Share.Copy((JObj)old[i]!)).ToList(), nw);
    }

    public void DeleteSelection()
    {
        if (MultiSel.Count == 0) { Hide(); return; }
        if (SceneMode != "3d" || Oper != null) return;
        var edits = EditsCopy(); var gone = new HashSet<int>(); int hidden = 0;
        foreach (var k in MultiSel)
        {
            if (k.StartsWith("obj:")) { gone.Add(int.Parse(k[4..])); continue; }
            var p = PickOf(k);
            if (p != null && p.Editable && p.Point < 0 && p.Pickup < 0) { var e = EditOf(edits, k); e["hidden"] = true; edits[k] = e; hidden++; }
        }
        if (gone.Count == 0 && hidden == 0) return;
        MultiSel = new(); Sel = null;
        var (o, ed) = Without(gone, edits);
        SetObjects(o, ed);
        Info($"Deleted {gone.Count} object(s){(hidden > 0 ? $", hid {hidden} of the road's own" : "")} - Ctrl+Z undoes", YellowInfo);
    }

    List<int> SelObjects() => (MultiSel.Count > 0 ? MultiSel : Sel != null ? new List<string> { Sel } : new()).Where(k => k.StartsWith("obj:")).Select(k => int.Parse(k[4..])).Distinct().OrderBy(i => i).ToList();

    string NewGroupName(string prefix)
    {
        var used = D.Map.Objs("objects").Select(o => o.Str("group")).ToHashSet();
        int n = 1;
        while (used.Contains($"{prefix} {n}")) n++;
        return $"{prefix} {n}";
    }

    List<object?> ObjsCopy() => (D.Map.Arr("objects") ?? new()).Select(o => (object?)Share.Copy((JObj)o!)).ToList();

    public void GroupSelection()
    {
        var idx = SelObjects();
        if (SceneMode != "3d" || idx.Count < 2) { Info("Ctrl+G: select 2 or more of YOUR placed objects first (Shift / Ctrl+click in the list, Shift+click in the view)", YellowInfo); return; }
        string name = NewGroupName("Group");
        var objs = ObjsCopy();
        foreach (int i in idx) ((JObj)objs[i]!)["group"] = name;
        _outlOpen.Add("group:" + name);
        SetObjects(objs, EditsCopy());
        Info($"{name}: {idx.Count} objects - click its line in the list to select them all", YellowInfo);
    }

    public void UngroupSelection()
    {
        var idx = SelObjects(); var objs = ObjsCopy();
        if (SceneMode != "3d" || !idx.Any(i => ((JObj)objs[i]!).Truthy("group"))) return;
        foreach (int i in idx) ((JObj)objs[i]!).Remove("group");
        SetObjects(objs, EditsCopy());
        Info($"Ungrouped {idx.Count} object(s)", YellowInfo);
    }

    public void Duplicate()
    {
        if (MultiSel.Count > 0) { DuplicateMany(); return; }
        var p = PickOf(Sel);
        if (p == null || SceneMode != "3d" || Oper != null || !p.Key.StartsWith("obj:")) { Info("Ctrl+D: select one of YOUR placed objects first (the road's own objects can't be copied)", YellowInfo); return; }
        int i = int.Parse(p.Key[4..]);
        var objs = ObjsCopy();
        if (i >= objs.Count) return;
        var cs = BoxCorners(p);
        var o = Share.Copy((JObj)objs[i]!);
        o["x"] = Py.RoundN(o.Num("x") + Math.Max(2.0, (cs.Max(c => c[0]) - cs.Min(c => c[0])) * .25), 2);
        objs.Add(o);
        var edits = EditsCopy();
        if (edits.Obj(p.Key) != null) edits["obj:" + (objs.Count - 1)] = Share.Copy(edits.Obj(p.Key)!);
        Sel = "obj:" + (objs.Count - 1);
        SetObjects(objs, edits);
    }

    void DuplicateMany()
    {
        var idx = SelObjects();
        if (SceneMode != "3d" || Oper != null || idx.Count == 0) return;
        var cs = idx.Select(i => PickOf("obj:" + i)).Where(q => q != null).SelectMany(q => BoxCorners(q!)).ToList();
        double width = cs.Count > 0 ? cs.Max(c => c[0]) - cs.Min(c => c[0]) : 0;
        var objs = ObjsCopy(); var edits = EditsCopy(); var renamed = new Dictionary<string, string>(); var nw = new List<string>();
        foreach (int i in idx)
        {
            var o = Share.Copy((JObj)objs[i]!);
            o["x"] = Py.RoundN(o.Num("x") + Math.Max(2.0, width * .25), 2);
            if (o.Truthy("group"))
            {
                string g = o.Str("group")!;
                if (!renamed.ContainsKey(g))
                {
                    var b = g.Split(' '); bool num = b.Length >= 2 && b[^1].All(char.IsDigit);
                    string name = NewGroupName(num ? string.Join(' ', b[..^1]) : g);
                    while (renamed.ContainsValue(name)) name += "+";
                    renamed[g] = name; _outlOpen.Add("group:" + name);
                }
                o["group"] = renamed[g];
            }
            objs.Add(o);
            if (edits.Obj("obj:" + i) != null) edits["obj:" + (objs.Count - 1)] = Share.Copy(edits.Obj("obj:" + i)!);
            nw.Add("obj:" + (objs.Count - 1));
        }
        MultiSel = nw; Sel = MultiKey;
        SetObjects(objs, edits);
    }

    void Move(object? _, MouseEventArgs e)
    {
        if (!Active) return;
        if (_drag != null)
        {
            var (mode, px, py) = _drag.Value;
            int dx = e.X - px, dy = e.Y - py;
            _drag = (mode, e.X, e.Y);
            if (mode == "turn")
            {
                if (_pivot == null) { Yaw -= dx * .008; Pitch = Math.Min(1.55, Math.Max(-1.55, Pitch + dy * .006)); }
                else Orbit(-dx * .008, dy * .006, _pivot);
            }
            else if (mode == "zoom")
            {
                double nw = Dist * (1 + dy * .01);
                if (nw < 2) { var d = ViewDir(); double k = Math.Max(.3, -dy * .05); Target = V(Target[0] + d[0] * k, Target[1] + d[1] * k, Target[2] + d[2] * k); }
                else Dist = Math.Min(40000, nw);
            }
            else
            {
                var c = Screen3(Target);
                if (c != null)
                {
                    var a = Unproject(c.Value.X, c.Value.Y, c.Value.Z); var b = Unproject(c.Value.X - dx, c.Value.Y - dy, c.Value.Z);
                    if (a != null && b != null) Target = V(Target[0] + b[0] - a[0], Target[1] + b[1] - a[1], Target[2] + b[2] - a[2]);
                }
            }
            G.Invalidate();
            return;
        }
        if (LandMode) { LandMove(e.X, e.Y); return; }
        HoverMotion(e.X, e.Y, (Control.ModifierKeys & Keys.Control) != 0);
    }

    void Orbit(double dyaw, double dpitch, double[] pv)
    {
        static double[] Dir(double y, double p) { double cp = Math.Cos(p); return V(cp * Math.Sin(y), Math.Sin(p), cp * Math.Cos(y)); }
        var t = (double[])Target.Clone();
        double c = Math.Cos(dyaw), s = Math.Sin(dyaw);
        double[] r = { t[0] - pv[0], t[1] - pv[1], t[2] - pv[2] };
        t = V(pv[0] + r[0] * c + r[2] * s, pv[1] + r[1], pv[2] - r[0] * s + r[2] * c);
        Yaw += dyaw;
        double np = Math.Min(1.55, Math.Max(-1.55, Pitch + dpitch));
        var d0 = Dir(Yaw, Pitch); var d1 = Dir(Yaw, np);
        var ax = Cross(d0, d1); double sn = Len(ax);
        if (sn > 1e-9)
        {
            ax = V(ax[0] / sn, ax[1] / sn, ax[2] / sn);
            double ang = Math.Atan2(sn, d0[0] * d1[0] + d0[1] * d1[1] + d0[2] * d1[2]), ca = Math.Cos(ang), sa = Math.Sin(ang);
            r = V(t[0] - pv[0], t[1] - pv[1], t[2] - pv[2]);
            var cr = Cross(ax, r); double dot = ax[0] * r[0] + ax[1] * r[1] + ax[2] * r[2];
            t = V(pv[0] + r[0] * ca + cr[0] * sa + ax[0] * dot * (1 - ca), pv[1] + r[1] * ca + cr[1] * sa + ax[1] * dot * (1 - ca), pv[2] + r[2] * ca + cr[2] * sa + ax[2] * dot * (1 - ca));
        }
        Pitch = np;
        Target = t;
    }

    void Up(object? _, MouseEventArgs e)
    {
        if (!Active) return;
        _drag = null;
        if (LandMode) { LandUp(); return; }
        if (e.Button == MouseButtons.Left && Oper is { Drag: true }) ConfirmOp();
    }

    void Wheel(object? _, MouseEventArgs e)
    {
        if (!Active) return;
        DriveT = null; CamAnim = null;
        var p = PointUnder(e.X, e.Y);
        var eye = Eye();
        bool hit = p != null && DepthOf(p) > .5;
        if (!hit) p = Target;
        double[] v = { p![0] - eye[0], p[1] - eye[1], p[2] - eye[2] };
        double ln = Len(v); if (ln == 0) ln = 1;
        double step = e.Delta > 0 ? Math.Max(1, ln * .18) : -Math.Max(1, ln * .22);
        if (hit && e.Delta > 0) { step = Math.Min(step, ln - 1.5); if (step <= 0) return; }
        if (Ortho) { Dist = Math.Min(40000, Math.Max(5, Dist * (e.Delta > 0 ? .85 : 1.18))); G.Invalidate(); return; }
        double[] ne = { eye[0] + v[0] / ln * step, eye[1] + v[1] / ln * step, eye[2] + v[2] / ln * step };
        var d = ViewDir();
        double depth = (p[0] - ne[0]) * d[0] + (p[1] - ne[1]) * d[1] + (p[2] - ne[2]) * d[2];
        if (depth < .5) depth = Math.Max(5, ln * .5);
        Target = V(ne[0] + d[0] * depth, ne[1] + d[1] * depth, ne[2] + d[2] * depth);
        Dist = Math.Min(40000, depth);
        G.Invalidate();
    }

    public bool Key(Keys key, bool ctrl, bool shift, bool alt)
    {
        if (alt)
        {
            switch (key)
            {
                case Keys.G: Clear("move"); return true;
                case Keys.R: Clear("turn"); return true;
                case Keys.S: Clear("scale"); return true;
                case Keys.H: UnhideAll(); return true;
            }
            return false;
        }
        switch (key)
        {
            case Keys.NumPad7: ViewAxis(ctrl ? "bottom" : "top"); return true;
            case Keys.NumPad1: ViewAxis(ctrl ? "back" : "front"); return true;
            case Keys.NumPad3: ViewAxis(ctrl ? "left" : "right"); return true;
            case Keys.NumPad5: case Keys.Clear: Ortho = !Ortho; G.Invalidate(); return true;
            case Keys.Home: Reset(); return true;
            case Keys.NumPad4: OrbitStep(-1, 0); return true;
            case Keys.NumPad6: OrbitStep(1, 0); return true;
            case Keys.NumPad8: OrbitStep(0, 1); return true;
            case Keys.NumPad2: OrbitStep(0, -1); return true;
            case Keys.Add: ZoomStep(.8); return true;
            case Keys.Subtract: ZoomStep(1.25); return true;
            case Keys.Escape: Esc(); return true;
        }
        if (ctrl) return false;
        switch (key)
        {
            case Keys.G: KeyOp("move"); return true;
            case Keys.R: KeyOp("turn"); return true;
            case Keys.S: KeyOp("scale"); return true;
            case Keys.X: if (Oper != null) AxisKey("x"); else Hide(); return true;
            case Keys.Y: AxisKey("y"); return true;
            case Keys.Z: AxisKey("z"); return true;
            case Keys.Delete: DeleteSelection(); return true;
            case Keys.Enter: ConfirmOp(); return true;
            case Keys.Divide: case Keys.OemQuestion: ToggleSolo(); return true;
            case Keys.Back: if (ScatterDrawing) ScatterPop(); return true;
        }
        return false;
    }

    // ---- start camera
    Dictionary<string, List<object?>> CamViews()
    {
        var o = new Dictionary<string, List<object?>>();
        if (D.Map.Obj("start_cams") is { } c) foreach (var (k, v) in c) if (v is List<object?> { Count: > 0 } l) o[k] = l.ToList();
        return o;
    }

    void CamStore(Dictionary<string, List<object?>> cams)
    {
        D.Snapshot();
        if (cams.Count > 0) { var j = new JObj(); foreach (var (k, v) in cams) j[k] = v; D.Map["start_cams"] = j; } else D.Map.Remove("start_cams");
        D.Touched(); Edited?.Invoke();
        _f.Say(cams.Count > 0 ? "Start camera: " + string.Join(", ", cams.Select(q => $"{q.Key} {q.Value.Count} view(s)")) + " - Build + install to see it in game, Ctrl+Z undoes" : "Start camera: automatic - Build + install to see it in game");
        CamInfo();
    }

    public string CamShot = "vista";

    public void CamAdd()
    {
        DriveT = null; CamAnim = null;
        var cams = CamViews();
        var views = cams.GetValueOrDefault(CamShot) ?? new();
        if (views.Count >= IntroCam.MaxViews) { CamInfo($"   - at most {IntroCam.MaxViews} views per shot"); return; }
        var eye = Eye(); var look = (double[])Target.Clone();
        views.Add(new JObj { ["eye"] = eye.Select(v => (object?)Py.RoundN(v, 2)).ToList(), ["look"] = look.Select(v => (object?)Py.RoundN(v, 2)).ToList() });
        cams[CamShot] = views;
        CamStore(cams);
        double far = Dist3(eye, look);
        CamInfo($"   - view {views.Count}: camera {eye[1] - Ground:0} m up, {far:0} m from what it looks at{(far > 250 || eye[1] - Ground > 120 ? "  (FAR: zoom in closer - in game only the road shows from so far)" : "")}");
    }

    public void CamRemove() { var cams = CamViews(); if (cams.TryGetValue(CamShot, out var v)) { v.RemoveAt(v.Count - 1); if (v.Count == 0) cams.Remove(CamShot); CamStore(cams); } }
    public void CamAuto() { var cams = CamViews(); if (cams.Remove(CamShot)) CamStore(cams); }

    public void CamPlay()
    {
        try
        {
            var r = D.Road() ?? throw new InvalidOperationException("no road");
            double s0 = r.Dist[GenAid.RaceLines(r, D.Map).Si];
            CamAnim = (DateTime.UtcNow, IntroCam.PlayFrames(r, s0, Fe.GroundY, D.Map));
            DriveT = null;
            _anim.Start();
        }
        catch (Exception e) { CamInfo("   - cannot play: " + e.Message); }
    }

    public void ToggleReal(bool on)
    {
        RealShapes = on;
        SettingSet("real_shapes", on);
        Dirty();
    }
}

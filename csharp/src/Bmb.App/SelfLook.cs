// The robot's tests of the road tilt, the short help and the window sizes.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm
{
    /// tilts every point of the map, runs `check`, puts it back
    void WithTilt(double deg, Action<Road> check)
    {
        D.Snapshot();
        foreach (var p in D.PointsAsObjects().OfType<JObj>()) p["tilt"] = deg;
        D.Touched();
        try { check(D.Road()!); }
        finally { D.Undo(); AfterUndo(); }
    }

    void TiltTests2D(Action<string, bool> ok)
    {
        double T(int i) => ((JObj)D.Points[i]!).Num("tilt");
        Map2.Adj("tilt", 3); ok("2D tilt of a point", T(2) == 3); RefreshProps(); G.Refresh();
        var m = new Message();
        ProcessCmdKey(ref m, Keys.OemPeriod); ok("2D tilt key .", T(2) == 4);
        ProcessCmdKey(ref m, Keys.Oemcomma); ok("2D tilt key ,", T(2) == 3);
        Map2.Adj("tilt", 99); ok("2D tilt stops at its limit", T(2) == Road.MaxTilt);
        Undo(); Undo(); Undo(); Undo(); ok("2D tilt undo", !((JObj)D.Points[2]!).ContainsKey("tilt") || T(2) == 0);
        Map2.AutoTilt(); ok("2D Auto tilt", Math.Abs(T(2)) <= 12); Undo();
        int n = D.Points.Count;
        Map2.Adj("tilt", 5); Map2.Reverse(); ok("2D Reverse flips the tilt", ((JObj)D.Points[n - 3]!).Num("tilt") == -5); Undo();
        Map2.Mirror(); ok("2D Mirror flips the tilt", T(2) == -5); Undo(); Undo(); Map2.Sel = 2;
        WithTilt(8, r =>
        {
            int i = Enumerable.Range(0, r.Count).FirstOrDefault(k => r.TiltKept[k] > 0.99, -1);
            ok("Tilt: stays on the open road", r.Tilted && i >= 0);
            if (i < 0) return;
            ok("Tilt: the left side goes up, the right side down", r.Surf(i, 10) > r.Height[i] && r.Surf(i, -10) < r.Height[i]);
            ok("Tilt: the low side stays above the ground", Enumerable.Range(0, r.Count).All(k => r.Surf(k, Math.CopySign(r.WidthAt[k] / 2, -r.Tan[k])) > -1e-6));
            var (si, _) = GenAid.RaceLines(r, D.Map);
            ok("Tilt: flat at the start line", r.Tan[si] == 0);
            double steep = 0, bend = 0;
            for (int sd = -1; sd <= 1; sd += 2)
                for (int k = 2; k < r.Count - 2; k++)
                {
                    double E(int q) => r.Surf(q, sd * r.WidthAt[q] / 2);
                    steep = Math.Max(steep, Math.Abs(E(k + 1) - E(k)) / Road.Step);
                    bend = Math.Max(bend, Math.Abs(E(k + 1) - 2 * E(k) + E(k - 1)) / Road.Step);
                }
            ok($"Tilt: the road edges climb gently ({100 * steep:0.0} %, slope change {100 * bend:0.00} % per step)", steep < 0.09 && bend < 0.015);
            var (_, decks) = Bmb.Core.Layout.CollisionPieces(r, D.Info()!, D.Map);
            var slopes = decks.SelectMany(d => Enumerable.Range(0, d.Corners.Count / 2).Select(k => d.Corners[2 * k + 1][1] - d.Corners[2 * k][1]));
            ok("Tilt: the collision deck slopes with it", slopes.Any(q => q > 1));
            var c = RoadMesh.NetworkChunks(D.Map, r)[0].Cs;
            ok("Tilt: the road model slopes with it", c.SelectMany(x => x.Verts).Max(v => v[1]) > c.SelectMany(x => x.Verts).Min(v => v[1]) + 4);
            var (spots, _) = Bmb.Core.Layout.PickupSpots(r, D.Info()!, D.Map);
            Checks(); RefreshProps(); G.Refresh();
            ok("Tilt: checks + power-ups + 2D drawing run", spots.Count > 0 && (_checks.Text.Contains("road tilt") || _checks.Text.Contains("наклон дороги")));
            Map2.Sel = 2;
        });
        G.Refresh();
    }

    async Task TiltTests3D(int point, Action<string, bool> ok, Func<int, Task> pump)
    {
        double T() => ((JObj)D.Points[point]!).Num("tilt");
        V3.PanelSet("tilt", 0, "6"); V3.PanelApply(); ok("3D typed road tilt", T() == 6); Undo(); V3.Ensure();
        var key = "point:" + point;
        V3.Select(key); V3.Gizmo = "rotate"; G.Refresh(); await pump(60);
        var at = V3.TiltRingPoint();
        ok("3D road point has a tilt ring", at != null);
        if (at != null)
        {
            var (x, y) = at.Value;
            G.SimMove(x, y, MouseButtons.None); G.SimDown(x, y);
            ok("3D the ring starts a tilt", V3.Oper is { Kind: "turn" });
            G.SimMove(x + 30, y + 30); G.Refresh(); G.SimMove(x + 60, y + 50); G.Refresh(); await pump(60);
            G.SimUp(x + 60, y + 50); await pump(60);
            ok("3D dragging the ring tilts the road", T() != 0 && Math.Abs(T()) <= Road.MaxTilt);
            Undo(); V3.Ensure();
        }
        V3.Gizmo = "move"; V3.Select(key); await pump(60);
    }

    /// the eye + solo icons of the object list: a row, a group of placed objects, a whole list
    async Task RowTests(Action<string, bool> ok, Func<int, Task> pump, Action<string> shot)
    {
        SetMode("3d"); V3.Dirty(); V3.Ensure(); V3.Reset(); await pump(120);
        string before = Json.Dumps(D.World, -1);
        var key = V3.Picks.First(p => p.Key.StartsWith("obj:")).Key;
        V3.RowEye(key); G.Refresh(); ok("Eye hides one object", V3.ViewHidden.Contains(key));
        V3.RowEye(key); G.Refresh(); ok("Eye shows it again", V3.ViewHidden.Count == 0);
        V3.RowEye("grp:side"); G.Refresh(); await pump(60); shot("eye_side_hidden");
        ok("Eye hides a whole list", V3.ViewHidden.Count == V3.RowKeys("grp:side").Count && V3.ViewHidden.Count > 0);
        V3.RowEye("grp:side"); ok("Eye shows the list again", V3.ViewHidden.Count == 0);
        V3.RowEye("grp:point"); G.Refresh(); ok("Eye hides the track points", V3.RowKeys("grp:point").All(V3.ViewHidden.Contains)); V3.RowEye("grp:point");
        D.Snapshot();
        var objs = D.Map.Arr("objects")!; for (int i = 0; i < Math.Min(2, objs.Count); i++) ((JObj)objs[i]!)["group"] = "test";
        D.Touched(); V3.Dirty(); V3.Ensure(); await pump(60);
        V3.RowEye("group:test"); G.Refresh(); ok("Eye hides a group", V3.ViewHidden.Count == 2); V3.RowEye("group:test");
        V3.RowSolo("group:test"); G.Refresh(); ok("Solo of a group", V3.SoloKey == "group:test" && V3.SoloSet.Count == 2);
        await pump(60); shot("solo_group");
        V3.RowSolo("group:test"); ok("Solo of a group off", V3.SoloKey == null);
        V3.Select(null); D.Undo(); AfterUndo(); V3.Dirty(); V3.Ensure();
        V3.RowSolo("grp:side"); G.Refresh(); ok("Solo of a whole list", V3.SoloKey == "grp:side" && V3.SoloSet.Count > 2); V3.RowSolo("grp:side");
        V3.RowSolo(key); G.Refresh(); ok("Solo of one object", V3.SoloKey == key && V3.Sel == key); V3.RowSolo(key); ok("Solo of one object off", V3.SoloKey == null);
        V3.Select(null);
        ok("Eye and solo do not change the map", Json.Dumps(D.World, -1) == before);
        ok("Language box in the corner", _langBox.Visible && _langBox.Items.Count == 2);
        int wide = G.Width;
        foreach (var n in new[] { "left", "library", "objects", "right" }) ToggleFold(n);
        await pump(150); G.Refresh(); shot("fold_all");
        ok("Arrows fold all four panels", _leftP.Width == FoldW && _rightP.Width == FoldW && G.Width > wide + 500 && _folds.Values.All(f => f.Arrow.Visible && f.Arrow.Right <= f.Panel.Width));
        ok("A folded panel shows only its arrow", _folds.Values.All(f => f.Panel.Controls.Cast<Control>().All(c => c == f.Arrow || !c.Visible)));
        SetMode("2d"); SetMode("3d"); await pump(100);
        ok("Panels stay folded after a tab change", _rightP.Width == FoldW && IsFolded("library"));
        foreach (var n in new[] { "left", "library", "objects", "right" }) ToggleFold(n);
        await pump(150);
        ok($"Arrows open them again (view {G.Width} was {wide}, hidden: {string.Join(",", _folds.Values.SelectMany(f => f.Panel.Controls.Cast<Control>().Where(c => !c.Visible).Select(c => f.Panel.GetType().Name + "/" + c.GetType().Name + "/" + c.Text)))})", _rightP.Width > FoldW && G.Width <= wide + 30 && _folds.Values.All(f => f.Panel.Controls.Cast<Control>().All(c => c.Visible)));
        WithTilt(10, r => { V3.Dirty(); V3.Ensure(); int i = Enumerable.Range(0, r.Count).OrderByDescending(k => Math.Abs(r.Tan[k])).First(); V3.LookAt(r.Centre[i].X, r.Height[i], r.Centre[i].Z, r.Heading[i]); G.Refresh(); Application.DoEvents(); shot("sun_tilt"); });
        V3.ToggleShadows(false); G.Refresh(); ok("Shadows off", !V3.Shadows); V3.ToggleShadows(true); G.Refresh(); ok("Shadows on", V3.Shadows);
        V3.Dirty(); V3.Reset(); await pump(60);
    }

    /// the Car skins tab, on the scratch copy only: the list, a picture, a skin loaded, Build skins into the scratch Blur REBORN pack and back
    async Task SkinTests(Action<string, bool> ok, Func<int, Task> pump, Action<string> shot)
    {
        SetMode("skins");
        for (int i = 0; i < 600 && (_skins.Cars == null || _skins.Busy); i++) await pump(100);
        ok("Car skins: the game's cars are listed", _skins.Cars is { Count: > 0 });
        ok($"Car skins: grouped by class ({_skins.Groups} groups)", _skins.Groups >= 4);
        int open0 = _skins.OpenGroups; _skins.ToggleGroup(0); ok("Car skins: a group opens and closes", _skins.OpenGroups != open0); _skins.ToggleGroup(0);
        ok("Car skins: no Build + install on this tab", !_build.Visible);
        var astra = _skins.Cars?.FindIndex(c => c.Car == "opel_astra_extreme_legend_viii") ?? -1;
        if (astra < 0) return;
        bool shown = false; _skins.Shown = () => shown = true;
        _skins.Pick(astra); for (int i = 0; i < 80 && !shown; i++) await pump(100);
        ok("Car skins: a car's picture with its UV lines shows", shown);
        shot("skins_tab");
        string dir = Path.Combine(Path.GetDirectoryName(Bmb.Core.Paths.Data)!, "skin_test"); Directory.CreateDirectory(dir);
        TestFile = Path.Combine(dir, "pic.png"); _skins.Run("export"); ok("Car skins: Export picture", File.Exists(Path.Combine(dir, "pic.png")));
        TestFile = Path.Combine(dir, "uv.png"); _skins.Run("uv"); ok("Car skins: Export UV map", File.Exists(Path.Combine(dir, "uv.png")));
        string skin = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Bmb.Core.Paths.Data)!)!, "test_skin.png");
        if (!File.Exists(skin)) skin = Path.Combine(dir, "pic.png");
        string pack = Path.Combine(Bmb.Core.Paths.PackDir, "root.pak");
        if (!pack.Contains("scratch", StringComparison.OrdinalIgnoreCase)) { ok("Car skins: the pack test only runs on the scratch copy", false); return; }
        if (!File.Exists(pack)) { Directory.CreateDirectory(Path.GetDirectoryName(pack)!); File.Copy(Bmb.Core.Paths.RootOriginal, pack); }
        TestFile = skin; shown = false; _skins.Run("load"); for (int i = 0; i < 80 && !shown; i++) await pump(100);
        ok("Car skins: Load my skin turns it on", Bmb.Core.Skins.OnNow().ContainsKey("opel_astra_extreme_legend_viii"));
        shot("skins_loaded");
        string msg = await Task.Run(() => Bmb.Core.Skins.Install(_ => { })).ContinueWith(t => $"{t.Result.On} on");      // the core: the button refuses while a real Blur runs
        using (var p = new Bmb.Core.Pak(pack))
        {
            var m = p.Read(Bmb.Core.Skins.ModelPath("opel_astra_extreme_legend_viii"))!;
            ok($"Build skins: the skin is in Blur REBORN's root.pak ({msg})", !m.AsSpan().SequenceEqual(Bmb.Core.Skins.ModelBytes("opel_astra_extreme_legend_viii")) && m.Length == Bmb.Core.Skins.ModelBytes("opel_astra_extreme_legend_viii").Length);
        }
        // normal Blur, on the scratch copy: the skin goes into its cache root.pak, and the original comes back byte for byte
        Bmb.Core.Skins.Normal = true; await Task.Run(() => Bmb.Core.Skins.Install(_ => { }));
        string normal = Bmb.Core.Skins.NormalPack;
        if (!normal.Contains("scratch", StringComparison.OrdinalIgnoreCase)) { ok("Car skins: the normal Blur test only runs on the scratch copy", false); return; }
        using (var p = new Bmb.Core.Pak(normal))
            ok("Also in normal Blur: the skin is in its cache root.pak", !p.Read(Bmb.Core.Skins.ModelPath("opel_astra_extreme_legend_viii"))!.AsSpan().SequenceEqual(Bmb.Core.Skins.ModelBytes("opel_astra_extreme_legend_viii")));
        Bmb.Core.Skins.TurnAllOff(); await Task.Run(() => Bmb.Core.Skins.Install(_ => { }));
        ok("Uninstall: normal Blur's root.pak is the original again, byte for byte", File.ReadAllBytes(normal).AsSpan().SequenceEqual(File.ReadAllBytes(Bmb.Core.Paths.RootOriginal)));
        Bmb.Core.Skins.Normal = false;
        using (var p = new Bmb.Core.Pak(pack))
            ok("Uninstall: the game's own picture is back in root.pak", p.Read(Bmb.Core.Skins.ModelPath("opel_astra_extreme_legend_viii"))!.AsSpan().SequenceEqual(Bmb.Core.Skins.ModelBytes("opel_astra_extreme_legend_viii")));
        Bmb.Core.Skins.Remove("opel_astra_extreme_legend_viii", _skins.PicNow!);
        SetMode("2d");
    }

    async Task LookTests(Action<string, bool> ok, Func<int, Task> pump, Action<string> shot)
    {
        await SkinTests(ok, pump, shot);
        await RowTests(ok, pump, shot);
        SetMode("2d"); Map2.Sel = 4; WithTilt(10, r => { Map2.Fit(); G.Refresh(); RefreshProps(); Application.DoEvents(); shot("tilt_2d"); });
        SetMode("3d"); WithTilt(10, r => { V3.Dirty(); V3.Ensure(); int i = Enumerable.Range(0, r.Count).OrderByDescending(k => Math.Abs(r.Tan[k])).First(); V3.Select("point:4"); V3.LookAt(r.Centre[i].X, r.Height[i], r.Centre[i].Z, r.Heading[i]); V3.Gizmo = "rotate"; G.Refresh(); Application.DoEvents(); shot("tilt_3d"); V3.Gizmo = "move"; });
        V3.Dirty(); V3.Reset(); await pump(60);
        SetMode("collision"); WithTilt(10, r => { V3.Dirty(); V3.Ensure(); G.Refresh(); Application.DoEvents(); shot("tilt_collision"); }); V3.Dirty(); await pump(60);
        foreach (var m in new[] { "2d", "3d", "collision", "ai", "light", "surface", "car", "skins" })
        {
            SetMode(m); Help(); await pump(120);
            ok($"Short help: {m}", HelpCard.Last is { Visible: true } && HelpCard.Last.Width < 520 && HelpCard.Last.Height < 420);
            if (m == "2d") { shot("help_card"); HelpCard.Last!.Close(); }
            else HelpCard.Last?.Close();
        }
        ok("Short help of every window has text", HelpCard.Texts.Values.All(t => t.Lines.Length is > 0 and <= 7 && t.Lines.All(l => l.Length < 100)));
        ok("The window has the program's icon", Icon != null && Icon.Width > 0 && Icon.Handle == Look.AppIcon.Handle);
        var was = Size;
        foreach (var (w, h) in new[] { (960, 600), (1100, 680), (1280, 720), (1920, 1040) })
        {
            WindowState = FormWindowState.Normal; Size = new Size(w, h); await pump(120);
            foreach (var m in new[] { "2d", "3d", "collision", "ai", "light", "surface", "car", "skins" })
            {
                SetMode(m); await pump(140);
                FitPanels(); await pump(40);
                shot($"size_{w}_{m}");
                var right = _topRight.RectangleToScreen(_topRight.ClientRectangle);
                var last = _topTabs.Controls[_topTabs.Controls.Count - 1];
                var tabs = last.RectangleToScreen(last.ClientRectangle);
                bool fits = right.Right <= RectangleToScreen(ClientRectangle).Right && (right.Top >= tabs.Bottom - 2 || tabs.Right <= right.Left) && tabs.Right <= RectangleToScreen(ClientRectangle).Right;
                ok($"Size {w}x{h} {m}: the top bar fits, the middle has room", fits && Centre.Width >= 300 && Centre.Height >= 180);
            }
        }
        Size = was; SetMode("2d"); await pump(120);
    }
}

// The robot test: drives the window like a player, through the same mouse + key code as a real one, and reports. Used by tools\run_ui_test.sh. Works on the scratch copy only.
using System.Diagnostics;
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm
{
    public async Task SelfTest(string outDir, bool two)
    {
        FormClosed += (_, _) => { if (Lang.Ru) File.WriteAllLines(Path.Combine(outDir, "lang_missing.txt"), Lang.Missing.OrderBy(s => s)); };
        Directory.CreateDirectory(outDir);
        TopMost = true; Activate();
        var rep = new List<string> { $"window shown {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds:0.00} s after the program started" };
        int pass = 0, fail = 0;
        void Ok(string what, bool ok) { rep.Add((ok ? "PASS " : "FAIL ") + what); if (ok) pass++; else fail++; File.WriteAllLines(Path.Combine(outDir, "ui_timing.txt"), rep); }
        void Try(string what, Action a) { try { a(); Ok(what, true); } catch (Exception e) { Ok(what + " -- " + e.GetType().Name + ": " + e.Message, false); } }
        async Task Pump(int ms = 60) { Application.DoEvents(); await Task.Delay(ms); Application.DoEvents(); }
        void Shot(string name)
        {
            G.Refresh(); Application.DoEvents();
            using var b = new Bitmap(Width, Height);
            bool got;
            using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); got = PrintWindow(Handle, dc, 2); g.ReleaseHdc(dc); }   // the window itself, even when covered
            if (!got) using (var g = Graphics.FromImage(b)) g.CopyFromScreen(Location, Point.Empty, Size);
            b.Save(Path.Combine(outDir, name + ".png"));
        }
        double Frames(int n, Action step)
        {
            var sw = Stopwatch.StartNew(); double worst = 0;
            for (int i = 0; i < n; i++) { var t = Stopwatch.StartNew(); step(); G.Refresh(); worst = Math.Max(worst, t.Elapsed.TotalMilliseconds); }
            rep.Add($"   worst frame {worst:0.0} ms");
            return sw.Elapsed.TotalMilliseconds / n;
        }
        bool Press(Keys k) { var m = new Message(); return ProcessCmdKey(ref m, k); }
        await Pump(400);
        Refresh2(); await Pump(100);
        if (Environment.GetEnvironmentVariable("BMB_ONLY") == "tour")
        {
            // pictures of every tab only (for looking at the UI); nothing is changed or built
            foreach (var m in new[] { "2d", "3d", "land", "collision", "ai", "light", "surface", "car", "skins" })
            {
                SetMode(m); await Pump(250);
                if (m == "2d") { Map2.Fit(); await Pump(100); }
                Shot("tour_" + m);
            }
            SetMode("2d"); Map2.Sel = 4; RefreshProps(); await Pump(150); Shot("tour_2d_point");
            SetMode("3d"); V3.Select("obj:0"); await Pump(200); Shot("tour_3d_selected"); V3.Select(null);
            void ShotOf(Form? f, string name)
            {
                if (f == null) return;
                using var b = new Bitmap(f.Width, f.Height);
                using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc); }
                b.Save(Path.Combine(outDir, name + ".png"));
            }
            PlayDialog(); await Pump(200); ShotOf(LastPreview, "tour_play"); LastPreview?.Close();
            Help(); await Pump(200); ShotOf(LastPreview, "tour_help"); LastPreview?.Close();
            // the dark message window: a timer takes its picture and closes it (it is modal)
            var tm = new System.Windows.Forms.Timer { Interval = 400 };
            tm.Tick += (_, _) => { tm.Stop(); var bx = Application.OpenForms.Cast<Form>().LastOrDefault(q => q != this && q.Modal); ShotOf(bx, "tour_msgbox"); bx?.Close(); };
            tm.Start();
            MessageBox.Show(this, "Flat Earth / drifting : length 7392 m\n\nBuild anyway?", "Problems", MessageBoxButtons.YesNo);
            SetMode("2d"); SettingsMenu(_save); await Pump(200); Shot("tour_menu_settings"); LastMenu?.Close();
            TreeMenu(_tree, new Point(40, 40)); await Pump(200); Shot("tour_menu_tree"); LastMenu?.Close();
            SetMode("2d");
            File.WriteAllLines(Path.Combine(outDir, "ui_timing.txt"), rep);
            return;
        }
        if (Environment.GetEnvironmentVariable("BMB_ONLY") == "land")
        {
            // a short check of the Landscape / Surface / ramps parts only (the full robot run is for releases)
            await LandTests(Ok, ms => Pump(ms), Shot);
            var qsw = Stopwatch.StartNew(); string qb = ""; BuildFinished = m => qb = m;
            Build();
            while (Building && qsw.Elapsed.TotalSeconds < 180) await Pump(100);
            rep.Add($"Build + install from the window: {qsw.Elapsed.TotalSeconds:0.0} s - {qb}");
            Ok("Build + install finished", qb.StartsWith("Built"));
            await Pump(1000);
            File.Delete(Doc.Autosave);
            rep.Add($"RESULT {pass} passed, {fail} failed");
            File.WriteAllLines(Path.Combine(outDir, "ui_timing.txt"), rep);
            return;
        }
        string worldAtStart = Json.Dumps(D.World, -1);
        foreach (var m in new[] { "2d", "3d", "collision", "ai", "light", "surface", "car" })
        {
            var sw = Stopwatch.StartNew();
            SetMode(m); await Pump(150);
            rep.Add($"tab {m}: {sw.ElapsedMilliseconds - 150} ms to show");
            await Pump(300);
            Shot("win_" + m);
        }
        // the loading bar: a first visit shows it, the window comes back with every tab
        { var was = LoadShown; bool shot = false; LoadStepped = p => { if (p >= 60 && !shot) { shot = true; Shot("tab_loading"); } }; _tabMs.Remove("light"); SetMode("light"); LoadStepped = null; await Pump(200); Ok("Tab change: the bar shows on a slow tab", LoadShown > was && !_load.Up); }
        foreach (var m in new[] { "3d", "car", "2d", "ai", "surface", "collision", "light" }) { var t = Stopwatch.StartNew(); SetMode(m); await Pump(60); rep.Add($"tab {m} again: {t.ElapsedMilliseconds - 60} ms, last time {_tabMs[m]} ms"); Ok($"Tab change to {m}: bar gone", !_load.Up); }
        SetMode("2d"); await Pump();
        Map2.Fit();
        rep.Add($"2D pan: {Frames(60, () => Map2.Ox += 1):0.00} ms per frame");
        var p0 = (JObj)D.PointsAsObjects()[Math.Min(3, D.Points.Count - 2)]!; double px0 = p0.Num("x");
        rep.Add($"2D drag a road point (road made again): {Frames(60, () => { p0["x"] = p0.Num("x") + 0.5; D.Touched(); }):0.00} ms per frame");
        p0["x"] = px0; D.Touched();
        SetMode("3d"); await Pump(200);
        double y0 = 0.7;
        rep.Add($"3D orbit: {Frames(120, () => { y0 += 0.01; V3.Yaw = y0; }):0.00} ms per frame, {V3.Tris:N0} triangles");
        var sw2 = Stopwatch.StartNew(); V3.Dirty(); G.Refresh(); rep.Add($"3D scene made again after an edit: {sw2.ElapsedMilliseconds} ms");

        // ---- 2D tab
        SetMode("2d"); Map2.Fit(); await Pump();
        int Pts() => D.Points.Count;
        (int X, int Y) Scr(double x, double z) { var (a, b) = Map2.ToScreen(x, z); return ((int)a, (int)b); }
        var road = D.Road()!; int before = Pts();
        SetTool("add"); G.SimClick(200, 120); Ok("2D Add point", Pts() == before + 1);
        Undo(); Ok("2D Undo", Pts() == before);
        Redo(); Ok("2D Redo", Pts() == before + 1); Undo();
        var q = (JObj)D.PointsAsObjects()[4]!; var (qx, qy) = Scr(q.Num("x"), q.Num("z")); double oldX = q.Num("x");
        SetTool("move"); G.SimDown(qx, qy); for (int i = 1; i <= 10; i++) G.SimMove(qx + i * 4, qy); G.SimUp(qx + 40, qy);
        Ok("2D Move drags a point", Math.Abs(((JObj)D.Points[4]!).Num("x") - oldX) > 10); Undo();
        var seg = road.At(road.Length * 0.3); var (ix, iy) = Scr(seg.X, seg.Z);
        SetTool("insert"); G.SimClick(ix, iy); Ok("2D Insert", Pts() == before + 1);
        SetTool("erase"); var ins = Road.NormPoint(D.Points[Map2.Sel >= 0 ? Map2.Sel : 1], 60); var (ex, ey) = Scr(ins.X, ins.Z); G.SimClick(ex, ey); Ok("2D Erase", Pts() == before);
        int rows = Map2.PickupItems().Count; D.Undo(); D.Redo();
        var mid = road.At(road.Length * 0.55); var (rx, ry) = Scr(mid.X, mid.Z);
        rows = D.Map["pickups"] is List<object?> lp ? lp.Count : Bmb.Core.Layout.PickupItems(road, D.Info()!, D.Map).Count;
        SetTool("row"); G.SimClick(rx, ry); Ok("2D Power-up row", Map2.PickupItems().Count == rows + 1);
        Press(Keys.D3); Ok("2D key 3 changes the selected power-up", ((JObj)D.Map.Arr("pickups")![^1]!).Str("type") == "Shock");
        Map2.RowN(1); Ok("2D row: more power-ups in it", ((JObj)D.Map.Arr("pickups")![^1]!).Int("n") > 0);
        Map2.MixedRow(); Ok("2D row: mixed types", !((JObj)D.Map.Arr("pickups")![^1]!).ContainsKey("type"));
        RefreshProps(); await Pump();
        SetTool("single"); G.SimClick(rx + 3, ry); Ok("2D Single power-up", Map2.PickupItems().Count == rows + 2);
        Undo(); Undo(); Undo(); Undo(); Undo();
        int br = D.Map.Arr("branches")?.Count ?? 0, sp = D.Map.Arr("splits")?.Count ?? 0;
        var s1 = road.At(road.Length * 0.62); var s2 = road.At(road.Length * 0.72); var off = road.At(road.Length * 0.67, 120);
        (int X, int Y) A = Scr(s1.X, s1.Z), Bb = Scr(s2.X, s2.Z), C2 = Scr(off.X, off.Z);
        SetTool("branch"); G.SimClick(A.X, A.Y); G.SimMove(C2.X, C2.Y, MouseButtons.None); G.Refresh(); G.SimClick(C2.X, C2.Y); G.SimClick(Bb.X, Bb.Y);
        Ok("2D Branch made + selected", (D.Map.Arr("branches")?.Count ?? 0) == br + 1 && Map2.SelBranch == br);
        Map2.NetAdj("width", 2); RefreshProps(); await Pump();
        Ok("2D Branch width", ((JObj)D.Map.Arr("branches")![br]!).Num("width") == Network.BranchWidth + 2);
        var bh = Map2.NetHandles().First(h => h.Kind == "bfrom" && h.I == br); var hs = Scr(bh.X, bh.Z); double from0 = ((JObj)D.Map.Arr("branches")![br]!).Num("from_m");
        SetTool("move"); G.SimDown(hs.X, hs.Y); var mvTo = road.At(from0 + 60); var ms = Scr(mvTo.X, mvTo.Z); G.SimMove(ms.X, ms.Y); G.SimUp(ms.X, ms.Y);
        Ok("2D Branch handle drags", Math.Abs(((JObj)D.Map.Arr("branches")![br]!).Num("from_m") - from0) > 10);
        SetTool("insert"); Map2.SelBranch = br; var bp = (JObj)((JObj)D.Map.Arr("branches")![br]!).Arr("points")![0]!; var cc = Scr(bp.Num("x") - 20, bp.Num("z")); int pts0 = ((JObj)D.Map.Arr("branches")![br]!).Arr("points")!.Count;
        G.SimClick(cc.X, cc.Y); Ok("2D Insert into a branch", ((JObj)D.Map.Arr("branches")![br]!).Arr("points")!.Count == pts0 + 1);
        SetTool("row"); var bro = D.Net()!.Branches[br].R; var bm = bro.At(bro.Length / 2); var bs = Scr(bm.X, bm.Z); int rw = Map2.PickupItems().Count; G.SimClick(bs.X, bs.Y);
        Ok("2D Row on a branch", Map2.PickupItems().Count == rw + 1);
        Undo(); while ((D.Map.Arr("branches")?.Count ?? 0) > br) Undo();
        Map2.ResetSelection();
        SetTool("split"); G.SimClick(A.X, A.Y); G.SimClick(Bb.X, Bb.Y); Ok("2D Lane split", (D.Map.Arr("splits")?.Count ?? 0) == sp + 1 && Map2.SelSplit == sp);
        Map2.NetLanes(3); RefreshProps(); Ok("2D 3 lanes", ((JObj)D.Map.Arr("splits")![sp]!).Int("lanes") == 3); G.Refresh(); Undo(); Undo(); Map2.ResetSelection();
        SetTool("measure"); G.SimClick(A.X, A.Y); G.SimMove(Bb.X, Bb.Y, MouseButtons.None); G.Refresh(); G.SimClick(Bb.X, Bb.Y); G.SimClick(C2.X, C2.Y);
        Ok("2D Measure text", Map2.MeasureText(Map2.Measure!).StartsWith("path"));
        Press(Keys.Back); Ok("2D Measure back", Map2.Measure!.Count == 2); Press(Keys.Enter); Ok("2D Measure finish", Map2.MeasureDone); Press(Keys.Escape); Ok("2D Measure clear", Map2.Measure == null);
        SetTool("select"); Map2.Sel = 2; Refresh2(); await Pump();
        TiltTests2D(Ok);
        double w0 = Road.NormPoint(D.Points[2], D.Map.Num("width", 60)).W; Map2.Adj("w", 5); Ok("2D width of a point", ((JObj)D.Points[2]!).Num("w") == w0 + 5); Undo();
        Map2.Adj("corner", 5); Ok("2D corner radius", ((JObj)D.Points[2]!).Num("corner") == Road.MinCorner); Undo();
        Map2.ToggleCorner(); Ok("2D corner on", ((JObj)D.Points[2]!).Truthy("corner")); Undo();
        Map2.Adj("h", 1); Ok("2D height", ((JObj)D.Points[2]!).Num("h") == 1); Undo();
        var tp = D.Points.Count; var nb = Share.Copy((JObj)D.Points[3]!);
        Map2.Sel = 2; Press(Keys.T); Ok("2D 90-degree turn", Math.Abs(((JObj)D.Points[3]!).Num("x") - nb.Num("x")) + Math.Abs(((JObj)D.Points[3]!).Num("z") - nb.Num("z")) > 1 && ((JObj)D.Points[2]!).Truthy("corner")); Undo();
        double cz0 = D.Points.Average(p => ((JObj)p!).Num("z")); var z3 = ((JObj)D.Points[3]!).Num("z");
        Map2.Mirror(); Ok("2D Mirror is about the middle z", Math.Abs(((JObj)D.Points[3]!).Num("z") - (2 * cz0 - z3)) < 1e-6); G.Refresh(); Undo();
        Map2.Reverse(); G.Refresh(); Undo(); Map2.Sel = -1;
        Map2.MoveLine("start", 20); Ok("2D Start at +", D.Map["start_m"] != null); Undo();
        Press(Keys.H); await Pump(); Ok("2D Help window", LastPreview is { Visible: true }); LastPreview!.Close();
        // lap track by dropping the end on the start
        bool lap0 = D.Map.Truthy("laps");
        if (!lap0)
        {
            SetTool("move");
            var pe = (JObj)D.Points[^1]!; var ps = (JObj)D.Points[0]!; var e0 = Scr(pe.Num("x"), pe.Num("z")); var st = Scr(ps.Num("x"), ps.Num("z")); int c0 = Pts();
            G.SimDown(e0.X, e0.Y); G.SimMove(st.X + 3, st.Y + 3); G.Refresh(); G.SimUp(st.X + 3, st.Y + 3);
            Ok("2D End dropped on start = lap track", D.Map.Truthy("laps") && Pts() == c0 - 1); Undo();
        }
        var vLaps = Share.Copy(D.Map); ToggleLaps(!lap0); Ok("Panel Lap track tick", D.Map.Truthy("laps") != lap0); Undo();
        // tree + world
        int maps0 = D.Loc.Arr("maps")!.Count, nloc = D.World.Arr("locations")!.Count;
        TestAnswers.Enqueue("Robot map"); NewMap(); Ok("Tree New map", D.Loc.Arr("maps")!.Count == maps0 + 1 && D.Map.Obj("scenery") != null); await Pump();
        DupMap(); Ok("Tree Duplicate map (300 m away)", D.Map.Str("name") == "Robot map copy"); Undo();
        TestAnswers.Enqueue("1"); TestAnswers.Enqueue("Renamed"); Rename(); Ok("Tree Rename map", D.Map.Str("name") == "Renamed"); Undo();
        TestAnswers.Enqueue("2"); TestAnswers.Enqueue("Renamed loc"); Rename(); Ok("Tree Rename location", D.Loc.Str("name") == "Renamed loc"); Undo();
        DeleteMap(); Ok("Tree Delete map", D.Loc.Arr("maps")!.Count == maps0); Undo(); Undo();
        Ok("Tree back as it was", D.Loc.Arr("maps")!.Count == maps0);
        D.Mi = 0; D.Touched(); Tree();
        TestAnswers.Enqueue("Robot loc"); NewLocation(); Ok("Tree New location", D.World.Arr("locations")!.Count == nloc + 1); DeleteLocation(); Ok("Tree Delete location", D.World.Arr("locations")!.Count == nloc); Undo(); Undo();
        D.Li = 0; D.Mi = 0; D.Touched(); Tree(); Map2.Fit();
        string bm0 = Path.Combine(outDir, "robot.blurmap");
        TestFile = bm0; ExportMap(); Ok("Export .blurmap", File.Exists(bm0) && Share.Read(bm0).Map.Arr("points")!.Count == D.Points.Count);
        TestFile = bm0; ImportMap(); Ok("Import .blurmap (own location)", D.World.Arr("locations")!.Count == nloc + 1 && D.Map.Str("name")!.Contains('(')); Undo();
        D.Li = 0; D.Mi = 0; D.Touched(); Tree(); Map2.Fit();
        // panel: minimap, scenery
        string mmf = Path.Combine(outDir, "robot_minimap.png");
        TestFile = mmf; MinimapExport(); Ok("Minimap Export road picture", File.Exists(mmf) && File.Exists(mmf + ".json"));
        TestFile = mmf; MinimapPick(); Ok("Minimap Pick own", D.Map.Obj("minimap")?.Str("image") == Path.GetFullPath(mmf)); RefreshProps();
        Try("Minimap Preview", () => { MinimapPreview(); LastPreview!.Close(); });
        Undo(); RefreshProps();
        double step0 = Bmb.Core.Layout.PickupStep(D.Map); MapNum("pickup_step", 25, 50, 600, 150); Ok("Panel Rows every", D.Map.Num("pickup_step") == step0 + 25); Undo();
        Press(Keys.Control | Keys.Z); await Pump();
        Redo();

        // ---- AI tab
        SetMode("ai"); await Pump();
        AiRacing(); Ok("AI Racing line", D.Map.Obj("ai_path")?.Str("mode") == "drawn"); Shot("ai_racing");
        AiLaneChanged(20); Ok("AI lane width", D.Map.Obj("ai_path")!.Num("lane") == 20); Undo(); AiLaneChanged(12); Undo(); AiAuto();
        Map2.AiPen = true;
        var ap = new[] { road.At(road.Length * .2), road.At(road.Length * .22, 6), road.At(road.Length * .24) };
        var a0 = Scr(ap[0].X, ap[0].Z); G.SimDown(a0.X, a0.Y);
        foreach (var w in ap) { var u = Scr(w.X, w.Z); G.SimMove(u.X, u.Y); }
        var a2 = Scr(ap[2].X, ap[2].Z); G.SimUp(a2.X, a2.Y);
        Ok("AI Pen stroke", D.Map.Obj("ai_path")?.Str("mode") == "drawn"); Map2.AiPen = false; Undo();

        // ---- Surface tab
        SetMode("surface"); await Pump(); Map2.Fit(); G.Refresh();
        var sf0 = road.At(road.Length * 0.4); var sc = Scr(sf0.X, sf0.Z); int cells = Surface.Settings(D.Map).Paint.Count; Map2.BrushSize = 12;
        G.SimDown(sc.X, sc.Y); G.SimMove(sc.X + 12, sc.Y); G.SimUp(sc.X + 12, sc.Y); Ok("Surface brush paints", Surface.Settings(D.Map).Paint.Count > cells); int cells1 = Surface.Settings(D.Map).Paint.Count;
        G.Refresh();
        G.SimDown(sc.X, sc.Y, MouseButtons.Right); G.SimMove(sc.X + 12, sc.Y, MouseButtons.Right); G.SimUp(sc.X + 12, sc.Y, MouseButtons.Right);
        Ok("Surface right button removes", Surface.Settings(D.Map).Paint.Count < cells1); Undo();
        SurfSet("sides", 4.0); Ok("Surface sides", Surface.Settings(D.Map).Sides == 4); G.Refresh(); Shot("surface_sides"); SurfSet("road", "dirt"); G.Refresh(); Ok("Surface road type", Surface.Settings(D.Map).Road == "dirt"); Undo(); Undo();
        SurfClear(); Ok("Surface Clear paint", Surface.Settings(D.Map).Paint.Count == 0); if (cells > 0) Undo();
        SurfFeel("extreme"); Ok("Surface grip", Surface.LocFeel(D.World, D.Loc) == "extreme"); Undo(); Undo();
        while (D.UndoCount > 0 && Json.Dumps(D.World, -1) != worldAtStart && false) Undo();

        // ---- Lighting tab
        SetMode("light"); await Pump(300);
        LightSet(preset: "Brighton"); await Pump(); Ok("Lighting preset", Lighting.Effective(D.Loc).Preset == "Brighton"); Shot("light_brighton");
        LightSet("sun", new[] { 1.0, 0.5, 0.5, 2.0 }); Ok("Lighting value", D.Loc.Obj("lighting")!.Obj("values")!.Arr("sun") != null);
        LightSet(many: new() { ["object_light"] = new[] { 1.0, .97, .92, .14 }, ["object_light_up"] = new[] { .6 } }); Ok("Lighting Light up placed objects", LightUi.Value(D.Loc, "object_light")[3] == .14);
        LightSet(reset: true); Ok("Lighting Reset", LightUi.Value(D.Loc, "sun")[3] != 2.0); Undo(); Undo(); Undo(); Undo();
        string pano = Path.Combine(outDir, "robot_sky.png");
        using (var bmp = new Bitmap(512, 256)) { using var g2 = Graphics.FromImage(bmp); g2.Clear(Color.SkyBlue); g2.FillEllipse(Brushes.White, 200, 30, 60, 60); bmp.Save(pano); }
        TestFile = pano; LightPickSky(); await Pump(100); Ok("Lighting Custom sky", D.Loc.Obj("lighting")?.Str("sky_image") == "skies/robot_sky.png"); Shot("light_sky"); LightSky(null); Ok("Lighting Use the preset's sky", D.Loc.Obj("lighting")?.Str("sky_image") == null); Undo(); Undo();
        try { File.Delete(Path.Combine(Paths.Tracks, "skies", "robot_sky.png")); } catch (IOException) { }

        // ---- Custom car tab
        SetMode("car"); await Pump(300);
        int cars0 = D.World.Arr("cars")?.Count ?? 0;
        _car.GetType().GetMethod("Add", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(_car, null); await Pump(300);
        var car = _car.Car()!;
        Ok("Car New", D.World.Arr("cars")!.Count == cars0 + 1 && CarUi.Total(car) <= CarUi.Budget((int)car.Int("class")));
        var tcs = new TaskCompletionSource(); void Done() => tcs.TrySetResult(); _car.PictureDone += Done; _car.Refresh2();
        await Task.WhenAny(tcs.Task, Task.Delay(8000)); _car.PictureDone -= Done;
        Ok("Car picture drawn", tcs.Task.IsCompleted);
        Shot("car_new");
        long budget = CarUi.Budget((int)car.Int("class"));
        _car.SetStat("speed", 10); _car.SetStat("grip", 10); _car.SetStat("drift", 10); _car.SetStat("strength", 10); _car.SetStat("acceleration", 10);
        Ok("Car stats stay in the budget", CarUi.Total(_car.Car()!) <= budget);
        foreach (var (k, _) in CarUi.StatRows) { var (lo, hi) = CarsData.Limit((int)_car.Car()!.Int("class"), k); long v = _car.Car()!.Obj("stats")!.Int(k); Ok($"Car {k} inside the class", v >= lo && v <= hi); }
        Undo(); Undo(); Undo(); Undo(); Undo();
        _car.WheelHover("RatRod_NA_Stock", new Point(Left + 700, Top + 200)); for (int i = 0; i < 60 && _car.WheelShown == null; i++) await Pump(50);
        Ok("Car Wheels menu: hover picture", _car.WheelShown == "RatRod_NA_Stock"); Shot("wheel_hover"); _car.WheelHoverHide();
        Ok("Car Wheels menu stays open with the picture, every time", await _car.TestWheelMenu());
        int wheelPics = 0; var wsw = Stopwatch.StartNew();
        var noPic = new List<string>(); foreach (var gc in CarUi.Pickable()) if (CarUi.WheelPicture(gc.Name) is { } wb) { wheelPics++; if (gc.Name.Contains("MustangGTR") || gc.Name.Contains("RatRod") || gc.Name.Contains("Navara_OffRoad") || gc.Name.Contains("Astra")) wb.Save(Path.Combine(outDir, "wheel_" + gc.Name + ".png")); } else noPic.Add(gc.Name);
        rep.Add("no wheel picture: " + string.Join(", ", noPic));
        rep.Add($"wheel pictures: {wheelPics} of {CarUi.Pickable().Count} in {wsw.ElapsedMilliseconds} ms");
        Ok("Car Wheels menu: a picture for every car", wheelPics == CarUi.Pickable().Count);
        string bodyGlb = Path.Combine(Paths.Tracks, "cars");
        var glbs = Directory.Exists(bodyGlb) ? Directory.GetFiles(bodyGlb, "*.glb") : Array.Empty<string>();
        if (glbs.Length > 0)
        {
            TestFile = glbs[0]; var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            _car.GetType().GetMethod("ImportBody", flags)!.Invoke(_car, null); await Pump(400);
            Ok("Car Import body (.glb)", _car.Car()!.Truthy("body_glb"));
            _car.GetType().GetMethod("Fit", flags)!.Invoke(_car, new object?[] { "size", 0.02 }); await Pump(400);
            Ok("Car fit the body", _car.Car()!.Obj("glb_fit")?.Num("size") == 1.02);
            var flabel = (Label)_car.GetType().GetField("_roomLabel", flags)!.GetValue(_car)!; Ok("Car room text", flabel.Text.Length > 10);
            Shot("car_glb"); Undo(); Undo();
        }
        Undo();
        Ok("Car undo", (D.World.Arr("cars")?.Count ?? 0) == cars0);

        // ---- 3D tab
        SetMode("3d"); await Pump(300); V3.Top(); G.Refresh(); await Pump(); G.Refresh();
        Shot("three_d_top");
        var lampI = V3.Picks.FindIndex(o => o.Key.StartsWith("lamp"));
        Ok("3D lists the objects", lampI >= 0 && V3.Picks.Any(o => o.Point >= 0) && V3.Picks.Any(o => o.Pickup >= 0));
        Ok("3D no scene error", !V3.Stats.StartsWith("cannot"));
        string key = V3.Picks[lampI].Key; V3.Select(key); await Pump();
        Press(Keys.R); var sr = V3.Oper; Ok("3D R starts a turn", sr?.Kind == "turn"); Press(Keys.Escape); Ok("3D Esc cancels it", V3.Oper == null);
        V3.Select(key); var lamp = V3.PickOf(key)!;
        V3.Gizmo = "rotate"; V3.PanelShow(lamp); V3.Gizmo = "move";
        var ctr = V3.Screen(lamp.Pos)!.Value; var (cx0, cy0) = ((int)ctr.X, (int)ctr.Y);
        G.Refresh();
        var hc = BoxC(lamp);
        var hs2 = V3.Screen(hc)!.Value;
        G.SimDown((int)hs2.X, (int)hs2.Y); for (int i = 1; i <= 8; i++) G.SimMove((int)hs2.X + i * 6, (int)hs2.Y); G.SimUp((int)hs2.X + 48, (int)hs2.Y);
        Ok("3D drag the middle handle moves it", (D.Map.Obj("edits")?.Obj(key)?.Num("dx") ?? 0) != 0 || (D.Map.Obj("edits")?.Obj(key)?.Num("dz") ?? 0) != 0); Undo(); V3.Ensure();
        V3.Select(key); V3.PanelSet("loc", 0, (V3.PickOf(key)!.Pos[0] + 5).ToString(System.Globalization.CultureInfo.InvariantCulture)); V3.PanelApply();
        Ok("3D typed value moves it", Math.Abs(D.Map.Obj("edits")?.Obj(key)?.Num("dx") - 5 ?? 9) < 1e-6); Undo(); V3.Ensure();
        V3.Select(key); Press(Keys.X); Ok("3D X hides", D.Map.Obj("edits")?.Obj(key)?.Truthy("hidden") == true); V3.UnhideAll(); Ok("3D Alt+H shows again", D.Map.Obj("edits")?.Obj(key) == null); Undo(); Undo(); V3.Ensure();
        // library: place + group + duplicate + delete
        var lib = Bmb.Core.Layout.Catalog().Values.Where(e => e.Str("kind") == "scenery").Take(3).ToList();
        int nobj = D.Map.Arr("objects")?.Count ?? 0;
        V3.PickLibrary(lib[0].Str("id")!); int mx = G.Width / 2, my = G.Height / 2;
        V3.Top(); G.Refresh();
        G.SimClick(mx, my); G.SimClick(mx + 40, my); Ok("3D library: placed twice", (D.Map.Arr("objects")?.Count ?? 0) == nobj + 2); Press(Keys.Escape); Ok("3D library: Esc stops placing", V3.Placing == null);
        V3.Ensure();
        string oa = "obj:" + nobj, ob = "obj:" + (nobj + 1);
        Ok("3D placed object is selected", V3.Sel == ob);
        var pob = V3.PickOf(ob)!; bool scal = pob.Scalable;
        V3.Select(ob); V3.PanelSet("scale", 0, "2"); V3.PanelApply(); Ok("3D typed scale", D.Map.Obj("edits")?.Obj(ob)?["scale"] != null); Undo(); V3.Ensure();
        V3.Select(ob); V3.PanelSet("rot", 0, "10"); V3.PanelApply(); Ok("3D typed tilt", Math.Abs((D.Map.Obj("edits")?.Obj(ob)?.Num("rx") ?? 0) - 10 * Math.PI / 180) < 1e-6); G.Refresh(); Undo(); V3.Ensure();
        V3.Select(ob); Press(Keys.Control | Keys.D); V3.Ensure(); Ok("3D Ctrl+D copies", (D.Map.Arr("objects")?.Count ?? 0) == nobj + 3); Undo(); V3.Ensure();
        V3.SetMulti(new() { oa, ob }); Press(Keys.Control | Keys.G); V3.Ensure(); Ok("3D Ctrl+G groups", D.Map.Objs("objects").Count(o => o.Truthy("group")) >= 2); G.Refresh(); Shot("three_d_group");
        V3.SetMulti(new() { oa, ob }); G.Refresh(); Press(Keys.S); var opS = V3.Oper; Ok("3D group: S scales all", opS?.Kind == "scale"); Press(Keys.Escape);
        Press(Keys.Control | Keys.Shift | Keys.G); Ok("3D Ctrl+Shift+G ungroups", D.Map.Objs("objects").All(o => !o.Truthy("group"))); Undo(); V3.Ensure(); V3.SetMulti(new() { oa, ob });
        Press(Keys.Delete); Ok("3D Delete many", (D.Map.Arr("objects")?.Count ?? 0) == nobj); Undo(); Undo(); Undo(); V3.Ensure();
        while ((D.Map.Arr("objects")?.Count ?? 0) > nobj) Undo();
        V3.Ensure();
        // solo, points, power-ups
        V3.Select(key); Press(Keys.Divide); Ok("3D Solo", V3.SoloKey == key); G.Refresh(); Shot("three_d_solo"); Press(Keys.Escape); Ok("3D Solo off", V3.SoloKey == null);
        var ptp = V3.Picks.First(p => p.Point == 2); V3.Select(ptp.Key); await Pump();
        await TiltTests3D(ptp.Point, Ok, ms => Pump(ms));
        ptp = V3.Picks.First(p => p.Point == 2); V3.Select(ptp.Key); await Pump();
        V3.PanelSet("loc", 2, "4"); V3.PanelApply(); Ok("3D typed road point height", ((JObj)D.Points[2]!).Num("h") == 4); Undo(); V3.Ensure();
        var pk = V3.Picks.First(p => p.Pickup >= 0); V3.Select(pk.Key); await Pump(); Ok("3D power-up panel", V3.Sel == pk.Key); V3.PanelSet("loc", 0, (V3.PickupList[pk.Pickup].Num("x") + 3).ToString(System.Globalization.CultureInfo.InvariantCulture)); V3.PanelApply(); Ok("3D typed power-up place", D.Map["pickups"] is List<object?>); Undo(); V3.Ensure();
        Ok("3D lists: clicks, Ctrl+click, menu, library, hover window", V3.TestLists() == 5);
        V3.Ensure();
        // start camera
        V3.CamAdd(); Ok("3D start camera: Add view", D.Map.Obj("start_cams")?.Arr("vista")?.Count == 1); V3.CamPlay(); await Pump(120); G.Refresh(); Ok("3D start camera: Play", V3.CamAnim != null); V3.StopDrive(); V3.CamRemove(); Ok("3D start camera: Remove", D.Map.Obj("start_cams") == null); Undo(); Undo();
        V3.StartDrive(); await Pump(120); G.Refresh(); Ok("3D Drive", V3.DriveT != null); V3.StopDrive();
        V3.Ortho = true; G.Refresh(); V3.Ortho = false;
        // path scatter
        V3.Top(); G.Refresh(); V3.ScatterOpen(); await Pump();
        foreach (var e in lib) V3.ScatterLibAdd(e);
        G.SimClick(mx - 100, my); G.SimClick(mx + 100, my + 40); await Pump();
        Ok("3D Path scatter preview", V3.Sc!.Spots.Count > 0); G.Refresh(); Shot("three_d_scatter");
        int n0 = D.Map.Arr("objects")?.Count ?? 0; int want = V3.Sc.Spots.Count(s => s.Fits);
        V3.ScatterApply(); V3.Ensure(); Ok("3D Path scatter adds a group", (D.Map.Arr("objects")?.Count ?? 0) == n0 + want && D.Map.Objs("objects").Any(o => (o.Str("group") ?? "").StartsWith("Scatter")));
        V3.ScatterClose(); Undo(); V3.Ensure();
        // real shapes + hover pictures
        int okm = 0, tried = 0;
        var cat = Bmb.Core.Layout.Catalog().Values.Where(e => e.Str("kind") != "import" && e.Truthy("model")).ToList();
        foreach (var e in cat.Where((_, i) => i % Math.Max(1, cat.Count / 60) == 0)) { tried++; if (Previews.Get(e.Str("model")!, wait: true) is { Count: > 0 }) okm++; }
        rep.Add($"Real shapes: {okm} of {tried} sampled game models read");
        Ok("3D real shapes: models read", okm > tried / 2);
        var hv = cat.First(e => Previews.Get(e.Str("model")!, wait: true) is { Count: > 0 });
        var (img, _) = V3.HoverPicture(hv); Ok("3D hover picture", img != null);
        if (img != null) img.Save(Path.Combine(outDir, "hover.png"));
        V3.PickLibrary(hv.Str("id")!); G.Refresh(); G.SimClick(mx, my); V3.StopPlacing(); V3.Ensure();
        V3.ToggleReal(true); V3.Ensure(); G.Refresh(); await Pump(); Ok("3D real shapes: textured draw", V3.Stats != "" && !V3.Stats.StartsWith("cannot")); Shot("three_d_real"); V3.ToggleReal(false); Undo(); V3.Ensure();
        // an imported model
        var glb = Environment.GetEnvironmentVariable("BMB_TEST_GLB");
        if (glb != null && File.Exists(glb))
        {
            ImportInfo? seen = null; V3.TestImport = i => { seen = i; return true; };
            var ifo = ImportUi.Analyse(glb); Ok("Import .glb check", ifo.Tris > 0 && !ifo.Blocked);
            string id = ImportUi.Add(glb, ifo); Bmb.Core.Layout.ReloadCatalog(); V3.FillLibrary();
            V3.PickLibrary(id); G.Refresh(); G.SimClick(mx, my); V3.StopPlacing(); V3.Ensure(); G.Refresh();
            Ok("Import .glb placed", V3.PickOf("obj:" + ((D.Map.Arr("objects")?.Count ?? 1) - 1)) != null); Shot("three_d_glb"); Undo(); V3.Ensure();
        }
        SetMode("collision"); await Pump(200); Ok("Collision scene", !V3.Stats.StartsWith("cannot")); Shot("collision_new"); SetMode("2d");
        // layouts
        int oldLeft = _leftP.Width; _sizes["left"] = 300L; _saved["test"] = State(); _sizes.Remove("left"); _current = "Default"; FillLayouts(); ApplySizes(false); await Pump();
        bool defOk = _leftP.Width == 210; _layoutBox.SelectedItem = "test"; LayoutLoad(); await Pump();
        Ok("Layouts: Default and a saved one", defOk && _leftP.Width == 300);
        _layoutBox.SelectedItem = "test"; LayoutDelete(); _sizes = new JObj(); ApplySizes(false); await Pump();
        foreach (var (w, h) in new[] { (1000, 620), (1500, 880) }) { Size = new Size(w, h); await Pump(150); }
        Shot("win_small_end");
        D.WriteAutosave(); Ok("Auto-save written", File.Exists(Doc.Autosave));
        // back to where it started
        while (D.UndoCount > 0 && Json.Dumps(D.World, -1) != worldAtStart) D.Undo();
        Tree();
        File.WriteAllText(Path.Combine(outDir, "world_after_test.json"), Json.Dumps(D.World, 1));
        Ok("Undoing everything: map as it started", Json.Dumps(D.World, -1) == worldAtStart);
        // Play: only the target file, on the scratch copy
        Ok("Play target written (off)", WriteQuickstart(false) && File.ReadAllText(Play.QuickstartTarget).Contains("enabled = false"));
        Ok("Play target route", File.ReadAllText(Play.QuickstartTarget).Contains(D.Map.Str("name")!));
        PlayDialog(); await Pump(); Ok("Play window opens", LastPreview is { Visible: true }); LastPreview!.Close();
        await LookTests(Ok, ms => Pump(ms), Shot);
        // the build, from the window
        var bsw = Stopwatch.StartNew(); string built = ""; BuildFinished = m => built = m;
        Build();
        while (Building && bsw.Elapsed.TotalSeconds < 120) await Pump(100);
        rep.Add($"Build + install from the window: {bsw.Elapsed.TotalSeconds:0.0} s - {built}");
        Ok("Build + install finished", built.StartsWith("Built"));
        await Pump(1500);
        File.Delete(Doc.Autosave);
        rep.Add($"RESULT {pass} passed, {fail} failed");
        File.WriteAllLines(Path.Combine(outDir, "ui_timing.txt"), rep);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    static double[] BoxC(Pick p)
    {
        var cs = View3D.BoxCorners(p);
        return new[] { p.Pos[0], (cs[0][1] + cs[7][1]) / 2, p.Pos[2] };
    }
}

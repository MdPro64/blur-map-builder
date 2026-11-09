// The right panel, the checks, the AI + Surface bars and the Lighting tab. Follows road_designer.py (build_props, show_checks, light_build, minimap_*) and designer_net / designer_surface.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm
{
    public void Refresh2() { Checks(); RefreshProps(); if (Is3D) V3.Dirty(); G.Invalidate(); }

    /// after every edit: redraw + auto-save
    public void Changed() { D.WriteAutosave(); Refresh2(); if (Mode == "light") LightBuild(); }

    public void Undo()
    {
        if (!D.Undo()) { Say("Nothing to undo"); return; }
        AfterUndo(); Say($"Undo ({D.UndoCount} more)");
    }

    public void Redo()
    {
        if (!D.Redo()) { Say("Nothing to redo"); return; }
        AfterUndo(); Say($"Redo ({D.RedoCount} more)");
    }

    void AfterUndo()
    {
        Map2.SelPick = -1; if (Map2.Sel >= D.Points.Count) Map2.Sel = -1;
        Tree(); V3.Dirty(); Changed();
        if (Mode == "car") _car.Refresh2();
        if (Mode == "surface") SurfRefresh();
        if (Mode == "ai") AiRefresh();
    }

    void DoSave()
    {
        try
        {
            D.Save();
            try { File.Delete(Doc.Autosave); } catch (IOException) { }
            Say("Saved " + Paths.WorldFile, Theme.Good);
        }
        catch (Exception e) { Say("Could not save: " + e.Message, Theme.Bad); }
    }

    void Checks()
    {
        _checks.Clear();
        var info = Color.FromArgb(0x9a, 0xa8, 0xb8);
        void Line(string s, Color c) { _checks.SelectionStart = _checks.TextLength; _checks.SelectionColor = c; _checks.AppendText(s + "\n"); }
        void Check(bool ok, string text) => Line((ok ? "OK   " : Lang.Ru ? "ИСПР " : "FIX  ") + Lang.T(text), ok ? Theme.Good : Theme.Bad);
        void Warn(string text) => Line((Lang.Ru ? "ВНИМ " : "WARN ") + Lang.T(text), Theme.Warn);
        var r = D.Road();
        if (r == null) { Check(false, "the road can't be made"); return; }
        Check(r.Length >= 900 && r.Length <= 5300, $"length {r.Length:0} m (900..5300)");
        double rad = r.MinRadius();
        Check(rad >= 35, $"tightest turn radius {rad:0} m (min 35)");
        var ub = r.UnbridgedCrossings();
        Check(ub.Count == 0, ub.Count == 0 ? $"crossings: {r.Crossings.Count}, all bridged" : $"{ub.Count} crossing(s) without a bridge (turn Auto bridges on / raise a point)");
        double g = r.MaxGrade();
        Check(g <= 0.12, $"steepest ramp {100 * g:0.0} % (max 12 %)");
        int dots = Map2.Dots(r).Count;
        Check(dots <= Bmb.Core.Layout.MaxPickups, $"power-ups {dots} (max {Bmb.Core.Layout.MaxPickups})");
        string? clash = null; int mi = -1;
        foreach (var m in D.Loc.Objs("maps"))
        {
            if (++mi == D.Mi) continue;
            var o = D.RoadOf(m);
            if (o == null) continue;
            for (int i = 0; i < r.Count && clash == null; i += 4) if (o.NearRoad(r.Centre[i].X, r.Centre[i].Z, (r.Width + o.Width) / 2 + 6)) clash = m.Str("name");
            if (clash != null) break;
        }
        Check(clash == null, clash == null ? "no overlap with other maps here" : $"overlaps the map '{clash}' (same location: their walls block each other)");
        Check(D.TotalMaps <= UiText.MaxMaps, $"maps in total: {D.TotalMaps} (max {UiText.MaxMaps})");
        var net = D.Net()!;
        if ((D.Map.Arr("branches")?.Count ?? 0) > 0 || (D.Map.Arr("splits")?.Count ?? 0) > 0)
        {
            foreach (var p in net.Problems) Check(false, p);
            var (si, fi) = GenAid.RaceLines(r, D.Map);
            var busy = net.BusyRanges();
            foreach (var (idx, name) in r.Closed ? new[] { (si, "START") } : new[] { (si, "START"), (fi, "FINISH") })
                foreach (var (a, b) in busy)
                    if ((r.Closed && Py.Mod(idx - a, r.Count) <= b - a) || (!r.Closed && a <= idx && idx <= b)) { Check(false, $"The {name} line is at a branch / lane split: move one of them"); break; }
            foreach (var (bi, len, ml, ratio) in net.Balance())
            {
                double pct = (ratio - 1.0) * 100;
                if (ratio < 0.8) Warn($"branch {bi + 1} is much SHORTER than the main road it skips: {len:0} m vs {ml:0} m ({pct:0} %) - a big shortcut, everyone will take it");
                else if (ratio > 1.35) Warn($"branch {bi + 1} is much LONGER than the main road it skips: {len:0} m vs {ml:0} m (+{pct:0} %) - nobody will take it");
                else Check(true, $"branch {bi + 1} balanced: {len:0} m vs main {ml:0} m ({pct:+0;-0;+0} %)");
            }
            int q = 0;
            foreach (var (_, br) in net.Branches) { if (br.Lowered > 0.5) Check(true, $"branch {q + 1}: heights eased to max 10 % slope (up to {br.Lowered:0.0} m lower/higher than set) - its ends stay level with the road"); q++; }
            if (net.Branches.Count > 0 || net.Splits.Count > 0) Check(true, $"{net.Branches.Count} branch(es), {net.Splits.Count} lane split(s): the AI takes them too");
        }
        if (!Surface.IsDefault(D.Map))
        {
            var s = Surface.Settings(D.Map); var parts = new List<string>();
            if (s.Road != "tarmac") parts.Add("road " + Surface.Surfaces[s.Road].Label);
            if (s.Sides > 0) parts.Add($"{s.Sides:0} m {Surface.Surfaces[s.SideType].Label.ToLowerInvariant()} sides");
            if (s.Variety) parts.Add("varied asphalt");
            if (s.Paint.Count > 0) parts.Add($"{s.Paint.Count * Surface.CellM * Surface.CellM:0} m2 painted");
            Check(true, "surface: " + string.Join(", ", parts));
        }
        int asked = D.Points.OfType<JObj>().Count(p => p.Truthy("tilt"));
        if (asked > 0)
        {
            if (!r.Tilted) Warn($"{asked} point(s) have a tilt, but all of them are too close to the start, the finish, a bridge, a branch or a lane split: the road stays flat");
            else
            {
                double most = r.Tan.Max(t => Math.Abs(Math.Atan(t))) * 180 / Math.PI, rise = Enumerable.Range(0, r.Count).Max(i => r.Height[i] - r.PlainH(i));
                Check(true, $"road tilt: up to {most:0.#}° on {asked} point(s){(rise > 0.05 ? $", the road rises up to {rise:0.0} m so its low side stays above the ground" : "")}");
            }
        }
        double mins = r.Length / (150 / 3.6) / 60;
        Line("", info);
        Line(Lang.T($"Length {r.Length / 1000:0.00} km, ~{(int)mins}:{(int)(mins * 60) % 60:00} at 150 km/h"), info);
        Line(Lang.T($"Width {r.WidthAt.Min():0}..{r.WidthAt.Max():0} m, highest {r.Height.Max():0.0} m, {r.Crossings.Count} bridge(s)"), info);
        Line(Lang.T($"Corners: {D.Points.OfType<JObj>().Count(p => p.Truthy("corner"))}, points: {D.Points.Count}"), info);
    }

    string _propsKey = "";
    public void RefreshProps()
    {
        string key = string.Join("|", Mode, D.Li, D.Mi, Map2.Sel, Map2.SelPick, Map2.SelBranch, Map2.SelBpt, Map2.SelSplit, Map2.PType, Json.Dumps(D.Map, -1), _props.ClientSize.Width);
        if (key == _propsKey) return;
        _propsKey = key;
        var scroll = _props.AutoScrollPosition;
        _props.SuspendLayout();
        foreach (Control c in _props.Controls.Cast<Control>().ToList()) c.Dispose();
        _props.Controls.Clear();
        MapProps();
        _props.ResumeLayout();
        _props.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
    }

    void H(string t) => _props.Controls.Add(Theme.Heading(t));
    void L(string t, Color? c = null) { var l = Theme.Lbl(t); if (c != null) l.ForeColor = c.Value; l.MaximumSize = new Size(_props.Width - 30, 0); _props.Controls.Add(l); }
    void R(string label, string value, Action m, Action p) => _props.Controls.Add(Theme.Row(label, value, m, p));
    void Bs(params Control[] c) { var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Math.Max(120, _props.ClientSize.Width - 12), 0), Margin = new Padding(0, 2, 0, 2), BackColor = Color.Transparent }; f.Controls.AddRange(c); _props.Controls.Add(f); }
    void Edit(Action a) { D.Snapshot(); a(); D.Touched(); Changed(); }

    void MapProps()
    {
        var m = D.Map; var pts = D.Points;
        var d = ColorTranslator.FromHtml("#7a2a3a");
        if (Map2.Sel >= 0 && Map2.Sel < pts.Count)
        {
            var po = pts[Map2.Sel] as JObj ?? new JObj();
            var p = Road.NormPoint(pts[Map2.Sel], m.Num("width", 60));
            H($"POINT {Map2.Sel + 1} of {pts.Count}");
            L($"x {p.X:0}   z {p.Z:0}");
            R("Width (Q/W)", $"{(po.Truthy("w") ? po.Num("w") : m.Num("width", 60)):0} m", () => Map2.Adj("w", -5), () => Map2.Adj("w", 5));
            R("Height (PgUp/Dn)", $"{po.Num("h"):0} m", () => Map2.Adj("h", -1), () => Map2.Adj("h", 1));
            R("Corner radius", po.Truthy("corner") ? $"{po.Num("corner"):0} m" : "smooth", () => Map2.Adj("corner", -5), () => Map2.Adj("corner", 5));
            R("Tilt ( , . )", MapView.TiltShort(po.Num("tilt")), () => Map2.Adj("tilt", -1), () => Map2.Adj("tilt", 1));
            _props.Controls.Add(new TiltPicture(po.Num("tilt")));
            Bs(Theme.Btn("⟋ Auto tilt for this bend", Map2.AutoTilt), Theme.Btn("▬ Flat", () => Map2.Adj("tilt", -po.Num("tilt"))));
            var road = D.Road();
            if (po.Truthy("tilt") && road != null && road.Tilted && Map2.Sel < road.Points.Count)
            {
                int ti = road.IndexAt(road.Project(p.X, p.Z).S);
                if (road.TiltKept[ti] < 0.98) L("Stays flatter here: it is close to the start, the finish, a bridge, a branch or a lane split.", Theme.Warn);
            }
            else if (po.Truthy("tilt") && road != null && !road.Tilted) L("Stays flat here: it is close to the start, the finish, a bridge, a branch or a lane split.", Theme.Warn);
            if (po.Truthy("corner") && road != null && Map2.Sel < road.Points.Count && road.Points[Map2.Sel].CornerUsed > 0 && road.Points[Map2.Sel].CornerUsed < po.Num("corner") - 0.5)
                L($"only {road.Points[Map2.Sel].CornerUsed:0} m fits here (the points next to it are too close)", Theme.Warn);
            Bs(Theme.Btn("↰ 90° left (T)", () => Map2.Turn90(1)), Theme.Btn("↱ 90° right (Y)", () => Map2.Turn90(-1)));
            Bs(Theme.Btn("◢ Corner (C)", Map2.ToggleCorner), Theme.Btn("✖ Delete", Map2.DeleteSelected, d));
        }
        else if (Map2.SelPick >= 0 && m.Arr("pickups") is { } items && Map2.SelPick < items.Count)
        {
            var it = (JObj)items[Map2.SelPick]!; bool row = it.Str("kind") == "row";
            H("POWER-UP " + (row ? "ROW" : ""));
            L($"type: {it.Str("type") ?? "mixed"} (keys 1-8 or the Type menu)");
            if (row) { R("Power-ups in this row", Bmb.Core.Layout.RowSize(m, it).ToString(), () => Map2.RowN(-1), () => Map2.RowN(1)); Bs(Theme.Btn("Mixed types", Map2.MixedRow), Theme.Btn("Delete", Map2.DeleteSelected, d)); }
            else Bs(Theme.Btn("Delete", Map2.DeleteSelected, d));
        }
        NetProps();
        H("MAP: " + m.Str("name"));
        R("Default width", $"{m.Num("width", 60):0} m", () => MapWidth(-5), () => MapWidth(5));
        var r = D.Road();
        if (r != null)
        {
            var (si, fi) = GenAid.RaceLines(r, m);
            R("Start at", $"{r.Dist[si]:0} m", () => Map2.MoveLine("start", -20), () => Map2.MoveLine("start", 20));
            if (!r.Closed) R("Finish at", $"{r.Dist[fi]:0} m", () => Map2.MoveLine("finish", -20), () => Map2.MoveLine("finish", 20));
            var f = new List<Control> { Theme.Lbl("(or drag the squares on the lines)") };
            if (m["start_m"] != null || m["finish_m"] != null) f.Add(Theme.Btn("Reset", ResetLines));
            Bs(f.ToArray());
        }
        var lap = Theme.Check("⟳ Lap track (the end joins the start)", m.Truthy("laps"), ToggleLaps); lap.ForeColor = ColorTranslator.FromHtml("#7fd0ff"); _props.Controls.Add(lap);
        if (m.Truthy("laps")) R("Laps", m.Int("laps").ToString(), () => SetLaps(-1), () => SetLaps(1));
        _props.Controls.Add(Theme.Check("Auto bridges where the road crosses", !m.ContainsKey("auto_bridges") || m.Truthy("auto_bridges"), v => Edit(() => m["auto_bridges"] = v)));
        _props.Controls.Add(Theme.Check("Obstacles (containers, barrels)", m.Truthy("obstacles"), v => Edit(() => m["obstacles"] = v)));
        L("Power-ups: " + (m["pickups"] is List<object?> l ? $"{l.Count} items" : "automatic"));
        R("Rows every", $"{Bmb.Core.Layout.PickupStep(m):0} m", () => MapNum("pickup_step", -25, 50, 600, 150), () => MapNum("pickup_step", 25, 50, 600, 150));
        R("Power-ups per row", Bmb.Core.Layout.RowSize(m).ToString(), () => MapNum("pickup_per_row", -1, 1, 9, 5), () => MapNum("pickup_per_row", 1, 1, 9, 5));
        Bs(Theme.Btn("⚡ Auto rows", () => { Map2.SelPick = -1; Edit(() => m["pickups"] = null); Say("Automatic power-up rows"); }), Theme.Btn("⌫ Clear all", () => { Map2.SelPick = -1; Edit(() => m["pickups"] = new List<object?>()); Say("All power-ups removed (add rows with R, single ones with P)"); }));
        Bs(Theme.Btn("⇄ Reverse (V)", Map2.Reverse), Theme.Btn("⇅ Mirror (X)", Map2.Mirror));
        H("AUTO SCENERY (made at every build)");
        foreach (var (key, text) in Bmb.Core.Layout.Scenery)
        {
            string k = key, t = text;
            _props.Controls.Add(Theme.Check(text, Bmb.Core.Layout.SceneryOn(m, key), v =>
            {
                Edit(() => { if (m.Obj("scenery") == null) m["scenery"] = new JObj(); m.Obj("scenery")![k] = v; });
                Say($"{t} {(v ? "ON" : "OFF")} - Build + install to see it in game");
            }));
        }
        H("MINIMAP");
        var mm = m.Obj("minimap") ?? new JObj();
        bool auto = !mm.ContainsKey("auto") || mm.Truthy("auto");
        _props.Controls.Add(Theme.Check("Auto minimap from the road", auto, MinimapAuto));
        if (!auto) L("own picture: " + (mm.Truthy("image") ? Path.GetFileName(mm.Str("image")) : "(no picture picked)"), mm.Truthy("image") ? Theme.Warn : Theme.Bad);
        Bs(Theme.Btn("✔ Export road picture", MinimapExport), Theme.Btn("▸ Pick own", MinimapPick), Theme.Btn("⛶ Preview", MinimapPreview));
    }

    void NetProps()
    {
        var m = D.Map; var r = D.Road(); var net = D.Net();
        var delc = ColorTranslator.FromHtml("#7a2a3a");
        var bs = m.Arr("branches"); var ss = m.Arr("splits");
        if (Map2.SelBranch >= 0 && bs != null && Map2.SelBranch < bs.Count)
        {
            var b = (JObj)bs[Map2.SelBranch]!;
            H($"BRANCH {Map2.SelBranch + 1} ({b.Arr("points")!.Count} points)");
            var (s0, s1) = r != null ? Network.BranchSpan(r, b) : (b.Num("from_m"), b.Num("to_m"));
            R("Width", $"{b.Num("width", Network.BranchWidth):0} m", () => Map2.NetAdj("width", -2), () => Map2.NetAdj("width", 2));
            if (net != null && Map2.SelBranch < net.Branches.Count)
            {
                var (_, blen, mlen, ratio) = net.Balance()[Map2.SelBranch];
                L($"Length {blen:0} m  vs  main road {mlen:0} m  ({(ratio - 1) * 100:+0;-0;+0} %)", ratio is < 0.8 or > 1.35 ? Theme.Warn : Theme.Good);
            }
            R("Leaves at", $"{s0:0} m", () => Map2.NetAdj("from_m", -20), () => Map2.NetAdj("from_m", 20));
            R("Joins at", $"{s1:0} m", () => Map2.NetAdj("to_m", -20), () => Map2.NetAdj("to_m", 20));
            if (Map2.SelBpt >= 0 && Map2.SelBpt < b.Arr("points")!.Count)
            {
                var p = (JObj)b.Arr("points")![Map2.SelBpt]!;
                R($"Point {Map2.SelBpt + 1} height", $"{p.Num("h"):0} m", () => Map2.NetAdj("h", -1), () => Map2.NetAdj("h", 1));
            }
            L("Move tool: drag its points / the round handles", ColorTranslator.FromHtml("#7f8a96"));
            Bs(Theme.Btn("✖ Delete branch", Map2.NetDeleteWhole, delc));
        }
        else if (Map2.SelSplit >= 0 && ss != null && Map2.SelSplit < ss.Count)
        {
            var sp = (JObj)ss[Map2.SelSplit]!;
            H($"LANE SPLIT {Map2.SelSplit + 1}");
            var (s0, s1) = r != null ? Network.SplitSpan(r, sp) : (sp.Num("from_m"), sp.Num("to_m"));
            Bs(Theme.Lbl("Lanes"), Theme.Btn("2 lanes", () => Map2.NetLanes(2), Network.LaneCount(sp) == 2 ? Theme.Blue : null), Theme.Btn("3 lanes", () => Map2.NetLanes(3), Network.LaneCount(sp) == 3 ? Theme.Blue : null));
            R("Starts at", $"{s0:0} m", () => Map2.NetAdj("from_m", -20), () => Map2.NetAdj("from_m", 20));
            R("Ends at", $"{s1:0} m", () => Map2.NetAdj("to_m", -20), () => Map2.NetAdj("to_m", 20));
            Bs(Theme.Btn("✖ Delete split", Map2.NetDeleteWhole, delc));
        }
    }

    void MapWidth(double d) => Edit(() => D.Map["width"] = Math.Max(30.0, Math.Min(200.0, D.Map.Num("width", 60.0) + d)));
    void MapNum(string key, double d, double lo, double hi, double def)
    {
        Edit(() => { double v = Math.Max(lo, Math.Min(hi, (D.Map.Truthy(key) ? D.Map.Num(key) : def) + d)); D.Map[key] = key == "pickup_per_row" ? (object)(long)v : v; });
        Say($"{key.Replace('_', ' ')}: {D.Map[key]} (the automatic power-up rows follow)");
    }
    void ResetLines() { Edit(() => { D.Map.Remove("start_m"); D.Map.Remove("finish_m"); }); Say("Start / finish back to the standard spots"); }

    void ToggleLaps(bool on)
    {
        var m = D.Map;
        Edit(() =>
        {
            if (on) { m["laps"] = m.Int("laps_last", 3); m.Remove("finish_m"); Say($"Lap track: the end now joins the start, {m.Int("laps")} laps (change with Laps - / +)", ColorTranslator.FromHtml("#7fd0ff")); }
            else { m["laps_last"] = m.Int("laps", 3); m.Remove("laps"); Say("Sprint again: start and finish lines"); }
            m.Remove("start_m");
        });
    }

    void SetLaps(int d) { Edit(() => D.Map["laps"] = Math.Max(1, Math.Min(10, D.Map.Int("laps", 3) + d))); Say($"{D.Map.Int("laps")} laps", ColorTranslator.FromHtml("#7fd0ff")); }

    // ---- minimap
    ((double X0, double X1, double Z0, double Z1) B, List<Road> Roads) LocBounds()
    {
        var roads = D.Loc.Objs("maps").Select(Road.ForMap).ToList();
        return (Minimap.CityBounds(roads), roads);
    }

    void MinimapAuto(bool auto)
    {
        Edit(() => { var mm = D.Map.Obj("minimap") ?? new JObj(); mm["auto"] = auto; D.Map["minimap"] = mm; });
        if (!auto && !D.Map.Obj("minimap")!.Truthy("image")) Say("Pick a picture for the minimap (tip: Export road picture, paint on it, then Pick own)", Theme.Warn);
    }

    void MinimapExport()
    {
        string? path = SaveFile("Save the road picture", "PNG picture|*.png", World.Slug(D.Map.Str("name")!) + ".png", Path.Combine(Paths.Tracks, "minimaps"));
        if (path == null) return;
        var (b, roads) = LocBounds();
        MinimapUi.ExportPng(path, b, roads, D.Mi);
        Say($"Saved {path}: WHITE = shown on the minimap. Paint on it (Paint is fine), then 'Pick own'.", Theme.Good);
    }

    void MinimapPick()
    {
        string? path = OpenFile("Pick the minimap picture", "Pictures|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*", Path.Combine(Paths.Tracks, "minimaps"));
        if (path == null) return;
        Edit(() => D.Map["minimap"] = new JObj { ["auto"] = false, ["image"] = Path.GetFullPath(path) });
        if (!File.Exists(path + ".json")) Say("This picture was not exported here: it is stretched over the whole location area (16:9)", Theme.Warn);
    }

    public Form? LastPreview;
    void MinimapPreview()
    {
        const int W = 960, H = 540;
        var (b, roads) = LocBounds();
        var bm = new Bitmap(W, H);
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(ColorTranslator.FromHtml("#05080c"));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int k = 0;
            foreach (var m in D.Loc.Objs("maps"))
            {
                var r = roads[k]; var mm = m.Obj("minimap") ?? new JObj();
                List<List<(double U, double V)>>? polys = null;
                if (mm.ContainsKey("auto") && !mm.Truthy("auto") && mm.Truthy("image") && File.Exists(mm.Str("image"))) polys = Minimap.ImagePolys(b, mm.Str("image")!);
                if (polys == null || polys.Count == 0) polys = Minimap.RoutePolys(b, r);
                using var br = new SolidBrush(k == D.Mi ? Color.White : ColorTranslator.FromHtml("#3a4250"));
                foreach (var p in polys) g.FillPolygon(br, p.Select(q => new PointF((float)(q.U * W), (float)(q.V * W))).ToArray());
                if (k == D.Mi)
                {
                    using var gr = new SolidBrush(ColorTranslator.FromHtml("#35e05a"));
                    foreach (var p in Minimap.StartPolys(b, r, r.Dist[GenAid.RaceLines(r, m).Si])) g.FillPolygon(gr, p.Select(q => new PointF((float)(q.U * W), (float)(q.V * W))).ToArray());
                }
                k++;
            }
        }
        var f = new Form { Text = "Minimap preview - " + D.Map.Str("name"), ClientSize = new Size(W, H + 24), BackColor = Theme.Panel, StartPosition = FormStartPosition.CenterParent, KeyPreview = true, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        Look.Dress(f, "minimap");
        f.Controls.Add(new PictureBox { Image = bm, Dock = DockStyle.Top, Height = H });
        f.Controls.Add(new Label { Text = "White = this map's route, green = start line. The cars move on it in the race.", Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleCenter });
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        LastPreview = f;
        f.Show(this);
    }

    // ---- AI path
    void AiRefresh()
    {
        var p = D.Map.Obj("ai_path") ?? new JObj(); bool own = p.Str("mode") == "drawn";
        if (own) { Map2.AiLane = p.Num("lane", Bmb.Core.Layout.AiLane); _aiLaneBox.Value = Map2.AiLane; }
        _aiState.Text = own ? "now: YOUR OWN line (drawn / racing line)" : "now: AUTOMATIC (the whole road; made at every build)";
        _aiState.ForeColor = own ? ColorTranslator.FromHtml("#f0e080") : ColorTranslator.FromHtml("#9aa8b8");
    }

    void AiAuto()
    {
        if (D.Map.Obj("ai_path")?.Str("mode") != "drawn") { Say("The AI path is already automatic"); return; }
        Edit(() => D.Map.Remove("ai_path")); AiRefresh();
        Say("AI path: automatic again (the AI uses the whole road) - Ctrl+Z undoes");
    }

    void AiRacing()
    {
        var r = D.Road();
        if (r == null) return;
        Edit(() => D.Map["ai_path"] = new JObj { ["mode"] = "drawn", ["lane"] = Map2.AiLane, ["points"] = Bmb.Core.Layout.RacingLine(r, Map2.AiLane) });
        AiRefresh();
        Say("Racing line made: the AI cuts the inside of the bends. Use the Pen to change any part of it.");
    }

    void AiLaneChanged(double lane)
    {
        Map2.AiLane = Math.Max(6.0, Math.Min(40.0, lane));
        var p = D.Map.Obj("ai_path");
        if (p?.Str("mode") == "drawn" && Math.Abs(p.Num("lane", Bmb.Core.Layout.AiLane) - Map2.AiLane) > 1e-6) Edit(() => p["lane"] = Map2.AiLane);
    }

    // ---- surface bar
    void SurfRefresh()
    {
        _filling2 = true;
        var s = Surface.Settings(D.Map);
        _surfRoad.SelectedItem = Surface.Surfaces[s.Road].Label;
        _surfSide.SelectedIndex = Math.Max(0, Array.IndexOf(SideKeys, s.SideType));
        SurfBrushShow();
        _surfVariety.Checked = s.Variety;
        string feel = Surface.LocFeel(D.World, D.Loc);
        _surfFeel.SelectedItem = UiText.FeelNames.FirstOrDefault(q => q.Key == feel).Name ?? "Strong";
        _surfSides.Text = s.Sides > 0 ? $"{s.Sides:0} m" : "off";
        var so = D.Map.Obj("surface");
        _bumpH.Value = so != null && so.ContainsKey("bump_h") ? so.Num("bump_h") : 0.3;
        _bumpSize.Value = so != null && so.ContainsKey("bump_size") ? so.Num("bump_size") : 3.0;
        _filling2 = false;
    }

    void SurfSet(string key, object? v)
    {
        Edit(() => { if (D.Map.Obj("surface") == null) D.Map["surface"] = new JObj(); D.Map.Obj("surface")![key] = v; });
        SurfRefresh();
        string name = key switch { "road" => "Road type", "side_type" => "Sides", "variety" => "Varied asphalt", "bump_h" => "Bump height (m)", "bump_size" => "Bump spacing (m)", _ => key };
        string shown = v is string s && Surface.Surfaces.TryGetValue(s, out var q) ? q.Label : v is true ? "ON" : v is false ? "OFF" : $"{v}";
        Say($"{name}: {shown} (Build + install to drive on it)", Theme.Good);
    }

    void SurfSides(int d) => SurfSet("sides", Math.Max(0.0, Math.Min(15.0, Surface.Settings(D.Map).Sides + d)));

    void SurfClear()
    {
        if (Surface.Settings(D.Map).Paint.Count == 0 && D.Map.Obj("surface")?.Obj("bumps") == null) return;
        Edit(() => { if (D.Map.Obj("surface") == null) D.Map["surface"] = new JObj(); D.Map.Obj("surface")!["paint"] = new JObj(); D.Map.Obj("surface")!.Remove("bumps"); });
        Say("All paint and bumps removed from this map");
    }

    void SurfFeel(string key)
    {
        Edit(() => D.Loc["surface_feel"] = key);
        Say($"Grip of the surfaces: {UiText.FeelNames.First(q => q.Key == key).Name} (all maps of {D.Loc.Str("name")}) - Build + install to feel it", Theme.Good);
    }

    // ---- the Lighting tab
    PictureBox? _lightPrev;
    readonly Dictionary<string, Bitmap> _skyPics = new();
    bool _lightBusy;

    void LightSet(string? key = null, double[]? value = null, string? preset = null, bool reset = false, Dictionary<string, double[]>? many = null)
    {
        D.Snapshot();
        var (name, vals0) = Lighting.Effective(D.Loc);
        var vals = Share.Copy(vals0);
        if (preset != null) { name = preset; vals = new JObj(); }
        if (reset) vals = new JObj();
        if (key != null) vals[key] = value!.Select(x => (object?)Py.RoundN(x, 5)).ToList();
        if (many != null) foreach (var (k, v) in many) vals[k] = v.Select(x => (object?)x).ToList();
        var sky = D.Loc.Obj("lighting")?.Str("sky_image");
        var lt = new JObj { ["preset"] = name, ["values"] = vals };
        if (!string.IsNullOrEmpty(sky)) lt["sky_image"] = sky;
        D.Loc["lighting"] = lt;
        D.Touched(); Changed();
    }

    void LightSky(string? path)
    {
        if (path != null)
        {
            string folder = Path.Combine(Paths.Tracks, "skies");
            Directory.CreateDirectory(folder);
            string dst = Path.Combine(folder, Path.GetFileName(path));
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dst, true);
        }
        D.Snapshot();
        var (name, vals) = Lighting.Effective(D.Loc);
        var lt = new JObj { ["preset"] = name, ["values"] = Share.Copy(vals) };
        if (path != null) lt["sky_image"] = "skies/" + Path.GetFileName(path);
        D.Loc["lighting"] = lt;
        D.Touched(); Changed();
    }

    void LightPickSky()
    {
        string? path = OpenFile("360 sky panorama (2:1, the top half becomes the sky)", "Sky panorama|*.hdr;*.exr;*.png;*.jpg;*.jpeg|All files|*.*");
        if (path == null) return;
        try { Sky.ReadPanorama(path); }
        catch (Exception e) when (e is not OutOfMemoryException) { Msg("Custom sky", "Cannot read this picture:\n" + e.Message); return; }
        LightSky(path);
    }

    Bitmap? SkyPicture(out string? err)
    {
        err = null;
        var lt = D.Loc.Obj("lighting"); var (name, _) = Lighting.Effective(D.Loc); string? own = lt?.Str("sky_image");
        string file = own != null ? Path.Combine(Paths.Tracks, own) : "";
        string key = own != null ? file + "|" + (File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0) : "preset|" + name;
        if (_skyPics.TryGetValue(key, out var hit)) return hit;
        try
        {
            Bitmap bm;
            if (own != null)
            {
                var (p, note) = LightUi.OwnSky(file, 420);
                bm = Previews.ToBitmap(p);
                using var g = Graphics.FromImage(bm);
                int h = bm.Height;
                using var pen = new Pen(Color.FromArgb(255, 210, 60));
                g.DrawLine(pen, 0, h / 2, 420, h / 2);
                using var f = new Font("Segoe UI", 8);
                using var y = new SolidBrush(Color.FromArgb(255, 210, 60)); using var gr = new SolidBrush(Color.FromArgb(160, 160, 160)); using var rd = new SolidBrush(Color.FromArgb(255, 110, 110));
                g.DrawString("top half = the sky", f, y, 6, h / 2 - 16); g.DrawString("bottom half: not used", f, gr, 6, h / 2 + 2);
                if (note != null) g.DrawString(note, f, rd, 6, 4);
            }
            else bm = Previews.ToBitmap(LightUi.PresetSky(name, 420));
            return _skyPics[key] = bm;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { err = e.Message; return null; }
    }

    public void LightBuild()
    {
        if (_lightBusy) return;
        _lightBusy = true;
        var f = _lightPanel;
        f.SuspendLayout();
        foreach (Control c in f.Controls.Cast<Control>().ToList()) c.Dispose();
        f.Controls.Clear();
        var (name, vals) = Lighting.Effective(D.Loc);
        int pageW = Math.Max(f.ClientSize.Width - 40, 440);
        var page = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(10), BackColor = Theme.Bg };
        page.Controls.Add(new Label { Text = $"☀ LIGHTING  -  location: {D.Loc.Str("name")}  (all its maps)", AutoSize = true, ForeColor = Theme.Warn, Font = new Font("Segoe UI Symbol", 12f, FontStyle.Bold) });
        var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, MaximumSize = new Size(pageW, 0) };
        bar.Controls.Add(Theme.Lbl("Start from the lighting of:"));
        var pv = Theme.Combo(220);
        foreach (var k in Lighting.Presets.Keys) pv.Items.Add(k);
        pv.SelectedItem = name;
        pv.SelectionChangeCommitted += (_, _) => { var v = (string)pv.SelectedItem!; BeginInvoke(() => LightSet(preset: v)); };
        bar.Controls.Add(pv);
        bar.Controls.Add(Theme.Colored("☀ Light up placed objects", "#7a5a10", () => LightSet(many: new() { ["object_light"] = new[] { 1.0, 0.97, 0.92, 0.14 }, ["object_light_up"] = new[] { 0.6 } })));
        bar.Controls.Add(Theme.Btn("↺ Reset to the preset", () => LightSet(reset: true)));
        page.Controls.Add(bar);
        page.Controls.Add(new Label { Text = "Changes need Build + install. Values you changed are marked *.", AutoSize = true, MaximumSize = new Size(pageW, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(4, 2, 4, 6) });
        var body = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, MaximumSize = new Size(pageW, 0) };
        var rows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(16, 0, 0, 0) };
        _lightPrev = new PictureBox { Width = 420, Height = 300, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
        right.Controls.Add(_lightPrev);
        string? own = D.Loc.Obj("lighting")?.Str("sky_image");
        right.Controls.Add(new Label { Text = "SKY PICTURE  -  " + (own != null ? "yours: " + Path.GetFileName(own) : $"the preset's ({name})"), AutoSize = true, ForeColor = Theme.Warn, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        var pic = SkyPicture(out string? err);
        if (pic != null) right.Controls.Add(new PictureBox { Image = pic, Width = pic.Width, Height = pic.Height });
        else right.Controls.Add(new Label { Text = "(no sky preview: " + err + ")", AutoSize = true, ForeColor = Theme.Bad });
        var sb = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg };
        sb.Controls.Add(Theme.Btn("🌄 Custom sky (HDRI)...", LightPickSky, Theme.Blue));
        var useP = Theme.Btn("Use the preset's sky", () => LightSky(null)); useP.Enabled = own != null; sb.Controls.Add(useP);
        right.Controls.Add(sb);
        right.Controls.Add(new Label { Text = "A 360 panorama (2:1, .hdr / .png / .jpg): its top half becomes the sky, from straight up down to the horizon. 'Sky picture rotation' turns it, 'Sky picture brightness' sets how bright. Needs Build + install.", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8") });
        var fields = LightUi.SampleFields.Select(kv => (kv.Key, kv.Value.N, kv.Value.Label, kv.Value.Max)).Concat(Lighting.Fields.Select(kv => (kv.Key, kv.Value.N, kv.Value.Label, kv.Value.Max))).ToList();
        foreach (var (key, n, label, mx) in fields)
        {
            var v = LightUi.Value(D.Loc, key);
            var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 1, 0, 1) };
            r.Controls.Add(new Label { Text = (vals.ContainsKey(key) ? "* " : "   ") + label, AutoSize = false, Width = 250, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = vals.ContainsKey(key) ? Theme.Warn : Theme.Text });
            if (n == 4)
            {
                double peak = Math.Max(v.Take(3).Max(), 1e-6);
                var cb = new RoundButton { Width = 34, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb((int)(255 * Math.Min(1, v[0] / peak)), (int)(255 * Math.Min(1, v[1] / peak)), (int)(255 * Math.Min(1, v[2] / peak))), TabStop = false, Margin = new Padding(4, 2, 4, 2) };
                string k0 = key;
                cb.Click += (_, _) => LightColour(k0);
                r.Controls.Add(cb);
            }
            if (key != "fog_colour")
            {
                int idx = n == 4 ? 3 : 0; double top = Math.Max(mx, v[idx]), res = mx / 200.0;
                var tb = new TrackBar { Minimum = 0, Maximum = (int)Math.Round(top / res), Value = Math.Min((int)Math.Round(top / res), (int)Math.Round(v[idx] / res)), TickStyle = TickStyle.None, Width = 200, Height = 28, BackColor = Theme.Bg, AutoSize = false };
                var val = new Label { Text = v[idx].ToString("0.###"), AutoSize = false, Width = 60, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White };
                string k1 = key; bool moved = false;
                tb.Scroll += (_, _) => { moved = true; double nv = tb.Value * res; val.Text = nv.ToString("0.###"); LightPreview(k1, idx, nv); };
                tb.MouseUp += (_, _) => { if (moved) { moved = false; var cur = LightUi.Value(D.Loc, k1); cur[idx] = tb.Value * res; BeginInvoke(() => LightSet(k1, cur)); } };
                tb.MouseWheel += (_, e) => ((HandledMouseEventArgs)e).Handled = true;
                r.Controls.Add(tb); r.Controls.Add(val);
            }
            rows.Controls.Add(r);
        }
        body.Controls.Add(rows); body.Controls.Add(right);
        page.Controls.Add(body);
        f.Controls.Add(page);
        f.ResumeLayout();
        LightPreview();
        _lightBusy = false;
    }

    void LightColour(string key)
    {
        var v = LightUi.Value(D.Loc, key);
        double peak = Math.Max(v.Take(3).Max(), 1e-6);
        using var d = new ColorDialog { FullOpen = true, Color = Color.FromArgb((int)(255 * Math.Min(1, v[0] / peak)), (int)(255 * Math.Min(1, v[1] / peak)), (int)(255 * Math.Min(1, v[2] / peak))) };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        double scale = key != "fog_colour" ? peak : 1.0;
        v[0] = d.Color.R / 255.0 * scale; v[1] = d.Color.G / 255.0 * scale; v[2] = d.Color.B / 255.0 * scale;
        LightSet(key, v);
    }

    void LightPreview(string? key = null, int i = 0, double value = 0)
    {
        if (_lightPrev == null || _lightPrev.IsDisposed) return;
        var keys = Lighting.Fields.Keys.Concat(LightUi.SampleFields.Keys).ToList();
        var vals = keys.ToDictionary(k => k, k => LightUi.Value(D.Loc, k));
        if (key != null) vals[key][i] = value;
        var bm = new Bitmap(420, 300);
        using (var g = Graphics.FromImage(bm))
        {
            static Color Hex(double r, double gr, double b) => Color.FromArgb(Math.Max(0, Math.Min(255, (int)(255 * r))), Math.Max(0, Math.Min(255, (int)(255 * gr))), Math.Max(0, Math.Min(255, (int)(255 * b))));
            var sky = vals["sky"]; var fog = vals["fog_colour"];
            double[] skyRgb = sky.Take(3).Select(c => c * Math.Min(1.0, 0.25 + vals["sky_intensity"][0] * 3)).ToArray();
            for (int y = 0; y < 180; y += 6)
            {
                double t = y / 180.0;
                using var br = new SolidBrush(Hex(skyRgb[0] * (1 - t) * 0.8 + fog[0] * t * 3, skyRgb[1] * (1 - t) * 0.8 + fog[1] * t * 3, skyRgb[2] * (1 - t) * 0.8 + fog[2] * t * 3));
                g.FillRectangle(br, 0, y, 420, 6);
            }
            using (var gb = new SolidBrush(ColorTranslator.FromHtml("#3a3e44"))) g.FillRectangle(gb, 0, 180, 420, 120);
            var ol = vals["object_light"]; double up = vals["object_light_up"][0], b0 = 0.55;
            double[] amb = ol.Take(3).Select(c => c * ol[3] * 3.0 * (1 - 0.5 * up)).ToArray(), dir = ol.Take(3).Select(c => c * ol[3] * 3.0 * up).ToArray();
            Color Face(double k) => Hex(b0 * (amb[0] + dir[0] * k), b0 * (amb[1] + dir[1] * k), b0 * (amb[2] + dir[2] * k));
            void Poly(Color c, params int[] p)
            {
                var pts = new Point[p.Length / 2]; for (int q = 0; q < pts.Length; q++) pts[q] = new Point(p[2 * q], p[2 * q + 1]);
                using var br = new SolidBrush(c); g.FillPolygon(br, pts); g.DrawPolygon(Pens.Black, pts);
            }
            Poly(Face(1.0), 150, 150, 230, 130, 290, 150, 210, 170); Poly(Face(0.55), 150, 150, 210, 170, 210, 250, 150, 230); Poly(Face(0.15), 210, 170, 290, 150, 290, 230, 210, 250);
            using var f = new Font("Segoe UI", 9); using var f2 = new Font("Segoe UI", 8);
            using var gg = new SolidBrush(ColorTranslator.FromHtml("#9aa8b8"));
            g.DrawString("rough preview of a placed object (light on placed objects)", f, Brushes.White, 10, 8);
            g.DrawString($"object light {ol[3]:0.00}   exposure {vals["middle_grey"][0]:0.00}   bloom {vals["bloom"][0]:0.00}", f2, gg, 10, 280);
        }
        var old = _lightPrev.Image; _lightPrev.Image = bm; old?.Dispose();
    }
}

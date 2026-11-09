// The 3D tab's bars and side panels: buttons on top, start camera, the typed-values panel, object library (+ hover pictures, Bake, import), outliner, Path scatter.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class View3D
{
    Panel _topBar = null!, _camBar = null!, _selFrame = null!, _lib = null!, _outl = null!, _panel = null!;
    Splitter _libSash = null!, _outlSash = null!;
    Label _info = null!, _camLabel = null!, _selBar = null!, _libInfo = null!, _outlTitle = null!;
    Button _soloBtn = null!, _bakeBtn = null!;
    readonly Dictionary<string, RadioButton> _gizmoBtns = new();
    TextBox _libSearch = null!, _outlSearch = null!;
    TreeView _libTree = null!, _outlTree = null!;
    public JObj? Placing;
    readonly HashSet<string> _outlOpen = new() { "grp:obj" };
    bool _outlSync;
    readonly Color Pn = ColorTranslator.FromHtml("#20242b"), Fg = ColorTranslator.FromHtml("#d8dee6");

    void Info(string t, Color? c = null) { if (_info != null) { _info.Text = t; _info.ForeColor = c ?? ColorTranslator.FromHtml("#9aa8b8"); } }
    void SelText(string t) => _selBar.Text = t;
    void SelBar(bool on, string t) { if (_selFrame == null) return; _selFrame.Visible = on && Active; _selBar.Text = t; }
    void SoloButton(bool on) { _soloBtn.Text = on ? "◎ Show all (/)" : "◎ Solo (/)"; _soloBtn.BackColor = ColorTranslator.FromHtml(on ? "#c08a1a" : "#3a3428"); }
    void GizmoButtons() { foreach (var (k, b) in _gizmoBtns) b.Checked = k == Gizmo; }

    public void MakePanels(Panel centre)
    {
        Button B(string t, Action a) { var b = Theme.Btn(t, a, Pn); b.ForeColor = Fg; return b; }
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Pn, Padding = new Padding(2) };
        top.Controls.Add(B("⬇ Top view", Top)); top.Controls.Add(B("⛶ Reset", Reset)); top.Controls.Add(B("▶ Drive", StartDrive)); top.Controls.Add(B("■ Stop", StopDrive));
        var sun = Theme.Toggle("☀ Sun + shadows", ToggleShadows); sun.Checked = Shadows; top.Controls.Add(Look.Tip(sun, "A low sun with hard shadows on the ground: tilted road shows up lighter or darker"));
        if (_f != null) { var hb = Theme.Icon("❔", _f.Help, "Short help for this tab (F1)"); hb.BackColor = Pn; top.Controls.Add(hb); }
        _info = new Label { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(8, 6, 4, 0), UseMnemonic = false };
        top.Controls.Add(_info);
        _topBar = top;
        var cb = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Pn, Padding = new Padding(2) };
        cb.Controls.Add(new Label { Text = "START CAMERA", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#7fd0ff"), Font = Theme.Bold, Margin = new Padding(6, 6, 4, 0) });
        foreach (var (val, txt) in new[] { ("vista", "Fly-in"), ("pack", "Grid shot") })
        {
            string v = val;
            var r = Theme.Radio(txt, "#20242b", () => { CamShot = v; CamInfo(); });
            r.Checked = v == "vista"; cb.Controls.Add(r);
        }
        cb.Controls.Add(B("＋ Add view", CamAdd)); cb.Controls.Add(B("－ Remove last", CamRemove)); cb.Controls.Add(B("▶ Play", CamPlay)); cb.Controls.Add(B("↺ Auto", CamAuto));
        _camLabel = new Label { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(16, 6, 8, 0) };
        cb.Controls.Add(_camLabel);
        _camBar = cb;
        var sf = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = ColorTranslator.FromHtml("#2a2f1a"), Padding = new Padding(2), Visible = false };
        foreach (var (val, txt) in new[] { ("move", "✥ Move (G)"), ("rotate", "↻ Rotate (R)"), ("scale", "⤢ Scale (S)") })
        {
            string v = val;
            var r = Theme.Radio(txt, "#3a4228", () => { Gizmo = v; G.Invalidate(); });
            r.FlatAppearance.CheckedBackColor = ColorTranslator.FromHtml("#c08a1a"); r.Font = Theme.Bold; r.Checked = v == "move";
            _gizmoBtns[v] = r; sf.Controls.Add(r);
        }
        var d = ColorTranslator.FromHtml("#3a3428");
        _soloBtn = Theme.Btn("◎ Solo (/)", ToggleSolo, d); sf.Controls.Add(_soloBtn);
        sf.Controls.Add(Theme.Btn("✖ Hide (X)", Hide, d)); sf.Controls.Add(Theme.Btn("↺ Reset object", ResetObject, d));
        _selBar = new Label { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#f0e080"), Margin = new Padding(6, 6, 4, 0), UseMnemonic = false };
        sf.Controls.Add(_selBar);
        _selFrame = sf;
        _lib = BuildLibrary(); _outl = BuildOutliner();
        _libSash = _f.PanelSash(DockStyle.Left, "library", _lib, 300); _outlSash = _f.PanelSash(DockStyle.Right, "objects", _outl, 270);
        _panel = new Panel { BackColor = Pn, Visible = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        Look.RoundOff(_panel);
        _f.Foldable("library", _lib, true); _f.Foldable("objects", _outl, false);
        centre.Controls.Add(_lib); centre.Controls.Add(_libSash); centre.Controls.Add(_outl); centre.Controls.Add(_outlSash);
        centre.Controls.Add(_selFrame); centre.Controls.Add(_camBar); centre.Controls.Add(_topBar);
        centre.Controls.Add(_panel);
        foreach (var c in new Control[] { _topBar, _camBar, _selFrame, _lib, _libSash, _outl, _outlSash }) c.Visible = false;
        centre.Resize += (_, _) => PlacePanel();
    }

    bool _narrow, _three;
    /// a narrow window has no room for the object list
    public void Narrow(bool on) { _narrow = on; if (_outl != null) { _outl.Visible = _three && !on; _outlSash.Visible = _three && !on; } }

    void ShowPanels(bool on, string mode)
    {
        bool three = on && mode == "3d";
        _three = three;
        _topBar.Visible = on; _camBar.Visible = three;
        _lib.Visible = three; _libSash.Visible = three; _outl.Visible = three && !_narrow; _outlSash.Visible = three && !_narrow;
        if (!on) { _selFrame.Visible = false; _panel.Visible = false; if (Sc != null) ScatterClose(); StopPlacing(); }
        if (!three) StopPlacing();
        _selFrame.SendToBack(); _camBar.SendToBack(); _topBar.SendToBack();
    }

    public void CamInfo(string extra = "")
    {
        if (_camLabel == null) return;
        var cams = CamViews();
        string Part(string shot, string label) { int n = cams.GetValueOrDefault(shot)?.Count ?? 0; return $"{label}: {(n > 0 ? $"{n} view{(n == 1 ? "" : "s")}" : "auto")}"; }
        _camLabel.Text = Part("vista", "Fly-in") + "   " + Part("pack", "Grid shot") + extra;
    }

    // ---- the typed-values panel
    readonly Dictionary<(string, int), (TextBox T, string Txt, double V)> _entries = new();
    CheckBox? _solidBox;

    void PlacePanel() { if (_panel.Visible) { _panel.Left = G.Left + 8; _panel.Top = G.Top + G.Height - _panel.Height - 8; _panel.BringToFront(); } }

    public void PanelShow(Pick? p)
    {
        if (_panel == null) return;
        foreach (Control c in _panel.Controls.Cast<Control>().ToList()) c.Dispose();
        _panel.Controls.Clear(); _entries.Clear();
        if (p == null || SceneMode != "3d") { _panel.Visible = false; return; }
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, BackColor = Pn, Padding = new Padding(4) };
        t.Controls.Add(new Label { Text = p.Label, AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = ColorTranslator.FromHtml("#f0e080"), Font = Theme.Bold, UseMnemonic = false }, 0, 0); t.SetColumnSpan(t.GetControlFromPosition(0, 0)!, 4);
        foreach (var (i, (s, c)) in new[] { (1, ("X", "#ff5a5a")), (2, ("Y", "#6ad04a")), (3, ("Z", "#5a8cff")) }) t.Controls.Add(new Label { Text = s, AutoSize = true, ForeColor = ColorTranslator.FromHtml(c), Font = Theme.Bold }, i, 1);
        double g = Ground;
        var rows = new List<(string Title, string Name, double[] Vals)> { ("Location (m)", "loc", new[] { p.Pos[0], p.Pos[2], p.Pos[1] - g }) };
        if (p.Pickup >= 0) { var it = PickupList[p.Pickup]; rows[0] = ("Location (m)", "loc", new[] { it.Num("x"), it.Num("z"), p.Pos[1] - g }); }
        else if (p.Point >= 0) { rows.Add(("Road width (m)", "width", new[] { p.Width })); rows.Add(("Road tilt (°)", "tilt", new[] { p.Tilt })); }
        else
        {
            double turn = Py.Mod(p.Turn * 180 / Math.PI + 180, 360) - 180;
            rows.Add(("Rotation (°)", "rot", new[] { p.Rx * 180 / Math.PI, p.Rz * 180 / Math.PI, turn }));
            rows.Add(("Scale", "scale", new[] { p.Scale[0], p.Scale[2], p.Scale[1] }));
        }
        int r = 2;
        foreach (var (title, name, vals) in rows)
        {
            t.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Fg, Margin = new Padding(3, 5, 3, 0) }, 0, r);
            for (int j = 0; j < vals.Length; j++)
            {
                string txt = vals[j].ToString(name == "scale" ? "0.000" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
                var e = new TextBox { Width = 66, Text = txt, BackColor = ColorTranslator.FromHtml("#1a1e24"), ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
                e.Enabled = p.Editable && !(name == "scale" && !p.Scalable) && !(p.Pickup >= 0 && j == 2);
                e.KeyDown += (_, ev) =>
                {
                    if (ev.KeyCode == Keys.Enter) { ev.SuppressKeyPress = true; PanelApply(); }
                    else if (ev.KeyCode == Keys.Escape) { ev.SuppressKeyPress = true; G.Focus(); PanelShow(PickOf(Sel)); }
                };
                _entries[(name, j)] = (e, txt, vals[j]);
                t.Controls.Add(e, j + 1, r);
            }
            r++;
        }
        if (p.Pickup >= 0)
        {
            t.Controls.Add(new Label { Text = "Type", AutoSize = true, ForeColor = Fg, Margin = new Padding(3, 5, 3, 0) }, 0, r);
            var om = Theme.Combo(100);
            if (p.Row) om.Items.Add("mixed");
            foreach (var k in World.PickupTypes.Keys) om.Items.Add(k);
            om.SelectedItem = p.PType ?? (p.Row ? "mixed" : "Shunt");
            om.SelectionChangeCommitted += (_, _) => { var v = (string)om.SelectedItem!; _f.BeginInvoke(() => PickupType(p, v)); };
            t.Controls.Add(om, 1, r); t.SetColumnSpan(om, 2);
            t.Controls.Add(Theme.Colored("✖ Delete", "#4a2a2a", () => DeletePickup(p)), 3, r);
            r++;
        }
        int rampN = RampIndex(p.Key);
        if (rampN >= 0) r = RampRows(t, r, rampN);
        if (p.Editable && p.Point < 0 && p.Pickup < 0 && rampN < 0)
        {
            var e = D.Map.Obj("edits")?.Obj(p.Key);
            _solidBox = Theme.Check("Solid - stops the car like a wall (off = just a shape)", e?.Truthy("solid") == true, v => { var q = EditOf(EditsCopy(), p.Key); q["solid"] = v; _f.BeginInvoke(() => Store(p.Key, q)); });
            _solidBox.ForeColor = Fg;
            t.Controls.Add(_solidBox, 0, r); t.SetColumnSpan(_solidBox, 4); r++;
            t.Controls.Add(Theme.Colored("↺ Reset", "#3a3428", ResetObject), 0, r);
        }
        var hint = new Label { Text = !p.Editable ? "fixed: moves with the road" : p.Point >= 0 ? "type + Enter. Tilt: + = left side up, or R and drag the ring" : "type a value + Enter", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#6f7c8a"), Margin = new Padding(3, 5, 3, 0) };
        t.Controls.Add(hint, 1, r); t.SetColumnSpan(hint, 3);
        _panel.Controls.Add(t);
        _panel.Visible = true;
        PlacePanel();
    }

    // ---- jump ramp settings (a ramp is a library object: G / R / S move, turn and size it; these change its shape)
    public bool PanelOpen => _panel is { Visible: true } && _panel.Controls.Count > 0;
    int RampIndex(string key)
    {
        if (!key.StartsWith("obj:") || !int.TryParse(key[4..], out int n)) return -1;
        var objs = D.Map.Arr("objects");
        if (objs == null || n >= objs.Count || objs[n] is not JObj o) return -1;
        return Terrain.IsRamp(Layout.Catalog().GetValueOrDefault(o.Str("id") ?? "")) ? n : -1;
    }

    static readonly (string Text, double V)[] RampCurves = { ("Straight", 0.0), ("Soft curve", 0.3), ("Curved", 0.55), ("Very curved (throws you up)", 0.85) };

    int RampRows(TableLayoutPanel t, int r, int n)
    {
        var ramp = Terrain.Ramps(D.Map).FirstOrDefault(q => q.Key == "obj:" + n);
        var o = (JObj)D.Map.Arr("objects")![n]!; var set = o.Obj("ramp") ?? new JObj();
        var e = Layout.Catalog()[o.Str("id")!];
        Label L(string txt) => new() { Text = txt, AutoSize = true, ForeColor = Fg, Margin = new Padding(3, 5, 3, 0) };
        if (ramp != null)
        {
            var size = new Label { Text = $"JUMP RAMP   {ramp.W:0.0} wide x {ramp.L:0.0} long x {ramp.H:0.0} high (m)  -  S + drag to size it", AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = ColorTranslator.FromHtml("#ff9a50"), Margin = new Padding(3, 8, 3, 2) };
            t.Controls.Add(size, 0, r); t.SetColumnSpan(size, 4); r++;
        }
        t.Controls.Add(L("Shape"), 0, r);
        var sh = Theme.Combo(170); sh.Items.Add("Kicker (up, then a drop)"); sh.Items.Add("Table top (up, flat, down)");
        string shape = set.Str("shape") ?? e.Str("shape") ?? "kicker";
        sh.SelectedIndex = shape == "table" ? 1 : 0;
        sh.SelectionChangeCommitted += (_, _) => { string v = sh.SelectedIndex == 1 ? "table" : "kicker"; _f.BeginInvoke(() => RampSet(n, "shape", v)); };
        t.Controls.Add(sh, 1, r); t.SetColumnSpan(sh, 3); r++;
        t.Controls.Add(L("Curve"), 0, r);
        var cv = Theme.Combo(170); foreach (var (tx, _) in RampCurves) cv.Items.Add(tx);
        double curve = set.ContainsKey("curve") ? set.Num("curve") : e.Num("curve");
        cv.SelectedIndex = RampCurves.Select((q, i) => (Math.Abs(q.V - curve), i)).Min().i;
        cv.SelectionChangeCommitted += (_, _) => { double v = RampCurves[cv.SelectedIndex].V; _f.BeginInvoke(() => RampSet(n, "curve", v)); };
        t.Controls.Add(cv, 1, r); t.SetColumnSpan(cv, 3); r++;
        t.Controls.Add(L("Flat top (m)"), 0, r);
        double lip = set.ContainsKey("lip") ? set.Num("lip") : e.Num("lip");
        var lb = new TextBox { Width = 66, Text = lip.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), BackColor = ColorTranslator.FromHtml("#1a1e24"), ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
        lb.KeyDown += (_, ev) =>
        {
            if (ev.KeyCode != Keys.Enter) return;
            ev.SuppressKeyPress = true;
            if (double.TryParse(lb.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { double vv = Math.Clamp(v, 0, 200); _f.BeginInvoke(() => RampSet(n, "lip", vv)); }
        };
        t.Controls.Add(lb, 1, r);
        var lh = new Label { Text = "(before scaling)", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#6f7c8a"), Margin = new Padding(3, 5, 3, 0) };
        t.Controls.Add(lh, 2, r); t.SetColumnSpan(lh, 2); r++;
        t.Controls.Add(L("Surface"), 0, r);
        var sf = Theme.Combo(170);
        var keys = Surface.Surfaces.Keys.Where(k => k is not ("water" or "deepwater")).ToList();
        foreach (var k in keys) sf.Items.Add(Surface.Surfaces[k].Label);
        string sk = set.Str("surface") ?? "tarmac";
        sf.SelectedIndex = Math.Max(0, keys.IndexOf(sk));
        sf.SelectionChangeCommitted += (_, _) => { string v = keys[sf.SelectedIndex]; _f.BeginInvoke(() => RampSet(n, "surface", v)); };
        t.Controls.Add(sf, 1, r); t.SetColumnSpan(sf, 3); r++;
        t.Controls.Add(Theme.Colored("↺ Reset", "#3a3428", ResetObject), 0, r);
        return r;
    }

    void RampSet(int n, string field, object v)
    {
        var objs = ObjsCopy();
        if (n >= objs.Count || objs[n] is not JObj o) return;
        if (o.Obj("ramp") is not { } set) o["ramp"] = set = new JObj();
        set[field] = v;
        SetObjects(objs, EditsCopy());
        Select("obj:" + n);
    }

    double[]? PanelValues(string name, int n)
    {
        var o = new double[n];
        for (int j = 0; j < n; j++)
        {
            if (!_entries.TryGetValue((name, j), out var e)) return null;
            string txt = e.T.Text.Trim();
            if (txt == e.Txt) o[j] = e.V;
            else if (!double.TryParse(txt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out o[j])) return null;
        }
        return o;
    }

    public void PanelSet(string name, int j, string text) { if (_entries.TryGetValue((name, j), out var e)) e.T.Text = text; }

    public void PanelApply()
    {
        var p = PickOf(Sel);
        if (p == null || !p.Editable || Oper != null || p.Multi) return;
        var loc = PanelValues("loc", 3);
        if (loc == null) { SelText("Please type numbers only (e.g. 12.5)"); return; }
        double g = Ground;
        if (p.Pickup >= 0)
        {
            var items = ItemsCopy(); var it = items[p.Pickup];
            if (Math.Abs(loc[0] - it.Num("x")) > 1e-6 || Math.Abs(loc[1] - it.Num("z")) > 1e-6) { it["x"] = Py.RoundN(loc[0], 1); it["z"] = Py.RoundN(loc[1], 1); SetPickups(items); }
            return;
        }
        if (p.Point >= 0)
        {
            var w = PanelValues("width", 1);
            var vals = new Dictionary<string, double> { ["x"] = loc[0], ["z"] = loc[1], ["h"] = Math.Max(0, loc[2]) };
            if (w != null) vals["w"] = Math.Max(5, w[0]);
            if (PanelValues("tilt", 1) is { } tl) vals["tilt"] = Math.Max(-Road.MaxTilt, Math.Min(Road.MaxTilt, tl[0]));
            var now = new Dictionary<string, double> { ["x"] = p.Pos[0], ["z"] = p.Pos[2], ["h"] = p.Pos[1] - g, ["w"] = p.Width, ["tilt"] = p.Tilt };
            vals = vals.Where(q => Math.Abs(q.Value - now[q.Key]) > 1e-6).ToDictionary(q => q.Key, q => q.Value);
            if (vals.Count > 0) SetPoint(p.Point, vals);
            return;
        }
        var rot = PanelValues("rot", 3); var sc = PanelValues("scale", 3);
        if (rot == null || (p.Scalable && sc == null)) { SelText("Please type numbers only (e.g. 12.5)"); return; }
        var old = EditOf(EditsCopy(), p.Key); var e = Share.Copy(old);
        e["dx"] = loc[0] - (p.Pos[0] - old.Num("dx")); e["dz"] = loc[1] - (p.Pos[2] - old.Num("dz")); e["dy"] = loc[2] + g - (p.Pos[1] - old.Num("dy"));
        e["rx"] = rot[0] * Math.PI / 180; e["rz"] = rot[1] * Math.PI / 180;
        double baseTurn = p.Turn - old.Num("turn");
        e["turn"] = Py.Mod(rot[2] * Math.PI / 180 - baseTurn + Math.PI, 2 * Math.PI) - Math.PI;
        if (p.Scalable) e["scale"] = KObj(new[] { Math.Max(.01, sc![0]), Math.Max(.01, sc[2]), Math.Max(.01, sc[1]) });
        bool same = new[] { "dx", "dy", "dz", "rx", "rz", "turn" }.All(k => Math.Abs(e.Num(k) - old.Num(k)) < 1e-4) && KVec(e["scale"]).Zip(KVec(old["scale"])).All(q => Math.Abs(q.First - q.Second) < 1e-4);
        if (!same) Store(p.Key, e);
    }

    // ---- the object library
    Panel BuildLibrary()
    {
        var bg = Pn;
        var f = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = bg };
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = bg, Padding = new Padding(6) };
        head.Controls.Add(new Label { Text = "OBJECT LIBRARY", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#8fb6e0"), Font = Theme.Bold });
        var wide = new List<Control>(); var texts = new List<Label>();
        void Wide(Button b) { b.AutoSize = false; b.Width = 270; b.Height = 28; head.Controls.Add(b); wide.Add(b); }
        Wide(Theme.Btn("⬆ Import 3D model (.glb)...", ImportModel, Theme.Blue));
        Wide(Theme.Colored("〰 Path scatter (objects along a path)...", "#7a3a8a", ScatterOpen));
        head.Controls.Add(new Label { Text = "Search:", AutoSize = true, ForeColor = Fg });
        _libSearch = new TextBox { Width = 270, BackColor = ColorTranslator.FromHtml("#1a1e24"), ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle };
        _libSearch.TextChanged += (_, _) => FillLibrary();
        head.Controls.Add(_libSearch); wide.Add(_libSearch);
        var low = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = bg, Padding = new Padding(6) };
        void Small(string t, Color c) { var l = new Label { Text = t, AutoSize = true, MaximumSize = new Size(280, 0), ForeColor = c, Font = new Font("Segoe UI", 8f) }; low.Controls.Add(l); texts.Add(l); }
        _libInfo = new Label { AutoSize = true, MaximumSize = new Size(280, 0), ForeColor = ColorTranslator.FromHtml("#f0e080"), UseMnemonic = false };
        low.Controls.Add(_libInfo); texts.Add(_libInfo);
        _bakeBtn = Theme.Btn("⬚ Bake 3D pictures", Bake, Theme.Blue); _bakeBtn.AutoSize = false; _bakeBtn.Width = 270; _bakeBtn.Height = 28; low.Controls.Add(_bakeBtn); wide.Add(_bakeBtn);
        Look.Tip(_bakeBtn, "Makes a picture of every object (once). Then hover an object in the list to see it.");
        var real = Theme.Toggle("◆  REAL SHAPES in the 3D view", ToggleReal); real.Checked = RealShapes; real.AutoSize = false; real.Width = 270; real.Height = 30; real.BackColor = ColorTranslator.FromHtml("#14a38b"); real.Font = Theme.Bold; real.TextAlign = ContentAlignment.MiddleCenter;
        low.Controls.Add(real); wide.Add(real);
        f.Resize += (_, _) => { int w = Math.Max(60, f.ClientSize.Width - 14); foreach (var c in wide) c.Width = w - 4; foreach (var l in texts) l.MaximumSize = new Size(w, 0); };
        Look.Tip(real, "On: placed game objects show as their own models. Off: boxes.");
        _libTree = new TreeView { Dock = DockStyle.Fill, BackColor = ColorTranslator.FromHtml("#08111f"), ForeColor = Fg, BorderStyle = BorderStyle.None, HideSelection = false, ShowLines = false, ShowRootLines = true, ItemHeight = 20, FullRowSelect = true };
        Look.DarkScroll(_libTree);
        _libTree.AfterSelect += (_, e) => LibSelect(e.Node);
        _libTree.MouseMove += LibHover;
        _libTree.MouseLeave += (_, _) => HoverHide();
        f.Controls.Add(_libTree); f.Controls.Add(low); f.Controls.Add(head);
        _libTree.BringToFront();
        FillLibrary();
        return f;
    }

    static readonly (string Kind, string Title)[] KindTitles = { ("ramp", "Jump ramps (drive over them)"), ("import", "Your models (imported .glb)"), ("scenery", "Scenery - look only (tick Solid = wall)"), ("breakable", "Breakable - smash / knock over"), ("movable", "Moves when hit (physics)"), ("animated", "Animated") };

    public void FillLibrary()
    {
        var t = _libTree;
        t.BeginUpdate(); t.Nodes.Clear();
        var cat = Layout.Catalog();
        string q = _libSearch.Text.Trim().ToLowerInvariant();
        if (cat.Count == 0) { _libInfo.Text = "No library yet"; t.EndUpdate(); return; }
        foreach (var (kind, title) in KindTitles)
        {
            var items = cat.Values.Where(e => e.Str("kind") == kind && (q == "" || $"{e.Str("name")} {e.Str("loc")} {e.Str("cat")}".ToLowerInvariant().Contains(q))).ToList();
            if (items.Count == 0) continue;
            var col = KindRgb[kind]; var fg = Color.FromArgb((int)(255 * col[0]), (int)(255 * col[1]), (int)(255 * col[2]));
            var top = t.Nodes.Add($"{title}  ({items.Count})"); top.ForeColor = fg;
            var locs = new Dictionary<string, TreeNode>(); var cats = new Dictionary<(string, string), TreeNode>();
            foreach (var e in items.OrderBy(e => e.Str("loc") != "Shared (all cities)").ThenBy(e => e.Str("loc"), StringComparer.Ordinal).ThenBy(e => e.Str("cat"), StringComparer.Ordinal).ThenBy(e => e.Str("name"), StringComparer.Ordinal))
            {
                string loc = e.Str("loc")!, c = e.Str("cat")!;
                if (!locs.TryGetValue(loc, out var ln)) locs[loc] = ln = top.Nodes.Add(loc);
                if (!cats.TryGetValue((loc, c), out var cn)) cats[(loc, c)] = cn = ln.Nodes.Add(c);
                string size = "size ?";
                if (e.Truthy("sized")) { var lo = e.Arr("lo")!.Select(Json.D).ToArray(); var hi = e.Arr("hi")!.Select(Json.D).ToArray(); size = $"{hi[0] - lo[0]:0} x {hi[2] - lo[2]:0} x {hi[1] - lo[1]:0} m"; }
                var n = cn.Nodes.Add($"{e.Str("name")}   ({size})"); n.Tag = e; n.ForeColor = fg;
            }
            if (q != "") top.ExpandAll();
        }
        Lang.Nodes(t.Nodes);
        t.EndUpdate();
    }

    void LibSelect(TreeNode? n)
    {
        if (_fillingLib || n?.Tag is not JObj e) { if (n != null && n.Tag == null && !_fillingLib) n.Toggle(); return; }
        if (Sc != null) { ScatterLibAdd(e); _libTree.SelectedNode = null; return; }
        Placing = e;
        Select(null);
        G.Cursor = Cursors.Cross;
        _libInfo.Text = $"PLACING: {e.Str("name")}\n{UiText.KindText.GetValueOrDefault(e.Str("kind") ?? "", "")}\nclick in the view (Esc = stop)";
        Info($"Placing {e.Str("name")}: click where it should stand", YellowInfo);
    }
    bool _fillingLib;

    public void PickLibrary(string id) { Placing = Layout.Catalog()[id]; G.Cursor = Cursors.Cross; }

    public void StopPlacing()
    {
        if (Placing == null) return;
        Placing = null; G.Cursor = Cursors.Default; _libInfo.Text = "";
        _fillingLib = true; _libTree.SelectedNode = null; _fillingLib = false;
    }

    void PlaceAt(int mx, int my)
    {
        var p = PointUnder(mx, my);
        if (p == null || Placing == null) return;
        var objs = ObjsCopy();
        double turn0 = 0.0;
        if (Terrain.IsRamp(Placing) && D.Road() is { } rr && net0(rr) is { } nn)
        {
            // a ramp faces the way the cars drive on the nearest road
            double best = 1e18; foreach (var q in nn.Drivable()) for (int i = 0; i < q.Count; i++) { double d = Math.Pow(q.Centre[i].X - p[0], 2) + Math.Pow(q.Centre[i].Z - p[2], 2); if (d < best) { best = d; turn0 = q.Heading[i]; } }
            turn0 = Py.RoundN(turn0, 4);
        }
        Network? net0(Road r) { try { return Network.Get(D.Map, r); } catch (Exception) { return null; } }
        objs.Add(new JObj { ["id"] = Placing.Str("id"), ["x"] = Py.RoundN(p[0], 2), ["z"] = Py.RoundN(p[2], 2), ["h"] = Py.RoundN(Math.Max(0, p[1] - Ground), 2), ["turn"] = turn0 });
        Sel = "obj:" + (objs.Count - 1);
        SetObjects(objs, EditsCopy());
    }

    void ImportModel()
    {
        string? path = _f.PickFile("Import a 3D model", "glTF Binary (.glb)|*.glb|All files|*.*");
        if (path == null) return;
        ImportInfo info;
        try { info = ImportUi.Analyse(path); }
        catch (ImportProblem e) { _f.Msg("Import 3D model", e.Message); return; }
        catch (Exception e) when (e is not OutOfMemoryException) { _f.Msg("Import 3D model", "Could not read this file:\n" + e.Message); return; }
        if (!ImportWindow(info)) return;
        string id;
        try { id = ImportUi.Add(path, info); }
        catch (Exception e) when (e is ImportProblem or IOException) { _f.Msg("Import 3D model", e.Message); return; }
        Layout.ReloadCatalog();
        _libSearch.Text = "";
        FillLibrary();
        PickLibrary(id);
        _libInfo.Text = $"PLACING: {Placing!.Str("name")}\nclick in the view (Esc = stop)";
    }

    public Func<ImportInfo, bool>? TestImport;
    bool ImportWindow(ImportInfo i)
    {
        if (TestImport != null) return TestImport(i);
        var colours = new Dictionary<string, string> { ["ok"] = "#58d06a", ["info"] = "#5ab4ff", ["warn"] = "#ffd23b", ["stop"] = "#ff6b6b" };
        var marks = new Dictionary<string, string> { ["ok"] = "✔", ["info"] = "ℹ", ["warn"] = "⚠", ["stop"] = "✖" };
        using var w = new Form { Text = "Import 3D model", BackColor = Theme.Bg, ForeColor = Theme.Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        Look.Dress(w, "import");
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16), BackColor = Theme.Bg };
        p.Controls.Add(new Label { Text = "IMPORT 3D MODEL", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold });
        p.Controls.Add(new Label { Text = i.Name, AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold) });
        string tex = i.Textures.Count > 0 ? string.Join(", ", i.Textures.Select(t => $"{t.W} x {t.H}")) : "none";
        p.Controls.Add(new Label { Text = $"{i.Tris} triangles   ·   {i.Size[0]:0.0} x {i.Size[2]:0.0} x {i.Size[1]:0.0} m (wide x long x high)   ·   {i.Materials} material(s)   ·   texture {tex}", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 0, 0, 8) });
        foreach (var (level, text) in i.Notes) p.Controls.Add(new Label { Text = marks[level] + "  " + text, AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = level == "stop" ? ColorTranslator.FromHtml(colours[level]) : Theme.Text, Margin = new Padding(0, 2, 0, 2) });
        if (!i.Blocked) p.Controls.Add(new Label { Text = "It joins the object library under \"Your models\" (kept with your maps). Then click in the view to place it; G / R / S move, turn, scale it.", AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Font = new Font("Segoe UI", 8f) });
        var foot = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, 10, 0, 0) };
        bool ok = false;
        if (!i.Blocked) { var b = Theme.Btn("⬆ Import + place", () => { ok = true; w.Close(); }, Theme.Orange); b.Font = Theme.Bold; foot.Controls.Add(b); }
        foot.Controls.Add(Theme.Btn(i.Blocked ? "Close" : "Cancel", () => w.Close()));
        p.Controls.Add(foot);
        w.Controls.Add(p);
        w.ShowDialog(_f);
        return ok;
    }

    // ---- hover pictures
    HoverWin? _hover; string? _hoverId; readonly Dictionary<string, Bitmap?> _hoverPics = new();
    sealed class HoverWin : Form
    {
        public HoverWin() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Theme.Panel; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; StartPosition = FormStartPosition.Manual; Padding = new Padding(6); }
        protected override bool ShowWithoutActivation => true;
    }

    void LibHover(object? _, MouseEventArgs ev)
    {
        var n = _libTree.GetNodeAt(ev.X, ev.Y);
        if (n?.Tag is not JObj e) { HoverHide(); return; }
        var at = _libTree.PointToScreen(new Point(_libTree.Width + 8, ev.Y - 40));
        if (_hoverId == e.Str("id") && _hover != null) { _hover.Location = at; return; }
        HoverHide();
        _hoverId = e.Str("id");
        var w = new HoverWin { Location = at };
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel };
        var (img, text) = HoverPicture(e);
        var pic = new PictureBox { Width = 168, Height = 168, Image = img, BackColor = ColorTranslator.FromHtml("#0b1426") };
        var msg = new Label { Text = text, AutoSize = true, ForeColor = ColorTranslator.FromHtml("#f0e080") };
        if (img != null) p.Controls.Add(pic); else p.Controls.Add(new Label { Text = text, AutoSize = false, Width = 168, Height = 110, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ColorTranslator.FromHtml("#f0e080") });
        p.Controls.Add(new Label { Text = e.Str("name"), AutoSize = true, MaximumSize = new Size(180, 0), ForeColor = Theme.Text, Font = Theme.Bold, UseMnemonic = false });
        if (e.Truthy("sized")) { var lo = e.Arr("lo")!.Select(Json.D).ToArray(); var hi = e.Arr("hi")!.Select(Json.D).ToArray(); p.Controls.Add(new Label { Text = $"{hi[0] - lo[0]:0} x {hi[2] - lo[2]:0} x {hi[1] - lo[1]:0} m", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Font = new Font("Segoe UI", 8f) }); }
        w.Controls.Add(p);
        _hover = w;
        w.Show(_f);
    }

    public (Bitmap? Img, string Text) HoverPicture(JObj e)
    {
        string id = e.Str("id")!;
        if (_hoverPics.TryGetValue(id, out var c)) return (c, c != null ? "" : "No picture");
        if (e.Str("kind") == "import")
        {
            var pic = Previews.Render(Previews.Imported(e.Str("model")!), seconds: 1.5);
            var bm = pic != null ? Previews.ToBitmap(pic) : null;
            _hoverPics[id] = bm;
            return (bm, bm != null ? "" : "No picture (too big to draw quickly)");
        }
        string? model = e.Str("model");
        if (string.IsNullOrEmpty(model)) return (null, "No picture");
        string tp = Previews.ThumbPath(model);
        if (File.Exists(tp)) { using var s = File.OpenRead(tp); var bm = new Bitmap(s); _hoverPics[id] = bm; return (bm, ""); }
        var mats = Previews.Get(model, wait: true);
        if (mats == null) { _hoverPics[id] = null; return (null, "No picture (this model can't be read)"); }
        var p = Previews.Render(mats, seconds: 1.5);
        if (p == null) { _hoverPics[id] = null; return (null, "No picture (too big to draw quickly)"); }
        try { Previews.SavePng(p, tp); } catch (Exception) { }
        var b = Previews.ToBitmap(p); _hoverPics[id] = b;
        return (b, "");
    }

    void HoverHide() { _hoverId = null; if (_hover != null) { var h = _hover; _hover = null; h.Close(); h.Dispose(); } }

    bool _baking, _bakeStop;
    async void Bake()
    {
        if (_baking) { _bakeStop = true; _bakeBtn.Text = "⬚ Bake 3D pictures (stopping...)"; return; }
        var models = Layout.Catalog().Values.Where(e => e.Str("kind") != "import" && e.Truthy("model")).Select(e => e.Str("model")!).ToList();
        _baking = true; _bakeStop = false; _bakeBtn.BackColor = ColorTranslator.FromHtml("#ff8a1e");
        var (done, failed, total) = await Task.Run(() => Previews.Bake(models, (d, _, t) => _f.BeginInvoke(() => _bakeBtn.Text = $"⏸ Baking 3D pictures... {d} / {t}  (click = stop)"), () => _bakeStop));
        _baking = false;
        if (_bakeStop) { _bakeBtn.Text = "⬚ Bake 3D pictures (stopped - click to go on)"; _bakeBtn.BackColor = Theme.Blue; }
        else { _bakeBtn.Text = "✔ 3D pictures baked" + (failed > 0 ? $" ({failed} without a picture)" : ""); _bakeBtn.BackColor = ColorTranslator.FromHtml("#58d06a"); _bakeBtn.ForeColor = ColorTranslator.FromHtml("#0a0f18"); }
        _hoverPics.Clear();
    }

    // ---- the outliner
    readonly List<TreeNode> _outlSel = new();

    Panel BuildOutliner()
    {
        var bg = Pn;
        var f = new Panel { Dock = DockStyle.Right, Width = 270, BackColor = bg };
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = bg, Padding = new Padding(6) };
        _outlTitle = new Label { Text = "OBJECTS IN THIS MAP", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#8fb6e0"), Font = Theme.Bold };
        head.Controls.Add(_outlTitle);
        head.Controls.Add(new Label { Text = "Search:", AutoSize = true, ForeColor = Fg });
        _outlSearch = new TextBox { Width = 240, BackColor = ColorTranslator.FromHtml("#1a1e24"), ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle };
        _outlSearch.TextChanged += (_, _) => FillOutliner();
        head.Controls.Add(_outlSearch);
        var foot = new Label { Dock = DockStyle.Bottom, Height = 62, Text = "Eye = hide in this view only (not in the game)   Target = solo\nClick = select   Shift / Ctrl+click = more   Double-click = go to it\nDelete = delete   Ctrl+G = group   Ctrl+Shift+G = ungroup", ForeColor = ColorTranslator.FromHtml("#6f7c8a"), Font = new Font("Segoe UI", 7.5f), Padding = new Padding(4, 0, 0, 0) };
        _outlTree = new IconTree { Dock = DockStyle.Fill, BackColor = ColorTranslator.FromHtml("#08111f"), ForeColor = Fg, BorderStyle = BorderStyle.None, HideSelection = false, ShowLines = false, ItemHeight = 22, FullRowSelect = true, DrawMode = TreeViewDrawMode.OwnerDrawText };
        _outlTree.DrawNode += OutlDraw;
        _outlTree.MouseMove += OutlTip;
        Look.DarkScroll(_outlTree);
        _outlTree.NodeMouseClick += OutlClick;
        _outlTree.NodeMouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left && OutlZone(e.X) == null) OutlinerGo(); };
        _outlTree.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; DeleteSelection(); } };
        f.Controls.Add(_outlTree); f.Controls.Add(foot); f.Controls.Add(head);
        _outlTree.BringToFront();
        return f;
    }

    // ---- the eye + solo icons of the object list
    public readonly HashSet<string> ViewHidden = new();
    (int, int) _hiddenOn = (-1, -1);
    const int EyeW = 24;

    /// the objects a row stands for: one, a named group, or a whole list
    public List<string> RowKeys(string name)
    {
        if (name.StartsWith("grp:")) return Picks.Where(p => Group(p) == name[4..]).Select(SoloOf).Distinct().ToList();
        if (name.StartsWith("group:"))
        {
            var objs = D.Map.Arr("objects") ?? new();
            return Enumerable.Range(0, objs.Count).Where(i => ((JObj)objs[i]!).Str("group") == name[6..]).Select(i => "obj:" + i).ToList();
        }
        return new() { name };
    }

    bool CanSolo(string name) => !name.StartsWith("point:") && name != "grp:point";

    public void RowEye(string name)
    {
        var keys = RowKeys(name);
        if (keys.Count == 0) return;
        bool hide = !keys.All(ViewHidden.Contains);
        foreach (var k in keys) if (hide) ViewHidden.Add(k); else ViewHidden.Remove(k);
        if (hide)
        {
            if (Sel != null && PickOf(Sel) is { Multi: false } p && ViewHidden.Contains(SoloOf(p))) Select(null);
            else if (MultiSel.Count > 0 && MultiSel.All(ViewHidden.Contains)) Select(null);
        }
        if (_mesh != null) Compile(_mesh);
        _outlTree.Invalidate(); G.Invalidate();
        Info($"{(hide ? "Hidden" : "Shown again")} in this view: {keys.Count} object(s). The game is not changed", YellowInfo);
    }

    public void RowSolo(string name)
    {
        if (SoloKey == name) { SoloOff(); return; }
        var keys = RowKeys(name).Where(k => !k.StartsWith("point:")).ToHashSet();
        if (keys.Count == 0 || !Picks.Any(p => keys.Contains(SoloOf(p)))) { Info("Solo: this row has nothing to show", YellowInfo); return; }
        if (SoloKey == null) _soloCam = ((double[])Target.Clone(), Dist, Yaw, Pitch, Ortho);
        var one = Picks.FirstOrDefault(p => SoloOf(p) == name);
        SoloShow(name, keys, one != null ? one.Label : $"{keys.Count} object(s)");
        if (name.StartsWith("group:")) SetMulti(keys.ToList());
        else if (one != null) Select(one.Key);
    }

    string? OutlZone(int x)
    {
        int w = _outlTree.ClientSize.Width;
        return x >= w - EyeW ? "eye" : x >= w - 2 * EyeW ? "solo" : null;
    }

    string? _tipZone;
    void OutlTip(object? _, MouseEventArgs e)
    {
        var z = OutlZone(e.X);
        if (z == _tipZone) return;
        _tipZone = z;
        Look.Tip(_outlTree, z == "eye" ? "Hide / show in this view only" : z == "solo" ? "Show only this (click again = show everything)" : "");
    }

    void OutlDraw(object? _, DrawTreeNodeEventArgs e)
    {
        var n = e.Node; var t = _outlTree;
        if (n == null || e.Bounds.Height == 0) return;
        string name = n.Name ?? "";
        int W = t.ClientSize.Width, h = e.Bounds.Height, y = e.Bounds.Y;
        var keys = RowKeys(name);
        bool off = keys.Count > 0 && keys.All(ViewHidden.Contains), some = !off && keys.Any(ViewHidden.Contains);
        bool solo = SoloKey == name || (SoloKey != null && keys.Count > 0 && keys.All(SoloSet.Contains));
        var g = e.Graphics;
        using (var back = new SolidBrush(n.BackColor.IsEmpty ? t.BackColor : n.BackColor)) g.FillRectangle(back, e.Bounds.X, y, W - e.Bounds.X, h);
        var fore = n.ForeColor.IsEmpty ? t.ForeColor : n.ForeColor;
        if (off) fore = ColorTranslator.FromHtml("#56606c");
        TextRenderer.DrawText(g, Lang.T(n.Text), t.Font, new Rectangle(e.Bounds.X + 2, y, W - 2 * EyeW - e.Bounds.X - 4, h), fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        // the eye
        int ex = W - EyeW + 4, ey = y + h / 2;
        var col = off ? ColorTranslator.FromHtml("#56606c") : some ? ColorTranslator.FromHtml("#c08a1a") : ColorTranslator.FromHtml("#b8c6d6");
        using (var pen = new Pen(col, 1.5f))
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        using (var dot = new SolidBrush(col))
        {
            path.AddBezier(ex, ey, ex + 5, ey - 7, ex + 11, ey - 7, ex + 16, ey);
            path.AddBezier(ex + 16, ey, ex + 11, ey + 7, ex + 5, ey + 7, ex, ey);
            g.DrawPath(pen, path);
            g.FillEllipse(dot, ex + 5.5f, ey - 2.5f, 5, 5);
            if (off) using (var cut = new Pen(ColorTranslator.FromHtml("#ff6060"), 2f)) g.DrawLine(cut, ex + 1, ey + 7, ex + 15, ey - 7);
        }
        // the solo target
        if (CanSolo(name))
        {
            int sx = W - 2 * EyeW + 12, sy = ey;
            var sc = solo ? Theme.Warn : ColorTranslator.FromHtml("#7f8a96");
            using var ring = new Pen(sc, 1.5f); using var mid = new SolidBrush(sc);
            g.DrawEllipse(ring, sx - 7, sy - 7, 14, 14);
            if (solo) g.FillEllipse(mid, sx - 4, sy - 4, 8, 8); else g.FillEllipse(mid, sx - 1.5f, sy - 1.5f, 3, 3);
        }
    }

    string Group(Pick p) => p.Point >= 0 ? "point" : p.Pickup >= 0 ? "pickup" : p.Key.StartsWith("obj:") ? "obj" : p.Editable ? "side" : "fixed";

    public void FillOutliner()
    {
        var t = _outlTree;
        if (t == null) return;
        foreach (TreeNode n in t.Nodes)
        {
            Remember(n);
            foreach (TreeNode c in n.Nodes) if ((c.Name ?? "").StartsWith("group:")) Remember(c);
        }
        void Remember(TreeNode n) { if (n.IsExpanded) _outlOpen.Add(n.Name); else _outlOpen.Remove(n.Name); }
        t.BeginUpdate(); t.Nodes.Clear(); _outlSel.Clear();
        string find = _outlSearch.Text.Trim().ToLowerInvariant();
        var rows = new Dictionary<string, Dictionary<string, string>>();
        foreach (var p in Picks)
        {
            string iid = SoloOf(p), g = Group(p);
            if (!rows.TryGetValue(g, out var r)) rows[g] = r = new();
            if (r.ContainsKey(iid)) continue;
            string shortL = p.Label.Split("  (")[0].Split(" - ")[0], text = $"{shortL}   [{iid}]";
            if (find != "" && !text.ToLowerInvariant().Contains(find)) continue;
            r[iid] = text;
        }
        var objs = D.Map.Arr("objects") ?? new(); var cat = Layout.Catalog();
        if (!rows.ContainsKey("obj")) rows["obj"] = new();
        for (int i = 0; i < objs.Count; i++)
        {
            string iid = "obj:" + i;
            if (rows["obj"].ContainsKey(iid)) continue;
            string text = $"{cat.GetValueOrDefault(((JObj)objs[i]!).Str("id") ?? "")?.Str("name") ?? "?"}   [{iid}]   - does not fit, not shown";
            if (find == "" || text.ToLowerInvariant().Contains(find)) rows["obj"][iid] = text;
        }
        int total = 0;
        foreach (var (g, title) in new[] { ("obj", "Placed objects"), ("side", "Roadside & scenery"), ("fixed", "Road layout (fixed)"), ("pickup", "Power-ups"), ("point", "Track points") })
        {
            if (!rows.TryGetValue(g, out var items) || items.Count == 0) continue;
            items = new(items);
            var node = t.Nodes.Add("grp:" + g, $"{title}  ({items.Count})"); node.ForeColor = ColorTranslator.FromHtml("#8fb6e0");
            if (g == "obj")
            {
                var groups = new Dictionary<string, List<string>>();
                foreach (var iid in items.Keys)
                {
                    int i = int.Parse(iid[4..]);
                    string? name = i < objs.Count ? ((JObj)objs[i]!).Str("group") : null;
                    if (!string.IsNullOrEmpty(name)) { if (!groups.TryGetValue(name, out var l)) groups[name] = l = new(); l.Add(iid); }
                }
                foreach (var (name, members) in groups)
                {
                    var gn = node.Nodes.Add("group:" + name, $"▣ {name}  ({members.Count})"); gn.ForeColor = ColorTranslator.FromHtml("#8fb6e0");
                    foreach (var iid in members) { gn.Nodes.Add(iid, items[iid]); items.Remove(iid); total++; }
                    if (find != "" || _outlOpen.Contains(gn.Name)) gn.Expand();
                }
            }
            foreach (var (iid, text) in items) { node.Nodes.Add(iid, text); total++; }
            if (find != "" || _outlOpen.Contains(node.Name)) node.Expand();
        }
        t.EndUpdate();
        _outlTitle.Text = $"OBJECTS IN THIS MAP  ({total})";
        OutlinerShow(PickOf(Sel));
    }

    TreeNode? Node(string key) => _outlTree.Nodes.Find(key, true).FirstOrDefault();

    void PaintOutl()
    {
        foreach (var n in _outlSel) { if (n.TreeView != null) { n.BackColor = Color.Empty; n.ForeColor = (n.Name ?? "").StartsWith("gr") ? ColorTranslator.FromHtml("#8fb6e0") : Fg; } }
    }

    void OutlinerShow(Pick? p)
    {
        if (_outlTree == null) return;
        PaintOutl(); _outlSel.Clear();
        _outlSync = true;
        var iids = MultiSel.Count > 0 ? MultiSel.ToList() : p != null ? new List<string> { SoloOf(p) } : new();
        foreach (var k in iids)
        {
            var n = Node(k);
            if (n == null) continue;
            n.BackColor = Theme.Blue; n.ForeColor = Color.White; _outlSel.Add(n);
            n.EnsureVisible();
        }
        _outlSync = false;
    }

    void OutlClick(object? _, TreeNodeMouseClickEventArgs e)
    {
        var n = e.Node; string name = n.Name ?? "";
        if (e.Button == MouseButtons.Left && OutlZone(e.X) is { } zone)
        {
            if (zone == "eye") RowEye(name); else if (CanSolo(name)) RowSolo(name);
            return;
        }
        if (e.Button == MouseButtons.Right) { if (name.StartsWith("grp:")) return; if (!_outlSel.Contains(n)) OutlPick(n, false, false); OutlMenu(e.Location); return; }
        if (e.Button != MouseButtons.Left) return;
        if (name.StartsWith("grp:")) return;
        var m = Control.ModifierKeys;
        OutlPick(n, (m & Keys.Control) != 0, (m & Keys.Shift) != 0);
    }

    TreeNode? _outlAnchor;
    public void OutlPick(TreeNode n, bool ctrl, bool shift)
    {
        var sel = _outlSel.Where(q => q.TreeView != null).ToList();
        if (shift && _outlAnchor?.Parent == n.Parent && n.Parent != null)
        {
            int a = _outlAnchor.Index, b = n.Index;
            sel = n.Parent.Nodes.Cast<TreeNode>().Where(q => q.Index >= Math.Min(a, b) && q.Index <= Math.Max(a, b)).ToList();
        }
        else if (ctrl) { if (sel.Contains(n)) sel.Remove(n); else sel.Add(n); _outlAnchor = n; }
        else { sel = new() { n }; _outlAnchor = n; }
        var objs = D.Map.Arr("objects") ?? new();
        var keys = new List<string>(); bool grp = false;
        foreach (var q in sel)
        {
            string iid = q.Name;
            if (iid.StartsWith("grp:")) continue;
            if (iid.StartsWith("group:")) { grp = true; for (int i = 0; i < objs.Count; i++) if (((JObj)objs[i]!).Str("group") == iid[6..]) keys.Add("obj:" + i); }
            else if (iid.StartsWith("obj:")) keys.Add(iid);
            else keys.AddRange(Picks.Where(p => SoloOf(p) == iid).Take(1).Select(p => p.Key));
        }
        keys = keys.Distinct().ToList();
        if (keys.Count == 1 && PickOf(keys[0]) != null && !grp) Select(keys[0]);
        else if (keys.Count > 0) SetMulti(keys);
        else Select(null);
    }

    public Action? OutlinerMenuTest;

    /// the robot's clicks in the two lists + the hover window
    public int TestLists()
    {
        int ok = 0;
        var objs = _outlTree.Nodes.Find("grp:obj", false).FirstOrDefault();
        if (objs is { Nodes.Count: >= 2 })
        {
            objs.Expand();
            OutlPick(objs.Nodes[0], false, false); if (Sel == objs.Nodes[0].Name) ok++;
            OutlPick(objs.Nodes[1], true, false); if (MultiSel.Count == 2) ok++;
            OutlMenu(new Point(10, 10)); ok++;
            OutlinerGo(); Select(null);
        }
        TreeNode? leaf = null;
        void Walk(TreeNodeCollection c) { foreach (TreeNode n in c) { if (leaf != null) return; if (n.Tag is JObj) leaf = n; else Walk(n.Nodes); } }
        Walk(_libTree.Nodes);
        if (leaf != null)
        {
            leaf.EnsureVisible();
            _libTree.SelectedNode = leaf; if (Placing != null) ok++;
            StopPlacing();
            var b = leaf.Bounds;
            LibHover(null, new MouseEventArgs(MouseButtons.None, 0, b.X + 4, b.Y + 4, 0));
            if (_hover is { Visible: true }) ok++;
            HoverHide();
        }
        return ok;
    }
    void OutlMenu(Point at)
    {
        var idx = SelObjects(); var objs = D.Map.Arr("objects") ?? new();
        bool grouped = idx.Any(i => ((JObj)objs[i]!).Truthy("group"));
        var m = new ContextMenuStrip();
        m.Items.Add("Duplicate   (Ctrl+D)", null, (_, _) => Duplicate()).Enabled = idx.Count > 0;
        m.Items.Add("Group   (Ctrl+G)", null, (_, _) => GroupSelection()).Enabled = idx.Count >= 2;
        m.Items.Add("Ungroup   (Ctrl+Shift+G)", null, (_, _) => UngroupSelection()).Enabled = grouped;
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Delete   (Delete)", null, (_, _) => DeleteSelection());
        Lang.Items(m.Items);
        m.Show(_outlTree, at);
    }

    void OutlinerGo()
    {
        var p = PickOf(Sel);
        if (p == null) return;
        var cs = BoxCorners(p);
        double[] lo = { cs.Min(c => c[0]), cs.Min(c => c[1]), cs.Min(c => c[2]) }, hi = { cs.Max(c => c[0]), cs.Max(c => c[1]), cs.Max(c => c[2]) };
        Target = V((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
        Dist = Math.Min(Dist, Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2])) * 4 + 40);
        DriveT = null; CamAnim = null;
        G.Invalidate();
    }

    // ---- Path scatter
    public sealed class ScatterState { public Form Win = null!; public List<double[]> Pts = new(); public bool Drawing; public List<string> Items = new(); public int Seed = 1; public List<ScatterSpot> Spots = new(); public Dictionary<string, TextBox> F = new(); public ComboBox Side = null!; public RadioButton Rnd = null!; public CheckBox Align = null!; public ListBox List = null!; public Button DrawB = null!, Add = null!; public Label Path = null!, Info = null!; }
    public ScatterState? Sc;
    public bool ScatterDrawing => Sc is { Drawing: true };

    public void ScatterOpen()
    {
        if (Sc != null) { Sc.Win.BringToFront(); return; }
        if (SceneMode != "3d") return;
        StopPlacing(); Select(null);
        var win = new Form { Text = "Path scatter", BackColor = Pn, ForeColor = Fg, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.Manual };
        Look.Dress(win, "scatter");
        var sc = Sc = new ScatterState { Win = win };
        var page = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8), BackColor = Pn };
        void Head(string t) => page.Controls.Add(new Label { Text = t, AutoSize = true, ForeColor = ColorTranslator.FromHtml("#8fb6e0"), Font = Theme.Bold, Margin = new Padding(0, 8, 0, 1) });
        FlowLayoutPanel Row() { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Pn, Margin = new Padding(0, 1, 0, 1) }; page.Controls.Add(r); return r; }
        void Field(FlowLayoutPanel r, string label, string key, string def, int w = 44)
        {
            r.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Fg, Margin = new Padding(0, 5, 2, 0) });
            var t = new TextBox { Text = def, Width = w, BackColor = ColorTranslator.FromHtml("#1a1e24"), ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
            t.TextChanged += (_, _) => ScatterUpdate();
            sc.F[key] = t; r.Controls.Add(t);
        }
        Head("1  PATH");
        var a = Row();
        sc.DrawB = Theme.Btn("✎ Draw path", () => ScatterDrawToggle(null), Theme.Blue); a.Controls.Add(sc.DrawB);
        a.Controls.Add(Theme.Btn("↶ Last point", ScatterPop)); a.Controls.Add(Theme.Btn("✖ Clear", () => { sc.Pts = new(); ScatterUpdate(); }));
        sc.Path = new Label { AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = ColorTranslator.FromHtml("#6f7c8a"), Font = new Font("Segoe UI", 8f) }; page.Controls.Add(sc.Path);
        Head("2  OBJECTS");
        page.Controls.Add(new Label { Text = "Click objects in the OBJECT LIBRARY (left) to add them here", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#6f7c8a"), Font = new Font("Segoe UI", 8f) });
        sc.List = new ListBox { Width = 330, Height = 90, BackColor = ColorTranslator.FromHtml("#08111f"), ForeColor = Fg, BorderStyle = BorderStyle.None, IntegralHeight = false }; page.Controls.Add(sc.List);
        var b = Row();
        b.Controls.Add(Theme.Btn("✖ Remove", () => { int i = sc.List.SelectedIndex >= 0 ? sc.List.SelectedIndex : sc.Items.Count - 1; if (i >= 0) { sc.Items.RemoveAt(i); sc.List.Items.RemoveAt(i); ScatterUpdate(); } }));
        sc.Rnd = new RadioButton { Text = "random", AutoSize = true, ForeColor = Fg, Checked = true, Margin = new Padding(6, 4, 2, 0) };
        var turn = new RadioButton { Text = "in turn", AutoSize = true, ForeColor = Fg, Margin = new Padding(6, 4, 2, 0) };
        sc.Rnd.CheckedChanged += (_, _) => ScatterUpdate();
        b.Controls.Add(sc.Rnd); b.Controls.Add(turn);
        Head("3  LAYOUT");
        var c = Row(); Field(c, "Every (m)", "spacing", "10"); Field(c, "Fill (%)", "fill", "100");
        var d = Row();
        d.Controls.Add(new Label { Text = "Side", AutoSize = true, ForeColor = Fg, Margin = new Padding(0, 5, 2, 0) });
        sc.Side = Theme.Combo(96); sc.Side.Items.AddRange(new object[] { "middle", "left", "right", "both sides" }); sc.Side.SelectedIndex = 0; sc.Side.SelectedIndexChanged += (_, _) => ScatterUpdate(); d.Controls.Add(sc.Side);
        Field(d, "Distance from path (m)", "side_dist", "0");
        Head("4  RANDOM");
        var e = Row();
        sc.Align = new CheckBox { Text = "Face along the path", AutoSize = true, ForeColor = Fg, Checked = true, Margin = new Padding(0, 4, 8, 0) }; sc.Align.CheckedChanged += (_, _) => ScatterUpdate(); e.Controls.Add(sc.Align);
        Field(e, "+ turn (°)", "turn", "0");
        var g = Row(); Field(g, "Random turn ± (°)", "turn_rand", "0"); Field(g, "Spread ± (m)", "spread", "0");
        var h = Row(); Field(h, "Size from", "size_min", "1"); Field(h, "to", "size_max", "1");
        h.Controls.Add(Theme.Btn("🎲 New random", () => { sc.Seed++; ScatterUpdate(); }));
        page.Controls.Add(new Label { Text = "(breakable objects keep their size)", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#6f7c8a"), Font = new Font("Segoe UI", 8f) });
        var k = Row(); k.Margin = new Padding(0, 10, 0, 8);
        sc.Add = Theme.Colored("✔ Add", "#1f9d55", ScatterApply); k.Controls.Add(sc.Add); k.Controls.Add(Theme.Colored("Close", "#4a2a2a", ScatterClose));
        sc.Info = new Label { AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = ColorTranslator.FromHtml("#f0e080") }; page.Controls.Add(sc.Info);
        win.Controls.Add(page);
        win.FormClosed += (_, _) => { if (Sc != null) { Sc = null; G.Cursor = Cursors.Default; G.Invalidate(); } };
        var at = _f.PointToScreen(new Point(Math.Max(0, _f.ClientSize.Width - 660), 120));
        win.Location = at;
        win.Show(_f);
        ScatterDrawToggle(true);
    }

    public void ScatterClose() { if (Sc == null) return; var w = Sc.Win; Sc = null; G.Cursor = Cursors.Default; w.Close(); G.Invalidate(); }

    public void ScatterDrawToggle(bool? on)
    {
        var sc = Sc!;
        sc.Drawing = on ?? !sc.Drawing;
        sc.DrawB.Text = sc.Drawing ? "✔ Done drawing" : "✎ Draw path"; sc.DrawB.BackColor = sc.Drawing ? ColorTranslator.FromHtml("#c08a1a") : Theme.Blue;
        G.Cursor = sc.Drawing ? Cursors.Cross : Cursors.Default;
        ScatterUpdate();
    }

    void ScatterClick(int mx, int my) { var p = PointUnder(mx, my); if (p != null) { Sc!.Pts.Add(p); ScatterUpdate(); } }
    void ScatterPop() { if (Sc is { Pts.Count: > 0 }) { Sc.Pts.RemoveAt(Sc.Pts.Count - 1); ScatterUpdate(); } }
    public void ScatterLibAdd(JObj e) { Sc!.Items.Add(e.Str("id")!); Sc.List.Items.Add($"{e.Str("name")}   ({UiText.KindText.GetValueOrDefault(e.Str("kind") ?? "", e.Str("kind") ?? "")})"); ScatterUpdate(); }

    double Num(string key, double def, double? lo = null, double? hi = null)
    {
        double v = double.TryParse(Sc!.F[key].Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double q) ? q : def;
        if (lo != null) v = Math.Max(lo.Value, v);
        if (hi != null) v = Math.Min(hi.Value, v);
        return v;
    }

    void ScatterUpdate()
    {
        var sc = Sc;
        if (sc == null || sc.F.Count < 8 || sc.Add == null || sc.Info == null) return;
        var pts = sc.Pts; double length = 0;
        for (int i = 0; i + 1 < pts.Count; i++) length += Math.Sqrt(Math.Pow(pts[i + 1][0] - pts[i][0], 2) + Math.Pow(pts[i + 1][2] - pts[i][2], 2));
        sc.Path.Text = $"{pts.Count} point(s), {length:0} m{(sc.Drawing ? "   -   click on the ground in the 3D view to add points; Backspace = remove the last, Enter / right-click = done" : "")}";
        var opt = new ScatterOpt { Spacing = Num("spacing", 10, .5), Side = (string)sc.Side.SelectedItem!, SideDist = Num("side_dist", 0), Fill = Num("fill", 100, 0, 100), Align = sc.Align.Checked, Turn = Num("turn", 0), TurnRand = Num("turn_rand", 0, 0, 180), SizeMin = Num("size_min", 1, .05, 50), SizeMax = Num("size_max", 1, .05, 50), Spread = Num("spread", 0, 0), Order = sc.Rnd.Checked ? "random" : "in turn" };
        sc.Spots = Scatter.PathSpots(pts, sc.Items, opt, sc.Seed);
        var cat = Layout.Catalog();
        int tilesFree = TileFree, lampsFree = Layout.LampPlacedMax - D.Map.Objs("objects").Count(o => cat.GetValueOrDefault(o.Str("id") ?? "")?.Str("kind") == "breakable");
        foreach (var s in sc.Spots)
        {
            var e = cat.GetValueOrDefault(s.Id);
            if (e?.Str("kind") == "breakable") { s.Fits = lampsFree > 0; if (s.Fits) lampsFree--; }
            else { int need = e?.Str("kind") == "import" ? Math.Max(1, (int)e.Int("pieces")) : 1; s.Fits = need <= tilesFree; if (s.Fits) tilesFree -= need; }
        }
        int n = sc.Spots.Count(s => s.Fits), lost = sc.Spots.Count - n;
        sc.Add.Text = $"✔ Add {n} object{(n == 1 ? "" : "s")}"; sc.Add.Enabled = n > 0;
        sc.Info.Text = pts.Count < 2 ? "Draw a path: at least 2 points." : sc.Items.Count == 0 ? "Now pick objects in the library on the left." : lost > 0 ? $"Pink = where they go. RED = {lost} that do NOT fit: a map has 144 places for big objects (road pieces use some; each of your own models needs one place per piece). Only the pink ones are added - fewer copies (bigger Every / lower Fill) or smaller models help." : "Pink = where they go. Change the numbers, then Add.";
        G.Invalidate();
    }

    public void ScatterApply()
    {
        var sc = Sc!;
        var spots = sc.Spots.Where(s => s.Fits).ToList();
        if (spots.Count == 0) return;
        var cat = Layout.Catalog(); var objs = ObjsCopy(); var edits = EditsCopy();
        string group = NewGroupName("Scatter");
        _outlOpen.Add("group:" + group);
        foreach (var s in spots)
        {
            objs.Add(new JObj { ["id"] = s.Id, ["x"] = Py.RoundN(s.X, 2), ["z"] = Py.RoundN(s.Z, 2), ["h"] = Py.RoundN(Math.Max(0, s.Y - Ground), 2), ["turn"] = Py.RoundN(s.Turn, 4), ["group"] = group });
            if (Math.Abs(s.Scale - 1) > 1e-3 && cat.GetValueOrDefault(s.Id)?.Str("kind") != "breakable") edits["obj:" + (objs.Count - 1)] = new JObj { ["scale"] = Py.RoundN(s.Scale, 3) };
        }
        sc.Pts = new(); sc.Seed++;
        SetObjects(objs, edits);
        ScatterDrawToggle(true);
        sc.Info.Text = $"Added {spots.Count} objects (Ctrl+Z takes them all away). Draw the next path, or Close.";
    }

    void ScatterDraw()
    {
        var sc = Sc;
        if (sc == null) return;
        GL.glDisable(GL.DEPTH_TEST);
        var pts = sc.Pts; var cyan = new Rgba(.2f, .9f, 1);
        if (pts.Count > 0)
        {
            var l = new Batch(); var d = new Batch();
            for (int i = 0; i < pts.Count; i++)
            {
                d.Add((float)pts[i][0], (float)(pts[i][1] + .3), (float)pts[i][2], cyan);
                if (i + 1 < pts.Count) { l.Add((float)pts[i][0], (float)(pts[i][1] + .3), (float)pts[i][2], cyan); l.Add((float)pts[i + 1][0], (float)(pts[i + 1][1] + .3), (float)pts[i + 1][2], cyan); }
            }
            GL.glLineWidth(3); l.Draw(GL.LINES); GL.glPointSize(9); d.Draw(GL.POINTS); GL.glPointSize(1);
        }
        var cat = Layout.Catalog(); var b = new Batch();
        foreach (var s in sc.Spots)
        {
            var col = s.Fits ? new Rgba(1, .35f, .85f) : new Rgba(1, .1f, .1f);
            var e = cat.GetValueOrDefault(s.Id);
            double x0 = -1, z0 = -1, x1 = 1, z1 = 1;
            if (e != null && e.Truthy("sized")) { var lo = e.Arr("lo")!; var hi = e.Arr("hi")!; x0 = Json.D(lo[0]); z0 = Json.D(lo[2]); x1 = Json.D(hi[0]); z1 = Json.D(hi[2]); }
            double k = e?.Str("kind") != "breakable" ? s.Scale : 1;
            var A = Layout.ActorAxes3(s.Turn, 0, 0);
            float Px(double a, double c) => (float)(s.X + (A[0][0] * a + A[2][0] * c) * k);
            float Pz(double a, double c) => (float)(s.Z + (A[0][2] * a + A[2][2] * c) * k);
            float y = (float)(s.Y + .25);
            var c2 = new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) };
            for (int i = 0; i < 4; i++) { var (a, q) = c2[i]; var (a2, q2) = c2[(i + 1) % 4]; b.Add(Px(a, q), y, Pz(a, q), col); b.Add(Px(a2, q2), y, Pz(a2, q2), col); }
            b.Add((float)s.X, y, (float)s.Z, col); b.Add(Px(0, z1), y, Pz(0, z1), col);
        }
        GL.glLineWidth(1.5f); b.Draw(GL.LINES); GL.glLineWidth(1);
        GL.glEnable(GL.DEPTH_TEST);
    }
}

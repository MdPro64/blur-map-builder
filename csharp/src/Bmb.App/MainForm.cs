// The main window: bars, side panels, tabs, layouts, keys. Follows road_designer.py + ui_layout.py.
using System.Diagnostics;
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm : Form
{
    public readonly Doc D = new();
    public readonly GlControl G = new() { Dock = DockStyle.Fill };
    public MapView Map2; public View3D V3;
    public string Mode = "2d";
    readonly Dictionary<string, Control> _bars = new();
    readonly Dictionary<string, CheckBox> _tabs = new();
    readonly Dictionary<string, List<CheckBox>> _tools = new();
    readonly Panel _toolRow = new() { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Panel2 };
    readonly ListBox _tree = new() { Dock = DockStyle.Fill, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, IntegralHeight = false, Font = new Font("Segoe UI Symbol", 10f) };
    readonly RichTextBox _checks = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f), ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = false };
    readonly FlowLayoutPanel _props = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(4) };
    readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Theme.Text, Font = Theme.Ui, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
    readonly ComboBox _layoutBox = Theme.Combo(140);
    readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly Panel _leftP = new() { Dock = DockStyle.Left, Width = 210, BackColor = Theme.Panel };
    readonly Panel _rightP = new() { Dock = DockStyle.Right, Width = 320, BackColor = Theme.Panel };
    readonly Panel _lowP = new() { Dock = DockStyle.Bottom, Height = 230, BackColor = Theme.Panel };
    public readonly Panel Centre = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly bool _test;
    readonly Button _build, _update;
    Panel _lightPanel = null!; CarPanel _car = null!;

    public MainForm(bool test = false)
    {
        _test = test;
        Text = $"Blur Map Builder by MDPRO {Paths.Version} - Track Designer";
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui;
        KeyPreview = true; MinimumSize = new Size(960, 600); Size = new Size(1500, 880); StartPosition = FormStartPosition.CenterScreen;
        Look.Dress(this);
        DoubleBuffered = true;
        Map2 = new MapView(D, G); V3 = new View3D(this, D, G);
        G.Render += () => { if (Is3D) V3.Draw(); else Map2.Draw(); };
        if (Doc.AutosaveNewer() && !test && MessageBox.Show("There are unsaved changes from last time (auto-save). Open them?", "Auto-save", MessageBoxButtons.YesNo) == DialogResult.Yes) { D.Load(); D.LoadAutosave(); D.Dirty = true; }
        else D.Load();
        _build = Theme.Btn("▶ Build + install (Ctrl+B)", Build, Theme.Orange);
        _update = Theme.Btn("⟳ Check for updates", () => CheckUpdates(false));
        MakeUi();
        Map2.Edited += Changed; Map2.Selected += RefreshProps; Map2.Say += (s, c) => Say(s, c);
        V3.Edited += Changed;
        LoadLayout();
        Shown += (_, _) => { ApplySizes(true); FitTop(); Map2.Fit(); Tree(); Refresh2(); SetMode("2d"); Startup(); };
        FormClosing += Closing2;
        FormClosed += (_, _) => { if (_restart) Restart(); };
        ResizeEnd += (_, _) => { FitPanels(); SaveLayoutState(); if (Mode == "light") LightBuild(); };
        SizeChanged += (_, _) => { if (IsHandleCreated) { FitTop(); FitPanels(); } };
    }

    public bool Is3D => Mode is "3d" or "collision";

    // ---- layout of the window ---------------------------------------------------------------------------------
    readonly List<(string Name, Control C, Splitter S, int Default, bool Height)> _panels = new();

    Splitter Sash(DockStyle dock, string name, Control c, int def, bool height = false)
    {
        var s = new Splitter { Dock = dock, BackColor = ColorTranslator.FromHtml("#1c2838"), MinSize = 110, MinExtra = 320 };
        if (height) s.Height = 5; else s.Width = 5;
        s.MouseEnter += (_, _) => s.BackColor = Theme.Blue;
        s.MouseLeave += (_, _) => s.BackColor = ColorTranslator.FromHtml("#1c2838");
        s.SplitterMoved += (_, _) => { _sizes[name] = (long)(height ? c.Height : c.Width); SaveLayoutState(); };
        s.DoubleClick += (_, _) => { _sizes.Remove(name); ApplySizes(false); SaveLayoutState(); };
        _panels.Add((name, c, s, def, height));
        return s;
    }

    public Splitter PanelSash(DockStyle dock, string name, Control c, int def, bool height = false) => Sash(dock, name, c, def, height);

    void MakeUi()
    {
        var bar = _topPanel;
        var right = _topRight;
        right.Controls.Add(Theme.Btn("⚙", UpdateSource));
        right.Controls.Add(_update);
        _save = Theme.Btn("✔ Save (Ctrl+S)", DoSave); right.Controls.Add(_save);
        _play = Theme.Btn("▶ Play (F5)", () => PlayDialog(), Theme.Blue); _play.Font = Theme.Bold; right.Controls.Add(_play);
        _build.Font = Theme.Bold;
        right.Controls.Add(_build);
        var tabs = _topTabs;
        foreach (var (val, txt) in new[] { ("2d", "▦ 2D map"), ("3d", "▲ 3D"), ("collision", "⚠ Collision"), ("ai", "➿ AI path"), ("light", "☀ Lighting"), ("surface", "▩ Surface"), ("car", "⛟ Custom car") })
        {
            var t = Theme.Toggle(txt, on => { if (on) SetMode(val); });
            t.Font = Theme.Bold;
            _tabs[val] = t; tabs.Controls.Add(t);
        }
        bar.Controls.Add(tabs); bar.Controls.Add(right);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Panel };
        var lay = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(0, 2, 4, 0) };
        lay.Controls.Add(Theme.Lbl("Layout")); lay.Controls.Add(_layoutBox);
        lay.Controls.Add(Theme.Btn("Load", LayoutLoad)); lay.Controls.Add(Theme.Btn("Save", LayoutSave)); lay.Controls.Add(Theme.Btn("Delete", LayoutDelete)); lay.Controls.Add(Theme.Btn("Reset current layout", LayoutReset));
        bottom.Controls.Add(_status); bottom.Controls.Add(lay);
        _layoutBar = lay;
        var langBar = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(4, 2, 6, 0) };
        _langBox.Items.Add("English"); _langBox.Items.Add("Русский");
        _langBox.SelectedIndex = Lang.Ru ? 1 : 0;
        _langBox.SelectionChangeCommitted += (_, _) => LanguageChanged();
        langBar.Controls.Add(new Label { Text = "🌐", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Ui, Margin = new Padding(0, 5, 2, 0) });
        langBar.Controls.Add(Look.Tip(_langBox, "Language"));
        bottom.Controls.Add(langBar);

        MakeToolBars();

        // body: tree | sash | centre | sash | properties + checks
        var tp = _leftP;
        tp.Controls.Add(_tree);
        var head = new Label { Text = "LOCATIONS & MAPS", UseMnemonic = false, Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Head, Font = Theme.Bold, Padding = new Padding(4, 6, 0, 0) };
        var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(4) };
        foreach (var (t, a, c) in new (string, Action, string?)[] { ("✚  New map (Ctrl+N)", NewMap, null), ("⌂  New location (Ctrl+L)", NewLocation, null), ("⧉  Duplicate map (Ctrl+D)", DupMap, null), ("✎  Rename (F2)", Rename, null),
            ("✖  Delete map", DeleteMap, "#7a2a3a"), ("✖  Delete location", DeleteLocation, "#7a2a3a"), ("⇩  Import map (.blurmap)", ImportMap, null), ("⇧  Export this map", ExportMap, null) })
        {
            var b = Theme.Btn(t, a, c != null ? ColorTranslator.FromHtml(c) : null);
            b.AutoSize = false; b.Width = 196; b.Height = 28; b.TextAlign = ContentAlignment.MiddleLeft; btns.Controls.Add(b);
            _treeBtns.Add(b);
        }
        tp.Controls.Add(head); tp.Controls.Add(btns);
        tp.Resize += (_, _) => { foreach (var q in _treeBtns) q.Width = Math.Max(60, tp.ClientSize.Width - 12); };
        _tree.BringToFront();
        _lowP.Controls.Add(_checks);
        _lowP.Controls.Add(new Label { Text = "CHECKS", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Head, Font = Theme.Bold, Padding = new Padding(4, 4, 0, 0) });
        _checks.BringToFront();
        _rightP.Controls.Add(_props);
        var lowS = Sash(DockStyle.Bottom, "checks", _lowP, 230, true);
        _rightP.Controls.Add(lowS); _rightP.Controls.Add(_lowP);
        _props.BringToFront();
        var ls = Sash(DockStyle.Left, "left", _leftP, 210); var rs = Sash(DockStyle.Right, "right", _rightP, 320);
        _leftSash = ls; _rightSash = rs;
        _body.Controls.Add(Centre); _body.Controls.Add(ls); _body.Controls.Add(_leftP); _body.Controls.Add(rs); _body.Controls.Add(_rightP);
        Centre.BringToFront();
        _lightPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Visible = false };
        _car = new CarPanel(this) { Dock = DockStyle.Fill, Visible = false };
        Centre.Controls.Add(G);
        Centre.Controls.Add(_lightPanel); Centre.Controls.Add(_car);
        V3.MakePanels(Centre);
        Foldable("left", _leftP, true); Foldable("right", _rightP, false);
        G.BringToFront();
        Controls.Add(_body); Controls.Add(_toolRow); Controls.Add(bar); Controls.Add(bottom);
        _body.BringToFront();
        Look.DarkScroll(_tree, _checks, _props, _lightPanel);
        _tree.SelectedIndexChanged += (_, _) => TreePick();
        _tree.DrawMode = DrawMode.OwnerDrawFixed; _tree.ItemHeight = 22;
        _tree.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(Theme.Field)) e.Graphics.FillRectangle(back, e.Bounds);
            if (sel)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var row = Look.RoundPath(new RectangleF(e.Bounds.X + 3, e.Bounds.Y + 1, e.Bounds.Width - 7, e.Bounds.Height - 2), 5);
                using var bg = new SolidBrush(Theme.Blue); e.Graphics.FillPath(bg, row);
            }
            TextRenderer.DrawText(e.Graphics, (string)_tree.Items[e.Index], _tree.Font, e.Bounds, sel ? Color.White : _treeRows[e.Index].M < 0 ? Theme.Head : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    readonly List<Button> _treeBtns = new();
    FlowLayoutPanel Bar() => new() { Dock = DockStyle.Top, WrapContents = true, BackColor = Theme.Panel2, Padding = new Padding(4, 3, 0, 3) };

    /// the tool bar's height: its buttons wrap onto more lines on a narrow window (ui_layout.flow)
    void FitBar()
    {
        int w = Math.Max(_toolRow.Width, 200), total = 0;
        foreach (Control c in _toolRow.Controls)
        {
            if (!c.Visible) continue;
            int h = 0;
            if (c is FlowLayoutPanel f) { h = f.GetPreferredSize(new Size(w, 0)).Height; f.Height = h; }
            else { foreach (Control q in c.Controls) if (q is FlowLayoutPanel f2) { int e = f2.GetPreferredSize(new Size(w, 0)).Height; f2.Height = e; h += e; } c.Height = h; }
            total += h;
        }
        if (total > 0 && _toolRow.Height != total) _toolRow.Height = total;
    }

    CheckBox ToolBtn(string key)
    {
        var (l, hk) = MapView.ToolInfo[key];
        string icon = key switch { "select" => "⬉", "move" => "✥", "add" => "✚", "insert" => "⤓", "row" => "•••", "single" => "●", "erase" => "✖", "branch" => "↱", "split" => "‖", _ => "⇔" };
        var t = Theme.Toggle($"{icon} {l} ({hk})", on => { if (on && !_settingTool) SetTool(key); });
        if (!_tools.TryGetValue(key, out var lst)) _tools[key] = lst = new();
        lst.Add(t);
        return t;
    }

    static Panel Group(FlowLayoutPanel parent, string title)
    {
        var g = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel2, Margin = new Padding(4, 0, 4, 0) };
        g.Controls.Add(new Label { Text = title, UseMnemonic = false, AutoSize = true, ForeColor = Theme.Head, Font = Theme.Small, Margin = new Padding(0) });
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel2, Margin = new Padding(0) };
        g.Controls.Add(row); parent.Controls.Add(g);
        return row;
    }

    public readonly ComboBox TypeBox = Theme.Combo(84);
    readonly Label _aiState = Theme.Lbl("");
    readonly List<(string Key, RadioButton B)> _brushBtns = new(), _sideBtns = new();
    readonly ComboBox _surfRoad = Theme.Combo(110), _surfFeel = Theme.Combo(110);
    readonly Label _surfSides = new() { AutoSize = false, Width = 48, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
    readonly CheckBox _surfVariety = new() { Text = "Varied asphalt", AutoSize = true, ForeColor = Theme.Text, Font = Theme.Ui, Margin = new Padding(8, 4, 2, 1), TabStop = false };
    Spin _aiLaneBox = null!;
    bool _filling2;

    void MakeToolBars()
    {
        var tb = Bar();
        var g = Group(tb, "ROAD"); foreach (var k in new[] { "select", "move", "add", "insert", "erase" }) g.Controls.Add(ToolBtn(k));
        g = Group(tb, "BRANCHES & LANES"); foreach (var k in new[] { "branch", "split" }) g.Controls.Add(ToolBtn(k));
        g = Group(tb, "MEASURE"); g.Controls.Add(ToolBtn("measure"));
        g = Group(tb, "POWER-UPS"); foreach (var k in new[] { "row", "single" }) g.Controls.Add(ToolBtn(k));
        g.Controls.Add(Theme.Lbl("type"));
        foreach (var k in MapView.Types) TypeBox.Items.Add(k);
        TypeBox.SelectedItem = "Nitro"; TypeBox.SelectionChangeCommitted += (_, _) => { Map2.SetType((string)TypeBox.SelectedItem!); RefreshProps(); };
        g.Controls.Add(TypeBox);
        tb.Controls.Add(_snapBox = Theme.Check("Snap (G)", false, v => Map2.Snap = v));
        Common(tb);
        _bars["2d"] = tb;
        foreach (var (mode, text) in new[] { ("3d", "3D VIEW"), ("collision", "COLLISION  -  green = drive here, red = solid, yellow = bridges and tilted road"), ("light", "LIGHTING of the whole location") })
        {
            var f = Bar();
            f.Controls.Add(new Label { Text = text, AutoSize = true, ForeColor = Theme.Head, Font = Theme.Ui, Margin = new Padding(4, 7, 8, 0) });
            Common(f);
            _bars[mode] = f;
        }
        var car = Bar();
        car.Controls.Add(new Label { Text = "Your own cars - fair: never better than the best car of their class", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Ui, Margin = new Padding(8, 6, 8, 6) });
        car.Controls.Add(Theme.Btn("❔ Help", Help));
        _bars["car"] = car;
        var ai = Bar();
        ai.Controls.Add(new Label { Text = "AI PATH", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#ffe27a"), Font = Theme.Bold, Margin = new Padding(4, 7, 8, 0) });
        ai.Controls.Add(Theme.Btn("⟳ Auto AI path", AiAuto)); ai.Controls.Add(Theme.Btn("🏁 Racing line", AiRacing));
        var pen = Theme.Toggle("✎ Pen (draw)", v => { Map2.AiPen = v; Say(v ? "Pen: drag along the road to draw where the AI should drive" : "Pen off: drag = move the map"); });
        pen.FlatAppearance.CheckedBackColor = ColorTranslator.FromHtml("#c08a1a"); ai.Controls.Add(pen);
        _aiPenBtn = pen;
        _aiLaneBox = new Spin("AI lane width (m):", Bmb.Core.Layout.AiLane, 2, 6, 40, "0"); _aiLaneBox.Set += AiLaneChanged; ai.Controls.Add(_aiLaneBox);
        ai.Controls.Add(ToolBtn("branch")); ai.Controls.Add(ToolBtn("split"));
        ai.Controls.Add(_aiState);
        Common(ai);
        _bars["ai"] = ai;
        // the Surface tab: brush on top, whole road below
        var sf = new Panel { Dock = DockStyle.Top, BackColor = ColorTranslator.FromHtml("#122036") };
        var low = Bar(); low.BackColor = sf.BackColor; var top = Bar(); top.BackColor = sf.BackColor;
        var note = new Label { Text = "LEFT click = add\nRIGHT click = delete", AutoSize = true, BackColor = ColorTranslator.FromHtml("#1c3a5c"), ForeColor = ColorTranslator.FromHtml("#7dffa8"), Font = Theme.Ui, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(4) };
        top.Controls.Add(note);
        var bt = Group(top, "BRUSH");
        var shortName = new Dictionary<string, string> { ["roughdirt"] = "Rough", ["sand"] = "Sand", ["water"] = "Water", ["deepwater"] = "Deep water", ["tarmac"] = "Asphalt" };
        foreach (var key in Surface.Order)
        {
            string k = key;
            var r = Theme.Radio(shortName.GetValueOrDefault(k, Surface.Surfaces[k].Label), Surface.Surfaces[k].Colour, () => { Map2.BrushKey = k; Say($"Brush: {Surface.Surfaces[k].Label} - left drag paints, right drag removes"); });
            r.Checked = k == "dirt"; _brushBtns.Add((k, r)); bt.Controls.Add(r);
        }
        var size = new Spin("size (m)", 6, 1, 1, 40, "0"); size.Set += v => Map2.BrushSize = v; bt.Controls.Add(size);
        var wr = Group(low, "WHOLE ROAD");
        wr.Controls.Add(Theme.Lbl("type"));
        foreach (var k in Surface.Surfaces.Keys.Where(k => k != "deepwater")) _surfRoad.Items.Add(Surface.Surfaces[k].Label);
        _surfRoad.SelectionChangeCommitted += (_, _) => SurfSet("road", Surface.Surfaces.First(kv => kv.Value.Label == (string)_surfRoad.SelectedItem!).Key);
        wr.Controls.Add(_surfRoad);
        wr.Controls.Add(Theme.Lbl("sides")); wr.Controls.Add(Theme.Btn("-", () => SurfSides(-1))); wr.Controls.Add(_surfSides); wr.Controls.Add(Theme.Btn("+", () => SurfSides(1)));
        foreach (var key in new[] { "dirt", "roughdirt", "gravel", "grass", "sand", "water", "deepwater" })
        {
            string k = key;
            var r = Theme.Radio(shortName.GetValueOrDefault(k, Surface.Surfaces[k].Label), Surface.Surfaces[k].Colour, () => SurfSet("side_type", k));
            _sideBtns.Add((k, r)); wr.Controls.Add(r);
        }
        _surfVariety.CheckedChanged += (_, _) => { if (!_filling2) SurfSet("variety", _surfVariety.Checked); };
        wr.Controls.Add(_surfVariety);
        wr.Controls.Add(Theme.Colored("Clear paint", "#7a2a3a", SurfClear));
        var gr = Group(low, "GRIP (this location)");
        foreach (var (_, n) in UiText.FeelNames) _surfFeel.Items.Add(n);
        _surfFeel.SelectionChangeCommitted += (_, _) => SurfFeel(UiText.FeelNames.First(q => q.Name == (string)_surfFeel.SelectedItem!).Key);
        gr.Controls.Add(_surfFeel);
        Common(top);
        sf.Controls.Add(low); sf.Controls.Add(top);
        _bars["surface"] = sf;
        foreach (var c in _bars.Values) { c.Visible = false; _toolRow.Controls.Add(c); }
        _toolRow.Resize += (_, _) => FitBar();
    }

    CheckBox _snapBox = null!, _aiPenBtn = null!;

    // the top bar on a narrow window: shorter names first, then two rows
    readonly Panel _topPanel = new() { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Panel };
    readonly FlowLayoutPanel _topRight = new() { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(0, 4, 6, 0) };
    readonly FlowLayoutPanel _topTabs = new() { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(4, 4, 0, 0) };
    Button _save = null!, _play = null!;
    Control _layoutBar = null!;

    Splitter _leftSash = null!, _rightSash = null!;

    // ---- a small arrow on a side panel folds it to a thin strip (kept with the layout)
    const int FoldW = 24;
    readonly Dictionary<string, (Control Panel, Button Arrow, bool Left)> _folds = new();
    bool Folded(string name) => _sizes.Truthy("fold_" + name);
    public bool IsFolded(string name) => Folded(name);

    public void Foldable(string name, Control panel, bool left)
    {
        var b = Theme.Btn("", () => ToggleFold(name));
        b.AutoSize = false; b.MinimumSize = new Size(0, 0); b.Padding = new Padding(0); b.Margin = new Padding(0); b.Size = new Size(20, 22); b.Font = Theme.Small;
        b.Location = new Point(panel.ClientSize.Width - b.Width - 2, 2); b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(b); b.BringToFront();
        _folds[name] = (panel, b, left);
    }

    public void ToggleFold(string name)
    {
        if (Folded(name)) _sizes.Remove("fold_" + name); else _sizes["fold_" + name] = true;
        FoldShow(); FitPanels(); SaveLayoutState(); G.Invalidate();
    }

    void FoldShow()
    {
        foreach (var (name, (panel, arrow, left)) in _folds)
        {
            bool f = Folded(name);
            foreach (Control c in panel.Controls) if (c != arrow) c.Visible = !f;
            ((RoundButton)arrow).Arrow = f == left ? 1 : -1;
            Look.Tip(arrow, f ? "Show this panel" : "Hide this panel");
            arrow.BringToFront();
        }
    }
    readonly ComboBox _langBox = Theme.Combo(92);
    bool _restart;

    void LanguageChanged()
    {
        string code = _langBox.SelectedIndex == 1 ? "ru" : "en";
        if (code == Lang.Code) return;
        Lang.Save(code);
        if (_test) return;
        if (MessageBox.Show(this, code == "ru" ? "Для смены языка программа перезапустится. Перезапустить сейчас?" : "The program restarts to change the language. Restart now?", "Blur Map Builder", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _restart = true;
        Close();
    }

    void Restart()
    {
        var p = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var a in Environment.GetCommandLineArgs().Skip(1)) p.ArgumentList.Add(a);
        Process.Start(p);
    }

    void FitTop()
    {
        int W = ClientSize.Width;
        if (W < 50 || _save == null) return;
        bool shorter = W < (Lang.Ru ? 1760 : 1420);
        _save.Text = shorter ? "✔ Save" : "✔ Save (Ctrl+S)"; _play.Text = shorter ? "▶ Play" : "▶ Play (F5)";
        if (!Building) _build.Text = shorter ? "▶ Build + install" : "▶ Build + install (Ctrl+B)";
        _update.Visible = W >= (Lang.Ru ? 1400 : 1180);
        int tabs = _topTabs.Controls.Cast<Control>().Sum(c => c.PreferredSize.Width + c.Margin.Horizontal) + 10, right = _topRight.PreferredSize.Width;
        bool two = tabs + right + 8 > W;
        _topPanel.Height = two ? 76 : 40;
        _topRight.Dock = two ? DockStyle.Bottom : DockStyle.Right; _topRight.Height = 36;
        _layoutBar.Visible = W >= 1120;
    }

    void Common(FlowLayoutPanel f)
    {
        f.Controls.Add(Look.Tip(Theme.Btn("❔ Help", Help), "Short help for this tab (F1)")); f.Controls.Add(Look.Tip(Theme.Btn("⛶ Fit", FitNow), "Show the whole map")); f.Controls.Add(Look.Tip(Theme.Btn("↶ Undo", Undo), "Ctrl+Z")); f.Controls.Add(Look.Tip(Theme.Btn("↷ Redo", Redo), "Ctrl+Y"));
    }

    void FitNow() { if (Is3D) V3.Reset(); else Map2.Fit(); G.Invalidate(); }

    bool _settingMode;
    public void SetMode(string m)
    {
        if (_settingMode) return;
        _settingMode = true;
        Mode = m;
        foreach (var (k, t) in _tabs) t.Checked = k == m;
        _settingMode = false;
        LoadBegin(m);
        try { ShowTab(m); }
        finally { LoadEnd(m); }
        switch (m)
        {
            case "light": Say($"LIGHTING of the location '{D.Loc.Str("name")}': pick a game city as the start, change what you like, Build + install, look in game"); break;
            case "car": Say("CUSTOM CAR: + New, pick a class and a body car, then spend the class's stat points"); break;
            case "surface": Say($"SURFACE: left drag = paint {Surface.Surfaces[Map2.BrushKey].Label}, right drag = remove, middle drag = move the map, wheel = zoom.  Whole road: type / sides / varied asphalt on the bar"); break;
            case "ai": Say("AI PATH: the yellow line is where the AI cars drive (the band = their lane). Auto = made for you; Pen = draw it yourself (drag on the road; drawing over a part replaces that part)"); break;
            case "3d": case "collision": Say($"{(m == "collision" ? "COLLISION view" : "3D view")}: middle-drag turn, Shift+middle move, wheel zoom, numpad 7/1/3 views, 5 flat, Home all  (F3 = next view)"); break;
            default: ToolHint(); break;
        }
    }

    // ---- the tab change: the window stays as it is, with a small bar, and the new tab appears whole
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr w, int msg, IntPtr a, IntPtr b);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr w, IntPtr r, IntPtr rgn, uint flags);
    readonly LoadBar _load = new();
    readonly Dictionary<string, long> _tabMs = new();
    readonly Stopwatch _tabTime = new();
    string _loadName = "";
    public int LoadShown; public Action<int>? LoadStepped;
    const int ShowAfterMs = 120;

    void LoadBegin(string m)
    {
        _tabTime.Restart();
        _loadName = m switch { "2d" => "2D map", "3d" => "3D view", "collision" => "Collision", "ai" => "AI path", "light" => "Lighting", "surface" => "Surface", _ => "Custom car" };
        if (!IsHandleCreated) return;
        SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);                 // WM_SETREDRAW off
        if (_tabMs.GetValueOrDefault(m, long.MaxValue) > ShowAfterMs) LoadShow(5);
    }

    void LoadShow(int pct)
    {
        if (!_load.Up)
        {
            LoadShown++;
            var c = PointToScreen(new Point(ClientSize.Width / 2 - _load.Width / 2, ClientSize.Height / 2 - _load.Height / 2));
            _load.Put(c);
        }
        _load.Set($"Loading {_loadName}...", pct);
        LoadStepped?.Invoke(pct);
    }

    /// shown as soon as the tab turns out to be slow
    void LoadStep(int pct) { if (_load.Up || _tabTime.ElapsedMilliseconds > ShowAfterMs) { if (IsHandleCreated) LoadShow(pct); } }

    void LoadEnd(string m)
    {
        if (IsHandleCreated)
        {
            LoadStep(100);
            SendMessage(Handle, 0x000B, (IntPtr)1, IntPtr.Zero);               // WM_SETREDRAW on
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);    // everything, now
            _load.Away();
        }
        _tabMs[m] = _tabTime.ElapsedMilliseconds;
    }

    void ShowTab(string m)
    {
        SuspendLayout();
        foreach (var (k, c) in _bars) c.Visible = k == m;
        Map2.Mode = m;
        LoadStep(20);
        V3.SetMode(m);
        LoadStep(60);
        bool canvas = m is not ("light" or "car");
        _rightP.Visible = _rightSash.Visible = canvas; _leftP.Visible = _leftSash.Visible = m != "car";
        G.Visible = canvas; _lightPanel.Visible = m == "light"; _car.Visible = m == "car";
        if (canvas) { G.BringToFront(); ActiveControl = G; G.Focus(); }
        else if (m == "light") { _lightPanel.BringToFront(); LightBuild(); }
        else { _car.BringToFront(); _car.Refresh2(); }
        ResumeLayout(true);
        LoadStep(80);
        FitBar();
        FitPanels();
        if (m == "surface") SurfRefresh();
        else if (m == "ai") AiRefresh();
        LoadStep(90);
        _propsKey = "";
        RefreshProps();
        G.Invalidate();
    }

    bool _settingTool;
    public void SetTool(string k)
    {
        _settingTool = true;
        Map2.Tool = k;
        foreach (var (key, l) in _tools) foreach (var t in l) t.Checked = key == k;
        _settingTool = false;
        Map2.ToolChanged();
        ToolHint();
        G.Invalidate();
    }

    void ToolHint()
    {
        var hints = new Dictionary<string, string>
        {
            ["select"] = "click a point or power-up to edit it on the right", ["move"] = "drag points and power-ups", ["add"] = "click to add a point at the end of the road",
            ["insert"] = "click near the road to insert a point there (near a branch: into the branch)", ["row"] = "click on the road (or a branch) to place a row of power-ups",
            ["single"] = $"click to place one {Map2.PType} power-up (1-8 = type)", ["erase"] = "click a point or power-up to delete it",
            ["branch"] = "click ON the road where the branch leaves, then its points beside the road, then ON the road where it joins again", ["split"] = "click ON the road where the lane split starts, then where it ends",
            ["measure"] = "click A, then B: the straight distance and, on the same road, the driving distance",
        };
        Say($"{MapView.ToolInfo[Map2.Tool].Label}: {hints[Map2.Tool]}");
    }

    public void Say(string s, Color? c = null) { _status.Text = s; _status.ForeColor = c ?? Theme.Text; }

    // ---- world tree ----------------------------------------------------------------------------------------------
    readonly List<(int L, int M)> _treeRows = new();
    bool _filling;
    public void Tree()
    {
        _filling = true;
        _tree.BeginUpdate();
        _tree.Items.Clear(); _treeRows.Clear();
        int li = 0;
        foreach (var l in D.World.Objs("locations"))
        {
            _tree.Items.Add("▣ " + l.Str("name")); _treeRows.Add((li, -1));
            int mi = 0;
            foreach (var m in l.Objs("maps")) { _tree.Items.Add("     ▸ " + m.Str("name")); _treeRows.Add((li, mi)); mi++; }
            li++;
        }
        _tree.EndUpdate();
        _tree.SelectedIndex = _treeRows.IndexOf((D.Li, D.Mi));
        _filling = false;
    }

    void TreePick()
    {
        if (_filling || _tree.SelectedIndex < 0) return;
        Map2.NetClearSel();
        var (l, m) = _treeRows[_tree.SelectedIndex];
        if (m < 0) m = 0;
        if ((l, m) == (D.Li, D.Mi)) { Tree(); return; }
        D.Li = l; D.Mi = m;
        Map2.ResetSelection();
        D.Touched(); Tree(); Map2.Fit(); V3.Dirty(); Refresh2();
        if (Mode == "light") LightBuild();
        if (Mode == "surface") SurfRefresh();
        if (Mode == "ai") AiRefresh();
    }

    string? Ask(string title, string prompt, string value = "")
    {
        if (TestAnswers.Count > 0) return TestAnswers.Dequeue();
        using var f = new Form { Text = title, Width = 380, Height = 170, BackColor = Theme.Panel, ForeColor = Theme.Text, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        Look.Dress(f, "ask");
        var l = new Label { Text = prompt, Left = 12, Top = 8, Width = 318, Height = 36 };
        var t = new TextBox { Text = value, Left = 12, Top = 46, Width = 340, BackColor = Theme.Field, ForeColor = Theme.Text };
        var ok = Theme.Btn("OK", () => { }, Theme.Blue); ok.DialogResult = DialogResult.OK; ok.AutoSize = false; ok.Left = 12; ok.Top = 78; ok.Width = 80;
        f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.AcceptButton = ok;
        return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
    }
    public readonly Queue<string> TestAnswers = new();

    void AfterWorldEdit() { D.Touched(); Tree(); Map2.Fit(); V3.Dirty(); Changed(); }
    bool TooMany() { if (D.TotalMaps < UiText.MaxMaps) return false; Msg("Too many maps", $"At most {UiText.MaxMaps} maps in total."); return true; }
    public DialogResult Msg(string title, string text, MessageBoxButtons b = MessageBoxButtons.OK) => _test ? DialogResult.Yes : MessageBox.Show(this, text, title, b);

    List<object?> FreeSpot(JObj loc)
    {
        var pts = loc.Objs("maps").SelectMany(m => m.Arr("points")!).Select(p => Road.NormPoint(p, 60)).ToList();
        if (pts.Count == 0) return new() { new JObj { ["x"] = 0.0, ["z"] = 0.0 }, new JObj { ["x"] = 1500.0, ["z"] = 0.0 } };
        double z = pts.Max(p => p.Z) + 400, x = pts.Min(p => p.X);
        return new() { new JObj { ["x"] = x, ["z"] = z }, new JObj { ["x"] = x + 1500, ["z"] = z } };
    }

    static JObj NewScenery() { var s = new JObj(); foreach (var (k, v) in Bmb.Core.Layout.NewMapScenery) s[k] = v; return s; }

    void NewMap()
    {
        if (TooMany()) return;
        string? n = Ask("New map", "Name of the new map:");
        if (string.IsNullOrEmpty(n)) return;
        D.Snapshot();
        var loc = D.Loc;
        loc.Arr("maps")!.Add(new JObj { ["id"] = World.Slug(n), ["name"] = n, ["width"] = 60.0, ["auto_bridges"] = true, ["obstacles"] = false, ["points"] = FreeSpot(loc), ["scenery"] = NewScenery() });
        D.Mi = loc.Arr("maps")!.Count - 1;
        Map2.ResetSelection();
        AfterWorldEdit();
    }

    void NewLocation()
    {
        if (TooMany()) return;
        string? n = Ask("New location", "Name of the new location (a new world):");
        if (string.IsNullOrEmpty(n)) return;
        var ids = D.World.Objs("locations").Select(l => l.Str("id")).ToHashSet();
        string lid = World.Slug(n);
        while (ids.Contains(lid) || lid == "flat_earth") lid += "_2";
        D.Snapshot();
        var locs = D.World.Arr("locations")!;
        locs.Add(new JObj { ["id"] = lid, ["name"] = n, ["maps"] = new List<object?> { new JObj { ["id"] = lid + "_1", ["name"] = n + " 1", ["width"] = 60.0, ["auto_bridges"] = true, ["obstacles"] = false, ["points"] = new List<object?> { new JObj { ["x"] = 0.0, ["z"] = 0.0 }, new JObj { ["x"] = 1500.0, ["z"] = 0.0 } }, ["scenery"] = NewScenery() } } });
        D.Li = locs.Count - 1; D.Mi = 0;
        Map2.ResetSelection();
        AfterWorldEdit();
    }

    void DupMap()
    {
        if (TooMany()) return;
        D.Snapshot();
        var c = Share.Copy(D.Map);
        c["name"] = c.Str("name") + " copy"; c["id"] = World.Slug(c.Str("name")!);
        foreach (var k in Share.GameIds) c.Remove(k);
        var pts = c.Arr("points")!;
        for (int k = 0; k < pts.Count; k++) if (pts[k] is not JObj) { var a = (List<object?>)pts[k]!; pts[k] = new JObj { ["x"] = Json.D(a[0]), ["z"] = Json.D(a[1]) }; }
        foreach (var p in pts.OfType<JObj>()) p["z"] = p.Num("z") + 300;
        foreach (var it in c.Objs("pickups")) it["z"] = it.Num("z") + 300;
        D.Loc.Arr("maps")!.Add(c); D.Mi = D.Loc.Arr("maps")!.Count - 1;
        AfterWorldEdit();
    }

    void Rename()
    {
        string? what = Ask("Rename", $"1 = this map ({D.Map.Str("name")})\n2 = this location ({D.Loc.Str("name")})", "1");
        if (what is not ("1" or "2")) return;
        var t = what == "1" ? D.Map : D.Loc;
        string? n = Ask("Rename", "New name:", t.Str("name")!);
        if (string.IsNullOrEmpty(n)) return;
        D.Snapshot(); t["name"] = n; Tree(); Changed();
    }

    void DeleteMap()
    {
        if (D.Loc.Arr("maps")!.Count <= 1) { Msg("Delete map", "A location needs at least one map (delete the location instead)."); return; }
        if (D.Li == 0 && D.Mi == 0) { Msg("Delete map", "The first map of the first location can't be deleted (it holds the original Flat Earth ids) - change its road instead."); return; }
        if (Msg("Delete map", $"Delete the map '{D.Map.Str("name")}'?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        D.Snapshot(); D.Loc.Arr("maps")!.RemoveAt(D.Mi); D.Mi = 0;
        AfterWorldEdit();
        Say("Map deleted here - press 'Build + install' to remove it from the game too", Theme.Warn);
    }

    void DeleteLocation()
    {
        var locs = D.World.Arr("locations")!;
        if (locs.Count <= 1) { Msg("Delete location", "This is the only location - make another one first."); return; }
        string extra = D.Li == 0 ? $"\n\nIt is the first location: '{((JObj)locs[1]!).Str("name")}' becomes the first one and takes over its place in the game." : "";
        if (Msg("Delete location", $"Delete the location '{D.Loc.Str("name")}' and its {D.Loc.Arr("maps")!.Count} map(s)?{extra}", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        D.Snapshot(); locs.RemoveAt(D.Li); D.Li = D.Mi = 0;
        AfterWorldEdit();
        Say("Location deleted here - press 'Build + install' to remove it from the game too", Theme.Warn);
    }

    public string? TestFile;
    string? OpenFile(string title, string filter, string? dir = null)
    {
        if (_test) { var f = TestFile; TestFile = null; return f; }
        using var d = new OpenFileDialog { Title = title, Filter = filter };
        if (dir != null) { Directory.CreateDirectory(dir); d.InitialDirectory = dir; }
        return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
    }
    string? SaveFile(string title, string filter, string name, string? dir = null)
    {
        if (_test) { var f = TestFile; TestFile = null; return f; }
        using var d = new SaveFileDialog { Title = title, Filter = filter, FileName = name };
        if (dir != null) { Directory.CreateDirectory(dir); d.InitialDirectory = dir; }
        return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
    }
    public string? PickFile(string title, string filter) => OpenFile(title, filter);

    void ExportMap()
    {
        var m = D.Map;
        string? path = SaveFile("Export the map for another player", "Blur map|*.blurmap", World.Slug(m.Str("name")!) + Share.Ext);
        if (path == null) return;
        try
        {
            var lt = D.Loc.Obj("lighting");
            int n = Share.Export(m, D.Loc.Str("name")!, path, new JObj { ["lighting"] = lt != null ? Share.Copy(lt) : null, ["surface_feel"] = Surface.LocFeel(D.World, D.Loc) });
            string pics = n > 0 ? $", {n} picture(s)" : "";
            Say($"Exported '{m.Str("name")}' with its lighting + grip -> {path} ({new FileInfo(path).Length / 1024.0:0.0} KB{pics}). Send this file; the other player uses Import map.", Theme.Good);
        }
        catch (IOException e) { Msg("Export", "Could not write the file:\n" + e.Message); }
    }

    void ImportMap()
    {
        string? path = OpenFile("Import a map from another player", "Blur map|*.blurmap|All files|*.*");
        if (path == null) return;
        if (D.TotalMaps >= UiText.MaxMaps) { Msg("Too many maps", $"At most {UiText.MaxMaps} maps in total - delete one first."); return; }
        BlurMapFile f;
        try { f = Share.Read(path); }
        catch (Exception e) when (e is ImportProblem or IOException or FormatException or InvalidCastException) { Msg("Import", "Could not import this file:\n" + e.Message); return; }
        var m = f.Map; var st = f.Info.Obj("settings") ?? new JObj();
        string? feel = UiText.FeelNames.Any(q => q.Key == st.Str("surface_feel")) ? st.Str("surface_feel") : null;
        var a = _test ? DialogResult.Yes : MessageBox.Show(this, $"Import '{m.Str("name") ?? "map"}' as its OWN location, with the maker's lighting and grip?\n\nYes = exactly as it was made (recommended)\nNo = into '{D.Loc.Str("name")}' (it then uses that location's lighting and grip)", "Import map", MessageBoxButtons.YesNoCancel);
        if (a == DialogResult.Cancel) return;
        foreach (var k in Share.GameIds) m.Remove(k);
        var names = D.World.Objs("locations").SelectMany(l => l.Objs("maps")).Select(q => q.Str("name")).ToHashSet();
        string bas = m.Str("name") ?? f.Info.Str("name") ?? "imported map"; bas = bas[..Math.Min(40, bas.Length)];
        string name = bas; int n = 2;
        while (names.Contains(name)) name = $"{bas} ({n++})";
        m["name"] = name;
        var ids = D.Loc.Objs("maps").Select(q => q.Str("id")).ToHashSet();
        string mid = World.Slug(name); n = 2;
        while (ids.Contains(mid)) mid = $"{World.Slug(name)}_{n++}";
        m["id"] = mid;
        string folder = Path.Combine(Paths.Tracks, "imported", mid);
        var saved = new Dictionary<string, string>();
        foreach (var (zn, raw) in f.Pics)
        {
            Directory.CreateDirectory(folder);
            string dst = Path.Combine(folder, Path.GetFileNameWithoutExtension(zn) + Path.GetExtension(zn).ToLowerInvariant());
            File.WriteAllBytes(dst, raw); saved[zn] = dst;
        }
        if (m.Obj("minimap") is { } mm && mm.Truthy("image")) { mm["image"] = saved.GetValueOrDefault(mm.Str("image")!, ""); if (!mm.Truthy("image")) mm["auto"] = true; }
        if (m.Truthy("picture")) { if (saved.TryGetValue(m.Str("picture")!, out var s)) m["picture"] = s; else m.Remove("picture"); }
        D.Snapshot();
        var locs = D.World.Arr("locations")!;
        if (a == DialogResult.Yes)
        {
            var lnames = D.World.Objs("locations").Select(l => l.Str("name")).ToHashSet();
            string lbase = f.Info.Str("location") ?? name; lbase = lbase[..Math.Min(40, lbase.Length)];
            string lname = lbase; n = 2;
            while (lnames.Contains(lname)) lname = $"{lbase} ({n++})";
            var lids = D.World.Objs("locations").Select(l => l.Str("id")).ToHashSet();
            string lid = World.Slug(lname); n = 2;
            while (lids.Contains(lid) || lid == "flat_earth") lid = $"{World.Slug(lname)}_{n++}";
            var nl = new JObj { ["id"] = lid, ["name"] = lname, ["maps"] = new List<object?>() };
            if (st.Obj("lighting") is { } lt) nl["lighting"] = Share.Copy(lt);
            if (feel != null) nl["surface_feel"] = feel;
            locs.Add(nl); D.Li = locs.Count - 1;
        }
        D.Loc.Arr("maps")!.Add(m); D.Mi = D.Loc.Arr("maps")!.Count - 1;
        AfterWorldEdit();
        string who = f.Info.Str("made") != null ? $" (from {f.Info.Str("location")}, {f.Info.Str("made")})" : "";
        Say($"Imported '{name}' into {D.Loc.Str("name")}{who} - press Build + install to race it", Theme.Good);
    }

    // ---- keys --------------------------------------------------------------------------------------------------------
    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        bool typing = ActiveControl is TextBox || ActiveControl is ComboBox || ActiveControl is RichTextBox || (ActiveControl as ContainerControl)?.ActiveControl is TextBox || InTextBox();
        var key = k & Keys.KeyCode; bool ctrl = (k & Keys.Control) != 0, shift = (k & Keys.Shift) != 0, alt = (k & Keys.Alt) != 0;
        if (ctrl)
        {
            switch (key)
            {
                case Keys.Z: if (shift) Redo(); else Undo(); return true;
                case Keys.Y: Redo(); return true;
                case Keys.S: DoSave(); return true;
                case Keys.B: Build(); return true;
                case Keys.N: NewMap(); return true;
                case Keys.L: NewLocation(); return true;
                case Keys.D: if (Mode == "3d") V3.Duplicate(); else DupMap(); return true;
                case Keys.G: if (Mode == "3d") { if (shift) V3.UngroupSelection(); else V3.GroupSelection(); } return true;
            }
            if (Is3D && V3.Key(key, ctrl, shift, alt)) return true;
            return base.ProcessCmdKey(ref msg, k);
        }
        switch (key)
        {
            case Keys.F1: Help(); return true;
            case Keys.F2: Rename(); return true;
            case Keys.F3: ToggleView(); return true;
            case Keys.F5: PlayDialog(); return true;
        }
        if (key == Keys.Escape && !Is3D) { Map2.Escape(); return true; }
        if (typing) return base.ProcessCmdKey(ref msg, k);
        if (Is3D) return V3.Key(key, ctrl, shift, alt) || base.ProcessCmdKey(ref msg, k);
        if (Mode is "light" or "car") return base.ProcessCmdKey(ref msg, k);
        if (alt) return base.ProcessCmdKey(ref msg, k);
        var tool = key switch { Keys.S => "select", Keys.M => "move", Keys.A => "add", Keys.I => "insert", Keys.E => "erase", Keys.R => "row", Keys.P => "single", Keys.B => "branch", Keys.L => "split", Keys.D => "measure", _ => null };
        if (tool != null) { SetTool(tool); return true; }
        if (key >= Keys.D1 && key <= Keys.D8) { Map2.SetType(MapView.Types[key - Keys.D1]); TypeBox.SelectedItem = Map2.PType; RefreshProps(); return true; }
        double step = shift ? 1 : 5;
        switch (key)
        {
            case Keys.Delete: case Keys.Back: Map2.DeleteSelected(); return true;
            case Keys.Enter: Map2.MeasureFinish(); return true;
            case Keys.W: Map2.Adj("w", 5); return true;
            case Keys.Q: Map2.Adj("w", -5); return true;
            case Keys.PageUp: Map2.Adj("h", 1); return true;
            case Keys.PageDown: Map2.Adj("h", -1); return true;
            case Keys.Oemcomma: Map2.Adj("tilt", -1); return true;
            case Keys.OemPeriod: Map2.Adj("tilt", 1); return true;
            case Keys.C: Map2.ToggleCorner(); return true;
            case Keys.T: Map2.Turn90(1); return true;
            case Keys.Y: Map2.Turn90(-1); return true;
            case Keys.V: Map2.Reverse(); return true;
            case Keys.X: Map2.Mirror(); return true;
            case Keys.F: Map2.Fit(); G.Invalidate(); return true;
            case Keys.H: Help(); return true;
            case Keys.G: _snapBox.Checked = !_snapBox.Checked; Say("Snap " + (Map2.Snap ? "on" : "off")); return true;
            case Keys.Left: Map2.Nudge(-step, 0); return true;
            case Keys.Right: Map2.Nudge(step, 0); return true;
            case Keys.Up: Map2.Nudge(0, step); return true;
            case Keys.Down: Map2.Nudge(0, -step); return true;
        }
        return base.ProcessCmdKey(ref msg, k);
    }

    bool InTextBox()
    {
        Control? c = ActiveControl;
        while (c is ContainerControl cc && cc.ActiveControl != null) c = cc.ActiveControl;
        return c is TextBox or ComboBox or RichTextBox;
    }

    void ToggleView()
    {
        var order = new[] { "2d", "3d", "collision", "ai", "light", "surface", "car" };
        SetMode(order[(Array.IndexOf(order, Mode) + 1) % order.Length]);
    }

    // ---- layouts ----------------------------------------------------------------------------------------------------
    string _layoutFile = "", _current = "Default";
    JObj _sizes = new(), _saved = new();
    List<object?>? _window; bool _zoomed;

    void LoadLayout()
    {
        _layoutFile = Path.Combine(Paths.Tracks, "ui_layouts_csharp.json");
        try
        {
            var d = (JObj)Json.Load(_layoutFile)!;
            _sizes = d.Obj("sizes") ?? new(); _saved = d.Obj("saved") ?? new(); _current = d.Str("current") ?? "Default"; _window = d.Arr("window"); _zoomed = d.Truthy("zoomed");
        }
        catch (Exception) { }
        if (_current != "Default" && !_saved.ContainsKey(_current)) _current = "Default";
        FillLayouts();
    }

    void FillLayouts()
    {
        _layoutBox.Items.Clear(); _layoutBox.Items.Add("Default");
        foreach (var k in _saved.Keys.OrderBy(s => s, StringComparer.Ordinal)) _layoutBox.Items.Add(k);
        _layoutBox.SelectedItem = _current;
    }

    int Sz(string name, int def) => _sizes.ContainsKey(name) ? (int)_sizes.Int(name) : def;

    void ApplySizes(bool window)
    {
        if (window && _window is { Count: 2 } && !_test)
        {
            if (_zoomed) WindowState = FormWindowState.Maximized;
            else { WindowState = FormWindowState.Normal; Size = new Size((int)Json.L(_window[0]), (int)Json.L(_window[1])); }
        }
        foreach (var (n, c, _, d, h) in _panels) { if (h) c.Height = Sz(n, d); else c.Width = Sz(n, d); }
        FoldShow();
        FitPanels();
        G.Invalidate();
    }

    /// the sizes as far as this window holds them (ui_layout.Layouts.fit)
    void FitPanels()
    {
        int W = ClientSize.Width, H = ClientSize.Height;
        if (W < 50) return;
        // the 3D tab on a narrow window: the object list goes first, the middle stays usable
        V3.Narrow(W < 1240);
        bool canvas = Mode is not ("light" or "car");
        _rightP.Visible = _rightSash.Visible = canvas && !(Mode == "3d" && W < 1240);
        var shown = _panels.Where(p => !p.Height && p.C.Visible && (p.C.Parent?.Visible ?? false)).ToList();
        int total = shown.Where(p => !Folded(p.Name)).Sum(p => Sz(p.Name, p.Default));
        double k = total > 0 ? Math.Min(1.0, Math.Max(0, W - 420.0) / total) : 1.0;
        foreach (var p in _panels)
            if (p.Height) p.C.Height = Math.Min(Sz(p.Name, p.Default), Math.Max(110, (int)(_body.Height * 0.4)));
            else if (Folded(p.Name)) { p.C.Width = FoldW; p.S.Visible = false; }
            else p.C.Width = shown.Contains(p) ? (int)Math.Max(p.Name == "right" ? 276 : 150, Sz(p.Name, p.Default) * k) : Sz(p.Name, p.Default);
    }

    JObj State() => new() { ["sizes"] = Share.Copy(_sizes), ["window"] = new List<object?> { (long)Width, (long)Height }, ["zoomed"] = WindowState == FormWindowState.Maximized };

    void Apply(JObj? st)
    {
        _sizes = st?.Obj("sizes") != null ? Share.Copy(st.Obj("sizes")!) : new JObj();
        _window = st?.Arr("window") ?? new List<object?> { 1500L, 880L }; _zoomed = st != null && st.Truthy("zoomed");
        ApplySizes(true);
        SaveLayoutState();
    }

    void LayoutLoad()
    {
        string n = _layoutBox.SelectedItem as string ?? "Default";
        _current = _saved.ContainsKey(n) ? n : "Default";
        Apply(_saved.Obj(_current));
        Say($"Layout '{_current}' loaded");
    }

    void LayoutSave()
    {
        string now = _layoutBox.SelectedItem as string ?? "Default";
        string? n = Ask("Save layout", "Name of this layout:", now == "Default" ? "" : now)?.Trim();
        if (string.IsNullOrEmpty(n)) return;
        if (n == "Default") { Say($"'{n}' is the program's own layout: pick another name", Theme.Bad); return; }
        _saved[n] = State(); _current = n;
        FillLayouts(); SaveLayoutState();
        Say($"Layout '{n}' saved: panel sizes + window size");
    }

    void LayoutDelete()
    {
        if (_layoutBox.SelectedItem is not string n || n == "Default") { Say("The Default layout can't be deleted", Theme.Bad); return; }
        if (Msg("Delete layout", $"Delete the layout '{n}'?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _saved.Remove(n);
        if (_current == n) _current = "Default";
        FillLayouts(); SaveLayoutState();
        Say($"Layout '{n}' deleted");
    }

    void LayoutReset()
    {
        Apply(_saved.Obj(_current));
        Say($"Layout '{_current}' reset: back to how it was saved{(_current == "Default" ? " (the program's own sizes)" : "")}");
    }

    void SaveLayoutState()
    {
        if (_layoutFile == "") return;
        var s = State();
        try { Json.Save(new JObj { ["current"] = _current, ["sizes"] = _sizes, ["window"] = s["window"], ["zoomed"] = s["zoomed"], ["saved"] = _saved }, _layoutFile); } catch (IOException) { }
    }

    void Closing2(object? s, FormClosingEventArgs e)
    {
        SaveLayoutState();
        if (_test || !D.Dirty) return;
        var a = MessageBox.Show(this, "Save your changes to the maps?", "Blur Map Builder", MessageBoxButtons.YesNoCancel);
        if (a == DialogResult.Cancel) e.Cancel = true;
        else if (a == DialogResult.Yes) DoSave();
        if (e.Cancel) _restart = false;
    }
}

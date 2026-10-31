// The Custom car tab. Follows car/car_tab.py: the list, name, class, body, icons, paint, wheels, imported body + fit, stats with the class chart, the car in 3D.
using Bmb.Core;

namespace Bmb.App;

public sealed class CarPanel : Panel
{
    readonly MainForm _f;
    public int Sel;
    bool _loading;
    readonly ListBox _list = new() { Dock = DockStyle.Fill, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, IntegralHeight = false, Font = new Font("Segoe UI", 10f) };
    readonly TextBox _name = new() { Width = 200, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11f), MaxLength = 24 };
    readonly RadioButton[] _cls = new RadioButton[4];
    readonly ComboBox _base = Theme.Combo(240), _icon = Theme.Combo(220), _mini = Theme.Combo(220), _wheels = Theme.Combo(240);
    readonly PictureBox _miniPic = new() { Width = 150, Height = 48, BackColor = Theme.Bg };
    readonly Panel[] _paint = { new() { Width = 34, Height = 22, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand }, new() { Width = 34, Height = 22, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand } };
    readonly Button[] _paintB = new Button[2];
    readonly Button _bodyBtn;
    readonly FlowLayoutPanel _fitRow = new() { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Visible = false };
    readonly Label _fitLabel = new() { AutoSize = true, ForeColor = Theme.Text, Visible = false }, _roomLabel = new() { AutoSize = true, ForeColor = Theme.Text, Visible = false, MaximumSize = new Size(900, 0) };
    readonly Label _points = new() { AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI", 11f, FontStyle.Bold) }, _top = new() { AutoSize = true, ForeColor = Theme.Text };
    readonly Dictionary<string, Label> _vals = new();
    readonly Panel _chart = new() { Width = 520, Height = 46 * 5, BackColor = Theme.Bg, Cursor = Cursors.Hand };
    readonly PictureBox _view = new() { Dock = DockStyle.Fill, BackColor = ColorTranslator.FromHtml("#060a14"), SizeMode = PictureBoxSizeMode.CenterImage, Cursor = Cursors.SizeWE };
    readonly Label _hint = new() { Dock = DockStyle.Bottom, Height = 18, Text = "drag = turn the car", ForeColor = ColorTranslator.FromHtml("#7f8a96"), BackColor = Theme.Bg };
    readonly Panel _left = new() { Dock = DockStyle.Left, Width = 240, BackColor = Theme.Panel }, _viewBox = new() { Dock = DockStyle.Bottom, Height = 300, BackColor = Theme.Bg };
    readonly FlowLayoutPanel _controls = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(10) };
    readonly List<Control> _needCar = new();
    double _yaw = -0.7; int _dragX; bool _dragging;
    CarUi.Parts? _parts; string _partsKey = "";
    int _job;
    const int RowH = 46;

    public CarPanel(MainForm f)
    {
        _f = f; BackColor = Theme.Bg;
        Look.DarkScroll(_controls, _list);
        _bodyBtn = Theme.Btn("Use the game body", () => Set("body_glb", null));
        // left: the list
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Panel, Padding = new Padding(8) };
        head.Controls.Add(new Label { Text = "MY CARS", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold });
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Panel };
        row.Controls.Add(Theme.Btn("+ New", Add)); row.Controls.Add(Theme.Btn("Copy", Copy)); row.Controls.Add(Theme.Btn("Delete", Delete));
        head.Controls.Add(row);
        _left.Controls.Add(_list); _left.Controls.Add(head); _list.BringToFront();
        _list.SelectedIndexChanged += (_, _) => { if (!_loading && _list.SelectedIndex >= 0) { Sel = _list.SelectedIndex; Load2(); } };
        // the controls
        var top = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, MaximumSize = new Size(1100, 0) };
        _controls.Resize += (_, _) => top.MaximumSize = new Size(Math.Max(300, Math.Min(1100, _controls.ClientSize.Width - 30)), 0);
        Control Col(string title, Control c, int w = 0)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 0, 14, 4) };
            p.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8") });
            p.Controls.Add(c);
            return p;
        }
        _name.TextChanged += (_, _) => { var v = _name.Text.Trim(); Set("name", v.Length > 0 ? v[..Math.Min(24, v.Length)] : "My car"); };
        top.Controls.Add(Col("Name", _name));
        var cls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
        for (int i = 0; i < 4; i++) { int k = i; _cls[i] = Theme.Radio($" {CarUi.Classes[i]} ", null, () => ClassChanged(k)); _cls[i].Font = new Font("Segoe UI", 10f, FontStyle.Bold); cls.Controls.Add(_cls[i]); }
        top.Controls.Add(Col("Class", cls));
        _base.SelectionChangeCommitted += (_, _) => BaseChanged();
        top.Controls.Add(Col("Body (a game car of this class)", _base));
        _icon.SelectionChangeCommitted += (_, _) => Set("icon", _icon.SelectedItem as string);
        top.Controls.Add(Col("Menu icon", _icon));
        _mini.SelectionChangeCommitted += (_, _) => Set("mini_icon", (string)_mini.SelectedItem! == MiniAuto ? CarIcon.Auto : _mini.SelectedItem as string);
        top.Controls.Add(Col("Mini icon (car list)", _mini));
        top.Controls.Add(_miniPic);
        _controls.Controls.Add(top); _wide.Add(top);
        var paint = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0, 10, 0, 0), MaximumSize = new Size(1100, 0) };
        paint.Controls.Add(Theme.Lbl("Paint"));
        for (int i = 0; i < 2; i++)
        {
            int k = i;
            _paint[i].Margin = new Padding(8, 4, 2, 0); _paint[i].Click += (_, _) => Paint(k); paint.Controls.Add(_paint[i]);
            _paintB[i] = Theme.Btn($"colour {i + 1}", () => Paint(k)); paint.Controls.Add(_paintB[i]);
        }
        paint.Controls.Add(Theme.Lbl("Wheels"));
        _wheels.SelectionChangeCommitted += (_, _) => Set("wheels", _wheels.SelectedItem as string);
        _wheels.DrawMode = DrawMode.OwnerDrawFixed;
        _wheels.DrawItem += WheelItem;
        _wheels.DropDownClosed += (_, _) => WheelHoverHide();
        paint.Controls.Add(_wheels);
        paint.Controls.Add(Theme.Btn("⇩ Import body (.glb)", ImportBody));
        paint.Controls.Add(_bodyBtn);
        _controls.Controls.Add(paint); _wide.Add(paint); _wide.Add(_fitRow);
        _fitRow.Controls.Add(new Label { Text = "Fit the body on the wheels:", AutoSize = true, ForeColor = Theme.Head, Margin = new Padding(0, 6, 8, 0) });
        foreach (var (label, key, step) in new[] { ("Size", "size", 0.02), ("Forward", "fwd", 0.03), ("Up", "up", 0.02), ("Wheels side to side", "track", 0.02), ("front to back", "wb", 0.02) })
        {
            string k = key; double s = step;
            _fitRow.Controls.Add(Theme.Lbl(label)); _fitRow.Controls.Add(Theme.Btn("−", () => Fit(k, -s))); _fitRow.Controls.Add(Theme.Btn("+", () => Fit(k, s)));
        }
        _fitRow.Controls.Add(Theme.Btn("↻ Turn 90°", () => Fit("turn", 90))); _fitRow.Controls.Add(Theme.Btn("Reset", () => Fit(null, 0)));
        _controls.Controls.Add(_fitRow); _controls.Controls.Add(_fitLabel); _controls.Controls.Add(_roomLabel);
        _controls.Controls.Add(new Label { Text = "STATS  (each one stays inside the class's range; all together inside its point budget)", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold, Margin = new Padding(0, 14, 0, 2) });
        _controls.Controls.Add(_points);
        var body = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg };
        var rows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };
        foreach (var (key, label) in CarUi.StatRows)
        {
            string k = key;
            var r = new FlowLayoutPanel { Width = 200, Height = RowH, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
            r.Controls.Add(new Label { Text = label, AutoSize = false, Width = 90, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text });
            r.Controls.Add(Theme.Btn("−", () => Step(k, -1)));
            var v = new Label { Text = "", AutoSize = false, Width = 30, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Orange, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
            _vals[k] = v; r.Controls.Add(v);
            r.Controls.Add(Theme.Btn("+", () => Step(k, 1)));
            rows.Controls.Add(r);
        }
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(_chart, true);
        _chart.Paint += ChartPaint;
        _chart.MouseDown += (_, e) => ChartClick(e);
        _chart.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) ChartClick(e); };
        body.Controls.Add(rows); body.Controls.Add(_chart);
        _controls.Controls.Add(body);
        _controls.Controls.Add(new Label { Text = "grey ticks = the class's cars,  blue line = class average,  box = class range,  orange = yours  (click or drag on a bar)", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8") });
        _controls.Controls.Add(_top);
        _controls.Controls.Add(new Label { Text = "Build + install puts your cars in the game (car select: their class).  The game finds a car's 3D model by its body car.", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 14, 0, 0) });
        _viewBox.Controls.Add(_view); _viewBox.Controls.Add(_hint);
        _view.MouseDown += (_, e) => { _dragX = e.X; _dragging = true; };
        _view.MouseMove += (_, e) => { if (_dragging && e.Button == MouseButtons.Left) { _yaw += (e.X - _dragX) * 0.012; _dragX = e.X; Draw(false); } };
        _view.MouseUp += (_, _) => { _dragging = false; Draw(true); };
        _view.Resize += (_, _) => Draw(true);
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var vs = f.PanelSash(DockStyle.Bottom, "car_view", _viewBox, 300, true);
        right.Controls.Add(_controls); right.Controls.Add(vs); right.Controls.Add(_viewBox);
        _controls.BringToFront();
        var ls = f.PanelSash(DockStyle.Left, "car_list", _left, 240);
        Controls.Add(right); Controls.Add(ls); Controls.Add(_left);
        right.BringToFront();
        _controls.Resize += (_, _) => { foreach (var c in _wide) c.MaximumSize = new Size(Math.Max(_controls.ClientSize.Width - 30, 300), 0); };
        foreach (var c in new Control[] { _name, _base, _icon, _mini, _wheels, paint, _chart }) _needCar.Add(c);
        foreach (var r in _cls) _needCar.Add(r);
    }

    readonly List<Control> _wide = new();

    // ---- the Wheels menu: a picture of the wheel under the mouse
    sealed class WheelPop : QuietPop
    {
        public readonly PictureBox Pic = new() { Width = 168, Height = 168, BackColor = ColorTranslator.FromHtml("#0b1426") };
        public readonly Label Name2 = new() { AutoSize = false, Width = 168, Height = 20, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false };
        public WheelPop()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Theme.Panel; StartPosition = FormStartPosition.Manual; ClientSize = new Size(180, 202);
            Pic.Location = new Point(6, 6); Name2.Location = new Point(6, 176);
            Controls.Add(Pic); Controls.Add(Name2);
        }
    }
    WheelPop? _pop; string? _popCar; int _popJob;
    public string? WheelShown => _pop is { Up: true } && _pop.Pic.Image != null ? _popCar : null;

    void WheelItem(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Theme.Blue : Theme.Field)) e.Graphics.FillRectangle(bg, e.Bounds);
        string name = (string)_wheels.Items[e.Index]!;
        TextRenderer.DrawText(e.Graphics, name, _wheels.Font, e.Bounds, sel ? Color.White : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (sel && _wheels.DroppedDown) WheelHover(name, _wheels.PointToScreen(new Point(_wheels.Width + 8, _wheels.Height)));
    }

    public void WheelHover(string car, Point at)
    {
        if (_popCar == car && _pop is { Up: true }) return;
        _popCar = car;
        _pop ??= new WheelPop();
        _pop.Name2.Text = car; _pop.Pic.Image = null;
        _pop.Put(at);
        int job = ++_popJob;
        Task.Run(() =>
        {
            var bm = CarUi.WheelPicture(car);
            BeginInvoke(() => { if (job == _popJob && _pop != null && !_pop.IsDisposed) { _pop.Pic.Image = bm; if (bm == null) _pop.Name2.Text = "No picture"; } });
        });
    }

    /// the robot: the menu open, a picture shown, closed - three times over. True if it stayed open each time
    public async Task<bool> TestWheelMenu()
    {
        bool ok = true;
        foreach (var car in new[] { "RatRod_NA_Stock", "Ford_MustangGTR_Stock", "Audi_TT_Stock" })
        {
            _wheels.Focus();
            _wheels.DroppedDown = true;
            await Task.Delay(120); Application.DoEvents();
            WheelHover(car, _wheels.PointToScreen(new Point(_wheels.Width + 8, _wheels.Height)));
            await Task.Delay(300); Application.DoEvents();
            ok &= _wheels.DroppedDown;
            _wheels.DroppedDown = false;
            await Task.Delay(120); Application.DoEvents();
        }
        return ok;
    }

    public void WheelHoverHide() { _popJob++; _popCar = null; _pop?.Away(); }

    const string MiniAuto = "Auto (side view of this car)";

    List<object?> Cars() { if (_f.D.World.Arr("cars") == null) _f.D.World["cars"] = new List<object?>(); return _f.D.World.Arr("cars")!; }
    public JObj? Car() { var c = Cars(); return Sel >= 0 && Sel < c.Count ? c[Sel] as JObj : null; }

    void Edit(Action<JObj> fn)
    {
        var c = Car();
        if (_loading || c == null) return;
        _f.D.Snapshot(); fn(c); _f.D.Touched(); _f.Changed();
    }

    void Set(string key, object? value)
    {
        var c = Car();
        if (c == null || _loading || Equals(c[key], value)) return;
        Edit(q => q[key] = value);
        if (key is "wheels" or "body_glb" or "glb_fit" or "mini_icon") Load2();
        if (key == "name") { _loading = true; _list.Items[Sel] = Label(Car()!); _loading = false; }
    }

    static string Label(JObj c) => $"{c.Str("name")}   [{CarUi.Classes[(int)c.Int("class")]}]";

    public void Refresh2()
    {
        var cars = Cars();
        _loading = true;
        _list.Items.Clear();
        foreach (var c in cars.OfType<JObj>()) _list.Items.Add(Label(c));
        Sel = Math.Min(Sel, cars.Count - 1);
        _loading = false;
        foreach (var c in _needCar) c.Enabled = cars.Count > 0;
        if (cars.Count == 0)
        {
            _points.Text = "No custom car yet: press + New";
            _view.Image = null;
            _chart.Invalidate();
            return;
        }
        Sel = Math.Max(Sel, 0);
        _loading = true; _list.SelectedIndex = Sel; _loading = false;
        Load2();
    }

    void Add()
    {
        _f.D.Snapshot();
        var car = CarUi.NewCar();
        var names = Cars().OfType<JObj>().Select(c => c.Str("name")).ToHashSet();
        int n = 1;
        while (names.Contains(car.Str("name"))) { n++; car["name"] = $"My car {n}"; }
        Cars().Add(car);
        Sel = Cars().Count - 1;
        _f.D.Touched(); _f.Changed();
        Refresh2();
    }

    void Copy()
    {
        var c0 = Car();
        if (c0 == null) return;
        _f.D.Snapshot();
        var c = Share.Copy(c0);
        string nm = c.Str("name") + " copy"; c["name"] = nm[..Math.Min(24, nm.Length)];
        Cars().Add(c);
        Sel = Cars().Count - 1;
        _f.D.Touched(); _f.Changed();
        Refresh2();
    }

    void Delete()
    {
        if (Car() == null) return;
        _f.D.Snapshot();
        Cars().RemoveAt(Sel);
        _f.D.Touched(); _f.Changed();
        Refresh2();
    }

    void Load2()
    {
        var c = Car();
        if (c == null) return;
        int cls = (int)c.Int("class");
        var classCars = CarsData.ClassCars(cls);
        if (!classCars.Any(x => x.Name == c.Str("base"))) c["base"] = classCars[0].Name;
        if (c.Truthy("wheels") && !CarUi.Pickable().Any(x => x.Name == c.Str("wheels"))) c["wheels"] = null;
        if (c.Truthy("body_glb") && c.Str("base") != CarGlb.GlbBody[cls]) { c["base"] = CarGlb.GlbBody[cls]; CarUi.Fair(c); }
        _loading = true;
        if (_name.Text != c.Str("name") && !_name.Focused) _name.Text = c.Str("name");
        _cls[cls].Checked = true;
        _base.Items.Clear(); foreach (var x in classCars) _base.Items.Add(x.Name);
        _base.SelectedItem = c.Str("base");
        if (_icon.Items.Count == 0) foreach (var i in CarsData.Cars().Select(x => x.Icon).Distinct().OrderBy(s => s, StringComparer.Ordinal)) _icon.Items.Add(i);
        _icon.SelectedItem = c.Str("icon");
        if (_wheels.Items.Count == 0) foreach (var x in CarUi.Pickable()) _wheels.Items.Add(x.Name);
        _wheels.SelectedItem = c.Truthy("wheels") ? c.Str("wheels") : c.Str("base");
        bool glb = c.Truthy("body_glb");
        _bodyBtn.Enabled = glb; _base.Enabled = !glb;
        foreach (var b in _paintB) b.Enabled = !glb;
        _fitRow.Visible = _fitLabel.Visible = _roomLabel.Visible = glb;
        if (glb)
        {
            var q = c.Obj("glb_fit") ?? new JObj();
            _fitLabel.Text = $"size x{q.Num("size", 1.0):0.00}, forward {q.Num("fwd"):+0.00;-0.00;+0.00} m, up {q.Num("up"):+0.00;-0.00;+0.00} m, turned {q.Int("turn")}°, wheels {q.Num("track"):+0.00;-0.00;+0.00} / {q.Num("wb"):+0.00;-0.00;+0.00} m";
            RoomText(c);
        }
        for (int i = 0; i < 2; i++) _paint[i].BackColor = ColorTranslator.FromHtml((string)c.Arr("paint")![i]!);
        ShowMini(c);
        _loading = false;
        ShowStats();
        Draw(true);
    }

    string GlbPath(JObj c) => Path.Combine(Paths.Tracks, "cars", c.Str("body_glb")!);

    void RoomText(JObj c)
    {
        try
        {
            string glb = GlbPath(c);
            var (p, t) = CarUi.Need(glb); var (v, _) = CarUi.Room(c.Str("base")!);
            if (CarUi.Fits(c.Str("base")!, glb)) { _roomLabel.Text = $"✔ The body fits {c.Str("base")} ({p} of its {v} points of room used)."; _roomLabel.ForeColor = Theme.Good; }
            else
            {
                _roomLabel.Text = $"⚠ Class {CarUi.Classes[(int)c.Int("class")]}'s body car ({c.Str("base")!.Replace('_', ' ')}) has room for only {100 * v / Math.Max(p, 1)}% of this body ({v} of {p} points): in game parts of it will be MISSING. Make the .glb smaller (Blender: Decimate) - to about {(int)(t * (double)v / Math.Max(p, 1) * 0.95)} triangles.";
                _roomLabel.ForeColor = ColorTranslator.FromHtml("#ffb020");
            }
        }
        catch (Exception e) { _roomLabel.Text = "(room check failed: " + e.Message + ")"; _roomLabel.ForeColor = Theme.Text; }
    }

    void ShowMini(JObj c)
    {
        try
        {
            if (_mini.Items.Count == 0) { _mini.Items.Add(MiniAuto); foreach (var s in CarUi.MiniShapes()) _mini.Items.Add(s); }
            string pick = CarUi.Mini(c);
            _mini.SelectedItem = pick == CarIcon.Auto ? MiniAuto : pick;
            var old = _miniPic.Image;
            _miniPic.Image = CarUi.MiniPicture(c, 150, Theme.Bg);
            old?.Dispose();
        }
        catch (Exception) { _miniPic.Image = null; }
    }

    void ClassChanged(int cls)
    {
        var c = Car();
        if (c == null || _loading || cls == c.Int("class")) return;
        var bas = CarsData.ClassCars(cls)[0];
        if (c.Truthy("body_glb")) bas = CarsData.ClassCars(cls).FirstOrDefault(x => x.Name == CarGlb.GlbBody[cls]) ?? bas;
        Edit(q => { q["class"] = (long)cls; q["base"] = bas.Name; q["stats"] = CarUi.StatsOf(bas); CarUi.Fair(q); });
        _loading = true; _list.Items[Sel] = Label(Car()!); _loading = false;
        Load2();
    }

    void BaseChanged()
    {
        var bas = CarsData.Cars().First(x => x.Name == (string)_base.SelectedItem!);
        Edit(q => { q["base"] = bas.Name; q["icon"] = bas.Icon; q["stats"] = CarUi.StatsOf(bas); CarUi.Fair(q); });
        Load2();
    }

    void Paint(int i)
    {
        var c = Car();
        if (c == null || c.Truthy("body_glb")) return;
        using var d = new ColorDialog { FullOpen = true, Color = ColorTranslator.FromHtml((string)c.Arr("paint")![i]!) };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        string col = $"#{d.Color.R:x2}{d.Color.G:x2}{d.Color.B:x2}";
        Edit(q => q.Arr("paint")![i] = col);
        Load2();
    }

    public void SetStat(string key, long value)
    {
        var c = Car();
        if (_loading || c == null || c.Obj("stats")!.Int(key) == value) return;
        int cls = (int)c.Int("class");
        var (lo, hi) = CarsData.Limit(cls, key);
        long budget = CarUi.Budget(cls), room = budget - (CarUi.Total(c) - c.Obj("stats")!.Int(key)), want = value;
        value = Math.Max(lo, Math.Min(value, Math.Min(room, hi)));
        if (value != c.Obj("stats")!.Int(key)) Edit(q => q.Obj("stats")![key] = value);
        if (value < want) _f.Say(value == room ? $"No points left: lower another stat first (the class's budget is {budget})" : $"That is the most any class {CarUi.Classes[cls]} car has");
        else if (value > want) _f.Say($"That is the least any class {CarUi.Classes[cls]} car has");
        ShowStats();
    }

    void Step(string key, int d) { var c = Car(); if (c != null) SetStat(key, c.Obj("stats")!.Int(key) + d); }

    void ChartClick(MouseEventArgs e)
    {
        if (Car() == null) return;
        int i = e.Y / RowH;
        if (i >= 0 && i < CarUi.StatRows.Length) { const int x0 = 10, x1 = 520 - 60; SetStat(CarUi.StatRows[i].Key, Py.Round((e.X - x0) * 10.0 / (x1 - x0))); }
    }

    void ShowStats()
    {
        var c = Car();
        if (c == null) return;
        int cls = (int)c.Int("class");
        long budget = CarUi.Budget(cls), left = budget - CarUi.Total(c);
        _points.Text = $"Points used: {CarUi.Total(c)} / {budget}   ({left} left)"; _points.ForeColor = left == 0 ? Theme.Orange : Theme.Text;
        var (m0, m1) = CarUi.Mph(cls);
        _top.Text = $"Top speed about {CarsData.TopSpeed(cls, c.Obj("stats")!.Int("speed"))} mph  (class {CarUi.Classes[cls]}: {m0} - {m1} mph)";
        foreach (var (k, _) in CarUi.StatRows) _vals[k].Text = c.Obj("stats")!.Int(k).ToString();
        _chart.Invalidate();
    }

    void ChartPaint(object? s, PaintEventArgs e)
    {
        var c = Car();
        if (c == null) return;
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        const float x0 = 10, x1 = 520 - 60;
        float X(double v) => x0 + (x1 - x0) * (float)v / 10f;
        int cls = (int)c.Int("class"); var cars = CarsData.ClassCars(cls);
        int i = 0;
        using var field = new SolidBrush(Theme.Field); using var glass = new SolidBrush(Theme.Glass); using var orange = new SolidBrush(Theme.Orange);
        using var edge = new Pen(Theme.GlassHi); using var muted = new Pen(ColorTranslator.FromHtml("#7f8a96")); using var head = new Pen(Theme.Head, 2);
        foreach (var (key, _) in CarUi.StatRows)
        {
            float y = i * RowH + RowH / 2f;
            var (lo, hi) = CarsData.Limit(cls, key);
            g.FillRectangle(field, X(0), y - 3, X(10) - X(0), 6);
            g.FillRectangle(glass, X(lo), y - 7, X(hi) - X(lo), 14); g.DrawRectangle(edge, X(lo), y - 7, X(hi) - X(lo), 14);
            foreach (var q in cars) g.DrawLine(muted, X(q.Stats[key]), y - 9, X(q.Stats[key]), y + 9);
            double avg = cars.Average(q => (double)q.Stats[key]);
            g.DrawLine(head, X(avg), y - 12, X(avg), y + 12);
            long v = c.Obj("stats")!.Int(key);
            g.FillRectangle(orange, X(0), y - 4, X(v) - X(0), 8);
            g.FillEllipse(orange, X(v) - 7, y - 7, 14, 14); g.DrawEllipse(Pens.White, X(v) - 7, y - 7, 14, 14);
            i++;
        }
    }

    void ImportBody()
    {
        var c0 = Car();
        if (c0 == null) return;
        string? path = _f.PickFile("Car body (.glb)", "glTF binary|*.glb");
        if (path == null) return;
        int n;
        try { n = CarModel.ReadGlbBody(path).Tri.Length / 9; }
        catch (Exception e) when (e is not OutOfMemoryException) { _f.Say("This .glb can't be used: " + e.Message); return; }
        if (n > CarUi.GlbMaxTris && _f.Msg("Car body", $"This body has {n} triangles - more than {CarUi.GlbMaxTris}.\n\nA car body this detailed may not fit: the game car's model only has room for about {CarUi.GlbMaxTris}, the rest would be missing (holes).\n\nMake it smaller in your 3D program first (Blender: Decimate modifier), or import it anyway?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        string folder = Path.Combine(Paths.Tracks, "cars");
        Directory.CreateDirectory(folder);
        string name = Path.GetFileName(path), dst = Path.Combine(folder, name);
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dst, true);
        Set("body_glb", name);
        var c = Car()!;
        string msg = $"Body imported: {name} ({n} triangles), on {c.Str("base")!.Replace('_', ' ')}";
        if (!CarUi.Fits(c.Str("base")!, GlbPath(c))) msg += " - TOO BIG for it: see the warning under the fit buttons";
        _f.Say(msg);
    }

    void Fit(string? key, double d)
    {
        var c = Car();
        var q = c?.Obj("glb_fit") != null ? Share.Copy(c.Obj("glb_fit")!) : new JObj();
        if (key == null) q = new JObj();
        else if (key == "turn") q["turn"] = Py.Mod(q.Int("turn") + d, 360);
        else q[key] = Py.RoundN(q.Num(key, key == "size" ? 1.0 : 0.0) + d, 3);
        Set("glb_fit", q);
    }

    void Draw(bool full)
    {
        var c = Car();
        if (c == null || !Visible) { _view.Image = null; return; }
        int w = _view.Width, h = _view.Height;
        if (w < 50 || h < 50) return;
        string key = Json.Dumps(new List<object?> { c["base"], c["wheels"], c["body_glb"], c["glb_fit"] }, -1);
        var col = ColorTranslator.FromHtml((string)c.Arr("paint")![0]!);
        byte[] paint = { col.R, col.G, col.B };
        int job = ++_job, size = Math.Max(60, Math.Min(w, h)); double yaw = _yaw;
        var car = Share.Copy(c);
        var known = _parts; string pk = _partsKey;
        Task.Run(() =>
        {
            Bitmap? bm = null; string? err = null; CarUi.Parts? parts = known;
            try
            {
                if (key != pk) parts = CarUi.CarParts(car);
                if (job == _job) bm = CarUi.Picture(parts!, paint, size, yaw, full);
            }
            catch (Exception e) { err = "This car can't be shown: " + e.Message; }
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(() =>
                {
                    if (parts != null) { _parts = parts; _partsKey = key; }
                    if (err != null) { _hint.Text = err; return; }
                    if (job != _job || bm == null) { bm?.Dispose(); return; }
                    _hint.Text = "drag = turn the car";
                    var old = _view.Image; _view.Image = bm; old?.Dispose();
                    PictureDone?.Invoke();
                });
        });
    }

    public event Action? PictureDone;
}

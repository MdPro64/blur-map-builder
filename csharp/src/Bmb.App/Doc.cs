// The open document: the world, the current map, undo / redo, cached roads. Shared by the views and the panels.
using Bmb.Core;

namespace Bmb.App;

public sealed class Doc
{
    public JObj World = new();
    public int Li, Mi;
    readonly List<string> _undo = new(), _redo = new();
    Road? _road; string _roadSig = ""; JObj? _roadMap;
    public event Action? Changed;      // the map's data changed
    public string Status = "";
    public const int MaxUndo = 30;

    public JObj Loc => World.Objs("locations").ElementAt(Li);
    public JObj Map => Loc.Objs("maps").ElementAt(Mi);
    public List<object?> Points => Map.Arr("points")!;
    public static Road? Safe(Func<Road> f) { try { return f(); } catch (Exception) { return null; } }

    public static string Autosave => Path.Combine(Paths.Tracks, "world.autosave.json");

    public void Load()
    {
        World = Bmb.Core.World.Load();
        Bmb.Core.World.AssignIds(World);
        Clamp();
        _undo.Clear(); _redo.Clear();
    }

    /// unsaved changes from last time? (road_designer.load_world)
    public static bool AutosaveNewer() => File.Exists(Autosave) && (!File.Exists(Paths.WorldFile) || (File.GetLastWriteTimeUtc(Autosave) - File.GetLastWriteTimeUtc(Paths.WorldFile)).TotalSeconds > 2);

    public void LoadAutosave()
    {
        World = (JObj)Json.Load(Autosave)!;
        Bmb.Core.World.AssignIds(World);
        Clamp();
    }

    public void WriteAutosave() { try { File.WriteAllText(Autosave, Json.Dumps(World, -1)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// the current map's points, [x, z] pairs turned into objects (road_designer.points)
    public List<object?> PointsAsObjects()
    {
        var pts = Points;
        for (int k = 0; k < pts.Count; k++)
            if (pts[k] is not JObj) { var a = (List<object?>)pts[k]!; pts[k] = new JObj { ["x"] = Json.D(a[0]), ["z"] = Json.D(a[1]) }; }
        return pts;
    }

    public int TotalMaps => World.Objs("locations").Sum(l => l.Objs("maps").Count());

    readonly Dictionary<JObj, (string Key, Road? R)> _roads = new();
    /// another map's road (made again only when its shape changed)
    public Road? RoadOf(JObj m)
    {
        string key = Json.Dumps(new List<object?> { m["points"], m["width"], m["auto_bridges"], m["laps"] }, -1);
        if (_roads.TryGetValue(m, out var hit) && hit.Key == key) return hit.R;
        if (_roads.Count > 32) _roads.Clear();
        var r = Safe(() => Bmb.Core.Road.ForMap(m));
        _roads[m] = (key, r);
        return r;
    }

    void Clamp()
    {
        Li = Math.Max(0, Math.Min(Li, World.Arr("locations")!.Count - 1));
        Mi = Math.Max(0, Math.Min(Mi, Loc.Arr("maps")!.Count - 1));
    }

    public bool Dirty;
    public void Save() { Bmb.Core.World.Save(World); Dirty = false; }

    /// before an edit
    public void Snapshot()
    {
        Dirty = true;
        _undo.Add(Json.Dumps(World, -1));
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void Touched() { _road = null; Changed?.Invoke(); }

    public int UndoCount => _undo.Count; public int RedoCount => _redo.Count;
    public bool Undo() => Swap(_undo, _redo);
    public bool Redo() => Swap(_redo, _undo);
    bool Swap(List<string> a, List<string> b)
    {
        if (a.Count == 0) return false;
        b.Add(Json.Dumps(World, -1));
        World = (JObj)Json.Parse(a[^1])!;
        a.RemoveAt(a.Count - 1);
        Clamp();
        Touched();
        return true;
    }

    /// the current map's Road (made again only when the map's points / width changed)
    public Road? Road()
    {
        if (_road != null && _roadMap == Map) return _road;
        var m = Map;
        _road = Safe(() => Bmb.Core.Road.ForMap(m));
        _roadMap = m;
        return _road;
    }

    public Network? Net() { var r = Road(); return r == null ? null : Network.Get(Map, r); }

    /// the race lines' info for the layout (the same as gen_aid.build_path gives, without writing the file)
    public AidInfo? Info()
    {
        var r = Road();
        if (r == null) return null;
        var (si, fi) = GenAid.RaceLines(r, Map);
        return new AidInfo { StartIndex = si, FinishIndex = fi, Closed = r.Closed };
    }
}

/// a small window that never takes the focus (an open menu closes as soon as another window does)
public class QuietPop : Form
{
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; return c; } }   // no-activate, tool window, topmost
    protected override void WndProc(ref Message m) { if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } base.WndProc(ref m); }                          // WM_MOUSEACTIVATE: no
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr w, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public bool Up;
    public void Put(Point at)
    {
        if (!IsHandleCreated) { Location = new Point(-2000, -2000); Show(); }
        SetWindowPos(Handle, (IntPtr)(-1), at.X, at.Y, 0, 0, 0x0001 | 0x0010 | 0x0040);     // no size, no activate, show
        Up = true;
    }
    public void Away() { if (IsHandleCreated) SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0080); Up = false; }   // hide
}

/// the small bar shown while a tab is being made
public sealed class LoadBar : QuietPop
{
    string _text = ""; int _pct;
    public LoadBar()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; ClientSize = new Size(280, 54); BackColor = Theme.Panel; DoubleBuffered = true;
        Look.Dress(this);
    }
    public void Set(string text, int pct) { _text = text; _pct = pct; Refresh(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, Lang.T(_text), Theme.Ui, new Rectangle(12, 8, Width - 24, 18), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        using (var bg = new SolidBrush(Theme.Field)) using (var p = Look.RoundPath(new RectangleF(12, 32, Width - 24, 10), 5)) g.FillPath(bg, p);
        int w = (Width - 24) * _pct / 100;
        if (w > 0) using (var fg = new SolidBrush(Theme.Blue)) using (var p = Look.RoundPath(new RectangleF(12, 32, Math.Max(w, 10), 10), 5)) g.FillPath(fg, p);
    }
}

public static class Theme
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#0b1420"), Panel = ColorTranslator.FromHtml("#101c2c"), Panel2 = ColorTranslator.FromHtml("#16243a"), Glass = ColorTranslator.FromHtml("#1d2f4a"),
        GlassHi = ColorTranslator.FromHtml("#2a4468"), Blue = ColorTranslator.FromHtml("#1e9bff"), Orange = ColorTranslator.FromHtml("#ff7a1a"), Text = ColorTranslator.FromHtml("#d8e4f0"), Head = ColorTranslator.FromHtml("#5ab4ff"),
        Field = ColorTranslator.FromHtml("#0d1826"), Good = ColorTranslator.FromHtml("#58d06a"), Bad = ColorTranslator.FromHtml("#ff6060"), Warn = ColorTranslator.FromHtml("#ffd23b");
    public static readonly Font Ui = new("Segoe UI Symbol", 9f), Bold = new("Segoe UI Symbol", 9.5f, FontStyle.Bold), Small = new("Segoe UI", 7.5f, FontStyle.Bold);

    public static Button Btn(string text, Action a, Color? bg = null)
    {
        var b = new RoundButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(28, 28), FlatStyle = FlatStyle.Flat, BackColor = bg ?? Glass, ForeColor = Color.White, Font = Ui, Margin = new Padding(2), Padding = new Padding(5, 1, 5, 1), UseVisualStyleBackColor = false, TabStop = false };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = GlassHi;
        b.Click += (_, _) => a();
        return b;
    }

    public static readonly Font IconFont = new("Segoe UI Symbol", 11.5f);
    /// a button that is only a symbol (⚙, ↶ ...): a bigger symbol, the name in its tooltip
    public static Button Icon(string symbol, Action a, string tip)
    {
        var b = Btn(symbol == "❔" ? "?" : symbol, a); b.Font = symbol == "❔" ? new Font("Segoe UI", 10.5f, FontStyle.Bold) : IconFont; b.MinimumSize = new Size(30, 28); b.Padding = new Padding(1, 0, 1, 0);
        Look.Tip(b, tip);
        return b;
    }

    public static CheckBox Toggle(string text, Action<bool> a)
    {
        var b = new RoundToggle { Text = text, Appearance = Appearance.Button, AutoSize = true, MinimumSize = new Size(28, 28), FlatStyle = FlatStyle.Flat, BackColor = Glass, ForeColor = Color.White, Font = Ui, Margin = new Padding(2), Padding = new Padding(5, 1, 5, 1), TabStop = false };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.CheckedBackColor = Blue; b.FlatAppearance.MouseOverBackColor = GlassHi;
        b.CheckedChanged += (_, _) => a(b.Checked);
        return b;
    }

    public static Button Colored(string text, string bg, Action a) { var b = Btn(text, a, ColorTranslator.FromHtml(bg)); return b; }

    public static RadioButton Radio(string text, string? bg, Action a)
    {
        var b = new RoundRadio { Text = text, Appearance = Appearance.Button, AutoSize = true, MinimumSize = new Size(28, 28), FlatStyle = FlatStyle.Flat, BackColor = bg != null ? ColorTranslator.FromHtml(bg) : Glass, ForeColor = Color.White, Font = Ui, Margin = new Padding(2), Padding = new Padding(5, 1, 5, 1), TabStop = false };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.CheckedBackColor = Blue; b.FlatAppearance.MouseOverBackColor = GlassHi;
        b.Click += (_, _) => a();
        return b;
    }

    public static ComboBox Combo(int width = 120)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = Field, ForeColor = Text, FlatStyle = FlatStyle.Flat, Font = Ui };
        Look.DarkCombo(c);
        c.MouseWheel += (_, e) => ((HandledMouseEventArgs)e).Handled = true;
        return c;
    }

    /// label  -  value  +  (road_designer.build_props row)
    public static FlowLayoutPanel Row(string label, string value, Action minus, Action plus)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent };
        f.Controls.Add(new Label { Text = label, AutoSize = false, Width = Lang.Ru ? 124 : 112, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Text, Font = Ui, AutoEllipsis = true, UseMnemonic = false });
        f.Controls.Add(Btn("-", minus));
        f.Controls.Add(new Label { Text = value, AutoSize = false, Width = 64, Height = 28, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = Ui, AutoEllipsis = true });
        f.Controls.Add(Btn("+", plus));
        return f;
    }

    public static Label Heading(string t) => new() { Text = t, UseMnemonic = false, AutoSize = true, ForeColor = Head, Font = Bold, Margin = new Padding(4, 8, 2, 2) };
    public static Label Lbl(string t) => new() { Text = t, UseMnemonic = false, AutoSize = true, ForeColor = Text, Font = Ui, Margin = new Padding(4, 4, 2, 2) };

    public static CheckBox Check(string text, bool on, Action<bool> a)
    {
        var c = new CheckBox { Text = text, AutoSize = true, ForeColor = Text, Font = Ui, Checked = on, Margin = new Padding(4, 1, 2, 1), TabStop = false };
        c.CheckedChanged += (_, _) => a(c.Checked);
        return c;
    }
}

/// number field with - / + buttons
public sealed class Spin : FlowLayoutPanel
{
    readonly TextBox _t;
    double _v; readonly double _step, _lo, _hi; readonly string _fmt;
    public event Action<double>? Set;
    public double Value { get => _v; set { _v = value; _t.Text = _v.ToString(_fmt); } }
    public Spin(string label, double v, double step, double lo, double hi, string fmt = "0.##")
    {
        _v = v; _step = step; _lo = lo; _hi = hi; _fmt = fmt;
        AutoSize = true; WrapContents = false; Margin = new Padding(2, 1, 2, 1); BackColor = Color.Transparent;
        Controls.Add(new Label { Text = label, AutoSize = false, Width = 104, Height = 24, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text, Font = Theme.Ui, Visible = label != "" });
        Controls.Add(Theme.Btn("-", () => Nudge(-_step)));
        _t = new TextBox { Width = 54, Text = v.ToString(fmt), BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui };
        _t.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Take(); e.SuppressKeyPress = true; } };
        _t.Leave += (_, _) => Take();
        Controls.Add(_t);
        Controls.Add(Theme.Btn("+", () => Nudge(_step)));
    }
    /// on a tool bar: the name only as wide as its text (the panels keep the fixed width so their boxes line up)
    public Spin Tight() { var l = (Label)Controls[0]; l.AutoSize = true; l.Padding = new Padding(0, 5, 0, 0); l.Margin = new Padding(4, 0, 0, 0); return this; }
    void Take() { if (double.TryParse(_t.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v != _v) Apply(v); else _t.Text = _v.ToString(_fmt); }
    void Nudge(double d) => Apply(_v + d);
    void Apply(double v) { _v = Math.Max(_lo, Math.Min(_hi, v)); _t.Text = _v.ToString(_fmt); Set?.Invoke(_v); }
}

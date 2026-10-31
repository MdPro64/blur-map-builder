// The look of the window: rounded buttons, the program's icon + dark title bars, the short help cards, the picture of a tilted road.
using System.Drawing.Drawing2D;
using Bmb.Core;

namespace Bmb.App;

public static class Look
{
    public const int Radius = 7;
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr w, int attr, ref int value, int size);

    static Icon? _icon;
    public static Icon AppIcon { get { if (_icon == null) { using var s = typeof(Look).Assembly.GetManifestResourceStream("icon.ico")!; _icon = new Icon(s); } return _icon; } }

    /// every window: the program's icon, a dark title bar, round corners; `help` = the key of its short help (a ? button appears)
    public static void Dress(Form f, string? help = null)
    {
        f.Icon = AppIcon;
        Lang.Watch(f);
        void Dark()
        {
            int on = 1, round = 2;
            DwmSetWindowAttribute(f.Handle, 20, ref on, 4);        // dark title bar
            DwmSetWindowAttribute(f.Handle, 33, ref round, 4);     // round corners (Windows 11)
        }
        if (f.IsHandleCreated) Dark(); else f.HandleCreated += (_, _) => Dark();
        if (help != null) f.Load += (_, _) => AddHelp(f, help);
    }

    static void AddHelp(Form f, string key)
    {
        var b = Theme.Btn("?", () => HelpCard.Show(f, key), Theme.Blue);
        b.AutoSize = false; b.Size = new Size(26, 26); b.Font = Theme.Bold; b.Margin = new Padding(0);
        b.Location = new Point(f.ClientSize.Width - b.Width - 6, 6); b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        f.Controls.Add(b); b.BringToFront();
        f.Tag = b;
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int SetWindowTheme(IntPtr w, string app, string? list);
    /// dark scroll bars
    public static void DarkScroll(params Control[] cs)
    {
        foreach (var c in cs)
        {
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        }
    }

    public static GraphicsPath RoundPath(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(2 * rad, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// round the corners of a panel that sits on top of something
    public static void RoundOff(Control c, int rad = 9)
    {
        void Fit() { if (c.Width > 0 && c.Height > 0) { using var p = RoundPath(new RectangleF(0, 0, c.Width, c.Height), rad); c.Region = new Region(p); } }
        c.SizeChanged += (_, _) => Fit();
        Fit();
    }

    /// the size a button needs for its text
    public static Size Fit(Control c, Size base0)
    {
        var t = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return new Size(Math.Max(c.MinimumSize.Width, t.Width + 16), Math.Max(c.MinimumSize.Height, Math.Max(base0.Height, t.Height + 6)));
    }

    static readonly ToolTip _tip = new() { InitialDelay = 400, ReshowDelay = 100 };
    public static T Tip<T>(T c, string text) where T : Control { _tip.SetToolTip(c, Lang.T(text)); return c; }

    /// a dark drop-down list (the closed box too)
    public static void DarkCombo(ComboBox c)
    {
        c.DrawMode = DrawMode.OwnerDrawFixed;
        c.DrawItem += (_, e) =>
        {
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(sel ? Theme.Blue : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, Lang.T(c.GetItemText(c.Items[e.Index])), c.Font, e.Bounds, c.Enabled ? (sel ? Color.White : Theme.Text) : Color.Gray, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    static Color Under(Control c)
    {
        for (var q = c.Parent; q != null; q = q.Parent) if (q.BackColor.A == 255) return q.BackColor;
        return Theme.Bg;
    }

    static Color Mix(Color a, Color b, double t) => Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void Paint(Graphics g, Control c, Color fill, bool hot, bool down, ContentAlignment align)
    {
        g.Clear(Under(c));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (fill.A < 255) fill = Theme.Glass;
        var col = !c.Enabled ? Mix(fill, Theme.Bg, .55) : down ? Mix(fill, Color.Black, .18) : hot ? Mix(fill, Color.White, .14) : fill;
        using (var p = RoundPath(new RectangleF(0.5f, 0.5f, c.Width - 1.5f, c.Height - 1.5f), Radius))
        using (var b = new SolidBrush(col)) g.FillPath(b, p);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        flags |= align is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        var r = new Rectangle(6, 0, c.Width - 12, c.Height);
        TextRenderer.DrawText(g, c.Text, c.Font, r, c.Enabled ? c.ForeColor : Mix(c.ForeColor, fill, .55), flags);
    }
}

public sealed class RoundButton : Button
{
    bool _hot, _down;
    public RoundButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    public override Size GetPreferredSize(Size proposed) => Look.Fit(this, base.GetPreferredSize(proposed));
    /// a small triangle instead of text: -1 left, 1 right
    public int Arrow { get => _arrow; set { _arrow = value; Invalidate(); } }
    int _arrow;
    protected override void OnPaint(PaintEventArgs e)
    {
        Look.Paint(e.Graphics, this, BackColor, _hot, _down, TextAlign);
        if (_arrow == 0) return;
        float cx = Width / 2f, cy = Height / 2f;
        using var b = new SolidBrush(ForeColor);
        e.Graphics.FillPolygon(b, new[] { new PointF(cx + 4 * _arrow, cy), new PointF(cx - 3 * _arrow, cy - 5), new PointF(cx - 3 * _arrow, cy + 5) });
    }
    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
}

public sealed class RoundToggle : CheckBox
{
    bool _hot;
    public RoundToggle() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    public override Size GetPreferredSize(Size proposed) => Look.Fit(this, base.GetPreferredSize(proposed));
    protected override void OnPaint(PaintEventArgs e) => Look.Paint(e.Graphics, this, Checked ? FlatAppearance.CheckedBackColor : BackColor, _hot, false, TextAlign);
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
}

public sealed class RoundRadio : RadioButton
{
    bool _hot;
    public RoundRadio() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    public override Size GetPreferredSize(Size proposed) => Look.Fit(this, base.GetPreferredSize(proposed));
    protected override void OnPaint(PaintEventArgs e) => Look.Paint(e.Graphics, this, Checked ? FlatAppearance.CheckedBackColor : BackColor, _hot, false, TextAlign);
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
}

/// a list without a sideways scroll bar (the eye + solo icons stay at the right edge)
public sealed class IconTree : TreeView
{
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.Style |= 0x8000; return c; } }   // TVS_NOHSCROLL
    public IconTree() { DoubleBuffered = true; }
}

/// a slice across the road: which side goes up
public sealed class TiltPicture : Control
{
    readonly double _deg;
    public TiltPicture(double deg)
    {
        _deg = deg; Width = 260; Height = 52; Margin = new Padding(4, 2, 2, 4);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Panel); g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Look.RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Look.Radius))
        using (var b = new SolidBrush(Theme.Field)) g.FillPath(b, p);
        float cx = Width / 2f, cy = Height / 2f + 2, half = Width / 2f - 46;
        float rise = (float)(half * Math.Tan(_deg * Math.PI / 180)) * 1.6f;     // shown a bit steeper: a few degrees must be visible here
        rise = Math.Max(-18, Math.Min(18, rise));
        using (var road = new Pen(ColorTranslator.FromHtml("#8a93a0"), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(road, cx - half, cy - rise, cx + half, cy + rise);
        using (var mid = new Pen(Color.White, 1)) g.DrawLine(mid, cx, cy - 4, cx, cy + 4);
        string a = Lang.T("LEFT"), b2 = Lang.T("RIGHT");
        Color up = ColorTranslator.FromHtml("#ff9a2e"), down = ColorTranslator.FromHtml("#39b6ff");
        TextRenderer.DrawText(g, a, Theme.Small, new Rectangle(4, 0, 40, Height), _deg > 0 ? up : _deg < 0 ? down : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        TextRenderer.DrawText(g, b2, Theme.Small, new Rectangle(Width - 44, 0, 40, Height), _deg < 0 ? up : _deg > 0 ? down : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
}

/// a small help window: what this window is for, in a few short lines
public sealed class HelpCard : Form
{
    public static HelpCard? Last;
    public static readonly Dictionary<string, (string Title, string[] Lines)> Texts = new()
    {
        ["2d"] = ("2D map", new[] { "Draw your road from above.", "A adds a point at the end. I inserts one in the road. E erases.", "S selects a point: change its width, height, corner and tilt on the right.", "Tilt: select a point in a bend, press Auto tilt, or use the , and . keys.", "Orange edge = the side that goes up. Blue edge = the side that goes down.", "B makes a branch, L a lane split, R a row of power-ups.", "Wheel zooms. Middle mouse moves the map." }),
        ["3d"] = ("3D view", new[] { "See what the build will place. Middle mouse turns the view, wheel zooms.", "Object list: the eye hides a row in this view only, the target shows only that row.", "Click an object, then G moves, R turns, S scales, X hides it.", "Click an orange road point: G moves it, R tilts the road there (drag the ring).", "Pick an object in the library, then click in the view to place it. Esc stops.", "Sun + shadows makes a tilted road easier to see." }),
        ["collision"] = ("Collision", new[] { "What the cars really touch.", "Green = the ground you drive on.", "Red = solid walls. White ticks show the side that pushes cars away.", "Yellow = bridge decks and tilted road.", "You look here. You change things in the 2D and 3D tabs." }),
        ["ai"] = ("AI path", new[] { "Where the AI cars drive.", "Auto AI path = made for you. You can skip this tab.", "Racing line = cuts the inside of the bends.", "Pen = drag on the road to draw it yourself.", "AI lane width = how wide their lane is." }),
        ["light"] = ("Lighting", new[] { "The light of the whole location.", "Pick a city to start from, then change what you like.", "Values you changed are marked with *.", "Placed game models look black? Press Light up placed objects.", "Custom sky = your own 360 picture (2:1).", "Changes show in game after Build + install." }),
        ["surface"] = ("Surface", new[] { "Paint dirt, gravel, grass, sand or water on the road.", "Left drag paints, right drag removes.", "Whole road: one type for all of it, and the side strips.", "Grip sets how the cars hold the road in this location." }),
        ["car"] = ("Custom car", new[] { "Make your own car.", "+ New, then pick a class and a body car.", "Spend the class's stat points: it can never beat the best car of its class.", "Wheels: hover a name to see the wheel.", "Your own body: load a .glb and fit it on the wheels." }),
        ["build"] = ("Building", new[] { "This puts your maps into the game.", "Blur must be closed.", "When it ends you can start the game.", "If it fails, the red lines say why." }),
        ["play"] = ("Play", new[] { "Custom LAN race: starts Blur and sets the lobby to this map.", "In game: Multiplayer > LAN > Host game (Y).", "Start normally: just starts the game.", "Build + install first: the game plays the last build." }),
        ["update"] = ("New version", new[] { "The notes show what is new.", "Update now downloads it and restarts the map builder.", "Your maps are kept." }),
        ["ask"] = ("Type a value", new[] { "Type your answer, then press OK.", "Close the window to cancel." }),
        ["minimap"] = ("Minimap preview", new[] { "This is how the map looks in the game's minimap.", "Bright = your road. Press Esc to close." }),
        ["import"] = ("Import 3D model", new[] { "Checks your .glb before it joins the library.", "Green is fine, yellow is a warning, red stops the import.", "After the import, click in the view to place it." }),
        ["scatter"] = ("Path scatter", new[] { "Places many objects along a path in one go.", "1  Draw a path on the ground.", "2  Click objects in the library to add them.", "3  Set the spacing and the sides. The preview shows where they go.", "4  Press Add. It becomes one group." }),
        ["setup"] = ("First start", new[] { "The program reads your own game files once.", "It does not change Blur.exe or the game's files.", "Wait until the bar is full." }),
    };

    public static void Show(Form owner, string key)
    {
        Last?.Close();
        var (title, lines) = Texts[key];
        var f = new HelpCard(title, lines, key, owner);
        Last = f;
        f.Show(owner);
    }

    HelpCard(string title, string[] lines, string key, Form owner)
    {
        Text = "Help - " + title; BackColor = Theme.Panel; ForeColor = Theme.Text; KeyPreview = true;
        FormBorderStyle = FormBorderStyle.FixedToolWindow; StartPosition = FormStartPosition.Manual; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        Look.Dress(this);
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14), BackColor = Theme.Panel };
        p.Controls.Add(new Label { Text = title, AutoSize = true, UseMnemonic = false, ForeColor = Theme.Head, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) });
        foreach (var l in lines) p.Controls.Add(new Label { Text = l, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(440, 0), ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f), Margin = new Padding(0, 2, 0, 2) });
        var foot = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Panel, Margin = new Padding(0, 10, 0, 0) };
        foot.Controls.Add(Theme.Btn("Got it", Close, Theme.Blue));
        if (owner is MainForm mf) foot.Controls.Add(Theme.Btn("All keys", () => { Close(); mf.FullHelp(); }));
        p.Controls.Add(foot);
        Controls.Add(p);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Load += (_, _) =>
        {
            var a = Screen.FromControl(owner).WorkingArea;
            Location = new Point(Math.Max(a.Left, Math.Min(owner.Right - Width - 24, a.Right - Width)), Math.Max(a.Top, Math.Min(owner.Top + 90, a.Bottom - Height)));
        };
        FormClosed += (_, _) => { if (Last == this) Last = null; };
    }
}

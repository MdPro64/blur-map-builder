// The SIMPLE layout (for new users): the tabs are round icons on a rail at the left (like Discord's server list),
// each tab has one short tool row with only the main tools, Save / Play / Build sit at the bottom right.
// The ADVANCED layout is the full one (every option). The switch is on the rail and in the ⚙ menu; it is remembered.
using System.Drawing.Drawing2D;
using Bmb.Core;

namespace Bmb.App;

/// one round icon on the rail: a circle, a rounded square when chosen or under the mouse, a small white bar on its left
public sealed class RailButton : Control
{
    readonly Func<bool> _on;
    bool _hover;
    public RailButton(string icon, Func<bool> on, Action click)
    {
        Text = icon; _on = on;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(64, 50); Margin = new Padding(0, 2, 0, 2); Cursor = Cursors.Hand; BackColor = Theme.Bg;
        Font = new Font("Segoe UI Symbol", 15f);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        Click += (_, _) => click();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        bool on = _on();
        const int s = 44;
        var r = new RectangleF((Width - s) / 2f + 3, (Height - s) / 2f, s, s);
        using (var bg = new SolidBrush(on ? Theme.Blue : _hover ? Theme.GlassHi : Theme.Panel2))
        {
            if (on || _hover) { using var p = Look.RoundPath(r, 14); g.FillPath(bg, p); }
            else g.FillEllipse(bg, r);
        }
        if (on || _hover)
        {
            float h = on ? 32 : 14;
            using var pill = Look.RoundPath(new RectangleF(-4, (Height - h) / 2f, 8, h), 3);
            using var w = new SolidBrush(Color.White); g.FillPath(w, pill);
        }
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), on ? Color.White : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public sealed partial class MainForm
{
    public bool Simple;
    Dictionary<string, Control> _advBars = null!;
    readonly Dictionary<string, Control> _simpleBars = new();
    readonly FlowLayoutPanel _rail = new() { Dock = DockStyle.Left, Width = 68, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 8, 0, 0), Visible = false };
    readonly List<RailButton> _railBtns = new();
    Panel _bottom = null!; Control _langBar = null!;
    static string LayoutModeFile => Path.Combine(Paths.Data, "layout_mode.txt");

    static readonly (string Key, string Icon, string Name, string Help)[] RailTabs =
    {
        ("2d", "▦", "Road (2D map)", "Draw the road: click to add points, drag to move them"),
        ("3d", "▲", "3D view", "Look at the map in 3D, drive it"),
        ("land", "⛰", "Landscape", "Hills: left drag builds, right drag flattens"),
        ("surface", "▩", "Surface", "Paint dirt, grass, water... on and next to the road"),
        ("light", "☀", "Lighting", "Pick how the location looks (sun, sky, fog)"),
        ("", "", "", ""),
        ("car", "⛟", "Custom car", "Make your own car"),
        ("skins", "🎨", "Car skins", "New paint for the cars"),
        ("", "", "", ""),
        ("ai", "➿", "AI path", "Where the AI cars drive (made by itself; change it here)"),
        ("collision", "⚠", "Collision", "Check where the car can drive"),
    };

    void MakeSimple()
    {
        _advBars = _bars;
        int n = 0;
        foreach (var (key, icon, name, _) in RailTabs)
        {
            if (key == "") { _rail.Controls.Add(new Panel { Size = new Size(30, 2), BackColor = Theme.GlassHi, Margin = new Padding(22, 6, 0, 6) }); continue; }
            string k = key;
            int num = Array.IndexOf(_tabs.Keys.ToArray(), k) + 1;
            var b = new RailButton(icon, () => Mode == k, () => SetMode(k));
            Look.Tip(b, $"{Lang.T(name)}  (Ctrl+{num})");
            _railBtns.Add(b); _rail.Controls.Add(b); n++;
        }
        var sw = new RailButton("⇄", () => false, () => SetSimple(false));
        Look.Tip(sw, "Advanced layout: all the options");
        sw.Margin = new Padding(0, 14, 0, 2);
        _rail.Controls.Add(sw);
        Look.DarkScroll(_rail);

        Control Head(string key)
        {
            var t = RailTabs.First(q => q.Key == key);
            var f = Bar();
            f.Controls.Add(new Label { Text = t.Icon + "  " + Lang.T(t.Name), AutoSize = true, UseMnemonic = false, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(6, 6, 6, 0) });
            f.Controls.Add(new Label { Text = "", AutoSize = false, Size = new Size(1, 22), BackColor = Theme.GlassHi, Margin = new Padding(4, 5, 8, 0) });
            _simpleBars[key] = f;
            return f;
        }
        void Hint(Control f, string key) => f.Controls.Add(new Label { Text = Lang.T(RailTabs.First(q => q.Key == key).Help), AutoSize = true, UseMnemonic = false, ForeColor = ColorTranslator.FromHtml("#8fa3b8"), Font = Theme.Ui, Margin = new Padding(6, 8, 6, 0) });
        void End(Control f, bool fit = false)
        {
            if (fit) f.Controls.Add(Look.Tip(Theme.Btn("⛶ Fit", FitNow), "Show the whole map (F)"));
            f.Controls.Add(Theme.Icon("❔", Help, "Short help for this tab (F1)"));
        }

        // road
        var r = Head("2d");
        foreach (var k in new[] { "add", "move", "select", "erase", "row" }) r.Controls.Add(ToolBtn(k));
        End(r, true);
        // 3D: the view has its own row (drive, top view...)
        var v = Head("3d"); Hint(v, "3d"); End(v);
        // landscape
        var l = Head("land");
        foreach (var (k, text, col) in new[] { ("raise", "▲ Raise", "#3f8f4a"), ("lower", "▼ Lower", "#8f4a3f"), ("smooth", "≈ Smooth", "#3f6a8f"), ("flatten", "▬ Flatten", "#8f7a3f") })
        {
            string kk = k;
            var rb = Theme.Radio(text, col, () => { V3.LandTool = kk; V3.G.Invalidate(); Say($"Landscape tool: {text.Substring(2)}"); });
            _landBtns.Add((k, rb)); l.Controls.Add(rb);
        }
        var ls = new Spin("size (m)", V3.LandSize, 10, 10, 800, "0").Tight(); ls.Set += x => { V3.LandSize = x; V3.G.Invalidate(); }; l.Controls.Add(ls);
        l.Controls.Add(Look.Tip(Theme.Colored("⛰ Auto hills...", "#2f7a3a", LandAuto), "Builds hills all around the road (the road stays clear)"));
        l.Controls.Add(Look.Tip(Theme.Colored("Clear hills", "#7a2a3a", LandClear), "Removes all hills of this map"));
        End(l);
        // surface
        var s = Head("surface");
        var names = new Dictionary<string, string> { ["tarmac"] = "Asphalt", ["sand"] = "Sand", ["water"] = "Water" };
        foreach (var key in new[] { "dirt", "gravel", "grass", "sand", "water", "tarmac" })
        {
            string k = key;
            var rb = Theme.Radio(names.GetValueOrDefault(k, Surface.Surfaces[k].Label), Surface.Surfaces[k].Colour, () => { Map2.BrushKey = k; SurfBrushShow(); Say($"Brush: {Surface.Surfaces[k].Label} - left drag paints, right drag removes"); });
            _brushBtns.Add((k, rb)); s.Controls.Add(rb);
        }
        {
            var rb = Theme.Radio("Bumps", MapView.BumpColour, () => { Map2.BrushKey = "bumps"; SurfBrushShow(); Say("Brush: BUMPS - left drag adds, right drag removes"); });
            _brushBtns.Add(("bumps", rb)); s.Controls.Add(Look.Tip(rb, "Bumps: rough ground that goes up and down (a broken road)"));
        }
        var ss = new Spin("size (m)", Map2.BrushSize, 1, 1, 40, "0").Tight(); ss.Set += x => Map2.BrushSize = x; s.Controls.Add(ss);
        s.Controls.Add(Look.Tip(Theme.Colored("Clear paint", "#7a2a3a", SurfClear), "Removes everything painted on this map"));
        End(s, true);
        // lighting, cars, skins, AI, collision
        var li = Head("light"); Hint(li, "light"); End(li);
        var c = Head("car"); Hint(c, "car"); End(c);
        var sk = Head("skins");
        var bs = Theme.Btn("⚙ Build skins", async () => await _skins.BuildSkins(), Theme.Orange); bs.Font = Theme.Bold; sk.Controls.Add(bs);
        End(sk);
        var ai = Head("ai");
        ai.Controls.Add(Theme.Btn("⟳ Auto AI path", AiAuto)); ai.Controls.Add(Theme.Btn("🏁 Racing line", AiRacing));
        End(ai, true);
        var co = Head("collision"); Hint(co, "collision"); End(co);
        foreach (var b in _simpleBars.Values) { b.Visible = false; _toolRow.Controls.Add(b); }

        bool on = true;
        if (_test) on = Environment.GetEnvironmentVariable("BMB_SIMPLE") == "1";
        else try { if (File.Exists(LayoutModeFile)) on = File.ReadAllText(LayoutModeFile).Trim() != "advanced"; } catch (IOException) { }
        SetSimple(on, false);
    }

    /// Simple <-> Advanced (show = redo the current tab now)
    public void SetSimple(bool on, bool show = true)
    {
        Simple = on;
        if (show && !_test) try { File.WriteAllText(LayoutModeFile, on ? "simple" : "advanced"); } catch (IOException) { }
        SuspendLayout();
        foreach (var c in _advBars.Values.Concat(_simpleBars.Values)) c.Visible = false;
        _bars = on ? _simpleBars : _advBars;
        _rail.Visible = on;
        _topPanel.Visible = !on;
        _langBar.Visible = !on;
        _topRight.Parent = on ? _bottom : _topPanel;
        _topRight.Dock = DockStyle.Right;
        _topRight.Padding = on ? new Padding(0, 3, 6, 0) : new Padding(0, 4, 6, 0);
        _bottom.Height = on ? 38 : 30;
        foreach (var (k, b) in _brushBtns) b.Checked = k == Map2.BrushKey;
        foreach (var (k, b) in _landBtns) b.Checked = k == V3.LandTool;
        ResumeLayout(true);
        if (show) { ShowTab(Mode); Say(on ? "Simple layout: the tabs are the icons on the left. ⇄ (bottom of the icons) = all the options" : "Advanced layout: every option. ⚙ > Simple layout to go back", Theme.Good); }
    }

    void RailRefresh() { foreach (var b in _railBtns) b.Invalidate(); }
}

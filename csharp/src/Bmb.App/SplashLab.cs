using System.Drawing;
using System.Windows.Forms;

namespace Bmb.App;

/// "Splash Lab" (--splash-lab): sliders for the start-up animation, a Play button that shows the real one over the whole screen,
/// and Save, which makes the program's start-up use these settings (splash_style.json next to the program)
public sealed class SplashLab : Form
{
    readonly SplashStyle _y = SplashStyle.Load();
    readonly FlowLayoutPanel _cols = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 6) };
    readonly CheckBox _loop = new() { Text = "Replay over and over", AutoSize = true, Checked = true };
    readonly CheckBox _sound = new() { Text = "Start sound", AutoSize = true };
    readonly Label _note = new() { AutoSize = true };
    readonly List<(TrackBar bar, Func<double> get, Action<double> set, double scale)> _bars = new();
    Splash? _on;

    public SplashLab()
    {
        Text = "Splash Lab - Blur Map Builder"; BackColor = Theme.Panel; ForeColor = Theme.Text; StartPosition = FormStartPosition.Manual;
        Font = new Font("Segoe UI", 10f); AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(12);
        var wa = Screen.PrimaryScreen!.WorkingArea; Location = new Point(wa.Left + 20, wa.Top + 20);
        var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        Controls.Add(col);
        col.Controls.Add(new Label { Text = "Change a slider: the next play shows it. Click the picture to end a play sooner.", AutoSize = true, ForeColor = Theme.Warn, Margin = new Padding(0, 0, 0, 8) });
        Slider(col, "Speed", 0.3, 3, 100, () => _y.Speed, v => _y.Speed = v, "x");
        Slider(col, "Strokes", 1, 6, 1, () => _y.Count, v => { _y.Count = (int)v; Colours(); }, "");
        Slider(col, "Thickness", 0.3, 3, 100, () => _y.Thick, v => _y.Thick = v, "x");
        Slider(col, "Thick start, thin end", 0, 1.2, 100, () => _y.Taper, v => _y.Taper = v, "");
        Slider(col, "Glow strength", 0, 3, 100, () => _y.Glow, v => _y.Glow = v, "x");
        Slider(col, "Glow size", 0.2, 4, 100, () => _y.GlowSize, v => _y.GlowSize = v, "x");
        Slider(col, "Glow threshold", 0, 0.95, 100, () => _y.Threshold, v => _y.Threshold = v, "");
        Slider(col, "Tail length", 0.3, 3, 100, () => _y.Tail, v => _y.Tail = v, "x");
        Slider(col, "Gap between strokes", 0, 0.6, 100, () => _y.Stagger, v => _y.Stagger = v, " s");
        Slider(col, "Hold at the end", 0, 3, 100, () => _y.Hold, v => _y.Hold = v, " s");
        col.Controls.Add(new Label { Text = "Colours (click one to change it):", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        col.Controls.Add(_cols); Colours();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(Theme.Btn("▶ Play", Play, Theme.Orange));
        row.Controls.Add(Theme.Btn("↺ Reset", () => { var d = new SplashStyle(); foreach (var p in typeof(SplashStyle).GetProperties()) p.SetValue(_y, p.GetValue(d)); Sync(); Colours(); }));
        row.Controls.Add(Theme.Btn("✔ Save as the start-up look", () => { try { _y.Save(); _note.Text = "Saved: the map builder starts with this look."; _note.ForeColor = Theme.Good; } catch (Exception e) { _note.Text = e.Message; _note.ForeColor = Theme.Bad; } }, Theme.Good));
        col.Controls.Add(row);
        var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; row2.Controls.Add(_loop); row2.Controls.Add(_sound);
        _sound.Checked = _y.Sound; _sound.CheckedChanged += (_, _) => _y.Sound = _sound.Checked;
        col.Controls.Add(row2); col.Controls.Add(_note);
        Look.Dress(this);
        Shown += (_, _) => Play();
        FormClosing += (_, _) => { _loop.Checked = false; _on?.Close(); };
    }

    void Slider(Control host, string name, double min, double max, double scale, Func<double> get, Action<double> set, string unit)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var l = new Label { Text = name, Width = 170, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
        var v = new Label { Width = 70, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Warn };
        var b = new TrackBar { Minimum = (int)Math.Round(min * scale), Maximum = (int)Math.Round(max * scale), Width = 300, TickStyle = TickStyle.None, AutoSize = false, Height = 30, BackColor = Theme.Panel };
        b.SmallChange = 1; b.LargeChange = Math.Max(1, (b.Maximum - b.Minimum) / 10);
        void Show() => v.Text = (scale == 1 ? $"{b.Value}" : $"{b.Value / scale:0.00}") + unit;
        b.Value = Math.Clamp((int)Math.Round(get() * scale), b.Minimum, b.Maximum); Show();
        b.ValueChanged += (_, _) => { set(b.Value / scale); Show(); };
        p.Controls.Add(l); p.Controls.Add(b); p.Controls.Add(v); host.Controls.Add(p);
        _bars.Add((b, get, set, scale));
    }

    void Sync() { foreach (var (b, get, _, sc) in _bars) b.Value = Math.Clamp((int)Math.Round(get() * sc), b.Minimum, b.Maximum); }

    void Colours()
    {
        _cols.Controls.Clear();
        if (_y.Colors.Length < 6) { var c = new SplashStyle().Colors; for (int i = 0; i < _y.Colors.Length; i++) c[i] = _y.Colors[i]; _y.Colors = c; }
        for (int i = 0; i < Math.Clamp(_y.Count, 1, 6); i++)
        {
            int k = i;
            var b = new Button { Width = 54, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = ColorTranslator.FromHtml(_y.Colors[k]), Text = $"{k + 1}", ForeColor = Color.Black, Margin = new Padding(0, 0, 6, 0) };
            b.Click += (_, _) =>
            {
                using var d = new ColorDialog { Color = b.BackColor, FullOpen = true };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                _y.Colors[k] = ColorTranslator.ToHtml(Color.FromArgb(d.Color.R, d.Color.G, d.Color.B)); b.BackColor = d.Color;
            };
            _cols.Controls.Add(b);
        }
    }

    void Play()
    {
        if (_on != null && !_on.IsDisposed) { _on.FormClosed -= Again; _on.Close(); }
        if (_y.Sound) Sfx.Play("startup.wav");
        _on = new Splash(_y.Copy(), true);
        _on.FormClosed += Again;
        _on.Show(); _on.Ready();
    }

    void Again(object? s, FormClosedEventArgs e)
    {
        if (!_loop.Checked || IsDisposed) return;
        var t = new System.Windows.Forms.Timer { Interval = 400 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); if (!IsDisposed && _loop.Checked) Play(); };
        t.Start();
    }
}

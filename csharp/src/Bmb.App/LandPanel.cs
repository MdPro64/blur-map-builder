// The Landscape tab's tool bar (the tab itself is the 3D view: View3DLand): brush tools, Paint + its texture, ground type,
// texture by slope on / off, Auto hills, Clear.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm
{
    readonly ComboBox _landGround = Theme.Combo(110), _landPaint = Theme.Combo(110);
    CheckBox? _landAuto;
    readonly Label _landInfo = new() { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Font = Theme.Ui, Margin = new Padding(8, 7, 4, 0) };
    readonly List<(string Key, RadioButton B)> _landBtns = new();

    Control LandBar()
    {
        var p = new Panel { Dock = DockStyle.Top, BackColor = Theme.Panel2 };
        var low = Bar(); var top = Bar();
        V3.LandChanged += LandRefresh;
        var note = new Label { Text = "LEFT drag = use the tool\nRIGHT drag = back to flat", AutoSize = true, BackColor = Theme.Panel, ForeColor = ColorTranslator.FromHtml("#7dffa8"), Font = Theme.Ui, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(4) };
        top.Controls.Add(note);
        var g = Group(top, "TOOL");
        foreach (var (k, text, col, tip) in new[]
        {
            ("raise", "▲ Raise", "#3f8f4a", "Lifts the ground under the brush (hold and drag to build a hill)"),
            ("lower", "▼ Lower", "#8f4a3f", "Lowers it again (never below the flat ground)"),
            ("smooth", "≈ Smooth", "#3f6a8f", "Rounds off sharp bumps and edges"),
            ("flatten", "▬ Flatten", "#8f7a3f", "Makes a flat top at the height where you started the drag"),
            ("noise", "✶ Rough", "#6a3f8f", "Adds random lumps (rocky hills)"),
            ("paint", "🖌 Paint", "#8a5a2a", "Paints a texture on the hills (right drag = back to automatic)"),
        })
        {
            string kk = k;
            var r = Theme.Radio(text, col, () => { V3.LandTool = kk; V3.G.Invalidate(); Say($"Landscape tool: {text.Substring(2)} - {tip}"); });
            r.Checked = k == "raise"; Look.Tip(r, tip); _landBtns.Add((k, r)); g.Controls.Add(r);
        }
        var size = new Spin("size (m)", V3.LandSize, 10, 10, 800, "0").Tight(); size.Set += v => { V3.LandSize = v; V3.G.Invalidate(); }; g.Controls.Add(size);
        var str = new Spin("strength", V3.LandStrength, 0.5, 0.1, 20, "0.0").Tight(); str.Set += v => V3.LandStrength = v; g.Controls.Add(str);
        foreach (var k in Terrain.PaintKeys) _landPaint.Items.Add(Surface.Surfaces[k].Label);
        _landPaint.SelectedIndex = Array.IndexOf(Terrain.PaintKeys, V3.LandPaintKey);
        _landPaint.SelectionChangeCommitted += (_, _) =>
        {
            V3.LandPaintKey = Terrain.PaintKeys[_landPaint.SelectedIndex];
            foreach (var (kk, b) in _landBtns) if (kk == "paint") b.Checked = true;
            V3.LandTool = "paint"; V3.G.Invalidate();
            Say($"Paint: {Surface.Surfaces[V3.LandPaintKey].Label} on the hills (left drag). Right drag = back to automatic");
        };
        g.Controls.Add(Theme.Lbl("paint")); g.Controls.Add(Look.Tip(_landPaint, "The texture the Paint tool puts on the hills"));
        Look.Tip(str, "Raise / Lower / Rough: metres per brush stroke step. Smooth / Flatten: how fast");
        var gr = Group(low, "GROUND");
        foreach (var k in Terrain.Grounds) _landGround.Items.Add(Surface.Surfaces[k].Label);
        _landGround.SelectionChangeCommitted += (_, _) =>
        {
            string k = Terrain.Grounds[_landGround.SelectedIndex];
            Edit(() => Terrain.Land(D.Map)["ground"] = k);
            V3.Dirty();
            Say($"Hills are now {Surface.Surfaces[k].Label} (look + grip)", Theme.Good);
        };
        gr.Controls.Add(_landGround);
        _landAuto = Theme.Check("Auto texture by slope", true, v =>
        {
            if (v == Terrain.AutoTex(D.Map)) return;
            Edit(() => Terrain.Land(D.Map)["auto_tex"] = v);
            V3.Dirty(); LandRefresh();
            Say(v ? "Auto texture: flat = the ground, slopes = dirt, steep = rock. Painted places keep their paint" : "Auto texture off: the hills are all the GROUND texture, except where you paint", Theme.Good);
        });
        gr.Controls.Add(Look.Tip(_landAuto, "On: flat = the GROUND type, slopes = dirt, steep = rock (from the game's own pictures). Off: one texture, paint your own with the Paint tool"));
        var au = Group(low, "AUTO");
        au.Controls.Add(Look.Tip(Theme.Colored("⛰ Auto hills...", "#2f7a3a", LandAuto), "Builds hills all around the road (the road stays clear)"));
        au.Controls.Add(Look.Tip(Theme.Colored("Clear hills", "#7a2a3a", LandClear), "Removes all hills of this map"));
        low.Controls.Add(_landInfo);
        Common(top);
        p.Controls.Add(low); p.Controls.Add(top);
        return p;
    }

    void LandRefresh()
    {
        _landGround.SelectedIndex = Math.Max(0, Array.IndexOf(Terrain.Grounds, Terrain.Ground(D.Map)));
        if (_landAuto != null) _landAuto.Checked = Terrain.AutoTex(D.Map);
        var r = D.Road();
        if (r == null) { _landInfo.Text = ""; return; }
        try
        {
            var net = Network.Get(D.Map, r);
            int budget = Terrain.Budget(D.Map, r, net);
            var t = Terrain.Make(D.Map, net, budget);
            var hs = Terrain.Heights(D.Map);
            _landInfo.Text = hs.Count == 0 ? $"No hills yet.  Room for {budget} ground models on this map."
                : $"Hills: highest {hs.Values.Max():0} m, {t.Hills} model(s) on a {t.HillStep:0} m grid ({string.Join(", ", t.HillKeys.Select(k => Surface.Surfaces[k].Label.ToLowerInvariant()))})  ·  ramps {t.Ramps}, bumps {t.Bumps}  ·  {budget - t.Chunks.Count} model(s) left";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { _landInfo.Text = "Hills: " + e.Message; }
    }

    void LandClear()
    {
        if (!Terrain.HasLand(D.Map)) return;
        if (Msg("Clear hills", "Remove all hills of this map?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Edit(() => { Terrain.Land(D.Map).Obj("h")!.Clear(); Terrain.Land(D.Map).Remove("paint"); });
        V3.Dirty(); LandRefresh();
        Say("All hills removed from this map");
    }

    public Func<Terrain.AutoSet, bool>? TestLandAuto;

    void LandAuto()
    {
        var r = D.Road();
        if (r == null) return;
        var s = new Terrain.AutoSet();
        if (Terrain.Land(D.Map).Obj("auto") is { } a)
        {
            s.Height = a.Num("height"); s.Near = a.Num("near"); s.Band = a.Num("band"); s.Rough = a.Num("rough"); s.Seed = (int)a.Int("seed");
        }
        if (TestLandAuto != null) { if (!TestLandAuto(s)) return; }
        else if (!LandAutoWindow(s)) return;
        int n = 0;
        Edit(() => n = Terrain.Auto(D.Map, Network.Get(D.Map, r), s));
        LandRefresh();
        V3.Dirty(); G.Invalidate();
        Say($"Auto hills: {n} points made. Build + install to drive between them", Theme.Good);
    }

    bool LandAutoWindow(Terrain.AutoSet s)
    {
        using var w = new Form { Text = "Auto hills", BackColor = Theme.Bg, ForeColor = Theme.Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16), BackColor = Theme.Bg };
        p.Controls.Add(new Label { Text = "AUTO HILLS AROUND THE ROAD", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold });
        p.Controls.Add(new Label { Text = "Hills start a little away from the road, rise, and fade out further away.\nThe road itself always stays clear.", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 4, 0, 8) });
        var h = new Spin("height (m)", s.Height, 5, 2, Terrain.MaxHeight, "0");
        var near = new Spin("start from road (m)", s.Near, 5, 5, 300, "0");
        var band = new Spin("hill area wide (m)", s.Band, 20, 30, 1000, "0");
        var rough = new Spin("rough (0 - 1)", s.Rough, 0.1, 0, 1, "0.0");
        var seed = new Spin("shape number", s.Seed, 1, 1, 9999, "0");
        foreach (var c in new Control[] { h, near, band, rough, seed }) p.Controls.Add(c);
        var keep = Theme.Check("Keep the hills I made (add to them)", false, v => s.Keep = v); keep.ForeColor = Theme.Text; p.Controls.Add(keep);
        var foot = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, 10, 0, 0) };
        bool ok = false;
        var b = Theme.Btn("⛰ Build hills", () => { ok = true; w.Close(); }, Theme.Orange); b.Font = Theme.Bold; foot.Controls.Add(b);
        foot.Controls.Add(Theme.Btn("🎲 Other shape", () => seed.Value = seed.Value + 1));
        foot.Controls.Add(Theme.Btn("Cancel", () => w.Close()));
        p.Controls.Add(foot);
        w.Controls.Add(p);
        Look.Dress(w, "land");
        w.ShowDialog(this);
        if (!ok) return false;
        s.Height = h.Value; s.Near = near.Value; s.Band = band.Value; s.Rough = Math.Clamp(rough.Value, 0, 1); s.Seed = (int)seed.Value;
        return true;
    }
}

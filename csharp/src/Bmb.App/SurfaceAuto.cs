// Surface tab: Auto bumps - bumpy stretches at random places along the road (Terrain.AutoBumps), not the whole road.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm
{
    public Func<Terrain.BumpSet, bool>? TestAutoBumps;

    void SurfAutoBumps()
    {
        var r = D.Road();
        if (r == null) return;
        var s = new Terrain.BumpSet();
        var surf = D.Map.Obj("surface");
        if (surf?.Obj("auto_bumps") is { } a)
        {
            s.Height = a.Num("height"); s.Size = a.Num("size"); s.MaxLen = a.Num("max_len"); s.MaxGap = a.Num("max_gap"); s.Seed = (int)a.Int("seed"); s.Whole = a.Truthy("whole");
        }
        if (TestAutoBumps != null) { if (!TestAutoBumps(s)) return; }
        else if (!AutoBumpsWindow(s)) return;
        int n = 0;
        Edit(() =>
        {
            n = Terrain.AutoBumps(D.Map, Network.Get(D.Map, r), s);
            D.Map.Obj("surface")!["auto_bumps"] = new JObj { ["height"] = s.Height, ["size"] = s.Size, ["max_len"] = s.MaxLen, ["max_gap"] = s.MaxGap, ["seed"] = (long)s.Seed, ["whole"] = s.Whole };
        });
        SurfRefresh(); V3.Dirty(); G.Invalidate();
        Say(n == 0 ? "Auto bumps: the road is too short for these settings" : $"Auto bumps: {n} bumpy stretch(es) along the road. The orange marks show them; Build + install to drive them", n == 0 ? Theme.Bad : Theme.Good);
    }

    bool AutoBumpsWindow(Terrain.BumpSet s)
    {
        using var w = new Form { Text = "Auto bumps", BackColor = Theme.Bg, ForeColor = Theme.Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16), BackColor = Theme.Bg };
        p.Controls.Add(new Label { Text = "AUTO BUMPS ALONG THE ROAD", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold });
        p.Controls.Add(new Label { Text = "Short bumpy stretches at random places, with smooth road between them.\nThey never cover the whole road.", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 4, 0, 8) });
        var auto = new RadioButton { Text = "Auto: a few short, low bumpy bits", AutoSize = true, Checked = true, ForeColor = Theme.Text };
        var custom = new RadioButton { Text = "Custom: I set the numbers", AutoSize = true, ForeColor = Theme.Text };
        p.Controls.Add(auto); p.Controls.Add(custom);
        var h = new Spin("bump height (m)", s.Height, 0.05, 0.05, 2, "0.00");
        var size = new Spin("bump spacing (m)", s.Size, 0.5, 0.5, 20, "0.0");
        var len = new Spin("longest bumpy bit (m)", s.MaxLen, 5, 4, 400, "0");
        var gap = new Spin("most road between (m)", s.MaxGap, 10, 10, 3000, "0");
        var seed = new Spin("place number", s.Seed, 1, 1, 9999, "0");
        var whole = Theme.Check("Across the whole road (off = sometimes one side only)", s.Whole, v => s.Whole = v); whole.ForeColor = Theme.Text;
        var keep = Theme.Check("Keep the bumps I made (add to them)", false, v => s.Keep = v); keep.ForeColor = Theme.Text;
        var nums = new Control[] { h, size, len, gap, whole };
        foreach (var c in nums) p.Controls.Add(c);
        p.Controls.Add(seed); p.Controls.Add(keep);
        void Mode() { foreach (var c in nums) c.Enabled = custom.Checked; }
        auto.CheckedChanged += (_, _) => Mode(); Mode();
        var foot = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, 10, 0, 0) };
        bool ok = false;
        var b = Theme.Btn("⚡ Make bumps", () => { ok = true; w.Close(); }, Theme.Orange); b.Font = Theme.Bold; foot.Controls.Add(b);
        foot.Controls.Add(Theme.Btn("🎲 Other places", () => seed.Value = seed.Value + 1));
        foot.Controls.Add(Theme.Btn("Cancel", () => w.Close()));
        p.Controls.Add(foot);
        w.Controls.Add(p);
        Look.Dress(w, "surface");
        w.ShowDialog(this);
        if (!ok) return false;
        if (auto.Checked) { s.Height = 0.22; s.Size = 2.2; s.MaxLen = 22; s.MaxGap = 260; s.Whole = false; }
        else { s.Height = h.Value; s.Size = size.Value; s.MaxLen = len.Value; s.MaxGap = gap.Value; }
        s.Seed = (int)seed.Value;
        return true;
    }
}

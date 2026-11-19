using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Bmb.Core;

namespace Bmb.App;

/// "Game tweaks": settings that go into Blur REBORN's own packs at Build + install (normal Blur is not changed):
/// more AI racers and harder AI in LAN / private custom races, and your own power-up icons.
public sealed class TweaksPanel : Panel
{
    readonly MainForm _f;
    readonly Dictionary<string, PictureBox> _pics = new();
    readonly Dictionary<string, Label> _picNote = new();
    CheckBox _moreAi = null!, _harder = null!, _iconsNormal = null!, _aiMods = null!, _sharp = null!;

    public TweaksPanel(MainForm f)
    {
        _f = f; BackColor = Theme.Bg; AutoScroll = true; Padding = new Padding(24);
        var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Dock = DockStyle.Top };
        Controls.Add(col);
        col.Controls.Add(new Label { Text = "GAME TWEAKS", AutoSize = true, ForeColor = Theme.Head, Font = new Font("Segoe UI", 14f, FontStyle.Bold) });
        col.Controls.Add(Hint(PowerupIcons.OnHold ? "They go into Blur REBORN. Press Build + install to use them." : "They go into Blur REBORN; the power-up icons can go into normal Blur too (this PC only). Press Build + install to use them."));

        // AI racers
        col.Controls.Add(Head("🏁  AI racers  (LAN and private games)"));
        col.Controls.Add(Where("BLUR REBORN ONLY", Theme.Orange));
        _moreAi = Theme.Check("More AI racers: up to 19 AI with you alone (the game allows 4 per player)", false, v => Change(t => t["more_ai"] = v));
        col.Controls.Add(_moreAi);
        col.Controls.Add(Hint("In the game: custom race settings > Add AI (now up to 19). One player + 19 AI = 20 racers, 2 players + 18 AI ..."));
        _harder = Theme.Check("Harder AI: Hard is harder than the game's Hard (full speed, the hardest boss's driving skill)", false, v => Change(t => t["harder_ai"] = v));
        col.Controls.Add(_harder);
        col.Controls.Add(Hint("In the game: custom race settings > AI: Hard."));
        _aiMods = Theme.Check("AI use mods: every AI gets Front Runner, plus a random 2nd and 3rd slot mod", false, v => Change(t => t["ai_mods"] = v));
        col.Controls.Add(_aiMods);
        col.Controls.Add(Hint("In the game: custom race settings > Mods: On. This changes Blur REBORN.exe a little (turn it off to put it back)."));
        col.Controls.Add(Hint("Playing a private game with friends? They need Blur REBORN with the same tweaks too: every game drives some of the AI."));

        // graphics: the texture filter fix
        col.Controls.Add(Head("🖥  Graphics"));
        col.Controls.Add(Where("BLUR REBORN  +  NORMAL BLUR  (this PC only)", Theme.Good));
        _sharp = Theme.Check("Sharp textures far away (anisotropic filtering 16x): road lines and asphalt stay sharp in the distance", true, v =>
        {
            if (_loading) return;
            var t = GameTweaks.Load(); t["sharp_far"] = v; GameTweaks.Save(t);
            Setup.RefreshFilter();
            _f.Say(v ? "Sharp textures far away: on (from the next game start)" : "Sharp textures far away: off (from the next game start)", Theme.Good);
        });
        col.Controls.Add(_sharp);
        col.Controls.Add(Hint("Installs a small add-on into the game (amax\\dlls\\aniso_fix.dll); off removes it. Blur never turns this on, so roads blur into a smear far ahead. Only changes how your PC draws the game: nothing online. Close Blur before changing it."));

        // power-up icons (on hold: made but not shown)
        Control box = PowerupIcons.OnHold ? new Panel() : col;
        box.Controls.Add(Head("⚡  Power-up icons"));
        box.Controls.Add(Where("BLUR REBORN  +  NORMAL BLUR (if turned on below)", Theme.Good));
        box.Controls.Add(Hint("Your own picture for a power-up: in the menu, floating on the road and held behind your car."));
        box.Controls.Add(Hint("Symbol: your picture's shape on the game's card (a PNG with a see-through background looks best).  Whole icon: your picture fills the whole card (4:3) - another shape is made smaller to fit, never stretched or cut."));
        box.Controls.Add(Hint("Your picture becomes pure white, like the game's own icons: the game colours each power-up (Shunt red, Nitro green ...). Bolt (2 left) / (1 left): only the Bolt held behind the car."));
        _iconsNormal = Theme.Check("Also in normal Blur (Blur.exe): its icons change too - only on this PC, other players still see the game's icons", false, IconsNormal);
        box.Controls.Add(_iconsNormal);
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, BackColor = Theme.Bg, Margin = new Padding(0, 6, 0, 0) };
        foreach (var (key, name) in PowerupIcons.Kinds)
        {
            var pic = new PictureBox { Size = new Size(72, 54), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Panel2, Margin = new Padding(0, 4, 10, 4) };
            var note = new Label { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 22, 12, 0) };
            _pics[key] = pic; _picNote[key] = note;
            grid.Controls.Add(new Label { Text = name, AutoSize = true, ForeColor = Theme.Text, Font = Theme.Bold, Margin = new Padding(0, 22, 14, 0), MinimumSize = new Size(70, 0) });
            grid.Controls.Add(pic);
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, 14, 10, 0) };
            string k = key;
            row.Controls.Add(Theme.Btn("⇩ Symbol...", () => Import(k, false)));
            row.Controls.Add(Theme.Btn("⇩ Whole icon...", () => Import(k, true)));
            row.Controls.Add(Theme.Btn("Game's icon", () => Remove(k)));
            grid.Controls.Add(row);
            grid.Controls.Add(note);
        }
        box.Controls.Add(grid);
    }

    /// a small tag: where a tweak works
    static Label Where(string t, Color c) => new() { Text = t, AutoSize = true, ForeColor = c, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Margin = new Padding(2, 0, 0, 4) };

    void IconsNormal(bool on)
    {
        if (_loading) return;
        if (on && _f.Msg("Power-up icons in normal Blur", "Build + install will also change normal Blur's cache\\root.pak (only the power-up icons; your car skins too if that is on).\n\nYour original is kept in Blur Map Builder\\base and comes back when you turn this off and Build + install.\nOther players online still see the game's icons. If Steam repairs the game files, your icons are gone until the next Build + install.\n\nTurn it on?", MessageBoxButtons.YesNo) != DialogResult.Yes)
        { _loading = true; _iconsNormal.Checked = false; _loading = false; return; }
        Change(t => t["icons_normal"] = on);
    }

    static Label Head(string t) => new() { Text = t, AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(0, 22, 0, 2) };
    static Label Hint(string t) => new() { Text = t, AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 2, 0, 4) };

    bool _loading;
    void Change(Action<JObj> fn)
    {
        if (_loading) return;
        var t = GameTweaks.Load(); fn(t); GameTweaks.Save(t);
        _f.Say("Saved. Build + install to put it into Blur REBORN", Theme.Good);
    }

    public void Refresh2()
    {
        _loading = true;
        var t = GameTweaks.Load();
        _moreAi.Checked = t.Truthy("more_ai"); _harder.Checked = t.Truthy("harder_ai"); _iconsNormal.Checked = t.Truthy("icons_normal"); _aiMods.Checked = t.Truthy("ai_mods");
        _sharp.Checked = GameTweaks.SharpFar(t);
        _loading = false;
        foreach (var (key, _) in PowerupIcons.Kinds) ShowIcon(key);
    }

    void ShowIcon(string key)
    {
        var old = _pics[key].Image; _pics[key].Image = null; old?.Dispose();
        string? p = PowerupIcons.FileOf(key) ?? PowerupIcons.WholeOf(key);
        _picNote[key].ForeColor = ColorTranslator.FromHtml("#9aa8b8");
        if (p != null)
            try
            {
                // what the game shows: the picture's shape in pure white (the game colours it)
                bool sym = p == PowerupIcons.FileOf(key);
                _pics[key].Image = sym ? PowerupIcons.WhitePreview(p, 128, 128) : PowerupIcons.WhitePreview(p, 128, 96);
                string? warn = PowerupIcons.Warning(key);
                _picNote[key].Text = (sym ? "your symbol" : "your whole icon") + (warn != null ? "   " + warn : "");
                if (warn != null) _picNote[key].ForeColor = Theme.Orange;
                return;
            }
            catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException) { }
        _picNote[key].Text = "the game's icon";
    }

    void Import(string key, bool whole)
    {
        string? path = _f.PickFile("Picture for the " + key + " icon", "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*");
        if (path == null) return;
        try
        {
            using var fs = File.OpenRead(path); using var im = Image.FromStream(fs);
            using var bm = new Bitmap(im);
            if (whole) PowerupIcons.SetWhole(key, bm); else PowerupIcons.Set(key, bm);
            ShowIcon(key);
            _f.Say($"The {key} icon has your picture. Build + install to see it in Blur REBORN", Theme.Good);
        }
        catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException) { _f.Say("This picture can't be used: " + e.Message, Theme.Bad); }
    }

    void Remove(string key)
    {
        PowerupIcons.Clear(key);
        ShowIcon(key);
        _f.Say($"The {key} icon is the game's again (after Build + install)", Theme.Good);
    }
}

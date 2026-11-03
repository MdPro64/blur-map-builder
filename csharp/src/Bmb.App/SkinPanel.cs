// The Car skins tab: new body pictures for the game's Boss / Legend / livery / Rat cars, written straight into Blur REBORN's root.pak.
// Follows car/skin_tab.py without its Texmod part. Blur.exe and the cache\ packs are never touched.
using Bmb.Core;

namespace Bmb.App;

public sealed class SkinPanel : Panel
{
    readonly MainForm _f;
    readonly TreeView _tree = new IconTree { Dock = DockStyle.Fill, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10f), DrawMode = TreeViewDrawMode.OwnerDrawText, ItemHeight = 26, ShowLines = false, ShowRootLines = true, HideSelection = false, FullRowSelect = true, Indent = 14 };
    readonly HashSet<string> _open = new();
    readonly Label _title = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 13f, FontStyle.Bold), Margin = new Padding(4, 2, 4, 6), UseMnemonic = false };
    readonly ComboBox _pic = Theme.Combo(320);
    readonly CheckBox _uv, _on;
    readonly Label _state = new() { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(4, 6, 4, 4), UseMnemonic = false };
    readonly PictureBox _view = new() { Dock = DockStyle.Fill, BackColor = ColorTranslator.FromHtml("#283038"), SizeMode = PictureBoxSizeMode.Zoom };
    readonly FlowLayoutPanel _top = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(10, 8, 10, 4) };
    public List<SkinCar>? Cars;
    SkinCar? _car;
    int _job; bool _filling;
    public bool Busy;

    public SkinPanel(MainForm f)
    {
        _f = f; BackColor = Theme.Bg;
        _uv = Theme.Check("Show the UV lines", true, _ => Show2());
        _on = Theme.Check("Skin on", false, v => { if (!_filling) Turn(v); });
        var left = new Panel { Dock = DockStyle.Left, Width = 290, BackColor = Theme.Panel };
        left.Controls.Add(_tree);
        left.Controls.Add(new Label { Text = "GAME CARS WITH A BODY PICTURE", Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Head, Font = Theme.Bold, Padding = new Padding(8, 8, 0, 0) });
        _tree.BringToFront();
        _tree.DrawNode += DrawCar;
        _tree.AfterSelect += (_, e) => { if (_filling) return; if (e.Node?.Tag is int) Pick(); };
        _tree.NodeMouseClick += (_, e) => { if (e.Node.Tag is string && e.Button == MouseButtons.Left && e.X > e.Node.Bounds.Left - 20) { if (e.Node.IsExpanded) e.Node.Collapse(); else e.Node.Expand(); } };
        _tree.AfterExpand += (_, e) => { if (e.Node?.Tag is string g) _open.Add(g); };
        _tree.AfterCollapse += (_, e) => { if (e.Node?.Tag is string g) _open.Remove(g); };
        _tree.Resize += (_, _) => _tree.Invalidate();          // the on / off marks sit at the right edge: draw the rows again when it moves
        Look.DarkScroll(_tree);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0) };
        row.Controls.Add(new Label { Text = "Picture", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(4, 7, 4, 0) });
        row.Controls.Add(_pic); row.Controls.Add(_uv);
        _pic.SelectionChangeCommitted += (_, _) => Show2();
        var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0, 6, 0, 0) };
        row2.Controls.Add(Theme.Btn("⇧ Export picture...", ExportPicture));
        row2.Controls.Add(Theme.Btn("⇧ Export UV map...", ExportUv));
        row2.Controls.Add(Theme.Btn("⇩ Load my skin...", LoadSkin, Theme.Blue));
        row2.Controls.Add(Theme.Btn("Use the game's picture", RemoveSkin));
        _on.Margin = new Padding(12, 6, 2, 0); row2.Controls.Add(_on);
        _top.Controls.Add(_title); _top.Controls.Add(row); _top.Controls.Add(row2); _top.Controls.Add(_state);
        _top.Resize += (_, _) => { row.MaximumSize = row2.MaximumSize = new Size(Math.Max(300, _top.ClientSize.Width - 24), 0); _state.MaximumSize = new Size(Math.Max(300, _top.ClientSize.Width - 30), 0); };
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(10, 0, 10, 10) };
        right.Controls.Add(_view); right.Controls.Add(_top);
        var ls = f.PanelSash(DockStyle.Left, "skin_list", left, 290);
        Controls.Add(right); Controls.Add(ls); Controls.Add(left);
    }

    // ---- the list
    public void Refresh2()
    {
        if (Cars != null) { Fill(); return; }
        if (Busy) return;
        Busy = true;
        _f.Say("CAR SKINS: looking for the game's cars with a body picture (once)...");
        Task.Run(() => { try { return (Skins.Cars(), (string?)null); } catch (Exception e) { return (new List<SkinCar>(), e.Message); } }).ContinueWith(t =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                Busy = false;
                Cars = t.Result.Item1;
                if (t.Result.Item2 != null) _f.Say("Car skins: the game's cars can't be read: " + t.Result.Item2, Theme.Bad);
                else _f.Say($"CAR SKINS: {Cars.Count} game cars can get a skin. Pick one, export its picture, paint it, load it, then Build skins");
                Fill();
            });
        });
    }

    /// the game's class of each car (its model is named after the game car), "Other" when the game has none
    Dictionary<string, string>? _classOf;
    string ClassOf(string car)
    {
        if (_classOf == null)
        {
            _classOf = new();
            try { foreach (var g in CarsData.Cars()) _classOf[g.Name.ToLowerInvariant()] = "Class " + CarUi.Classes[Math.Clamp(g.Class, 0, 3)]; } catch (Exception) { }
        }
        return _classOf.GetValueOrDefault(car.ToLowerInvariant(), "Other");
    }

    void Fill()
    {
        if (Cars == null) return;
        _filling = true;
        int sel = _tree.SelectedNode?.Tag is int i ? i : _car != null ? Cars.IndexOf(_car) : 0;
        if (_open.Count == 0 && Cars.Count > 0) _open.Add(ClassOf(Cars[Math.Max(0, sel)].Car));
        _tree.BeginUpdate(); _tree.Nodes.Clear();
        TreeNode? pick = null;
        foreach (var grp in Cars.Select((c, k) => (c, k)).GroupBy(q => ClassOf(q.c.Car)).OrderBy(g => g.Key == "Other" ? "Z" : g.Key))
        {
            var gn = _tree.Nodes.Add(grp.Key, $"{grp.Key}  ({grp.Count()})"); gn.Tag = grp.Key;
            foreach (var (c, k) in grp.OrderBy(q => Skins.Nice(q.c.Car)))
            {
                var n = gn.Nodes.Add(c.Car, Skins.Nice(c.Car)); n.Tag = k;
                if (k == sel) pick = n;
            }
            if (_open.Contains(grp.Key)) gn.Expand();
        }
        _tree.EndUpdate();
        if (pick != null) { pick.EnsureVisible(); _tree.SelectedNode = pick; }
        _filling = false;
        Pick();
    }

    void DrawCar(object? s, DrawTreeNodeEventArgs e)
    {
        var n = e.Node;
        if (n == null || e.Bounds.Height == 0 || Cars == null) return;
        int W = _tree.ClientSize.Width, x = e.Bounds.X, y = e.Bounds.Y, h = e.Bounds.Height;
        var g = e.Graphics;
        bool sel = n == _tree.SelectedNode && n.Tag is int;
        using (var back = new SolidBrush(Theme.Field)) g.FillRectangle(back, n.Tag is int ? 0 : x, y, W, h);      // a car row: from the left edge (no grey box beside it)
        if (n.Tag is string grp)
        {
            int skins = n.Nodes.Cast<TreeNode>().Count(q => q.Tag is int k && Skins.All().Obj(Cars[k].Car)?.Values.OfType<JObj>().Any(v => v.Truthy("on")) == true);
            TextRenderer.DrawText(g, Lang.T(grp) + $"  ({n.Nodes.Count})", Theme.Bold, new Rectangle(x + 2, y, W - x - 70, h), Theme.Head, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (skins > 0) TextRenderer.DrawText(g, Lang.T($"{skins} on"), Theme.Ui, new Rectangle(W - 72, y, 66, h), Theme.Good, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            return;
        }
        var c = Cars[(int)n.Tag!];
        if (sel)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var p = Look.RoundPath(new RectangleF(x, y + 1, W - x - 4, h - 2), 5);
            using var b = new SolidBrush(Theme.Blue); g.FillPath(b, p);
        }
        var mine = Skins.All().Obj(c.Car);
        string mark = mine == null ? "" : mine.Values.OfType<JObj>().Any(q => q.Truthy("on")) ? Lang.T("● on") : Lang.T("○ off");
        TextRenderer.DrawText(g, Skins.Nice(c.Car), _tree.Font, new Rectangle(x + 6, y, W - x - (mark != "" ? 76 : 12), h), sel ? Color.White : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (mark != "") TextRenderer.DrawText(g, mark, Theme.Ui, new Rectangle(W - 72, y, 66, h), sel ? Color.White : mark.StartsWith("●") ? Theme.Good : Theme.Warn, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    void Pick()
    {
        if (Cars == null || _tree.SelectedNode?.Tag is not int k) return;
        var c = Cars[k];
        if (c != _car)
        {
            _car = c;
            _pic.Items.Clear();
            foreach (var p in c.Pictures) _pic.Items.Add(p);
            var mine = c.Pictures.FirstOrDefault(p => Skins.Has(c.Car, p));
            _pic.SelectedItem = mine ?? c.Body;
        }
        _title.Text = Skins.Nice(c.Car);
        Show2();
    }

    string? Pic => _pic.SelectedItem as string;
    public string? PicNow => Pic;
    public void Pick(int i)
    {
        foreach (TreeNode gn in _tree.Nodes) foreach (TreeNode n in gn.Nodes) if (n.Tag is int k && k == i) { gn.Expand(); _tree.SelectedNode = n; return; }
    }
    public int Groups => _tree.Nodes.Count;
    public int OpenGroups => _tree.Nodes.Cast<TreeNode>().Count(n => n.IsExpanded);
    public void ToggleGroup(int g) { var n = _tree.Nodes[g]; if (n.IsExpanded) n.Collapse(); else n.Expand(); }
    /// the robot presses a button
    public void Run(string what) { if (what == "export") ExportPicture(); else if (what == "uv") ExportUv(); else if (what == "load") LoadSkin(); }

    /// the skin (or the game's picture) with the UV lines, made in the background
    void Show2()
    {
        if (_car == null || Pic == null) return;
        string car = _car.Car, pic = Pic; bool uv = _uv.Checked, has = Skins.Has(car, pic);
        _filling = true; _on.Checked = Skins.IsOn(car, pic); _on.Enabled = has; _filling = false;
        int job = ++_job;
        _state.Text = Lang.T("Loading the picture...");
        Task.Run(() =>
        {
            try
            {
                var game = Skins.Picture(car, pic);
                var img = has && File.Exists(Skins.SkinFile(car, pic)) ? Bmb.Core.Rgba.Load(Skins.SkinFile(car, pic)) : game;
                if (img.W > 1024) img = img.Resize(1024, Math.Max(1, img.H * 1024 / img.W));
                if (uv) img = Skins.UvMap(car, pic, img.W, img.H, img);
                return (img.ToBitmap(), Lang.T($"{(has ? "YOUR SKIN" : "the game's own picture")} - {pic}, game picture {game.W} x {game.H}"));
            }
            catch (Exception e) { return ((Bitmap?)null, Lang.T("This picture can't be shown: " + e.Message)); }
        }).ContinueWith(t =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (job != _job) { t.Result.Item1?.Dispose(); return; }
                var old = _view.Image; _view.Image = t.Result.Item1; old?.Dispose();
                _state.Text = t.Result.Item2;
                Shown?.Invoke();
            });
        });
    }
    public Action? Shown;

    // ---- the buttons
    void ExportPicture()
    {
        if (_car == null || Pic == null) return;
        var path = _f.SaveAs("Export the game's picture", "PNG picture|*.png", $"{_car.Car}_{Pic}.png");
        if (path == null) return;
        Skins.Picture(_car.Car, Pic).Save(path);
        _f.Say("Saved the game's picture: " + path, Theme.Good);
    }

    void ExportUv()
    {
        if (_car == null || Pic == null) return;
        var path = _f.SaveAs("Export the UV map", "PNG picture|*.png", $"{_car.Car}_{Pic}_uv.png");
        if (path == null) return;
        Skins.UvMap(_car.Car, Pic).Save(path);
        _f.Say("Saved the UV map (lines on a clear picture - put it as the top layer): " + path, Theme.Good);
    }

    void LoadSkin()
    {
        if (_car == null || Pic == null) return;
        var path = _f.PickFile("Your skin", "Pictures|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*");
        if (path == null) return;
        try { Skins.Set(_car.Car, Pic, path); }
        catch (Exception e) { _f.Say("This picture can't be used: " + e.Message, Theme.Bad); return; }
        _tree.Invalidate(); Show2();
        _f.Say($"Skin loaded for {Skins.Nice(_car.Car)}. Build skins puts it in the game", Theme.Good);
    }

    void RemoveSkin()
    {
        if (_car == null || Pic == null || !Skins.Has(_car.Car, Pic)) return;
        if (_f.Msg("Car skins", $"Forget your skin for {Skins.Nice(_car.Car)} ({Pic})?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Skins.Remove(_car.Car, Pic);
        _tree.Invalidate(); Show2();
        _f.Say("Back to the game's picture. Build skins again to take it out of the game");
    }

    void Turn(bool on)
    {
        if (_car == null || Pic == null) return;
        Skins.Turn(_car.Car, Pic, on);
        _tree.Invalidate();
        _f.Say(on ? "Skin on. Build skins again for the game" : "Skin off. Build skins again for the game");
    }

    /// writes every skin that is on into Blur REBORN's root.pak (and the game's picture back where a skin was turned off)
    public async Task<string> BuildSkins()
    {
        if (Busy) return "";
        if (Play.GameRunning()) { _f.Say("Blur is running: close it first, then Build skins", Theme.Bad); return "running"; }
        Busy = true;
        _f.Say("Building skins into Blur REBORN...");
        var lines = new List<string>();
        string msg; Color col;
        try
        {
            var (on, back) = await Task.Run(() => Skins.Install(s => { lock (lines) lines.Add(s); }));
            msg = on == 0 && back == 0 ? Lang.T("No skin is on: nothing to change") : Lang.T($"Skins built: {on} car(s) with your skin, {back} back to the game's picture. Start Blur REBORN to see them");
            col = Theme.Good;
        }
        catch (Exception e) { msg = Lang.T("Build skins failed: " + e.Message); col = Theme.Bad; }
        Busy = false;
        _f.Say(msg, col);
        _tree.Invalidate();
        return msg;
    }

    public void SetNormal(bool on)
    {
        if (on && _f.Msg("Car skins in normal Blur", "Build skins will also change normal Blur's cache\\root.pak (only the cars with a skin).\n\nYour original is kept in Blur Map Builder\\base and comes back when you turn this off and Build skins.\nOther players online still see the normal car. If Steam repairs the game files, the skins are gone until the next Build skins.\n\nTurn it on?", MessageBoxButtons.YesNo) != DialogResult.Yes) { _f.SkinNormalShow(false); return; }
        Skins.Normal = on;
        _f.Say(on ? "Normal Blur gets the skins too at the next Build skins" : "Only Blur REBORN gets the skins. Build skins puts normal Blur's own pack back", Theme.Warn);
    }

    public async Task UninstallAll()
    {
        if (_f.Msg("Car skins", "Turn every skin off and put the game's own pictures back (Blur REBORN, and normal Blur if it has them)?\n\nYour skin pictures are kept: tick 'Skin on' to use one again.", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Skins.TurnAllOff();
        await BuildSkins();
        Show2();
    }
}

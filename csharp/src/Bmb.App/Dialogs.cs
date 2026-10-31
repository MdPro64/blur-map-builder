// Help, Build window, Play, updates, the start of the program. Follows road_designer.py (help, build, play_dialog ...), update_window.py, bmb_main.py.
using System.Diagnostics;
using Bmb.Core;

namespace Bmb.App;

public sealed partial class MainForm
{
    const string HelpText = @"MOUSE
  wheel: zoom      middle-drag or right-drag on empty ground: move the map
TOOLS
  S  Select: click a point / power-up to edit it on the right
  M  Move: drag points and power-ups
  A  Add point at the END of the road        I  Insert a point into the road
  R  Power-up ROW (5 across the road)        P  Single power-up (type = the chosen one)
  E  Erase: click a point or power-up to delete it
  B  BRANCH: click ON the road where it leaves, click its points beside the road, click ON the road where it
     joins again (Esc = cancel). The AI takes it too. Move tool: drag its points / its round handles
  L  LANE SPLIT: click ON the road where it starts, then where it ends: a wall splits the road into 2 lanes
     (3 lanes: on the right). Drag its round handles to move the ends.  Select a branch / split + Delete = remove
  1-8  power-up type: 1 Shunt 2 Barge 3 Shock 4 Mine 5 Bolt 6 Nitro 7 Shield 8 Repair (also changes the selected one)
SELECTED POINT
  W / Q  road wider / narrower here      PgUp / PgDn  higher / lower (bridge, ramp)
  , / .  tilt the road here (one side up, the other down)   Auto tilt on the right = a good tilt for the bend
         Orange edge = goes up, blue edge = goes down. It stays flat at the start, finish, bridges, branches, lane splits
  C  corner on/off (sharp turn)          T / Y  90-degree turn LEFT / RIGHT here
     (a corner is never tighter than half the road + 10 m: the inside wall would fold)
  arrows  nudge 5 m (Shift: 1 m)         Delete  delete      Esc  deselect
ROAD
  V  reverse the direction    X  mirror    G  snap to a 10 m grid    F  fit the view
  Auto bridges (right panel): where the road crosses itself the later part rises onto a 9 m deck
WORLD
  Ctrl+N new map    Ctrl+L new location    Ctrl+D duplicate map    F2 rename
  Ctrl+Z undo    Ctrl+Y (or Ctrl+Shift+Z) redo   (30 steps)
  Ctrl+S save    Ctrl+B build + install    F1 / H this help
VIEW
  F3 (or the 2D / 3D / Collision buttons at the top left)  switch views
  3D view (Blender keys): middle-drag turn, Shift+middle-drag move, Ctrl+middle-drag or wheel zoom,
     Alt+left-drag = middle-drag, numpad 7 top / 1 front / 3 side (Ctrl = opposite), 5 flat, 2 4 6 8 turn, Home all
  Collision view: green = drivable ground, red = solid (white ticks = the side that pushes cars away),
     yellow = bridge decks
  The 3D view shows what the build will place (same layout code); it updates after every change
  AI PATH tab: the yellow line = where the AI cars drive, the band = their lane.  ""Auto AI path"" = made for you
     (the whole road; this is also what you get if you never open the tab).  ""Racing line"" = cuts the inside of the
     bends.  ""Pen"" on: drag along the road to draw the line yourself - drawing over a part replaces only that part.
     Pen off: drag moves the map.  AI lane width = how wide the AI's lane is around the line.
  3D OBJECTS: click = select (yellow box + handles, buttons Move / Rotate / Scale / Hide / Reset on top).
     Drag a handle: red = X, green = Y, blue = up; white dot = move on the ground / scale everything;
     rings = rotate (red X / green Y tilt, blue Z turn).  Scale handles stretch the object along its own axes.
     Panel at the bottom left: location / rotation / scale - type a value + Enter.  Orange dots = the road's
     points: click one, drag its arrows (blue = height: a ramp / bridge), or type x / y / height / width / tilt.
     R (or Rotate) on a road point: drag the ring to tilt the road there.
     Keys: G move, R turn (Ctrl = 15 degree steps), S scale (ships, cranes, panels...), X or Delete hide.
     While moving: X / Y = only along one ground axis, Z = up/down.
     Click / Enter = OK, right-click / Esc = cancel.  Alt+G / Alt+R / Alt+S = back to how it was placed,
     Alt+H = show hidden objects again.  Ctrl+D = copy the selected placed object (a bit to the side).
     Several: Shift / Ctrl+click in OBJECTS IN THIS MAP (Shift+click in the view) - Delete deletes them all,
     Ctrl+G groups them (Ctrl+Shift+G ungroups), click a group's line = all of it.  Path scatter makes one group.
     Ctrl+Z undoes.  Save + Build puts the changes in the game.
     PATH SCATTER (button in the object library): draw a path on the ground, pick a few library objects, set the
     spacing / sides / random turn, size and spread - a preview shows where they go - then Add them all at once.";

    static string KeysText()
    {
        if (!Lang.Ru) return HelpText;
        using var r = new StreamReader(typeof(Lang).Assembly.GetManifestResourceStream("ru_keys.txt")!);
        return r.ReadToEnd();
    }

    /// the short help of the tab that is open; its All keys button opens the long list
    void Help() { HelpCard.Show(this, Mode); LastPreview = HelpCard.Last; }

    public void FullHelp()
    {
        var f = new Form { Text = "Keys / About", BackColor = Theme.Panel, ForeColor = Theme.Text, Width = 960, Height = 760, StartPosition = FormStartPosition.CenterParent, KeyPreview = true };
        Look.Dress(f);
        var t = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10f), Text = KeysText().Replace("\r\n", "\n").Replace("\n", "\r\n") };
        var c = new Label { Dock = DockStyle.Bottom, Height = 84, ForeColor = ColorTranslator.FromHtml("#7fd0ff"), Padding = new Padding(8), Text = $"BLUR MAP BUILDER  by MDPRO   -   version {Paths.Version}   (bugs / crash reports: {UiText.Discord} on Discord)\nBlur (c) 2010 Bizarre Creations / Activision - this is a free fan tool, it needs your own copy of the game.\nAmax by the Amax team.   Model layout: BlurImportExport by TimberStalker (github.com/TimberStalker).\nMade with C# and .NET." };
        var b = Theme.Btn("Close", f.Close); b.Dock = DockStyle.Bottom;
        f.Controls.Add(t); f.Controls.Add(c); f.Controls.Add(b);
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        f.Show(this);
        LastPreview = f;
    }

    void Startup()
    {
        if (_test) return;
        WriteQuickstart(false);
        CheckUpdates(true);
    }

    // ---- updates: the newest GitHub release (updater.py + update_window.py)
    (string Tag, string Url, string Notes, Dictionary<string, string> Files)? _updateInfo;

    public async void CheckUpdates(bool auto)
    {
        if (_updateInfo != null) { ShowUpdate(); return; }
        _update.Text = "⟳ Checking...";
        var info = await Updates.Latest();
        if (IsDisposed) return;
        if (info == null)
        {
            _update.Text = "⚠ No connection - check again";
            if (!auto) Say("Could not reach GitHub - check your internet connection and try again", Theme.Warn);
        }
        else if (Updates.Newer(info.Value.Tag))
        {
            _updateInfo = info;
            _update.Text = "⬇ Update " + info.Value.Tag; _update.BackColor = Theme.Orange;
            ShowUpdate();
        }
        else
        {
            _update.Text = $"✔ Up to date ({Paths.Version})"; _update.BackColor = ColorTranslator.FromHtml("#7ee08a"); _update.ForeColor = ColorTranslator.FromHtml("#0a0f18");
            if (!auto) Say($"You have the newest version ({Paths.Version})", Theme.Good);
        }
    }

    void ShowUpdate()
    {
        if (_updateInfo == null) return;
        var (tag, url, notes, files) = _updateInfo.Value;
        var f = new Form { Text = "New version", BackColor = Theme.Bg, ForeColor = Theme.Text, Width = 620, Height = 420, StartPosition = FormStartPosition.CenterParent };
        Look.Dress(f, "update");
        var t = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Text = (notes == "" ? "(no notes)" : notes).Replace("\n", "\r\n") };
        var h = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 6, 6, 0), Font = Theme.Bold, ForeColor = Theme.Text, Text = $"Blur Map Builder {tag} is out (you have {Paths.Version}).\nYour maps are kept. The map builder restarts by itself when the update is done." };
        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = Theme.Bg, Padding = new Padding(8) };
        var bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 14, Maximum = 1000, Visible = false };
        var step = new Label { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(10, 4, 6, 0), ForeColor = Theme.Text, Visible = false, UseMnemonic = false };
        bool busy = false;
        var go = Theme.Btn("⬇ Update now", async () =>
        {
            busy = true; foot.Visible = false; step.Visible = bar.Visible = true; step.Text = "Connecting to GitHub...";
            string? err = await Updates.Install(files, (name, got, total) => BeginInvoke(() =>
            {
                if (f.IsDisposed) return;
                step.Text = total > 0 ? $"{name}: {got >> 20} MB of {total >> 20} MB" : $"{name}: {got >> 20} MB";
                if (total > 0) bar.Value = (int)(got * 1000 / total);
            }));
            busy = false;
            if (err != null) { step.Text = err; step.ForeColor = Theme.Bad; step.Height = 60; bar.Visible = false; foot.Visible = true; return; }
            step.Text = "Update installed - restarting the map builder..."; step.ForeColor = Theme.Good;
            _restart = true;
            f.Close(); Close();
        }, Theme.Orange);
        go.Font = Theme.Bold; go.Enabled = files.Count > 0;
        foot.Controls.Add(go);
        foot.Controls.Add(Theme.Btn("Later", f.Close));
        foot.Controls.Add(Theme.Btn("Open the release page", () => { if (url != "") Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }));
        f.FormClosing += (_, e) => e.Cancel = busy;
        f.Controls.Add(t); f.Controls.Add(h); f.Controls.Add(step); f.Controls.Add(bar); f.Controls.Add(foot);
        f.Show(this);
    }

    void UpdateSource()
    {
        string? r = Ask("Update source", "The GitHub page updates come from (owner/name or a link).\nEmpty = back to MDPRO's.", Updates.Repo());
        if (r == null) return;
        if (r.Trim() != "" && Updates.ParseRepo(r) == null) { Say("That is not a GitHub page (owner/name)", Theme.Bad); return; }
        _updateInfo = null;
        Say("Updates now come from github.com/" + Updates.SetRepo(r.Trim() == "" ? null : r), Theme.Good);
        CheckUpdates(false);
    }

    // ---- build
    static string BuildTimeFile => Path.Combine(Paths.Tracks, "last_build_seconds.txt");
    static string BuildLog => Path.Combine(Paths.Logs, "last_build.txt");
    static readonly (string Key, int Pct, string? Label)[] Stages =
    {
        ("start line at", 5, "Preparing the race lines"), ("building actor lists", 8, "Placing objects"), ("building game database", 12, "Building the game database"), ("database:", 50, "Game database done"),
        ("writing the pack", 55, "Writing gamedata.pak"), ("built ", 66, "gamedata.pak written"), ("seamless road", 72, "Making the road models"), ("root pack:", 92, "root.pak written (models, pictures, minimaps)"),
        ("installing the packs", 96, "Installing into the game"), ("pack: ", 0, null),
    };
    public bool Building;
    public Action<string>? BuildFinished;

    public void Build()
    {
        if (Building) { Say("A build is already running", Theme.Bad); return; }
        var bad = new List<string>();
        foreach (var loc in D.World.Objs("locations"))
            foreach (var m in loc.Objs("maps"))
                try
                {
                    var r = Road.ForMap(m);
                    if (r.UnbridgedCrossings().Count > 0) bad.Add($"{loc.Str("name")} / {m.Str("name")}: crossing without a bridge");
                    if (r.Length < 900 || r.Length > 5300) bad.Add($"{loc.Str("name")} / {m.Str("name")}: length {r.Length:0} m");
                }
                catch (Exception e) { bad.Add($"{loc.Str("name")} / {m.Str("name")}: {e.Message}"); }
        if (bad.Count > 0 && !_test && MessageBox.Show(this, string.Join("\n", bad) + "\n\nBuild anyway?", "Problems", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        DoSave();
        Building = true; _build.Enabled = false; _build.Text = "Building...";
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var opt = new BuildOptions { Log = lines.Enqueue };
        var log = new Form { Text = "Building", Width = 820, Height = 480, BackColor = Theme.Panel, StartPosition = FormStartPosition.CenterParent };
        Look.Dress(log, "build");
        var stage = new Label { Text = "Starting the build...", Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        var pctL = new Label { Text = "0 %", Dock = DockStyle.Right, Width = 90, ForeColor = Theme.Warn, Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
        var head = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 4, 40, 0) }; head.Controls.Add(stage); head.Controls.Add(pctL);
        var bar = new ProgressBar { Dock = DockStyle.Top, Height = 16, Maximum = 100 };
        var box = new RichTextBox { ReadOnly = true, Dock = DockStyle.Fill, BackColor = ColorTranslator.FromHtml("#0a1424"), ForeColor = ColorTranslator.FromHtml("#4fc3ff"), ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9), DetectUrls = false };
        log.Controls.Add(box); log.Controls.Add(bar); log.Controls.Add(head);
        log.Show(this);
        double expected = 15; try { expected = Math.Max(5, double.Parse(File.ReadAllText(BuildTimeFile), System.Globalization.CultureInfo.InvariantCulture)); } catch (Exception) { }
        int maps = D.TotalMaps, mapsDone = 0; double stagePct = 0;
        var sw = Stopwatch.StartNew();
        var all = new List<string>();
        void Show(string s)
        {
            all.Add(s);
            if (log.IsDisposed) return;
            bool err = s.Contains("error", StringComparison.OrdinalIgnoreCase) || s.Contains("failed", StringComparison.OrdinalIgnoreCase);
            box.SelectionStart = box.TextLength; box.SelectionColor = err ? ColorTranslator.FromHtml("#ff6b6b") : ColorTranslator.FromHtml("#4fc3ff");
            box.AppendText(s + "\n"); box.ScrollToCaret();
            string low = s.ToLowerInvariant();
            if (low.Contains(": road ") && low.Contains(" m (")) { mapsDone++; stagePct = Math.Max(stagePct, 12 + 36.0 * mapsDone / Math.Max(1, maps)); stage.Text = $"Level files: map {mapsDone} of {maps}"; return; }
            if (low.Contains("object light")) { stage.Text = "Lighting: light points along the roads"; return; }
            if (low.Contains("collision floor")) { stage.Text = "Collision floor (walls, bridges)"; return; }
            foreach (var (key, pct, label) in Stages)
                if (low.Contains(key)) { if (pct > 0) stagePct = Math.Max(stagePct, pct); if (label != null) stage.Text = label; break; }
        }
        var timer = new System.Windows.Forms.Timer { Interval = 80 };
        timer.Tick += (_, _) =>
        {
            while (lines.TryDequeue(out var s)) Show(s);
            if (log.IsDisposed) return;
            double pct = Math.Max(stagePct, Math.Min(95.0, 95.0 * sw.Elapsed.TotalSeconds / expected));
            bar.Value = (int)pct; pctL.Text = $"{(int)pct} %";
        };
        timer.Start();
        Task.Run(() =>
        {
            Exception? err = null;
            try
            {
                if (Play.GameRunning()) { lines.Enqueue("Blur is running: the level files can't be replaced. Close Blur and build again."); throw new InvalidOperationException("close Blur first"); }
                Bmb.Core.Build.Run(opt);
                double took = sw.Elapsed.TotalSeconds;
                try { File.WriteAllText(BuildTimeFile, took.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)); } catch (IOException) { }
                lines.Enqueue("Installing the packs for Blur REBORN (rebrn\\)...");
                Directory.CreateDirectory(Paths.PackDir);
                foreach (var (src, name) in new[] { (Paths.GamedataOut, "gamedata.pak"), (Paths.RootOut, "root.pak") })
                {
                    string dst = Path.Combine(Paths.PackDir, name);
                    File.Copy(src, dst + ".new", true);
                    File.Move(dst + ".new", dst, true);
                }
            }
            catch (Exception e) { err = e; lines.Enqueue(e.ToString()); }
            BeginInvoke(() =>
            {
                timer.Stop();
                while (lines.TryDequeue(out var s)) Show(s);
                try { Directory.CreateDirectory(Paths.Logs); File.WriteAllLines(BuildLog, all); } catch (Exception) { }
                Building = false; _build.Enabled = true; FitTop();
                string msg = err == null ? $"Built and installed in {sw.Elapsed.TotalSeconds:0.0} s." : err.Message;
                Say(msg, err == null ? Theme.Good : Theme.Bad);
                if (!log.IsDisposed) { bar.Value = 100; pctL.Text = err == null ? "100 %" : "FAILED"; pctL.ForeColor = err == null ? Theme.Good : Theme.Bad; stage.Text = err == null ? "Built and installed" : "The build failed (see below)"; }
                if (!_test) System.Media.SystemSounds.Asterisk.Play();
                BuildFinished?.Invoke(msg);
                if (err == null)
                {
                    WriteQuickstart(false);
                    var close = new System.Windows.Forms.Timer { Interval = 1200 };
                    close.Tick += (_, _) => { close.Stop(); if (!log.IsDisposed) log.Close(); };
                    close.Start();
                    if (!_test) PlayDialog(msg);
                }
                else if (!_test) MessageBox.Show(this, $"{msg}\n\nThe full log is in the Building window and in {BuildLog}", "Build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        });
    }

    // ---- play
    bool WriteQuickstart(bool on)
    {
        string? e = Play.WriteQuickstart(D.World, D.Loc, D.Map, on);
        if (e != null) Say("could not write the QuickStart target: " + e, Theme.Bad);
        return e == null;
    }

    public void StartGame(bool lan)
    {
        if (Play.GameRunning()) { Msg("Blur is running", "Blur is already running - close it first, then press Play again."); return; }
        if (!File.Exists(Play.RebornExe)) { Msg("Blur REBORN", "Blur REBORN.exe is not there yet - start the map builder again (its first-start setup makes it)."); return; }
        if (!File.Exists(Path.Combine(Paths.PackDir, "root.pak"))) { Msg("Build first", "Press Build + install once first: Blur REBORN plays the built maps."); return; }
        WriteQuickstart(lan);
        Process.Start(new ProcessStartInfo(Play.RebornExe) { WorkingDirectory = Paths.Game, UseShellExecute = false });
        if (lan)
        {
            Say($"Starting Blur: Multiplayer > LAN > Host game (Y) - the race is set to {D.Loc.Str("name")} / {D.Map.Str("name")}", Theme.Good);
            QuickstartOffLater();
        }
    }

    /// the custom LAN race is ONE launch only: when the game closes the target goes back to off
    void QuickstartOffLater()
    {
        bool seen = false; int waited = 0;
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (_, _) =>
        {
            waited += 3;
            bool running = Play.GameRunning();
            seen |= running;
            if ((seen && !running) || (!seen && waited > 600)) { t.Stop(); WriteQuickstart(false); }
        };
        t.Start();
    }

    void PlayDialog(string msg = "")
    {
        var f = new Form { Text = "Play", BackColor = Theme.Panel, ForeColor = Theme.Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, KeyPreview = true };
        Look.Dress(f, "play");
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 14, 40, 14), BackColor = Theme.Panel };
        if (msg != "") p.Controls.Add(new Label { Text = msg, AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Theme.Good });
        p.Controls.Add(new Label { Text = "Start the game?", AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Margin = new Padding(0, 4, 0, 6) });
        void Go(bool? lan) { f.Close(); if (lan != null) StartGame(lan.Value); }
        var a = Theme.Btn($"▶  Custom LAN race on  {D.Loc.Str("name")}  >  {D.Map.Str("name")}", () => Go(true), Theme.Orange); a.Font = Theme.Bold; p.Controls.Add(a);
        p.Controls.Add(new Label { Text = "   in game: Multiplayer > LAN > Host game (Y): the lobby is already on this map", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8") });
        p.Controls.Add(Theme.Btn("▶  Start the game normally", () => Go(false)));
        p.Controls.Add(Theme.Btn("Not now", () => Go(null)));
        f.Controls.Add(p);
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        LastPreview = f;
        if (_test) f.Show(this); else f.ShowDialog(this);
    }
}

/// the first start's window (bmb_main.run_setup)
public static class SetupWindow
{
    public static bool Run()
    {
        if (!Setup.Needed()) return true;
        var f = new Form { Text = "Blur Map Builder - first start", Width = 540, Height = 210, BackColor = ColorTranslator.FromHtml("#0b1426"), ForeColor = Color.White, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        Look.Dress(f, "setup");
        var t = new Label { Text = "Setting up the map builder from YOUR game files (only the first time)", AutoSize = true, Left = 18, Top = 12, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
        var t2 = new Label { Text = "Blur.exe and the game's own files are not changed. Takes a few minutes.", AutoSize = true, Left = 18, Top = 38, ForeColor = ColorTranslator.FromHtml("#9aa8b8") };
        var bar = new ProgressBar { Left = 18, Top = 64, Width = 490, Height = 18, Maximum = 100 };
        var step = new Label { Left = 18, Top = 90, Width = 490, Height = 20, ForeColor = ColorTranslator.FromHtml("#7fd0ff") };
        var line = new Label { Left = 18, Top = 112, Width = 490, Height = 40, ForeColor = ColorTranslator.FromHtml("#9aa8b8") };
        f.Controls.AddRange(new Control[] { t, t2, bar, step, line });
        string? error = null;
        f.Shown += async (_, _) =>
        {
            try
            {
                await Task.Run(() => Setup.RunAll(m => f.BeginInvoke(() => line.Text = m.Length > 200 ? m[..200] : m), (k, n, l) => f.BeginInvoke(() => { bar.Value = k * 100 / n; step.Text = l; })));
            }
            catch (Exception e) { error = e.Message; }
            f.Close();
        };
        Application.Run(f);
        if (error != null) { MessageBox.Show(error, "Blur Map Builder - setup"); return false; }
        return true;
    }
}

/// the start-up picture (road_designer.show_splash)
public sealed class Splash : Form
{
    const int Ms = 2500;
    readonly long _t0 = Environment.TickCount64;
    public Splash()
    {
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false; BackColor = Theme.Bg;
        Look.Dress(this);
        using var s = typeof(Splash).Assembly.GetManifestResourceStream("splash.png")!;
        var img = new Bitmap(s);
        ClientSize = new Size(img.Width, img.Height + 24);
        var p = new PictureBox { Image = img, Dock = DockStyle.Top, Height = img.Height };
        var l = new Label { Text = "Loading...", Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Text, Padding = new Padding(8, 3, 0, 0) };
        p.Click += (_, _) => Close();
        Controls.Add(p); Controls.Add(l);
    }

    /// keep it up for its time (a click closes it sooner), then hand over
    public void Finish(Action shown)
    {
        int wait = Math.Max(1, Ms - (int)(Environment.TickCount64 - _t0));
        var t = new System.Windows.Forms.Timer { Interval = wait };
        FormClosed += (_, _) => { t.Stop(); shown(); };
        t.Tick += (_, _) => { t.Stop(); Close(); };
        t.Start();
    }
}

/// "Blur Map Tester": Blur REBORN straight into a custom LAN race on the last map (map_tester.py). Started with --tester
public static class Tester
{
    static bool SetEnabled(bool on)
    {
        string t = Play.QuickstartTarget;
        if (!File.Exists(t)) return false;
        string s = File.ReadAllText(t), s2 = new System.Text.RegularExpressions.Regex(@"enabled\s*=\s*(true|false)").Replace(s, "enabled = " + (on ? "true" : "false"), 1);
        if (!s2.Contains("route_names")) return false;
        File.WriteAllText(t, s2, new System.Text.UTF8Encoding(false));
        return true;
    }

    public static async Task<int> Run()
    {
        const string T = "Blur Map Tester";
        if (!File.Exists(Play.RebornExe) || !File.Exists(Path.Combine(Paths.PackDir, "root.pak"))) { MessageBox.Show("Open the Blur Map Builder first and press Build + install once: the tester plays the map you built.", T); return 0; }
        if (Play.GameRunning()) { MessageBox.Show("Blur is already running - close it first.", T); return 0; }
        if (!SetEnabled(true)) { MessageBox.Show("No map picked yet: in the Blur Map Builder select your map and press Play (F5) once - the tester then always starts that map.", T); return 0; }
        Process.Start(new ProcessStartInfo(Play.RebornExe) { WorkingDirectory = Paths.Game, UseShellExecute = false });
        long t0 = Environment.TickCount64; bool seen = false;
        try
        {
            while (true)
            {
                await Task.Delay(3000);
                if (Play.GameRunning()) seen = true;
                else if (seen || Environment.TickCount64 - t0 > 600_000) break;
            }
        }
        finally { SetEnabled(false); }
        return 0;
    }
}

static class Program
{
    /// the updater on its own, against a fake release (BMB_UPDATE_API): no window, and only in a test folder. The new program answers with --update-done
    static int UpdateTest(List<string> a)
    {
        var info = Updates.Latest().GetAwaiter().GetResult();
        string log = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "update_test.txt");
        if (info == null) { File.AppendAllText(log, "no release\n"); return 1; }
        File.AppendAllText(log, $"{Paths.Version}: release {info.Value.Tag} newer {Updates.Newer(info.Value.Tag)} files {info.Value.Files.Count}\n");
        string? err = Updates.Install(info.Value.Files, null).GetAwaiter().GetResult();
        File.AppendAllText(log, "install: " + (err ?? "ok") + "\n");
        if (err == null) Process.Start(new ProcessStartInfo(Environment.ProcessPath!, new[] { "--game", Paths.Game, "--update-done" }) { UseShellExecute = false });
        return 0;
    }

    [STAThread]
    static int Main(string[] args)
    {
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)!.TrimEnd('\\');
        bool asked = Lang.Load();
        string? game = Environment.GetEnvironmentVariable("BMB_GAME");
        game ??= File.Exists(Path.Combine(exeDir, "Blur.exe")) ? exeDir : null;
        var a = args.ToList();
        int i = a.IndexOf("--game");
        if (i >= 0 && i + 1 < a.Count) game = a[i + 1];
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        if (game == null)
        {
            MessageBox.Show("Put this program into your Blur folder (next to Blur.exe), or start it with --game \"<Blur folder>\".", "Blur Map Builder (C#)");
            return 1;
        }
        Paths.Init(game, Env("BMB_MAPS"), Env("BMB_WORK"), Env("BMB_PACKS"), Env("BMB_LEVELS"));
        Application.ThreadException += (_, e) => Crash(e.Exception);
        if (a.Contains("--setup"))
        {
            try { Setup.RunAll(Console.WriteLine, (k, n, l) => Console.WriteLine($"[{k}/{n}] {l}")); return 0; }
            catch (Exception e) { Console.WriteLine("STOPPED: " + e.Message); return 1; }
        }
        if (a.Contains("--tester")) return Tester.Run().GetAwaiter().GetResult();
        if (a.Contains("--update-test")) return UpdateTest(a);
        if (a.Contains("--update-done")) { File.AppendAllText(Path.Combine(exeDir, "update_test.txt"), "the new program started\n"); return 0; }
        if (!asked && !a.Contains("--selftest") && !a.Contains("--selftest2")) LangWindow.Run();
        if (!a.Contains("--selftest") && !a.Contains("--selftest2") && !SetupWindow.Run()) return 1;
        Directory.CreateDirectory(Paths.Work); Directory.CreateDirectory(Paths.Tracks);
        bool test = a.Contains("--selftest") || a.Contains("--selftest2");
        Splash? splash = test ? null : new Splash();
        splash?.Show(); splash?.Refresh();
        var f = new MainForm(test);
        if (splash != null)
        {
            var hold = new ApplicationContext(splash);
            splash.Finish(() => { });
            Application.Run(hold);
        }
        if (test)
        {
            string o = a[a.IndexOf(a.Contains("--selftest") ? "--selftest" : "--selftest2") + 1];
            f.Shown += async (_, _) =>
            {
                try { await f.SelfTest(o, a.Contains("--selftest2")); }
                catch (Exception e) { File.WriteAllText(Path.Combine(o, "ui_error.txt"), e.ToString()); }
                f.Close();
            };
        }
        Application.Run(f);
        return 0;
    }

    static void Crash(Exception e)
    {
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            string name = Path.Combine(Paths.Logs, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(name, $"Blur Map Builder (C#) {Paths.Version}\ntime: {DateTime.Now}\ngame folder: {Paths.Game}\n\n{e}\n");
            if (Environment.GetEnvironmentVariable("BMB_TEST") == null) MessageBox.Show($"Sorry, the map builder hit an error.\n\nA report was saved:\n{name}\n\nPlease send that file to {UiText.Discord} on Discord. Your maps are safe (auto-save).", "Blur Map Builder - something went wrong");
        }
        catch (Exception) { }
    }

    static string? Env(string k) => Environment.GetEnvironmentVariable(k);
}

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
  F3 (or the 2D / 3D / Collision buttons at the top left)  switch views    Ctrl+1 .. Ctrl+9  the tabs in their order
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
    public void Help() { HelpCard.Show(this, Mode); LastPreview = HelpCard.Last; }

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
        CarMesh.Prepare();      // the Lighting preview's car: made once from the game's files (again after an update)
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
            _update.Text = "⬇ Update " + info.Value.Tag; _update.BackColor = Theme.Orange; _updateFound = true; FitTop();
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
                foreach (var (src, name) in new[] { (Paths.GamedataOut, "gamedata.pak"), (Paths.RootOut, "root.pak"), (Paths.BootOut, "gamedata_boot.pak") })
                {
                    string dst = Path.Combine(Paths.PackDir, name);
                    if (!File.Exists(src)) continue;
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
                if (!_test) { if (err == null) Sfx.Play("build_done.wav"); else System.Media.SystemSounds.Hand.Play(); }
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
        p.Controls.Add(new Label { Text = "   the game goes to the LAN lobby on this map by itself", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8") });
        p.Controls.Add(Theme.Btn("▶  Start the game normally", () => Go(false)));
        var fast = Theme.Check("Fast start: skip the intro movies and \"Press Start\" (both buttons)", Play.Fast, v => Play.Fast = v);
        fast.ForeColor = Theme.Text; fast.Margin = new Padding(3, 8, 3, 4); p.Controls.Add(Look.Tip(fast, "The game skips the Activision / Bizarre movies, the Blur movie and the Press Start screen by itself. The Amax login box still needs you"));
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

/// the program's own sounds (assets/*.wav, built in). One plays at a time; the player is kept so the sound isn't cut off
public static class Sfx
{
    static System.Media.SoundPlayer? _p;
    public static void Play(string name)
    {
        try
        {
            using var s = typeof(Sfx).Assembly.GetManifestResourceStream(name);
            if (s == null) return;
            var m = new MemoryStream(); s.CopyTo(m); m.Position = 0;
            _p?.Stop(); _p = new System.Media.SoundPlayer(m); _p.Play();
        }
        catch (Exception) { }
    }
}

/// the start-up picture (road_designer.show_splash): two light strokes swoop in from the screen's edges and draw a glowing frame
/// around it (light-painting style). A see-through window over the whole screen, drawn by code; it runs on its own thread so it keeps
/// moving while the main window loads
/// how the start-up animation looks; the Splash Lab (--splash-lab) changes it and saves it next to the program
public sealed class SplashStyle
{
    // the defaults are the look picked in the lab (2026-11-16)
    public double Speed { get; set; } = 0.79; public double Thick { get; set; } = 1.13; public double Taper { get; set; } = 0.65; public double Glow { get; set; } = 2.16; public double Tail { get; set; } = 1; public double Stagger { get; set; } = 0.19; public double Hold { get; set; } = 0.6; public double GlowSize { get; set; } = 1; public double Threshold { get; set; } = 0.25;
    public int Count { get; set; } = 2;
    public bool Sound { get; set; } = true;     // the start sound with it
    public string[] Colors { get; set; } = { "#ff7a2a", "#1fa8ff", "#8dff3a", "#ff2fa8", "#8a5cff", "#ffd23f" };
    public static string File0 => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)!, "splash_style.json");
    public static SplashStyle Load()
    {
        try { if (File.Exists(File0)) return System.Text.Json.JsonSerializer.Deserialize<SplashStyle>(File.ReadAllText(File0)) ?? new(); } catch (Exception) { }
        return new();
    }
    public void Save() => File.WriteAllText(File0, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    public SplashStyle Copy() => System.Text.Json.JsonSerializer.Deserialize<SplashStyle>(System.Text.Json.JsonSerializer.Serialize(this))!;
}

public sealed class Splash : Form
{
    const double Step = 3, Shrink = 0.5, PhotoOut = 0.2;     // pixels between a stroke's points (small = smooth); seconds the strokes shrink away, then the picture goes
    const int Pad = 1;                                       // the frame sits right on the picture's edge, square corners

    /// one light stroke: it swoops in from the screen's edge, then draws a third of the frame. Points every Step pixels
    sealed class Stroke
    {
        public PointF[] P = Array.Empty<PointF>(); public PointF[] Nrm = Array.Empty<PointF>();
        public int FrameFrom;               // points from here on are the frame: they stay lit
        public double Start, Dur; public Color Col, Core;
        public double Len => (P.Length - 1) * Step;
        public double HeadAt(double t) { double u = Math.Clamp((t - Start) / Dur, 0, 1); return Len * (1 - Math.Pow(1 - u, 2.3)); }     // fast in, slows down while drawing
        public double End => Start + Dur;
        public double T0 = 1.55, Taper = 0.65;
        public double Thick(int i) => T0 - Taper * i / Math.Max(1, P.Length - 1);     // thick where it starts, thinner at its end
    }
    readonly Stroke[] _st;
    readonly SplashStyle _y;
    public readonly bool Lab;
    readonly Bitmap _photo;
    readonly Rectangle _scr;
    Rectangle _pr;
    readonly Stopwatch _sw = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 15 };
    volatile bool _ready; bool _skip; double _outAt = -1;
    IntPtr _dc, _dib, _old, _bits; Bitmap? _surf;

    public Splash(SplashStyle? style = null, bool lab = false)
    {
        _y = style ?? SplashStyle.Load(); Lab = lab;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        _scr = SystemInformation.VirtualScreen;      // all the screens together: the strokes come in from the outer edges of the whole desktop
        var home = Screen.FromPoint(Cursor.Position).Bounds;     // the picture sits in the middle of the screen with the mouse
        Bounds = _scr;
        using var s = typeof(Splash).Assembly.GetManifestResourceStream("splash.png")!;
        using var img = new Bitmap(s);
        _photo = new Bitmap(img.Width, img.Height + 24, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_photo))
        {
            g.Clear(Theme.Bg); g.DrawImage(img, 0, 0, img.Width, img.Height);
            TextRenderer.DrawText(g, "Loading...", Font, new Rectangle(8, img.Height + 3, img.Width - 8, 21), Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);
        }
        _pr = new Rectangle(home.X - _scr.X + (home.Width - _photo.Width) / 2, home.Y - _scr.Y + (home.Height - _photo.Height) / 2, _photo.Width, _photo.Height);
        _st = MakeStrokes();
        _tick.Tick += (_, _) => Frame();
        MouseDown += (_, e) => { if (_pr.Contains(e.Location)) _skip = true; };
    }

    /// the frame is shared out between the strokes, all drawn the same way round (from the bottom-left corner along the bottom,
    /// up the right, along the top, down the left); each swoops in from the nearest screen edge and joins its piece along the line
    Stroke[] MakeStrokes()
    {
        float W = _scr.Width, H = _scr.Height;
        float L = _pr.Left - Pad, T = _pr.Top - Pad, R = _pr.Right + Pad, B = _pr.Bottom + Pad, per = 2 * (R - L + B - T);
        int n = Math.Clamp(_y.Count, 1, 6); float part = per / n;
        var o = new Stroke[n];
        for (int k = 0; k < n; k++)
        {
            float d0 = k * part;
            var j = Perim(L, T, R, B, d0 + 0.01f);
            var j2 = Perim(L, T, R, B, d0 + 1);
            float dx = j2.X - j.X, dy = j2.Y - j.Y, dl = MathF.Max(1e-3f, MathF.Sqrt(dx * dx + dy * dy)); dx /= dl; dy /= dl;     // the way along the frame
            float nx = dy, ny = -dx;                                                    // out from the picture (the frame runs anticlockwise on screen)
            float side = k % 2 == 0 ? 1 : 0.6f;
            PointF Cl(float x, float y) => new(Math.Clamp(x, 40, W - 40), Math.Clamp(y, 40, H - 40));
            var j0 = Perim(L, T, R, B, d0);
            var bp = new PointF(j0.X - dx * 150 + nx * 90 * side, j0.Y - dy * 150 + ny * 90 * side);
            var a = Cl(j0.X - dx * 480 + nx * 280 * side, j0.Y - dy * 480 + ny * 280 * side);
            // off the desktop's edge in the way it swoops in from (with several screens, that can be the far side of another screen)
            float vx = a.X - j0.X, vy = a.Y - j0.Y;
            int e = Math.Abs(vx) >= Math.Abs(vy) ? (vx < 0 ? 0 : 1) : (vy < 0 ? 2 : 3);
            PointF Off(float m) => e switch { 0 => new(-m, a.Y - dy * 200), 1 => new(W + m, a.Y - dy * 200), 2 => new(a.X - dx * 200, -m), _ => new(a.X - dx * 200, H + m) };
            var raw = Spline(Off(260), Off(80), a, bp, j0, new PointF(j0.X + dx * 40, j0.Y + dy * 40));
            int f = raw.Count; raw.AddRange(Perimeter(L, T, R, B, d0, part));
            var col = C(_y.Colors.Length > 0 ? _y.Colors[k % _y.Colors.Length] : "#1fa8ff");
            double sp = Math.Max(0.1, _y.Speed);
            o[k] = Resample(raw, f, (0.1 + k * _y.Stagger) / sp, 2.0 / sp, col, Mix(col, Color.White, 0.8));
            o[k].Taper = _y.Taper * _y.Thick; o[k].T0 = (1 + _y.Taper * 0.85) * _y.Thick;
        }
        return o;
    }
    static Color Mix(Color a, Color b, double k) => Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    static Color C(string h) => ColorTranslator.FromHtml(h);

    /// a point on the picture's frame, d pixels from the bottom-left corner, going right along the bottom, up the right, left along the top, down the left
    static PointF Perim(float L, float T, float R, float B, float d)
    {
        float w = R - L, h = B - T, per = 2 * (w + h);
        d = ((d % per) + per) % per;
        if (d < w) return new PointF(L + d, B); d -= w;
        if (d < h) return new PointF(R, B - d); d -= h;
        if (d < w) return new PointF(R - d, T); d -= w;
        return new PointF(L, T + d);
    }
    static List<PointF> Perimeter(float L, float T, float R, float B, float from, float len)
    {
        var o = new List<PointF>();
        for (float d = 0; d < len; d += 2) o.Add(Perim(L, T, R, B, from + d));
        o.Add(Perim(L, T, R, B, from + len));
        return o;
    }

    /// a smooth curve through the points (Catmull-Rom); the first and last are only there to bend the ends, the curve runs from the 2nd to the 2nd last
    static List<PointF> Spline(params PointF[] k)
    {
        var o = new List<PointF>();
        for (int i = 1; i < k.Length - 2; i++)
        {
            PointF p0 = k[i - 1], p1 = k[i], p2 = k[i + 1], p3 = k[i + 2];
            int n = Math.Max(8, (int)(Dist(p1, p2) / 2));
            for (int j = 0; j < n; j++)
            {
                float t = (float)j / n, t2 = t * t, t3 = t2 * t;
                o.Add(new PointF(
                    0.5f * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
                    0.5f * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3)));
            }
        }
        return o;
    }

    static float Dist(PointF a, PointF b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// even spacing along the stroke (so it moves at a steady speed and draws smooth), with the side direction at each point
    static Stroke Resample(List<PointF> raw, int frameFrom, double start, double dur, Color col, Color core)
    {
        var p = new List<PointF> { raw[0] }; int ff = 0; double carry = 0;
        for (int i = 1; i < raw.Count; i++)
        {
            PointF a = raw[i - 1], b = raw[i]; double d = Dist(a, b), at = Step - carry;
            while (at <= d) { p.Add(new PointF((float)(a.X + (b.X - a.X) * at / d), (float)(a.Y + (b.Y - a.Y) * at / d))); at += Step; }
            carry = d - (at - Step);
            if (i == frameFrom) ff = p.Count;
        }
        var n = new PointF[p.Count];
        for (int i = 0; i < p.Count; i++)
        {
            PointF a = p[Math.Max(0, i - 1)], b = p[Math.Min(p.Count - 1, i + 1)]; float dx = b.X - a.X, dy = b.Y - a.Y, l = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-4f;
            n[i] = new PointF(-dy / l, dx / l);
        }
        return new Stroke { P = p.ToArray(), Nrm = n, FrameFrom = ff, Start = start, Dur = dur, Col = col, Core = core };
    }

    /// the main window is made: the splash may end once its show is over
    public void Ready() => _ready = true;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8; return cp; }      // layered, tool window, topmost
    }
    protected override bool ShowWithoutActivation => true;
    public void Skip() => _skip = true;
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _dc = CreateCompatibleDC(IntPtr.Zero);
        var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = _scr.Width, biHeight = -_scr.Height, biPlanes = 1, biBitCount = 32 };
        _dib = CreateDIBSection(_dc, ref bi, 0, out _bits, IntPtr.Zero, 0);
        _old = SelectObject(_dc, _dib);
        _surf = new Bitmap(_scr.Width, _scr.Height, _scr.Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _bits);
        Frame(); _tick.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Stop(); _surf?.Dispose(); _photo.Dispose(); _glow?.Dispose();
        if (_dc != IntPtr.Zero) { SelectObject(_dc, _old); DeleteObject(_dib); DeleteDC(_dc); }
        base.OnFormClosed(e);
    }

    void Frame()
    {
        if (_surf == null || IsDisposed) return;
        double t = _sw.Elapsed.TotalSeconds, done = _st.Max(x => x.End), show = done + _y.Hold;
        if (_outAt < 0 && _ready && (t >= show || _skip)) _outAt = t;
        double gone = _outAt < 0 ? 0 : (t - _outAt) / Shrink;                       // 0..1: the strokes shrink away
        double shrink = gone >= 1 ? 0 : 1 - gone * gone;
        double photo = gone <= 1 ? 1 : 1 - (t - _outAt - Shrink) / PhotoOut;         // then the picture fades
        if (photo <= 0) { _tick.Stop(); Close(); return; }
        using (var g = Graphics.FromImage(_surf))
        {
            double flash = t > done ? Math.Max(0, 1 - (t - done) / 0.7) : 0;     // the frame lights up when it closes, then settles
            if (shrink > 0 && _y.Glow > 0.01) { Bloom(t, flash, shrink); Upscale(); }      // fills the whole surface: no clear needed
            else
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
            }
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            if (shrink > 0) foreach (var s in _st) DrawStroke(g, s, t, flash, shrink, false);
            g.DrawImageUnscaled(_photo, _pr.X, _pr.Y);
        }
        var dst = new POINT { x = _scr.X, y = _scr.Y }; var src = new POINT(); var size = new SIZE { cx = _scr.Width, cy = _scr.Height };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = (byte)(255 * Math.Clamp(photo, 0, 1)), AlphaFormat = 1 };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _dc, ref src, 0, ref blend, 2);
    }

    // the glow: the strokes' bright parts drawn small, blurred (box blur a few times = a smooth bell-shaped glow, a tight one
    // and a wide one), brightened, then stretched under the sharp lines
    Bitmap? _glow; int _gw, _gh, _q; float[]? _src, _tight, _wide, _tmp; byte[]? _buf;

    void Bloom(double t, double flash, double shrink)
    {
        if (_glow == null)
        {
            _q = Math.Max(4, (int)Math.Ceiling(Math.Sqrt((double)_scr.Width * _scr.Height / 150000)));     // the glow is soft: a small picture (about 150,000 points, however many screens) is plenty
            _gw = (_scr.Width + _q - 1) / _q; _gh = (_scr.Height + _q - 1) / _q;
            _glow = new Bitmap(_gw, _gh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            int n = _gw * _gh * 4; _src = new float[n]; _tight = new float[n]; _wide = new float[n]; _tmp = new float[n]; _buf = new byte[n];
        }
        using (var g = Graphics.FromImage(_glow))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.ScaleTransform(1f / _q, 1f / _q);
            foreach (var s in _st) DrawStroke(g, s, t, flash, shrink, true);
        }
        var rect = new Rectangle(0, 0, _gw, _gh);
        var bd = _glow.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            int row = _gw * 4;
            for (int y = 0; y < _gh; y++) System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, _buf!, y * row, row);
            for (int i = 0; i < _buf!.Length; i++) _src![i] = _buf[i];
            double size = Math.Clamp(_y.GlowSize, 0.2, 4) * 3.0 / _q;     // blur sizes in full-screen pixels / the shrink
            Blur(_src!, _tight!, Math.Max(1, (int)Math.Round(4 * size)), 2);
            Blur(_tight!, _wide!, Math.Max(2, (int)Math.Round(15 * size)), 2);     // the wide one grows from the tight one
            float gain = (float)Math.Max(0, _y.Glow);
            for (int i = 0; i < _buf.Length; i += 4)
            {
                float a = Math.Min(255, gain * (1.4f * _tight![i + 3] + 2.2f * _wide![i + 3]));
                for (int c = 0; c < 3; c++) _buf[i + c] = (byte)Math.Min(a, gain * (1.4f * _tight[i + c] + 2.2f * _wide[i + c]));
                _buf[i + 3] = (byte)a;
            }
        }
        finally { _glow.UnlockBits(bd); }
    }

    /// the glow picture (_buf) stretched smoothly over the whole screen surface, on all cores; this also wipes the last frame
    int[]? _xa, _xb; float[]? _xt;
    unsafe void Upscale()
    {
        int W = _scr.Width, H = _scr.Height, gw = _gw, gh = _gh; float q = _q; var src = _buf!;
        if (_xa == null)
        {
            _xa = new int[W]; _xb = new int[W]; _xt = new float[W];
            for (int x = 0; x < W; x++) { float fx = (x + 0.5f) / q - 0.5f; int x0 = (int)MathF.Floor(fx); _xt[x] = fx - x0; _xa[x] = Math.Clamp(x0, 0, gw - 1) * 4; _xb[x] = Math.Clamp(x0 + 1, 0, gw - 1) * 4; }
        }
        int[] xa = _xa, xb = _xb!; float[] xt = _xt!; IntPtr bits = _bits;
        System.Threading.Tasks.Parallel.For(0, H, y =>
        {
            float fy = (y + 0.5f) / q - 0.5f; int y0 = (int)MathF.Floor(fy); float ty = fy - y0;
            int ra = Math.Clamp(y0, 0, gh - 1) * gw * 4, rb = Math.Clamp(y0 + 1, 0, gh - 1) * gw * 4;
            byte* d = (byte*)bits + (long)y * W * 4;
            for (int x = 0; x < W; x++)
            {
                int a = xa[x], b = xb[x]; float tx = xt[x];
                for (int c = 0; c < 4; c++)
                {
                    float top = src[ra + a + c] + (src[ra + b + c] - src[ra + a + c]) * tx, bot = src[rb + a + c] + (src[rb + b + c] - src[rb + a + c]) * tx;
                    d[x * 4 + c] = (byte)(top + (bot - top) * ty);
                }
            }
        });
    }

    /// box blur, rows then columns, `times` over (3 times is close to a gaussian); edges held
    void Blur(float[] from, float[] to, int r, int times)
    {
        int W = _gw, H = _gh; Array.Copy(from, to, from.Length);
        var b = _tmp!;
        for (int it = 0; it < times; it++)
        {
            System.Threading.Tasks.Parallel.For(0, H, y =>
            {
                int o = y * W * 4; float inv = 1f / (2 * r + 1);
                for (int c = 0; c < 4; c++)
                {
                    float sum = 0;
                    for (int x = -r; x <= r; x++) sum += to[o + Math.Clamp(x, 0, W - 1) * 4 + c];
                    for (int x = 0; x < W; x++)
                    {
                        b[o + x * 4 + c] = sum * inv;
                        sum += to[o + Math.Min(W - 1, x + r + 1) * 4 + c] - to[o + Math.Max(0, x - r) * 4 + c];
                    }
                }
            });
            System.Threading.Tasks.Parallel.For(0, W, x =>
            {
                float inv = 1f / (2 * r + 1);
                for (int c = 0; c < 4; c++)
                {
                    float sum = 0;
                    for (int y = -r; y <= r; y++) sum += b[(Math.Clamp(y, 0, H - 1) * W + x) * 4 + c];
                    for (int y = 0; y < H; y++)
                    {
                        to[(y * W + x) * 4 + c] = sum * inv;
                        sum += b[(Math.Min(H - 1, y + r + 1) * W + x) * 4 + c] - b[(Math.Max(0, y - r) * W + x) * 4 + c];
                    }
                }
            });
        }
    }

    /// light-painting: a hot head, a tail that cools behind it (its fine strands parting a little), the swoop shrinking away
    /// behind the head, and the frame part staying lit. Nothing fades: lines get thinner until they are gone
    void DrawStroke(Graphics g, Stroke s, double t, double flash, double shrink, bool glow)
    {
        double head = s.HeadAt(t);
        if (head <= 0) return;
        int hi = Math.Min(s.P.Length - 1, (int)(head / Step));
        double swoopGone = t > s.End ? Math.Clamp((t - s.End) / 0.6, 0, 1) : 0;
        double frameLit = 0.6 + 0.4 * flash;
        const int Lv = 20, Wq = 60;
        var lv = new int[hi + 1]; var wq = new int[hi + 1];
        for (int i = 0; i <= hi; i++)
        {
            double d = head - i * Step;
            double b = Math.Exp(-d / (380.0 * _y.Tail)), wf = s.Thick(i) * shrink;            // the hot tail
            if (i < s.FrameFrom) { b = Math.Max(b, 0.45); wf *= Math.Clamp(1 - d / (1500.0 * _y.Tail), 0, 1) * (1 - swoopGone); }     // the swoop thins out behind the head
            else b = Math.Max(b, frameLit);
            lv[i] = (int)Math.Round(Math.Clamp(b, 0, 1) * Lv);
            wq[i] = (int)Math.Round(Math.Clamp(wf / 4, 0, 1) * Wq);
        }
        // runs of the same brightness and width as one line each (few calls, no seams)
        for (int pass = glow ? 0 : 2; pass < (glow ? 1 : 4); pass++)
        {
            int i0 = 0;
            while (i0 < hi)
            {
                int i1 = i0 + 1;
                while (i1 < hi && lv[i1] == lv[i0] && wq[i1] == wq[i0]) i1++;
                if (lv[i0] > 0 && wq[i0] > 0) Run(g, s, i0, i1, lv[i0] / (double)Lv, wq[i0] * 4.0 / Wq, pass);
                i0 = i1;
            }
        }
        // the head: a white-hot spot in its colour's glow; it shrinks away once the stroke is done
        double hb = (t > s.End ? Math.Max(0, 1 - (t - s.End) / 0.35) : 1) * shrink * s.Thick(hi);
        if (hb > 0.02)
        {
            var h = s.P[hi];
            if (glow)     // a bright spot for the blur to spread: the head blooms most
            {
                float r = (float)(9 * hb); using var br = new SolidBrush(s.Col); g.FillEllipse(br, h.X - r, h.Y - r, r * 2, r * 2);
                r = (float)(4.5 * hb); using var wb = new SolidBrush(s.Core); g.FillEllipse(wb, h.X - r, h.Y - r, r * 2, r * 2);
            }
            else
                for (int i = 1; i >= 0; i--)
                {
                    float r = (float)((i == 0 ? 3.2 : 5.5) * hb);
                    using var br = new SolidBrush(Color.FromArgb(i == 0 ? 255 : 200, i == 0 ? Color.White : s.Core));
                    g.FillEllipse(br, h.X - r, h.Y - r, r * 2, r * 2);
                }
        }
    }

    void Run(Graphics g, Stroke s, int i0, int i1, double b, double wf, int pass)
    {
        var pts = new PointF[i1 - i0 + 1];
        float w; int a; Color col; int strands = 1; float spread = 0;
        switch (pass)
        {
            case 0:     // what feeds the glow: only what is brighter than the threshold
                double th = Math.Clamp(_y.Threshold, 0, 0.95), k = Math.Max(0, (b - th) / (1 - th));
                w = 6; a = (int)(255 * k); col = s.Col; break;
            case 2: w = 4.2f; a = (int)(180 * b); col = s.Col; break;
            default: w = (float)(1.1 + 1.3 * b); a = (int)(255 * Math.Min(1, b * 1.3)); col = s.Core; strands = 3; spread = (float)(3.5 * (1 - b) * wf); break;     // hot core, strands part in the old tail
        }
        w *= (float)wf;
        if (a <= 0 || w < 0.25f) return;
        using var pen = new Pen(Color.FromArgb(Math.Min(255, a), col), w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        for (int k = 0; k < strands; k++)
        {
            float off = strands == 1 ? 0 : (k - 1) * spread;
            for (int i = i0; i <= i1; i++) pts[i - i0] = new PointF(s.P[i].X + s.Nrm[i].X * off, s.P[i].Y + s.Nrm[i].Y * off);
            if (pts.Length >= 2) g.DrawLines(pen, pts);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct POINT { public int x, y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);

    /// shows the splash on its own thread and plays the start sound; call Ready() + Join() on what comes back once the main window is made
    public static (Splash? s, Thread? th) Start()
    {
        if (SplashStyle.Load().Sound) Sfx.Play("startup.wav");
        Splash? made = null; using var ok = new ManualResetEventSlim();
        var th = new Thread(() =>
        {
            try { made = new Splash(); } catch (Exception) { }
            ok.Set();
            if (made != null) try { Application.Run(made); } catch (Exception) { }
        }) { IsBackground = true };
        th.SetApartmentState(ApartmentState.STA); th.Start();
        ok.Wait(5000);
        return (made, th);
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
        if (a.Contains("--splash-lab")) { Application.Run(new SplashLab()); return 0; }
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
        var (splash, splashTh) = test ? (null, null) : Splash.Start();
        var f = new MainForm(test);
        if (splash != null) { splash.Ready(); splashTh!.Join(15000); }
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

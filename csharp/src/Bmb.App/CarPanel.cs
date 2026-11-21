// The Custom car tab. Follows car/car_tab.py: the list, name, class, body, icons, paint, wheels, imported body + fit, stats with the class chart, the car in 3D.
using Bmb.Core;

namespace Bmb.App;

public sealed class CarPanel : Panel
{
    readonly MainForm _f;
    public int Sel;
    bool _loading;
    readonly ListBox _list = new() { Dock = DockStyle.Fill, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, IntegralHeight = false, Font = new Font("Segoe UI", 10f) };
    readonly TextBox _name = new() { Width = 200, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11f), MaxLength = 24 };
    readonly RadioButton[] _cls = new RadioButton[4];
    readonly ComboBox _base = Theme.Combo(240), _icon = Theme.Combo(220), _mini = Theme.Combo(220), _wheels = Theme.Combo(240), _sound = Theme.Combo(300);
    // the two small tabs at the top: the body (look, paint, wheels) and the sound; name, class and stats are for both
    readonly FlowLayoutPanel _bodyPage = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
    readonly FlowLayoutPanel _soundPage = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0), Visible = false };
    readonly RadioButton _tabBody, _tabSound;
    readonly Label _soundNow = new() { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 10, 0, 0), Font = Theme.Bold };
    static bool _soundTab;
    const string SameSound = "Same as the body car";
    readonly List<string?> _soundCars = new();          // the sound list's cars (null = the body car's sound)
    readonly ComboBox _replace = Theme.Combo(330);
    readonly List<string?> _replaceCars = new();        // the replace list's cars (null = a new car)
    readonly Label _replaceNote = new() { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 0, 0, 8) };
    const string NoReplace = "No - add it as a new car";
    List<Control> _statsCtrls = new();                  // the stats: shown on the Body tab only
    System.Media.SoundPlayer? _player;
    Button? _playing;                                       // the Listen button whose sound plays now (it shows Stop)
    readonly System.Windows.Forms.Timer _playEnd = new();
    CheckBox _ownOn = null!; Label _ownFile = null!; Spin _ownRpm = null!; Control _ownRow = null!;
    readonly PictureBox _miniPic = new() { Width = 150, Height = 48, BackColor = Theme.Bg };
    readonly Dictionary<string, Label> _kindFile = new();
    List<double[]> _flames = new();                           // where the exhaust flames come out (car model space)
    int _flameDrag = -1; Point _flameFrom; readonly double[] _flameD = new double[3];
    readonly Label _flameNote = new() { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(6, 8, 0, 0) };
    readonly PictureBox _logoPic = new() { Width = 96, Height = 48, BackColor = ColorTranslator.FromHtml("#3c3c46"), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(8, 0, 8, 0) };
    readonly Label _logoNote = new() { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 14, 0, 0) };
    readonly Panel[] _paint = { new() { Width = 34, Height = 22, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand }, new() { Width = 34, Height = 22, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand } };
    readonly Button[] _paintB = new Button[2];
    readonly Button _bodyBtn;
    readonly Label _roomLabel = new() { AutoSize = true, ForeColor = Theme.Text, MaximumSize = new Size(900, 0) };
    readonly Label _points = new() { AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI", 11f, FontStyle.Bold) }, _top = new() { AutoSize = true, ForeColor = Theme.Text };
    readonly Dictionary<string, Label> _vals = new();
    readonly Panel _chart = new() { Width = 520, Height = 46 * 5, BackColor = Theme.Bg, Cursor = Cursors.Hand };
    readonly PictureBox _view = new() { Dock = DockStyle.Fill, BackColor = ColorTranslator.FromHtml("#060a14"), SizeMode = PictureBoxSizeMode.CenterImage, Cursor = Cursors.SizeWE };
    readonly Label _hint = new() { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(6, 2, 6, 0), Text = "drag = turn the car", ForeColor = ColorTranslator.FromHtml("#7f8a96"), BackColor = Theme.Bg };
    readonly Panel _left = new() { Dock = DockStyle.Left, Width = 240, BackColor = Theme.Panel }, _viewBox = new() { Dock = DockStyle.Right, Width = 460, BackColor = Theme.Bg };
    readonly FlowLayoutPanel _controls = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(10) };
    readonly List<Control> _needCar = new();
    double _yaw = -0.7, _pitch = -0.35; int _dragX; bool _dragging;
    bool _dragRear;                                                       // the dragged wheel is a rear one
    double _zoom = 1, _panX, _panY; Point _panFrom; bool _panning;     // the preview's zoom (mouse wheel) and move (right drag)
    // the preview's camera buttons: top / side / front, and orbit (turns by itself)
    readonly System.Windows.Forms.Timer _orbit = new() { Interval = 40 };
    Button? _orbitBtn;
    // the wheel handles (imported bodies): drag a wheel to set "wheels side to side" / "front to back"
    CarUi.ViewInfo? _viewInfo; List<(double X, double Y, double Z, int Side, int End)> _wheelsAt = new();
    int _wheelDrag = -1; Point _wheelFrom; double _dTrack, _dWb;
    // the body handles: index 100 = move (up / forward), 101 = size
    const int BodyHandle = 100, SizeHandle = 101;
    double[]? _bodyLo, _bodyHi; double _dUp, _dFwd, _kSize = 1;
    CarUi.Parts? _parts; string _partsKey = "";
    int _job;
    const int RowH = 46;

    public CarPanel(MainForm f)
    {
        _f = f; BackColor = Theme.Bg;
        Look.DarkScroll(_controls, _list);
        RoundList.Dress(_list, 26);
        _bodyBtn = Theme.Btn("↺ Use the game body", GameBody);
        // left: the list
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Panel, Padding = new Padding(8) };
        head.Controls.Add(new Label { Text = "MY CARS", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold });
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Panel };
        row.Controls.Add(Theme.Btn("+ New", Add)); row.Controls.Add(Theme.Btn("Copy", Copy)); row.Controls.Add(Theme.Btn("Delete", Delete));
        head.Controls.Add(row);
        _left.Controls.Add(_list); _left.Controls.Add(head); _list.BringToFront();
        _list.SelectedIndexChanged += (_, _) => { if (!_loading && _list.SelectedIndex >= 0) { Sel = _list.SelectedIndex; Load2(); } };
        // the controls
        var top = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, MaximumSize = new Size(1100, 0) };
        _controls.Resize += (_, _) => top.MaximumSize = new Size(Math.Max(300, Math.Min(1100, _controls.ClientSize.Width - 30)), 0);
        Control Col(string title, Control c, int w = 0)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 14, 4) };
            p.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8") });
            p.Controls.Add(c);
            return p;
        }
        _name.TextChanged += (_, _) => { var v = _name.Text.Trim(); Set("name", v.Length > 0 ? v[..Math.Min(24, v.Length)] : "My car"); };
        top.Controls.Add(Col("Name", _name));
        var cls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
        for (int i = 0; i < 4; i++) { int k = i; _cls[i] = Theme.Radio($" {CarUi.Classes[i]} ", null, () => ClassChanged(k)); _cls[i].Font = new Font("Segoe UI", 10f, FontStyle.Bold); cls.Controls.Add(_cls[i]); }
        top.Controls.Add(Col("Class", cls));
        var tabs = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(30, 16, 0, 0) };
        _tabBody = Theme.Radio("⛟  Body", null, () => ShowPage(false));
        _tabSound = Theme.Radio("🔊  Sound", null, () => ShowPage(true));
        foreach (var t in new[] { _tabBody, _tabSound }) { t.Font = Theme.Bold; t.MinimumSize = new Size(110, 30); t.TextAlign = ContentAlignment.MiddleCenter; tabs.Controls.Add(t); }
        Look.Tip(_tabBody, "The car's look: body, icons, paint, wheels");
        Look.Tip(_tabSound, "The car's engine sound");
        top.Controls.Add(tabs);
        _controls.Controls.Add(top); _wide.Add(top);
        _controls.Controls.Add(_line);
        // the Body page: cards, one per job (the 3D model, the look, the flames, new car or replace)
        foreach (var cb in new[] { _base, _icon }) { cb.FormattingEnabled = true; cb.Format += (_, e) => { if (e.ListItem is string s) e.Value = Skins.Nice(s); }; }
        _base.SelectionChangeCommitted += (_, _) => BaseChanged();
        _icon.SelectionChangeCommitted += (_, _) => Set("icon", _icon.SelectedItem as string);
        _mini.SelectionChangeCommitted += (_, _) => Set("mini_icon", (string)_mini.SelectedItem! == MiniAuto ? CarIcon.Auto : _mini.SelectedItem as string);
        _replace.SelectionChangeCommitted += (_, _) => { int i = _replace.SelectedIndex; ReplacePicked(i > 0 && i < _replaceCars.Count ? _replaceCars[i] : null); };
        Look.Tip(_replace, "Pick a game car: in game it gets this car's body, wheels and sound (online too, on your screen). Its handling and stats stay the game car's.");
        _wheels.SelectionChangeCommitted += (_, _) => Set("wheels", _wheels.SelectedItem as string);
        _wheels.DrawMode = DrawMode.OwnerDrawFixed;
        _wheels.DrawItem += WheelItem;
        _wheels.DropDownClosed += (_, _) => WheelHoverHide();
        _wheels.FormattingEnabled = true; _wheels.Format += (_, e) => { if (e.ListItem is string s) e.Value = Skins.Nice(s); };
        _base.Width = _wheels.Width = 260;

        // 0. a new car, or a game car replaced
        var repC = Card("🎮  IN GAME", "A new car in the car list, or one of the game's cars swapped for yours");
        CardRow(repC).Controls.Add(Col("Replace a game car (see your car online too)", _replace));
        _replaceNote.Margin = new Padding(0, 2, 0, 0);
        repC.Controls.Add(_replaceNote); _wideIn.Add(_replaceNote);

        // 1. the 3D model: a game car's body or your own .glb
        var model = Card("🚗  3D MODEL", "A game car's body, or your own 3D model (.glb)");
        var mRow = CardRow(model);
        mRow.Controls.Add(Col("Body (a game car of this class)", _base));
        mRow.Controls.Add(Col("Wheels (any game car's)", _wheels));
        var bRow = CardRow(model);
        bRow.Controls.Add(Look.Tip(Theme.Btn("⇩ Import body (.glb)", ImportBody, Theme.Blue), "Your own 3D car model (.glb, from Blender ...). It sits on the wheels of this car; fit it below or drag the handles in the preview"));
        Button? exp = null;
        exp = Look.Tip(Theme.Btn("⇧ Export a game car (.glb)", () => ExportMenu(exp!)), "Save a game car's own model as a .glb - every part its own object, like the game - to change it in Blender and import it back as a body");
        bRow.Controls.Add(exp);
        bRow.Controls.Add(Look.Tip(_bodyBtn, "Back to the game car's own body (your imported .glb is not used)"));
        _glbNote.Margin = new Padding(4, 8, 0, 0); bRow.Controls.Add(_glbNote);
        _clearGlass = Theme.Check("See-through glass", true, v => Set("clear_glass", v));
        _paintBody = Theme.Check("Paintable body", true, v => Set("paint_body", v));
        _paintBody.Margin = new Padding(14, 7, 0, 0);
        bRow.Controls.Add(Look.Tip(_paintBody, "On: the parts of your body named like paint or car skin (a plain colour, no picture) take the paint colours you pick below, and pictures with see-through parts (badges, stickers) are drawn over the paint. Off: every colour from the .glb stays as it is"));
        _clearGlass.Margin = new Padding(14, 7, 0, 0);
        bRow.Controls.Add(Look.Tip(_clearGlass, "On: the body's glass (see-through or named glass in the .glb) is clear, you see inside the car. Off: the game's own dark windows - for a body without an inside"));
        // the fit of an imported body: steppers that show their numbers
        _fitBox.Controls.Add(_roomLabel);
        _roomLabel.Margin = new Padding(0, 6, 0, 6);
        _fitBox.Controls.Add(new Label { Text = "Fit the body on the wheels", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold, Margin = new Padding(0, 4, 0, 2) });
        var fBody = Row2(); var fWheels = Row2();
        fBody.Controls.Add(RowTitle("Body"));
        fWheels.Controls.Add(RowTitle("Wheels"));
        foreach (var (label, key, step) in new[] { ("Size", "size", 0.02), ("Forward", "fwd", 0.03), ("Up", "up", 0.02), ("Front width", "track", 0.02), ("Rear width", "track_r", 0.02), ("Length", "wb", 0.02) })
        {
            string k = key; double s = step;
            var st = Stepper(label, k, s);
            (k is "track" or "track_r" or "wb" ? fWheels : fBody).Controls.Add(st);
            if (k is "track" or "track_r" or "wb") _spacing.Add(st);
        }
        _turnBtn = Look.Tip(Theme.Btn("↻ Turn 90°", () => Fit("turn", 90)), "Turn the body a quarter round (when it comes in facing the wrong way)");
        var reset = Look.Tip(Theme.Btn("↺ Reset fit", () => Fit(null, 0)), "Back to the automatic fit");
        var fBtns = Row2(); fBtns.Margin = new Padding(72, 2, 0, 0);
        fBtns.Controls.Add(_turnBtn); fBtns.Controls.Add(reset);
        _fitBox.Controls.Add(fBody); _fitBox.Controls.Add(fWheels); _fitBox.Controls.Add(fBtns);
        _fitBox.Controls.Add(Hint("Or drag in the preview: the wheels (width / length), the dot on top of the body (move), the dot on its nose (size). Right-drag moves the view, the wheel zooms."));
        model.Controls.Add(_fitBox);

        // 2. the look: paint, logo, icons
        var lookC = Card("🎨  LOOK", "Paint, maker logo and the icons the menus show");
        var pRow = CardRow(lookC);
        pRow.Controls.Add(RowTitle("Paint"));
        for (int i = 0; i < 2; i++)
        {
            int k = i;
            _paint[i].Width = 44; _paint[i].Height = 28;
            _paint[i].Margin = new Padding(4, 4, 2, 0); _paint[i].Click += (_, _) => Paint(k); pRow.Controls.Add(Look.Tip(_paint[i], $"Colour {k + 1}: click to change"));
            _paintB[i] = Theme.Btn(k == 0 ? "Main colour" : "Second colour", () => Paint(k)); _paintB[i].Margin = new Padding(2, 3, 12, 3); pRow.Controls.Add(_paintB[i]);
        }
        _paintNote.Margin = new Padding(0, 9, 0, 0); pRow.Controls.Add(_paintNote);
        var logoRow = CardRow(lookC);
        logoRow.Controls.Add(RowTitle("Logo"));
        _logoPic.Margin = new Padding(4, 0, 8, 0); _logoPic.BackColor = Theme.Panel2;
        _logoPic.Paint += (_, e) => { if (_logoPic.Image == null) TextRenderer.DrawText(e.Graphics, Lang.T("game logo"), Theme.Small, _logoPic.ClientRectangle, Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); };
        logoRow.Controls.Add(_logoPic);
        logoRow.Controls.Add(Look.Tip(Theme.Btn("⇩ Import logo (.png)", ImportLogo), "A picture of the maker logo, best a .png with a see-through background. Shown in the garage and car select."));
        logoRow.Controls.Add(Look.Tip(Theme.Btn("↺ Game logo", () => { if (Car()?.Truthy("logo") == true) { Set("logo", null); _f.Say("The car shows its maker's game logo again", Theme.Good); } }), "Back to the maker's logo of the body car"));
        logoRow.Controls.Add(_logoNote);
        var iRow = CardRow(lookC);
        iRow.Controls.Add(Col("Menu icon", _icon));
        iRow.Controls.Add(Col("Mini icon (car list)", _mini));
        _miniPic.Margin = new Padding(0, 14, 0, 0);
        iRow.Controls.Add(_miniPic);

        // 3. the exhaust flames
        var flameC = Card("🔥  EXHAUST FLAMES", "Where the flames come out - drag the orange dots in the preview");
        var flameRow = CardRow(flameC);
        flameRow.Controls.Add(Look.Tip(Theme.Btn("+ Pipe", () => FlamePipes(1)), "One more exhaust pipe"));
        flameRow.Controls.Add(Look.Tip(Theme.Btn("− Pipe", () => FlamePipes(-1)), "One exhaust pipe less"));
        flameRow.Controls.Add(Look.Tip(Theme.Btn("↺ Game spots", () => { Set("exhaust", null); }), "Back to where the game car's pipes are"));
        flameRow.Controls.Add(_flameNote);

        // the sound page
        _soundPage.Controls.Add(new Label { Text = "ENGINE SOUND", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold, Margin = new Padding(0, 2, 0, 4) });
        var sRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, MaximumSize = new Size(1100, 0) };
        _sound.Width = 380;
        _sound.SelectionChangeCommitted += (_, _) => { int i = _sound.SelectedIndex; Set("sound", i >= 0 && i < _soundCars.Count ? _soundCars[i] : null); ShowSound(); };
        sRow.Controls.Add(_sound);
        Button listen = null!; listen = Theme.Btn("▶ Listen", () => Listen(listen), Theme.Blue);
        Look.Tip(listen, "Play a short rev of this engine (press again to stop)");
        sRow.Controls.Add(listen);
        sRow.Controls.Add(Look.Tip(Theme.Btn("🎲 Random", RandomSound), "A random car's sound"));
        sRow.Controls.Add(Look.Tip(Theme.Btn("↺ Body car's sound", () => { Set("sound", null); Load2(); }), "Back to the sound of the body car"));
        _soundPage.Controls.Add(sRow); _wide.Add(sRow);
        _soundPage.Controls.Add(new Label { Text = "Pick the car whose engine you want: its engine, exhaust, gear changes and turbo. Press Listen to hear it. You hear it in game after Build + install.", AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 4, 0, 0) });
        // my own sound: one recording of an engine at steady revs; the build makes every rev step of the engine above from it
        _soundPage.Controls.Add(new Label { Text = "MY OWN ENGINE SOUND", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold, Margin = new Padding(0, 16, 0, 4) });
        var oRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, MaximumSize = new Size(1100, 0) };
        _ownOn = Theme.Toggle("My own sound: OFF", OwnToggle);
        _ownOn.CheckedChanged += (_, _) => _ownOn.Text = _ownOn.Checked ? "✔ My own sound: ON" : "My own sound: OFF";
        Look.Tip(_ownOn, "On: your recording replaces the engine, exhaust and intake of the sound above (turbo and gear sounds stay)");
        oRow.Controls.Add(_ownOn);
        oRow.Controls.Add(Look.Tip(Theme.Btn("⇩ Import sound...", ImportSound), "A .wav or .mp3 recording of an engine at steady revs (1-3 seconds is enough); an .mp3 is turned into a .wav"));
        _ownFile = new Label { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(6, 8, 10, 0), UseMnemonic = false };
        oRow.Controls.Add(_ownFile);
        _ownRpm = new Spin("Recorded at (rpm)", OwnSound.DefaultRpm, 250, 500, 12000, "0").Tight();
        _ownRpm.Set += v => { Set("sound_rpm", v); ShowSound(); };
        Look.Tip(_ownRpm, "About how fast the engine was turning in your recording. Higher = your sound plays lower in game");
        oRow.Controls.Add(_ownRpm);
        Button listenOwn = null!; listenOwn = Theme.Btn("▶ Listen", () => ListenOwn(listenOwn), Theme.Blue);
        oRow.Controls.Add(Look.Tip(listenOwn, "Play a short rev made from your sound (press again to stop)"));
        _ownRow = oRow;
        _soundPage.Controls.Add(oRow); _wide.Add(oRow);
        _soundPage.Controls.Add(new Label { Text = "Best: 1-3 seconds of an engine at steady revs, no music or talking. The program pitches it to every rev step; the turbo and gear sounds come from the engine chosen above.", AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 4, 0, 10) });
        _soundPage.Controls.Add(new Label { Text = "Recorded at (rpm): how fast the engine was turning in your recording (rpm = engine turns per minute). The game needs your sound at every engine speed, from idle (about 800) to the top (about 6500), and makes them by playing it slower or faster from this number. 3000 is a good start.\nSounds too high in game? Raise it (try 4000-5000). Too low? Lower it (try 2000). Press Listen to hear the change before you build.", AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 0, 0, 10) });
        // the other sounds: each its own .wav (turbo, supercharger, gear, pops); the rest stays the engine's above
        _soundPage.Controls.Add(new Label { Text = "MY OWN OTHER SOUNDS", AutoSize = true, ForeColor = Theme.Head, Font = Theme.Bold, Margin = new Padding(0, 16, 0, 4) });
        foreach (var k in OwnSound.Kinds)
        {
            var kind = k;
            var kRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
            kRow.Controls.Add(Look.Tip(new Label { Text = kind.Label, Width = 160, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text }, kind.Tip));
            kRow.Controls.Add(Look.Tip(Theme.Btn("⇩ Import sound...", () => ImportKind(kind)), kind.Tip + " (.wav or .mp3)"));
            Button play = null!; play = Theme.Btn("▶ Listen", () => ListenKind(kind, play), Theme.Blue);
            kRow.Controls.Add(Look.Tip(play, "Play your sound for this (press again to stop)"));
            kRow.Controls.Add(Look.Tip(Theme.Btn("↺ Game sound", () => SetKind(kind, null)), "Back to the engine's own sound for this"));
            var file = new Label { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(6, 8, 0, 0), UseMnemonic = false };
            kRow.Controls.Add(file); _kindFile[kind.Key] = file;
            _soundPage.Controls.Add(kRow); _wide.Add(kRow);
        }
        _soundPage.Controls.Add(new Label { Text = "Each one is a .wav of just that sound. Turbo and supercharger sounds are only heard on engines that have them (pick one in the list above); the gear, pop, damage and tyre sounds work on every car. Sounds that play once (blow-off, gear shift, gear crunch, pops) keep your recording's length - keep them short; the others loop, so 1-3 seconds of a steady sound is best.", AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 4, 0, 10) });
        _soundPage.Controls.Add(_soundNow);
        _controls.Controls.Add(_bodyPage); _controls.Controls.Add(_soundPage);
        // the stats (a new car only: a replaced game car keeps its own)
        var statC = Card("📊  STATS", "Each one stays inside the class's range; all together inside its point budget", _controls);
        _points.Margin = new Padding(0, 2, 0, 4);
        statC.Controls.Add(_points);
        var body = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var rows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        foreach (var (key, label) in CarUi.StatRows)
        {
            string k = key;
            var r = new FlowLayoutPanel { Width = 200, Height = RowH, WrapContents = false, Margin = new Padding(0) };
            r.Controls.Add(new Label { Text = label, AutoSize = false, Width = 90, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text });
            r.Controls.Add(Theme.Btn("−", () => Step(k, -1)));
            var v = new Label { Text = "", AutoSize = false, Width = 30, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Orange, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
            _vals[k] = v; r.Controls.Add(v);
            r.Controls.Add(Theme.Btn("+", () => Step(k, 1)));
            rows.Controls.Add(r);
        }
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(_chart, true);
        _chart.BackColor = Theme.Panel;
        _chart.Paint += ChartPaint;
        _chart.MouseDown += (_, e) => ChartClick(e);
        _chart.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) ChartClick(e); };
        body.Controls.Add(rows); body.Controls.Add(_chart);
        statC.Controls.Add(body);
        statC.Controls.Add(Hint("grey ticks = the class's cars,  blue line = class average,  box = class range,  orange = yours  (click or drag on a bar)"));
        _top.Margin = new Padding(0, 4, 0, 0);
        statC.Controls.Add(_top);
        _controls.Controls.Add(new Label { Text = "Build + install puts your cars in the game (car select: their class).", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(2, 4, 0, 0) });
        _viewBox.Controls.Add(_view);
        var cams = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(6, 2, 0, 2) };
        void Cam(string text, string tip, double yaw, double pitch) => cams.Controls.Add(Look.Tip(Theme.Btn(text, () => { OrbitStop(); _yaw = yaw; _pitch = pitch; ZoomReset(); Draw(true); }), tip));
        Cam("⬒ Top", "Look down on the car", 0, -1.5);
        Cam("◧ Side", "The car from its side", Math.PI / 2, -0.08);
        Cam("◉ Front", "The car from the front", 0, -0.12);
        _orbitBtn = Look.Tip(Theme.Btn("⟳ Orbit", OrbitToggle), "Turn around the car by itself (press again to stop)");
        cams.Controls.Add(_orbitBtn);
        cams.Controls.Add(Look.Tip(Theme.Btn("＋", () => ZoomBy(1.25, null)), "Zoom in (or the mouse wheel on the car)"));
        cams.Controls.Add(Look.Tip(Theme.Btn("−", () => ZoomBy(0.8, null)), "Zoom out (or the mouse wheel on the car)"));
        cams.Controls.Add(Look.Tip(Theme.Btn("↺", () => { OrbitStop(); _yaw = -0.7; _pitch = -0.35; ZoomReset(); Draw(true); }), "Back to the normal view"));
        _view.MouseWheel += (_, e) => ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2, e.Location);
        _view.MouseEnter += (_, _) => { if (FindForm()?.ActiveControl is not TextBoxBase) _view.Focus(); };
        _orbit.Tick += (_, _) => { _yaw += 0.035; Draw(false); };
        _zoomDone.Tick += (_, _) => { _zoomDone.Stop(); Draw(true); };
        _flameAnim.Tick += (_, _) => { if (FlamesOn && Visible && _view.Visible) _view.Invalidate(); };
        _flameAnim.Start();
        _viewBox.Controls.Add(cams); _viewBox.Controls.Add(_hint);
        _view.MouseDown += (_, e) =>
        {
            if (e.Button is MouseButtons.Right or MouseButtons.Middle) { _panning = true; _panFrom = e.Location; return; }
            int fl = FlameAt(e.Location);
            if (fl >= 0 && e.Button == MouseButtons.Left) { _flameDrag = fl; _flameFrom = e.Location; Array.Clear(_flameD); return; }
            int w = WheelAt(e.Location);
            if (w >= 0 && e.Button == MouseButtons.Left) { _wheelDrag = w; _wheelFrom = e.Location; _dTrack = _dWb = _dUp = _dFwd = 0; _kSize = 1; return; }
            OrbitStop();
            _dragX = e.X; _dragging = true;
        };
        _view.MouseMove += (_, e) =>
        {
            if (_panning) { _panX += e.X - _panFrom.X; _panY += e.Y - _panFrom.Y; _panFrom = e.Location; Draw(false); return; }
            if (_flameDrag >= 0) { FlameDragTo(e.Location); return; }
            if (_wheelDrag >= 0) { WheelDragTo(e.Location); return; }
            if (_dragging && e.Button == MouseButtons.Left) { _yaw += (e.X - _dragX) * 0.012; _dragX = e.X; Draw(false); return; }
            _view.Cursor = WheelAt(e.Location) >= 0 || FlameAt(e.Location) >= 0 ? Cursors.SizeAll : Cursors.SizeWE;
        };
        _view.MouseUp += (_, _) =>
        {
            if (_panning) { _panning = false; Draw(true); return; }
            if (_flameDrag >= 0) { FlameDragDone(); return; }
            if (_wheelDrag >= 0) { WheelDragDone(); return; }
            _dragging = false; Draw(true);
        };
        _view.Paint += (_, e) => { WheelHandles(e.Graphics); FlameHandles(e.Graphics); };
        _view.Resize += (_, _) => Draw(true);
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var vs = f.PanelSash(DockStyle.Right, "car_view_side", _viewBox, 460);
        right.Controls.Add(_controls); right.Controls.Add(vs); right.Controls.Add(_viewBox);
        _controls.BringToFront();
        var ls = f.PanelSash(DockStyle.Left, "car_list", _left, 240);
        Controls.Add(right); Controls.Add(ls); Controls.Add(_left);
        right.BringToFront();
        _controls.Resize += (_, _) => FitWidths();
        _wideIn.Add(_roomLabel);
        foreach (var c in new Control[] { _name, _base, _icon, _mini, _wheels, pRow, _chart, _sound, sRow, oRow }) _needCar.Add(c);
        _statsCtrls = _controls.Controls.Cast<Control>().SkipWhile(c => c != _soundPage).Skip(1).ToList();
        ShowPage(_soundTab);
        foreach (var r in _cls) _needCar.Add(r);
    }

    readonly List<Control> _wide = new();
    readonly List<Control> _cards = new(), _wideIn = new();        // the cards (all one width) and what wraps inside them

    /// every card as wide as the room (at most 1100), the rows inside them wrap at its inside width
    void FitWidths()
    {
        int w = Math.Max(Math.Min(_controls.ClientSize.Width - 30, 1100), 320);
        _controls.SuspendLayout();
        foreach (var c in _wide) c.MaximumSize = new Size(w, 0);
        _line.Width = w;
        foreach (var c in _cards) { c.MinimumSize = new Size(w, 0); c.MaximumSize = new Size(w, 0); }
        foreach (var c in _wideIn) c.MaximumSize = new Size(w - 30, 0);
        _controls.ResumeLayout();
    }
    readonly FlowLayoutPanel _fitBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 0, 0), Visible = false };
    readonly Dictionary<string, Label> _fitVals = new();
    readonly Label _line = new() { AutoSize = false, Height = 1, Width = 600, BackColor = Theme.Glass, Margin = new Padding(0, 6, 0, 8) };
    readonly Label _glbNote = new() { AutoSize = true, ForeColor = Theme.Good, UseMnemonic = false };
    CheckBox _clearGlass = null!, _paintBody = null!;
    readonly Label _paintNote = new() { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#9aa8b8") };
    Button _turnBtn = null!;
    static readonly Color Muted = ColorTranslator.FromHtml("#9aa8b8");

    /// a card: a darker box with a title and a short line under it; everything of one job goes in it
    FlowLayoutPanel Card(string title, string sub, Control? into = null)
    {
        var c = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 10) };
        c.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Theme.Head, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 0), UseMnemonic = false });
        c.Controls.Add(new Label { Text = sub, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 0, 6) });
        (into ?? _bodyPage).Controls.Add(c); _cards.Add(c);
        return c;
    }

    FlowLayoutPanel CardRow(FlowLayoutPanel card)
    {
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 4) };
        card.Controls.Add(r); _wideIn.Add(r);
        return r;
    }

    FlowLayoutPanel Row2() { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 2) }; _wideIn.Add(r); return r; }

    static Label RowTitle(string t) => new() { Text = t, AutoSize = false, Width = 72, Height = 34, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text, Font = Theme.Bold };

    Label Hint(string t) { var l = new Label { Text = t, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 4, 0, 0) }; _wideIn.Add(l); return l; }

    /// one fit number: its name, − , the number now, +
    Control Stepper(string label, string key, double step)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel2, Padding = new Padding(6, 1, 2, 1), Margin = new Padding(0, 2, 6, 2) };
        p.Controls.Add(new Label { Text = label, AutoSize = false, Width = 78, Height = 32, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted, Margin = new Padding(0, 1, 0, 0) });
        var minus = Theme.Btn("−", () => Fit(key, -step)); minus.Margin = new Padding(1, 3, 1, 3);
        var v = new Label { AutoSize = false, Width = 46, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Orange, Font = Theme.Bold, Margin = new Padding(0, 2, 0, 0) };
        var plus = Theme.Btn("+", () => Fit(key, step)); plus.Margin = new Padding(1, 3, 1, 3);
        p.Controls.Add(minus); p.Controls.Add(v); p.Controls.Add(plus);
        _fitVals[key] = v;
        return p;
    }

    public void Page(bool sound) => ShowPage(sound);

    void ShowPage(bool sound)
    {
        _soundTab = sound;
        _tabBody.Checked = !sound; _tabSound.Checked = sound;
        _controls.SuspendLayout();
        _bodyPage.Visible = !sound; _soundPage.Visible = sound;
        bool replacing = Car()?.Truthy("replace") == true;     // a replaced game car keeps its own handling: no stats to set
        foreach (var c in _statsCtrls) c.Visible = !sound && !replacing;
        _controls.ResumeLayout();
    }

    void ShowSound()
    {
        var c = Car();
        if (c == null) { _soundNow.Text = ""; return; }
        bool own = c.Truthy("sound") && c.Str("sound") != c.Str("base");
        var engines = SafeEngines();
        string car = own ? c.Str("sound")! : c.Str("base")!;
        string eng = engines.TryGetValue(car, out var e) ? $"  -  engine: {CarSoundPreview.Nice(e)}" : "";
        _soundNow.Text = (own ? $"🔊  {c.Str("name")} sounds like the {Skins.Nice(car)}." : $"🔊  {c.Str("name")} sounds like its body car, the {Skins.Nice(car)}.") + eng;
        if (c.Truthy("sound_own")) _soundNow.Text = $"🔊  {c.Str("name")} uses your own sound ({c.Str("sound_own")}, recorded at {c.Num("sound_rpm", OwnSound.DefaultRpm):0} rpm) on the {Skins.Nice(car)}'s engine" + eng;
        _soundNow.ForeColor = own ? Theme.Good : Theme.Text;
    }

    static Dictionary<string, string> SafeEngines()
    {
        try { return CarSoundPreview.Engines(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return new(); }
    }

    /// a short rev of the chosen engine (made on another thread, then played)
    /// stops the sound that plays (if any) and puts its button back to Listen
    void StopSound()
    {
        _playEnd.Stop();
        _player?.Stop(); _player?.Dispose(); _player = null;
        if (_playing != null && !_playing.IsDisposed) _playing.Text = "▶ Listen";
        _playing = null;
    }

    /// plays a .wav; its button shows Stop until the sound ends or is pressed again
    void PlaySound(Button b, byte[] wav)
    {
        StopSound();
        _player = new System.Media.SoundPlayer(new MemoryStream(wav));
        _player.Play();
        _playing = b; b.Text = "■ Stop";
        // how long it plays: the data size / bytes per second (byte rate at +28 of the .wav header)
        int rate = wav.Length >= 44 ? BitConverter.ToInt32(wav, 28) : 0;
        int ms = rate > 0 ? (int)((wav.Length - 44) * 1000L / rate) + 200 : 3000;
        _playEnd.Interval = Math.Max(100, ms);
        _playEnd.Tick -= PlayEnded; _playEnd.Tick += PlayEnded;
        _playEnd.Start();
    }

    void PlayEnded(object? s, EventArgs e) => StopSound();

    void Listen(Button b)
    {
        if (_playing == b) { StopSound(); return; }
        var c = Car();
        if (c == null) return;
        string car = c.Truthy("sound") ? c.Str("sound")! : c.Str("base")!;
        _f.Say($"Playing the engine of the {Skins.Nice(car)}...");
        Task.Run(() =>
        {
            try { return (CarSoundPreview.RevWav(car), (string?)null); }
            catch (Exception e) when (e is not OutOfMemoryException) { return (Array.Empty<byte>(), e.Message); }
        }).ContinueWith(t =>
        {
            var (wav, err) = t.Result;
            if (err != null) { _f.Say("Cannot play this sound: " + err, Theme.Bad); return; }
            PlaySound(b, wav);
            _f.Say($"Engine of the {Skins.Nice(car)} (a short rev up and off the gas)", Theme.Good);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    string EngineCar(JObj c) => c.Truthy("sound") ? c.Str("sound")! : c.Str("base")!;

    void OwnToggle(bool on)
    {
        var c = Car();
        if (c == null || _loading) return;
        if (!on) { Set("sound_own", null); ShowSound(); return; }
        string? file = c.Truthy("sound_own_file") ? c.Str("sound_own_file") : null;
        if (file != null && File.Exists(OwnSound.FilePath(file))) { Set("sound_own", file); ShowSound(); }
        else { ImportSound(); if (!c.Truthy("sound_own")) { _loading = true; _ownOn.Checked = false; _loading = false; } }
    }

    /// a .wav into maps\cars\sounds\ (checked first), then this car uses it
    void ImportSound()
    {
        var c = Car();
        if (c == null) return;
        string? path = _f.PickFile("Engine sound (.wav or .mp3)", OwnSound.Filter);
        if (path == null) return;
        string name;
        try { OwnSound.ReadWav(path); name = OwnSound.Import(path); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { _f.Say("This sound can't be used: " + e.Message, Theme.Bad); return; }
        Edit(q => { q["sound_own"] = name; q["sound_own_file"] = name; if (!q.ContainsKey("sound_rpm")) q["sound_rpm"] = OwnSound.DefaultRpm; });
        Load2();
        _f.Say($"Your sound {name} is on {c.Str("name")}. Press Listen, set the revs it was recorded at, then Build + install.", Theme.Good);
    }

    /// a .wav for one of the other sound kinds, into maps\cars\sounds\
    void ImportKind(OwnSound.Kind kind)
    {
        var c = Car();
        if (c == null) return;
        string? path = _f.PickFile($"{kind.Label} (.wav or .mp3)", OwnSound.Filter);
        if (path == null) return;
        string name;
        try { OwnSound.ReadWav(path); name = OwnSound.Import(path); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { _f.Say("This sound can't be used: " + e.Message, Theme.Bad); return; }
        SetKind(kind, name);
        _f.Say($"{kind.Label}: your sound {name} is on {c.Str("name")}. You hear it after Build + install.", Theme.Good);
    }

    void SetKind(OwnSound.Kind kind, string? file)
    {
        if (Car() == null) return;
        Edit(q =>
        {
            var sd = q.Obj("sounds") != null ? Share.Copy(q.Obj("sounds")!) : new JObj();
            if (file == null) sd.Remove(kind.Key); else sd[kind.Key] = file;
            q["sounds"] = sd.Count > 0 ? sd : null;
        });
        ShowKinds();
    }

    void ShowKinds()
    {
        var c = Car();
        foreach (var k in OwnSound.Kinds)
        {
            if (!_kindFile.TryGetValue(k.Key, out var l)) continue;
            string? f = c?.Obj("sounds")?.Str(k.Key);
            if (string.IsNullOrEmpty(f)) { l.Text = "(the engine's own)"; l.ForeColor = ColorTranslator.FromHtml("#9aa8b8"); }
            else if (!File.Exists(OwnSound.FilePath(f))) { l.Text = f + "  (missing!)"; l.ForeColor = Theme.Bad; }
            else { l.Text = f; l.ForeColor = Theme.Good; }
        }
    }

    void ListenKind(OwnSound.Kind kind, Button b)
    {
        if (_playing == b) { StopSound(); return; }
        var c = Car();
        if (c == null) return;
        string? f = c.Obj("sounds")?.Str(kind.Key);
        if (string.IsNullOrEmpty(f) || !File.Exists(OwnSound.FilePath(f)))
        {
            // no own sound yet: the game's, so you hear what this row changes
            string car = EngineCar(c);
            _f.Say($"The game's {kind.Label.ToLowerInvariant()}...");
            Task.Run(() =>
            {
                try { using var root = new Pak(Paths.RootOriginal); return (OwnSound.GameSound(car, kind.Key, root), (string?)null); }
                catch (Exception e) when (e is not OutOfMemoryException) { return ((byte[]?)null, e.Message); }
            }).ContinueWith(t =>
            {
                var (wav, err) = t.Result;
                if (err != null) { _f.Say("Cannot play the game sound: " + err, Theme.Bad); return; }
                if (wav == null) { _f.Say($"The {Skins.Nice(car)}'s engine has no {kind.Label.ToLowerInvariant()} - pick an engine that has one above", Theme.Warn); return; }
                PlaySound(b, wav);
                _f.Say($"The game's {kind.Label.ToLowerInvariant()} ({Skins.Nice(car)}). Import a .wav to change it.", Theme.Good);
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }
        try { PlaySound(b, OwnSound.Wav16(OwnSound.ReadWav(OwnSound.FilePath(f)))); }
        catch (Exception e) when (e is IOException or InvalidDataException) { _f.Say("Cannot play this sound: " + e.Message, Theme.Bad); }
    }

    void ListenOwn(Button b)
    {
        if (_playing == b) { StopSound(); return; }
        var c = Car();
        if (c == null) return;
        string? file = c.Truthy("sound_own") ? c.Str("sound_own") : c.Truthy("sound_own_file") ? c.Str("sound_own_file") : null;
        if (file == null || !File.Exists(OwnSound.FilePath(file))) { _f.Say("Import a .wav first", Theme.Warn); return; }
        string car = EngineCar(c); double rpm = c.Num("sound_rpm", OwnSound.DefaultRpm);
        _f.Say("Making a rev from your sound...");
        Task.Run(() =>
        {
            try { return (CarSoundPreview.OwnRevWav(car, OwnSound.FilePath(file), rpm), (string?)null); }
            catch (Exception e) when (e is not OutOfMemoryException) { return (Array.Empty<byte>(), e.Message); }
        }).ContinueWith(t =>
        {
            var (wav, err) = t.Result;
            if (err != null) { _f.Say("Cannot play this sound: " + err, Theme.Bad); return; }
            PlaySound(b, wav);
            _f.Say($"Your sound as a rev (recorded at {rpm:0} rpm)", Theme.Good);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void RandomSound()
    {
        var c = Car();
        if (c == null) return;
        var all = CarUi.Pickable().Select(x => x.Name).Where(n => n != c.Str("sound") && n != c.Str("base")).ToList();
        if (all.Count == 0) return;
        Set("sound", all[Random.Shared.Next(all.Count)]);
        Load2();
    }

    // ---- the Wheels menu: a picture of the wheel under the mouse
    sealed class WheelPop : QuietPop
    {
        public readonly PictureBox Pic = new() { Width = 168, Height = 168, BackColor = ColorTranslator.FromHtml("#0b1426") };
        public readonly Label Name2 = new() { AutoSize = false, Width = 168, Height = 20, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false };
        public WheelPop()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Theme.Panel; StartPosition = FormStartPosition.Manual; ClientSize = new Size(180, 202);
            Pic.Location = new Point(6, 6); Name2.Location = new Point(6, 176);
            Controls.Add(Pic); Controls.Add(Name2);
        }
    }
    WheelPop? _pop; string? _popCar; int _popJob;
    public string? WheelShown => _pop is { Up: true } && _pop.Pic.Image != null ? _popCar : null;

    void WheelItem(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Theme.Blue : Theme.Field)) e.Graphics.FillRectangle(bg, e.Bounds);
        string name = (string)_wheels.Items[e.Index]!;
        TextRenderer.DrawText(e.Graphics, name, _wheels.Font, e.Bounds, sel ? Color.White : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (sel && _wheels.DroppedDown) WheelHover(name, _wheels.PointToScreen(new Point(_wheels.Width + 8, _wheels.Height)));
    }

    public void WheelHover(string car, Point at)
    {
        if (_popCar == car && _pop is { Up: true }) return;
        _popCar = car;
        _pop ??= new WheelPop();
        _pop.Name2.Text = car; _pop.Pic.Image = null;
        _pop.Put(at);
        int job = ++_popJob;
        Task.Run(() =>
        {
            var bm = CarUi.WheelPicture(car);
            BeginInvoke(() => { if (job == _popJob && _pop != null && !_pop.IsDisposed) { _pop.Pic.Image = bm; if (bm == null) _pop.Name2.Text = "No picture"; } });
        });
    }

    /// the robot: the menu open, a picture shown, closed - three times over. True if it stayed open each time
    public async Task<bool> TestWheelMenu()
    {
        bool ok = true;
        ShowPage(false); _controls.ScrollControlIntoView(_wheels); await Task.Delay(150); Application.DoEvents();     // as a user sees it: the list on screen
        foreach (var car in new[] { "RatRod_NA_Stock", "Ford_MustangGTR_Stock", "Audi_TT_Stock" })
        {
            _wheels.Focus();
            _wheels.DroppedDown = true;
            await Task.Delay(120); Application.DoEvents();
            WheelHover(car, _wheels.PointToScreen(new Point(_wheels.Width + 8, _wheels.Height)));
            await Task.Delay(300); Application.DoEvents();
            ok &= _wheels.DroppedDown;
            _wheels.DroppedDown = false;
            await Task.Delay(120); Application.DoEvents();
        }
        return ok;
    }

    public void WheelHoverHide() { _popJob++; _popCar = null; _pop?.Away(); }

    const string MiniAuto = "Auto (side view of this car)";

    List<object?> Cars() { if (_f.D.World.Arr("cars") == null) _f.D.World["cars"] = new List<object?>(); return _f.D.World.Arr("cars")!; }
    public JObj? Car() { var c = Cars(); return Sel >= 0 && Sel < c.Count ? c[Sel] as JObj : null; }

    void Edit(Action<JObj> fn)
    {
        var c = Car();
        if (_loading || c == null) return;
        _f.D.Snapshot(); fn(c); _f.D.Touched(); _f.Changed();
    }

    void Set(string key, object? value)
    {
        var c = Car();
        if (c == null || _loading || Equals(c[key], value)) return;
        Edit(q => q[key] = value);
        if (key is "wheels" or "body_glb" or "glb_fit" or "mini_icon" or "logo" or "exhaust" or "replace" or "clear_glass" or "paint_body") Load2();
        if (key == "name") { _loading = true; _list.Items[Sel] = Label(Car()!); _loading = false; }
    }

    static string Label(JObj c) => $"{c.Str("name")}   [{CarUi.Classes[(int)c.Int("class")]}]";

    public void Refresh2()
    {
        var cars = Cars();
        _loading = true;
        _list.Items.Clear();
        foreach (var c in cars.OfType<JObj>()) _list.Items.Add(Label(c));
        Sel = Math.Min(Sel, cars.Count - 1);
        _loading = false;
        foreach (var c in _needCar) c.Enabled = cars.Count > 0;
        if (cars.Count == 0)
        {
            _points.Text = "No custom car yet: press + New";
            _view.Image = null;
            _chart.Invalidate();
            return;
        }
        Sel = Math.Max(Sel, 0);
        _loading = true; _list.SelectedIndex = Sel; _loading = false;
        Load2();
    }

    void Add()
    {
        _f.D.Snapshot();
        var car = CarUi.NewCar();
        car["net_id"] = CarNetId.New();
        var names = Cars().OfType<JObj>().Select(c => c.Str("name")).ToHashSet();
        int n = 1;
        while (names.Contains(car.Str("name"))) { n++; car["name"] = $"My car {n}"; }
        Cars().Add(car);
        Sel = Cars().Count - 1;
        _f.D.Touched(); _f.Changed();
        Refresh2();
    }

    void Copy()
    {
        var c0 = Car();
        if (c0 == null) return;
        _f.D.Snapshot();
        var c = Share.Copy(c0);
        string nm = c.Str("name") + " copy"; c["name"] = nm[..Math.Min(24, nm.Length)];
        c["net_id"] = CarNetId.New();      // a copy is another car online
        Cars().Add(c);
        Sel = Cars().Count - 1;
        _f.D.Touched(); _f.Changed();
        Refresh2();
    }

    void Delete()
    {
        var c = Car();
        if (c == null) return;
        _f.D.Snapshot();
        Cars().RemoveAt(Sel);
        // its imported body (.glb) and own sound (.wav) go too (to the Recycle Bin), unless another car still uses them
        var gone = new List<string>();
        bool UsedKind(string file) => Cars().OfType<JObj>().Any(o => OwnSound.Kinds.Any(k => string.Equals(o.Obj("sounds")?.Str(k.Key), file, StringComparison.OrdinalIgnoreCase)));
        bool Used(string key, string file) => Cars().OfType<JObj>().Any(o => string.Equals(o.Str(key), file, StringComparison.OrdinalIgnoreCase));
        void Bin(string path)
        {
            try { if (File.Exists(path)) { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); gone.Add(Path.GetFileName(path)); } }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        }
        if (c.Truthy("body_glb") && !Used("body_glb", c.Str("body_glb")!)) Bin(GlbPath(c));
        foreach (var file in new[] { c.Str("sound_own"), c.Str("sound_own_file") }.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!Used("sound_own", file!) && !Used("sound_own_file", file!) && !UsedKind(file!)) Bin(OwnSound.FilePath(file!));
        foreach (var file in OwnSound.Kinds.Select(k => c.Obj("sounds")?.Str(k.Key)).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!Used("sound_own", file!) && !Used("sound_own_file", file!) && !UsedKind(file!)) Bin(OwnSound.FilePath(file!));
        if (c.Truthy("logo") && !Used("logo", c.Str("logo")!)) Bin(CarLogo.FilePath(c.Str("logo")!));
        _f.D.Touched(); _f.Changed();
        Refresh2();
        if (gone.Count > 0) _f.Say($"{c.Str("name")} deleted, and its files went to the Recycle Bin: {string.Join(", ", gone)}", Theme.Good);
    }

    void Load2()
    {
        var c = Car();
        if (c == null) return;
        int cls = (int)c.Int("class");
        var classCars = CarsData.ClassCars(cls);
        if (!classCars.Any(x => x.Name == c.Str("base"))) c["base"] = classCars[0].Name;
        if (c.Truthy("wheels") && !CarUi.Pickable().Any(x => x.Name == c.Str("wheels"))) c["wheels"] = null;
        if (c.Truthy("body_glb") && c.Str("base") != CarGlb.GlbBody[cls]) { c["base"] = CarGlb.GlbBody[cls]; CarUi.Fair(c); }
        _loading = true;
        if (_name.Text != c.Str("name") && !_name.Focused) _name.Text = c.Str("name");
        _cls[cls].Checked = true;
        _base.Items.Clear(); foreach (var x in classCars) _base.Items.Add(x.Name);
        _base.SelectedItem = c.Str("base");
        if (_icon.Items.Count == 0) foreach (var i in CarsData.Cars().Select(x => x.Icon).Distinct().OrderBy(s => s, StringComparer.Ordinal)) _icon.Items.Add(i);
        _icon.SelectedItem = c.Str("icon");
        if (_wheels.Items.Count == 0) foreach (var x in CarUi.Pickable()) _wheels.Items.Add(x.Name);
        _wheels.SelectedItem = c.Truthy("wheels") ? c.Str("wheels") : c.Str("base");
        // the list again each time: a game car another of your cars already replaces says so (one car each)
        _replace.Items.Clear(); _replaceCars.Clear();
        _replace.Items.Add(NoReplace); _replaceCars.Add(null);
        foreach (var x in CarUi.Pickable().OrderBy(x => x.Class).ThenBy(x => Skins.Nice(x.Name), StringComparer.OrdinalIgnoreCase))
        {
            string? by = ReplacedBy(x.Name, c);
            _replace.Items.Add($"[{CarUi.Classes[x.Class]}]  {Skins.Nice(x.Name)}{(by != null ? $"   (already replaced by {by})" : "")}"); _replaceCars.Add(x.Name);
        }
        if (c.Truthy("replace") && !_replaceCars.Contains(c.Str("replace"))) c["replace"] = null;
        _replace.SelectedIndex = c.Truthy("replace") ? _replaceCars.IndexOf(c.Str("replace")) : 0;
        ReplaceNote();
        var engines = SafeEngines();
        string Eng(string car) => engines.TryGetValue(car, out var e) ? $"   ({CarSoundPreview.Nice(e)})" : "";
        _sound.Items.Clear(); _soundCars.Clear();
        _sound.Items.Add($"{SameSound} ({Skins.Nice(c.Str("base")!)}){Eng(c.Str("base")!)}"); _soundCars.Add(null);
        foreach (var x in CarUi.Pickable().Select(x => x.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) { _sound.Items.Add(Skins.Nice(x) + Eng(x)); _soundCars.Add(x); }
        if (c.Truthy("sound") && !_soundCars.Contains(c.Str("sound"))) c["sound"] = null;
        _sound.SelectedIndex = c.Truthy("sound") ? _soundCars.IndexOf(c.Str("sound")) : 0;
        string? own = c.Truthy("sound_own") ? c.Str("sound_own") : c.Truthy("sound_own_file") ? c.Str("sound_own_file") : null;
        _ownOn.Checked = c.Truthy("sound_own");
        _ownFile.Text = own == null ? "(no file yet)" : File.Exists(OwnSound.FilePath(own)) ? own : own + "  (missing!)";
        _ownFile.ForeColor = own != null && !File.Exists(OwnSound.FilePath(own)) ? Theme.Bad : Theme.Text;
        _ownRpm.Value = c.Num("sound_rpm", OwnSound.DefaultRpm);
        ShowKinds();
        ShowSound();
        bool glb = c.Truthy("body_glb");
        bool rep = c.Truthy("replace");
        _bodyBtn.Enabled = glb || rep; _base.Enabled = !glb;
        foreach (var r in _cls) r.Enabled = !rep;     // a replaced car keeps its game class

        bool paintable = !glb && !rep;      // an imported body: its paint parts (named like paint / car skin in the .glb) take these colours
        foreach (var b in _paintB) b.Enabled = paintable;     // a replaced car keeps its game paints (you pick them in game)
        foreach (var p in _paint) p.Cursor = paintable ? Cursors.Hand : Cursors.Default;
        _paintNote.Text = rep ? "a replaced car keeps the game car's paints (pick them in game)" : glb ? (CarModel.PaintBody(c) ? "an imported body: parts named like paint / car skin take these colours, the rest keeps its colours (from its .glb)" : "paint colours for imported bodies: still under work. An imported body keeps its own colours (from its .glb)") : "";
        _fitBox.Visible = glb;
        _glbNote.Text = glb ? "✔ your body: " + c.Str("body_glb") : "";
        _clearGlass.Visible = glb; _clearGlass.Checked = CarModel.ClearGlass(c);
        _paintBody.Visible = false; _paintBody.Checked = CarModel.PaintBody(c);      // hidden: imported cars keep their own colours
        _base.Enabled = !glb;
        if (glb)
        {
            var q = c.Obj("glb_fit") ?? new JObj();
            string S(double v) => v.ToString("+0.00;-0.00;0.00");
            _fitVals["size"].Text = "x" + q.Num("size", 1.0).ToString("0.00");
            _fitVals["fwd"].Text = S(q.Num("fwd")); _fitVals["up"].Text = S(q.Num("up"));
            _fitVals["track"].Text = S(CarGlb.Track(q, false)); _fitVals["track_r"].Text = S(CarGlb.Track(q, true)); _fitVals["wb"].Text = S(q.Num("wb"));
            _turnBtn.Text = $"↻ Turn 90°  (now {q.Int("turn")}°)";
            RoomText(c);
        }
        for (int i = 0; i < 2; i++) _paint[i].BackColor = ColorTranslator.FromHtml((string)c.Arr("paint")![i]!);
        ShowMini(c);
        ShowLogo(c);
        ShowFlames();
        _loading = false;
        ShowStats();
        Draw(true);
    }

    string GlbPath(JObj c) => Path.Combine(Paths.Tracks, "cars", c.Str("body_glb")!);

    void ReplaceNote()
    {
        var c = Car();
        if (c == null) return;
        if (c.Truthy("replace"))
        {
            string n = Skins.Nice(c.Str("replace")!);
            _replaceNote.Visible = true; _replaceNote.Text = $"In game the {n} gets this car's body, wheels and sound - online too, on your screen (other players see the normal {n}). Its wheels sit where yours do; the rest of its handling, its stats and class stay the {n}'s. It is not added as a new car. You see it after Build + install.";
            _replaceNote.ForeColor = ColorTranslator.FromHtml("#ffd27a");
        }
        else
        {
            _replaceNote.Text = ""; _replaceNote.Visible = false;
            _replaceNote.ForeColor = ColorTranslator.FromHtml("#9aa8b8");
        }
        if (_statsCtrls.Count > 0) ShowPage(_soundTab);
    }

    void RoomText(JObj c)
    {
        try
        {
            string glb = GlbPath(c);
            var (p, t) = CarUi.Need(glb); var (v, _) = CarUi.Room(CarGlb.Container);
            if (CarUi.Fits(CarGlb.Container, glb)) { _roomLabel.Text = $"✔ The body fits ({p} of {v} points of room used)."; _roomLabel.ForeColor = Theme.Good; }
            else
            {
                _roomLabel.Text = $"⚠ The body model has room for only {100 * v / Math.Max(p, 1)}% of this body ({v} of {p} points): in game parts of it will be MISSING. Make the .glb smaller (Blender: Decimate) - to about {(int)(t * (double)v / Math.Max(p, 1) * 0.95)} triangles.";
                _roomLabel.ForeColor = ColorTranslator.FromHtml("#ffb020");
            }
        }
        catch (Exception e) { _roomLabel.Text = "(room check failed: " + e.Message + ")"; _roomLabel.ForeColor = Theme.Text; }
    }

    void ShowMini(JObj c)
    {
        try
        {
            if (_mini.Items.Count == 0) { _mini.Items.Add(MiniAuto); foreach (var s in CarUi.MiniShapes()) _mini.Items.Add(s); }
            string pick = CarUi.Mini(c);
            _mini.SelectedItem = pick == CarIcon.Auto ? MiniAuto : pick;
            var old = _miniPic.Image;
            _miniPic.Image = CarUi.MiniPicture(c, 150, Theme.Bg);
            old?.Dispose();
        }
        catch (Exception) { _miniPic.Image = null; }
    }

    void ClassChanged(int cls)
    {
        var c = Car();
        if (c == null || _loading || cls == c.Int("class")) return;
        var bas = CarsData.ClassCars(cls)[0];
        if (c.Truthy("body_glb")) bas = CarsData.ClassCars(cls).FirstOrDefault(x => x.Name == CarGlb.GlbBody[cls]) ?? bas;
        Edit(q => { q["class"] = (long)cls; q["base"] = bas.Name; q["stats"] = CarUi.StatsOf(bas); CarUi.Fair(q); });
        _loading = true; _list.Items[Sel] = Label(Car()!); _loading = false;
        Load2();
    }

    /// "Use the game body": no imported body. When this car replaces a game car: back to that car exactly as it is (its body, its wheels)
    void GameBody()
    {
        var c = Car();
        if (c == null) return;
        var t = c.Truthy("replace") ? CarsData.Cars().FirstOrDefault(x => x.Name == c.Str("replace")) : null;
        if (t == null) { Set("body_glb", null); return; }
        Edit(q => { q["body_glb"] = null; q["class"] = (long)t.Class; q["base"] = t.Name; q["icon"] = t.Icon; q["wheels"] = null; q["stats"] = CarUi.StatsOf(t); CarUi.Fair(q); });
        _loading = true; _list.Items[Sel] = Label(Car()!); _loading = false;
        Load2();
        _f.Say($"Back to the {Skins.Nice(t.Name)} as it is: its own body and wheels. Pick another body or wheels to change it.", Theme.Good);
    }

    /// a game car to replace: start from that car as it is (its class, body and wheels), so only what you change is different
    void ReplacePicked(string? target)
    {
        var c = Car();
        if (c == null) return;
        var t = target == null ? null : CarsData.Cars().FirstOrDefault(x => x.Name == target);
        if (t == null) { Set("replace", null); ReplaceNote(); return; }
        if (ReplacedBy(t.Name, c) is { } other)
        {
            _f.Say($"The {Skins.Nice(t.Name)} is already replaced by your car \"{other}\" - a game car can be replaced by one of your cars only. Pick another, or change \"{other}\" first.", Theme.Warn);
            Load2(); return;
        }
        Edit(q =>
        {
            q["replace"] = t.Name; q["class"] = (long)t.Class;
            if (!q.Truthy("body_glb")) { q["base"] = t.Name; q["icon"] = t.Icon; q["wheels"] = null; }
            else q["base"] = CarGlb.GlbBody[t.Class];
            q["stats"] = CarUi.StatsOf(t); CarUi.Fair(q);
        });
        _loading = true; _list.Items[Sel] = Label(Car()!); _loading = false;
        Load2();
    }

    /// the name of your other car that already replaces this game car (or null)
    string? ReplacedBy(string gameCar, JObj me)
    {
        foreach (var o in Cars().OfType<JObj>())
            if (!ReferenceEquals(o, me) && string.Equals(o.Str("replace"), gameCar, StringComparison.OrdinalIgnoreCase)) return o.Str("name");
        return null;
    }

    void BaseChanged()
    {
        var bas = CarsData.Cars().First(x => x.Name == (string)_base.SelectedItem!);
        Edit(q => { q["base"] = bas.Name; q["icon"] = bas.Icon; q["stats"] = CarUi.StatsOf(bas); CarUi.Fair(q); });
        Load2();
    }

    void Paint(int i)
    {
        var c = Car();
        if (c == null || c.Truthy("body_glb") || c.Truthy("replace")) return;
        using var d = new ColorDialog { FullOpen = true, Color = ColorTranslator.FromHtml((string)c.Arr("paint")![i]!) };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        string col = $"#{d.Color.R:x2}{d.Color.G:x2}{d.Color.B:x2}";
        Edit(q => q.Arr("paint")![i] = col);
        Load2();
    }

    public void SetStat(string key, long value)
    {
        var c = Car();
        if (_loading || c == null || c.Obj("stats")!.Int(key) == value) return;
        int cls = (int)c.Int("class");
        var (lo, hi) = CarsData.Limit(cls, key);
        long budget = CarUi.Budget(cls), room = budget - (CarUi.Total(c) - c.Obj("stats")!.Int(key)), want = value;
        value = Math.Max(lo, Math.Min(value, Math.Min(room, hi)));
        if (value != c.Obj("stats")!.Int(key)) Edit(q => q.Obj("stats")![key] = value);
        if (value < want) _f.Say(value == room ? $"No points left: lower another stat first (the class's budget is {budget})" : $"That is the most any class {CarUi.Classes[cls]} car has");
        else if (value > want) _f.Say($"That is the least any class {CarUi.Classes[cls]} car has");
        ShowStats();
    }

    void Step(string key, int d) { var c = Car(); if (c != null) SetStat(key, c.Obj("stats")!.Int(key) + d); }

    void ChartClick(MouseEventArgs e)
    {
        if (Car() == null) return;
        int i = e.Y / RowH;
        if (i >= 0 && i < CarUi.StatRows.Length) { const int x0 = 10, x1 = 520 - 60; SetStat(CarUi.StatRows[i].Key, Py.Round((e.X - x0) * 10.0 / (x1 - x0))); }
    }

    void ShowStats()
    {
        var c = Car();
        if (c == null) return;
        int cls = (int)c.Int("class");
        long budget = CarUi.Budget(cls), left = budget - CarUi.Total(c);
        _points.Text = $"Points used: {CarUi.Total(c)} / {budget}   ({left} left)"; _points.ForeColor = left == 0 ? Theme.Orange : Theme.Text;
        var (m0, m1) = CarUi.Mph(cls);
        _top.Text = $"Top speed about {CarsData.TopSpeed(cls, c.Obj("stats")!.Int("speed"))} mph  (class {CarUi.Classes[cls]}: {m0} - {m1} mph)";
        foreach (var (k, _) in CarUi.StatRows) _vals[k].Text = c.Obj("stats")!.Int(k).ToString();
        _chart.Invalidate();
    }

    void ChartPaint(object? s, PaintEventArgs e)
    {
        var c = Car();
        if (c == null) return;
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        const float x0 = 10, x1 = 520 - 60;
        float X(double v) => x0 + (x1 - x0) * (float)v / 10f;
        int cls = (int)c.Int("class"); var cars = CarsData.ClassCars(cls);
        int i = 0;
        using var field = new SolidBrush(Theme.Field); using var glass = new SolidBrush(Theme.Glass); using var orange = new SolidBrush(Theme.Orange);
        using var edge = new Pen(Theme.GlassHi); using var muted = new Pen(ColorTranslator.FromHtml("#7f8a96")); using var head = new Pen(Theme.Head, 2);
        foreach (var (key, _) in CarUi.StatRows)
        {
            float y = i * RowH + RowH / 2f;
            var (lo, hi) = CarsData.Limit(cls, key);
            g.FillRectangle(field, X(0), y - 3, X(10) - X(0), 6);
            g.FillRectangle(glass, X(lo), y - 7, X(hi) - X(lo), 14); g.DrawRectangle(edge, X(lo), y - 7, X(hi) - X(lo), 14);
            foreach (var q in cars) g.DrawLine(muted, X(q.Stats[key]), y - 9, X(q.Stats[key]), y + 9);
            double avg = cars.Average(q => (double)q.Stats[key]);
            g.DrawLine(head, X(avg), y - 12, X(avg), y + 12);
            long v = c.Obj("stats")!.Int(key);
            g.FillRectangle(orange, X(0), y - 4, X(v) - X(0), 8);
            g.FillEllipse(orange, X(v) - 7, y - 7, 14, 14); g.DrawEllipse(Pens.White, X(v) - 7, y - 7, 14, 14);
            i++;
        }
    }

    /// a picture into maps\cars\logos\, then this car shows it as its maker logo in game (after Build + install)
    void ImportLogo()
    {
        var c = Car();
        if (c == null) return;
        string? path = _f.PickFile("Logo picture", "Pictures|*.png;*.jpg;*.jpeg;*.bmp");
        if (path == null) return;
        try { using var b = new Bitmap(path); if (b.Width < 8 || b.Height < 8) throw new ArgumentException("the picture is too small"); }
        catch (Exception e) when (e is ArgumentException or IOException or OutOfMemoryException) { _f.Say("This picture can't be used: " + e.Message, Theme.Bad); return; }
        string folder = Path.Combine(Paths.Tracks, "cars", CarLogo.Folder);
        Directory.CreateDirectory(folder);
        string name = Path.GetFileName(path), dst = Path.Combine(folder, name);
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dst, true);
        Set("logo", name);
        _f.Say($"Logo {name} is on {c.Str("name")}. You see it in the garage after Build + install.", Theme.Good);
    }

    void ShowLogo(JObj c)
    {
        var old = _logoPic.Image; _logoPic.Image = null; old?.Dispose();
        string? file = c.Truthy("logo") ? c.Str("logo") : null;
        if (file == null) { _logoNote.Text = "the maker's game logo"; _logoNote.ForeColor = Muted; return; }
        string p = CarLogo.FilePath(file);
        try
        {
            using var b = new Bitmap(p);
            _logoPic.Image = new Bitmap(b);
            _logoNote.Text = file; _logoNote.ForeColor = Theme.Good;
        }
        catch (Exception e) when (e is ArgumentException or IOException or OutOfMemoryException)
        {
            _logoNote.Text = $"{file} is missing - the game logo stays"; _logoNote.ForeColor = Theme.Warn;
        }
    }

    /// a menu of the game cars by class; the one picked is saved as a .glb
    void ExportMenu(Control at)
    {
        var menu = new ContextMenuStrip();
        foreach (var grp in CarUi.Pickable().GroupBy(g => g.Class).OrderBy(g => g.Key))
        {
            var sub = new ToolStripMenuItem($"Class {CarUi.Classes[grp.Key]}");
            foreach (var gc in grp.OrderBy(g => Skins.Nice(g.Name)))
            {
                string car = gc.Name;
                sub.DropDownItems.Add(Skins.Nice(car), null, (_, _) => ExportCar(car));
            }
            menu.Items.Add(sub);
        }
        menu.Show(at, new Point(0, at.Height));
    }

    void ExportCar(string car)
    {
        string? path = _f.SaveAs("Save the game car's model", "glTF binary|*.glb", car + ".glb");
        if (path == null) return;
        _f.Say($"Saving {Skins.Nice(car)}...");
        Task.Run(() =>
        {
            try { File.WriteAllBytes(path, CarExport.Glb(car)); return (string?)null; }
            catch (Exception e) when (e is not OutOfMemoryException) { return e.Message; }
        }).ContinueWith(t =>
        {
            if (t.Result != null) _f.Say("Cannot save the car: " + t.Result, Theme.Bad);
            else _f.Say($"Saved {Path.GetFileName(path)} - open it in Blender. To use it as a body, make it smaller (Decimate) to about {CarUi.GlbMaxTris} triangles, then Import body.", Theme.Good);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void ImportBody()
    {
        var c0 = Car();
        if (c0 == null) return;
        string? path = _f.PickFile("Car body (.glb)", "glTF binary|*.glb");
        if (path == null) return;
        int n;
        try { n = CarModel.ReadGlbBody(path).Tri.Length / 9; }
        catch (Exception e) when (e is not OutOfMemoryException) { _f.Say("This .glb can't be used: " + e.Message); return; }
        // the real room check (the body car's model), not a fixed number: warn only when it does not fit
        bool fits = true; int p = 0, v = 0, max = 0;
        try { fits = CarUi.Fits(CarGlb.Container, path); p = CarUi.Need(path).P; v = CarUi.Room(CarGlb.Container).V; max = CarUi.GlbMaxTris; }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        if (!fits && _f.Msg("Car body", $"This body is too big for the car model: it has {n} triangles and needs {p} points, the model has room for {v} points (about {max} triangles).\n\nThe parts that don't fit would be missing in the game (holes).\n\nMake it smaller in your 3D program first (Blender: Decimate modifier), or import it anyway?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        string folder = Path.Combine(Paths.Tracks, "cars");
        Directory.CreateDirectory(folder);
        string name = Path.GetFileName(path), dst = Path.Combine(folder, name);
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dst, true);
        Set("body_glb", name);
        var c = Car()!;
        string msg = $"Body imported: {name} ({n} triangles), on {c.Str("base")!.Replace('_', ' ')}";
        if (!CarUi.Fits(CarGlb.Container, GlbPath(c))) msg +=" - TOO BIG for it: see the warning under the fit buttons";
        _f.Say(msg);
    }

    void Fit(string? key, double d)
    {
        var c = Car();
        var q = c?.Obj("glb_fit") != null ? Share.Copy(c.Obj("glb_fit")!) : new JObj();
        if (key == null) q = new JObj();
        else if (key == "turn") q["turn"] = Py.Mod(q.Int("turn") + d, 360);
        else
        {
            // the first change of one axle's width: the other keeps where it is (before, one number moved both)
            if (key is "track" or "track_r" && !q.ContainsKey("track_r")) q["track_r"] = q.Num("track");
            q[key] = Py.RoundN(q.Num(key, key == "size" ? 1.0 : 0.0) + d, 3);
        }
        Set("glb_fit", q);
    }

    void Draw(bool full)
    {
        var c = Car();
        if (c == null || !Visible) { _view.Image = null; return; }
        int w = _view.Width, h = _view.Height;
        if (w < 50 || h < 50) return;
        string key = Json.Dumps(new List<object?> { c["base"], c["wheels"], c["body_glb"], c["glb_fit"], c["clear_glass"], c["paint_body"] }, -1);
        var col = ColorTranslator.FromHtml((string)c.Arr("paint")![0]!);
        byte[] paint = { col.R, col.G, col.B };
        int job = ++_job, size = Math.Max(60, Math.Min(w, h)); double yaw = _yaw, pitch = _pitch, zoom = _zoom, panX = _panX, panY = _panY;
        var info = new CarUi.ViewInfo();
        var car = Share.Copy(c);
        var known = _parts; string pk = _partsKey;
        Task.Run(() =>
        {
            Bitmap? bm = null; string? err = null; CarUi.Parts? parts = known;
            try
            {
                if (key != pk) parts = CarUi.CarParts(car);
                if (job == _job) bm = CarUi.Picture(parts!, paint, size, yaw, full, pitch, info, zoom, panX, panY);
            }
            catch (Exception e) { err = "This car can't be shown: " + e.Message; }
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(() =>
                {
                    if (parts != null) { _parts = parts; _partsKey = key; }
                    if (err != null) { _hint.Text = err; return; }
                    if (job != _job || bm == null) { bm?.Dispose(); return; }
                    _hint.Text = "drag = turn the car";
                    var old = _view.Image; _view.Image = bm; old?.Dispose();
                    _viewInfo = info; _wheelsAt = parts != null ? CarUi.Wheels(parts) : new();
                    ShowFlames();
                    _bodyLo = _bodyHi = null;
                    if (parts != null && parts.Kinds.TryGetValue("glb", out var gt) && gt.Length >= 3)
                    {
                        _bodyLo = new double[] { 1e30, 1e30, 1e30 }; _bodyHi = new double[] { -1e30, -1e30, -1e30 };
                        for (int i = 0; i < gt.Length; i++) { _bodyLo[i % 3] = Math.Min(_bodyLo[i % 3], gt[i]); _bodyHi[i % 3] = Math.Max(_bodyHi[i % 3], gt[i]); }
                    }
                    _hint.Text = Car()?.Truthy("body_glb") == true ? "drag = turn the car,  right-drag = move,  mouse wheel = zoom.   Handles: blue circles = wheels,  orange = move the body,  diamond = its size,  fire = exhaust flames" : "drag = turn the car,  right-drag = move,  mouse wheel = zoom.   Fire dots = exhaust flames (drag them)";
                    PictureDone?.Invoke();
                });
        });
    }

    public event Action? PictureDone;

    void ZoomReset() { _zoom = 1; _panX = _panY = 0; }

    /// zoom by k; with a mouse point the spot under it stays where it is (else the middle of the picture)
    void ZoomBy(double k, Point? at)
    {
        double z = Math.Clamp(_zoom * k, 0.5, 8);
        if (z == _zoom) return;
        // the mouse point from the middle of the picture (the picture is centred in the control)
        double sx = 0, sy = 0;
        if (at is { } p) { sx = p.X - _view.Width / 2.0; sy = p.Y - _view.Height / 2.0; }
        _panX = sx - (sx - _panX) * z / _zoom; _panY = sy - (sy - _panY) * z / _zoom;
        _zoom = z;
        if (_zoom <= 1.0001) { _panX = _panY = 0; }
        Draw(false);
        _zoomDone.Stop(); _zoomDone.Start();
    }
    readonly System.Windows.Forms.Timer _zoomDone = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer _flameAnim = new() { Interval = 33 };     // the little fires flicker

    void OrbitToggle() { if (_orbit.Enabled) OrbitStop(); else { if (_pitch < -1.2) _pitch = -0.35; _orbit.Start(); if (_orbitBtn != null) _orbitBtn.Text = "■ Stop"; } }

    void OrbitStop()
    {
        if (!_orbit.Enabled) return;
        _orbit.Stop();
        if (_orbitBtn != null) _orbitBtn.Text = "⟳ Orbit";
        Draw(true);
    }

    // ---- exhaust flame handles: where the flames come out (every car)
    void ShowFlames()
    {
        var c = Car();
        if (c == null) { _flames = new(); return; }
        _flames = CarExhaust.Points(c);
        bool own = c.Truthy("exhaust");
        _flameNote.Text = _flames.Count == 0 ? "(this car has none)" : $"{_flames.Count} of at most {CarExhaust.Max(c)} pipe(s), {(own ? "your spots" : "the game car's spots")} - drag the fire dots in the preview";
        _flameNote.ForeColor = own ? Theme.Good : Theme.Text;
        _view.Invalidate();
    }

    bool FlamesOn => _viewInfo != null && _view.Image != null && _flames.Count > 0 && !_dragging;

    /// the other pipe that mirrors pipe i (same height and length, the other side), or -1
    int Mirror(int i)
    {
        var p = _flames[i];
        if (Math.Abs(p[0]) < 0.03) return -1;
        for (int j = 0; j < _flames.Count; j++)
            if (j != i && Math.Abs(_flames[j][0] + p[0]) < 0.03 && Math.Abs(_flames[j][1] - p[1]) < 0.03 && Math.Abs(_flames[j][2] - p[2]) < 0.03) return j;
        return -1;
    }

    double[] FlameAtNow(int i)
    {
        var p = _flames[i];
        if (_flameDrag < 0) return p;
        if (i == _flameDrag) return new[] { p[0] + _flameD[0], p[1] + _flameD[1], p[2] + _flameD[2] };
        return p;
    }

    int FlameAt(Point q)
    {
        if (!FlamesOn) return -1;
        for (int i = 0; i < _flames.Count; i++) { var p = ViewPoint(_flames[i][0], _flames[i][1], _flames[i][2]); if (Math.Abs(p.X - q.X) < 11 && Math.Abs(p.Y - q.Y) < 11) return i; }
        return -1;
    }

    /// screen move -> car-space move along the two of the car's axes that show best on screen now
    void FlameDragTo(Point q)
    {
        var p = _flames[_flameDrag];
        var o = CarUi.Project(_viewInfo!, p[0], p[1], p[2]);
        var ax = new (double X, double Y)[3];
        for (int k = 0; k < 3; k++) { var e = (double[])p.Clone(); e[k] += 1; var s = CarUi.Project(_viewInfo!, e[0], e[1], e[2]); ax[k] = (s.X - o.X, s.Y - o.Y); }
        (int A, int B) best = (0, 2); double bestDet = 0;
        foreach (var (a, b) in new[] { (0, 2), (1, 2), (0, 1) })
        {
            double d = Math.Abs(ax[a].X * ax[b].Y - ax[a].Y * ax[b].X);
            if (d > bestDet * 1.2) { bestDet = d; best = (a, b); }
        }
        var (ia, ib) = best;
        double ux = ax[ia].X, uy = ax[ia].Y, vx = ax[ib].X, vy = ax[ib].Y, det = ux * vy - uy * vx;
        double dx = q.X - _flameFrom.X, dy = q.Y - _flameFrom.Y;
        Array.Clear(_flameD);
        if (Math.Abs(det) > 1e-3) { _flameD[ia] = Math.Round((dx * vy - dy * vx) / det, 3); _flameD[ib] = Math.Round((ux * dy - uy * dx) / det, 3); }
        var n = FlameAtNow(_flameDrag);
        _f.Say($"Pipe {_flameDrag + 1}: side {n[0]:+0.00;-0.00;+0.00} m, up {n[1]:+0.00;-0.00;+0.00} m, along {n[2]:+0.00;-0.00;+0.00} m  (let go to keep it)");
        _view.Invalidate();
    }

    void FlameDragDone()
    {
        int i = _flameDrag;
        var pts = Enumerable.Range(0, _flames.Count).Select(FlameAtNow).ToList();
        _flameDrag = -1;
        if (i < 0 || _flameD.All(v => v == 0)) { _view.Invalidate(); return; }
        Array.Clear(_flameD);
        SaveFlames(pts);
    }

    void SaveFlames(List<double[]> pts) => Set("exhaust", pts.Select(p => (object?)p.Select(v => (object?)Py.RoundN(v, 3)).ToList()).ToList());

    /// one pipe more (the twin of the last one on the other side, or next to it) or less (never none)
    void FlamePipes(int d)
    {
        if (Car() == null || _flames.Count == 0) { _f.Say("This car has no exhaust flames", Theme.Warn); return; }
        var pts = _flames.Select(p => (double[])p.Clone()).ToList();
        if (d < 0) { if (pts.Count <= 1) { _f.Say("A car needs at least one pipe", Theme.Warn); return; } pts.RemoveAt(pts.Count - 1); }
        else
        {
            int max = CarExhaust.Max(Car()!);
            if (pts.Count >= max) { _f.Say(Car()!.Truthy("replace") ? $"The car it replaces has {max} pipe(s) - that is the most" : "A new car has at most 2 pipes", Theme.Warn); return; }
            var l = pts[^1];
            var twin = new[] { -l[0], l[1], l[2] };
            bool taken = pts.Any(p => Math.Abs(p[0] - twin[0]) < 0.05 && Math.Abs(p[1] - twin[1]) < 0.05 && Math.Abs(p[2] - twin[2]) < 0.05);
            pts.Add(taken ? new[] { l[0] + (l[0] >= 0 ? 0.1 : -0.1), l[1], l[2] } : twin);
        }
        SaveFlames(pts);
    }

    void FlameHandles(Graphics g)
    {
        if (!FlamesOn) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        double time = Environment.TickCount64 / 1000.0;
        for (int i = 0; i < _flames.Count; i++)
        {
            var p = FlameAtNow(i);
            var at = ViewPoint(p[0], p[1], p[2]);
            // a small flickering fire blowing backwards out of the pipe (along -z), so it is clear what the dot is
            var back = ViewPoint(p[0], p[1], p[2] - 0.35);
            float dx = back.X - at.X, dy = back.Y - at.Y, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 4) { dx = 0; dy = -1; len = 1; }         // seen from straight behind: the fire points up a little
            float ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
            // a backfire: every ~1.2 s a burst shoots out of the pipe - puffs that fly back, grow, turn from white-yellow to
            // orange to red and fade - then a short rest with only a tiny glow at the pipe
            float reach = Math.Clamp(len * 2.6f, 60f, 140f) * (float)(_zoom > 1 ? Math.Min(_zoom, 2.5) : 1);
            double ph = (time / 1.2 + i * 0.37) % 1.0;
            const int Puffs = 10;
            if (ph < 0.45)
            {
                float t = (float)(ph / 0.45);                    // 0 -> 1 over the burst
                for (int k = Puffs - 1; k >= 0; k--)
                {
                    float s = (float)k / Puffs;                    // later puffs leave later
                    float life = Math.Clamp(t * 1.7f - s * 0.7f, 0f, 1f);
                    if (life <= 0) continue;
                    float dist = reach * (1 - (1 - life) * (1 - life)) * (0.75f + 0.25f * (1 - s));   // fast out, slowing down
                    float side = (float)Math.Sin(k * 2.3 + i + time * 9) * dist * 0.08f;
                    float r = 4f + dist * 0.16f;
                    float fade = (1 - life) * (1 - t * 0.5f);
                    if (fade <= 0.02f) continue;
                    var c = life < 0.3f ? Color.FromArgb((int)(240 * fade), 255, 240, 170) : life < 0.65f ? Color.FromArgb((int)(220 * fade), 255, 150, 40) : Color.FromArgb((int)(190 * fade), 230, 70, 20);
                    float px = at.X + ux * dist + nx * side, py = at.Y + uy * dist + ny * side;
                    using (var glow = new SolidBrush(Color.FromArgb((int)(c.A * 0.35f), c.R, c.G, c.B))) g.FillEllipse(glow, px - r * 1.8f, py - r * 1.8f, r * 3.6f, r * 3.6f);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, px - r, py - r, 2 * r, 2 * r);
                }
                // the flash at the pipe at the start of the burst
                if (t < 0.35f) { float fr = 9 * (1 - t / 0.35f) + 4; using var fl = new SolidBrush(Color.FromArgb((int)(230 * (1 - t / 0.35f)), 255, 250, 210)); g.FillEllipse(fl, at.X - fr, at.Y - fr, 2 * fr, 2 * fr); }
            }
            else
            {
                float gl = 3 + (float)Math.Sin(time * 20 + i) * 1.2f;
                using var idle = new SolidBrush(Color.FromArgb(110, 255, 120, 30)); g.FillEllipse(idle, at.X + ux * 4 - gl, at.Y + uy * 4 - gl, 2 * gl, 2 * gl);
            }
            using (var fill = new SolidBrush(Color.FromArgb(255, 255, 110, 30))) g.FillEllipse(fill, at.X - 5, at.Y - 5, 10, 10);
            using (var ring = new Pen(i == _flameDrag ? Color.FromArgb(255, 255, 230, 120) : Color.White, 1.5f)) g.DrawEllipse(ring, at.X - 5, at.Y - 5, 10, 10);
        }
    }

    // ---- wheel handles: only for an imported body (its fit moves the wheels)
    bool WheelHandlesOn => Car()?.Truthy("body_glb") == true && _viewInfo != null && _view.Image != null && _wheelsAt.Count > 0;
    bool WheelsFixed => false;      // (a replaced car takes this car's wheel spots too)
    readonly List<Control> _spacing = new();                   // the wheel spacing buttons (hidden for a replaced car)

    /// a wheel's middle on the preview control (the picture is centred in it)
    PointF WheelPoint(int i, double dSide = 0, double dEnd = 0)
    {
        var w = _wheelsAt[i];
        var (x, y) = CarUi.Project(_viewInfo!, w.X + w.Side * dSide, w.Y, w.Z + w.End * dEnd);
        float ox = (_view.Width - _view.Image!.Width) / 2f, oy = (_view.Height - _view.Image.Height) / 2f;
        return new PointF(x + ox, y + oy);
    }

    /// a car-space point on the preview control
    PointF ViewPoint(double x, double y, double z)
    {
        var (px, py) = CarUi.Project(_viewInfo!, x, y, z);
        return new PointF(px + (_view.Width - _view.Image!.Width) / 2f, py + (_view.Height - _view.Image.Height) / 2f);
    }

    /// the body handles: on top of the body (move), and the middle of its nose (size); with the drag so far
    PointF BodyPoint(int which)
    {
        var lo = _bodyLo!; var hi = _bodyHi!;
        double cx = (lo[0] + hi[0]) / 2, cz = (lo[2] + hi[2]) / 2;
        if (which == BodyHandle) return ViewPoint(cx, hi[1] + 0.15 + _dUp, cz + _dFwd);
        // size grows from the bottom middle: the corner moves away from it
        return ViewPoint(cx, lo[1] + _dUp + (hi[1] - lo[1]) * 0.45 * _kSize, cz + _dFwd + (hi[2] - cz) * _kSize);     // the middle of its nose
    }

    PointF BodyBase() { var lo = _bodyLo!; var hi = _bodyHi!; return ViewPoint((lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2); }

    int WheelAt(Point p)
    {
        if (!WheelHandlesOn) return -1;
        if (_bodyLo != null)
            foreach (int b in new[] { SizeHandle, BodyHandle }) { var q = BodyPoint(b); if (Math.Abs(q.X - p.X) < 14 && Math.Abs(q.Y - p.Y) < 14) return b; }
        if (WheelsFixed) return -1;
        for (int i = 0; i < _wheelsAt.Count; i++) { var q = WheelPoint(i); if (Math.Abs(q.X - p.X) < 16 && Math.Abs(q.Y - p.Y) < 16) return i; }
        return -1;
    }

    /// screen move -> car-space move along two of the car's axes (a = along the first, b = along the second)
    (double A, double B) Split(Point p, double[] at, double[] e1, double[] e2)
    {
        var o = CarUi.Project(_viewInfo!, at[0], at[1], at[2]);
        var a1 = CarUi.Project(_viewInfo!, at[0] + e1[0], at[1] + e1[1], at[2] + e1[2]); var a2 = CarUi.Project(_viewInfo!, at[0] + e2[0], at[1] + e2[1], at[2] + e2[2]);
        double ux = a1.X - o.X, uy = a1.Y - o.Y, vx = a2.X - o.X, vy = a2.Y - o.Y, det = ux * vy - uy * vx;
        double dx = p.X - _wheelFrom.X, dy = p.Y - _wheelFrom.Y;
        if (Math.Abs(det) < 1e-3)
        {
            bool first = Math.Abs(ux) + Math.Abs(uy) > Math.Abs(vx) + Math.Abs(vy);
            return first ? ((dx * ux + dy * uy) / (ux * ux + uy * uy + 1e-9), 0) : (0, (dx * vx + dy * vy) / (vx * vx + vy * vy + 1e-9));
        }
        return ((dx * vy - dy * vx) / det, (ux * dy - uy * dx) / det);
    }

    /// the mouse moved on screen -> metres sideways / along the car (from how the car's own axes show on screen)
    void WheelDragTo(Point p)
    {
        var fit0 = Car()?.Obj("glb_fit");
        if (_wheelDrag == BodyHandle && _bodyLo != null)
        {
            double[] at = { (_bodyLo[0] + _bodyHi![0]) / 2, _bodyHi[1], (_bodyLo[2] + _bodyHi[2]) / 2 };
            var (up, fwd) = Split(p, at, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 });
            _dUp = Math.Round(up, 2); _dFwd = Math.Round(fwd, 2);
            _f.Say($"Body up {(fit0?.Num("up") ?? 0) + _dUp:+0.00;-0.00;+0.00} m,  forward {(fit0?.Num("fwd") ?? 0) + _dFwd:+0.00;-0.00;+0.00} m  (let go to keep it)");
            _view.Invalidate(); return;
        }
        if (_wheelDrag == SizeHandle && _bodyLo != null)
        {
            var b0 = BodyBase(); double k0 = _kSize; _kSize = 1; var c0 = BodyPoint(SizeHandle); _kSize = k0;
            double d0 = Math.Sqrt(Math.Pow(c0.X - b0.X, 2) + Math.Pow(c0.Y - b0.Y, 2));
            double d1 = Math.Sqrt(Math.Pow(c0.X + p.X - _wheelFrom.X - b0.X, 2) + Math.Pow(c0.Y + p.Y - _wheelFrom.Y - b0.Y, 2));
            _kSize = Math.Clamp(Math.Round(d1 / Math.Max(1, d0), 3), 0.5, 2.0);
            _f.Say($"Body size x{(fit0?.Num("size", 1.0) ?? 1.0) * _kSize:0.00}  (let go to keep it)");
            _view.Invalidate(); return;
        }
        var w = _wheelsAt[_wheelDrag];
        var o = CarUi.Project(_viewInfo!, w.X, w.Y, w.Z); var ax = CarUi.Project(_viewInfo!, w.X + 1, w.Y, w.Z); var az = CarUi.Project(_viewInfo!, w.X, w.Y, w.Z + 1);
        double ux = ax.X - o.X, uy = ax.Y - o.Y, vx = az.X - o.X, vy = az.Y - o.Y, det = ux * vy - uy * vx;
        double dx = p.X - _wheelFrom.X, dy = p.Y - _wheelFrom.Y, a, b;
        if (Math.Abs(det) < 1e-3) { a = Math.Abs(ux) + Math.Abs(uy) > Math.Abs(vx) + Math.Abs(vy) ? (dx * ux + dy * uy) / (ux * ux + uy * uy + 1e-9) : 0; b = a == 0 ? (dx * vx + dy * vy) / (vx * vx + vy * vy + 1e-9) : 0; }
        else { a = (dx * vy - dy * vx) / det; b = (ux * dy - uy * dx) / det; }
        // outward from the car's middle = wider; both sides move together, so the whole width changes twice as much
        _dTrack = Math.Round(2 * a * w.Side, 2); _dWb = Math.Round(2 * b * w.End, 2);
        var q = Car()?.Obj("glb_fit");
        _dragRear = w.End < 0;
        _f.Say($"{(_dragRear ? "Rear" : "Front")} wheels side to side {(q != null ? CarGlb.Track(q, _dragRear) : 0) + _dTrack:+0.00;-0.00;+0.00} m,  front to back {(q?.Num("wb") ?? 0) + _dWb:+0.00;-0.00;+0.00} m  (let go to keep it)");
        _view.Invalidate();
    }

    void WheelDragDone()
    {
        int i = _wheelDrag; _wheelDrag = -1;
        if (i < 0 || (_dTrack == 0 && _dWb == 0 && _dUp == 0 && _dFwd == 0 && _kSize == 1)) { _view.Invalidate(); return; }
        var c = Car();
        var q = c?.Obj("glb_fit") != null ? Share.Copy(c.Obj("glb_fit")!) : new JObj();
        if (i == BodyHandle) { q["up"] = Py.RoundN(q.Num("up") + _dUp, 3); q["fwd"] = Py.RoundN(q.Num("fwd") + _dFwd, 3); }
        else if (i == SizeHandle) q["size"] = Py.RoundN(q.Num("size", 1.0) * _kSize, 3);
        else
        {
            if (!q.ContainsKey("track_r")) q["track_r"] = q.Num("track");
            string tk = _dragRear ? "track_r" : "track";
            q[tk] = Py.RoundN(q.Num(tk) + _dTrack, 3); q["wb"] = Py.RoundN(q.Num("wb") + _dWb, 3);
        }
        _dTrack = _dWb = _dUp = _dFwd = 0; _kSize = 1;
        Set("glb_fit", q);
    }

    void WheelHandles(Graphics g)
    {
        if (!WheelHandlesOn || _dragging) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var blue = Color.FromArgb(255, 80, 180, 255);
        for (int i = 0; i < (WheelsFixed ? 0 : _wheelsAt.Count); i++)
        {
            var at = WheelPoint(i);
            bool on = i == _wheelDrag;
            using (var ring = new Pen(Color.FromArgb(on ? 120 : 200, blue), 2f)) g.DrawEllipse(ring, at.X - 11, at.Y - 11, 22, 22);
            if (!on) { using var dot = new SolidBrush(blue); g.FillEllipse(dot, at.X - 3, at.Y - 3, 6, 6); }
        }
        if (_bodyLo != null)
        {
            var orange = Color.FromArgb(255, 255, 150, 60);
            var mv = BodyPoint(BodyHandle); var sz = BodyPoint(SizeHandle);
            if (_wheelDrag is BodyHandle or SizeHandle)
            {
                // the body's outline where it goes
                var lo = _bodyLo; var hi = _bodyHi!; double cx = (lo[0] + hi[0]) / 2, cz = (lo[2] + hi[2]) / 2;
                PointF C(int ix, int iy, int iz)
                {
                    double x = ix == 0 ? lo[0] : hi[0], y = iy == 0 ? lo[1] : hi[1], z = iz == 0 ? lo[2] : hi[2];
                    return ViewPoint(cx + (x - cx) * _kSize, lo[1] + _dUp + (y - lo[1]) * _kSize, cz + _dFwd + (z - cz) * _kSize);
                }
                using var box = new Pen(Color.FromArgb(170, 255, 210, 90), 1.3f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                foreach (var (a, b) in new[] { ((0, 0, 0), (1, 0, 0)), ((0, 0, 1), (1, 0, 1)), ((0, 1, 0), (1, 1, 0)), ((0, 1, 1), (1, 1, 1)), ((0, 0, 0), (0, 0, 1)), ((1, 0, 0), (1, 0, 1)), ((0, 1, 0), (0, 1, 1)), ((1, 1, 0), (1, 1, 1)), ((0, 0, 0), (0, 1, 0)), ((1, 0, 0), (1, 1, 0)), ((0, 0, 1), (0, 1, 1)), ((1, 0, 1), (1, 1, 1)) })
                    g.DrawLine(box, C(a.Item1, a.Item2, a.Item3), C(b.Item1, b.Item2, b.Item3));
            }
            using (var fill = new SolidBrush(orange)) g.FillEllipse(fill, mv.X - 7, mv.Y - 7, 14, 14);
            using (var ring = new Pen(Color.White, 1.5f)) g.DrawEllipse(ring, mv.X - 7, mv.Y - 7, 14, 14);
            using (var arrows = new Pen(Color.FromArgb(30, 20, 10), 1.5f)) { g.DrawLine(arrows, mv.X, mv.Y - 4, mv.X, mv.Y + 4); g.DrawLine(arrows, mv.X - 4, mv.Y, mv.X + 4, mv.Y); }
            var dia = new[] { new PointF(sz.X, sz.Y - 8), new PointF(sz.X + 8, sz.Y), new PointF(sz.X, sz.Y + 8), new PointF(sz.X - 8, sz.Y) };
            using (var fill = new SolidBrush(Color.FromArgb(255, 255, 210, 90))) g.FillPolygon(fill, dia);
            using (var ring = new Pen(Color.White, 1.5f)) g.DrawPolygon(ring, dia);
        }
        if (_wheelDrag < 0 || _wheelDrag >= BodyHandle) return;
        // where the dragged wheel goes, and the other three with it
        for (int i = 0; i < _wheelsAt.Count; i++)
        {
            bool same = (_wheelsAt[i].End < 0) == _dragRear;      // only the dragged axle gets wider; the length moves all four
            var from = WheelPoint(i); var to = WheelPoint(i, same ? _dTrack / 2 : 0, _dWb / 2);
            using var line = new Pen(Color.FromArgb(160, 255, 210, 90), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(line, from, to);
            using var hit = new SolidBrush(Color.FromArgb(230, 255, 210, 90)); g.FillEllipse(hit, to.X - 7, to.Y - 7, 14, 14);
        }
    }
}

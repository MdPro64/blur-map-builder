// The 3D tab's "Animation" sub-tab: the start camera, and animations of your placed objects (docs\object_animation.md).
// A keyframe panel at the bottom in the style of After Effects: the animated objects, each with Position / Rotation rows,
// a stopwatch per row, a timeline with a playhead, keys as diamonds (click, drag in time, Delete), Play / Stop.
// What the game can play (and so what can be made here): keyframes (position + turn), follow a path, spin in place;
// start with the race or by a trigger box. Size and tilt cannot change in the game, so those rows are greyed.
using Bmb.Core;

namespace Bmb.App;

public sealed partial class View3D
{
    public string Sub = "objects";
    public bool AnimTab => Active && SceneMode == "3d" && Sub == "anim";
    public double PlayT;
    public bool Playing;
    DateTime _playStart; double _playFrom;
    Panel _animDock = null!; Splitter _animSash = null!; FlowLayoutPanel _animSet = null!; AnimTimeline _tl = null!;
    // the Animation sub-tab's right column: the object list on top, the selected object's animation settings below
    Panel _animCol = null!; Splitter _animColSash = null!, _colSplit = null!;
    (Control Home, int Index)? _outlHome; int _colTopH;
    Label _animTime = null!, _animInfo = null!; Button _playBtn = null!;
    RadioButton _subObj = null!, _subAnim = null!; Control _subRow = null!;
    readonly HashSet<string> _moving = new();
    readonly Dictionary<string, (double[] P, double T)> _rest = new();
    public string? PathEditing;      // obj key whose path is being drawn / edited
    public string? TrigPlacing;      // obj key whose trigger box is being placed
    int _dragPt = -1;
    static readonly Color AnimBg = ColorTranslator.FromHtml("#171b22");

    // ---- the data
    public int ObjIndex(string? key) => key != null && key.StartsWith("obj:") && int.TryParse(key[4..], out int n) && n < (D.Map.Arr("objects")?.Count ?? 0) ? n : -1;
    JObj? ObjOf(string? key) { int n = ObjIndex(key); return n < 0 ? null : D.Map.Arr("objects")![n] as JObj; }
    public JObj? ObjOfPublic(string? key) => ObjOf(key);
    public JObj? AnimOf(string? key) => ObjOf(key)?.Obj("anim");

    /// why this object cannot move in the game (null = it can)
    public string? AnimNo(string? key)
    {
        var o = ObjOf(key);
        if (o == null) return key == null ? "Pick one of YOUR placed objects: click it in the 3D view or in the object list." : "Only your placed objects can move (not the road's own objects, track points or power-ups).";
        var e = Layout.Catalog().GetValueOrDefault(o.Str("id") ?? "");
        if (e == null) return "This object is not in the library any more.";
        string kind = e.Str("kind") ?? "";
        if (Terrain.IsRamp(e)) return "Jump ramps are part of the ground: they cannot move.";
        if (kind == "breakable") return "Breakable objects (lamps, signs ...) cannot move in the game. Scenery, movable, animated and your imported models can.";
        return null;
    }

    public List<string> AnimatedKeys()
    {
        var o = new List<string>();
        var objs = D.Map.Arr("objects");
        if (objs == null) return o;
        for (int i = 0; i < objs.Count; i++) if (objs[i] is JObj j && j.Obj("anim") != null && AnimNo("obj:" + i) == null) o.Add("obj:" + i);
        return o;
    }

    /// the name as the object list shows it
    public string ObjName(string key)
    {
        var o = ObjOf(key);
        var e = o == null ? null : Layout.Catalog().GetValueOrDefault(o.Str("id") ?? "");
        return $"{e?.Str("name") ?? key}  [{key}]";
    }

    /// store an object's animation (null = none); Ctrl+Z undoes
    public void AnimSet(string key, JObj? a, string say)
    {
        int n = ObjIndex(key);
        if (n < 0) return;
        D.Snapshot();
        var o = (JObj)D.Map.Arr("objects")![n]!;
        if (a != null) o["anim"] = a; else o.Remove("anim");
        D.Touched(); Edited?.Invoke();
        _f.Say(say + " - Ctrl+Z undoes, Build + install puts it in the game");
        FillOutliner();
        AnimChanged();
    }

    JObj AnimCopy(string key) => Share.Copy(AnimOf(key) ?? new JObj());

    void AnimChanged()
    {
        AnimRefresh();
        AnimPosePicks();
        AnimSettings();
        _tl?.Invalidate();
        G.Invalidate();
    }

    // ---- the panels
    void MakeAnimPanels(Panel centre, FlowLayoutPanel top)
    {
        _subObj = Theme.Radio("▦ Objects", "#2a3a52", () => SetSub("objects"));
        _subAnim = Theme.Radio("▶ Animation", "#2a3a52", () => SetSub("anim"));
        foreach (var r in new[] { _subObj, _subAnim }) { r.Font = Theme.Bold; r.FlatAppearance.CheckedBackColor = ColorTranslator.FromHtml("#c08a1a"); }
        _subObj.Checked = true;
        Look.Tip(_subObj, "Add, move, turn and size your objects");
        Look.Tip(_subAnim, "The start camera, and objects that move: keyframes, a path, spin, a trigger box");
        // fixed columns (a flow panel showed them the other way round in Russian)
        var subs = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 10, 0), BackColor = Color.Transparent };
        subs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); subs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        subs.Controls.Add(_subObj, 0, 0); subs.Controls.Add(_subAnim, 1, 0);
        top.Controls.Add(subs); top.Controls.SetChildIndex(subs, 0);
        _subRow = subs;

        _animDock = new Panel { Dock = DockStyle.Bottom, Height = 330, BackColor = AnimBg };
        _animSet = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Pn, Padding = new Padding(6, 4, 4, 4) };
        Look.DarkScroll(_animSet);
        var right = new Panel { Dock = DockStyle.Fill, BackColor = AnimBg };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Pn, Padding = new Padding(2), WrapContents = true };
        Button B(string t, Action a, string tip) { var b = Theme.Btn(t, a, Pn); b.ForeColor = Fg; Look.Tip(b, tip); bar.Controls.Add(b); return b; }
        B("⏮", () => { AnimStop(); PlayT = 0; AnimTimeMoved(); }, "To the start (Home)");
        _playBtn = B("▶ Play", AnimPlayToggle, "Play / pause the preview (Space)");
        B("■ Stop", () => { AnimStop(); PlayT = 0; AnimTimeMoved(); }, "Stop and go back to the start");
        _animTime = new Label { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#ffd23b"), Font = Theme.Bold, Margin = new Padding(10, 6, 10, 0), Text = "0.00 s" };
        bar.Controls.Add(_animTime);
        B("◆ Key here", AnimKeyHere, "Add a key at the time marker for each row whose stopwatch is on (keyframes only)");
        B("✖ Delete key", AnimDeleteKeys, "Delete the selected keys (Delete)");
        B("↔ Fit", () => _tl.Fit(), "Show the whole animation in the timeline");
        _animInfo = new Label { AutoSize = true, ForeColor = ColorTranslator.FromHtml("#8a97a6"), Margin = new Padding(10, 6, 4, 0), UseMnemonic = false };
        bar.Controls.Add(_animInfo);
        _tl = new AnimTimeline(this) { Dock = DockStyle.Fill };
        right.Controls.Add(_tl); right.Controls.Add(bar);
        _tl.BringToFront();
        _animDock.Controls.Add(right);
        _animSash = _f.PanelSash(DockStyle.Bottom, "animation", _animDock, 330, true);
        centre.Controls.Add(_animDock); centre.Controls.Add(_animSash);
        _animDock.Visible = false; _animSash.Visible = false;
        // the right column (the map panel is hidden in this sub-tab): the object list moves in while it is open
        _animCol = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = Pn };
        _colSplit = new Splitter { Dock = DockStyle.Top, Height = 5, BackColor = ColorTranslator.FromHtml("#2a313c"), MinSize = 90, MinExtra = 120 };
        _animCol.Controls.Add(_animSet); _animCol.Controls.Add(_colSplit);
        _animColSash = _f.PanelSash(DockStyle.Right, "animcol", _animCol, 340);
        centre.Controls.Add(_animCol); centre.Controls.Add(_animColSash);
        _animCol.Visible = false; _animColSash.Visible = false;
    }

    public void SetSub(string s)
    {
        if (s == Sub) return;
        Sub = s;
        if (_subObj != null) { _subObj.Checked = s == "objects"; _subAnim.Checked = s == "anim"; }
        PathEditing = null; TrigPlacing = null; AnimStop();
        if (s == "anim") StopPlacing();
        ShowPanels(Active, SceneMode);
        _f.Refit();
        AnimChanged();
        PanelShow(AnimTab ? null : PickOf(Sel));
        if (AnimTab) AnimInfoLine();
    }

    /// the object list: in the Animation sub-tab at the top of the right column, else back at the right of the 3D view
    void AnimColumn(bool anim)
    {
        if (_animCol == null || _outl == null) return;
        _animCol.Visible = anim; _animColSash.Visible = anim;
        if (anim && _outl.Parent != _animCol)
        {
            _outlHome = (_outl.Parent!, _outl.Parent!.Controls.GetChildIndex(_outl));
            _animCol.Controls.Add(_outl);
            _animCol.Controls.SetChildIndex(_outl, _animCol.Controls.Count - 1);       // docked first: the top
            _outl.Dock = DockStyle.Top;
            _outl.Height = _colTopH > 0 ? _colTopH : Math.Max(160, (int)(_animCol.Height * 0.42));
        }
        else if (!anim && _outl.Parent == _animCol && _outlHome is { } h)
        {
            _colTopH = _outl.Height;
            h.Home.Controls.Add(_outl);
            h.Home.Controls.SetChildIndex(_outl, h.Index);
            _outl.Dock = DockStyle.Right;
        }
    }

    /// for the robot test: where the right column is, and the width it lives in
    public Rectangle AnimColBounds() => _animCol.Bounds;
    public int CentreWidth => _animCol.Parent?.ClientSize.Width ?? 0;
    public bool OutlinerInColumn => _outl.Parent == _animCol && _outl.Visible;

    void AnimInfoLine()
    {
        if (_animInfo == null) return;
        _animInfo.Text = PathEditing != null ? "PATH: click the ground to add points, drag a point to move it, Shift+click a point = remove it, Enter / right click = done"
            : TrigPlacing != null ? "TRIGGER BOX: click on the road where the car should start it (Esc = cancel)"
            : "";
        _animInfo.ForeColor = PathEditing != null || TrigPlacing != null ? ColorTranslator.FromHtml("#ffb050") : ColorTranslator.FromHtml("#8a97a6");
    }

    // ---- the settings of the selected object (left of the timeline)
    public void AnimSettings()
    {
        if (_animSet == null || !AnimTab) return;
        _animSet.SuspendLayout();
        foreach (Control c in _animSet.Controls.Cast<Control>().ToList()) c.Dispose();
        _animSet.Controls.Clear();
        int w = _animSet.ClientSize.Width - 16;
        Label L(string t, Color? c = null, bool bold = false) { var l = new Label { Text = t, AutoSize = true, MaximumSize = new Size(Math.Max(120, w), 0), ForeColor = c ?? Fg, Font = bold ? Theme.Bold : Theme.Ui, Margin = new Padding(2, 4, 2, 2), UseMnemonic = false }; _animSet.Controls.Add(l); return l; }
        Button Btn(string t, string bg, Action a, string? tip = null) { var b = Theme.Colored(t, bg, a); b.AutoSize = false; b.Width = Math.Max(120, w); b.Height = 28; b.TextAlign = ContentAlignment.MiddleLeft; if (tip != null) Look.Tip(b, tip); _animSet.Controls.Add(b); return b; }
        FlowLayoutPanel Row() { var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent }; _animSet.Controls.Add(f); return f; }
        L("ANIMATION", ColorTranslator.FromHtml("#ffb050"), true);
        string? key = Sel;
        string? no = AnimNo(key);
        if (no != null)
        {
            L(no, ColorTranslator.FromHtml("#9aa8b8"));
            int nObj = D.Map.Arr("objects")?.Count ?? 0;
            var can = Enumerable.Range(0, nObj).Select(i => "obj:" + i).Where(k => AnimNo(k) == null).ToList();
            if (can.Count > 0)
            {
                var moving = AnimatedKeys();
                L($"Your objects that can move: {can.Count} ({moving.Count} moving)", ColorTranslator.FromHtml("#7fd0ff"));
                foreach (var k in can.OrderBy(k => moving.Contains(k) ? 0 : 1).Take(12)) Btn((moving.Contains(k) ? "▶  " : "●  ") + ObjName(k), moving.Contains(k) ? "#3a3220" : "#24324a", () => { Select(k); AnimFrame(k); });
                if (can.Count > 12) L("... and more in the object list (right).", ColorTranslator.FromHtml("#6f7c8a"));
            }
            else L("Tip: place an object in the Objects sub-tab first (bus, container, your own model ...).", ColorTranslator.FromHtml("#6f7c8a"));
            _animSet.ResumeLayout();
            return;
        }
        var hr = Row();
        var nm = new Label { Text = ObjName(key!), AutoSize = true, MaximumSize = new Size(Math.Max(80, w - 40), 0), ForeColor = ColorTranslator.FromHtml("#f0e080"), Font = Theme.Bold, Margin = new Padding(2, 7, 4, 2), UseMnemonic = false };
        hr.Controls.Add(nm);
        var go = Theme.Colored("⌖ Show", "#24324a", () => AnimFrame(key!)); go.Margin = new Padding(2, 3, 2, 2); Look.Tip(go, "Show it and its whole movement in the 3D view"); hr.Controls.Add(go);
        var a = AnimOf(key);
        if (a == null)
        {
            L("This object stands still. Make it move:", Fg);
            Btn("◆  Keyframes: set it at times", "#2d4a6e", () => AnimStart(key!, "keys"), "Move the time marker, then move / turn the object: each change becomes a key (like After Effects)");
            Btn("〰  Follow a path", "#3a2d6e", () => AnimStart(key!, "path"), "Click points on the ground; the object drives along them");
            Btn("⟳  Spin in place", "#2d6e4a", () => AnimStart(key!, "spin"), "Turns around its up axis");
            L("The game can move and turn objects. It cannot change their size or tilt them while racing.", ColorTranslator.FromHtml("#6f7c8a"));
            _animSet.ResumeLayout();
            return;
        }
        string mode = AnimEval.Mode(a);
        var mr = Row();
        foreach (var (m, t) in new[] { ("keys", "◆ Keys"), ("path", "〰 Path"), ("spin", "⟳ Spin") })
        {
            string mm = m;
            var r = Theme.Radio(t, "#24324a", () => AnimMode(key!, mm)); r.Checked = mode == m; mr.Controls.Add(r);
        }
        var sr = Row();
        sr.Controls.Add(new Label { Text = "Starts:", AutoSize = true, ForeColor = Fg, Margin = new Padding(2, 7, 2, 0) });
        bool trig = a.Str("start") == "trigger";
        var r1 = Theme.Radio("with the race", "#24324a", () => AnimField(key!, "start", "race", "Starts with the race")); r1.Checked = !trig; sr.Controls.Add(r1);
        var r2 = Theme.Radio("trigger box", "#24324a", () => AnimTrigger(key!)); r2.Checked = trig; sr.Controls.Add(r2);
        Look.Tip(r2, "Starts when your car drives through an invisible box (then it runs once, or loops if Loop is on)");
        if (mode != "spin")
        {
            var lp = Theme.Check("Loop (start again at the end)", a.Truthy("loop"), v => AnimField(key!, "loop", v, v ? "Loops" : "Plays once"));
            lp.ForeColor = Fg; _animSet.Controls.Add(lp);
        }
        if (mode == "keys")
        {
            int nk = AnimEval.Keys(a).Count, nr = AnimEval.RKeys(a).Count;
            L($"{nk} position key(s), {nr} rotation key(s), {AnimEval.Duration(a):0.0} s", ColorTranslator.FromHtml("#7fd0ff"));
            if (KeyEditor(key!, a) == false)
                L(nk < 3 ? "How: move the time marker (top of the timeline), then press G or R and move / turn the object. With a row's stopwatch on, each change is a key at that time."
                    : "Click a key (diamond) in the timeline to type its time and place exactly.", ColorTranslator.FromHtml("#9aa8b8"));
            if (nk < 2) L("At least 2 position keys are needed before it moves in the game.", Theme.Warn);
            if (nk >= 2 && nr > 0) L("In the game the object faces the way it drives; the rotation keys add a turn to that, changed at each position key.", ColorTranslator.FromHtml("#6f7c8a"));
        }
        else if (mode == "path")
        {
            var pts = AnimEval.PathPoints(a);
            double len = pts.Count < 2 ? 0 : AnimEval.Length(AnimEval.Curve(pts, a.Truthy("closed")));
            L($"{pts.Count} point(s), {len:0} m, one pass {AnimEval.Duration(a):0.0} s", ColorTranslator.FromHtml("#7fd0ff"));
            var sp = new Spin("Speed (m/s)", a.Num("speed", 20), 1, 0.5, 120, "0.#"); sp.Set += v => AnimField(key!, "speed", Math.Round(v, 1), $"Speed {v:0.#} m/s ({v * 3.6:0} km/h)"); _animSet.Controls.Add(sp);
            var an = new Spin("Turn (°)", a.Num("angle"), 15, -180, 180, "0"); an.Set += v => AnimField(key!, "angle", Math.Round(v), $"Turned {v:0}° from the way it drives"); _animSet.Controls.Add(an);
            Look.Tip(an, "The object faces the way it drives; this turns it on top (90 = sideways, 180 = backwards)");
            var cl = Theme.Check("Closed path (back to the first point)", a.Truthy("closed"), v => AnimField(key!, "closed", v, v ? "Closed path" : "Open path")); cl.ForeColor = Fg; _animSet.Controls.Add(cl);
            bool ed = PathEditing == key;
            Btn(ed ? "✔  Done editing the path (Enter)" : "✎  Draw / edit the path", ed ? "#8a5a1a" : "#3a2d6e", () => AnimPathEdit(key!, !ed), "Click the ground to add points, drag points to move them");
            if (ed && pts.Count > 1) Btn("↶  Remove the last point", "#2a2f3a", () => AnimPathPop(key!));
            if (pts.Count < 2) L("Click at least 2 points on the ground.", Theme.Warn);
        }
        else
        {
            var rp = new Spin("Turns / minute", a.Num("rpm", 20), 5, -300, 300, "0"); rp.Set += v => AnimField(key!, "rpm", Math.Round(v), $"Spins {v:0} turns a minute"); _animSet.Controls.Add(rp);
            Look.Tip(rp, "Below 0 = the other way round");
            L("Spins around its up axis where you placed it.", ColorTranslator.FromHtml("#9aa8b8"));
        }
        if (trig)
        {
            var t = a.Obj("trigger");
            bool pl = TrigPlacing == key;
            Btn(pl ? "…  click on the road (Esc = cancel)" : t == null ? "⬚  Place the trigger box" : "⬚  Move the trigger box", pl ? "#8a5a1a" : "#5a3a1a", () => AnimTrigPlace(key!, !pl), "Click on the road where the car should start it");
            if (t != null)
            {
                var ws = new Spin("Box width (m)", t.Num("w", 40), 5, 5, 400, "0"); ws.Set += v => AnimTrigSize(key!, "w", v); _animSet.Controls.Add(ws);
                var ds = new Spin("Box length (m)", t.Num("d", 40), 5, 5, 400, "0"); ds.Set += v => AnimTrigSize(key!, "d", v); _animSet.Controls.Add(ds);
            }
            else L("No box yet: until you place one, it starts with the race.", Theme.Warn);
            L("The preview plays it at once; in the game it waits for the car.", ColorTranslator.FromHtml("#6f7c8a"));
        }
        Btn("✖  Remove the animation", "#5a2a2a", () => { PathEditing = null; TrigPlacing = null; AnimSet(key!, null, "Animation removed"); });
        _animSet.ResumeLayout();
    }

    /// one key clicked in the timeline: its time and place / turn as numbers to type (false = no single key selected)
    bool KeyEditor(string key, JObj a)
    {
        var sk = _tl?.SelKeys.Where(q => q.Key == key).ToList();
        if (sk == null || sk.Count == 0) return false;
        if (sk.Count > 1)
        {
            _animSet.Controls.Add(new Label { Text = $"{sk.Count} KEYS SELECTED", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#4fc3ff"), Font = Theme.Bold, Margin = new Padding(2, 8, 2, 2), UseMnemonic = false });
            var info = new Label { Text = "Drag one of them in the timeline: they all move together. Delete removes them.", AutoSize = true, MaximumSize = new Size(Math.Max(120, _animSet.ClientSize.Width - 16), 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(2, 2, 2, 4), UseMnemonic = false };
            _animSet.Controls.Add(info);
            var del = Theme.Colored("✖  Delete these keys", "#5a2a2a", AnimDeleteKeys);
            del.AutoSize = false; del.Width = Math.Max(120, _animSet.ClientSize.Width - 16); del.Height = 26; del.TextAlign = ContentAlignment.MiddleLeft;
            _animSet.Controls.Add(del);
            return true;
        }
        var (_, row, t) = sk[0];
        var k = (a.Arr(row) ?? new()).OfType<JObj>().FirstOrDefault(q => Math.Abs(q.Num("t") - t) < 1e-6);
        if (k == null) return false;
        var head = new Label { Text = row == "keys" ? "SELECTED POSITION KEY" : "SELECTED ROTATION KEY", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#4fc3ff"), Font = Theme.Bold, Margin = new Padding(2, 8, 2, 2), UseMnemonic = false };
        _animSet.Controls.Add(head);
        void S(string label, string field, double v, double step, double lo, double hi, string fmt)
        {
            var sp = new Spin(label, v, step, lo, hi, fmt); sp.Set += nv => KeyEdit(key, row, t, field, nv); _animSet.Controls.Add(sp);
        }
        S("Time (s)", "t", t, 0.1, 0, 600, "0.00");
        if (row == "keys")
        {
            S("X (m)", "x", k.Num("x"), 1, -100000, 100000, "0.##");
            S("Height (m)", "y", k.Num("y"), 0.5, -10000, 10000, "0.##");
            S("Z (m)", "z", k.Num("z"), 1, -100000, 100000, "0.##");
        }
        else S("Turn (°)", "angle", k.Num("angle"), 15, -3600, 3600, "0.#");
        var go = Theme.Colored("⏵  Go to this key", "#24324a", () => { AnimStop(); PlayT = t; AnimTimeMoved(); });
        go.AutoSize = false; go.Width = Math.Max(120, _animSet.ClientSize.Width - 16); go.Height = 26; go.TextAlign = ContentAlignment.MiddleLeft;
        _animSet.Controls.Add(go);
        return true;
    }

    void KeyEdit(string key, string row, double t, string field, double v)
    {
        var a = AnimCopy(key);
        var l = a.Arr(row);
        var k = l?.OfType<JObj>().FirstOrDefault(q => Math.Abs(q.Num("t") - t) < 1e-6);
        if (l == null || k == null) return;
        v = Math.Round(v, 2);
        if (field == "t")
        {
            v = Math.Max(0, v);
            if (l.OfType<JObj>().Any(q => q != k && Math.Abs(q.Num("t") - v) < 0.03)) { _f.Say($"Another key is already at {v:0.00} s"); AnimSettings(); return; }
        }
        k[field] = v;
        if (field == "t")
        {
            a[row] = l.OfType<JObj>().OrderBy(q => q.Num("t")).Select(q => (object?)q).ToList();
            _tl.SelKeys.Clear(); _tl.SelKeys.Add((key, row, v));
        }
        AnimSet(key, a, field == "t" ? $"Key moved to {v:0.00} s" : "Key changed");
    }

    /// the camera shows the object and everything it moves through (path, keys, trigger box)
    public void AnimFrame(string key)
    {
        var p = PickOf(key);
        if (p == null) return;
        var pts = BoxCorners(p).ToList();
        if (_rest.TryGetValue(key, out var r)) pts.Add(r.P);
        if (AnimOf(key) is { } a)
        {
            pts.AddRange(AnimEval.PathPoints(a));
            pts.AddRange(AnimEval.Keys(a).Select(k => new[] { k.Num("x"), k.Num("y"), k.Num("z") }));
            if (a.Str("start") == "trigger" && a.Obj("trigger") is { } t) pts.Add(new[] { t.Num("x"), t.Num("y", Ground), t.Num("z") });
        }
        double[] lo = { pts.Min(c => c[0]), pts.Min(c => c[1]), pts.Min(c => c[2]) }, hi = { pts.Max(c => c[0]), pts.Max(c => c[1]), pts.Max(c => c[2]) };
        Target = V((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
        Dist = Math.Clamp(Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2])) * 1.4 + 40, 30, 8000);
        DriveT = null; CamAnim = null;
        G.Invalidate();
    }

    void AnimStart(string key, string mode)
    {
        var p = PickOf(key);
        if (p == null) return;
        var (rp, rt) = _rest.TryGetValue(key, out var r) ? r : ((double[])p.Pos.Clone(), p.Turn);
        var a = new JObj { ["mode"] = mode, ["start"] = "race", ["loop"] = true };
        if (mode == "keys") { a["keys"] = new List<object?> { KeyAt(PlayT, rp) }; }
        else if (mode == "path") { a["path"] = new List<object?> { Pt3(rp) }; a["speed"] = 20.0; a["closed"] = false; a["angle"] = 0.0; }
        else a["rpm"] = 20.0;
        AnimSet(key, a, mode == "keys" ? "Keyframes: move the time marker, then G / R the object - each change is a key" : mode == "path" ? "Path: click points on the ground (the first is where the object stands), Enter = done" : "Spins 20 turns a minute");
        if (mode == "path") AnimPathEdit(key, true);
    }

    static JObj KeyAt(double t, double[] p) => new() { ["t"] = Math.Round(t, 2), ["x"] = Math.Round(p[0], 2), ["y"] = Math.Round(p[1], 2), ["z"] = Math.Round(p[2], 2) };
    static List<object?> Pt3(double[] p) => new() { Math.Round(p[0], 2), Math.Round(p[1], 2), Math.Round(p[2], 2) };

    void AnimField(string key, string field, object v, string say)
    {
        var a = AnimCopy(key);
        if (Equals(a.GetValueOrDefault(field), v)) return;
        a[field] = v;
        AnimSet(key, a, say);
    }

    /// switching the kind keeps what can be kept: keys <-> path points
    void AnimMode(string key, string mode)
    {
        var a = AnimCopy(key);
        string old = AnimEval.Mode(a);
        if (old == mode) return;
        if (mode == "path" && old == "keys") a["path"] = AnimEval.Keys(a).Select(k => (object?)Pt3(new[] { k.Num("x"), k.Num("y"), k.Num("z") })).ToList();
        if (mode == "keys" && old == "path")
        {
            var pts = AnimEval.PathPoints(a); double sp = Math.Max(0.5, a.Num("speed", 20)), t = 0;
            var ks = new List<object?>();
            for (int i = 0; i < pts.Count; i++) { if (i > 0) t += Math.Sqrt(Math.Pow(pts[i][0] - pts[i - 1][0], 2) + Math.Pow(pts[i][2] - pts[i - 1][2], 2)) / sp; ks.Add(KeyAt(t, pts[i])); }
            a["keys"] = ks;
        }
        if (mode == "keys" && AnimEval.Keys(a).Count == 0 && _rest.TryGetValue(key, out var r)) a["keys"] = new List<object?> { KeyAt(0, r.P) };
        if (mode == "spin" && !a.ContainsKey("rpm")) a["rpm"] = 20.0;
        if (mode == "path" && !a.ContainsKey("speed")) a["speed"] = 20.0;
        a["mode"] = mode;
        PathEditing = null;
        AnimSet(key, a, mode == "keys" ? "Now keyframes" : mode == "path" ? "Now a path" : "Now spinning");
    }

    void AnimTrigger(string key)
    {
        var a = AnimCopy(key);
        a["start"] = "trigger";
        bool place = a.Obj("trigger") == null;
        AnimSet(key, a, "Starts by a trigger box");
        if (place) AnimTrigPlace(key, true);
    }

    void AnimTrigPlace(string key, bool on)
    {
        TrigPlacing = on ? key : null; PathEditing = null;
        G.Cursor = on ? Cursors.Cross : Cursors.Default;
        AnimSettings(); AnimInfoLine(); G.Invalidate();
    }

    void AnimTrigSize(string key, string f, double v)
    {
        var a = AnimCopy(key);
        if (a.Obj("trigger") is not { } t) return;
        t[f] = Math.Round(v);
        AnimSet(key, a, $"Trigger box {t.Num("w", 40):0} x {t.Num("d", 40):0} m");
    }

    void AnimPathEdit(string key, bool on)
    {
        PathEditing = on ? key : null; TrigPlacing = null;
        G.Cursor = on ? Cursors.Cross : Cursors.Default;
        AnimSettings(); AnimInfoLine(); G.Invalidate();
    }

    void AnimPathPop(string key)
    {
        var a = AnimCopy(key);
        var l = a.Arr("path");
        if (l == null || l.Count == 0) return;
        l.RemoveAt(l.Count - 1);
        AnimSet(key, a, $"Path: {l.Count} point(s)");
    }

    // ---- the time
    public double TimeLen()
    {
        double m = AnimEval.DefaultLength;
        foreach (var k in AnimatedKeys()) { var a = AnimOf(k)!; double d = AnimEval.Duration(a); if (AnimEval.Mode(a) != "spin") m = Math.Max(m, d * (a.Truthy("loop") ? 1.25 : 1.1)); }
        return Math.Ceiling(m);
    }

    public void AnimPlayToggle() { if (Playing) AnimStop(); else AnimPlay(); }

    void AnimPlay()
    {
        if (AnimatedKeys().Count == 0) { _f.Say("Nothing moves yet: pick an object and make it move first"); return; }
        PathEditing = null; TrigPlacing = null; G.Cursor = Cursors.Default;
        if (PlayT >= TimeLen() - 0.05) PlayT = 0;
        Playing = true; _playStart = DateTime.UtcNow; _playFrom = PlayT;
        if (_playBtn != null) _playBtn.Text = "❚❚ Pause";
        _anim.Start();
        AnimInfoLine();
    }

    public void AnimStop()
    {
        Playing = false;
        if (_playBtn != null) _playBtn.Text = "▶ Play";
    }

    /// the timer: move the playhead while playing (wraps at the end of the timeline)
    void AnimTick()
    {
        if (!Playing) return;
        if (!AnimTab) { AnimStop(); return; }
        double len = TimeLen();
        PlayT = _playFrom + (DateTime.UtcNow - _playStart).TotalSeconds;
        if (PlayT > len) { _playFrom = 0; _playStart = DateTime.UtcNow; PlayT = 0; }
        AnimTimeMoved(false);
    }

    public void AnimTimeMoved(bool settle = true)
    {
        if (_animTime != null) _animTime.Text = $"{PlayT:0.00} s";
        AnimPosePicks();
        _tl?.Invalidate();
        if (settle && PickOf(Sel) is { } p) Select(p.Key, true);
        G.Invalidate();
    }

    // ---- where the objects are drawn
    /// after a rebuild: remember where each object stands (its rest), then move the animated ones to the time
    void AnimAfterScene()
    {
        _rest.Clear();
        foreach (var p in Picks) if (p.Key.StartsWith("obj:") && !_rest.ContainsKey(p.Key)) _rest[p.Key] = ((double[])p.Pos.Clone(), p.Turn);
        AnimRefresh();
        AnimPosePicks();
        if (AnimTab) AnimSettings();
        _tl?.Invalidate();
    }

    (double[] P, double Turn)? PoseOf(string key)
    {
        var a = AnimOf(key);
        if (a == null || !_rest.TryGetValue(key, out var r)) return null;
        return AnimEval.Pose(a, PlayT, r.P, r.T);
    }

    /// the picks (selection boxes, handles) follow the animation in the Animation sub-tab, else stand at rest
    void AnimPosePicks()
    {
        bool on = AnimTab;
        foreach (var p in Picks)
        {
            if (!_rest.TryGetValue(p.Key, out var r)) continue;
            var pose = on ? PoseOf(p.Key) : null;
            if (pose == null) { p.Pos = (double[])r.P.Clone(); p.Turn = r.T; continue; }
            p.Pos = (double[])pose.Value.P.Clone(); p.Turn = pose.Value.Turn;
        }
    }

    /// which objects are drawn moving (left out of the still scene); the still scene is made again when that changes
    void AnimRefresh()
    {
        var nw = AnimTab ? AnimatedKeys().Where(k => !ViewHidden.Contains(k)).ToHashSet() : new HashSet<string>();
        if (nw.SetEquals(_moving)) return;
        _moving.Clear(); foreach (var k in nw) _moving.Add(k);
        if (_mesh != null) Compile(_mesh);
    }

    bool Hid(string? k) => k != null && (ViewHidden.Contains(k) || _moving.Contains(k));

    unsafe void AnimDraw(Mesh M)
    {
        if (_moving.Count == 0 || SoloKey != null) return;
        foreach (var key in _moving)
        {
            if (ViewHidden.Contains(key) || !_rest.TryGetValue(key, out var r) || PoseOf(key) is not { } pose) continue;
            double d = pose.Turn - r.T, c = Math.Cos(d), s = Math.Sin(d);
            double tx = pose.P[0] - (c * r.P[0] + s * r.P[2]), ty = pose.P[1] - r.P[1], tz = pose.P[2] - (-s * r.P[0] + c * r.P[2]);
            float[] m = { (float)c, 0, (float)-s, 0, 0, 1, 0, 0, (float)s, 0, (float)c, 0, (float)tx, (float)ty, (float)tz, 1 };
            GL.glPushMatrix();
            fixed (float* mp = m) GL.glMultMatrixf(mp);
            var rs = M.Ranges.GetValueOrDefault(key) ?? new();
            foreach (var (b, first, n) in rs) if (b == M.V) b.DrawRange(GL.TRIANGLES, first, n); else if (b == M.L) b.DrawRange(GL.LINES, first, n);
            var tex = _textured.Where(t => t.Owner == key).ToList();
            if (tex.Count > 0) DrawTextured(tex);
            GL.glDepthMask(0); foreach (var (b, first, n) in rs) if (b == M.T) b.DrawRange(GL.TRIANGLES, first, n); GL.glDepthMask(1);
            GL.glPopMatrix();
        }
    }

    /// paths, key points and trigger boxes (only in the Animation sub-tab)
    void AnimOverlays()
    {
        if (!AnimTab) return;
        GL.glDisable(GL.DEPTH_TEST);
        var lines = new Batch(); var pts = new Batch(); var sel = new Batch();
        foreach (var key in AnimatedKeys().Concat(Sel != null && AnimOf(Sel) != null && !AnimatedKeys().Contains(Sel) ? new[] { Sel } : Array.Empty<string>()))
        {
            var a = AnimOf(key)!;
            bool me = key == Sel;
            var col = me ? new Rgba(1, .85f, .2f) : new Rgba(.75f, .6f, .25f, .55f);
            string mode = AnimEval.Mode(a);
            List<double[]> c = new(), dots = new();
            if (mode == "path") { var p = AnimEval.PathPoints(a); dots = p; c = AnimEval.Curve(p, a.Truthy("closed")); }
            else if (mode == "keys")
            {
                var ks = AnimEval.Keys(a); dots = ks.Select(k => new[] { k.Num("x"), k.Num("y"), k.Num("z") }).ToList();
                for (int i = 0; i + 1 < dots.Count; i++) c.AddRange(AnimEval.KeyPiece(dots, i));
            }
            for (int i = 0; i + 1 < c.Count; i++) { lines.Add((float)c[i][0], (float)c[i][1] + .4f, (float)c[i][2], col); lines.Add((float)c[i + 1][0], (float)c[i + 1][1] + .4f, (float)c[i + 1][2], col); }
            // arrows on the selected object's way: which way it goes
            double clen = c.Count > 1 ? AnimEval.Length(c) : 0;
            if (me && clen > 1)
            {
                double size = Math.Clamp(clen / 40, 2.5, 14), gap = Math.Max(size * 4, clen / 14);
                for (double d = gap / 2; d < clen; d += gap)
                {
                    var (ap, dir) = AnimEval.Along(c, d);
                    double fx = Math.Sin(dir), fz = Math.Cos(dir), y0 = ap[1] + .5;
                    foreach (int side in new[] { -1, 1 })
                    {
                        lines.Add((float)ap[0], (float)y0, (float)ap[2], col);
                        lines.Add((float)(ap[0] - fx * size + side * fz * size * .6), (float)y0, (float)(ap[2] - fz * size - side * fx * size * .6), col);
                    }
                }
            }
            for (int i = 0; i < dots.Count; i++)
            {
                var b = me && (key == PathEditing || mode == "keys") ? sel : pts;
                b.Add((float)dots[i][0], (float)dots[i][1] + .4f, (float)dots[i][2], me ? (i == _dragPt && key == PathEditing ? new Rgba(1, 1, 1) : i == 0 ? new Rgba(.3f, 1, .4f) : new Rgba(1, .85f, .2f)) : new Rgba(.75f, .6f, .25f, .7f));
            }
            if (a.Str("start") == "trigger" && a.Obj("trigger") is { } t)
            {
                double x = t.Num("x"), y = t.Num("y", Ground), z = t.Num("z"), hw = t.Num("w", 40) / 2, hd = t.Num("d", 40) / 2, h = t.Num("h", 20) / 2;
                var tc = me ? new Rgba(1, .45f, .15f) : new Rgba(1, .45f, .15f, .5f);
                var cs = new List<double[]>();
                foreach (double yy in new[] { y - h, y + h }) foreach (double zz in new[] { z - hd, z + hd }) foreach (double xx in new[] { x - hw, x + hw }) cs.Add(V(xx, yy, zz));
                BoxLines(cs, tc);
            }
        }
        GL.glLineWidth(2.5f); lines.Draw(GL.LINES); GL.glLineWidth(1);
        GL.glPointSize(7); pts.Draw(GL.POINTS); GL.glPointSize(11); sel.Draw(GL.POINTS); GL.glPointSize(1);
        GL.glEnable(GL.DEPTH_TEST);
    }

    // ---- the mouse in 3D
    /// a left click in the Animation sub-tab: path points, the trigger box. true = used
    bool AnimClick(int x, int y)
    {
        if (TrigPlacing is { } tk)
        {
            var h = PointUnder(x, y);
            if (h == null) return true;
            var a = AnimCopy(tk);
            var t = a.Obj("trigger") ?? new JObj { ["w"] = 40.0, ["h"] = 20.0, ["d"] = 40.0 };
            t["x"] = Math.Round(h[0], 2); t["y"] = Math.Round(h[1] + 1, 2); t["z"] = Math.Round(h[2], 2);
            a["trigger"] = t; a["start"] = "trigger";
            TrigPlacing = null; G.Cursor = Cursors.Default;
            AnimSet(tk, a, "Trigger box placed: the object starts when your car drives through it");
            AnimInfoLine();
            return true;
        }
        if (PathEditing is { } pk)
        {
            var a = AnimOf(pk);
            if (a == null) { PathEditing = null; return false; }
            var pts = AnimEval.PathPoints(a);
            for (int i = 0; i < pts.Count; i++)
            {
                var s = Screen(V(pts[i][0], pts[i][1] + .4, pts[i][2]));
                if (s != null && Math.Sqrt(Math.Pow(s.Value.X - x, 2) + Math.Pow(s.Value.Y - y, 2)) < 10)
                {
                    if ((Control.ModifierKeys & Keys.Shift) != 0)
                    {
                        var c = AnimCopy(pk); var cl = c.Arr("path")!; cl.RemoveAt(i);
                        AnimSet(pk, c, $"Path point removed: {cl.Count} point(s) left");
                        return true;
                    }
                    D.Snapshot(); _dragPt = i; G.Invalidate(); return true;
                }
            }
            var h = PointUnder(x, y);
            if (h == null) return true;
            var b = AnimCopy(pk);
            var l = b.Arr("path") ?? new List<object?>();
            l.Add(Pt3(new[] { h[0], h[1] + (_rest.TryGetValue(pk, out var r) ? r.P[1] - Ground : 0), h[2] }));
            b["path"] = l;
            AnimSet(pk, b, $"Path: {l.Count} point(s) - Enter / right click = done");
            return true;
        }
        return false;
    }

    /// dragging a path point; true = used
    bool AnimMove(int x, int y)
    {
        if (_dragPt < 0 || PathEditing == null) return false;
        var a = AnimOf(PathEditing); var l = a?.Arr("path");
        if (l == null || _dragPt >= l.Count) { _dragPt = -1; return false; }
        var cur = ((List<object?>)l[_dragPt]!).Select(Json.D).ToArray();
        var h = GroundHit(x, y, cur[1]);
        if (h == null) return true;
        l[_dragPt] = Pt3(new[] { h[0], cur[1], h[2] });        // live; stored (with undo) when the mouse goes up
        G.Invalidate();
        return true;
    }

    bool AnimUp()
    {
        if (_dragPt < 0 || PathEditing == null) return false;
        _dragPt = -1;
        // the undo step was taken when the drag started; the point moved in the map while dragging
        D.Touched(); Edited?.Invoke();
        _f.Say("Path point moved - Ctrl+Z undoes");
        AnimChanged();
        return true;
    }

    /// a move / turn of the selected object in the Animation sub-tab becomes a key (or moves the whole path). true = used
    bool AnimRecord(Pick p, Op op)
    {
        if (!AnimTab || op.Kind is not ("move" or "turn")) return false;
        var a0 = AnimOf(p.Key);
        if (a0 == null) return false;
        var a = Share.Copy(a0);
        string mode = AnimEval.Mode(a);
        bool moved = op.Delta.Any(v => Math.Abs(v) > 1e-6), turned = Math.Abs(op.DTurn) > 1e-6;
        if (mode == "path")
        {
            if (moved && a.Arr("path") is { } l)
                a["path"] = l.OfType<List<object?>>().Select(q => (object?)Pt3(new[] { Json.D(q[0]) + op.Delta[0], Json.D(q[1]) + op.Delta[1], Json.D(q[2]) + op.Delta[2] })).ToList();
            if (turned) a["angle"] = Math.Round(Py.Mod(a.Num("angle") + op.DTurn * 180 / Math.PI + 180, 360) - 180);
            AnimSet(p.Key, a, moved ? "The whole path moved" : "Turned on the path");
            return true;
        }
        if (mode != "keys") return false;
        bool posOn = AnimEval.Keys(a).Count > 0, rotOn = AnimEval.RKeys(a).Count > 0, done = false;
        double t = Math.Round(PlayT, 2);
        if (moved && posOn)
        {
            var np = V(p.Pos[0] + op.Delta[0], p.Pos[1] + op.Delta[1], p.Pos[2] + op.Delta[2]);
            var ks = AnimEval.Keys(a).Where(k => Math.Abs(k.Num("t") - t) > 0.03).Select(k => (object?)k).ToList();
            ks.Add(KeyAt(t, np));
            a["keys"] = ks.OrderBy(k => ((JObj)k!).Num("t")).ToList();
            done = true;
        }
        if (turned && rotOn)
        {
            double ang = Math.Round(AnimEval.AngleAt(a, t) + op.DTurn * 180 / Math.PI, 1);
            var rk = AnimEval.RKeys(a).Where(k => Math.Abs(k.Num("t") - t) > 0.03).Select(k => (object?)k).ToList();
            rk.Add(new JObj { ["t"] = t, ["angle"] = ang });
            a["rkeys"] = rk.OrderBy(k => ((JObj)k!).Num("t")).ToList();
            done = true;
        }
        if (!done)
        {
            // nothing to record: tell why, and leave the object where it is (a key-animated object has no place of its own)
            _f.Say(moved ? "Turn on the Position stopwatch (⏱ in the timeline) to make position keys" : "Turn on the Rotation stopwatch (⏱ in the timeline) to make turn keys");
            G.Invalidate();
            return true;
        }
        AnimSet(p.Key, a, $"Key at {t:0.00} s");
        return true;
    }

    /// for the robot test: a move / turn as if made with G / R
    public bool AnimRecordTest(string key, double[] delta, double dturn)
    {
        var p = PickOf(key); if (p == null) return false;
        return AnimRecord(p, new Op { Kind = dturn != 0 ? "turn" : "move", Delta = delta, DTurn = dturn });
    }
    public void AnimStartTest(string key, string mode) => AnimStart(key, mode);
    public void KeyEditTest(string key, string row, double t, string field, double v) => KeyEdit(key, row, t, field, v);
    public AnimTimeline Timeline => _tl;

    /// keys in the Animation sub-tab; true = used
    bool AnimKey(Keys key)
    {
        if (!AnimTab) return false;
        switch (key)
        {
            case Keys.Space: AnimPlayToggle(); return true;
            case Keys.Home: AnimStop(); PlayT = 0; AnimTimeMoved(); return true;
            case Keys.Enter when PathEditing != null: AnimPathEdit(PathEditing, false); return true;
            case Keys.Back when PathEditing != null: AnimPathPop(PathEditing); return true;
            case Keys.Escape when PathEditing != null || TrigPlacing != null: PathEditing = null; TrigPlacing = null; G.Cursor = Cursors.Default; AnimSettings(); AnimInfoLine(); G.Invalidate(); return true;
            case Keys.Delete when _tl.SelKeys.Count > 0: AnimDeleteKeys(); return true;
            case Keys.Oemcomma: _tl.Step(-1); return true;
            case Keys.OemPeriod: _tl.Step(1); return true;
        }
        return false;
    }

    // ---- keys from the timeline
    public void AnimKeyHere()
    {
        var key = Sel; var a = AnimOf(key);
        if (key == null || a == null || AnimEval.Mode(a) != "keys") { _f.Say("Keys: pick an object with keyframes first"); return; }
        var p = PickOf(key);
        if (p == null) return;
        var b = Share.Copy(a); double t = Math.Round(PlayT, 2);
        bool posOn = AnimEval.Keys(b).Count > 0, rotOn = AnimEval.RKeys(b).Count > 0;
        if (posOn) b["keys"] = AnimEval.Keys(b).Where(k => Math.Abs(k.Num("t") - t) > 0.03).Append(KeyAt(t, p.Pos)).OrderBy(k => k.Num("t")).Select(k => (object?)k).ToList();
        if (rotOn) b["rkeys"] = AnimEval.RKeys(b).Where(k => Math.Abs(k.Num("t") - t) > 0.03).Append(new JObj { ["t"] = t, ["angle"] = Math.Round(AnimEval.AngleAt(b, t), 1) }).OrderBy(k => k.Num("t")).Select(k => (object?)k).ToList();
        if (!posOn && !rotOn) { _f.Say("Turn on a stopwatch (Position or Rotation) first"); return; }
        AnimSet(key, b, $"Key at {t:0.00} s");
    }

    /// a stopwatch: on = keys start at the time marker, off = all keys of that row go (like After Effects)
    public void AnimStopwatch(string key, string row)
    {
        var p = PickOf(key); if (p == null) return;
        var a = AnimOf(key) == null ? new JObj { ["mode"] = "keys", ["start"] = "race", ["loop"] = true } : AnimCopy(key);
        if (AnimEval.Mode(a) != "keys") { _f.Say("Stopwatches are for keyframes: switch this object to ◆ Keys first (right)"); return; }
        string f = row == "pos" ? "keys" : "rkeys";
        bool on = (a.Arr(f)?.Count ?? 0) > 0;
        double t = Math.Round(PlayT, 2);
        if (on) a.Remove(f);
        else a[f] = new List<object?> { row == "pos" ? KeyAt(t, p.Pos) : new JObj { ["t"] = t, ["angle"] = 0.0 } };
        if (a.Arr("keys") == null && a.Arr("rkeys") == null) { AnimSet(key, null, "Animation removed (no keys left)"); return; }
        AnimSet(key, a, on ? $"{(row == "pos" ? "Position" : "Rotation")} keys removed" : $"{(row == "pos" ? "Position" : "Rotation")}: first key at {t:0.00} s - move the time marker and {(row == "pos" ? "move (G)" : "turn (R)")} the object for more");
    }

    public void AnimDeleteKeys()
    {
        var sel = _tl.SelKeys.ToList();
        if (sel.Count == 0) { _f.Say("Click a key (diamond) in the timeline first"); return; }
        foreach (var g in sel.GroupBy(q => q.Key))
        {
            var a = AnimCopy(g.Key);
            foreach (var row in new[] { "keys", "rkeys" })
            {
                var ts = g.Where(q => q.Row == row).Select(q => q.T).ToList();
                if (ts.Count == 0 || a.Arr(row) is not { } l) continue;
                a[row] = l.OfType<JObj>().Where(k => !ts.Any(t => Math.Abs(k.Num("t") - t) < 1e-6)).Select(k => (object?)k).ToList();
                if (((List<object?>)a[row]!).Count == 0) a.Remove(row);
            }
            _tl.SelKeys.Clear();
            if (a.Arr("keys") == null && a.Arr("rkeys") == null) AnimSet(g.Key, null, "Last key deleted: the object stands still again");
            else AnimSet(g.Key, a, $"{g.Count()} key(s) deleted");
        }
    }

    /// keys dragged in the timeline: every selected key moves by dt (seconds)
    public void AnimShiftKeys(List<(string Key, string Row, double T)> keys, double dt)
    {
        foreach (var g in keys.GroupBy(q => q.Key))
        {
            var a = AnimCopy(g.Key);
            foreach (var row in new[] { "keys", "rkeys" })
            {
                if (a.Arr(row) is not { } l) continue;
                var ts = g.Where(q => q.Row == row).Select(q => q.T).ToList();
                foreach (var k in l.OfType<JObj>()) if (ts.Any(t => Math.Abs(k.Num("t") - t) < 1e-6)) k["t"] = Math.Round(Math.Max(0, k.Num("t") + dt), 2);
                a[row] = l.OfType<JObj>().OrderBy(k => k.Num("t")).Select(k => (object?)k).ToList();
            }
            AnimSet(g.Key, a, $"Key(s) moved {dt:+0.00;-0.00} s");
        }
    }
}

/// the keyframe panel's timeline (drawn by hand: rows, ruler, diamonds, playhead)
public sealed class AnimTimeline : Control
{
    readonly View3D V;
    public const int LabelW = 250, RowH = 22, Ruler = 24, Pad = 10, BarH = 12;
    double _t0 = 0, _len = 10;                         // the time shown: from _t0, _len seconds
    bool _autoFit = true;                              // until the user zooms / scrolls: the view grows with the animation
    Point _boxA, _boxB; bool _boxing, _boxAdd; List<(string Key, string Row, double T)> _boxBase = new();
    public readonly HashSet<(string Key, string Row, double T)> SelKeys = new();
    (string Mode, int X0, double T0)? _drag;            // "scrub" / "keys" / "box" / "pan" / "bar"
    List<(string Key, string Row, double T)> _dragKeys = new();
    double _dragDt;
    readonly List<(Rectangle R, string Kind, string Key, string Row, double T)> _hits = new();
    static readonly Color Bg = ColorTranslator.FromHtml("#171b22"), Line = ColorTranslator.FromHtml("#2a313c"), Txt = ColorTranslator.FromHtml("#d8dee6"), Dim = ColorTranslator.FromHtml("#5d6875"),
        Head = ColorTranslator.FromHtml("#20262f"), SelRow = ColorTranslator.FromHtml("#26303e"), Key = ColorTranslator.FromHtml("#ffd23b"), KeySel = ColorTranslator.FromHtml("#4fc3ff"), Play = ColorTranslator.FromHtml("#ff4a4a");
    static readonly Font F = new("Segoe UI", 8.5f), Fb = new("Segoe UI", 8.5f, FontStyle.Bold), Fs = new("Segoe UI Symbol", 9.5f);

    public AnimTimeline(View3D v)
    {
        V = v;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
    }

    double PxPerS => Math.Max(1, Width - LabelW - Pad - 12) / _len;
    float X(double t) => (float)(LabelW + Pad + (t - _t0) * PxPerS);
    double T(int x) => _t0 + (x - LabelW - Pad) / PxPerS;
    /// how far the scroll bar reaches (seconds)
    double Total => Math.Max(Math.Max(V.TimeLen() * 1.15, 10), Math.Max(_t0 + _len, V.PlayT + 1));
    Rectangle Bar => new(LabelW, Height - BarH, Math.Max(1, Width - LabelW), BarH);
    Rectangle Thumb { get { var b = Bar; double tot = Total; int x = b.X + (int)(_t0 / tot * b.Width), w = Math.Max(24, (int)(_len / tot * b.Width)); return new(Math.Min(x, b.Right - w), b.Y + 2, w, BarH - 4); } }
    void ScrollTo(double t0) { _t0 = Math.Clamp(t0, 0, Math.Max(0, Total - _len)); _autoFit = false; Invalidate(); }

    public void Fit() { _t0 = 0; _len = Math.Max(2, V.TimeLen()); _autoFit = true; Invalidate(); }
    public double ViewStart => _t0;
    public double ViewLen => _len;
    /// for the robot test: the mouse wheel / a box drawn over the keys (points in the timeline)
    public void WheelTest(int x, int delta, bool shift = false) => Wheel(x, delta, shift);
    public void BoxTest(Point a, Point b, bool add = false)
    {
        _boxBase = add ? SelKeys.ToList() : new(); _boxA = a; _boxB = b; _boxing = true; _boxAdd = add;
        BoxSelect(); _boxing = false; V.AnimSettings(); Invalidate();
    }
    public float XOf(double t) => X(t);
    public int RowY(string key, string kind) { int y = Ruler; foreach (var r in Rows()) { if (r.Key == key && r.Kind == kind) return y; y += RowH; } return -1; }
    public void Step(int dir) { V.AnimStop(); V.PlayT = Math.Max(0, Math.Round(V.PlayT + dir * 0.1, 2)); V.AnimTimeMoved(); }

    /// the rows: (object key, row kind, label); kinds: obj, pos, rot, scale, tilt
    List<(string Key, string Kind, string Label)> Rows()
    {
        var o = new List<(string, string, string)>();
        var keys = V.AnimatedKeys();
        string? sel = V.Sel != null && V.AnimNo(V.Sel) == null ? V.Sel : null;
        if (sel != null && !keys.Contains(sel)) keys.Insert(0, sel);
        foreach (var k in keys)
        {
            o.Add((k, "obj", k));
            if (k != sel) continue;
            o.Add((k, "pos", "Position")); o.Add((k, "rot", "Rotation (Y)")); o.Add((k, "scale", "Scale")); o.Add((k, "tilt", "Tilt (X / Z)"));
        }
        return o;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Bg);
        _hits.Clear();
        if (_len <= 0) _len = 10;
        if (_autoFit && _len < V.TimeLen() && _drag == null && !V.Playing) _len = Math.Max(_len, V.TimeLen());
        if (V.Playing && (V.PlayT > _t0 + _len || V.PlayT < _t0)) { _t0 = Math.Max(0, V.PlayT - _len * 0.05); _autoFit = false; }   // follow the time marker
        int w = Width, trackW = w - LabelW;
        // ruler
        using (var hb = new SolidBrush(Head)) g.FillRectangle(hb, 0, 0, w, Ruler);
        double step = new[] { 0.1, 0.25, 0.5, 1, 2, 5, 10, 30, 60 }.First(s => s * PxPerS >= 46 || s == 60);
        using var lp = new Pen(Line); using var dp = new Pen(Dim);
        for (double t = Math.Floor(_t0 / step) * step; t <= _t0 + _len + 1e-9; t += step)
        {
            float x = X(t); if (x < LabelW + 2) continue;
            g.DrawLine(dp, x, Ruler - 7, x, Ruler);
            g.DrawLine(lp, x, Ruler, x, Height);
            TextRenderer.DrawText(g, step < 1 ? $"{t:0.##}s" : $"{t:0}s", F, new Point((int)x + 2, 4), Dim, TextFormatFlags.NoPrefix);
        }
        TextRenderer.DrawText(g, Lang.T("drag here = scrub"), F, new Rectangle(4, 0, LabelW - 8, Ruler), Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        _hits.Add((new Rectangle(LabelW, 0, trackW, Ruler), "ruler", "", "", 0));
        // rows
        var rows = Rows();
        int y = Ruler;
        if (rows.Count == 0)
            TextRenderer.DrawText(g, Lang.T("Nothing moves yet. Pick one of your objects (3D view or object list), then use the buttons on the right."), F, new Rectangle(LabelW + 10, Ruler + 14, Math.Max(100, trackW - 20), 60), Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        foreach (var (key, kind, label) in rows)
        {
            var a = V.AnimOf(key);
            bool selObj = key == V.Sel;
            var rr = new Rectangle(0, y, w, RowH);
            if (kind == "obj") using (var b = new SolidBrush(selObj ? SelRow : Head)) g.FillRectangle(b, rr);
            g.DrawLine(lp, 0, y + RowH - 1, w, y + RowH - 1);
            string mode = a == null ? "" : AnimEval.Mode(a);
            if (kind == "obj")
            {
                string sym = selObj ? "▾" : "▸";
                string what = a == null ? Lang.T("still") : mode == "keys" ? Lang.T("keyframes") : mode == "path" ? Lang.T("path") : Lang.T("spin");
                if (a?.Str("start") == "trigger") what += " · " + Lang.T("trigger");
                var ws = TextRenderer.MeasureText(what, F);
                int ww = Math.Min(LabelW / 2, ws.Width + 4);
                TextRenderer.DrawText(g, $"{sym} {V.ObjName(key)}", Fb, new Rectangle(4, y, LabelW - ww - 8, RowH), selObj ? Color.White : Txt, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, what, F, new Rectangle(LabelW - ww - 4, y, ww, RowH), a?.Str("start") == "trigger" ? ColorTranslator.FromHtml("#d08050") : Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                _hits.Add((new Rectangle(0, y, LabelW, RowH), "obj", key, "", 0));
                if (a != null) { g.SetClip(new Rectangle(LabelW, y, w - LabelW, RowH)); DrawSummary(g, key, a, y); g.ResetClip(); }
            }
            else
            {
                bool grey = kind is "scale" or "tilt" || (mode != "keys" && mode != "" && kind is "pos" or "rot");
                string f = kind == "pos" ? "keys" : "rkeys";
                bool on = a != null && mode == "keys" && (a.Arr(f)?.Count ?? 0) > 0;
                if (kind is "pos" or "rot")
                {
                    var sw = new Rectangle(14, y + 2, 18, RowH - 4);
                    TextRenderer.DrawText(g, "⏱", Fs, sw, mode is "" or "keys" ? (on ? Key : Dim) : ColorTranslator.FromHtml("#3a4350"), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    if (mode is "" or "keys") _hits.Add((sw, "watch", key, kind, 0));
                }
                TextRenderer.DrawText(g, Lang.T(label), F, new Rectangle(36, y, LabelW - 40, RowH), grey ? Dim : Txt, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                var track = new Rectangle(LabelW, y, trackW, RowH);
                if (kind is "scale" or "tilt")
                {
                    using var hatch = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.BackwardDiagonal, ColorTranslator.FromHtml("#232933"), Bg);
                    g.FillRectangle(hatch, track);
                    TextRenderer.DrawText(g, Lang.T(kind == "scale" ? "the game cannot change an object's size while racing" : "the game cannot tilt a moving object (it turns around its up axis only)"), F, new Rectangle(LabelW + 8, y, trackW - 16, RowH), Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
                else if (a != null && mode == "keys")
                {
                    if (on) { g.SetClip(new Rectangle(LabelW, y, w - LabelW, RowH)); DrawKeys(g, key, f, a, y, Key); g.ResetClip(); }
                    else TextRenderer.DrawText(g, Lang.T(kind == "pos" ? "click the stopwatch to make position keys" : "click the stopwatch to make turn keys"), F, new Rectangle(LabelW + 8, y, trackW - 16, RowH), Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    _hits.Add((track, "track", key, f, 0));
                }
                else if (a != null)
                {
                    string txt = mode == "path"
                        ? (kind == "pos" ? $"{Lang.T("follows its path at")} {a.Num("speed", 20):0.#} m/s ({AnimEval.Duration(a):0.0} s)" : $"{Lang.T("faces the way it drives")} {a.Num("angle"):+0;-0;+0}°")
                        : (kind == "pos" ? Lang.T("stays where it is") : $"{Lang.T("spins")} {a.Num("rpm", 20):0} {Lang.T("turns a minute")}");
                    TextRenderer.DrawText(g, txt, F, new Rectangle(LabelW + 8, y, trackW - 16, RowH), Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
                else
                {
                    TextRenderer.DrawText(g, Lang.T(kind is "pos" or "rot" ? "click the stopwatch to start keyframes" : ""), F, new Rectangle(LabelW + 8, y, trackW - 16, RowH), Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
            y += RowH;
        }
        // the playhead
        float px = X(V.PlayT);
        if (px >= LabelW && px <= w)
        {
            using var pp = new Pen(Play, 1.6f);
            g.DrawLine(pp, px, 0, px, Height);
            using var pb = new SolidBrush(Play);
            g.FillPolygon(pb, new[] { new PointF(px - 6, 0), new PointF(px + 6, 0), new PointF(px + 6, 9), new PointF(px, 15), new PointF(px - 6, 9) });
            string pt = $"{V.PlayT:0.00}";
            var sz = TextRenderer.MeasureText(pt, F);
            int lx = px + 8 + sz.Width > w ? (int)px - 8 - sz.Width : (int)px + 8;
            g.FillRectangle(pb, lx - 2, 3, sz.Width + 2, Ruler - 8);
            TextRenderer.DrawText(g, pt, F, new Point(lx - 1, 4), Color.White, TextFormatFlags.NoPrefix);
        }
        // the box being drawn over keys
        if (_boxing)
        {
            var br = BoxRect();
            using var bb = new SolidBrush(Color.FromArgb(40, KeySel)); g.FillRectangle(bb, br);
            using var bp = new Pen(KeySel); g.DrawRectangle(bp, br);
        }
        // the scroll bar (bottom)
        using (var sb = new SolidBrush(ColorTranslator.FromHtml("#11151b"))) g.FillRectangle(sb, Bar);
        using (var tb = new SolidBrush(ColorTranslator.FromHtml(_drag?.Mode == "bar" ? "#56657a" : "#3a4556"))) g.FillRectangle(tb, Thumb);
        _hits.Add((Bar, "bar", "", "", 0));
        // how to use it (bottom lines)
        var hint = Lang.T("Space = play / pause   drag the ruler = scrub   wheel = zoom   bar below / middle drag = scroll   drag over keys = select many (Ctrl adds)   G / R on the selected object = a key at the time marker");
        if (Height - y > 50 + BarH) TextRenderer.DrawText(g, hint, F, new Rectangle(LabelW + 8, Height - 36 - BarH, w - LabelW - 12, 34), Dim, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.Bottom);
        using (var sp = new Pen(Line)) g.DrawLine(sp, LabelW - 1, 0, LabelW - 1, Height);
    }


    void DrawKeys(Graphics g, string key, string row, JObj a, int y, Color c)
    {
        var ks = (a.Arr(row) ?? new()).OfType<JObj>().OrderBy(k => k.Num("t")).ToList();
        using var lineP = new Pen(Color.FromArgb(90, c), 2);
        for (int i = 0; i + 1 < ks.Count; i++) g.DrawLine(lineP, X(ks[i].Num("t") + Drag(key, row, ks[i].Num("t"))), y + RowH / 2, X(ks[i + 1].Num("t") + Drag(key, row, ks[i + 1].Num("t"))), y + RowH / 2);
        foreach (var k in ks)
        {
            double t = k.Num("t"), td = t + Drag(key, row, t);
            float x = X(td);
            if (x < LabelW - 6 || x > Width + 6) continue;
            bool s = SelKeys.Contains((key, row, t));
            if (x < LabelW) continue;                    // behind the names: not shown, not clickable
            Diamond(g, x, y + RowH / 2, s ? KeySel : c, s);
            _hits.Add((new Rectangle((int)x - 7, y + 2, 14, RowH - 4), "key", key, row, t));
        }
    }

    double Drag(string key, string row, double t) => _drag?.Mode == "keys" && _dragKeys.Contains((key, row, t)) ? _dragDt : 0;

    static void Diamond(Graphics g, float x, float y, Color c, bool sel)
    {
        float r = sel ? 6.5f : 5.5f;
        var pts = new[] { new PointF(x, y - r), new PointF(x + r, y), new PointF(x, y + r), new PointF(x - r, y) };
        using var b = new SolidBrush(c); g.FillPolygon(b, pts);
        using var p = new Pen(Color.FromArgb(200, 10, 12, 16), 1.2f); g.DrawPolygon(p, pts);
    }

    void DrawSummary(Graphics g, string key, JObj a, int y)
    {
        string mode = AnimEval.Mode(a);
        if (mode == "keys")
        {
            var ts = AnimEval.Keys(a).Select(k => k.Num("t")).Concat(AnimEval.RKeys(a).Select(k => k.Num("t"))).Distinct().ToList();
            foreach (var t in ts) { float x = X(t); if (x >= LabelW - 4) Diamond(g, x, y + RowH / 2, ColorTranslator.FromHtml("#b89a3a"), false); }
            return;
        }
        double d = AnimEval.Duration(a), end = mode == "spin" || a.Truthy("loop") ? _t0 + _len : d;
        using var br = new SolidBrush(mode == "spin" ? ColorTranslator.FromHtml("#2d6e4a") : ColorTranslator.FromHtml("#3a2d6e"));
        float x0 = Math.Max(LabelW, X(0)), x1 = Math.Min(Width, X(end));
        if (x1 > x0) g.FillRectangle(br, x0, y + 6, x1 - x0, RowH - 12);
        if (mode == "path" && a.Truthy("loop")) using (var pen = new Pen(ColorTranslator.FromHtml("#8f7ad8"))) for (double t = d; t < _t0 + _len; t += Math.Max(0.1, d)) { float x = X(t); if (x > LabelW) g.DrawLine(pen, x, y + 4, x, y + RowH - 4); }
    }

    (Rectangle R, string Kind, string Key, string Row, double T)? HitAt(Point p)
    {
        foreach (var h in _hits.Where(h => h.Kind == "key")) if (h.R.Contains(p)) return h;
        foreach (var h in _hits) if (h.R.Contains(p)) return h;
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var h = HitAt(e.Location);
        bool add = (ModifierKeys & (Keys.Shift | Keys.Control)) != 0;
        if (e.Button == MouseButtons.Right && h is { Kind: "key" } kr)
        {
            if (!SelKeys.Contains((kr.Key, kr.Row, kr.T))) { SelKeys.Clear(); SelKeys.Add((kr.Key, kr.Row, kr.T)); }
            var m = new ContextMenuStrip { BackColor = Head, ForeColor = Txt, ShowImageMargin = false };
            m.Items.Add(Lang.T("Go to this key"), null, (_, _) => { V.AnimStop(); V.PlayT = kr.T; V.AnimTimeMoved(); });
            m.Items.Add(Lang.T("Delete key"), null, (_, _) => V.AnimDeleteKeys());
            m.Show(this, e.Location);
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.Middle && e.X >= LabelW) { _drag = ("pan", e.X, _t0); Cursor = Cursors.SizeWE; return; }
        if (e.Button != MouseButtons.Left) return;
        if (h is { Kind: "bar" })
        {
            var th = Thumb;
            if (!th.Contains(e.Location)) ScrollTo(_t0 + (e.X - (th.X + th.Width / 2)) * Total / Math.Max(1, Bar.Width));
            _drag = ("bar", e.X, _t0); Invalidate();
            return;
        }
        if (h is { Kind: "ruler" })
        {
            V.AnimStop(); _drag = ("scrub", e.X, V.PlayT); Scrub(e.X);
            Invalidate();
            return;
        }
        if ((h == null || h.Value.Kind == "track") && e.X >= LabelW && e.Y > Ruler)
        {
            // empty space: a drag draws a box that selects the keys in it; a plain click moves the time marker there
            _drag = ("box", e.X, 0); _boxA = _boxB = e.Location; _boxing = false; _boxAdd = add;
            _boxBase = add ? SelKeys.ToList() : new();
            return;
        }
        if (h == null) return;
        var hh = h.Value;
        switch (hh.Kind)
        {
            case "obj": V.Select(hh.Key); break;
            case "watch": V.AnimStopwatch(hh.Key, hh.Row); break;
            case "key":
                var k = (hh.Key, hh.Row, hh.T);
                if (add) { if (!SelKeys.Remove(k)) SelKeys.Add(k); }
                else if (!SelKeys.Contains(k)) { SelKeys.Clear(); SelKeys.Add(k); }
                if (hh.Key != V.Sel) V.Select(hh.Key);
                _dragKeys = SelKeys.ToList(); _dragDt = 0;
                _drag = ("keys", e.X, hh.T);
                V.AnimSettings();
                break;
        }
        Invalidate();
    }

    void Scrub(int x)
    {
        double t = Math.Max(0, T(x));
        if ((ModifierKeys & Keys.Alt) == 0) t = Math.Round(t * 20) / 20;      // 0.05 s steps (hold Alt = free)
        V.PlayT = t;
        V.AnimTimeMoved(false);
    }

    Rectangle BoxRect() => Rectangle.FromLTRB(Math.Min(_boxA.X, _boxB.X), Math.Min(_boxA.Y, _boxB.Y), Math.Max(_boxA.X, _boxB.X), Math.Max(_boxA.Y, _boxB.Y));

    void BoxSelect()
    {
        var r = BoxRect();
        SelKeys.Clear();
        foreach (var q in _boxBase) SelKeys.Add(q);
        foreach (var h in _hits) if (h.Kind == "key" && h.R.IntersectsWith(r)) SelKeys.Add((h.Key, h.Row, h.T));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is { Mode: "scrub" }) { Scrub(e.X); return; }
        if (_drag is { Mode: "pan" } pd) { ScrollTo(pd.T0 - (e.X - pd.X0) / PxPerS); return; }
        if (_drag is { Mode: "bar" } bd) { ScrollTo(bd.T0 + (e.X - bd.X0) * Total / Math.Max(1, Bar.Width)); return; }
        if (_drag is { Mode: "box" })
        {
            _boxB = new Point(Math.Clamp(e.X, LabelW, Width - 1), Math.Clamp(e.Y, Ruler, Height - 1));
            if (!_boxing && (Math.Abs(_boxB.X - _boxA.X) > 4 || Math.Abs(_boxB.Y - _boxA.Y) > 4)) _boxing = true;
            if (_boxing) { BoxSelect(); Invalidate(); }
            return;
        }
        if (_drag is { Mode: "keys" } d)
        {
            double dt = (e.X - d.X0) / PxPerS;
            double min = _dragKeys.Min(q => q.T);
            dt = Math.Max(-min, dt);
            if ((ModifierKeys & Keys.Alt) == 0) dt = Math.Round(dt * 20) / 20;
            _dragDt = dt;
            Invalidate();
            return;
        }
        var h = HitAt(e.Location);
        Cursor = h?.Kind is "key" or "watch" or "obj" or "bar" ? Cursors.Hand : e.Y < Ruler && e.X > LabelW ? Cursors.SizeWE : Cursors.Default;
        string tip = h is { Kind: "key" } hk ? KeyTip(hk.Key, hk.Row, hk.T) : h is { Kind: "watch" } ? Lang.T("Stopwatch: on = this row makes keys, off = all its keys go") : "";
        if (tip != _tipText) { _tipText = tip; if (tip == "") _tip.Hide(this); else _tip.Show(tip, this, e.X + 14, e.Y + 18, 4000); }
    }

    readonly ToolTip _tip = new() { BackColor = ColorTranslator.FromHtml("#20262f"), ForeColor = ColorTranslator.FromHtml("#d8dee6") };
    string _tipText = "";

    string KeyTip(string key, string row, double t)
    {
        var k = (V.AnimOf(key)?.Arr(row) ?? new()).OfType<JObj>().FirstOrDefault(q => Math.Abs(q.Num("t") - t) < 1e-6);
        if (k == null) return "";
        string head = Lang.T($"Key at {t:0.00} s");
        return row == "keys" ? head + "\n" + $"X {k.Num("x"):0.#}   {Lang.T("Height")} {k.Num("y"):0.#}   Z {k.Num("z"):0.#}"
            : head + "\n" + $"{Lang.T("Turn")} {k.Num("angle"):0.#}°";
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _tipText = ""; _tip.Hide(this); }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitAt(e.Location) is { Kind: "key" } h) { V.AnimStop(); V.PlayT = h.T; V.AnimTimeMoved(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var d = _drag; _drag = null;
        if (d is { Mode: "scrub" }) { V.AnimTimeMoved(); return; }
        if (d is { Mode: "pan" or "bar" }) { Cursor = Cursors.Default; Invalidate(); return; }
        if (d is { Mode: "box" })
        {
            if (_boxing) { _boxing = false; V.AnimSettings(); Invalidate(); return; }
            // a plain click on empty space: nothing selected any more, the time marker goes there
            if (!_boxAdd && SelKeys.Count > 0) { SelKeys.Clear(); V.AnimSettings(); }
            V.AnimStop(); Scrub(e.X); V.AnimTimeMoved(); Invalidate();
            return;
        }
        if (d is { Mode: "keys" } && Math.Abs(_dragDt) > 1e-6)
        {
            var moved = _dragKeys.ToList(); double dt = _dragDt;
            SelKeys.Clear(); foreach (var q in moved) SelKeys.Add((q.Key, q.Row, Math.Round(Math.Max(0, q.T + dt), 2)));
            _dragDt = 0; _dragKeys.Clear();
            V.AnimShiftKeys(moved, dt);
        }
        _dragDt = 0;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Wheel(e.X, e.Delta, (ModifierKeys & Keys.Shift) != 0); }

    /// wheel = zoom with the left edge staying where it is (0 stays at the left); Shift + wheel = scroll
    void Wheel(int x, int delta, bool shift)
    {
        if (shift) { ScrollTo(_t0 - Math.Sign(delta) * _len * 0.2); return; }
        _len = Math.Clamp(_len * (delta > 0 ? 0.8 : 1.25), 0.5, 600);
        _autoFit = false;
        _t0 = Math.Clamp(_t0, 0, Math.Max(0, Total - _len));
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.A && e.Control && V.Sel is { } sk && V.AnimOf(sk) is { } sa)
        {
            // Ctrl+A: every key of the selected object
            SelKeys.Clear();
            foreach (var row in new[] { "keys", "rkeys" }) foreach (var k in (sa.Arr(row) ?? new()).OfType<JObj>()) SelKeys.Add((sk, row, k.Num("t")));
            V.AnimSettings(); Invalidate(); e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left) { Step(-1); e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { Step(1); e.Handled = true; }
    }
}

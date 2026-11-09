// The language of the window. The program is written in English; assets\ru.txt holds the Russian. Texts are translated as they reach the screen,
// so nothing that reads names, items or keys back is touched: with English nothing here does anything.
using System.Text.RegularExpressions;

namespace Bmb.App;

public static class Lang
{
    public static string Code = "en";
    public static bool Ru => Code == "ru";
    public static readonly HashSet<string> Missing = new();
    static readonly Dictionary<string, string> _exact = new(), _seen = new();
    static readonly List<(Regex Rx, string To)> _forms = new();
    static readonly Regex Ends = new(@"^([^\p{L}\d]*)(.*?)([^\p{L}\d)\]]*)$", RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex Tail = new(@"^(.*?)(\s+[\(\[][^()\[\]]*[\)\]])$", RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly string[] Seps = { "   |   ", "  -  ", " - ", ":  ", ": ", ", " };

    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlurMapBuilderCS");
    static string File1 => Path.Combine(Folder, "language.txt");

    /// false on the very first start: the program then asks
    public static bool Load()
    {
        string? env = Environment.GetEnvironmentVariable("BMB_LANG"), code = env;
        if (code == null) try { code = File.Exists(File1) ? File.ReadAllText(File1).Trim() : null; } catch (IOException) { }
        Use(code ?? "en");
        return code != null;
    }

    public static void Save(string code)
    {
        try { Directory.CreateDirectory(Folder); File.WriteAllText(File1, code); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static void Use(string code)
    {
        Code = code == "ru" ? "ru" : "en";
        _exact.Clear(); _forms.Clear(); _seen.Clear();
        if (!Ru) return;
        using var r = new StreamReader(typeof(Lang).Assembly.GetManifestResourceStream("ru.txt")!);
        var forms = new List<(string En, string To)>();
        while (r.ReadLine() is { } line)
        {
            int at = line.IndexOf(" ||| ", StringComparison.Ordinal);
            if (at <= 0) continue;
            string en = line[..at].Replace("\\n", "\n"), to = line[(at + 5)..].Replace("\\n", "\n");
            if (en.Contains("{}")) forms.Add((en, to)); else _exact[en] = to;
        }
        foreach (var (en, to) in forms.OrderByDescending(f => f.En.Replace("{}", "").Length))
            _forms.Add((new Regex("^" + string.Join("(.*?)", en.Split("{}").Select(Regex.Escape)) + "$", RegexOptions.Singleline | RegexOptions.Compiled), to));
    }

    public static string T(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        if (!Ru) return s;
        if (_seen.TryGetValue(s, out var hit)) return hit;
        if (_seen.Count > 8000) _seen.Clear();
        return _seen[s] = Find(s, 0) ?? Note(s);
    }

    static string Note(string s) { if (Regex.IsMatch(s, "[A-Za-z]{3}") && !Regex.IsMatch(s, "[А-Яа-я]")) lock (Missing) Missing.Add(s); return s; }

    static string? Find(string s, int depth)
    {
        if (_exact.TryGetValue(s, out var e)) return e;
        foreach (var (rx, to) in _forms)
        {
            var m = rx.Match(s);
            if (!m.Success) continue;
            int k = 0;
            return Regex.Replace(to, @"\{\}", _ => { var g = m.Groups[++k].Value; return depth < 3 ? Find(g, depth + 1) ?? g : g; });
        }
        if (depth > 3) return null;
        var w = Ends.Match(s);                                 // icons and spaces around it
        if (w.Success && w.Groups[2].Length > 1 && w.Groups[2].Length < s.Length && Find(w.Groups[2].Value, depth + 1) is { } inner) return w.Groups[1].Value + inner + w.Groups[3].Value;
        var t = Tail.Match(s);                                 // "(Ctrl+N)", "[lamp:3]", "(12)" after it
        if (t.Success && Find(t.Groups[1].Value, depth + 1) is { } head) return head + t.Groups[2].Value;
        foreach (var sep in Seps)
        {
            int at = s.IndexOf(sep, StringComparison.Ordinal);
            if (at <= 0) continue;
            string a = s[..at], b = s[(at + sep.Length)..];
            string? ta = Find(a, depth + 1), tb = Find(b, depth + 1);
            if (ta != null || tb != null) return (ta ?? a) + sep + (tb ?? b);
        }
        return null;
    }

    /// every label, button and window under this one, also the ones added or changed later
    public static void Watch(Control c)
    {
        if (!Ru) return;
        if (c is Label or ButtonBase or Form or GroupBox)
        {
            void Fix() { string t = T(c.Text); if (t != c.Text) c.Text = t; }
            c.TextChanged += (_, _) => Fix();
            Fix();
        }
        c.ControlAdded += (_, e) => { if (e.Control != null) Watch(e.Control); };
        foreach (Control q in c.Controls) Watch(q);
    }

    public static void Nodes(TreeNodeCollection n) { if (Ru) foreach (TreeNode q in n) q.Text = T(q.Text); }
    public static void Items(ToolStripItemCollection n) { if (Ru) foreach (ToolStripItem q in n) q.Text = T(q.Text); }
}

/// every message box of the program, in its language and in the program's dark look (not the white Windows box)
static class MessageBox
{
    public static DialogResult Show(string text, string title = "") => DarkBox.Show(null, text, title, MessageBoxButtons.OK, MessageBoxIcon.None);
    public static DialogResult Show(IWin32Window owner, string text, string title = "", MessageBoxButtons b = MessageBoxButtons.OK, MessageBoxIcon i = MessageBoxIcon.None) => DarkBox.Show(owner, text, title, b, i);
    public static DialogResult Show(string text, string title, MessageBoxButtons b, MessageBoxIcon i = MessageBoxIcon.None) => DarkBox.Show(null, text, title, b, i);
}

/// the dark message window: a symbol, the text (scrolls when long), the buttons on the right; Enter = the first, Esc = cancel / no
static class DarkBox
{
    public static DialogResult Show(IWin32Window? owner, string text, string title, MessageBoxButtons b, MessageBoxIcon i)
    {
        using var f = new Form
        {
            Text = Lang.T(title == "" ? "Blur Map Builder" : title), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = owner == null,
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Ui,
            Padding = new Padding(0), KeyPreview = true,
        };
        Look.Dress(f);
        var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(18, 18, 22, 12), BackColor = Theme.Panel, Dock = DockStyle.Top };
        (string Sym, Color Col)? sym = i switch
        {
            MessageBoxIcon.Error => ("✖", Theme.Bad),
            MessageBoxIcon.Warning => ("⚠", Theme.Warn),
            MessageBoxIcon.Question => ("?", Theme.Head),
            MessageBoxIcon.Information => ("ℹ", Theme.Head),
            _ => b is MessageBoxButtons.YesNo or MessageBoxButtons.YesNoCancel ? ("?", Theme.Head) : null,
        };
        if (sym != null) body.Controls.Add(new Label { Text = sym.Value.Sym, AutoSize = true, ForeColor = sym.Value.Col, Font = new Font("Segoe UI Symbol", 20f, FontStyle.Bold), Margin = new Padding(0, 0, 14, 0) }, 0, 0);
        string t = Lang.T(text);
        var lbl = new Label { Text = t, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f), UseMnemonic = false, Margin = new Padding(0, 4, 0, 0) };
        Control msg = lbl;
        if (lbl.GetPreferredSize(new Size(560, 0)).Height > 420)
        {
            var box = new TextBox { Text = t.Replace("\r\n", "\n").Replace("\n", "\r\n"), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f), Size = new Size(580, 400), TabStop = false };
            msg = box;
        }
        body.Controls.Add(msg, sym != null ? 1 : 0, 0);
        if (sym == null) body.SetColumnSpan(msg, 2);
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top, BackColor = Theme.Panel2, Padding = new Padding(12, 10, 14, 10), WrapContents = false };
        var list = b switch
        {
            MessageBoxButtons.YesNo => new[] { ("Yes", DialogResult.Yes), ("No", DialogResult.No) },
            MessageBoxButtons.YesNoCancel => new[] { ("Yes", DialogResult.Yes), ("No", DialogResult.No), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.OKCancel => new[] { ("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.RetryCancel => new[] { ("Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel) },
            _ => new[] { ("OK", DialogResult.OK) },
        };
        var result = list.Any(q => q.Item2 == DialogResult.Cancel) ? DialogResult.Cancel : list.Any(q => q.Item2 == DialogResult.No) ? DialogResult.No : list[0].Item2;
        Button? first = null;
        foreach (var (name, r) in list.Reverse())       // right to left: the first button ends up on the left
        {
            var rr = r;
            var btn = Theme.Btn(Lang.T(name), () => { result = rr; f.Close(); }, rr == list[0].Item2 ? Theme.Blue : null);
            btn.MinimumSize = new Size(88, 30); btn.Margin = new Padding(6, 0, 0, 0);
            if (rr == list[0].Item2) { btn.Font = Theme.Bold; first = btn; }
            row.Controls.Add(btn);
        }
        f.Controls.Add(row); f.Controls.Add(body);
        var bs = body.GetPreferredSize(Size.Empty); var rs = row.GetPreferredSize(Size.Empty);
        f.ClientSize = new Size(Math.Max(380, Math.Max(bs.Width, rs.Width)), bs.Height + rs.Height);
        f.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { f.Close(); e.Handled = true; }        // result stays cancel / no / ok
            else if (e.KeyCode == Keys.Enter) { result = list[0].Item2; f.Close(); e.Handled = true; }
        };
        f.Shown += (_, _) => { first?.Focus(); f.Activate(); };
        if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
        return result;
    }
}

/// the first start: English or Russian
public static class LangWindow
{
    public static void Run()
    {
        var f = new Form { Text = "Blur Map Builder", ClientSize = new Size(420, 170), BackColor = Theme.Panel, ForeColor = Theme.Text, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        Look.Dress(f);
        f.Controls.Add(new Label { Text = "Choose your language", AutoSize = false, Bounds = new Rectangle(0, 18, 420, 26), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Head });
        f.Controls.Add(new Label { Text = "Выберите язык", AutoSize = false, Bounds = new Rectangle(0, 44, 420, 26), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Head });
        string code = "en";
        void Add(string text, string c, int x)
        {
            var b = Theme.Btn(text, () => { code = c; f.Close(); }, Theme.Blue);
            b.AutoSize = false; b.Bounds = new Rectangle(x, 92, 170, 46); b.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            f.Controls.Add(b);
        }
        Add("English", "en", 30); Add("Русский", "ru", 220);
        Application.Run(f);
        Lang.Save(code); Lang.Use(code);
    }
}

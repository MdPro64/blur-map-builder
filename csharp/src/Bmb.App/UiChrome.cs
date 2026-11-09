// The window's frame, kept small so the work area gets the room (like a pro editor: one slim top bar, settings in a
// menu, small icon rows on the panels, right-click menus): the ⚙ menu, the dark menu look, the map list's tools.
using Bmb.Core;

namespace Bmb.App;

/// dark drop-down menus (the Windows ones are white)
public static class DarkMenu
{
    sealed class Colours : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.GlassHi;
        public override Color MenuItemBorder => Theme.Blue;
        public override Color MenuBorder => Theme.GlassHi;
        public override Color ToolStripDropDownBackground => Theme.Panel2;
        public override Color ImageMarginGradientBegin => Theme.Panel2;
        public override Color ImageMarginGradientMiddle => Theme.Panel2;
        public override Color ImageMarginGradientEnd => Theme.Panel2;
        public override Color SeparatorDark => Theme.GlassHi;
        public override Color SeparatorLight => Theme.Panel2;
        public override Color MenuItemSelectedGradientBegin => Theme.GlassHi;
        public override Color MenuItemSelectedGradientEnd => Theme.GlassHi;
        public override Color CheckBackground => Theme.Blue;
        public override Color CheckSelectedBackground => Theme.Blue;
    }
    sealed class Painter : ToolStripProfessionalRenderer
    {
        public Painter() : base(new Colours()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Color.FromArgb(110, 125, 140) : e.Item.Tag as string == "danger" ? Theme.Bad : Theme.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Text; base.OnRenderArrow(e); }
    }

    public static ContextMenuStrip Make()
    {
        var m = new ContextMenuStrip { Renderer = new Painter(), BackColor = Theme.Panel2, ForeColor = Theme.Text, Font = Theme.Ui, ShowImageMargin = false };
        return m;
    }

    public static ToolStripMenuItem Item(ToolStripItemCollection to, string text, Action a, bool danger = false, bool on = true)
    {
        var i = new ToolStripMenuItem(Lang.T(text)) { ForeColor = Theme.Text, Enabled = on, Tag = danger ? "danger" : null };
        i.Click += (_, _) => a();
        to.Add(i);
        return i;
    }

    public static ToolStripMenuItem Sub(ToolStripItemCollection to, string text)
    {
        var i = new ToolStripMenuItem(Lang.T(text)) { ForeColor = Theme.Text };
        if (i.DropDown is ToolStripDropDownMenu d) { d.Renderer = new Painter(); d.BackColor = Theme.Panel2; d.ShowImageMargin = false; d.ShowCheckMargin = true; }
        to.Add(i);
        return i;
    }
}

/// a list drawn like the map list: roomy rows, the chosen one as a rounded blue bar
public static class RoundList
{
    public static void Dress(ListBox l, int rowH = 24)
    {
        l.DrawMode = DrawMode.OwnerDrawFixed; l.ItemHeight = rowH;
        l.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(l.BackColor)) e.Graphics.FillRectangle(back, e.Bounds);
            if (sel)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var row = Look.RoundPath(new RectangleF(e.Bounds.X + 3, e.Bounds.Y + 1, e.Bounds.Width - 7, e.Bounds.Height - 2), 5);
                using var bg = new SolidBrush(Theme.Blue); e.Graphics.FillPath(bg, row);
            }
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, l.Items[e.Index]?.ToString() ?? "", l.Font, r, sel ? Color.White : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        };
    }
}

public sealed partial class MainForm
{
    bool _updateFound;

    /// ⚙: everything that is not needed every minute
    void SettingsMenu(Control under)
    {
        var m = DarkMenu.Make();
        m.ShowCheckMargin = true;
        DarkMenu.Item(m.Items, "Simple layout  (icons on the side)", () => SetSimple(true)).Checked = Simple;
        DarkMenu.Item(m.Items, "Advanced layout  (all the options)", () => SetSimple(false)).Checked = !Simple;
        m.Items.Add(new ToolStripSeparator());
        DarkMenu.Item(m.Items, "⟳  Check for updates", () => CheckUpdates(false));
        DarkMenu.Item(m.Items, "⚙  Where updates come from...", UpdateSource);
        m.Items.Add(new ToolStripSeparator());
        var lay = DarkMenu.Sub(m.Items, "▭  Window layout");
        foreach (var n in _layoutBox.Items.Cast<string>())
        {
            string nn = n;
            var it = DarkMenu.Item(lay.DropDownItems, nn, () => { _layoutBox.SelectedItem = nn; LayoutLoad(); });
            it.Checked = nn == _current;
        }
        lay.DropDownItems.Add(new ToolStripSeparator());
        DarkMenu.Item(lay.DropDownItems, "Save this layout as...", LayoutSave);
        DarkMenu.Item(lay.DropDownItems, "Reset the panels (back to the saved sizes)", LayoutReset);
        DarkMenu.Item(lay.DropDownItems, "Delete the current layout", () => { _layoutBox.SelectedItem = _current; LayoutDelete(); }, true, _current != "Default");
        var lang = DarkMenu.Sub(m.Items, "🌐  Language");
        foreach (var (i, n) in new[] { (0, "English"), (1, "Русский") })
        {
            int ii = i;
            var it = DarkMenu.Item(lang.DropDownItems, n, () => { _langBox.SelectedIndex = ii; LanguageChanged(); });
            it.Checked = _langBox.SelectedIndex == i;
        }
        m.Items.Add(new ToolStripSeparator());
        DarkMenu.Item(m.Items, "❔  Help for this tab  (F1)", Help);
        DarkMenu.Item(m.Items, $"Blur Map Builder {Paths.Version}", () => { }, false, false);
        LastMenu = m;
        m.Show(under, new Point(0, under.Height));
    }
    public ContextMenuStrip? LastMenu;

    /// the map list: a small row of icons under its title + the same as a right-click menu
    Control TreeTools()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(2, 0, 0, 2) };
        Button Icon(string t, string tip, Action a)
        {
            var b = Theme.Icon(t, a, tip); b.Margin = new Padding(2, 0, 0, 0); b.AutoSize = false; b.Size = new Size(34, 28); b.Padding = new Padding(0); b.AutoEllipsis = false; b.TextAlign = ContentAlignment.MiddleCenter;
            row.Controls.Add(b); _treeBtns.Add(b);
            return b;
        }
        Icon("✚", "New map (Ctrl+N)", NewMap);
        Icon("⌂", "New location (Ctrl+L)", NewLocation);
        Icon("⧉", "Duplicate this map (Ctrl+D)", DupMap);
        Icon("✎", "Rename (F2)", Rename);
        Button? more = null;
        more = Icon("⋯", "More: import, export, delete", () => TreeMenu(more!, new Point(0, more!.Height)));
        _tree.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int i = _tree.IndexFromPoint(e.Location);
            if (i >= 0 && i != _tree.SelectedIndex) _tree.SelectedIndex = i;
            TreeMenu(_tree, e.Location);
        };
        return row;
    }

    void TreeMenu(Control at, Point p)
    {
        var m = DarkMenu.Make();
        DarkMenu.Item(m.Items, "✚  New map   (Ctrl+N)", NewMap);
        DarkMenu.Item(m.Items, "⌂  New location   (Ctrl+L)", NewLocation);
        DarkMenu.Item(m.Items, "⧉  Duplicate map   (Ctrl+D)", DupMap);
        DarkMenu.Item(m.Items, "✎  Rename   (F2)", Rename);
        m.Items.Add(new ToolStripSeparator());
        DarkMenu.Item(m.Items, "⇩  Import map (.blurmap)...", ImportMap);
        DarkMenu.Item(m.Items, "⇧  Export this map...", ExportMap);
        m.Items.Add(new ToolStripSeparator());
        DarkMenu.Item(m.Items, "✖  Delete map", DeleteMap, true);
        DarkMenu.Item(m.Items, "✖  Delete location", DeleteLocation, true);
        LastMenu = m;
        m.Show(at, p);
    }
}

using System.Drawing;
using System.Windows.Forms;
using Bmb.Core;

namespace Bmb.App;

/// "Export / Import": one file for another player - a map (.blurmap: the map + its pictures, 3D models and sky), a custom car
/// (.blurcar: the car + its body, sounds and logo) or a game car's skins (.blurskin). Importing puts every file in its place.
public sealed class SharePanel : Panel
{
    readonly MainForm _f;
    readonly Label _mapName = new() { AutoSize = true, ForeColor = Theme.Good, Margin = new Padding(0, 8, 12, 0) };
    readonly ComboBox _car = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Margin = new Padding(0, 4, 8, 0) };
    readonly ComboBox _skin = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Margin = new Padding(0, 4, 8, 0) };
    readonly List<string> _skinCars = new();

    public SharePanel(MainForm f)
    {
        _f = f; BackColor = Theme.Bg; AutoScroll = true; Padding = new Padding(24);
        var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Dock = DockStyle.Top };
        Controls.Add(col);
        col.Controls.Add(new Label { Text = "EXPORT / IMPORT", AutoSize = true, ForeColor = Theme.Head, Font = new Font("Segoe UI", 14f, FontStyle.Bold) });
        col.Controls.Add(Hint("Make ONE file to give to another player. They press Import here and everything it needs is put in place. Then Build + install."));

        // a map
        col.Controls.Add(Head("🗺  A map  (.blurmap)"));
        col.Controls.Add(Hint("The map you have open, with its menu picture, minimap, your 3D models on it and its location's lighting + sky picture."));
        var r1 = Row();
        r1.Controls.Add(Theme.Lbl("This map:")); r1.Controls.Add(_mapName);
        r1.Controls.Add(Theme.Btn("⇧ Export map...", () => { _f.ExportMap(); }, Theme.Orange));
        r1.Controls.Add(Theme.Btn("⇩ Import map...", () => { _f.ImportMap(); Refresh2(); }));
        col.Controls.Add(r1);

        // a custom car
        col.Controls.Add(Head("⛟  A custom car  (.blurcar)"));
        col.Controls.Add(Hint("The car with its settings, its imported body (.glb), all its own sounds and its logo."));
        var r2 = Row();
        r2.Controls.Add(_car);
        r2.Controls.Add(Theme.Btn("⇧ Export car...", ExportCar, Theme.Orange));
        r2.Controls.Add(Theme.Btn("⇩ Import car...", ImportCar));
        col.Controls.Add(r2);

        // skins
        col.Controls.Add(Head("🎨  Car skins  (.blurskin)"));
        col.Controls.Add(Hint("All your skins of one game car, and which of them are on."));
        var r3 = Row();
        r3.Controls.Add(_skin);
        r3.Controls.Add(Theme.Btn("⇧ Export skins...", ExportSkins, Theme.Orange));
        r3.Controls.Add(Theme.Btn("⇩ Import skins...", ImportSkins));
        col.Controls.Add(r3);

    }

    static Label Head(string t) => new() { Text = t, AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(0, 22, 0, 2) };
    static Label Hint(string t) => new() { Text = t, AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = ColorTranslator.FromHtml("#9aa8b8"), Margin = new Padding(0, 2, 0, 4) };
    static FlowLayoutPanel Row() => new() { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0, 4, 0, 0) };

    List<JObj> Cars() => _f.D.World.Objs("cars").ToList();

    public void Refresh2()
    {
        _mapName.Text = $"{_f.D.Map.Str("name")}  ({_f.D.Loc.Str("name")})";
        int ci = _car.SelectedIndex;
        _car.Items.Clear();
        foreach (var c in Cars()) _car.Items.Add(c.Str("name") ?? "car");
        if (_car.Items.Count == 0) _car.Items.Add("(no custom car yet)");
        _car.SelectedIndex = Math.Clamp(ci, 0, _car.Items.Count - 1);
        string? sk = _skin.SelectedIndex >= 0 && _skin.SelectedIndex < _skinCars.Count ? _skinCars[_skin.SelectedIndex] : null;
        _skin.Items.Clear(); _skinCars.Clear();
        foreach (var (car, v) in Skins.All())
            if (v is JObj pics && pics.Count > 0) { _skinCars.Add(car); _skin.Items.Add($"{Skins.Nice(car)}   ({pics.Count} skin{(pics.Count == 1 ? "" : "s")})"); }
        if (_skin.Items.Count == 0) _skin.Items.Add("(no skin yet)");
        _skin.SelectedIndex = sk != null && _skinCars.Contains(sk) ? _skinCars.IndexOf(sk) : 0;
    }

    void ExportCar()
    {
        var cars = Cars();
        if (cars.Count == 0 || _car.SelectedIndex < 0 || _car.SelectedIndex >= cars.Count) { _f.Say("Make a custom car first (Custom car tab)", Theme.Warn); return; }
        var c = cars[_car.SelectedIndex];
        string? path = _f.SaveAs("Export the car for another player", "Blur car|*" + Packs.CarExt, Packs.Plain(c.Str("name") ?? "car") + Packs.CarExt);
        if (path == null) return;
        try
        {
            var (n, missing) = Packs.ExportCar(c, path);
            _f.Say($"Exported {c.Str("name")} with {n} file(s) to {path}" + (missing.Count > 0 ? $" - MISSING here (not packed): {string.Join(", ", missing)}" : ""), missing.Count > 0 ? Theme.Warn : Theme.Good);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _f.Say("Could not export: " + e.Message, Theme.Bad); }
    }

    void ImportCar()
    {
        string? path = _f.PickFile("Import a car from another player", "Blur car|*" + Packs.CarExt + "|All files|*.*");
        if (path == null) return;
        JObj car;
        try { car = Packs.ImportCar(path); }
        catch (Exception e) when (e is ImportProblem or IOException or UnauthorizedAccessException or FormatException or InvalidCastException) { _f.Msg("Import", "Could not import this file:\n" + e.Message); return; }
        // a free name (24 letters at most), and a game car can only be replaced by one car
        var names = Cars().Select(c => c.Str("name")).ToHashSet();
        string bas = (car.Str("name") ?? "car"); bas = bas[..Math.Min(20, bas.Length)];
        string name = bas; int n = 2;
        while (names.Contains(name)) name = $"{bas} ({n++})";
        car["name"] = name;
        string note = "";
        if (!CarNetId.Unique(car, Cars())) note = " - you already have this car (same number), so this copy gets its own number: online it is not the other player's car";
        if (car.Truthy("replace") && Cars().Any(c => c.Str("replace") == car.Str("replace")))
        {
            note = $" - the {Skins.Nice(car.Str("replace")!)} is already replaced by another of your cars, so this one is added as a new car";
            car["replace"] = null;
        }
        _f.D.Snapshot();
        if (_f.D.World.Arr("cars") == null) _f.D.World["cars"] = new List<object?>();
        _f.D.World.Arr("cars")!.Add(car);
        _f.D.Touched(); _f.Changed();
        Refresh2();
        _car.SelectedIndex = Math.Max(0, Cars().Count - 1);
        _f.Say($"Imported the car '{name}'{note}. See it in the Custom car tab; Build + install to drive it.", note.Length > 0 ? Theme.Warn : Theme.Good);
    }

    void ExportSkins()
    {
        if (_skinCars.Count == 0 || _skin.SelectedIndex < 0 || _skin.SelectedIndex >= _skinCars.Count) { _f.Say("Make a skin first (Car skins tab)", Theme.Warn); return; }
        string car = _skinCars[_skin.SelectedIndex];
        string? path = _f.SaveAs("Export the skins for another player", "Blur skins|*" + Packs.SkinExt, car + Packs.SkinExt);
        if (path == null) return;
        try { int n = Packs.ExportSkins(car, path); _f.Say($"Exported {n} skin picture(s) of the {Skins.Nice(car)} to {path}", Theme.Good); }
        catch (Exception e) when (e is ImportProblem or IOException or UnauthorizedAccessException) { _f.Say("Could not export: " + e.Message, Theme.Bad); }
    }

    void ImportSkins()
    {
        string? path = _f.PickFile("Import skins from another player", "Blur skins|*" + Packs.SkinExt + "|All files|*.*");
        if (path == null) return;
        try
        {
            var (car, n) = Packs.ImportSkins(path);
            Refresh2();
            _f.Say($"Imported {n} skin picture(s) of the {Skins.Nice(car)}. See them in the Car skins tab; Build + install (or Build skins) to see them in game.", Theme.Good);
        }
        catch (Exception e) when (e is ImportProblem or IOException or UnauthorizedAccessException or InvalidDataException or FormatException or InvalidCastException) { _f.Msg("Import", "Could not import this file:\n" + e.Message); }
    }
}

// One-file packages for sharing with other players: a car (.blurcar = the car + its imported body, own sounds and logo) and
// the skins of a game car (.blurskin = its pictures + which are on), the power-up icons (.blurpowerups). The map (.blurmap) is in Share. Each is a zip; reading
// one never trusts its paths: every file goes into the program's own folders under a plain name.
using System.IO.Compression;
using System.Text;

namespace Bmb.Core;

public static class Packs
{
    public const string CarExt = ".blurcar", SkinExt = ".blurskin", IconExt = ".blurpowerups";
    const int Format = 1;
    const long MaxEntry = 200L * 1024 * 1024;

    // ---- helpers ------------------------------------------------------------------------------------------------------

    /// a file name that is safe to write in our folders (no folders, no odd characters)
    public static string Plain(string name)
    {
        name = Path.GetFileName(name.Replace('\\', '/'));
        var s = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ' or '(' or ')' ? c : '_').ToArray()).Trim();
        if (s.Length == 0 || s.Trim('.').Length == 0) s = "file";
        return s.Length > 80 ? s[..60] + Path.GetExtension(s) : s;
    }

    /// writes bytes into folder as name; a different file of the same name already there: name_2, name_3 ... (the same bytes:
    /// the one there is used). Returns the name it has.
    public static string Place(string folder, string name, byte[] data)
    {
        Directory.CreateDirectory(folder);
        name = Plain(name);
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int n = 1; ; n++)
        {
            string cand = n == 1 ? name : $"{stem}_{n}{ext}", p = Path.Combine(folder, cand);
            if (!File.Exists(p)) { File.WriteAllBytes(p, data); return cand; }
            if (new FileInfo(p).Length == data.Length && File.ReadAllBytes(p).AsSpan().SequenceEqual(data)) return cand;
        }
    }

    static void Text(ZipArchive z, string name, string s) { using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false)); w.Write(s); }

    /// every entry (not too big, no folder tricks) as name -> bytes
    static Dictionary<string, byte[]> All(ZipArchive z)
    {
        var o = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in z.Entries)
        {
            if (e.Length > MaxEntry || e.FullName.Contains("..") || e.FullName.StartsWith('/') || e.FullName.Contains(':') || e.FullName.EndsWith('/')) continue;
            using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms);
            o[e.FullName.Replace('\\', '/')] = ms.ToArray();
        }
        return o;
    }

    static Dictionary<string, byte[]> Open(string path, string what)
    {
        try { using var z = ZipFile.OpenRead(path); return All(z); }
        catch (InvalidDataException) { throw new ImportProblem($"this is not a {what} file"); }
    }

    static JObj JsonOf(Dictionary<string, byte[]> f, string name, string what)
    {
        if (!f.TryGetValue(name, out var b)) throw new ImportProblem($"no {what} inside ({name} missing)");
        if (Json.Parse(Encoding.UTF8.GetString(b)) is not JObj o) throw new ImportProblem($"{name} is broken");
        if (o.Int("format", Format) > Format) throw new ImportProblem($"made by a newer map builder (format {o.Int("format")}) - update yours");
        return o;
    }

    // ---- a car ------------------------------------------------------------------------------------------------------

    static string CarsDir => Path.Combine(Paths.Tracks, "cars");

    /// the car's files: (key in the car, folder, file name)
    static IEnumerable<(string Key, string? Kind, string Folder, string File)> CarFiles(JObj car)
    {
        if (car.Truthy("body_glb")) yield return ("body_glb", null, CarsDir, car.Str("body_glb")!);
        foreach (var k in new[] { "sound_own", "sound_own_file" })
            if (car.Truthy(k)) yield return (k, null, Path.Combine(CarsDir, OwnSound.Folder), car.Str(k)!);
        if (car.Obj("sounds") is { } sd)
            foreach (var (k, v) in sd) if (v is string f && f.Length > 0) yield return ("sounds", k, Path.Combine(CarsDir, OwnSound.Folder), f);
        if (car.Truthy("logo")) yield return ("logo", null, Path.Combine(CarsDir, CarLogo.Folder), car.Str("logo")!);
    }

    static string Sub(string folder) => folder.EndsWith(OwnSound.Folder) ? "sounds/" : folder.EndsWith(CarLogo.Folder) ? "logo/" : "body/";

    /// a car and everything it needs into one .blurcar. Returns the number of files packed (and the ones that were missing)
    public static (int Packed, List<string> Missing) ExportCar(JObj car, string path)
    {
        var missing = new List<string>();
        CarNetId.Pin(car);                 // its number goes with it: the same car online on the other PC
        if (File.Exists(path)) File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, folder, file) in CarFiles(car))
        {
            string src = Path.Combine(folder, file), entry = Sub(folder) + Plain(file);
            if (!done.Add(entry)) continue;
            if (File.Exists(src)) z.CreateEntryFromFile(src, entry); else missing.Add(file);
        }
        Text(z, "car.json", Json.Dumps(car, 1));
        Text(z, "info.json", Json.Dumps(new JObj { ["format"] = (long)Format, ["kind"] = "car", ["name"] = car.Str("name"), ["made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["app"] = "Blur Map Builder" }, 1));
        return (done.Count - missing.Count, missing);
    }

    /// a .blurcar read and its files put into our folders (a different file of the same name there: the new one gets _2 ...).
    /// Returns the car, with its file names as they are here now (the caller gives it a free name and adds it)
    public static JObj ImportCar(string path)
    {
        var f = Open(path, ".blurcar");
        var car = JsonOf(f, "car.json", "car");
        if (!car.Truthy("base") || car["class"] == null) throw new ImportProblem("the car in this file has no body car / class");
        var placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);      // entry -> name here
        string? Put(string folder, string file)
        {
            string entry = Sub(folder) + Plain(file);
            if (placed.TryGetValue(entry, out var got)) return got;
            if (!f.TryGetValue(entry, out var data)) return null;
            return placed[entry] = Place(folder, file, data);
        }
        foreach (var (key, kind, folder, file) in CarFiles(car).ToList())
        {
            string? here = Put(folder, file);
            if (key == "sounds") { var sd = car.Obj("sounds")!; if (here != null) sd[kind!] = here; else sd.Remove(kind!); }
            else if (here != null) car[key] = here;
            else car[key] = null;          // its file was not in the package
        }
        if (car.Obj("sounds") is { Count: 0 }) car["sounds"] = null;
        return car;
    }

    // ---- skins ------------------------------------------------------------------------------------------------------

    /// every skin of one game car (its pictures + which are on) into one .blurskin. Returns the number of pictures
    public static int ExportSkins(string car, string path)
    {
        var pics = Skins.All().Obj(car) ?? throw new ImportProblem("this car has no skin");
        if (File.Exists(path)) File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        var list = new JObj(); int n = 0;
        foreach (var (pic, v) in pics)
        {
            string src = Skins.SkinFile(car, pic);
            if (!File.Exists(src)) continue;
            z.CreateEntryFromFile(src, "pictures/" + Plain(pic) + ".png");
            list[pic] = new JObj { ["on"] = v is JObj j && j.Truthy("on") }; n++;      // the picture's own name (the game's slot, e.g. "..._scratch#DXT5")
        }
        Text(z, "skins.json", Json.Dumps(new JObj { ["format"] = (long)Format, ["kind"] = "skin", ["car"] = car, ["pictures"] = list, ["made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["app"] = "Blur Map Builder" }, 1));
        return n;
    }

    /// a .blurskin put into the player's skins (a picture of the same name is replaced). Returns (car, pictures)
    public static (string Car, int Pictures) ImportSkins(string path)
    {
        var f = Open(path, ".blurskin");
        var info = JsonOf(f, "skins.json", "skin");
        string car = info.Str("car") ?? throw new ImportProblem("no car named in this file");
        if (!Skins.Cars().Any(c => c.Car == car)) throw new ImportProblem($"{car} is not a car whose skin can be changed in this game");
        int n = 0;
        foreach (var (pic, v) in info.Obj("pictures") ?? new JObj())
        {
            // the name is the game's picture slot: kept as it is, but never a path
            if (pic.Length == 0 || pic.Length > 120 || pic.Contains('/') || pic.Contains('\\') || pic.Contains("..") || pic.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            if (!f.TryGetValue("pictures/" + Plain(pic) + ".png", out var data)) continue;
            string tmp = Path.Combine(Path.GetTempPath(), "bmb_skin_" + Guid.NewGuid().ToString("N") + ".png");
            try { File.WriteAllBytes(tmp, data); Skins.Set(car, pic, tmp); } finally { try { File.Delete(tmp); } catch (IOException) { } }
            Skins.Turn(car, pic, v is JObj j && j.Truthy("on"));
            n++;
        }
        return (car, n);
    }

    // ---- the power-up icons -------------------------------------------------------------------------------------------

    /// all the player's power-up pictures (symbol or whole icon per power-up, and their shape notes) into one .blurpowerups.
    /// Returns how many power-ups have a picture
    public static int ExportIcons(string path)
    {
        string dir = PowerupIcons.OwnFolder;
        if (File.Exists(path)) File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        var icons = new JObj(); int n = 0;
        foreach (var (key, _) in PowerupIcons.Kinds)
        {
            bool any = false;
            foreach (var name in PowerupIcons.FileNames(key))
            {
                string src = Path.Combine(dir, name);
                if (!File.Exists(src)) continue;
                z.CreateEntryFromFile(src, "icons/" + name);
                icons[key] = new JObj { ["file"] = name, ["warning"] = PowerupIcons.Warning(key) };
                any = true;
            }
            if (any) n++;
        }
        Text(z, "icons.json", Json.Dumps(icons, 1));
        Text(z, "info.json", Json.Dumps(new JObj { ["format"] = (long)Format, ["kind"] = "powerups", ["made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["app"] = "Blur Map Builder" }, 1));
        return n;
    }

    /// a .blurpowerups read: each power-up in it gets that picture (its other picture, if any, goes); the power-ups not in
    /// the file keep theirs. Only real pictures of the known power-ups are taken. Returns the names of the power-ups set
    public static List<string> ImportIcons(string path)
    {
        var f = Open(path, IconExt);
        var icons = JsonOf(f, "icons.json", "power-up icons");
        var done = new List<string>();
        string dir = PowerupIcons.OwnFolder;
        Directory.CreateDirectory(dir);
        foreach (var (key, nice) in PowerupIcons.Kinds)
        {
            if (icons.Obj(key) is not { } it) continue;
            string? name = it.Str("file");
            if (name == null || !PowerupIcons.FileNames(key).Contains(name)) continue;
            if (!f.TryGetValue("icons/" + name, out var data)) continue;
            try { using var ms = new MemoryStream(data); using var bm = new System.Drawing.Bitmap(ms); }       // a real picture?
            catch (ArgumentException) { continue; }
            foreach (var other in PowerupIcons.FileNames(key)) { string o = Path.Combine(dir, other); if (File.Exists(o)) File.Delete(o); }
            File.WriteAllBytes(Path.Combine(dir, name), data);
            string? warn = it.Str("warning");
            PowerupIcons.SetWarning(key, warn is { Length: > 0 and < 200 } ? warn : null);
            done.Add(nice);
        }
        if (done.Count == 0) throw new ImportProblem("no power-up pictures inside");
        return done;
    }
}

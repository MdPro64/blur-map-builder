// What the last Build + install put into the game, part by part (maps, cars, skins), so a build can update only some parts:
// an unticked part is built from this copy, so the game keeps exactly what it had. The copies live in work\installed\.
namespace Bmb.Core;

public static class Installed
{
    static string Dir => Path.Combine(Paths.Work, "installed");
    static string MapsFile => Path.Combine(Dir, "maps.json");
    static string CarsFile => Path.Combine(Dir, "cars.json");
    public static string SkinDir => Path.Combine(Dir, "skins");

    public static bool HasMaps => File.Exists(MapsFile);
    public static bool HasCars => File.Exists(CarsFile);
    public static bool HasSkins => File.Exists(Path.Combine(SkinDir, "skins.json"));
    public static string TweaksDir => Path.Combine(Dir, "tweaks");
    public static bool HasTweaks => File.Exists(Path.Combine(TweaksDir, "tweaks.json"));

    /// the game tweaks (settings + the power-up pictures with their size) of a data folder, to see if anything changed
    static string TweaksState(string data)
    {
        string t = Path.Combine(data, "tweaks.json");
        var o = new List<string> { File.Exists(t) ? Json.Dumps(Json.Load(t), -1) : "{}" };
        foreach (var (key, _) in PowerupIcons.Kinds)
            foreach (var n in PowerupIcons.FileNames(key)) { var f = new FileInfo(Path.Combine(data, "powerups", n)); o.Add(f.Exists ? $"{n}:{f.Length}:{f.LastWriteTimeUtc.Ticks}" : n + ":-"); }
        return string.Join("|", o);
    }

    public static bool TweaksChanged() { try { return !HasTweaks || TweaksState(Paths.Data) != TweaksState(TweaksDir); } catch (Exception) { return true; } }

    /// the data folder the build reads the tweaks from: the current ones (null), or (not ticked) the last install's copy
    public static string? TweaksFrom(BuildOptions opt) => !opt.Tweaks && HasTweaks ? TweaksDir : null;

    /// the tweaks as they are now become the installed copy (the pictures keep their file time)
    public static void RememberTweaks()
    {
        string tmp = TweaksDir + ".new";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(Path.Combine(tmp, "powerups"));
        string t = Path.Combine(Paths.Data, "tweaks.json");
        if (File.Exists(t)) File.Copy(t, Path.Combine(tmp, "tweaks.json"), true); else File.WriteAllText(Path.Combine(tmp, "tweaks.json"), "{}");
        foreach (var (key, _) in PowerupIcons.Kinds)
            foreach (var n in PowerupIcons.FileNames(key))
            {
                string src = Path.Combine(Paths.Data, "powerups", n);
                if (File.Exists(src)) { string dst = Path.Combine(tmp, "powerups", n); File.Copy(src, dst, true); File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); }
            }
        if (Directory.Exists(TweaksDir)) Directory.Delete(TweaksDir, true);
        Directory.Move(tmp, TweaksDir);
    }

    static string Key(object? v) => Json.Dumps(v, -1);

    /// the parts that changed since the last install (all of them when there is no copy yet)
    public static (bool Maps, bool Cars, bool Skins) Changed(JObj world)
    {
        bool maps = !HasMaps || Key(Json.Load(MapsFile)) != Key(world["locations"]);
        bool cars = !HasCars || Key(Json.Load(CarsFile)) != Key(world["cars"] ?? new List<object?>());
        bool skins = !HasSkins || Key(SkinState(Skins.Folder)) != Key(SkinState(SkinDir));
        return (maps, cars, skins);
    }

    /// the skins that are on, with their pictures' size + time (to see if anything changed)
    static JObj SkinState(string folder)
    {
        var o = new JObj();
        foreach (var (car, pics) in Skins.OnNow(folder))
            o[car] = pics.Select(p => { var f = new FileInfo(Path.Combine(folder, car, p + ".png")); return (object?)$"{p}:{(f.Exists ? f.Length : 0)}"; }).ToList();
        return o;
    }

    /// the world to build: the ticked parts as they are now, the others as the last install had them. Logs what it did.
    public static JObj ForBuild(JObj world, BuildOptions opt)
    {
        var w = Share.Copy(world);
        if (!opt.Maps)
        {
            if (HasMaps) { w["locations"] = Json.Load(MapsFile); opt.Log("maps: not ticked - the maps of the last install are built again (unchanged)"); }
            else opt.Log("maps: not ticked, but there is no copy of an earlier install yet - the current maps are used");
        }
        if (!opt.Cars)
        {
            if (HasCars) { w["cars"] = Json.Load(CarsFile); opt.Log("cars: not ticked - the cars of the last install are built again (unchanged)"); }
            else opt.Log("cars: not ticked, but there is no copy of an earlier install yet - the current cars are used");
        }
        if (!opt.Skins && !HasSkins) opt.Log("skins: not ticked, but there is no copy of an earlier install yet - the current skins are used");
        return w;
    }

    /// the folder the build reads the skins from: the current ones, or (not ticked) the last install's copy
    public static string? SkinsFrom(BuildOptions opt) => !opt.Skins && HasSkins ? SkinDir : null;

    /// after a good install: the parts that went in become the new copies
    public static void Remember(JObj builtWorld, BuildOptions opt)
    {
        Directory.CreateDirectory(Dir);
        Json.Save(builtWorld["locations"], MapsFile);
        Json.Save(builtWorld["cars"] ?? new List<object?>(), CarsFile);
        if (opt.Skins || !HasSkins) RememberSkins();
        if (opt.Tweaks || !HasTweaks) RememberTweaks();
    }

    /// the skins as they are now (their list + the pictures that are on) become the installed copy
    public static void RememberSkins()
    {
        string tmp = SkinDir + ".new";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        string list = Path.Combine(Skins.Folder, "skins.json");
        if (File.Exists(list)) File.Copy(list, Path.Combine(tmp, "skins.json"), true);
        else Json.Save(new JObj(), Path.Combine(tmp, "skins.json"));
        foreach (var (car, pics) in Skins.OnNow())
            foreach (var p in pics)
            {
                Directory.CreateDirectory(Path.Combine(tmp, car));
                File.Copy(Skins.SkinFile(car, p), Path.Combine(tmp, car, p + ".png"), true);
            }
        if (Directory.Exists(SkinDir)) Directory.Delete(SkinDir, true);
        Directory.Move(tmp, SkinDir);
    }
}

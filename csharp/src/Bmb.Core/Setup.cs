// The first start: everything the map builder needs, made from the player's OWN game. Port of setup_game.py + library/make_catalog.py + level/hide_scenery.py.
// Not ported: fetch_addon (the C# version reads models itself, it needs no add-on).
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Bmb.Core;

public sealed class SetupProblem : Exception { public SetupProblem(string s) : base(s) { } }

public static class Setup
{
    static readonly (int Off, byte[] Old, byte[] New)[] ExePatches =
    {
        (0x8B667A, new byte[] { 0x75 }, new byte[] { 0xEB }),
        (0xA668F8, Z("cache/gamedata.pak"), Z("rebrn/gamedata.pak")),
        (0xA66628, Z("GAME:\\cache\\gamedata.pak"), Z("GAME:\\rebrn\\gamedata.pak")),
        (0xA66978, Z("cache/root.pak"), Z("rebrn/root.pak")),
        (0x72D54A, new byte[] { 0x74, 0x12 }, new byte[] { 0xEB, 0x12 }),
    };
    static byte[] Z(string s) => Encoding.Latin1.GetBytes(s + "\0");
    const string KnownSha = "dba0264b7808a5e38d99c8c162ecd71ab06a48fbc424feb1b9a3bc1bff2fbf3f";
    static readonly string[] Unpack2 = { "gamedata/levels/jpn_mharuna/actorpack.xt", "gamedata/levels/jpn_mharuna/jpn_mharuna_1.aid", "gamedata/levels/usa_la_river/actorpack.xt", "gamedata/levels/usa_la_river/usa_la_river_6.aid" };
    const string UnpackPrefix = "gamedata/xt/", OurMark = "gamedata/levels/flat_earth/actorpack.xt";
    const string PluginLine = "require(\"quickstart.quickstart\"), -- Blur Map Builder: custom LAN race on the map being tested";

    static string BlurExe => Path.Combine(Paths.Game, "Blur.exe");
    static string Reborn => Path.Combine(Paths.Game, "Blur REBORN.exe");
    static string Amax => Path.Combine(Paths.Game, "amax");
    static string Plugins => Path.Combine(Amax, "plugins");
    static string QuickTarget => Path.Combine(Plugins, "quickstart", "target.lua");

    static string Sha(byte[] d) => Convert.ToHexString(SHA256.HashData(d)).ToLowerInvariant();

    static void CheckGame(Action<string> log)
    {
        if (!File.Exists(BlurExe)) throw new SetupProblem($"Blur.exe was not found in {Paths.Game}.\nPut this program into your Blur folder (next to Blur.exe) and start it from there.");
        foreach (var need in new[] { Path.Combine(Paths.Cache, "gamedata.pak"), Path.Combine(Paths.Cache, "root.pak"), Path.Combine(Paths.Levels, "showroom", "level.levelrender") })
            if (!File.Exists(need)) throw new SetupProblem("A game file is missing: " + need);
        var d = File.ReadAllBytes(BlurExe);
        foreach (var (off, old, _) in ExePatches)
            if (off + old.Length > d.Length || !d.AsSpan(off, old.Length).SequenceEqual(old))
                throw new SetupProblem($"This Blur.exe is not a version the map builder knows. The map builder needs Blur version 1.2 (Blur 1.0 does not work - update the game to 1.2). Nothing was changed.\nAlready on 1.2? Send {UiText.Discord} on Discord the file {Path.Combine(Paths.Logs, "setup.log")}");
        if (!Directory.Exists(Amax)) log("NOTE: Amax was not found (amax\\). The map tester's quick race needs Amax; everything else works.");
        log($"Blur.exe OK ({(Sha(d) == KnownSha ? "known version" : "untested version, same bytes")})");
    }

    static bool IsOriginal(string pak) { try { using var p = new Pak(pak); return p.Find(OurMark) == null; } catch (Exception) { return false; } }

    static void BackupPacks(Action<string> log)
    {
        Directory.CreateDirectory(Paths.Base);
        foreach (var (name, dst) in new[] { ("gamedata.pak", Paths.GamedataOriginal), ("root.pak", Paths.RootOriginal) })
        {
            if (File.Exists(dst)) continue;
            string src = Path.Combine(Paths.Cache, name);
            if (!IsOriginal(src)) throw new SetupProblem($"cache\\{name} is not the game's original (an older map build is installed in it).\nPut the original back (verify the game files in Steam / reinstall), then start again.");
            log($"copying the original cache\\{name} ({new FileInfo(src).Length >> 20} MB) ...");
            File.Copy(src, dst + ".part", true);
            File.Move(dst + ".part", dst, true);
        }
    }

    static void UnpackGame(Action<string> log)
    {
        using var p = new Pak(Paths.GamedataOriginal);
        var names = new Dictionary<string, PakEntry>();
        foreach (var e in p.Files)
        {
            int end = Array.IndexOf(p.Names, (byte)0, (int)e.NameOff);
            names[Encoding.Latin1.GetString(p.Names, (int)e.NameOff, end - (int)e.NameOff).Replace('\\', '/').TrimStart('/').ToLowerInvariant()] = e;
        }
        var want = names.Keys.Where(n => n.StartsWith(UnpackPrefix)).Concat(Unpack2.Select(n => n.ToLowerInvariant())).ToList();
        int done = 0;
        foreach (var n in want)
        {
            if (!names.TryGetValue(n, out var e)) throw new SetupProblem("gamedata.pak has no " + n);
            string o = Path.Combine(new[] { Paths.Unpacked }.Concat(n.Split('/').Skip(1)).ToArray());
            if (File.Exists(o) && new FileInfo(o).Length == e.Size) continue;
            var data = p.Read(e);
            if (data == null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(o)!);
            File.WriteAllBytes(o, data); done++;
        }
        log($"game data: {done} file(s) unpacked");
    }

    static bool IsRecord(ReadOnlySpan<float> v)
    {
        float x0 = v[0], y0 = v[1], z0 = v[2], x1 = v[3], y1 = v[4], z1 = v[5], a = v[6], b = v[7];
        if (!(-1e5f < x0 && x0 <= x1 && x1 < 1e5f && -1e5f < y0 && y0 <= y1 && y1 < 1e5f && -1e5f < z0 && z0 <= z1 && z1 < 1e5f && x1 - x0 > 0.01f)) return false;
        return a < b && (Math.Abs(a) > 1e30f || (0 <= a && a < 1e6f)) && (Math.Abs(b) > 1e30f || (0 < b && b < 1e6f));
    }

    /// hide_scenery.hide(data, loose=True)
    public static (byte[] Data, int Hits) HideScenery(byte[] data)
    {
        var d = (byte[])data.Clone();
        int n = data.Length - 32, hits = 0, last = -32;
        Span<float> v = stackalloc float[8];
        for (int i = 0; i < n; i++)
        {
            byte b27 = data[i + 27], b31 = data[i + 31];
            if (!(b27 is 0x7E or 0xFE || b31 is 0x7E or 0x42 or 0x43 or 0x44 or 0x45 or 0x46)) continue;
            if (i < last + 32) continue;
            for (int k = 0; k < 8; k++) v[k] = B.F32(d, i + 4 * k);
            if (IsRecord(v)) { B.Put(d, i + 24, 0f); B.Put(d, i + 28, 0f); hits++; last = i; }
        }
        return (d, hits);
    }

    static void LevelTemplate(Action<string> log)
    {
        string o = Paths.LevelTemplate;
        if (new[] { "level.level", "level.levelrender", "level.levelstream" }.All(f => File.Exists(Path.Combine(o, f)))) return;
        Directory.CreateDirectory(o);
        string src = Path.Combine(Paths.Levels, "showroom");
        File.Copy(Path.Combine(src, "level.level"), Path.Combine(o, "level.level"), true);
        foreach (var name in new[] { "level.levelrender", "level.levelstream" })
        {
            var (data, hits) = HideScenery(File.ReadAllBytes(Path.Combine(src, name)));
            File.WriteAllBytes(Path.Combine(o, name), data);
            log($"level template {name}: {hits} scenery nodes hidden");
        }
    }

    static void MakeCatalogStep(Action<string> log)
    {
        if (File.Exists(Paths.Catalog)) return;
        log("making the object library (every placeable object of the game; once) ...");
        File.WriteAllText(Paths.Catalog, Json.Dumps(MakeCatalog(log), 0));
    }

    static readonly Dictionary<string, string> Locations = new()
    {
        ["shared"] = "Shared (all cities)", ["usa_la_river"] = "LA River", ["usa_la_downtown"] = "LA Downtown", ["usa_la_longbeachdocks"] = "LA Long Beach docks", ["usa_la_observatory"] = "LA Observatory", ["usa_amboy"] = "Amboy",
        ["usa_ny_dumbo"] = "New York Dumbo", ["usa_sanfran_russianhill"] = "San Francisco Russian Hill", ["usa_sanfran_sausalito"] = "San Francisco Sausalito", ["uk_brighton"] = "Brighton", ["uk_london_hackney"] = "London Hackney",
        ["spa_barcelona_gracia"] = "Barcelona Gracia", ["spa_barcelona_oval"] = "Barcelona Oval", ["jpn_shutoko"] = "Tokyo Shutoko", ["jpn_mountaintrack"] = "Mount Haruna",
    };
    static readonly string[] Kinds = { "breakable", "movable", "animated", "scenery" };

    static string Pretty(string s0)
    {
        string s = s0.Replace('_', ' ').Trim();
        var o = new StringBuilder();
        for (int i = 0; i < s.Length; i++) { if (i > 0 && char.IsUpper(s[i]) && char.IsLower(s[i - 1])) o.Append(' '); o.Append(s[i]); }
        string t = o.ToString();
        return t.Length == 0 ? t : t[..1].ToUpperInvariant() + t[1..];
    }

    static (string Loc, string Cat)? PlaceOf(string model)
    {
        var p = model.Replace('/', '\\').Split('\\');
        var low = p.Select(x => x.ToLowerInvariant()).ToArray();
        if (p.Length < 4 || low[0] != "models" || low[1] != "objects") return null;
        string loc = Locations.GetValueOrDefault(low[2], Pretty(p[2])), cat = p.Length > 4 ? Pretty(p[3]) : "Misc";
        if (low[2] == "shared" && p.Length > 4) cat = low[3] switch { "us" => "USA", "uk" => "UK", "jpn" => "Japan", "spa" => "Spain", _ => Pretty(p[3]) };
        return (loc, cat);
    }

    static string? Uid(object? o) => (o as Dictionary<string, object?>)?.GetValueOrDefault("uid") as string;

    /// every object of the game that can be placed; the model sizes come from our own model reader
    public static JObj MakeCatalog(Action<string> log)
    {
        var A = new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")));
        var uidOf = new Dictionary<int, string>();
        foreach (var (u, _, oi) in A.Refs) if (oi != 0xFFFF) uidOf[oi] = u.ToString("X8");
        var models = new Dictionary<string, string>(); var defs = new List<(string, Dictionary<string, object?>)>(); var recs = new List<(string, Dictionary<string, object?>)>();
        for (int i = 0; i < A.Objects.Count; i++)
        {
            var o = A.Objects[i]; string tn = A.Types[o.Blocks[0].Type].Name;
            if (tn == "GameModel") models[uidOf[i]] = (string)A.Top(o)["Filename"]!;
            else if (tn == "DynamicObjDef") defs.Add((uidOf[i], A.Top(o)));
            else if (tn == "ElementObjectRecord") recs.Add((uidOf[i], A.Top(o)));
        }
        var files = models.Values.Where(f => PlaceOf(f) != null).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
        log($"{files.Count} models, {defs.Count} object defs, {recs.Count} breakable records; reading model sizes...");
        var sizes = new System.Collections.Concurrent.ConcurrentDictionary<string, (double[] Lo, double[] Hi)>();
        int done = 0;
        using (var R = new Pak(Paths.RootOriginal))
        {
            var bytes = files.Select(f => (f, R.Read("\\" + f.ToLowerInvariant()))).Where(q => q.Item2 != null).ToList();
            Parallel.ForEach(bytes, q =>
            {
                try
                {
                    var M = CpModel.Read(q.Item2!);
                    double[] lo = new double[3], hi = new double[3];
                    for (int k = 0; k < 3; k++) { lo[k] = M.AllElements.Min(e => e.Box[k]); hi[k] = M.AllElements.Max(e => e.Box[k + 3]); }
                    if (lo.Concat(hi).All(v => Math.Abs(v) < 1e5)) sizes[q.f.ToLowerInvariant()] = (lo, hi);
                }
                catch (Exception) { }
                int n = Interlocked.Increment(ref done);
                if (n % 200 == 0) log($"  models read: {n} / {bytes.Count}");
            });
        }
        var outp = new List<JObj>();
        static List<object?> Rnd(double[] v) => v.Select(x => (object?)Py.RoundN(x, 3)).ToList();
        void Add(string kind, string uid, string? model, string name, object? weight = null, bool hasWeight = false)
        {
            var w = model == null ? null : PlaceOf(model);
            if (w == null) return;
            bool sized = sizes.TryGetValue(model!.ToLowerInvariant(), out var box);
            var (lo, hi) = sized ? box : (new[] { -1.0, 0.0, -1.0 }, new[] { 1.0, 2.0, 1.0 });
            var e = new JObj { ["id"] = $"{kind}:{uid}", ["kind"] = kind, ["name"] = name, ["loc"] = w.Value.Loc, ["cat"] = w.Value.Cat, ["model"] = model, ["uid"] = uid, ["lo"] = Rnd(lo), ["hi"] = Rnd(hi), ["sized"] = sized };
            if (hasWeight) e["weight"] = weight;
            outp.Add(e);
        }
        static string Stem(string? p) => Path.GetFileNameWithoutExtension((p ?? "?").Replace('\\', '/').Split('/')[^1]);
        foreach (var (uid, top) in recs)
        {
            var model = Uid(top.GetValueOrDefault("Model")) is { } u ? models.GetValueOrDefault(u) : null;
            var objs = top.GetValueOrDefault("objects") as System.Collections.IList;
            string? nm = objs is { Count: > 0 } ? (objs[0] as Dictionary<string, object?>)?.GetValueOrDefault("Name") as string : null;
            string name = !string.IsNullOrEmpty(nm) ? nm : Stem(model);
            Add("breakable", uid, model, Pretty(name.Length >= 2 && name[..2] is "O_" or "M_" or "S_" ? name[2..] : name));
        }
        foreach (var (uid, top) in defs)
        {
            var model = Uid(top.GetValueOrDefault("Model")) is { } u ? models.GetValueOrDefault(u) : null;
            if (model == null) continue;
            bool anim = top.GetValueOrDefault("AnimationEvents") is System.Collections.ICollection { Count: > 0 };
            bool phys = top.GetValueOrDefault("PhysType") is { } pt && pt is not string && Convert.ToInt64(pt) == 2;
            if (!anim && !phys) continue;
            Add(anim ? "animated" : "movable", uid, model, Pretty(Stem(model)), top.GetValueOrDefault("Weight"), true);
        }
        foreach (var (uid, model) in models) Add("scenery", uid, model, Pretty(Stem(model)));
        var seen = new HashSet<(string, string)>(); var uniq = new List<object?>();
        foreach (var e in outp.OrderBy(e => Array.IndexOf(Kinds, e.Str("kind"))).ThenBy(e => e.Str("loc"), StringComparer.Ordinal).ThenBy(e => e.Str("cat"), StringComparer.Ordinal).ThenBy(e => e.Str("name")!.ToLowerInvariant(), StringComparer.Ordinal).ThenBy(e => e.Str("uid"), StringComparer.Ordinal))
            if (seen.Add((e.Str("kind")!, e.Str("model")!.ToLowerInvariant()))) uniq.Add(e);
        log($"object library: {uniq.Count} objects");
        return new JObj { ["made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["objects"] = uniq };
    }

    static void MakeReborn(Action<string> log)
    {
        var d = File.ReadAllBytes(BlurExe);
        foreach (var (off, _, nw) in ExePatches) nw.CopyTo(d, off);
        if (!(File.Exists(Reborn) && File.ReadAllBytes(Reborn).AsSpan().SequenceEqual(d))) { File.WriteAllBytes(Reborn, d); log("made Blur REBORN.exe (Blur.exe itself is not changed)"); }
        Directory.CreateDirectory(Paths.PackDir);
        foreach (var (name, src) in new[] { ("gamedata.pak", Paths.GamedataOriginal), ("root.pak", Paths.RootOriginal) })
        {
            string dst = Path.Combine(Paths.PackDir, name);
            if (File.Exists(dst)) continue;
            if (!CreateHardLink(dst, src, IntPtr.Zero)) { File.Copy(src, dst + ".part", true); File.Move(dst + ".part", dst, true); }
            log($"rebrn\\{name} = the game's original (until your first build)");
        }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string link, string target, IntPtr sec);

    static void InstallPlugin(Action<string> log)
    {
        if (!Directory.Exists(Plugins)) return;
        string dst = Path.Combine(Plugins, "quickstart");
        Directory.CreateDirectory(dst);
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("quickstart.lua")!)
        using (var f = File.Create(Path.Combine(dst, "quickstart.lua"))) s.CopyTo(f);
        if (!File.Exists(QuickTarget)) File.WriteAllText(QuickTarget, "return { enabled = false }\n", new UTF8Encoding(false));
        string lst = Path.Combine(Plugins, "plugins.lua");
        if (File.Exists(lst))
        {
            string s = new UTF8Encoding(false).GetString(File.ReadAllBytes(lst)).TrimStart('﻿');
            if (!s.Contains("quickstart.quickstart"))
            {
                int i = s.LastIndexOf('}');
                string head = s[..i].TrimEnd();
                File.WriteAllBytes(lst, new UTF8Encoding(false).GetBytes(head + (head.EndsWith(",") ? "\n" : ",\n") + "\t" + PluginLine + "\n}\n"));
                log("Amax: QuickStart plugin added to plugins.lua");
            }
        }
    }

    static bool RebornCurrent()
    {
        try { var d = File.ReadAllBytes(Reborn); return ExePatches.All(p => d.AsSpan(p.Off, p.New.Length).SequenceEqual(p.New)); } catch (Exception) { return false; }
    }

    public static bool Needed() =>
        !new[] { Paths.GamedataOriginal, Paths.RootOriginal, Path.Combine(Paths.Unpacked, "xt", "amax.bin"), Path.Combine(Paths.LevelTemplate, "level.levelstream"), Paths.Catalog, Reborn }.All(File.Exists) || !RebornCurrent();

    public static void RunAll(Action<string> log, Action<int, int, string>? progress = null)
    {
        foreach (var d in new[] { Paths.Work, Paths.FeWork, Paths.Tracks, Paths.Logs, Paths.PackDir }) Directory.CreateDirectory(d);
        using var f = new StreamWriter(Path.Combine(Paths.Logs, "setup.log"), true, new UTF8Encoding(false)) { AutoFlush = true };
        void Both(string m) { lock (f) f.WriteLine(m); log(m); }
        f.WriteLine($"\n######## {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Blur Map Builder (C#) {Paths.Version}  in {Paths.Game}");
        var steps = new (string, Action<Action<string>>)[] { ("Checking the game", CheckGame), ("Backing up the original packs", BackupPacks), ("Unpacking the game data", UnpackGame), ("Level template", LevelTemplate), ("Object library", MakeCatalogStep), ("Blur REBORN.exe", MakeReborn), ("Amax plugin", InstallPlugin) };
        for (int k = 0; k < steps.Length; k++)
        {
            progress?.Invoke(k, steps.Length, steps[k].Item1);
            Both("== " + steps[k].Item1);
            try { steps[k].Item2(Both); }
            catch (Exception e) { f.WriteLine("STOPPED: " + e); throw; }
        }
        progress?.Invoke(steps.Length, steps.Length, "Ready");
    }
}

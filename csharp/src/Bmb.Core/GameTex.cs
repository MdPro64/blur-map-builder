// Pictures taken from the game's own model files (root.pak), so the map builder does not have to ship them.
// The level ground textures are streamed and cannot be read (see _mod_project/LOG.md "GAME TEXTURES research"), but some
// object models carry whole ground-like tiles: asphalt, rock, concrete, hay, a worn cracked surface, a cloudy mask.
// Where each one lives is found once (a scan of the pack, a few seconds) and kept in Paths.Data/gametex.json.
using System.Collections.Concurrent;

namespace Bmb.Core;

public static class GameTex
{
    /// the pictures this program uses (game name)
    public static readonly string[] Wanted =
    {
        "tarmacplain02_diff", "ambient_rock02_diff", "concreteblock1_diff", "hay01_diff", "drilltowertexture01_diff", "uv_distort_04",
    };
    static readonly ConcurrentDictionary<(string, int), Pic?> _pics = new();
    static Dictionary<string, string>? _where;
    static readonly object _lock = new();

    static string IndexFile => Path.Combine(Paths.Data, "gametex.json");

    /// can the game's pictures be read here? (false in tests without the game's pack)
    public static bool Available => File.Exists(Paths.RootOriginal) && Environment.GetEnvironmentVariable("BMB_NO_GAMETEX") == null;

    static Dictionary<string, string> Where()
    {
        lock (_lock)
        {
            if (_where != null) return _where;
            var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Json.Load(IndexFile) is JObj j && Wanted.All(j.ContainsKey))
                    foreach (var (k, v) in j) if (v is string s) o[k] = s;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or FormatException) { }
            if (o.Count == 0 && Available)
            {
                using var p = new Pak(Paths.RootOriginal);
                var want = new HashSet<string>(Wanted, StringComparer.OrdinalIgnoreCase);
                foreach (var e in p.Files.Where(f => f.Offset != Pak.NoData && f.Name.EndsWith(".model", StringComparison.OrdinalIgnoreCase)))
                {
                    if (want.Count == 0) break;
                    try
                    {
                        var d = p.Read(e); if (d == null) continue;
                        foreach (var t in CpModel.Read(d).TexList)
                            if (want.Remove(t.Name)) o[t.Name] = e.Name;
                    }
                    catch (Exception x) when (x is InvalidDataException or IndexOutOfRangeException or ArgumentException) { }
                }
                try { Directory.CreateDirectory(Paths.Data); var j = new JObj(); foreach (var (k, v) in o) j[k] = v; File.WriteAllText(IndexFile, Json.Dumps(j, 1)); }
                catch (IOException) { }
            }
            return _where = o;
        }
    }

    /// the game's picture by name, at most max pixels wide (its largest stored size that fits), or null
    public static Pic? Get(string name, int max = 512)
    {
        if (!Available) return null;
        return _pics.GetOrAdd((name.ToLowerInvariant(), max), _ =>
        {
            try
            {
                if (!Where().TryGetValue(name, out var model)) return null;
                byte[]? d;
                lock (_lock) { using var p = new Pak(Paths.RootOriginal); d = p.Read(model); }
                if (d == null) return null;
                var t = CpModel.Read(d).TexList.FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                return t == null ? null : Previews.ReadTexture(d, t, max);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or IndexOutOfRangeException or ArgumentException) { return null; }
        });
    }

    /// the picture made w x h (a square tile stretched to the texture slot: the UVs make it square again in the world)
    public static byte[]? Rgb(string name, int w, int h)
    {
        var p = Get(name, Math.Max(w, h) >= 512 ? 1024 : 512);
        if (p == null) return null;
        return (p.W == w && p.H == h ? p : p.ResizeLanczos(w, h)).Px;
    }
}

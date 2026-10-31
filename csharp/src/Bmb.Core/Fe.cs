// Shared constants of the build + folders. Port of the top of flat_earth/build.py and of paths.py / world.py.
namespace Bmb.Core;

public static class Paths
{
    public const string Version = "2.0.0";
    /// game = the player's Blur folder (read only here); data = its "Blur Map Builder" folder; maps / outDir = where maps are read and packs go
    public static string Game = "", Data = "", Base = "", Unpacked = "", GamedataOriginal = "", RootOriginal = "", LevelTemplate = "", Addon = "",
        Catalog = "", ModelCache = "", Work = "", Tracks = "", Logs = "", Levels = "", PackDir = "", FeWork = "", GamedataOut = "", RootOut = "", WorldFile = "", Cache = "";

    public static void Init(string game, string? maps = null, string? work = null, string? packDir = null, string? levels = null)
    {
        Game = game; Data = Path.Combine(game, "Blur Map Builder"); Base = Path.Combine(Data, "base");
        Unpacked = Path.Combine(Base, "unpacked", "gamedata");
        GamedataOriginal = Path.Combine(Base, "gamedata.pak.original"); RootOriginal = Path.Combine(Base, "root.pak.original");
        LevelTemplate = Path.Combine(Base, "level_template"); Addon = Path.Combine(Base, "addon"); Catalog = Path.Combine(Base, "catalog.json");
        ModelCache = Path.Combine(Base, "model_cache"); Cache = Path.Combine(game, "cache");
        Work = work ?? Path.Combine(Data, "work"); Tracks = maps ?? Path.Combine(Data, "maps"); Logs = Path.Combine(Data, "logs");
        Levels = levels ?? Path.Combine(game, "levels"); PackDir = packDir ?? Path.Combine(game, "rebrn");
        FeWork = Path.Combine(Work, "flat_earth"); GamedataOut = Path.Combine(Work, "gamedata_track.pak"); RootOut = Path.Combine(Work, "root_track.pak");
        WorldFile = Path.Combine(Tracks, "world.json");
    }
}

public sealed class BigDef { public string Name = ""; public string? File, GmUid; public uint OurGm, DefUid; public double Lift; }

public static class Fe
{
    public const double GroundY = 1.0, PieceStep = 48.0, PieceLen = 56.0, RoadWidth = 60.0, RoadLen = 3000.0;
    public const uint TileDefUid = 0xFFFFFF31, ModelUid = 0xFFFFFF30, LineModelUid = 0xFFFFFF33, LineDefUid = 0xFFFFFF34, TestDefUid = 0xFFFFFF32,
        BlockDefUid = 0xFFFFFF56, WallModelUid = 0xFFFFFF57, WallDefUid = 0xFFFFFF58, ChevronModelUid = 0xFFFFFF59, ChevronDefUid = 0xFFFFFF5A,
        TowerUid = 0xFFFFFF76, TowerUid2 = 0xF1E10000, ArchUid = 0xF1E20000, TileVariantBase = 0xFFFFFF66, IntroCamUid = 0xF1D60000, IntroCycleUid = 0xF1D70000;
    public const double TileTopBelow = 0.02, LineTopBelow = -0.03, LineW = 3.0, LineLen = RoadWidth + 8.0, TileSize = 128.0, TileDrawDistance = 5000.0,
        PickupStep = 150.0, LampStep = 50.0, BarrierStep = 45.0, BillboardStep = 300.0, TowerZ = 300.0, TowerStep = 240.0, ShipZ = 110.0, CraneZ = 175.0,
        ArchW = 40.0, ArchPillar = 2.0, ArchY0 = 9.0, ArchY1 = 12.5, ArchDepth = 2.0, RoadsideDrawDistance = 1500.0, WallH = 1.2,
        ChevronLen = 16.0, ChevronH = 2.5, ChevronLift = 1.8, ChevronStroke = 0.9;
    public const int ObstacleGroups = 12, CranesPerSide = 8, ChevronCount = 4, RoadGrey = 105, RoadGlow = 95, LineGlow = 130, RoadShine = 30, WallGlow = 255, BankGlow = 35;
    public const bool EdgeBarriers = false;
    public static readonly (double L, double H, double W) Container = (13.7, 2.9, 2.4);
    public static readonly int[] TileWidths = { 30, 40, 50, 60, 70, 80, 90, 100 };
    public static uint TileDef(int b) => TileVariantBase + 2 * (uint)Array.IndexOf(TileWidths, b) + 1;
    public static string TileVariantFile(int b) => $"models\\objects\\flat_earth\\roadtile_w{b}.model";
    public static readonly (double O, double H)[] Bank = { (0.0, 0.0), (0.9, 0.45), (1.6, 1.2), (2.2, 2.0) };
    public static readonly byte[] WallColour = { 0, 220, 255 }, BankColour = { 34, 38, 48 };

    public static readonly (string Name, string Src, uint Ours)[] Roadside =
    {
        ("barrier", "52428956", 0xFFFFFF40), ("billboard", "EF4052EE", 0xFFFFFF41), ("flashing", "5C3279E4", 0xFFFFFF42), ("light", "D40A6188", 0xFFFFFF43),
        ("barrel", "EBA0FFD7", 0xFFFFFF44), ("rwbarrier", "E7A4B0AD", 0xFFFFFF45), ("cone", "A099EA98", 0xFFFFFF46), ("lamp", "9D39678A", 0xFFFFFF47),
    };
    public static uint Rs(string name) => Roadside.First(r => r.Name == name).Ours;

    public static uint AudioUid(string name) => name switch
    {
        "crowd_s" => 0x5526BBF3, "crowd_m" => 0x2B6AB5B6, "crowd_l" => 0x582CC295, "haruna" => 0x3D1D4428, _ => throw new KeyNotFoundException(name),
    };

    public static readonly (string Name, double Wx, double Dz, double H, int Seed)[] Towers =
    {
        ("tower_a", 40, 40, 140, 1), ("tower_b", 55, 35, 95, 2), ("tower_c", 30, 30, 200, 3), ("tower_d", 70, 45, 60, 4),
        ("tower_e", 50, 50, 180, 5), ("tower_f", 60, 30, 150, 6), ("tower_g", 36, 36, 230, 7), ("tower_h", 90, 24, 75, 8),
        ("tower_i", 70, 70, 110, 9), ("tower_j", 64, 64, 90, 10), ("tower_k", 22, 22, 170, 11), ("tower_l", 110, 60, 40, 12),
    };
    public static readonly (string Name, string Text, int Seed)[] Arches = { ("arch_start", "START", 21), ("arch_finish", "FINISH", 22), ("arch_lap", "START  FINISH", 23) };

    public static (uint, uint) TowerUids(int k) => k < 4 ? (TowerUid + 2 * (uint)k, TowerUid + 2 * (uint)k + 1) : (TowerUid2 + 2 * (uint)(k - 4), TowerUid2 + 2 * (uint)(k - 4) + 1);

    public static readonly List<BigDef> Big = MakeBig();
    static List<BigDef> MakeBig()
    {
        var b = new List<BigDef>
        {
            new() { Name = "ferris", File = "models\\specialfx\\ferriswheel.model", OurGm = 0xFFFFFF50, DefUid = 0xFFFFFF51 },
            new() { Name = "tanker", GmUid = "411EF6CC", DefUid = 0xFFFFFF52 },
            new() { Name = "cruise", GmUid = "2A9D443A", DefUid = 0xFFFFFF53, Lift = 7.8 },
            new() { Name = "crane", GmUid = "FD1DE76F", DefUid = 0xFFFFFF54 },
            new() { Name = "radio", GmUid = "FF1D13E4", DefUid = 0xFFFFFF55 },
        };
        for (int k = 0; k < Towers.Length; k++)
        {
            var (g, d) = TowerUids(k);
            b.Add(new() { Name = Towers[k].Name, File = $"models\\objects\\flat_earth\\{Towers[k].Name}.model", OurGm = g, DefUid = d });
        }
        for (int k = 0; k < Arches.Length; k++)
            b.Add(new() { Name = Arches[k].Name, File = $"models\\objects\\flat_earth\\{Arches[k].Name}.model", OurGm = ArchUid + 2 * (uint)k, DefUid = ArchUid + 2 * (uint)k + 1 });
        return b;
    }

    public static List<(double Z, double Y, double V)> RoadProfile(double width)
    {
        double e = width / 2;
        var pts = new List<(double Z, double Y)>();
        for (int i = Bank.Length - 1; i >= 0; i--) pts.Add((-(e + Bank[i].O), Bank[i].H));
        foreach (var (o, h) in Bank) pts.Add((e + o, h));
        var arc = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) arc[i] = arc[i - 1] + Py.Hypot(pts[i].Z - pts[i - 1].Z, pts[i].Y - pts[i - 1].Y);
        var o2 = new List<(double, double, double)>();
        for (int i = 0; i < pts.Count; i++) o2.Add((pts[i].Z, pts[i].Y, arc[i] / arc[^1]));
        return o2;
    }
}

/// world.json: locations holding maps. Port of flat_earth/world.py
public static class World
{
    public const uint ExtraBase = 0xFFFFFF80, ListBase = 0xFFFFFFA0, LocBase = 0xF00D0100;
    public const int ListsPerMap = 7;
    public static readonly Dictionary<string, string> PickupTypes = new()
    {
        ["Shunt"] = "2BEC03D3", ["Barge"] = "60A35DF0", ["Shock"] = "3A03CB15", ["Mine"] = "2BB324A4", ["Bolt"] = "4CF8489B", ["Nitro"] = "8DAF6717",
        ["Shield"] = "6F453F79", ["Repair"] = "09B1575A",
    };

    public static JObj Load(string? path = null)
    {
        string f = path ?? Paths.WorldFile;
        return File.Exists(f) ? (JObj)Json.Load(f)! : DefaultWorld();
    }

    /// a new install: the old single-road file if there is one, else the straight Home Run road
    static JObj DefaultWorld()
    {
        string old = Path.Combine(Paths.Tracks, "flat_earth_road.json");
        var r = File.Exists(old) ? (JObj)Json.Load(old)! : new JObj { ["points"] = new List<object?> { new List<object?> { -710.0, 2160.0 }, new List<object?> { 2290.0, 2160.0 } }, ["width"] = 60.0 };
        var map = new JObj { ["id"] = "flat_earth_1", ["name"] = "Home Run v1", ["width"] = r.Num("width", 60.0), ["points"] = r["points"], ["auto_bridges"] = true, ["obstacles"] = false };
        return new JObj { ["locations"] = new List<object?> { new JObj { ["id"] = "flat_earth", ["name"] = "Flat Earth", ["maps"] = new List<object?> { map } } } };
    }
    public static void Save(JObj w, string? path = null) => Json.Save(w, path ?? Paths.WorldFile);

    public static string Slug(string name)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return s.Length > 0 ? s : "track";
    }

    public static JObj AssignIds(JObj world)
    {
        uint nxt = ExtraBase, lst = ListBase, locid = LocBase;
        int li = 0;
        foreach (var loc in world.Objs("locations"))
        {
            string folder = li == 0 ? "flat_earth" : Slug(loc.Str("id")!);
            loc["folder"] = folder;
            string city = li == 0 ? "Flat_Earth" : string.Join("_", folder.Split('_').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
            loc["city_name"] = city;
            if (li == 0) { loc["city_uid"] = 0xFFFFFF20L; loc["loc_id"] = 0xF00D0010L; }
            else { loc["city_uid"] = (long)nxt; loc["loc_id"] = (long)locid; nxt++; locid++; }
            int mi = 0;
            foreach (var m in loc.Objs("maps"))
            {
                m["route_name"] = $"{city}_{mi + 1}";
                if (li == 0 && mi == 0) { m["route_uid"] = 0xFFFFFF21L; m["loc_id"] = 0xF00D0011L; }
                else { m["route_uid"] = (long)nxt; m["loc_id"] = (long)locid; nxt++; locid++; }
                if (mi > 0)
                {
                    var l = new List<object?>();
                    for (int k = 0; k < ListsPerMap; k++) l.Add((long)(lst + k));
                    m["list_uids"] = l;
                    lst += ListsPerMap;
                }
                else m["list_uids"] = null;
                mi++;
            }
            li++;
        }
        if (nxt > ListBase) throw new InvalidOperationException("too many locations / maps");
        return world;
    }
}

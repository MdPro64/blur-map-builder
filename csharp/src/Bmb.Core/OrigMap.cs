// The game's own maps, read only: which cities and routes exist (amax.bin), every placed object of a city
// (gamedata\levels\<city>\actorpack.xt) and each route's race line (gamedata\levels\<city>\<route>.aid).
// Nothing here writes; the first step of "edit original maps" (docs\original_maps.md).
namespace Bmb.Core;

public sealed class OrigRoute
{
    public string Name = ""; public int Number; public uint Uid;
    public List<uint> Lists = new(), MpLists = new();
}

public sealed class OrigCity
{
    public string Name = "", Folder = ""; public uint Uid; public bool DevOnly;
    public List<uint> Lists = new(), MpLists = new();
    public List<OrigRoute> Routes = new();
}

/// one placed object of a city. Matrix = 12 floats (x axis, y axis, z axis, position) in game space;
/// MatrixAt = where those 48 bytes are in the actorpack file (so a move is a same-size write)
public sealed class OrigActor
{
    public string Type = "", Name = ""; public uint ListUid; public int Obj, MatrixAt;
    public float[] Matrix = new float[12];
    public uint? DefUid; public string? DefType, Model, Pickup;
    public double X => Matrix[9]; public double Y => Matrix[10]; public double Z => Matrix[11];
}

public sealed class OrigList { public uint Uid; public int Obj; public string? Name; public List<OrigActor> Actors = new(); }

public sealed class OrigLine { public string Name = ""; public uint Flags; public List<float[]> Points = new(); }

public static class OrigMap
{
    static uint U(object? o) => (o as Dictionary<string, object?>)?.GetValueOrDefault("uid") is string s ? Convert.ToUInt32(s, 16) : 0;
    static List<uint> Us(object? o) => (o as System.Collections.IEnumerable)?.Cast<object?>().Select(U).Where(u => u != 0).ToList() ?? new();

    /// every city with its routes, from the game's own amax.bin
    public static List<OrigCity> Cities(byte[] amax)
    {
        var A = new Kslf(amax);
        var uidOf = new Dictionary<int, uint>();
        foreach (var (u, _, oi) in A.Refs) if (oi != 0xFFFF) uidOf[oi] = u;
        var cities = new Dictionary<uint, OrigCity>();
        var routes = new List<(uint City, OrigRoute R)>();
        for (int i = 0; i < A.Objects.Count; i++)
        {
            var o = A.Objects[i]; string tn = A.Types[o.Blocks[0].Type].Name;
            if (tn != "City" && tn != "Route") continue;
            var t = A.Top(o);
            if (tn == "City")
                cities[uidOf.GetValueOrDefault(i)] = new OrigCity
                {
                    Uid = uidOf.GetValueOrDefault(i), Name = t["Name"] as string ?? "", Folder = (t.GetValueOrDefault("ActorListFolder") as string ?? "").ToLowerInvariant(),
                    DevOnly = t.GetValueOrDefault("DevOnly") is long d && d != 0, Lists = Us(t["ActorLists"]), MpLists = Us(t["MultiplayerActorLists"]),
                };
            else
                routes.Add((U(t["City"]), new OrigRoute
                {
                    Uid = uidOf.GetValueOrDefault(i), Name = t["Name"] as string ?? "", Number = (int)(long)(t.GetValueOrDefault("RouteNumber") ?? 0L),
                    Lists = Us(t["ActorLists"]), MpLists = Us(t["MultiplayerActorLists"]),
                }));
        }
        foreach (var (c, r) in routes) if (cities.TryGetValue(c, out var city)) city.Routes.Add(r);
        foreach (var c in cities.Values) c.Routes.Sort((a, b) => a.Number.CompareTo(b.Number));
        return cities.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool Derives(List<TypeDef> types, int t, string name)
    {
        if (types[t].Name == name) return true;
        foreach (var (b, _) in types[t].Bases) if (Derives(types, b, name)) return true;
        return false;
    }

    /// model file of each placeable definition (DynamicObjDef / ElementObjectRecord uid -> GameModel file)
    public static Dictionary<uint, (string Type, string? Model)> Defs(byte[] amax)
    {
        var A = new Kslf(amax);
        var uidOf = new Dictionary<int, uint>();
        foreach (var (u, _, oi) in A.Refs) if (oi != 0xFFFF) uidOf[oi] = u;
        var models = new Dictionary<uint, string>(); var defs = new List<(uint, string, uint)>();
        for (int i = 0; i < A.Objects.Count; i++)
        {
            var o = A.Objects[i]; string tn = A.Types[o.Blocks[0].Type].Name;
            if (tn == "GameModel") models[uidOf.GetValueOrDefault(i)] = A.Top(o)["Filename"] as string ?? "";
            else if (tn is "DynamicObjDef" or "ElementObjectRecord") defs.Add((uidOf.GetValueOrDefault(i), tn, U(A.Top(o).GetValueOrDefault("Model"))));
        }
        var outp = new Dictionary<uint, (string, string?)>();
        foreach (var (u, tn, m) in defs) outp[u] = (tn, models.GetValueOrDefault(m));
        return outp;
    }

    /// every placed object of a city's actorpack.xt, list by list
    public static List<OrigList> Actors(byte[] pack, Dictionary<uint, (string Type, string? Model)>? defs = null)
    {
        var K = new Kslf(pack);
        var uidOf = new Dictionary<int, uint>();
        foreach (var (u, _, oi) in K.Refs) if (oi != 0xFFFF) uidOf[oi] = u;
        var lists = new List<OrigList>();
        for (int oi = 0; oi < K.Objects.Count; oi++)
        {
            var o = K.Objects[oi];
            var L = new OrigList { Obj = oi, Uid = uidOf.GetValueOrDefault(oi), Name = K.Name(o) };
            foreach (var b in o.Blocks)
            {
                if (b.Kind != 0 || !Derives(K.Types, b.Type, "PosActorCreate")) continue;
                int mOff = K.FieldOffset(b.Type, "WorldMatrix");
                for (int e = 0; e < b.Count; e++)
                {
                    int at = b.Offset + e * b.Size;
                    var f = K.Struct(o, b.Type, at);
                    var a = new OrigActor { Type = b.Name, Name = f.GetValueOrDefault("Name") as string ?? "", ListUid = L.Uid, Obj = oi, MatrixAt = at + mOff };
                    for (int i = 0; i < 12; i++) a.Matrix[i] = B.F32(K.Data, a.MatrixAt + 4 * i);
                    var def = f.GetValueOrDefault("Definition") ?? f.GetValueOrDefault("DynamicObjType");
                    if (def is Dictionary<string, object?> dd)
                    {
                        a.DefUid = U(dd); a.DefType = dd.GetValueOrDefault("type") as string;
                        if (defs != null && defs.TryGetValue(a.DefUid.Value, out var dm)) a.Model = dm.Model;
                    }
                    if (f.GetValueOrDefault("PickupType") is { } pt) a.Pickup = pt.ToString();
                    L.Actors.Add(a);
                }
            }
            lists.Add(L);
        }
        return lists;
    }

    /// every spline of a route's .aid (race line, lanes ...), points in game space (x, y, z: the file stores -z)
    public static List<OrigLine> Lines(byte[] aid)
    {
        var o = new List<OrigLine>();
        foreach (var s in Aid.Splines(aid))
        {
            var L = new OrigLine { Name = s.Name, Flags = s.Flags };
            for (int i = 0; i < s.Count; i++)
            {
                int at = s.First + (s.Name == "Start" ? 4 : 0) + i * Aid.Point;
                if (at + 44 > aid.Length) break;
                var p = Aid.PointAt(aid, at);
                L.Points.Add(new[] { p[0], p[1], -p[2], p[3] });
            }
            o.Add(L);
        }
        return o;
    }

    public static string ActorPackName(string folder) => $"gamedata/levels/{folder}/actorpack.xt";
    public static string AidName(string folder, string route) => $"gamedata/levels/{folder}/{route.ToLowerInvariant()}.aid";
}

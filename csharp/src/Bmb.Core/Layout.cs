// Everything placed along a road. Port of flat_earth/road_layout.py (distance along the road + a sideways offset).
using System.Text;

namespace Bmb.Core;

public sealed class TileSpot
{
    public double X, Y, Z, Turn, Pitch, Rx, Rz; public uint Uid; public double[] Scale = { 1, 1, 1 };
    public TileSpot Copy() { var t = (TileSpot)MemberwiseClone(); t.Scale = (double[])Scale.Clone(); return t; }
}
public sealed class LampSpot { public double X, Y, Z, Turn, Rx, Rz; public uint Uid; public LampSpot Copy() => (LampSpot)MemberwiseClone(); }
public sealed class PickupSpot { public double X, Y, Z, A; public string? Type; public bool HasHeading = true; }
public sealed class AudioSpot { public double X, Y, Z; public uint Uid; }
public sealed class AidInfo
{
    public int Points, StartIndex, FinishIndex; public double[] StartLine = new double[3], FinishLine = new double[3]; public double StartAngle, FinishAngle;
    public Dictionary<string, int> Gates = new(); public bool Closed;
}

public static class Layout
{
    const double HalfPi = Math.PI / 2;
    public const double PickupStepDefault = 150.0; public const int PickupPerRowDefault = 5, MaxPickups = 109, LampPlacedMax = 340, BigMin = 6;
    public const double Elevated = 0.5, ChevronRadius = 260.0, PanelLen = 16.0, ChevronOut = 3.0, WallMark = 2.5, DividerSolid = 2.0, AiLane = 12.0;
    public const uint SceneryDefBase = 0xF1B00000;
    public static readonly (string Key, string Text)[] Scenery =
    {
        ("ships", "Ships (horizon)"), ("cranes", "Cranes"), ("landmarks", "Ferris wheel + radio mast"), ("lamps", "Street lamps"), ("billboards", "Billboards"),
        ("chevrons", "Arrow boards on bends"), ("signs", "Start / finish lights"), ("buildings", "Tall buildings"), ("arch", "Start / finish arch"),
    };
    public static readonly Dictionary<string, bool> NewMapScenery = new()
    {
        ["ships"] = false, ["cranes"] = false, ["landmarks"] = false, ["lamps"] = true, ["billboards"] = false, ["chevrons"] = true, ["signs"] = true, ["buildings"] = false, ["arch"] = true,
    };
    public static readonly string[] AutoPattern = { "Nitro", "Shunt", "Shield", "Barge", "Bolt", "Shock", "Mine", "Repair" };
    static readonly HashSet<string> FixedKinds = new(new[] { "road", "line", "linebox", "block" }.Concat(Fe.Arches.Select(a => a.Name)));
    public static bool IsFixed(string key) => FixedKinds.Contains(key.Split(':')[0]);
    public static int TileFree = 144;

    public static readonly Dictionary<string, double[]> ObjectBox = MakeBox();
    static Dictionary<string, double[]> MakeBox()
    {
        var b = new Dictionary<string, double[]>
        {
            ["tanker"] = new double[] { -18, 18, 0, 36, -166, 166 }, ["cruise"] = new double[] { -18, 18, -7.8, 28, -131, 131 }, ["crane"] = new double[] { -60, 60, 0, 71, -13, 13 },
            ["ferris"] = new double[] { -22, 22, 0, 48, -3, 3 }, ["radio"] = new double[] { -3, 3, 0, 60, -3, 3 },
            ["chevron"] = new double[] { -8, 8, 0, 2.5, -.3, .3 }, ["line"] = new double[] { -1.5, 1.5, 0, .3, -34, 34 }, ["linebox"] = new double[] { -13.7, 0, 0, 2.9, 0, 2.4 },
            ["block"] = new double[] { -13.7, 0, 0, 2.9, 0, 2.4 }, ["lamp"] = new double[] { -.6, .6, 0, 10.5, -.6, .6 }, ["barrier"] = new double[] { -1.8, 1.8, 0, 1, -.4, .4 },
            ["billboard"] = new double[] { -4, 4, 0, 8, -.3, .3 }, ["flashing"] = new double[] { -.8, .8, 0, 2.2, -.15, .15 }, ["light"] = new double[] { -.75, .75, 0, 6.8, -.3, .3 },
            ["barrel"] = new double[] { -.4, .4, 0, 1, -.4, .4 }, ["rwbarrier"] = new double[] { -1.2, 1.2, 0, 1.1, -.2, .2 }, ["cone"] = new double[] { -.25, .25, 0, .8, -.25, .25 },
        };
        foreach (var (name, wx, dz, h, _) in Fe.Towers) b[name] = new[] { -wx / 2, wx / 2, 0, h, -dz / 2, dz / 2 };
        foreach (var (name, _, _) in Fe.Arches) b[name] = new[] { -Fe.ArchW / 2 - Fe.ArchPillar / 2, Fe.ArchW / 2 + Fe.ArchPillar / 2, 0, Fe.ArchY1, -Fe.ArchDepth / 2, Fe.ArchDepth / 2 };
        return b;
    }

    public static double PickupStep(JObj? m) => Math.Max(40.0, Math.Min(600.0, m != null && m.Truthy("pickup_step") ? m.Num("pickup_step") : PickupStepDefault));
    public static int RowSize(JObj? m, JObj? it = null)
    {
        double n = it != null && it.Truthy("n") ? it.Num("n") : m != null && m.Truthy("pickup_per_row") ? m.Num("pickup_per_row") : PickupPerRowDefault;
        return Math.Max(1, Math.Min(9, (int)n));
    }
    public static bool SceneryOn(JObj? m, string key)
    {
        var s = m?.Obj("scenery");
        if (s != null && s.ContainsKey(key)) return Json.Truthy(s[key]);
        return NewMapScenery.GetValueOrDefault(key, true);
    }

    public static (double, double) LineS(Road road, AidInfo info)
    {
        double s0 = road.Dist[info.StartIndex];
        return road.Closed ? (s0, s0 + road.Length) : (s0, road.Dist[info.FinishIndex]);
    }
    static double Y(Road road, double s, double lat = 0.0) => Fe.GroundY + road.HAt(s, lat);
    static bool IsElevated(Road road, double s) => road.HAt(s) > Elevated || road.TiltDeg(s) != 0;

    static List<(double X, double Z, double A)> BlockSpots(Road road, AidInfo info, JObj m)
    {
        var o = new List<(double, double, double)>();
        int g = 0;
        foreach (var (s, lane, gi) in ObstacleGroups(road, info, m))
        {
            double w = road.WAt(s), other = new[] { -1, 0, 1 }[(gi + 1) % 3] * w / 4;
            o.Add(road.At(s + 12, lane)); o.Add(road.At(s + 40, other));
            g++;
        }
        return o;
    }

    // Python numbers each group by its enumerate() position among the groups kept
    static List<(double S, double Lane, int G)> ObstacleGroups(Road road, AidInfo info, JObj m)
    {
        var r = new List<(double, double, int)>();
        if (!m.Truthy("obstacles")) return r;
        var (s0, s1) = LineS(road, info);
        double span = s1 - s0;
        var net = Network.Get(m, road);
        var busy = net.BusyRanges().Select(b => (b.Item1 * 4.0 - 80, b.Item2 * 4.0 + 80)).ToList();
        double L = road.Length;
        int kept = 0;
        for (int g = 0; g < Fe.ObstacleGroups; g++)
        {
            double s = s0 + span * (g + 1) / (Fe.ObstacleGroups + 1) + Fe.PickupStep / 2;
            if (new[] { -80, 0, 50, 120 }.Any(d => IsElevated(road, s + d))) continue;
            bool hit = false;
            foreach (var (a, b) in busy)
                foreach (double sh in road.Closed ? new[] { 0.0, L, -L } : new[] { 0.0 })
                    if (a <= s + sh && s + sh <= b) hit = true;
            if (hit) continue;
            r.Add((s, new[] { -1, 0, 1 }[g % 3] * road.WAt(s) / 4, kept++));
        }
        return r;
    }

    public static (double[][] A, double[]) ActorAxes(double turn, double rx = 0, double rz = 0) => (ActorAxes3(turn, rx, rz), Array.Empty<double>());

    public static double[][] ActorAxes3(double turn, double rx, double rz)
    {
        double c = Math.Cos(turn), s = Math.Sin(turn), ca = Math.Cos(rx), sa = Math.Sin(rx), cb = Math.Cos(rz), sb = Math.Sin(rz);
        double[] F(double x, double y, double z)
        {
            double y1 = ca * y - sa * z, z1 = sa * y + ca * z;
            double x2 = cb * x - sb * y1, y2 = sb * x + cb * y1;
            return new[] { c * x2 + s * z1, y2, -s * x2 + c * z1 };
        }
        return new[] { F(1, 0, 0), F(0, 1, 0), F(0, 0, 1) };
    }

    static string TileKind(uint uid)
    {
        foreach (var b in Fe.Big) if (b.DefUid == uid) return b.Name;
        if (uid == Fe.LineDefUid) return "line";
        if (uid == Fe.TestDefUid) return "linebox";
        if (uid == Fe.BlockDefUid) return "block";
        if (uid == Fe.ChevronDefUid) return "chevron";
        return "road";
    }

    static string LampKind(LampSpot sp)
    {
        if (sp.Uid != 0) foreach (var r in Fe.Roadside) if (r.Ours == sp.Uid) return r.Name;
        return "lamp";
    }

    public static List<string> ObjectKeys(IEnumerable<string> kinds)
    {
        var count = new Dictionary<string, int>(); var o = new List<string>();
        foreach (var k in kinds)
        {
            if (k.Contains(':')) { o.Add(k); continue; }
            int c = count.GetValueOrDefault(k);
            o.Add($"{k}:{c}"); count[k] = c + 1;
        }
        return o;
    }

    static double[] ScaleOf(object? k)
    {
        if (k is List<object?> l) return new[] { Json.D(l[0]), Json.D(l[1]), Json.D(l[2]) };
        double v = k == null ? 1.0 : Json.D(k);
        return new[] { v, v, v };
    }

    public static (List<TileSpot>, List<string>) ApplyEdits(List<TileSpot> spots, List<string> kinds, JObj? edits)
    {
        var o = new List<TileSpot>(); var keys = new List<string>(); var ks = ObjectKeys(kinds);
        for (int i = 0; i < spots.Count; i++)
        {
            var sp = spots[i]; string key = ks[i];
            var e = edits?.Obj(key);
            if (e != null && e.Count > 0 && !IsFixed(key))
            {
                if (e.Truthy("hidden")) continue;
                sp = sp.Copy();
                sp.X += e.Num("dx"); sp.Y += e.Num("dy"); sp.Z += e.Num("dz"); sp.Turn += e.Num("turn");
                sp.Scale = e.ContainsKey("scale") ? ScaleOf(e["scale"]) : new[] { 1.0, 1.0, 1.0 };
                sp.Rx = e.Num("rx"); sp.Rz = e.Num("rz");
            }
            o.Add(sp); keys.Add(key);
        }
        return (o, keys);
    }

    public static (List<LampSpot>, List<string>) ApplyEdits(List<LampSpot> spots, List<string> kinds, JObj? edits)
    {
        var o = new List<LampSpot>(); var keys = new List<string>(); var ks = ObjectKeys(kinds);
        for (int i = 0; i < spots.Count; i++)
        {
            var sp = spots[i]; string key = ks[i];
            var e = edits?.Obj(key);
            if (e != null && e.Count > 0 && !IsFixed(key))
            {
                if (e.Truthy("hidden")) continue;
                sp = sp.Copy();
                sp.X += e.Num("dx"); sp.Y += e.Num("dy"); sp.Z += e.Num("dz"); sp.Turn += e.Num("turn");
                sp.Rx = e.Num("rx"); sp.Rz = e.Num("rz");
            }
            o.Add(sp); keys.Add(key);
        }
        return (o, keys);
    }

    public static (List<TileSpot> Spots, List<string> Keys) TileSpotsK(Road road, AidInfo info, JObj m)
    {
        var spots = RoadMesh.Spots(m, road);
        var (s0, s1) = LineS(road, info);
        foreach (double s in road.Closed ? new[] { s0 } : new[] { s0, s1 })
        {
            var (x, z, a) = road.At(s);
            double across = Math.Max(1.0, (road.WAt(s) + 8.0) / Fe.LineLen);
            spots.Add(new TileSpot { X = x, Y = Y(road, s) - Fe.LineTopBelow, Z = z, Uid = Fe.LineDefUid, Turn = a - HalfPi, Pitch = road.PitchAt(s), Scale = new[] { 1.0, 1.0, across } });
            if (SceneryOn(m, "arch"))
            {
                string name = road.Closed ? "arch_lap" : s == s0 ? "arch_start" : "arch_finish";
                uint uid = Fe.Big.First(b => b.Name == name).DefUid;
                double sx = (road.WAt(s) / 2 + 3.5) / (Fe.ArchW / 2 - Fe.ArchPillar / 2);
                spots.Add(new TileSpot { X = x, Y = Y(road, s), Z = z, Uid = uid, Turn = a + Math.PI, Scale = new[] { sx, 1.0, 1.0 } });
            }
        }
        foreach (var (cx, cz, a) in BlockSpots(road, info, m))
        {
            double fx = Math.Sin(a), fz = Math.Cos(a), sx = -fz, sz = fx;
            var (L, _, W) = Fe.Container;
            spots.Add(new TileSpot { X = cx + sx * L / 2 + fx * W / 2, Y = Fe.GroundY, Z = cz + sz * L / 2 + fz * W / 2, Uid = Fe.BlockDefUid, Turn = a + Math.PI });
        }
        var kinds = spots.Select(t => TileKind(t.Uid)).ToList();
        var groups = new List<(string Key, List<TileSpot> L)>();
        foreach (var (sp, key) in PlacedSpots(m).Tiles)
        {
            var g = groups.FirstOrDefault(q => q.Key == key);
            if (g.L == null) { g = (key, new()); groups.Add(g); }
            g.L.Add(sp);
        }
        var placed = new List<(TileSpot, string)>();
        foreach (var (key, g) in groups)
        {
            if (spots.Count + placed.Count + g.Count > 144) continue;
            placed.AddRange(g.Select(s => (s, key)));
        }
        TileFree = 144 - spots.Count - placed.Count;
        foreach (var (sp, key) in placed) { spots.Add(sp); kinds.Add(key); }
        if (SceneryOn(m, "chevrons"))
            foreach (var (s, lat) in ChevronPanels(road, 144 - spots.Count - BigMin, m))
            {
                var (x, z, a) = road.At(s, lat);
                spots.Add(new TileSpot { X = x, Y = Y(road, s), Z = z, Uid = Fe.ChevronDefUid, Turn = a - HalfPi });
                kinds.Add("chevron");
            }
        var big = BigSpots(road, info, 144 - spots.Count, m);
        spots.AddRange(big);
        kinds.AddRange(big.Select(t => TileKind(t.Uid)));
        if (spots.Count > 144) throw new InvalidOperationException($"too many objects: {spots.Count}");
        return ApplyEdits(spots, kinds, m.Obj("edits"));
    }

    public static List<TileSpot> TileSpots(Road road, AidInfo info, JObj m) => TileSpotsK(road, info, m).Spots;

    static List<TileSpot> BigSpots(Road road, AidInfo info, int room, JObj m)
    {
        var net = Network.Get(m, road);
        bool Clear(double x, double z, double a, double halfLen, double margin)
        {
            if (net.Branches.Count == 0) return true;
            double fx = Math.Sin(a), fz = Math.Cos(a);
            return !new[] { -halfLen, 0.0, halfLen }.Any(t => net.NearBranch(x + fx * t, z + fz * t, margin));
        }
        BigDef Def(string n) => Fe.Big.First(b => b.Name == n);
        var (s0, s1) = LineS(road, info);
        var spots = new List<TileSpot>();
        if (SceneryOn(m, "landmarks"))
            foreach (var (s, lat, name) in new[] { (Math.Max(s0 - 60, 0), -(road.WAt(s0) / 2 + 40), "ferris"), (Math.Min(s1 + 60, road.Length), road.WAt(s1) / 2 + 40, "radio") })
            {
                var (x, z, a) = road.At(s, lat);
                if (!road.NearRoad(x, z, road.Width / 2 + 20) && Clear(x, z, a, 0.0, 40.0))
                    spots.Add(new TileSpot { X = x, Y = Fe.GroundY, Z = z, Uid = Def(name).DefUid });
            }
        var ships = new List<TileSpot>();
        if (SceneryOn(m, "ships"))
            foreach (int side in new[] { -1, 1 })
            {
                double s = side < 0 ? -150.0 : -20.0; int k = side < 0 ? 0 : 1;
                while (s < road.Length + 150)
                {
                    string name = k % 2 == 0 ? "tanker" : "cruise";
                    double length = name == "tanker" ? 333.0 : 263.0;
                    var (x, z, a) = road.At(s + length / 2, side * (road.Width / 2 + Fe.ShipZ - 30));
                    if (!road.NearRoad(x, z, Fe.ShipZ - 20) && ships.All(o => Py.Hypot(x - o.X, z - o.Z) > 200) && Clear(x, z, a, length / 2, 50.0))
                        ships.Add(new TileSpot { X = x, Y = Fe.GroundY + Def(name).Lift, Z = z, Uid = Def(name).DefUid, Turn = a });
                    s += length + 10; k++;
                }
            }
        var cranes = new List<TileSpot>();
        if (SceneryOn(m, "cranes"))
            foreach (int side in new[] { -1, 1 })
            {
                double s = side < 0 ? 300.0 : 450.0;
                while (s < road.Length - 200)
                {
                    var (x, z, a) = road.At(s, side * (road.Width / 2 + Fe.CraneZ - 30));
                    if (!road.NearRoad(x, z, Fe.CraneZ - 30) && Clear(x, z, a, 0.0, 40.0))
                        cranes.Add(new TileSpot { X = x, Y = Fe.GroundY, Z = z, Uid = Def("crane").DefUid, Turn = a - HalfPi });
                    s += road.Length / Math.Max(Fe.CranesPerSide, 1);
                }
            }
        var towers = new List<TileSpot>();
        if (SceneryOn(m, "buildings"))
        {
            int k = 0;
            foreach (int side in new[] { -1, 1 })
            {
                double s = side < 0 ? 60.0 : 180.0;
                while (s < road.Length)
                {
                    var (name, wx, dz, _, _) = Fe.Towers[k % Fe.Towers.Length];
                    double lat = side * (road.WAt(s) / 2 + Fe.TowerZ + (k * 37 % 90));
                    var (x, z, a) = road.At(s, lat);
                    double gap = Math.Max(wx, dz) / 2 + 60;
                    if (!road.NearRoad(x, z, gap + road.Width / 2) && Clear(x, z, a, 0.0, gap) && towers.All(o => Py.Hypot(x - o.X, z - o.Z) > Math.Max(wx, dz) + 40))
                        towers.Add(new TileSpot { X = x, Y = Fe.GroundY, Z = z, Uid = Def(name).DefUid, Turn = a });
                    k++; s += Fe.TowerStep;
                }
            }
        }
        foreach (var sp in ships.Concat(cranes).Concat(towers)) { if (spots.Count >= room) break; spots.Add(sp); }
        return room >= 0 && spots.Count > room ? spots.GetRange(0, room) : room < 0 ? spots.GetRange(0, Math.Max(0, spots.Count + room)) : spots;
    }

    public static List<(double S, double Lat)> ChevronPanels(Road road, int room, JObj? m)
    {
        var net = m != null ? Network.Get(m, road) : null;
        int n = road.Count, seg = (int)Py.Round(PanelLen / 4.0), i = 2;
        var cand = new List<(double, double)>();
        while (i < n - 2)
        {
            double da = Py.Mod(road.Heading[i + 2] - road.Heading[i - 2] + Math.PI, 2 * Math.PI) - Math.PI;
            double radius = Math.Abs(da) > 1e-6 ? 16.0 / Math.Abs(da) : 1e9;
            if (radius < ChevronRadius && road.Height[i] < 0.05)
            {
                int side = da > 0 ? 1 : -1;
                double lat = side * (road.WidthAt[i] / 2 + ChevronOut);
                var (x, z, _) = road.At(road.Dist[i], lat);
                if (net == null || !net.NearBranch(x, z, PanelLen)) cand.Add((road.Dist[i], lat));
                i += seg;
            }
            else i++;
        }
        if (room <= 0) return new();
        int keep = 1;
        while ((cand.Count + keep - 1) / keep > room) keep++;
        return cand.Where((_, k) => k % keep == 0).ToList();
    }

    public static (List<LampSpot> Spots, List<string> Keys) LampSpotsK(Road road, AidInfo info, JObj m, double spread = 1.0)
    {
        var placed = PlacedSpots(m).Lamps;
        if (placed.Count > LampPlacedMax) placed = placed.GetRange(0, LampPlacedMax);
        var spots = LampSpots0(road, info, m, spread);
        for (int i = 0; i < 60; i++)
        {
            if (spots.Count + placed.Count <= 356) break;
            spread *= 1.15;
            spots = LampSpots0(road, info, m, spread);
        }
        if (spots.Count > 356 - placed.Count) spots = spots.GetRange(0, 356 - placed.Count);
        var kinds = spots.Select(LampKind).Concat(placed.Select(p => p.Key)).ToList();
        spots.AddRange(placed.Select(p => p.Sp));
        return ApplyEdits(spots, kinds, m.Obj("edits"));
    }

    public static List<LampSpot> LampSpots(Road road, AidInfo info, JObj m) => LampSpotsK(road, info, m).Spots;

    static List<LampSpot> LampSpots0(Road road, AidInfo info, JObj m, double spread)
    {
        var spots = new List<LampSpot>();
        Network? net = Network.Get(m, road);
        if (net.Branches.Count == 0) net = null;
        void Add(double s, double lat, Func<double, double> turn, uint uid, bool check = true)
        {
            var (x, z, a) = road.At(s, lat);
            if (check && lat != 0 && !road.OffRoad(x, z, lat)) return;
            if (check && net != null && net.NearBranch(x, z, 4.0)) return;
            spots.Add(new LampSpot { X = x, Y = Y(road, s), Z = z, Turn = turn(a), Uid = uid });
        }
        double Plus(double a) => a + HalfPi;
        double Minus(double a) => a - HalfPi;
        double sx = 10.0;
        while (SceneryOn(m, "lamps") && sx <= road.Length - 10)
        {
            if (!IsElevated(road, sx))
            {
                double lat = road.WAt(sx) / 2 + 10;
                Add(sx, lat, Plus, Fe.Rs("lamp"));
                Add(sx, -lat, Minus, Fe.Rs("lamp"));
            }
            sx += Fe.LampStep * spread;
        }
        sx = 10.0 + Fe.BillboardStep / 2;
        while (SceneryOn(m, "billboards") && sx <= road.Length - 10)
        {
            if (!IsElevated(road, sx)) Add(sx, road.WAt(sx) / 2 + 20, Plus, Fe.Rs("billboard"));
            double s2 = sx + Fe.BillboardStep / 2;
            if (s2 <= road.Length - 10 && !IsElevated(road, s2)) Add(s2, -(road.WAt(s2) / 2 + 20), Minus, Fe.Rs("billboard"));
            sx += Fe.BillboardStep;
        }
        if (SceneryOn(m, "signs"))
        {
            var (a0, a1) = LineS(road, info);
            foreach (double ls in new[] { a0, a1 })
            {
                double lat = road.WAt(ls) / 2 + 10;
                foreach (int side in new[] { -1, 1 })
                {
                    Func<double, double> face = side > 0 ? Plus : Minus;
                    Add(ls - 6, side * lat, face, Fe.Rs("flashing"));
                    Add(ls + 6, side * lat, face, Fe.Rs("light"));
                }
            }
        }
        foreach (var (gs, lane, _) in ObstacleGroups(road, info, m))
        {
            Add(gs, lane, a => a, Fe.Rs("rwbarrier"), false);
            int k = 0;
            foreach (var (ds, dl) in new[] { (-3, -3), (-3, 3), (3, -5), (3, 5) })
                Add(gs + ds, lane + dl, Minus, k++ < 2 ? Fe.Rs("barrel") : Fe.Rs("cone"), false);
        }
        return spots;
    }

    // ---- the library -------------------------------------------------------------------------------------------
    static Dictionary<string, JObj>? _catalog;
    public static Dictionary<string, JObj> Catalog()
    {
        if (_catalog != null) return _catalog;
        var c = new Dictionary<string, JObj>();
        try
        {
            var j = (JObj)Json.Load(Paths.Catalog)!;
            foreach (var e in j.Objs("objects")) c[e.Str("id")!] = e;
        }
        catch (IOException) { }
        foreach (var e in ModelImport.Entries()) c[e.Str("id")!] = e;
        return _catalog = c;
    }
    public static void ReloadCatalog() => _catalog = null;

    public static uint SceneryDefUid(string modelUid) => SceneryDefBase | (Pak.Crc32Of(Encoding.ASCII.GetBytes(modelUid.ToUpperInvariant())) & 0xFFFFF);

    public static (List<(TileSpot Sp, string Key)> Tiles, List<(LampSpot Sp, string Key)> Lamps) PlacedSpots(JObj m)
    {
        var tiles = new List<(TileSpot, string)>(); var lamps = new List<(LampSpot, string)>();
        int i = -1;
        foreach (var o in m.Objs("objects"))
        {
            i++;
            if (!Catalog().TryGetValue(o.Str("id") ?? "", out var e)) continue;
            double x = o.Num("x"), z = o.Num("z"), a = o.Num("turn"), y = Fe.GroundY + o.Num("h");
            string key = $"obj:{i}", kind = e.Str("kind")!;
            if (kind == "breakable") lamps.Add((new LampSpot { X = x, Y = y, Z = z, Turn = a, Uid = Convert.ToUInt32(e.Str("uid"), 16) }, key));
            else if (kind == "import")
                for (int k = 0; k < e.Int("pieces"); k++) tiles.Add((new TileSpot { X = x, Y = y, Z = z, Uid = ModelImport.Uids((int)e.Int("slot"), k).Def, Turn = a }, key));
            else
            {
                uint uid = kind == "scenery" ? SceneryDefUid(e.Str("uid")!) : Convert.ToUInt32(e.Str("uid"), 16);
                tiles.Add((new TileSpot { X = x, Y = y, Z = z, Uid = uid, Turn = a }, key));
            }
        }
        return (tiles, lamps);
    }

    public static List<string> SceneryModels(JObj wld)
    {
        var o = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var loc in wld.Objs("locations"))
            foreach (var m in loc.Objs("maps"))
                foreach (var ob in m.Objs("objects"))
                    if (Catalog().TryGetValue(ob.Str("id") ?? "", out var e) && e.Str("kind") == "scenery") o.Add(e.Str("uid")!.ToUpperInvariant());
        return o.ToList();
    }

    public static double[]? ObjectBoxOf(JObj m, string key)
    {
        var parts = key.Split(':');
        if (parts[0] == "obj")
        {
            var objs = m.Arr("objects") ?? new();
            int n = int.Parse(parts[1]);
            if (n >= objs.Count || !Catalog().TryGetValue(((JObj)objs[n]!).Str("id") ?? "", out var e)) return null;
            var lo = e.Arr("lo")!; var hi = e.Arr("hi")!;
            return new[] { Json.D(lo[0]), Json.D(hi[0]), Json.D(lo[1]), Json.D(hi[1]), Json.D(lo[2]), Json.D(hi[2]) };
        }
        return ObjectBox.GetValueOrDefault(parts[0]);
    }

    static List<Piece> SolidBoxes(Road road, AidInfo info, JObj m)
    {
        var edits = m.Obj("edits");
        var o = new List<Piece>();
        if (edits == null || !edits.Values.OfType<JObj>().Any(e => e.Truthy("solid"))) return o;
        var seen = new HashSet<string>();
        void One(string key, double x, double y, double z, double t, double[] k)
        {
            var e = edits.Obj(key);
            if (e == null || !e.Truthy("solid") || !seen.Add(key)) return;
            var box = ObjectBoxOf(m, key);
            if (box == null) return;
            double x0 = box[0], x1 = box[1], y0 = box[2], y1 = box[3], z0 = box[4], z1 = box[5], kx = k[0], ky = k[1], kz = k[2];
            double xc = (x0 + x1) / 2 * kx, zc = (z0 + z1) / 2 * kz;
            double cx = x + xc * Math.Cos(t) + zc * Math.Sin(t), cz = z - xc * Math.Sin(t) + zc * Math.Cos(t);
            double hl = Math.Max((z1 - z0) / 2 * kz, 0.3), hw = Math.Max((x1 - x0) / 2 * kx, 0.3);
            o.Add(Physics.TurnedBox(cx, cz, y + y0 * ky, y + Math.Max(y1 * ky, y0 * ky + 1.0), hl, hw, t));
        }
        var (ts, tk) = TileSpotsK(road, info, m);
        for (int i = 0; i < ts.Count; i++) One(tk[i], ts[i].X, ts[i].Y, ts[i].Z, ts[i].Turn, ts[i].Scale);
        var (ls, lk) = LampSpotsK(road, info, m);
        var one = new[] { 1.0, 1.0, 1.0 };
        for (int i = 0; i < ls.Count; i++) One(lk[i], ls[i].X, ls[i].Y, ls[i].Z, ls[i].Turn, one);
        return o;
    }

    // ---- power-ups, sounds ----------------------------------------------------------------------------------------
    public static List<JObj> PickupItems(Road road, AidInfo info, JObj m)
    {
        if (m["pickups"] is List<object?> l) return l.OfType<JObj>().ToList();
        var (s0, s1) = LineS(road, info);
        return Network.Get(m, road).AutoRows(s0, s1, PickupStep(m), MaxPickups / RowSize(m));
    }

    public static (List<PickupSpot> Spots, List<int> Own) PickupSpots(Road main, AidInfo info, JObj m)
    {
        var net = Network.Get(m, main);
        var items = PickupItems(main, info, m);
        var spots = new List<PickupSpot>(); var own = new List<int>();
        int k = 0;
        for (int n = 0; n < items.Count; n++)
        {
            var it = items[n];
            int lanes = RowSize(m, it);
            bool single = it.Str("kind") == "single";
            for (int q = 0; q < (single ? 1 : lanes); q++) own.Add(n);
            var road = net.PickupRoad(it);
            double ix = it.Num("x"), iz = it.Num("z");
            var (s, _) = road.Project(ix, iz);
            if (road == main && !single) s = net.ClearOfSplits(s);
            string? ty = it.Truthy("type") ? it.Str("type") : null;
            if (single)
            {
                var (_, _, a) = road.At(s);
                string t = ty ?? AutoPattern[k % AutoPattern.Length];
                spots.Add(new PickupSpot { X = ix, Y = Y(road, s, road.Tilted ? road.Project(ix, iz).Lat : 0.0) + 0.70, Z = iz, A = a, Type = World.PickupTypes[t] });
                k++;
                continue;
            }
            double w = road.WAt(s);
            for (int li = 0; li < lanes; li++)
            {
                double lat = (li - (lanes - 1) / 2.0) * w / (lanes + 1);
                var (x, z, a) = road.At(s, lat);
                string t = ty ?? AutoPattern[k % AutoPattern.Length];
                spots.Add(new PickupSpot { X = x, Y = Y(road, s, lat) + 0.70, Z = z, A = a, Type = World.PickupTypes[t] });
                k++;
            }
        }
        return (spots.Take(MaxPickups).ToList(), own.Take(MaxPickups).ToList());
    }

    public static List<AudioSpot> AudioSpots(Road road, AidInfo info)
    {
        var (s0, s1) = LineS(road, info);
        var (x0, z0, _) = road.At((s0 + s1) / 2);
        var spots = new List<AudioSpot> { new() { X = x0, Y = Y(road, (s0 + s1) / 2) + 2, Z = z0, Uid = Fe.AudioUid("haruna") } };
        foreach (double s in new[] { s0 + (s1 - s0) / 3, s0 + 2 * (s1 - s0) / 3 })
        {
            double side = road.WAt(s) / 2 + 3;
            foreach (double lat in new[] { side, -side })
            {
                var (x, z, _) = road.At(s, lat);
                spots.Add(new AudioSpot { X = x, Y = Y(road, s) + 2, Z = z, Uid = Fe.AudioUid("crowd_m") });
            }
        }
        foreach (double ls in new[] { s0 + 90, s1 })
        {
            double side = road.WAt(ls) / 2 + 3;
            foreach (var (ds, lat, name) in new[] { (0.0, side, "crowd_l"), (0.0, -side, "crowd_l"), (-25.0, -side, "crowd_m"), (25.0, side, "crowd_s"), (-25.0, side, "crowd_s") })
            {
                var (x, z, _) = road.At(ls + ds, lat);
                spots.Add(new AudioSpot { X = x, Y = Y(road, ls + ds) + 2, Z = z, Uid = Fe.AudioUid(name) });
            }
        }
        return spots.Take(15).ToList();
    }

    // ---- collision --------------------------------------------------------------------------------------------------
    static void WallPieces(List<Piece> pieces, List<(double X, double Z)> pts, List<double> y0, List<(double X, double Z)> nrm)
    {
        for (int k = 0; k < pts.Count - 1; k += 120)
        {
            int n = Math.Min(121, pts.Count - k);
            if (n < 2) continue;
            int kk = k;
            pieces.Add(Physics.WallStrip(pts.GetRange(k, n), i => y0[kk + i], i => y0[kk + i] + 3.0, nrm.GetRange(k, n)));
        }
    }

    static List<Piece> RunWalls(Road road, Network net)
    {
        net.Areas().TryGetValue(road, out var area);
        var pieces = new List<Piece>();
        foreach (int side in new[] { -1, 1 })
            foreach (var run in net.WallRuns(road, side, true))
            {
                var pts = new List<(double X, double Z)>(); var hs = new List<double>();
                foreach (var p in run)
                {
                    if (pts.Count > 0 && Sq(p[0] - pts[^1].X) + Sq(p[1] - pts[^1].Z) < 0.04) continue;
                    pts.Add((p[0], p[1])); hs.Add(p[2]);
                }
                if (pts.Count < 2) continue;
                var nrm = new List<(double, double)>(); var y0 = new List<double>();
                for (int k = 0; k < pts.Count; k++)
                {
                    var (x, z) = pts[k];
                    var a = pts[Math.Max(k - 1, 0)]; var b = pts[Math.Min(k + 1, pts.Count - 1)];
                    double dx = b.X - a.X, dz = b.Z - a.Z, ln = Py.Hypot(dx, dz);
                    if (ln == 0) ln = 1.0;
                    double nx = -dz / ln, nz = dx / ln;
                    if (area != null && area.Depth(x + nx, z + nz, hs[k]) < area.Depth(x - nx, z - nz, hs[k])) { nx = -nx; nz = -nz; }
                    nrm.Add((nx, nz));
                    y0.Add(Fe.GroundY + hs[k]);
                }
                WallPieces(pieces, pts, y0, nrm);
            }
        return pieces;
    }

    static double Sq(double v) => v * v;

    static List<Piece> Walls(Road road, bool[][]? mask)
    {
        if (road.Kind == "divider") return DividerWalls(road);
        var pieces = new List<Piece>();
        var idx = new List<int>();
        for (int i = 0; i < road.Count; i += 2) idx.Add(i);
        if (idx[^1] != road.Count - 1) idx.Add(road.Count - 1);
        if (road.Closed) idx.Add(0);
        int si = 0;
        foreach (int side in new[] { -1, 1 })
        {
            var cut = mask?[si];
            var runs = new List<List<int>>(); var cur = new List<int>();
            foreach (int i in idx)
            {
                if (cut != null && cut[i]) { if (cur.Count > 0) runs.Add(cur); cur = new(); }
                else cur.Add(i);
            }
            if (cur.Count > 0) runs.Add(cur);
            var edge = road.Edge(side, 1.0);
            foreach (var run in runs)
            {
                if (run.Count < 2) continue;
                var pts = new List<(double X, double Z)>(); var normals = new List<(double, double)>(); var y0 = new List<double>();
                foreach (int i in run)
                {
                    var (sx, sz) = road.Side(i);
                    if (pts.Count > 0 && Sq(edge[i].X - pts[^1].X) + Sq(edge[i].Z - pts[^1].Z) < 0.25 && i != run[^1]) continue;
                    pts.Add(edge[i]); normals.Add((-sx * side, -sz * side)); y0.Add(Fe.GroundY + road.Surf(i, side * (road.WidthAt[i] / 2 + 1)));
                }
                WallPieces(pieces, pts, y0, normals);
            }
            si++;
        }
        return pieces;
    }

    static List<Piece> DividerWalls(Road road)
    {
        int n = road.Count;
        var idx = new List<int>();
        for (int i = 0; i < n; i += 2) idx.Add(i);
        if (idx[^1] != n - 1) idx.Add(n - 1);
        var pieces = new List<Piece>();
        var taper = road.Taper ?? Enumerable.Repeat(1.0, n).ToList();
        var sidePts = new Dictionary<int, (List<(double X, double Z)> P, List<double> Y)>();
        foreach (int side in new[] { -1, 1 })
        {
            var pts = new List<(double X, double Z)>(); var y0 = new List<double>();
            foreach (int i in idx)
            {
                var (cx, cz) = road.Centre[i]; var (sx, sz) = road.Side(i);
                double d = DividerSolid * taper[i];
                pts.Add((cx + sx * side * d, cz + sz * side * d));
                y0.Add(Fe.GroundY + road.Height[i]);
            }
            var nrm = new List<(double, double)>();
            for (int k = 0; k < pts.Count; k++)
            {
                var a = pts[Math.Max(k - 1, 0)]; var b = pts[Math.Min(k + 1, pts.Count - 1)];
                double dx = b.X - a.X, dz = b.Z - a.Z, ln = Py.Hypot(dx, dz);
                if (ln == 0) ln = 1.0;
                double nx = -dz / ln, nz = dx / ln;
                var (sx, sz) = road.Side(idx[k]);
                if (nx * sx * side + nz * sz * side < 0) { nx = -nx; nz = -nz; }
                nrm.Add((nx, nz));
            }
            sidePts[side] = (pts, y0);
            for (int k = 0; k < pts.Count - 1; k += 120)
            {
                int c = Math.Min(121, pts.Count - k), kk = k;
                pieces.Add(Physics.WallStrip(pts.GetRange(k, c), i => y0[kk + i], i => y0[kk + i] + 3.0, nrm.GetRange(k, c)));
            }
        }
        foreach (var (end, i, sign) in new[] { (0, 0, -1), (-1, n - 1, 1) })
        {
            var lp = sidePts[-1].P; var rp = sidePts[1].P;
            var a = lp[end < 0 ? lp.Count - 1 : 0]; var b = rp[end < 0 ? rp.Count - 1 : 0];
            if (Py.Hypot(a.X - b.X, a.Z - b.Z) < 0.2) continue;
            var (fx, fz) = road.Forward(i);
            double y = sidePts[-1].Y[end < 0 ? sidePts[-1].Y.Count - 1 : 0];
            pieces.Add(Physics.WallStrip(new[] { a, b }, _ => y, _ => y + 3.0, new[] { (fx * sign, fz * sign), (fx * sign, fz * sign) }));
        }
        return pieces;
    }

    static List<Piece> Decks(Road road)
    {
        var decks = new List<Piece>(); var runs = new List<List<int>>();
        for (int i = 0; i < road.Count; i++)
        {
            if (road.Height[i] <= 0.05) continue;
            if (runs.Count > 0 && i == runs[^1][^1] + 1) runs[^1].Add(i);
            else runs.Add(new List<int> { i });
        }
        foreach (var run0 in runs)
        {
            var run = new List<int> { Math.Max(run0[0] - 1, 0) }; run.AddRange(run0); run.Add(Math.Min(run0[^1] + 1, road.Count - 1));
            for (int k = 0; k < run.Count - 1; k += 126)
            {
                var left = new List<double[]>(); var right = new List<double[]>();
                foreach (int i in run.GetRange(k, Math.Min(127, run.Count - k)))
                {
                    var (cx, cz) = road.Centre[i]; var (sx, sz) = road.Side(i);
                    double half = road.WidthAt[i] / 2 + 1;
                    left.Add(new[] { cx - sx * half, Fe.GroundY + road.Surf(i, -half), cz - sz * half });
                    right.Add(new[] { cx + sx * half, Fe.GroundY + road.Surf(i, half), cz + sz * half });
                }
                decks.Add(Physics.DeckStrip(left, right));
            }
        }
        return decks;
    }

    public static (List<Piece> Pieces, List<Piece> Decks) CollisionPieces(Road road, AidInfo info, JObj m)
    {
        var net = Network.Get(m, road);
        var pieces = new List<Piece>(); var decks = new List<Piece>();
        foreach (var r in net.Roads())
        {
            if (r.Kind != "divider" && net.Branches.Count > 0) pieces.AddRange(RunWalls(r, net));
            else pieces.AddRange(Walls(r, net.WallMask(r)));
            if (r.Kind != "divider") decks.AddRange(Decks(r));
        }
        var (L, H, W) = Fe.Container;
        foreach (var (cx, cz, a) in BlockSpots(road, info, m))
            pieces.Add(Physics.TurnedBox(cx, cz, Fe.GroundY, Fe.GroundY + H, W / 2, L / 2, a));
        pieces.AddRange(SolidBoxes(road, info, m));
        return (pieces, decks);
    }

    // ---- the AI ------------------------------------------------------------------------------------------------------
    static List<(double, double, double, double)> BlockedSpans(Road road, AidInfo info, JObj m)
    {
        var (L, _, W) = Fe.Container;
        var spans = new List<(double, double, double, double)>();
        foreach (var (s, lane, g) in ObstacleGroups(road, info, m))
        {
            double other = new[] { -1, 0, 1 }[(g + 1) % 3] * road.WAt(s) / 4;
            spans.Add((s - 6, s + 6, lane - 7, lane + 7));
            foreach (var (ds, lat) in new[] { (12.0, lane), (40.0, other) })
                spans.Add((s + ds - W / 2 - 1, s + ds + W / 2 + 1, lat - L / 2 - 1.5, lat + L / 2 + 1.5));
        }
        return spans;
    }

    public static List<(double S, double Lat)> ProjectSeq(Road road, IEnumerable<(double X, double Z)> pts)
    {
        int n = road.Count, i0 = -1;
        var o = new List<(double, double)>();
        foreach (var (x, z) in pts)
        {
            int Best(int a, int b)
            {
                int bi = a; double bd = double.MaxValue;
                for (int k = a; k < b; k++)
                {
                    double d = Sq(road.Centre[k].X - x) + Sq(road.Centre[k].Z - z);
                    if (d < bd) { bd = d; bi = k; }
                }
                return bi;
            }
            int i = i0 < 0 ? Best(0, n) : Best(Math.Max(0, i0 - 40), Math.Min(n, i0 + 60));
            if (i0 >= 0 && Sq(road.Centre[i].X - x) + Sq(road.Centre[i].Z - z) > 150.0 * 150.0) i = Best(0, n);
            i0 = i;
            var (cx, cz) = road.Centre[i]; var (fx, fz) = road.Forward(i); var (sx, sz) = road.Side(i);
            o.Add((road.Dist[i] + (x - cx) * fx + (z - cz) * fz, (x - cx) * sx + (z - cz) * sz));
        }
        return o;
    }

    static double Interp(List<(double S, double Lat)> samples, double s)
    {
        if (s <= samples[0].S) return samples[0].Lat;
        if (s >= samples[^1].S) return samples[^1].Lat;
        int lo = 0, hi = samples.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (samples[mid].S <= s) lo = mid; else hi = mid;
        }
        var (s0, a) = samples[lo]; var (s1, b) = samples[hi];
        return a + (b - a) * (s - s0) / Math.Max(s1 - s0, 1e-9);
    }

    public static List<double>? AiProfile(Road road, JObj m)
    {
        var p = m.Obj("ai_path");
        if (p == null || p.Str("mode") != "drawn" || (p.Arr("points")?.Count ?? 0) < 2) return null;
        double lane = p.ContainsKey("lane") ? p.Num("lane") : AiLane;
        var pts = p.Arr("points")!.Cast<List<object?>>().Select(q => (Json.D(q[0]), Json.D(q[1])));
        var samples = ProjectSeq(road, pts).OrderBy(q => q.S).ThenBy(q => q.Lat).ToList();
        var o = new List<double>();
        for (int i = 0; i < road.Count; i++)
        {
            double room = Math.Max(road.WidthAt[i] / 2 - lane / 2 - 1.0, 0.0);
            o.Add(Math.Max(-room, Math.Min(room, Interp(samples, road.Dist[i]))));
        }
        return o;
    }

    public static List<(double Lo, double Hi)> AiCorridor(Road road, AidInfo info, JObj m, double lead = 70.0, double margin = 2.5)
    {
        var net = Network.Get(m, road);
        var o = AiCorridor0(road, info, m, lead, margin);
        if (net.Splits.Count > 0) for (int i = 0; i < o.Count; i++) o[i] = net.MainLane(i, o[i].Lo, o[i].Hi);
        return o;
    }

    static List<(double Lo, double Hi)> AiCorridor0(Road road, AidInfo info, JObj m, double lead, double margin)
    {
        var prof = AiProfile(road, m);
        var o = new List<(double, double)>();
        if (prof != null)
        {
            var p = m.Obj("ai_path")!;
            double halfLane = (p.ContainsKey("lane") ? p.Num("lane") : AiLane) / 2;
            for (int i = 0; i < prof.Count; i++)
            {
                double half = road.WidthAt[i] / 2 - margin;
                double lo = Math.Max(prof[i] - halfLane, -half), hi = Math.Min(prof[i] + halfLane, half);
                o.Add((lo, Math.Max(hi, lo + 4.0)));
            }
            return o;
        }
        var spans = BlockedSpans(road, info, m);
        for (int i = 0; i < road.Count; i++)
        {
            double s = road.Dist[i], half = road.WidthAt[i] / 2 - margin, lo = -half, hi = half;
            var near = spans.Where(b => b.Item1 - lead <= s && s <= b.Item2 + 10).ToList();
            if (near.Count > 0)
            {
                var cuts = near.Select(b => (A: b.Item3 - margin, B: b.Item4 + margin)).OrderBy(c => c.A).ThenBy(c => c.B).ToList();
                var gaps = new List<(double A, double B)>();
                double cur = -half;
                foreach (var (a, b) in cuts) { if (a > cur) gaps.Add((cur, a)); cur = Math.Max(cur, b); }
                if (cur < half) gaps.Add((cur, half));
                if (gaps.Count > 0)
                {
                    var best = gaps[0];
                    foreach (var g in gaps) if (g.B - g.A > best.B - best.A) best = g;
                    double d = near.Min(b => Math.Min(Math.Abs(s - (b.Item1 - lead)), Math.Abs(s - (b.Item2 + 10))));
                    double w = Math.Min(1.0, d / (lead * 0.6));
                    lo += (best.A - lo) * w; hi += (best.B - hi) * w;
                }
            }
            o.Add((lo, Math.Max(hi, lo + 4.0)));
        }
        return o;
    }

    public static List<object?> AiLinePoints(Road road, IList<double> lats, int every = 2)
    {
        var idx = new List<int>();
        for (int i = 0; i < road.Count; i += every) idx.Add(i);
        if (idx[^1] != road.Count - 1) idx.Add(road.Count - 1);
        var o = new List<object?>();
        foreach (int i in idx)
        {
            var (x, z, _) = road.At(road.Dist[i], lats[i]);
            o.Add(new List<object?> { Py.RoundN(x, 2), Py.RoundN(z, 2) });
        }
        return o;
    }

    public static List<object?> RacingLine(Road road, double lane = AiLane, double fullAtRadius = 70.0)
    {
        int n = road.Count;
        var curv = new double[n]; var lat = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(i - 3, 0), b = Math.Min(i + 3, n - 1);
            double da = Py.Mod(road.Heading[b] - road.Heading[a] + Math.PI, 2 * Math.PI) - Math.PI;
            curv[i] = da / Math.Max(road.Dist[b] - road.Dist[a], 1e-6);
        }
        for (int i = 0; i < n; i++)
        {
            double room = Math.Max(road.WidthAt[i] / 2 - lane / 2 - 1.0, 0.0);
            lat[i] = -room * Math.Max(-1.0, Math.Min(1.0, curv[i] * fullAtRadius));
        }
        for (int t = 0; t < 3; t++)
        {
            const int k = 10;
            var pre = new double[n + 1];
            for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + lat[i];
            for (int i = 0; i < n; i++) lat[i] = (pre[Math.Min(i + k + 1, n)] - pre[Math.Max(i - k, 0)]) / (Math.Min(i + k + 1, n) - Math.Max(i - k, 0));
        }
        return AiLinePoints(road, lat);
    }
}

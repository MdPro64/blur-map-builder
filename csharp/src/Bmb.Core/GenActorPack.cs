// A location's actorpack.xt = a copy of Mount Haruna's with the lists moved onto our road. Port of flat_earth/gen_actorpack.py.
using System.Text;

namespace Bmb.Core;

public sealed class MapSpec
{
    public double[] StartLine = null!, FinishLine = null!; public double StartAngle, Width;
    public List<TileSpot> Tiles = new(); public uint TileDefUid; public List<LampSpot> Lamps = new(); public List<AudioSpot> Audio = new();
    public List<PickupSpot> PickupSpots = new(); public List<long>? ListUids;
}

public static class GenActorPack
{
    const double GridGap = 8.0, CarAboveGround = 1.0, ParkBelow = 300.0;
    static readonly string[] Roles = { "tiles", "lamps", "grid_sp", "grid_mp", "pick_sp", "pick_mp", "audio" };
    static readonly Dictionary<string, string> RoleSource = new()
    {
        ["tiles"] = "879EC5CA", ["lamps"] = "3ED4CF3F", ["grid_sp"] = "E0B89805", ["grid_mp"] = "3D66BA69", ["pick_sp"] = "699ADE84", ["pick_mp"] = "73B55A31", ["audio"] = "AD458635",
    };
    static readonly Dictionary<string, string> RoleRouteUid = new()
    {
        ["tiles"] = "3ED4CF3F", ["lamps"] = "DE7D2096", ["grid_sp"] = "E0B89805", ["grid_mp"] = "3D66BA69", ["pick_sp"] = "699ADE84", ["pick_mp"] = "73B55A31", ["audio"] = "AD458635",
    };
    const string TileList = "879EC5CA", TileListAs = "3ED4CF3F", LampListAs = "DE7D2096";

    static bool Derives(List<TypeDef> types, int t, string name)
    {
        if (types[t].Name == name) return true;
        foreach (var (b, _) in types[t].Bases) if (Derives(types, b, name)) return true;
        return false;
    }

    static IEnumerable<(string Name, int At)> EachActor(EditCtx ctx)
    {
        for (int bi = 0; bi < ctx.Rows.Count; bi++)
        {
            var r = ctx.Rows[bi];
            if (r[2] != 0 || !Derives(ctx.Types, r[0], "PosActorCreate")) continue;
            int size = ctx.Types[r[0]].Size;
            for (int e = 0; e < r[1]; e++) yield return (ctx.Types[r[0]].Name, ctx.Blocks[bi] + e * size + 12);
        }
    }

    static void Put12(EditCtx ctx, int at, IReadOnlyList<double> v) { for (int i = 0; i < 12; i++) ctx.Data.Put(at + 4 * i, (float)v[i]); }

    static (double, double, double, double) StartFrame(EditCtx ctx)
    {
        var cars = new List<double[]>();
        foreach (var (name, at) in EachActor(ctx))
            if (name == "StartPosition")
            {
                var m = new double[12];
                for (int i = 0; i < 12; i++) m[i] = ctx.Data.F32(at + 4 * i);
                cars.Add(m);
            }
        double fx = cars.Sum(m => m[6]) / cars.Count, fz = cars.Sum(m => m[8]) / cars.Count, ang = Math.Atan2(fx, fz);
        double ux = Math.Sin(ang), uz = Math.Cos(ang), cx = cars.Sum(m => m[9]) / cars.Count, cz = cars.Sum(m => m[11]) / cars.Count;
        double front = cars.Max(m => (m[9] - cx) * ux + (m[11] - cz) * uz);
        return (cx + ux * front, cars.Sum(m => m[10]) / cars.Count, cz + uz * front, ang);
    }

    static Action<EditCtx> MoveGrid((double X, double Z) line, double angle, double groundY, (double X, double Z) finish)
    {
        double lx = line.X - Math.Sin(angle) * GridGap, lz = line.Z - Math.Cos(angle) * GridGap;
        groundY += CarAboveGround;
        return ctx =>
        {
            StartFrame(ctx);
            double ux = Math.Sin(angle), uz = Math.Cos(angle);
            double[] axis = { uz, 0.0, -ux, 0.0, 1.0, 0.0, ux, 0.0, uz };
            int car = 0;
            foreach (var (name, at) in EachActor(ctx).ToList())
            {
                double[] m = new double[12];
                for (int i = 0; i < 12; i++) m[i] = ctx.Data.F32(at + 4 * i);
                if (name == "StartPosition")
                {
                    int row = car / 2; double col = car % 2 == 0 ? -5.0 : 5.0, back = 8.0 * row + (car % 2 != 0 ? 4.0 : 0.0);
                    axis.CopyTo(m, 0);
                    m[9] = lx - ux * back + uz * col; m[10] = groundY; m[11] = lz - uz * back - ux * col;
                    car++;
                }
                else if (name == "StartLineInfo")
                {
                    uint kind = ctx.Data.U32(at - 12 + 92);
                    var (px, pz) = kind == 3 ? finish : line;
                    axis.CopyTo(m, 0);
                    m[9] = px; m[10] = groundY - CarAboveGround; m[11] = pz;
                }
                else { m[9] = lx; m[10] = groundY - 1000.0; m[11] = lz; }
                Put12(ctx, at, m);
            }
        };
    }

    static Action<EditCtx> PlacePickups(List<PickupSpot> spots) => ctx =>
    {
        int k = 0;
        foreach (var (name, at) in EachActor(ctx).ToList())
        {
            if (name != "PickupPosition") continue;
            var sp = spots[Math.Min(k, spots.Count - 1)];
            double y = sp.Y;
            if (k >= spots.Count) y -= ParkBelow;
            double a = sp.A, c = Math.Cos(a), s = Math.Sin(a);
            Put12(ctx, at, new[] { c, 0.0, -s, 0.0, 1.0, 0.0, s, 0.0, c, sp.X, y, sp.Z });
            if (k < spots.Count && sp.Type != null) ctx.Data.Put(at - 12 + 60, (uint)ctx.AddUid(Convert.ToUInt32(sp.Type, 16), "PickupType"));
            k++;
        }
    };

    static Action<EditCtx> PlaceAudio(List<AudioSpot> spots) => ctx =>
    {
        int k = 0;
        foreach (var (name, at) in EachActor(ctx).ToList())
        {
            if (name != "AudioActor") continue;
            double x, y, z;
            if (k < spots.Count) { x = spots[k].X; y = spots[k].Y; z = spots[k].Z; }
            else { x = spots[^1].X; y = spots[^1].Y - ParkBelow; z = spots[^1].Z; }
            ctx.Data.Put(at + 36, (float)x); ctx.Data.Put(at + 40, (float)y); ctx.Data.Put(at + 44, (float)z);
            if (k < spots.Count && spots[k].Uid != 0) ctx.Data.Put(at - 12 + 64, (uint)ctx.AddUid(spots[k].Uid, "AudioEntityProxy"));
            k++;
        }
    };

    static Action<EditCtx> PlaceTiles(List<TileSpot> tiles, uint defUid) => ctx =>
    {
        var refs = new Dictionary<uint, int>();
        foreach (uint u in new SortedSet<uint>(tiles.Select(t => t.Uid != 0 ? t.Uid : defUid))) refs[u] = ctx.AddUid(u, "DynamicObjDef");
        int k = 0;
        foreach (var (name, at) in EachActor(ctx).ToList())
        {
            if (name != "DynamicObjActor") continue;
            var t = tiles[Math.Min(k, tiles.Count - 1)];
            double y = t.Y;
            if (k >= tiles.Count) y -= ParkBelow;
            var ax = Layout.ActorAxes3(t.Turn, t.Rx, t.Pitch + t.Rz);
            var r = new List<double>(12);
            var baked = ModelImport.BakedFor(t.Uid);     // an import's model is already built at its biggest size
            for (int q = 0; q < 3; q++) foreach (double v in ax[q]) r.Add(v * t.Scale[q] / (baked?[q] ?? 1.0));
            r.Add(t.X); r.Add(y); r.Add(t.Z);
            Put12(ctx, at, r);
            int rec = at - 12;
            ctx.Data.Put(rec + 60, (uint)refs[t.Uid != 0 ? t.Uid : defUid]);
            ctx.Data.Put(rec + 80, 1u);
            k++;
        }
    };

    static Action<EditCtx> PlaceLamps(List<LampSpot> spots) => ctx =>
    {
        var refs = new Dictionary<uint, int>();
        foreach (uint u in new SortedSet<uint>(spots.Where(s => s.Uid != 0).Select(s => s.Uid))) refs[u] = ctx.AddUid(u, "ElementObjectRecord");
        int k = 0;
        foreach (var (name, at) in EachActor(ctx).ToList())
        {
            if (name != "PhysicalObjectActor") continue;
            var sp = spots[Math.Min(k, spots.Count - 1)];
            double y = sp.Y;
            if (k >= spots.Count) y -= ParkBelow;
            var ax = Layout.ActorAxes3(sp.Turn, sp.Rx, sp.Rz);
            Put12(ctx, at, new[] { ax[0][0], ax[0][1], ax[0][2], ax[1][0], ax[1][1], ax[1][2], ax[2][0], ax[2][1], ax[2][2], sp.X, y, sp.Z });
            if (sp.Uid != 0) ctx.Data.Put(at - 12 + 60, (uint)refs[sp.Uid]);
            k++;
        }
    };

    static void SwapUids(byte[] d, string uidA, string uidB)
    {
        var k = new Kslf(d);
        var (o4, n4) = k.Sections[4];
        var rows = new Dictionary<uint, int>();
        for (int i = 0; i < n4; i++) rows[B.U32(d, o4 + 8 * i)] = o4 + 8 * i;
        int a = rows[Convert.ToUInt32(uidA, 16)], b = rows[Convert.ToUInt32(uidB, 16)];
        ushort ia = B.U16(d, a + 6), ib = B.U16(d, b + 6);
        B.Put(d, a + 6, ib); B.Put(d, b + 6, ia);
        var (o5, _) = k.Sections[5];
        int ra = (a - o4) / 8, rb = (b - o4) / 8;
        B.Put(d, o5 + 16 * ia, (uint)rb); B.Put(d, o5 + 16 * ib, (uint)ra);
    }

    static Action<EditCtx> RenumberSerials(uint salt) => ctx =>
    {
        int k = 0;
        for (int bi = 0; bi < ctx.Rows.Count; bi++)
        {
            var r = ctx.Rows[bi];
            if (r[2] != 0) continue;
            var f = AllFields(ctx.Types, r[0]).FirstOrDefault(q => q.Name == "SerialisableUid");
            if (f == null) continue;
            int size = ctx.Types[r[0]].Size;
            for (int e = 0; e < r[1]; e++)
            {
                int at = ctx.Blocks[bi] + e * size + f.Offset;
                ctx.Data.Put(at, Pak.Crc32Of(Encoding.ASCII.GetBytes($"{salt:X8}:{k}")) | 0x80000000);
                k++;
            }
        }
    };

    static List<FieldDef> AllFields(List<TypeDef> types, int t)
    {
        var o = new List<FieldDef>();
        foreach (var (b, off) in types[t].Bases)
            foreach (var f in AllFields(types, b)) o.Add(new FieldDef { Name = f.Name, Type = f.Type, Offset = f.Offset + off, Flags = f.Flags });
        o.AddRange(types[t].Fields);
        return o;
    }

    /// Returns (actorpack bytes, per map {Haruna list uid as the route names it: our copy's uid})
    public static (byte[] Data, List<Dictionary<string, uint>> Lists) BuildLocation(List<MapSpec> maps)
    {
        string source = Path.Combine(Paths.Unpacked, "levels", "jpn_mharuna", "actorpack.xt");
        var data = File.ReadAllBytes(source);
        var P = new Kslf(data);
        var uid2obj = new Dictionary<string, int>();
        foreach (var (u, _, oi) in P.Refs) if (oi != 0xFFFF) uid2obj[u.ToString("X8")] = oi;
        void Edit(int obj, Action<EditCtx> fn) => data = KslfWrite.EditObject(data, obj, fn);

        var need = new SortedSet<(uint, string)>();
        foreach (var m in maps)
        {
            foreach (var t in m.Tiles) need.Add((t.Uid != 0 ? t.Uid : m.TileDefUid, "DynamicObjDef"));
            foreach (var s in m.Lamps) if (s.Uid != 0) need.Add((s.Uid, "ElementObjectRecord"));
            foreach (var s in m.Audio) if (s.Uid != 0) need.Add((s.Uid, "AudioEntityProxy"));
            foreach (var s in m.PickupSpots) if (s.Type != null) need.Add((Convert.ToUInt32(s.Type, 16), "PickupType"));
        }
        foreach (var (uid, tname) in need) data = KslfWrite.InsertUid(data, uid, tname, out _);
        var objs = new List<Dictionary<string, int>>();
        foreach (var m in maps)
        {
            var mine = new Dictionary<string, int>();
            if (m.ListUids == null || m.ListUids.Count == 0)
            {
                foreach (var r in Roles) mine[r] = uid2obj[RoleSource[r]];
                objs.Add(mine);
                continue;
            }
            for (int i = 0; i < Roles.Length; i++)
            {
                uint uid = (uint)m.ListUids[i];
                int n = new Kslf(data).Objects.Count;
                data = KslfWrite.CloneObject(data, uid2obj[RoleSource[Roles[i]]], null, uid, null, out _);
                mine[Roles[i]] = n;
                Edit(n, RenumberSerials(uid));
            }
            objs.Add(mine);
        }
        for (int i = 0; i < maps.Count; i++)
        {
            var m = maps[i]; var o = objs[i];
            var sl = m.StartLine; var fl = m.FinishLine; double a = m.StartAngle;
            var grid = MoveGrid((sl[0], sl[2]), a, sl[1], (fl[0], fl[2]));
            Edit(o["grid_sp"], grid);
            Edit(o["grid_mp"], grid);
            var pk = m.PickupSpots.Count > 0 ? m.PickupSpots : new List<PickupSpot> { new() { X = sl[0], Y = sl[1] - ParkBelow, Z = sl[2] } };
            Edit(o["pick_sp"], PlacePickups(pk));
            Edit(o["pick_mp"], PlacePickups(pk));
            Edit(o["tiles"], PlaceTiles(m.Tiles.Count > 0 ? m.Tiles : new List<TileSpot> { new() { X = sl[0], Y = sl[1] - ParkBelow, Z = sl[2] } }, m.TileDefUid));
            Edit(o["lamps"], PlaceLamps(m.Lamps.Count > 0 ? m.Lamps : new List<LampSpot> { new() { X = sl[0], Y = sl[1] - ParkBelow, Z = sl[2] } }));
            Edit(o["audio"], PlaceAudio(m.Audio.Count > 0 ? m.Audio : new List<AudioSpot> { new() { X = sl[0], Y = sl[1] - ParkBelow, Z = sl[2] } }));
        }
        SwapUids(data, TileList, TileListAs);
        SwapUids(data, TileList, LampListAs);
        var res = new List<Dictionary<string, uint>>();
        foreach (var m in maps)
        {
            var d = new Dictionary<string, uint>();
            if (m.ListUids != null && m.ListUids.Count > 0)
                for (int i = 0; i < Roles.Length; i++) d[RoleRouteUid[Roles[i]]] = (uint)m.ListUids[i];
            res.Add(d);
        }
        return (data, res);
    }
}

// The whole build: database edits, level files, gamedata pack, root pack. Port of flat_earth/build.py + build_track.py.
// Unlike the Python build this never re-parses through temp files, and the slow per-road work runs on all cores.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text;

namespace Bmb.Core;

public sealed class BuildOptions { public bool Dry; public bool NoCars; public bool NoRoot; public Action<string> Log = Console.WriteLine; }

public static class Build
{
    const uint CityUid = 0xFFFFFF20;
    const int SrcCityObj = 805, SrcRouteObj = 395, LightCityObj = 2226, CameraRouteObj = 3081, AudioVistaId = 12, UiConfigObj = 95, MapShapesField = 576;
    const string EmptyList = "6992B639", GlobalList = "60AD2B34", SrcGameModel = "1F3F42A6", SrcDynDef = "01ADFCF8", IntroCycleAliasOf = "animations/cameras/pack_lar6.cycle",
        TileModel = "models\\objects\\flat_earth\\roadtile01.model", TileModelAliasOf = "models\\objects\\usa_la_river\\misc\\dynamic_cardboardbox01.model",
        GlowSource = "\\models\\objects\\usa_la_downtown\\misc\\sign_wetfloor.model", GlowDiff = "sign_wetfloor_diff", GlowMat = "sign_wetfloor_ipr0",
        TestModelFile = "models\\objects\\USA_LA_LongBeachDocks\\containers\\ContainerDarkRed.model", LineModel = "models\\objects\\flat_earth\\startline01.model",
        WallModel = "models\\objects\\flat_earth\\edgewall01.model", ChevronModel = "models\\objects\\flat_earth\\chevron01.model", FrontendBpr = "\\ui\\data\\ui_frontend.bpr";
    const double RoadLenC = Fe.RoadLen;

    static string IntroCycleName(JObj m, string shot) => $"animations/cameras/fe_{m.Str("route_name")!.ToLowerInvariant()}_{shot}";
    static Dictionary<string, int> Objs(Kslf A)
    {
        var o = new Dictionary<string, int>();
        foreach (var (u, _, oi) in A.Refs) if (oi != 0xFFFF) o[u.ToString("X8")] = oi;
        return o;
    }
    static Dictionary<string, int> Rows(Kslf A)
    {
        var o = new Dictionary<string, int>(); int i = 0;
        foreach (var (u, _, _) in A.Refs) o[u.ToString("X8")] = i++;
        return o;
    }
    static List<Road> RoadsOf(JObj loc) => loc.Objs("maps").Select(Road.ForMap).ToList();

    static Action<EditCtx> SwapLists(Dictionary<int, int> mapping) => ctx =>
    {
        for (int bi = 0; bi < ctx.Rows.Count; bi++)
        {
            var r = ctx.Rows[bi];
            if (ctx.Types[r[0]].Name == "LevelActorList" && r[2] == 2)
                for (int e = 0; e < r[1]; e++)
                {
                    int at = ctx.Blocks[bi] + 4 * e;
                    if (mapping.TryGetValue((int)ctx.Data.U32(at), out int nw)) ctx.Data.Put(at, (uint)nw);
                }
        }
    };

    static ClonePatch FileName(string name) => (data, pool, blocks) => { data.Put(blocks[0], (uint)pool.Len); pool.AddSz(name); };
    static ClonePatch DefPatch(int modelRef) => (data, pool, blocks) =>
    {
        int b = blocks[0];
        data.Put(b, (uint)modelRef); data.Put(b + 12, (float)Fe.TileDrawDistance); data.Put(b + 24, 1u); data.Put(b + 32, 0u); data.Put(b + 68, 0u);
    };

    static byte[] ModelAndDef(byte[] amax, Dictionary<string, int> obj, uint mu, uint du, string name, bool any = true)
    {
        amax = any ? KslfWrite.CloneObjectAny(amax, obj[SrcGameModel], null, mu, FileName(name)) : KslfWrite.CloneObject(amax, obj[SrcGameModel], null, mu, FileName(name), out _);
        int row = new Kslf(amax).RowOf(mu);
        return any ? KslfWrite.CloneObjectAny(amax, obj[SrcDynDef], null, du, DefPatch(row)) : KslfWrite.CloneObject(amax, obj[SrcDynDef], null, du, DefPatch(row), out _);
    }

    static Action<EditCtx> DrawDistance(double dist) => ctx =>
    {
        for (int bi = 0; bi < ctx.Rows.Count; bi++)
        {
            var r = ctx.Rows[bi];
            if (r[2] != 0 || ctx.Types[r[0]].Name != "PhysicalObject") continue;
            int off = FieldOffset(ctx.Types, r[0], "DrawDistance")!.Value;
            for (int e = 0; e < r[1]; e++)
            {
                int at = ctx.Blocks[bi] + e * ctx.Types[r[0]].Size + off;
                float v = ctx.Data.F32(at);
                if (0 < v && v < dist) ctx.Data.Put(at, (float)dist);
            }
        }
    };

    static int? FieldOffset(List<TypeDef> types, int t, string name)
    {
        foreach (var f in types[t].Fields) if (f.Name == name) return f.Offset;
        foreach (var (b, off) in types[t].Bases) { var o = FieldOffset(types, b, name); if (o != null) return off + o; }
        return null;
    }

    static int GameModelRowOf(Kslf A, string file)
    {
        for (int i = 0; i < A.Refs.Length; i++)
        {
            var (_, t, oi) = A.Refs[i];
            if (oi != 0xFFFF && A.Types[t].Name == "GameModel" && ((string)A.Top(A.Objects[oi])["Filename"]!).Equals(file, StringComparison.OrdinalIgnoreCase)) return i;
        }
        throw new KeyNotFoundException(file);
    }

    static byte[] SharedModels(byte[] amax)
    {
        var A = new Kslf(amax);
        var obj = Objs(A);
        amax = KslfWrite.CloneObject(amax, obj[SrcGameModel], null, Fe.ModelUid, FileName(TileModel), out _);
        A = new Kslf(amax);
        amax = KslfWrite.CloneObject(amax, obj[SrcDynDef], null, Fe.TileDefUid, DefPatch(A.RowOf(Fe.ModelUid)), out _);
        int testRef = GameModelRowOf(A, TestModelFile);
        amax = KslfWrite.CloneObject(amax, obj[SrcDynDef], null, Fe.TestDefUid, DefPatch(testRef), out _);
        amax = KslfWrite.CloneObject(amax, obj[SrcGameModel], null, Fe.LineModelUid, FileName(LineModel), out _);
        amax = KslfWrite.CloneObject(amax, obj[SrcDynDef], null, Fe.LineDefUid, DefPatch(new Kslf(amax).RowOf(Fe.LineModelUid)), out _);
        foreach (var (_, src, uid) in Fe.Roadside.OrderBy(r => r.Ours))
        {
            int n = new Kslf(amax).Objects.Count;
            amax = KslfWrite.CloneObject(amax, obj[src], null, uid, null, out _);
            amax = KslfWrite.EditObject(amax, n, DrawDistance(Fe.RoadsideDrawDistance));
        }
        foreach (var b in Fe.Big.Where(b => !b.Name.StartsWith("tower_") && !b.Name.StartsWith("arch_")))
        {
            uint gm;
            if (b.GmUid == null)
            {
                amax = KslfWrite.CloneObjectAny(amax, obj[SrcGameModel], null, b.OurGm, FileName(b.File!));
                gm = b.OurGm;
            }
            else gm = Convert.ToUInt32(b.GmUid, 16);
            amax = KslfWrite.CloneObjectAny(amax, obj[SrcDynDef], null, b.DefUid, DefPatch(new Kslf(amax).RowOf(gm)));
        }
        A = new Kslf(amax);
        amax = KslfWrite.CloneObject(amax, obj[SrcDynDef], null, Fe.BlockDefUid, DefPatch(GameModelRowOf(A, TestModelFile)), out _);
        amax = KslfWrite.CloneObject(amax, obj[SrcGameModel], null, Fe.WallModelUid, FileName(WallModel), out _);
        amax = KslfWrite.CloneObject(amax, obj[SrcDynDef], null, Fe.WallDefUid, DefPatch(new Kslf(amax).RowOf(Fe.WallModelUid)), out _);
        amax = KslfWrite.CloneObject(amax, obj[SrcGameModel], null, Fe.ChevronModelUid, FileName(ChevronModel), out _);
        amax = KslfWrite.CloneObject(amax, obj[SrcDynDef], null, Fe.ChevronDefUid, DefPatch(new Kslf(amax).RowOf(Fe.ChevronModelUid)), out _);
        return amax;
    }

    static byte[] BuildAmax(byte[] amax, JObj wld, Dictionary<(int, int), Dictionary<string, uint>> listMaps)
    {
        Dictionary<int, byte[]> locEnv;
        (amax, locEnv) = Lighting.Prepare(amax, wld);
        var A = new Kslf(amax);
        var refMap = Rows(A);
        int la = A.Objects[LightCityObj].Blocks[0].Offset;
        var light = amax.AsSpan(la + 52, 80).ToArray();
        // (rows of the source city's own lists, taken here as the Python build does)
        var cityTop = A.Top(A.Objects[SrcCityObj]);
        var srcLists = new HashSet<int>();
        foreach (var k in new[] { "ActorLists", "MultiplayerActorLists" })
            foreach (Dictionary<string, object?> x in (List<object?>)cityTop[k]!)
                if ((string)x["uid"]! != GlobalList) srcLists.Add(refMap[(string)x["uid"]!]);
        var feelUids = wld.Obj("_feel_uids") ?? new JObj();
        var routeTop = A.Top(A.Objects[CameraRouteObj]);
        int CamObj(string key) => (int)(long)((Dictionary<string, object?>)routeTop[key]!)["object"]!;
        int[] camSrcs = { CamObj("VistaCameraSequence"), CamObj("PackCameraSequence"), CamObj("PackCameraSequenceMP"), CamObj("PackCameraSequenceSingle"), CamObj("PackCameraSequenceBoss") };
        var mapCams = new Dictionary<long, uint[]>();
        {
            int tCyc = A.TypeIndex("CycleResourceDef"), tSeq = A.TypeIndex("AnimatedCameraSequence");
            int fnOff = FieldOffset(A.Types, tCyc, "FilenameMinusExt")!.Value, animOff = FieldOffset(A.Types, tSeq, "Animation")!.Value, stageOff = FieldOffset(A.Types, tSeq, "StageTime")!.Value;
            int cycSrc = (int)(long)((Dictionary<string, object?>)A.Top(A.Objects[camSrcs[2]])["Animation"]!)["object"]!;
            var allMaps = wld.Objs("locations").SelectMany(l => l.Objs("maps")).ToList();
            for (int k = 0; k < allMaps.Count; k++)
            {
                var m = allMaps[k];
                var seqs = new List<(uint Seq, uint Cyc, double Stage)>();
                foreach (var (j, shot, seqSrc, stage) in new[] { (0, "vista", camSrcs[0], IntroCam.VistaStage), (1, "pack", camSrcs[2], IntroCam.PackStage) })
                {
                    uint cycUid = Fe.IntroCycleUid + 16 * (uint)k + (uint)j, seqUid = Fe.IntroCamUid + 16 * (uint)k + (uint)j;
                    string name = IntroCycleName(m, shot);
                    amax = KslfWrite.CloneObjectAny(amax, cycSrc, null, cycUid, (data, pool, blocks) => { data.Put(blocks[0] + fnOff, (uint)pool.Len); pool.AddSz(name); });
                    amax = KslfWrite.CloneObjectAny(amax, seqSrc, null, seqUid, null);
                    seqs.Add((seqUid, cycUid, stage));
                }
                mapCams[m.Int("route_uid")] = new[] { seqs[0].Seq, seqs[1].Seq, seqs[1].Seq, seqs[1].Seq, seqs[1].Seq };
                foreach (var (seqUid, cycUid, stage) in seqs)
                {
                    var kk = new Kslf(amax);
                    int seqObj = kk.ObjOf(seqUid), row = kk.RowOf(cycUid);
                    amax = KslfWrite.EditObject(amax, seqObj, ctx => { ctx.Data.Put(ctx.Blocks[0] + animOff, (uint)row); ctx.Data.Put(ctx.Blocks[0] + stageOff, (float)stage); });
                }
            }
        }
        A = new Kslf(amax);
        refMap = Rows(A);
        byte[] CamRefs(byte[] am, JObj m)
        {
            var kk = new Kslf(am);
            var o = new byte[20]; int i = 0;
            foreach (uint u in mapCams[m.Int("route_uid")]) { B.Put(o, i, (uint)kk.RowOf(u)); i += 4; }
            return o;
        }
        var locs = wld.Objs("locations").ToList();
        byte[] AddCity(byte[] am, JObj loc)
        {
            string feel = Surface.LocFeel(wld, loc);
            int? surfRow = null, surfOff = null;
            if (feelUids.ContainsKey(feel))
            {
                var kk = new Kslf(am);
                surfRow = kk.RowOf((uint)feelUids.Int(feel));
                surfOff = FieldOffset(kk.Types, kk.TypeIndex("City"), "surfaceMappings");
            }
            var (x0, x1, z0, z1) = Minimap.CityBounds(RoadsOf(loc));
            int li = locs.IndexOf(loc);
            int n = new Kslf(am).Objects.Count;
            am = KslfWrite.CloneObject(am, SrcCityObj, loc.Str("city_name"), (uint)loc.Int("city_uid"), (data, pool, blocks) =>
            {
                int b = blocks[0];
                if (surfRow != null) data.Put(b + surfOff!.Value, (uint)surfRow.Value);
                data.Put(b + 16, 15); data.Put(b + 20, (uint)loc.Int("loc_id")); data[b + 24] = 0;
                data.Put(b + 28, (uint)pool.Len); pool.AddSz("la_river");
                data.Put(b + 32, (uint)pool.Len); pool.AddSz(loc.Str("city_name")!);
                data.Put(b + 140, (float)x0); data.Put(b + 144, (float)z0); data.Put(b + 148, (float)x1); data.Put(b + 152, (float)z1);
                data.Put(b + 52, locEnv.GetValueOrDefault(li, light));
            }, out _);
            var map = new Dictionary<int, int>();
            foreach (int r in srcLists) map[r] = refMap[EmptyList];
            return KslfWrite.EditObject(am, n, SwapLists(map));
        }
        byte[] AddRoute(byte[] am, JObj loc, JObj m)
        {
            var cams = CamRefs(am, m);
            var A2 = new Kslf(am);
            int cityRef = A2.RowOf((uint)loc.Int("city_uid"));
            string rn = m.Str("route_name")!;
            int number = int.Parse(rn[(rn.LastIndexOf('_') + 1)..]);
            bool laps = m.Truthy("laps");
            int n = A2.Objects.Count;
            am = KslfWrite.CloneObject(am, SrcRouteObj, rn, (uint)m.Int("route_uid"), (data, pool, blocks) =>
            {
                int b = blocks[0];
                data.Put(b + 16, number); data.Put(b + 20, (uint)m.Int("loc_id")); data.Put(b + 24, (uint)cityRef); data.Put(b + 28, number);
                data.Put(b + 36, (uint)AudioVistaId); data.Put(b + 32, (uint)pool.Len);
                data.Put(b + 88, cams);
                if (laps) { data.Put(b + 76, 0u); data.Put(b + 84, 10); }
                pool.AddSz(rn);
            }, out _);
            return KslfWrite.EditObject(am, n, SwapLists(new()));
        }
        amax = AddCity(amax, locs[0]);
        amax = AddRoute(amax, locs[0], locs[0].Objs("maps").First());
        amax = SharedModels(amax);
        A = new Kslf(amax);
        var obj = Objs(A);
        for (int k = 0; k < Fe.TileWidths.Length; k++)
        {
            uint mu = Fe.TileVariantBase + 2 * (uint)k;
            amax = ModelAndDef(amax, obj, mu, mu + 1, Fe.TileVariantFile(Fe.TileWidths[k]), false);
        }
        foreach (var loc in locs)
            foreach (var m in loc.Objs("maps"))
            {
                var r = Road.ForMap(m);
                int cnt = RoadMesh.ModelCount(m, r);
                for (int k = 0; k < cnt; k++) { var (mu, du) = RoadMesh.Uids(m, k); amax = ModelAndDef(amax, obj, mu, du, RoadMesh.ModelFile(m, k)); }
            }
        // scenery defs
        var used = Layout.SceneryModels(wld);
        if (used.Count > 0)
        {
            A = new Kslf(amax);
            var have = A.Refs.Select(r => r.Uid).ToHashSet();
            foreach (var gm in used)
            {
                uint du = Layout.SceneryDefUid(gm);
                if (!have.Add(du)) throw new InvalidOperationException($"scenery def uid {du:X8} already used (model {gm})");
                amax = KslfWrite.CloneObjectAny(amax, obj[SrcDynDef], null, du, DefPatch(new Kslf(amax).RowOf(Convert.ToUInt32(gm, 16))));
            }
        }
        foreach (var e in ModelImport.Used(wld))
            for (int k = 0; k < e.Int("pieces"); k++)
            {
                var (mu, du) = ModelImport.Uids((int)e.Int("slot"), k);
                amax = ModelAndDef(amax, obj, mu, du, ModelImport.ModelFile((int)e.Int("slot"), k));
            }
        for (int li = 0; li < locs.Count; li++)
        {
            if (li > 0) amax = AddCity(amax, locs[li]);
            int mi = 0;
            foreach (var m in locs[li].Objs("maps")) { if (li > 0 || mi > 0) amax = AddRoute(amax, locs[li], m); mi++; }
        }
        for (int li = 0; li < locs.Count; li++)
        {
            int mi = -1;
            foreach (var m in locs[li].Objs("maps"))
            {
                mi++;
                if (!listMaps.TryGetValue((li, mi), out var mapping) || mapping.Count == 0) continue;
                A = new Kslf(amax);
                int routeObj = A.ObjOf((uint)m.Int("route_uid"));
                var refsNow = Rows(A);
                amax = KslfWrite.EditObject(amax, routeObj, ctx =>
                {
                    var nw = new Dictionary<int, int>();
                    foreach (var (old, uid) in mapping.OrderBy(kv => kv.Value)) nw[refsNow[old]] = ctx.AddUid(uid, "LevelActorList");
                    SwapLists(nw)(ctx);
                });
            }
        }
        A = new Kslf(amax);
        obj = Objs(A);
        foreach (var b in Fe.Big.Where(b => b.Name.StartsWith("tower_") || b.Name.StartsWith("arch_")))
            amax = ModelAndDef(amax, obj, b.OurGm, b.DefUid, b.File!);
        return amax;
    }

    // ---- models of the root pack ---------------------------------------------------------------------------------
    static byte[] RoadPieceModel(byte[] bas, int w, int h, double width)
    {
        var (dv, mv) = Surface.RoadTextureProfile(w, h, width);
        return CpModel.MakeProfile(bas, Fe.PieceLen, Fe.RoadProfile(width), Fe.PieceLen / 14.0, new Dictionary<string, byte[]> { [GlowDiff] = dv, [GlowMat] = mv });
    }

    static void ToStrip(List<int> strip, IEnumerable<(int, int, int)> tris)
    {
        foreach (var (a, b, c) in tris)
        {
            if (strip.Count > 0) { strip.Add(strip[^1]); strip.Add(a); if (strip.Count % 2 != 0) strip.Add(a); }
            strip.Add(a); strip.Add(b); strip.Add(c);
        }
    }

    static byte[] ChevronMesh(byte[] bas)
    {
        var tex = CpModel.Parse(bas).Textures;
        var verts = new List<double[]>(); var strip = new List<int>();
        void Quad((double, double) a, (double, double) b, (double, double) c, (double, double) d)
        {
            foreach (var order in new[] { new[] { a, b, d, c }, new[] { b, a, c, d } })
            {
                int k = verts.Count;
                foreach (var (x, y) in order) verts.Add(new[] { x, y, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0 });
                if (strip.Count > 0) { strip.Add(strip[^1]); strip.Add(k); if (strip.Count % 2 != 0) strip.Add(k); }
                strip.AddRange(new[] { k, k + 1, k + 2, k + 3 });
            }
        }
        double cell = Fe.ChevronLen / Fe.ChevronCount, y0 = Fe.ChevronLift, y1 = Fe.ChevronLift + Fe.ChevronH, ym = (y0 + y1) / 2, t = Fe.ChevronStroke;
        for (int k = 0; k < Fe.ChevronCount; k++)
        {
            double x0 = -Fe.ChevronLen / 2 + k * cell + cell * 0.15, tip = x0 + cell * 0.55;
            Quad((x0, y1), (x0 + t, y1), (tip + t, ym), (tip, ym));
            Quad((tip, ym), (tip + t, ym), (x0 + t, y0), (x0, y0));
        }
        var enc = new Dictionary<string, byte[]>();
        foreach (var (name, r, g, b) in new[] { (RoadMesh.Diff, (byte)250, (byte)250, (byte)250), (RoadMesh.Mat, (byte)Fe.RoadShine, (byte)255, (byte)0) })
        {
            var (_, w, h, mips) = tex[name];
            enc[name] = CpModel.EncodeMips(CpModel.Solid(w, h, r, g, b), w, h, mips);
        }
        return CpModel.NewTextureIds(CpModel.MakeMesh(bas, verts, strip, enc), 0x0F1A7E04);
    }

    sealed record TowerStyle(string Shape, byte[] Frame, byte[] Win, double Lit, byte[]? LitCol = null, int Cols = 4, int Rows = 4, bool Ribbon = false);
    static readonly Dictionary<string, TowerStyle> TowerStyles = new()
    {
        ["tower_a"] = new("box", new byte[] { 24, 30, 40 }, new byte[] { 52, 70, 92 }, 0.35), ["tower_b"] = new("box", new byte[] { 24, 30, 40 }, new byte[] { 52, 70, 92 }, 0.35),
        ["tower_c"] = new("box", new byte[] { 24, 30, 40 }, new byte[] { 52, 70, 92 }, 0.35), ["tower_d"] = new("box", new byte[] { 24, 30, 40 }, new byte[] { 52, 70, 92 }, 0.35),
        ["tower_e"] = new("setback", new byte[] { 28, 44, 66 }, new byte[] { 70, 110, 160 }, 0.30),
        ["tower_f"] = new("twin", new byte[] { 175, 175, 168 }, new byte[] { 40, 50, 62 }, 0.25, Ribbon: true),
        ["tower_g"] = new("spire", new byte[] { 10, 12, 16 }, new byte[] { 20, 26, 34 }, 0.30, new byte[] { 80, 230, 255 }, 8, 2),
        ["tower_h"] = new("slab", new byte[] { 120, 62, 46 }, new byte[] { 54, 48, 44 }, 0.45, null, 6, 5),
        ["tower_i"] = new("stepped", new byte[] { 30, 60, 44 }, new byte[] { 70, 130, 96 }, 0.25),
        ["tower_j"] = new("lshape", new byte[] { 150, 120, 60 }, new byte[] { 110, 92, 52 }, 0.30, Ribbon: true),
        ["tower_k"] = new("box", new byte[] { 200, 200, 205 }, new byte[] { 60, 80, 110 }, 0.30, null, 2, 6),
        ["tower_l"] = new("box", new byte[] { 90, 92, 96 }, new byte[] { 36, 40, 46 }, 0.20, Ribbon: true),
    };

    public static byte[] TowerFrame(string name) => TowerStyles.TryGetValue(name, out var st) ? st.Frame : new byte[] { 40, 51, 69 };

    public static List<(double X0, double X1, double Z0, double Z1, double Y0, double Y1)> TowerParts(string name)
    {
        var (_, wx, dz, h, _) = Fe.Towers.First(t => t.Name == name);
        double X = wx / 2, Z = dz / 2;
        switch (TowerStyles[name].Shape)
        {
            case "setback": return new() { (-X, X, -Z, Z, 0, h * .55), (-X * .75, X * .75, -Z * .75, Z * .75, h * .55, h * .82), (-X * .5, X * .5, -Z * .5, Z * .5, h * .82, h) };
            case "twin": { double g = X * .12; return new() { (-X, X, -Z, Z, 0, h * .18), (-X, -g, -Z * .85, Z * .85, h * .18, h), (g, X, -Z * .85, Z * .85, h * .18, h * .88) }; }
            case "spire": return new() { (-X, X, -Z, Z, 0, h * .78), (-X * .6, X * .6, -Z * .6, Z * .6, h * .78, h * .86), (-1.5, 1.5, -1.5, 1.5, h * .86, h) };
            case "stepped":
                return new[] { (1.0, 0.0, .35), (.8, .35, .6), (.6, .6, .82), (.4, .82, 1.0) }.Select(q => (-X * q.Item1, X * q.Item1, -Z * q.Item1, Z * q.Item1, h * q.Item2, h * q.Item3)).ToList();
            case "lshape": return new() { (-X, X, -Z, -Z * .1, 0, h), (-X, -X * .1, -Z * .1, Z, 0, h * .7) };
            default: return new() { (-X, X, -Z, Z, 0, h) };
        }
    }

    static List<KeyValuePair<string, byte[]>> TowerModels(byte[] bas)
    {
        var tex = CpModel.Parse(bas).Textures;
        var (_, tw, th, tm) = tex[RoadMesh.Diff]; var (_, mw, mh, mm) = tex[RoadMesh.Mat];
        var o = new KeyValuePair<string, byte[]>[Fe.Towers.Length];
        Parallel.For(0, Fe.Towers.Length, ti =>
        {
            var (name, _, _, _, seed) = Fe.Towers[ti];
            var st = TowerStyles[name];
            var rng = new NumpyRng(seed);
            var litv = rng.Fill(st.Rows * st.Cols);
            byte[] litCol = st.LitCol ?? new byte[] { 250, 214, 140 };
            var img = new byte[th * tw * 3]; var mat = new byte[mh * mw * 3];
            for (int i = 0; i < th * tw; i++) st.Frame.CopyTo(img, 3 * i);
            for (int i = 0; i < mh * mw; i++) mat[3 * i] = 60;
            for (int i = 0; i < st.Rows; i++)
                for (int j = 0; j < st.Cols; j++)
                {
                    bool lit = litv[i * st.Cols + j] < st.Lit;
                    double fx0 = (j + (st.Ribbon ? 0.0 : 0.125)) / st.Cols, fx1 = (j + (st.Ribbon ? 1.0 : 0.875)) / st.Cols, fy0 = (i + 0.2) / st.Rows, fy1 = (i + 0.8) / st.Rows;
                    var c = lit ? litCol : st.Win;
                    for (int y = (int)(fy0 * th); y < (int)(fy1 * th); y++) for (int x = (int)(fx0 * tw); x < (int)(fx1 * tw); x++) c.CopyTo(img, 3 * (y * tw + x));
                    if (lit) for (int y = (int)(fy0 * mh); y < (int)(fy1 * mh); y++) for (int x = (int)(fx0 * mw); x < (int)(fx1 * mw); x++) mat[3 * (y * mw + x) + 1] = 255;
                }
            var enc = new Dictionary<string, byte[]> { [RoadMesh.Diff] = CpModel.EncodeMips(img, tw, th, tm), [RoadMesh.Mat] = CpModel.EncodeMips(mat, mw, mh, mm) };
            var verts = new List<double[]>(); var tris = new List<(int, int, int)>();
            void Quad(double[] p0, double[] p1, double[] p2, double[] p3, double[] n, double uw, double vh, double v0 = 0.0)
            {
                int b = verts.Count;
                var uv = new[] { (0.0, v0 + vh), (uw, v0 + vh), (uw, v0), (0.0, v0) };
                var ps = new[] { p0, p1, p2, p3 };
                for (int k = 0; k < 4; k++) verts.Add(new[] { ps[k][0], ps[k][1], ps[k][2], n[0], n[1], n[2], uv[k].Item1, uv[k].Item2 });
                tris.Add((b, b + 2, b + 1)); tris.Add((b, b + 3, b + 2));
            }
            const double rep = 16.0;
            foreach (var (x0, x1, z0, z1, y0, y1) in TowerParts(name))
            {
                foreach (var (ax, az, bx, bz, nrm) in new[] { (x0, z0, x1, z0, new double[] { 0, 0, -1 }), (x1, z0, x1, z1, new double[] { 1, 0, 0 }), (x1, z1, x0, z1, new double[] { 0, 0, 1 }), (x0, z1, x0, z0, new double[] { -1, 0, 0 }) })
                {
                    double span = Py.Hypot(bx - ax, bz - az);
                    Quad(new[] { ax, y0, az }, new[] { bx, y0, bz }, new[] { bx, y1, bz }, new[] { ax, y1, az }, nrm, span / rep, (y1 - y0) / rep, -y1 / rep);
                }
                Quad(new[] { x0, y1, z0 }, new[] { x1, y1, z0 }, new[] { x1, y1, z1 }, new[] { x0, y1, z1 }, new double[] { 0, 1, 0 }, 0.02, 0.02);
            }
            var strip = new List<int>();
            ToStrip(strip, tris);
            o[ti] = new($"models\\objects\\flat_earth\\{name}.model", CpModel.NewTextureIds(CpModel.MakeMesh(bas, verts, strip, enc), 0x0F4D0000 + (uint)seed));
        });
        return o.ToList();
    }

    static (byte[], byte[]) ArchTexture(string text, int tw, int th, int mw, int mh)
    {
        (int, int) Band(int a, int b, int n) => (a * n / 256, b * n / 256);
        var img = new byte[th * tw * 3]; var mat = new byte[mh * mw * 3];
        for (int i = 0; i < mh * mw; i++) mat[3 * i] = 40;
        void Fill(byte[] px, int w, int y0, int y1, int x0, int x1, byte r, byte g, byte b)
        { for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { int o = 3 * (y * w + x); px[o] = r; px[o + 1] = g; px[o + 2] = b; } }
        void MatC(int y0, int y1, int x0, int x1, int c, byte v) { for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) mat[3 * (y * mw + x) + c] = v; }
        var (sy0, sy1) = Band(0, 48, th);
        Fill(img, tw, sy0, sy1, 0, tw, 14, 20, 36);
        int sh = sy1 - sy0;
        using (var bm = new Bitmap(tw, sh, PixelFormat.Format24bppRgb))
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(Color.Black);
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            float size = sh - 10;
            Font font = new("Arial", size, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = StringFormat.GenericTypographic;
            while (size > 8 && g.MeasureString(text, font, 9999, sf).Width > tw - 16)
            {
                size -= 2; font.Dispose(); font = new Font("Arial", size, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            var ms = g.MeasureString(text, font, 9999, sf);
            g.DrawString(text, font, Brushes.White, (tw - ms.Width) / 2, (sh - ms.Height) / 2, sf);
            font.Dispose();
            var letters = Pic.FromBitmap(bm);
            for (int y = 0; y < sh; y++) for (int x = 0; x < tw; x++)
                if (letters.Px[3 * (y * tw + x)] > 128) { int o = 3 * ((sy0 + y) * tw + x); img[o] = 255; img[o + 1] = 255; img[o + 2] = 255; }
            var (m0, m1) = Band(0, 48, mh);
            for (int y = m0; y < m1; y++) for (int x = 0; x < mw; x++)
                if (letters.Px[3 * (((y - m0) * sh / (m1 - m0)) * tw + x * tw / mw)] > 128) mat[3 * (y * mw + x) + 1] = 255;
            MatC(m0, m0 + 1, 0, mw, 1, 255); MatC(m1 - 1, m1, 0, mw, 1, 255);
        }
        Fill(img, tw, sy0, sy0 + 2, 0, tw, 0, 220, 255); Fill(img, tw, sy1 - 2, sy1, 0, tw, 0, 220, 255);
        var (cy0, cy1) = Band(48, 80, th);
        int sq = (cy1 - cy0) / 2;
        for (int r = 0; r < 2; r++)
            for (int k = 0; k < tw / sq; k++)
            {
                bool white = (r + k) % 2 == 0;
                Fill(img, tw, cy0 + r * sq, cy0 + (r + 1) * sq, k * sq, (k + 1) * sq, (byte)(white ? 235 : 16), (byte)(white ? 235 : 16), (byte)(white ? 235 : 16));
            }
        var (cm0, cm1) = Band(48, 80, mh);
        int msq = Math.Max((cm1 - cm0) / 2, 1);
        for (int r = 0; r < 2; r++)
            for (int k = 0; k < mw / msq; k++)
                if ((r + k) % 2 == 0) MatC(cm0 + r * msq, cm0 + (r + 1) * msq, k * msq, (k + 1) * msq, 1, 150);
        var (py0, py1) = Band(80, 160, th);
        Fill(img, tw, py0, py1, 0, tw, 70, 76, 88);
        Fill(img, tw, py0, py1, tw * 7 / 16, tw * 9 / 16, 0, 220, 255);
        var (pm0, pm1) = Band(80, 160, mh);
        MatC(pm0, pm1, mw * 7 / 16, mw * 9 / 16, 1, 255);
        MatC(pm0, pm1, 0, mw, 0, 90);
        var (by0, by1) = Band(160, 256, th);
        Fill(img, tw, by0, by1, 0, tw, 20, 24, 34);
        var (ly0, ly1) = Band(236, 244, th);
        Fill(img, tw, ly0, ly1, 0, tw, 0, 220, 255);
        var (lm0, lm1) = Band(236, 244, mh);
        MatC(lm0, Math.Max(lm1, lm0 + 1), 0, mw, 1, 255);
        return (img, mat);
    }

    static List<KeyValuePair<string, byte[]>> ArchModels(byte[] bas)
    {
        var tex = CpModel.Parse(bas).Textures;
        var (_, tw, th, tm) = tex[RoadMesh.Diff]; var (_, mw, mh, mm) = tex[RoadMesh.Mat];
        (double, double) VSign = (0.0, 48 / 256.0), VCheck = (48 / 256.0, 80 / 256.0), VPillar = (80 / 256.0, 160 / 256.0), VBeam = (160 / 256.0, 1.0);
        var o = new List<KeyValuePair<string, byte[]>>();
        foreach (var (name, text, seed) in Fe.Arches)
        {
            var (dv, mv) = ArchTexture(text, tw, th, mw, mh);
            var enc = new Dictionary<string, byte[]> { [RoadMesh.Diff] = CpModel.EncodeMips(dv, tw, th, tm), [RoadMesh.Mat] = CpModel.EncodeMips(mv, mw, mh, mm) };
            var verts = new List<double[]>(); var tris = new List<(int, int, int)>();
            void Quad(double[][] p, double[] n, (double U, double V)[] uv)
            {
                int b = verts.Count;
                for (int k = 0; k < 4; k++) verts.Add(new[] { p[k][0], p[k][1], p[k][2], n[0], n[1], n[2], uv[k].U, uv[k].V });
                foreach (var t in new[] { (b, b + 1, b + 2), (b, b + 2, b + 3) })
                {
                    var A = verts[t.Item1]; var Bb = verts[t.Item2]; var C = verts[t.Item3];
                    double cx = (Bb[1] - A[1]) * (C[2] - A[2]) - (Bb[2] - A[2]) * (C[1] - A[1]), cy = (Bb[2] - A[2]) * (C[0] - A[0]) - (Bb[0] - A[0]) * (C[2] - A[2]), cz = (Bb[0] - A[0]) * (C[1] - A[1]) - (Bb[1] - A[1]) * (C[0] - A[0]);
                    tris.Add(cx * n[0] + cy * n[1] + cz * n[2] > 0 ? t : (t.Item1, t.Item3, t.Item2));
                }
            }
            void Box(double x0, double x1, double z0, double z1, double y0, double y1, (double, double) vb, double uRep)
            {
                var (va, vz) = vb;
                foreach (var (ax, az, bx, bz, n) in new[] { (x0, z0, x1, z0, new double[] { 0, 0, -1 }), (x1, z0, x1, z1, new double[] { 1, 0, 0 }), (x1, z1, x0, z1, new double[] { 0, 0, 1 }), (x0, z1, x0, z0, new double[] { -1, 0, 0 }) })
                {
                    double uw = Py.Hypot(bx - ax, bz - az) / uRep;
                    Quad(new[] { new[] { ax, y0, az }, new[] { bx, y0, bz }, new[] { bx, y1, bz }, new[] { ax, y1, az } }, n, new[] { (0.0, vz), (uw, vz), (uw, va), (0.0, va) });
                }
                Quad(new[] { new[] { x0, y1, z0 }, new[] { x1, y1, z0 }, new[] { x1, y1, z1 }, new[] { x0, y1, z1 } }, new double[] { 0, 1, 0 }, new[] { (0.0, vz), (0.5, vz), (0.5, va), (0.0, va) });
            }
            double X = Fe.ArchW / 2, P = Fe.ArchPillar / 2, D = Fe.ArchDepth / 2;
            foreach (int sx in new[] { -1, 1 }) Box(sx * X - P, sx * X + P, -P, P, 0.0, Fe.ArchY0, VPillar, 2 * P);
            double y0b = Fe.ArchY0, y1b = Fe.ArchY1, bx0 = -X - P, bx1 = X + P;
            Quad(new[] { new[] { bx0, y1b, -D }, new[] { bx1, y1b, -D }, new[] { bx1, y1b, D }, new[] { bx0, y1b, D } }, new double[] { 0, 1, 0 }, new[] { (0.0, 1.0), (8.0, 1.0), (8.0, VBeam.Item1), (0.0, VBeam.Item1) });
            Quad(new[] { new[] { bx0, y0b, -D }, new[] { bx1, y0b, -D }, new[] { bx1, y0b, D }, new[] { bx0, y0b, D } }, new double[] { 0, -1, 0 }, new[] { (0.0, 1.0), (8.0, 1.0), (8.0, VBeam.Item1), (0.0, VBeam.Item1) });
            foreach (int sx in new[] { -1, 1 })
            {
                double xe = sx > 0 ? bx1 : bx0;
                Quad(new[] { new[] { xe, y0b, -D }, new[] { xe, y0b, D }, new[] { xe, y1b, D }, new[] { xe, y1b, -D } }, new double[] { sx, 0, 0 }, new[] { (0.0, 1.0), (1.0, 1.0), (1.0, VBeam.Item1), (0.0, VBeam.Item1) });
            }
            double h = y1b - y0b, halfSign = h * (256 / 48.0) / 2, sqM = h / 2;
            foreach (var (fz, n) in new[] { (D, new double[] { 0, 0, 1 }), (-D, new double[] { 0, 0, -1 }) })
            {
                int right = fz > 0 ? -1 : 1;
                foreach (var (xa, xb, kind) in new[] { (-right * (X + P), -right * halfSign, "check"), (-right * halfSign, right * halfSign, "sign"), (right * halfSign, right * (X + P), "check") })
                {
                    (double, double)[] uv;
                    if (kind == "sign") uv = new[] { (0.0, VSign.Item2), (1.0, VSign.Item2), (1.0, VSign.Item1), (0.0, VSign.Item1) };
                    else { double uw = Math.Abs(xb - xa) / (sqM * 16 / 1); uv = new[] { (0.0, VCheck.Item2), (uw, VCheck.Item2), (uw, VCheck.Item1), (0.0, VCheck.Item1) }; }
                    Quad(new[] { new[] { xa, y0b, fz }, new[] { xb, y0b, fz }, new[] { xb, y1b, fz }, new[] { xa, y1b, fz } }, n, uv);
                }
            }
            var strip = new List<int>();
            ToStrip(strip, tris);
            o.Add(new($"models\\objects\\flat_earth\\{name}.model", CpModel.NewTextureIds(CpModel.MakeMesh(bas, verts, strip, enc), 0x0F4E0000 + (uint)seed)));
        }
        return o;
    }

    static List<KeyValuePair<string, byte[]>> RoadChunkFiles(byte[] bas, JObj wld, Action<string> log)
    {
        var tex = CpModel.Parse(bas).Textures;
        var (_, dw, dh, dm) = tex[RoadMesh.Diff]; var (_, mw, mh, mm) = tex[RoadMesh.Mat];
        var files = new List<KeyValuePair<string, byte[]>>();
        foreach (var loc in wld.Objs("locations"))
            foreach (var m in loc.Objs("maps"))
            {
                int k = 0;
                var main = Road.ForMap(m);
                var net = RoadMesh.NetworkChunks(m, main);
                var ss = Surface.Settings(m);
                int variants = ss.Variety ? 3 : 1;
                var jobs = new List<(string Name, Func<byte[]> Make)>();
                for (int ri = 0; ri < net.Count; ri++)
                {
                    var nc = net[ri];
                    var encs = new List<Dictionary<string, byte[]>>();
                    for (int v = 0; v < variants; v++)
                    {
                        var pd = nc.Kind == "divider" ? Surface.RoadTextureProfile(dw, dh, nc.Width) : Surface.RoadTexture(dw, dh, nc.Width, m, v);
                        var pm = dw == mw && dh == mh ? pd : nc.Kind == "divider" ? Surface.RoadTextureProfile(mw, mh, nc.Width) : Surface.RoadTexture(mw, mh, nc.Width, m, v);
                        encs.Add(new() { [RoadMesh.Diff] = CpModel.EncodeMips(pd.Diff, dw, dh, dm), [RoadMesh.Mat] = CpModel.EncodeMips(pm.Mat, mw, mh, mm) });
                    }
                    for (int ci = 0; ci < nc.Cs.Count; ci++)
                    {
                        var c = nc.Cs[ci]; int var = ci % encs.Count, rr = ri;
                        jobs.Add((RoadMesh.ModelFile(m, k), () => CpModel.NewTextureIds(CpModel.MakeMesh(bas, c.Verts, c.Strip, encs[var]), RoadMesh.TextureSalt(m, rr) ^ ((uint)var << 20))));
                        k++;
                    }
                }
                var over = RoadMesh.OverlayChunks(m, main);
                var encOf = new Dictionary<string, Dictionary<string, byte[]>>();
                foreach (var c in over)
                {
                    if (!encOf.ContainsKey(c.Key))
                    {
                        var pd = Surface.MaterialTexture(c.Key, dw, dh); var pm = dw == mw && dh == mh ? pd : Surface.MaterialTexture(c.Key, mw, mh);
                        encOf[c.Key] = new() { [RoadMesh.Diff] = CpModel.EncodeMips(pd.Diff, dw, dh, dm), [RoadMesh.Mat] = CpModel.EncodeMips(pm.Mat, mw, mh, mm) };
                    }
                    var cc = c;
                    jobs.Add((RoadMesh.ModelFile(m, k), () => CpModel.NewTextureIds(CpModel.MakeMesh(bas, cc.Verts, cc.Strip, encOf[cc.Key]), Surface.TextureSalt(m, cc.Key))));
                    k++;
                }
                var built = new byte[jobs.Count][];
                Parallel.For(0, jobs.Count, i => built[i] = jobs[i].Make());
                for (int i = 0; i < jobs.Count; i++) files.Add(new(jobs[i].Name, built[i]));
                if (over.Count > 0) log($"{m.Str("name")}: painted surfaces = {over.Count} model(s) ({string.Join(", ", over.Select(c => c.Key).Distinct().OrderBy(s => s, StringComparer.Ordinal))})");
                var allc = net.SelectMany(n => n.Cs).ToList();
                log($"{m.Str("name")}: seamless road = {allc.Count} models ({net.Count} road(s); {allc.Max(c => c.Verts.Count)} vertices, {allc.Max(c => c.Strip.Count)} indices in the biggest)");
            }
        return files;
    }

    static byte[] ThumbnailRgb(JObj loc, JObj m)
    {
        var r = Road.ForMap(m);
        var net = Network.Get(m, r);
        const int W = 512, H = 256;
        using var bm = new Bitmap(W, H);
        using var d = Graphics.FromImage(bm);
        d.Clear(Color.FromArgb(14, 18, 28));
        using (var p = new Pen(Color.FromArgb(20, 26, 40))) for (int k = 0; k < H; k += 16) d.DrawLine(p, 0, k, W, k);
        var bb = new[] { r }.Concat(net.Branches.Select(b => b.R)).Select(q => q.Bounds(r.Width)).ToList();
        double x0 = bb.Min(q => q.X0), x1 = bb.Max(q => q.X1), z0 = bb.Min(q => q.Z0), z1 = bb.Max(q => q.Z1);
        double sc = Math.Min((W - 40) / Math.Max(x1 - x0, 1), (H - 70) / Math.Max(z1 - z0, 1));
        double ox = (W - (x1 - x0) * sc) / 2, oz = 50 + (H - 70 - (z1 - z0) * sc) / 2;
        PointF P(double x, double z) => new((float)(ox + (x - x0) * sc), (float)(oz + (z1 - z) * sc));
        var pts = r.Centre.Select(c => P(c.X, c.Z)).ToList();
        if (r.Closed) pts.Add(pts[0]);
        int wpx = Math.Max(3, (int)(r.Width * sc));
        var bps = net.Branches.Select(b => (b.R.Centre.Select(c => P(c.X, c.Z)).ToArray(), Math.Max(3, (int)(b.R.Width * sc)))).ToList();
        Pen Pn(Color c, int w) => new(c, w) { LineJoin = LineJoin.Round };
        foreach (var (bp, bw) in bps) { using var q = Pn(Color.FromArgb(0, 140, 170), bw + 4); d.DrawLines(q, bp); }
        { using var q = Pn(Color.FromArgb(0, 140, 170), wpx + 4); d.DrawLines(q, pts.ToArray()); }
        foreach (var (bp, bw) in bps) { using var q = Pn(Color.FromArgb(70, 74, 82), bw); d.DrawLines(q, bp); }
        { using var q = Pn(Color.FromArgb(70, 74, 82), wpx); d.DrawLines(q, pts.ToArray()); }
        foreach (var dv in net.Dividers) { using var q = Pn(Color.FromArgb(255, 176, 32), 2); d.DrawLines(q, dv.Centre.Select(c => P(c.X, c.Z)).ToArray()); }
        using (var br = new SolidBrush(Color.FromArgb(255, 200, 60)))
            for (int i = 0; i < r.Height.Count; i += 3) if (r.PlainH(i) > 0.5) d.FillEllipse(br, pts[i].X - 2, pts[i].Y - 2, 4, 4);
        using (var br = new SolidBrush(Color.FromArgb(60, 230, 90))) d.FillEllipse(br, pts[0].X - 6, pts[0].Y - 6, 12, 12);
        d.FillEllipse(Brushes.White, pts[^1].X - 6, pts[^1].Y - 6, 12, 12);
        using var big = new Font("Arial", 26, FontStyle.Bold, GraphicsUnit.Pixel); using var small = new Font("Arial", 16, GraphicsUnit.Pixel);
        d.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        d.DrawString(m.Str("name"), big, Brushes.White, 14, 8);
        using (var br = new SolidBrush(Color.FromArgb(120, 200, 230))) d.DrawString($"{loc.Str("name")}  -  {r.Length / 1000:0.0} km", small, br, 16, 36);
        return Pic.FromBitmap(bm).Px;
    }

    static byte[] ThumbnailDxt(Pic img)
    {
        var o = new ByteBuf(81920);
        foreach (var (lw, lh) in new[] { (512, 256), (256, 128) }) o.Add(Dxt.Dxt1Encode(img.ResizeLanczos(lw, lh).Px, lw, lh));
        return o.ToArray();
    }

    public static string BuildRoot(JObj wld, BuildOptions opt)
    {
        var log = opt.Log;
        using var src = new Pak(Paths.RootOriginal);
        var bas = src.Read(GlowSource)!;
        var tex = CpModel.Parse(bas).Textures;
        var (_, w, h, _) = tex[GlowDiff];
        var meshBase = src.Read(RoadMesh.Source)!;
        var files = new List<KeyValuePair<string, byte[]>>();
        void Add(string n, byte[] d) => files.Add(new(n, d));
        var par = new byte[3 + Fe.TileWidths.Length][];
        var chk = new byte[w * h * 3];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { byte v = (byte)(((x / 8 + y / 8) % 2) != 0 ? 235 : 20); int o = 3 * (y * w + x); chk[o] = v; chk[o + 1] = v; chk[o + 2] = v; }
        var tasks = new List<Action>
        {
            () => par[0] = CpModel.NewTextureIds(RoadPieceModel(bas, w, h, Fe.RoadWidth), 0x0F1A7E01),
            () => par[1] = CpModel.NewTextureIds(CpModel.MakeQuad(bas, Fe.LineW, Fe.LineLen, 1, 21, new Dictionary<string, byte[]> { [GlowDiff] = chk, [GlowMat] = CpModel.Solid(w, h, Fe.RoadShine, Fe.LineGlow, 0) }), 0x0F1A7E02),
            () => par[2] = CpModel.NewTextureIds(CpModel.MakeQuad(bas, RoadLenC, Fe.WallH, RoadLenC / 4, 1, new Dictionary<string, byte[]> { [GlowDiff] = CpModel.Solid(w, h, Fe.WallColour[0], Fe.WallColour[1], Fe.WallColour[2]), [GlowMat] = CpModel.Solid(w, h, Fe.RoadShine, Fe.WallGlow, 0) }, true), 0x0F1A7E03),
        };
        for (int k = 0; k < Fe.TileWidths.Length; k++)
        {
            int kk = k;
            tasks.Add(() => par[3 + kk] = CpModel.NewTextureIds(RoadPieceModel(bas, w, h, Fe.TileWidths[kk]), 0x0F1A7E10 + (uint)kk));
        }
        Parallel.Invoke(tasks.ToArray());
        Add(TileModel, par[0]); Add(LineModel, par[1]); Add(WallModel, par[2]); Add(ChevronModel, ChevronMesh(meshBase));
        files.AddRange(TowerModels(meshBase));
        files.AddRange(ArchModels(meshBase));
        for (int k = 0; k < Fe.TileWidths.Length; k++) Add(Fe.TileVariantFile(Fe.TileWidths[k]), par[3 + k]);
        files.AddRange(RoadChunkFiles(meshBase, wld, log));
        files.AddRange(ModelImport.BuildFiles(meshBase, wld, log));
        string tmp = Paths.RootOut + "." + Environment.ProcessId + ".tmp";
        var srcIpf = src.Read(Minimap.SrcGameIpf)!; var srcTex = src.Read(Minimap.SrcTexture)!;
        var aliases = files.Select(f => new KeyValuePair<string, string>(f.Key, TileModelAliasOf)).ToList();
        foreach (var loc in wld.Objs("locations"))
        {
            var maps = loc.Objs("maps").ToList();
            var roads = maps.Select(Road.ForMap).ToList();
            var b = Minimap.CityBounds(roads);
            var routes = new List<(string, Road, double, List<List<(double U, double V)>>?)>();
            for (int i = 0; i < maps.Count; i++)
            {
                var m = maps[i]; var r = roads[i];
                List<List<(double U, double V)>>? own = null;
                var mm = m.Obj("minimap") ?? new JObj();
                if (mm.ContainsKey("auto") && !mm.Truthy("auto") && mm.Truthy("image") && File.Exists(mm.Str("image")))
                {
                    own = Minimap.ImagePolys(b, mm.Str("image")!);
                    log($"minimap of {m.Str("name")}: own picture {mm.Str("image")} ({own.Count} shapes)");
                }
                var branches = Network.Get(m, r).Branches.Select(x => x.R).ToList();
                if (own == null && branches.Count > 0)
                {
                    own = Minimap.RoutePolys(b, r);
                    foreach (var br in branches) own.AddRange(Minimap.RoutePolys(b, br));
                }
                routes.Add((m.Str("route_name")!, r, r.Dist[GenAid.RaceLines(r, m).Si], own));
            }
            var pic = roads.Concat(maps.Zip(roads).SelectMany(q => Network.Get(q.First, q.Second).Branches.Select(x => x.R)));
            string city = loc.Str("city_name")!;
            foreach (var (name, data, like) in new[] { (Minimap.GameIpfName(city), Minimap.GameIpf(srcIpf, b, routes), Minimap.SrcGameIpf), (Minimap.TextureName(city), Minimap.Texture(srcTex, Minimap.Picture(b, pic)), Minimap.SrcTexture) })
            {
                Add(name, data);
                if (src.Find(name) == null) aliases.Add(new(name, like));
            }
        }
        foreach (var loc in wld.Objs("locations"))
            foreach (var m in loc.Objs("maps"))
            {
                var r = Road.ForMap(m);
                double s0 = r.Dist[GenAid.RaceLines(r, m).Si];
                foreach (var (shot, data) in IntroCam.CyclesForMap(r, s0, Fe.GroundY, m))
                {
                    string name = IntroCycleName(m, shot) + ".cycle";
                    Add(name, data);
                    aliases.Add(new(name, IntroCycleAliasOf));
                }
            }
        if (!opt.NoCars)
        {
            aliases.AddRange(CarBuild.ModelAliases(wld));
            files.AddRange(CarBuild.ModelFiles(wld, src, log));
            files.AddRange(CarBuild.IconFiles(wld, src, log));
        }
        var (skyFiles, skyAliases) = Lighting.SkyFiles(wld, Paths.Tracks, n => src.Read(n)!, log);
        files.AddRange(skyFiles); aliases.AddRange(skyAliases);
        Pak.AddAliases(Paths.RootOriginal, tmp, aliases);
        var frontend = src.Read(FrontendBpr)!;
        int li = 0;
        string thumbPng = Path.Combine(Paths.Tracks, "flat_earth_1_thumbnail.png");
        foreach (var loc in wld.Objs("locations"))
        {
            int mi = 0;
            foreach (var m in loc.Objs("maps"))
            {
                string? pic = m.Str("picture");
                if (string.IsNullOrEmpty(pic) && li == 0 && mi == 0 && File.Exists(thumbPng)) pic = thumbPng;
                byte[] dxt = !string.IsNullOrEmpty(pic) && File.Exists(pic) ? ThumbnailDxt(Pic.Load(pic)) : ThumbnailDxt(new Pic(512, 256, ThumbnailRgb(loc, m)));
                frontend = Bpr.SetPicture(frontend, m.Str("route_name")!, dxt);
                mi++;
            }
            li++;
        }
        if (!opt.NoCars) frontend = CarBuild.MenuPictures(frontend, wld);
        Add(FrontendBpr, frontend);
        files.AddRange(Skins.BuildFiles(files, n => src.Read(n)!, log));      // the player's car skins, over whatever the build changed in those models
        // a later entry replaces an earlier one of the same name (Python's dict)
        var uniq = new Dictionary<string, byte[]>();
        foreach (var (k, v) in files) uniq[k] = v;
        Pak.ReplaceFiles(tmp, Paths.RootOut, uniq.ToList(), false);
        File.Delete(tmp);
        log($"root pack: {Paths.RootOut} ({uniq.Count - 1} files, {wld.Objs("locations").Sum(l => l.Objs("maps").Count())} menu pictures, minimaps for {wld.Objs("locations").Count()} location(s))");
        return Paths.RootOut;
    }

    static byte[] AddMapShapes(byte[] ui, IEnumerable<string> names) => KslfWrite.EditObject(ui, UiConfigObj, ctx =>
    {
        var d = ctx.Data;
        int blk = B.U16(d.A, MapShapesField), first = B.U16(d.A, MapShapesField + 2); int cnt = (int)B.U32(d.A, MapShapesField + 4);
        if (ctx.Rows[blk][1] != first + cnt) throw new InvalidOperationException("MapShapes is not the last list of its block");
        var have = new HashSet<string>();
        for (int k = 0; k < cnt; k++) have.Add(B.Sz(ctx.Pool.Span, (int)d.U32(ctx.Blocks[blk] + 4 * (first + k))).ToLowerInvariant());
        var nw = new ByteBuf();
        foreach (var n in names)
            if (have.Add(n.ToLowerInvariant())) { nw.AddU32((uint)ctx.Pool.Len); ctx.Pool.AddSz(n.ToLowerInvariant()); }
        if (nw.Len > 0)
        {
            ctx.PoolChanged = true;
            ctx.Insert(blk, first + cnt, nw.Span);
            d.Put(MapShapesField, (ushort)blk); d.Put(MapShapesField + 2, (ushort)first); d.Put(MapShapesField + 4, (uint)(cnt + nw.Len / 4));
        }
    });

    static byte[] BuildMultiplayer(byte[] mp, JObj wld)
    {
        var uids = wld.Objs("locations").SelectMany(l => l.Objs("maps")).Select(m => (uint)m.Int("route_uid")).ToList();
        return KslfWrite.EditObject(mp, 585, c =>
        {
            const int RoutesF = 160, Arenas = 168;
            var d = c.Data;
            int blk = B.U16(d.A, RoutesF), first = B.U16(d.A, RoutesF + 2), cnt = (int)B.U32(d.A, RoutesF + 4);
            int ablk = B.U16(d.A, Arenas), afirst = B.U16(d.A, Arenas + 2);
            var refs = new ByteBuf();
            foreach (uint u in uids) refs.AddU32((uint)c.AddUid(u, "Route"));
            c.Insert(blk, first + cnt, refs.Span);
            d.Put(RoutesF + 4, (uint)(cnt + uids.Count));
            d.Put(Arenas, (ushort)ablk); d.Put(Arenas + 2, (ushort)(afirst + uids.Count));
        });
    }

    static (Dictionary<int, byte[]>, Dictionary<(int, int), byte[]>, Dictionary<(int, int), Dictionary<string, uint>>) BuildLevelFiles(JObj wld, BuildOptions opt)
    {
        var log = opt.Log;
        var packs = new Dictionary<int, byte[]>(); var aids = new Dictionary<(int, int), byte[]>(); var lists = new Dictionary<(int, int), Dictionary<string, uint>>();
        int li = -1;
        foreach (var loc in wld.Objs("locations"))
        {
            li++;
            string work = Path.Combine(Paths.FeWork, loc.Str("folder")!);
            Directory.CreateDirectory(work);
            var specs = new List<MapSpec>(); var pieces = new List<Piece>(); var decks = new List<Piece>(); var patches = new List<(Piece, int)>();
            (double X0, double X1, double Z0, double Z1)? bounds = null;
            int mi = -1;
            foreach (var m in loc.Objs("maps"))
            {
                mi++;
                var road = Road.ForMap(m);
                var lines = GenAid.RaceLines(road, m);
                var net = Network.Get(m, road);
                foreach (var p in net.Problems) log($"{loc.Str("name")} / {m.Str("name")}: {p}");
                var alts = net.AltRoutes(Fe.GroundY); var avoid = net.BusyRanges();
                var (_, info) = GenAid.BuildPath(road, Fe.GroundY, null, lines, alts, avoid, null);
                var corridor = Layout.AiCorridor(road, info, m);
                alts = net.AltRoutes(Fe.GroundY, corridor);
                var band = net.LogicBand(alts);
                var (dAid, info2) = GenAid.BuildPath(road, Fe.GroundY, corridor, lines, alts, avoid, band);
                info = info2;
                if (alts.Count > 0) log($"{loc.Str("name")} / {m.Str("name")}: {net.Branches.Count} branch(es), {net.Splits.Count} lane split(s) -> {alts.Count} alternative AI route(s)");
                aids[(li, mi)] = dAid;
                File.WriteAllBytes(Path.Combine(work, m.Str("route_name")!.ToLowerInvariant() + ".aid"), dAid);
                var pickups = Layout.PickupSpots(road, info, m).Spots;
                specs.Add(new MapSpec
                {
                    StartLine = info.StartLine, FinishLine = info.FinishLine, StartAngle = info.StartAngle, Width = road.Width, Tiles = Layout.TileSpots(road, info, m), TileDefUid = Fe.TileDefUid,
                    Lamps = Layout.LampSpots(road, info, m), Audio = Layout.AudioSpots(road, info), PickupSpots = pickups, ListUids = m.Arr("list_uids")?.Select(Json.L).ToList(),
                });
                var (p2, dk) = Layout.CollisionPieces(road, info, m);
                pieces.AddRange(p2); decks.AddRange(dk);
                var sp = Surface.PhysicsPatches(m, net);
                patches.AddRange(sp);
                if (sp.Count > 0)
                    log($"{loc.Str("name")} / {m.Str("name")}: surface patches " + string.Join(", ", sp.GroupBy(q => q.Mat).OrderBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}")));
                var b = road.Bounds(1500.0);
                bounds = bounds == null ? b : (Math.Min(bounds.Value.X0, b.X0), Math.Max(bounds.Value.X1, b.X1), Math.Min(bounds.Value.Z0, b.Z0), Math.Max(bounds.Value.Z1, b.Z1));
                log($"{loc.Str("name")} / {m.Str("name")}: road {road.Length:0} m ({info.Points} points, tightest turn {road.MinRadius():0} m, {road.Crossings.Count} bridge(s), steepest {100 * road.MaxGrade():0} %), {pickups.Count} power-ups");
            }
            var (pack, mapsLists) = GenActorPack.BuildLocation(specs);
            for (int i = 0; i < mapsLists.Count; i++) if (mapsLists[i].Count > 0) lists[(li, i)] = mapsLists[i];
            packs[li] = pack;
            File.WriteAllBytes(Path.Combine(work, "actorpack.xt"), pack);
            string gameDir = opt.Dry ? Path.Combine(work, "level_folder") : Path.Combine(Paths.Levels, loc.Str("folder")!);
            Directory.CreateDirectory(gameDir);
            if (!opt.Dry)
            {
                foreach (string f in new[] { "level.level", "level.levelrender", "level.levelstream" })
                {
                    string dst = Path.Combine(gameDir, f);
                    if (!File.Exists(dst)) File.Copy(Path.Combine(Paths.LevelTemplate, f), dst);
                }
                var lv = Lighting.Effective(loc).Values;
                if (lv.Truthy("object_light"))
                {
                    var roads = RoadsOf(loc);
                    string f = Path.Combine(gameDir, "level.levelrender");
                    var d = File.ReadAllBytes(f);
                    var nw = LevelLight.Write(d, lv, roads, Fe.GroundY);
                    if (nw != null) { File.WriteAllBytes(f, nw); log($"{loc.Str("name")}: object light -> level light points along the roads"); }
                }
            }
            var frame = File.ReadAllBytes(Path.Combine(Paths.Levels, "showroom", "Level.levelphysics"));
            var data = Physics.BuildFlat(frame, Fe.GroundY, bounds!.Value.X0, bounds.Value.X1, bounds.Value.Z0, bounds.Value.Z1, null, pieces, decks, patches);
            File.WriteAllBytes(Path.Combine(work, "level.levelphysics"), data);
            if (opt.Dry) log("collision floor (dry run): " + Path.Combine(work, "level.levelphysics"));
            else
            {
                try { File.WriteAllBytes(Path.Combine(gameDir, "level.levelphysics"), data); log($"collision floor: {pieces.Count} solid pieces + {decks.Count} bridge decks -> {gameDir}"); }
                catch (IOException e) { log($"collision floor written to {work}, but NOT copied into the game ({e.Message}) - close Blur and build again"); }
            }
        }
        return (packs, aids, lists);
    }

    /// the gamedata pack + (unless NoRoot) the root pack, into Paths.Work
    public static void Run(BuildOptions opt)
    {
        var log = opt.Log;
        Directory.CreateDirectory(Paths.FeWork);
        var wld = World.AssignIds(World.Load());
        string U = Paths.Unpacked;
        byte[] R(params string[] p) => File.ReadAllBytes(Path.Combine(new[] { U }.Concat(p).ToArray()));
        var f = new Dictionary<string, byte[]>
        {
            ["gamedata/xt/amax.bin"] = R("xt", "amax.bin"), ["gamedata/xt/multiplayer.bin"] = R("xt", "multiplayer.bin"),
            ["gamedata/xt/loc/cityandroutenames.loc"] = R("xt", "loc", "cityandroutenames.loc"),
        };
        var aliases = new List<KeyValuePair<string, string>>();
        var (packs, aids, lists) = BuildLevelFiles(wld, opt);
        var locs = wld.Objs("locations").ToList();
        log($"database: {locs.Count} location(s), {locs.Sum(l => l.Objs("maps").Count())} map(s)");
        var rp = R("xt", "racingphysics.bin");
        var feelUids = new JObj();
        foreach (var feel in locs.Select(lc => Surface.LocFeel(wld, lc)).Distinct().OrderBy(s => s, StringComparer.Ordinal))
        {
            uint? uid; (rp, uid) = Surface.FeelTable(rp, feel);
            if (uid != null) { feelUids[feel] = (long)uid.Value; log($"surface grip: {feel} (our own grip table {uid:X8})"); }
        }
        wld["_feel_uids"] = feelUids;
        if (feelUids.Count > 0) f["gamedata/xt/racingphysics.bin"] = rp;
        var amax = f["gamedata/xt/amax.bin"];
        foreach (var kv in feelUids.OrderBy(kv => (long)kv.Value!)) amax = KslfWrite.InsertUid(amax, (uint)(long)kv.Value!, "RP_SurfaceMappingList", out _);
        f["gamedata/xt/amax.bin"] = BuildAmax(amax, wld, lists);
        f["gamedata/xt/multiplayer.bin"] = BuildMultiplayer(f["gamedata/xt/multiplayer.bin"], wld);
        var names = new List<(uint, string, string)>();
        foreach (var lc in locs)
        {
            names.Add(((uint)lc.Int("loc_id"), "CRN_" + lc.Str("city_name")!.ToUpperInvariant(), lc.Str("name")!));
            foreach (var m in lc.Objs("maps")) names.Add(((uint)m.Int("loc_id"), "CRN_" + m.Str("route_name")!.ToUpperInvariant(), m.Str("name")!));
        }
        f["gamedata/xt/loc/cityandroutenames.loc"] = Loc.Write(f["gamedata/xt/loc/cityandroutenames.loc"], names, null);
        if (!opt.NoCars) CarBuild.AddCars(f, wld, log);
        f["gamedata/xt/ui.bin"] = AddMapShapes(R("xt", "ui.bin"), locs.Select(lc => lc.Str("city_name")!.ToLowerInvariant()));
        f["gamedata/xt/loc/common.loc"] = Loc.Write(R("xt", "loc", "common.loc"), null, null);
        int li = 0;
        foreach (var l in locs)
        {
            string folder = l.Str("folder")!;
            string ap = $"gamedata/levels/{folder}/actorpack.xt";
            aliases.Add(new(ap, "gamedata/levels/showroom/actorpack.xt"));
            f[ap] = packs[li];
            int mi = 0;
            foreach (var m in l.Objs("maps"))
            {
                string ai = $"gamedata/levels/{folder}/{m.Str("route_name")!.ToLowerInvariant()}.aid";
                aliases.Add(new(ai, "gamedata/levels/jpn_mharuna/jpn_mharuna_1.aid"));
                f[ai] = aids[(li, mi)];
                mi++;
            }
            li++;
        }
        log("writing the pack");
        string tmp = Paths.GamedataOut + "." + Environment.ProcessId + ".tmp";
        Pak.AddAliases(Paths.GamedataOriginal, tmp, aliases);
        Pak.ReplaceFiles(tmp, Paths.GamedataOut, f.ToList());
        File.Delete(tmp);
        log("built " + Paths.GamedataOut);
        if (!opt.NoRoot) BuildRoot(wld, opt);
    }
}




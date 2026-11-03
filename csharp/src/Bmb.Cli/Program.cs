// Test / compare tool. Each command prints JSON or hashes that tools\compare_*.py checks against the Python program.
using System.Diagnostics;
using System.Security.Cryptography;
using Bmb.Core;

static string Sha(ReadOnlySpan<byte> d) => Convert.ToHexString(SHA1.HashData(d)).ToLowerInvariant();

string cmd = args.Length > 0 ? args[0] : "";
var sw = Stopwatch.StartNew();
switch (cmd)
{
    case "pak-list":        // pak-list FILE: name, size, crc, sha1 of the unpacked data, one per line
    {
        using var p = new Pak(args[1]);
        var files = p.Files.Where(f => f.Offset != Pak.NoData).ToArray();
        var lines = new string[files.Length];
        Parallel.For(0, files.Length, i =>
        {
            var e = files[i];
            lines[i] = $"{e.Name}\t{e.Size}\t{e.Crc:X8}\t{Sha(p.Read(e))}";
        });
        File.WriteAllLines(args[2], lines);
        Console.Error.WriteLine($"{files.Length} files, {sw.ElapsedMilliseconds} ms");
        break;
    }
    case "kslf-dump":       // kslf-dump FILE OUT: every object as JSON
    {
        var k = new Kslf(File.ReadAllBytes(args[1]));
        var o = new List<object?>();
        foreach (var ob in k.Objects)
        {
            var row = new JObj { ["type"] = ob.Blocks[0].Name, ["value"] = ToJ(k.Top(ob)) };
            o.Add(row);
        }
        File.WriteAllText(args[2], Json.Dumps(o, 0));
        Console.Error.WriteLine($"{o.Count} objects, {sw.ElapsedMilliseconds} ms");
        break;
    }
    case "model-parse":     // model-parse PAK OUT: layout of every model
    {
        using var p = new Pak(args[1]);
        var files = p.Files.Where(f => f.Name.EndsWith(".model", StringComparison.OrdinalIgnoreCase) && f.Offset != Pak.NoData).ToArray();
        var lines = new string[files.Length];
        Parallel.For(0, files.Length, i =>
        {
            try
            {
                var M = CpModel.Parse(p.Read(files[i]));
                lines[i] = files[i].Name + "\t" + Json.Dumps(new JObj
                {
                    ["vstart"] = (long)M.VStart, ["vcount"] = (long)M.VCount, ["stride"] = (long)M.Stride, ["fstart"] = (long)M.FStart, ["fcount"] = (long)M.FCount,
                    ["elements"] = M.Elements.Select(e => (object?)new List<object?> { (long)e.Type, (long)e.Channel, (long)e.Off }).ToList(),
                    ["meshes"] = M.Meshes.Select(e => (object?)new List<object?> { (long)e.FaceOff, (long)e.FaceCount, (long)e.Type }).ToList(),
                    ["bbox"] = M.BBox.Select(x => (object?)x).ToList(),
                }, -1);
            }
            catch (Exception ex) { lines[i] = files[i].Name + "\tERR " + ex.GetType().Name; }
        });
        File.WriteAllLines(args[2], lines);
        Console.Error.WriteLine($"{files.Length} models, {sw.ElapsedMilliseconds} ms");
        break;
    }
    case "json-roundtrip":  // json-roundtrip IN OUT
        File.WriteAllText(args[2], Json.Dumps(Json.Load(args[1]), 1));
        break;
    case "random":          // random SEED: 20 numbers
    {
        var r = new PyRandom(long.Parse(args[1]));
        for (int i = 0; i < 10; i++) Console.WriteLine(Json.FloatRepr(r.Random()));
        for (int i = 0; i < 10; i++) Console.WriteLine(r.RandInt(-40, 1000));
        break;
    }
    case "dxt":             // dxt WIDTH HEIGHT SEED: hash of an encoded noise picture
    {
        int w = int.Parse(args[1]), h = int.Parse(args[2]);
        var px = CpModel.Noise(w, h, 100, 60, int.Parse(args[3]));
        var enc = Dxt.Dxt1Encode(px, w, h);
        Console.WriteLine(Sha(enc) + " " + Sha(Dxt.Dxt1Decode(enc, w, h)) + " " + Sha(CpModel.EncodeMips(px, w, h, 3)));
        break;
    }
    case "phys":            // phys FILE: mesh counts, then a flat build's hash
    {
        var d = File.ReadAllBytes(args[1]);
        var m = Physics.ReadMesh(d);
        Console.WriteLine($"{m.Version} {m.NVerts} {m.Nodes.Count} {m.TrisRaw.Count} {m.Groups.Count} {m.StreamPos}");
        var o = Physics.BuildFlat(d, 1.0, -500, 500, -400, 400,
            new[] { new double[] { 0, 10, 1, 4, 0, 20 }, new double[] { 30, 44, 1, 4, 30, 33 } },
            new[] { Physics.TurnedBox(5, 6, 1, 3, 4, 2, 0.7), Physics.WallStrip(new[] { (0.0, 0.0), (5.0, 1.0), (9.0, 4.0) }, _ => 1.0, _ => 3.5, new[] { (0.0, 1.0), (0.2, 0.98), (0.5, 0.86) }) },
            new[] { Physics.DeckStrip(new[] { new double[] { 0, 2, 0 }, new double[] { 5, 3, 0 } }, new[] { new double[] { 0, 2, 4 }, new double[] { 5, 3, 4 } }) },
            new[] { (Physics.DeckStrip(new[] { new double[] { 8, 2, 0 }, new double[] { 9, 3, 0 } }, new[] { new double[] { 8, 2, 4 }, new double[] { 9, 3, 4 } }), 4) });
        Console.WriteLine(Sha(o) + " " + o.Length);
        break;
    }
    case "build":           // build GAME MAPS WORK LEVELS [dry] [nocars] [noroot]
    {
        Paths.Init(args[1], args[2], args[3], Path.Combine(args[3], "rebrn"), args[4]);
        var opt = new BuildOptions { Dry = args.Contains("dry"), NoCars = args.Contains("nocars"), NoRoot = args.Contains("noroot"), Log = s => Console.WriteLine($"{sw.Elapsed.TotalSeconds,7:0.00}  {s}") };
        Build.Run(opt);
        Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:0.00} s");
        break;
    }
    case "wheels":          // wheels GAME: every game car's wheels on every body car an imported body uses
    {
        Paths.Init(args[1]);
        using var root = new Pak(Paths.RootOriginal);
        int bad = 0, n = 0, less = 0;
        var why = new Dictionary<string, int>(); CarGlb.Why = w => why[w] = why.GetValueOrDefault(w) + 1;
        foreach (var bas in CarGlb.GlbBody.Distinct())
        {
            var orig = CarModel.ModelBytes(root, CarModel.ModelPath(bas));
            foreach (var c in CarsData.Cars().Where(c => c.Pick && c.Name != bas))
            {
                var d = (byte[])orig.Clone(); string msg = "";
                int k = CarGlb.SwapWheels(d, orig, root, c.Name, s => msg += s + " | ");
                n++; if (msg.Contains("less detailed")) less++;
                if (k < 4) bad++;
                CpModel.Read(d);
            }
        }
        foreach (var (w, q) in why.OrderByDescending(q => q.Value).Take(24)) Console.WriteLine($"{q,5} {w}");
        Console.WriteLine($"{n} pairs, {less} use a less detailed tyre, {bad} not complete");
        break;
    }
    case "skins":           // skins GAME OUTDIR [CAR PIC SKIN.png]: the skinnable cars; with a car: its picture, UV map, and the skin written + read back
    {
        Paths.Init(args[1]);
        var cars = Skins.Cars(Console.WriteLine);
        Console.WriteLine($"{cars.Count} skinnable cars ({sw.Elapsed.TotalSeconds:0.0} s)");
        foreach (var c in cars) Console.WriteLine($"{c.Car}	{c.Body}	{string.Join(",", c.Pictures.Take(6))}");
        if (args.Length > 4)
        {
            string car = args[3], pic = args[4], dir = args[2];
            Directory.CreateDirectory(dir);
            var t = System.Diagnostics.Stopwatch.StartNew();
            Skins.Picture(car, pic).Save(Path.Combine(dir, car + "_" + pic + ".png"));
            Skins.UvMap(car, pic).Save(Path.Combine(dir, car + "_" + pic + "_uv.png"));
            Console.WriteLine($"picture + UV map {t.ElapsedMilliseconds} ms");
            var skin = Rgba.Load(args[5]);
            var model = Skins.ModelBytes(car);
            var lv = Skins.Levels(model, pic);
            t.Restart();
            foreach (var l in lv)
            {
                var enc = Skins.Encode(skin.Resize(l.W, l.H), l.Fmt, l.Mips, l.Size);
                Console.WriteLine($"  {l.W} x {l.H} {l.Fmt} mips {l.Mips}: {enc.Length} bytes, stored {l.Size} {(enc.Length == l.Size ? "OK" : "DIFFERENT")}");
            }
            Console.WriteLine($"encoded in {t.ElapsedMilliseconds} ms");
            Skins.Set(car, pic, args[5]);
            var put = Skins.Apply(model, car, new[] { pic }, Console.WriteLine);
            File.WriteAllBytes(Path.Combine(dir, car + ".skinned.model"), put);
            int changed = 0; for (int i = 0; i < put.Length; i++) if (put[i] != model[i]) changed++;
            Console.WriteLine($"same length {put.Length == model.Length}, {changed} bytes changed, only inside the picture: {lv.Sum(l => l.Size)} bytes of picture data");
            var back = Skins.Levels(put, pic).OrderByDescending(q => q.W).First();
            var dec = Previews.Dxt5Decode(put.AsSpan(back.At, back.W * back.H), back.W, back.H);
            var want = skin.Resize(back.W, back.H).Px; double err = 0; for (int i = 0; i < dec.Length; i++) err += Math.Abs(dec[i] - want[i]);
            Console.WriteLine($"read back 1024: mean error {err / dec.Length:0.00} of 255");
            CpModel.Read(put); Console.WriteLine("the skinned model still reads as a model");
        }
        break;
    }
    case "skininstall":     // skininstall GAME: Build skins (every skin that is on) into the game's rebrn root.pak, as the tab's button does
    {
        Paths.Init(args[1]);
        if (Play.GameRunning()) { Console.WriteLine("Blur is running: close it first"); break; }
        var (on, back) = Skins.Install(Console.WriteLine);
        Console.WriteLine($"done: {on} car(s) with a skin, {back} back to the game's picture, {sw.Elapsed.TotalSeconds:0.0} s");
        break;
    }
    case "skincheck":       // skincheck PACK ORIGINAL CAR...: is each car's model in PACK changed from ORIGINAL (same length)?
    {
        using var P = new Pak(args[1]); using var O = new Pak(args[2]);
        foreach (var car in args.Skip(3)) { var a = P.Read(Skins.ModelPath(car))!; var o = O.Read(Skins.ModelPath(car))!; Console.WriteLine($"{car}: {(a.AsSpan().SequenceEqual(o) ? "the game's own" : a.Length == o.Length ? "SKINNED (same length)" : "CHANGED LENGTH")}"); }
        break;
    }
    case "bake":            // bake GAME: Bake 3D pictures (BMB_PREVIEW = where they go)
    {
        Paths.Init(args[1]);
        var models = Layout.Catalog().Values.Where(e => e.Str("kind") != "import" && e.Truthy("model")).Select(e => e.Str("model")!).ToList();
        var (done, failed, total) = Previews.Bake(models, (_, _, _) => { }, () => false);
        Console.WriteLine($"baked {done} of {total} models, {failed} without a picture, {sw.Elapsed.TotalSeconds:0.0} s");
        break;
    }
    case "surface":         // surface GAME MAPS OUT: the C# surface pieces as JSON (compare with tools\debug_surface.py)
    {
        Paths.Init(args[1], args[2]);
        var w = World.AssignIds(World.Load());
        var m = w.Objs("locations").First().Objs("maps").First();
        var r = Road.ForMap(m); var net = Network.Get(m, r);
        var sp = Surface.PhysicsPatches(m, net);
        Console.WriteLine($"{sp.Count} {sp.Sum(p => p.P.Corners.Count)} {sp.Sum(p => p.P.Faces.Count)}");
        File.WriteAllText(args[3], Json.Dumps(sp.Select(p => (object?)new JObj { ["mat"] = (long)p.Mat, ["c"] = p.P.Corners.Select(c => (object?)c.Select(v => (object?)Math.Round(v, 6)).ToList()).ToList(), ["f"] = (long)p.P.Faces.Count }).ToList(), -1));
        var o = Surface.OverlayChunks(m, net);
        Console.WriteLine(string.Join(" ", o.Select(c => $"{c.Key} {c.Verts.Count} {c.Strip.Count} {c.Anchor[0]} {c.Anchor[2]}")));
        File.WriteAllText(args[3] + ".ov", Json.Dumps(o.Select(c => (object?)c.Verts.Select(v => (object?)v.Select(x => (object?)x).ToList()).ToList()).ToList(), -1));
        break;
    }
    default:
        Console.WriteLine("commands: build pak-list kslf-dump model-parse json-roundtrip random dxt phys");
        break;
}

static object? ToJ(object? v) => v switch
{
    Dictionary<string, object?> d => Conv(d),
    List<object?> l => l.Select(ToJ).ToList(),
    _ => v,
};
static JObj Conv(Dictionary<string, object?> d) { var o = new JObj(); foreach (var kv in d) o[kv.Key] = ToJ(kv.Value); return o; }

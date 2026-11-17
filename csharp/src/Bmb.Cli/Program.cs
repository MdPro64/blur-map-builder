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
    case "carexport":       // carexport GAME OUTDIR CAR...: each game car's model as OUTDIR\CAR.glb (look only)
    {
        Paths.Init(args[1]); Directory.CreateDirectory(args[2]);
        foreach (var car in args.Skip(3))
        {
            string f = Path.Combine(args[2], car + ".glb");
            File.WriteAllBytes(f, CarExport.Glb(car, Console.WriteLine));
            var pic = Previews.Render(Previews.Imported(f), 640);
            if (pic != null) using (var bm = Previews.ToBitmap(pic)) bm.Save(Path.ChangeExtension(f, ".png"));
        }
        break;
    }
    case "logotest":        // logotest GAME WORLD.json OUTDIR: the custom cars made in memory: each one's maker record + its logo picture read back as PNG (look only)
    {
        Paths.Init(args[1]); Directory.CreateDirectory(args[3]);
        var files = new Dictionary<string, byte[]>();
        var wld = (JObj)Json.Load(args[2])!;
        CarBuild.AddCars(files, wld, Console.WriteLine);
        foreach (var l in CarLogo.Dump(files["gamedata/xt/amax.bin"], new[] { "BMB_", "BMW_M3E92_Drift" })) Console.WriteLine(l);
        using var R = new Pak(Paths.RootOriginal);
        var fe = CarBuild.MenuPictures(R.Read("\\ui\\data\\ui_frontend.bpr")!, wld);
        var b = Bpr.Parse(fe);
        foreach (var r in b.Recs.Where(r => b.NameOf(r).StartsWith("man_bmb_") || b.NameOf(r) == "man_bmw"))
        {
            var rgba = Previews.Dxt5Decode(b.Data.Span.Slice(r[2], r[9] * r[10]), r[9], r[10]);
            using var bm = new System.Drawing.Bitmap(r[9], r[10]);
            for (int y = 0; y < r[10]; y++) for (int x = 0; x < r[9]; x++) { int a = 4 * (y * r[9] + x); bm.SetPixel(x, y, System.Drawing.Color.FromArgb(rgba[a + 3], rgba[a], rgba[a + 1], rgba[a + 2])); }
            bm.Save(Path.Combine(args[3], b.NameOf(r) + ".png"));
            Console.WriteLine($"{b.NameOf(r)}: {r[9]}x{r[10]} format {r[7]} mips {r[8]} bytes {r[5]}");
        }
        Console.WriteLine($"frontend pack {fe.Length} bytes, reads back: {Bpr.Parse(fe).Recs.Count} pictures");
        break;
    }
    case "soundtest":       // soundtest GAME WORLD.json CAR...: the custom cars made in memory + the sound fields of them and of the given game cars
    {
        Paths.Init(args[1]);
        var files = new Dictionary<string, byte[]>();
        CarBuild.AddCars(files, (JObj)Json.Load(args[2])!, Console.WriteLine);
        foreach (var l in CarSound.Dump(new[] { "BMB_*" }.Concat(args.Skip(3)), files["gamedata/xt/amax.bin"])) Console.WriteLine(l);
        break;
    }
    case "pak-names":       // pak-names FILE: every file name + size (no unpacking)
    {
        using var pk = new Pak(args[1]);
        foreach (var e in pk.Files) Console.WriteLine($"{e.Name}	{e.Size}	{(e.Offset == Pak.NoData ? "nodata" : "")}");
        break;
    }
    case "bankwaves":       // bankwaves GAME CAR...: each car's engine sound bank: every sound's name, format, length (look only)
    {
        Paths.Init(args[1]);
        using var rp = new Pak(Paths.RootOriginal);
        if (args[2] == "ALL")
        {
            // every engine bank's sound names, with the numbers taken out, and how many banks have each
            var seen = new Dictionary<string, SortedSet<string>>();
            foreach (var bk in CarSoundPreview.Engines().Values.Distinct().Select(e => CarSoundPreview.BankOf(e, rp)).Distinct().Append("Car_Common"))
            {
                var d = rp.Read(@"\audio\" + bk.ToLowerInvariant() + ".baf");
                if (d == null) { Console.WriteLine($"{bk}: no bank file"); continue; }
                foreach (var w in SoundBank.Read(d).Waves)
                {
                    string k = System.Text.RegularExpressions.Regex.Replace(w.Name, @"^[^_]+_", "*_");
                    k = System.Text.RegularExpressions.Regex.Replace(k, @"\d+", "#");
                    if (bk == "Car_Common") k = "COMMON " + w.Name;
                    if (!seen.TryGetValue(k, out var set)) seen[k] = set = new();
                    set.Add(bk);
                }
            }
            foreach (var (k, v) in seen.OrderBy(q => q.Key)) Console.WriteLine($"{k,-45} {v.Count,3} banks  e.g. {v.First()}");
            break;
        }
        foreach (var car in args.Skip(2))
        {
            var plan = OwnSound.MakePlan(car, 0, rp);
            var bank = SoundBank.Read(rp.Read(@"\audio\" + plan.Bank.ToLowerInvariant() + ".baf")!);
            Console.WriteLine($"{car}: engine {plan.Engine}, bank {plan.Bank}, {bank.Waves.Count} sounds; dats {string.Join(" ", plan.Dats.Select(d => d.OldFile))}");
            foreach (var w in bank.Waves) Console.WriteLine($"   {w.Name,-40} fmt {w.Format} ch {w.Channels} {w.Samples / (double)Math.Max(1, w.Rate):0.00} s");
        }
        break;
    }
    case "netidtest":       // netidtest GAME WORLD.json: the custom cars made in memory: each one's number (net_id) and uid (look only)
    {
        Paths.Init(args[1]);
        var w = (JObj)Json.Load(args[2])!;
        foreach (var c in w.Objs("cars")) Console.WriteLine($"{c.Str("name")}: net_id {(c.ContainsKey("net_id") ? c.Int("net_id").ToString() : "-")} -> slot {CarNetId.Of(c)} = {CarNetId.First + 4 * (uint)CarNetId.Of(c):X8}");
        var files = new Dictionary<string, byte[]>();
        CarBuild.AddCars(files, w, _ => { });
        foreach (var l in CarNetId.Dump(files["gamedata/xt/amax.bin"])) Console.WriteLine("   built " + l);
        break;
    }
    case "exhausttest":     // exhausttest GAME WORLD.json: the custom cars made in memory: each one's flame points (look only)
    {
        Paths.Init(args[1]);
        var files = new Dictionary<string, byte[]>();
        CarBuild.AddCars(files, (JObj)Json.Load(args[2])!, l => { if (l.Contains("exhaust")) Console.WriteLine(l); });
        foreach (var l in CarExhaust.Dump(files["gamedata/xt/amax.bin"], new[] { "BMB_", "BMW_M3E92_Drift" })) Console.WriteLine(l);
        break;
    }
    case "flamelook":       // flamelook GAME OUT.png BASECAR [GLB]: the Custom car preview of a car with its flame spots drawn on (look only)
    {
        Paths.Init(args[1]);
        var car = new JObj { ["base"] = args[3], ["class"] = 1L, ["paint"] = new List<object?> { "#2050a0", "#202020" } };
        if (args.Length > 4) car["body_glb"] = args[4];
        var parts = CarUi.CarParts(car); var info = new CarUi.ViewInfo();
        using var bm = CarUi.Picture(parts, new byte[] { 32, 80, 160 }, 700, 2.5, true, -0.25, info);
        using var g = System.Drawing.Graphics.FromImage(bm);
        foreach (var q in CarExhaust.Game(args[3])) { var (x, y) = CarUi.Project(info, q[0], q[1], q[2]); g.FillEllipse(System.Drawing.Brushes.OrangeRed, x - 6, y - 6, 12, 12); }
        bm.Save(args[2]);
        break;
    }
    case "usedsounds":      // usedsounds GAME CAR...: every sound each car can play, by sound field / .dat / bank (look only)
    {
        Paths.Init(args[1]);
        using var rp = new Pak(Paths.RootOriginal);
        foreach (var car in args.Skip(2)) { Console.WriteLine("== " + car); foreach (var l in OwnSound.UsedSounds(car, rp)) Console.WriteLine(l); }
        break;
    }
    case "gamesound":       // gamesound GAME CAR OUTDIR: the game's own sound of every kind for this car's engine, as OUTDIR\KIND.wav (look only)
    {
        Paths.Init(args[1]); Directory.CreateDirectory(args[3]);
        using var rp = new Pak(Paths.RootOriginal);
        foreach (var k in OwnSound.Kinds)
        {
            var w = OwnSound.GameSound(args[2], k.Key, rp);
            Console.WriteLine($"{k.Key}: {(w == null ? "none" : w.Length + " bytes")}");
            if (w != null) File.WriteAllBytes(Path.Combine(args[3], k.Key + ".wav"), w);
        }
        break;
    }
    case "mp3wav":          // mp3wav IN OUT.wav: a sound file (.mp3 ...) read the way an import reads it, written as the .wav it becomes (look only)
    {
        var x = OwnSound.ReadWav(args[1]);
        File.WriteAllBytes(args[2], OwnSound.Wav16(x));
        Console.WriteLine($"{x.Length} samples at 48 kHz = {x.Length / 48000.0:0.00} s, peak {x.Max(Math.Abs):0.000}");
        break;
    }
    case "packtest":        // packtest GAME SRCMAPS SRCDATA DSTMAPS DSTDATA OUTDIR: car / skins / map exported from SRC, imported into DST (test only)
    {
        Paths.Init(args[1], args[2]); Paths.Data = args[3];
        var wld = (JObj)Json.Load(Paths.WorldFile)!;
        string outd = args[6]; Directory.CreateDirectory(outd);
        // a car with an imported body + own sounds + a logo
        var car = (JObj)wld.Arr("cars")!.First(c => ((JObj)c!).Truthy("body_glb"))!;
        car["logo"] = "test_logo.png"; car["sound_own"] = "eng.wav"; car["sound_own_file"] = "eng.wav"; car["sounds"] = new JObj { ["gear"] = "gear.wav", ["tyre_skid"] = "skid.wav" };
        Console.WriteLine($"car: {car.Str("name")}, body {car.Str("body_glb")}");
        var (n, miss) = Packs.ExportCar(car, Path.Combine(outd, "car.blurcar"));
        Console.WriteLine($"car exported: {n} files, missing [{string.Join(", ", miss)}]");
        // skins of the first car that has some
        string skCar = Skins.All().First(kv => kv.Value is JObj j && j.Count > 0).Key;
        Console.WriteLine($"skins exported: {Packs.ExportSkins(skCar, Path.Combine(outd, "skins.blurskin"))} of {skCar}");
        // the map with an imported model + the location's sky
        var loc = wld.Objs("locations").First(); var map = loc.Objs("maps").First(m => m.Objs("objects").Any(o => (o.Str("id") ?? "").StartsWith("import:")));
        var st = new JObj { ["lighting"] = loc.Obj("lighting") != null ? Share.Copy(loc.Obj("lighting")!) : null };
        Console.WriteLine($"map: {map.Str("name")}, sky {loc.Obj("lighting")?.Str("sky_image")}, pictures {Share.Export(map, loc.Str("name")!, Path.Combine(outd, "map.blurmap"), st)}");
        // ---- into the other folders, twice (the second time: the same files are used again, nothing doubled)
        Paths.Init(args[1], args[4]); Paths.Data = args[5];
        for (int round = 1; round <= 2; round++)
        {
            Console.WriteLine($"== import round {round}");
            var c2 = Packs.ImportCar(Path.Combine(outd, "car.blurcar"));
            Console.WriteLine($"car: body {c2.Str("body_glb")}, own {c2.Str("sound_own")}, logo {c2.Str("logo")}, sounds {Json.Dumps(c2["sounds"], -1)}");
            var (sc, sn) = Packs.ImportSkins(Path.Combine(outd, "skins.blurskin"));
            Console.WriteLine($"skins: {sn} of {sc}; on now: {Json.Dumps(Skins.OnNow().GetValueOrDefault(sc), -1)}");
            var f = Share.Read(Path.Combine(outd, "map.blurmap"));
            var st2 = f.Info.Obj("settings") ?? new JObj();
            int added = f.PlaceExtras(st2);
            Console.WriteLine($"map: {added} model(s) added; files in package: {string.Join(", ", f.Files.Keys)}; objects: {string.Join(", ", f.Map.Objs("objects").Select(o => o.Str("id")).Where(i => i!.StartsWith("import:")).Distinct())}; sky -> {st2.Obj("lighting")?.Str("sky_image")}");
        }
        foreach (var d in new[] { "cars", "cars/sounds", "cars/logos", "imports", "skies" })
        {
            string dir = Path.Combine(args[4], d.Replace('/', '\\'));
            Console.WriteLine($"{d}: {(Directory.Exists(dir) ? string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName)) : "-")}");
        }
        break;
    }
    case "kindtest":        // kindtest GAME ENGINECAR SOUND.wav [ENGINE]: every sound kind from SOUND.wav: which sounds of each bank copy changed (look only)
    {
        Paths.Init(args[1]);
        using var rp = new Pak(Paths.RootOriginal);
        var x = OwnSound.ReadWav(args[3]);
        var snd = new OwnSound.Sounds { Engine = args.Length > 4 ? x : null };
        foreach (var k in OwnSound.Kinds) snd.Extra[k.Key] = x;
        var plans = OwnSound.MakePlans(args[2], 0, rp, true); plans.AddRange(OwnSound.MakeWheelPlans(args[2], 0, rp));
        var plan = plans[0];
        foreach (var pl in plans) Console.WriteLine($"plan {pl.Target}{(pl.Ai ? " AI" : "")}: entity {pl.Engine}, bank {pl.Bank} -> {pl.NewBank}, dats {string.Join(" ", pl.Dats.Select(d => d.OldFile + "->" + d.NewFile))}");
        foreach (var (n, d) in plans.SelectMany(pl => OwnSound.RootFiles(pl, rp, snd)).GroupBy(q => q.Key).Select(g => g.First()))
        {
            if (!n.EndsWith(".baf")) { Console.WriteLine($"{n}: {d.Length} bytes{(B.Latin1.GetString(d).Contains(plan.NewCommon) ? ", names " + plan.NewCommon : "")}"); continue; }
            var nb = SoundBank.Read(d);
            var gname = n.Contains(plan.NewCommon.ToLowerInvariant()) ? OwnSound.CommonBank : plans.First(pl => n.Contains(pl.NewBank.ToLowerInvariant())).Bank;
            var gb = SoundBank.Read(rp.Read(@"\audio\" + gname.ToLowerInvariant() + ".baf")!);
            var changed = nb.Waves.Zip(gb.Waves).Where(q => !d.AsSpan(q.First.Off, q.First.Size).SequenceEqual(rp.Read(@"\audio\" + gname.ToLowerInvariant() + ".baf")!.AsSpan(q.Second.Off, q.Second.Size))).Select(q => $"{q.First.Name} ({q.Second.Samples}->{q.First.Samples})").ToList();
            Console.WriteLine($"{n} (bank '{nb.Name}', {nb.Waves.Count} sounds): changed {changed.Count}: {string.Join(", ", changed)}");
        }
        var list = B.Latin1.GetString(rp.Read(OwnSound.BankList)!);
        foreach (var pl in plans) list = OwnSound.AddToBankList(list, pl, snd.Common);
        foreach (var l in list.Split(';').Select(q => q.Trim()).Where(q => q.StartsWith("zb"))) Console.WriteLine("bank list: " + l);
        break;
    }
    case "owntest":         // owntest GAME ENGINECAR SOUND.wav RPM OUTDIR: the own-sound copy made in memory + checked (look only)
    {
        Paths.Init(args[1]);
        using var rp = new Pak(Paths.RootOriginal);
        // 1. the encoder: a game loop through encode + decode
        var gb = SoundBank.Read(rp.Read(@"\audio\dodge_v10.baf")!);
        var w0 = gb.Waves.First(w => w.Name == "Viper_Engine_3108"); var x = gb.Decode(w0)!;
        var enc = OwnSound.EncodeVag(x);
        var tb = new SoundBank { Data = enc }; var y = tb.Decode(new SoundBank.Wave { Format = 7, Off = 0, Size = enc.Length, Samples = x.Length })!;
        double se = 0, sx = 0; for (int i = 0; i < x.Length; i++) { se += (x[i] - y[i]) * (x[i] - y[i]); sx += x[i] * x[i]; }
        Console.WriteLine($"encoder: {enc.Length} bytes (game {w0.Size}), signal/noise {10 * Math.Log10(sx / Math.Max(se, 1e-12)):0.0} dB");
        // 2. the plan + root files
        var own = OwnSound.ReadWav(args[3]); double rpm = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        var plan = OwnSound.MakePlan(args[2], 0, rp);
        Console.WriteLine($"engine {plan.Engine}, bank {plan.Bank} -> {plan.NewBank}, proxy {plan.OldProxy:X8} -> {plan.NewProxy:X8}, entity -> {plan.NewDef:X8}");
        foreach (var dq in plan.Dats) Console.WriteLine($"   {dq.OldFile} ({dq.OldObj:X8}) -> {dq.NewFile} ({dq.NewObj:X8})");
        foreach (var (n, d) in OwnSound.RootFiles(plan, rp, own, rpm))
        {
            Console.WriteLine($"   root file {n}: {d.Length} bytes");
            if (n.EndsWith(".baf")) { var nb = SoundBank.Read(d); Console.WriteLine($"      bank '{nb.Name}', {nb.Waves.Count} sounds"); File.WriteAllBytes(Path.Combine(args[5], "own_rev.wav"), CarSoundPreview.RevWav(nb)); File.WriteAllBytes(Path.Combine(args[5], "own_bank.baf"), d); }
        }
        // 3. the gamedata entries
        string xt = Path.Combine(Paths.Unpacked, "xt");
        var (au, en) = OwnSound.AddEntries(plan, File.ReadAllBytes(Path.Combine(xt, "audio.bin")), File.ReadAllBytes(Path.Combine(xt, "audioentities.bin")));
        var ka = new Kslf(au); var ke = new Kslf(en);
        Console.WriteLine($"audio.bin {au.Length} bytes: proxy {ka.Objects.Count} objects; new proxy -> {Json.Dumps(ka.Top(ka.Objects[ka.ObjOf(plan.NewProxy)]), -1)}");
        foreach (var dq in plan.Dats) Console.WriteLine($"   new object {dq.NewObj:X8}: {Json.Dumps(ka.Top(ka.Objects[ka.ObjOf(dq.NewObj)]), -1)}");
        var nd = ke.Top(ke.Objects[ke.ObjOf(plan.NewDef)]);
        Console.WriteLine($"entity copy: name {nd["name"]}, objects {string.Join(" ", OwnSound.AudioObjects(nd).Distinct().Select(u => u.ToString("X8")))}");
        var od = ke.Objects.Select(o => o.Blocks[0].Name == "AudioEntityDef" ? ke.Top(o) : null).First(t => t != null && t["name"] as string == plan.Engine)!;
        Console.WriteLine($"entity game: name {od["name"]}, objects {string.Join(" ", OwnSound.AudioObjects(od).Distinct().Select(u => u.ToString("X8")))}");
        break;
    }
    case "revwav":          // revwav GAME OUTDIR CAR...: each car's engine name + its rev preview as OUTDIR\CAR.wav (look only)
    {
        Paths.Init(args[1]);
        var eng = CarSoundPreview.Engines();
        if (args.Length == 3 && args[2] == "all") { using var rp = new Pak(Paths.RootOriginal); foreach (var e in eng.Values.Distinct().OrderBy(q => q)) Console.WriteLine($"{e} -> {CarSoundPreview.BankOf(e, rp)} {(rp.Find(@"\audio\" + CarSoundPreview.BankOf(e, rp).ToLowerInvariant() + ".baf") != null ? "ok" : "MISSING")}"); break; }
        Console.WriteLine($"{eng.Count} cars with an engine sound, {eng.Values.Distinct().Count()} engines");
        foreach (var car in args.Skip(3)) { var w = CarSoundPreview.RevWav(car); File.WriteAllBytes(Path.Combine(args[2], car + ".wav"), w); Console.WriteLine($"{car}: {eng[car]}, {w.Length / 96000.0:0.0} s"); }
        break;
    }
    case "wheelof":         // wheelof GAMEDATA.pak CAR...: each car's wheel model file in a built pack (look only)
    {
        using var gp = new Pak(args[1]);
        var am = gp.Read(@"\gamedata\xt\amax.bin")!;
        foreach (var c in args.Skip(2)) Console.WriteLine($"{c}: wheels {CarBuild.WheelFileIn(am, c)}");
        break;
    }
    case "wheelpic":        // wheelpic GAME OUTDIR CAR...: each game car's wheel as a picture (look only)
    {
        Paths.Init(args[1]);
        foreach (var c in args.Skip(3)) CarUi.WheelPicture(c, 360)?.Save(Path.Combine(args[2], "wheel_" + c + ".png"));
        break;
    }
    case "glbmodel":        // glbmodel GAME BODY.glb OUT.model: an imported body written into the container model, with the build's log (look only)
    {
        Paths.Init(args[1]);
        using var R = new Pak(Paths.RootOriginal);
        File.WriteAllBytes(args[3], CarGlb.GlbModel(R, new JObj { ["base"] = CarGlb.Container, ["class"] = 1L }, args[2], Console.WriteLine));
        break;
    }
    case "glbroom":         // glbroom GAME: how much room each game car's model has for an imported body (look only)
    {
        Paths.Init(args[1]);
        foreach (var gc in CarUi.Pickable().OrderBy(g => g.Class))
        {
            try { var (v, i) = CarUi.Room(gc.Name); Console.WriteLine($"{"ABCD"[gc.Class]} {gc.Name,-40} points {v,6}  indices {i,6}  ~triangles {Math.Min(v / 1.1, i / 2.3):0}"); }
            catch (Exception e) { Console.WriteLine($"{gc.Name}: {e.Message}"); }
        }
        break;
    }
    case "sounddump":       // sounddump GAME GAMEDATA.pak CAR...: the sound fields of cars in a built pack's amax.bin (look only)
    {
        Paths.Init(args[1]);
        using var gp = new Pak(args[2]);
        foreach (var l in CarSound.Dump(args.Skip(3), gp.Read(@"\gamedata\xt\amax.bin")!)) Console.WriteLine(l);
        break;
    }
    case "install":         // install GAME: what the app's Build + install does (cars get their number first, as when the app loads the world)
    {
        Paths.Init(args[1]);
        var w0 = World.Load();
        if (CarNetId.PinAll(w0)) { World.Save(w0); Console.WriteLine("cars got their own number (world saved)"); }
        var opt = new BuildOptions { Log = s => Console.WriteLine($"{sw.Elapsed.TotalSeconds,7:0.00}  {s}") };
        Build.Run(opt);
        Directory.CreateDirectory(Paths.PackDir);
        foreach (var (src, name) in new[] { (Paths.GamedataOut, "gamedata.pak"), (Paths.RootOut, "root.pak"), (Paths.BootOut, "gamedata_boot.pak") })
        {
            string dst = Path.Combine(Paths.PackDir, name);
            if (!File.Exists(src)) continue;
            File.Copy(src, dst + ".new", true); File.Move(dst + ".new", dst, true);
        }
        if (opt.Built != null) Installed.Remember(opt.Built, opt);
        Setup.RefreshPlugin();
        Skins.InstallNormal(Console.WriteLine);
        Console.WriteLine($"installed into {Paths.PackDir} in {sw.Elapsed.TotalSeconds:0.00} s");
        break;
    }
    case "carsto":          // carsto FROMGAME TOGAME OUTDIR: every custom car of FROM exported as .blurcar into OUTDIR, and TO's cars replaced by them imported (a second player)
    {
        Paths.Init(args[1]);
        var w1 = World.Load();
        if (CarNetId.PinAll(w1)) World.Save(w1);
        Directory.CreateDirectory(args[3]);
        var files = new List<string>();
        foreach (var c in w1.Objs("cars"))
        {
            string f = Path.Combine(args[3], Packs.Plain(c.Str("name") ?? "car") + Packs.CarExt);
            var (n, miss) = Packs.ExportCar(c, f);
            Console.WriteLine($"exported {c.Str("name")} (net_id {c.Int("net_id")}): {n} file(s){(miss.Count > 0 ? ", missing " + string.Join(", ", miss) : "")}");
            files.Add(f);
        }
        Paths.Init(args[2]);
        var w2 = World.Load();
        var cars = new List<object?>(); w2["cars"] = cars;
        foreach (var f in files)
        {
            var car = Packs.ImportCar(f);
            CarNetId.Unique(car, cars.OfType<JObj>());
            cars.Add(car);
            Console.WriteLine($"imported {car.Str("name")} into {Paths.Game}: net_id {car.Int("net_id")}");
        }
        World.Save(w2);
        break;
    }
    case "build":           // build GAME MAPS WORK LEVELS [dry] [nocars] [noroot]
    {
        Paths.Init(args[1], args[2], args[3], Path.Combine(args[3], "rebrn"), args[4]);
        // -maps / -cars / -skins: that part is not updated (the last install's copy goes in); remember: keep what was built as installed
        var opt = new BuildOptions { Dry = args.Contains("dry"), NoCars = args.Contains("nocars"), NoRoot = args.Contains("noroot"), Log = s => Console.WriteLine($"{sw.Elapsed.TotalSeconds,7:0.00}  {s}"),
            Maps = !args.Contains("-maps"), Cars = !args.Contains("-cars"), Skins = !args.Contains("-skins") };
        Build.Run(opt);
        if (args.Contains("remember") && opt.Built != null) { Installed.Remember(opt.Built, opt); Console.WriteLine("remembered as installed"); }
        Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:0.00} s");
        break;
    }
    case "terrainprep":     // terrainprep GAME IN_WORLD.json OUT_WORLD.json: every map gets 3 jump ramps, auto hills and a bump patch (test only)
    {
        Paths.Init(args[1]);
        var w = (JObj)Json.Load(args[2])!;
        foreach (var loc in w.Objs("locations"))
            foreach (var m in loc.Objs("maps"))
            {
                var road = Road.ForMap(m); var net = Network.Get(m, road);
                var objs = m.Arr("objects") ?? new List<object?>(); m["objects"] = objs;
                foreach (var (f, id) in new[] { (0.3, "ramp:jump"), (0.55, "ramp:kicker"), (0.8, "ramp:table") })
                {
                    int i = (int)(road.Count * f);
                    objs.Add(new JObj { ["id"] = id, ["x"] = road.Centre[i].X, ["z"] = road.Centre[i].Z, ["h"] = road.Height[i], ["turn"] = road.Heading[i] });
                }
                for (int i = (int)(road.Count * 0.45); i < (int)(road.Count * 0.5); i++) Terrain.BumpBrush(m, road.Centre[i].X, road.Centre[i].Z, road.WidthAt[i] / 2, false);
                int n = Terrain.Auto(m, net, new Terrain.AutoSet());
                var t = Terrain.Make(m, Network.Get(m, road), Terrain.Budget(m, road, Network.Get(m, road)));
                Console.WriteLine($"{m.Str("name")}: {n} hill points; budget {Terrain.Budget(m, road, net)}; ramps {t.Ramps}, bumps {t.Bumps} ({t.BumpStep} m), hills {t.Hills} ({t.HillStep} m); max verts {t.Chunks.Max(c => c.Verts.Count)}, max strip {t.Chunks.Max(c => c.Strip.Count)}; decks {t.Decks.Count}, walls {t.Walls.Count}");
            }
        Json.Save(w, args[3]);
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
    case "carlook":         // carlook GAME [CLASS]: every game car of a class: its model, its shaders and its pictures (look only)
    {
        Paths.Init(args[1]);
        var skinnable = Skins.Cars().Select(c => c.Car).ToHashSet();
        using var P = new Pak(Paths.RootOriginal);
        var models = P.Files.Select(f => f.Name).Where(n => n.StartsWith("\\models\\vehicles\\") && n.EndsWith(".model") && n.Count(c => c == '\\') == 3).Select(n => n[17..^6]).ToHashSet();
        foreach (var g in CarsData.Cars().Where(c => args.Length < 3 || CarUi.Classes[Math.Clamp(c.Class, 0, 3)].ToString() == args[2]))
        {
            string car = g.Name.ToLowerInvariant();
            Console.WriteLine($"== {g.Name} class {CarUi.Classes[Math.Clamp(g.Class, 0, 3)]} model {(models.Contains(car) ? "yes" : "NO")} skinnable {skinnable.Contains(car)}");
            if (!models.Contains(car)) continue;
            var data = P.Read(Skins.ModelPath(car))!;
            var M = CpModel.Read(data);
            Console.WriteLine("   fx: " + string.Join(", ", M.FxFiles));
            var names = System.Text.RegularExpressions.Regex.Matches(B.Latin1.GetString(data), "([A-Za-z0-9_]{3,})RA\\x01").Select(m => m.Groups[1].Value).Distinct();
            foreach (var n in names) { var lv = Skins.Levels(data, n); if (lv.Count > 0) Console.WriteLine($"   pic {n}: {string.Join(" ", lv.Select(l => $"{l.W}x{l.H} {l.Fmt}"))}"); }
            foreach (var mesh in M.MeshInfos) if (mesh.Material >= 0 && mesh.Material < M.FxFiles.Count && M.FxFiles[mesh.Material].Contains("paint", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine($"   paint mesh uses {M.FxFiles[mesh.Material]}"); break; }
        }
        break;
    }
    case "carsave":         // carsave GAME OUTDIR CAR...: the game's own model of each car into OUTDIR (look only)
    {
        Paths.Init(args[1]); Directory.CreateDirectory(args[2]);
        foreach (var car in args.Skip(3)) File.WriteAllBytes(Path.Combine(args[2], car + ".model"), Skins.ModelBytes(car));
        break;
    }
    case "cartwin":         // cartwin GAME CAR_A CAR_B: do two cars share the same body (vertex + face data)? (look only)
    {
        Paths.Init(args[1]);
        var da = Skins.ModelBytes(args[2]); var db = Skins.ModelBytes(args[3]);
        var A = CpModel.Read(da); var Bm = CpModel.Read(db);
        string H(byte[] d, int at, int n) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(d.AsSpan(at, n)))[..10];
        Console.WriteLine($"vstreams {A.VStreams.Count} / {Bm.VStreams.Count}, fstreams {A.FStreams.Count} / {Bm.FStreams.Count}, meshes {A.MeshInfos.Count} / {Bm.MeshInfos.Count}");
        for (int i = 0; i < Math.Max(A.VStreams.Count, Bm.VStreams.Count); i++)
        {
            string x = i < A.VStreams.Count ? $"{A.VStreams[i].Count}x{A.VStreams[i].Bytes} {H(da, A.VStreams[i].Start, A.VStreams[i].Count * A.VStreams[i].Bytes)}" : "-";
            string y = i < Bm.VStreams.Count ? $"{Bm.VStreams[i].Count}x{Bm.VStreams[i].Bytes} {H(db, Bm.VStreams[i].Start, Bm.VStreams[i].Count * Bm.VStreams[i].Bytes)}" : "-";
            Console.WriteLine($"  v{i}: {x}   |   {y}");
        }
        for (int i = 0; i < Math.Max(A.FStreams.Count, Bm.FStreams.Count); i++)
        {
            string x = i < A.FStreams.Count ? $"{A.FStreams[i].Count} {H(da, A.FStreams[i].Start, A.FStreams[i].Count * 2)}" : "-";
            string y = i < Bm.FStreams.Count ? $"{Bm.FStreams[i].Count} {H(db, Bm.FStreams[i].Start, Bm.FStreams[i].Count * 2)}" : "-";
            Console.WriteLine($"  f{i}: {x}   |   {y}");
        }
        var pa = A.MeshInfos.Select(m => A.FxFiles[m.Material]).ToList(); var pb = Bm.MeshInfos.Select(m => Bm.FxFiles[m.Material]).ToList();
        int diff = 0; for (int i = 0; i < Math.Min(pa.Count, pb.Count); i++) if (pa[i] != pb[i]) { if (diff++ < 12) Console.WriteLine($"  mesh {i}: {pa[i]} -> {pb[i]}"); }
        Console.WriteLine($"{diff} meshes with another shader");
        break;
    }
    case "uvlook":          // uvlook GAME OUTDIR CAR PIC: the car's picture and its body's UV map drawn on it (look only)
    {
        Paths.Init(args[1]); Directory.CreateDirectory(args[2]);
        var pic = Skins.Picture(args[3], args[4]);
        pic.Save(Path.Combine(args[2], args[3] + "_" + args[4] + ".png"));
        Skins.UvMap(args[3], args[4], 1024, 1024, pic).Save(Path.Combine(args[2], args[3] + "_" + args[4] + "_uv.png"));
        break;
    }
    case "twins":           // twins GAME: every game car without a body picture, and a skinnable car with the same body shape (look only)
    {
        Paths.Init(args[1]);
        var sk = Skins.Cars();
        string Shape(string car) { var M = CpModel.Read(Skins.ModelBytes(car)); return string.Join(",", M.VStreams.Select(v => v.Count)) + "/" + M.MeshInfos.Count; }
        var shapes = sk.ToDictionary(c => c.Car, c => { try { return Shape(c.Car); } catch (Exception) { return ""; } });
        foreach (var g in CarsData.Cars().Where(c => c.Pick).OrderBy(c => c.Class))
        {
            string car = g.Name.ToLowerInvariant();
            if (shapes.ContainsKey(car)) continue;
            string me; try { me = Shape(car); } catch (Exception) { continue; }
            int Close(string o) { var x = me.Split('/')[0].Split(','); var y = o.Split('/')[0].Split(','); if (x.Length != y.Length) return 99999; return x.Zip(y).Sum(q => Math.Abs(int.Parse(q.First) - int.Parse(q.Second))); }
            var best = shapes.Where(q => q.Value != "").OrderBy(q => Close(q.Value)).First();
            Console.WriteLine($"{CarUi.Classes[Math.Clamp(g.Class, 0, 3)]}  {car,-36} {(Close(best.Value) < 50 ? "twin " + best.Key : "no twin")}");
        }
        break;
    }
    case "twintest":        // twintest GAME OUTPAK PLAIN TWIN PIC SKIN.png: a copy of rebrn root.pak where PLAIN wears TWIN's body with SKIN in PIC (the game is not changed)
    {
        Paths.Init(args[1]);
        string plain = args[3], twin = args[4], pic = args[5];
        var model = Skins.ModelBytes(twin); var o = (byte[])model.Clone();
        var img = Rgba.Load(args[6]);
        foreach (var l in Skins.Levels(model, pic)) { var enc = Skins.Encode(img.Resize(l.W, l.H), l.Fmt, l.Mips, l.Size); if (enc.Length == l.Size) enc.CopyTo(o, l.At); else Console.WriteLine($"size {l.W} skipped"); }
        CpModel.Read(o);
        Skins.UvMap(twin, pic, 1024, 1024, img).Save(Path.ChangeExtension(args[2], ".uv.png"));
        string pack = Path.Combine(Paths.PackDir, "root.pak");
        Pak.ReplaceFiles(pack, args[2], new List<KeyValuePair<string, byte[]>> { new(Skins.ModelPath(plain), o) });
        using var P = new Pak(args[2]);
        Console.WriteLine($"{plain}: {P.Read(Skins.ModelPath(plain))!.Length} bytes in the test pack (was {Skins.ModelBytes(plain).Length}), same as written: {P.Read(Skins.ModelPath(plain))!.AsSpan().SequenceEqual(o)}");
        break;
    }
    case "pakcopyfile":     // pakcopyfile SRC DST FROMPAK NAME: a copy of SRC (as DST) with NAME taken from FROMPAK
    {
        byte[] f; using (var F = new Pak(args[3])) f = F.Read(args[4])!;
        Pak.ReplaceFiles(args[1], args[2], new List<KeyValuePair<string, byte[]>> { new(args[4], f) });
        using var P = new Pak(args[2]);
        Console.WriteLine($"{args[4]}: {P.Read(args[4])!.Length} bytes, same as FROMPAK: {P.Read(args[4])!.AsSpan().SequenceEqual(f)}");
        break;
    }
    case "vdefs":           // vdefs GAME CAR: vertex streams, definitions, and what each paint mesh uses (look only)
    {
        Paths.Init(args[1]);
        var M = CpModel.Read(Skins.ModelBytes(args[2]));
        for (int i = 0; i < M.VStreams.Count; i++) Console.WriteLine($"v{i} {M.VStreams[i].Count}x{M.VStreams[i].Bytes}: " + string.Join(" ", M.VStreams[i].Def.Select(d => $"t{d.Type}c{d.Channel}s{d.Sub}")));
        for (int i = 0; i < M.VertDefs.Count; i++) Console.WriteLine($"def{i}: " + string.Join(" ", M.VertDefs[i].Select(d => $"{d.Prefix}:{d.Type}")));
        foreach (var g in M.MeshInfos.Select((m, k) => (m, k)).GroupBy(q => (M.FxFiles[q.m.Material], q.m.Definition))) Console.WriteLine($"{g.Key.Item1} def{g.Key.Definition}: {g.Count()} meshes");
        break;
    }
    case "skinfile":        // skinfile IN.model PIC SKIN.png OUT.model: the skin written over every stored size of PIC (look / test only)
    {
        var model = File.ReadAllBytes(args[1]); var o = (byte[])model.Clone(); var img = Rgba.Load(args[3]);
        foreach (var l in Skins.Levels(model, args[2])) { var enc = Skins.Encode(img.Resize(l.W, l.H), l.Fmt, l.Mips, l.Size); if (enc.Length == l.Size) { enc.CopyTo(o, l.At); Console.WriteLine($"  {l.W} x {l.H} {l.Fmt} written"); } else Console.WriteLine($"  {l.W} skipped"); }
        var M = CpModel.Read(o);
        Console.WriteLine($"reads as a model: {M.MeshInfos.Count} meshes, fx: {string.Join(", ", M.FxFiles.Where(f => f.Contains("Paint", StringComparison.OrdinalIgnoreCase)))}");
        File.WriteAllBytes(args[4], o);
        break;
    }
    case "pakput":          // pakput SRC DST NAME FILE: a copy of the pack SRC (as DST) with NAME replaced by FILE
    {
        var f = File.ReadAllBytes(args[4]);
        Pak.ReplaceFiles(args[1], args[2], new List<KeyValuePair<string, byte[]>> { new(args[3], f) });
        using var P = new Pak(args[2]);
        Console.WriteLine($"{args[3]}: {P.Read(args[3])!.Length} bytes, same as FILE: {P.Read(args[3])!.AsSpan().SequenceEqual(f)}");
        break;
    }
    case "streams":         // streams FILE.model: where the vertex / face data and pictures start (look only)
    {
        var d = File.ReadAllBytes(args[1]); var M = CpModel.Read(d);
        foreach (var v in M.VStreams) Console.WriteLine($"offs {string.Join(",", v.Def.Zip(v.Offs).Select(q => $"t{q.First.Type}c{q.First.Channel}s{q.First.Sub}@{q.Second}"))}"); foreach (var v in M.VStreams) Console.WriteLine($"v start {v.Start} (mod16 {v.Start % 16}, mod2048 {v.Start % 2048}) len {v.Length}");
        foreach (var f in M.FStreams) Console.WriteLine($"f start {f.Start} (mod16 {f.Start % 16}, mod2048 {f.Start % 2048}) len {f.Length}");
        foreach (var t in M.TexList) Console.WriteLine($"t {t.Name} off {t.Off} (mod16 {t.Off % 16})");
        break;
    }
    case "plaincheck":      // plaincheck GAME [CAR OUT.model]: every game car with a plain body: does it get a picture slot? (look only)
    {
        Paths.Init(args[1]);
        if (args.Length > 3) { File.WriteAllBytes(args[3], PlainSkin.AddSlot(Skins.ModelBytes(args[2]))!); break; }
        using var P = new Pak(Paths.RootOriginal);
        var names = P.Files.Select(f => f.Name).Where(n => n.StartsWith(@"\models\vehicles\") && n.EndsWith(".model") && n.Count(c => c == '\\') == 3).Select(n => n[17..^6]).OrderBy(n => n).ToList();
        int ok = 0, plain = 0;
        foreach (var car in names)
        {
            var d = P.Read(Skins.ModelPath(car))!;
            bool isPlain = CpModel.Read(d).FxFiles.Any(f => f.StartsWith("veh_paint_a", StringComparison.OrdinalIgnoreCase));
            if (!isPlain) continue;
            plain++;
            string? pic = PlainSkin.ScratchPicture(d);
            byte[]? r = null; string err = "";
            try { r = PlainSkin.AddSlot(d); } catch (Exception e) { err = e.Message; }
            if (r != null && pic != null) ok++;
            Console.WriteLine($"{(r != null && pic != null ? "OK  " : "NO  ")}{car,-40} picture {pic ?? "-"} {(r == null ? "no slot " + err + PlainSkin.Why : $"+{r.Length - d.Length} bytes")}");
        }
        Console.WriteLine($"{ok} of {plain} plain-paint models get a picture slot");
        break;
    }
    case "uvall":           // uvall GAME OUTDIR: every listed car's body picture with its UV lines, small (look only)
    {
        Paths.Init(args[1]); Directory.CreateDirectory(args[2]);
        foreach (var c in Skins.Cars())
        {
            try { Skins.UvMap(c.Car, c.Body, 512, 512, Skins.Picture(c.Car, c.Body)).Save(Path.Combine(args[2], (c.Plain ? "plain_" : "") + c.Car + ".png")); }
            catch (Exception e) { Console.WriteLine($"{c.Car}: {e.Message}"); }
        }
        Console.WriteLine($"{Skins.Cars().Count} cars drawn");
        break;
    }
    case "uvdebug":         // uvdebug GAME CAR: each paint mesh: vertex block, layout, UV channels and their ranges (look only)
    {
        Paths.Init(args[1]);
        var d = Skins.ModelBytes(args[2]); var M = CpModel.Read(d);
        for (int i = 0; i < M.MeshInfos.Count; i++)
        {
            var m = M.MeshInfos[i];
            if (m.Material < 0 || m.Material >= M.FxFiles.Count || !M.FxFiles[m.Material].Contains("paint", StringComparison.OrdinalIgnoreCase)) continue;
            var vs = m.VStream.Count > 0 && m.VStream[0] < M.VStreams.Count ? M.VStreams[m.VStream[0]] : null;
            var types = M.VertDefs[m.Definition].Where(q => q.Prefix == 0).Select(q => q.Type).ToList();
            string ranges = "";
            if (vs != null)
            {
                int off = 0;
                foreach (var (t, ch, sub) in vs.Def)
                {
                    if (ch == 5 && t == 9)
                    {
                        float lo = 9, hi = -9; int vb = m.VOffset[0] / vs.Bytes;
                        for (int v = vb; v < Math.Min(vs.Count, vb + m.Data1[0].VCount); v++) { float u = (float)BitConverter.UInt16BitsToHalf(B.U16(d, vs.Start + v * vs.Bytes + off)); lo = Math.Min(lo, u); hi = Math.Max(hi, u); }
                        ranges += $" c5s{sub}[{lo:0.00}..{hi:0.00}]";
                    }
                    off += t == 6 ? 12 : t == 9 ? 8 : 4;
                }
            }
            var fs0 = M.FStreams[m.FaceStream]; var (fo0, fc0, dv0, dc0) = m.Data1[0];
            int imin = int.MaxValue, imax = -1; for (int q = 0; q < fc0; q++) { int ix = B.U16(d, fs0.Start + 2 * (fo0 + q)); imin = Math.Min(imin, ix); imax = Math.Max(imax, ix); }
            Console.Write($"ft {m.FaceType} fs {m.FaceStream} fo {fo0} fc {fc0} idx {imin}..{imax} data1v {dv0}+{dc0} lods {m.Data1.Count}  ");
            Console.WriteLine($"mesh {i} {M.FxFiles[m.Material]} def{m.Definition} [{string.Join(",", types)}] block {(m.VStream.Count > 0 ? m.VStream[0] : -1)} [{(vs == null ? "-" : string.Join(",", vs.Def.Select(q => $"t{q.Type}c{q.Channel}s{q.Sub}")))}] voff {m.VOffset[0]} vcount {m.Data1[0].VCount}{ranges}");
        }
        break;
    }
    case "uvparts":         // uvparts GAME CAR OUT.png: each painted piece in its own colour + a list (look only)
    {
        Paths.Init(args[1]);
        var d = Skins.ModelBytes(args[2]); var M = CpModel.Read(d);
        Skins.UnoverlapLog = Console.WriteLine;
        var parts = Skins.BodyMeshes(d, M, args.Length > 4 ? args[4] == "plain" : Skins.IsPlain(args[2]));
        int W = 1024;
        using var bmp = new System.Drawing.Bitmap(W, W); using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Black);
        var cols = new[] { "#ff4040", "#40ff40", "#4080ff", "#ffff40", "#ff40ff", "#40ffff", "#ff9040", "#a0a0ff", "#ffffff", "#90ff90", "#ff9090", "#c080ff" };
        for (int i = 0; i < parts.Count; i++)
        {
            var m = parts[i];
            using var pen = new System.Drawing.Pen(System.Drawing.ColorTranslator.FromHtml(cols[i % cols.Length]));
            float U(int v, int k) => k == 0 ? m.Uv(d, v).U : m.Uv(d, v).V;
            float u0 = 9, u1 = -9, v0 = 9, v1 = -9;
            foreach (var (a, b, c) in m.Tris)
            {
                g.DrawPolygon(pen, new[] { new System.Drawing.PointF(U(a, 0) * W, U(a, 1) * W), new System.Drawing.PointF(U(b, 0) * W, U(b, 1) * W), new System.Drawing.PointF(U(c, 0) * W, U(c, 1) * W) });
                foreach (int v in new[] { a, b, c }) { u0 = Math.Min(u0, U(v, 0)); u1 = Math.Max(u1, U(v, 0)); v0 = Math.Min(v0, U(v, 1)); v1 = Math.Max(v1, U(v, 1)); }
            }
            Console.WriteLine($"part {i} {cols[i % cols.Length]}: {m.Tris.Count} triangles, moved x{m.S:0.00} +{m.Ou:0.00},{m.Ov:0.00}, pair {m.Pair}, block start {m.Vs.Start} base {m.VBase}, u {u0:0.00}..{u1:0.00} v {v0:0.00}..{v1:0.00}");
        }
        bmp.Save(args[3]);
        for (int i = 0; i < parts.Count; i++) for (int j = i + 1; j < parts.Count; j++)
        {
            var ki = parts[i].Verts.Select(v => ((long)parts[i].Vs.Start << 24) + parts[i].VBase + v).ToHashSet();
            int n = parts[j].Verts.Count(v => ki.Contains(((long)parts[j].Vs.Start << 24) + parts[j].VBase + v));
            if (n > 0) Console.WriteLine($"parts {i} and {j} share {n} points (of {parts[i].Verts.Length} / {parts[j].Verts.Length})");
        }
        break;
    }
    case "uvmask":          // uvmask GAME CAR OUT.png: the UV mask the Car skins tab exports (look only)
    {
        Paths.Init(args[1]); var c = Skins.Cars().First(q => q.Car == args[2]);
        Skins.UvMap(c.Car, c.Body, mask: true).Save(args[3]);
        break;
    }
    case "texscan":         // texscan PAK OUTDIR REGEX: every picture of every model in PAK whose name matches REGEX, as small PNGs + a list (look only)
    {
        using var P = new Pak(args[1]); Directory.CreateDirectory(args[2]);
        var rx = new System.Text.RegularExpressions.Regex(args[3], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var lines = new List<string>(); var seen = new HashSet<string>();
        foreach (var e in P.Files.Where(f => f.Offset != Pak.NoData && f.Name.EndsWith(".model", StringComparison.OrdinalIgnoreCase)))
        {
            byte[]? d; ModelInfo mi;
            try { d = P.Read(e); if (d == null) continue; mi = CpModel.Read(d); } catch (Exception) { continue; }
            foreach (var t in mi.TexList)
            {
                if (!rx.IsMatch(t.Name)) continue;
                lines.Add($"{t.Name}	{t.W}x{t.H}	{(t.Dxt == 0x31545844 ? "DXT1" : t.Dxt == 0x35545844 ? "DXT5" : t.Dxt.ToString("X"))}	{e.Name}");
                if (!seen.Add(t.Name.ToLowerInvariant())) continue;
                var pic = Previews.ReadTexture(d, t, 128);
                if (pic == null) continue;
                Previews.SavePng(pic, Path.Combine(args[2], t.Name + ".png"));
            }
        }
        File.WriteAllLines(Path.Combine(args[2], "list.txt"), lines);
        Console.Error.WriteLine($"{lines.Count} pictures ({seen.Count} names), {sw.ElapsedMilliseconds} ms");
        break;
    }
    case "texmodels":       // texmodels PAK OUTDIR FILEREGEX: the first picture of each model whose file name matches, one PNG per model + its picture id (look only)
    {
        using var P = new Pak(args[1]); Directory.CreateDirectory(args[2]);
        var rx = new System.Text.RegularExpressions.Regex(args[3], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var e in P.Files.Where(f => f.Offset != Pak.NoData && rx.IsMatch(f.Name)))
        {
            var d = P.Read(e); if (d == null) continue;
            ModelInfo mi; try { mi = CpModel.Read(d); } catch (Exception) { continue; }
            var nm = Path.GetFileNameWithoutExtension(e.Name);
            var parts = new List<string>();
            foreach (var t in mi.TexList)
            {
                var pic = Previews.ReadTexture(d, t, 64); if (pic == null) continue;
                Previews.SavePng(pic, Path.Combine(args[2], nm + "_" + t.Name + ".png"));
                long r = 0, g = 0, b2 = 0; int n = pic.W * pic.H;
                for (int i = 0; i < n; i++) { r += pic.Px[i * 3]; g += pic.Px[i * 3 + 1]; b2 += pic.Px[i * 3 + 2]; }
                parts.Add($"{t.Name}={r / n},{g / n},{b2 / n}");
            }
            Console.WriteLine($"{nm}	{string.Join("  ", parts)}");
        }
        break;
    }
    case "boxes":           // boxes PAK NAME: a model's boxes and the numbers around them (look only)
    {
        using var P = new Pak(args[1]); var d = P.Read(args[2])!; var mi = CpModel.Read(d);
        string F(IEnumerable<float> v) => string.Join(" ", v.Select(x => x.ToString("0.###")));
        for (int i = 0; i < mi.ModelBoxes.Count; i++) Console.WriteLine($"model {i} @{mi.ModelRawAt[i]}: pre [{F(mi.ModelRaw[i])}] box [{string.Join(" ", mi.ModelBoxes[i].Select(x => x.ToString("0.###")))}] post [{F(mi.ModelRaw2[i])}]");
        foreach (var e in mi.AllElements) Console.WriteLine($"element {e.Name} parent {e.Parent} box [{string.Join(" ", e.Box.Select(x => x.ToString("0.###")))}] m [{F(e.M.Select(x => (float)x))}]");
        break;
    }
    case "mpconfig":        // mpconfig GAME: the multiplayer config's AI fields (look only)
    {
        Paths.Init(args[1]);
        foreach (var l in GameTweaks.Dump(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "multiplayer.bin")))) Console.WriteLine(l);
        break;
    }
    case "caruids":         // caruids GAME: every game car: uid, class (0 = A), multiplayer pick, name (look only)
    {
        Paths.Init(args[1]);
        var kd = File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "amax.bin")); var kk = new Kslf(kd);
        var mpk = new Kslf(File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "multiplayer.bin")));
        var cfg = mpk.Objects.First(o => mpk.Types[o.Blocks[0].Type].Name == "MultiplayerConfig");
        int voff = mpk.Types[cfg.Blocks[0].Type].Fields.First(f => f.Name == "vehicles").Offset;
        int vat = cfg.Blocks[0].Offset + voff, vblk = B.U16(mpk.Data, vat), vfirst = B.U16(mpk.Data, vat + 2), vcnt = (int)B.U32(mpk.Data, vat + 4);
        var picks = new HashSet<uint>();
        for (int j = 0; j < vcnt; j++) picks.Add(mpk.Refs[B.U32(mpk.Data, cfg.Blocks[vblk].Offset + 4 * (vfirst + j))].Uid);
        foreach (var (u, t, oi) in kk.Refs)
        {
            if (kk.Types[t].Name != "AmaxVehicle") continue;
            var o = kk.Objects[oi]; var bb = o.Blocks[0];
            var f = kk.AllFields(bb.Type).GroupBy(q => q.Name).ToDictionary(g => g.Key, g => g.Last());
            Console.WriteLine($"{u:X8}	{B.I32(kd, bb.Offset + f["class"].Offset)}	{(picks.Contains(u) ? "mp" : "--")}	{B.Sz(o.Pool, (int)B.U32(kd, bb.Offset + f["Name"].Offset))}");
        }
        break;
    }
    case "aimods":        // aimods GAME on|off: only the AI-mods patch of Blur REBORN.exe (what Build + install does with the tweak)
    {
        Paths.Init(args[1]);
        Console.WriteLine(GameTweaks.SetAiMods(args[2] == "on") ?? "nothing to change");
        break;
    }
    case "harderai":        // harderai IN.csv OUT.csv: the multiplayer Hard AI file made harder (look only)
    {
        File.WriteAllText(args[2], GameTweaks.HarderAi(File.ReadAllText(args[1])));
        break;
    }
    case "hudicons":        // hudicons IPF PICTURE.png OUTDIR: perk_icons.ipf read + written back (same bytes?), the shunt icon from PICTURE, both drawn (look only)
    {
        var ipf = File.ReadAllBytes(args[1]);
        Console.WriteLine($"round trip same bytes: {HudIcons.RoundTrip(ipf)}");
        Directory.CreateDirectory(args[3]);
        HudIcons.Draw(ipf, "perk_shunt", Path.Combine(args[3], "shunt_game.png"));
        var made = HudIcons.Make(ipf, k => k == "shunt" ? args[2] : null, Console.WriteLine)!;
        Console.WriteLine($"made {made.Length} bytes (was {ipf.Length}); reads back: {HudIcons.RoundTrip(made)}");
        HudIcons.Draw(made, "perk_shunt", Path.Combine(args[3], "shunt_mine.png"));
        HudIcons.Draw(made, "perk_shunt_x2", Path.Combine(args[3], "shunt_x2_mine.png"));
        HudIcons.Draw(made, "perk_barge", Path.Combine(args[3], "barge_untouched.png"));
        break;
    }
    case "modelinfo":       // modelinfo PAK NAME: a model's streams, meshes, materials and textures (look only)
    {
        using var pk = new Pak(args[1]);
        var md = CpModel.Read(pk.Read(args[2]) ?? throw new FileNotFoundException(args[2]));
        Console.WriteLine($"fx: {string.Join(", ", md.FxFiles)}");
        Console.WriteLine($"textures: {string.Join(", ", md.TexList.Select(t => $"{t.Name} {t.W}x{t.H} dxt{t.Dxt:X}"))}");
        Console.WriteLine($"elements: {string.Join(", ", md.AllElements.Select(e => e.Name))}");
        foreach (var v in md.VStreams) Console.WriteLine($"vstream: {v.Count} x {v.Bytes} bytes, def {string.Join(" ", v.Def.Select(q => $"{q.Type}/{q.Channel}/{q.Sub}"))}");
        foreach (var m in md.MeshInfos) Console.WriteLine($"mesh: material {m.Material}, def {m.Definition}, faces {string.Join(" ", m.Data1.Select(q => $"{q.FaceCount}@{q.FaceOff} v{q.VCount}@{q.VOff}"))}, vstreams {string.Join(",", m.VStream)}");
        Console.WriteLine($"bbox {string.Join(" ", md.BBox.Select(x => x.ToString("0.###")))}");
        break;
    }
    case "roadicons":       // roadicons PAK OUTDIR KIND...: each road pickup's icon pictures as PNG + their sizes (look only)
    {
        using var pk = new Pak(args[1]);
        Directory.CreateDirectory(args[2]);
        foreach (var k in args.Skip(3))
        {
            var md = pk.Read(RoadIcons.ModelOf(k)) ?? throw new FileNotFoundException(k);
            foreach (var l in RoadIcons.Dump(md, k)) Console.WriteLine(l);
            RoadIcons.Decode(md, RoadIcons.Pic(k)).Save(Path.Combine(args[2], k + ".png"));
            RoadIcons.Decode(md, RoadIcons.PicInverse(k)).Save(Path.Combine(args[2], k + "_inverse.png"));
            if (args[2].Length > 0 && File.Exists(Path.Combine(args[2], "test.png")))
            {
                var made = RoadIcons.Make(md, k, Path.Combine(args[2], "test.png"), Console.WriteLine)!;
                Console.WriteLine($"   made {made.Length} bytes (same: {made.Length == md.Length})");
                RoadIcons.Decode(made, RoadIcons.Pic(k)).Save(Path.Combine(args[2], k + "_mine.png"));
                RoadIcons.Decode(made, RoadIcons.PicInverse(k)).Save(Path.Combine(args[2], k + "_inverse_mine.png"));
            }
        }
        break;
    }
    case "bprnames":        // bprnames PAK BPR: every picture name in a UI bundle with its record fields (look only)
    {
        using var pk = new Pak(args[1]);
        var bp = Bpr.Parse(pk.Read(args[2]) ?? throw new FileNotFoundException(args[2]));
        foreach (var r in bp.Recs) Console.WriteLine($"{bp.NameOf(r)}	{string.Join(",", r)}");
        break;
    }
    case "pakget":          // pakget PAK NAME OUT: one file out of a pack (look only)
    {
        using var P = new Pak(args[1]); File.WriteAllBytes(args[3], P.Read(args[2])!);
        break;
    }
    case "fxcount":         // fxcount FILE.model: how many meshes use each shader (look only)
    {
        var M = CpModel.Read(File.ReadAllBytes(args[1]));
        foreach (var g in M.MeshInfos.GroupBy(m => m.Material).OrderBy(g => g.Key)) Console.WriteLine($"{g.Key} {M.FxFiles[g.Key]}: {g.Count()} meshes");
        break;
    }
    case "plaintest":       // plaintest GAME SRCPAK OUTPAK SKIN.png CAR...: a copy of SRCPAK where each plain car has its slot + SKIN (test only)
    {
        Paths.Init(args[1]);
        var img = Rgba.Load(args[4]).OnColour(200, 200, 200);
        var files = new List<KeyValuePair<string, byte[]>>();
        foreach (var car in args.Skip(5))
        {
            var orig = Skins.ModelBytes(car);
            var m = PlainSkin.AddSlot(orig, Skins.IsDecal(car)) ?? throw new Exception(car + ": no slot " + PlainSkin.Why);
            string pic = PlainSkin.ScratchPicture(orig)!;
            int done = 0;
            foreach (var l in Skins.Levels(m, pic)) { var enc = Skins.Encode(img.Resize(l.W, l.H), l.Fmt, l.Mips, l.Size); if (enc.Length == l.Size) { enc.CopyTo(m, l.At); done++; } }
            CpModel.Read(m);
            files.Add(new(Skins.ModelPath(car), m));
            Console.WriteLine($"{car}: +{m.Length - orig.Length} bytes, skin in {pic} ({done} sizes)");
        }
        Pak.ReplaceFiles(args[2], args[3], files);
        Console.WriteLine($"test pack: {args[3]}");
        break;
    }
    case "levels":          // levels FILE.model PIC: every stored size of a picture (look only)
    {
        var d = File.ReadAllBytes(args[1]);
        foreach (var l in Skins.Levels(d, args[2])) Console.WriteLine($"{l.W} x {l.H} {l.Fmt} mips {l.Mips} at {l.At} size {l.Size} ends {l.At + l.Size}");
        var M = CpModel.Read(d);
        foreach (var t in M.TexList) Console.WriteLine($"  tex {t.Name} off {t.Off} len {t.Len}");
        break;
    }
    case "plainlist":       // plainlist GAME: the plain-paint cars of the Car skins tab: model, class, game name (look only)
    {
        Paths.Init(args[1]);
        var games = CarsData.Cars().ToDictionary(c => c.Name.ToLowerInvariant(), c => c);
        foreach (var c in Skins.Cars().Where(q => q.Plain))
        {
            games.TryGetValue(c.Car, out var g);
            Console.WriteLine($"{c.Car}	{(g == null ? "-" : CarUi.Classes[Math.Clamp(g.Class, 0, 3)].ToString())}	{(g?.Pick == true ? "player" : "other")}	{(c.Decal ? "decal" : "")}");
        }
        break;
    }
    case "uvcheck":         // uvcheck GAME CAR: the body UVs after the picture slot: broken numbers, and how far they go (look only)
    {
        Paths.Init(args[1]);
        var o = Skins.ModelBytes(args[2]); var d = PlainSkin.AddSlot(o, Skins.IsDecal(args[2]))!;
        var parts = Skins.BodyMeshes(o, CpModel.Read(o), plain: true);
        int bad = 0, outside = 0, all = 0, moved = parts.Count(m => m.S != 1 || m.Ou != 0 || m.Ov != 0);
        foreach (var m in parts)
            foreach (int v in m.Verts)
            {
                int at = m.Vs.Start + (m.VBase + v) * m.Vs.Bytes + m.UvAt;
                // the converted file has other offsets after the edits: read the same vertex in the original after applying the change
                float u = (float)m.Uv(o, v).U, q = (float)m.Uv(o, v).V; all++;
                if (float.IsNaN(u) || float.IsInfinity(u) || float.IsNaN(q) || float.IsInfinity(q)) bad++;
                else if (u < -0.01f || u > 1.01f || q < -0.01f || q > 1.01f) outside++;
            }
        Console.WriteLine($"{args[2]}: {parts.Count} pieces ({moved} moved), {all} points, broken {bad}, outside the picture {outside}");
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
    case "landkeys":        // landkeys GAME MAPS: every map with hills: its textures (quads per key) and slope spread (look only)
    {
        Paths.Init(args[1], args[2]);
        var w = World.AssignIds(World.Load());
        foreach (var loc in w.Objs("locations"))
            foreach (var m in loc.Objs("maps"))
            {
                if (Terrain.Heights(m).Count == 0) continue;
                var r = Road.ForMap(m); var net = Network.Get(m, r);
                var sm = Terrain.Sample(m, net, 8);
                var t = Terrain.Make(m, net, Terrain.Budget(m, r, net));
                Console.WriteLine($"{loc.Str("name")} / {m.Str("name")}: ground={Terrain.Ground(m)} auto={Terrain.AutoTex(m)} paint={Terrain.LandPaint(m).Count} highest={Terrain.Heights(m).Values.Max():0} m");
                Console.WriteLine("  8 m keys: " + string.Join(", ", sm.Keys.GroupBy(k => k.Value).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
                Console.WriteLine($"  build: {t.Hills} hill models on {t.HillStep} m, keys {string.Join(",", t.HillKeys)}");
                var slopes = sm.Keys.Keys.Select(q => { double h00 = sm.H(q.Item1, q.Item2), h10 = sm.H(q.Item1 + 1, q.Item2), h01 = sm.H(q.Item1, q.Item2 + 1); double gx = (h10 - h00) / 8, gz = (h01 - h00) / 8; return Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI; }).OrderBy(v => v).ToList();
                if (slopes.Count > 0) Console.WriteLine($"  slope deg: median {slopes[slopes.Count / 2]:0}, 90% {slopes[(int)(slopes.Count * 0.9)]:0}, max {slopes[^1]:0}");
            }
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

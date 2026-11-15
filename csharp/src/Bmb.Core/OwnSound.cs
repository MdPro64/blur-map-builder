// A custom car's own engine sound. The player gives one recording of an engine running at steady revs (a .wav); the build makes
// a copy of the car's game engine sound (its bank of loops, the .dat files that name the bank, the sound entries in audio.bin /
// audioentities.bin) where the engine + exhaust loops are that recording, pitched to each rev step. Intake, turbo and the other
// sounds stay the game engine's. The car then points at the copy. Nothing of the game's own sounds changes.
namespace Bmb.Core;

public static class OwnSound
{
    public const string Folder = "sounds";                         // maps\cars\sounds\ (next to the imported bodies)
    public const double DefaultRpm = 3000;
    const uint Uid0 = 0xBA500000;                                   // 16 uids per custom car: 0-7 audio objects, 8 entity, 9 proxy

    public static string FilePath(string file) => Path.Combine(Paths.Tracks, "cars", Folder, file);

    // ---- reading the player's file -----------------------------------------------------------------------------------

    /// a .wav file (PCM 8 / 16 / 24 / 32 bit or 32-bit float, any rate, any channels) as mono floats at 48 kHz
    public static float[] ReadWav(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 12 || B.Latin1.GetString(d, 0, 4) != "RIFF" || B.Latin1.GetString(d, 8, 4) != "WAVE") throw new InvalidDataException("not a .wav file");
        int at = 12, fmt = 0, ch = 0, rate = 0, bits = 0; int dataAt = -1, dataLen = 0;
        while (at + 8 <= d.Length)
        {
            string id = B.Latin1.GetString(d, at, 4); int len = (int)B.U32(d, at + 4);
            if (id == "fmt ") { fmt = B.U16(d, at + 8); ch = B.U16(d, at + 10); rate = (int)B.U32(d, at + 12); bits = B.U16(d, at + 22); if (fmt == 0xFFFE && len >= 26) fmt = B.U16(d, at + 32); }
            else if (id == "data") { dataAt = at + 8; dataLen = Math.Min(len, d.Length - dataAt); break; }
            at += 8 + len + (len & 1);
        }
        if (dataAt < 0 || ch <= 0 || rate <= 0) throw new InvalidDataException("the .wav file has no sound data");
        if (!(fmt == 1 && bits is 8 or 16 or 24 or 32) && !(fmt == 3 && bits == 32)) throw new InvalidDataException($"this .wav format is not supported (format {fmt}, {bits} bit) - save it as 16-bit PCM");
        int bps = bits / 8, frames = dataLen / (bps * ch);
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float s = 0;
            for (int c = 0; c < ch; c++)
            {
                int a = dataAt + (i * ch + c) * bps;
                s += fmt == 3 ? BitConverter.ToSingle(d, a) : bits switch
                {
                    8 => (d[a] - 128) / 128f,
                    16 => (short)B.U16(d, a) / 32768f,
                    24 => ((d[a] | d[a + 1] << 8 | (sbyte)d[a + 2] << 16)) / 8388608f,
                    _ => (int)B.U32(d, a) / 2147483648f,
                };
            }
            mono[i] = s / ch;
        }
        if (frames < rate / 10) throw new InvalidDataException("the sound is too short (at least 0.1 s, best 1-3 s of steady revs)");
        return rate == 48000 ? mono : Resample(mono, rate / 48000.0, (int)(frames * 48000L / rate));
    }

    /// n samples read through s at the given speed (1 = same, 2 = an octave up), linear in between, wrapping round
    public static float[] Resample(float[] s, double speed, int n)
    {
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            double p = i * speed; int a = (int)p; float t = (float)(p - a);
            o[i] = s[a % s.Length] * (1 - t) + s[(a + 1) % s.Length] * t;
        }
        return o;
    }

    /// a loop of n samples that joins its end to its start without a click (the start fades in from what comes after the end)
    public static float[] Loop(float[] s, double speed, int n)
    {
        int fade = Math.Min(n / 4, 4800);
        var y = Resample(s, speed, n + fade);
        var o = new float[n];
        for (int j = 0; j < n; j++) o[j] = y[j];
        for (int j = 0; j < fade; j++) { float t = (float)j / fade; o[j] = y[j] * t + y[n + j] * (1 - t); }
        return o;
    }

    static float Rms(float[] s) { double q = 0; foreach (var v in s) q += v * v; return (float)Math.Sqrt(q / Math.Max(1, s.Length)); }

    // ---- the game's 4-bit ADPCM (VAG style) ---------------------------------------------------------------------------

    static readonly float[,] F = { { 0, 0 }, { 60 / 64f, 0 }, { 115 / 64f, -52 / 64f }, { 98 / 64f, -55 / 64f }, { 122 / 64f, -60 / 64f } };

    /// floats (-1..1) into 33-byte blocks of 64 samples (a filter/scale byte + 32 bytes of 4-bit steps), the best filter per block
    public static byte[] EncodeVag(float[] x)
    {
        int blocks = (x.Length + 63) / 64;
        var o = new byte[blocks * 33];
        float p1 = 0, p2 = 0;
        Span<int> best = stackalloc int[64]; Span<int> cur = stackalloc int[64];
        for (int b = 0; b < blocks; b++)
        {
            double bestErr = double.MaxValue; int bestF = 0, bestS = 0; float bp1 = 0, bp2 = 0;
            for (int f = 0; f < 5; f++)
            {
                float a = F[f, 0], c = F[f, 1];
                // the biggest step this filter needs (from the true samples), then the finest scale that still holds it
                float q1 = p1, q2 = p2, max = 0;
                for (int i = 0; i < 64; i++)
                {
                    int k = b * 64 + i; float v = k < x.Length ? x[k] : 0;
                    max = Math.Max(max, MathF.Abs(v - (a * q1 + c * q2))); q2 = q1; q1 = v;
                }
                for (int sh0 = 0; sh0 <= 1; sh0++)
                {
                    int sh = Math.Clamp((int)Math.Floor(-3 - Math.Log2(Math.Max(max, 1e-9) / 7.0)) - sh0, 0, 12);
                    float step = MathF.Pow(2, -3 - sh);
                    double err = 0; q1 = p1; q2 = p2;
                    for (int i = 0; i < 64; i++)
                    {
                        int k = b * 64 + i; float v = k < x.Length ? x[k] : 0;
                        float pred = a * q1 + c * q2;
                        int n = Math.Clamp((int)MathF.Round((v - pred) / step), -8, 7);
                        float dec = n * step + pred;
                        err += (dec - v) * (dec - v);
                        cur[i] = n; q2 = q1; q1 = dec;
                    }
                    if (err < bestErr) { bestErr = err; bestF = f; bestS = sh; cur.CopyTo(best); bp1 = q1; bp2 = q2; }
                }
            }
            int at = b * 33;
            o[at] = (byte)(bestF << 4 | bestS);
            for (int i = 0; i < 32; i++) o[at + 1 + i] = (byte)((best[2 * i] & 15) | (best[2 * i + 1] & 15) << 4);
            p1 = bp1; p2 = bp2;
        }
        return o;
    }

    // ---- the bank ------------------------------------------------------------------------------------------------------

    static readonly System.Text.RegularExpressions.Regex Step = new(@"_(engine|exhaust|induction)_(\d+|decel)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// a copy of a game bank named newName, its engine + exhaust loops (and off-the-gas) made from the player's sound
    /// (recorded at rpm), each as loud as the game's sound it replaces; the other sounds stay as they are
    public static byte[] MakeBank(byte[] game, string newName, float[] own, double rpm)
    {
        var bank = SoundBank.Read(game);
        int hdr = (int)B.U32(game, 4);
        var o = new ByteBuf(game.Length);
        o.Add(game.AsSpan(0, hdr));
        for (int i = 0; i < 34; i++) o[18 + i] = 0;
        var nb = B.Latin1.GetBytes(newName); for (int i = 0; i < Math.Min(33, nb.Length); i++) o[18 + i] = nb[i];
        // the wave headers, in order (their sizes stay; data places and lengths change)
        int at = hdr; var heads = new List<int>();
        foreach (var w in bank.Waves) { heads.Add(o.Len); int size = (int)B.U32(game, at + 4); o.Add(game.AsSpan(at, size)); at += size; }
        var revs = bank.Waves.Select(w => Step.Match(w.Name)).Where(m => m.Success && m.Groups[2].Value.All(char.IsDigit)).Select(m => int.Parse(m.Groups[2].Value)).Distinct().OrderBy(r => r).ToList();
        for (int k = 0; k < bank.Waves.Count; k++)
        {
            var w = bank.Waves[k];
            byte[] data = game.AsSpan(w.Off, w.Size).ToArray(); int samples = w.Samples;
            var m = Step.Match(w.Name);
            if (m.Success && w.Format == 7 && bank.Decode(w) is { } orig)
            {
                double r = m.Groups[2].Value.All(char.IsDigit) ? int.Parse(m.Groups[2].Value) : (revs.Count > 1 ? revs[1] : revs.FirstOrDefault(2000)) * 0.9;
                int n = Math.Max(64, (orig.Length / 64) * 64);
                var loop = Loop(own, r / rpm * 48000.0 / (w.Rate > 0 ? w.Rate : 48000), n);   // the own sound is 48 kHz; the wave plays at its own rate
                float k0 = Rms(orig) / Math.Max(Rms(loop), 1e-6f);
                for (int i = 0; i < loop.Length; i++) loop[i] = Math.Clamp(loop[i] * k0, -0.98f, 0.98f);
                data = EncodeVag(loop); samples = loop.Length;
                if (w.Channels == 2)   // stereo waves: the blocks take turns (left, right ...): the same sound on both sides
                {
                    var st = new byte[data.Length * 2];
                    for (int b = 0; b < data.Length / 33; b++) { Array.Copy(data, b * 33, st, b * 66, 33); Array.Copy(data, b * 33, st, b * 66 + 33, 33); }
                    data = st;
                }
            }
            int h = heads[k];
            o.Put(h + 44, (uint)(o.Len + 8)); o.Put(h + 48, (uint)data.Length);
            if (w.Format == 7) o.Put(h + 68, (uint)samples);
            o.Add(B.Latin1.GetBytes("DATA")); o.AddU32((uint)data.Length + 8); o.Add(data);
        }
        return o.ToArray();
    }

    // ---- names: a copy's names are as long as the game's (the .dat files keep their layout) ------------------------------

    static string B36(int i) => i < 10 ? ((char)('0' + i)).ToString() : ((char)('a' + i - 10)).ToString();
    /// "Dodge_V10" -> "zb3ge_V10" for custom car 3 (same length, unique per car)
    public static string Rename(string s, int car) => s.Length >= 4 ? "zb" + B36(car % 36) + s[3..] : throw new InvalidDataException("name too short: " + s);

    public sealed class Plan
    {
        public string Engine = "", Bank = "", NewBank = "";
        public readonly List<(uint OldObj, uint NewObj, string OldFile, string NewFile)> Dats = new();
        public uint NewDef, NewProxy, OldProxy;
        public bool Ai;     // the AI version of the engine sound (the car's aiVehicleAudioEntity)
    }

    /// the copies a car's own sound needs: the full engine sound, and the AI version (the game plays that one for cars it does
    /// not treat as the focus car, e.g. in LAN races)
    public static List<Plan> MakePlans(string engineCar, int car, Pak root)
    {
        var o = new List<Plan> { MakePlan(engineCar, car, root) };
        try { var ai = MakePlan(engineCar, car, root, true); if (ai.Engine != o[0].Engine) o.Add(ai); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException) { }
        return o;
    }

    /// what the copy of a car's engine sound is made of (the same for the gamedata and the root pack)
    public static Plan MakePlan(string engineCar, int car, Pak root, bool ai = false)
    {
        var p = new Plan { Ai = ai };
        string xt = Path.Combine(Paths.Unpacked, "xt");
        var en = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audioentities.bin")));
        var au = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audio.bin")));
        if (!ai)
        {
            p.Engine = CarSoundPreview.Engines().TryGetValue(engineCar, out var e) ? e : throw new InvalidDataException("no engine sound for " + engineCar);
            p.OldProxy = CarSoundPreview.ProxyOf(engineCar);
        }
        else
        {
            // the AI sound entry -> its entity's name ("VW_flat4_AI")
            p.OldProxy = CarSoundPreview.ProxyOf(engineCar, "aiVehicleAudioEntity");
            uint defUid = OneRef(au, au.ObjOf(p.OldProxy));
            p.Engine = en.Top(en.Objects[en.ObjOf(defUid)]).TryGetValue("name", out var n) && n is string ns ? ns : throw new InvalidDataException("AI sound has no name");
        }
        p.Bank = CarSoundPreview.BankOf(p.Engine, root); p.NewBank = Rename(p.Bank, car);
        uint first = Uid0 + 16 * (uint)car + (ai ? 10u : 0u); int most = ai ? 4 : 8;
        // the entity's audio objects whose .dat names the bank
        var def = en.Objects.Select(o => (O: o, T: o.Blocks[0].Name == "AudioEntityDef" ? en.Top(o) : null)).First(q => q.T != null && q.T.TryGetValue("name", out var n) && n as string == p.Engine).T!;
        int k = 0;
        foreach (uint u in AudioObjects(def).Distinct())
        {
            string? file = au.Top(au.Objects[au.ObjOf(u)]).TryGetValue("filename", out var f) ? f as string : null;
            if (file == null) continue;
            var dat = root.Read(@"\audio\dat\" + file.ToLowerInvariant());
            if (dat == null || B.Latin1.GetString(dat).IndexOf(p.Bank, StringComparison.Ordinal) < 0) continue;
            if (k >= most) break;
            p.Dats.Add((u, first + (uint)k, file, Rename(Path.GetFileNameWithoutExtension(file), car) + Path.GetExtension(file)));
            k++;
        }
        if (p.Dats.Count == 0) throw new InvalidDataException($"no sound file of {p.Engine} names its bank {p.Bank}");
        p.NewDef = Uid0 + 16 * (uint)car + (ai ? 14u : 8u); p.NewProxy = Uid0 + 16 * (uint)car + (ai ? 15u : 9u);
        return p;
    }

    public static List<uint> AudioObjects(object? v)
    {
        var uids = new List<uint>();
        void Walk(object? x)
        {
            switch (x)
            {
                case Dictionary<string, object?> d:
                    if (d.TryGetValue("type", out var t) && t as string == "AudioObject" && d.TryGetValue("uid", out var u) && u is string us) uids.Add(Convert.ToUInt32(us, 16));
                    foreach (var y in d.Values) Walk(y);
                    break;
                case System.Collections.IEnumerable e when x is not string:
                    foreach (var y in e) Walk(y);
                    break;
            }
        }
        Walk(v);
        return uids;
    }

    /// the root pack's new files: the bank copy and the .dat copies naming it
    public static IEnumerable<KeyValuePair<string, byte[]>> RootFiles(Plan p, Pak root, float[] own, double rpm)
    {
        var bank = root.Read(@"\audio\" + p.Bank.ToLowerInvariant() + ".baf") ?? throw new FileNotFoundException(p.Bank);
        yield return new(@"\audio\" + p.NewBank.ToLowerInvariant() + ".baf", MakeBank(bank, p.NewBank, own, rpm));
        foreach (var (_, _, oldFile, newFile) in p.Dats)
        {
            var dat = (byte[])root.Read(@"\audio\dat\" + oldFile.ToLowerInvariant())!.Clone();
            Replace(dat, Path.GetFileNameWithoutExtension(oldFile), Path.GetFileNameWithoutExtension(newFile));
            Replace(dat, p.Bank, p.NewBank);
            yield return new(@"\audio\dat\" + newFile.ToLowerInvariant(), dat);
        }
    }

    public const string BankList = @"\audio\datbanklists.wbl";

    /// the game's list of which banks each .dat file needs (read once while it starts): a line per .dat copy, its game line with
    /// the bank swapped for the copy's ("Toyota_i6_engine.dat,Toyota_i6.baf,Car_Common.baf,;" ->
    /// "zb0ota_i6_engine.dat,zb0ota_i6.baf,Car_Common.baf,;")
    public static string AddToBankList(string list, Plan p)
    {
        var lines = list.Split(';').ToList();
        var add = new List<string>();
        foreach (var (_, _, oldFile, newFile) in p.Dats)
        {
            var line = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(oldFile + ",", StringComparison.OrdinalIgnoreCase));
            if (line == null) { add.Add($"{newFile},{p.NewBank}.baf,"); continue; }
            var parts = line.Split(',').ToList();
            parts[0] = newFile;
            for (int i = 1; i < parts.Count; i++) if (parts[i].Equals(p.Bank + ".baf", StringComparison.OrdinalIgnoreCase)) parts[i] = p.NewBank + ".baf";
            add.Add(string.Join(",", parts));
        }
        string body = list.TrimEnd();
        return body + (body.EndsWith(";") ? "" : ";") + string.Concat(add.Select(a => "\r\n" + a + ";")) + "\r\n";
    }

    static void Replace(byte[] d, string a, string b)
    {
        if (a.Length != b.Length || a.Length == 0) return;
        var x = B.Latin1.GetBytes(a); var y = B.Latin1.GetBytes(b);
        for (int i = 0; i + x.Length <= d.Length; i++)
            if (d.AsSpan(i, x.Length).SequenceEqual(x)) { y.CopyTo(d, i); i += x.Length - 1; }
    }

    /// the gamedata side: new audio objects (their .dat copies) + a copy of the entity using them + a new sound entry for it.
    /// Returns the new audio.bin and audioentities.bin; the car's record then points at p.NewProxy
    public static (byte[] Audio, byte[] Entities) AddEntries(Plan p, byte[] au, byte[] en)
    {
        foreach (var (oldObj, newObj, _, newFile) in p.Dats)
        {
            var k = new Kslf(au);
            int oi = k.ObjOf(oldObj), t = k.Objects[oi].Blocks[0].Type;
            int off = k.AllFields(t).First(f => f.Name == "filename").Offset;
            au = KslfWrite.CloneObjectAny(au, oi, null, newObj, (data, pool, blocks) => { data.Put(blocks[0] + off, (uint)pool.Len); pool.AddSz(newFile); });
            en = KslfWrite.InsertUid(en, newObj, "AudioObject", out _);
        }
        // the entity copy: its own name, its references to the old audio objects moved to the new ones
        {
            var k = new Kslf(en);
            int di = -1;
            for (int i = 0; i < k.Objects.Count && di < 0; i++)
                if (k.Objects[i].Blocks[0].Name == "AudioEntityDef" && k.Top(k.Objects[i]).TryGetValue("name", out var n) && n as string == p.Engine) di = i;
            int t = k.Objects[di].Blocks[0].Type, nameOff = k.AllFields(t).First(f => f.Name == "name").Offset;
            string newName = Rename(p.Engine, (int)((p.NewDef - Uid0) / 16));
            en = KslfWrite.CloneObjectAny(en, di, null, p.NewDef, (data, pool, blocks) => { data.Put(blocks[0] + nameOff, (uint)pool.Len); pool.AddSz(newName); });
            en = Remap(en, p.NewDef, p.Dats.ToDictionary(q => q.OldObj, q => q.NewObj));
        }
        // the sound entry (proxy) copy, pointing at the entity copy
        {
            var k = new Kslf(au);
            int pi = k.ObjOf(p.OldProxy);
            uint oldDef = OneRef(k, pi);
            string defType = k.Types[k.Refs.First(r => r.Uid == oldDef).Type].Name;
            au = KslfWrite.InsertUid(au, p.NewDef, defType, out _);
            au = KslfWrite.CloneObjectAny(au, pi, null, p.NewProxy, null);
            au = Remap(au, p.NewProxy, new Dictionary<uint, uint> { [oldDef] = p.NewDef });
        }
        return (au, en);
    }

    /// the uid the (one) reference of an object points at
    static uint OneRef(Kslf k, int oi)
    {
        var o = k.Objects[oi];
        foreach (int a in KslfWrite.RefSlots(k))
            if (a >= o.Data && a < o.Data + o.DSize) { uint v = B.U32(k.Data, a); if (v < k.Refs.Length) return k.Refs[v].Uid; }
        throw new InvalidDataException("the sound entry has no reference");
    }

    /// in the object with this uid: every reference to an old uid now points at its new uid (rows looked up in the file)
    static byte[] Remap(byte[] d, uint objUid, Dictionary<uint, uint> map)
    {
        var k = new Kslf(d);
        var o = k.Objects[k.ObjOf(objUid)];
        var row = new Dictionary<uint, int>();
        for (int i = 0; i < k.Refs.Length; i++) row[k.Refs[i].Uid] = i;
        var outp = (byte[])d.Clone();
        foreach (int a in KslfWrite.RefSlots(k))
        {
            if (a < o.Data || a >= o.Data + o.DSize) continue;
            uint v = B.U32(d, a);
            if (v < k.Refs.Length && map.TryGetValue(k.Refs[v].Uid, out var nu)) B.Put(outp, a, (uint)row[nu]);
        }
        return outp;
    }
}

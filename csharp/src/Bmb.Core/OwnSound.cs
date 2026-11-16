// A custom car's own engine sound. The player gives one recording of an engine running at steady revs (a .wav); the build makes
// a copy of the car's game engine sound (its bank of loops, the .dat files that name the bank, the sound entries in audio.bin /
// audioentities.bin) where the engine, exhaust and intake loops are that recording, pitched to each rev step. Turbo, supercharger, blow-off,
// gear and the other sounds stay the game engine's. The car then points at the copy. Nothing of the game's own sounds changes.
namespace Bmb.Core;

public static class OwnSound
{
    public const string Folder = "sounds";                         // maps\cars\sounds\ (next to the imported bodies)
    public const double DefaultRpm = 3000;

    /// the other sounds a player can change, each from its own .wav (car["sounds"][Key] = file in maps\cars\sounds\).
    /// Loop: the sound runs on (made as long as the game's, joined without a click); else it plays once (as long as the player's).
    /// Common: the sound is in the shared bank Car_Common (all cars), so the car gets its own copy of that bank.
    /// Bank: "engine" = the engine's own bank, "common" = the shared Car_Common bank (the car gets its own copy), "wheel" = the
    /// tyre bank Wheel_dry (the car gets its own copy of the tyre sound entry too)
    public sealed record Kind(string Key, string Label, System.Text.RegularExpressions.Regex Rx, bool Loop, string Bank, string Tip)
    {
        public bool Common => Bank == "common";
    }
    static System.Text.RegularExpressions.Regex Rx(string p) => new(p, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static readonly Kind[] Kinds =
    {
        new("supercharger", "Supercharger whine", Rx(@"_sc_(\d+)$"), true, "engine", "The whine of a supercharger (only engines that have one)"),
        new("turbo_spool", "Turbo spool", Rx(@"_(turbo_spool|induction_boost)$"), true, "engine", "The turbo whistle while it spins up (only turbo engines)"),
        new("turbo_bov", "Turbo blow-off", Rx(@"_turbo_bov\d*$"), false, "engine", "The pssh when you lift off the gas (only turbo engines)"),
        new("turbo_air", "Turbo air", Rx(@"_(turbo|ext)_air$"), true, "engine", "The air rush of the turbo (only turbo engines)"),
        new("gear", "Gear shift", Rx(@"^transmission_race_gear_(up|down)$"), false, "common", "The clunk of a gear change"),
        new("gear_whine", "Gearbox whine", Rx(@"^transmission_(straight|helix)$"), true, "common", "The whine of the gearbox while driving fast"),
        new("gear_crunch", "Gear crunch", Rx(@"^(gears_crunch_\d+|grinding_loop_\d+)$"), false, "common", "The crunch / grind of a missed gear"),
        new("pops", "Backfire pops", Rx(@"^(pop_\d+|bang_(both|left|right))$"), false, "common", "The pops and bangs from the exhaust"),
        new("damage", "Damage hiss / fire", Rx(@"^(steam_loop|car_fire\d*l?)$"), true, "common", "The hiss and fire of a badly damaged engine"),
        new("tyre_roll", "Tyres rolling", Rx(@"^(asphalt|concrete|cobbles|wood|metal)_road$"), true, "wheel", "The tyres on the road (asphalt, concrete, cobbles, wood, metal)"),
        new("tyre_skid", "Tyre skid / drift", Rx(@"^asphalt_lateral_\d+$"), true, "wheel", "The squeal of the tyres sliding sideways (drift)"),
        new("tyre_brake", "Brake squeal", Rx(@"^asphalt_brakes(_abs)?$"), true, "wheel", "The tyres when you brake hard"),
        new("tyre_spin", "Wheelspin", Rx(@"^asphalt_spin$"), true, "wheel", "The tyres spinning on the spot (burnout, fast start)"),
    };
    public const string CommonBank = "Car_Common", WheelBank = "Wheel_dry";
    const uint WheelUid0 = 0xBA580000;                              // 16 uids per custom car for its tyre sound copy (as Uid0)

    /// what a car's own sounds are: the engine recording (or none) and the other kinds it changes
    public sealed class Sounds
    {
        public float[]? Engine; public double Rpm = DefaultRpm;
        public readonly Dictionary<string, float[]> Extra = new();
        public bool Any => Engine != null || Extra.Count > 0;
        public bool Common => Uses("common");
        public bool Wheel => Uses("wheel");
        public bool Uses(string bank) => Extra.Keys.Any(k => Kinds.First(q => q.Key == k).Bank == bank);
    }
    const uint Uid0 = 0xBA500000;                                   // 16 uids per custom car: 0-7 audio objects, 8 entity, 9 proxy

    /// floats (-1..1, mono, 48 kHz unless told) as a 16-bit .wav file to play
    public static byte[] Wav16(float[] x, int rate = 48000)
    {
        var o = new ByteBuf(44 + 2 * x.Length);
        o.Add(B.Latin1.GetBytes("RIFF")).AddU32((uint)(36 + 2 * x.Length)).Add(B.Latin1.GetBytes("WAVEfmt ")).AddU32(16).AddU16(1).AddU16(1)
         .AddU32((uint)rate).AddU32((uint)(2 * rate)).AddU16(2).AddU16(16).Add(B.Latin1.GetBytes("data")).AddU32((uint)(2 * x.Length));
        foreach (var v in x) o.AddU16((ushort)(short)Math.Clamp(v * 32767f, -32768f, 32767f));
        return o.ToArray();
    }

    /// the game's own sound of one kind, as the car with this engine plays it (to hear what a row changes): a loop played for
    /// about 2 s (its middle step when there are several), sounds that play once one after the other (up to 4). Null = this
    /// engine has none of that kind (e.g. no turbo).
    public static byte[]? GameSound(string engineCar, string kindKey, Pak root)
    {
        var kind = Kinds.First(k => k.Key == kindKey);
        string bankName = kind.Bank switch { "common" => CommonBank, "wheel" => WheelBank, _ => CarSoundPreview.BankOf(CarSoundPreview.Engines()[engineCar], root) };
        var d = root.Read(@"\audio\" + bankName.ToLowerInvariant() + ".baf");
        if (d == null) return null;
        var bank = SoundBank.Read(d);
        var waves = bank.Waves.Where(w => kind.Rx.IsMatch(w.Name) && w.Format == 7).ToList();
        if (waves.Count == 0) return null;
        int rate = waves[0].Rate > 0 ? waves[0].Rate : 48000;
        var o = new List<float>();
        if (kind.Loop)
        {
            var x = bank.Decode(waves[waves.Count / 2]);
            if (x == null || x.Length == 0) return null;
            while (o.Count < rate * 2) o.AddRange(x);
        }
        else
            foreach (var w in waves.Take(4))
            {
                var x = bank.Decode(w);
                if (x == null) continue;
                o.AddRange(x); o.AddRange(new float[rate / 4]);
            }
        return o.Count == 0 ? null : Wav16(o.ToArray(), rate);
    }

    /// a player's sound file into the sounds folder: a .wav as it is, anything else (.mp3 ...) turned into a .wav first
    /// (16-bit, 48 kHz, mono - the build reads .wav). Returns the file name it has there.
    public static string Import(string path)
    {
        string folder = Path.Combine(Paths.Tracks, "cars", Folder);
        Directory.CreateDirectory(folder);
        bool wav = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase);
        string name = wav ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path) + ".wav", dst = Path.Combine(folder, name);
        if (wav) { if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dst, true); }
        else File.WriteAllBytes(dst, Wav16(ReadWav(path)));
        return name;
    }

    /// the file types the import windows offer
    public const string Filter = "Sound (.wav, .mp3)|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac";

    public static string FilePath(string file) => Path.Combine(Paths.Tracks, "cars", Folder, file);

    // ---- reading the player's file -----------------------------------------------------------------------------------

    /// a .wav file (PCM 8 / 16 / 24 / 32 bit or 32-bit float, any rate, any channels) as mono floats at 48 kHz
    public static float[] ReadWav(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length >= 12 && B.Latin1.GetString(d, 0, 4) != "RIFF")
        {
            // not a .wav (an .mp3 ...): Windows decodes it
            var (m, r) = Mp3.Read(path);
            if (m.Length < r / 10) throw new InvalidDataException("the sound is too short (at least 0.1 s, best 1-3 s of steady revs)");
            return r == 48000 ? m : Resample(m, r / 48000.0, (int)(m.Length * 48000L / r));
        }
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

    // the rev steps: engine / exhaust / intake loops, named "_Engine_3180", "_eng_3180", "_ext_2500", "_extA_2500", "_ind_4020" ...
    static readonly System.Text.RegularExpressions.Regex Step = new(@"_(engine|eng|exhaust|ext|exta|induction|ind)_(\d+|decel)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// a copy of a game bank named newName, its engine + exhaust loops (and off-the-gas) made from the player's sound
    /// (recorded at rpm), each as loud as the game's sound it replaces; the other sounds stay as they are
    public static byte[] MakeBank(byte[] game, string newName, float[] own, double rpm) => MakeBank(game, newName, new Sounds { Engine = own, Rpm = rpm }, "engine");
    public static byte[] MakeBank(byte[] game, string newName, Sounds s, bool common) => MakeBank(game, newName, s, common ? "common" : "engine");

    /// a copy of a game bank named newName with the player's sounds in it: the engine rev steps (when s.Engine), and each other
    /// kind s has (the engine bank's kinds, or with common the shared bank's), each as loud as the game sound it replaces
    public static byte[] MakeBank(byte[] game, string newName, Sounds s, string target)
    {
        float[]? own = target == "engine" ? s.Engine : null; double rpm = s.Rpm;
        var bank = SoundBank.Read(game);
        int hdr = (int)B.U32(game, 4);
        var o = new ByteBuf(game.Length);
        o.Add(game.AsSpan(0, hdr));
        for (int i = 0; i < 34; i++) o[18 + i] = 0;
        var nb = B.Latin1.GetBytes(newName); for (int i = 0; i < Math.Min(33, nb.Length); i++) o[18 + i] = nb[i];
        // the wave headers, in order (their sizes stay; data places and lengths change)
        int at = hdr; var heads = new List<int>();
        foreach (var w in bank.Waves) { heads.Add(o.Len); int size = (int)B.U32(game, at + 4); o.Add(game.AsSpan(at, size)); at += size; }
        var scNums = bank.Waves.Select(w => Kinds[0].Rx.Match(w.Name)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).OrderBy(v => v).ToList();
        double scMid = scNums.Count > 0 ? scNums[scNums.Count / 2] : 1;
        var revs = bank.Waves.Select(w => Step.Match(w.Name)).Where(m => m.Success && m.Groups[2].Value.All(char.IsDigit)).Select(m => int.Parse(m.Groups[2].Value)).Distinct().OrderBy(r => r).ToList();
        for (int k = 0; k < bank.Waves.Count; k++)
        {
            var w = bank.Waves[k];
            byte[] data = game.AsSpan(w.Off, w.Size).ToArray(); int samples = w.Samples;
            var m = Step.Match(w.Name);
            if (own == null) m = System.Text.RegularExpressions.Match.Empty;
            var kind = Kinds.FirstOrDefault(q => q.Bank == target && s.Extra.ContainsKey(q.Key) && q.Rx.IsMatch(w.Name));
            float[]? made = null;
            if (!m.Success && kind != null && w.Format == 7 && bank.Decode(w) is { } orig2)
            {
                var x = s.Extra[kind.Key]; int rate = w.Rate > 0 ? w.Rate : 48000;
                if (kind.Loop)
                {
                    double speed = kind.Key == "supercharger" && Kinds[0].Rx.Match(w.Name) is { Success: true } sm ? int.Parse(sm.Groups[1].Value) / scMid : 1;
                    made = Loop(x, speed * 48000.0 / rate, Math.Max(64, (orig2.Length / 64) * 64));
                }
                else
                {
                    int n = Math.Max(64, (int)((long)x.Length * rate / 48000 / 64 * 64));
                    made = Resample(x, 48000.0 / rate, n);
                    int fade = Math.Min(n / 8, 480); for (int i = 0; i < fade; i++) made[n - 1 - i] *= (float)i / fade;   // no click at the end
                }
                float k1 = Rms(orig2) / Math.Max(Rms(made), 1e-6f);
                for (int i = 0; i < made.Length; i++) made[i] = Math.Clamp(made[i] * k1, -0.98f, 0.98f);
            }
            if (made != null)
            {
                data = EncodeVag(made); samples = made.Length;
                if (w.Channels == 2)
                {
                    var st = new byte[data.Length * 2];
                    for (int b = 0; b < data.Length / 33; b++) { Array.Copy(data, b * 33, st, b * 66, 33); Array.Copy(data, b * 33, st, b * 66 + 33, 33); }
                    data = st;
                }
            }
            else if (m.Success && w.Format == 7 && bank.Decode(w) is { } orig)
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
        public string Engine = "", Bank = "", NewBank = "", NewCommon = "";
        public int Car;
        public string Target = "engine";      // "engine" = the car's engine sound entry, "wheel" = its tyre sound entry
        public readonly List<(uint OldObj, uint NewObj, string OldFile, string NewFile)> Dats = new();
        public uint NewDef, NewProxy, OldProxy;
        public bool Ai;     // the AI version of the engine sound (the car's aiVehicleAudioEntity)
    }

    /// the copies a car's own sound needs: the full engine sound, and the AI version (the game plays that one for cars it does
    /// not treat as the focus car, e.g. in LAN races)
    public static List<Plan> MakePlans(string engineCar, int car, Pak root) => MakePlans(engineCar, car, root, false);

    /// common: the car changes sounds of the shared bank, so the .dat files naming it are copied too (e.g. Car_common_road.dat,
    /// where the gear sounds play from)
    public static List<Plan> MakePlans(string engineCar, int car, Pak root, bool common)
    {
        var o = new List<Plan> { MakePlan(engineCar, car, root, false, common) };
        try { var ai = MakePlan(engineCar, car, root, true, common); if (ai.Engine != o[0].Engine) o.Add(ai); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException) { }
        return o;
    }

    /// what the copy of a car's engine sound is made of (the same for the gamedata and the root pack)
    public static Plan MakePlan(string engineCar, int car, Pak root, bool ai = false, bool common = false)
    {
        var p = new Plan { Ai = ai, Car = car };
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
        p.Bank = CarSoundPreview.BankOf(p.Engine, root); p.NewBank = Rename(p.Bank, car); p.NewCommon = Rename(CommonBank, car);
        uint first = Uid0 + 16 * (uint)car + (ai ? 10u : 0u); int most = ai ? 4 : 8;
        // the entity's audio objects whose .dat names the bank
        var def = en.Objects.Select(o => (O: o, T: o.Blocks[0].Name == "AudioEntityDef" ? en.Top(o) : null)).First(q => q.T != null && q.T.TryGetValue("name", out var n) && n as string == p.Engine).T!;
        int k = 0;
        foreach (uint u in AudioObjects(def).Distinct())
        {
            string? file = au.Top(au.Objects[au.ObjOf(u)]).TryGetValue("filename", out var f) ? f as string : null;
            if (file == null) continue;
            var dat = root.Read(@"\audio\dat\" + file.ToLowerInvariant());
            if (dat == null) continue;
            string dt = B.Latin1.GetString(dat);
            if (dt.IndexOf(p.Bank, StringComparison.Ordinal) < 0 && !(common && dt.IndexOf(CommonBank, StringComparison.Ordinal) >= 0)) continue;
            if (k >= most) break;
            p.Dats.Add((u, first + (uint)k, file, Rename(Path.GetFileNameWithoutExtension(file), car) + Path.GetExtension(file)));
            k++;
        }
        if (p.Dats.Count == 0) throw new InvalidDataException($"no sound file of {p.Engine} names its bank {p.Bank}");
        p.NewDef = Uid0 + 16 * (uint)car + (ai ? 14u : 8u); p.NewProxy = Uid0 + 16 * (uint)car + (ai ? 15u : 9u);
        return p;
    }

    /// the copies of the tyre sound entry (the full one + the AI one) for a car that changes tyre sounds: their .dat files that
    /// name the tyre bank Wheel_dry, the bank copy, a copy of the entity and a new sound entry (the car's dry wheel fields point at it)
    public static List<Plan> MakeWheelPlans(string engineCar, int car, Pak root)
    {
        var o = new List<Plan>();
        string xt = Path.Combine(Paths.Unpacked, "xt");
        var en = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audioentities.bin")));
        var au = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audio.bin")));
        foreach (bool ai in new[] { false, true })
        {
            var p = new Plan { Ai = ai, Car = car, Target = "wheel", Bank = WheelBank, NewBank = Rename(WheelBank, car), NewCommon = Rename(CommonBank, car) };
            p.OldProxy = CarSoundPreview.ProxyOf(engineCar, ai ? "aiDryWheelAudioEntity" : "dryWheelAudioEntity");
            uint defUid = OneRef(au, au.ObjOf(p.OldProxy));
            p.Engine = en.Top(en.Objects[en.ObjOf(defUid)]).TryGetValue("name", out var n) && n is string ns ? ns : throw new InvalidDataException("tyre sound has no name");
            if (o.Any(q => q.Engine == p.Engine)) continue;
            var def = en.Top(en.Objects[en.ObjOf(defUid)]);
            uint first = WheelUid0 + 16 * (uint)car + (ai ? 10u : 0u); int k = 0;
            foreach (uint u in AudioObjects(def).Distinct())
            {
                string? file = au.Top(au.Objects[au.ObjOf(u)]).TryGetValue("filename", out var f) ? f as string : null;
                var dat = file != null ? root.Read(@"\audio\dat\" + file.ToLowerInvariant()) : null;
                if (dat == null || B.Latin1.GetString(dat).IndexOf(WheelBank, StringComparison.Ordinal) < 0 || k >= 4) continue;
                p.Dats.Add((u, first + (uint)k, file!, Rename(Path.GetFileNameWithoutExtension(file!), car) + Path.GetExtension(file!)));
                k++;
            }
            if (p.Dats.Count == 0) throw new InvalidDataException($"no sound file of {p.Engine} names the tyre bank");
            p.NewDef = WheelUid0 + 16 * (uint)car + (ai ? 14u : 8u); p.NewProxy = WheelUid0 + 16 * (uint)car + (ai ? 15u : 9u);
            o.Add(p);
        }
        return o;
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
    public static IEnumerable<KeyValuePair<string, byte[]>> RootFiles(Plan p, Pak root, float[] own, double rpm) => RootFiles(p, root, new Sounds { Engine = own, Rpm = rpm });

    /// the root pack's new files: the bank copy (+ a copy of the shared bank when s changes one of its sounds) and the .dat copies
    public static IEnumerable<KeyValuePair<string, byte[]>> RootFiles(Plan p, Pak root, Sounds s)
    {
        var bank = root.Read(@"\audio\" + p.Bank.ToLowerInvariant() + ".baf") ?? throw new FileNotFoundException(p.Bank);
        yield return new(@"\audio\" + p.NewBank.ToLowerInvariant() + ".baf", MakeBank(bank, p.NewBank, s, p.Target));
        if (p.Target == "engine" && s.Common)
        {
            var cb = root.Read(@"\audio\" + CommonBank.ToLowerInvariant() + ".baf") ?? throw new FileNotFoundException(CommonBank);
            yield return new(@"\audio\" + p.NewCommon.ToLowerInvariant() + ".baf", MakeBank(cb, p.NewCommon, s, true));
        }
        foreach (var (_, _, oldFile, newFile) in p.Dats)
        {
            var dat = (byte[])root.Read(@"\audio\dat\" + oldFile.ToLowerInvariant())!.Clone();
            Replace(dat, Path.GetFileNameWithoutExtension(oldFile), Path.GetFileNameWithoutExtension(newFile));
            Replace(dat, p.Bank, p.NewBank);
            if (p.Target == "engine" && s.Common) Replace(dat, CommonBank, p.NewCommon);
            yield return new(@"\audio\dat\" + newFile.ToLowerInvariant(), dat);
        }
    }

    public const string BankList = @"\audio\datbanklists.wbl";

    /// the game's list of which banks each .dat file needs (read once while it starts): a line per .dat copy, its game line with
    /// the bank swapped for the copy's ("Toyota_i6_engine.dat,Toyota_i6.baf,Car_Common.baf,;" ->
    /// "zb0ota_i6_engine.dat,zb0ota_i6.baf,Car_Common.baf,;")
    public static string AddToBankList(string list, Plan p) => AddToBankList(list, p, false);

    public static string AddToBankList(string list, Plan p, bool common)
    {
        var lines = list.Split(';').ToList();
        var add = new List<string>();
        foreach (var (_, _, oldFile, newFile) in p.Dats)
        {
            if (lines.Any(l => l.Trim().StartsWith(newFile + ",", StringComparison.OrdinalIgnoreCase)) || add.Any(a => a.StartsWith(newFile + ",", StringComparison.OrdinalIgnoreCase))) continue;
            var line = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(oldFile + ",", StringComparison.OrdinalIgnoreCase));
            if (line == null) { add.Add($"{newFile},{p.NewBank}.baf,"); continue; }
            var parts = line.Split(',').ToList();
            parts[0] = newFile;
            for (int i = 1; i < parts.Count; i++)
            {
                if (parts[i].Equals(p.Bank + ".baf", StringComparison.OrdinalIgnoreCase)) parts[i] = p.NewBank + ".baf";
                else if (common && parts[i].Equals(CommonBank + ".baf", StringComparison.OrdinalIgnoreCase)) parts[i] = p.NewCommon + ".baf";
            }
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
            string newName = Rename(p.Engine, p.Car);
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
    /// (look only) every sound a car can play: for each of its sound fields, the entity, its .dat files, the banks the game's
    /// bank list gives each .dat, and which sounds of those banks the .dat names
    public static List<string> UsedSounds(string car, Pak root)
    {
        var o = new List<string>();
        string xt = Path.Combine(Paths.Unpacked, "xt");
        var en = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audioentities.bin")));
        var au = new Kslf(File.ReadAllBytes(Path.Combine(xt, "audio.bin")));
        var list = B.Latin1.GetString(root.Read(BankList) ?? Array.Empty<byte>()).Split(';').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var banks = new Dictionary<string, SoundBank?>(StringComparer.OrdinalIgnoreCase);
        SoundBank? Bank(string n) { if (!banks.TryGetValue(n, out var b)) { var d = root.Read(@"\audio\" + n.ToLowerInvariant()); banks[n] = b = d != null ? SoundBank.Read(d) : null; } return b; }
        var seenDat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in new[] { "vehicleAudioEntity", "aiVehicleAudioEntity", "sharedAudioEntity", "aiSharedAudioEntity", "dryWheelAudioEntity", "wetWheelAudioEntity", "aiDryWheelAudioEntity", "aiWetWheelAudioEntity" })
        {
            uint proxy;
            try { proxy = CarSoundPreview.ProxyOf(car, field); } catch (Exception) { o.Add($"{field}: none"); continue; }
            Dictionary<string, object?>? def = null;
            try { uint du = OneRef(au, au.ObjOf(proxy)); def = en.Top(en.Objects[en.ObjOf(du)]); }
            catch (Exception) { }
            if (def == null) { o.Add($"{field}: {proxy:X8} (no entity)"); continue; }
            o.Add($"{field}: entity {def.GetValueOrDefault("name")}");
            foreach (uint u in AudioObjects(def).Distinct())
            {
                string? file = null;
                try { file = au.Top(au.Objects[au.ObjOf(u)]).TryGetValue("filename", out var f) ? f as string : null; } catch (Exception) { }
                if (file == null) continue;
                var line = list.FirstOrDefault(l => l.StartsWith(file + ",", StringComparison.OrdinalIgnoreCase));
                var bankNames = line?.Split(',').Skip(1).Where(x => x.EndsWith(".baf", StringComparison.OrdinalIgnoreCase)).ToList() ?? new();
                if (!seenDat.Add(file)) { o.Add($"   {file} (above)"); continue; }
                var dat = root.Read(@"\audio\dat\" + file.ToLowerInvariant());
                if (dat == null) { o.Add($"   {file}: missing"); continue; }
                string txt = B.Latin1.GetString(dat);
                o.Add($"   {file}: banks {string.Join(" ", bankNames)}");
                foreach (var bn in bankNames)
                {
                    var b = Bank(bn);
                    if (b == null) continue;
                    var used = b.Waves.Where(w => txt.Contains(w.Name, StringComparison.Ordinal)).Select(w => w.Name).ToList();
                    o.Add($"      {bn}: {used.Count} of {b.Waves.Count} named: {string.Join(", ", used)}");
                }
            }
        }
        return o;
    }

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

// "Game tweaks" (Blur REBORN only: they go into REBORN's own packs): more AI racers in LAN / private games, harder AI.
// The game's AI count in a network custom race is the smallest of: the free places, the race's "Add AI" setting, and
// players x max_ai_per_console (6: one player = 6 AI). This raises that per-player limit.
namespace Bmb.Core;

public static class GameTweaks
{
    public const int MaxRacers = 20;
    /// the data folder the tweaks are read from: null = the player's (Paths.Data); a build with Game tweaks not ticked sets the
    /// last install's copy (Installed.TweaksDir) while it runs
    public static string? From;
    public static string DataDir => From ?? Paths.Data;
    static string FileOf => Path.Combine(DataDir, "tweaks.json");

    public static JObj Load()
    {
        try { if (File.Exists(FileOf) && Json.Load(FileOf) is JObj o) return o; } catch (Exception) { }
        return new JObj();
    }

    public static void Save(JObj t)
    {
        Directory.CreateDirectory(Paths.Data);
        File.WriteAllText(Path.Combine(Paths.Data, "tweaks.json"), Json.Dumps(t, 1));
    }

    /// the power-up icons also in normal Blur (its cache root.pak; only on this PC - other players see the game's icons)
    public static bool IconsNormal(JObj t) => t.Truthy("icons_normal");

    /// AI racers per player (19 = every free place can be AI); 0 = the game's own (6)
    public static int AiPerPlayer(JObj t) => t.Truthy("more_ai") ? Math.Clamp((int)t.Int("ai_per_player", MaxRacers - 1), 1, MaxRacers - 1) : 0;

    static int Config(Db mp) => Enumerable.Range(0, mp.K.Objects.Count).First(i => mp.TName(i) == "MultiplayerConfig");

    /// (test) the AI fields of the multiplayer config
    public static IEnumerable<string> Dump(byte[] mpD)
    {
        var mp = new Db(mpD);
        int mi = Config(mp), t = mp.K.Objects[mi].Blocks[0].Type;
        foreach (var f in mp.K.AllFields(t))
            if (f.Name.Contains("ai", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("player", StringComparison.OrdinalIgnoreCase))
            {
                string v;
                try { v = mp.Get(mi, f.Name).ToString(System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { v = "?"; }
                yield return $"{f.Name} ({mp.K.Types[f.Type].Name}) = {v}";
            }
    }

    /// the multiplayer config with its AI-per-player limit set
    internal static byte[] WithAiPerPlayer(byte[] mpD, int n)
    {
        var mp = new Db(mpD);
        int mi = Config(mp), t = mp.K.Objects[mi].Blocks[0].Type;
        return KslfWrite.EditObject(mpD, mi, c => mp.Set(c.Data, c.Blocks[0], t, "max_ai_per_console", n));
    }

    public const string HardAiFile = "gamedata/events/multiplayer_hard.csv";
    const int BossSkill = 70;          // the AI skill template of the hardest boss race (bossbattlet5_hard)

    /// the multiplayer Hard AI file made harder: every AI at full track speed, a higher lowest speed, the boss skill.
    /// The file: 3 header lines + a blank one, then per race section 9 lines (to, distance, max track speed, min track
    /// speed, on, group, skill template, power-up template, blank); column 0 is the player.
    public static string HarderAi(string csv)
    {
        var lines = csv.Replace("\r", "").Split('\n').ToList();
        for (int i = 4; i + 7 < lines.Count; i += 9)
        {
            string Map(string line, Func<double, double> f) => string.Join(",", line.Split(',').Select((v, c) =>
                c == 0 || !double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) || x <= 0 ? v
                : f(x).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            lines[i + 2] = Map(lines[i + 2], _ => 1.0);
            lines[i + 3] = Map(lines[i + 3], x => Math.Max(x, 0.65));
            lines[i + 6] = Map(lines[i + 6], _ => BossSkill);
        }
        return string.Join("\r\n", lines);
    }

    // ---- AI mods: Blur REBORN.exe only (network AI racers get only base values in the game code; this gives them a mod
    // loadout through the same game function the players' mods use, in custom races with Mods on).
    // The player's shop rules: slot 1 = Front Runner (Drifter for the first two AI is ON HOLD 2026-11-17: the setup code
    // compares the AI number with 0 instead of 2, so no AI is a Drifter; the drift driving and drift-car swap stay unused); slot 2 = Splash Damage /
    // Decoy Drop / Shielding Efficiency; slot 3 = Silent Running / Mastermine / ECM (picked per AI number from a fixed
    // random table, so every PC in the race gives each AI the same mods).
    // The two Drifter AI also drift: the game's AI drive with extra grip forces and never slide, so their driving step gets
    // a handbrake tap into fast corners and less grip help while they turn (see the drift patch below).
    // Counters and settings live in unused memory after the TLS template (VA 0x1259F00..), readable from the running game.
    // Every player in the race needs the same setting: each game sets up the AI cars itself.
    static byte[] H(string s) => Convert.FromHexString(s);
    static byte[] Cc(int n) => Enumerable.Repeat((byte)0xCC, n).ToArray();
    static readonly (int Off, byte[] Old, byte[] New, byte[][] Before)[] AiModPatches =
    {
        // AI setup: the AI's call to OfflineValues -> the new setup code
        (0x7F9502, H("e8492dfcff"), H("e8199a1f00"), Array.Empty<byte[]>()),
        // setup code (int3 gap, VA 0xDF3B20): mods per AI, and the two Drifter AI's car numbers as bits at VA 0x1259F08
        (0x9F2F20, Cc(224), H(
            "51e82a93dcff59ff05009f250189d82b442418750ac705089f2501000000008b542414837a2801756e803d05e92301017565" +
            "ff05049f250183f8137605b81300000083ec0cc704240200000083f8007313c70424030000000fab1d089f2501e8dcb2ffff" +
            "0fb690c03bdf00895424040fb690d43bdf008954240889e283e9385152e85a7a63ff89c1e8137b63ff89c1e87c25dcff83c4" +
            "0cc3cccccccccccccccc0b080c0c0c0b0b0c0c0c080c0c0c080c080b0b0c1515171517151017101715171717171710101015" +
            "cccccccccccccccccccccccccccccccccccccccccccccccc"),
            new[] { H(
            "51e82a93dcff59ff05009f250189d82b442418750ac705089f2501000000008b542414837a2801756e803d05e92301017565" +
            "ff05049f250183f8137605b81300000083ec0cc704240200000083f8027313c70424030000000fab1d089f2501e8dcb2ffff" +
            "0fb690c03bdf00895424040fb690d43bdf008954240889e283e9385152e85a7a63ff89c1e8137b63ff89c1e87c25dcff83c4" +
            "0cc3cccccccccccccccc0b080c0c0c0b0b0c0c0c080c0c0c080c080b0b0c1515171517151017101715171717171710101015" +
            "cccccccccccccccccccccccccccccccccccccccccccccccc"), H(
            "51e82a93dcff59ff05009f250189d82b442418750ac705089f2501000000008b542414837a28017569803d05e92301017560" +
            "ff05049f250183f8137605b81300000083ec0cc704240200000083f802730ec70424030000000fab1d089f25010fb690c03b" +
            "df00895424040fb690d43bdf008954240889e283e9385152e85f7a63ff89c1e8187b63ff89c1e88125dcff83c40cc3cccccc" +
            "cccccccccccccccccccc0b080c0c0c0b0b0c0c0c080c0c0c080c080b0b0c1515171517151017101715171717171710101015" +
            "cccccccccccccccccccccccccccccccccccccccccccccccc"), H(
            "51e82a93dcff598b542414837a28017540803d05e9230101753789d82b44241883f8027307ba803bdf00eb0d83e0038d0440" +
            "8d1485903bdf0083e9385152e89d7a63ff89c1e8567b63ff89c1e8bf25dcffc3cccccccccccccccccccccccccccc03000000" +
            "040000000d000000cccccccc00000000070000000c00000002000000040000000b000000010000000600000010000000070000" +
            "000e00000016000000").Concat(Cc(64)).ToArray(), H(
            "51e82a93dcff59ff05009f25018b542414837a28017568803d05e9230101755fff05049f250189d82b44241883f8137605b8" +
            "1300000083ec0cc704240200000083f8027307c70424030000000fb690c03bdf00895424040fb690d43bdf008954240889e2" +
            "83e9385152e8727a63ff89c1e82b7b63ff89c1e89425dcff83c40cc3cccccccccccccccccccccccccccccccccccccccccccc" +
            "cccccccccccccccccccc0b080c0c0c0b0b0c0c0c080c0c0c080c080b0b0c1515171517151017101715171717171710101015" +
            "cccccccccccccccccccccccccccccccccccccccccccccccc") }),
        // AI driving step (VA 0x4C2B7A): the call to the AI's steering-forces helper goes through the drift code
        (0xC1F7A, H("e821b0ffff"), H("e801fd9800"), Array.Empty<byte[]>()),
        // drift code (unused zeros at the end of the code, VA 0xE52880, 384 bytes): for a Drifter AI going faster than
        // 27 m/s that steers harder than 0.3, a handbrake tap (6 steps) and, until it steers less than 0.08, full
        // throttle and no brake, 4x the steering turn force and 0.4x the cornering grip help - so it slides through the corner. Settings live
        // at VA 0x1259F20 (speed, steer on, steer off, turn x, grip x, tap steps) and can be changed while the game runs.
        (0xA51C80, new byte[384], H(
            "8b44247c83e01f0fa305089f25010f8346010000833d109f2501007521565751bee429e500bf209f2501b906000000f3a559" +
            "5f5ec705109f250101000000535689c389ce8b4c240c8b01ff501489c18b01ff5014f30f1080340100008b5424148b421025" +
            "ffffff7f660f6ec880bb409f250100752c0f2f05209f2501764a0f2f0d249f25017641c683409f250101a1349f2501888360" +
            "9f2501ff050c9f2501eb300f2f0d289f25017217f30f1015209f2501f30f5915fc29e5000f2fc27202eb10c683409f250100" +
            "89f15e5be98900000080bb609f250100740afe8b609f2501c6421801c7420c00000000c742080000803fff35444a1301ff35" +
            "7c4a1301f30f1005444a1301f30f59052c9f2501f30f1105444a1301f30f10057c4a1301f30f5905309f2501f30f11057c4a" +
            "130189f1ff742424ff742424ff742424ff742424ff742424e8c73dfaff8f057c4a13018f05444a13015e5bc21400e9c1b166" +
            "ffcccccccccc0000d8419a99993e0ad7a33d00008040cdcccc3e060000009a99193f"),
            new[] { H(
            "8b44247c83e01f0fa305089f25010f8346010000833d109f2501007521565751bee429e500bf209f2501b906000000f3a559" +
            "5f5ec705109f250101000000535689c389ce8b4c240c8b01ff501489c18b01ff5014f30f1080340100008b5424148b421025" +
            "ffffff7f660f6ec880bb409f250100752c0f2f05209f2501764a0f2f0d249f25017641c683409f250101a1349f2501888360" +
            "9f2501ff050c9f2501eb300f2f0d289f25017217f30f1015209f2501f30f5915fc29e5000f2fc27202eb10c683409f250100" +
            "89f15e5be98900000080bb609f250100740afe8b609f2501c6421801c7420c00000000c742080000803fff35444a1301ff35" +
            "7c4a1301f30f1005444a1301f30f59052c9f2501f30f1105444a1301f30f10057c4a1301f30f5905309f2501f30f11057c4a" +
            "130189f1ff742424ff742424ff742424ff742424ff742424e8d7b166ff8f057c4a13018f05444a13015e5bc21400e9c1b166" +
            "ffcccccccccc0000d8419a99993e0ad7a33d00008040cdcccc3e0a0000009a99193f"), H(
            "8b44247c83f81f0f87420100000fa305089f25010f8335010000833d109f2501007521565751bed829e500bf209f2501b906" +
            "000000f3a5595f5ec705109f250101000000535689c389ce8b4c240c8b01ff501489c18b01ff5014f30f1080340100008b54" +
            "24148b421025ffffff7f660f6ec880bb409f250100752c0f2f05209f2501764a0f2f0d249f25017641c683409f250101a134" +
            "9f25018883609f2501ff050c9f2501eb2d0f2f0d289f25017217f30f1015209f2501f30f5915f029e5000f2fc27202eb0dc6" +
            "83409f25010089f15e5beb7b80bb609f250100740afe8b609f2501c6421801ff35444a1301ff357c4a1301f30f1005444a13" +
            "01f30f59052c9f2501f30f1105444a1301f30f10057c4a1301f30f5905309f2501f30f11057c4a130189f1ff742424ff7424" +
            "24ff742424ff742424ff742424e8e2b166ff8f057c4a13018f05444a13015e5bc21400e9ccb166ffcccccccc0000a0419a99" +
            "993ecdcc4c3d00000041000000002d0000009a99193f000000000000000000000000"), H(
            "8b44247c83f81f0f87420100000fa305089f25010f8335010000833d109f2501007521565751bed829e500bf209f2501b906" +
            "000000f3a5595f5ec705109f250101000000535689c389ce8b4c240c8b01ff501489c18b01ff5014f30f1080340100008b54" +
            "24148b421025ffffff7f660f6ec880bb409f250100752c0f2f05209f2501764a0f2f0d249f25017641c683409f250101a134" +
            "9f25018883609f2501ff050c9f2501eb2d0f2f0d289f25017217f30f1015209f2501f30f5915f029e5000f2fc27202eb0dc6" +
            "83409f25010089f15e5beb7b80bb609f250100740afe8b609f2501c6421801ff35444a1301ff357c4a1301f30f1005444a13" +
            "01f30f59052c9f2501f30f1105444a1301f30f10057c4a1301f30f5905309f2501f30f11057c4a130189f1ff742424ff7424" +
            "24ff742424ff742424ff742424e8e2b166ff8f057c4a13018f05444a13015e5bc21400e9ccb166ffcccccccc0000b0413333" +
            "b33e8fc2f53d000040403333b33e0c0000009a99193f000000000000000000000000") }),
        // drift-car swap (int3 gap, VA 0xDEEE60, called by the setup code for the two Drifter AI): their car becomes the
        // drift car of its class (A Mustang GT-R, B Challenger Tuner, C Shelby GT500, D ACS1 Coupe) - looked up in the
        // table below by the top 16 bits of the car's uid; a car not in the table (custom cars) stays as it is
        (0x9EE260, Cc(160), H(
            "5052568b51ccc1ea1031f666391475583fdf0074084683fe3c72f0eb2989f0c1e8020fb690d03fdf0089f083e003518d0c45" +
            "00000000d3ea5983e2038b1495e03fdf008951cc5e5a58c3cccccccccccccccccccccccccccccccccccccccccccccccccccc" +
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" +
            "cccccccccccccccccccc"), Array.Empty<byte[]>()),
        // its table (int3 gap, VA 0xDF3F58): the 60 multiplayer cars' uid keys, their class (2 bits each), the 4 drift cars
        (0x9F3358, Cc(168), H(
            "52004609d80b2d0d4c12bf1318142521ac24462afb2c46301b33d0369341c5417e4222437f49844a724bd657cf5c5a683e6a" +
            "a271d07d867ee67f6a835089b390329132949f948598019f89a118a80faa78b02bb3f2b66ab90eba26c50bc762cfbecfb0d5" +
            "74dd43de10e586ea0ef08af3abf61afca4fdfefff08d19a1fae78b45ce0d558637288fccff9610e553cc6ab9955f86eabfe0" +
            "724bcccccccccccccccccccccccccccccccc"), Array.Empty<byte[]>()),
        // drift speed keeper (int3 gap, VA 0xDF6790; the drift code calls the steering-forces helper through it): while a
        // Drifter AI drifts, a forward push (setting at VA 0x1259F38, 37500 = 1.5x the push the game gives AI that are
        // behind) so the slide does not cost its speed
        (0x9F5B90, Cc(112), H(
            "8b15389f250185d2750bba007c12478915389f250151ff742418ff742418ff742418ff742418ff742418e8e1736cff59f30f" +
            "104120f30f5905943ce600f30f5905389f25018b442414f30f584008f30f114008c21400cccccccccccccccccccccccccccc" +
            "cccccccccccccccccccccccc"), Array.Empty<byte[]>()),
    };
    /// (test) where the counters are in the running game (VA): AI set up, AI given mods, Drifter bits, drifts started
    public const int AiModsCounters = 0x1259F00;

    public static bool AiMods(JObj t) => t.Truthy("ai_mods");

    /// Blur REBORN.exe with the AI-mods patch on or off (written in place: other changes in that file stay). Returns a
    /// line for the log, or null when nothing changed
    public static string? SetAiMods(bool on)
    {
        string exe = Path.Combine(Paths.Game, "Blur REBORN.exe");
        if (!File.Exists(exe)) return null;
        var d = File.ReadAllBytes(exe);
        bool Is(byte[] want, int off) => off + want.Length <= d.Length && d.AsSpan(off, want.Length).SequenceEqual(want);
        bool allNew = AiModPatches.All(p => Is(p.New, p.Off)), allOld = AiModPatches.All(p => Is(p.Old, p.Off));
        if (on ? allNew : allOld) return null;
        if (!AiModPatches.All(p => Is(p.Old, p.Off) || Is(p.New, p.Off) || p.Before.Any(b => Is(b, p.Off))))
            return "game tweaks: AI mods - Blur REBORN.exe is not the version this was made for, left as it is";
        foreach (var p in AiModPatches) (on ? p.New : p.Old).CopyTo(d, p.Off);
        File.WriteAllBytes(exe, d);
        return on ? "game tweaks: AI mods on (Blur REBORN.exe: AI racers get mods in custom races with Mods on: Front Runner + 2 random mods)"
                  : "game tweaks: AI mods off (Blur REBORN.exe back to plain AI)";
    }

    /// Build: the tweaks into the files of REBORN's gamedata pack
    internal static void Apply(Dictionary<string, byte[]> files, Action<string> log)
    {
        var t = Load();
        try { if (SetAiMods(AiMods(t)) is { } l) log(l); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log("game tweaks: could not change Blur REBORN.exe (is it running?): " + e.Message); }
        if (t.Truthy("harder_ai"))
        {
            using var p = new Pak(Paths.GamedataOriginal);
            var d = p.Read("\\" + HardAiFile.Replace('/', '\\'));
            if (d != null)
            {
                files[HardAiFile] = System.Text.Encoding.ASCII.GetBytes(HarderAi(System.Text.Encoding.ASCII.GetString(d)));
                log("game tweaks: harder AI (Hard in LAN / private custom races: full speed, the boss skill)");
            }
        }
        int ai = AiPerPlayer(t);
        if (ai > 0)
        {
            const string mpName = "gamedata/xt/multiplayer.bin";
            byte[] mp = files.TryGetValue(mpName, out var d) ? d : File.ReadAllBytes(Path.Combine(Paths.Unpacked, "xt", "multiplayer.bin"));
            files[mpName] = WithAiPerPlayer(mp, ai);
            log($"game tweaks: up to {ai} AI racers per player (LAN / private games)");
        }
    }
}

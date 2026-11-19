// Animated objects of our own maps -> actors the game plays (docs\object_animation.md, "How we export").
// Only pieces the game itself uses in its own cities:
//   path   = DynamicObjActor + SplineMovementComponent (NY subway trains, Long Beach boats: start at once + loop)
//   trigger= TriggerRegion -> ActivateMessage -> MessageScript -> MovementSplineMessage (LA Downtown police cars)
//   keys   = one 2-point path per key pair, a MessageScript sends each with speed = length / time, then waits
//   spin   = SpinnerMovementComponent + MovementSpinUpYAxis from a MessageScript (LA River hazard lights)
// The object's "anim" in world.json:
//   {"mode": "path"|"keys"|"spin", "start": "race"|"trigger", "loop": true,
//    "path": [[x,y,z]...], "closed": false, "speed": 20, "angle": 0,            (path)
//    "keys": [{"t": 0, "x": .., "y": .., "z": .., "angle": 0}...],             (keys: t in seconds, angle = turn in degrees)
//    "rpm": 20,                                                                 (spin)
//    "trigger": {"x": .., "y": .., "z": .., "w": 40, "h": 20, "d": 40}}         (start = trigger: a box the car drives through)
using System.Text;

namespace Bmb.Core;

public static class AnimExport
{
    // types our actor pack (a copy of Mount Haruna's) lacks, and a game city that has each
    static readonly (string City, string[] Types)[] Donors =
    {
        ("usa_ny_dumbo", new[] { "SplineMovementComponent" }),
        ("usa_la_downtown", new[] { "MovementSplineMessage", "MessageScript", "MessageScriptMessageItem", "MessageScriptDelayItem", "DelayedSendMessage", "SendMessage", "ParamActorLevelActor", "ParamFloatVal", "ParamBoolVal", "ActivateMessage", "TriggerRegion" }),
        ("usa_la_river", new[] { "SpinnerMovementComponent", "MovementSpinUpYAxis" }),
    };

    public static bool Any(IEnumerable<TileSpot> tiles) => tiles.Any(t => t.Anim != null);
    /// what the last build did (Build logs and clears it)
    public static readonly List<string> Notes = new();

    /// our pack + every type the animations need (copied from the game's own city packs)
    public static byte[] WithTypes(byte[] pack, Func<string, byte[]> cityPack)
    {
        foreach (var (city, types) in Donors) pack = KslfBuild.AddTypes(pack, cityPack(city), types);
        return pack;
    }

    public static byte[] CityPack(string city)
    {
        using var P = new Pak(Paths.GamedataOriginal);
        return P.Read($"gamedata/levels/{city}/actorpack.xt") ?? throw new FileNotFoundException(city);
    }

    sealed class Ids { uint salt; int n; public Ids(uint s) { salt = s; } public long Next() => (long)(Pak.Crc32Of(Encoding.ASCII.GetBytes($"anim:{salt:X8}:{n++}")) | 0x80000000); }

    static Dictionary<string, object?> V(double x, double y, double z) => new() { ["vx"] = x, ["vy"] = y, ["vz"] = z };
    static Dictionary<string, object?> Mat(double x, double y, double z, double turn = 0)
    {
        double c = Math.Cos(turn), s = Math.Sin(turn);
        return new() { ["ax"] = V(c, 0, -s), ["ay"] = V(0, 1, 0), ["az"] = V(s, 0, c), ["aw"] = V(x, y, z) };
    }
    static Dictionary<string, object?> P(Dictionary<string, object?>? actor) => new() { ["_type"] = "ParamActorLevelActor", ["Actor"] = actor };
    static Dictionary<string, object?> F(double v) => new() { ["_type"] = "ParamFloatVal", ["Value"] = v };
    static Dictionary<string, object?> Bo(bool v) => new() { ["_type"] = "ParamBoolVal", ["value"] = v ? 1L : 0L };
    static Dictionary<string, object?> Send(Dictionary<string, object?>? actor, Dictionary<string, object?>? msg) => new() { ["Actor"] = P(actor), ["Disable"] = 0L, ["DebugBreak"] = 0L, ["Message"] = msg };

    /// a path actor through world points (Catmull-Rom tangents x 0.6, as the game's own paths; knots relative to the first point)
    static Dictionary<string, object?> Spline(Ids ids, string name, IList<double[]> pts, bool closed)
    {
        var o = pts[0]; int n = pts.Count;
        var knots = new List<object?>();
        for (int i = 0; i < n; i++)
        {
            double[] a = i > 0 ? pts[i - 1] : closed ? pts[n - 1] : pts[i], b = i < n - 1 ? pts[i + 1] : closed ? pts[0] : pts[i];
            double k = (i > 0 && i < n - 1) || closed ? 0.3 : 0.6;   // an end: one-sided difference, same length
            var t = V((b[0] - a[0]) * k, (b[1] - a[1]) * k, (b[2] - a[2]) * k);
            knots.Add(new Dictionary<string, object?> { ["position"] = V(pts[i][0] - o[0], pts[i][1] - o[1], pts[i][2] - o[2]), ["tangentIn"] = t, ["tangentOut"] = t, ["tangentScale"] = 1.0 });
        }
        return new()
        {
            ["_type"] = "SplineActor", ["SerialisableUid"] = ids.Next(), ["Name"] = name, ["IsCommented"] = 0L, ["WorldMatrix"] = Mat(o[0], o[1], o[2]),
            ["SplineData"] = new Dictionary<string, object?> { ["reparamRes"] = 0L, ["closed"] = closed ? 1L : 0L, ["knots"] = knots, ["knotData"] = new List<object?>() },
            ["Attacher"] = new Dictionary<string, object?> { ["SerialisableUid"] = ids.Next(), ["ParentActorCreate"] = null, ["DopplerSpeed"] = 0.0 },
        };
    }

    static double Len(IList<double[]> p) { double s = 0; for (int i = 1; i < p.Count; i++) s += Math.Sqrt(Math.Pow(p[i][0] - p[i - 1][0], 2) + Math.Pow(p[i][1] - p[i - 1][1], 2) + Math.Pow(p[i][2] - p[i - 1][2], 2)); return s; }

    /// restart = start again at the end (only for scripts that start with the race: every script the game's own cities start
    /// from a trigger box has RestartWhenFinished = 0, and a triggered keyframe loop with it did not start in the game test)
    static Dictionary<string, object?> Script(Ids ids, string name, bool startRunning, bool restart, bool oneShot, List<object?> seq) => new()
    {
        ["_type"] = "MessageScript", ["SerialisableUid"] = ids.Next(), ["Name"] = name, ["IsCommented"] = 0L,
        ["ScriptComponent"] = new Dictionary<string, object?>
        {
            ["SerialisableUid"] = ids.Next(), ["StartRunning"] = startRunning ? 1L : 0L, ["StartDisabled"] = 0L,
            ["RestartWhenFinished"] = restart ? 1L : 0L, ["OneShot"] = oneShot ? 1L : 0L, ["Sequence"] = seq,
        },
    };
    static Dictionary<string, object?> SendItem(params Dictionary<string, object?>[] sends) => new()
    {
        ["_type"] = "MessageScriptMessageItem", ["Disable"] = 0L,
        ["Messages"] = sends.Select(s => (object?)new Dictionary<string, object?> { ["Message"] = s, ["Delay"] = 0.0 }).ToList(),
    };
    static Dictionary<string, object?> Wait(double sec) => new() { ["_type"] = "MessageScriptDelayItem", ["Disable"] = 0L, ["DelayTime"] = F(sec) };
    static Dictionary<string, object?> MoveMsg(Dictionary<string, object?> spline, double speed, double angle, bool loop) => new()
    {
        ["_type"] = "MovementSplineMessage", ["SplineActor"] = P(spline), ["MoveSpeed"] = F(speed), ["RotationAngle"] = F(angle), ["Loop"] = Bo(loop), ["MovementType"] = 0L,
    };

    static Dictionary<string, object?> Trigger(Ids ids, string name, JObj t, Dictionary<string, object?> script) => new()
    {
        ["_type"] = "TriggerRegion", ["SerialisableUid"] = ids.Next(), ["Name"] = name, ["IsCommented"] = 0L,
        // the game puts the box's CENTRE at WorldMatrix + half the size (its position is the box's low corner); the
        // editor's x, y, z is the centre, so the corner is the centre minus half the size
        ["WorldMatrix"] = Mat(t.Num("x") - t.Num("w", 40) / 2, t.Num("y") - t.Num("h", 20) / 2, t.Num("z") - t.Num("d", 40) / 2),
        ["Box"] = new Dictionary<string, object?> { ["Orientation"] = Mat(0, 0, 0), ["Size"] = V(t.Num("w", 40), t.Num("h", 20), t.Num("d", 40)) },
        ["Type"] = 0L, ["RefNumber"] = 0L, ["DestroyWhenTriggered"] = 0L, ["PlayerControlled"] = 1L, ["AiControlled"] = 0L, ["MinLap"] = 0L, ["MaxLap"] = 0L,
        ["OnTriggered"] = Send(script, new() { ["_type"] = "ActivateMessage" }), ["OnExited"] = Send(null, null),
        ["VehicleMessage"] = null, ["VehicleMessageOnExit"] = null,
    };

    /// the game's path mover leads with the object's back (game test 2026-11-19: a van drove backwards), the editor shows the
    /// front leading: half a turn more than the editor's turn (degrees)
    public static double GameAngle(double editorDeg) { double v = (editorDeg + 180) % 360; return v > 180 ? v - 360 : v <= -180 ? v + 360 : v; }

    static List<double[]> Pts(JObj a, string key) => (a.Arr(key) ?? new()).Select(p => ((List<object?>)p!).Select(v => Convert.ToDouble(v)).ToArray()).ToList();

    /// the tiles list object `obj` of `pack` with the animations of `tiles` (the k-th DynamicObjActor = tiles[k])
    public static byte[] Apply(byte[] pack, int obj, IList<TileSpot> tiles, uint salt, Action<string>? log = null)
    {
        var K = new Kslf(pack);
        var top = KslfBuild.ReadTree(K, obj, out var byBlock);
        var o = K.Objects[obj];
        int blk = o.Blocks.FindIndex(b => b.Name == "DynamicObjActor" && b.Kind == 0);
        var actorsList = (List<object?>)top["LevelActors"]!;
        var ids = new Ids(salt);
        int count = blk < 0 ? 0 : o.Blocks[blk].Count;
        for (int k = 0; k < Math.Min(count, tiles.Count); k++)
        {
            var an = tiles[k].Anim;
            if (an == null || !byBlock.TryGetValue((blk, k), out var actor)) continue;
            string mode = an.Str("mode") ?? "path", nm = $"anim{k}";
            var wm = (Dictionary<string, object?>)((Dictionary<string, object?>)actor["WorldMatrix"]!)["aw"]!;
            log?.Invoke($"animation: tile {k} at ({tiles[k].X:0}, {tiles[k].Z:0}), actor {actor.GetValueOrDefault("Name")} at ({Convert.ToDouble(wm["vx"]):0}, {Convert.ToDouble(wm["vz"]):0})");
            // our tiles are copies of Mount Haruna's flashing cones (EnableRespawn 1, AnimStartTimer 0.3): such an object
            // ignores a trigger's move message until a car hits it (game test 2026-11-19). Every mover of the game's
            // own cities (47 in 7 cities, the trigger-started LA police cars too) has 0 / 0.0.
            actor["EnableRespawn"] = 0L; actor["AnimStartTimer"] = 0.0;
            bool trig = an.Str("start") == "trigger" && an.Obj("trigger") != null, loop = an.Truthy("loop");
            Dictionary<string, object?>? script = null;
            if (mode == "spin")
            {
                double rpm = an.Num("rpm", 20);
                actor["MovementComponent"] = new Dictionary<string, object?> { ["_type"] = "SpinnerMovementComponent", ["SerialisableUid"] = ids.Next(), ["ParentActorCreate"] = null, ["InitialRpm"] = trig ? 0.0 : rpm };
                script = Script(ids, nm + "_script", !trig, false, true, new() { SendItem(Send(actor, new() { ["_type"] = "MovementSpinUpYAxis", ["RPM"] = F(rpm), ["Time"] = F(0.5), ["MovementType"] = 0L })) });
                log?.Invoke($"animation: object {k} spins {rpm:0} rpm" + (trig ? " (trigger)" : ""));
            }
            else if (mode == "keys")
            {
                var keys = AnimEval.ExportKeys(an);
                if (keys.Count < 2) continue;
                var pts = keys.Select(q => new[] { q.Num("x"), q.Num("y"), q.Num("z") }).ToList();
                var seq = new List<object?>(); Dictionary<string, object?>? first = null; double firstSpeed = 0;
                for (int i = 0; i + 1 < keys.Count; i++)
                {
                    // the segment with its neighbours' directions, so the joins stay smooth
                    var seg = new List<double[]> { pts[i], pts[i + 1] };
                    var sp = Spline(ids, $"{nm}_seg{i}", seg, false);
                    var kn = (List<object?>)((Dictionary<string, object?>)sp["SplineData"]!)["knots"]!;
                    for (int e = 0; e < 2; e++)
                    {
                        int j = i + e;
                        double[] a = pts[Math.Max(j - 1, 0)], b = pts[Math.Min(j + 1, pts.Count - 1)];
                        double kk = j > 0 && j < pts.Count - 1 ? 0.3 : 0.6;
                        var t = V((b[0] - a[0]) * kk, (b[1] - a[1]) * kk, (b[2] - a[2]) * kk);
                        var kd = (Dictionary<string, object?>)kn[e]!; kd["tangentIn"] = t; kd["tangentOut"] = t;
                    }
                    actorsList.Add(sp);
                    double dt = Math.Max(0.05, keys[i + 1].Num("t") - keys[i].Num("t")), speed = Math.Max(0.01, Len(seg) / dt);
                    if (first == null) { first = sp; firstSpeed = speed; }
                    seq.Add(SendItem(Send(actor, MoveMsg(sp, speed, GameAngle(keys[i].Num("angle")), false))));
                    seq.Add(Wait(dt));
                }
                actor["MovementComponent"] = new Dictionary<string, object?>
                {
                    ["_type"] = "SplineMovementComponent", ["SerialisableUid"] = ids.Next(), ["ParentActorCreate"] = null, ["Spline"] = first,
                    ["MoveSpeed"] = firstSpeed, ["RotationAngle"] = GameAngle(keys[0].Num("angle")), ["Loop"] = 0L, ["StartImmediately"] = 0L, ["StartFromNearestPoint"] = 0L,
                };
                if (trig && loop)
                {
                    // started by a trigger and looping: two plain scripts start each other (script A = the keys, then
                    // it starts B; B waits a moment, then starts A again), like the game's own triggered scripts
                    script = Script(ids, nm + "_script", false, false, false, seq);
                    var again = Script(ids, nm + "_again", false, false, false, new() { Wait(0.1), SendItem(Send(script, new() { ["_type"] = "ActivateMessage" })) });
                    seq.Add(SendItem(Send(again, new() { ["_type"] = "ActivateMessage" })));
                    actorsList.Add(again);
                }
                else script = Script(ids, nm + "_script", !trig, loop && !trig, !(loop && !trig), seq);
                log?.Invoke($"animation: object {k} keyframes ({keys.Count} keys, {keys[^1].Num("t") - keys[0].Num("t"):0.#} s)" + (loop ? ", loops" : "") + (trig ? ", trigger" : ""));
            }
            else
            {
                var pts = Pts(an, "path");
                if (pts.Count < 2) continue;
                bool closed = an.Truthy("closed");
                var sp = Spline(ids, nm + "_path", pts, closed);
                actorsList.Add(sp);
                double speed = an.Num("speed", 20), angle = GameAngle(an.Num("angle"));
                actor["MovementComponent"] = new Dictionary<string, object?>
                {
                    ["_type"] = "SplineMovementComponent", ["SerialisableUid"] = ids.Next(), ["ParentActorCreate"] = null, ["Spline"] = sp,
                    ["MoveSpeed"] = speed, ["RotationAngle"] = angle, ["Loop"] = loop ? 1L : 0L, ["StartImmediately"] = trig ? 0L : 1L, ["StartFromNearestPoint"] = trig ? 0L : 1L,
                };
                if (trig) script = Script(ids, nm + "_script", false, false, true, new() { SendItem(Send(actor, MoveMsg(sp, speed, angle, loop))) });
                log?.Invoke($"animation: object {k} on a path ({pts.Count} points, {Len(pts):0} m, {speed:0} m/s)" + (loop ? ", loops" : "") + (trig ? ", trigger" : ""));
            }
            if (script != null)
            {
                actorsList.Add(script);
                if (trig) actorsList.Add(Trigger(ids, nm + "_trigger", an.Obj("trigger")!, script));
            }
        }
        return KslfBuild.ReplaceObject(pack, obj, top);
    }
}

// A custom car's own number in the game (its uid). Online, Blur sends only this number (and its paint's) to the other
// players, and each game looks it up in its own car list. So a car shared as a .blurcar must get the SAME number on every PC,
// and two different cars must not get the same one. The number comes from "net_id" in the car (kept on export / import,
// new on New and Copy). A car from before gets a random one the first time the map builder loads it (saved at once):
// a number from the name would be the same for every player's "My car".
using System.Text;

namespace Bmb.Core;

public static class CarNetId
{
    /// the car's 4 uids (car, physics, paint, wheels) are First + 4 * slot; the range ends below the logo uids (0xCA4F0000)
    public const uint First = 0xCA400000;
    public const int Slots = 0x3C000;
    const string Key = "net_id";

    public static long New() => Random.Shared.Next(Slots);

    static int FromName(string name)
    {
        uint h = 2166136261;                                         // FNV-1a: the same on every PC
        foreach (byte b in Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant())) { h ^= b; h *= 16777619; }
        return (int)(h % Slots);
    }

    public static int Of(JObj car)
    {
        if (car.ContainsKey(Key) && car[Key] != null) { long v = car.Int(Key); if (v >= 0 && v < Slots) return (int)v; }
        return FromName(car.Str("name") ?? "");
    }

    /// has no number of its own yet (none, a broken one, or the one its name gives - what this version did first)
    static bool Unpinned(JObj car) => !car.ContainsKey(Key) || car[Key] == null || car.Int(Key) < 0 || car.Int(Key) >= Slots || car.Int(Key) == FromName(car.Str("name") ?? "");

    /// gives the car a random number of its own if it has none yet
    public static bool Pin(JObj car) { if (!Unpinned(car)) return false; car[Key] = New(); return true; }

    /// every car without a number gets one. True = something changed (save the world)
    public static bool PinAll(JObj world) { bool ch = false; foreach (var c in world.Objs("cars")) ch |= Pin(c); return ch; }

    /// a car that is put next to the player's cars (import): a new number when one of them already has its number
    public static bool Unique(JObj car, IEnumerable<JObj> others)
    {
        int me = Of(car);
        if (!others.Any(o => Of(o) == me)) { Pin(car); return true; }
        var used = others.Select(Of).ToHashSet();
        long n; do n = New(); while (used.Contains((int)n));
        car[Key] = n;
        return false;
    }

    /// (test) each custom car in a built amax.bin: its name and uid
    public static IEnumerable<string> Dump(byte[] amax)
    {
        var d = new Db(amax);
        foreach (var (n, i) in d.Cars()) if (n.StartsWith("BMB_")) yield return $"{n}: uid {d.K.Refs.First(r => r.Obj == i).Uid:X8}";
    }

    /// the first uid of each car, in order. A number one of the game's records already uses, or that an earlier car in the
    /// list took, moves on to the next free one (the game's data is the same on every PC, so that is the same everywhere too)
    internal static List<uint> Uids(IList<JObj> cars, params byte[][] dbs)
    {
        var taken = new HashSet<uint>();
        foreach (var d in dbs) foreach (var r in new Db(d).K.Refs) taken.Add(r.Uid);
        var o = new List<uint>();
        foreach (var c in cars)
        {
            int s = Of(c);
            for (int k = 0; k < Slots; k++, s = (s + 1) % Slots)
            {
                uint u = First + 4 * (uint)s;
                if (!taken.Contains(u) && !taken.Contains(u + 1) && !taken.Contains(u + 2) && !taken.Contains(u + 3)) break;
            }
            uint uid = First + 4 * (uint)s;
            for (uint q = 0; q < 4; q++) taken.Add(uid + q);
            o.Add(uid);
        }
        return o;
    }
}

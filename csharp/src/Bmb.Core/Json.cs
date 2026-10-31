// JSON that behaves like Python's json module (maps stay byte-compatible with the Python program) and Python's Random.
// Values: Dictionary<string, object?> (JObj), List<object?>, long, double, string, bool, null. Unknown keys always survive.
using System.Globalization;
using System.Text;

namespace Bmb.Core;

public sealed class JObj : Dictionary<string, object?>
{
    public JObj() { }
    public new object? this[string k] { get => TryGetValue(k, out var v) ? v : null; set => base[k] = value; }
    public double Num(string k, double dflt = 0) => TryGetValue(k, out var v) && v != null ? Json.D(v) : dflt;
    public long Int(string k, long dflt = 0) => TryGetValue(k, out var v) && v != null ? Json.L(v) : dflt;
    public string? Str(string k) => TryGetValue(k, out var v) ? v as string : null;
    public List<object?>? Arr(string k) => TryGetValue(k, out var v) ? v as List<object?> : null;
    public JObj? Obj(string k) => TryGetValue(k, out var v) ? v as JObj : null;
    public bool Truthy(string k) => TryGetValue(k, out var v) && Json.Truthy(v);
    public IEnumerable<JObj> Objs(string k) => (Arr(k) ?? new()).OfType<JObj>();
}

public static class Json
{
    public static double D(object? v) => v switch { double d => d, long l => l, bool b => b ? 1 : 0, int i => i, null => 0, _ => throw new InvalidCastException(v.GetType().Name) };
    public static long L(object? v) => v switch { long l => l, double d => (long)d, bool b => b ? 1 : 0, int i => i, null => 0, _ => throw new InvalidCastException(v.GetType().Name) };
    public static bool Truthy(object? v) => v switch
    {
        null => false, bool b => b, long l => l != 0, double d => d != 0, string s => s.Length > 0,
        List<object?> a => a.Count > 0, JObj o => o.Count > 0, _ => true,
    };

    public static object? Parse(string s) { int i = 0; var v = Val(s, ref i); return v; }
    public static object? Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }

    static object? Val(string s, ref int i)
    {
        Ws(s, ref i);
        char c = s[i];
        if (c == '{')
        {
            var o = new JObj(); i++; Ws(s, ref i);
            if (s[i] == '}') { i++; return o; }
            while (true)
            {
                Ws(s, ref i);
                string k = Str(s, ref i);
                Ws(s, ref i); i++;                  // ':'
                o[k] = Val(s, ref i);
                Ws(s, ref i);
                if (s[i++] == '}') return o;
            }
        }
        if (c == '[')
        {
            var a = new List<object?>(); i++; Ws(s, ref i);
            if (s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(Val(s, ref i));
                Ws(s, ref i);
                if (s[i++] == ']') return a;
            }
        }
        if (c == '"') return Str(s, ref i);
        if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        if (string.CompareOrdinal(s, i, "NaN", 0, 3) == 0) { i += 3; return double.NaN; }
        if (string.CompareOrdinal(s, i, "Infinity", 0, 8) == 0) { i += 8; return double.PositiveInfinity; }
        if (string.CompareOrdinal(s, i, "-Infinity", 0, 9) == 0) { i += 9; return double.NegativeInfinity; }
        int st = i; bool fl = false;
        if (s[i] == '-') i++;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
        {
            if (!char.IsAsciiDigit(s[i])) fl = true;
            i++;
        }
        var t = s.AsSpan(st, i - st);
        if (!fl && long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
        return double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static string Str(string s, ref int i)
    {
        var sb = new StringBuilder(); i++;
        while (s[i] != '"')
        {
            char c = s[i++];
            if (c != '\\') { sb.Append(c); continue; }
            c = s[i++];
            switch (c)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u': sb.Append((char)int.Parse(s.AsSpan(i, 4), NumberStyles.HexNumber)); i += 4; break;
                default: sb.Append(c); break;
            }
        }
        i++;
        return sb.ToString();
    }

    public static string FloatRepr(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
        if (d == 0) return double.IsNegative(d) ? "-0.0" : "0.0";
        string r = d.ToString("E16", CultureInfo.InvariantCulture);
        // shortest round trip, then Python's layout: plain between 1e-4 and 1e16, else 1e+16 / 1e-05
        string s = d.ToString("R", CultureInfo.InvariantCulture);
        double a = Math.Abs(d);
        if (a >= 1e-4 && a < 1e16)
        {
            if (s.Contains('E')) s = decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            return s.Contains('.') ? s : s + ".0";
        }
        if (!s.Contains('E'))
        {
            // .NET stayed plain: turn into d.ddde+XX
            string e = d.ToString("E16", CultureInfo.InvariantCulture);
            int ex = int.Parse(e[(e.IndexOf('E') + 1)..], CultureInfo.InvariantCulture);
            string digits = s.Replace("-", "").Replace(".", "").TrimStart('0').TrimEnd('0');
            s = (d < 0 ? "-" : "") + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "E" + (ex >= 0 ? "+" : "") + ex;
        }
        int k = s.IndexOf('E');
        string m = s[..k]; int x = int.Parse(s[(k + 1)..], CultureInfo.InvariantCulture);
        return m + "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
    }

    static void WStr(StringBuilder b, string s)
    {
        b.Append('"');
        foreach (char c in s)
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                case '\f': b.Append("\\f"); break;
                case '\b': b.Append("\\b"); break;
                default:
                    if (c < 0x20 || c > 0x7E) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        b.Append('"');
    }

    /// json.dumps(v, indent=indent) (indent -1 = compact with Python's ", " and ": ")
    public static string Dumps(object? v, int indent = 1)
    {
        var b = new StringBuilder();
        W(b, v, indent, 0);
        return b.ToString();
    }

    static void Nl(StringBuilder b, int indent, int lvl) { if (indent >= 0) b.Append('\n').Append(' ', indent * lvl); }

    static void W(StringBuilder b, object? v, int indent, int lvl)
    {
        switch (v)
        {
            case null: b.Append("null"); break;
            case bool x: b.Append(x ? "true" : "false"); break;
            case long l: b.Append(l.ToString(CultureInfo.InvariantCulture)); break;
            case int i: b.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case uint u: b.Append(u.ToString(CultureInfo.InvariantCulture)); break;
            case ulong u: b.Append(u.ToString(CultureInfo.InvariantCulture)); break;
            case double d: b.Append(FloatRepr(d)); break;
            case string s: WStr(b, s); break;
            case List<object?> a:
                if (a.Count == 0) { b.Append("[]"); break; }
                b.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0) b.Append(indent >= 0 ? "," : ", ");
                    Nl(b, indent, lvl + 1);
                    W(b, a[i], indent, lvl + 1);
                }
                Nl(b, indent, lvl); b.Append(']');
                break;
            case IDictionary<string, object?> o:
                if (o.Count == 0) { b.Append("{}"); break; }
                b.Append('{');
                bool first = true;
                foreach (var kv in o)
                {
                    if (!first) b.Append(indent >= 0 ? "," : ", ");
                    first = false;
                    Nl(b, indent, lvl + 1);
                    WStr(b, kv.Key); b.Append(": ");
                    W(b, kv.Value, indent, lvl + 1);
                }
                Nl(b, indent, lvl); b.Append('}');
                break;
            default: throw new InvalidOperationException("json: " + v.GetType());
        }
    }

    public static void Save(object? v, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Dumps(v, 1).Replace("\n", "\r\n"), new UTF8Encoding(false));     // Python on Windows: text mode
        File.Move(tmp, path, true);
    }
}

/// Python's random.Random (Mersenne Twister) - same numbers for the same seed, so generated textures match
public sealed class PyRandom
{
    const int N = 624, M = 397;
    readonly uint[] mt = new uint[N];
    int idx = N + 1;

    public PyRandom(long seed)
    {
        ulong a = (ulong)Math.Abs(seed);
        var key = a > uint.MaxValue ? new[] { (uint)a, (uint)(a >> 32) } : new[] { (uint)a };
        InitArray(key);
    }

    void InitGen(uint s)
    {
        mt[0] = s;
        for (idx = 1; idx < N; idx++) mt[idx] = (uint)(1812433253U * (mt[idx - 1] ^ (mt[idx - 1] >> 30)) + idx);
    }

    void InitArray(uint[] key)
    {
        InitGen(19650218U);
        int i = 1, j = 0;
        for (int k = Math.Max(N, key.Length); k > 0; k--)
        {
            mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1664525U)) + key[j] + (uint)j;
            i++; j++;
            if (i >= N) { mt[0] = mt[N - 1]; i = 1; }
            if (j >= key.Length) j = 0;
        }
        for (int k = N - 1; k > 0; k--)
        {
            mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1566083941U)) - (uint)i;
            i++;
            if (i >= N) { mt[0] = mt[N - 1]; i = 1; }
        }
        mt[0] = 0x80000000U;
    }

    public uint Next32()
    {
        if (idx >= N)
        {
            int k;
            for (k = 0; k < N - M; k++) { uint y = (mt[k] & 0x80000000U) | (mt[k + 1] & 0x7fffffffU); mt[k] = mt[k + M] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfU : 0); }
            for (; k < N - 1; k++) { uint y = (mt[k] & 0x80000000U) | (mt[k + 1] & 0x7fffffffU); mt[k] = mt[k + (M - N)] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfU : 0); }
            uint z = (mt[N - 1] & 0x80000000U) | (mt[0] & 0x7fffffffU);
            mt[N - 1] = mt[M - 1] ^ (z >> 1) ^ ((z & 1) != 0 ? 0x9908b0dfU : 0);
            idx = 0;
        }
        uint r = mt[idx++];
        r ^= r >> 11; r ^= (r << 7) & 0x9d2c5680U; r ^= (r << 15) & 0xefc60000U; r ^= r >> 18;
        return r;
    }

    public double Random() { uint a = Next32() >> 5, b = Next32() >> 6; return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0); }
    uint GetRandBits(int k) => Next32() >> (32 - k);
    int RandBelow(int n)
    {
        int k = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)n);
        uint r = GetRandBits(k);
        while (r >= n) r = GetRandBits(k);
        return (int)r;
    }
    public int RandInt(int a, int b) => a + RandBelow(b - a + 1);
    public double Uniform(double a, double b) => a + (b - a) * Random();
    public void Shuffle<T>(IList<T> x)
    {
        for (int i = x.Count - 1; i > 0; i--) { int j = RandBelow(i + 1); (x[i], x[j]) = (x[j], x[i]); }
    }
}

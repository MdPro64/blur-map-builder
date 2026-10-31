// General .model (CPModel) editor. Port of model/cpmodel.py + the layout part of BlurImportExport's reader
// (only the offsets are needed, not the vertex values, so no Python add-on is involved here).
using System.Text.RegularExpressions;

namespace Bmb.Core;

public sealed class ModelInfo
{
    public int VStart, VCount, Stride, FStart, FCount;
    public List<(int Type, int Channel, int Off)> Elements = new();
    public List<(int FaceOff, int FaceCount, int Type)> Meshes = new();
    public double[] BBox = new double[6];
    public Dictionary<string, (int Off, int W, int H, int Mips)> Textures = new();
    public List<ElementInfo> AllElements = new();
    public List<VStreamInfo> VStreams = new();
    public List<FStreamInfo> FStreams = new();
    public List<MeshInfo> MeshInfos = new();
    public List<string> FxFiles = new();
    public List<List<(int Prefix, int Type)>> VertDefs = new();
    public List<double[]> ModelBoxes = new();
    public List<TexInfo> TexList = new();
}
public sealed class ElementInfo { public string Name = ""; public double[] Box = new double[6]; public float[] M = new float[12]; public int Parent, ModelIndex, ElementIndex; }
public sealed class TexInfo { public string Name = ""; public int W, H, Mips, Off, Len; public uint Dxt; }
public sealed class VStreamInfo { public int Bytes, Count, Start, Length; public List<(int Type, int Channel, int Sub)> Def = new(); }
public sealed class FStreamInfo { public int Start, Count, Length; }
public sealed class MeshInfo { public int Material, Definition, FaceType, FaceStream, Object; public List<(int FaceOff, int FaceCount, int VOff, int VCount)> Data1 = new(); public List<int> VOffset = new(); }

public static class CpModel
{
    static readonly Dictionary<int, int> TypeSize = new() { [6] = 12, [0xB] = 4, [8] = 4, [9] = 8, [0xA] = 4 };
    static readonly Regex TexRx = new("([A-Za-z0-9_]+)RA", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    sealed class R
    {
        public readonly byte[] D; public int P;
        public R(byte[] d) { D = d; }
        public int Int() { int v = B.I32(D, P); P += 4; return v; }
        public short Short() { short v = B.I16(D, P); P += 2; return v; }
        public float Float() { float v = B.F32(D, P); P += 4; return v; }
        public void Str() { int n = Int(); if (n < 0) throw new InvalidDataException("string"); P += n; }
        public void Ad(int n = 4) { P += n; if (P > D.Length || P < 0) throw new InvalidDataException("past the end"); }
        public void Sec(int len, int clip) { P += len; P += 8; }
        public double[] Box() { var o = new double[6]; for (int i = 0; i < 6; i++) o[i] = Float(); return o; }   // file order x, z(=up?), y as stored
        public void AdvanceTo(int val)
        {
            while (true)
            {
                if (P + 4 > D.Length) throw new InvalidDataException("advance_to past the end");
                if (B.I32(D, P) == val) return;
                P++;
            }
        }
    }

    /// the layout of a model. Works on all the file's streams; Parse() adds the one-stream check of cpmodel.parse
    public static ModelInfo Read(byte[] data)
    {
        var r = new R(data);
        var m = new ModelInfo();
        r.Ad(4);                    // ..CP
        r.Sec(8, 3); r.Sec(8, 2); r.Ad(); r.Sec(8, 2); r.Sec(8, 2); r.Ad();
        int nModels = r.Int(), nElements = r.Int();
        r.Ad();
        r.Box();
        r.Sec(8, 2);
        int nNames = r.Int() + 1;
        r.Ad(4 * nNames);
        var names = new List<string>();
        for (int i = 1; i < nNames; i++)
        {
            int e = Array.IndexOf(data, (byte)0, r.P);
            names.Add(B.Latin1.GetString(data, r.P, e - r.P));
            r.P = e + 1;
        }
        r.Sec(8, 2);
        for (int i = 0; i < nModels; i++)
        {
            r.Ad(4 * 12); m.ModelBoxes.Add(r.Box());
            int nameIdx = r.Int();
            if (nameIdx >= names.Count || nameIdx < -names.Count) throw new InvalidDataException("name index");
            r.Ad(4 * 6);
        }
        r.Sec(8, 1);
        for (int i = 0; i < nElements; i++)
        {
            var el = new ElementInfo { ModelIndex = r.Int() };
            for (int k = 0; k < 12; k++) el.M[k] = r.Float();
            el.Box = r.Box();
            int nameIdx = r.Int();
            if (nameIdx >= names.Count || nameIdx < -names.Count) throw new InvalidDataException("name index");
            el.Name = names[nameIdx < 0 ? nameIdx + names.Count : nameIdx];
            el.ElementIndex = r.Int(); el.Parent = r.Int();
            r.Ad(12); r.Ad(4);
            m.AllElements.Add(el);
        }
        r.Sec(8, 2); r.Sec(8, 2); r.Sec(8, 2); r.Sec(8, 2); r.Ad(); r.Sec(8, 3);
        r.Ad(); r.Ad(8); r.Ad(); r.Ad(8); r.Ad(12); r.Ad(8); r.Ad(8); r.Ad(8); r.Ad();
        int nArch = r.Int();
        r.Ad(8 * nArch);
        r.Ad(0xC); r.Ad(8); r.Ad(0x20); r.Ad(8); r.Ad();
        int ff = r.Int();
        r.Ad(ff + 4);
        r.Ad(8); r.Ad(8); r.Ad();
        int nDefs = r.Int();
        for (int i = 0; i < nDefs; i++)
        {
            r.Ad();
            int n = r.Int();
            var vd = new List<(int, int)>();
            for (int j = 0; j < n; j++)
            {
                r.Ad();
                int prefix = r.Short(); r.Short();
                int type = r.Int();
                r.Ad(9);
                vd.Add((prefix, type));
            }
            m.VertDefs.Add(vd);
        }
        r.Ad();
        int nFx = r.Int();
        for (int i = 0; i < nFx; i++) { r.Ad(); r.Ad(); int n = r.Int(); m.FxFiles.Add(B.Latin1.GetString(data, r.P, n)); r.Ad(n); }
        r.Ad(); r.Ad(); r.Ad(); r.Ad();
        int nTex = r.Int();
        for (int i = 0; i < nTex; i++)
        {
            int tn = B.I32(data, r.P); string tname = tn > 0 && tn < 512 ? B.Latin1.GetString(data, r.P + 4, tn) : "";
            r.Str(); r.Ad(4); r.Ad(); r.Ad(); r.Ad();
            r.Str(); r.Ad();
            r.Ad(4 * 11);
            int length = r.Int();
            int th = r.Int(), tw = r.Int(); r.Ad(); int tmips = r.Int(); uint tdxt = (uint)r.Int(); r.Ad(8);
            m.TexList.Add(new TexInfo { Name = tname, W = tw, H = th, Mips = tmips, Dxt = tdxt, Off = r.P, Len = length - 0x1C });
            r.Ad(length - 0x1C);
        }
        r.Ad(8 * 6); r.Ad(); r.Ad(); r.Ad(7); r.Ad(); r.Ad(); r.Ad(8); r.Ad(8);
        r.Ad(4); r.Ad(); r.Ad(); r.Ad(8); r.Ad(); r.Ad(); r.Ad(8); r.Ad();
        int nVS = r.Int();
        for (int i = 0; i < nVS; i++)
        {
            r.Ad(1); r.Ad(); r.Ad();
            int byteLen = r.Int();
            r.Ad();
            var vs = new VStreamInfo { Bytes = byteLen };
            int nd = r.Int();
            for (int j = 0; j < nd; j++)
            {
                r.Ad();
                int type = r.Int(); r.Ad(4); int ch = r.Int(), sub = r.Int();
                vs.Def.Add((type, ch, sub));
            }
            r.Ad();
            int no = r.Int();
            r.Ad(2 * no);
            vs.Count = r.Int(); r.Ad();
            vs.Length = r.Int(); r.Ad();
            r.Ad(vs.Length);
            r.AdvanceTo(i == nVS - 1 ? 0x4152 : 0x1415202);
            r.Ad(-vs.Length);
            vs.Start = r.P;
            foreach (var (t, _, _) in vs.Def) if (!TypeSize.ContainsKey(t)) throw new InvalidDataException("vertex type " + t);
            int vb = vs.Def.Sum(x => TypeSize[x.Type]);
            r.Ad(vb * vs.Count);
            m.VStreams.Add(vs);
        }
        r.Ad();
        int nFS = r.Int();
        for (int i = 0; i < nFS; i++)
        {
            r.Ad(1); r.Ad();
            var fs = new FStreamInfo { Count = r.Int() };
            r.Ad(); r.Ad();
            fs.Length = r.Int();
            r.Ad();
            r.Ad(fs.Length);
            r.AdvanceTo(i == nFS - 1 ? 0x4152 : 0x1415202);
            r.Ad(-fs.Length);
            fs.Start = r.P;
            r.Ad(2 * fs.Count);
            m.FStreams.Add(fs);
        }
        r.Ad();
        int nRd = r.Int();
        for (int i = 0; i < nRd; i++)
        {
            r.Ad(1); r.Str();
            int common = r.Int();
            if (common == 0x4152) { r.Ad(0x58); int c = r.Int(); r.Ad(c + 4); break; }
            r.Ad(); r.Str(); r.Ad();
            int nu = r.Int();
            r.Ad(8 * nu);
            r.Ad(16); r.Ad(); r.Box(); r.Ad(8); r.Ad(); r.Ad(4); r.Ad();
            int fc = r.Int();
            r.Ad(fc + 4); r.Ad(16);
        }
        r.Ad(24);
        int nSh = r.Int();
        for (int i = 0; i < nSh; i++)
        {
            r.Ad();
            int nl = r.Int();
            if (nl == 0) { r.Ad(); r.Ad(12); r.Ad(); r.Ad(12); continue; }
            r.Ad(nl); r.Ad();
            int np = r.Int();
            for (int j = 0; j < np; j++) { r.Ad(); r.Str(); r.Ad(8); }
            r.Ad(8); r.Ad();
            int no = r.Int();
            r.Ad(2 * (no + 1));
            r.Ad(6);
        }
        r.Ad(12);
        int nMesh = r.Int();
        for (int i = 0; i < nMesh; i++)
        {
            r.Ad();
            var mi = new MeshInfo { Material = r.Int(), Definition = r.Int(), FaceType = r.Int(), FaceStream = r.Int() };
            mi.Object = r.Short(); r.Short();
            r.Ad(); r.Ad(8); r.Ad(); r.Ad(8);
            r.Ad(4 * 8);
            int n1 = r.Int();
            for (int j = 0; j < n1; j++) { r.Ad(); int a = r.Int(), b = r.Int(), c = r.Int(), d = r.Int(); mi.Data1.Add((a, b, c, d)); }
            r.Ad();
            int n2 = r.Int();
            for (int j = 0; j < n2; j++) { r.Ad(); r.Ad(8); mi.VOffset.Add(r.Int()); r.Ad(4); r.Ad(); r.Ad(8); }
            m.MeshInfos.Add(mi);
        }
        return m;
    }

    /// cpmodel.parse: the one-stream view the map builder edits
    public static ModelInfo Parse(byte[] data)
    {
        var m = Read(data);
        if (m.VStreams.Count != 1 || m.FStreams.Count != 1) throw new InvalidDataException("only 1 vertex + 1 face stream supported");
        var vs = m.VStreams[0]; var fs = m.FStreams[0];
        int off = 0;
        foreach (var (t, ch, _) in vs.Def) { m.Elements.Add((t, ch, off)); off += TypeSize[t]; }
        if (off != vs.Bytes) throw new InvalidDataException("stride");
        foreach (var mi in m.MeshInfos)
        {
            if (mi.VOffset[0] != 0) throw new InvalidDataException("vOffset");
            m.Meshes.Add((mi.Data1[0].FaceOff, mi.Data1[0].FaceCount, mi.FaceType));
        }
        var bb = (m.AllElements.Count > 1 ? m.AllElements[1] : m.AllElements[0]).Box;
        // stored x, y(up), z; the add-on's {x, z, y} naming and cpmodel's re-order cancel out
        m.BBox = new[] { bb[0], bb[1], bb[2], bb[3], bb[4], bb[5] };
        string s = B.Latin1.GetString(data);
        foreach (Match mt in TexRx.Matches(s))
        {
            int j = s.IndexOf("DXT1", mt.Index + mt.Length, StringComparison.Ordinal);
            if (j < 0 || j - (mt.Index + mt.Length) > 80) continue;
            m.Textures[mt.Groups[1].Value] = (j + 12, (int)B.U32(data, j - 16), (int)B.U32(data, j - 12), (int)B.U32(data, j - 4));
        }
        m.VStart = vs.Start; m.VCount = vs.Count; m.Stride = vs.Bytes; m.FStart = fs.Start; m.FCount = fs.Count;
        return m;
    }

    public static byte[] NewTextureIds(byte[] data, uint salt)
    {
        var d = (byte[])data.Clone();
        foreach (var name in Parse(d).Textures.Keys)
        {
            var rx = new Regex(Regex.Escape(name) + "(....)RA", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            string s = B.Latin1.GetString(d);
            var m = rx.Match(s);
            var old = d.AsSpan(m.Groups[1].Index, 4).ToArray();
            var nw = new byte[4]; B.Put(nw, 0, B.U32(old, 0) ^ salt);
            // every place the old id bytes appear (Python's bytes.replace: left to right, no overlaps)
            for (int i = 0; i + 4 <= d.Length;)
            {
                int k = d.AsSpan(i).IndexOf(old);
                if (k < 0) break;
                nw.CopyTo(d, i + k);
                i += k + 4;
            }
        }
        return d;
    }

    public static List<(int W, int H)> MipSizes(int w, int h, int mips)
    {
        var o = new List<(int, int)>();
        for (int i = 0; i < mips; i++) { o.Add((w, h)); w = Math.Max(w / 2, 1); h = Math.Max(h / 2, 1); }
        return o;
    }

    public static int TextureBytes(int w, int h, int mips) => MipSizes(w, h, mips).Sum(s => Math.Max(s.W / 4, 1) * Math.Max(s.H / 4, 1) * 8);

    public static byte[] EncodeMips(byte[] px, int w, int h, int mips)
    {
        var o = new ByteBuf(TextureBytes(w, h, mips));
        var sizes = MipSizes(w, h, mips);
        for (int i = 0; i < sizes.Count; i++)
        {
            if (i > 0) (px, w, h) = Half2(px, w, h);
            int bw = Math.Max(w, 4), bh = Math.Max(h, 4);
            var pad = new byte[bw * bh * 3];
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int s = (Math.Min(y, h - 1) * w + Math.Min(x, w - 1)) * 3, t = (y * bw + x) * 3;
                    pad[t] = px[s]; pad[t + 1] = px[s + 1]; pad[t + 2] = px[s + 2];
                }
            o.Add(Dxt.Dxt1Encode(pad, bw, bh));
        }
        return o.ToArray();
    }

    static (byte[], int, int) Half2(byte[] px, int w, int h)
    {
        int nw = Math.Max(w / 2, 1), nh = Math.Max(h / 2, 1);
        var o = new byte[nw * nh * 3];
        Parallel.For(0, nh, y =>
        {
            for (int x = 0; x < nw; x++)
                for (int c = 0; c < 3; c++)
                {
                    int s = 0;
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                        s += px[(Math.Min(2 * y + dy, h - 1) * w + Math.Min(2 * x + dx, w - 1)) * 3 + c];
                    o[(y * nw + x) * 3 + c] = (byte)(s / 4);
                }
        });
        return (o, nw, nh);
    }

    public static byte[] Solid(int w, int h, byte r, byte g, byte b)
    {
        var o = new byte[w * h * 3];
        for (int i = 0; i < o.Length; i += 3) { o[i] = r; o[i + 1] = g; o[i + 2] = b; }
        return o;
    }

    public static byte[] Noise(int w, int h, int bas, int spread, int seed = 7)
    {
        var r = new PyRandom(seed);
        var px = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            int v = bas + r.RandInt(-spread, spread);
            if (r.Random() < 0.04) v += r.RandInt(15, 35);
            v = Math.Max(0, Math.Min(255, v));
            px[3 * i] = (byte)v; px[3 * i + 1] = (byte)v; px[3 * i + 2] = (byte)Math.Min(v + 2, 255);
        }
        return px;
    }

    static void WriteVertex(byte[] d, ModelInfo P, int vi, double x, double y, double z, double u, double v, byte n0, byte n1, byte n2, byte n3)
    {
        int at = P.VStart + P.Stride * vi;
        foreach (var (typ, ch, off) in P.Elements)
        {
            int a = at + off;
            if (typ == 6) { B.Put(d, a, (float)x); B.Put(d, a + 4, (float)y); B.Put(d, a + 8, (float)z); }
            else if (typ == 0xB && ch == 3) { d[a] = n0; d[a + 1] = n1; d[a + 2] = n2; d[a + 3] = n3; }
            else if (typ == 0xB) { d[a] = 255; d[a + 1] = 255; d[a + 2] = 255; d[a + 3] = 255; }
            else if (typ == 8) { B.Put(d, a, (Half)u); B.Put(d, a + 2, (Half)v); }
            else if (typ == 9) { B.Put(d, a, (Half)u); B.Put(d, a + 2, (Half)v); B.Put(d, a + 4, (Half)0.0); B.Put(d, a + 6, (Half)0.0); }
        }
    }

    /// every 6-float window close to the source's main box -> box; returns their offsets
    public static List<int> WriteBBox(byte[] d, ModelInfo P, double[] box)
    {
        var ob = P.BBox;
        double tol = 0.15 * Math.Max(ob[3] - ob[0], Math.Max(ob[4] - ob[1], ob[5] - ob[2]));
        int vend = P.VStart + P.Stride * P.VCount;
        var ranges = P.Textures.Values.Select(t => (t.Off, End: t.Off + TextureBytes(t.W, t.H, t.Mips))).ToArray();
        var found = new List<int>();
        int at = 0, n = d.Length - 24;
        Span<float> v = stackalloc float[6];
        while (at < n)
        {
            if (at >= P.VStart && at < vend) { at = vend; continue; }
            bool skip = false;
            foreach (var (a, b) in ranges) if (a <= at && at < b) { at = b; skip = true; break; }
            if (skip) continue;
            float f0 = B.F32(d, at);
            if (Math.Abs(f0 - ob[0]) > tol) { at++; continue; }
            bool ok = true;
            for (int i = 0; i < 6; i++) { v[i] = B.F32(d, at + 4 * i); if (!(Math.Abs(v[i] - ob[i]) <= tol)) { ok = false; break; } }
            if (ok && v[0] < v[3] && v[1] <= v[4] && v[2] < v[5])
            {
                for (int i = 0; i < 6; i++) B.Put(d, at + 4 * i, (float)box[i]);
                found.Add(at);
                at += 24;
                continue;
            }
            at++;
        }
        return found;
    }

    /// profile = (z across, y up, v texture) in rising z; the piece runs x -length/2 .. +length/2 with u 0 .. uvU
    public static byte[] MakeProfile(byte[] data, double length, IList<(double Z, double Y, double V)> profile, double uvU,
                                     IDictionary<string, byte[]>? textures = null)
    {
        var P = Parse(data);
        var d = (byte[])data.Clone();
        double hx = length / 2;
        var idx = new ushort[P.FCount];
        for (int i = 0; i < P.FCount; i++) idx[i] = B.U16(d, P.FStart + 2 * i);
        var sets = new List<List<int>>();
        foreach (var (foff, fcnt, _) in P.Meshes)
        {
            var mine = new List<int>(); var seen = new HashSet<int>();
            for (int i = foff; i < foff + fcnt; i++) if (seen.Add(idx[i])) mine.Add(idx[i]);
            sets.Add(mine);
        }
        int need = 2 * profile.Count;
        var taken = new HashSet<int>();
        var order = Enumerable.Range(0, sets.Count).OrderBy(k => sets[k].Count).ToList();
        var chosen = new Dictionary<int, List<int>>();
        foreach (int k in order)
        {
            var others = new HashSet<int>();
            for (int j = 0; j < sets.Count; j++) if (j != k) others.UnionWith(sets[j]);
            var pick = sets[k].Where(v => !taken.Contains(v) && !others.Contains(v)).ToList();
            var ps = new HashSet<int>(pick);
            pick.AddRange(sets[k].Where(v => !taken.Contains(v) && !ps.Contains(v)));
            if (pick.Count < need) throw new InvalidOperationException($"source mesh {k} has too few vertices for this profile");
            chosen[k] = pick.GetRange(0, need);
            taken.UnionWith(chosen[k]);
        }
        for (int k = 0; k < P.Meshes.Count; k++)
        {
            var (foff, fcnt, ftype) = P.Meshes[k];
            var verts = chosen[k];
            if (fcnt < need) throw new InvalidOperationException("too few indices");
            var strip = new List<int>();
            for (int j = 0; j < profile.Count; j++)
            {
                var p0 = profile[Math.Max(j - 1, 0)]; var p1 = profile[Math.Min(j + 1, profile.Count - 1)];
                double tz = p1.Z - p0.Z, ty = p1.Y - p0.Y;
                double ln = Py.Hypot(tz, ty); if (ln == 0) ln = 1.0;
                double nz = -ty / ln, ny = tz / ln;
                byte n1 = (byte)(int)(127 + 127 * ny), n2 = (byte)(int)(127 + 127 * nz);
                for (int e = 0; e < 2; e++)
                {
                    double x = e == 0 ? hx : -hx, u = e == 0 ? uvU : 0.0;
                    int vi = verts[2 * j + e];
                    strip.Add(vi);
                    WriteVertex(d, P, vi, x, profile[j].Y, profile[j].Z, u, profile[j].V, 127, n1, n2, 0);
                }
            }
            List<int> nw;
            if (ftype == 1) { nw = new List<int>(strip); while (nw.Count < fcnt) nw.Add(strip[^1]); }
            else
            {
                nw = new List<int>();
                for (int j = 0; j < profile.Count - 1; j++)
                {
                    int a = strip[2 * j], b = strip[2 * j + 1], c = strip[2 * j + 2], e = strip[2 * j + 3];
                    nw.AddRange(new[] { a, b, c, b, e, c });
                }
                if (nw.Count > fcnt) throw new InvalidOperationException("too many indices");
                while (nw.Count < fcnt) nw.Add(nw[^1]);
            }
            for (int i = 0; i < fcnt; i++) B.Put(d, P.FStart + 2 * (foff + i), (ushort)nw[i]);
        }
        double ymin = profile.Min(p => p.Y), ymax = profile.Max(p => p.Y), zmin = profile.Min(p => p.Z), zmax = profile.Max(p => p.Z);
        WriteBBox(d, P, new[] { -hx, ymin - 0.01, zmin, hx, ymax + 0.01, zmax });
        if (textures != null) foreach (var (name, px) in textures) PutTexture(d, P, name, px);
        return d;
    }

    static void PutTexture(byte[] d, ModelInfo P, string name, byte[] px)
    {
        var (o, w, h, m) = P.Textures[name];
        var enc = EncodeMips(px, w, h, m);
        if (enc.Length != TextureBytes(w, h, m)) throw new InvalidOperationException("texture size");
        enc.CopyTo(d, o);
    }

    /// ANY mesh into a one-mesh, triangle-strip model. verts = x y z nx ny nz u v
    public static byte[] MakeMesh(byte[] data, IList<double[]> verts, IList<int> strip, IDictionary<string, byte[]>? encoded = null)
    {
        var P = Parse(data);
        if (P.Meshes.Count != 1 || P.Meshes[0].Type != 1) throw new InvalidOperationException("needs a model with one triangle-strip mesh");
        if (verts.Count > P.VCount) throw new InvalidOperationException($"too many vertices ({verts.Count} > {P.VCount})");
        var (foff, fcnt, _) = P.Meshes[0];
        if (strip.Count > fcnt) throw new InvalidOperationException($"too many indices ({strip.Count} > {fcnt})");
        var d = (byte[])data.Clone();
        static byte N(double c) => (byte)Math.Max(0, Math.Min(254, (int)Py.Round(127 + 127 * c)));
        for (int vi = 0; vi < verts.Count; vi++)
        {
            var v = verts[vi];
            WriteVertex(d, P, vi, v[0], v[1], v[2], v[6], v[7], N(v[3]), N(v[4]), N(v[5]), 0);
        }
        int last = strip[strip.Count - 1];
        for (int i = 0; i < fcnt; i++) B.Put(d, P.FStart + 2 * (foff + i), (ushort)(i < strip.Count ? strip[i] : last));
        double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
        foreach (var v in verts)
        {
            x0 = Math.Min(x0, v[0]); x1 = Math.Max(x1, v[0]); y0 = Math.Min(y0, v[1]); y1 = Math.Max(y1, v[1]); z0 = Math.Min(z0, v[2]); z1 = Math.Max(z1, v[2]);
        }
        WriteBBox(d, P, new[] { x0, y0 - 0.01, z0, x1, y1 + 0.01, z1 });
        if (encoded != null)
            foreach (var (name, enc) in encoded)
            {
                var (o, w, h, m) = P.Textures[name];
                if (enc.Length != TextureBytes(w, h, m)) throw new InvalidOperationException(name);
                enc.CopyTo(d, o);
            }
        return d;
    }

    /// flat quad (both sides), top at y = 0, centred; vertical = a wall size_x long, size_z high in the plane z = 0
    public static byte[] MakeQuad(byte[] data, double sizeX, double sizeZ, double uvU, double uvV, IDictionary<string, byte[]>? textures = null, bool vertical = false)
    {
        var P = Parse(data);
        var d = (byte[])data.Clone();
        double hx = sizeX / 2, hz = sizeZ / 2;
        var corners = new (double x, double z, double u, double w)[] { (-hx, -hz, 0, 0), (-hx, hz, 0, uvV), (hx, -hz, uvU, 0), (hx, hz, uvU, uvV) };
        var idx = new ushort[P.FCount];
        for (int i = 0; i < P.FCount; i++) idx[i] = B.U16(d, P.FStart + 2 * i);
        foreach (var (foff, fcnt, ftype) in P.Meshes)
        {
            var mine = new List<int>(); var seen = new HashSet<int>();
            for (int i = foff; i < foff + fcnt; i++) if (seen.Add(idx[i])) mine.Add(idx[i]);
            if (mine.Count < 4 || fcnt < 12) throw new InvalidOperationException("mesh too small");
            int A = mine[0], Bv = mine[1], C = mine[2], D = mine[3];
            var vs = new[] { A, Bv, C, D };
            for (int k = 0; k < 4; k++)
            {
                var (x, z, u, w) = corners[k];
                if (vertical) WriteVertex(d, P, vs[k], x, z + hz, 0.0, u, w, 127, 127, 254, 0);
                else WriteVertex(d, P, vs[k], x, 0.0, z, u, w, 127, 254, 127, 0);
            }
            var nw = ftype == 1 ? new List<int> { A, Bv, C, D, D, A, A, C, Bv, D } : new List<int> { A, Bv, C, Bv, D, C, A, C, Bv, Bv, C, D };
            while (nw.Count < fcnt) nw.Add(D);
            for (int i = 0; i < fcnt; i++) B.Put(d, P.FStart + 2 * (foff + i), (ushort)nw[i]);
        }
        var box = vertical ? new[] { -hx, 0.0, -0.01, hx, 2 * hz, 0.01 } : new[] { -hx, -0.01, -hz, hx, 0.01, hz };
        WriteBBox(d, P, box);
        if (textures != null) foreach (var (name, px) in textures) PutTexture(d, P, name, px);
        return d;
    }
}

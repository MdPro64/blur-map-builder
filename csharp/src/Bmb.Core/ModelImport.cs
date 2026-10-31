// Imported .glb models + picture helpers. Port of flat_earth/model_import.py; Pic.ResizeLanczos follows Pillow's resampling (8-bit fixed point).
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text;

namespace Bmb.Core;

/// an RGB picture
public sealed class Pic
{
    public int W, H; public byte[] Px;
    public Pic(int w, int h, byte[]? px = null) { W = w; H = h; Px = px ?? new byte[w * h * 3]; }

    [SupportedOSPlatform("windows")]
    public static Pic Load(Stream s)
    {
        using var bm = new Bitmap(s, false);
        return FromBitmap(bm);
    }
    [SupportedOSPlatform("windows")]
    public static Pic Load(string path) { using var f = File.OpenRead(path); return Load(f); }

    [SupportedOSPlatform("windows")]
    public static unsafe Pic FromBitmap(Bitmap bm)
    {
        var p = new Pic(bm.Width, bm.Height);
        var d = bm.LockBits(new Rectangle(0, 0, bm.Width, bm.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < p.H; y++)
        {
            byte* r = (byte*)d.Scan0 + y * d.Stride;
            for (int x = 0; x < p.W; x++) { int o = (y * p.W + x) * 3; p.Px[o] = r[4 * x + 2]; p.Px[o + 1] = r[4 * x + 1]; p.Px[o + 2] = r[4 * x]; }
        }
        bm.UnlockBits(d);
        return p;
    }

    static double Sinc(double x) { if (x == 0) return 1; x *= Math.PI; return Math.Sin(x) / x; }
    static double Lanczos(double x) => x > -3 && x < 3 ? Sinc(x) * Sinc(x / 3) : 0;

    static (int[] Bounds, int[,] Co, int KSize) Coeffs(int inSize, int outSize)
    {
        double scale = (double)inSize / outSize, fs = Math.Max(scale, 1.0), support = 3.0 * fs;
        int ks = (int)Math.Ceiling(support) * 2 + 1;
        var kk = new double[outSize, ks]; var bounds = new int[outSize * 2];
        for (int xx = 0; xx < outSize; xx++)
        {
            double center = (xx + 0.5) * scale;
            int xmin = Math.Max(0, (int)(center - support + 0.5));
            int xmax = Math.Min(inSize, (int)(center + support + 0.5)) - xmin;
            double ww = 0;
            var k = new double[xmax];
            for (int x = 0; x < xmax; x++) { k[x] = Lanczos((x + xmin - center + 0.5) / fs); ww += k[x]; }
            for (int x = 0; x < xmax; x++) kk[xx, x] = ww != 0 ? k[x] / ww : k[x];
            bounds[2 * xx] = xmin; bounds[2 * xx + 1] = xmax;
        }
        var co = new int[outSize, ks];
        for (int i = 0; i < outSize; i++)
            for (int j = 0; j < ks; j++)
            {
                double v = kk[i, j] * (1 << 22);
                co[i, j] = (int)(v < 0 ? -0.5 + v : 0.5 + v);
            }
        return (bounds, co, ks);
    }

    static byte Clip8(int v) { v >>= 22; return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

    /// Image.resize((w, h), LANCZOS) on an RGB picture (horizontal pass, then vertical, as Pillow)
    public Pic ResizeLanczos(int w, int h)
    {
        if (w == W && h == H) return new Pic(w, h, (byte[])Px.Clone());
        var (bx, cx, kx) = Coeffs(W, w);
        var (by, cy, ky) = Coeffs(H, h);
        int y0 = by[0], y1 = by[2 * h - 2] + by[2 * h - 1];
        int th = y1 - y0;
        var tmp = new byte[th * w * 3];
        if (w == W) Array.Copy(Px, y0 * W * 3, tmp, 0, th * W * 3);
        else
            Parallel.For(0, th, yy =>
            {
                int sr = (yy + y0) * W * 3, dr = yy * w * 3;
                for (int xx = 0; xx < w; xx++)
                {
                    int xmin = bx[2 * xx], xmax = bx[2 * xx + 1];
                    for (int c = 0; c < 3; c++)
                    {
                        int ss = 1 << 21;
                        for (int x = 0; x < xmax; x++) ss += Px[sr + (x + xmin) * 3 + c] * cx[xx, x];
                        tmp[dr + xx * 3 + c] = Clip8(ss);
                    }
                }
            });
        var o = new byte[w * h * 3];
        if (h == H) { tmp.CopyTo(o, 0); return new Pic(w, h, o); }
        Parallel.For(0, h, yy =>
        {
            int ymin = by[2 * yy], ymax = by[2 * yy + 1];
            for (int xx = 0; xx < w * 3; xx++)
            {
                int ss = 1 << 21;
                for (int y = 0; y < ymax; y++) ss += tmp[(y + ymin - y0) * w * 3 + xx] * cy[yy, y];
                o[yy * w * 3 + xx] = Clip8(ss);
            }
        });
        return new Pic(w, h, o);
    }
}

public sealed class GlbPart { public double[] Pos = null!, Nrm = null!, Uv = null!; public int[] Tri = null!; public int Mat; public int N => Pos.Length / 3; }
public sealed class GlbMat { public string Name = ""; public Pic? Image; public double[] Colour = { 1, 1, 1 }; public double Glow; public string? Note; }
public sealed class Glb { public List<GlbPart> Parts = new(); public List<GlbMat> Materials = new(); public List<string> Skipped = new(); }
public sealed class ImportProblem : Exception { public ImportProblem(string s) : base(s) { } }
public sealed class Piece3 { public int Mat; public List<double[]> Verts = new(); public List<int> Strip = new(); }

public static class ModelImport
{
    public const int VCap = 3232, ICap = 4861, GameTex = 256, Shine = 25;
    public const uint ImportUid = 0xF1E30000;
    public static string Folder => Path.Combine(Paths.Tracks, "imports");

    static (JObj Js, byte[] Bin) Chunks(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 20 || B.U32(d, 0) != 0x46546C67) throw new ImportProblem("Not a .glb file. In Blender: File > Export > glTF 2.0, Format: glTF Binary (.glb).");
        if (B.U32(d, 4) != 2) throw new ImportProblem("Only glTF 2.0 .glb files are supported.");
        int off = 12; JObj? js = null; byte[] bin = Array.Empty<byte>();
        while (off + 8 <= d.Length)
        {
            int n = (int)B.U32(d, off); uint typ = B.U32(d, off + 4);
            if (typ == 0x4E4F534A) js = (JObj)Json.Parse(Encoding.UTF8.GetString(d, off + 8, n))!;
            else if (typ == 0x004E4942) bin = d.AsSpan(off + 8, Math.Min(n, d.Length - off - 8)).ToArray();
            off += 8 + n;
        }
        return (js ?? throw new ImportProblem("The .glb file has no scene description (broken file?)."), bin);
    }

    static int Nc(string t) => t switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new InvalidDataException(t) };

    static double[] Accessor(JObj js, byte[] bin, int i, bool norm = false)
    {
        var a = (JObj)js.Arr("accessors")![i]!;
        int n = (int)a.Int("count"), ct = (int)a.Int("componentType"), nc = Nc(a.Str("type")!);
        int sz = ct switch { 5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4 };
        var o = new double[n * nc];
        if (a.ContainsKey("sparse")) throw new ImportProblem("This .glb uses 'sparse' data (morph / shape keys) - apply them in Blender first.");
        if (!a.ContainsKey("bufferView")) return o;
        var bv = (JObj)js.Arr("bufferViews")![(int)a.Int("bufferView")]!;
        if (bv.Int("buffer") != 0) throw new ImportProblem("The model's data is in a separate file - export as glTF Binary (.glb).");
        int start = (int)(bv.Int("byteOffset") + a.Int("byteOffset"));
        int stride = bv.Truthy("byteStride") ? (int)bv.Int("byteStride") : sz * nc;
        double div = !norm ? 1 : ct switch { 5120 => 127.0, 5121 => 255.0, 5122 => 32767.0, 5123 => 65535.0, _ => 1 };
        for (int r = 0; r < n; r++)
            for (int c = 0; c < nc; c++)
            {
                int at = start + r * stride + c * sz;
                double v = ct switch
                {
                    5120 => (sbyte)bin[at], 5121 => bin[at], 5122 => B.I16(bin, at), 5123 => B.U16(bin, at), 5125 => B.U32(bin, at), _ => B.F32(bin, at),
                };
                o[r * nc + c] = v / div;
            }
        return o;
    }

    static double[] Mul(double[] a, double[] b)     // 4 x 4, row order
    {
        var o = new double[16];
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) for (int k = 0; k < 4; k++) o[4 * i + j] += a[4 * i + k] * b[4 * k + j];
        return o;
    }

    static double[] NodeMatrix(JObj node)
    {
        if (node.ContainsKey("matrix"))
        {
            var m = node.Arr("matrix")!; var o = new double[16];
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) o[4 * i + j] = Json.D(m[4 * j + i]);
            return o;
        }
        double[] Get(string k, params double[] d) { var l = node.Arr(k); return l == null ? d : l.Select(Json.D).ToArray(); }
        var t = Get("translation", 0, 0, 0); var q = Get("rotation", 0, 0, 0, 1); var s = Get("scale", 1, 1, 1);
        double x = q[0], y = q[1], z = q[2], w = q[3];
        return new[]
        {
            (1 - 2 * (y * y + z * z)) * s[0], (2 * (x * y - z * w)) * s[1], (2 * (x * z + y * w)) * s[2], t[0],
            (2 * (x * y + z * w)) * s[0], (1 - 2 * (x * x + z * z)) * s[1], (2 * (y * z - x * w)) * s[2], t[1],
            (2 * (x * z - y * w)) * s[0], (2 * (y * z + x * w)) * s[1], (1 - 2 * (x * x + y * y)) * s[2], t[2],
            0, 0, 0, 1,
        };
    }

    [SupportedOSPlatform("windows")]
    static (Pic?, string?) Image(JObj js, byte[] bin, int texIndex)
    {
        var tex = (JObj)js.Arr("textures")![texIndex]!;
        if (tex["source"] == null) return (null, "its texture format is not supported (KTX2 / Basis / WebP) - plain colour used");
        var im = (JObj)js.Arr("images")![(int)tex.Int("source")]!;
        if (!im.ContainsKey("bufferView")) return (null, "its picture is a separate file - export as glTF Binary (.glb) to include it");
        var bv = (JObj)js.Arr("bufferViews")![(int)im.Int("bufferView")]!;
        try { using var ms = new MemoryStream(bin, (int)bv.Int("byteOffset"), (int)bv.Int("byteLength")); return (Pic.Load(ms), null); }
        catch (Exception) { return (null, $"its picture could not be read ({im.Str("mimeType") ?? "?"}) - plain colour used"); }
    }

    [SupportedOSPlatform("windows")]
    public static Glb ReadGlb(string path)
    {
        var (js, bin) = Chunks(path);
        var need = (js.Arr("extensionsRequired") ?? new()).OfType<string>().ToHashSet();
        if (need.Contains("KHR_draco_mesh_compression") || need.Contains("EXT_meshopt_compression"))
            throw new ImportProblem("This .glb is compressed (Draco / meshopt). In Blender export it again with 'Compression' switched off.");
        var g = new Glb(); var matOf = new Dictionary<long, int>(); var skipped = new SortedSet<string>();
        var mats = js.Arr("materials") ?? new();
        int Material(long? mi)
        {
            long key = mi ?? -1;
            if (matOf.TryGetValue(key, out int have)) return have;
            var m = mi != null && mi < mats.Count ? (JObj)mats[(int)mi]! : new JObj();
            var pbr = m.Obj("pbrMetallicRoughness") ?? new JObj();
            var f = pbr.Arr("baseColorFactor")?.Select(Json.D).ToArray() ?? new double[] { 1, 1, 1, 1 };
            Pic? img = null; string? note = null;
            if (pbr.Obj("baseColorTexture") is JObj bt) (img, note) = Image(js, bin, (int)bt.Int("index"));
            var em = m.Arr("emissiveFactor")?.Select(Json.D).ToArray() ?? new double[] { 0, 0, 0 };
            double glow = m.ContainsKey("emissiveTexture") || em.Max() > 0 ? em.Max() : 0.0;
            g.Materials.Add(new GlbMat { Name = m.Truthy("name") ? m.Str("name")! : $"material {g.Materials.Count}", Image = img, Colour = f[..3], Glow = Math.Min(1.0, glow), Note = note });
            return matOf[key] = g.Materials.Count - 1;
        }
        var nodes = js.Arr("nodes") ?? new(); var meshes = js.Arr("meshes") ?? new();
        void Visit(int ni, double[] parent)
        {
            var node = (JObj)nodes[ni]!;
            var world = Mul(parent, NodeMatrix(node));
            if (node.ContainsKey("mesh"))
                foreach (var prim in ((JObj)meshes[(int)node.Int("mesh")]!).Objs("primitives"))
                {
                    long mode = prim.ContainsKey("mode") ? prim.Int("mode") : 4;
                    if (mode != 4) { skipped.Add(mode < 4 ? "lines / points" : "strips / fans"); continue; }
                    var at = prim.Obj("attributes")!;
                    if (!at.ContainsKey("POSITION")) continue;
                    var pos = Accessor(js, bin, (int)at.Int("POSITION"));
                    int n = pos.Length / 3;
                    int[] tri;
                    if (prim.ContainsKey("indices")) { var ix = Accessor(js, bin, (int)prim.Int("indices")); tri = ix.Take(ix.Length / 3 * 3).Select(v => (int)v).ToArray(); }
                    else tri = Enumerable.Range(0, n / 3 * 3).ToArray();
                    var uv = at.ContainsKey("TEXCOORD_0") ? Accessor(js, bin, (int)at.Int("TEXCOORD_0"), true) : new double[n * 2];
                    var pw = new double[n * 3];
                    for (int i = 0; i < n; i++)
                        for (int c = 0; c < 3; c++)
                            pw[3 * i + c] = pos[3 * i] * world[4 * c] + pos[3 * i + 1] * world[4 * c + 1] + pos[3 * i + 2] * world[4 * c + 2] + world[4 * c + 3];
                    var nrm = new double[n * 3];
                    double m00 = world[0], m01 = world[1], m02 = world[2], m10 = world[4], m11 = world[5], m12 = world[6], m20 = world[8], m21 = world[9], m22 = world[10];
                    double det = m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
                    if (at.ContainsKey("NORMAL"))
                    {
                        var nr = Accessor(js, bin, (int)at.Int("NORMAL"));
                        // inverse transpose
                        double c00 = (m11 * m22 - m12 * m21) / det, c01 = (m12 * m20 - m10 * m22) / det, c02 = (m10 * m21 - m11 * m20) / det,
                            c10 = (m02 * m21 - m01 * m22) / det, c11 = (m00 * m22 - m02 * m20) / det, c12 = (m01 * m20 - m00 * m21) / det,
                            c20 = (m01 * m12 - m02 * m11) / det, c21 = (m02 * m10 - m00 * m12) / det, c22 = (m00 * m11 - m01 * m10) / det;
                        for (int i = 0; i < n; i++)
                        {
                            double x = nr[3 * i], y = nr[3 * i + 1], z = nr[3 * i + 2];
                            nrm[3 * i] = x * c00 + y * c01 + z * c02; nrm[3 * i + 1] = x * c10 + y * c11 + z * c12; nrm[3 * i + 2] = x * c20 + y * c21 + z * c22;
                        }
                    }
                    else
                        for (int t = 0; t + 2 < tri.Length; t += 3)
                        {
                            int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                            double ux = pw[3 * b] - pw[3 * a], uy = pw[3 * b + 1] - pw[3 * a + 1], uz = pw[3 * b + 2] - pw[3 * a + 2];
                            double vx = pw[3 * c] - pw[3 * a], vy = pw[3 * c + 1] - pw[3 * a + 1], vz = pw[3 * c + 2] - pw[3 * a + 2];
                            double fx = uy * vz - uz * vy, fy = uz * vx - ux * vz, fz = ux * vy - uy * vx;
                            foreach (int q in new[] { a, b, c }) { nrm[3 * q] += fx; nrm[3 * q + 1] += fy; nrm[3 * q + 2] += fz; }
                        }
                    for (int i = 0; i < n; i++)
                    {
                        double l = Math.Max(Math.Sqrt(nrm[3 * i] * nrm[3 * i] + nrm[3 * i + 1] * nrm[3 * i + 1] + nrm[3 * i + 2] * nrm[3 * i + 2]), 1e-9);
                        nrm[3 * i] /= l; nrm[3 * i + 1] /= l; nrm[3 * i + 2] /= l;
                    }
                    if (det < 0) for (int t = 0; t + 2 < tri.Length; t += 3) (tri[t + 1], tri[t + 2]) = (tri[t + 2], tri[t + 1]);
                    int stride2 = at.ContainsKey("TEXCOORD_0") ? uv.Length / Math.Max(n, 1) : 2;
                    var uv2 = new double[n * 2];
                    for (int i = 0; i < n; i++) { uv2[2 * i] = uv[stride2 * i]; uv2[2 * i + 1] = uv[stride2 * i + 1]; }
                    g.Parts.Add(new GlbPart { Pos = pw, Nrm = nrm, Uv = uv2, Tri = tri, Mat = Material(prim["material"] == null ? null : prim.Int("material")) });
                }
            foreach (var c in node.Arr("children") ?? new()) Visit((int)Json.L(c), world);
        }
        var eye = new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        IEnumerable<int> roots;
        if (js.Arr("scenes") is { Count: > 0 } sc) roots = (((JObj)sc[(int)js.Int("scene")]!).Arr("nodes") ?? new()).Select(v => (int)Json.L(v));
        else roots = Enumerable.Range(0, nodes.Count);
        foreach (int ni in roots) Visit(ni, eye);
        if (g.Parts.Count == 0) throw new ImportProblem($"No triangles found in this .glb (only {(skipped.Count > 0 ? string.Join(", ", skipped) : "empty")}?).");
        g.Skipped = skipped.ToList();
        return g;
    }

    /// the parts in the game's axes (z mirrored, winding reversed), standing on y = 0, centred on x / z; also the box
    public static (List<GlbPart> Parts, double[] Lo, double[] Hi) GameSpace(Glb g)
    {
        var lo = new[] { double.MaxValue, double.MaxValue, double.MaxValue }; var hi = new[] { double.MinValue, double.MinValue, double.MinValue };
        foreach (var p in g.Parts)
            for (int i = 0; i < p.Pos.Length; i++) { int c = i % 3; lo[c] = Math.Min(lo[c], p.Pos[i]); hi[c] = Math.Max(hi[c], p.Pos[i]); }
        double sx = -(lo[0] + hi[0]) / 2, sy = -lo[1], sz = -(lo[2] + hi[2]) / 2;
        var o = new List<GlbPart>();
        foreach (var p in g.Parts)
        {
            var pos = new double[p.Pos.Length]; var nrm = new double[p.Nrm.Length]; var tri = (int[])p.Tri.Clone();
            for (int i = 0; i < p.N; i++)
            {
                pos[3 * i] = p.Pos[3 * i] + sx; pos[3 * i + 1] = p.Pos[3 * i + 1] + sy; pos[3 * i + 2] = (p.Pos[3 * i + 2] + sz) * -1.0;
                nrm[3 * i] = p.Nrm[3 * i]; nrm[3 * i + 1] = p.Nrm[3 * i + 1]; nrm[3 * i + 2] = p.Nrm[3 * i + 2] * -1.0;
            }
            for (int t = 0; t + 2 < tri.Length; t += 3) (tri[t + 1], tri[t + 2]) = (tri[t + 2], tri[t + 1]);
            o.Add(new GlbPart { Pos = pos, Nrm = nrm, Uv = p.Uv, Tri = tri, Mat = p.Mat });
        }
        return (o, new[] { lo[0] + sx, 0.0, -(hi[2] + sz) }, new[] { hi[0] + sx, hi[1] + sy, -(lo[2] + sz) });
    }

    public static List<Piece3> Pieces(List<GlbPart> parts)
    {
        var o = new List<Piece3>();
        foreach (var grp in parts.GroupBy(p => p.Mat).OrderBy(g => g.Key))
        {
            Piece3? cur = null;
            foreach (var p in grp)
            {
                var vmap = new Dictionary<int, int>();
                for (int t = 0; t + 2 < p.Tri.Length; t += 3)
                {
                    int a = p.Tri[t], b = p.Tri[t + 1], c = p.Tri[t + 2];
                    if (a == b || b == c || a == c) continue;
                    int nw = (vmap.ContainsKey(a) ? 0 : 1) + (vmap.ContainsKey(b) ? 0 : 1) + (vmap.ContainsKey(c) ? 0 : 1);
                    int join = cur != null && cur.Strip.Count > 0 ? 2 + cur.Strip.Count % 2 : 0;
                    if (cur == null || cur.Verts.Count + nw > VCap || cur.Strip.Count + join + 3 > ICap)
                    {
                        cur = new Piece3 { Mat = grp.Key };
                        o.Add(cur);
                        vmap = new();
                    }
                    var idx = new int[3]; int q = 0;
                    foreach (int v in new[] { a, b, c })
                    {
                        if (!vmap.TryGetValue(v, out int m))
                        {
                            m = vmap[v] = cur.Verts.Count;
                            cur.Verts.Add(new[] { p.Pos[3 * v], p.Pos[3 * v + 1], p.Pos[3 * v + 2], p.Nrm[3 * v], p.Nrm[3 * v + 1], p.Nrm[3 * v + 2], p.Uv[2 * v], p.Uv[2 * v + 1] });
                        }
                        idx[q++] = m;
                    }
                    var st = cur.Strip;
                    if (st.Count > 0) { st.Add(st[^1]); st.Add(idx[0]); if (st.Count % 2 != 0) st.Add(idx[0]); }
                    st.AddRange(idx);
                }
            }
        }
        return o;
    }

    static List<JObj> Sidecars()
    {
        var o = new List<JObj>();
        if (!Directory.Exists(Folder)) return o;
        foreach (var f in Directory.GetFiles(Folder).OrderBy(x => x, StringComparer.Ordinal))
            if (f.EndsWith(".glb.json", StringComparison.OrdinalIgnoreCase))
                try { o.Add((JObj)Json.Load(f)!); } catch (Exception) { }
        return o;
    }

    public static List<JObj> Entries()
    {
        var o = new List<JObj>();
        foreach (var s in Sidecars())
        {
            if (!File.Exists(Path.Combine(Folder, s.Str("file")!))) continue;
            o.Add(new JObj
            {
                ["id"] = "import:" + s.Str("file"), ["kind"] = "import", ["name"] = s["name"], ["loc"] = "Your models", ["cat"] = "Imported", ["model"] = s["file"],
                ["uid"] = Uids((int)s.Int("slot"), 0).Model.ToString("X8"), ["lo"] = s["lo"], ["hi"] = s["hi"], ["sized"] = true, ["slot"] = s["slot"], ["pieces"] = s["pieces"], ["tris"] = s["tris"],
            });
        }
        return o;
    }

    public static (uint Model, uint Def) Uids(int slot, int k)
    {
        uint b = ImportUid + (uint)slot * 256;
        return (b + 2 * (uint)k, b + 2 * (uint)k + 1);
    }

    public static string ModelFile(int slot, int k) => $"models\\objects\\flat_earth\\imp_{slot:x2}_{k:000}.model";

    public static List<JObj> Used(JObj wld)
    {
        var ids = new HashSet<string>();
        foreach (var loc in wld.Objs("locations")) foreach (var m in loc.Objs("maps")) foreach (var o in m.Objs("objects")) if (o.Str("id") is string s) ids.Add(s);
        return Entries().Where(e => ids.Contains(e.Str("id")!)).ToList();
    }

    static byte Col(double c) => (byte)Py.Round(255 * Math.Max(0.0, Math.Min(1.0, c)));

    public static (byte[] Diff, byte[] Mat) Textures(GlbMat mat, int w, int h, int mw, int mh)
    {
        byte r = Col(mat.Colour[0]), g = Col(mat.Colour[1]), b = Col(mat.Colour[2]);
        byte[] diff;
        if (mat.Image != null)
        {
            var img = mat.Image.ResizeLanczos(w, h);
            diff = img.Px;
            if (r != 255 || g != 255 || b != 255)
                for (int i = 0; i < diff.Length; i += 3)
                {
                    diff[i] = (byte)Math.Max(0, Math.Min(255, diff[i] * (r / 255.0)));
                    diff[i + 1] = (byte)Math.Max(0, Math.Min(255, diff[i + 1] * (g / 255.0)));
                    diff[i + 2] = (byte)Math.Max(0, Math.Min(255, diff[i + 2] * (b / 255.0)));
                }
        }
        else diff = CpModel.Solid(w, h, r, g, b);
        return (diff, CpModel.Solid(mw, mh, Shine, (byte)Py.Round(255 * mat.Glow), 0));
    }

    [SupportedOSPlatform("windows")]
    public static List<KeyValuePair<string, byte[]>> BuildFiles(byte[] bas, JObj wld, Action<string> log)
    {
        var tex = CpModel.Parse(bas).Textures;
        var (_, tw, th, tm) = tex[RoadMesh.Diff]; var (_, mw, mh, mm) = tex[RoadMesh.Mat];
        var files = new List<KeyValuePair<string, byte[]>>();
        foreach (var e in Used(wld))
        {
            var g = ReadGlb(Path.Combine(Folder, e.Str("model")!));
            var (parts, _, _) = GameSpace(g);
            var ps = Pieces(parts);
            if (ps.Count != e.Int("pieces")) throw new InvalidOperationException($"{e.Str("model")} changed since it was imported - import it again");
            var enc = new Dictionary<int, Dictionary<string, byte[]>>();
            foreach (var p in ps)
                if (!enc.ContainsKey(p.Mat))
                {
                    var (d, mt) = Textures(g.Materials[p.Mat], tw, th, mw, mh);
                    enc[p.Mat] = new() { [RoadMesh.Diff] = CpModel.EncodeMips(d, tw, th, tm), [RoadMesh.Mat] = CpModel.EncodeMips(mt, mw, mh, mm) };
                }
            int slot = (int)e.Int("slot");
            var built = new byte[ps.Count][];
            Parallel.For(0, ps.Count, k =>
            {
                var p = ps[k];
                built[k] = CpModel.NewTextureIds(CpModel.MakeMesh(bas, p.Verts, p.Strip, enc[p.Mat]), 0x0F5A0000 | ((uint)slot << 8) | ((uint)p.Mat & 0xFF));
            });
            for (int k = 0; k < ps.Count; k++) files.Add(new(ModelFile(slot, k), built[k]));
            log($"imported model {e.Str("name")}: {e.Int("tris")} triangles in {ps.Count} piece(s)");
        }
        return files;
    }
}

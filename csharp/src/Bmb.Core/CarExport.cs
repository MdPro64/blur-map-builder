// A game car's own model saved as a .glb (glTF binary) for Blender and other 3D programs: every part at the finest detail,
// each part its own object with the game's own name and the game's own tree (exterior > doors > mirrors, wheels > each
// wheel > tyre / rim / disc / caliper; a wheel turns round its middle), looks shared by what a part is (body, glass,
// lights, tyres, rims ...), the body with its picture when the car has one. The axes are
// the ones the body import reads back (x = side, y = up, -z = forward), so an edited export can be imported as a car body.
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace Bmb.Core;

public static class CarExport
{
    // the parts, their names in the file and their look (colour linear-ish RGBA, metal, rough, glow)
    enum Part { Body, Paint, Glass, HeadLamp, RedLamp, AmberLamp, LampGlass, Tyre, Rim, Disc, Caliper, Trim }
    static readonly (string Name, float[] Col, float Metal, float Rough, float[]? Glow)[] Looks =
    {
        ("body (picture)", new[] { 1f, 1f, 1f, 1f }, 0.3f, 0.35f, null),
        ("body paint", new[] { 0.55f, 0.56f, 0.6f, 1f }, 0.3f, 0.35f, null),
        ("glass", new[] { 0.05f, 0.06f, 0.08f, 0.35f }, 0f, 0.05f, null),
        ("head lights", new[] { 1f, 1f, 0.95f, 1f }, 0f, 0.2f, new[] { 1f, 1f, 0.9f }),
        ("rear lights", new[] { 0.8f, 0.02f, 0.02f, 1f }, 0f, 0.2f, new[] { 0.8f, 0f, 0f }),
        ("signal lights", new[] { 1f, 0.45f, 0.02f, 1f }, 0f, 0.2f, new[] { 1f, 0.4f, 0f }),
        ("light covers", new[] { 0.8f, 0.85f, 0.9f, 0.25f }, 0f, 0.05f, null),
        ("tyres", new[] { 0.03f, 0.03f, 0.035f, 1f }, 0f, 0.9f, null),
        ("rims", new[] { 0.7f, 0.72f, 0.75f, 1f }, 1f, 0.25f, null),
        ("brake discs", new[] { 0.35f, 0.35f, 0.36f, 1f }, 1f, 0.4f, null),
        ("brake calipers", new[] { 0.6f, 0.05f, 0.05f, 1f }, 0.2f, 0.4f, null),
        ("trim", new[] { 0.06f, 0.06f, 0.07f, 1f }, 0.1f, 0.6f, null),
    };

    static Part Classify(string fx, string el)
    {
        fx = fx.ToLowerInvariant(); el = el.ToLowerInvariant();
        if (fx.Contains("paint")) return fx.Contains("_c") ? Part.Body : Part.Paint;     // "c" = with the body picture
        if (fx.Contains("glass")) return el.Contains("headlightcover") ? Part.LampGlass : Part.Glass;
        if (fx.Contains("light"))
        {
            if (el.Contains("rear") || el.Contains("brake") || el.Contains("reverse")) return Part.RedLamp;
            if (el.Contains("running") || el.Contains("signal")) return Part.AmberLamp;
            return Part.HeadLamp;
        }
        if (fx.Contains("alloy")) return Part.Rim;
        if (el.Contains("tyre")) return Part.Tyre;
        if (el.Contains("brakedisc")) return Part.Disc;
        if (el.Contains("brakecaliper")) return Part.Caliper;
        return Part.Trim;
    }

    static readonly string[] Skip = { "collision", "collider", "shadow", "dam", "debris", "broken", "smash" };

    sealed class Piece { public List<float> P = new(), N = new(), UV = new(); public List<int> Tri = new(); }
    sealed class Node { public string Name = ""; public float[] At = new float[3]; public List<(Part Kind, Piece P)> Pieces = new(); public List<int> Kids = new(); }

    static string Esc(string s) => new string(s.Where(c => c >= ' ' && c != '"' && c != '\\').ToArray());

    /// the car's model as .glb bytes; log gets one line on what went in
    public static byte[] Glb(string car, Action<string>? log = null)
    {
        var data = Skins.ModelBytes(car);
        var M = CpModel.Read(data);
        var parts = new Dictionary<(int El, Part Kind), Piece>();
        foreach (var mesh in M.MeshInfos)
        {
            if (mesh.Material < 0 || mesh.Material >= M.FxFiles.Count || mesh.Data1.Count == 0 || mesh.VOffset.Count == 0) continue;
            string fx = M.FxFiles[mesh.Material];
            var el = mesh.Object >= 0 && mesh.Object < M.AllElements.Count ? M.AllElements[mesh.Object] : null;
            string name = (el?.Name ?? "").ToLowerInvariant();
            if (fx.Contains("shadow", StringComparison.OrdinalIgnoreCase) || Skip.Any(name.Contains)) continue;
            // the finest level that has triangles
            var lv = mesh.Data1.FirstOrDefault(d => d.FaceCount > 0);
            var (fo, fc, _, _) = lv;
            if (fc < 3) continue;
            var fs = M.FStreams[Math.Clamp(mesh.FaceStream, 0, M.FStreams.Count - 1)];
            if (fo + fc > fs.Count) fs = M.FStreams.FirstOrDefault(q => fo + fc <= q.Count) ?? fs;
            if (fo < 0 || fo + fc > fs.Count) continue;
            var idx = new int[fc];
            for (int i = 0; i < fc; i++) idx[i] = B.U16(data, fs.Start + 2 * (fo + i));
            var tris = new List<(int, int, int)>();
            if (mesh.FaceType == 1) { for (int i = 0; i + 2 < fc; i++) { int a = idx[i], b = idx[i + 1], c = idx[i + 2]; if (a != b && b != c && a != c) tris.Add(i % 2 == 0 ? (a, b, c) : (a, c, b)); } }
            else for (int i = 0; i + 2 < fc; i += 3) { int a = idx[i], b = idx[i + 1], c = idx[i + 2]; if (a != b && b != c && a != c) tris.Add((a, b, c)); }
            if (tris.Count == 0) continue;
            int maxIdx = idx.Max();
            // the vertex block: the same layout, the offset fits, the indices fit; of those the one with the shortest edges
            var types = M.VertDefs[mesh.Definition].Where(d => d.Prefix == 0).Select(d => d.Type).ToList();
            VStreamInfo? best = null; int bestBase = 0, posAt = 0, posType = 9, uvAt = -1; float bestEdge = float.MaxValue;
            foreach (var vs in M.VStreams)
            {
                if (vs.Bytes <= 0 || mesh.VOffset[0] % vs.Bytes != 0 || !vs.Def.Select(d => d.Type).SequenceEqual(types)) continue;
                int vb = mesh.VOffset[0] / vs.Bytes;
                if (vb + maxIdx >= vs.Count) continue;
                int pAt = -1, pType = 0, uAt = -1;
                for (int k = 0; k < vs.Def.Count; k++)
                {
                    var (t, ch, sub) = vs.Def[k];
                    if (ch == 0 && sub == 0 && pAt < 0) { pAt = vs.Offs[k]; pType = t; }
                    if (ch == 5 && sub == 0 && t == 9 && uAt < 0) uAt = vs.Offs[k];
                }
                if (pAt < 0) continue;
                var edges = tris.Take(64).Select(t => Dist(Pos(data, vs, vb, t.Item1, pAt, pType), Pos(data, vs, vb, t.Item2, pAt, pType))).OrderBy(e => e).ToList();
                float med = edges[edges.Count / 2];
                if (float.IsFinite(med) && med < bestEdge) { bestEdge = med; best = vs; bestBase = vb; posAt = pAt; posType = pType; uvAt = uAt; }
            }
            if (best == null) continue;
            var kind = Classify(fx, name);
            int elAt = el != null ? mesh.Object : 0;
            if (!parts.TryGetValue((elAt, kind), out var pc)) parts[(elAt, kind)] = pc = new Piece();
            var map = new Dictionary<int, int>();
            var local = new List<float[]>(); var luv = new List<float[]>();
            foreach (int v in tris.SelectMany(t => new[] { t.Item1, t.Item2, t.Item3 }).Distinct())
            {
                map[v] = local.Count;
                local.Add(Pos(data, best, bestBase, v, posAt, posType));
                int a = best.Start + (bestBase + v) * best.Bytes;
                luv.Add(uvAt >= 0 ? new[] { (float)BitConverter.ToHalf(data, a + uvAt), (float)BitConverter.ToHalf(data, a + uvAt + 2) } : new[] { 0f, 0f });
            }
            // a wheel part stored around its own middle: put it where its element is
            if (el != null && el.M.Length >= 12)
            {
                var tr = new[] { el.M[9], el.M[10], el.M[11] };
                var mid = new float[3]; foreach (var q in local) for (int k = 0; k < 3; k++) mid[k] += q[k] / local.Count;
                float Len(float[] q) => MathF.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2]);
                if (Len(tr) > 0.3f && Len(mid) < 0.3f && Dist(mid, tr) > 0.3f)
                    for (int i = 0; i < local.Count; i++)
                    {
                        var q = local[i];
                        local[i] = new[] { q[0] * el.M[0] + q[1] * el.M[3] + q[2] * el.M[6] + tr[0], q[0] * el.M[1] + q[1] * el.M[4] + q[2] * el.M[7] + tr[1], q[0] * el.M[2] + q[1] * el.M[5] + q[2] * el.M[8] + tr[2] };
                    }
            }
            int bas0 = pc.P.Count / 3;
            // into the file's axes (z turned round, so the winding turns round too)
            foreach (var q in local) { pc.P.Add(q[0]); pc.P.Add(q[1]); pc.P.Add(-q[2]); }
            foreach (var u in luv) { pc.UV.Add(u[0]); pc.UV.Add(u[1]); }
            foreach (var (a, b, c) in tris) { pc.Tri.Add(bas0 + map[a]); pc.Tri.Add(bas0 + map[c]); pc.Tri.Add(bas0 + map[b]); }
        }
        if (parts.Count == 0) throw new InvalidDataException("no triangles read from " + car);
        // the tyres stand on y = 0, the car's middle at x = z = 0
        var tyres = parts.Where(k => k.Key.Kind == Part.Tyre).Select(k => k.Value).ToList();
        float ground = tyres.Count > 0 ? tyres.Min(p => MinOf(p.P, 1)) : parts.Values.Min(p => MinOf(p.P, 1));
        float zMid = (parts.Values.Min(p => MinOf(p.P, 2)) + parts.Values.Max(p => MaxOf(p.P, 2))) / 2;
        foreach (var p in parts.Values) for (int i = 0; i < p.P.Count; i += 3) { p.P[i + 1] -= ground; p.P[i + 2] -= zMid; }
        foreach (var p in parts.Values) Normals(p);
        // the body picture (a car with a picture on its body)
        byte[]? png = null; float[]? plain = null;
        if (parts.Keys.Any(k => k.Kind == Part.Body))
            try
            {
                var sc = Skins.Cars().FirstOrDefault(c => string.Equals(c.Car, car, StringComparison.OrdinalIgnoreCase) && c.Pictures.Count > 0);
                if (sc != null)
                {
                    var pic = Skins.Picture(car, sc.Pictures[0]);
                    png = Png(pic);
                    // the plain paint: the picture's main colour (as the game paints those parts in the car's colour)
                    double r = 0, g = 0, bl = 0; int n = 0;
                    for (int i = 0; i + 3 < pic.Px.Length; i += 4)
                    {
                        int R = pic.Px[i], G = pic.Px[i + 1], Bc = pic.Px[i + 2];
                        if (R * 0.3 + G * 0.6 + Bc * 0.1 > 60) { r += R; g += G; bl += Bc; n++; }
                    }
                    if (n > 0) plain = new[] { Lin(r / n), Lin(g / n), Lin(bl / n), 1f };
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or KeyNotFoundException) { png = null; }
        int tris0 = parts.Values.Sum(p => p.Tri.Count / 3);
        // the game's parts as a tree: every element with triangles and the ones above it. Its point = where the game turns
        // it (a wheel's middle), in the file's axes; a part without its own point keeps the one above it
        var els = M.AllElements;
        bool Ok(int e) => e >= 0 && e < els.Count;
        var keep = new HashSet<int> { 0 };
        foreach (int e in parts.Keys.Select(k => k.El).Distinct())
            for (int q = e, g = 0; Ok(q) && g < 64; q = els[q].Parent, g++) keep.Add(q);
        var nodes = new List<Node>();
        int Add(int e, int parentNode, float[] parentPivot)
        {
            var pv = parentPivot;
            if (e != 0 && Ok(e) && els[e].M.Length >= 12 && Ok(els[e].Parent) && els[els[e].Parent].M.Length >= 12
                && MathF.Abs(els[e].M[9] - els[els[e].Parent].M[9]) + MathF.Abs(els[e].M[10] - els[els[e].Parent].M[10]) + MathF.Abs(els[e].M[11] - els[els[e].Parent].M[11]) < 1e-3f) { }     // the same point as the part above it
            else if (e != 0 && Ok(e) && els[e].M.Length >= 12 && MathF.Abs(els[e].M[9]) + MathF.Abs(els[e].M[10]) + MathF.Abs(els[e].M[11]) > 1e-3f)
                pv = new[] { els[e].M[9], els[e].M[10] - ground, -els[e].M[11] - zMid };
            var n = new Node { Name = e == 0 || !Ok(e) ? car : els[e].Name, At = new[] { pv[0] - parentPivot[0], pv[1] - parentPivot[1], pv[2] - parentPivot[2] } };
            foreach (var (k, p) in parts.Where(q => q.Key.El == e).OrderBy(q => q.Key.Kind))
            {
                for (int i = 0; i < p.P.Count; i += 3) { p.P[i] -= pv[0]; p.P[i + 1] -= pv[1]; p.P[i + 2] -= pv[2]; }
                n.Pieces.Add((k.Kind, p));
            }
            nodes.Add(n); int me = nodes.Count - 1;
            if (parentNode >= 0) nodes[parentNode].Kids.Add(me);
            for (int c = 0; c < els.Count; c++)
                if (c != e && els[c].Parent == e && keep.Contains(c)) Add(c, me, pv);
            return me;
        }
        bool InTree(int e) { for (int q = e, g = 0; Ok(q) && g < 64; q = els[q].Parent, g++) if (q == 0) return true; return false; }
        Add(0, -1, new float[3]);
        // a part whose element is not under the car's first one (rare): straight under the car
        foreach (int e in keep.Where(e => e != 0 && !InTree(e) && !(Ok(e) && keep.Contains(els[e].Parent))).OrderBy(e => e).ToList()) Add(e, 0, new float[3]);
        log?.Invoke($"{car}: {tris0} triangles in {nodes.Count(n => n.Pieces.Count > 0)} parts{(png != null ? ", with its body picture" : "")}");
        return Write(nodes, png, plain);
    }

    static float Lin(double c) { double v = c / 255; return (float)(v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4)); }

    static float[] Pos(byte[] d, VStreamInfo vs, int vb, int v, int at, int type)
    {
        int a = vs.Start + (vb + v) * vs.Bytes + at;
        return type == 6 ? new[] { BitConverter.ToSingle(d, a), BitConverter.ToSingle(d, a + 4), BitConverter.ToSingle(d, a + 8) }
                         : new[] { (float)BitConverter.ToHalf(d, a), (float)BitConverter.ToHalf(d, a + 2), (float)BitConverter.ToHalf(d, a + 4) };
    }
    static float Dist(float[] a, float[] b) { float x = a[0] - b[0], y = a[1] - b[1], z = a[2] - b[2]; return MathF.Sqrt(x * x + y * y + z * z); }
    static float MinOf(List<float> p, int c) { float m = float.MaxValue; for (int i = c; i < p.Count; i += 3) m = Math.Min(m, p[i]); return m; }
    static float MaxOf(List<float> p, int c) { float m = float.MinValue; for (int i = c; i < p.Count; i += 3) m = Math.Max(m, p[i]); return m; }

    /// smooth normals inside each part (the game splits its points at hard edges already)
    static void Normals(Piece p)
    {
        int n = p.P.Count / 3; var nr = new float[3 * n];
        for (int t = 0; t < p.Tri.Count; t += 3)
        {
            int a = p.Tri[t], b = p.Tri[t + 1], c = p.Tri[t + 2];
            float ux = p.P[3 * b] - p.P[3 * a], uy = p.P[3 * b + 1] - p.P[3 * a + 1], uz = p.P[3 * b + 2] - p.P[3 * a + 2];
            float vx = p.P[3 * c] - p.P[3 * a], vy = p.P[3 * c + 1] - p.P[3 * a + 1], vz = p.P[3 * c + 2] - p.P[3 * a + 2];
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            foreach (int v in new[] { a, b, c }) { nr[3 * v] += nx; nr[3 * v + 1] += ny; nr[3 * v + 2] += nz; }
        }
        for (int i = 0; i < n; i++)
        {
            float l = MathF.Sqrt(nr[3 * i] * nr[3 * i] + nr[3 * i + 1] * nr[3 * i + 1] + nr[3 * i + 2] * nr[3 * i + 2]);
            if (l < 1e-12f) { nr[3 * i] = 0; nr[3 * i + 1] = 1; nr[3 * i + 2] = 0; } else { nr[3 * i] /= l; nr[3 * i + 1] /= l; nr[3 * i + 2] /= l; }
        }
        p.N = nr.ToList();
    }

    static byte[] Png(Rgba img)
    {
        using var bm = new Bitmap(img.W, img.H, PixelFormat.Format32bppArgb);
        var d = bm.LockBits(new Rectangle(0, 0, img.W, img.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[img.W * 4];
            for (int y = 0; y < img.H; y++)
            {
                for (int x = 0; x < img.W; x++) { int s = 4 * (y * img.W + x); row[4 * x] = img.Px[s + 2]; row[4 * x + 1] = img.Px[s + 1]; row[4 * x + 2] = img.Px[s]; row[4 * x + 3] = 255; }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, row.Length);
            }
        }
        finally { bm.UnlockBits(d); }
        using var ms = new MemoryStream();
        bm.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    // ---- the glTF binary file ------------------------------------------------------------------------------------------

    static byte[] Write(List<Node> nodes, byte[]? png, float[]? plain)
    {
        var bin = new MemoryStream();
        var views = new List<string>(); var accs = new List<string>(); var prims = new List<string>(); var mats = new List<string>();
        string F(float v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        int View(byte[] b, int? target)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            views.Add($"{{\"buffer\":0,\"byteOffset\":{bin.Length},\"byteLength\":{b.Length}{(target != null ? $",\"target\":{target}" : "")}}}");
            bin.Write(b);
            return views.Count - 1;
        }
        int Acc(List<float> v, int comps, bool minMax)
        {
            var b = new byte[v.Count * 4]; Buffer.BlockCopy(v.ToArray(), 0, b, 0, b.Length);
            int vw = View(b, 34962);
            string mm = "";
            if (minMax)
            {
                var lo = new float[comps]; var hi = new float[comps];
                for (int c = 0; c < comps; c++) { lo[c] = float.MaxValue; hi[c] = float.MinValue; }
                for (int i = 0; i < v.Count; i++) { lo[i % comps] = Math.Min(lo[i % comps], v[i]); hi[i % comps] = Math.Max(hi[i % comps], v[i]); }
                mm = $",\"min\":[{string.Join(",", lo.Select(F))}],\"max\":[{string.Join(",", hi.Select(F))}]";
            }
            accs.Add($"{{\"bufferView\":{vw},\"componentType\":5126,\"count\":{v.Count / comps},\"type\":\"{(comps == 3 ? "VEC3" : "VEC2")}\"{mm}}}");
            return accs.Count - 1;
        }
        int Idx(List<int> t)
        {
            var b = new byte[t.Count * 4]; Buffer.BlockCopy(t.ToArray(), 0, b, 0, b.Length);
            int vw = View(b, 34963);
            accs.Add($"{{\"bufferView\":{vw},\"componentType\":5125,\"count\":{t.Count},\"type\":\"SCALAR\"}}");
            return accs.Count - 1;
        }
        string images = "", textures = "", samplers = "";
        if (png != null)
        {
            int pv = View(png, null);
            images = $",\"images\":[{{\"bufferView\":{pv},\"mimeType\":\"image/png\",\"name\":\"body\"}}]";
            textures = ",\"textures\":[{\"source\":0,\"sampler\":0}]";
            samplers = ",\"samplers\":[{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}]";
        }
        // one look per kind, shared by every part that has it
        var matOf = new Dictionary<Part, int>();
        foreach (var kind in nodes.SelectMany(n => n.Pieces).Select(p => p.Kind).Distinct().OrderBy(k => k))
        {
            var (name, col, metal, rough, glow) = Looks[(int)kind];
            bool tex = kind == Part.Body && png != null;
            if (kind == Part.Body && png == null) { name = Looks[(int)Part.Paint].Name; col = Looks[(int)Part.Paint].Col; }
            if (kind is Part.Paint || (kind == Part.Body && png == null)) col = plain ?? col;
            var m = new StringBuilder($"{{\"name\":\"{name}\",\"pbrMetallicRoughness\":{{\"baseColorFactor\":[{string.Join(",", col.Select(F))}],\"metallicFactor\":{F(metal)},\"roughnessFactor\":{F(rough)}");
            if (tex) m.Append(",\"baseColorTexture\":{\"index\":0}");
            m.Append('}');
            if (glow != null) m.Append($",\"emissiveFactor\":[{string.Join(",", glow.Select(F))}]");
            if (col[3] < 1) m.Append(",\"alphaMode\":\"BLEND\"");
            if (kind is Part.Glass or Part.LampGlass) m.Append(",\"doubleSided\":true");
            m.Append('}');
            mats.Add(m.ToString());
            matOf[kind] = mats.Count - 1;
        }
        var meshes = new List<string>(); var nodeJs = new List<string>();
        foreach (var n in nodes)
        {
            string mesh = "";
            if (n.Pieces.Count > 0)
            {
                prims.Clear();
                foreach (var (kind, p) in n.Pieces)
                {
                    int pos = Acc(p.P, 3, true), nrm = Acc(p.N, 3, false), uv = Acc(p.UV, 2, false), ind = Idx(p.Tri);
                    prims.Add($"{{\"attributes\":{{\"POSITION\":{pos},\"NORMAL\":{nrm},\"TEXCOORD_0\":{uv}}},\"indices\":{ind},\"material\":{matOf[kind]}}}");
                }
                meshes.Add($"{{\"name\":\"{Esc(n.Name)}\",\"primitives\":[{string.Join(",", prims)}]}}");
                mesh = $",\"mesh\":{meshes.Count - 1}";
            }
            string at = n.At.Any(v => v != 0) ? $",\"translation\":[{string.Join(",", n.At.Select(F))}]" : "";
            string kids = n.Kids.Count > 0 ? $",\"children\":[{string.Join(",", n.Kids)}]" : "";
            nodeJs.Add($"{{\"name\":\"{Esc(n.Name)}\"{mesh}{at}{kids}}}");
        }
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        string json = $"{{\"asset\":{{\"version\":\"2.0\",\"generator\":\"Blur Map Builder\"}},\"scene\":0,\"scenes\":[{{\"nodes\":[0]}}],\"nodes\":[{string.Join(",", nodeJs)}]," +
                      $"\"meshes\":[{string.Join(",", meshes)}],\"materials\":[{string.Join(",", mats)}]{images}{textures}{samplers}," +
                      $"\"accessors\":[{string.Join(",", accs)}],\"bufferViews\":[{string.Join(",", views)}],\"buffers\":[{{\"byteLength\":{bin.Length}}}]}}";
        var jb = Encoding.UTF8.GetBytes(json);
        int jLen = (jb.Length + 3) / 4 * 4;
        var o = new MemoryStream();
        void U32(uint v) => o.Write(BitConverter.GetBytes(v));
        U32(0x46546C67); U32(2); U32((uint)(12 + 8 + jLen + 8 + bin.Length));
        U32((uint)jLen); U32(0x4E4F534A); o.Write(jb); for (int i = jb.Length; i < jLen; i++) o.WriteByte(0x20);
        U32((uint)bin.Length); U32(0x004E4942); o.Write(bin.ToArray());
        return o.ToArray();
    }
}

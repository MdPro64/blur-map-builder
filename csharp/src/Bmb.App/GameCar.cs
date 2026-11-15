// The Lighting preview's car: the game's own Dodge Challenger Tuner, read from the player's root.pak.original (its model at a
// lower detail level, its blue body picture). Made once into a small file in the data folder (on the first start, and again
// when the game's files change or this program's car format changes), so the Lighting tab opens fast. If the game files
// cannot be read, the preview uses a simple car made in code.
using System.Numerics;
using Bmb.Core;

namespace Bmb.App;

/// a car for the preview: triangles with a material each, a body picture (linear colour) for the paint
public sealed class CarMesh
{
    // materials of a game car (a code car uses its own part numbers, see PreviewCar)
    public const byte Paint = 0, Glass = 1, HeadLamp = 2, RedLamp = 3, AmberLamp = 4, Tyre = 5, Rim = 6, Trim = 7, Plate = 8, Disc = 9, Caliper = 10, LampGlass = 11, PlainPaint = 12;

    public Vector3[] P = Array.Empty<Vector3>(), N = Array.Empty<Vector3>();
    public Vector2[] UV = Array.Empty<Vector2>();
    public int[] Tri = Array.Empty<int>();
    public byte[] Mat = Array.Empty<byte>();          // per triangle
    public Vector3[] Wheels = new Vector3[4];
    public float HalfLen = 2.2f, HalfWid = 0.9f, Height = 1.4f, WheelR = 0.33f;
    public bool Game;                                  // false = the code car (its Mat = part numbers)
    public int TexW, TexH; public float[] Tex = Array.Empty<float>();   // linear RGB
    public int Triangles => Tri.Length / 3;
    /// the plain paint (parts without the body picture): the body picture's main colour
    public Vector3 PaintColour = new(0.1f, 0.3f, 0.6f);

    void FindPaintColour()
    {
        var sum = Vector3.Zero; int n = 0;
        for (int i = 0; i + 2 < Tex.Length; i += 3)
        {
            var c = new Vector3(Tex[i], Tex[i + 1], Tex[i + 2]);
            if (c.X * 0.3f + c.Y * 0.6f + c.Z * 0.1f > 0.04f) { sum += c; n++; }
        }
        if (n > 0) PaintColour = sum / n;
    }

    public Vector3 TexAt(Vector2 uv)
    {
        if (TexW == 0) return new Vector3(0.1f, 0.3f, 0.6f);
        float fx = (uv.X - MathF.Floor(uv.X)) * TexW - 0.5f, fy = (uv.Y - MathF.Floor(uv.Y)) * TexH - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy); float tx = fx - x0, ty = fy - y0;
        Vector3 Px(int x, int y) { x = (x % TexW + TexW) % TexW; y = (y % TexH + TexH) % TexH; int i = 3 * (y * TexW + x); return new Vector3(Tex[i], Tex[i + 1], Tex[i + 2]); }
        return Vector3.Lerp(Vector3.Lerp(Px(x0, y0), Px(x0 + 1, y0), tx), Vector3.Lerp(Px(x0, y0 + 1), Px(x0 + 1, y0 + 1), tx), ty);
    }

    // ---- the code car as a CarMesh
    public static CarMesh Code()
    {
        var m = new CarMesh { P = PreviewCar.P.ToArray(), N = PreviewCar.N.ToArray(), UV = new Vector2[PreviewCar.P.Count], Tri = PreviewCar.Tri.ToArray(), Wheels = PreviewCar.Wheels.ToArray(), HalfLen = PreviewCar.Lh, HalfWid = PreviewCar.Wh, WheelR = PreviewCar.WheelR };
        m.Mat = new byte[m.Triangles];
        for (int t = 0; t < m.Triangles; t++) m.Mat[t] = (byte)PreviewCar.Part[m.Tri[3 * t]];
        return m;
    }

    // ---- the game car
    public const string CarName = "dodge_challenger_tuner", BodyPic = "aotexture_blue";
    const int MaxTris = 20000, TexSize = 512, Format = 2;
    static CarMesh? _car;
    static readonly object _lock = new();
    static string CacheFile => Path.Combine(Paths.Data, "light_preview_car.bin");

    static string Stamp()
    {
        var fi = new FileInfo(Paths.RootOriginal);
        return $"{CarName} {fi.Length} {fi.LastWriteTimeUtc.Ticks} f{Format} {MaxTris}";
    }

    /// the preview's car: the game's (from the cache file, made if missing or old) or, if that fails, the code car
    public static CarMesh Get()
    {
        lock (_lock)
        {
            if (_car != null) return _car;
            try { _car = Load() ?? Make(); _car.FindPaintColour(); }
            catch (Exception e) when (e is not OutOfMemoryException) { LastError = e.Message; _car = Code(); }
            return _car;
        }
    }
    public static string? LastError;

    /// on the program's start: make the cache file now (in the background) if it is missing or old
    public static void Prepare() => ThreadPool.QueueUserWorkItem(_ => { try { Get(); } catch (Exception) { } });

    static CarMesh? Load()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            using var r = new BinaryReader(File.OpenRead(CacheFile));
            if (r.ReadString() != Stamp()) return null;
            var m = new CarMesh { Game = true };
            int nv = r.ReadInt32(); m.P = new Vector3[nv]; m.N = new Vector3[nv]; m.UV = new Vector2[nv];
            for (int i = 0; i < nv; i++) { m.P[i] = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); m.N[i] = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); m.UV[i] = new(r.ReadSingle(), r.ReadSingle()); }
            int nt = r.ReadInt32(); m.Tri = new int[3 * nt]; m.Mat = new byte[nt];
            for (int i = 0; i < 3 * nt; i++) m.Tri[i] = r.ReadInt32();
            for (int i = 0; i < nt; i++) m.Mat[i] = r.ReadByte();
            for (int i = 0; i < 4; i++) m.Wheels[i] = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            m.HalfLen = r.ReadSingle(); m.HalfWid = r.ReadSingle(); m.Height = r.ReadSingle(); m.WheelR = r.ReadSingle();
            m.TexW = r.ReadInt32(); m.TexH = r.ReadInt32(); m.Tex = new float[3 * m.TexW * m.TexH];
            for (int i = 0; i < m.Tex.Length; i++) m.Tex[i] = r.ReadByte() / 255f;
            for (int i = 0; i < m.Tex.Length; i++) m.Tex[i] = m.Tex[i] * m.Tex[i] * (0.31f * m.Tex[i] + 0.69f);   // about ^2.2
            return m;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return null; }      // a broken or old file: make it again
    }

    static void Save(CarMesh m, byte[] texGamma)
    {
        string tmp = CacheFile + ".tmp";
        using (var w = new BinaryWriter(File.Create(tmp)))
        {
            w.Write(Stamp());
            w.Write(m.P.Length);
            for (int i = 0; i < m.P.Length; i++) { w.Write(m.P[i].X); w.Write(m.P[i].Y); w.Write(m.P[i].Z); w.Write(m.N[i].X); w.Write(m.N[i].Y); w.Write(m.N[i].Z); w.Write(m.UV[i].X); w.Write(m.UV[i].Y); }
            w.Write(m.Triangles);
            foreach (int i in m.Tri) w.Write(i);
            w.Write(m.Mat);
            foreach (var p in m.Wheels) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
            w.Write(m.HalfLen); w.Write(m.HalfWid); w.Write(m.Height); w.Write(m.WheelR);
            w.Write(m.TexW); w.Write(m.TexH); w.Write(texGamma);
        }
        File.Move(tmp, CacheFile, true);
    }

    static byte Classify(string fx, string el)
    {
        fx = fx.ToLowerInvariant(); el = el.ToLowerInvariant();
        if (fx.Contains("paint")) return fx.Contains("_c") ? Paint : PlainPaint;   // "c" = with the body picture
        if (fx.Contains("glass")) return el.Contains("headlightcover") ? LampGlass : Glass;
        if (fx.Contains("light"))
        {
            if (el.Contains("rear") || el.Contains("brake")) return RedLamp;
            if (el.Contains("running") || el.Contains("signal")) return AmberLamp;
            return HeadLamp;
        }
        if (fx.Contains("alloy")) return Rim;
        if (el.Contains("tyre")) return Tyre;
        if (fx.Contains("neon")) return Plate;
        if (el.Contains("brakedisc")) return Disc;
        if (el.Contains("brakecaliper")) return Caliper;
        return Trim;
    }

    static readonly string[] Skip = { "e_interior", "steeringwheel", "gearstick", "signalstalk", "mirror_centre", "collision", "collider", "shadow" };

    /// reads the game car from root.pak.original and writes the cache file
    static CarMesh Make()
    {
        var data = Skins.ModelBytes(CarName);
        var M = CpModel.Read(data);
        // the pieces to draw
        var pieces = new List<(MeshInfo Mesh, byte Mat, ElementInfo? El)>();
        foreach (var mesh in M.MeshInfos)
        {
            if (mesh.Material < 0 || mesh.Material >= M.FxFiles.Count || mesh.Data1.Count == 0 || mesh.VOffset.Count == 0) continue;
            string fx = M.FxFiles[mesh.Material];
            var el = mesh.Object >= 0 && mesh.Object < M.AllElements.Count ? M.AllElements[mesh.Object] : null;
            string name = (el?.Name ?? "").ToLowerInvariant();
            if (fx.Contains("shadow", StringComparison.OrdinalIgnoreCase) || Skip.Any(name.Contains)) continue;
            pieces.Add((mesh, Classify(fx, name), el));
        }
        static int TrisOf((int FaceOff, int FaceCount, int VOff, int VCount) l, int type) => l.FaceCount <= 0 ? 0 : type == 1 ? Math.Max(0, l.FaceCount - 2) : l.FaceCount / 3;
        (int, int, int, int) Lod(MeshInfo m, int k)
        {
            // the level asked for; an empty one -> the next finer level that has triangles
            for (int j = Math.Min(k, m.Data1.Count - 1); j >= 0; j--) if (m.Data1[j].FaceCount > 0) return m.Data1[j];
            return m.Data1[0];
        }
        int lod = 3;
        for (int k = 0; k < 4; k++) if (pieces.Sum(p => TrisOf(Lod(p.Mesh, k), p.Mesh.FaceType)) <= MaxTris) { lod = k; break; }

        var P = new List<Vector3>(); var UV = new List<Vector2>(); var Tri = new List<int>(); var Mat = new List<byte>(); var pieceOf = new List<int>();
        int pi = 0;
        foreach (var (mesh, mat, el) in pieces)
        {
            pi++;
            var (fo, fc, _, _) = Lod(mesh, lod);
            var fs = M.FStreams[Math.Clamp(mesh.FaceStream, 0, M.FStreams.Count - 1)];
            if (fo + fc > fs.Count) fs = M.FStreams.FirstOrDefault(q => fo + fc <= q.Count) ?? fs;
            if (fc < 3 || fo < 0 || fo + fc > fs.Count) continue;
            var idx = new int[fc];
            for (int i = 0; i < fc; i++) idx[i] = B.U16(data, fs.Start + 2 * (fo + i));
            var tris = new List<(int, int, int)>();
            if (mesh.FaceType == 1) for (int i = 0; i + 2 < fc; i++) { int a = idx[i], b = idx[i + 1], c = idx[i + 2]; if (a == b || b == c || a == c) continue; tris.Add(i % 2 == 0 ? (a, b, c) : (a, c, b)); }
            else for (int i = 0; i + 2 < fc; i += 3) { int a = idx[i], b = idx[i + 1], c = idx[i + 2]; if (a != b && b != c && a != c) tris.Add((a, b, c)); }
            if (tris.Count == 0) continue;
            int maxIdx = idx.Max();
            // the vertex block: the same layout, the offset fits, the indices fit; of those the one with the shortest edges
            var types = M.VertDefs[mesh.Definition].Where(d => d.Prefix == 0).Select(d => d.Type).ToList();
            VStreamInfo? best = null; int bestBase = 0; float bestEdge = float.MaxValue; int posAt = 0, posType = 9, uvAt = -1;
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
                Vector3 Pos(int v) { int a = vs.Start + (vb + v) * vs.Bytes + pAt; return pType == 6 ? new(BitConverter.ToSingle(data, a), BitConverter.ToSingle(data, a + 4), BitConverter.ToSingle(data, a + 8)) : new((float)BitConverter.ToHalf(data, a), (float)BitConverter.ToHalf(data, a + 2), (float)BitConverter.ToHalf(data, a + 4)); }
                var edges = tris.Take(64).Select(t => Vector3.Distance(Pos(t.Item1), Pos(t.Item2))).OrderBy(e => e).ToList();
                float med = edges[edges.Count / 2];
                if (float.IsFinite(med) && med < bestEdge) { bestEdge = med; best = vs; bestBase = vb; posAt = pAt; posType = pType; uvAt = uAt; }
            }
            if (best == null) continue;
            var map = new Dictionary<int, int>();
            var local = new List<Vector3>();
            foreach (int v in tris.SelectMany(t => new[] { t.Item1, t.Item2, t.Item3 }).Distinct())
            {
                int a = best.Start + (bestBase + v) * best.Bytes;
                var p = posType == 6 ? new Vector3(BitConverter.ToSingle(data, a + posAt), BitConverter.ToSingle(data, a + posAt + 4), BitConverter.ToSingle(data, a + posAt + 8))
                                     : new Vector3((float)BitConverter.ToHalf(data, a + posAt), (float)BitConverter.ToHalf(data, a + posAt + 2), (float)BitConverter.ToHalf(data, a + posAt + 4));
                var uv = uvAt >= 0 ? new Vector2((float)BitConverter.ToHalf(data, a + uvAt), (float)BitConverter.ToHalf(data, a + uvAt + 2)) : Vector2.Zero;
                map[v] = P.Count + local.Count; local.Add(p); UV.Add(uv);
            }
            // a wheel part stored around its own middle: put it where its element is
            if (el != null && el.M.Length >= 12)
            {
                var tr = new Vector3(el.M[9], el.M[10], el.M[11]);
                var mid = local.Aggregate(Vector3.Zero, (s, q) => s + q) / local.Count;
                if (tr.Length() > 0.3f && mid.Length() < 0.3f && Vector3.Distance(mid, tr) > 0.3f)
                    for (int i = 0; i < local.Count; i++) { var q = local[i]; local[i] = new Vector3(q.X * el.M[0] + q.Y * el.M[3] + q.Z * el.M[6], q.X * el.M[1] + q.Y * el.M[4] + q.Z * el.M[7], q.X * el.M[2] + q.Y * el.M[5] + q.Z * el.M[8]) + tr; }
            }
            P.AddRange(local);
            foreach (var (a, b, c) in tris) { Tri.Add(map[a]); Tri.Add(map[b]); Tri.Add(map[c]); Mat.Add(mat); pieceOf.Add(pi); }
        }
        if (Tri.Count == 0) throw new InvalidDataException("no triangles read from " + CarName);

        // the game's axes (x = side, y = up, z = forward) -> the preview's (x = forward, y = up, z = side); the tyres stand on y = 0
        float ground = float.MaxValue; float zMin = float.MaxValue, zMax = float.MinValue, xMax = 0, yMax = float.MinValue;
        for (int t = 0; t < Mat.Count; t++)
            for (int k = 0; k < 3; k++)
            {
                var p = P[Tri[3 * t + k]];
                if (Mat[t] == Tyre) ground = Math.Min(ground, p.Y);
                zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z); xMax = Math.Max(xMax, Math.Abs(p.X)); yMax = Math.Max(yMax, p.Y);
            }
        if (ground == float.MaxValue) ground = P.Min(q => q.Y);
        float zMid = (zMin + zMax) / 2;
        Vector3 ToPreview(Vector3 q) => new(q.Z - zMid, q.Y - ground, -q.X);
        var m = new CarMesh { Game = true, P = P.Select(ToPreview).ToArray(), UV = UV.ToArray(), Tri = Tri.ToArray(), Mat = Mat.ToArray(), HalfLen = (zMax - zMin) / 2, HalfWid = xMax, Height = yMax - ground };
        // wheels: from the wheel elements
        var wheelEls = M.AllElements.Where(e => e.Name.StartsWith("e_wheel_", StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        for (int i = 0; i < 4; i++)
            m.Wheels[i] = i < wheelEls.Count ? ToPreview(new Vector3(wheelEls[i].M[9], wheelEls[i].M[10], wheelEls[i].M[11])) : new Vector3(i < 2 ? 1.4f : -1.4f, 0.35f, i % 2 == 0 ? 0.8f : -0.8f);
        m.WheelR = Math.Max(0.2f, m.Wheels.Average(w => w.Y));
        // smooth normals inside each piece (the game splits its points at hard edges already)
        var nrm = new Vector3[m.P.Length];
        for (int t = 0; t < m.Triangles; t++)
        {
            int a = m.Tri[3 * t], b = m.Tri[3 * t + 1], c = m.Tri[3 * t + 2];
            var fn = Vector3.Cross(m.P[b] - m.P[a], m.P[c] - m.P[a]);
            nrm[a] += fn; nrm[b] += fn; nrm[c] += fn;
        }
        for (int i = 0; i < nrm.Length; i++) nrm[i] = nrm[i].LengthSquared() > 1e-14f ? Vector3.Normalize(nrm[i]) : Vector3.UnitY;
        // which way the game winds its triangles: most paint normals point away from the car's middle
        float vote = 0;
        for (int t = 0; t < m.Triangles; t++)
        {
            if (m.Mat[t] != Paint) continue;
            int a = m.Tri[3 * t];
            vote += MathF.Sign(Vector3.Dot(nrm[a], m.P[a] - new Vector3(0, m.Height * 0.45f, 0)));
        }
        if (vote < 0) for (int i = 0; i < nrm.Length; i++) nrm[i] = -nrm[i];
        m.N = nrm;
        // the body picture, 512 wide, linear colour
        var pic = Skins.Picture(CarName, BodyPic);
        int f = Math.Max(1, pic.W / TexSize);
        m.TexW = pic.W / f; m.TexH = pic.H / f;
        var gamma = new byte[3 * m.TexW * m.TexH]; m.Tex = new float[gamma.Length];
        for (int y = 0; y < m.TexH; y++)
            for (int x = 0; x < m.TexW; x++)
                for (int c = 0; c < 3; c++)
                {
                    int s = 0;
                    for (int j = 0; j < f; j++) for (int i = 0; i < f; i++) s += pic.Px[4 * ((y * f + j) * pic.W + x * f + i) + c];
                    int o = 3 * (y * m.TexW + x) + c;
                    gamma[o] = (byte)(s / (f * f));
                    float g = gamma[o] / 255f; m.Tex[o] = g * g * (0.31f * g + 0.69f);
                }
        try { Save(m, gamma); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return m;
    }
}

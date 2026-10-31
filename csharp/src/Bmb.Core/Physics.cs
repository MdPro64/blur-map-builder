// Level.levelphysics: read the collision mesh, write our own. Ports of physics/levelphysics.py and physics/write_flat.py.
namespace Bmb.Core;

public sealed class PhysMesh
{
    public uint Version; public float[] BBox = new float[6];
    public float[] Verts = Array.Empty<float>();          // x y z ...
    public int NVerts, NTable, Tribytes;
    public List<(short[] Box, uint NTri, uint Child, uint VFirst, uint VCount)> Nodes = new();
    public List<(int Node, uint G0, byte A, byte Bb, byte C)> TrisRaw = new();
    public List<(uint Surf, uint Flags)> Surfaces = new();
    public List<(int Node, (uint A, ushort B, uint C, uint D, byte N) Group)> Groups = new();
    public int StreamPos, ChunkEnd, Body, Size;
}

/// a physics piece: corners + faces (piece-local indices + the wanted outward normal)
public sealed class Piece
{
    public List<double[]> Corners = new();
    public List<(int A, int B, int C, double Nx, double Ny, double Nz)> Faces = new();
}

public static class Physics
{
    public static (int Body, int Size) FindChunk(byte[] data, string name)
    {
        int p = 0x14;
        while (p + 16 <= data.Length)
        {
            string c = B.Latin1.GetString(data, p, 8).TrimEnd('\0');
            int size = (int)B.U32(data, p + 8);
            if (c == name) return (p + 16, size - 16);
            p += size;
        }
        throw new InvalidDataException("chunk " + name + " not found");
    }

    public static PhysMesh ReadMesh(byte[] data)
    {
        var (body, size) = FindChunk(data, "Main");
        int p = body;
        if (B.U32(data, p) != 0x64616548) throw new InvalidDataException("not a physics stream");
        p += 4;
        var m = new PhysMesh { Version = B.U32(data, p), Body = body, Size = size }; p += 4;
        if (B.U32(data, p) != 0) throw new InvalidDataException("expected a mesh");
        p += 4;
        p += 8 * 4 + 12 + 4 + (m.Version > 6 ? 4 : 0);
        m.NVerts = (int)B.U32(data, p); m.NTable = (int)B.U32(data, p + 4); m.Tribytes = (int)B.U32(data, p + 8);
        int nnodes = (int)B.U32(data, p + 12); p += 16;
        for (int i = 0; i < 6; i++) m.BBox[i] = B.F32(data, p + 4 * i);
        p += 24;
        m.Verts = new float[m.NVerts * 3];
        Buffer.BlockCopy(data, p, m.Verts, 0, m.NVerts * 12);
        p += m.NVerts * 12 + m.NTable * 4;
        for (int i = 0; i < nnodes; i++)
        {
            var box = new short[6];
            for (int k = 0; k < 6; k++) box[k] = B.I16(data, p + 2 * k);
            m.Nodes.Add((box, B.U32(data, p + 12), B.U32(data, p + 16), B.U32(data, p + 20), B.U32(data, p + 24)));
            p += 28;
        }
        var stack = new Stack<int>(); stack.Push(0);
        (uint, ushort, uint, uint, byte) Group() { var g = (B.U32(data, p), B.U16(data, p + 4), B.U32(data, p + 6), B.U32(data, p + 10), data[p + 14]); p += 15; return g; }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            var nd = m.Nodes[i];
            if (nd.NTri == 0) { stack.Push(i + (int)nd.Child); stack.Push(i + 1); continue; }
            var group = Group();
            int left = group.Item5;
            m.Groups.Add((i, group));
            for (int t = 0; t < nd.NTri; t++)
            {
                if (left == 0) { group = Group(); left = group.Item5; m.Groups.Add((i, group)); }
                m.TrisRaw.Add((i, group.Item1, data[p], data[p + 1], data[p + 2]));
                m.Surfaces.Add((B.U32(data, p + 3), B.U32(data, p + 7)));
                p += 11;
                left--;
            }
        }
        m.StreamPos = p; m.ChunkEnd = body + size;
        return m;
    }

    public static int[] Triangles(PhysMesh m)
    {
        var t = new int[m.TrisRaw.Count * 3];
        for (int i = 0; i < m.TrisRaw.Count; i++)
        {
            var r = m.TrisRaw[i]; int vf = (int)m.Nodes[r.Node].VFirst;
            t[3 * i] = vf + r.A; t[3 * i + 1] = vf + r.Bb; t[3 * i + 2] = vf + r.C;
        }
        return t;
    }

    static int MeshEnd(byte[] d, PhysMesh m) => m.StreamPos + 4 + 12 * (int)B.U32(d, m.StreamPos);

    static readonly (int A, int B, int C, int D, int Nx, int Ny, int Nz)[] BoxFaces =
        { (4, 5, 7, 6, 0, 1, 0), (0, 1, 5, 4, 0, 0, -1), (2, 6, 7, 3, 0, 0, 1), (0, 4, 6, 2, -1, 0, 0), (1, 3, 7, 5, 1, 0, 0) };

    public static Piece BoxMesh(double x0, double x1, double y0, double y1, double z0, double z1)
    {
        var p = new Piece();
        for (int i = 0; i < 8; i++) p.Corners.Add(new[] { (i & 1) != 0 ? x1 : x0, (i >> 2 & 1) != 0 ? y1 : y0, (i >> 1 & 1) != 0 ? z1 : z0 });
        foreach (var f in BoxFaces) { p.Faces.Add((f.A, f.B, f.C, f.Nx, f.Ny, f.Nz)); p.Faces.Add((f.A, f.C, f.D, f.Nx, f.Ny, f.Nz)); }
        return p;
    }

    static readonly byte[] WallHeader = Convert.FromHexString("00000000000000000ad7233c00000000000000005d000000100000000000803f01000000" + "0000000000000000000000000000000000000000");
    const int Trailer = 84;

    static uint PackNormal(double nx, double ny, double nz)
    {
        double l = Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-12;
        nx /= l; ny /= l; nz /= l;
        uint x = (uint)(Py.Round(nx * 1023) & 0x7FF), y = (uint)(Py.Round(ny * 1023) & 0x7FF), z = (uint)(Py.Round(nz * 511) & 0x3FF);
        return x | y << 11 | z << 22;
    }

    public static Piece TurnedBox(double cx, double cz, double y0, double y1, double halfLen, double halfWid, double heading)
    {
        double fx = Math.Sin(heading), fz = Math.Cos(heading), sx = -fz, sz = fx;
        var p = new Piece();
        for (int i = 0; i < 8; i++)
        {
            double f = (i & 1) != 0 ? halfLen : -halfLen, w = (i >> 1 & 1) != 0 ? halfWid : -halfWid;
            p.Corners.Add(new[] { cx + fx * f + sx * w, (i >> 2 & 1) != 0 ? y1 : y0, cz + fz * f + sz * w });
        }
        foreach (var fc in BoxFaces)
        {
            double nx, ny, nz;
            if (fc.Ny == 1) (nx, ny, nz) = (0, 1, 0);
            else if (fc.Nz == -1) (nx, ny, nz) = (-sx, 0, -sz);
            else if (fc.Nz == 1) (nx, ny, nz) = (sx, 0, sz);
            else if (fc.Nx == -1) (nx, ny, nz) = (-fx, 0, -fz);
            else (nx, ny, nz) = (fx, 0, fz);
            p.Faces.Add((fc.A, fc.B, fc.C, nx, ny, nz)); p.Faces.Add((fc.A, fc.C, fc.D, nx, ny, nz));
        }
        return p;
    }

    public static Piece WallStrip(IList<(double X, double Z)> pts, Func<int, double> y0, Func<int, double> y1, IList<(double X, double Z)> normals)
    {
        var p = new Piece();
        for (int k = 0; k < pts.Count; k++) { p.Corners.Add(new[] { pts[k].X, y0(k), pts[k].Z }); p.Corners.Add(new[] { pts[k].X, y1(k), pts[k].Z }); }
        for (int i = 0; i < pts.Count - 1; i++)
        {
            double nx = (normals[i].X + normals[i + 1].X) / 2, nz = (normals[i].Z + normals[i + 1].Z) / 2;
            int b0 = 2 * i, t0 = 2 * i + 1, b1 = 2 * i + 2, t1 = 2 * i + 3;
            p.Faces.Add((b0, b1, t1, nx, 0, nz)); p.Faces.Add((b0, t1, t0, nx, 0, nz));
        }
        return p;
    }

    public static Piece DeckStrip(IList<double[]> left, IList<double[]> right)
    {
        var p = new Piece();
        int n = Math.Min(left.Count, right.Count);
        for (int i = 0; i < n; i++) { p.Corners.Add((double[])left[i].Clone()); p.Corners.Add((double[])right[i].Clone()); }
        for (int i = 0; i < left.Count - 1; i++)
        {
            int l0 = 2 * i, r0 = 2 * i + 1, l1 = 2 * i + 2, r1 = 2 * i + 3;
            p.Faces.Add((l0, r0, r1, 0, 1, 0)); p.Faces.Add((l0, r1, l1, 0, 1, 0));
        }
        return p;
    }

    public static List<Piece> PackPieces(IEnumerable<Piece> pieces, int limit = 256)
    {
        var o = new List<Piece>(); var cur = new Piece();
        foreach (var p in pieces)
        {
            if (p.Corners.Count > limit) throw new InvalidOperationException("piece too big");
            if (cur.Corners.Count + p.Corners.Count > limit) { o.Add(cur); cur = new Piece(); }
            int bas = cur.Corners.Count;
            cur.Corners.AddRange(p.Corners);
            foreach (var f in p.Faces) cur.Faces.Add((f.A + bas, f.B + bas, f.C + bas, f.Nx, f.Ny, f.Nz));
        }
        if (cur.Corners.Count > 0) o.Add(cur);
        return o;
    }

    static float[] ToF32(List<double[]> c)
    {
        var v = new float[c.Count * 3];
        for (int i = 0; i < c.Count; i++) { v[3 * i] = (float)c[i][0]; v[3 * i + 1] = (float)c[i][1]; v[3 * i + 2] = (float)c[i][2]; }
        return v;
    }

    static void Cross(float[] v, int a, int b, int c, out float x, out float y, out float z)
    {
        float ux = v[3 * b] - v[3 * a], uy = v[3 * b + 1] - v[3 * a + 1], uz = v[3 * b + 2] - v[3 * a + 2];
        float wx = v[3 * c] - v[3 * a], wy = v[3 * c + 1] - v[3 * a + 1], wz = v[3 * c + 2] - v[3 * a + 2];
        x = uy * wz - uz * wy; y = uz * wx - ux * wz; z = ux * wy - uy * wx;
    }

    static List<(int, int, int)> Wind(float[] v, List<(int A, int B, int C, double Nx, double Ny, double Nz)> wanted, int sign)
    {
        var t = new List<(int, int, int)>(wanted.Count);
        foreach (var f in wanted)
        {
            Cross(v, f.A, f.B, f.C, out float x, out float y, out float z);
            double dot = (double)x * f.Nx + (double)y * f.Ny + (double)z * f.Nz;
            t.Add(dot * sign > 0 ? (f.A, f.B, f.C) : (f.A, f.C, f.B));
        }
        return t;
    }

    static void Tri(ByteBuf w, float[] v, (int A, int B, int C) t, uint flags)
    {
        Cross(v, t.A, t.B, t.C, out float x, out float y, out float z);
        w.Add((byte)t.A).Add((byte)t.B).Add((byte)t.C).AddU32(PackNormal(x, y, z)).AddU32(flags);
    }

    static void Group(ByteBuf w, uint a, ushort b, uint c, uint d, int n) => w.AddU32(a).AddU16(b).AddU32(c).AddU32(d).Add((byte)n);

    static void Node(ByteBuf w, short[] box, int ntri, int nverts)
    {
        foreach (var s in box) w.AddU16((ushort)s);
        w.AddU32((uint)ntri).AddU32(0).AddU32(0).AddU32((uint)nverts);
    }

    public static byte[] BuildFlat(byte[] data, double y, double x0, double x1, double z0, double z1,
        IEnumerable<double[]>? boxes = null, IEnumerable<Piece>? pieces = null, IEnumerable<Piece>? decks = null, IEnumerable<(Piece P, int Mat)>? patches = null)
    {
        var mesh = ReadMesh(data);
        int body = mesh.Body;
        int start = body + 4 + 4 + 4 + 8 * 4 + 12 + 4 + (mesh.Version > 6 ? 4 : 0);
        int end = MeshEnd(data, mesh);

        var V = mesh.Verts; var T = Triangles(mesh);
        // the most upward-facing triangle: its surface + flags (float32 maths, as numpy on the float32 array)
        int floor = 0; float best = -1; bool up = false;
        for (int i = 0; i < T.Length / 3; i++)
        {
            Cross(V, T[3 * i], T[3 * i + 1], T[3 * i + 2], out float nx, out float ny, out float nz);
            float l = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            float n = (float)(ny / (l + 1e-12f));
            if (Math.Abs(n) > best) { best = Math.Abs(n); floor = i; up = n > 0; }
        }
        var (_, flags) = mesh.Surfaces[floor];
        var g = mesh.Groups[0].Group;
        int sign = up ? 1 : -1;

        var verts = ToF32(new List<double[]> { new[] { x0, y, z0 }, new[] { x1, y, z0 }, new[] { x1, y, z1 }, new[] { x0, y, z1 } });
        var tris = Wind(verts, new() { (0, 1, 2, 0, 1, 0), (0, 2, 3, 0, 1, 0) }, sign);
        int tableAt = start + 16 + 24 + mesh.NVerts * 12;
        var table = data.AsSpan(tableAt, 4 * mesh.NTable).ToArray();
        var root = mesh.Nodes[0];

        var o = new ByteBuf(1 << 16);
        void Counts(ByteBuf w, int nv, int ngroups, int ntris) => w.AddU32((uint)nv).AddU32((uint)mesh.NTable).AddU32((uint)(16 * ngroups + 12 * ntris)).AddU32(1);
        void Floats(ByteBuf w, float[] v) { foreach (var f in v) w.AddF32(f); }
        Counts(o, 4, 1, tris.Count);
        Floats(o, new[] { (float)x0, (float)(y - 2.0), (float)z0, (float)x1, (float)(y + 20.0), (float)z1 });
        Floats(o, verts);
        o.Add(table);
        Node(o, root.Box, tris.Count, 4);
        Group(o, g.A, g.B, g.C, g.D, tris.Count);
        foreach (var t in tris) Tri(o, verts, t, flags);
        o.AddU32(0);

        var tail = data.AsSpan(end, Trailer).ToArray();
        (float[] Lo, float[] Hi) Bounds(float[] v)
        {
            var lo = new[] { float.MaxValue, float.MaxValue, float.MaxValue }; var hi = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < v.Length; i++) { int a = i % 3; lo[a] = Math.Min(lo[a], v[i]); hi[a] = Math.Max(hi[a], v[i]); }
            return (lo, hi);
        }

        var wall = new ByteBuf(1 << 16);
        var allPieces = (boxes ?? Array.Empty<double[]>()).Select(b => BoxMesh(b[0], b[1], b[2], b[3], b[4], b[5])).Concat(pieces ?? Array.Empty<Piece>());
        foreach (var pk in PackPieces(allPieces))
        {
            var wv = ToF32(pk.Corners);
            var wt = Wind(wv, pk.Faces, sign);
            var (lo, hi) = Bounds(wv);
            int ng = (wt.Count + 254) / 255;
            wall.Add(WallHeader);
            Counts(wall, pk.Corners.Count, ng, wt.Count);
            Floats(wall, new[] { lo[0] - 1, lo[1] - 2.0f, lo[2] - 1, hi[0] + 1, hi[1] + 2.0f, hi[2] + 1 });
            Floats(wall, wv);
            wall.Add(table);
            Node(wall, root.Box, wt.Count, pk.Corners.Count);
            for (int i = 0; i < wt.Count; i += 255)
            {
                int n = Math.Min(255, wt.Count - i);
                Group(wall, 93, 2, 2, 0, n);
                for (int j = i; j < i + n; j++) Tri(wall, wv, wt[j], 0);
            }
            wall.AddU32(0).Add(tail);
        }

        var roadHeader = data.AsSpan(body + 8, start - body - 8).ToArray();
        var deck = new ByteBuf(1 << 16);
        var jobs = new List<(List<Piece> L, uint Mat)> { ((decks ?? Array.Empty<Piece>()).ToList(), g.C) };
        var byMat = new SortedDictionary<int, List<Piece>>();
        foreach (var (pc, mat) in patches ?? Array.Empty<(Piece, int)>())
        {
            if (!byMat.TryGetValue(mat, out var l)) byMat[mat] = l = new();
            l.Add(pc);
        }
        foreach (var (mat, l) in byMat) jobs.Add((l, (uint)mat));
        foreach (var (jobPieces, mat) in jobs)
            foreach (var pk in PackPieces(jobPieces))
            {
                var dv = ToF32(pk.Corners);
                var dt = Wind(dv, pk.Faces, sign);
                var (lo, hi) = Bounds(dv);
                int ng = (dt.Count + 254) / 255;
                deck.Add(roadHeader);
                Counts(deck, pk.Corners.Count, ng, dt.Count);
                Floats(deck, new[] { lo[0] - 1, lo[1] - 2.0f, lo[2] - 1, hi[0] + 1, hi[1] + 20.0f, hi[2] + 1 });
                Floats(deck, dv);
                deck.Add(table);
                Node(deck, root.Box, dt.Count, pk.Corners.Count);
                for (int i = 0; i < dt.Count; i += 255)
                {
                    int n = Math.Min(255, dt.Count - i);
                    Group(deck, g.A, g.B, mat, g.D, n);
                    for (int j = i; j < i + n; j++) Tri(deck, dv, dt[j], flags);
                }
                deck.AddU32(0).Add(tail);
            }

        var nw = new ByteBuf(data.Length + o.Len + wall.Len + deck.Len);
        nw.Add(data.AsSpan(0, start)).Add(o.Span).Add(tail).Add(wall.Span).Add(deck.Span).Add(data.AsSpan(end + Trailer));
        int delta = nw.Len - data.Length;
        int mainHdr = body - 16;
        nw.Put(mainHdr + 8, B.U32(data, mainHdr + 8) + (uint)delta);
        nw.Put(0x0C, B.U32(data, 0x0C) + (uint)delta);
        return nw.ToArray();
    }
}

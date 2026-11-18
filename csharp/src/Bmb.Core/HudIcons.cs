// Game tweaks: the held power-up icons (\ui\data\ipf\perk_icons.ipf). They are vector shapes: one group per power-up
// (perk_shunt ...) of a frame (the rounded box, the same for all) + the symbol; perk_<kind>_x2 = one shape (two held).
// A picture becomes one filled shape of blocks (its silhouette: the game tints the icon). Units: u 0..1 across, v 0..0.737 down.
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bmb.Core;

public static class HudIcons
{
    public const string File = "\\ui\\data\\ipf\\perk_icons.ipf";
    const int Grid = 72;
    const double U0 = 0.25, V0 = 0.118, Side = 0.5;

    sealed class Node
    {
        public uint Kind, Flags; public string Name = ""; public List<Node> Kids = new();
        public byte[] Head = Array.Empty<byte>();             // shape: its 34-byte header (colour = 3 floats at 4)
        public List<(float[] V, ushort[] I)> Polys = new();   // shape: per polygon its points (6 floats each) and triangles
    }

    static Node Read(byte[] d, ref int o)
    {
        var n = new Node { Kind = B.U32(d, o), Flags = B.U32(d, o + 4) };
        int len = (int)B.U32(d, o + 8); n.Name = B.Latin1.GetString(d, o + 12, len); o += 12 + len;
        if (n.Kind == 3)
        {
            int c = (int)B.U32(d, o); o += 4;
            for (int i = 0; i < c; i++) n.Kids.Add(Read(d, ref o));
            return n;
        }
        n.Head = d.AsSpan(o, 34).ToArray(); o += 34 + 24 + 16;
        int pc = (int)B.U32(d, o); o += 4;
        var verts = new List<float[]>();
        for (int p = 0; p < pc; p++)
        {
            int vc = (int)B.U32(d, o); o += 4;
            var v = new float[vc * 6];
            for (int j = 0; j < v.Length; j++) v[j] = B.F32(d, o + 4 * j);
            o += 24 * vc; verts.Add(v);
        }
        int pc2 = (int)B.U32(d, o); o += 4;
        for (int p = 0; p < pc2; p++)
        {
            int ic = (int)B.U32(d, o); o += 4;
            var ix = new ushort[ic];
            for (int j = 0; j < ic; j++) ix[j] = B.U16(d, o + 2 * j);
            o += 2 * ic;
            n.Polys.Add((verts[p], ix));
        }
        o += 4;
        return n;
    }

    static float[] Box(Node n)
    {
        float u0 = float.MaxValue, v0 = float.MaxValue, u1 = float.MinValue, v1 = float.MinValue;
        foreach (var (v, _) in n.Polys) for (int j = 0; j < v.Length; j += 6) { u0 = Math.Min(u0, v[j]); u1 = Math.Max(u1, v[j]); v0 = Math.Min(v0, v[j + 1]); v1 = Math.Max(v1, v[j + 1]); }
        return n.Polys.Count == 0 ? new float[4] : new[] { u0, v0, u1, v1 };
    }

    static void Write(ByteBuf b, Node n, byte[] orig, Dictionary<Node, (int At, int Len)> raw)
    {
        b.AddU32(n.Kind).AddU32(n.Flags).AddU32((uint)n.Name.Length).Add(B.Latin1.GetBytes(n.Name));
        if (n.Kind == 3) { b.AddU32((uint)n.Kids.Count); foreach (var k in n.Kids) Write(b, k, orig, raw); return; }
        if (raw.TryGetValue(n, out var r)) { b.Add(orig.AsSpan(r.At, r.Len)); return; }      // an unchanged shape: its own bytes
        b.Add(n.Head);
        var bx = Box(n);
        foreach (float f in new[] { bx[0], bx[1], 0f, bx[2], bx[3], 0f, 0f, 0f, 0f, 0f }) b.AddF32(f);
        b.AddU32((uint)n.Polys.Count);
        foreach (var (v, _) in n.Polys) { b.AddU32((uint)(v.Length / 6)); foreach (float f in v) b.AddF32(f); }
        b.AddU32((uint)n.Polys.Count);
        foreach (var (_, ix) in n.Polys) { b.AddU32((uint)ix.Length); foreach (var i in ix) b.AddU16(i); }
        b.AddU32(0);
    }

    /// the tree, and for every shape where its bytes (after the name) are in the file
    static (Node Root, Dictionary<Node, (int, int)> Raw) Parse(byte[] d)
    {
        var raw = new Dictionary<Node, (int, int)>();
        int o = 8;
        Node Walk()
        {
            int at = o;
            uint kind = B.U32(d, o); int len = (int)B.U32(d, o + 8);
            if (kind == 3)
            {
                var g = new Node { Kind = kind, Flags = B.U32(d, o + 4), Name = B.Latin1.GetString(d, o + 12, len) };
                o += 12 + len;
                int c = (int)B.U32(d, o); o += 4;
                for (int i = 0; i < c; i++) g.Kids.Add(Walk());
                return g;
            }
            var n = Read(d, ref o);
            int body = at + 12 + len;
            raw[n] = (body, o - body);
            return n;
        }
        return (Walk(), raw);
    }

    static bool IsFrame(Node s)
    {
        if (s.Kind == 3 || s.Polys.Count == 0) return false;
        var b = Box(s);
        return b[2] - b[0] > 0.98;
    }

    /// the picture's silhouette (see PowerupIcons.Silhouette) as blocks in icon units (rows of runs; a run the same as the
    /// one right above it makes that rectangle taller)
    public static List<(double U0, double V0, double U1, double V1)> Blocks(string png)
    {
        var fill = PowerupIcons.Silhouette(png, Grid);
        double cell = Side / Grid;
        var rects = new List<(double, double, double, double)>();
        var open = new Dictionary<(int, int), int>();          // (x0, x1) -> its rectangle's first row
        for (int y = 0; y <= Grid; y++)
        {
            var next = new Dictionary<(int, int), int>();
            if (y < Grid)
                for (int x = 0; x < Grid; x++)
                    if (fill[x, y])
                    {
                        int s = x; while (x + 1 < Grid && fill[x + 1, y]) x++;
                        var r = (s, x + 1);
                        next[r] = open.TryGetValue(r, out int y0) ? y0 : y;
                    }
            foreach (var (r, y0) in open)
                if (!next.TryGetValue(r, out int y1) || y1 != y0) rects.Add((U0 + r.Item1 * cell, V0 + y0 * cell, U0 + r.Item2 * cell, V0 + y * cell));
            open = next;
        }
        return rects;
    }

    static (float[] V, ushort[] I) Quad((double U0, double V0, double U1, double V1) r)
    {
        // turning the same way as the game's own triangles (cross product > 0 in u, v): the other way round they are not
        // drawn at all (2026-11-17: an empty frame). Triangles 3-0-1 and 1-2-3
        var pts = new[] { (r.U0, r.V0), (r.U1, r.V0), (r.U1, r.V1), (r.U0, r.V1) };
        var v = new float[24];
        for (int j = 0; j < 4; j++) { v[6 * j] = (float)pts[j].Item1; v[6 * j + 1] = (float)pts[j].Item2; v[6 * j + 4] = 1f; }
        return (v, new ushort[] { 3, 0, 1, 1, 2, 3 });
    }

    /// a new shape with the header of the frame as it is: the game's icons are one colour (it tints them per power-up).
    /// (The 3 floats at header + 4 are NOT a colour: set to a picture's colour, black made the icon vanish - 2026-11-17)
    static Node Shape(Node like, IEnumerable<(float[] V, ushort[] I)> polys) =>
        new() { Kind = 1, Flags = like.Flags, Name = "<compound path>", Head = (byte[])like.Head.Clone(), Polys = polys.ToList() };

    /// perk_icons.ipf with the player's pictures in place of the symbols (the frame stays); null = nothing changed
    public static byte[]? Make(byte[] ipf, Func<string, string?> picture, Action<string> log)
    {
        var (root, raw) = Parse(ipf);
        bool any = false;
        var groups = new Dictionary<string, Node>();
        foreach (var k in root.Kids) if (k.Kind == 3) groups[k.Name] = k;
        foreach (var (key, _) in PowerupIcons.Kinds)
        {
            string? png = picture(key);
            if (png == null || !groups.TryGetValue("perk_" + key, out var grp)) continue;
            var frame = grp.Kids.FirstOrDefault(IsFrame);
            if (frame == null) { log($"   power-up icon {key}: the game's icon has no frame - left as it is"); continue; }
            var blocks = Blocks(png);
            if (blocks.Count == 0) { log($"   power-up icon {key}: the picture is all see-through - left as it is"); continue; }
            grp.Kids = new List<Node> { frame, Shape(frame, blocks.Select(Quad)) };
            // two held: one shape - the frame and the picture
            var x2 = root.Kids.FirstOrDefault(k => k.Kind != 3 && k.Name == "perk_" + key + "_x2");
            if (x2 != null) { x2.Polys = frame.Polys.Concat(blocks.Select(Quad)).ToList(); raw.Remove(x2); }
            log($"   power-up icon {key}: your picture ({blocks.Count} blocks)");
            any = true;
        }
        if (!any) return null;
        var o = new ByteBuf(ipf.Length + 65536);
        o.Add(ipf.AsSpan(0, 8));
        Write(o, root, ipf, raw);
        return o.ToArray();
    }

    /// (test) read and written again with nothing changed: the same bytes?
    public static bool RoundTrip(byte[] ipf)
    {
        var (root, raw) = Parse(ipf);
        var o = new ByteBuf(ipf.Length); o.Add(ipf.AsSpan(0, 8)); Write(o, root, ipf, raw);
        return o.ToArray().AsSpan().SequenceEqual(ipf);
    }

    /// the build: the changed icon file for REBORN's root pack (nothing when no picture is set)
    public static IEnumerable<KeyValuePair<string, byte[]>> RootFiles(Func<string, byte[]?> read, Action<string> log)
    {
        if (!PowerupIcons.Any()) yield break;
        var src = read(File);
        if (src == null) { log("power-up icons: the game's icon file is missing - skipped"); yield break; }
        var made = Make(src, PowerupIcons.AnyOf, log);
        if (made != null) yield return new(File, made);
    }

    /// (test) one power-up's icon drawn as a picture: its shapes filled in their colours (white = no colour)
    public static void Draw(byte[] ipf, string group, string png)
    {
        var (root, _) = Parse(ipf);
        var n = root.Kids.FirstOrDefault(k => k.Name == group) ?? throw new KeyNotFoundException(group);
        const int W = 400;
        using var bm = new Bitmap(W, (int)(W * 0.737));
        using var g = Graphics.FromImage(bm);
        g.Clear(Color.FromArgb(30, 40, 60)); g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var s in n.Kind == 3 ? n.Kids : new List<Node> { n })
        {
            float r = BitConverter.ToSingle(s.Head, 4), gg = BitConverter.ToSingle(s.Head, 8), b = BitConverter.ToSingle(s.Head, 12);
            using var br = new SolidBrush(Color.FromArgb((int)Math.Clamp(r * 255, 0, 255), (int)Math.Clamp(gg * 255, 0, 255), (int)Math.Clamp(b * 255, 0, 255)));
            foreach (var (v, ix) in s.Polys)
                for (int t = 0; t + 2 < ix.Length; t += 3)
                    g.FillPolygon(br, new[] { ix[t], ix[t + 1], ix[t + 2] }.Select(i => new PointF(v[6 * i] * W, v[6 * i + 1] * W)).ToArray());
        }
        bm.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    }
}

// Game tweaks: the power-up icons the car holds (shown behind the car in the race). They are pictures in ui_common.bpr:
// "perk_sheet" (DXT1: the SELECTED look, a card with a white symbol) and "perk_sheet_inverted" (DXT5: the NOT-SELECTED
// look, white tinted by the game, alpha = how much shows: a softly glowing card with the symbol cut out dark), 128 x 1024,
// one 96-pixel cell per icon (the card at x 18..110, y 14..84 of its cell); "perk_sheet_support" (128 x 512) holds more
// Bolt cells. Found 2026-11-17: the race's held icons did not change with perk_icons.ipf.
namespace Bmb.Core;

public static class HeldIcons
{
    public const string File = "\\ui\\data\\ui_common.bpr";
    const int Cell = 96, CardX0 = 18, CardX1 = 110, CardY0 = 14, CardY1 = 84, Corner = 9;
    const int MidY0 = 19, MidY1 = 70, Side = 44, CenterY = 48, GlowR = 6;
    const int CardW = CardX1 - CardX0, CardH = CardY1 - CardY0;

    /// which cells of which sheet show each power-up. The Bolt's cells are its shots left: the main sheet has 1 (cell 5),
    /// 3 (cell 6) and 2 (cell 9); the support sheet 4, 3, 2, 1 (4 uses the picture of 3)
    static readonly (string Sheet, string Kind, int[] Cells)[] Map =
    {
        ("perk_sheet", "mine", new[] { 0 }), ("perk_sheet", "barge", new[] { 1 }), ("perk_sheet", "shunt", new[] { 2 }),
        ("perk_sheet", "shock", new[] { 3 }), ("perk_sheet", "nitro", new[] { 4 }), ("perk_sheet", "bolt1", new[] { 5 }),
        ("perk_sheet", "bolt", new[] { 6 }), ("perk_sheet", "shield", new[] { 7 }), ("perk_sheet", "repair", new[] { 8 }),
        ("perk_sheet", "bolt2", new[] { 9 }),
        ("perk_sheet_support", "bolt", new[] { 0, 1 }), ("perk_sheet_support", "bolt2", new[] { 2 }), ("perk_sheet_support", "bolt1", new[] { 3 }),
    };

    static (int At, int Size, int Fmt, int Mips, int W, int H)? Rec(Bpr b, int dataAt, string name)
    {
        foreach (var r in b.Recs) if (b.NameOf(r) == name) return (dataAt + r[2], r[5], r[7], r[8], r[9], r[10]);
        return null;
    }

    static Rgba Decode(byte[] d, (int At, int Size, int Fmt, int Mips, int W, int H) p)
    {
        if (p.Fmt == 5) return new Rgba(p.W, p.H, Previews.Dxt5Decode(d.AsSpan(p.At, p.W * p.H), p.W, p.H));
        var rgb = Dxt.Dxt1Decode(d.AsSpan(p.At, p.W * p.H / 2), p.W, p.H);
        var o = new Rgba(p.W, p.H);
        for (int i = 0; i < p.W * p.H; i++) { o.Px[4 * i] = rgb[3 * i]; o.Px[4 * i + 1] = rgb[3 * i + 1]; o.Px[4 * i + 2] = rgb[3 * i + 2]; o.Px[4 * i + 3] = 255; }
        return o;
    }

    /// the picture's silhouette as cover (0..1) in a Side x Side square, drawn at 4x and smoothed down, and its soft glow
    /// (0..1, GlowR pixels round it) on a (Side + 2 GlowR) square
    static (double[,] Cov, double[,] Glow) Cover(string png)
    {
        const int Ss = 4;
        var fill = PowerupIcons.Silhouette(png, Side * Ss);
        var c = new double[Side, Side];
        for (int y = 0; y < Side * Ss; y++)
            for (int x = 0; x < Side * Ss; x++)
                if (fill[x, y]) c[x / Ss, y / Ss] += 1.0 / (Ss * Ss);
        int G = Side + 2 * GlowR;
        var g = new double[G, G];
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                if (c[x, y] <= 0) continue;
                for (int dy = -GlowR; dy <= GlowR; dy++)
                    for (int dx = -GlowR; dx <= GlowR; dx++)
                    {
                        double k = 1 - Math.Sqrt(dx * dx + dy * dy) / (GlowR + 1);
                        if (k <= 0) continue;
                        ref double v = ref g[x + GlowR + dx, y + GlowR + dy];
                        v = Math.Max(v, k * c[x, y]);
                    }
            }
        return (c, g);
    }

    /// x, y (in a cell) inside the card, its rounded corners left out
    static bool InCard(int x, int y)
    {
        if (x < CardX0 || x > CardX1 || y < CardY0 || y > CardY1) return false;
        int dx = Math.Max(0, Math.Max(CardX0 + Corner - x, x - (CardX1 - Corner))), dy = Math.Max(0, Math.Max(CardY0 + Corner - y, y - (CardY1 - Corner)));
        return dx * dx + dy * dy <= Corner * Corner;
    }

    /// a cell's middle rebuilt from its two sides (no trace of the old symbol or its glow), in channels c0..c1
    static void Smooth(Rgba img, int cell, int c0, int c1)
    {
        int W = img.W, y0 = cell * Cell, l = CardX0 + 7, r = CardX1 - 7;
        for (int y = y0 + MidY0; y <= y0 + MidY1; y++)
            for (int x = l + 1; x < r; x++)
            {
                double t = (double)(x - l) / (r - l);
                for (int c = c0; c <= c1; c++) img.Px[(y * W + x) * 4 + c] = (byte)Math.Round(img.Px[(y * W + l) * 4 + c] * (1 - t) + img.Px[(y * W + r) * 4 + c] * t);
            }
    }

    /// SELECTED look (the card sheet): the card, the new symbol white with a soft light round it
    static void DrawCard(Rgba img, int cell, (double[,] Cov, double[,] Glow) p)
    {
        Smooth(img, cell, 0, 2);
        int W = img.W, px0 = (CardX0 + CardX1) / 2 - Side / 2, py0 = cell * Cell + CenterY - Side / 2;
        for (int y = -GlowR; y < Side + GlowR; y++)
            for (int x = -GlowR; x < Side + GlowR; x++)
            {
                double gl = p.Glow[x + GlowR, y + GlowR], a = x >= 0 && y >= 0 && x < Side && y < Side ? p.Cov[x, y] : 0;
                if (gl <= 0 && a <= 0) continue;
                int d = ((py0 + y) * W + px0 + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    double v = Math.Min(255, img.Px[d + c] + 70 * gl);
                    img.Px[d + c] = (byte)Math.Round(v * (1 - a) + 255 * a);
                }
            }
    }

    /// NOT-SELECTED look (the inverted sheet): the card glows softly, the symbol is cut out dark (black, alpha 100 like the
    /// game's) with a brighter glow round it
    static void DrawInverse(Rgba img, int cell, (double[,] Cov, double[,] Glow) p)
    {
        int W = img.W, y0 = cell * Cell;
        for (int y = y0; y < y0 + Cell && y < img.H; y++)
            for (int x = 0; x < W; x++) { int d = (y * W + x) * 4; img.Px[d] = img.Px[d + 1] = img.Px[d + 2] = 255; }
        Smooth(img, cell, 3, 3);
        int px0 = (CardX0 + CardX1) / 2 - Side / 2, py0 = y0 + CenterY - Side / 2;
        for (int y = -GlowR; y < Side + GlowR; y++)
            for (int x = -GlowR; x < Side + GlowR; x++)
            {
                double gl = p.Glow[x + GlowR, y + GlowR], a = x >= 0 && y >= 0 && x < Side && y < Side ? p.Cov[x, y] : 0;
                if (gl <= 0 && a <= 0) continue;
                int d = ((py0 + y) * W + px0 + x) * 4;
                double al = Math.Max(img.Px[d + 3], Math.Min(195, img.Px[d + 3] + 110 * gl));
                img.Px[d + 3] = (byte)Math.Round(al * (1 - a) + 100 * a);
                img.Px[d] = img.Px[d + 1] = img.Px[d + 2] = (byte)Math.Round(255 * (1 - a));
            }
    }

    /// WHOLE icon: the picture's light fills the card (selected = as it is; not selected = the same, dimmer)
    static void DrawWhole(Rgba img, int cell, double[,] light, bool inverse)
    {
        int W = img.W, y0 = cell * Cell;
        if (inverse)
            for (int y = y0; y < y0 + Cell && y < img.H; y++)
                for (int x = 0; x < W; x++) img.Px[(y * W + x) * 4 + 3] = 0;
        for (int y = CardY0; y <= CardY1; y++)
            for (int x = CardX0; x <= CardX1; x++)
            {
                if (!InCard(x, y)) continue;
                int d = ((y0 + y) * W + x) * 4;
                byte v = (byte)Math.Round(255 * light[Math.Min(x - CardX0, CardW - 1), Math.Min(y - CardY0, CardH - 1)]);
                img.Px[d] = img.Px[d + 1] = img.Px[d + 2] = v;
                if (inverse) img.Px[d + 3] = 130;
            }
    }

    /// ui_common.bpr with the player's pictures in the held icons; null = nothing changed.
    /// symbol(kind) = a symbol picture or null; whole(kind) = a whole-icon picture or null
    public static byte[]? Make(byte[] bpr, Func<string, string?> symbol, Func<string, string?> whole, Action<string> log)
    {
        var b = Bpr.Parse(bpr);
        int dataAt = 0x14 + B.U16(bpr, 6);
        var o = (byte[])bpr.Clone();
        bool any = false;
        var covers = new Dictionary<string, (double[,], double[,])>();
        var lights = new Dictionary<string, double[,]>();
        foreach (var sheet in Map.Select(m => m.Sheet).Distinct())
        {
            var mine = Map.Where(m => m.Sheet == sheet && (symbol(m.Kind) != null || whole(m.Kind) != null)).ToList();
            if (mine.Count == 0) continue;
            foreach (var (name, inv) in new[] { (sheet, false), (sheet + "_inverted", true) })
            {
                var p = Rec(b, dataAt, name);
                if (p == null) { log($"   held power-up icons: {name} not found - left as it is"); continue; }
                var img = Decode(bpr, p.Value);
                foreach (var m in mine)
                {
                    if (whole(m.Kind) is { } wp)
                    {
                        if (!lights.TryGetValue(m.Kind, out var li)) lights[m.Kind] = li = PowerupIcons.Light(wp, CardW, CardH);
                        foreach (int c in m.Cells) DrawWhole(img, c, li, inv);
                        continue;
                    }
                    if (!covers.TryGetValue(m.Kind, out var cov)) covers[m.Kind] = cov = Cover(symbol(m.Kind)!);
                    foreach (int c in m.Cells) { if (inv) DrawInverse(img, c, cov); else DrawCard(img, c, cov); }
                }
                var enc = Skins.Encode(img, p.Value.Fmt == 5 ? "DXT5" : "DXT1", p.Value.Mips, p.Value.Size);
                if (enc.Length != p.Value.Size) { log($"   held power-up icons: {name} came out a different size - left as it is"); continue; }
                enc.CopyTo(o, p.Value.At);
                any = true;
            }
        }
        if (any) log($"   held power-up icons: {string.Join(", ", covers.Keys.Concat(lights.Keys).Distinct())}");
        return any ? o : null;
    }

    /// the build: ui_common.bpr for the root pack (nothing when no picture is set)
    public static IEnumerable<KeyValuePair<string, byte[]>> RootFiles(Func<string, byte[]?> read, Action<string> log)
    {
        if (!PowerupIcons.Any()) yield break;
        var src = read(File);
        if (src == null) { log("held power-up icons: the game's ui_common.bpr is missing - skipped"); yield break; }
        var made = Make(src, PowerupIcons.FileOf, PowerupIcons.WholeOf, log);
        if (made != null) yield return new(File, made);
    }

    /// (test) the sheets of a ui_common.bpr as PNGs
    public static void Dump(byte[] bpr, string outDir)
    {
        var b = Bpr.Parse(bpr); int dataAt = 0x14 + B.U16(bpr, 6);
        foreach (var name in new[] { "perk_sheet", "perk_sheet_inverted", "perk_sheet_support", "perk_sheet_support_inverted" })
            if (Rec(b, dataAt, name) is { } p) Decode(bpr, p).Save(Path.Combine(outDir, name + ".png"));
    }
}

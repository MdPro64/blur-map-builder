// Game tweaks: the player's own power-up icon pictures (Blur REBORN only). Kept as square PNGs in
// <data>\powerups\<kind>.png; the build turns them into the game's icons.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Bmb.Core;

public static class PowerupIcons
{
    public static readonly (string Key, string Name)[] Kinds =
    {
        ("shunt", "Shunt"), ("barge", "Barge"), ("shock", "Shock"), ("mine", "Mine"),
        ("bolt", "Bolt"), ("nitro", "Nitro"), ("shield", "Shield"), ("repair", "Repair"),
    };
    public const int Size = 256;

    static string Folder => Path.Combine(GameTweaks.DataDir, "powerups");
    static string OwnFolder => Path.Combine(Paths.Data, "powerups");

    /// the picture of this power-up, or null (the game's icon)
    /// ON HOLD (2026-11-17, 2.6.1: the icons did not show right in game): no picture is used anywhere (builds, normal Blur)
    /// and the Game tweaks tab hides the section. Set false to bring them back.
    public const bool OnHold = true;
    public static string? FileOf(string key) { if (OnHold) return null; string p = Path.Combine(Folder, key + ".png"); return File.Exists(p) ? p : null; }

    public static bool Any() => Kinds.Any(k => FileOf(k.Key) != null);

    /// keeps a picture as the power-up's icon: fitted into a 256 x 256 square (its shape kept, see-through around it)
    public static void Set(string key, Bitmap src)
    {
        Directory.CreateDirectory(OwnFolder);
        using var bm = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            double k = Math.Min((double)Size / src.Width, (double)Size / src.Height);
            int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
            g.DrawImage(src, (Size - w) / 2, (Size - h) / 2, w, h);
        }
        bm.Save(Path.Combine(OwnFolder, key + ".png"), ImageFormat.Png);
    }

    public static void Clear(string key) { string p = Path.Combine(OwnFolder, key + ".png"); if (File.Exists(p)) File.Delete(p); }

    /// the parts of the picture that become the icon (the game draws its icons in one colour): the see-through parts are
    /// left out; when the picture has light parts and clearly darker ones (a yellow face with black eyes), only the light
    /// parts are filled and the dark ones become holes (the game's own icon style); a picture of one shade (all black,
    /// all white ...) is filled wherever it is not see-through
    public static bool[,] Silhouette(string png, int n)
    {
        using var src = new Bitmap(png);
        using var small = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, 0, 0, n, n);
        }
        var luma = new double[n, n]; var solid = new bool[n, n];
        var ls = new List<double>();
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = small.GetPixel(x, y);
                solid[x, y] = c.A >= 128;
                luma[x, y] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                if (solid[x, y]) ls.Add(luma[x, y]);
            }
        var fill = new bool[n, n];
        if (ls.Count == 0) return fill;
        // two groups of brightness (Otsu): split where the two groups are most apart
        ls.Sort();
        double best = -1, cut = 0, total = ls.Sum(), left = 0;
        for (int i = 1; i < ls.Count; i++)
        {
            left += ls[i - 1];
            double w0 = i, w1 = ls.Count - i, m0 = left / w0, m1 = (total - left) / w1, v = w0 * w1 * (m0 - m1) * (m0 - m1);
            if (v > best) { best = v; cut = (ls[i - 1] + ls[i]) / 2; }
        }
        var dark = ls.Where(l => l <= cut).ToList(); var light = ls.Where(l => l > cut).ToList();
        bool twoShades = dark.Count > 0 && light.Count >= ls.Count * 0.15 && light.Average() - dark.Average() > 64;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                fill[x, y] = solid[x, y] && (!twoShades || luma[x, y] > cut);
        return fill;
    }
}

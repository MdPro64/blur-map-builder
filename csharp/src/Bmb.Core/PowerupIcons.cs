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
        ("nitro", "Nitro"), ("shield", "Shield"), ("repair", "Repair"),
        ("bolt", "Bolt (3)"), ("bolt2", "Bolt (2 left)"), ("bolt1", "Bolt (1 left)"),     // the Bolt's three together
    };
    /// held only: the Bolt with 2 / 1 shots left (the road and the menu show the Bolt of 3)
    public static bool HeldOnly(string key) => key is "bolt2" or "bolt1";
    /// a whole-icon picture: the icon's shape (4:3, the game's cards), kept as WholeW x WholeH
    public const int WholeW = 256, WholeH = 192;
    public const int Size = 256;

    static string Folder => Path.Combine(GameTweaks.DataDir, "powerups");
    internal static string OwnFolder => Path.Combine(Paths.Data, "powerups");

    /// the picture of this power-up, or null (the game's icon)
    /// ON HOLD (2026-11-17, 2.6.1: the icons did not show right in game): no picture is used anywhere (builds, normal Blur)
    /// and the Game tweaks tab hides the section. Set false to bring them back.
    public const bool OnHold = false;
    public static string? FileOf(string key) { if (OnHold) return null; string p = Path.Combine(Folder, key + ".png"); return File.Exists(p) ? p : null; }
    /// the whole-icon picture of this power-up (it replaces the card too; the game still tints it), or null
    public static string? WholeOf(string key) { if (OnHold) return null; string p = Path.Combine(Folder, key + ".whole.png"); return File.Exists(p) ? p : null; }
    /// the picture of either kind (the menu's vector icons use its shape)
    public static string? AnyOf(string key) => FileOf(key) ?? WholeOf(key);
    /// the picture files a power-up can have (for change checks and the installed copy)
    public static IEnumerable<string> FileNames(string key) => new[] { key + ".png", key + ".whole.png" };

    public static bool Any() => Kinds.Any(k => AnyOf(k.Key) != null);

    /// keeps a picture as the WHOLE icon (256 x 192, the icon's 4:3 shape): a picture of another shape is made smaller until
    /// all of it fits, in the middle (a square or tall one fills the height; never stretched, nothing cut off)
    public static void SetWhole(string key, Bitmap src)
    {
        Directory.CreateDirectory(OwnFolder);
        double want = (double)WholeW / WholeH, k = Math.Min((double)WholeW / src.Width, (double)WholeH / src.Height);
        int dw = Math.Max(1, (int)Math.Round(src.Width * k)), dh = Math.Max(1, (int)Math.Round(src.Height * k));
        var dst = new Rectangle((WholeW - dw) / 2, (WholeH - dh) / 2, dw, dh);
        using var bm = new Bitmap(WholeW, WholeH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bm))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, dst);
        }
        bm.Save(Path.Combine(OwnFolder, key + ".whole.png"), ImageFormat.Png);
        string sym = Path.Combine(OwnFolder, key + ".png"); if (File.Exists(sym)) File.Delete(sym);
        double r = (double)src.Width / src.Height;
        SetWarning(key, Math.Abs(r / want - 1) > 0.04 ? $"⚠ not 4:3 ({src.Width} x {src.Height}): made smaller to fit" : null);
    }

    /// a warning about a power-up's picture (its shape did not fit), shown next to its row; kept in powerups\warnings.json
    static string WarnFile => Path.Combine(OwnFolder, "warnings.json");
    public static string? Warning(string key) { try { return System.IO.File.Exists(WarnFile) && Json.Load(WarnFile) is JObj o ? o.Str(key) : null; } catch (Exception) { return null; } }
    internal static void SetWarning(string key, string? text)
    {
        JObj o; try { o = System.IO.File.Exists(WarnFile) && Json.Load(WarnFile) is JObj j ? j : new JObj(); } catch (Exception) { o = new JObj(); }
        if (text == null) o.Remove(key); else o[key] = text;
        Directory.CreateDirectory(OwnFolder); System.IO.File.WriteAllText(WarnFile, Json.Dumps(o, 1));
    }

    /// what the game will show of a picture: its shape in pure white (see Silhouette), see-through elsewhere
    public static Bitmap WhitePreview(string png, int w, int h)
    {
        var fill = Silhouette(png, w, h);
        var bm = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) bm.SetPixel(x, y, fill[x, y] ? Color.White : Color.Transparent);
        return bm;
    }

    /// a whole-icon picture as light 0..1 at w x h: its shape (see Silhouette) PURE WHITE, the rest dark - like the game's
    /// own icons, which are white and coloured by the game. Drawn at 3x and smoothed down for soft edges
    public static double[,] Light(string png, int w, int h)
    {
        const int Ss = 3;
        var fill = Silhouette(png, w * Ss, h * Ss);
        var o = new double[w, h];
        for (int y = 0; y < h * Ss; y++)
            for (int x = 0; x < w * Ss; x++)
                if (fill[x, y]) o[x / Ss, y / Ss] += 1.0 / (Ss * Ss);
        return o;
    }

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
        string whole = Path.Combine(OwnFolder, key + ".whole.png"); if (File.Exists(whole)) File.Delete(whole);
        double r = (double)src.Width / src.Height;
        SetWarning(key, Math.Abs(r - 1) > 0.04 ? $"⚠ not square ({src.Width} x {src.Height}): it is made smaller to fit" : null);
    }

    public static void Clear(string key) { foreach (var n in FileNames(key)) { string p = Path.Combine(OwnFolder, n); if (File.Exists(p)) File.Delete(p); } SetWarning(key, null); }

    /// the parts of the picture that become the icon (the game draws its icons in one colour): the see-through parts are
    /// left out; when the picture has light parts and clearly darker ones (a yellow face with black eyes), only the light
    /// parts are filled and the dark ones become holes (the game's own icon style); a picture of one shade (all black,
    /// all white ...) is filled wherever it is not see-through
    public static bool[,] Silhouette(string png, int n) => Silhouette(png, n, n);
    public static bool[,] Silhouette(string png, int w, int h)
    {
        using var src = new Bitmap(png);
        using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, 0, 0, w, h);
        }
        var luma = new double[w, h]; var solid = new bool[w, h];
        var ls = new List<double>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = small.GetPixel(x, y);
                solid[x, y] = c.A >= 128;
                luma[x, y] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                if (solid[x, y]) ls.Add(luma[x, y]);
            }
        var fill = new bool[w, h];
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
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                fill[x, y] = solid[x, y] && (!twoShades || luma[x, y] > cut);
        return fill;
    }
}

// Game tweaks: the power-up icons floating on the road (\models\perks\pickups\<kind>pickup.model). Each is a flat quad
// with the icon as a picture in the model: icon_<kind>_01 and icon_<kind>_01_inverse (256 x 256, DXT1).
namespace Bmb.Core;

public static class RoadIcons
{
    public static string ModelOf(string key) => $"\\models\\perks\\pickups\\{key}pickup.model";
    public static string Pic(string key) => $"icon_{key}_01";
    public static string PicInverse(string key) => $"icon_{key}_01_inverse";

    static int Blocks(int w, int h) => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4);

    /// where a picture is in a pickup model: after "NAME" "RA" 1 its header W H 1 mips "DXT1" 0 3, then the data (all sizes)
    public static (int W, int H, int Mips, string Fmt, int At, int Size)? Find(byte[] m, string pic)
    {
        var key = B.Latin1.GetBytes(pic + "RA\u0001");
        int i = m.AsSpan().IndexOf(key);
        if (i < 0) return null;
        for (int f = i + key.Length; f < Math.Min(m.Length - 12, i + key.Length + 200); f++)
        {
            string fmt = B.Latin1.GetString(m, f, 4);
            if (fmt != "DXT1" && fmt != "DXT5") continue;
            int w = (int)B.U32(m, f - 16), h = (int)B.U32(m, f - 12), mips = (int)B.U32(m, f - 4);
            if (w < 4 || w > 4096 || h < 4 || h > 4096 || mips < 1 || mips > 13 || B.U32(m, f + 8) != 3) return null;
            int block = fmt == "DXT5" ? 16 : 8, size = 0;
            for (int k = 0; k < mips; k++) size += Blocks(w >> k, h >> k) * block;
            int at = f + 12;
            return at + size <= m.Length ? (w, h, mips, fmt, at, size) : null;
        }
        return null;
    }

    /// (test) a picture in a model at its biggest size
    public static Rgba Decode(byte[] model, string pic)
    {
        var f = Find(model, pic) ?? throw new KeyNotFoundException(pic);
        var l = (f.W, f.H, f.Fmt, f.At);
        int bw = Math.Max(4, l.W), bh = Math.Max(4, l.H);
        byte[] px;
        if (l.Fmt == "DXT5") px = Previews.Dxt5Decode(model.AsSpan(l.At, Blocks(l.W, l.H) * 16), bw, bh);
        else
        {
            var rgb = Dxt.Dxt1Decode(model.AsSpan(l.At, Blocks(l.W, l.H) * 8), bw, bh);
            px = new byte[bw * bh * 4];
            for (int i = 0; i < bw * bh; i++) { px[4 * i] = rgb[3 * i]; px[4 * i + 1] = rgb[3 * i + 1]; px[4 * i + 2] = rgb[3 * i + 2]; px[4 * i + 3] = 255; }
        }
        var o = new Rgba(l.W, l.H);
        for (int y = 0; y < l.H; y++) Array.Copy(px, y * bw * 4, o.Px, y * l.W * 4, l.W * 4);
        return o;
    }

    const int PicSide = 120, PicX = 68, PicY = 98;           // where the symbol sits on the 256 x 256 card

    /// the pickup model with the player's picture: on the card (the old symbol filled with the card around it) and as the
    /// "inverse" (the symbol alone, light on black). Same byte length. Null = this model can't be changed
    public static byte[]? Make(byte[] model, string key, string png, Action<string> log)
    {
        var fi = Find(model, Pic(key)); var fv = Find(model, PicInverse(key));
        if (fi == null || fv == null || fi.Value.W != 256 || fv.Value.W != 256) { log($"   power-up icon {key}: the road model has no icon picture - left as it is"); return null; }
        var card = Decode(model, Pic(key)); var inv = Decode(model, PicInverse(key));
        int W = card.W, H = card.H;
        // the old symbol: where the inverse is lit (a little wider), filled along each row from the card on its two sides
        var mask = new bool[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool lit = false;
                for (int dy = -3; dy <= 3 && !lit; dy++)
                    for (int dx = -3; dx <= 3 && !lit; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                        int s = (yy * W + xx) * 4;
                        lit = inv.Px[s] + inv.Px[s + 1] + inv.Px[s + 2] > 90;
                    }
                mask[y * W + x] = lit;
            }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!mask[y * W + x]) continue;
                int l = x; while (l > 0 && mask[y * W + l]) l--;
                int r = x; while (r < W - 1 && mask[y * W + r]) r++;
                double t = r == l ? 0 : (double)(x - l) / (r - l);
                for (int c = 0; c < 3; c++) card.Px[(y * W + x) * 4 + c] = (byte)Math.Round(card.Px[(y * W + l) * 4 + c] * (1 - t) + card.Px[(y * W + r) * 4 + c] * t);
            }
        // the game's style (it tints the icon per power-up): the symbol dark on the light card, and light alone in the
        // inverse. The picture's silhouette is drawn at 4x and smoothed down, so its edges are soft like the game's
        const int Ss = 4;
        var fill = PowerupIcons.Silhouette(png, PicSide * Ss);
        var cover = new double[PicSide, PicSide];
        for (int y = 0; y < PicSide * Ss; y++)
            for (int x = 0; x < PicSide * Ss; x++)
                if (fill[x, y]) cover[x / Ss, y / Ss] += 1.0 / (Ss * Ss);
        var newInv = new Rgba(W, H);
        for (int i = 0; i < W * H; i++) newInv.Px[4 * i + 3] = 255;
        for (int y = 0; y < PicSide; y++)
            for (int x = 0; x < PicSide; x++)
            {
                double a = cover[x, y];
                if (a <= 0) continue;
                int d = ((PicY + y) * W + PicX + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    card.Px[d + c] = (byte)Math.Round(card.Px[d + c] * (1 - a) + 12 * a);
                    newInv.Px[d + c] = (byte)Math.Round(255 * a);
                }
            }
        var o = (byte[])model.Clone();
        foreach (var (f, img) in new[] { (fi.Value, card), (fv.Value, newInv) })
        {
            var enc = Skins.Encode(img, f.Fmt, f.Mips, f.Size);
            if (enc.Length != f.Size) { log($"   power-up icon {key}: unexpected picture size - left as it is"); return null; }
            enc.CopyTo(o, f.At);
        }
        return o;
    }

    /// the build: the road pickups with the player's pictures, for REBORN's root pack (the bolt has no road model of its own)
    public static IEnumerable<KeyValuePair<string, byte[]>> RootFiles(Func<string, byte[]?> read, Action<string> log)
    {
        foreach (var (key, _) in PowerupIcons.Kinds)
        {
            string? png = PowerupIcons.FileOf(key);
            if (png == null) continue;
            var md = read(ModelOf(key));
            if (md == null) { if (key != "bolt") log($"   power-up icon {key}: no road model - only the held icon changes"); continue; }
            var made = Make(md, key, png, log);
            if (made != null) { log($"   power-up icon {key}: your picture on the road"); yield return new(ModelOf(key), made); }
        }
    }

    /// (test) the levels of the icon pictures in a pickup model
    public static IEnumerable<string> Dump(byte[] model, string key)
    {
        foreach (var p in new[] { Pic(key), PicInverse(key) })
            yield return Find(model, p) is { } l ? $"{p}: {l.W}x{l.H} {l.Fmt} mips {l.Mips} size {l.Size} at {l.At:X}" : $"{p}: not found";
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

/// <summary>
/// 选图窗 / 物品头图共用：按选项对整图图集做解码后降采样，降低超大 PNG 的内存占用。
/// </summary>
internal static class IconAtlasDecodeHelper
{
    private static volatile int s_maxDecodedMegapixels;

    public static void SetMaxDecodedMegapixels(int megapixels) =>
        s_maxDecodedMegapixels = Math.Clamp(megapixels, 0, 512);

    /// <summary>
    /// 若超过上限则按比例缩小并释放原图；否则返回原实例。<paramref name="scaleOut"/> 为相对原解码尺寸的缩放（用于推算格宽）。
    /// </summary>
    public static Bitmap MaybeDownscaleDecodedAtlas(Bitmap decoded, out double scaleOut)
    {
        scaleOut = 1.0;
        var capMp = s_maxDecodedMegapixels;
        if (capMp <= 0)
        {
            return decoded;
        }

        long w = decoded.Width;
        long h = decoded.Height;
        if (w <= 0 || h <= 0)
        {
            return decoded;
        }

        var pix = w * h;
        var cap = (long)capMp * 1_000_000L;
        if (pix <= cap)
        {
            return decoded;
        }

        scaleOut = Math.Sqrt(cap / (double)pix);
        var nw = Math.Max(1, (int)Math.Round(w * scaleOut));
        var nh = Math.Max(1, (int)Math.Round(h * scaleOut));
        var scaled = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
        try
        {
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(decoded, 0, 0, nw, nh);
            }
        }
        catch
        {
            scaled.Dispose();
            throw;
        }

        decoded.Dispose();
        return scaled;
    }
}

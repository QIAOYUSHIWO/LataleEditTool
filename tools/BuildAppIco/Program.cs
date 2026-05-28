// One-off / occasional: merge AppIconSources\Icon_*.jpg into a multi-size app.ico (PNG payloads, Explorer-friendly).
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: BuildAppIco <sourceDir> <out.ico>");
    return 1;
}

var srcDir = Path.GetFullPath(args[0]);
var outIco = Path.GetFullPath(args[1]);
if (!Directory.Exists(srcDir))
{
    Console.Error.WriteLine($"Source dir not found: {srcDir}");
    return 1;
}

var sizes = new[] { 32, 64, 128, 256 };
var entries = new List<(int W, int H, byte[] Png)>();
foreach (var s in sizes)
{
    var jpg = Path.Combine(srcDir, $"Icon_{s}.jpg");
    if (!File.Exists(jpg))
    {
        Console.Error.WriteLine($"Missing: {jpg}");
        return 1;
    }

    using var loaded = new Bitmap(jpg);
    using var square = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(square))
    {
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.DrawImage(loaded, new Rectangle(0, 0, s, s));
    }

    using var ms = new MemoryStream();
    square.Save(ms, ImageFormat.Png);
    entries.Add((s, s, ms.ToArray()));
}

var dir = Path.GetDirectoryName(outIco);
if (!string.IsNullOrEmpty(dir))
    Directory.CreateDirectory(dir);
using (var fs = File.Create(outIco))
    WriteIcoWithPngImages(fs, entries);

Console.WriteLine($"Wrote {outIco} ({entries.Count} sizes).");
return 0;

static void WriteIcoWithPngImages(Stream stream, IReadOnlyList<(int W, int H, byte[] Png)> images)
{
    var n = images.Count;
    using var bw = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true);
    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)n);

    var offset = 6u + (uint)(16 * n);
    foreach (var (w, h, png) in images)
    {
        bw.Write(WidthHeightByte(w));
        bw.Write(WidthHeightByte(h));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(png.Length);
        bw.Write(offset);
        offset += (uint)png.Length;
    }

    foreach (var (_, _, png) in images)
        bw.Write(png);
}

static byte WidthHeightByte(int dim) => dim >= 256 ? (byte)0 : (byte)dim;

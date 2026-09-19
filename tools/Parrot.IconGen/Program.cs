using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Parrot.Core.Branding;

namespace Parrot.IconGen;

/// <summary>
/// Rasterizes <see cref="ParrotLogo"/> into the multi-size .ico the app ships with.
/// Run from the repo root: dotnet run --project tools/Parrot.IconGen
/// </summary>
internal static class Program
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var outputDirectory = args.Length > 0
            ? args[0]
            : Path.Combine(FindRepoRoot(), "assets");

        Directory.CreateDirectory(outputDirectory);

        var drawing = BuildDrawing();

        var frames = IconSizes.ToDictionary(size => size, size => EncodePng(Render(drawing, size)));

        var icoPath = Path.Combine(outputDirectory, "parrot.ico");
        File.WriteAllBytes(icoPath, BuildIco(frames));
        Console.WriteLine($"wrote {icoPath}");

        var previewPath = Path.Combine(outputDirectory, "parrot-256.png");
        File.WriteAllBytes(previewPath, frames[256]);
        Console.WriteLine($"wrote {previewPath}");

        // A contact sheet makes it obvious when the small sizes turn to mush.
        var sheetPath = Path.Combine(outputDirectory, "parrot-sizes.png");
        File.WriteAllBytes(sheetPath, EncodePng(RenderContactSheet(drawing)));
        Console.WriteLine($"wrote {sheetPath}");

        return 0;
    }

    private static DrawingGroup BuildDrawing()
    {
        var group = new DrawingGroup();

        foreach (var shape in ParrotLogo.Shapes)
        {
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(shape.Fill)!),
                pen: null,
                Geometry.Parse(shape.PathData)));
        }

        group.Freeze();
        return group;
    }

    private static RenderTargetBitmap Render(DrawingGroup drawing, int size)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            var scale = size / ParrotLogo.CanvasSize;
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawDrawing(drawing);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static RenderTargetBitmap RenderContactSheet(DrawingGroup drawing)
    {
        const int width = 512;
        const int height = 320;

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height / 2.0));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null,
                new Rect(0, height / 2.0, width, height / 2.0));

            foreach (var onDark in new[] { false, true })
            {
                var x = 16.0;
                var baseline = onDark ? height / 2.0 + 16 : 16.0;

                foreach (var size in IconSizes)
                {
                    context.PushTransform(new TranslateTransform(x, baseline));
                    context.PushTransform(new ScaleTransform(size / ParrotLogo.CanvasSize, size / ParrotLogo.CanvasSize));
                    context.DrawDrawing(drawing);
                    context.Pop();
                    context.Pop();

                    x += size + 16;
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Packs PNG frames into an .ico. Windows has accepted PNG-compressed icon frames
    /// since Vista, which keeps the 256px frame small.
    /// </summary>
    private static byte[] BuildIco(Dictionary<int, byte[]> frames)
    {
        var ordered = frames.OrderBy(f => f.Key).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);                 // reserved
        writer.Write((ushort)1);                 // type: icon
        writer.Write((ushort)ordered.Count);

        var offset = 6 + ordered.Count * 16;

        foreach (var (size, png) in ordered)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);               // palette size
            writer.Write((byte)0);               // reserved
            writer.Write((ushort)1);             // colour planes
            writer.Write((ushort)32);            // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);

            offset += png.Length;
        }

        foreach (var (_, png) in ordered)
            writer.Write(png);

        writer.Flush();
        return stream.ToArray();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Parrot.slnx")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new InvalidOperationException("Parrot.slnx not found above " + AppContext.BaseDirectory);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Parrot.App.Branding;
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

        var tile = LogoDrawings.Colored(withTile: true);

        var icoPath = Path.Combine(outputDirectory, "parrot.ico");
        File.WriteAllBytes(icoPath, LogoDrawings.BuildIco(tile, IconSizes));
        Console.WriteLine($"wrote {icoPath}");

        var previewPath = Path.Combine(outputDirectory, "parrot-256.png");
        File.WriteAllBytes(previewPath, LogoDrawings.EncodePng(LogoDrawings.Render(tile, 256)));
        Console.WriteLine($"wrote {previewPath}");

        // A contact sheet makes it obvious when the small sizes turn to mush.
        var sheetPath = Path.Combine(outputDirectory, "parrot-sizes.png");
        File.WriteAllBytes(sheetPath, LogoDrawings.EncodePng(RenderContactSheet()));
        Console.WriteLine($"wrote {sheetPath}");

        return 0;
    }

    /// <summary>
    /// Three rows: the app icon on white, and the bare tray bird on a light and a dark taskbar.
    /// </summary>
    private static RenderTargetBitmap RenderContactSheet()
    {
        const int width = 720;
        const int rowHeight = 288;

        var rows = new (Drawing Drawing, Brush Background)[]
        {
            (LogoDrawings.Colored(withTile: true), Brushes.White),
            (LogoDrawings.Colored(withTile: false), new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE))),
            (LogoDrawings.Colored(withTile: false), new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20))),
        };

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            for (var row = 0; row < rows.Length; row++)
            {
                var top = row * rowHeight;
                context.DrawRectangle(rows[row].Background, null, new Rect(0, top, width, rowHeight));

                var x = 16.0;
                foreach (var size in IconSizes)
                {
                    var scale = size / ParrotLogo.CanvasSize;
                    context.PushTransform(new TranslateTransform(x, top + 16));
                    context.PushTransform(new ScaleTransform(scale, scale));
                    context.DrawDrawing(rows[row].Drawing);
                    context.Pop();
                    context.Pop();

                    x += size + 16;
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, rowHeight * rows.Length, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
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

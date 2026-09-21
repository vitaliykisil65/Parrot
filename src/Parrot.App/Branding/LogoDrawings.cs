using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Parrot.Core.Branding;

namespace Parrot.App.Branding;

/// <summary>
/// Turns the <see cref="ParrotLogo"/> geometry into WPF drawings and bitmaps. Shared with
/// tools/Parrot.IconGen as a linked source file, so the app and the .ico render identically.
/// </summary>
public static class LogoDrawings
{
    private static readonly Rect Canvas = new(0, 0, ParrotLogo.CanvasSize, ParrotLogo.CanvasSize);

    /// <summary>The full-colour logo, optionally on its rounded tile.</summary>
    public static DrawingGroup Colored(bool withTile)
    {
        var group = new DrawingGroup
        {
            ClipGeometry = withTile
                ? new RectangleGeometry(Canvas, ParrotLogo.TileCornerRadius, ParrotLogo.TileCornerRadius)
                : new RectangleGeometry(Canvas),
        };

        // Pins the drawing's bounds to the whole canvas, so every variant scales the same way.
        group.Children.Add(new GeometryDrawing(
            withTile ? Brush(ParrotLogo.TileFill) : Brushes.Transparent, null, new RectangleGeometry(Canvas)));

        foreach (var shape in ParrotLogo.Shapes)
            group.Children.Add(new GeometryDrawing(Brush(shape.Fill), null, Geometry.Parse(shape.PathData)));

        group.Freeze();
        return group;
    }

    /// <summary>Single-colour line art, for the tray on a dark taskbar.</summary>
    public static DrawingGroup Outline(Color color)
    {
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, ParrotLogo.OutlineStrokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(Canvas) };
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(Canvas)));

        foreach (var stroke in ParrotLogo.OutlineStrokes)
            group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(stroke)));

        foreach (var dot in ParrotLogo.OutlineDots)
            group.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse(dot)));

        group.Freeze();
        return group;
    }

    public static RenderTargetBitmap Render(Drawing drawing, int size)
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

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>A multi-size .ico of <paramref name="drawing"/>.</summary>
    public static byte[] BuildIco(Drawing drawing, IEnumerable<int> sizes) =>
        IcoWriter.Build(sizes.Distinct().ToDictionary(size => size, size => EncodePng(Render(drawing, size))));

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);
}

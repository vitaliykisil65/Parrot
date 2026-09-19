using System.Windows;
using System.Windows.Media;
using Parrot.Core.Branding;
using Drawing = System.Drawing;

namespace Parrot.App.Branding;

/// <summary>Builds WPF and Win32 images from the shared <see cref="ParrotLogo"/> geometry.</summary>
public static class LogoFactory
{
    private static readonly Lazy<DrawingImage> LazyImage = new(BuildImage);
    private static readonly Lazy<Drawing.Icon> LazyIcon = new(LoadIcon);

    /// <summary>Vector logo for use anywhere in the UI.</summary>
    public static DrawingImage Image => LazyImage.Value;

    /// <summary>Multi-size icon for the tray and the window chrome.</summary>
    public static Drawing.Icon Icon => LazyIcon.Value;

    private static DrawingImage BuildImage()
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

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Drawing.Icon LoadIcon()
    {
        var stream = Application.GetResourceStream(new Uri("parrot.ico", UriKind.Relative))?.Stream;

        return stream is null
            ? Drawing.SystemIcons.Application
            : new Drawing.Icon(stream);
    }
}

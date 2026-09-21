using System.IO;
using System.Windows;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Parrot.App.Branding;

/// <summary>Builds WPF and Win32 images from the shared logo geometry.</summary>
public static class LogoFactory
{
    private static readonly Lazy<DrawingImage> LazyImage = new(() => Freeze(new DrawingImage(LogoDrawings.Colored(withTile: true))));
    private static readonly Lazy<Drawing.Icon> LazyIcon = new(LoadIcon);

    /// <summary>Vector logo on its tile, for use anywhere in the UI.</summary>
    public static DrawingImage Image => LazyImage.Value;

    /// <summary>Multi-size icon for the window chrome and the taskbar.</summary>
    public static Drawing.Icon Icon => LazyIcon.Value;

    /// <summary>
    /// Tray icon matched to the taskbar: white line art on a dark taskbar (a filled logo
    /// turns into a dark blob there), the full-colour bird on a light one.
    /// </summary>
    public static Drawing.Icon TrayIcon(bool darkTaskbar)
    {
        var drawing = darkTaskbar
            ? LogoDrawings.Outline(Colors.White)
            : LogoDrawings.Colored(withTile: false);

        // Every size Windows may ask for between 100% and 250% scaling.
        var ico = LogoDrawings.BuildIco(drawing, [16, 20, 24, 32, 40, 48]);
        return new Drawing.Icon(new MemoryStream(ico), Forms.SystemInformation.SmallIconSize);
    }

    private static Drawing.Icon LoadIcon()
    {
        var stream = Application.GetResourceStream(new Uri("parrot.ico", UriKind.Relative))?.Stream;

        return stream is null
            ? Drawing.SystemIcons.Application
            : new Drawing.Icon(stream);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

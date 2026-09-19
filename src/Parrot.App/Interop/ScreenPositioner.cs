using System.Windows;
using System.Windows.Media;
using Parrot.Core.Models;
using Parrot.Core.Settings;
using Forms = System.Windows.Forms;

namespace Parrot.App.Interop;

/// <summary>
/// Places a window at one of nine anchors on the chosen monitor.
/// Everything is computed in physical pixels against the monitor's work area (so the
/// taskbar is respected) and converted back to WPF units at the end — mixing the two
/// coordinate systems is what breaks positioning on multi-DPI desktops.
/// </summary>
public static class ScreenPositioner
{
    public static void Position(Window window, AppSettings settings)
    {
        var screen = settings.Monitor == MonitorChoice.WithCursor
            ? Forms.Screen.FromPoint(Forms.Control.MousePosition)
            : Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];

        var area = screen.WorkingArea;

        var source = PresentationSource.FromVisual(window);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var width = window.ActualWidth * toDevice.M11;
        var height = window.ActualHeight * toDevice.M22;
        var marginX = settings.MarginX * toDevice.M11;
        var marginY = settings.MarginY * toDevice.M22;

        var x = HorizontalPosition(settings.Anchor, area, width, marginX);
        var y = VerticalPosition(settings.Anchor, area, height, marginY);

        // Never let a small screen or a large prompt push the window off the work area.
        x = Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        y = Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));

        var topLeft = fromDevice.Transform(new Point(x, y));
        window.Left = topLeft.X;
        window.Top = topLeft.Y;
    }

    private static double HorizontalPosition(ScreenAnchor anchor, System.Drawing.Rectangle area, double width, double margin) =>
        anchor switch
        {
            ScreenAnchor.TopLeft or ScreenAnchor.MiddleLeft or ScreenAnchor.BottomLeft => area.Left + margin,
            ScreenAnchor.TopRight or ScreenAnchor.MiddleRight or ScreenAnchor.BottomRight => area.Right - width - margin,
            _ => area.Left + (area.Width - width) / 2,
        };

    private static double VerticalPosition(ScreenAnchor anchor, System.Drawing.Rectangle area, double height, double margin) =>
        anchor switch
        {
            ScreenAnchor.TopLeft or ScreenAnchor.TopCenter or ScreenAnchor.TopRight => area.Top + margin,
            ScreenAnchor.BottomLeft or ScreenAnchor.BottomCenter or ScreenAnchor.BottomRight => area.Bottom - height - margin,
            _ => area.Top + (area.Height - height) / 2,
        };

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);
}

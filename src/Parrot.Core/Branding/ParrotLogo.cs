namespace Parrot.Core.Branding;

/// <summary>One filled path of the logo, in a 120x120 coordinate space.</summary>
public sealed record LogoShape(string PathData, string Fill);

/// <summary>
/// The blue-and-gold macaw bust, kept as plain geometry data so the same drawing serves the
/// WPF windows, the tray and the generated .ico — the logo is defined exactly once.
/// </summary>
public static class ParrotLogo
{
    public const double CanvasSize = 120;

    /// <summary>The app icon sits on a rounded "sky" tile, like a modern launcher icon.</summary>
    public const double TileCornerRadius = 28;
    public const string TileFill = "#E3EDFF";

    public const string BlueDeep = "#1747B8";
    public const string BlueSoft = "#6FA3FF";
    public const string Blue = "#2F6BF2";
    public const string BlueShade = "#1F5FE0";
    public const string Gold = "#FFC23A";
    public const string FacePale = "#F7F4EC";
    public const string Ink = "#14202E";

    public static IReadOnlyList<LogoShape> Shapes { get; } =
    [
        // Crest feathers, swept back over the skull.
        new("M71 27c-1-11 5-19 15-21-3 7-3 13 0 19z", BlueDeep),
        new("M80 30c5-9 14-13 23-11-5 5-7 11-6 17z", BlueSoft),

        // Gold chest; the tile clips it at the bottom edge.
        new(Circle(72, 118, 38), Gold),

        // Head, with a darker rim on the far side so it reads as round.
        new(Circle(72, 62, 35), Blue),
        new("M91 33a35 35 0 0 1 8 52c-6-12-9-28-8-52z", BlueShade),

        // Bare pale face patch of a macaw, and the eye.
        new(Ellipse(60, 60, 17, 18), FacePale),
        new(Circle(62, 57, 6.5), Ink),
        new(Circle(64.3, 54.8, 2.1), "#FFFFFF"),

        // Hooked upper mandible and the lower one tucked under it.
        new("M45 46C21 42 11 62 21 82c3-10 12-15 24-13z", Ink),
        new("M29 74c2 9 10 13 18 10l-2-14z", "#39485A"),
    ];

    private static string Circle(double cx, double cy, double r) => Ellipse(cx, cy, r, r);

    private static string Ellipse(double cx, double cy, double rx, double ry) =>
        $"M {F(cx - rx)},{F(cy)} A {F(rx)},{F(ry)} 0 1 0 {F(cx + rx)},{F(cy)} A {F(rx)},{F(ry)} 0 1 0 {F(cx - rx)},{F(cy)} Z";

    private static string F(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

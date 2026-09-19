namespace Parrot.Core.Branding;

/// <summary>One filled path of the logo, in a 256x256 coordinate space.</summary>
public sealed record LogoShape(string PathData, string Fill);

/// <summary>
/// The blue macaw, kept as plain geometry data so the same drawing serves the WPF windows
/// and the generated .ico — the logo is defined exactly once.
/// </summary>
public static class ParrotLogo
{
    public const double CanvasSize = 256;

    public const string BlueDark = "#1340A6";
    public const string BlueMid = "#2D7DF6";
    public const string BlueLight = "#5BA4FF";
    public const string Gold = "#F5B324";
    public const string FacePale = "#F4F1E8";
    public const string Charcoal = "#222B36";
    public const string Ink = "#101820";

    public static IReadOnlyList<LogoShape> Shapes { get; } =
    [
        // Crest feathers, swept back over the top of the skull.
        new("M 152,46 C 158,18 180,2 200,10 C 186,28 172,44 166,62 Z", BlueDark),
        new("M 126,42 C 124,12 144,-2 162,6 C 150,22 142,38 140,58 Z", BlueLight),
        new("M 102,52 C 94,24 108,6 126,10 C 116,28 110,40 114,62 Z", BlueDark),

        // Gold chest, drawn first so the head sits on top of it.
        new(Circle(176, 218, 74), Gold),
        new(Circle(176, 226, 56), "#FFD166"),

        // Head.
        new(Circle(150, 106, 76), BlueMid),
        new(Crescent(), BlueLight),

        // Bare white cheek patch of a blue-and-gold macaw.
        new(Circle(116, 110, 46), FacePale),

        new(Circle(112, 102, 16), Ink),
        new(Circle(118, 96, 5.5), "#FFFFFF"),

        // Upper mandible: the big downward hook that makes a parrot read as a parrot.
        new("""
            M 104,62
            C 70,66 34,88 22,120
            C 14,142 22,158 40,160
            C 44,172 50,180 60,184
            C 68,186 72,180 68,172
            C 62,162 60,152 62,144
            C 76,146 92,154 104,166
            C 92,134 90,94 104,62 Z
            """, Charcoal),

        // Lower mandible, tucked under the hook.
        new("""
            M 62,146
            C 78,148 94,156 106,166
            C 102,184 84,194 68,186
            C 58,180 56,162 62,146 Z
            """, "#39485A"),

        // Nostril.
        new(Circle(86, 86, 6), "#39485A"),
    ];

    private static string Circle(double cx, double cy, double r) =>
        $"M {F(cx - r)},{F(cy)} A {F(r)},{F(r)} 0 1 0 {F(cx + r)},{F(cy)} A {F(r)},{F(r)} 0 1 0 {F(cx - r)},{F(cy)} Z";

    /// <summary>A leaf shape through four control points, used for the crest.</summary>
    private static string Feather(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3) =>
        $"M {F(x0)},{F(y0)} C {F(x1)},{F(y1)} {F(x2)},{F(y2)} {F(x3)},{F(y3)} " +
        $"C {F(x3 - 14)},{F(y3 - 6)} {F(x0 - 10)},{F(y0 + 14)} {F(x0)},{F(y0)} Z";

    /// <summary>A lighter sliver along the top-right of the skull, so the head reads as round.</summary>
    private static string Crescent() =>
        "M 150,30 C 192,30 226,64 226,106 C 226,124 220,140 210,153 " +
        "C 210,110 184,66 142,54 C 144,44 146,36 150,30 Z";

    private static string F(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

using System.Windows;
using System.Windows.Media;

namespace Parrot.App.Controls;

/// <summary>
/// Attached properties the shared control templates read, so a nav item or a chip can carry
/// an icon, a counter or a coloured dot without a dedicated control class.
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.RegisterAttached(
        "IconSize", typeof(double), typeof(Ui), new FrameworkPropertyMetadata(16.0));

    public static readonly DependencyProperty BadgeProperty = DependencyProperty.RegisterAttached(
        "Badge", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DotProperty = DependencyProperty.RegisterAttached(
        "Dot", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(10)));

    /// <summary>Placeholder text for text boxes.</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
        "Hint", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject o) => (Geometry?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, Geometry? value) => o.SetValue(IconProperty, value);

    public static double GetIconSize(DependencyObject o) => (double)o.GetValue(IconSizeProperty);
    public static void SetIconSize(DependencyObject o, double value) => o.SetValue(IconSizeProperty, value);

    public static string? GetBadge(DependencyObject o) => (string?)o.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject o, string? value) => o.SetValue(BadgeProperty, value);

    public static Brush? GetDot(DependencyObject o) => (Brush?)o.GetValue(DotProperty);
    public static void SetDot(DependencyObject o, Brush? value) => o.SetValue(DotProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius value) => o.SetValue(CornerRadiusProperty, value);

    public static string? GetHint(DependencyObject o) => (string?)o.GetValue(HintProperty);
    public static void SetHint(DependencyObject o, string? value) => o.SetValue(HintProperty, value);
}

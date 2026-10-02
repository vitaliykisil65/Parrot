using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Parrot.App.Controls;

/// <summary>
/// The app's small movements: a press that gives under the finger, pages and dialogs that
/// arrive instead of appearing, an answer that nods or shakes. All short and eased out, and
/// all off when Windows is set to show no animations.
/// </summary>
public static class Motion
{
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(90);

    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(200);

    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(260);

    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    private static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    private static readonly IEasingFunction Overshoot = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });

    /// <summary>Windows' own "Show animations in Windows" switch, and a GPU to draw them.</summary>
    public static bool IsOn => SystemParameters.ClientAreaAnimation && RenderCapability.Tier > 0;

    /// <summary>Moves a number property to a value; without animations it simply jumps there.</summary>
    public static void To(UIElement element, DependencyProperty property, double to, TimeSpan duration, bool inOut = false)
    {
        if (!IsOn)
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, to);
            return;
        }

        element.BeginAnimation(property, new DoubleAnimation(to, duration) { EasingFunction = inOut ? EaseInOut : EaseOut });
    }

    /// <summary>
    /// A page, card or panel arriving: it fades in and rises a few pixels into place. The
    /// element keeps its layout the whole time, so nothing around it moves.
    /// </summary>
    public static void Enter(UIElement element, double rise = 8, TimeSpan? delay = null)
    {
        if (!IsOn)
            return;

        var shift = Translate(element);
        var begin = delay ?? TimeSpan.Zero;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Normal) { BeginTime = begin, EasingFunction = EaseOut });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Slow) { BeginTime = begin, EasingFunction = EaseOut });
    }

    /// <summary>A dialog or toast popping up: it fades in while growing from just below full size.</summary>
    public static void Pop(UIElement element)
    {
        if (!IsOn)
            return;

        var scale = Scale(element);
        var grow = new DoubleAnimation(0.96, 1, Normal) { EasingFunction = EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Quick));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    /// <summary>Fades an element in, e.g. the dimmed backdrop behind a dialog.</summary>
    public static void FadeIn(UIElement element)
    {
        if (IsOn)
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Normal) { EasingFunction = EaseOut });
    }

    /// <summary>A right answer: the element swells a little past full size and settles back.</summary>
    public static void Bump(UIElement element, double to = 1.06)
    {
        if (!IsOn)
            return;

        var scale = Scale(element);
        var swell = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
        swell.KeyFrames.Add(new EasingDoubleKeyFrame(to, TimeSpan.FromMilliseconds(110), EaseOut));
        swell.KeyFrames.Add(new EasingDoubleKeyFrame(1, TimeSpan.FromMilliseconds(320), Overshoot));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, swell);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, swell);
    }

    /// <summary>
    /// A miss: a short, soft shake from side to side. Small on purpose — it says "not this
    /// one", it doesn't scold.
    /// </summary>
    public static void Shake(UIElement element, double distance = 5)
    {
        if (!IsOn)
            return;

        var shift = Translate(element);
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
        var step = 0;
        foreach (var x in new[] { -distance, distance, -distance * 0.6, distance * 0.6, -distance * 0.25, 0 })
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(x, TimeSpan.FromMilliseconds(60 * ++step), EaseInOut));
        shift.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    // ── Press ────────────────────────────────────────────────────────────────

    /// <summary>
    /// How far a control gives when pressed: 0.96 shrinks it by 4%, 1 turns it off. Set by the
    /// shared styles, so every button, chip and sidebar entry answers the mouse.
    /// </summary>
    public static readonly DependencyProperty PressScaleProperty = DependencyProperty.RegisterAttached(
        "PressScale", typeof(double), typeof(Motion), new FrameworkPropertyMetadata(1.0, OnPressScaleChanged));

    public static double GetPressScale(DependencyObject o) => (double)o.GetValue(PressScaleProperty);

    public static void SetPressScale(DependencyObject o, double value) => o.SetValue(PressScaleProperty, value);

    private static void OnPressScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.PreviewMouseLeftButtonDown -= OnPressed;
        element.PreviewMouseLeftButtonUp -= OnReleased;
        element.MouseLeave -= OnReleased;
        element.LostMouseCapture -= OnReleased;

        if (e.NewValue is double scale && scale < 1)
        {
            element.PreviewMouseLeftButtonDown += OnPressed;
            element.PreviewMouseLeftButtonUp += OnReleased;
            element.MouseLeave += OnReleased;
            element.LostMouseCapture += OnReleased;
        }
    }

    /// <summary>Set while a press holds the element down, so only a release of that press lets it go.</summary>
    private static readonly DependencyProperty IsGivingProperty = DependencyProperty.RegisterAttached(
        "IsGiving", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(false));

    private static void OnPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement { IsEnabled: true } element || !IsOn)
            return;

        element.SetValue(IsGivingProperty, true);
        ScaleTo(element, GetPressScale(element), Quick);
    }

    /// <summary>
    /// Springs back after a press. Anything else that leaves the element (a click that answers
    /// with a <see cref="Bump"/>, the mouse moving off later) has nothing to undo.
    /// </summary>
    private static void OnReleased(object sender, EventArgs e)
    {
        if (sender is not UIElement element || !(bool)element.GetValue(IsGivingProperty))
            return;

        element.SetValue(IsGivingProperty, false);
        ScaleTo(element, 1, Normal);
    }

    private static void ScaleTo(UIElement element, double to, TimeSpan duration)
    {
        var scale = Scale(element);
        var animation = new DoubleAnimation(to, duration) { EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    // ── Transforms ───────────────────────────────────────────────────────────

    /// <summary>The element's own translate, created on first use.</summary>
    private static TranslateTransform Translate(UIElement element) => (TranslateTransform)Group(element).Children[1];

    /// <summary>The element's own scale about its centre, created on first use.</summary>
    private static ScaleTransform Scale(UIElement element)
    {
        var group = Group(element);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (ScaleTransform)group.Children[0];
    }

    /// <summary>
    /// One scale and one translate per element, so a press and an entrance can run together.
    /// Only elements without a transform of their own are moved this way.
    /// </summary>
    private static TransformGroup Group(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { Children.Count: 2, IsFrozen: false } existing
            && existing.Children[0] is ScaleTransform && existing.Children[1] is TranslateTransform)
            return existing;

        var group = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
        element.RenderTransform = group;
        return group;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

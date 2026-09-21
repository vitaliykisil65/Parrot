using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Parrot.App.Controls;

/// <summary>"− 20 хв +" — a bounded integer picker. The template lives in Themes/Shared.xaml.</summary>
[TemplatePart(Name = "PART_Down", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Up", Type = typeof(ButtonBase))]
public sealed class NumberStepper : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumberStepper),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(NumberStepper), new PropertyMetadata(0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumberStepper), new PropertyMetadata(100, OnRangeChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(int), typeof(NumberStepper), new PropertyMetadata(1));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(NumberStepper), new PropertyMetadata("", (d, _) => ((NumberStepper)d).UpdateText()));

    /// <summary>Shown instead of the number when the value is zero ("без ліміту").</summary>
    public static readonly DependencyProperty ZeroTextProperty = DependencyProperty.Register(
        nameof(ZeroText), typeof(string), typeof(NumberStepper), new PropertyMetadata(null, (d, _) => ((NumberStepper)d).UpdateText()));

    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(NumberStepper), new PropertyMetadata("0"));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;

    static NumberStepper()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberStepper), new FrameworkPropertyMetadata(typeof(NumberStepper)));
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public string? ZeroText
    {
        get => (string?)GetValue(ZeroTextProperty);
        set => SetValue(ZeroTextProperty, value);
    }

    public string DisplayText => (string)GetValue(DisplayTextProperty);

    /// <summary>Raised when the user changes the value (not when code sets it).</summary>
    public event EventHandler? ValueCommitted;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_Down") is ButtonBase down)
            down.Click += (_, _) => Nudge(-1);

        if (GetTemplateChild("PART_Up") is ButtonBase up)
            up.Click += (_, _) => Nudge(+1);

        UpdateText();
    }

    private void Nudge(int direction)
    {
        var next = Math.Clamp(Value + direction * Step, Minimum, Maximum);
        if (next == Value)
            return;

        Value = next;
        ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NumberStepper)d).UpdateText();

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.CoerceValue(ValueProperty);

    private static object CoerceValue(DependencyObject d, object value)
    {
        var stepper = (NumberStepper)d;
        return Math.Clamp((int)value, stepper.Minimum, Math.Max(stepper.Minimum, stepper.Maximum));
    }

    private void UpdateText()
    {
        var text = Value == 0 && ZeroText is not null
            ? ZeroText
            : string.IsNullOrEmpty(Suffix) ? Value.ToString() : $"{Value} {Suffix}";

        SetValue(DisplayTextKey, text);
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Parrot.App.Branding;
using Parrot.App.Services;
using Parrot.Core;
using Parrot.Core.Models;
using Parrot.Core.Settings;

namespace Parrot.App.Views;

/// <summary>A labelled enum value for the combo boxes.</summary>
public sealed record Option<T>(T Value, string Label);

public sealed partial class SettingsWindow : Window
{
    private static readonly (DayOfWeek Day, string Label)[] Days =
    [
        (DayOfWeek.Monday, "Пн"), (DayOfWeek.Tuesday, "Вт"), (DayOfWeek.Wednesday, "Ср"),
        (DayOfWeek.Thursday, "Чт"), (DayOfWeek.Friday, "Пт"),
        (DayOfWeek.Saturday, "Сб"), (DayOfWeek.Sunday, "Нд"),
    ];

    private readonly SettingsService _settings;
    private readonly List<CheckBox> _dayBoxes = [];

    public SettingsWindow(SettingsService settings)
    {
        _settings = settings;

        InitializeComponent();
        Icon = LogoFactory.Image;

        BuildOptions();
        Load(settings.Current);

        DataPathText.Text = $"Дані зберігаються в {AppPaths.DataDirectory}";
    }

    private void BuildOptions()
    {
        AnchorBox.ItemsSource = new List<Option<ScreenAnchor>>
        {
            new(ScreenAnchor.TopLeft, "Зверху зліва"),
            new(ScreenAnchor.TopCenter, "Зверху по центру"),
            new(ScreenAnchor.TopRight, "Зверху справа"),
            new(ScreenAnchor.MiddleLeft, "Посередині зліва"),
            new(ScreenAnchor.Center, "По центру екрана"),
            new(ScreenAnchor.MiddleRight, "Посередині справа"),
            new(ScreenAnchor.BottomLeft, "Знизу зліва"),
            new(ScreenAnchor.BottomCenter, "Знизу по центру"),
            new(ScreenAnchor.BottomRight, "Знизу справа"),
        };

        MonitorBox.ItemsSource = new List<Option<MonitorChoice>>
        {
            new(MonitorChoice.Primary, "Основний"),
            new(MonitorChoice.WithCursor, "Там, де курсор"),
        };

        DirectionBox.ItemsSource = new List<Option<TranslationDirection>>
        {
            new(TranslationDirection.FrontToBack, "Питання → переклад"),
            new(TranslationDirection.BackToFront, "Переклад → питання"),
            new(TranslationDirection.Random, "Випадково"),
        };

        StrictnessBox.ItemsSource = new List<Option<AnswerStrictness>>
        {
            new(AnswerStrictness.Lenient, "Пробачати одруківки"),
            new(AnswerStrictness.Strict, "Тільки точний збіг"),
        };

        ThemeBox.ItemsSource = new List<Option<AppTheme>>
        {
            new(AppTheme.System, "Як у системі"),
            new(AppTheme.Light, "Світла"),
            new(AppTheme.Dark, "Темна"),
        };

        foreach (var (day, label) in Days)
        {
            var box = new CheckBox { Content = label, Tag = day, Margin = new Thickness(0, 0, 14, 0) };
            _dayBoxes.Add(box);
            DaysPanel.Children.Add(box);
        }
    }

    private void Load(AppSettings s)
    {
        IntervalBox.Text = s.IntervalMinutes.ToString();
        JitterBox.Text = s.JitterPercent.ToString();
        DisplaySecondsBox.Text = s.DisplaySeconds.ToString();
        CooldownBox.Text = s.IgnoreCooldownMinutes.ToString();
        MaxPromptsBox.Text = s.MaxPromptsPerDay.ToString();
        MaxNewBox.Text = s.MaxNewCardsPerDay.ToString();
        AlwaysShowBox.IsChecked = s.AlwaysShowSomething;

        QuietEnabledBox.IsChecked = s.QuietHoursEnabled;
        QuietFromBox.Text = FormatTime(s.QuietFrom);
        QuietToBox.Text = FormatTime(s.QuietTo);
        IdleBox.Text = s.IdleThresholdMinutes.ToString();
        FullscreenBox.IsChecked = s.PauseOnFullscreen;

        foreach (var box in _dayBoxes)
            box.IsChecked = s.ActiveDays.Contains((DayOfWeek)box.Tag);

        Select(AnchorBox, s.Anchor);
        Select(MonitorBox, s.Monitor);
        MarginXBox.Text = s.MarginX.ToString();
        MarginYBox.Text = s.MarginY.ToString();
        FocusInputBox.IsChecked = s.FocusInputOnShow;

        Select(DirectionBox, s.Direction);
        Select(StrictnessBox, s.Strictness);

        // Read the real registry state rather than the stored flag: the user may have
        // removed the entry from Task Manager behind our back.
        AutoStartBox.IsChecked = AutoStartService.IsEnabled();
        SoundBox.IsChecked = s.SoundEnabled;
        Select(ThemeBox, s.Theme);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = _settings.Current.Clone();

        if (!TryReadInt(IntervalBox, "Інтервал", 1, 1440, out var interval) ||
            !TryReadInt(JitterBox, "Розкид", 0, 90, out var jitter) ||
            !TryReadInt(DisplaySecondsBox, "Час показу", 0, 600, out var displaySeconds) ||
            !TryReadInt(CooldownBox, "Пауза після ігнорування", 1, 1440, out var cooldown) ||
            !TryReadInt(MaxPromptsBox, "Максимум показів", 0, 1000, out var maxPrompts) ||
            !TryReadInt(MaxNewBox, "Нових карток", 0, 1000, out var maxNew) ||
            !TryReadInt(IdleBox, "Поріг бездіяльності", 0, 600, out var idle) ||
            !TryReadInt(MarginXBox, "Відступ по горизонталі", 0, 2000, out var marginX) ||
            !TryReadInt(MarginYBox, "Відступ по вертикалі", 0, 2000, out var marginY) ||
            !TryReadTime(QuietFromBox, "Початок тихих годин", out var quietFrom) ||
            !TryReadTime(QuietToBox, "Кінець тихих годин", out var quietTo))
            return;

        var activeDays = _dayBoxes.Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag).ToList();
        if (activeDays.Count == 0)
        {
            Fail("Оберіть хоча б один день тижня.");
            return;
        }

        s.IntervalMinutes = interval;
        s.JitterPercent = jitter;
        s.DisplaySeconds = displaySeconds;
        s.IgnoreCooldownMinutes = cooldown;
        s.MaxPromptsPerDay = maxPrompts;
        s.MaxNewCardsPerDay = maxNew;
        s.AlwaysShowSomething = AlwaysShowBox.IsChecked == true;

        s.QuietHoursEnabled = QuietEnabledBox.IsChecked == true;
        s.QuietFrom = quietFrom;
        s.QuietTo = quietTo;
        s.ActiveDays = activeDays;
        s.IdleThresholdMinutes = idle;
        s.PauseOnFullscreen = FullscreenBox.IsChecked == true;

        s.Anchor = Selected(AnchorBox, s.Anchor);
        s.Monitor = Selected(MonitorBox, s.Monitor);
        s.MarginX = marginX;
        s.MarginY = marginY;
        s.FocusInputOnShow = FocusInputBox.IsChecked == true;

        s.Direction = Selected(DirectionBox, s.Direction);
        s.Strictness = Selected(StrictnessBox, s.Strictness);

        s.SoundEnabled = SoundBox.IsChecked == true;
        s.Theme = Selected(ThemeBox, s.Theme);

        var wantsAutoStart = AutoStartBox.IsChecked == true;
        if (wantsAutoStart != AutoStartService.IsEnabled() && !AutoStartService.Set(wantsAutoStart))
        {
            Fail("Не вдалося змінити автозапуск — Windows відхилила запис у реєстр.");
            return;
        }

        s.RunAtStartup = AutoStartService.IsEnabled();

        _settings.Save(s);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    // ── Input helpers ────────────────────────────────────────────────────────

    private bool TryReadInt(TextBox box, string field, int min, int max, out int value)
    {
        if (int.TryParse(box.Text.Trim(), out value) && value >= min && value <= max)
            return true;

        Fail($"«{field}»: очікується число від {min} до {max}.");
        box.Focus();
        return false;
    }

    private bool TryReadTime(TextBox box, string field, out TimeSpan value)
    {
        if (TimeSpan.TryParseExact(box.Text.Trim(), [@"hh\:mm", @"h\:mm"], CultureInfo.InvariantCulture, out value))
            return true;

        Fail($"«{field}»: очікується час у форматі ГГ:ХХ.");
        box.Focus();
        return false;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string FormatTime(TimeSpan value) => $"{value.Hours:00}:{value.Minutes:00}";

    private static void Select<T>(Selector box, T value)
    {
        box.SelectedItem = box.ItemsSource.OfType<Option<T>>().FirstOrDefault(o => Equals(o.Value, value));
    }

    private static T Selected<T>(Selector box, T fallback) =>
        box.SelectedItem is Option<T> option ? option.Value : fallback;
}

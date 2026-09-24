using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Parrot.App.Localization;
using Parrot.App.Services;
using Parrot.Core;
using Parrot.Core.Models;
using Parrot.Core.Settings;

namespace Parrot.App.Views;

/// <summary>A labelled enum value for combo boxes and segmented controls.</summary>
public sealed record Option<T>(T Value, string Label);

/// <summary>
/// Settings apply as soon as they change — there is no Save button to forget. Invalid input
/// (a malformed time, no active days) is rejected on the spot and the old value restored.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    /// <summary>Monday first, the way the week is laid out in Ukraine and most of Europe.</summary>
    private static readonly DayOfWeek[] Days =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private readonly SettingsService _settings;
    private readonly MainWindow _shell;
    private readonly List<ToggleButton> _dayButtons = [];
    private readonly List<RadioButton> _anchorButtons = [];
    private readonly List<RadioButton> _directionButtons = [];
    private readonly List<RadioButton> _strictnessButtons = [];
    private readonly List<RadioButton> _themeButtons = [];
    private readonly DispatcherTimer _jitterDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private bool _loading;

    public SettingsView(SettingsService settings, MainWindow shell)
    {
        _settings = settings;
        _shell = shell;

        InitializeComponent();

        BuildChoices();
        Load(settings.Current);

        _jitterDebounce.Tick += (_, _) =>
        {
            _jitterDebounce.Stop();
            Update(s => s.JitterPercent = (int)JitterSlider.Value);
        };

        DataPathText.Text = L.F("Settings.DataPath", AppPaths.DataDirectory);
        VersionText.Text = L.F("Settings.Version", UpdateService.CurrentVersionText);
        ShowUpdateStatus(null);

        // A background check can find a release while this page is open.
        Loaded += (_, _) => shell.Updates.StateChanged += OnUpdatesChanged;
        Unloaded += (_, _) => shell.Updates.StateChanged -= OnUpdatesChanged;
    }

    private void OnUpdatesChanged(object? sender, EventArgs e)
    {
        if (CheckUpdatesButton.IsEnabled)
            Dispatcher.Invoke(() => ShowUpdateStatus(null));
    }

    // ── Building ─────────────────────────────────────────────────────────────

    private void BuildChoices()
    {
        foreach (var day in Days)
        {
            var label = L.Culture.TextInfo.ToTitleCase(L.Culture.DateTimeFormat.GetAbbreviatedDayName(day));
            var button = new ToggleButton { Content = label, Tag = day, Style = (Style)FindResource("DayChip") };
            button.Click += OnDayClicked;
            _dayButtons.Add(button);
            DaysPanel.Children.Add(button);
        }

        foreach (var anchor in Enum.GetValues<ScreenAnchor>())
        {
            var label = L.T($"Anchor.{anchor}");
            var button = new RadioButton
            {
                Style = (Style)FindResource("AnchorCell"),
                GroupName = "Anchor",
                Tag = anchor,
                ToolTip = label,
                HorizontalAlignment = anchor switch
                {
                    ScreenAnchor.TopLeft or ScreenAnchor.MiddleLeft or ScreenAnchor.BottomLeft => HorizontalAlignment.Left,
                    ScreenAnchor.TopRight or ScreenAnchor.MiddleRight or ScreenAnchor.BottomRight => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center,
                },
                VerticalAlignment = anchor switch
                {
                    ScreenAnchor.TopLeft or ScreenAnchor.TopCenter or ScreenAnchor.TopRight => VerticalAlignment.Top,
                    ScreenAnchor.BottomLeft or ScreenAnchor.BottomCenter or ScreenAnchor.BottomRight => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Center,
                },
            };
            button.Checked += (_, _) => Update(s => s.Anchor = anchor);
            _anchorButtons.Add(button);
            AnchorGrid.Children.Add(button);
        }

        LanguageBox.ItemsSource = L.Available;
        LanguageBox.IsEnabled = L.Available.Count > 1;
        LanguageNote.Text = L.T(L.Available.Count > 1 ? "Settings.LanguageNote" : "Settings.LanguageSoon");

        MonitorBox.ItemsSource = new List<Option<MonitorChoice>>
        {
            new(MonitorChoice.Primary, L.T("Monitor.Primary")),
            new(MonitorChoice.WithCursor, L.T("Monitor.WithCursor")),
        };

        Segments<TranslationDirection>(DirectionPanel, _directionButtons, "Direction",
            [new(TranslationDirection.FrontToBack, L.T("Direction.FrontToBack")),
             new(TranslationDirection.BackToFront, L.T("Direction.BackToFront")),
             new(TranslationDirection.Random, L.T("Direction.Both"))],
            value => Update(s => s.Direction = value));

        Segments<AnswerStrictness>(StrictnessPanel, _strictnessButtons, "Strictness",
            [new(AnswerStrictness.Strict, L.T("Strictness.Strict")), new(AnswerStrictness.Lenient, L.T("Strictness.Lenient"))],
            value => Update(s => s.Strictness = value));

        Segments<AppTheme>(ThemePanel, _themeButtons, "Theme",
            [new(AppTheme.System, L.T("Theme.System")), new(AppTheme.Light, L.T("Theme.Light")), new(AppTheme.Dark, L.T("Theme.Dark"))],
            value => Update(s => s.Theme = value));
    }

    private void Segments<T>(Panel panel, List<RadioButton> buttons, string group, Option<T>[] options, Action<T> onPick)
    {
        foreach (var option in options)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = group,
                Content = option.Label,
                Tag = option.Value,
            };
            button.Checked += (_, _) => onPick(option.Value);
            buttons.Add(button);
            panel.Children.Add(button);
        }
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private void Load(AppSettings s)
    {
        _loading = true;

        IntervalStepper.Value = s.IntervalMinutes;
        JitterSlider.Value = s.JitterPercent;
        JitterText.Text = $"±{s.JitterPercent}%";
        DisplayStepper.Value = s.DisplaySeconds;
        NewCardsStepper.Value = s.MaxNewCardsPerDay;
        MaxPromptsStepper.Value = s.MaxPromptsPerDay;
        CooldownStepper.Value = s.IgnoreCooldownMinutes;
        AlwaysShowSwitch.IsChecked = s.AlwaysShowSomething;

        QuietSwitch.IsChecked = s.QuietHoursEnabled;
        QuietFromBox.Text = FormatTime(s.QuietFrom);
        QuietToBox.Text = FormatTime(s.QuietTo);
        UpdateQuietBoxes(s.QuietHoursEnabled);
        foreach (var button in _dayButtons)
            button.IsChecked = s.ActiveDays.Contains((DayOfWeek)button.Tag);
        IdleStepper.Value = s.IdleThresholdMinutes;
        FullscreenSwitch.IsChecked = s.PauseOnFullscreen;

        foreach (var button in _anchorButtons)
            button.IsChecked = Equals(button.Tag, s.Anchor);
        MonitorBox.SelectedItem = MonitorBox.ItemsSource.OfType<Option<MonitorChoice>>().FirstOrDefault(o => o.Value == s.Monitor);
        MarginXStepper.Value = s.MarginX;
        MarginYStepper.Value = s.MarginY;
        FocusSwitch.IsChecked = s.FocusInputOnShow;

        Check(_directionButtons, s.Direction);
        Check(_strictnessButtons, s.Strictness);
        Check(_themeButtons, s.Theme);

        // Read the real registry state rather than the stored flag: the user may have
        // removed the entry from Task Manager behind our back.
        AutoStartSwitch.IsChecked = AutoStartService.IsEnabled();
        SoundSwitch.IsChecked = s.SoundEnabled;
        AutoUpdateSwitch.IsChecked = s.CheckForUpdates;
        LanguageBox.SelectedItem = L.Available.FirstOrDefault(l => l.Code == L.Language);

        _loading = false;
    }

    private static void Check<T>(IEnumerable<RadioButton> buttons, T value)
    {
        foreach (var button in buttons)
            button.IsChecked = Equals(button.Tag, value);
    }

    /// <summary>Stores one change. The settings object is copied, never mutated in place.</summary>
    private void Update(Action<AppSettings> change)
    {
        if (_loading)
            return;

        var next = _settings.Current.Clone();
        next.ActiveDays = [.. next.ActiveDays];
        change(next);
        _settings.Save(next);

        HideError();
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnIntervalChanged(object? sender, EventArgs e) => Update(s => s.IntervalMinutes = IntervalStepper.Value);

    private void OnDisplayChanged(object? sender, EventArgs e) => Update(s => s.DisplaySeconds = DisplayStepper.Value);

    private void OnNewCardsChanged(object? sender, EventArgs e) => Update(s => s.MaxNewCardsPerDay = NewCardsStepper.Value);

    private void OnMaxPromptsChanged(object? sender, EventArgs e) => Update(s => s.MaxPromptsPerDay = MaxPromptsStepper.Value);

    private void OnCooldownChanged(object? sender, EventArgs e) => Update(s => s.IgnoreCooldownMinutes = CooldownStepper.Value);

    private void OnIdleChanged(object? sender, EventArgs e) => Update(s => s.IdleThresholdMinutes = IdleStepper.Value);

    private void OnMarginChanged(object? sender, EventArgs e) => Update(s =>
    {
        s.MarginX = MarginXStepper.Value;
        s.MarginY = MarginYStepper.Value;
    });

    /// <summary>The label follows the thumb live; the value is stored once the thumb settles.</summary>
    private void OnJitterMoved(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        JitterText.Text = $"±{(int)e.NewValue}%";

        if (_loading)
            return;

        _jitterDebounce.Stop();
        _jitterDebounce.Start();
    }

    private void OnAlwaysShowChanged(object sender, RoutedEventArgs e) =>
        Update(s => s.AlwaysShowSomething = AlwaysShowSwitch.IsChecked == true);

    private void OnFullscreenChanged(object sender, RoutedEventArgs e) =>
        Update(s => s.PauseOnFullscreen = FullscreenSwitch.IsChecked == true);

    private void OnFocusChanged(object sender, RoutedEventArgs e) =>
        Update(s => s.FocusInputOnShow = FocusSwitch.IsChecked == true);

    private void OnSoundChanged(object sender, RoutedEventArgs e) =>
        Update(s => s.SoundEnabled = SoundSwitch.IsChecked == true);

    private void OnPreviewSound(object sender, RoutedEventArgs e) => SoundService.PlayChime();

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is UiLanguage language && language.Code != L.Language)
            Update(s => s.UiLanguage = language.Code);
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorBox.SelectedItem is Option<MonitorChoice> option)
            Update(s => s.Monitor = option.Value);
    }

    private void OnQuietChanged(object sender, RoutedEventArgs e)
    {
        var enabled = QuietSwitch.IsChecked == true;
        UpdateQuietBoxes(enabled);
        Update(s => s.QuietHoursEnabled = enabled);
    }

    private void UpdateQuietBoxes(bool enabled)
    {
        QuietFromBox.IsEnabled = enabled;
        QuietToBox.IsEnabled = enabled;
    }

    private void OnTimeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            CommitQuietTime(box);
            e.Handled = true;
        }
    }

    private void OnQuietTimeCommitted(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            CommitQuietTime(box);
    }

    private void CommitQuietTime(TextBox box)
    {
        var current = box == QuietFromBox ? _settings.Current.QuietFrom : _settings.Current.QuietTo;

        if (!TryParseTime(box.Text, out var value))
        {
            ShowError(L.T("Settings.Error.Time"));
            box.Text = FormatTime(current);
            return;
        }

        box.Text = FormatTime(value);

        if (value == current)
            return;

        Update(s =>
        {
            if (box == QuietFromBox)
                s.QuietFrom = value;
            else
                s.QuietTo = value;
        });
    }

    private void OnDayClicked(object sender, RoutedEventArgs e)
    {
        var active = _dayButtons.Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag).ToList();

        if (active.Count == 0)
        {
            ((ToggleButton)sender).IsChecked = true;
            ShowError(L.T("Settings.Error.Days"));
            return;
        }

        Update(s => s.ActiveDays = active);
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        var wanted = AutoStartSwitch.IsChecked == true;

        if (!AutoStartService.Set(wanted))
        {
            AutoStartSwitch.IsChecked = AutoStartService.IsEnabled();
            ShowError(L.T("Settings.Error.AutoStart"));
            return;
        }

        Update(s => s.RunAtStartup = AutoStartService.IsEnabled());
    }

    // ── Updates ──────────────────────────────────────────────────────────────

    private void OnAutoUpdateChanged(object sender, RoutedEventArgs e) =>
        Update(s => s.CheckForUpdates = AutoUpdateSwitch.IsChecked == true);

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        var updates = _shell.Updates;

        // A known release turns the button into "install", so one click is enough.
        if (updates.Release is not null && updates.Stage is UpdateStage.Available or UpdateStage.Failed)
        {
            await updates.InstallAsync();
            return;
        }

        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = L.T("Settings.UpdateChecking");

        var result = await updates.CheckAsync();

        CheckUpdatesButton.IsEnabled = true;
        ShowUpdateStatus(result);
    }

    private void ShowUpdateStatus(UpdateCheckResult? result)
    {
        var updates = _shell.Updates;
        var release = updates.Release;

        UpdateStatusText.Text = result switch
        {
            _ when !updates.IsEnabled => L.T("Settings.UpdateUnavailable"),
            UpdateCheckResult.Failed => L.T("Update.ErrorNetwork"),
            _ when release is not null => L.F("Settings.UpdateAvailable", release.Version.ToString(3)),
            UpdateCheckResult.UpToDate => L.T("Settings.UpToDate"),
            _ => "",
        };
        UpdateStatusText.Visibility = UpdateStatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        CheckUpdatesButton.IsEnabled = updates.IsEnabled;
        CheckUpdatesButton.Style = (Style)FindResource(release is null ? "Button.Base" : "Button.Primary");
        CheckUpdatesButton.Content = release is null
            ? L.T("Settings.CheckUpdates")
            : updates.CanInstallInPlace ? L.T("Update.Install") : L.T("Update.Download");
    }

    private async void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        var confirmed = await _shell.ConfirmAsync(
            L.T("Settings.Reset.Title"),
            L.T("Settings.Reset.Text"),
            L.T("Settings.Reset.Confirm"));

        if (!confirmed)
            return;

        // Autostart lives in the registry and is not a preference to reset silently; game records
        // are achievements, not preferences.
        _settings.Save(new AppSettings
        {
            RunAtStartup = _settings.Current.RunAtStartup,
            DismissedUpdateVersion = _settings.Current.DismissedUpdateVersion,
            MatchBestMs = _settings.Current.MatchBestMs,
            TrueFalseBest = _settings.Current.TrueFalseBest,
        });
        Load(_settings.Current);
        _shell.ShowToast(L.T("Settings.Reset.Done"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBar.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorBar.Visibility = Visibility.Collapsed;

    private static bool TryParseTime(string text, out TimeSpan value) =>
        TimeSpan.TryParseExact(text.Trim(), [@"hh\:mm", @"h\:mm"], CultureInfo.InvariantCulture, out value) &&
        value < TimeSpan.FromDays(1);

    private static string FormatTime(TimeSpan value) => $"{value.Hours:00}:{value.Minutes:00}";
}

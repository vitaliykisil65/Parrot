using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Parrot.App.Branding;
using Parrot.App.Controls;
using Parrot.App.Interop;
using Parrot.App.Localization;
using Parrot.App.Services;
using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Settings;
using Parrot.Core.Statistics;

namespace Parrot.App.Views;

public enum AppPage { Dictionary, Statistics, Settings }

/// <summary>
/// The single app window: a sidebar with sections, decks and the prompt schedule, and one
/// content area that hosts the dictionary, the statistics or the settings.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly CardRepository _repository;
    private readonly SettingsService _settings;
    private readonly IPromptHost _host;
    private readonly UpdateService _updates;

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    private DictionaryView? _dictionary;
    private StatisticsView? _statistics;
    private SettingsView? _settingsView;

    private Action? _toastAction;
    private TaskCompletionSource<bool>? _dialog;

    /// <summary>The deck being renamed in the sidebar, or null when a new deck is being named.</summary>
    private Deck? _renamingDeck;

    private long? _deckFilter;

    public MainWindow(CardRepository repository, SettingsService settings, IPromptHost host, UpdateService updates)
    {
        _repository = repository;
        _settings = settings;
        _host = host;
        _updates = updates;

        InitializeComponent();

        Icon = LogoFactory.Image;
        LogoImage.Source = LogoFactory.Image;

        _clock.Tick += (_, _) => UpdateSchedule();
        _toastTimer.Tick += (_, _) => HideToast();
        _host.StateChanged += OnHostStateChanged;
        _updates.StateChanged += OnUpdateStateChanged;

        SourceInitialized += (_, _) => NativeMethods.UseRoundedCorners(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) => UpdateMaximizedState();
        Loaded += (_, _) =>
        {
            RefreshSidebar();
            RenderUpdate();
            _clock.Start();
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _toastTimer.Stop();
            _host.StateChanged -= OnHostStateChanged;
            _updates.StateChanged -= OnUpdateStateChanged;
            _dialog?.TrySetResult(false);
        };

        NavDictionary.IsChecked = true;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    public void ShowPage(AppPage page)
    {
        var target = page switch
        {
            AppPage.Statistics => NavStatistics,
            AppPage.Settings => NavSettings,
            _ => NavDictionary,
        };

        if (target.IsChecked == true)
            OnNavChecked(target, new RoutedEventArgs());
        else
            target.IsChecked = true;
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;

        if (sender == NavStatistics)
        {
            _statistics ??= new StatisticsView(_repository);
            _statistics.Reload();
            PageHost.Content = _statistics;
        }
        else if (sender == NavSettings)
        {
            _settingsView ??= new SettingsView(_settings, this);
            PageHost.Content = _settingsView;
        }
        else
        {
            _dictionary ??= CreateDictionary();
            PageHost.Content = _dictionary;
        }
    }

    private DictionaryView CreateDictionary()
    {
        var view = new DictionaryView(_repository, this);
        view.CardsChanged += (_, _) => RefreshSidebar();
        view.SetDeck(_deckFilter);
        return view;
    }

    /// <summary>Keeps every view honest after a prompt changed a card's schedule.</summary>
    public UpdateService Updates => _updates;

    public void RefreshData()
    {
        RefreshSidebar();
        _dictionary?.Reload();

        if (PageHost.Content == _statistics)
            _statistics?.Reload();
    }

    // ── Sidebar: decks ───────────────────────────────────────────────────────

    private void RefreshSidebar()
    {
        RefreshDecks();
        RefreshStreak();
        UpdateSchedule();
    }

    private void RefreshDecks()
    {
        var decks = _repository.GetDecks();
        var total = decks.Sum(d => d.CardCount);

        NavDictionary.SetValue(Ui.BadgeProperty, total.ToString());

        // A filter pointing at a deck that no longer exists falls back to "all decks".
        if (_deckFilter is { } id && decks.All(d => d.Id != id))
            SelectDeck(null);

        DeckList.Children.Clear();
        DeckList.Children.Add(DeckButton(L.T("Sidebar.AllDecks"), null, null, total, isActive: true));

        for (var i = 0; i < decks.Count; i++)
        {
            var deck = decks[i];
            var dot = (Brush)FindResource($"Brush.Deck{i % 4}");
            DeckList.Children.Add(DeckButton(deck.Name, deck, dot, deck.CardCount, deck.IsActive));
        }
    }

    private RadioButton DeckButton(string name, Deck? deck, Brush? dot, int count, bool isActive)
    {
        var button = new RadioButton
        {
            Style = (Style)FindResource("DeckItem"),
            GroupName = "Decks",
            Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis },
            Tag = deck,
            IsChecked = deck?.Id == _deckFilter,
            Margin = new Thickness(0, 0, 0, 2),
            ToolTip = isActive ? null : L.T("Deck.InactiveTip"),
            Opacity = isActive ? 1 : 0.6,
        };

        button.SetValue(Ui.DotProperty, dot);
        button.SetValue(Ui.BadgeProperty, count.ToString());

        if (deck is null)
            button.SetValue(Ui.IconProperty, FindResource("Icon.Folder"));
        else
            button.ContextMenu = DeckMenu(deck);

        button.Checked += (_, _) =>
        {
            _deckFilter = deck?.Id;
            ShowPage(AppPage.Dictionary);
            _dictionary?.SetDeck(_deckFilter);
        };

        return button;
    }

    private ContextMenu DeckMenu(Deck deck)
    {
        var rename = new MenuItem { Header = L.T("Deck.Rename") };
        rename.SetValue(Ui.IconProperty, FindResource("Icon.Pencil"));
        rename.Click += (_, _) => BeginDeckName(deck);

        var active = new MenuItem { Header = L.T("Deck.Active"), IsCheckable = true, IsChecked = deck.IsActive };
        active.Click += (_, _) =>
        {
            deck.IsActive = active.IsChecked;
            _repository.UpdateDeck(deck);
            RefreshDecks();
        };

        var delete = new MenuItem { Header = L.T("Deck.Delete"), Style = (Style)FindResource("MenuItem.Danger") };
        delete.SetValue(Ui.IconProperty, FindResource("Icon.Trash"));
        delete.Click += async (_, _) =>
        {
            var confirmed = await ConfirmAsync(
                L.F("Deck.DeleteTitle", deck.Name),
                L.F("Deck.DeleteText", L.Plural(deck.CardCount, "Plural.Card")),
                L.T("Common.Delete"), danger: true);

            if (!confirmed)
                return;

            _repository.DeleteDeck(deck.Id);
            RefreshSidebar();
            _dictionary?.Reload();
            ShowToast(L.F("Deck.Deleted", deck.Name));
        };

        return new ContextMenu { Items = { rename, active, new Separator(), delete } };
    }

    private void SelectDeck(long? deckId)
    {
        _deckFilter = deckId;
        _dictionary?.SetDeck(deckId);
    }

    private void OnNewDeck(object sender, RoutedEventArgs e) => BeginDeckName(null);

    private void BeginDeckName(Deck? deck)
    {
        _renamingDeck = deck;
        DeckNameBox.Text = deck?.Name ?? "";
        DeckNameBox.Visibility = Visibility.Visible;
        NewDeckButton.Visibility = Visibility.Collapsed;
        DeckNameBox.Focus();
        DeckNameBox.SelectAll();
    }

    private void OnDeckNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitDeckName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndDeckName();
            e.Handled = true;
        }
    }

    private void OnDeckNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitDeckName();

    private void CommitDeckName()
    {
        if (DeckNameBox.Visibility != Visibility.Visible)
            return;

        var name = DeckNameBox.Text.Trim();
        var deck = _renamingDeck;
        EndDeckName();

        if (name.Length == 0)
            return;

        if (deck is null)
        {
            var created = new Deck { Name = name };
            _repository.AddDeck(created);
            _deckFilter = created.Id;
            ShowToast(L.F("Deck.Created", name));
        }
        else if (deck.Name != name)
        {
            deck.Name = name;
            _repository.UpdateDeck(deck);
        }

        RefreshDecks();
        _dictionary?.SetDeck(_deckFilter);
    }

    private void EndDeckName()
    {
        DeckNameBox.Visibility = Visibility.Collapsed;
        NewDeckButton.Visibility = Visibility.Visible;
        _renamingDeck = null;
    }

    // ── Sidebar: streak and schedule ─────────────────────────────────────────

    private void RefreshStreak()
    {
        var report = StatisticsCalculator.Calculate(
            _repository.GetCards(new CardQuery()), _repository.GetReviews(), DateTimeOffset.Now);

        StreakText.Text = report.CurrentStreak > 0
            ? L.F("Streak.Running", L.Plural(report.CurrentStreak, "Plural.Day"))
            : L.T("Streak.None");

        StreakNote.Text = report.BestStreak > report.CurrentStreak
            ? L.F("Streak.Record", report.BestStreak)
            : report.CurrentStreak > 0 ? L.T("Streak.IsRecord") : L.T("Streak.Hint");

        StreakDays.Children.Clear();
        foreach (var day in report.Daily.TakeLast(7))
        {
            var answered = day.Correct + day.Missed > 0;
            StreakDays.Children.Add(new Rectangle
            {
                Height = 6,
                RadiusX = 3,
                RadiusY = 3,
                Margin = new Thickness(2, 0, 2, 0),
                Fill = (Brush)FindResource(answered ? "Brush.Primary" : "Brush.StreakEmpty"),
                ToolTip = day.Day.ToString("dddd, d MMMM", L.Culture),
            });
        }
    }

    private void UpdateSchedule()
    {
        var now = DateTimeOffset.Now;

        if (_host.PausedUntil is { } until && until > now)
        {
            NextLabel.Text = L.T("Schedule.Paused");
            NextText.Text = L.F("Schedule.Until", until.ToString(until.Date == now.Date ? "HH:mm" : "dd.MM HH:mm"));
            PauseButton.SetValue(Ui.IconProperty, FindResource("Icon.Play"));
            PauseButton.ToolTip = L.T("Schedule.Resume");
            return;
        }

        PauseButton.SetValue(Ui.IconProperty, FindResource("Icon.Pause"));
        PauseButton.ToolTip = L.T("Schedule.Pause");

        if (!_settings.Current.IsWithinActiveWindow(now))
        {
            NextLabel.Text = L.T("Settings.QuietHours");
            NextText.Text = L.F("Schedule.Until", _settings.Current.QuietTo.ToString(@"hh\:mm"));
            return;
        }

        NextLabel.Text = L.T("Sidebar.NextCard");
        NextText.Text = _host.NextPromptAt is not { } next
            ? "—"
            : (next - now).TotalMinutes switch
            {
                < 1 => L.T("Schedule.InAMinute"),
                < 60 and var minutes => L.F("Schedule.InMinutes", Math.Ceiling(minutes)),
                _ => L.F("Schedule.At", next.ToString("HH:mm")),
            };
    }

    private void OnHostStateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshSidebar);

    private void OnPromptNow(object sender, RoutedEventArgs e) => _host.PromptNow();

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_host.PausedUntil is { } until && until > DateTimeOffset.Now)
        {
            _host.Resume();
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = PauseButton,
            Placement = PlacementMode.Top,
        };

        foreach (var (label, duration) in new[]
                 {
                     (L.T("Pause.Minutes15"), TimeSpan.FromMinutes(15)),
                     (L.T("Pause.Hour"), TimeSpan.FromHours(1)),
                     (L.T("Pause.Hours3"), TimeSpan.FromHours(3)),
                     (L.T("Pause.Tomorrow"), UntilTomorrowMorning()),
                 })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => _host.Pause(duration);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static TimeSpan UntilTomorrowMorning()
    {
        var now = DateTime.Now;
        return now.Date.AddDays(1).AddHours(8) - now;
    }

    // ── Sidebar: update card ─────────────────────────────────────────────────

    private void OnUpdateStateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RenderUpdate);

    private void RenderUpdate()
    {
        var stage = _updates.CardStage;
        UpdateCard.Visibility = stage is null ? Visibility.Collapsed : Visibility.Visible;
        if (stage is null || _updates.Release is not { } release)
        {
            StopIndeterminate();
            return;
        }

        var version = release.Version.ToString(3);
        var failed = stage == UpdateStage.Failed;
        var busy = stage is UpdateStage.Downloading or UpdateStage.Installing;

        UpdateCard.BorderBrush = (Brush)FindResource(stage == UpdateStage.Available ? "Brush.Primary" : "Brush.Line");
        UpdateIconBox.Background = (Brush)FindResource(stage switch
        {
            UpdateStage.Available => "Brush.Primary",
            UpdateStage.Failed => "Brush.DangerSoft",
            _ => "Brush.Active",
        });
        UpdateIcon.Foreground = (Brush)FindResource(stage switch
        {
            UpdateStage.Available => "Brush.PrimaryText",
            UpdateStage.Failed => "Brush.Danger",
            _ => "Brush.ActiveIcon",
        });
        UpdateIcon.Data = (Geometry)FindResource(stage switch
        {
            UpdateStage.Failed => "Icon.Alert",
            UpdateStage.Installing => "Icon.Reset",
            _ => "Icon.Download",
        });

        UpdateTitle.Text = stage switch
        {
            UpdateStage.Downloading => L.T("Update.Downloading"),
            UpdateStage.Installing => L.T("Update.Installing"),
            UpdateStage.Failed => L.T("Update.Failed"),
            _ => L.F("Update.Available", version),
        };

        UpdateNote.Text = stage switch
        {
            UpdateStage.Downloading => DownloadNote(),
            UpdateStage.Installing => L.T("Update.Restarting"),
            UpdateStage.Failed => _updates.Error ?? "",
            _ => "",
        };
        UpdateNote.Visibility = UpdateNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateLinkText.Visibility = stage == UpdateStage.Available ? Visibility.Visible : Visibility.Collapsed;

        UpdateTrack.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (stage == UpdateStage.Installing)
            StartIndeterminate();
        else
        {
            StopIndeterminate();
            UpdateFill.Width = UpdateTrack.ActualWidth * _updates.Progress;
        }

        UpdateClose.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        UpdateText.Margin = new Thickness(10, 0, busy ? 0 : 24, 0);
        UpdateButton.Visibility = stage == UpdateStage.Installing ? Visibility.Collapsed : Visibility.Visible;
        UpdateButton.Style = (Style)FindResource(stage switch
        {
            UpdateStage.Available => "Button.Primary",
            UpdateStage.Failed => "Button.Soft",
            _ => "Button.Ghost",
        });
        UpdateButton.Content = stage switch
        {
            UpdateStage.Downloading => L.T("Common.Cancel"),
            UpdateStage.Failed => L.T("Update.Retry"),
            _ when !_updates.CanInstallInPlace => L.T("Update.Download"),
            _ => L.T("Update.Install"),
        };
        UpdateButton.Margin = new Thickness(0, stage == UpdateStage.Downloading ? 6 : 12, 0, 0);
        UpdateButton.MinHeight = stage == UpdateStage.Downloading ? 28 : 36;
    }

    private string DownloadNote()
    {
        var total = _updates.Release?.InstallerSize ?? 0;
        var percent = (int)Math.Round(_updates.Progress * 100);
        return total > 0
            ? L.F("Update.Progress", percent, Megabytes(_updates.Downloaded), Megabytes(total))
            : $"{percent} %";
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0", L.Culture);

    private void OnUpdateTrackSizeChanged(object sender, SizeChangedEventArgs e) => RenderUpdate();

    private bool _indeterminate;

    /// <summary>A short segment sliding along the track while the installer runs.</summary>
    private void StartIndeterminate()
    {
        if (_indeterminate || UpdateTrack.ActualWidth <= 0)
            return;

        _indeterminate = true;
        var width = UpdateTrack.ActualWidth;
        UpdateFill.Width = width * 0.35;
        UpdateFillShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-width * 0.35, width, TimeSpan.FromSeconds(1.2))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private void StopIndeterminate()
    {
        if (!_indeterminate)
            return;

        _indeterminate = false;
        UpdateFillShift.BeginAnimation(TranslateTransform.XProperty, null);
        UpdateFillShift.X = 0;
    }

    private async void OnUpdateAction(object sender, RoutedEventArgs e)
    {
        if (_updates.Stage == UpdateStage.Downloading)
            _updates.CancelDownload();
        else
            await _updates.InstallAsync();
    }

    private void OnUpdateDismiss(object sender, RoutedEventArgs e) => _updates.Dismiss();

    private void OnUpdateNotes(object sender, MouseButtonEventArgs e) => _updates.OpenReleasePage();

    // ── Toast and dialog ─────────────────────────────────────────────────────

    public void ShowToast(string message, string? actionText = null, Action? action = null)
    {
        ToastText.Text = message;
        ToastAction.Content = actionText;
        ToastAction.Visibility = actionText is null ? Visibility.Collapsed : Visibility.Visible;
        _toastAction = action;

        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.Visibility = Visibility.Collapsed;
        _toastAction = null;
    }

    private void OnToastAction(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        HideToast();
        action?.Invoke();
    }

    private void OnToastClose(object sender, RoutedEventArgs e) => HideToast();

    /// <summary>An in-window confirmation, so questions look like the rest of the app.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogConfirm.Content = confirmText;
        DialogConfirm.Style = (Style)FindResource(danger ? "Button.DangerSolid" : "Button.Primary");
        DialogLayer.Visibility = Visibility.Visible;
        DialogCancel.Focus();

        return _dialog.Task;
    }

    private void CloseDialog(bool result)
    {
        DialogLayer.Visibility = Visibility.Collapsed;
        var dialog = _dialog;
        _dialog = null;
        dialog?.TrySetResult(result);
    }

    private void OnDialogConfirm(object sender, RoutedEventArgs e) => CloseDialog(true);

    private void OnDialogCancel(object sender, RoutedEventArgs e) => CloseDialog(false);

    // ── Keyboard ─────────────────────────────────────────────────────────────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DialogLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseDialog(false); e.Handled = true; }
            else if (e.Key == Key.Enter && !DialogCancel.IsKeyboardFocused) { CloseDialog(true); e.Handled = true; }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1: ShowPage(AppPage.Dictionary); e.Handled = true; return;
                case Key.D2: ShowPage(AppPage.Statistics); e.Handled = true; return;
                case Key.D3: ShowPage(AppPage.Settings); e.Handled = true; return;
                case Key.K or Key.F:
                    ShowPage(AppPage.Dictionary);
                    _dictionary?.FocusSearch();
                    e.Handled = true;
                    return;
                case Key.N:
                    ShowPage(AppPage.Dictionary);
                    _dictionary?.BeginAdd();
                    e.Handled = true;
                    return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    // ── Window chrome ────────────────────────────────────────────────────────

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizedState()
    {
        var maximized = WindowState == WindowState.Maximized;

        MaximizeButton.SetValue(Ui.IconProperty, FindResource(maximized ? "Icon.Restore" : "Icon.Maximize"));
        MaximizeButton.ToolTip = L.T(maximized ? "Window.Restore" : "Window.Maximize");

        if (!maximized)
        {
            Frame.BorderThickness = new Thickness(1);
            Frame.Padding = new Thickness(0);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var overhang = NativeMethods.MaximizedOverhang((uint)Math.Round(dpi.PixelsPerInchX));

        Frame.BorderThickness = new Thickness(0);
        Frame.Padding = new Thickness(overhang / dpi.DpiScaleX);
    }

}

using System.Windows;
using System.Windows.Threading;
using Parrot.App.Localization;
using Parrot.App.Services;
using Parrot.App.Views;
using Parrot.Core;
using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Scheduling;
using Parrot.Core.Settings;

namespace Parrot.App;

public partial class App : Application, IPromptHost
{
    private const string SingleInstanceMutexName = @"Local\Parrot.SingleInstance";

    /// <summary>
    /// One instance per data folder: a copy pointed elsewhere with PARROT_DATA_DIR (a demo or
    /// a test) can run next to the everyday one without touching its database.
    /// </summary>
    private static string InstanceMutexName() =>
        Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable) is { Length: > 0 }
            ? $"{SingleInstanceMutexName}.{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(AppPaths.DataDirectory.ToUpperInvariant())))[..16]}"
            : SingleInstanceMutexName;

    private Mutex? _instanceMutex;
    private Database? _database;
    private CardRepository? _repository;
    private SettingsService? _settings;
    private PromptService? _prompts;
    private PromptScheduler? _scheduler;
    private TrayIconService? _tray;
    private UpdateService? _updates;

    private PromptWindow? _activePrompt;
    private MainWindow? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Until the settings are read, messages follow the Windows display language.
        L.Apply(null);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName(), out var isFirstInstance);
        if (!isFirstInstance)
        {
            // A second copy would fight the first one over the database and the tray icon.
            MessageBox.Show(L.T("App.AlreadyRunning"), "Parrot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        try
        {
            Compose();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start the app", ex);
            MessageBox.Show(L.F("App.StartFailed", ex.Message, Log.CurrentFile), "Parrot",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Launched by the Run key, the app should appear only as a tray icon.
        if (e.Args.Contains("--updated", StringComparer.OrdinalIgnoreCase))
        {
            Log.Info($"Updated to {UpdateService.CurrentVersionText}");
            ShowMain(AppPage.Dictionary);
            _main?.ShowToast(L.F("Update.Done", UpdateService.CurrentVersionText));
        }
        else if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
        {
            ShowMain(AppPage.Dictionary);
        }
    }

    private void Compose()
    {
        AppPaths.EnsureCreated();
        Log.Info($"Starting Parrot {UpdateService.CurrentVersionText}");

        _database = new Database();
        _repository = new CardRepository(_database);
        _settings = new SettingsService();
        L.Apply(_settings.Current.UiLanguage);
        Log.Info($"UI language: {L.Language}");

        SeedData.EnsureSeeded(_repository, DevData.StarterDeck.Read(), L.T("Deck.DefaultName"));
        ThemeManager.Apply(_settings.Current.Theme);
        _settings.Changed += (_, updated) =>
        {
            if (updated.UiLanguage != L.Language)
                Dispatcher.BeginInvoke(() => ApplyLanguage(updated.UiLanguage));

            ThemeManager.Apply(updated.Theme);
            _tray?.ShowNextPromptTime(_scheduler?.NextPromptAt);
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        ThemeManager.Applied += (_, _) => _main?.RefreshData();

        _prompts = new PromptService(_repository, _settings);

        _scheduler = new PromptScheduler(_prompts, _settings);
        _scheduler.PromptReady += (_, request) => ShowPrompt(request);
        _scheduler.PromptSkipped += (_, skipped) => Log.Info($"Prompt skipped: {skipped.Reason}");

        _tray = CreateTray();

        _scheduler.Start();
        _tray.ShowNextPromptTime(_scheduler.NextPromptAt);

        UpdateService.CleanUpDownloads();
        _updates = new UpdateService(_settings);
        _updates.StateChanged += (_, _) => _tray?.SetUpdate(_updates.Release);
        _updates.InstallerStarted += (_, _) => ShutdownForUpdate();
        _updates.Start();
    }

    private TrayIconService CreateTray()
    {
        var tray = new TrayIconService();
        tray.LibraryRequested += (_, _) => ShowMain(AppPage.Dictionary);
        tray.SettingsRequested += (_, _) => ShowMain(AppPage.Settings);
        tray.StatisticsRequested += (_, _) => ShowMain(AppPage.Statistics);
        tray.PromptNowRequested += (_, _) => PromptNow();
        tray.PauseRequested += (_, duration) => Pause(duration);
        tray.ResumeRequested += (_, _) => Resume();
        tray.ExitRequested += (_, _) => Shutdown();
        tray.UpdateRequested += async (_, _) =>
        {
            ShowMain(AppPage.Dictionary);
            if (_updates is not null)
                await _updates.InstallAsync();
        };
        tray.SetUpdate(_updates?.Release);
        return tray;
    }

    /// <summary>
    /// UI text is read when a window is built, so a new language means rebuilding what is on
    /// screen: the tray menu and, if open, the main window (reopened on the settings page).
    /// </summary>
    private void ApplyLanguage(string language)
    {
        L.Apply(language);

        _tray?.Dispose();
        _tray = CreateTray();
        _tray.SetPaused(PausedUntil is not null, PausedUntil);
        if (PausedUntil is null)
            _tray.ShowNextPromptTime(NextPromptAt);

        if (_main is null)
            return;

        _main.Close();
        ShowMain(AppPage.Settings);
    }

    // ── Prompts ──────────────────────────────────────────────────────────────

    private void ShowPrompt(PromptRequest request)
    {
        if (_activePrompt is not null || _settings is null || _prompts is null || _repository is null)
            return;

        var deckName = _repository.GetDecks().FirstOrDefault(d => d.Id == request.Card.DeckId)?.Name ?? "Parrot";

        var window = new PromptWindow(request, _prompts.CreateChecker(), _settings.Current, deckName);
        _activePrompt = window;
        _scheduler!.IsPromptOnScreen = true;

        window.Completed += (_, result) =>
        {
            try
            {
                _prompts.Record(request, result.Outcome, result.Answer, DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save the answer", ex);
            }

            _activePrompt = null;
            _scheduler.IsPromptOnScreen = false;
            _tray?.ShowNextPromptTime(_scheduler.NextPromptAt);
            _main?.RefreshData();
            StateChanged?.Invoke(this, EventArgs.Empty);
        };

        window.Show();

        if (_settings.Current.SoundEnabled)
            SoundService.PlayChime();
    }

    // ── IPromptHost ──────────────────────────────────────────────────────────

    public DateTimeOffset? NextPromptAt => _scheduler?.NextPromptAt;

    public DateTimeOffset? PausedUntil => _prompts?.IsPaused(DateTimeOffset.Now) == true ? _prompts.PausedUntil : null;

    public event EventHandler? StateChanged;

    public void PromptNow()
    {
        if (_activePrompt is not null)
        {
            _activePrompt.Activate();
            return;
        }

        if (_scheduler?.TriggerNow() == false)
        {
            var message = L.T("App.NothingDue");

            if (_main is { IsVisible: true })
                _main.ShowToast(message);
            else
                _tray?.Notify(message);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause(TimeSpan duration)
    {
        _prompts?.Pause(duration);
        _scheduler?.Reschedule();
        _tray?.SetPaused(true, _prompts?.PausedUntil);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        _prompts?.Resume();
        _scheduler?.Reschedule();
        _tray?.SetPaused(false, null);
        _tray?.ShowNextPromptTime(_scheduler?.NextPromptAt);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    private void ShowMain(AppPage page)
    {
        if (_repository is null || _settings is null)
            return;

        if (_main is null)
        {
            _main = new MainWindow(_repository, _settings, this, _updates!);
            _main.Closed += (_, _) => _main = null;
            _main.Show();
        }

        _main.ShowPage(page);
        Restore(_main);
    }

    private static void Restore(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }

    // ── Shutdown ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The installer is waiting for this process to let go of the exe and the single-instance
    /// mutex; it starts the new version itself once it has finished.
    /// </summary>
    private void ShutdownForUpdate()
    {
        Log.Info("Exiting to install an update");
        _scheduler?.Stop();
        _activePrompt?.Close();
        _main?.Close();
        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception", e.Exception);

        MessageBox.Show(L.F("App.Crashed", e.Exception.Message, Log.CurrentFile), "Parrot",
            MessageBoxButton.OK, MessageBoxImage.Warning);

        // A failed prompt or a bad card must not take the whole tray app down with it.
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Exit");

        _scheduler?.Dispose();
        _updates?.Dispose();
        _tray?.Dispose();
        _database?.Dispose();

        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}

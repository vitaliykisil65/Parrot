using System.Windows;
using System.Windows.Threading;
using Parrot.App.Services;
using Parrot.App.Views;
using Parrot.Core;
using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Scheduling;
using Parrot.Core.Settings;

namespace Parrot.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Parrot.SingleInstance";

    private Mutex? _instanceMutex;
    private Database? _database;
    private CardRepository? _repository;
    private SettingsService? _settings;
    private PromptService? _prompts;
    private PromptScheduler? _scheduler;
    private TrayIconService? _tray;

    private PromptWindow? _activePrompt;
    private LibraryWindow? _library;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // A second copy would fight the first one over the database and the tray icon.
            MessageBox.Show("Parrot уже запущено — шукайте його в системному треї.", "Parrot",
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
            Log.Error("Не вдалося запустити застосунок", ex);
            MessageBox.Show($"Не вдалося запустити Parrot:\n\n{ex.Message}\n\nДеталі: {Log.CurrentFile}", "Parrot",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Launched by the Run key, the app should appear only as a tray icon.
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
            ShowLibrary();
    }

    private void Compose()
    {
        AppPaths.EnsureCreated();
        Log.Info("Запуск Parrot");

        _database = new Database();
        _repository = new CardRepository(_database);
        SeedData.EnsureSeeded(_repository);

        _settings = new SettingsService();
        ThemeManager.Apply(_settings.Current.Theme);
        _settings.Changed += (_, updated) => ThemeManager.Apply(updated.Theme);

        _prompts = new PromptService(_repository, _settings);

        _scheduler = new PromptScheduler(_prompts, _settings);
        _scheduler.PromptReady += (_, request) => ShowPrompt(request);
        _scheduler.PromptSkipped += (_, skipped) => Log.Info($"Показ пропущено: {skipped.Reason}");

        _tray = new TrayIconService();
        _tray.LibraryRequested += (_, _) => ShowLibrary();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.PromptNowRequested += (_, _) => PromptNow();
        _tray.PauseRequested += (_, duration) => Pause(duration);
        _tray.ResumeRequested += (_, _) => Resume();
        _tray.ExitRequested += (_, _) => Shutdown();

        _scheduler.Start();
        _tray.ShowNextPromptTime(_scheduler.NextPromptAt);
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
                Log.Error("Не вдалося зберегти відповідь", ex);
            }

            _activePrompt = null;
            _scheduler.IsPromptOnScreen = false;
            _tray?.ShowNextPromptTime(_scheduler.NextPromptAt);
            _library?.RefreshIfVisible();
        };

        window.Show();
    }

    private void PromptNow()
    {
        if (_activePrompt is not null)
        {
            _activePrompt.Activate();
            return;
        }

        if (_scheduler?.TriggerNow() == false)
            _tray?.Notify("Зараз нема чого показати — усі картки ще не на черзі.");
    }

    private void Pause(TimeSpan duration)
    {
        _prompts?.Pause(duration);
        _scheduler?.Reschedule();
        _tray?.SetPaused(true, _prompts?.PausedUntil);
    }

    private void Resume()
    {
        _prompts?.Resume();
        _scheduler?.Reschedule();
        _tray?.SetPaused(false, null);
        _tray?.ShowNextPromptTime(_scheduler?.NextPromptAt);
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    private void ShowLibrary()
    {
        if (_repository is null || _settings is null)
            return;

        if (_library is null)
        {
            _library = new LibraryWindow(_repository);
            _library.Closed += (_, _) => _library = null;
            _library.SettingsRequested += (_, _) => ShowSettings();
            _library.Show();
        }

        Restore(_library);
    }

    private void ShowSettings()
    {
        if (_settings is null)
            return;

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        Restore(_settingsWindow);
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

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Необроблена помилка", e.Exception);

        MessageBox.Show($"Сталася помилка:\n\n{e.Exception.Message}\n\nДеталі: {Log.CurrentFile}", "Parrot",
            MessageBoxButton.OK, MessageBoxImage.Warning);

        // A failed prompt or a bad card must not take the whole tray app down with it.
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Вихід");

        _scheduler?.Dispose();
        _tray?.Dispose();
        _database?.Dispose();

        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}

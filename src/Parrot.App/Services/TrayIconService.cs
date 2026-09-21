using Parrot.App.Branding;
using Parrot.App.Localization;
using Forms = System.Windows.Forms;

namespace Parrot.App.Services;

/// <summary>
/// The app's permanent home. Everything else — prompts, the main window — is reachable
/// from here, and closing a window never quits the app.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _resumeItem;
    private readonly Forms.ToolStripMenuItem _updateItem;
    private readonly Forms.ToolStripSeparator _updateSeparator;
    private System.Drawing.Icon? _trayImage;

    public TrayIconService()
    {
        _pauseItem = new Forms.ToolStripMenuItem(L.T("Tray.Pause"));
        _pauseItem.DropDownItems.Add(PauseEntry(L.T("Pause.Minutes15"), TimeSpan.FromMinutes(15)));
        _pauseItem.DropDownItems.Add(PauseEntry(L.T("Pause.Hour"), TimeSpan.FromHours(1)));
        _pauseItem.DropDownItems.Add(PauseEntry(L.T("Pause.Hours3"), TimeSpan.FromHours(3)));
        _pauseItem.DropDownItems.Add(PauseEntry(L.T("Pause.Tomorrow"), TimeSpan.FromHours(12)));

        _resumeItem = new Forms.ToolStripMenuItem(L.T("Schedule.Resume"), null, (_, _) => ResumeRequested?.Invoke(this, EventArgs.Empty))
        {
            Visible = false,
        };

        _updateItem = new Forms.ToolStripMenuItem("", null, (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty))
        {
            Visible = false,
        };
        _updateSeparator = new Forms.ToolStripSeparator { Visible = false };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Tray.Open"), null, (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new System.Drawing.Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold),
        });
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Tray.ShowNow"), null, (_, _) => PromptNowRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Nav.Statistics"), null, (_, _) => StatisticsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_resumeItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Nav.Settings"), null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_updateItem);
        menu.Items.Add(_updateSeparator);
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Tray.Exit"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new Forms.NotifyIcon
        {
            Text = "Parrot",
            ContextMenuStrip = menu,
        };

        UpdateIcon();
        _icon.Visible = true;

        _icon.DoubleClick += (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty);
        ThemeManager.SystemThemeChanged += OnSystemThemeChanged;
    }

    public event EventHandler? LibraryRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? StatisticsRequested;
    public event EventHandler? PromptNowRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<TimeSpan>? PauseRequested;
    public event EventHandler? UpdateRequested;

    /// <summary>Shows "Update to X" while a newer release is known.</summary>
    public void SetUpdate(Parrot.Core.Updates.ReleaseInfo? release)
    {
        _updateItem.Visible = _updateSeparator.Visible = release is not null;
        if (release is not null)
            _updateItem.Text = L.F("Update.TrayItem", release.Version.ToString(3));
    }

    public void SetPaused(bool paused, DateTimeOffset? until)
    {
        _resumeItem.Visible = paused;
        _pauseItem.Visible = !paused;

        SetTooltip(paused && until is not null
            ? L.F("Tray.PausedUntil", until.Value.ToString("HH:mm"))
            : "Parrot");
    }

    public void ShowNextPromptTime(DateTimeOffset? next)
    {
        SetTooltip(next is null ? "Parrot" : L.F("Tray.NextAt", next.Value.ToString("HH:mm")));
    }

    public void Notify(string message, string title = "Parrot")
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(3000);
    }

    /// <summary>Line art on a dark taskbar, the colour bird on a light one.</summary>
    private void UpdateIcon()
    {
        var previous = _trayImage;
        _trayImage = LogoFactory.TrayIcon(ThemeManager.IsTaskbarDark());
        _icon.Icon = _trayImage;
        previous?.Dispose();
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e) => UpdateIcon();

    /// <summary>The Win32 tooltip is capped at 63 characters; anything longer is dropped silently.</summary>
    private void SetTooltip(string text) =>
        _icon.Text = text.Length <= 63 ? text : text[..63];

    private Forms.ToolStripMenuItem PauseEntry(string label, TimeSpan duration) =>
        new(label, null, (_, _) => PauseRequested?.Invoke(this, duration));

    public void Dispose()
    {
        ThemeManager.SystemThemeChanged -= OnSystemThemeChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _trayImage?.Dispose();
    }
}

using Parrot.App.Branding;
using Forms = System.Windows.Forms;

namespace Parrot.App.Services;

/// <summary>
/// The app's permanent home. Everything else — prompts, library, settings — is reachable
/// from here, and closing a window never quits the app.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _resumeItem;

    public TrayIconService()
    {
        _pauseItem = new Forms.ToolStripMenuItem("Пауза");
        _pauseItem.DropDownItems.Add(PauseEntry("15 хвилин", TimeSpan.FromMinutes(15)));
        _pauseItem.DropDownItems.Add(PauseEntry("1 година", TimeSpan.FromHours(1)));
        _pauseItem.DropDownItems.Add(PauseEntry("3 години", TimeSpan.FromHours(3)));
        _pauseItem.DropDownItems.Add(PauseEntry("До завтра", TimeSpan.FromHours(12)));

        _resumeItem = new Forms.ToolStripMenuItem("Відновити показ", null, (_, _) => ResumeRequested?.Invoke(this, EventArgs.Empty))
        {
            Visible = false,
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Бібліотека", null, (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new System.Drawing.Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold),
        });
        menu.Items.Add(new Forms.ToolStripMenuItem("Показати картку зараз", null, (_, _) => PromptNowRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Статистика", null, (_, _) => StatisticsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_resumeItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Налаштування", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Вихід", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new Forms.NotifyIcon
        {
            Icon = LogoFactory.Icon,
            Text = "Parrot",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? LibraryRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? StatisticsRequested;
    public event EventHandler? PromptNowRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<TimeSpan>? PauseRequested;

    public void SetPaused(bool paused, DateTimeOffset? until)
    {
        _resumeItem.Visible = paused;
        _pauseItem.Visible = !paused;

        SetTooltip(paused && until is not null
            ? $"Parrot — пауза до {until:HH:mm}"
            : "Parrot");
    }

    public void ShowNextPromptTime(DateTimeOffset? next)
    {
        SetTooltip(next is null ? "Parrot" : $"Parrot — наступна картка о {next:HH:mm}");
    }

    public void Notify(string message, string title = "Parrot")
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(3000);
    }

    /// <summary>The Win32 tooltip is capped at 63 characters; anything longer is dropped silently.</summary>
    private void SetTooltip(string text) =>
        _icon.Text = text.Length <= 63 ? text : text[..63];

    private Forms.ToolStripMenuItem PauseEntry(string label, TimeSpan duration) =>
        new(label, null, (_, _) => PauseRequested?.Invoke(this, duration));

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

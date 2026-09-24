using System.Windows.Threading;
using Parrot.App.Interop;
using Parrot.Core.Scheduling;
using Parrot.Core.Settings;

namespace Parrot.App.Services;

public sealed record SkippedPrompt(string Reason);

/// <summary>
/// Decides when the next prompt fires. The scheduler owns only the clock and the
/// "is the user actually here?" checks; which card to show is <see cref="PromptService"/>'s job.
/// </summary>
public sealed class PromptScheduler : IDisposable
{
    /// <summary>
    /// When a prompt is suppressed because the user stepped away or is in a game, we come
    /// back soon rather than burning the whole interval — otherwise a single busy moment
    /// can cost half an hour of practice.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);

    private readonly DispatcherTimer _timer = new();
    private readonly PromptService _prompts;
    private readonly SettingsService _settings;
    private readonly Random _random = new();

    /// <summary>The timing the current countdown was drawn from.</summary>
    private (int Interval, int Jitter) _timing;

    public PromptScheduler(PromptService prompts, SettingsService settings)
    {
        _prompts = prompts;
        _settings = settings;

        _timer.Tick += OnTick;
        // Settings now apply on every change; only a new interval or spread restarts the clock,
        // otherwise flipping the theme would postpone the next card.
        _settings.Changed += (_, updated) =>
        {
            if ((updated.IntervalMinutes, updated.JitterPercent) != _timing)
                Reschedule();
        };
    }

    public event EventHandler<PromptRequest>? PromptReady;
    public event EventHandler<SkippedPrompt>? PromptSkipped;

    public DateTimeOffset? NextPromptAt { get; private set; }

    /// <summary>Set by the UI while a prompt window is visible, to avoid stacking prompts.</summary>
    public bool IsPromptOnScreen { get; set; }

    /// <summary>Set while the user plays a practice game; a prompt then would only get in the way.</summary>
    public bool IsPracticeActive { get; set; }

    public void Start() => Reschedule();

    public void Stop()
    {
        _timer.Stop();
        NextPromptAt = null;
    }

    /// <summary>
    /// "Show one now" from the tray menu. Ignores the clock and the idle checks — the user
    /// just asked for it — but still respects pause and quiet hours reporting.
    /// </summary>
    public bool TriggerNow()
    {
        var prompt = _prompts.TryCreatePrompt(DateTimeOffset.Now, out var reason);

        if (prompt is null)
        {
            PromptSkipped?.Invoke(this, new SkippedPrompt(Describe(reason)));
            return false;
        }

        PromptReady?.Invoke(this, prompt);
        Reschedule();
        return true;
    }

    public void Reschedule()
    {
        _timer.Stop();

        var settings = _settings.Current;
        _timing = (settings.IntervalMinutes, settings.JitterPercent);

        var delay = settings.NextDelay(_random);
        NextPromptAt = DateTimeOffset.Now + delay;

        _timer.Interval = delay;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (TryFire() is { } retryIn)
        {
            NextPromptAt = DateTimeOffset.Now + retryIn;
            _timer.Interval = retryIn;
            _timer.Start();
            return;
        }

        Reschedule();
    }

    /// <summary>Returns a retry delay when the prompt was suppressed, or null when it was shown.</summary>
    private TimeSpan? TryFire()
    {
        if (IsPromptOnScreen)
            return RetryDelay;

        if (IsPracticeActive)
            return Skip("A practice session is running.");

        var settings = _settings.Current;

        if (settings.PauseOnFullscreen && NativeMethods.IsFullScreenOrPresenting())
            return Skip("A full-screen app or presentation is active.");

        // No point quizzing an empty chair: an unanswered prompt would be recorded as an
        // ignore and the card would be pushed back for nothing.
        if (settings.IdleThresholdMinutes > 0 &&
            NativeMethods.GetIdleTime() > TimeSpan.FromMinutes(settings.IdleThresholdMinutes))
            return Skip($"No user activity for over {settings.IdleThresholdMinutes} min.");

        var prompt = _prompts.TryCreatePrompt(DateTimeOffset.Now, out var reason);

        if (prompt is null)
        {
            PromptSkipped?.Invoke(this, new SkippedPrompt(Describe(reason)));
            return reason == PromptBlockReason.NothingDue ? RetryDelay : null;
        }

        PromptReady?.Invoke(this, prompt);
        return null;
    }

    /// <summary>
    /// Reports a suppressed prompt and asks for a short retry. Without this the OS-level
    /// checks would fail silently, and "why is it never showing anything?" would be
    /// impossible to answer from the log.
    /// </summary>
    private TimeSpan Skip(string reason)
    {
        PromptSkipped?.Invoke(this, new SkippedPrompt(reason));
        return RetryDelay;
    }

    private static string Describe(PromptBlockReason reason) => reason switch
    {
        PromptBlockReason.QuietHours => "Quiet hours.",
        PromptBlockReason.DailyLimitReached => "Daily prompt limit reached.",
        PromptBlockReason.Paused => "Prompts are paused.",
        PromptBlockReason.NothingDue => "No card is due yet.",
        _ => "Nothing to show.",
    };

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}

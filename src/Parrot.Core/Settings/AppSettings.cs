using Parrot.Core.Models;
using Parrot.Core.Practice;

namespace Parrot.Core.Settings;

public enum AppTheme { System = 0, Light = 1, Dark = 2 }

public sealed class AppSettings
{
    // ── When to interrupt ────────────────────────────────────────────────────

    /// <summary>Average gap between prompts.</summary>
    public int IntervalMinutes { get; set; } = 20;

    /// <summary>
    /// Random spread around the interval, in percent. A perfectly regular beat starts to
    /// feel like a metronome and is easier to tune out.
    /// </summary>
    public int JitterPercent { get; set; } = 25;

    /// <summary>Seconds the prompt stays on screen. Zero means "wait until I answer".</summary>
    public int DisplaySeconds { get; set; } = 20;

    /// <summary>Upper bound on prompts per day. Zero means unlimited.</summary>
    public int MaxPromptsPerDay { get; set; } = 60;

    public int MaxNewCardsPerDay { get; set; } = 10;

    /// <summary>Minutes an ignored card is held back before it can return.</summary>
    public int IgnoreCooldownMinutes { get; set; } = 15;

    /// <summary>Show the nearest card even when nothing is due yet.</summary>
    public bool AlwaysShowSomething { get; set; }

    // ── When to stay quiet ───────────────────────────────────────────────────

    public bool QuietHoursEnabled { get; set; } = true;
    public TimeSpan QuietFrom { get; set; } = TimeSpan.FromHours(22);
    public TimeSpan QuietTo { get; set; } = TimeSpan.FromHours(9);

    public List<DayOfWeek> ActiveDays { get; set; } =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    /// <summary>Skip the prompt if the keyboard and mouse have been still this long. Zero disables.</summary>
    public int IdleThresholdMinutes { get; set; } = 5;

    /// <summary>Don't interrupt games, video or presentations.</summary>
    public bool PauseOnFullscreen { get; set; } = true;

    // ── Where it appears ─────────────────────────────────────────────────────

    public ScreenAnchor Anchor { get; set; } = ScreenAnchor.BottomRight;
    public MonitorChoice Monitor { get; set; } = MonitorChoice.Primary;
    public int MarginX { get; set; } = 24;
    public int MarginY { get; set; } = 24;

    /// <summary>
    /// Give the answer box keyboard focus right away. Off by default: stealing focus from
    /// whatever you are typing in is the fastest way to make this app infuriating.
    /// </summary>
    public bool FocusInputOnShow { get; set; }

    // ── Learning ─────────────────────────────────────────────────────────────

    public TranslationDirection Direction { get; set; } = TranslationDirection.FrontToBack;
    public AnswerStrictness Strictness { get; set; } = AnswerStrictness.Lenient;

    // ── Practice ─────────────────────────────────────────────────────────────
    // The last session's choices, so "one more round" is a single click.

    public PracticeMode PracticeMode { get; set; } = PracticeMode.Flashcards;
    public PracticeScope PracticeScope { get; set; } = PracticeScope.Random;
    public int PracticeCount { get; set; } = 20;
    public TranslationDirection PracticeDirection { get; set; } = TranslationDirection.FrontToBack;

    /// <summary>Correct answers in a row a card needs before it counts as learned in a session.</summary>
    public int PracticeGoal { get; set; } = 1;

    /// <summary>Fastest full match board, in milliseconds. Zero until the first one.</summary>
    public int MatchBestMs { get; set; }

    /// <summary>Most right answers in one round of true or false.</summary>
    public int TrueFalseBest { get; set; }

    // ── Shell ────────────────────────────────────────────────────────────────

    public bool RunAtStartup { get; set; }
    public bool SoundEnabled { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// UI language code ("en", "uk", …). Empty — the default — follows the Windows display
    /// language; unknown codes fall back to English.
    /// </summary>
    public string UiLanguage { get; set; } = "";

    // ── Updates ──────────────────────────────────────────────────────────────

    /// <summary>Look for a new release in the background.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>A version the user closed the update card for; only a later one brings it back.</summary>
    public string? DismissedUpdateVersion { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>True when prompts are allowed at <paramref name="now"/>.</summary>
    public bool IsWithinActiveWindow(DateTimeOffset now)
    {
        if (!ActiveDays.Contains(now.DayOfWeek))
            return false;

        if (!QuietHoursEnabled || QuietFrom == QuietTo)
            return true;

        var time = now.TimeOfDay;

        // A window like 22:00 → 09:00 wraps past midnight, so the test flips.
        return QuietFrom < QuietTo
            ? time < QuietFrom || time >= QuietTo
            : time >= QuietTo && time < QuietFrom;
    }

    /// <summary>The next gap between prompts, with jitter applied.</summary>
    public TimeSpan NextDelay(Random random)
    {
        var baseMinutes = Math.Max(1, IntervalMinutes);
        var spread = baseMinutes * Math.Clamp(JitterPercent, 0, 90) / 100.0;
        var offset = (random.NextDouble() * 2 - 1) * spread;
        return TimeSpan.FromMinutes(Math.Max(0.5, baseMinutes + offset));
    }
}

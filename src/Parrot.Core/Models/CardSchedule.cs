namespace Parrot.Core.Models;

/// <summary>
/// The learning state of a card, kept separate from its text so that editing a typo
/// in the translation does not disturb hard-won progress (and "reset progress" is a
/// single-row delete).
/// </summary>
public sealed class CardSchedule
{
    public const double DefaultEase = 2.5;
    public const double MinEase = 1.3;
    public const double MaxEase = 2.8;
    public const int FirstIntervalMinutes = 10;

    public long CardId { get; set; }

    /// <summary>SM-2 ease factor: how fast the interval grows. Low ease = hard card.</summary>
    public double EaseFactor { get; set; } = DefaultEase;

    public double IntervalMinutes { get; set; } = FirstIntervalMinutes;

    /// <summary>Consecutive successful reviews. Reset to zero on a failure.</summary>
    public int Repetitions { get; set; }

    /// <summary>How many times a known card was forgotten. Drives the difficulty weight.</summary>
    public int Lapses { get; set; }

    /// <summary>When this card becomes eligible to be shown again.</summary>
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? LastShownAt { get; set; }

    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int IgnoredCount { get; set; }

    /// <summary>
    /// Ignores in a row. A card nobody ever engages with gets quietly pushed down the
    /// queue instead of nagging forever.
    /// </summary>
    public int ConsecutiveIgnores { get; set; }

    /// <summary>Current run of correct answers, for encouragement in the UI.</summary>
    public int Streak { get; set; }

    public bool IsNew => Repetitions == 0 && LastShownAt is null;

    /// <summary>A card is considered learned once its interval passes a week.</summary>
    public bool IsLearned => IntervalMinutes >= TimeSpan.FromDays(7).TotalMinutes;

    public int TotalAnswers => CorrectCount + WrongCount;

    public double Accuracy => TotalAnswers == 0 ? 0 : (double)CorrectCount / TotalAnswers;
}

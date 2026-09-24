using Parrot.Core.Models;

namespace Parrot.Core.Scheduling;

/// <summary>
/// SM-2 spaced repetition, adapted to a minutes-scale clock: Parrot interrupts you every
/// few minutes rather than once a day, so a freshly failed card must come back soon.
/// </summary>
public sealed class SrsEngine(int ignoreCooldownMinutes = SrsEngine.DefaultIgnoreCooldownMinutes)
{
    public const int DefaultIgnoreCooldownMinutes = 15;

    /// <summary>Roughly half a year. Past this, re-testing is just noise.</summary>
    public static readonly double MaxIntervalMinutes = TimeSpan.FromDays(180).TotalMinutes;

    /// <summary>After this many ignores in a row, stop putting the card at the front of the queue.</summary>
    public const int IgnoreFatigueThreshold = 5;

    public int IgnoreCooldownMinutes { get; } = Math.Max(1, ignoreCooldownMinutes);

    /// <summary>
    /// Folds one review outcome into the card's schedule, in place.
    /// </summary>
    public void Apply(CardSchedule schedule, ReviewOutcome outcome, DateTimeOffset now)
    {
        schedule.LastShownAt = now;

        switch (outcome)
        {
            // An ignored prompt says nothing about whether the user knows the card — they
            // were simply busy. Ease, interval and repetitions are deliberately untouched;
            // we only push the card out far enough that it does not pop up again at once.
            case ReviewOutcome.Ignored:
            case ReviewOutcome.Timeout:
                schedule.IgnoredCount++;
                // The backoff is based on the ignores *before* this one, so the first
                // dismissal costs exactly the configured cooldown and no more.
                schedule.DueAt = now.AddMinutes(IgnoreCooldownMinutes * IgnoreBackoff(schedule.ConsecutiveIgnores));
                schedule.ConsecutiveIgnores++;
                return;

            case ReviewOutcome.Correct:
                schedule.ConsecutiveIgnores = 0;
                schedule.CorrectCount++;
                schedule.Streak++;
                schedule.Repetitions++;
                schedule.EaseFactor = Clamp(schedule.EaseFactor + 0.1, CardSchedule.MinEase, CardSchedule.MaxEase);
                schedule.IntervalMinutes = NextInterval(schedule.IntervalMinutes * schedule.EaseFactor);
                break;

            // Accepted, but the shaky spelling means the card has not earned faster growth.
            case ReviewOutcome.Typo:
                schedule.ConsecutiveIgnores = 0;
                schedule.CorrectCount++;
                schedule.Streak++;
                schedule.Repetitions++;
                schedule.IntervalMinutes = NextInterval(schedule.IntervalMinutes * 1.2);
                break;

            case ReviewOutcome.Wrong:
                schedule.ConsecutiveIgnores = 0;
                schedule.WrongCount++;
                schedule.Streak = 0;
                schedule.Lapses++;
                schedule.Repetitions = 0;
                schedule.EaseFactor = Clamp(schedule.EaseFactor - 0.2, CardSchedule.MinEase, CardSchedule.MaxEase);
                schedule.IntervalMinutes = CardSchedule.FirstIntervalMinutes;
                break;

            // Admitting you don't know is not the same as getting it wrong: the card comes
            // straight back, but we don't record a lapse against it.
            case ReviewOutcome.DontKnow:
                schedule.ConsecutiveIgnores = 0;
                schedule.WrongCount++;
                schedule.Streak = 0;
                schedule.Repetitions = 0;
                schedule.EaseFactor = Clamp(schedule.EaseFactor - 0.2, CardSchedule.MinEase, CardSchedule.MaxEase);
                schedule.IntervalMinutes = CardSchedule.FirstIntervalMinutes;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unhandled review outcome.");
        }

        schedule.DueAt = now.AddMinutes(schedule.IntervalMinutes);
    }

    /// <summary>
    /// A mistake made in a practice session. Drilling the same card five times in ten minutes
    /// says little about long-term memory, so correct practice answers leave the schedule alone —
    /// but a miss is real news: the card gets harder and comes back as a prompt soon.
    /// Counters, streaks and "last shown" stay untouched; they describe the prompts.
    /// </summary>
    public void ApplyPracticeMistake(CardSchedule schedule, DateTimeOffset now)
    {
        schedule.EaseFactor = Clamp(schedule.EaseFactor - 0.2, CardSchedule.MinEase, CardSchedule.MaxEase);
        schedule.Repetitions = 0;
        schedule.IntervalMinutes = CardSchedule.FirstIntervalMinutes;

        var soon = now.AddMinutes(CardSchedule.FirstIntervalMinutes);
        if (schedule.DueAt > soon)
            schedule.DueAt = soon;
    }

    /// <summary>
    /// How strongly this card should compete for the next prompt. Hard cards (low ease,
    /// many lapses) and long-overdue cards win more often, but every due card keeps a
    /// non-zero share so the rotation never becomes predictable.
    /// </summary>
    public double Weight(CardSchedule schedule, DateTimeOffset now)
    {
        var difficulty = CardSchedule.MaxEase + 0.1 - schedule.EaseFactor; // 0.1 … 1.6
        var lapsePenalty = schedule.Lapses * 0.3;

        var overdueHours = Math.Max(0, (now - schedule.DueAt).TotalHours);
        var urgency = Math.Min(overdueHours * 0.05, 2.0);

        var weight = difficulty + lapsePenalty + urgency;

        // Someone who keeps dismissing this card is telling us something, even if it isn't
        // "I know it". Back off instead of nagging.
        if (schedule.ConsecutiveIgnores >= IgnoreFatigueThreshold)
            weight *= 0.25;

        return Math.Max(weight, 0.05);
    }

    private static double IgnoreBackoff(int previousIgnores) =>
        Math.Min(1 + previousIgnores * 0.5, 6);

    private static double NextInterval(double minutes) =>
        Math.Min(Math.Max(minutes, CardSchedule.FirstIntervalMinutes), MaxIntervalMinutes);

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), max);
}

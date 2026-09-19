using Parrot.Core.Models;

namespace Parrot.Core.Statistics;

/// <summary>Prompt outcomes over some stretch of time.</summary>
public sealed record PeriodStats(int Shown, int Correct, int Missed, int Ignored)
{
    /// <summary>Prompts the user actually engaged with. Ignored ones say nothing about knowledge.</summary>
    public int Answered => Correct + Missed;

    /// <summary>Null when nothing was answered — "0%" would falsely read as "got everything wrong".</summary>
    public double? Accuracy => Answered == 0 ? null : (double)Correct / Answered;
}

public sealed record DailyActivity(DateOnly Day, int Correct, int Missed, int Ignored)
{
    public int Total => Correct + Missed + Ignored;
}

public sealed record DifficultyBucket(string Label, int MinPercent, int MaxPercent, int Count);

public sealed record StatisticsReport
{
    public required PeriodStats Today { get; init; }
    public required PeriodStats Last7Days { get; init; }
    public required PeriodStats Last30Days { get; init; }
    public required PeriodStats AllTime { get; init; }

    /// <summary>Days in a row, up to today, with at least one answered prompt.</summary>
    public required int CurrentStreak { get; init; }
    public required int BestStreak { get; init; }

    public required int TotalCards { get; init; }
    public required int NewCards { get; init; }
    public required int LearnedCards { get; init; }
    public required int SuspendedCards { get; init; }

    /// <summary>One entry per calendar day, oldest first, ending today.</summary>
    public required IReadOnlyList<DailyActivity> Daily { get; init; }

    /// <summary>Cards already seen, grouped by how hard they currently are.</summary>
    public required IReadOnlyList<DifficultyBucket> Difficulty { get; init; }

    /// <summary>Cards the user has missed, hardest first.</summary>
    public required IReadOnlyList<Card> Hardest { get; init; }
}

/// <summary>
/// Turns raw history into the numbers on the statistics screen. Pure — no database, no clock —
/// so every figure can be pinned down by a test.
/// </summary>
public static class StatisticsCalculator
{
    public const int DailyWindow = 30;
    public const int HardestCount = 10;

    private static readonly (string Label, int Min, int Max)[] Buckets =
    [
        ("Легкі", 0, 25),
        ("Середні", 25, 50),
        ("Складні", 50, 75),
        ("Дуже складні", 75, 101),
    ];

    /// <param name="cards">Library cards, without the deleted ones.</param>
    /// <param name="reviews">Review history; any order.</param>
    /// <param name="now">The moment "today" is measured from, in the user's local offset.</param>
    public static StatisticsReport Calculate(IReadOnlyCollection<Card> cards, IReadOnlyCollection<ReviewLog> reviews, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        var seen = cards.Where(c => !c.Schedule.IsNew).ToList();
        var activeDays = reviews
            .Where(r => IsAnswered(r.Outcome))
            .Select(r => LocalDay(r.ShownAt))
            .ToHashSet();

        return new StatisticsReport
        {
            Today = Summarize(reviews, from: today),
            Last7Days = Summarize(reviews, from: today.AddDays(-6)),
            Last30Days = Summarize(reviews, from: today.AddDays(-(DailyWindow - 1))),
            AllTime = Summarize(reviews, from: DateOnly.MinValue),

            CurrentStreak = CurrentStreak(activeDays, today),
            BestStreak = BestStreak(activeDays),

            TotalCards = cards.Count,
            NewCards = cards.Count(c => c.Schedule.IsNew),
            LearnedCards = cards.Count(c => c.Schedule.IsLearned),
            SuspendedCards = cards.Count(c => c.IsSuspended),

            Daily = Daily(reviews, today),

            Difficulty = Buckets
                .Select(b => new DifficultyBucket(b.Label, b.Min, b.Max,
                    seen.Count(c => c.Schedule.DifficultyPercent >= b.Min && c.Schedule.DifficultyPercent < b.Max)))
                .ToList(),

            Hardest = seen
                .Where(c => c.Schedule.WrongCount > 0)
                .OrderByDescending(c => c.Schedule.DifficultyPercent)
                .ThenByDescending(c => c.Schedule.Lapses)
                .ThenBy(c => c.Schedule.Accuracy)
                .Take(HardestCount)
                .ToList(),
        };
    }

    public static bool IsAnswered(ReviewOutcome outcome) =>
        outcome is not (ReviewOutcome.Ignored or ReviewOutcome.Timeout);

    public static bool IsCorrect(ReviewOutcome outcome) =>
        outcome is ReviewOutcome.Correct or ReviewOutcome.Typo;

    private static PeriodStats Summarize(IEnumerable<ReviewLog> reviews, DateOnly from)
    {
        int shown = 0, correct = 0, missed = 0, ignored = 0;

        foreach (var review in reviews)
        {
            if (LocalDay(review.ShownAt) < from)
                continue;

            shown++;

            if (IsCorrect(review.Outcome)) correct++;
            else if (IsAnswered(review.Outcome)) missed++;
            else ignored++;
        }

        return new PeriodStats(shown, correct, missed, ignored);
    }

    private static List<DailyActivity> Daily(IEnumerable<ReviewLog> reviews, DateOnly today)
    {
        var first = today.AddDays(-(DailyWindow - 1));

        var byDay = reviews
            .Select(r => (Day: LocalDay(r.ShownAt), r.Outcome))
            .Where(r => r.Day >= first && r.Day <= today)
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Outcome).ToList());

        return Enumerable.Range(0, DailyWindow)
            .Select(i => first.AddDays(i))
            .Select(day =>
            {
                var outcomes = byDay.GetValueOrDefault(day) ?? [];
                return new DailyActivity(day,
                    Correct: outcomes.Count(IsCorrect),
                    Missed: outcomes.Count(o => IsAnswered(o) && !IsCorrect(o)),
                    Ignored: outcomes.Count(o => !IsAnswered(o)));
            })
            .ToList();
    }

    /// <summary>
    /// A streak is not broken until a whole day passes without practice, so a morning with
    /// no prompts yet still shows yesterday's run.
    /// </summary>
    private static int CurrentStreak(HashSet<DateOnly> activeDays, DateOnly today)
    {
        var day = activeDays.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;

        while (activeDays.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }

    private static int BestStreak(HashSet<DateOnly> activeDays)
    {
        var best = 0;

        foreach (var day in activeDays)
        {
            // Only count from the first day of each run, so the whole thing stays linear.
            if (activeDays.Contains(day.AddDays(-1)))
                continue;

            var length = 1;
            while (activeDays.Contains(day.AddDays(length)))
                length++;

            best = Math.Max(best, length);
        }

        return best;
    }

    private static DateOnly LocalDay(DateTimeOffset moment) => DateOnly.FromDateTime(moment.LocalDateTime);
}

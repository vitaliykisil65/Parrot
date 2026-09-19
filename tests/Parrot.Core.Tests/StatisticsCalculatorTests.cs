using Parrot.Core.Models;
using Parrot.Core.Statistics;

namespace Parrot.Core.Tests;

public class StatisticsCalculatorTests
{
    // Midday, so "n days ago" never slips across midnight whatever the local time zone.
    private static readonly DateTimeOffset Now = new(DateTime.Today.AddHours(12));

    private static ReviewLog Review(ReviewOutcome outcome, int daysAgo = 0, long cardId = 1) => new()
    {
        CardId = cardId,
        ShownAt = Now.AddDays(-daysAgo),
        Outcome = outcome,
    };

    private static Card Card(double ease = CardSchedule.DefaultEase, int reps = 1, int wrong = 0, int correct = 0,
        int lapses = 0, double intervalMinutes = 10, bool suspended = false, string front = "x") => new()
    {
        Front = front,
        IsSuspended = suspended,
        Schedule = new CardSchedule
        {
            EaseFactor = ease,
            Repetitions = reps,
            LastShownAt = reps > 0 || wrong > 0 ? Now : null,
            WrongCount = wrong,
            CorrectCount = correct,
            Lapses = lapses,
            IntervalMinutes = intervalMinutes,
        },
    };

    private static StatisticsReport Calculate(IReadOnlyCollection<ReviewLog> reviews, IReadOnlyCollection<Card>? cards = null) =>
        StatisticsCalculator.Calculate(cards ?? [], reviews, Now);

    [Fact]
    public void Typos_count_as_correct_and_ignores_do_not_affect_accuracy()
    {
        var report = Calculate(
        [
            Review(ReviewOutcome.Correct),
            Review(ReviewOutcome.Typo),
            Review(ReviewOutcome.Wrong),
            Review(ReviewOutcome.DontKnow),
            Review(ReviewOutcome.Ignored),
            Review(ReviewOutcome.Timeout),
        ]);

        Assert.Equal(6, report.Today.Shown);
        Assert.Equal(2, report.Today.Correct);
        Assert.Equal(2, report.Today.Missed);
        Assert.Equal(2, report.Today.Ignored);
        Assert.Equal(0.5, report.Today.Accuracy);
    }

    [Fact]
    public void Accuracy_is_unknown_rather_than_zero_when_nothing_was_answered()
    {
        var report = Calculate([Review(ReviewOutcome.Ignored)]);

        Assert.Null(report.Today.Accuracy);
        Assert.Null(report.Last7Days.Accuracy);
    }

    [Fact]
    public void Periods_include_exactly_their_days()
    {
        var report = Calculate(
        [
            Review(ReviewOutcome.Correct, daysAgo: 0),
            Review(ReviewOutcome.Correct, daysAgo: 6),
            Review(ReviewOutcome.Correct, daysAgo: 7),
            Review(ReviewOutcome.Correct, daysAgo: 29),
            Review(ReviewOutcome.Correct, daysAgo: 30),
        ]);

        Assert.Equal(1, report.Today.Shown);
        Assert.Equal(2, report.Last7Days.Shown);
        Assert.Equal(4, report.Last30Days.Shown);
        Assert.Equal(5, report.AllTime.Shown);
    }

    [Fact]
    public void Daily_activity_covers_thirty_days_ending_today()
    {
        var report = Calculate(
        [
            Review(ReviewOutcome.Correct, daysAgo: 2),
            Review(ReviewOutcome.Wrong, daysAgo: 2),
            Review(ReviewOutcome.Timeout, daysAgo: 2),
            Review(ReviewOutcome.Correct, daysAgo: 45),
        ]);

        Assert.Equal(StatisticsCalculator.DailyWindow, report.Daily.Count);
        Assert.Equal(DateOnly.FromDateTime(Now.LocalDateTime), report.Daily[^1].Day);

        var day = report.Daily[^3];
        Assert.Equal((1, 1, 1), (day.Correct, day.Missed, day.Ignored));
        Assert.Equal(3, report.Daily.Sum(d => d.Total));
    }

    [Fact]
    public void Streak_counts_consecutive_days_up_to_today()
    {
        var report = Calculate(
        [
            Review(ReviewOutcome.Correct, daysAgo: 0),
            Review(ReviewOutcome.Wrong, daysAgo: 1),
            Review(ReviewOutcome.Correct, daysAgo: 2),
            Review(ReviewOutcome.Correct, daysAgo: 4),
        ]);

        Assert.Equal(3, report.CurrentStreak);
    }

    [Fact]
    public void Streak_survives_until_today_is_over()
    {
        var report = Calculate(
        [
            Review(ReviewOutcome.Correct, daysAgo: 1),
            Review(ReviewOutcome.Correct, daysAgo: 2),
        ]);

        Assert.Equal(2, report.CurrentStreak);
    }

    [Fact]
    public void Streak_breaks_after_a_missed_day()
    {
        var report = Calculate([Review(ReviewOutcome.Correct, daysAgo: 2)]);

        Assert.Equal(0, report.CurrentStreak);
        Assert.Equal(1, report.BestStreak);
    }

    [Fact]
    public void Ignoring_every_prompt_does_not_keep_a_streak_alive()
    {
        var report = Calculate(
        [
            Review(ReviewOutcome.Correct, daysAgo: 1),
            Review(ReviewOutcome.Ignored, daysAgo: 0),
            Review(ReviewOutcome.Timeout, daysAgo: 2),
        ]);

        Assert.Equal(1, report.CurrentStreak);
    }

    [Fact]
    public void Best_streak_finds_the_longest_run_in_history()
    {
        var reviews = new List<ReviewLog>();
        foreach (var daysAgo in new[] { 0, 1, 10, 11, 12, 13, 14, 20 })
            reviews.Add(Review(ReviewOutcome.Correct, daysAgo));

        var report = Calculate(reviews);

        Assert.Equal(2, report.CurrentStreak);
        Assert.Equal(5, report.BestStreak);
    }

    [Fact]
    public void Card_counts_reflect_the_library()
    {
        var report = Calculate([],
        [
            Card(reps: 0),
            Card(reps: 0, suspended: true),
            Card(reps: 3, intervalMinutes: TimeSpan.FromDays(8).TotalMinutes),
            Card(reps: 1),
        ]);

        Assert.Equal(4, report.TotalCards);
        Assert.Equal(2, report.NewCards);
        Assert.Equal(1, report.LearnedCards);
        Assert.Equal(1, report.SuspendedCards);
    }

    [Fact]
    public void Difficulty_distribution_ignores_unseen_cards()
    {
        var report = Calculate([],
        [
            Card(reps: 0),                          // new — not counted
            Card(ease: CardSchedule.MaxEase),       // 0%
            Card(ease: CardSchedule.DefaultEase),   // 20%
            Card(ease: 1.9),                        // 60%
            Card(ease: CardSchedule.MinEase),       // 100%
        ]);

        Assert.Equal([2, 0, 1, 1], report.Difficulty.Select(b => b.Count));
    }

    [Fact]
    public void Hardest_cards_are_those_missed_ordered_by_difficulty()
    {
        var report = Calculate([],
        [
            Card(front: "never missed", ease: CardSchedule.MinEase, correct: 5),
            Card(front: "medium", ease: 2.0, wrong: 1, correct: 3),
            Card(front: "hardest", ease: CardSchedule.MinEase, wrong: 4, lapses: 3),
            Card(front: "hard, fewer lapses", ease: CardSchedule.MinEase, wrong: 2, lapses: 1),
        ]);

        Assert.Equal(["hardest", "hard, fewer lapses", "medium"], report.Hardest.Select(c => c.Front));
    }
}

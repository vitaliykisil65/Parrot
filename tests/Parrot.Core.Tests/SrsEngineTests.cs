using Parrot.Core.Models;
using Parrot.Core.Scheduling;

namespace Parrot.Core.Tests;

public class SrsEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly SrsEngine _srs = new(ignoreCooldownMinutes: 15);

    [Fact]
    public void Correct_answer_grows_ease_and_interval()
    {
        var schedule = new CardSchedule();

        _srs.Apply(schedule, ReviewOutcome.Correct, Now);

        Assert.Equal(1, schedule.Repetitions);
        Assert.Equal(1, schedule.CorrectCount);
        Assert.Equal(1, schedule.Streak);
        Assert.Equal(2.6, schedule.EaseFactor, 3);
        Assert.Equal(26, schedule.IntervalMinutes, 3); // 10 * 2.6
        Assert.Equal(Now.AddMinutes(26), schedule.DueAt);
    }

    [Fact]
    public void Ease_factor_is_capped()
    {
        var schedule = new CardSchedule();

        for (var i = 0; i < 20; i++)
            _srs.Apply(schedule, ReviewOutcome.Correct, Now);

        Assert.Equal(CardSchedule.MaxEase, schedule.EaseFactor, 3);
        Assert.True(schedule.IntervalMinutes <= SrsEngine.MaxIntervalMinutes);
    }

    [Fact]
    public void Typo_is_accepted_but_does_not_raise_ease()
    {
        var schedule = new CardSchedule { EaseFactor = 2.5, IntervalMinutes = 100 };

        _srs.Apply(schedule, ReviewOutcome.Typo, Now);

        Assert.Equal(2.5, schedule.EaseFactor, 3);
        Assert.Equal(120, schedule.IntervalMinutes, 3);
        Assert.Equal(1, schedule.CorrectCount);
    }

    [Fact]
    public void Wrong_answer_resets_interval_and_records_a_lapse()
    {
        var schedule = new CardSchedule { EaseFactor = 2.5, IntervalMinutes = 4000, Repetitions = 6, Streak = 6 };

        _srs.Apply(schedule, ReviewOutcome.Wrong, Now);

        Assert.Equal(0, schedule.Repetitions);
        Assert.Equal(0, schedule.Streak);
        Assert.Equal(1, schedule.Lapses);
        Assert.Equal(1, schedule.WrongCount);
        Assert.Equal(2.3, schedule.EaseFactor, 3);
        Assert.Equal(CardSchedule.FirstIntervalMinutes, schedule.IntervalMinutes, 3);
    }

    [Fact]
    public void DontKnow_costs_ease_but_not_a_lapse()
    {
        var schedule = new CardSchedule { EaseFactor = 2.5, IntervalMinutes = 4000, Repetitions = 6 };

        _srs.Apply(schedule, ReviewOutcome.DontKnow, Now);

        Assert.Equal(0, schedule.Lapses);
        Assert.Equal(2.3, schedule.EaseFactor, 3);
        Assert.Equal(CardSchedule.FirstIntervalMinutes, schedule.IntervalMinutes, 3);
    }

    [Theory]
    [InlineData(ReviewOutcome.Ignored)]
    [InlineData(ReviewOutcome.Timeout)]
    public void Ignoring_a_prompt_leaves_the_rating_untouched(ReviewOutcome outcome)
    {
        var schedule = new CardSchedule
        {
            EaseFactor = 1.9,
            IntervalMinutes = 640,
            Repetitions = 4,
            Lapses = 2,
            Streak = 4,
        };

        _srs.Apply(schedule, outcome, Now);

        Assert.Equal(1.9, schedule.EaseFactor, 3);
        Assert.Equal(640, schedule.IntervalMinutes, 3);
        Assert.Equal(4, schedule.Repetitions);
        Assert.Equal(2, schedule.Lapses);
        Assert.Equal(4, schedule.Streak);

        // The only effect is a short cooldown, so the same card is not shown again at once.
        Assert.Equal(1, schedule.IgnoredCount);
        Assert.Equal(Now.AddMinutes(15), schedule.DueAt);
    }

    [Fact]
    public void Repeated_ignores_back_off_further_each_time()
    {
        var schedule = new CardSchedule();

        _srs.Apply(schedule, ReviewOutcome.Ignored, Now);
        var firstGap = schedule.DueAt - Now;

        _srs.Apply(schedule, ReviewOutcome.Ignored, Now);
        var secondGap = schedule.DueAt - Now;

        Assert.True(secondGap > firstGap);
        Assert.Equal(2, schedule.ConsecutiveIgnores);
    }

    [Fact]
    public void Answering_clears_the_ignore_streak()
    {
        var schedule = new CardSchedule { ConsecutiveIgnores = 3 };

        _srs.Apply(schedule, ReviewOutcome.Correct, Now);

        Assert.Equal(0, schedule.ConsecutiveIgnores);
    }

    [Fact]
    public void Harder_cards_outweigh_easier_ones()
    {
        var hard = new CardSchedule { EaseFactor = 1.4, Lapses = 3, DueAt = Now };
        var easy = new CardSchedule { EaseFactor = 2.8, Lapses = 0, DueAt = Now };

        Assert.True(_srs.Weight(hard, Now) > _srs.Weight(easy, Now));
    }

    [Fact]
    public void Overdue_cards_gain_weight()
    {
        var fresh = new CardSchedule { DueAt = Now };
        var stale = new CardSchedule { DueAt = Now.AddHours(-10) };

        Assert.True(_srs.Weight(stale, Now) > _srs.Weight(fresh, Now));
    }

    [Fact]
    public void A_card_the_user_keeps_dismissing_is_pushed_down_the_queue()
    {
        var engaged = new CardSchedule { EaseFactor = 1.5, DueAt = Now };
        var dismissed = new CardSchedule { EaseFactor = 1.5, DueAt = Now, ConsecutiveIgnores = SrsEngine.IgnoreFatigueThreshold };

        Assert.True(_srs.Weight(dismissed, Now) < _srs.Weight(engaged, Now));
        Assert.True(_srs.Weight(dismissed, Now) > 0);
    }
}

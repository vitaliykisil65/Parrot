using Parrot.Core.Models;
using Parrot.Core.Scheduling;

namespace Parrot.Core.Tests;

public class CardPickerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly CardPicker _picker = new(new SrsEngine(), new Random(Seed: 42));

    private static Card Card(long id, DateTimeOffset due, double ease = 2.5, bool suspended = false) => new()
    {
        Id = id,
        Front = $"card {id}",
        Back = $"картка {id}",
        IsSuspended = suspended,
        Schedule = new CardSchedule { CardId = id, DueAt = due, EaseFactor = ease, Repetitions = 1, LastShownAt = due },
    };

    [Fact]
    public void Nothing_due_means_no_prompt()
    {
        var cards = new[] { Card(1, Now.AddHours(3)) };

        Assert.Null(_picker.Pick(cards, Now, new PickOptions()));
    }

    [Fact]
    public void AlwaysShowSomething_falls_back_to_the_nearest_card()
    {
        var cards = new[] { Card(1, Now.AddHours(5)), Card(2, Now.AddHours(2)) };

        var picked = _picker.Pick(cards, Now, new PickOptions { AlwaysShowSomething = true });

        Assert.Equal(2, picked?.Id);
    }

    [Fact]
    public void Suspended_and_deleted_cards_are_never_picked()
    {
        var deleted = Card(2, Now.AddMinutes(-5));
        deleted.DeletedAt = Now;

        var cards = new[] { Card(1, Now.AddMinutes(-5), suspended: true), deleted };

        Assert.Null(_picker.Pick(cards, Now, new PickOptions { AlwaysShowSomething = true }));
    }

    [Fact]
    public void New_cards_stop_appearing_once_the_daily_quota_is_used_up()
    {
        var fresh = new Card
        {
            Id = 1,
            Front = "new",
            Back = "нова",
            Schedule = new CardSchedule { CardId = 1, DueAt = Now.AddMinutes(-1) },
        };

        var atQuota = new PickOptions { MaxNewPerDay = 5, NewIntroducedToday = 5 };
        Assert.Null(_picker.Pick([fresh], Now, atQuota));

        var underQuota = new PickOptions { MaxNewPerDay = 5, NewIntroducedToday = 4 };
        Assert.Equal(1, _picker.Pick([fresh], Now, underQuota)?.Id);
    }

    [Fact]
    public void Hard_cards_come_up_more_often_than_easy_ones()
    {
        var cards = new[] { Card(1, Now.AddMinutes(-1), ease: 1.3), Card(2, Now.AddMinutes(-1), ease: 2.8) };

        var hits = new Dictionary<long, int> { [1] = 0, [2] = 0 };

        for (var i = 0; i < 2000; i++)
            hits[_picker.Pick(cards, Now, new PickOptions())!.Id]++;

        Assert.True(hits[1] > hits[2]);

        // ...but the easy card is still shown sometimes, so the rotation stays unpredictable.
        Assert.True(hits[2] > 100);
    }

    [Fact]
    public void Random_direction_resolves_to_a_concrete_one()
    {
        var resolved = _picker.ResolveDirection(TranslationDirection.Random);

        Assert.NotEqual(TranslationDirection.Random, resolved);
    }

    [Fact]
    public void A_fixed_direction_is_passed_through()
    {
        Assert.Equal(TranslationDirection.BackToFront, _picker.ResolveDirection(TranslationDirection.BackToFront));
    }
}

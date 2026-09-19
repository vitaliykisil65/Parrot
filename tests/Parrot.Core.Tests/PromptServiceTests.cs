using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Scheduling;
using Parrot.Core.Settings;

namespace Parrot.Core.Tests;

public class PromptServiceTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"parrot-{Guid.NewGuid():N}.json");
    private readonly SettingsService _settings;
    private readonly PromptService _service;
    private readonly long _deckId;

    public PromptServiceTests()
    {
        _settings = new SettingsService(_settingsPath);
        _service = new PromptService(_db.Repository, _settings, new SrsEngine(), new CardPicker(new SrsEngine(), new Random(1)));
        _deckId = _db.SeedDeck();
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_settingsPath);
    }

    private Card AddDueCard(string front = "deadline", string back = "кінцевий термін")
    {
        var id = _db.Repository.AddCard(new Card { DeckId = _deckId, Front = front, Back = back });
        var card = _db.Repository.GetCard(id)!;
        card.Schedule.DueAt = DateTimeOffset.Now.AddMinutes(-1);
        _db.Repository.SaveSchedule(card.Schedule);
        return card;
    }

    private void Configure(Action<AppSettings> change)
    {
        var updated = _settings.Current.Clone();
        change(updated);
        _settings.Save(updated);
    }

    [Fact]
    public void A_due_card_produces_a_prompt()
    {
        var card = AddDueCard();
        Configure(s => s.QuietHoursEnabled = false);

        var prompt = _service.TryCreatePrompt(DateTimeOffset.Now, out var reason);

        Assert.Equal(PromptBlockReason.None, reason);
        Assert.Equal(card.Id, prompt?.Card.Id);
        Assert.Equal("deadline", prompt?.Question);
        Assert.Equal("кінцевий термін", prompt?.Answer);
    }

    [Fact]
    public void Reverse_direction_swaps_question_and_answer()
    {
        AddDueCard();
        Configure(s =>
        {
            s.QuietHoursEnabled = false;
            s.Direction = TranslationDirection.BackToFront;
        });

        var prompt = _service.TryCreatePrompt(DateTimeOffset.Now, out _)!;

        Assert.Equal("кінцевий термін", prompt.Question);
        Assert.Equal("deadline", prompt.Answer);
    }

    [Fact]
    public void Quiet_hours_block_the_prompt()
    {
        AddDueCard();
        Configure(s =>
        {
            s.QuietHoursEnabled = true;
            s.QuietFrom = TimeSpan.Zero;
            s.QuietTo = TimeSpan.FromHours(23.99);
        });

        Assert.Null(_service.TryCreatePrompt(DateTimeOffset.Now, out var reason));
        Assert.Equal(PromptBlockReason.QuietHours, reason);
    }

    [Fact]
    public void Pausing_blocks_the_prompt_until_it_expires()
    {
        AddDueCard();
        Configure(s => s.QuietHoursEnabled = false);

        _service.Pause(TimeSpan.FromMinutes(30));
        Assert.Null(_service.TryCreatePrompt(DateTimeOffset.Now, out var reason));
        Assert.Equal(PromptBlockReason.Paused, reason);

        _service.Resume();
        Assert.NotNull(_service.TryCreatePrompt(DateTimeOffset.Now, out _));
    }

    [Fact]
    public void The_daily_prompt_limit_is_respected()
    {
        var card = AddDueCard();
        Configure(s =>
        {
            s.QuietHoursEnabled = false;
            s.MaxPromptsPerDay = 2;
        });

        for (var i = 0; i < 2; i++)
            _db.Repository.LogReview(new ReviewLog { CardId = card.Id, ShownAt = DateTimeOffset.Now, Outcome = ReviewOutcome.Correct });

        Assert.Null(_service.TryCreatePrompt(DateTimeOffset.Now, out var reason));
        Assert.Equal(PromptBlockReason.DailyLimitReached, reason);
    }

    [Fact]
    public void Nothing_due_is_reported_separately_from_being_blocked()
    {
        _db.Repository.AddCard(new Card { DeckId = _deckId, Front = "scope", Back = "обсяг" });
        Configure(s =>
        {
            s.QuietHoursEnabled = false;
            s.MaxNewCardsPerDay = 0; // the only card is new, so nothing is eligible
        });

        Assert.Null(_service.TryCreatePrompt(DateTimeOffset.Now, out var reason));
        Assert.Equal(PromptBlockReason.NothingDue, reason);
    }

    [Fact]
    public void Recording_an_answer_persists_both_schedule_and_history()
    {
        var card = AddDueCard();
        Configure(s => s.QuietHoursEnabled = false);

        var prompt = _service.TryCreatePrompt(DateTimeOffset.Now, out _)!;
        var now = DateTimeOffset.Now;

        _service.Record(prompt, ReviewOutcome.Correct, "кінцевий термін", now);

        var reloaded = _db.Repository.GetCard(card.Id)!.Schedule;
        Assert.Equal(1, reloaded.CorrectCount);
        Assert.True(reloaded.DueAt > now);

        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        Assert.Equal(1, _db.Repository.CountPromptsSince(startOfDay));
    }

    [Fact]
    public void Ignoring_a_prompt_is_recorded_without_touching_the_rating()
    {
        var card = AddDueCard();
        Configure(s => s.QuietHoursEnabled = false);

        var before = _db.Repository.GetCard(card.Id)!.Schedule;
        var prompt = _service.TryCreatePrompt(DateTimeOffset.Now, out _)!;

        _service.Record(prompt, ReviewOutcome.Ignored, null, DateTimeOffset.Now);

        var after = _db.Repository.GetCard(card.Id)!.Schedule;
        Assert.Equal(before.EaseFactor, after.EaseFactor, 3);
        Assert.Equal(before.IntervalMinutes, after.IntervalMinutes, 3);
        Assert.Equal(1, after.IgnoredCount);
    }

    [Fact]
    public void The_checker_follows_the_configured_strictness()
    {
        Configure(s => s.Strictness = AnswerStrictness.Strict);

        Assert.Equal(AnswerStrictness.Strict, _service.CreateChecker().Strictness);
    }

    [Fact]
    public void Asking_for_the_front_side_accepts_fronts_of_cards_with_the_same_translation()
    {
        AddDueCard("accurate", "точний");

        var synonymId = _db.Repository.AddCard(new Card { DeckId = _deckId, Front = "precise", Back = "точний" });
        var synonym = _db.Repository.GetCard(synonymId)!;
        synonym.Schedule.LastShownAt = DateTimeOffset.Now.AddDays(-1);
        synonym.Schedule.Repetitions = 1;
        synonym.Schedule.DueAt = DateTimeOffset.Now.AddDays(1);
        _db.Repository.SaveSchedule(synonym.Schedule);

        Configure(s =>
        {
            s.QuietHoursEnabled = false;
            s.Direction = TranslationDirection.BackToFront;
        });

        var prompt = _service.TryCreatePrompt(DateTimeOffset.Now, out _)!;

        Assert.Equal("accurate", prompt.Card.Front);
        Assert.Equal(["precise"], prompt.AlsoAccepted);
    }

    [Fact]
    public void Asking_for_the_translation_needs_no_synonyms()
    {
        AddDueCard("accurate", "точний");
        _db.Repository.AddCard(new Card { DeckId = _deckId, Front = "precise", Back = "точний" });
        Configure(s => s.QuietHoursEnabled = false);

        var prompt = _service.TryCreatePrompt(DateTimeOffset.Now, out _)!;

        Assert.Empty(prompt.AlsoAccepted);
    }
}

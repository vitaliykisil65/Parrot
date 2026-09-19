using Parrot.Core.Data;
using Parrot.Core.Models;

namespace Parrot.Core.Tests;

/// <summary>Gives every test its own throwaway database file.</summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"parrot-{Guid.NewGuid():N}.db");

    public TempDatabase()
    {
        Database = new Database(_path);
        Repository = new CardRepository(Database);
    }

    public Database Database { get; }
    public CardRepository Repository { get; }

    public long SeedDeck(string name = "Test") => Repository.AddDeck(new Deck { Name = name });

    public void Dispose()
    {
        Database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try { File.Delete(_path); } catch (IOException) { /* the OS will clean up %TEMP% */ }
    }
}

public class RepositoryTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly CardRepository _repo;
    private readonly long _deckId;

    public RepositoryTests()
    {
        _repo = _db.Repository;
        _deckId = _db.SeedDeck();
    }

    public void Dispose() => _db.Dispose();

    private Card AddCard(string front = "deadline", string back = "кінцевий термін") =>
        _repo.GetCard(_repo.AddCard(new Card { DeckId = _deckId, Front = front, Back = back }))!;

    [Fact]
    public void Adding_a_card_creates_its_schedule()
    {
        var card = AddCard();

        Assert.True(card.Id > 0);
        Assert.Equal(card.Id, card.Schedule.CardId);
        Assert.Equal(CardSchedule.DefaultEase, card.Schedule.EaseFactor, 3);
        Assert.True(card.Schedule.IsNew);
    }

    [Fact]
    public void Cyrillic_text_survives_a_round_trip()
    {
        var card = AddCard("to figure out", "зрозуміти; розібратися");

        Assert.Equal("зрозуміти; розібратися", card.Back);
        Assert.Equal(["зрозуміти", "розібратися"], card.AcceptedAnswers);
    }

    [Fact]
    public void Editing_a_card_leaves_its_progress_alone()
    {
        var card = AddCard();
        card.Schedule.EaseFactor = 1.7;
        card.Schedule.Repetitions = 5;
        _repo.SaveSchedule(card.Schedule);

        card.Back = "дедлайн";
        _repo.UpdateCard(card);

        var reloaded = _repo.GetCard(card.Id)!;
        Assert.Equal("дедлайн", reloaded.Back);
        Assert.Equal(1.7, reloaded.Schedule.EaseFactor, 3);
        Assert.Equal(5, reloaded.Schedule.Repetitions);
    }

    [Fact]
    public void Timestamps_survive_a_round_trip_to_the_second()
    {
        var card = AddCard();
        var due = DateTimeOffset.Now.AddMinutes(137);
        card.Schedule.DueAt = due;
        card.Schedule.LastShownAt = due.AddMinutes(-5);
        _repo.SaveSchedule(card.Schedule);

        var reloaded = _repo.GetCard(card.Id)!.Schedule;

        Assert.True((reloaded.DueAt - due).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.NotNull(reloaded.LastShownAt);
    }

    [Fact]
    public void Soft_deleted_cards_are_hidden_but_recoverable()
    {
        var card = AddCard();
        _repo.SoftDelete(card.Id);

        Assert.Empty(_repo.GetCards(CardQuery.All));
        Assert.Single(_repo.GetCards(CardQuery.All with { IncludeDeleted = true }));

        _repo.Restore(card.Id);
        Assert.Single(_repo.GetCards(CardQuery.All));
    }

    [Fact]
    public void Suspended_cards_stay_in_the_library_but_leave_the_prompt_queue()
    {
        var card = AddCard();
        _repo.SetSuspended(card.Id, true);

        Assert.Single(_repo.GetCards(CardQuery.All));
        Assert.Empty(_repo.GetCards(CardQuery.Promptable));
    }

    [Fact]
    public void Cards_of_an_inactive_deck_are_not_promptable()
    {
        AddCard();
        var deck = _repo.GetDecks().Single();
        deck.IsActive = false;
        _repo.UpdateDeck(deck);

        Assert.Empty(_repo.GetCards(CardQuery.Promptable));
        Assert.Single(_repo.GetCards(CardQuery.All));
    }

    [Fact]
    public void Search_matches_either_side_and_the_tags()
    {
        _repo.AddCard(new Card { DeckId = _deckId, Front = "bottleneck", Back = "вузьке місце", Tags = "noun" });
        _repo.AddCard(new Card { DeckId = _deckId, Front = "deadline", Back = "кінцевий термін", Tags = "noun" });

        Assert.Single(_repo.GetCards(CardQuery.All with { Search = "bottle" }));
        Assert.Single(_repo.GetCards(CardQuery.All with { Search = "кінцевий" }));
        Assert.Equal(2, _repo.GetCards(CardQuery.All with { Search = "noun" }).Count);
    }

    [Fact]
    public void Resetting_progress_returns_a_card_to_never_seen()
    {
        var card = AddCard();
        card.Schedule.EaseFactor = 1.4;
        card.Schedule.Repetitions = 9;
        card.Schedule.LastShownAt = DateTimeOffset.Now;
        _repo.SaveSchedule(card.Schedule);

        _repo.ResetProgress(card.Id);

        var reloaded = _repo.GetCard(card.Id)!.Schedule;
        Assert.Equal(CardSchedule.DefaultEase, reloaded.EaseFactor, 3);
        Assert.True(reloaded.IsNew);
    }

    [Fact]
    public void Deleting_a_deck_takes_its_cards_with_it()
    {
        AddCard();

        _repo.DeleteDeck(_deckId);

        Assert.Empty(_repo.GetDecks());
        Assert.Empty(_repo.GetCards(CardQuery.All with { IncludeDeleted = true }));
    }

    [Fact]
    public void Daily_counters_only_look_at_todays_history()
    {
        var card = AddCard();
        var now = DateTimeOffset.Now;

        _repo.LogReview(new ReviewLog { CardId = card.Id, ShownAt = now.AddDays(-2), Outcome = ReviewOutcome.Correct });
        _repo.LogReview(new ReviewLog { CardId = card.Id, ShownAt = now, Outcome = ReviewOutcome.Correct });

        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);

        Assert.Equal(1, _repo.CountPromptsSince(startOfDay));

        // The card was first seen two days ago, so it does not count against today's
        // new-card quota even though it was reviewed again today.
        Assert.Equal(0, _repo.CountNewCardsSince(startOfDay));
    }

    [Fact]
    public void A_card_seen_for_the_first_time_today_counts_as_new()
    {
        var card = AddCard();
        var now = DateTimeOffset.Now;
        _repo.LogReview(new ReviewLog { CardId = card.Id, ShownAt = now, Outcome = ReviewOutcome.Correct });

        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);

        Assert.Equal(1, _repo.CountNewCardsSince(startOfDay));
    }

    [Fact]
    public void Deck_card_count_ignores_deleted_cards()
    {
        var card = AddCard();
        AddCard("scope", "обсяг");
        _repo.SoftDelete(card.Id);

        Assert.Equal(1, _repo.GetDecks().Single().CardCount);
    }
}

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
    public void Review_history_round_trips_and_filters_by_date()
    {
        var card = AddCard();
        var now = DateTimeOffset.Now;

        _repo.LogReview(new ReviewLog { CardId = card.Id, ShownAt = now.AddDays(-3), Outcome = ReviewOutcome.Wrong });
        _repo.LogReview(new ReviewLog
        {
            CardId = card.Id,
            ShownAt = now.AddSeconds(-5),
            AnsweredAt = now,
            Outcome = ReviewOutcome.Typo,
            UserAnswer = "кінцевий термн",
            Direction = TranslationDirection.BackToFront,
            ResponseMs = 5000,
        });

        var all = _repo.GetReviews();
        Assert.Equal([ReviewOutcome.Wrong, ReviewOutcome.Typo], all.Select(r => r.Outcome));

        var recent = Assert.Single(_repo.GetReviews(since: now.AddDays(-1)));
        Assert.Equal("кінцевий термн", recent.UserAnswer);
        Assert.Equal(TranslationDirection.BackToFront, recent.Direction);
        Assert.Equal(5000, recent.ResponseMs);
        Assert.NotNull(recent.AnsweredAt);
    }

    [Fact]
    public void Deck_card_count_ignores_deleted_cards()
    {
        var card = AddCard();
        AddCard("scope", "обсяг");
        _repo.SoftDelete(card.Id);

        Assert.Equal(1, _repo.GetDecks().Single().CardCount);
    }

    [Fact]
    public void Transcription_and_kind_are_stored()
    {
        var id = _repo.AddCard(new Card
        {
            DeckId = _deckId, Front = "deadline", Back = "термін", Transcription = "/ˈdedlaɪn/", Kind = CardKind.Word,
        });

        var card = _repo.GetCard(id)!;
        Assert.Equal("/ˈdedlaɪn/", card.Transcription);
        Assert.Equal(CardKind.Word, card.Kind);

        card.Transcription = null;
        card.Kind = CardKind.Idiom;
        _repo.UpdateCard(card);

        var updated = _repo.GetCards(CardQuery.All).Single();
        Assert.Null(updated.Transcription);
        Assert.Equal(CardKind.Idiom, updated.Kind);
    }

    [Fact]
    public void Practice_answers_do_not_use_up_the_daily_limits()
    {
        var card = AddCard();
        var now = DateTimeOffset.Now;

        _repo.LogReview(new ReviewLog { CardId = card.Id, ShownAt = now, Outcome = ReviewOutcome.Correct, Source = ReviewSource.Practice });
        Assert.Equal(0, _repo.CountPromptsSince(now.AddMinutes(-1)));
        Assert.Equal(0, _repo.CountNewCardsSince(now.AddMinutes(-1)));

        _repo.LogReview(new ReviewLog { CardId = card.Id, ShownAt = now, Outcome = ReviewOutcome.Correct });
        Assert.Equal(1, _repo.CountPromptsSince(now.AddMinutes(-1)));
        Assert.Equal(1, _repo.CountNewCardsSince(now.AddMinutes(-1)));
    }
}

public class MigrationTests
{
    /// <summary>The schema as the first release created it.</summary>
    private const string Version1 = """
        CREATE TABLE Deck (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, FrontLang TEXT NOT NULL DEFAULT 'en',
            BackLang TEXT NOT NULL DEFAULT 'uk', IsActive INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL);
        CREATE TABLE Card (Id INTEGER PRIMARY KEY AUTOINCREMENT, DeckId INTEGER NOT NULL REFERENCES Deck(Id) ON DELETE CASCADE,
            Front TEXT NOT NULL, Back TEXT NOT NULL, Hint TEXT, Example TEXT, Tags TEXT, Notes TEXT,
            IsSuspended INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT);
        CREATE TABLE CardSchedule (CardId INTEGER PRIMARY KEY REFERENCES Card(Id) ON DELETE CASCADE, EaseFactor REAL NOT NULL,
            IntervalMinutes REAL NOT NULL, Repetitions INTEGER NOT NULL, Lapses INTEGER NOT NULL, DueAt TEXT NOT NULL,
            LastShownAt TEXT, CorrectCount INTEGER NOT NULL, WrongCount INTEGER NOT NULL, IgnoredCount INTEGER NOT NULL,
            ConsecutiveIgnores INTEGER NOT NULL, Streak INTEGER NOT NULL);
        CREATE TABLE ReviewLog (Id INTEGER PRIMARY KEY AUTOINCREMENT, CardId INTEGER NOT NULL REFERENCES Card(Id) ON DELETE CASCADE,
            ShownAt TEXT NOT NULL, AnsweredAt TEXT, Outcome INTEGER NOT NULL, UserAnswer TEXT, Direction INTEGER NOT NULL,
            ResponseMs INTEGER NOT NULL);
        INSERT INTO Deck (Name, CreatedAt) VALUES ('English', '2026-01-01T10:00:00.0000000+00:00');
        INSERT INTO Card (DeckId, Front, Back, CreatedAt, UpdatedAt)
            VALUES (1, 'deadline', 'термін', '2026-01-01T10:00:00.0000000+00:00', '2026-01-01T10:00:00.0000000+00:00');
        INSERT INTO CardSchedule VALUES (1, 2.5, 10, 0, 0, '2026-01-01T10:00:00.0000000+00:00', NULL, 0, 0, 0, 0, 0);
        INSERT INTO ReviewLog (CardId, ShownAt, Outcome, Direction, ResponseMs)
            VALUES (1, '2026-01-02T10:00:00.0000000+00:00', 0, 0, 1200);
        PRAGMA user_version = 1;
        """;

    [Fact]
    public void A_version_1_database_is_upgraded_in_place()
    {
        var path = Path.Combine(Path.GetTempPath(), $"parrot-v1-{Guid.NewGuid():N}.db");

        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = Version1;
                command.ExecuteNonQuery();
            }

            using var database = new Database(path);
            var repository = new CardRepository(database);

            var card = repository.GetCards(CardQuery.All).Single();
            Assert.Equal("deadline", card.Front);
            Assert.Null(card.Transcription);
            Assert.Equal(CardKind.None, card.Kind);

            var review = repository.GetReviews().Single();
            Assert.Equal(ReviewSource.Prompt, review.Source);
            Assert.Equal(1200, review.ResponseMs);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}

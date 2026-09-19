using Microsoft.Data.Sqlite;
using Parrot.Core.Models;

namespace Parrot.Core.Data;

public sealed class CardRepository(Database database)
{
    private const string CardColumns = """
        c.Id, c.DeckId, c.Front, c.Back, c.Hint, c.Example, c.Tags, c.Notes,
        c.IsSuspended, c.CreatedAt, c.UpdatedAt, c.DeletedAt,
        s.EaseFactor, s.IntervalMinutes, s.Repetitions, s.Lapses, s.DueAt, s.LastShownAt,
        s.CorrectCount, s.WrongCount, s.IgnoredCount, s.ConsecutiveIgnores, s.Streak
        """;

    // ── Decks ────────────────────────────────────────────────────────────────

    public List<Deck> GetDecks()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Id, d.Name, d.FrontLang, d.BackLang, d.IsActive, d.CreatedAt,
                   (SELECT COUNT(*) FROM Card c WHERE c.DeckId = d.Id AND c.DeletedAt IS NULL)
            FROM Deck d
            ORDER BY d.Name COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var decks = new List<Deck>();

        while (reader.Read())
        {
            decks.Add(new Deck
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                FrontLang = reader.GetString(2),
                BackLang = reader.GetString(3),
                IsActive = reader.GetBoolean(4),
                CreatedAt = SqlTime.From(reader.GetString(5)),
                CardCount = reader.GetInt32(6),
            });
        }

        return decks;
    }

    public long AddDeck(Deck deck)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Deck (Name, FrontLang, BackLang, IsActive, CreatedAt)
            VALUES ($name, $front, $back, $active, $created);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", deck.Name);
        command.Parameters.AddWithValue("$front", deck.FrontLang);
        command.Parameters.AddWithValue("$back", deck.BackLang);
        command.Parameters.AddWithValue("$active", deck.IsActive);
        command.Parameters.AddWithValue("$created", SqlTime.To(deck.CreatedAt));

        deck.Id = (long)command.ExecuteScalar()!;
        return deck.Id;
    }

    public void UpdateDeck(Deck deck)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Deck SET Name = $name, FrontLang = $front, BackLang = $back, IsActive = $active
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$name", deck.Name);
        command.Parameters.AddWithValue("$front", deck.FrontLang);
        command.Parameters.AddWithValue("$back", deck.BackLang);
        command.Parameters.AddWithValue("$active", deck.IsActive);
        command.Parameters.AddWithValue("$id", deck.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteDeck(long deckId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Deck WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", deckId);
        command.ExecuteNonQuery();
    }

    // ── Cards ────────────────────────────────────────────────────────────────

    public List<Card> GetCards(CardQuery query)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();

        var where = new List<string>();

        if (!query.IncludeDeleted)
            where.Add("c.DeletedAt IS NULL");

        if (!query.IncludeSuspended)
            where.Add("c.IsSuspended = 0");

        if (query.OnlyActiveDecks)
            where.Add("d.IsActive = 1");

        if (query.DeckId is { } deckId)
        {
            where.Add("c.DeckId = $deckId");
            command.Parameters.AddWithValue("$deckId", deckId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            where.Add("(c.Front LIKE $search OR c.Back LIKE $search OR IFNULL(c.Tags, '') LIKE $search)");
            command.Parameters.AddWithValue("$search", $"%{query.Search.Trim()}%");
        }

        var filter = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        command.CommandText = $"""
            SELECT {CardColumns}
            FROM Card c
            JOIN Deck d ON d.Id = c.DeckId
            JOIN CardSchedule s ON s.CardId = c.Id
            {filter}
            ORDER BY c.Front COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var cards = new List<Card>();

        while (reader.Read())
            cards.Add(ReadCard(reader));

        return cards;
    }

    public Card? GetCard(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {CardColumns}
            FROM Card c
            JOIN CardSchedule s ON s.CardId = c.Id
            WHERE c.Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCard(reader) : null;
    }

    public long AddCard(Card card)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Card (DeckId, Front, Back, Hint, Example, Tags, Notes, IsSuspended, CreatedAt, UpdatedAt, DeletedAt)
                VALUES ($deck, $front, $back, $hint, $example, $tags, $notes, $suspended, $created, $updated, NULL);
                SELECT last_insert_rowid();
                """;
            BindCardFields(command, card);
            command.Parameters.AddWithValue("$created", SqlTime.To(card.CreatedAt));
            command.Parameters.AddWithValue("$updated", SqlTime.To(card.UpdatedAt));

            card.Id = (long)command.ExecuteScalar()!;
        }

        card.Schedule.CardId = card.Id;
        InsertSchedule(connection, transaction, card.Schedule);

        transaction.Commit();
        return card.Id;
    }

    public void UpdateCard(Card card)
    {
        card.UpdatedAt = DateTimeOffset.Now;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Card SET DeckId = $deck, Front = $front, Back = $back, Hint = $hint,
                            Example = $example, Tags = $tags, Notes = $notes,
                            IsSuspended = $suspended, UpdatedAt = $updated
            WHERE Id = $id;
            """;
        BindCardFields(command, card);
        command.Parameters.AddWithValue("$updated", SqlTime.To(card.UpdatedAt));
        command.Parameters.AddWithValue("$id", card.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Hides a card without losing it, so a misclick can be taken back.</summary>
    public void SoftDelete(long cardId) => SetDeletedAt(cardId, DateTimeOffset.Now);

    public void Restore(long cardId) => SetDeletedAt(cardId, null);

    public void DeleteForever(long cardId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Card WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", cardId);
        command.ExecuteNonQuery();
    }

    public void SetSuspended(long cardId, bool suspended)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Card SET IsSuspended = $suspended, UpdatedAt = $updated WHERE Id = $id;";
        command.Parameters.AddWithValue("$suspended", suspended);
        command.Parameters.AddWithValue("$updated", SqlTime.To(DateTimeOffset.Now));
        command.Parameters.AddWithValue("$id", cardId);
        command.ExecuteNonQuery();
    }

    /// <summary>Wipes the learning history of a card, returning it to "never seen".</summary>
    public void ResetProgress(long cardId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM CardSchedule WHERE CardId = $id;";
            command.Parameters.AddWithValue("$id", cardId);
            command.ExecuteNonQuery();
        }

        InsertSchedule(connection, transaction, new CardSchedule { CardId = cardId });
        transaction.Commit();
    }

    // ── Scheduling and history ───────────────────────────────────────────────

    public void SaveSchedule(CardSchedule schedule)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CardSchedule
            SET EaseFactor = $ease, IntervalMinutes = $interval, Repetitions = $reps, Lapses = $lapses,
                DueAt = $due, LastShownAt = $lastShown, CorrectCount = $correct, WrongCount = $wrong,
                IgnoredCount = $ignored, ConsecutiveIgnores = $streakIgnored, Streak = $streak
            WHERE CardId = $id;
            """;
        BindScheduleFields(command, schedule);
        command.ExecuteNonQuery();
    }

    public long LogReview(ReviewLog log)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ReviewLog (CardId, ShownAt, AnsweredAt, Outcome, UserAnswer, Direction, ResponseMs)
            VALUES ($card, $shown, $answered, $outcome, $answer, $direction, $ms);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$card", log.CardId);
        command.Parameters.AddWithValue("$shown", SqlTime.To(log.ShownAt));
        command.Parameters.AddWithValue("$answered", (object?)SqlTime.ToNullable(log.AnsweredAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$outcome", (int)log.Outcome);
        command.Parameters.AddWithValue("$answer", (object?)log.UserAnswer ?? DBNull.Value);
        command.Parameters.AddWithValue("$direction", (int)log.Direction);
        command.Parameters.AddWithValue("$ms", log.ResponseMs);

        log.Id = (long)command.ExecuteScalar()!;
        return log.Id;
    }

    public int CountPromptsSince(DateTimeOffset since)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReviewLog WHERE ShownAt >= $since;";
        command.Parameters.AddWithValue("$since", SqlTime.To(since));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Distinct cards that were seen for the very first time since <paramref name="since"/> —
    /// this is what the "new cards per day" limit counts.
    /// </summary>
    public int CountNewCardsSince(DateTimeOffset since)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM (
                SELECT CardId, MIN(ShownAt) AS FirstSeen
                FROM ReviewLog
                GROUP BY CardId
                HAVING FirstSeen >= $since
            );
            """;
        command.Parameters.AddWithValue("$since", SqlTime.To(since));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

    private void SetDeletedAt(long cardId, DateTimeOffset? value)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Card SET DeletedAt = $deleted, UpdatedAt = $updated WHERE Id = $id;";
        command.Parameters.AddWithValue("$deleted", (object?)SqlTime.ToNullable(value) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", SqlTime.To(DateTimeOffset.Now));
        command.Parameters.AddWithValue("$id", cardId);
        command.ExecuteNonQuery();
    }

    private static void InsertSchedule(SqliteConnection connection, SqliteTransaction transaction, CardSchedule schedule)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CardSchedule (CardId, EaseFactor, IntervalMinutes, Repetitions, Lapses, DueAt,
                                      LastShownAt, CorrectCount, WrongCount, IgnoredCount, ConsecutiveIgnores, Streak)
            VALUES ($id, $ease, $interval, $reps, $lapses, $due, $lastShown, $correct, $wrong, $ignored, $streakIgnored, $streak);
            """;
        BindScheduleFields(command, schedule);
        command.ExecuteNonQuery();
    }

    private static void BindCardFields(SqliteCommand command, Card card)
    {
        command.Parameters.AddWithValue("$deck", card.DeckId);
        command.Parameters.AddWithValue("$front", card.Front);
        command.Parameters.AddWithValue("$back", card.Back);
        command.Parameters.AddWithValue("$hint", (object?)card.Hint ?? DBNull.Value);
        command.Parameters.AddWithValue("$example", (object?)card.Example ?? DBNull.Value);
        command.Parameters.AddWithValue("$tags", (object?)card.Tags ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)card.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$suspended", card.IsSuspended);
    }

    private static void BindScheduleFields(SqliteCommand command, CardSchedule schedule)
    {
        command.Parameters.AddWithValue("$id", schedule.CardId);
        command.Parameters.AddWithValue("$ease", schedule.EaseFactor);
        command.Parameters.AddWithValue("$interval", schedule.IntervalMinutes);
        command.Parameters.AddWithValue("$reps", schedule.Repetitions);
        command.Parameters.AddWithValue("$lapses", schedule.Lapses);
        command.Parameters.AddWithValue("$due", SqlTime.To(schedule.DueAt));
        command.Parameters.AddWithValue("$lastShown", (object?)SqlTime.ToNullable(schedule.LastShownAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$correct", schedule.CorrectCount);
        command.Parameters.AddWithValue("$wrong", schedule.WrongCount);
        command.Parameters.AddWithValue("$ignored", schedule.IgnoredCount);
        command.Parameters.AddWithValue("$streakIgnored", schedule.ConsecutiveIgnores);
        command.Parameters.AddWithValue("$streak", schedule.Streak);
    }

    private static Card ReadCard(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        DeckId = reader.GetInt64(1),
        Front = reader.GetString(2),
        Back = reader.GetString(3),
        Hint = reader.IsDBNull(4) ? null : reader.GetString(4),
        Example = reader.IsDBNull(5) ? null : reader.GetString(5),
        Tags = reader.IsDBNull(6) ? null : reader.GetString(6),
        Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
        IsSuspended = reader.GetBoolean(8),
        CreatedAt = SqlTime.From(reader.GetString(9)),
        UpdatedAt = SqlTime.From(reader.GetString(10)),
        DeletedAt = reader.IsDBNull(11) ? null : SqlTime.From(reader.GetString(11)),
        Schedule = new CardSchedule
        {
            CardId = reader.GetInt64(0),
            EaseFactor = reader.GetDouble(12),
            IntervalMinutes = reader.GetDouble(13),
            Repetitions = reader.GetInt32(14),
            Lapses = reader.GetInt32(15),
            DueAt = SqlTime.From(reader.GetString(16)),
            LastShownAt = reader.IsDBNull(17) ? null : SqlTime.From(reader.GetString(17)),
            CorrectCount = reader.GetInt32(18),
            WrongCount = reader.GetInt32(19),
            IgnoredCount = reader.GetInt32(20),
            ConsecutiveIgnores = reader.GetInt32(21),
            Streak = reader.GetInt32(22),
        },
    };
}

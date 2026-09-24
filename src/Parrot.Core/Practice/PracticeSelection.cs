using Parrot.Core.Models;
using Parrot.Core.Statistics;

namespace Parrot.Core.Practice;

/// <summary>The games a practice session can be played as.</summary>
public enum PracticeMode
{
    /// <summary>Flip a card, say honestly whether you knew it.</summary>
    Flashcards = 0,

    /// <summary>Pick the translation out of four.</summary>
    Choice = 1,

    /// <summary>Type the translation.</summary>
    Write = 2,

    /// <summary>Every card climbs from multiple choice to typing until the whole set is learned.</summary>
    Learn = 3,

    /// <summary>Pair words with their translations against the clock.</summary>
    Match = 4,

    /// <summary>Is this the right translation? As many as you can in a minute.</summary>
    TrueFalse = 5,
}

/// <summary>Which cards go into a session.</summary>
public enum PracticeScope
{
    /// <summary>Every card that passes the filters.</summary>
    All = 0,

    /// <summary>A random handful.</summary>
    Random = 1,

    /// <summary>The cards with the worst record.</summary>
    Hardest = 2,

    /// <summary>Cards that have never been shown.</summary>
    New = 3,

    /// <summary>Cards answered wrong in the last <see cref="PracticeSelection.MistakeWindowDays"/> days.</summary>
    Mistakes = 4,
}

public sealed record PracticeFilter
{
    /// <summary>Null means every deck — switched off ones included: they only mute prompts.</summary>
    public long? DeckId { get; init; }

    /// <summary>Null means any kind.</summary>
    public CardKind? Kind { get; init; }

    public PracticeScope Scope { get; init; } = PracticeScope.Random;

    /// <summary>How many cards to take. Ignored for <see cref="PracticeScope.All"/>.</summary>
    public int Count { get; init; } = 20;
}

public static class PracticeSelection
{
    public const int MistakeWindowDays = 7;

    /// <summary>
    /// Picks the cards for a session, in the order they should be asked. Deleted and suspended
    /// cards never take part.
    /// </summary>
    /// <param name="reviews">Recent history; only needed for <see cref="PracticeScope.Mistakes"/>.</param>
    public static List<Card> Select(IEnumerable<Card> cards, PracticeFilter filter, IEnumerable<ReviewLog> reviews,
        DateTimeOffset now, Random random)
    {
        var pool = Candidates(cards, filter).ToList();
        var count = Math.Max(1, filter.Count);

        var picked = filter.Scope switch
        {
            PracticeScope.All => pool,

            PracticeScope.Hardest => pool
                .Where(c => !c.Schedule.IsNew)
                .OrderByDescending(c => c.Schedule.DifficultyPercent)
                .ThenByDescending(c => c.Schedule.Lapses)
                .ThenBy(c => c.Schedule.Accuracy)
                .Take(count)
                .ToList(),

            PracticeScope.New => pool
                .Where(c => c.Schedule.IsNew)
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Take(count)
                .ToList(),

            PracticeScope.Mistakes => MissedRecently(pool, reviews, now)
                .Take(count)
                .ToList(),

            _ => Shuffle(pool, random).Take(count).ToList(),
        };

        // Whatever the selection, asking in a fixed order would let position give answers away.
        return Shuffle(picked, random);
    }

    /// <summary>The cards <see cref="Select"/> chooses from, before the scope is applied.</summary>
    public static IEnumerable<Card> Candidates(IEnumerable<Card> cards, PracticeFilter filter) =>
        cards.Where(c => c.DeletedAt is null && !c.IsSuspended)
            .Where(c => filter.DeckId is not { } deckId || c.DeckId == deckId)
            .Where(c => filter.Kind is not { } kind || c.Kind == kind);

    /// <summary>Most recently missed first, so a small count still gets the freshest mistakes.</summary>
    private static IEnumerable<Card> MissedRecently(List<Card> pool, IEnumerable<ReviewLog> reviews, DateTimeOffset now)
    {
        var since = now.AddDays(-MistakeWindowDays);

        var lastMiss = reviews
            .Where(r => r.ShownAt >= since && StatisticsCalculator.IsAnswered(r.Outcome) && !StatisticsCalculator.IsCorrect(r.Outcome))
            .GroupBy(r => r.CardId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.ShownAt));

        return pool
            .Where(c => lastMiss.ContainsKey(c.Id))
            .OrderByDescending(c => lastMiss[c.Id]);
    }

    public static List<T> Shuffle<T>(IEnumerable<T> items, Random random)
    {
        var list = items.ToList();

        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}

using Parrot.Core.Models;

namespace Parrot.Core.Answers;

/// <summary>
/// When asked "точний" (Ukrainian for "accurate/precise"), both "accurate" and "precise" are honest answers, even though only
/// one of them lives on this card. This finds the other cards that share a translation.
/// </summary>
public static class Synonyms
{
    /// <summary>
    /// Fronts of cards in <paramref name="pool"/> that share at least one accepted translation
    /// with <paramref name="card"/>. The card itself is excluded.
    /// </summary>
    public static IReadOnlyList<string> FrontsSharingMeaning(Card card, IEnumerable<Card> pool)
    {
        var meanings = Meanings(card);
        if (meanings.Count == 0)
            return [];

        return pool
            .Where(other => other.Id != card.Id && !ReferenceEquals(other, card))
            .Where(other => Meanings(other).Overlaps(meanings))
            .Select(other => other.Front)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> Meanings(Card card) =>
        card.AcceptedAnswers
            .Select(AnswerNormalizer.Normalize)
            .Where(m => m.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
}

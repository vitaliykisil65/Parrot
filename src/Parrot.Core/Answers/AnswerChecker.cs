using Parrot.Core.Models;

namespace Parrot.Core.Answers;

public readonly record struct AnswerResult(ReviewOutcome Outcome, string? MatchedAnswer)
{
    public bool IsAccepted => Outcome is ReviewOutcome.Correct or ReviewOutcome.Typo;
}

public sealed class AnswerChecker(AnswerStrictness strictness = AnswerStrictness.Lenient)
{
    public AnswerStrictness Strictness { get; } = strictness;

    /// <summary>
    /// Compares a typed answer against every accepted variant of the card.
    /// An exact match always wins over a typo match, so the user is not told
    /// "almost right" when one of the variants was spot on.
    /// </summary>
    public AnswerResult Check(Card card, string? userAnswer, TranslationDirection direction)
    {
        var expected = direction == TranslationDirection.BackToFront
            ? Split(card.Front)
            : card.AcceptedAnswers;

        return Check(expected, userAnswer);
    }

    public AnswerResult Check(IEnumerable<string> acceptedAnswers, string? userAnswer)
    {
        var typed = AnswerNormalizer.Normalize(userAnswer);
        if (typed.Length == 0)
            return new AnswerResult(ReviewOutcome.Wrong, null);

        string? typoMatch = null;

        foreach (var accepted in acceptedAnswers)
        {
            var target = AnswerNormalizer.Normalize(accepted);
            if (target.Length == 0)
                continue;

            if (string.Equals(typed, target, StringComparison.Ordinal))
                return new AnswerResult(ReviewOutcome.Correct, accepted);

            if (Strictness == AnswerStrictness.Lenient && typoMatch is null && IsTypo(typed, target))
                typoMatch = accepted;
        }

        return typoMatch is null
            ? new AnswerResult(ReviewOutcome.Wrong, null)
            : new AnswerResult(ReviewOutcome.Typo, typoMatch);
    }

    /// <summary>
    /// Short answers get no slack — with a three-letter target, one edit away is usually a
    /// different word ("cat" vs "cut"), not a slip of the finger.
    /// </summary>
    private static bool IsTypo(string typed, string target)
    {
        var budget = target.Length switch
        {
            < 4 => 0,
            < 8 => 1,
            _ => 2,
        };

        return budget > 0 && Levenshtein.Distance(typed, target, budget) <= budget;
    }

    private static IEnumerable<string> Split(string value) =>
        value.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

using Parrot.Core.Answers;
using Parrot.Core.Models;

namespace Parrot.Core.Practice;

/// <summary>One answer button in a multiple-choice question.</summary>
public sealed record ChoiceOption(string Text, bool IsCorrect);

/// <summary>A "does this translation fit?" question.</summary>
public sealed record TrueFalseQuestion(Card Card, TranslationDirection Direction, string Question, string Shown, bool IsTrue);

/// <summary>Turning cards into questions: which side is asked, and what the wrong options are.</summary>
public static class PracticeCards
{
    /// <summary>The back side for display: "зрозуміти; розібратися" becomes "зрозуміти, розібратися".</summary>
    public static string BackText(Card card) => string.Join(", ", card.AcceptedAnswers);

    public static string Question(Card card, TranslationDirection direction) =>
        direction == TranslationDirection.BackToFront ? BackText(card) : card.Front;

    public static string Answer(Card card, TranslationDirection direction) =>
        direction == TranslationDirection.BackToFront ? card.Front : BackText(card);

    /// <summary>Resolves "mixed" into one side for a single question.</summary>
    public static TranslationDirection Resolve(TranslationDirection direction, Random random) =>
        direction == TranslationDirection.Random
            ? random.Next(2) == 0 ? TranslationDirection.FrontToBack : TranslationDirection.BackToFront
            : direction;

    /// <summary>
    /// The correct answer plus up to <paramref name="count"/> − 1 wrong ones, in random order.
    /// Wrong options come from <paramref name="pool"/> and look like the right one — same kind of
    /// card, similar length — so the question can't be solved by elimination. Cards that share a
    /// meaning with the asked one are never offered as wrong: "precise" is not wrong for "точний"
    /// just because it lives on another card. Fewer options come back when the pool is small.
    /// </summary>
    public static List<ChoiceOption> Options(Card card, TranslationDirection direction, IEnumerable<Card> pool,
        Random random, int count = 4)
    {
        var correct = Answer(card, direction);

        var options = Distractors(card, direction, pool, random, count - 1)
            .Select(text => new ChoiceOption(text, IsCorrect: false))
            .Append(new ChoiceOption(correct, IsCorrect: true));

        return PracticeSelection.Shuffle(options, random);
    }

    /// <summary>
    /// Half the time the real translation, the other half a convincing wrong one. Falls back to a
    /// true question when there is nothing to make a false one from.
    /// </summary>
    public static TrueFalseQuestion TrueFalse(Card card, TranslationDirection direction, IEnumerable<Card> pool, Random random)
    {
        var question = Question(card, direction);

        if (random.Next(2) == 0 && Distractors(card, direction, pool, random, 1) is [var wrong])
            return new TrueFalseQuestion(card, direction, question, wrong, IsTrue: false);

        return new TrueFalseQuestion(card, direction, question, Answer(card, direction), IsTrue: true);
    }

    private static List<string> Distractors(Card card, TranslationDirection direction, IEnumerable<Card> pool,
        Random random, int count)
    {
        if (count <= 0)
            return [];

        var correct = Answer(card, direction);
        var taken = new HashSet<string>(StringComparer.Ordinal) { AnswerNormalizer.Normalize(correct) };
        var meanings = Meanings(card);
        var front = AnswerNormalizer.Normalize(card.Front);

        var candidates = new List<(string Text, bool SameKind, double Score)>();

        foreach (var other in pool)
        {
            if ((other.Id != 0 && other.Id == card.Id) || ReferenceEquals(other, card) || other.DeletedAt is not null)
                continue;

            // A synonym or a duplicate card would be a second right answer.
            if (Meanings(other).Overlaps(meanings) || AnswerNormalizer.Normalize(other.Front) == front)
                continue;

            var text = Answer(other, direction);
            if (!taken.Add(AnswerNormalizer.Normalize(text)))
                continue;

            var sameKind = card.Kind == CardKind.None || other.Kind == card.Kind;
            var lengthGap = Math.Abs(text.Length - correct.Length);
            candidates.Add((text, sameKind, lengthGap + random.NextDouble() * 6));
        }

        // Cards of the same kind first; within them, the best-looking few are shuffled so the
        // same card doesn't always bring the same three companions.
        var sameKindCount = candidates.Count(c => c.SameKind);
        var shortlist = candidates
            .OrderBy(c => c.SameKind ? 0 : 1)
            .ThenBy(c => c.Score)
            .Take(sameKindCount >= count ? Math.Min(sameKindCount, count * 3) : count)
            .Select(c => c.Text);

        return PracticeSelection.Shuffle(shortlist, random).Take(count).ToList();
    }

    private static HashSet<string> Meanings(Card card) =>
        card.AcceptedAnswers
            .Select(AnswerNormalizer.Normalize)
            .Where(m => m.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
}

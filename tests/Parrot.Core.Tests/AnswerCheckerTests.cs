using Parrot.Core.Answers;
using Parrot.Core.Models;

namespace Parrot.Core.Tests;

public class AnswerCheckerTests
{
    private readonly AnswerChecker _lenient = new(AnswerStrictness.Lenient);
    private readonly AnswerChecker _strict = new(AnswerStrictness.Strict);

    private static Card Card(string front, string back) => new() { Front = front, Back = back };

    [Theory]
    [InlineData("розібратися")]
    [InlineData("  Розібратися  ")]
    [InlineData("РОЗІБРАТИСЯ")]
    [InlineData("розібратися.")]
    public void Casing_spacing_and_punctuation_are_forgiven(string answer)
    {
        var result = _lenient.Check(Card("to figure out", "зрозуміти; розібратися"), answer, TranslationDirection.FrontToBack);

        Assert.Equal(ReviewOutcome.Correct, result.Outcome);
    }

    [Fact]
    public void Any_listed_variant_counts()
    {
        var card = Card("to figure out", "зрозуміти; розібратися");

        Assert.Equal(ReviewOutcome.Correct, _lenient.Check(card, "зрозуміти", TranslationDirection.FrontToBack).Outcome);
        Assert.Equal(ReviewOutcome.Correct, _lenient.Check(card, "розібратися", TranslationDirection.FrontToBack).Outcome);
    }

    [Fact]
    public void Articles_and_infinitive_marker_are_ignored()
    {
        var card = Card("недолік", "a drawback");

        Assert.Equal(ReviewOutcome.Correct, _lenient.Check(card, "drawback", TranslationDirection.FrontToBack).Outcome);
        Assert.Equal(ReviewOutcome.Correct, _lenient.Check(card, "the drawback", TranslationDirection.FrontToBack).Outcome);
    }

    [Fact]
    public void Reverse_direction_asks_for_the_front_side()
    {
        var card = Card("deadline", "кінцевий термін");

        var result = _lenient.Check(card, "deadline", TranslationDirection.BackToFront);

        Assert.Equal(ReviewOutcome.Correct, result.Outcome);
    }

    [Fact]
    public void A_single_slip_in_a_long_word_is_a_typo_not_a_failure()
    {
        var result = _lenient.Check(Card("bottleneck", "вузьке місце"), "вузке місце", TranslationDirection.FrontToBack);

        Assert.Equal(ReviewOutcome.Typo, result.Outcome);
        Assert.True(result.IsAccepted);
        Assert.Equal("вузьке місце", result.MatchedAnswer);
    }

    [Fact]
    public void Short_words_get_no_slack_because_one_edit_is_a_different_word()
    {
        var result = _lenient.Check(Card("кіт", "cat"), "cut", TranslationDirection.FrontToBack);

        Assert.Equal(ReviewOutcome.Wrong, result.Outcome);
    }

    [Fact]
    public void An_exact_variant_wins_over_a_near_miss_on_another_variant()
    {
        var card = Card("scope", "обсяг; межі");

        var result = _lenient.Check(card, "межі", TranslationDirection.FrontToBack);

        Assert.Equal(ReviewOutcome.Correct, result.Outcome);
        Assert.Equal("межі", result.MatchedAnswer);
    }

    [Fact]
    public void Strict_mode_rejects_typos()
    {
        var result = _strict.Check(Card("bottleneck", "вузьке місце"), "вузке місце", TranslationDirection.FrontToBack);

        Assert.Equal(ReviewOutcome.Wrong, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_answer_is_wrong(string? answer)
    {
        var result = _lenient.Check(Card("scope", "обсяг"), answer, TranslationDirection.FrontToBack);

        Assert.Equal(ReviewOutcome.Wrong, result.Outcome);
    }

    [Theory]
    [InlineData("don't", "dont")]
    [InlineData("re-read", "reread")]
    [InlineData("  many   spaces ", "many spaces")]
    [InlineData("\"quoted\"", "quoted")]
    public void Normalizer_strips_noise(string input, string expected)
    {
        Assert.Equal(expected, AnswerNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalizer_keeps_meaningful_leading_words()
    {
        // "to the point" must not lose "the" — only a single leading article is dropped.
        Assert.Equal("the point", AnswerNormalizer.Normalize("to the point"));
    }

    [Fact]
    public void A_synonym_from_another_card_is_accepted_after_the_cards_own_answers()
    {
        var card = Card("accurate", "точний");

        var synonym = _lenient.Check(card, "precise", TranslationDirection.BackToFront, ["precise"]);
        Assert.Equal((ReviewOutcome.Correct, "precise"), (synonym.Outcome, synonym.MatchedAnswer));

        var own = _lenient.Check(card, "accurate", TranslationDirection.BackToFront, ["precise"]);
        Assert.Equal("accurate", own.MatchedAnswer);

        Assert.Equal(ReviewOutcome.Wrong, _lenient.Check(card, "precise", TranslationDirection.BackToFront).Outcome);
    }

    [Fact]
    public void Synonyms_are_cards_sharing_any_translation_variant()
    {
        var card = new Card { Id = 1, Front = "robust", Back = "надійний; стійкий" };
        Card[] pool =
        [
            card,
            new() { Id = 2, Front = "reliable", Back = "Надійний" },
            new() { Id = 3, Front = "steady", Back = "стабільний; рівномірний" },
            new() { Id = 4, Front = "resilient", Back = "стійкий; витривалий" },
        ];

        Assert.Equal(["reliable", "resilient"], Synonyms.FrontsSharingMeaning(card, pool));
    }
}

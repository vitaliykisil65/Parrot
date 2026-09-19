using Parrot.Core.Data;
using Parrot.Core.Models;

namespace Parrot.Core.Tests;

public class CsvCardIoTests
{
    [Fact]
    public void Header_row_is_skipped()
    {
        var result = CsvCardIo.Parse("Front,Back\ndeadline,кінцевий термін\n", deckId: 3, out var cards);

        Assert.Equal(1, result.Imported);
        Assert.Equal("deadline", cards[0].Front);
        Assert.Equal(3, cards[0].DeckId);
    }

    [Fact]
    public void A_file_without_a_header_is_still_imported()
    {
        CsvCardIo.Parse("deadline,кінцевий термін\nscope,обсяг", deckId: 1, out var cards);

        Assert.Equal(2, cards.Count);
    }

    [Fact]
    public void Semicolons_stay_inside_the_translation()
    {
        // ';' separates alternative answers, so it must not be treated as a column break.
        CsvCardIo.Parse("to figure out,зрозуміти; розібратися", deckId: 1, out var cards);

        Assert.Equal("зрозуміти; розібратися", cards[0].Back);
        Assert.Equal(["зрозуміти", "розібратися"], cards[0].AcceptedAnswers);
    }

    [Fact]
    public void Quoted_fields_may_contain_commas_and_quotes()
    {
        CsvCardIo.Parse("""
            Front,Back,Hint,Example
            "by the way","до речі, між іншим",,"He said ""hi"" by the way."
            """, deckId: 1, out var cards);

        Assert.Equal("до речі, між іншим", cards[0].Back);
        Assert.Equal("He said \"hi\" by the way.", cards[0].Example);
    }

    [Fact]
    public void Tab_separated_input_works()
    {
        CsvCardIo.Parse("deadline\tкінцевий термін", deckId: 1, out var cards);

        Assert.Equal("кінцевий термін", cards[0].Back);
    }

    [Fact]
    public void Incomplete_rows_are_reported_rather_than_silently_dropped()
    {
        var result = CsvCardIo.Parse("Front,Back\ndeadline,кінцевий термін\nlonely\n", deckId: 1, out var cards);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Single(result.Errors);
        Assert.Single(cards);
    }

    [Fact]
    public void Blank_lines_are_skipped_without_an_error()
    {
        var result = CsvCardIo.Parse("Front,Back\n\ndeadline,кінцевий термін\n\n", deckId: 1, out _);

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Export_then_import_gives_back_the_same_cards()
    {
        var original = new List<Card>
        {
            new() { Front = "by the way", Back = "до речі, між іншим", Example = "He said \"hi\"." },
            new() { Front = "to figure out", Back = "зрозуміти; розібратися", Tags = "phrasal" },
        };

        CsvCardIo.Parse(CsvCardIo.Export(original), deckId: 9, out var roundTripped);

        Assert.Equal(2, roundTripped.Count);
        Assert.Equal(original[0].Back, roundTripped[0].Back);
        Assert.Equal(original[0].Example, roundTripped[0].Example);
        Assert.Equal(original[1].Tags, roundTripped[1].Tags);
    }

    [Fact]
    public void The_starter_deck_parses_cleanly()
    {
        using var db = new TempDatabase();
        SeedData.EnsureSeeded(db.Repository);

        var cards = db.Repository.GetCards(CardQuery.Promptable);

        Assert.True(cards.Count >= 400, $"expected a starter deck of several hundred cards, got {cards.Count}");
        Assert.All(cards, c => Assert.False(string.IsNullOrWhiteSpace(c.Back)));
    }

    [Fact]
    public void Every_starter_card_is_complete_and_unique()
    {
        var result = CsvCardIo.Parse(SeedData.ReadStarterDeck(), deckId: 1, out var cards);

        Assert.Equal(0, result.Skipped);
        Assert.All(cards, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Example), $"'{c.Front}' has no example");
            Assert.False(string.IsNullOrWhiteSpace(c.Tags), $"'{c.Front}' has no tag");
            Assert.DoesNotContain(",", c.Tags!);
        });

        var duplicates = cards.GroupBy(c => c.Front, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Seeding_twice_does_not_duplicate_the_starter_deck()
    {
        using var db = new TempDatabase();

        SeedData.EnsureSeeded(db.Repository);
        SeedData.EnsureSeeded(db.Repository);

        Assert.Single(db.Repository.GetDecks());
    }
}

namespace Parrot.Core.Data;

/// <summary>
/// Creates the deck a brand-new install starts with, so "Add" has somewhere to put cards.
/// Public builds start it empty; dev builds pass the starter word list to have something
/// to show on the very first prompt.
/// </summary>
public static class SeedData
{
    public const string DefaultDeckName = "English";

    /// <param name="starterCsv">Cards to fill the new deck with (CsvCardIo format), or null for an empty deck.</param>
    /// <param name="deckName">The deck's name in the UI language.</param>
    public static void EnsureSeeded(CardRepository repository, string? starterCsv = null, string deckName = DefaultDeckName)
    {
        if (repository.GetDecks().Count > 0)
            return;

        var deckId = repository.AddDeck(new Models.Deck
        {
            Name = deckName,
            FrontLang = "en",
            BackLang = "uk",
        });

        if (string.IsNullOrWhiteSpace(starterCsv))
            return;

        CsvCardIo.Parse(starterCsv, deckId, out var cards);
        repository.AddCards(cards);
    }
}

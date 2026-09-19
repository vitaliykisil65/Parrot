using System.Reflection;

namespace Parrot.Core.Data;

/// <summary>
/// Gives a brand-new install something to show on the very first prompt. Without this the
/// app would start silent and look broken.
/// </summary>
public static class SeedData
{
    public const string StarterDeckName = "Англійська — старт";

    /// <summary>
    /// Kept as a plain CSV next to this file (and embedded in the assembly), so the word list
    /// can be edited or opened in a spreadsheet without touching code.
    /// </summary>
    private const string StarterDeckResource = "Parrot.Core.Data.StarterDeck.csv";

    public static void EnsureSeeded(CardRepository repository)
    {
        if (repository.GetDecks().Count > 0)
            return;

        var deckId = repository.AddDeck(new Models.Deck
        {
            Name = StarterDeckName,
            FrontLang = "en",
            BackLang = "uk",
        });

        CsvCardIo.Parse(ReadStarterDeck(), deckId, out var cards);
        repository.AddCards(cards);
    }

    public static string ReadStarterDeck()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(StarterDeckResource)
                           ?? throw new InvalidOperationException($"Missing embedded resource {StarterDeckResource}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

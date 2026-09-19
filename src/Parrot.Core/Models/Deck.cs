namespace Parrot.Core.Models;

/// <summary>
/// A set of cards. Decks carry their own language pair so the app is not tied to English —
/// a future deck could just as well be "chemistry terms" or "German verbs".
/// </summary>
public sealed class Deck
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string FrontLang { get; set; } = "en";
    public string BackLang { get; set; } = "uk";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Not persisted — filled in by the repository for display purposes.</summary>
    public int CardCount { get; set; }
}

namespace Parrot.Core.Data;

public sealed record CardQuery
{
    public string? Search { get; init; }
    public long? DeckId { get; init; }
    public bool IncludeDeleted { get; init; }
    public bool IncludeSuspended { get; init; } = true;

    /// <summary>Restrict to cards belonging to decks the user has switched on.</summary>
    public bool OnlyActiveDecks { get; init; }

    public static CardQuery All { get; } = new();

    /// <summary>What the prompt scheduler may choose from.</summary>
    public static CardQuery Promptable { get; } = new()
    {
        IncludeDeleted = false,
        IncludeSuspended = false,
        OnlyActiveDecks = true,
    };
}

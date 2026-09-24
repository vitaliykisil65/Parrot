namespace Parrot.Core.Models;

/// <summary>
/// One thing to learn. <see cref="Front"/> is the prompt side (e.g. the English phrase),
/// <see cref="Back"/> the answer side. Several acceptable answers may be listed,
/// separated by ';' or '|'.
/// </summary>
public sealed class Card
{
    public long Id { get; set; }
    public long DeckId { get; set; }
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";

    /// <summary>How the front side is pronounced, e.g. "/ˈdedlaɪn/". Optional.</summary>
    public string? Transcription { get; set; }

    /// <summary>Word, phrase, idiom… Optional.</summary>
    public CardKind Kind { get; set; }

    /// <summary>Optional nudge shown on demand before answering.</summary>
    public string? Hint { get; set; }

    /// <summary>Optional usage example, shown together with the answer.</summary>
    public string? Example { get; set; }

    /// <summary>Comma-separated free-form tags.</summary>
    public string? Tags { get; set; }

    public string? Notes { get; set; }

    /// <summary>Suspended cards stay in the library but are never picked for prompts.</summary>
    public bool IsSuspended { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Soft delete, so an accidental removal can be undone.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Scheduling state. Always present for cards loaded through the repository.</summary>
    public CardSchedule Schedule { get; set; } = new();

    public IEnumerable<string> AcceptedAnswers =>
        Back.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

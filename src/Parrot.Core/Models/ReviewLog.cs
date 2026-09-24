namespace Parrot.Core.Models;

/// <summary>One prompt or practice question shown to the user, recorded for statistics.</summary>
public sealed class ReviewLog
{
    public long Id { get; set; }
    public long CardId { get; set; }
    public DateTimeOffset ShownAt { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public ReviewOutcome Outcome { get; set; }
    public string? UserAnswer { get; set; }
    public TranslationDirection Direction { get; set; }
    public int ResponseMs { get; set; }
    public ReviewSource Source { get; set; }
}

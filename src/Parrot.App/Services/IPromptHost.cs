namespace Parrot.App.Services;

/// <summary>What the main window needs to know about, and do to, the prompt schedule.</summary>
public interface IPromptHost
{
    DateTimeOffset? NextPromptAt { get; }

    /// <summary>Set while prompts are paused; null otherwise.</summary>
    DateTimeOffset? PausedUntil { get; }

    void PromptNow();
    void Pause(TimeSpan duration);
    void Resume();

    /// <summary>Raised after a pause, a resume or a finished prompt.</summary>
    event EventHandler? StateChanged;
}

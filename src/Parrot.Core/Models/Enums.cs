namespace Parrot.Core.Models;

/// <summary>How a prompt ended. Only some of these affect the card's rating.</summary>
public enum ReviewOutcome
{
    /// <summary>Exact match after normalization.</summary>
    Correct = 0,

    /// <summary>Close enough — counted as correct, but the ease factor does not grow.</summary>
    Typo = 1,

    /// <summary>User answered, but wrong.</summary>
    Wrong = 2,

    /// <summary>User pressed "I don't know". A failure, but an honest one — no lapse recorded.</summary>
    DontKnow = 3,

    /// <summary>User dismissed the prompt. Does not mean they don't know the card.</summary>
    Ignored = 4,

    /// <summary>The prompt closed on its own. Same meaning as <see cref="Ignored"/>.</summary>
    Timeout = 5,
}

public enum TranslationDirection
{
    /// <summary>Show the foreign term, ask for the native translation.</summary>
    FrontToBack = 0,

    /// <summary>Show the native translation, ask for the foreign term.</summary>
    BackToFront = 1,

    /// <summary>Pick a direction at random for every prompt.</summary>
    Random = 2,
}

public enum AnswerStrictness
{
    /// <summary>Only an exact match (after normalization) counts.</summary>
    Strict = 0,

    /// <summary>Small typos are forgiven and reported back to the user.</summary>
    Lenient = 1,
}

/// <summary>Where on the screen the prompt window appears.</summary>
public enum ScreenAnchor
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, Center, MiddleRight,
    BottomLeft, BottomCenter, BottomRight,
}

public enum MonitorChoice
{
    Primary = 0,
    WithCursor = 1,
}

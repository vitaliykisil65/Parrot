using Parrot.Core.Answers;
using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Settings;

namespace Parrot.Core.Scheduling;

/// <summary>A card chosen for one prompt, together with how it should be asked.</summary>
public sealed class PromptRequest
{
    public required Card Card { get; init; }
    public required TranslationDirection Direction { get; init; }
    public DateTimeOffset ShownAt { get; init; } = DateTimeOffset.Now;

    /// <summary>The text put in front of the user.</summary>
    public string Question => Direction == TranslationDirection.BackToFront ? Card.Back : Card.Front;

    /// <summary>The answer they are expected to produce.</summary>
    public string Answer => Direction == TranslationDirection.BackToFront ? Card.Front : Card.Back;
}

public enum PromptBlockReason
{
    None = 0,
    QuietHours,
    DailyLimitReached,
    Paused,
    NothingDue,
}

/// <summary>
/// The decision layer between "a timer fired" and "a window appears": may we interrupt,
/// and if so with which card. Kept free of UI and Win32 so it can be tested directly.
/// </summary>
public sealed class PromptService
{
    private readonly CardRepository _repository;
    private readonly SettingsService _settingsService;
    private readonly SrsEngine _srs;
    private readonly CardPicker _picker;

    public PromptService(
        CardRepository repository,
        SettingsService settingsService,
        SrsEngine? srs = null,
        CardPicker? picker = null)
    {
        _repository = repository;
        _settingsService = settingsService;
        _srs = srs ?? new SrsEngine(settingsService.Current.IgnoreCooldownMinutes);
        _picker = picker ?? new CardPicker(_srs);
    }

    /// <summary>Set while the user has muted prompts from the tray menu.</summary>
    public DateTimeOffset? PausedUntil { get; set; }

    public bool IsPaused(DateTimeOffset now) => PausedUntil is { } until && now < until;

    public void Pause(TimeSpan duration) => PausedUntil = DateTimeOffset.Now + duration;

    public void Resume() => PausedUntil = null;

    public PromptBlockReason CheckAvailability(DateTimeOffset now)
    {
        var settings = _settingsService.Current;

        if (IsPaused(now))
            return PromptBlockReason.Paused;

        if (!settings.IsWithinActiveWindow(now))
            return PromptBlockReason.QuietHours;

        if (settings.MaxPromptsPerDay > 0 &&
            _repository.CountPromptsSince(StartOfDay(now)) >= settings.MaxPromptsPerDay)
            return PromptBlockReason.DailyLimitReached;

        return PromptBlockReason.None;
    }

    /// <summary>
    /// Returns the next prompt, or null with a reason when the user should be left alone.
    /// Nothing is written to the database here: a card only counts as shown once the UI
    /// confirms it via <see cref="Record"/>.
    /// </summary>
    public PromptRequest? TryCreatePrompt(DateTimeOffset now, out PromptBlockReason reason)
    {
        reason = CheckAvailability(now);
        if (reason != PromptBlockReason.None)
            return null;

        var settings = _settingsService.Current;

        var card = _picker.Pick(_repository.GetCards(CardQuery.Promptable), now, new PickOptions
        {
            MaxNewPerDay = settings.MaxNewCardsPerDay,
            NewIntroducedToday = _repository.CountNewCardsSince(StartOfDay(now)),
            AlwaysShowSomething = settings.AlwaysShowSomething,
        });

        if (card is null)
        {
            reason = PromptBlockReason.NothingDue;
            return null;
        }

        return new PromptRequest
        {
            Card = card,
            Direction = _picker.ResolveDirection(settings.Direction),
            ShownAt = now,
        };
    }

    public AnswerChecker CreateChecker() => new(_settingsService.Current.Strictness);

    /// <summary>Applies the outcome to the card's schedule and writes it to history.</summary>
    public void Record(PromptRequest request, ReviewOutcome outcome, string? userAnswer, DateTimeOffset now)
    {
        _srs.Apply(request.Card.Schedule, outcome, now);
        _repository.SaveSchedule(request.Card.Schedule);

        _repository.LogReview(new ReviewLog
        {
            CardId = request.Card.Id,
            ShownAt = request.ShownAt,
            AnsweredAt = outcome is ReviewOutcome.Ignored or ReviewOutcome.Timeout ? null : now,
            Outcome = outcome,
            UserAnswer = userAnswer,
            Direction = request.Direction,
            ResponseMs = (int)Math.Clamp((now - request.ShownAt).TotalMilliseconds, 0, int.MaxValue),
        });
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset now) =>
        new(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
}

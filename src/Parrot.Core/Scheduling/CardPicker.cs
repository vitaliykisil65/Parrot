using Parrot.Core.Models;

namespace Parrot.Core.Scheduling;

public sealed record PickOptions
{
    /// <summary>Cap on unseen cards introduced per day, so a big import doesn't bury the user.</summary>
    public int MaxNewPerDay { get; init; } = 10;

    /// <summary>How many new cards have already been introduced today.</summary>
    public int NewIntroducedToday { get; init; }

    /// <summary>
    /// When nothing is due, show the nearest upcoming card anyway instead of skipping
    /// the tick. Handy for a small deck you want to drill hard.
    /// </summary>
    public bool AlwaysShowSomething { get; init; }
}

/// <summary>Chooses which card the next prompt should show.</summary>
public sealed class CardPicker(SrsEngine srs, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    public Card? Pick(IReadOnlyList<Card> candidates, DateTimeOffset now, PickOptions options)
    {
        var allowNew = options.NewIntroducedToday < options.MaxNewPerDay;

        var eligible = candidates
            .Where(c => !c.IsSuspended && c.DeletedAt is null)
            .Where(c => allowNew || !c.Schedule.IsNew)
            .ToList();

        if (eligible.Count == 0)
            return null;

        var due = eligible.Where(c => c.Schedule.DueAt <= now).ToList();

        if (due.Count == 0)
        {
            return options.AlwaysShowSomething
                ? eligible.MinBy(c => c.Schedule.DueAt)
                : null;
        }

        return WeightedPick(due, now);
    }

    private Card WeightedPick(List<Card> due, DateTimeOffset now)
    {
        var weights = new double[due.Count];
        var total = 0d;

        for (var i = 0; i < due.Count; i++)
        {
            weights[i] = srs.Weight(due[i].Schedule, now);
            total += weights[i];
        }

        var roll = _random.NextDouble() * total;

        for (var i = 0; i < due.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
                return due[i];
        }

        return due[^1]; // floating-point dust
    }

    /// <summary>Picks the direction for a prompt, resolving <see cref="TranslationDirection.Random"/>.</summary>
    public TranslationDirection ResolveDirection(TranslationDirection configured) =>
        configured == TranslationDirection.Random
            ? (_random.Next(2) == 0 ? TranslationDirection.FrontToBack : TranslationDirection.BackToFront)
            : configured;
}

using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Scheduling;
using Parrot.Core.Statistics;

namespace Parrot.Core.Practice;

/// <summary>
/// Writes practice answers to history, and lets real mistakes reach the prompt schedule: a card
/// missed in practice gets harder and comes back as a prompt soon. Correct answers leave the
/// schedule alone — see <see cref="SrsEngine.ApplyPracticeMistake"/>. One recorder per session.
/// </summary>
public sealed class PracticeRecorder(CardRepository repository, SrsEngine srs)
{
    /// <summary>A card missed three times in one session is still only one piece of news.</summary>
    private readonly HashSet<long> _penalized = [];

    /// <param name="affectsSchedule">
    /// False for the speed games, where a slip under the clock says little about memory.
    /// </param>
    public void Record(Card card, TranslationDirection direction, ReviewOutcome outcome, string? userAnswer,
        DateTimeOffset shownAt, DateTimeOffset now, bool affectsSchedule = true)
    {
        repository.LogReview(new ReviewLog
        {
            CardId = card.Id,
            ShownAt = shownAt,
            AnsweredAt = now,
            Outcome = outcome,
            UserAnswer = userAnswer,
            Direction = direction,
            ResponseMs = (int)Math.Clamp((now - shownAt).TotalMilliseconds, 0, int.MaxValue),
            Source = ReviewSource.Practice,
        });

        if (!affectsSchedule || StatisticsCalculator.IsCorrect(outcome) || !_penalized.Add(card.Id))
            return;

        // Start from what is stored, not from the copy loaded when the session began: a prompt
        // answered in the meantime must not be overwritten.
        var schedule = repository.GetCard(card.Id)?.Schedule ?? card.Schedule;
        srs.ApplyPracticeMistake(schedule, now);
        repository.SaveSchedule(schedule);
        card.Schedule = schedule;
    }
}

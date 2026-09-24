using Parrot.Core.Models;

namespace Parrot.Core.Practice;

/// <summary>How one question in a study session is asked.</summary>
public enum StudyStep
{
    /// <summary>A flashcard: the user flips it and grades themselves.</summary>
    Recall,

    /// <summary>Pick the answer out of several.</summary>
    Choice,

    /// <summary>Type the answer.</summary>
    Write,
}

/// <summary>One card's progress through a session.</summary>
public sealed class StudyItem(Card card)
{
    public Card Card { get; } = card;

    /// <summary>How many of the required steps are done; drops back on a mistake.</summary>
    public int Stage { get; internal set; }

    public int Answers { get; internal set; }
    public int Mistakes { get; internal set; }

    public bool IsMastered { get; internal set; }
}

/// <summary>
/// "Keep going until I know all of them": the engine behind flashcards, multiple choice,
/// writing and learn mode. Each card must pass a sequence of steps — a correct answer moves it
/// one step on, a mistake moves it back — and the session ends when every card has passed them
/// all. Pure logic, no clock and no database, so every rule can be tested.
/// </summary>
public sealed class StudySession
{
    /// <summary>
    /// Learn mode works on a few cards at a time, the way a person would: with fifty cards in
    /// one loop, a word learned by multiple choice would not be asked in writing for fifty turns.
    /// </summary>
    public const int LearnBatchSize = 7;

    /// <summary>A missed card comes back after this many other questions — soon, but not at once.</summary>
    public const int RetryGap = 3;

    private readonly List<StudyItem> _items;
    private readonly List<StudyItem> _queue;
    private readonly Queue<StudyItem> _waiting;
    private readonly StudyStep[] _steps;
    private readonly PracticeMode _mode;

    /// <param name="goal">Correct answers in a row a card needs; for learn mode, answers in writing.</param>
    public StudySession(IEnumerable<Card> cards, PracticeMode mode, int goal = 1)
    {
        if (mode is PracticeMode.Match or PracticeMode.TrueFalse)
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Match and true-or-false are timed games, not study sessions.");

        _mode = mode;
        _steps = Steps(mode, Math.Clamp(goal, 1, 3));
        _items = cards.Select(c => new StudyItem(c)).ToList();

        var batch = mode == PracticeMode.Learn ? LearnBatchSize : int.MaxValue;
        _queue = _items.Take(batch).ToList();
        _waiting = new Queue<StudyItem>(_items.Skip(_queue.Count));
    }

    public PracticeMode Mode => _mode;

    public IReadOnlyList<StudyItem> Items => _items;

    /// <summary>The card being asked, or null once everything is learned.</summary>
    public StudyItem? Current => _queue.Count > 0 ? _queue[0] : null;

    /// <summary>How <see cref="Current"/> is to be asked.</summary>
    public StudyStep CurrentStep => Current is { } item ? _steps[item.Stage] : _steps[^1];

    /// <summary>Correct answers each card needs before it counts as learned.</summary>
    public int StepsPerCard => _steps.Length;

    public bool IsFinished => Current is null;

    public int Total => _items.Count;
    public int MasteredCount => _items.Count(i => i.IsMastered);
    public int Answers => _items.Sum(i => i.Answers);
    public int Mistakes => _items.Sum(i => i.Mistakes);

    /// <summary>0…1: steps passed out of all the steps the session needs.</summary>
    public double Progress => _items.Count == 0
        ? 1
        : _items.Sum(i => i.IsMastered ? _steps.Length : i.Stage) / (double)(_items.Count * _steps.Length);

    /// <summary>Cards that needed more than one try, the most troublesome first.</summary>
    public IReadOnlyList<StudyItem> Troublesome =>
        _items.Where(i => i.Mistakes > 0).OrderByDescending(i => i.Mistakes).ToList();

    /// <summary>Records the answer to <see cref="Current"/> and moves on to the next question.</summary>
    public void Answer(bool correct)
    {
        if (Current is not { } item)
            throw new InvalidOperationException("The session is already finished.");

        _queue.RemoveAt(0);
        item.Answers++;

        if (correct)
        {
            item.Stage++;

            if (item.Stage >= _steps.Length)
            {
                item.IsMastered = true;
                if (_waiting.TryDequeue(out var next))
                    _queue.Add(next);
                return;
            }

            // Passed a step but not done: back of the line, so other cards get a turn first.
            _queue.Add(item);
            return;
        }

        item.Mistakes++;

        // Learn mode steps back one stage (a word missed in writing goes back to multiple choice);
        // the others need the whole run of correct answers again.
        item.Stage = _mode == PracticeMode.Learn ? Math.Max(0, item.Stage - 1) : 0;

        if (_mode == PracticeMode.Flashcards)
            _queue.Add(item); // "Not yet" goes to the bottom of the pile, like a real stack of cards.
        else
            _queue.Insert(Math.Min(RetryGap, _queue.Count), item);
    }

    private static StudyStep[] Steps(PracticeMode mode, int goal) => mode switch
    {
        PracticeMode.Flashcards => Enumerable.Repeat(StudyStep.Recall, goal).ToArray(),
        PracticeMode.Choice => Enumerable.Repeat(StudyStep.Choice, goal).ToArray(),
        PracticeMode.Write => Enumerable.Repeat(StudyStep.Write, goal).ToArray(),
        _ => [StudyStep.Choice, .. Enumerable.Repeat(StudyStep.Write, goal)],
    };
}

using Parrot.Core.Models;
using Parrot.Core.Practice;

namespace Parrot.Core.Tests;

public class StudySessionTests
{
    private static List<Card> Cards(int count) =>
        Enumerable.Range(1, count).Select(i => new Card { Id = i, Front = $"word{i}", Back = $"слово{i}" }).ToList();

    private static void AnswerAll(StudySession session, bool correct = true)
    {
        var guard = 0;
        while (!session.IsFinished && guard++ < 1000)
            session.Answer(correct);
    }

    [Fact]
    public void A_session_ends_when_every_card_is_answered_right()
    {
        var session = new StudySession(Cards(5), PracticeMode.Write);

        AnswerAll(session);

        Assert.True(session.IsFinished);
        Assert.Equal(5, session.MasteredCount);
        Assert.Equal(5, session.Answers);
        Assert.Equal(1, session.Progress);
    }

    [Fact]
    public void A_goal_of_two_needs_two_right_answers_per_card()
    {
        var session = new StudySession(Cards(3), PracticeMode.Choice, goal: 2);

        AnswerAll(session);

        Assert.Equal(6, session.Answers);
    }

    [Fact]
    public void A_flashcard_marked_not_yet_goes_to_the_bottom_of_the_pile()
    {
        var session = new StudySession(Cards(4), PracticeMode.Flashcards);
        var first = session.Current!;

        session.Answer(correct: false);

        Assert.NotSame(first, session.Current);
        session.Answer(true);
        session.Answer(true);
        session.Answer(true);
        Assert.Same(first, session.Current);
    }

    [Fact]
    public void A_missed_written_card_comes_back_after_a_few_others()
    {
        var session = new StudySession(Cards(10), PracticeMode.Write);
        var missed = session.Current!;

        session.Answer(correct: false);

        for (var i = 0; i < StudySession.RetryGap; i++)
        {
            Assert.NotSame(missed, session.Current);
            session.Answer(true);
        }

        Assert.Same(missed, session.Current);
    }

    [Fact]
    public void A_mistake_resets_the_run_of_right_answers()
    {
        var session = new StudySession(Cards(1), PracticeMode.Write, goal: 2);

        session.Answer(true);
        session.Answer(false);
        Assert.Equal(0, session.Current!.Stage);

        session.Answer(true);
        session.Answer(true);
        Assert.True(session.IsFinished);
        Assert.Equal(1, session.Items[0].Mistakes);
    }

    [Fact]
    public void Learn_mode_moves_from_choice_to_writing_and_back_on_a_mistake()
    {
        var session = new StudySession(Cards(1), PracticeMode.Learn);

        Assert.Equal(StudyStep.Choice, session.CurrentStep);
        session.Answer(true);
        Assert.Equal(StudyStep.Write, session.CurrentStep);

        session.Answer(false);
        Assert.Equal(StudyStep.Choice, session.CurrentStep);

        session.Answer(true);
        session.Answer(true);
        Assert.True(session.IsFinished);
    }

    [Fact]
    public void Learn_mode_works_on_a_small_batch_at_a_time()
    {
        var cards = Cards(20);
        var session = new StudySession(cards, PracticeMode.Learn);

        var asked = new HashSet<long>();
        for (var i = 0; i < StudySession.LearnBatchSize * 2; i++)
        {
            asked.Add(session.Current!.Card.Id);
            // Every card gets its choice question right, then its written one wrong: nobody finishes.
            session.Answer(session.CurrentStep == StudyStep.Choice);
        }

        Assert.Equal(StudySession.LearnBatchSize, asked.Count);
    }

    [Fact]
    public void Learning_a_card_in_learn_mode_lets_the_next_one_in()
    {
        var session = new StudySession(Cards(StudySession.LearnBatchSize + 1), PracticeMode.Learn);

        AnswerAll(session);

        Assert.Equal(StudySession.LearnBatchSize + 1, session.MasteredCount);
    }

    [Fact]
    public void Troublesome_lists_cards_with_mistakes_worst_first()
    {
        var session = new StudySession(Cards(3), PracticeMode.Flashcards);
        var a = session.Current!;
        session.Answer(false);                  // a: 1 mistake
        var b = session.Current!;
        session.Answer(false);                  // b: 1 mistake
        session.Answer(true);                   // c: right
        Assert.Same(a, session.Current);
        session.Answer(false);                  // a: 2 mistakes
        AnswerAll(session);

        Assert.Equal([a, b], session.Troublesome);
    }

    [Fact]
    public void Timed_games_are_not_study_sessions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StudySession(Cards(2), PracticeMode.Match));
    }
}

public class PracticeSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static Card Card(long id, long deck = 1, CardKind kind = CardKind.None, bool isNew = false,
        double ease = CardSchedule.DefaultEase, bool suspended = false, bool deleted = false) => new()
    {
        Id = id,
        DeckId = deck,
        Front = $"w{id}",
        Back = $"с{id}",
        Kind = kind,
        IsSuspended = suspended,
        DeletedAt = deleted ? Now : null,
        CreatedAt = Now.AddDays(-id),
        Schedule = new CardSchedule
        {
            CardId = id,
            EaseFactor = ease,
            Repetitions = isNew ? 0 : 1,
            LastShownAt = isNew ? null : Now,
        },
    };

    private static List<Card> Select(IEnumerable<Card> cards, PracticeFilter filter, IEnumerable<ReviewLog>? reviews = null) =>
        PracticeSelection.Select(cards, filter, reviews ?? [], Now, new Random(1));

    [Fact]
    public void Suspended_and_deleted_cards_are_left_out()
    {
        var picked = Select([Card(1), Card(2, suspended: true), Card(3, deleted: true)], new PracticeFilter { Scope = PracticeScope.All });

        Assert.Equal([1L], picked.Select(c => c.Id));
    }

    [Fact]
    public void Deck_and_kind_filters_apply()
    {
        var cards = new[] { Card(1, deck: 1, kind: CardKind.Word), Card(2, deck: 2, kind: CardKind.Word), Card(3, deck: 1, kind: CardKind.Idiom) };

        var picked = Select(cards, new PracticeFilter { Scope = PracticeScope.All, DeckId = 1, Kind = CardKind.Word });

        Assert.Equal([1L], picked.Select(c => c.Id));
    }

    [Fact]
    public void Random_takes_the_requested_number()
    {
        var cards = Enumerable.Range(1, 30).Select(i => Card(i)).ToList();

        var picked = Select(cards, new PracticeFilter { Scope = PracticeScope.Random, Count = 10 });

        Assert.Equal(10, picked.Count);
        Assert.Equal(10, picked.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Hardest_prefers_low_ease_and_skips_new_cards()
    {
        var cards = new[] { Card(1, ease: 2.5), Card(2, ease: 1.3), Card(3, ease: 1.8), Card(4, isNew: true) };

        var picked = Select(cards, new PracticeFilter { Scope = PracticeScope.Hardest, Count = 2 });

        Assert.Equal([2L, 3L], picked.Select(c => c.Id).Order());
    }

    [Fact]
    public void New_takes_only_unseen_cards()
    {
        var picked = Select([Card(1), Card(2, isNew: true), Card(3, isNew: true)], new PracticeFilter { Scope = PracticeScope.New, Count = 10 });

        Assert.Equal([2L, 3L], picked.Select(c => c.Id).Order());
    }

    [Fact]
    public void Mistakes_takes_cards_missed_this_week_in_prompts_or_practice()
    {
        var reviews = new[]
        {
            new ReviewLog { CardId = 1, ShownAt = Now.AddDays(-1), Outcome = ReviewOutcome.Wrong },
            new ReviewLog { CardId = 2, ShownAt = Now.AddHours(-1), Outcome = ReviewOutcome.DontKnow, Source = ReviewSource.Practice },
            new ReviewLog { CardId = 3, ShownAt = Now.AddDays(-1), Outcome = ReviewOutcome.Correct },
            new ReviewLog { CardId = 4, ShownAt = Now.AddDays(-30), Outcome = ReviewOutcome.Wrong },
            new ReviewLog { CardId = 5, ShownAt = Now.AddHours(-1), Outcome = ReviewOutcome.Ignored },
        };

        var picked = Select(Enumerable.Range(1, 5).Select(i => Card(i)), new PracticeFilter { Scope = PracticeScope.Mistakes, Count = 10 }, reviews);

        Assert.Equal([1L, 2L], picked.Select(c => c.Id).Order());
    }
}

public class PracticeCardsTests
{
    private static Card Card(long id, string front, string back, CardKind kind = CardKind.None) =>
        new() { Id = id, Front = front, Back = back, Kind = kind };

    private static readonly List<Card> Pool =
    [
        Card(1, "deadline", "кінцевий термін"),
        Card(2, "scope", "обсяг"),
        Card(3, "accurate", "точний"),
        Card(4, "precise", "точний; чіткий"),
        Card(5, "to give up", "здатися; кинути"),
        Card(6, "outcome", "результат"),
        Card(7, "insight", "розуміння"),
    ];

    [Fact]
    public void Options_hold_exactly_one_right_answer()
    {
        var options = PracticeCards.Options(Pool[0], TranslationDirection.FrontToBack, Pool, new Random(3));

        Assert.Equal(4, options.Count);
        Assert.Single(options, o => o.IsCorrect);
        Assert.Equal("кінцевий термін", options.Single(o => o.IsCorrect).Text);
        Assert.Equal(4, options.Select(o => o.Text).Distinct().Count());
    }

    [Fact]
    public void A_synonym_is_never_offered_as_a_wrong_option()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var options = PracticeCards.Options(Pool[2], TranslationDirection.BackToFront, Pool, new Random(seed));

            Assert.DoesNotContain(options, o => o.Text == "precise");
        }
    }

    [Fact]
    public void Asking_for_the_front_side_offers_fronts()
    {
        var options = PracticeCards.Options(Pool[1], TranslationDirection.BackToFront, Pool, new Random(1));

        Assert.Equal("scope", options.Single(o => o.IsCorrect).Text);
        Assert.All(options, o => Assert.Contains(Pool, c => c.Front == o.Text));
    }

    [Fact]
    public void A_small_pool_gives_fewer_options()
    {
        var options = PracticeCards.Options(Pool[0], TranslationDirection.FrontToBack, Pool.Take(2), new Random(1));

        Assert.Equal(2, options.Count);
    }

    [Fact]
    public void Options_prefer_the_same_kind_of_card()
    {
        var pool = new List<Card>
        {
            Card(1, "to give up", "здатися", CardKind.PhrasalVerb),
            Card(2, "to figure out", "зрозуміти", CardKind.PhrasalVerb),
            Card(3, "to come up with", "придумати", CardKind.PhrasalVerb),
            Card(4, "to look after", "доглядати", CardKind.PhrasalVerb),
        };
        pool.AddRange(Enumerable.Range(10, 20).Select(i => Card(i, $"noun{i}", $"іменник{i}", CardKind.Word)));

        var options = PracticeCards.Options(pool[0], TranslationDirection.BackToFront, pool, new Random(5));

        Assert.All(options, o => Assert.StartsWith("to ", o.Text));
    }

    [Fact]
    public void The_back_side_is_shown_without_semicolons()
    {
        Assert.Equal("здатися, кинути", PracticeCards.Answer(Pool[4], TranslationDirection.FrontToBack));
        Assert.Equal("здатися, кинути", PracticeCards.Question(Pool[4], TranslationDirection.BackToFront));
    }

    [Fact]
    public void True_or_false_questions_are_honest()
    {
        var trues = 0;

        for (var seed = 0; seed < 100; seed++)
        {
            var q = PracticeCards.TrueFalse(Pool[0], TranslationDirection.FrontToBack, Pool, new Random(seed));

            Assert.Equal("deadline", q.Question);
            Assert.Equal(q.IsTrue, q.Shown == "кінцевий термін");
            if (q.IsTrue) trues++;
        }

        Assert.InRange(trues, 25, 75);
    }

    [Fact]
    public void True_or_false_without_other_cards_is_always_true()
    {
        var q = PracticeCards.TrueFalse(Pool[0], TranslationDirection.FrontToBack, [Pool[0]], new Random(0));

        Assert.True(q.IsTrue);
    }

    [Fact]
    public void Mixed_direction_resolves_to_both_sides()
    {
        var random = new Random(2);
        var sides = Enumerable.Range(0, 40).Select(_ => PracticeCards.Resolve(TranslationDirection.Random, random)).ToHashSet();

        Assert.Equal([TranslationDirection.FrontToBack, TranslationDirection.BackToFront], sides.Order());
    }
}

public class MatchBoardTests
{
    private static List<Card> Cards(int count) =>
        Enumerable.Range(1, count).Select(i => new Card { Id = i, Front = $"word{i}", Back = $"слово{i}" }).ToList();

    [Fact]
    public void A_board_has_two_tiles_per_pair()
    {
        var board = new MatchBoard(Cards(10), new Random(1));

        Assert.Equal(MatchBoard.DefaultPairs * 2, board.Tiles.Count);
        Assert.All(board.Cards, card => Assert.Equal(2, board.Tiles.Count(t => ReferenceEquals(t.Card, card))));
    }

    [Fact]
    public void Picking_a_word_and_its_translation_matches_them()
    {
        var board = new MatchBoard(Cards(2), new Random(1));
        var card = board.Cards[0];

        Assert.Equal(MatchPick.Selected, board.Pick(board.Tiles.First(t => t.Card == card && t.IsFront)));
        Assert.Equal(MatchPick.Matched, board.Pick(board.Tiles.First(t => t.Card == card && !t.IsFront)));
        Assert.False(board.IsComplete);
        Assert.False(board.WasMissed(card));
    }

    [Fact]
    public void A_wrong_pair_counts_as_a_mistake_for_both_cards()
    {
        var board = new MatchBoard(Cards(2), new Random(1));
        var (a, b) = (board.Cards[0], board.Cards[1]);

        board.Pick(board.Tiles.First(t => t.Card == a && t.IsFront));
        Assert.Equal(MatchPick.Mismatched, board.Pick(board.Tiles.First(t => t.Card == b && !t.IsFront)));

        Assert.Equal(1, board.Mistakes);
        Assert.True(board.WasMissed(a));
        Assert.True(board.WasMissed(b));
        Assert.Null(board.Selected);
    }

    [Fact]
    public void Clicking_the_selected_tile_again_lets_it_go()
    {
        var board = new MatchBoard(Cards(2), new Random(1));
        var tile = board.Tiles[0];

        board.Pick(tile);

        Assert.Equal(MatchPick.Deselected, board.Pick(tile));
        Assert.Null(board.Selected);
        Assert.Equal(0, board.Mistakes);
    }

    [Fact]
    public void Matching_every_pair_completes_the_board()
    {
        var board = new MatchBoard(Cards(3), new Random(4));

        foreach (var card in board.Cards)
        {
            board.Pick(board.Tiles.First(t => t.Card == card && t.IsFront));
            board.Pick(board.Tiles.First(t => t.Card == card && !t.IsFront));
        }

        Assert.True(board.IsComplete);
        Assert.Equal(MatchPick.Ignored, board.Pick(board.Tiles[0]));
    }

    [Fact]
    public void Cards_that_read_the_same_do_not_share_a_board()
    {
        var cards = new List<Card>
        {
            new() { Id = 1, Front = "accurate", Back = "точний" },
            new() { Id = 2, Front = "precise", Back = "точний; чіткий" },
            new() { Id = 3, Front = "scope", Back = "обсяг" },
        };

        var board = new MatchBoard(cards, new Random(1));

        Assert.Equal([1L, 3L], board.Cards.Select(c => c.Id));
    }
}

public class PracticeRecorderTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly long _deckId;

    public PracticeRecorderTests() => _deckId = _db.SeedDeck();

    public void Dispose() => _db.Dispose();

    private Card AddCard(string front = "deadline")
    {
        var card = new Card { DeckId = _deckId, Front = front, Back = "термін" };
        card.Schedule.IntervalMinutes = TimeSpan.FromDays(20).TotalMinutes;
        card.Schedule.DueAt = DateTimeOffset.Now.AddDays(20);
        card.Schedule.Repetitions = 4;
        return _db.Repository.GetCard(_db.Repository.AddCard(card))!;
    }

    [Fact]
    public void Practice_answers_are_logged_but_do_not_count_as_prompts()
    {
        var card = AddCard();
        var recorder = new PracticeRecorder(_db.Repository, new Scheduling.SrsEngine());
        var now = DateTimeOffset.Now;

        recorder.Record(card, TranslationDirection.FrontToBack, ReviewOutcome.Correct, "термін", now.AddSeconds(-3), now);

        var review = Assert.Single(_db.Repository.GetReviews());
        Assert.Equal(ReviewSource.Practice, review.Source);
        Assert.Equal(0, _db.Repository.CountPromptsSince(now.AddHours(-1)));
        Assert.Equal(0, _db.Repository.CountNewCardsSince(now.AddHours(-1)));
    }

    [Fact]
    public void A_correct_practice_answer_leaves_the_schedule_alone()
    {
        var card = AddCard();
        var before = _db.Repository.GetCard(card.Id)!.Schedule;
        var recorder = new PracticeRecorder(_db.Repository, new Scheduling.SrsEngine());

        recorder.Record(card, TranslationDirection.FrontToBack, ReviewOutcome.Correct, null, DateTimeOffset.Now, DateTimeOffset.Now);

        var after = _db.Repository.GetCard(card.Id)!.Schedule;
        Assert.Equal(before.EaseFactor, after.EaseFactor);
        Assert.Equal(before.DueAt, after.DueAt);
        Assert.Equal(before.CorrectCount, after.CorrectCount);
    }

    [Fact]
    public void A_practice_mistake_makes_the_card_harder_and_due_soon_once_per_session()
    {
        var card = AddCard();
        var recorder = new PracticeRecorder(_db.Repository, new Scheduling.SrsEngine());
        var now = DateTimeOffset.Now;

        recorder.Record(card, TranslationDirection.FrontToBack, ReviewOutcome.Wrong, "x", now, now);
        recorder.Record(card, TranslationDirection.FrontToBack, ReviewOutcome.Wrong, "y", now, now);

        var schedule = _db.Repository.GetCard(card.Id)!.Schedule;
        Assert.Equal(CardSchedule.DefaultEase - 0.2, schedule.EaseFactor, precision: 6);
        Assert.Equal(0, schedule.Repetitions);
        Assert.True(schedule.DueAt <= now.AddMinutes(CardSchedule.FirstIntervalMinutes).AddSeconds(1));
        Assert.Equal(0, schedule.WrongCount);
        Assert.Equal(2, _db.Repository.GetReviews().Count);
    }

    [Fact]
    public void Speed_game_mistakes_do_not_touch_the_schedule()
    {
        var card = AddCard();
        var recorder = new PracticeRecorder(_db.Repository, new Scheduling.SrsEngine());

        recorder.Record(card, TranslationDirection.FrontToBack, ReviewOutcome.Wrong, null, DateTimeOffset.Now, DateTimeOffset.Now,
            affectsSchedule: false);

        Assert.Equal(CardSchedule.DefaultEase, _db.Repository.GetCard(card.Id)!.Schedule.EaseFactor);
    }
}

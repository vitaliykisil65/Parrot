using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Parrot.App.Controls;
using Parrot.App.Localization;
using Parrot.App.Services;
using Parrot.Core.Answers;
using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Practice;
using Parrot.Core.Scheduling;
using Parrot.Core.Settings;
using Parrot.Core.Statistics;

namespace Parrot.App.Views;

/// <summary>A card that gave trouble, as listed on the results screen.</summary>
public sealed record TroubleRow(string Front, string Transcription, string Back, string Count);

/// <summary>
/// Practice games: pick a set of cards and a game, then play until the set is learned (or the
/// clock runs out). The rules live in Parrot.Core.Practice; this view only asks and shows.
/// </summary>
public sealed partial class PracticeView : UserControl
{
    private static readonly TimeSpan TrueFalseDuration = TimeSpan.FromSeconds(60);

    /// <summary>How long a right answer stays on screen before the next question comes by itself.</summary>
    private static readonly TimeSpan RightAnswerPause = TimeSpan.FromMilliseconds(700);

    private static readonly (PracticeMode Mode, string Icon)[] Modes =
    [
        (PracticeMode.Flashcards, "Icon.Cards"),
        (PracticeMode.Choice, "Icon.List"),
        (PracticeMode.Write, "Icon.Type"),
        (PracticeMode.Learn, "Icon.Target"),
        (PracticeMode.Match, "Icon.Grid"),
        (PracticeMode.TrueFalse, "Icon.Zap"),
    ];

    private static readonly PracticeScope[] Scopes =
        [PracticeScope.All, PracticeScope.Random, PracticeScope.Hardest, PracticeScope.New, PracticeScope.Mistakes];

    private static readonly TranslationDirection[] Directions =
        [TranslationDirection.FrontToBack, TranslationDirection.BackToFront, TranslationDirection.Random];

    private readonly CardRepository _repository;
    private readonly SettingsService _settings;
    private readonly MainWindow _shell;
    private readonly SrsEngine _srs;
    private readonly Random _random = new();

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _advance = new();
    private readonly Stopwatch _stopwatch = new();

    private readonly List<RadioButton> _modeButtons = [];
    private readonly List<RadioButton> _scopeButtons = [];
    private readonly List<RadioButton> _directionButtons = [];
    private readonly List<RadioButton> _playDirectionButtons = [];
    private readonly List<RadioButton> _goalButtons = [];

    // ── Setup ────────────────────────────────────────────────────────────────

    private PracticeMode _mode;
    private PracticeScope _scope;
    private TranslationDirection _direction;
    private int _goal;

    private List<Card> _library = [];
    private List<ReviewLog> _recentReviews = [];
    private List<Card> _selection = [];
    private bool _loading;

    // ── Session ──────────────────────────────────────────────────────────────

    private List<Card> _sessionCards = [];
    private List<Card> _pool = [];
    private PracticeRecorder? _recorder;

    private StudySession? _study;
    private StudyStep _step;
    private Card? _card;
    private TranslationDirection _cardDirection;
    private DateTimeOffset _shownAt;
    private bool _flipped;
    private int _flipToken;
    private List<(Button Button, ChoiceOption Option)> _options = [];

    /// <summary>An answer given but not yet counted: it is counted when the user moves on, so "I was right" can still change it.</summary>
    private (ReviewOutcome Outcome, string? Answer)? _pending;

    private MatchBoard? _board;
    private readonly Dictionary<MatchTile, Button> _tiles = [];
    private DateTimeOffset _boardStartedAt;

    private TrueFalseQuestion? _question;
    private readonly Queue<Card> _trueFalseDeck = new();
    private readonly Dictionary<long, (Card Card, int Misses)> _trueFalseMisses = [];
    private int _score;
    private int _misses;

    private bool _newRecord;

    public PracticeView(CardRepository repository, SettingsService settings, MainWindow shell)
    {
        _repository = repository;
        _settings = settings;
        _shell = shell;
        _srs = new SrsEngine(settings.Current.IgnoreCooldownMinutes);

        InitializeComponent();

        _clock.Tick += (_, _) => OnClockTick();
        _advance.Tick += (_, _) =>
        {
            _advance.Stop();
            Continue();
        };

        BuildSetup();
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) => _advance.Stop();
    }

    /// <summary>True while a game is on screen (not the setup or the results).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Raised when a game starts or ends, so prompts can wait while it runs.</summary>
    public event EventHandler? PlayingChanged;

    // ═══ Setup ═══════════════════════════════════════════════════════════════

    private void BuildSetup()
    {
        _loading = true;

        var s = _settings.Current;
        _mode = s.PracticeMode;
        _scope = s.PracticeScope;
        _direction = s.PracticeDirection;
        _goal = Math.Clamp(s.PracticeGoal, 1, 2);
        CountStepper.Value = s.PracticeCount;

        foreach (var (mode, icon) in Modes)
        {
            var tile = new RadioButton
            {
                Style = (Style)FindResource("ModeTile"),
                Content = L.T($"Mode.{mode}"),
                Tag = mode,
                IsChecked = mode == _mode,
            };
            tile.SetValue(Ui.IconProperty, FindResource(icon));
            tile.SetValue(Ui.HintProperty, L.T($"Mode.{mode}.Note"));
            tile.Checked += (_, _) => { _mode = mode; UpdatePreview(); };
            tile.MouseDoubleClick += (_, _) => Start();
            _modeButtons.Add(tile);
            ModeGrid.Children.Add(tile);
        }

        Segments(ScopePanel, _scopeButtons, "Scope", Scopes.Select(v => (v, L.T($"Scope.{v}"))), _scope,
            value => { _scope = value; UpdatePreview(); });
        Segments(DirectionPanel, _directionButtons, "Direction", Directions.Select(v => (v, DirectionName(v))), _direction,
            value => { _direction = value; SyncDirection(); });
        Segments(PlayDirectionPanel, _playDirectionButtons, "PlayDirection", Directions.Select(v => (v, DirectionName(v))), _direction,
            value => { _direction = value; SyncDirection(); OnDirectionChangedInGame(); });
        Segments(GoalPanel, _goalButtons, "Goal", [(1, L.T("Goal.One")), (2, L.T("Goal.Two"))], _goal,
            value => _goal = value);

        KindPicker.ItemsSource = new[] { new KindFilter(null, L.T("Practice.AnyKind")) }
            .Concat(CardText.Kinds.Where(k => k != CardKind.None).Select(k => new KindFilter(k, CardText.KindName(k))))
            .ToList();
        KindPicker.SelectedIndex = 0;

        _loading = false;
    }

    private void Segments<T>(Panel panel, List<RadioButton> buttons, string group, IEnumerable<(T Value, string Label)> options,
        T selected, Action<T> onPick)
    {
        foreach (var (value, label) in options)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = group,
                Content = label,
                Tag = value,
                IsChecked = EqualityComparer<T>.Default.Equals(value, selected),
            };
            button.Checked += (_, _) =>
            {
                if (!_loading)
                    onPick(value);
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }
    }

    private static string DirectionName(TranslationDirection direction) => direction switch
    {
        TranslationDirection.BackToFront => L.T("Direction.BackToFront"),
        TranslationDirection.Random => L.T("Direction.Both"),
        _ => L.T("Direction.FrontToBack"),
    };

    /// <summary>The direction can be switched on the setup screen and during a game; both switches show the same.</summary>
    private void SyncDirection()
    {
        _loading = true;
        foreach (var button in _directionButtons.Concat(_playDirectionButtons))
            button.IsChecked = (TranslationDirection)button.Tag == _direction;
        _loading = false;
    }

    /// <summary>Re-reads the library. Called whenever the page is shown; a running game is left alone.</summary>
    public void Reload()
    {
        if (!IsLoaded || IsPlaying)
            return;

        _loading = true;

        var decks = _repository.GetDecks();
        var selectedDeck = (DeckPicker.SelectedItem as DeckFilter)?.Id;
        DeckPicker.ItemsSource = new[] { new DeckFilter(null, L.T("Sidebar.AllDecks")) }
            .Concat(decks.Select(d => new DeckFilter(d.Id, d.Name)))
            .ToList();
        DeckPicker.SelectedItem = DeckPicker.Items.OfType<DeckFilter>().FirstOrDefault(d => d.Id == selectedDeck)
                                  ?? DeckPicker.Items[0];

        _library = _repository.GetCards(new CardQuery { IncludeDeleted = false });
        _recentReviews = _repository.GetReviews(DateTimeOffset.Now.AddDays(-PracticeSelection.MistakeWindowDays));

        _loading = false;
        UpdatePreview();
    }

    private PracticeFilter Filter() => new()
    {
        DeckId = (DeckPicker.SelectedItem as DeckFilter)?.Id,
        Kind = (KindPicker.SelectedItem as KindFilter)?.Kind,
        Scope = _scope,
        Count = CountStepper.Value,
    };

    private void UpdatePreview()
    {
        if (_loading)
            return;

        var filter = Filter();
        _selection = PracticeSelection.Select(_library, filter, _recentReviews, DateTimeOffset.Now, _random);

        ScopeNote.Text = L.T($"ScopeNote.{_scope}");
        CountRow.Visibility = _scope == PracticeScope.All ? Visibility.Collapsed : Visibility.Visible;
        GoalRow.Visibility = IsStudyMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        DirectionRow.Visibility = _mode == PracticeMode.Match ? Visibility.Collapsed : Visibility.Visible;

        var needed = _mode is PracticeMode.Match or PracticeMode.TrueFalse ? 2 : 1;
        var enough = _selection.Count >= needed;

        SummaryText.Text = _selection.Count == 0
            ? L.T("Practice.NoCards")
            : L.F("Practice.Summary", L.Plural(_selection.Count, "Plural.Card"));
        SummaryNote.Text = _selection.Count > 0 && !enough ? L.F("Practice.TooFew", L.Plural(needed, "Plural.Card")) : "";
        SummaryNote.Visibility = SummaryNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = enough;
    }

    private static bool IsStudyMode(PracticeMode mode) => mode is not (PracticeMode.Match or PracticeMode.TrueFalse);

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void OnCountChanged(object? sender, EventArgs e) => UpdatePreview();

    private void OnStart(object sender, RoutedEventArgs e) => Start();

    private void Start()
    {
        if (!StartButton.IsEnabled)
            return;

        SavePreferences();

        var filter = Filter();
        var deckCards = PracticeSelection.Candidates(_library, filter with { Kind = null }).ToList();

        // Wrong options are drawn from the same deck when it is big enough to make them believable.
        _pool = deckCards.Count >= 8 ? deckCards : _library.Where(c => c.DeletedAt is null).ToList();
        _sessionCards = _selection.ToList();

        Play(_sessionCards);
    }

    private void SavePreferences()
    {
        var s = _settings.Current;
        if ((s.PracticeMode, s.PracticeScope, s.PracticeCount, s.PracticeDirection, s.PracticeGoal) ==
            (_mode, _scope, CountStepper.Value, _direction, _goal))
            return;

        var updated = s.Clone();
        updated.PracticeMode = _mode;
        updated.PracticeScope = _scope;
        updated.PracticeCount = CountStepper.Value;
        updated.PracticeDirection = _direction;
        updated.PracticeGoal = _goal;
        _settings.Save(updated);
    }

    // ═══ Playing ═════════════════════════════════════════════════════════════

    private void Play(IReadOnlyList<Card> cards)
    {
        _recorder = new PracticeRecorder(_repository, _srs);
        _newRecord = false;

        PlayTitle.Text = L.T($"Mode.{_mode}");
        PlayDirectionBox.Visibility = _mode == PracticeMode.Match ? Visibility.Collapsed : Visibility.Visible;
        ScoreText.Visibility = _mode == PracticeMode.TrueFalse ? Visibility.Visible : Visibility.Collapsed;
        TimerText.Visibility = IsStudyMode(_mode) ? Visibility.Collapsed : Visibility.Visible;
        StepChip.Visibility = Visibility.Collapsed;

        ShowPanel(PlayPanel);
        SetPlaying(true);
        _stopwatch.Restart();

        switch (_mode)
        {
            case PracticeMode.Match:
                StartMatch();
                break;

            case PracticeMode.TrueFalse:
                StartTrueFalse(cards);
                break;

            default:
                _study = new StudySession(cards, _mode, _goal);
                NextQuestion();
                break;
        }

        Focus();
    }

    private void SetPlaying(bool playing)
    {
        if (IsPlaying == playing)
            return;

        IsPlaying = playing;
        if (!playing)
        {
            _clock.Stop();
            _advance.Stop();
            _stopwatch.Stop();
        }

        PlayingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowPanel(FrameworkElement panel)
    {
        foreach (var p in new FrameworkElement[] { SetupPanel, PlayPanel, ResultPanel })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowSurface(FrameworkElement surface)
    {
        foreach (var s in new FrameworkElement[] { FlashSurface, QuestionSurface, MatchSurface, TrueFalseSurface })
            s.Visibility = s == surface ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetProgress(double fraction, string text)
    {
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(Math.Clamp(fraction, 0, 1), TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        ProgressText.Text = text;
    }

    private void Record(Card card, TranslationDirection direction, ReviewOutcome outcome, string? answer,
        DateTimeOffset shownAt, bool affectsSchedule = true)
    {
        try
        {
            _recorder?.Record(card, direction, outcome, answer, shownAt, DateTimeOffset.Now, affectsSchedule);
        }
        catch (Exception ex)
        {
            // A lost history row must not end the game.
            Log.Error("Failed to save a practice answer", ex);
        }
    }

    // ── Study modes: flashcards, choice, writing, learn ──────────────────────

    private void NextQuestion()
    {
        _advance.Stop();
        _pending = null;

        if (_study is null)
            return;

        SetProgress(_study.Progress, L.F("Practice.Progress", _study.MasteredCount, _study.Total));

        if (_study.Current is not { } item)
        {
            ShowResults(completed: true);
            return;
        }

        _card = item.Card;
        _cardDirection = PracticeCards.Resolve(_direction, _random);
        _shownAt = DateTimeOffset.Now;
        _step = _study.CurrentStep;

        AskCurrent();
    }

    /// <summary>Puts the current card on screen the way the current step asks it.</summary>
    private void AskCurrent()
    {
        if (_card is null)
            return;

        switch (_step)
        {
            case StudyStep.Recall:
                ShowFlashcard();
                break;

            case StudyStep.Choice:
                var options = PracticeCards.Options(_card, _cardDirection, _pool, _random);
                // With hardly any other cards there is nothing to choose between; ask in writing instead.
                if (options.Count >= 2)
                    ShowChoice(options);
                else
                    ShowWrite();
                break;

            default:
                ShowWrite();
                break;
        }

        if (_mode == PracticeMode.Learn)
        {
            StepChip.Visibility = Visibility.Visible;
            StepChipText.Text = L.T(_step == StudyStep.Choice ? "Mode.Choice" : "Mode.Write");
        }

        FadeIn(_step == StudyStep.Recall ? FlashCard : QuestionSurface);
    }

    private static void FadeIn(UIElement element) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(160)));

    /// <summary>A new direction applies at once to a question not yet answered, otherwise from the next one.</summary>
    private void OnDirectionChangedInGame()
    {
        if (!IsPlaying)
            return;

        if (_mode == PracticeMode.TrueFalse)
            return;

        if (_card is not null && _pending is null && !_flipped)
        {
            _cardDirection = PracticeCards.Resolve(_direction, _random);
            AskCurrent();
        }
    }

    // Flashcards

    private void ShowFlashcard()
    {
        ShowSurface(FlashSurface);
        _flipToken++;
        _flipped = false;
        FlashScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FlashScale.ScaleX = 1;
        RenderFlashFace();
    }

    private void RenderFlashFace()
    {
        if (_card is null)
            return;

        var showingFront = _flipped == (_cardDirection == TranslationDirection.BackToFront);

        FlashSide.Text = L.T(showingFront ? "Dict.Column.Word" : "Dict.Column.Translation");
        FlashText.Text = showingFront ? _card.Front : PracticeCards.BackText(_card);
        SetText(FlashTranscription, showingFront ? CardText.Transcription(_card) : null);
        SetText(FlashExample, _flipped ? _card.Example : null);
        FlashCard.SetResourceReference(Border.BackgroundProperty, _flipped ? "Brush.SurfaceAlt" : "Brush.Surface");
    }

    private void Flip()
    {
        if (_card is null)
            return;

        // A flip still turning when the card is graded must not turn the next card over.
        var token = ++_flipToken;
        var shrink = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        shrink.Completed += (_, _) =>
        {
            if (token != _flipToken)
                return;

            _flipped = !_flipped;
            RenderFlashFace();
            FlashScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        };
        FlashScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
    }

    private void Grade(bool knew)
    {
        if (_card is null || _study is null || _step != StudyStep.Recall)
            return;

        _pending = (knew ? ReviewOutcome.Correct : ReviewOutcome.DontKnow, null);
        Continue();
    }

    private void OnFlashClick(object sender, MouseButtonEventArgs e) => Flip();

    private void OnStillLearning(object sender, RoutedEventArgs e) => Grade(knew: false);

    private void OnKnow(object sender, RoutedEventArgs e) => Grade(knew: true);

    // Question card shared by choice and writing

    private void ShowQuestion(string caption)
    {
        if (_card is null)
            return;

        ShowSurface(QuestionSurface);
        QuestionCaption.Text = caption;
        QuestionText.Text = PracticeCards.Question(_card, _cardDirection);

        var asksFront = _cardDirection == TranslationDirection.FrontToBack;
        SetText(QuestionTranscription, asksFront ? CardText.Transcription(_card) : null);
        SetText(QuestionHint, asksFront ? _card.Hint : null);

        FeedbackPanel.Visibility = Visibility.Collapsed;
        ContinuePanel.Visibility = Visibility.Collapsed;
    }

    // Multiple choice

    private void ShowChoice(List<ChoiceOption> options)
    {
        _step = StudyStep.Choice;
        ShowQuestion(L.T("Practice.ChooseAnswer"));
        WritePanel.Visibility = Visibility.Collapsed;
        ChoicePanel.Visibility = Visibility.Visible;

        ChoicePanel.Children.Clear();
        _options = [];

        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var text = new TextBlock { Text = options[i].Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var key = new Border { Style = (Style)FindResource("Kbd"), Margin = new Thickness(0, 0, 12, 0), Child = new TextBlock { Text = (i + 1).ToString() } };
            DockPanel.SetDock(key, Dock.Left);

            var button = new Button
            {
                Style = (Style)FindResource("OptionButton"),
                Content = new DockPanel { Children = { key, text } },
            };
            button.Click += (_, _) => Pick(index);

            _options.Add((button, options[i]));
            ChoicePanel.Children.Add(button);
        }
    }

    private void Pick(int index)
    {
        if (_pending is not null || _step != StudyStep.Choice || index < 0 || index >= _options.Count)
            return;

        var (picked, option) = _options[index];

        foreach (var (button, o) in _options)
        {
            if (o.IsCorrect)
                Paint(button, "Brush.Selection", "Brush.Primary");
            else if (button == picked)
                Paint(button, "Brush.InfoSofter", "Brush.InfoStrong");
            else
                button.Opacity = 0.5;

            button.IsHitTestVisible = false;
        }

        _pending = (option.IsCorrect ? ReviewOutcome.Correct : ReviewOutcome.Wrong, option.Text);

        if (option.IsCorrect)
        {
            _advance.Interval = RightAnswerPause;
            _advance.Start();
        }
        else
        {
            ShowFeedback(ReviewOutcome.Wrong, userAnswer: null, matched: null);
        }
    }

    private static void Paint(Control control, string background, string border)
    {
        control.SetResourceReference(BackgroundProperty, background);
        control.SetResourceReference(BorderBrushProperty, border);
    }

    // Writing

    private void ShowWrite()
    {
        _step = StudyStep.Write;
        ShowQuestion(L.T("Practice.WriteAnswer"));
        ChoicePanel.Visibility = Visibility.Collapsed;
        WritePanel.Visibility = Visibility.Visible;
        WriteActions.Visibility = Visibility.Visible;

        WriteBox.IsReadOnly = false;
        WriteBox.Text = "";
        Dispatcher.BeginInvoke(() => WriteBox.Focus(), DispatcherPriority.Input);
    }

    private void Check()
    {
        if (_card is null || _pending is not null || _step != StudyStep.Write || string.IsNullOrWhiteSpace(WriteBox.Text))
            return;

        var also = _cardDirection == TranslationDirection.BackToFront
            ? Synonyms.FrontsSharingMeaning(_card, _library)
            : [];

        var result = new AnswerChecker(_settings.Current.Strictness).Check(_card, WriteBox.Text, _cardDirection, also);
        _pending = (result.Outcome, WriteBox.Text);
        ShowFeedback(result.Outcome, WriteBox.Text, result.MatchedAnswer, also);

        if (result.Outcome == ReviewOutcome.Correct)
        {
            _advance.Interval = RightAnswerPause;
            _advance.Start();
        }
    }

    private void DontKnow()
    {
        if (_card is null || _pending is not null || _step != StudyStep.Write)
            return;

        _pending = (ReviewOutcome.DontKnow, null);
        ShowFeedback(ReviewOutcome.DontKnow, userAnswer: null, matched: null);
    }

    private void OnCheck(object sender, RoutedEventArgs e) => Check();

    private void OnDontKnow(object sender, RoutedEventArgs e) => DontKnow();

    /// <summary>
    /// The checker only knows the answers written on the cards. When the user is sure their
    /// answer is right too — a synonym the card doesn't list — they get the benefit of the doubt.
    /// </summary>
    private void OnOverride(object sender, RoutedEventArgs e)
    {
        if (_pending is not { Outcome: ReviewOutcome.Wrong } pending)
            return;

        _pending = (ReviewOutcome.Correct, pending.Answer);
        Continue();
    }

    private void OnContinue(object sender, RoutedEventArgs e) => Continue();

    /// <summary>Same look as the prompt window: yellow for "got it", blue for "we'll come back to it".</summary>
    private void ShowFeedback(ReviewOutcome outcome, string? userAnswer, string? matched, IReadOnlyList<string>? synonyms = null)
    {
        if (_card is null)
            return;

        var success = StatisticsCalculator.IsCorrect(outcome);
        var answer = PracticeCards.Answer(_card, _cardDirection);

        WriteBox.IsReadOnly = true;
        WriteActions.Visibility = Visibility.Collapsed;
        FeedbackPanel.Visibility = Visibility.Visible;
        ContinuePanel.Visibility = Visibility.Visible;
        OverrideButton.Visibility = outcome == ReviewOutcome.Wrong && _step == StudyStep.Write ? Visibility.Visible : Visibility.Collapsed;

        FeedbackPanel.Background = (Brush)FindResource(success ? "Brush.Selection" : "Brush.InfoSofter");
        FeedbackBadge.Background = (Brush)FindResource(success ? "Brush.Primary" : "Brush.InfoStrong");
        FeedbackIcon.Foreground = success ? (Brush)FindResource("Brush.PrimaryText") : Brushes.White;
        FeedbackIcon.Data = (Geometry)FindResource(success ? "Icon.Check" : "Icon.Reset");
        FeedbackStatus.Foreground = (Brush)FindResource(success ? "Brush.YellowInk" : "Brush.InfoDeep");

        FeedbackStatus.Text = outcome switch
        {
            ReviewOutcome.Correct => L.T("Prompt.Correct"),
            ReviewOutcome.Typo => L.T("Prompt.Typo"),
            ReviewOutcome.DontKnow => L.T("Prompt.Remember"),
            _ => L.T("Prompt.Wrong"),
        };

        FeedbackYours.Inlines.Clear();
        if (outcome == ReviewOutcome.Wrong && !string.IsNullOrWhiteSpace(userAnswer))
        {
            FeedbackYours.Inlines.Add(L.T("Prompt.YourAnswer") + " ");
            FeedbackYours.Inlines.Add(new Run(userAnswer.Trim()) { TextDecorations = TextDecorations.Strikethrough });
            FeedbackYours.Visibility = Visibility.Visible;
        }
        else
        {
            FeedbackYours.Visibility = Visibility.Collapsed;
        }

        FeedbackAnswer.Text = matched is not null && synonyms?.Contains(matched) == true
            ? L.F("Prompt.Synonym", matched, answer)
            : outcome == ReviewOutcome.Correct && matched is not null ? matched : answer;

        SetText(FeedbackTranscription, CardText.Transcription(_card));
        SetText(FeedbackExample, _card.Example);

        if (!success || outcome == ReviewOutcome.Typo)
            Focus();
    }

    /// <summary>Counts the pending answer and moves to the next question.</summary>
    private void Continue()
    {
        _advance.Stop();

        if (_pending is not { } pending || _card is null || _study is null)
            return;

        _pending = null;
        Record(_card, _cardDirection, pending.Outcome, pending.Answer, _shownAt);
        _study.Answer(StatisticsCalculator.IsCorrect(pending.Outcome));
        NextQuestion();
    }

    // ── Match ────────────────────────────────────────────────────────────────

    private void StartMatch()
    {
        _board = new MatchBoard(PracticeSelection.Shuffle(_sessionCards, _random), _random);
        _boardStartedAt = DateTimeOffset.Now;

        ShowSurface(MatchSurface);
        MatchGrid.Children.Clear();
        _tiles.Clear();

        foreach (var tile in _board.Tiles)
        {
            var button = new Button
            {
                Style = (Style)FindResource("MatchTile"),
                Content = new TextBlock
                {
                    Text = tile.Text,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = tile.IsFront ? FontWeights.SemiBold : FontWeights.Normal,
                },
            };
            button.Click += (_, _) => PickTile(tile);
            _tiles[tile] = button;
            MatchGrid.Children.Add(button);
        }

        var best = _settings.Current.MatchBestMs;
        ProgressText.Text = best > 0 ? L.F("Practice.Best", Seconds(best)) : "";
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ProgressScale.ScaleX = 0;

        _stopwatch.Restart();
        _clock.Start();
        OnClockTick();
    }

    private void PickTile(MatchTile tile)
    {
        if (_board is null)
            return;

        var first = _board.Selected;
        var result = _board.Pick(tile);
        var button = _tiles[tile];

        switch (result)
        {
            case MatchPick.Selected:
                Paint(button, "Brush.Active", "Brush.Primary");
                break;

            case MatchPick.Deselected:
                ResetTile(button);
                break;

            case MatchPick.Matched:
                foreach (var matched in new[] { _tiles[first!], button })
                {
                    Paint(matched, "Brush.Selection", "Brush.Primary");
                    matched.IsHitTestVisible = false;
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(120) };
                    matched.BeginAnimation(OpacityProperty, fade);
                }

                var done = _board.Tiles.Count(t => t.IsMatched) / (double)_board.Tiles.Count;
                SetProgress(done, ProgressText.Text);

                if (_board.IsComplete)
                    FinishMatch();
                break;

            case MatchPick.Mismatched:
                FlashMismatch(_tiles[first!], button);
                break;
        }
    }

    private async void FlashMismatch(Button a, Button b)
    {
        Paint(a, "Brush.InfoSofter", "Brush.InfoStrong");
        Paint(b, "Brush.InfoSofter", "Brush.InfoStrong");

        await Task.Delay(420);

        foreach (var button in new[] { a, b })
        {
            // Unless the user has already picked it again in the meantime.
            if (_board?.Selected is { } selected && _tiles.GetValueOrDefault(selected) == button)
                continue;
            ResetTile(button);
        }
    }

    private static void ResetTile(Button button)
    {
        button.ClearValue(BackgroundProperty);
        button.ClearValue(BorderBrushProperty);
    }

    private void FinishMatch()
    {
        if (_board is null)
            return;

        var elapsed = (int)_stopwatch.ElapsedMilliseconds;
        _clock.Stop();
        _stopwatch.Stop();
        TimerText.Text = Seconds(elapsed);

        foreach (var card in _board.Cards)
            Record(card, TranslationDirection.FrontToBack, _board.WasMissed(card) ? ReviewOutcome.Wrong : ReviewOutcome.Correct,
                null, _boardStartedAt, affectsSchedule: false);

        // Only a full board is a fair race; a board of three pairs would make an unbeatable record.
        if (_board.Cards.Count == MatchBoard.DefaultPairs)
            _newRecord = SaveRecord(elapsed, s => s.MatchBestMs, (s, v) => s.MatchBestMs = v, lowerIsBetter: true);

        // Let the last pair fade out before the results replace the board.
        _advance.Stop();
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(450);
            if (IsPlaying && _board?.IsComplete == true)
                ShowResults(completed: true);
        });
    }

    // ── True or false ────────────────────────────────────────────────────────

    private void StartTrueFalse(IReadOnlyList<Card> cards)
    {
        _trueFalseDeck.Clear();
        _trueFalseMisses.Clear();
        _score = 0;
        _misses = 0;
        TfFeedback.Text = "";
        ScoreText.Text = L.F("Practice.ScoreValue", 0);

        var best = _settings.Current.TrueFalseBest;
        ProgressText.Text = best > 0 ? L.F("Practice.Best", best) : "";

        ShowSurface(TrueFalseSurface);
        NextTrueFalse();

        _stopwatch.Restart();
        _clock.Start();
        OnClockTick();
    }

    private void NextTrueFalse()
    {
        if (_trueFalseDeck.Count == 0)
        {
            // A fresh shuffle each lap, but never the card that was just asked straight away again.
            var lap = PracticeSelection.Shuffle(_sessionCards, _random);
            if (lap.Count > 1 && ReferenceEquals(lap[0], _question?.Card))
                (lap[0], lap[^1]) = (lap[^1], lap[0]);
            foreach (var card in lap)
                _trueFalseDeck.Enqueue(card);
        }

        var next = _trueFalseDeck.Dequeue();
        _question = PracticeCards.TrueFalse(next, PracticeCards.Resolve(_direction, _random), _pool, _random);
        _shownAt = DateTimeOffset.Now;

        TfQuestion.Text = _question.Question;
        SetText(TfTranscription, _question.Direction == TranslationDirection.FrontToBack ? CardText.Transcription(next) : null);
        TfShown.Text = _question.Shown;
        FadeIn(TfCard);
    }

    private void AnswerTrueFalse(bool saidTrue)
    {
        if (_question is not { } question || !IsPlaying || _mode != PracticeMode.TrueFalse)
            return;

        var right = saidTrue == question.IsTrue;
        Record(question.Card, question.Direction, right ? ReviewOutcome.Correct : ReviewOutcome.Wrong, question.Shown, _shownAt,
            affectsSchedule: false);

        if (right)
        {
            _score++;
            TfFeedback.Text = "";
        }
        else
        {
            _misses++;
            var previous = _trueFalseMisses.GetValueOrDefault(question.Card.Id);
            _trueFalseMisses[question.Card.Id] = (question.Card, previous.Misses + 1);
            TfFeedback.Text = $"{question.Question} — {PracticeCards.Answer(question.Card, question.Direction)}";
        }

        ScoreText.Text = L.F("Practice.ScoreValue", _score);
        PulseBorder(TfCard, right ? "Brush.Primary" : "Brush.InfoStrong");
        NextTrueFalse();
    }

    private static void PulseBorder(Border border, string brush)
    {
        border.SetResourceReference(Border.BorderBrushProperty, brush);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            border.ClearValue(Border.BorderBrushProperty);
        };
        timer.Start();
    }

    private void FinishTrueFalse()
    {
        _clock.Stop();
        _newRecord = _score > 0 && SaveRecord(_score, s => s.TrueFalseBest, (s, v) => s.TrueFalseBest = v, lowerIsBetter: false);
        ShowResults(completed: true);
    }

    private void OnTrue(object sender, RoutedEventArgs e) => AnswerTrueFalse(true);

    private void OnFalse(object sender, RoutedEventArgs e) => AnswerTrueFalse(false);

    // ── Clock ────────────────────────────────────────────────────────────────

    private void OnClockTick()
    {
        switch (_mode)
        {
            case PracticeMode.Match:
                TimerText.Text = Seconds((int)_stopwatch.ElapsedMilliseconds);
                break;

            case PracticeMode.TrueFalse:
                var left = TrueFalseDuration - _stopwatch.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    TimerText.Text = Clock(TimeSpan.Zero);
                    FinishTrueFalse();
                    return;
                }

                TimerText.Text = Clock(left);
                TimerText.SetResourceReference(TextBlock.ForegroundProperty, left.TotalSeconds <= 10 ? "Brush.InfoStrong" : "Brush.Text");
                ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ProgressScale.ScaleX = left / TrueFalseDuration;
                break;
        }
    }

    private static string Seconds(int milliseconds) =>
        $"{(milliseconds / 1000.0).ToString("0.0", L.Culture)} {L.T("Unit.Seconds")}";

    private static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private bool SaveRecord(int value, Func<AppSettings, int> read, Action<AppSettings, int> write, bool lowerIsBetter)
    {
        var best = read(_settings.Current);
        var better = best == 0 || (lowerIsBetter ? value < best : value > best);
        if (!better)
            return false;

        var updated = _settings.Current.Clone();
        write(updated, value);
        _settings.Save(updated);
        return true;
    }

    // ═══ Results ═════════════════════════════════════════════════════════════

    private void ShowResults(bool completed)
    {
        var elapsed = _stopwatch.Elapsed;
        SetPlaying(false);
        ShowPanel(ResultPanel);

        List<TroubleRow> trouble;
        var showSrsNote = false;

        switch (_mode)
        {
            case PracticeMode.Match when _board is not null:
                ResultIcon.Data = (Geometry)FindResource(_newRecord ? "Icon.Trophy" : "Icon.Grid");
                ResultTitle.Text = L.T(_newRecord ? "Result.Record" : "Result.MatchDone");
                ResultNote.Text = L.Plural(_board.Cards.Count, "Plural.Card");
                Tiles((Seconds((int)elapsed.TotalMilliseconds), L.T("Result.Time")),
                    (_board.Mistakes.ToString(L.Culture), L.T("Result.Mistakes")),
                    (_settings.Current.MatchBestMs > 0 ? Seconds(_settings.Current.MatchBestMs) : "—", L.T("Result.Best")));
                trouble = _board.Cards.Where(_board.WasMissed).Select(c => Trouble(c, null)).ToList();
                RetryMistakesButton.Visibility = Visibility.Collapsed;
                break;

            case PracticeMode.TrueFalse:
                ResultIcon.Data = (Geometry)FindResource(_newRecord ? "Icon.Trophy" : "Icon.Zap");
                ResultTitle.Text = L.T(_newRecord ? "Result.Record" : "Result.TimeUp");
                ResultNote.Text = L.Plural(_score + _misses, "Plural.Answer");
                Tiles((_score.ToString(L.Culture), L.T("Result.Score")),
                    (_misses.ToString(L.Culture), L.T("Result.Mistakes")),
                    (_settings.Current.TrueFalseBest > 0 ? _settings.Current.TrueFalseBest.ToString(L.Culture) : "—", L.T("Result.Best")));
                trouble = _trueFalseMisses.Values.OrderByDescending(m => m.Misses).Select(m => Trouble(m.Card, m.Misses)).ToList();
                RetryMistakesButton.Visibility = trouble.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                break;

            default:
                var study = _study!;
                ResultIcon.Data = (Geometry)FindResource(completed ? "Icon.Check" : "Icon.Flame");
                ResultTitle.Text = L.T(completed ? "Result.Learned" : "Result.Stopped");
                ResultNote.Text = completed
                    ? $"{L.Plural(study.Total, "Plural.Card")} · {Clock(elapsed)}"
                    : L.F("Result.StoppedNote", study.MasteredCount, study.Total);

                var accuracy = study.Answers == 0 ? "—" : $"{(study.Answers - study.Mistakes) * 100.0 / study.Answers:0}%";
                Tiles((accuracy, L.T("Result.Accuracy")),
                    (Clock(elapsed), L.T("Result.Time")),
                    (study.Mistakes.ToString(L.Culture), L.T("Result.Mistakes")));

                trouble = study.Troublesome.Select(i => Trouble(i.Card, i.Mistakes)).ToList();
                RetryMistakesButton.Visibility = trouble.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                showSrsNote = study.Mistakes > 0;
                break;
        }

        TroublesomeList.ItemsSource = trouble;
        TroublesomeBlock.Visibility = trouble.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultPerfect.Visibility = trouble.Count == 0 && completed ? Visibility.Visible : Visibility.Collapsed;
        ResultSrsNote.Visibility = showSrsNote ? Visibility.Visible : Visibility.Collapsed;
        AgainButton.Style = (Style)FindResource(RetryMistakesButton.Visibility == Visibility.Visible ? "Button.Base" : "Button.Primary");

        _shell.RefreshData();
    }

    private void Tiles(params (string Value, string Label)[] tiles)
    {
        (ResultValue1.Text, ResultLabel1.Text) = tiles[0];
        (ResultValue2.Text, ResultLabel2.Text) = tiles[1];
        (ResultValue3.Text, ResultLabel3.Text) = tiles[2];
    }

    private static TroubleRow Trouble(Card card, int? mistakes) => new(
        card.Front,
        CardText.Transcription(card) is { } t ? "  " + t : "",
        PracticeCards.BackText(card),
        mistakes is > 0 ? $"× {mistakes}" : "");

    private List<Card> TroublesomeCards() => _mode switch
    {
        PracticeMode.TrueFalse => _trueFalseMisses.Values.Select(m => m.Card).ToList(),
        PracticeMode.Match => _board?.Cards.Where(_board.WasMissed).ToList() ?? [],
        _ => _study?.Troublesome.Select(i => i.Card).ToList() ?? [],
    };

    private void OnRetryMistakes(object sender, RoutedEventArgs e)
    {
        var cards = PracticeSelection.Shuffle(TroublesomeCards(), _random);
        if (cards.Count == 0)
            return;

        // True or false needs more than one card to be a game; the mistakes are drilled as flashcards instead.
        if (_mode == PracticeMode.TrueFalse)
            SelectMode(PracticeMode.Flashcards);

        _sessionCards = cards;
        Play(cards);
    }

    private void OnAgain(object sender, RoutedEventArgs e) => Play(PracticeSelection.Shuffle(_sessionCards, _random));

    private void OnBackToSetup(object sender, RoutedEventArgs e) => BackToSetup();

    private void BackToSetup()
    {
        SetPlaying(false);
        ShowPanel(SetupPanel);
        Reload();
    }

    private void SelectMode(PracticeMode mode)
    {
        _mode = mode;
        _loading = true;
        foreach (var tile in _modeButtons)
            tile.IsChecked = (PracticeMode)tile.Tag == mode;
        _loading = false;
    }

    private void OnFinish(object sender, RoutedEventArgs e) => Finish();

    /// <summary>
    /// Leaving a study session early still shows what was done; leaving a timed game throws the
    /// round away (a half-played board has no meaningful time).
    /// </summary>
    private void Finish()
    {
        if (!IsPlaying)
            return;

        if (IsStudyMode(_mode) && (_study is { Answers: > 0 } || _pending is not null))
        {
            Continue();
            if (IsPlaying)
                ShowResults(completed: false);
        }
        else
        {
            BackToSetup();
        }
    }

    // ═══ Keyboard ════════════════════════════════════════════════════════════

    /// <summary>Called by the main window for every key press while this page is shown. True when handled.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        // Typing into some other box (a deck name in the sidebar) is none of our business.
        if (Keyboard.FocusedElement is TextBox box && box != WriteBox)
            return false;

        // An open drop-down owns Enter and the arrows; a closed one doesn't use them.
        if (Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true } or ComboBoxItem)
            return false;

        if (SetupPanel.Visibility == Visibility.Visible)
        {
            if (e.Key != Key.Enter)
                return false;

            Start();
            return true;
        }

        if (ResultPanel.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Enter when RetryMistakesButton.Visibility == Visibility.Visible:
                    OnRetryMistakes(this, e);
                    return true;
                case Key.Enter:
                    OnAgain(this, e);
                    return true;
                case Key.Escape:
                    BackToSetup();
                    return true;
                default:
                    return false;
            }
        }

        if (!IsPlaying)
            return false;

        if (e.Key == Key.Escape)
        {
            Finish();
            return true;
        }

        switch (_mode)
        {
            case PracticeMode.TrueFalse:
                switch (e.Key)
                {
                    case Key.Left or Key.D1 or Key.NumPad1 or Key.N:
                        AnswerTrueFalse(false);
                        return true;
                    case Key.Right or Key.D2 or Key.NumPad2 or Key.Y:
                        AnswerTrueFalse(true);
                        return true;
                }

                return false;

            case PracticeMode.Match:
                return false;
        }

        switch (_step)
        {
            case StudyStep.Recall:
                switch (e.Key)
                {
                    case Key.Space or Key.Up or Key.Down:
                        Flip();
                        return true;
                    case Key.Left or Key.D1 or Key.NumPad1:
                        Grade(knew: false);
                        return true;
                    case Key.Right or Key.D2 or Key.NumPad2:
                        Grade(knew: true);
                        return true;
                }

                return false;

            case StudyStep.Choice:
                if (e.Key == Key.Enter && _pending is not null)
                {
                    Continue();
                    return true;
                }

                var digit = e.Key switch
                {
                    >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
                    >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
                    _ => -1,
                };

                if (digit < 0)
                    return false;

                Pick(digit);
                return true;

            default:
                if (e.Key != Key.Enter)
                    return false;

                if (_pending is not null)
                    Continue();
                else
                    Check();
                return true;
        }
    }

    // ═══ Helpers ═════════════════════════════════════════════════════════════

    private static void SetText(TextBlock block, string? text)
    {
        block.Text = text ?? "";
        block.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed record DeckFilter(long? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record KindFilter(CardKind? Kind, string Label)
    {
        public override string ToString() => Label;
    }
}

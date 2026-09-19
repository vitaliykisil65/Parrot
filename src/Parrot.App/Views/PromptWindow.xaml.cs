using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Parrot.App.Branding;
using Parrot.App.Interop;
using Parrot.Core.Answers;
using Parrot.Core.Models;
using Parrot.Core.Scheduling;
using Parrot.Core.Settings;

namespace Parrot.App.Views;

public sealed partial class PromptWindow : Window
{
    private static readonly TimeSpan ResultLingerDuration = TimeSpan.FromSeconds(3.5);

    private readonly PromptRequest _request;
    private readonly AnswerChecker _checker;
    private readonly AppSettings _settings;

    private readonly Storyboard _countdown = new();
    private readonly DispatcherTimer _lingerTimer = new();

    private ReviewOutcome? _outcome;
    private string? _userAnswer;
    private bool _countdownPaused;

    public PromptWindow(PromptRequest request, AnswerChecker checker, AppSettings settings, string deckName)
    {
        _request = request;
        _checker = checker;
        _settings = settings;

        InitializeComponent();

        LogoImage.Source = LogoFactory.Image;
        DeckLabel.Text = deckName;
        QuestionText.Text = request.Question;

        if (!string.IsNullOrWhiteSpace(request.Card.Hint) && request.Direction == TranslationDirection.FrontToBack)
        {
            HintText.Text = request.Card.Hint;
            HintText.Visibility = Visibility.Visible;
        }

        _lingerTimer.Tick += (_, _) => { _lingerTimer.Stop(); Close(); };
    }

    /// <summary>
    /// Raised exactly once, when the prompt is finished. An untouched window reports
    /// <see cref="ReviewOutcome.Timeout"/>, and a dismissed one <see cref="ReviewOutcome.Ignored"/>;
    /// neither costs the card any rating.
    /// </summary>
    public event EventHandler<(ReviewOutcome Outcome, string? Answer)>? Completed;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Applied before the window is shown so it never takes focus, not even for a frame.
        if (!_settings.FocusInputOnShow)
            NativeMethods.MakeNonActivating(new WindowInteropHelper(this).Handle);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        ScreenPositioner.Position(this, _settings);
        PlayEntrance();
        StartCountdown();

        if (_settings.FocusInputOnShow)
        {
            Activate();
            AnswerBox.Focus();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdown.Stop(this);
        _lingerTimer.Stop();

        // Closing without an answer is not a failure — the user was simply busy.
        Completed?.Invoke(this, (_outcome ?? ReviewOutcome.Ignored, _userAnswer));

        base.OnClosed(e);
    }

    // ── Entrance and countdown ───────────────────────────────────────────────

    private void PlayEntrance()
    {
        var fromTop = _settings.Anchor is ScreenAnchor.TopLeft or ScreenAnchor.TopCenter or ScreenAnchor.TopRight;
        var offset = fromTop ? -24 : 24;

        SlideTransform.Y = offset;

        var slide = new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));

        SlideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    private void StartCountdown()
    {
        if (_settings.DisplaySeconds <= 0)
        {
            // "Wait until I answer" mode: no bar, no timeout.
            CountdownScale.ScaleX = 0;
            return;
        }

        var animation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(_settings.DisplaySeconds));
        Storyboard.SetTarget(animation, CountdownScale);
        Storyboard.SetTargetProperty(animation, new PropertyPath(ScaleTransform.ScaleXProperty));

        _countdown.Children.Add(animation);
        _countdown.Completed += OnCountdownElapsed;
        _countdown.Begin(this, isControllable: true);
    }

    private void OnCountdownElapsed(object? sender, EventArgs e)
    {
        if (_outcome is not null)
            return;

        _outcome = ReviewOutcome.Timeout;
        Close();
    }

    /// <summary>
    /// Stops the clock as soon as the user starts typing. Snatching the window away
    /// mid-answer would be the single most annoying thing this app could do.
    /// </summary>
    private void OnAnswerChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_countdownPaused || _settings.DisplaySeconds <= 0 || AnswerBox.Text.Length == 0)
            return;

        _countdownPaused = true;
        _countdown.Pause(this);

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        CountdownBar.BeginAnimation(OpacityProperty, fade);
    }

    // ── Answering ────────────────────────────────────────────────────────────

    private void OnAnswerKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when _outcome is null:
                Check();
                e.Handled = true;
                break;

            case Key.Enter:
                Close();
                e.Handled = true;
                break;

            case Key.Escape:
                Dismiss();
                e.Handled = true;
                break;
        }
    }

    private void OnCheck(object sender, RoutedEventArgs e) => Check();

    private void OnDontKnow(object sender, RoutedEventArgs e)
    {
        _userAnswer = null;
        ShowResult(ReviewOutcome.DontKnow);
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Dismiss();

    private void OnContinue(object sender, RoutedEventArgs e) => Close();

    private void Dismiss()
    {
        _outcome ??= ReviewOutcome.Ignored;
        Close();
    }

    private void Check()
    {
        _userAnswer = AnswerBox.Text;
        var result = _checker.Check(_request.Card, _userAnswer, _request.Direction, _request.AlsoAccepted);
        ShowResult(result.Outcome, result.MatchedAnswer);
    }

    private void ShowResult(ReviewOutcome outcome, string? matched = null)
    {
        if (_outcome is not null)
            return;

        _outcome = outcome;

        _countdown.Stop(this);
        CountdownBar.Opacity = 0;

        AnswerBox.IsReadOnly = true;
        ActionPanel.Visibility = Visibility.Collapsed;
        ContinueButton.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Visible;

        (ResultStatus.Text, ResultStatus.Foreground) = outcome switch
        {
            ReviewOutcome.Correct => ("Правильно", Swatch("Brush.Success")),
            ReviewOutcome.Typo => ("Майже — зараховано, але пишеться так:", Swatch("Brush.Gold")),
            ReviewOutcome.DontKnow => ("Запам'ятовуємо:", Swatch("Brush.TextMuted")),
            _ => ("Неправильно", Swatch("Brush.Danger")),
        };

        // On a correct answer the user does not need to be told the answer they just gave;
        // showing the full variant list is still useful, so we show it for typos and misses.
        ResultAnswer.Text = outcome == ReviewOutcome.Correct
            ? matched ?? _request.Answer
            : _request.Answer;

        // A synonym from another card is accepted, but this card still wants its own word learned.
        if (matched is not null && _request.AlsoAccepted.Contains(matched))
            ResultAnswer.Text = $"{matched} — теж так; ця картка: {_request.Answer}";

        if (!string.IsNullOrWhiteSpace(_request.Card.Example))
        {
            ResultExample.Text = _request.Card.Example;
            ResultExample.Visibility = Visibility.Visible;
        }

        _lingerTimer.Interval = ResultLingerDuration;
        _lingerTimer.Start();
    }

    private Brush Swatch(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;
}

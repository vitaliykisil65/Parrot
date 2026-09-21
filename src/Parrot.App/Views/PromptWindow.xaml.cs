using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Parrot.App.Branding;
using Parrot.App.Interop;
using Parrot.App.Localization;
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

    private readonly DispatcherTimer _countdownTimer = new();
    private readonly DispatcherTimer _lingerTimer = new();
    private AnimationClock? _countdownClock;

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

        _countdownTimer.Tick += OnCountdownElapsed;
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
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeNonActivating(handle);
            HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        ScreenPositioner.Position(this, _settings);
        PlayEntrance();
        StartCountdown();

        // The result panel makes the card taller; keep it pinned to its anchor instead of
        // letting it grow off the bottom of the screen.
        SizeChanged += (_, args) =>
        {
            if (args.HeightChanged)
                ScreenPositioner.Position(this, _settings);
        };

        if (_settings.FocusInputOnShow)
        {
            Activate();
            AnswerBox.Focus();
        }
    }

    /// <summary>
    /// A non-activating window never receives keyboard focus, not even when clicked — typed
    /// letters would keep going to whatever app was active before. The first click is the user
    /// saying "I'm answering now": answering WM_MOUSEACTIVATE with MA_ACTIVATE lets Windows
    /// activate the window as part of that click, which (unlike calling Activate() afterwards)
    /// is never refused by the foreground-lock rules.
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmMouseActivate)
            return IntPtr.Zero;

        NativeMethods.AllowActivation(hwnd);
        handled = true;
        return NativeMethods.MaActivate;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // After the result is shown the answer box is gone, so Enter and Esc are handled here.
        if (_outcome is not null && e.Key is Key.Enter or Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (_outcome is null && !AnswerBox.IsKeyboardFocused)
            AnswerBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopCountdown();
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
            CountdownTrack.Visibility = Visibility.Collapsed;
            return;
        }

        var duration = TimeSpan.FromSeconds(_settings.DisplaySeconds);

        // The bar is only a picture of the clock. The timeout itself runs on a plain timer, so
        // the window still goes away even if the animation never gets to tick.
        _countdownClock = new DoubleAnimation(1, 0, duration).CreateClock();
        CountdownScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _countdownClock);

        _countdownTimer.Interval = duration;
        _countdownTimer.Start();
    }

    private void StopCountdown()
    {
        _countdownTimer.Stop();
        _countdownClock?.Controller?.Stop();
    }

    private void OnCountdownElapsed(object? sender, EventArgs e)
    {
        _countdownTimer.Stop();

        if (_outcome is not null || _countdownPaused)
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
        _countdownTimer.Stop();
        _countdownClock?.Controller?.Pause();

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        CountdownTrack.BeginAnimation(OpacityProperty, fade);
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

        StopCountdown();
        CountdownTrack.Visibility = Visibility.Collapsed;

        AnswerBox.IsReadOnly = true;
        AnswerBox.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Collapsed;
        ContinuePanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Visible;

        // Yellow means "got it", blue means "we'll come back to this" — never an alarming red.
        var success = outcome is ReviewOutcome.Correct or ReviewOutcome.Typo;

        ResultPanel.Background = Swatch(success ? "Brush.Selection" : "Brush.InfoSofter");
        ResultBadge.Background = Swatch(success ? "Brush.Primary" : "Brush.InfoStrong");
        ResultIcon.Foreground = success ? Swatch("Brush.PrimaryText") : Brushes.White;
        ResultIcon.Data = (Geometry)FindResource(success ? "Icon.Check" : "Icon.Reset");
        ResultStatus.Foreground = Swatch(success ? "Brush.YellowInk" : "Brush.InfoDeep");

        ResultStatus.Text = outcome switch
        {
            ReviewOutcome.Correct => L.T("Prompt.Correct"),
            ReviewOutcome.Typo => L.T("Prompt.Typo"),
            ReviewOutcome.DontKnow => L.T("Prompt.Remember"),
            _ => L.T("Prompt.Wrong"),
        };

        NextNote.Text = L.T(success ? "Prompt.LaterNote" : "Prompt.SoonNote");

        if (outcome == ReviewOutcome.Wrong && !string.IsNullOrWhiteSpace(_userAnswer))
        {
            ResultYours.Inlines.Clear();
            ResultYours.Inlines.Add(L.T("Prompt.YourAnswer") + " ");
            ResultYours.Inlines.Add(new System.Windows.Documents.Run(_userAnswer.Trim())
            {
                TextDecorations = TextDecorations.Strikethrough,
            });
            ResultYours.Visibility = Visibility.Visible;
        }

        // On a correct answer the user does not need to be told the answer they just gave;
        // showing the full variant list is still useful, so we show it for typos and misses.
        ResultAnswer.Text = outcome == ReviewOutcome.Correct
            ? matched ?? _request.Answer
            : _request.Answer;

        // A synonym from another card is accepted, but this card still wants its own word learned.
        if (matched is not null && _request.AlsoAccepted.Contains(matched))
            ResultAnswer.Text = L.F("Prompt.Synonym", matched, _request.Answer);

        if (!string.IsNullOrWhiteSpace(_request.Card.Example))
        {
            ResultExample.Text = _request.Card.Example;
            ResultExample.Visibility = Visibility.Visible;
        }

        // Keyboard focus moves off the (now hidden) answer box so Enter still means "next".
        ContinueButton.Focus();

        _lingerTimer.Interval = ResultLingerDuration;
        _lingerTimer.Start();
    }

    private Brush Swatch(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;
}

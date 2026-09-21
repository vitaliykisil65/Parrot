using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Parrot.App.Localization;
using Parrot.Core.Data;
using Parrot.Core.Statistics;

namespace Parrot.App.Views;

public sealed record DayBar(string Label, string ToolTip, double CorrectHeight, double MissedHeight, double IgnoredHeight)
{
    public Visibility EmptyVisibility =>
        CorrectHeight + MissedHeight + IgnoredHeight == 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed record DifficultyRow(string Label, int Count, double Share, Brush Brush, string ToolTip);

public sealed record HardCardRow(string Front, string Back, string Answers, double BarWidth, Brush BarBrush,
    string DifficultyTip, string Accuracy);

/// <summary>Scales a 0…1 share onto an available width: <c>[share, width] → pixels</c>.</summary>
public sealed class Fraction : IMultiValueConverter
{
    public static Fraction Converter { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double share, double width] ? Math.Max(0, share * width) : 0.0;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed partial class StatisticsView : UserControl
{
    /// <summary>Tallest bar in the activity chart, in pixels; leaves room for the day labels.</summary>
    private const double ChartHeight = 124;

    private const int HardestShown = 5;

    private readonly CardRepository _repository;
    private StatisticsReport? _report;

    public StatisticsView(CardRepository repository)
    {
        _repository = repository;
        InitializeComponent();
    }

    public void Reload()
    {
        var cards = _repository.GetCards(new CardQuery { IncludeDeleted = false });
        _report = StatisticsCalculator.Calculate(cards, _repository.GetReviews(), DateTimeOffset.Now);

        EmptyHint.Visibility = _report.AllTime.Shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = L.F("Stats.Subtitle", L.Plural(_report.Last30Days.Shown, "Plural.Prompt"));

        ShowTiles(_report);
        ShowDaily(_report);
        ShowDifficulty(_report);
        ShowHardest(_report);
    }

    private void OnRangeChanged(object sender, RoutedEventArgs e)
    {
        if (_report is not null)
            ShowDaily(_report);
    }

    private void ShowTiles(StatisticsReport report)
    {
        StreakValue.Text = report.CurrentStreak.ToString(L.Culture);
        StreakUnit.Text = L.PluralWord(report.CurrentStreak, "Plural.Day");
        StreakNote.Text = report.BestStreak > report.CurrentStreak
            ? L.F("Streak.Record", L.Plural(report.BestStreak, "Plural.Day"))
            : report.CurrentStreak > 0 ? L.T("Streak.IsRecord") : L.T("Streak.Hint");

        (Accuracy7Value.Text, Accuracy7Note.Text) = Accuracy(report.Last7Days);
        (Accuracy30Value.Text, Accuracy30Note.Text) = Accuracy(report.Last30Days);

        LearnedValue.Text = report.LearnedCards.ToString(L.Culture);
        LearnedOf.Text = L.F("Stats.LearnedOf", report.TotalCards);

        LearnedBar.Tag = report.TotalCards == 0 ? 0.0 : (double)report.LearnedCards / report.TotalCards;
    }

    private void ShowDaily(StatisticsReport report)
    {
        var days = Range7.IsChecked == true ? report.Daily.TakeLast(7).ToList() : report.Daily.ToList();

        var peak = Math.Max(1, days.Max(d => d.Total));
        var scale = ChartHeight / peak;
        var lastIndex = days.Count - 1;
        var step = days.Count <= 7 ? 1 : 7;

        DailyChart.ItemsSource = days
            .Select((day, i) => new DayBar(
                // A label every week, counted back from today, keeps the axis readable.
                Label: i == lastIndex ? L.T("Stats.Today") : (lastIndex - i) % step == 0 ? day.Day.ToString(step == 1 ? "ddd" : "d MMM", L.Culture) : "",
                ToolTip: DayToolTip(day),
                CorrectHeight: day.Correct * scale,
                MissedHeight: day.Missed * scale,
                IgnoredHeight: day.Ignored * scale))
            .ToList();

        var today = report.Today;
        TodayNote.Text = today.Shown == 0
            ? L.T("Stats.TodayEmpty")
            : L.F("Stats.TodaySummary", L.Plural(today.Shown, "Plural.Card"), today.Correct, today.Missed, today.Ignored);
    }

    private void ShowDifficulty(StatisticsReport report)
    {
        var rows = new List<DifficultyRow>
        {
            new(L.T("Stats.NewCards"), report.NewCards, 0, Res("Brush.Skipped"), L.T("Stats.NewCardsTip")),
        };

        rows.AddRange(report.Difficulty.Select((bucket, i) => new DifficultyRow(
            // The bucket labels in Core are Ukrainian log-friendly names; the UI uses its own.
            L.T($"Stats.Bucket{Math.Min(i, 3)}"), bucket.Count, 0, Res($"Brush.Difficulty{Math.Min(i, 3)}"),
            L.F("Stats.BucketTip", bucket.MinPercent, Math.Min(bucket.MaxPercent, 100)))));

        var peak = Math.Max(1, rows.Max(r => r.Count));
        DifficultyList.ItemsSource = rows.Select(r => r with { Share = (double)r.Count / peak }).ToList();

        var seen = report.Difficulty.Sum(b => b.Count);
        DifficultyNote.Text = seen == 0
            ? L.T("Stats.DifficultyEmpty")
            : L.T("Stats.DifficultyNote");
    }

    private void ShowHardest(StatisticsReport report)
    {
        HardestList.ItemsSource = report.Hardest
            .Take(HardestShown)
            .Select(card => new HardCardRow(
                card.Front,
                string.Join(", ", card.AcceptedAnswers),
                L.Plural(card.Schedule.TotalAnswers, "Plural.Answer"),
                Math.Max(6, card.Schedule.DifficultyPercent / 100.0 * 88),
                Res($"Brush.Difficulty{CardRow.DifficultyLevel(card.Schedule.DifficultyPercent)}"),
                L.F("Stats.DifficultyTip", card.Schedule.DifficultyPercent),
                $"{card.Schedule.Accuracy * 100:0}%"))
            .ToList();

        HardestEmpty.Visibility = report.Hardest.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    private static (string Value, string Note) Accuracy(PeriodStats period) =>
        period.Accuracy is { } accuracy
            ? ($"{accuracy * 100:0}%", L.Plural(period.Answered, "Plural.Answer"))
            : ("—", L.T("Stats.NoAnswers"));

    private static string DayToolTip(DailyActivity day)
    {
        var date = day.Day.ToString("dddd, d MMMM", L.Culture);

        return day.Total == 0
            ? L.F("Stats.DayEmpty", date)
            : L.F("Stats.DayTip", date, day.Correct, day.Missed, day.Ignored);
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}

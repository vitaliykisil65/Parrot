using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Parrot.App.Branding;
using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Statistics;

namespace Parrot.App.Views;

public sealed record DayBar(string Label, string ToolTip, double CorrectHeight, double MissedHeight, double IgnoredHeight);

public sealed record DifficultyRow(string Label, int Count, double Share, Brush Brush, string ToolTip);

public sealed record HardCardRow(string Front, string Back, string Difficulty, string Accuracy);

/// <summary>Scales a 0…1 share onto an available width: <c>[share, width] → pixels</c>.</summary>
public sealed class Fraction : IMultiValueConverter
{
    public static Fraction Converter { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double share, double width] ? Math.Max(0, share * width) : 0.0;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed partial class StatisticsWindow : Window
{
    /// <summary>Tallest bar in the activity chart, in pixels; leaves room for the day labels.</summary>
    private const double ChartHeight = 120;

    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    private readonly CardRepository _repository;

    public StatisticsWindow(CardRepository repository)
    {
        _repository = repository;

        InitializeComponent();

        Icon = LogoFactory.Image;
        LogoImage.Source = LogoFactory.Image;

        Reload();
    }

    public void RefreshIfVisible()
    {
        if (IsVisible)
            Reload();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        var cards = _repository.GetCards(new CardQuery { IncludeDeleted = false });
        var report = StatisticsCalculator.Calculate(cards, _repository.GetReviews(), DateTimeOffset.Now);

        EmptyHint.Visibility = report.AllTime.Shown == 0 ? Visibility.Visible : Visibility.Collapsed;

        ShowTiles(report);
        ShowDaily(report);
        ShowDifficulty(report);
        ShowHardest(report);
    }

    private void ShowTiles(StatisticsReport report)
    {
        StreakValue.Text = report.CurrentStreak.ToString(Ukrainian);
        StreakNote.Text = report.BestStreak > report.CurrentStreak
            ? $"рекорд — {Days(report.BestStreak)}"
            : report.CurrentStreak > 0 ? "це ваш рекорд" : "відповідайте щодня";

        (Accuracy7Value.Text, Accuracy7Note.Text) = Accuracy(report.Last7Days);
        (Accuracy30Value.Text, Accuracy30Note.Text) = Accuracy(report.Last30Days);

        LearnedValue.Text = report.LearnedCards.ToString(Ukrainian);
        LearnedNote.Text = $"із {report.TotalCards} · нових {report.NewCards}";
    }

    private void ShowDaily(StatisticsReport report)
    {
        var peak = Math.Max(1, report.Daily.Max(d => d.Total));
        var scale = ChartHeight / peak;
        var lastIndex = report.Daily.Count - 1;

        DailyChart.ItemsSource = report.Daily
            .Select((day, i) => new DayBar(
                // A label every week, counted back from today, keeps the axis readable.
                Label: (lastIndex - i) % 7 == 0 ? day.Day.ToString("dd.MM", Ukrainian) : "",
                ToolTip: DayToolTip(day),
                CorrectHeight: day.Correct * scale,
                MissedHeight: day.Missed * scale,
                IgnoredHeight: day.Ignored * scale))
            .ToList();

        var today = report.Today;
        TodayNote.Text = today.Shown == 0
            ? "Сьогодні ще не було карток."
            : $"Сьогодні: {today.Shown} {Plural(today.Shown, "картка", "картки", "карток")} · правильно {today.Correct} · " +
              $"помилок {today.Missed} · пропущено {today.Ignored}";
    }

    private void ShowDifficulty(StatisticsReport report)
    {
        var seen = report.Difficulty.Sum(b => b.Count);
        var peak = Math.Max(1, report.Difficulty.Max(b => b.Count));

        // Green through red, matching how the card feels rather than an arbitrary palette.
        string[] brushes = ["Brush.Success", "Brush.Accent", "Brush.Gold", "Brush.Danger"];

        DifficultyList.ItemsSource = report.Difficulty
            .Select((bucket, i) => new DifficultyRow(
                bucket.Label,
                bucket.Count,
                (double)bucket.Count / peak,
                Swatch(brushes[Math.Min(i, brushes.Length - 1)]),
                $"Складність {bucket.MinPercent}–{Math.Min(bucket.MaxPercent, 100)}%"))
            .ToList();

        DifficultyNote.Text = seen == 0
            ? "Тут з'являться картки, які ви вже бачили."
            : $"Показано {seen} {Plural(seen, "картку", "картки", "карток")}, які вже траплялися. " +
              "Складність росте з кожною помилкою і падає з правильними відповідями.";
    }

    private void ShowHardest(StatisticsReport report)
    {
        HardestList.ItemsSource = report.Hardest
            .Select(card => new HardCardRow(
                card.Front,
                card.Back,
                $"{card.Schedule.DifficultyPercent}%",
                $"{card.Schedule.Accuracy * 100:0}% з {card.Schedule.TotalAnswers}"))
            .ToList();

        HardestEmpty.Visibility = report.Hardest.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    private static (string Value, string Note) Accuracy(PeriodStats period) =>
        period.Accuracy is { } accuracy
            ? ($"{accuracy * 100:0}%", $"{period.Answered} {Plural(period.Answered, "відповідь", "відповіді", "відповідей")}")
            : ("—", "ще немає відповідей");

    private static string DayToolTip(DailyActivity day)
    {
        var date = day.Day.ToString("dddd, d MMMM", Ukrainian);

        return day.Total == 0
            ? $"{date}\nбез карток"
            : $"{date}\nправильно: {day.Correct}\nпомилок: {day.Missed}\nпропущено: {day.Ignored}";
    }

    private static string Days(int count) => $"{count} {Plural(count, "день", "дні", "днів")}";

    /// <summary>Ukrainian has three plural forms: 1 картка, 2 картки, 5 карток (and 11–14 take the last).</summary>
    private static string Plural(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;

        if (mod100 is >= 11 and <= 14) return many;
        if (mod10 == 1) return one;
        if (mod10 is >= 2 and <= 4) return few;
        return many;
    }

    private Brush Swatch(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;
}

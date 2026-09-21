using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Parrot.App.Controls;
using Parrot.App.Localization;
using Parrot.Core.Data;
using Parrot.Core.Models;

namespace Parrot.App.Views;

/// <summary>One row of the dictionary table, flattened for display.</summary>
public sealed class CardRow
{
    public CardRow(Card card, Deck? deck, Brush deckBrush, FrameworkElement resources)
    {
        Card = card;
        Deck = deck;
        DeckBrush = deckBrush;

        var schedule = card.Schedule;
        var level = DifficultyLevel(schedule.DifficultyPercent);

        Back = string.Join(", ", card.AcceptedAnswers);
        DifficultyWidth = 6 + schedule.DifficultyPercent / 100.0 * 58;
        DifficultyBrush = (Brush)resources.FindResource($"Brush.Difficulty{level}");
        DifficultyTip = L.F("Card.DifficultyTip", schedule.DifficultyPercent, DifficultyName(level));

        var dueToday = card.DeletedAt is null && !card.IsSuspended && !schedule.IsNew &&
                       schedule.DueAt.LocalDateTime.Date <= DateTime.Today;
        NextBrush = (Brush)resources.FindResource(dueToday ? "Brush.Text" : "Brush.TextMuted");
    }

    /// <summary>"easy", "medium", "hard", "very hard".</summary>
    public static string DifficultyName(int level) => L.T($"Difficulty.Level{level}");

    public Card Card { get; }
    public Deck? Deck { get; }
    public Brush DeckBrush { get; }

    public string Front => Card.Front;
    public string Back { get; }

    public double DifficultyWidth { get; }
    public Brush DifficultyBrush { get; }
    public string DifficultyTip { get; }

    public Visibility BarVisibility => Card.Schedule.IsNew ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NewVisibility => Card.Schedule.IsNew ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Suspended and deleted cards stay in the list, dimmed.</summary>
    public double RowOpacity => Card.DeletedAt is not null || Card.IsSuspended ? 0.55 : 1;

    public string Next => NextLong(Card);
    public Brush NextBrush { get; }

    /// <summary>What screen readers and UI Automation announce for the row.</summary>
    public override string ToString() => $"{Card.Front} — {Back}";

    /// <summary>Four difficulty steps, matching the buckets on the statistics page.</summary>
    public static int DifficultyLevel(int percent) => percent switch
    {
        < 25 => 0,
        < 50 => 1,
        < 75 => 2,
        _ => 3,
    };

    public static string NextLong(Card card)
    {
        if (card.DeletedAt is not null) return L.T("Card.Status.Deleted");
        if (card.IsSuspended) return L.T("Card.Status.Suspended");
        if (card.Schedule.IsNew) return "—";

        var due = card.Schedule.DueAt.LocalDateTime;
        var today = DateTime.Today;

        if (due <= DateTime.Now) return L.T("Due.Now");
        if (due.Date == today) return L.F("Due.TodayAt", L.Time(due));
        if (due.Date == today.AddDays(1)) return L.F("Due.TomorrowAt", L.Time(due));

        var days = (due.Date - today).Days;
        return days < 14 ? L.F("Due.InDays", L.Plural(days, "Plural.Day")) : due.ToString(L.DayMonthFormat(), L.Culture);
    }

    public static string NextShort(Card card)
    {
        if (card.IsSuspended || card.DeletedAt is not null) return "—";

        var due = card.Schedule.DueAt.LocalDateTime;
        var today = DateTime.Today;

        if (due <= DateTime.Now) return L.T("Due.Now");
        if (due.Date == today) return L.Time(due);
        if (due.Date == today.AddDays(1)) return L.T("Due.Tomorrow");

        var days = (due.Date - today).Days;
        return days < 14 ? L.Plural(days, "Plural.Day") : due.ToString(L.DayMonthFormat(shortMonth: true), L.Culture);
    }
}

public sealed partial class DictionaryView : UserControl
{
    private enum Filter { All, Due, Hard, New, Suspended, Deleted }

    private readonly CardRepository _repository;
    private readonly MainWindow _shell;
    private readonly ObservableCollection<CardRow> _rows = [];

    private long? _deckId;
    private List<Deck> _decks = [];
    private Filter _filter = Filter.All;

    /// <summary>The card open in the editor; a new card has Id 0.</summary>
    private Card? _editing;

    private bool _reloading;

    public DictionaryView(CardRepository repository, MainWindow shell)
    {
        _repository = repository;
        _shell = shell;

        InitializeComponent();

        CardsList.ItemsSource = _rows;
        Loaded += (_, _) => Reload();
    }

    /// <summary>Raised when cards were added, removed or moved, so deck counters can update.</summary>
    public event EventHandler? CardsChanged;

    // ── Loading ──────────────────────────────────────────────────────────────

    public void SetDeck(long? deckId)
    {
        if (_deckId == deckId && _rows.Count > 0)
            return;

        _deckId = deckId;
        Reload();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void Reload()
    {
        if (!IsLoaded)
            return;

        _reloading = true;

        var selectedIds = CardsList.SelectedItems.OfType<CardRow>().Select(r => r.Card.Id).ToHashSet();

        _decks = _repository.GetDecks();
        var deckBrushes = _decks
            .Select((deck, i) => (deck.Id, Brush: (Brush)FindResource($"Brush.Deck{i % 4}")))
            .ToDictionary(x => x.Id, x => x.Brush);

        var cards = _repository.GetCards(new CardQuery
        {
            Search = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
            DeckId = _deckId,
            IncludeDeleted = true,
        });

        UpdateCounts(cards);

        _rows.Clear();
        foreach (var card in cards.Where(Matches))
        {
            var deck = _decks.FirstOrDefault(d => d.Id == card.DeckId);
            _rows.Add(new CardRow(card, deck, deckBrushes.GetValueOrDefault(card.DeckId, Brushes.Gray), this));
        }

        foreach (var row in _rows.Where(r => selectedIds.Contains(r.Card.Id)))
            CardsList.SelectedItems.Add(row);

        UpdateEmptyState();
        _reloading = false;

        if (_editing is null)
            UpdateDetail();
    }

    private void UpdateCounts(List<Card> cards)
    {
        var live = cards.Where(c => c.DeletedAt is null).ToList();
        var now = DateTimeOffset.Now;

        var due = live.Count(c => IsDue(c, now));
        var deckName = _deckId is { } id ? _decks.FirstOrDefault(d => d.Id == id)?.Name : null;

        SubtitleText.Text = (deckName is null ? "" : $"{deckName} · ") +
                            $"{L.Plural(live.Count, "Plural.Card")} · " +
                            (due == 0 ? L.T("Dict.NothingDue") : L.F("Dict.DueCount", due));

        Badge(FilterAll, live.Count);
        Badge(FilterDue, due);
        Badge(FilterHard, live.Count(IsHard));
        Badge(FilterNew, live.Count(c => c.Schedule.IsNew));
        Badge(FilterSuspended, live.Count(c => c.IsSuspended));

        var deleted = cards.Count - live.Count;
        Badge(FilterDeleted, deleted);
        FilterDeleted.Visibility = deleted > 0 || _filter == Filter.Deleted ? Visibility.Visible : Visibility.Collapsed;
        FilterSuspended.Visibility = live.Any(c => c.IsSuspended) || _filter == Filter.Suspended
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void Badge(ToggleButton chip, int count) => chip.SetValue(Ui.BadgeProperty, count.ToString());

    private bool Matches(Card card) => _filter switch
    {
        Filter.Deleted => card.DeletedAt is not null,
        _ when card.DeletedAt is not null => false,
        Filter.Due => IsDue(card, DateTimeOffset.Now),
        Filter.Hard => IsHard(card),
        Filter.New => card.Schedule.IsNew,
        Filter.Suspended => card.IsSuspended,
        _ => true,
    };

    private static bool IsDue(Card card, DateTimeOffset now) =>
        !card.IsSuspended && !card.Schedule.IsNew && card.Schedule.DueAt <= now;

    private static bool IsHard(Card card) => !card.Schedule.IsNew && card.Schedule.DifficultyPercent >= 50;

    private void UpdateEmptyState()
    {
        EmptyList.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyListText.Text = !string.IsNullOrWhiteSpace(SearchBox.Text)
            ? L.F("Dict.Empty.Search", SearchBox.Text.Trim())
            : _filter switch
            {
                Filter.Due => L.T("Dict.Empty.Due"),
                Filter.Hard => L.T("Dict.Empty.Hard"),
                Filter.New => L.T("Dict.Empty.New"),
                Filter.Suspended => L.T("Dict.Empty.Suspended"),
                Filter.Deleted => L.T("Dict.Empty.Deleted"),
                _ => L.T("Dict.Empty.Deck"),
            };
    }

    // ── Detail panel ─────────────────────────────────────────────────────────

    private List<CardRow> Selected() => CardsList.SelectedItems.OfType<CardRow>().ToList();

    private void ShowPanel(FrameworkElement panel)
    {
        foreach (var p in new FrameworkElement[] { EmptyPanel, ViewPanel, EditPanel, MultiPanel })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDetail()
    {
        var selection = Selected();

        switch (selection.Count)
        {
            case 0:
                ShowPanel(EmptyPanel);
                break;

            case 1:
                ShowCard(selection[0]);
                break;

            default:
                ShowPanel(MultiPanel);
                MultiTitle.Text = L.F("Multi.Title", L.Plural(selection.Count, "Plural.CardAccusative"));

                var deleted = selection.All(r => r.Card.DeletedAt is not null);
                MultiSuspendButton.Visibility = deleted ? Visibility.Collapsed : Visibility.Visible;
                MultiSuspendButton.Content = L.T(selection.All(r => r.Card.IsSuspended) ? "Schedule.Resume" : "Card.Suspend");
                MultiDeleteButton.Content = L.T(deleted ? "Card.Restore" : "Common.Delete");
                break;
        }
    }

    private void ShowCard(CardRow row)
    {
        ShowPanel(ViewPanel);

        var card = row.Card;
        var schedule = card.Schedule;

        DetailDeckDot.Fill = row.DeckBrush;
        DetailDeck.Text = row.Deck?.Name ?? "—";
        DetailFront.Text = card.Front;
        DetailBack.Text = row.Back;

        Section(DetailHintBlock, DetailHint, card.Hint);
        Section(DetailExampleBlock, DetailExample, card.Example);
        Section(DetailNotesBlock, DetailNotes, card.Notes);

        var tags = (card.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DetailTags.ItemsSource = tags;
        DetailTagsBlock.Visibility = tags.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var level = CardRow.DifficultyLevel(schedule.DifficultyPercent);
        (StatusBadgeText.Text, StatusBadge.Background, StatusBadgeText.Foreground) = card switch
        {
            { DeletedAt: not null } => (L.T("Card.Status.InBin"), Res("Brush.DangerSoft"), Res("Brush.Danger")),
            { IsSuspended: true } => (L.T("Card.Status.Suspended"), Res("Brush.Segmented"), Res("Brush.TextSecondary")),
            _ when schedule.IsNew => (L.T("Card.New"), Res("Brush.Active"), Res("Brush.YellowInk")),
            _ when level >= 2 => (CardRow.DifficultyName(level), Res("Brush.InfoSoft"), Res("Brush.InfoDeep")),
            _ => (CardRow.DifficultyName(level), Res("Brush.Segmented"), Res("Brush.TextSecondary")),
        };

        DetailProgress.Visibility = schedule.IsNew ? Visibility.Collapsed : Visibility.Visible;
        NewCardNote.Visibility = schedule.IsNew ? Visibility.Visible : Visibility.Collapsed;

        StatAnswers.Text = schedule.TotalAnswers.ToString();
        StatAccuracy.Text = schedule.TotalAnswers == 0 ? "—" : $"{schedule.Accuracy * 100:0}%";
        StatNext.Text = CardRow.NextShort(card);
        DifficultyPercentText.Text = $"{schedule.DifficultyPercent}%";
        DifficultyFill.Background = row.DifficultyBrush;
        DifficultyFill.Width = Math.Max(6, schedule.DifficultyPercent / 100.0 * 278);

        var deleted = card.DeletedAt is not null;
        EditButton.IsEnabled = !deleted;
        SuspendButton.Visibility = deleted ? Visibility.Collapsed : Visibility.Visible;
        SuspendButton.SetValue(Ui.IconProperty, FindResource(card.IsSuspended ? "Icon.Play" : "Icon.Pause"));
        SuspendButton.ToolTip = L.T(card.IsSuspended ? "Card.ResumeTip" : "Card.SuspendTip");
        DeleteButton.SetValue(Ui.IconProperty, FindResource(deleted ? "Icon.Undo" : "Icon.Trash"));
        DeleteButton.ToolTip = L.T(deleted ? "Card.Restore" : "Card.Delete");
    }

    private static void Section(FrameworkElement block, TextBlock text, string? value)
    {
        text.Text = value ?? "";
        block.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    // ── Editing ──────────────────────────────────────────────────────────────

    public void BeginAdd()
    {
        if (_decks.Count == 0)
            _decks = _repository.GetDecks();

        if (_decks.Count == 0)
        {
            _shell.ShowToast(L.T("Dict.NoDecks"));
            return;
        }

        BeginEdit(new Card { DeckId = _deckId ?? _decks[0].Id });
    }

    private void BeginEdit(Card card)
    {
        _editing = card;

        EditTitle.Text = L.T(card.Id == 0 ? "Edit.NewTitle" : "Edit.Title");
        EditError.Visibility = Visibility.Collapsed;

        DeckBox.ItemsSource = _decks;
        DeckBox.SelectedItem = _decks.FirstOrDefault(d => d.Id == card.DeckId) ?? _decks.FirstOrDefault();

        FrontBox.Text = card.Front;
        BackBox.Text = card.Back;
        HintBox.Text = card.Hint ?? "";
        ExampleBox.Text = card.Example ?? "";
        TagsBox.Text = card.Tags ?? "";
        NotesBox.Text = card.Notes ?? "";

        ShowPanel(EditPanel);
        Dispatcher.BeginInvoke(() => { FrontBox.Focus(); FrontBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SaveEdit()
    {
        if (_editing is not { } card)
            return;

        if (string.IsNullOrWhiteSpace(FrontBox.Text) || string.IsNullOrWhiteSpace(BackBox.Text))
        {
            EditError.Text = L.T("Edit.Required");
            EditError.Visibility = Visibility.Visible;
            (string.IsNullOrWhiteSpace(FrontBox.Text) ? FrontBox : BackBox).Focus();
            return;
        }

        if (DeckBox.SelectedItem is not Deck deck)
        {
            EditError.Text = L.T("Edit.PickDeck");
            EditError.Visibility = Visibility.Visible;
            return;
        }

        card.DeckId = deck.Id;
        card.Front = FrontBox.Text.Trim();
        card.Back = BackBox.Text.Trim();
        card.Hint = Optional(HintBox.Text);
        card.Example = Optional(ExampleBox.Text);
        card.Tags = Optional(TagsBox.Text);
        card.Notes = Optional(NotesBox.Text);

        var isNew = card.Id == 0;
        if (isNew)
            _repository.AddCard(card);
        else
            _repository.UpdateCard(card);

        _editing = null;

        // A card saved into another deck would vanish under the current filter; follow it.
        if (_deckId is { } filter && filter != deck.Id && isNew)
            _shell.ShowToast(L.F("Edit.AddedToDeck", deck.Name));

        Reload();
        SelectCard(card.Id);
        CardsChanged?.Invoke(this, EventArgs.Empty);

        if (isNew)
            _shell.ShowToast(L.F("Edit.Added", card.Front), L.T("Edit.AddAnother"), BeginAdd);
    }

    private void CancelEdit()
    {
        _editing = null;
        UpdateDetail();
        CardsList.Focus();
    }

    private void SelectCard(long id)
    {
        var row = _rows.FirstOrDefault(r => r.Card.Id == id);
        if (row is null)
            return;

        CardsList.SelectedItems.Clear();
        CardsList.SelectedItem = row;
        CardsList.ScrollIntoView(row);
    }

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Events ───────────────────────────────────────────────────────────────

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Reload();

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        _filter = sender switch
        {
            _ when sender == FilterDue => Filter.Due,
            _ when sender == FilterHard => Filter.Hard,
            _ when sender == FilterNew => Filter.New,
            _ when sender == FilterSuspended => Filter.Suspended,
            _ when sender == FilterDeleted => Filter.Deleted,
            _ => Filter.All,
        };

        Reload();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_reloading)
            return;

        // Picking another card abandons an unsaved edit, the same way clicking away does in a list app.
        _editing = null;
        UpdateDetail();
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected() is [{ Card.DeletedAt: null } row])
            BeginEdit(row.Card);
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Selected() is [{ Card.DeletedAt: null } row]:
                BeginEdit(row.Card);
                e.Handled = true;
                break;

            case Key.Delete:
                OnDelete(sender, e);
                e.Handled = true;
                break;
        }
    }

    private void OnEditKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveEdit();
            e.Handled = true;
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e) => BeginAdd();

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Selected() is [{ } row])
            BeginEdit(row.Card);
    }

    private void OnSaveEdit(object sender, RoutedEventArgs e) => SaveEdit();

    private void OnCancelEdit(object sender, RoutedEventArgs e) => CancelEdit();

    private void OnToggleSuspend(object sender, RoutedEventArgs e)
    {
        var selection = Selected().Where(r => r.Card.DeletedAt is null).ToList();
        if (selection.Count == 0)
            return;

        var suspend = !selection.All(r => r.Card.IsSuspended);

        foreach (var row in selection)
            _repository.SetSuspended(row.Card.Id, suspend);

        Reload();
        var count = selection.Count;
        _shell.ShowToast(count == 1
            ? L.T(suspend ? "Toast.SuspendedOne" : "Toast.ResumedOne")
            : L.F(suspend ? "Toast.Suspended" : "Toast.Resumed", L.Plural(count, "Plural.Card")));
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        var selection = Selected();
        if (selection.Count == 0)
            return;

        var confirmed = await _shell.ConfirmAsync(
            L.T("Reset.Title"),
            L.F("Reset.Text", L.Plural(selection.Count, "Plural.CardGenitive")),
            L.T("Reset.Confirm"), danger: true);

        if (!confirmed)
            return;

        foreach (var row in selection)
            _repository.ResetProgress(row.Card.Id);

        Reload();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var selection = Selected();
        if (selection.Count == 0)
            return;

        var ids = selection.Select(r => r.Card.Id).ToList();

        if (selection.All(r => r.Card.DeletedAt is not null))
        {
            foreach (var id in ids)
                _repository.Restore(id);

            Reload();
            CardsChanged?.Invoke(this, EventArgs.Empty);
            _shell.ShowToast(ids.Count == 1 ? L.T("Toast.RestoredOne") : L.F("Toast.Restored", L.Plural(ids.Count, "Plural.CardAccusative")));
            return;
        }

        foreach (var id in ids)
            _repository.SoftDelete(id);

        Reload();
        CardsChanged?.Invoke(this, EventArgs.Empty);

        _shell.ShowToast(ids.Count == 1 ? L.T("Toast.DeletedOne") : L.F("Toast.Deleted", L.Plural(ids.Count, "Plural.CardAccusative")), L.T("Toast.Undo"), () =>
        {
            foreach (var id in ids)
                _repository.Restore(id);

            Reload();
            CardsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    // ── Import and export ────────────────────────────────────────────────────

    private void OnMore(object sender, RoutedEventArgs e)
    {
        MoreButton.ContextMenu!.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = PlacementMode.Bottom;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var decks = _repository.GetDecks();
        if (decks.Count == 0)
        {
            _shell.ShowToast(L.T("Dict.NoDecks"));
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = L.T("Import.Title"),
            Filter = L.T("Import.Filter"),
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var target = _deckId is { } deckId ? decks.First(d => d.Id == deckId) : decks[0];

        try
        {
            var result = CsvCardIo.Parse(File.ReadAllText(dialog.FileName, Encoding.UTF8), target.Id, out var cards);
            _repository.AddCards(cards);

            Reload();
            CardsChanged?.Invoke(this, EventArgs.Empty);

            var message = L.F("Import.Done", L.Plural(result.Imported, "Plural.CardAccusative"), target.Name);
            if (result.Skipped > 0)
                message += " " + L.F("Import.Skipped", result.Skipped, result.SkippedRows[0]);

            _shell.ShowToast(message);
        }
        catch (IOException ex)
        {
            _shell.ShowToast(L.F("Import.Failed", ex.Message));
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = L.T("Export.Title"),
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"parrot-{DateTime.Now:yyyy-MM-dd}.csv",
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var cards = _rows.Select(r => r.Card).Where(c => c.DeletedAt is null).ToList();

        try
        {
            // UTF-8 with a BOM, otherwise Excel opens the Cyrillic columns as mojibake.
            File.WriteAllText(dialog.FileName, CsvCardIo.Export(cards), new UTF8Encoding(true));
            _shell.ShowToast(L.F("Export.Done", L.Plural(cards.Count, "Plural.CardAccusative")));
        }
        catch (IOException ex)
        {
            _shell.ShowToast(L.F("Export.Failed", ex.Message));
        }
    }
}

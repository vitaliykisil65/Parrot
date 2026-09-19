using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Parrot.App.Branding;
using Parrot.Core.Data;
using Parrot.Core.Models;
using Parrot.Core.Settings;

namespace Parrot.App.Views;

/// <summary>One row of the library grid, flattened for display.</summary>
public sealed class LibraryRow(Card card, string deckName)
{
    public Card Card { get; } = card;

    public string Front => Card.Front;
    public string Back => Card.Back;
    public string Tags => Card.Tags ?? "";
    public string Deck { get; } = deckName;

    public string Difficulty => Card.Schedule.IsNew ? "—" : $"{Card.Schedule.DifficultyPercent}%";

    public string Accuracy => Card.Schedule.TotalAnswers == 0
        ? "—"
        : $"{Card.Schedule.Accuracy * 100:0}% ({Card.Schedule.TotalAnswers})";

    public string Due
    {
        get
        {
            if (Card.DeletedAt is not null || Card.IsSuspended)
                return "—";

            if (Card.Schedule.IsNew)
                return "нова";

            var due = Card.Schedule.DueAt;
            if (due <= DateTimeOffset.Now)
                return "зараз";

            return due.Date == DateTime.Today ? $"сьогодні {due:HH:mm}" : due.ToString("dd.MM HH:mm");
        }
    }

    public string Status
    {
        get
        {
            if (Card.DeletedAt is not null) return "видалена";
            if (Card.IsSuspended) return "призупинена";
            if (Card.Schedule.IsLearned) return "вивчена";
            return "активна";
        }
    }
}

public sealed partial class LibraryWindow : Window
{
    private static readonly Deck AllDecks = new() { Id = 0, Name = "Усі колоди" };

    private readonly CardRepository _repository;
    private readonly ObservableCollection<LibraryRow> _rows = [];

    private List<long> _lastDeleted = [];
    private bool _loading;

    public LibraryWindow(CardRepository repository)
    {
        _repository = repository;

        InitializeComponent();

        Icon = LogoFactory.Image;
        CardsGrid.ItemsSource = _rows;

        ReloadDecks();
        Reload();
        UpdateActionState();
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? StatisticsRequested;

    public ImageSource Logo => LogoFactory.Image;

    /// <summary>Keeps the grid honest after a prompt changed a card's schedule.</summary>
    public void RefreshIfVisible()
    {
        if (IsVisible)
            Reload();
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private void ReloadDecks()
    {
        _loading = true;

        var decks = new List<Deck> { AllDecks };
        decks.AddRange(_repository.GetDecks());

        DeckFilter.ItemsSource = decks;
        DeckFilter.SelectedIndex = 0;

        _loading = false;
    }

    private void Reload()
    {
        var query = new CardQuery
        {
            Search = SearchBox.Text,
            DeckId = FilteredDeckId,
            IncludeDeleted = ShowDeleted.IsChecked == true,
        };

        var deckNames = _repository.GetDecks().ToDictionary(d => d.Id, d => d.Name);

        _rows.Clear();
        foreach (var card in _repository.GetCards(query))
            _rows.Add(new LibraryRow(card, deckNames.GetValueOrDefault(card.DeckId, "—")));

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var active = _rows.Count(r => r.Card is { IsSuspended: false, DeletedAt: null });
        var learned = _rows.Count(r => r.Card.Schedule.IsLearned);
        var newCards = _rows.Count(r => r.Card.Schedule.IsNew);

        StatusText.Text = $"Карток: {_rows.Count}   ·   активних: {active}   ·   нових: {newCards}   ·   вивчених: {learned}";
    }

    private void UpdateActionState()
    {
        var selection = SelectedRows();
        var any = selection.Count > 0;

        EditButton.IsEnabled = selection.Count == 1;
        DeleteButton.IsEnabled = any;
        ResetButton.IsEnabled = any;
        SuspendButton.IsEnabled = any;

        SuspendButton.Content = any && selection.All(r => r.Card.IsSuspended) ? "Відновити" : "Призупинити";
        UndoButton.Visibility = _lastDeleted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<LibraryRow> SelectedRows() => CardsGrid.SelectedItems.OfType<LibraryRow>().ToList();

    /// <summary>The deck the filter is pointing at, or null when "all decks" is selected.</summary>
    private long? FilteredDeckId =>
        DeckFilter.SelectedItem is Deck { Id: > 0 } deck ? deck.Id : null;

    // ── Events ───────────────────────────────────────────────────────────────

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
            Reload();
    }

    private void OnDeckFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            Reload();
    }

    private void OnRefreshRequested(object sender, RoutedEventArgs e) => Reload();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionState();

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnStatistics(object sender, RoutedEventArgs e) => StatisticsRequested?.Invoke(this, EventArgs.Empty);

    private void OnGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedRows().Count == 1)
            OnEdit(sender, e);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var decks = _repository.GetDecks();
        if (decks.Count == 0)
        {
            MessageBox.Show(this, "Спершу створіть колоду.", "Parrot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var card = new Card { DeckId = FilteredDeckId ?? decks[0].Id };

        if (new CardEditorWindow(card, decks) { Owner = this }.ShowDialog() == true)
        {
            _repository.AddCard(card);
            Reload();
        }
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (SelectedRows() is not [{ } row])
            return;

        var card = row.Card;
        var snapshot = (card.Front, card.Back, card.Hint, card.Example, card.Tags, card.Notes, card.DeckId);

        if (new CardEditorWindow(card, _repository.GetDecks()) { Owner = this }.ShowDialog() == true)
        {
            _repository.UpdateCard(card);
            Reload();
        }
        else
        {
            // The editor mutates the card in place, so a cancel has to put it back.
            (card.Front, card.Back, card.Hint, card.Example, card.Tags, card.Notes, card.DeckId) = snapshot;
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var selection = SelectedRows();
        if (selection.Count == 0)
            return;

        foreach (var row in selection)
            _repository.SoftDelete(row.Card.Id);

        _lastDeleted = selection.Select(r => r.Card.Id).ToList();
        Reload();
        UpdateActionState();
    }

    private void OnUndoDelete(object sender, RoutedEventArgs e)
    {
        foreach (var id in _lastDeleted)
            _repository.Restore(id);

        _lastDeleted = [];
        Reload();
        UpdateActionState();
    }

    private void OnToggleSuspend(object sender, RoutedEventArgs e)
    {
        var selection = SelectedRows();
        if (selection.Count == 0)
            return;

        var suspend = !selection.All(r => r.Card.IsSuspended);

        foreach (var row in selection)
            _repository.SetSuspended(row.Card.Id, suspend);

        Reload();
        UpdateActionState();
    }

    private void OnResetProgress(object sender, RoutedEventArgs e)
    {
        var selection = SelectedRows();
        if (selection.Count == 0)
            return;

        var confirm = MessageBox.Show(this,
            $"Скинути прогрес для {selection.Count} карток? Історію відповідей буде втрачено.",
            "Parrot", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        foreach (var row in selection)
            _repository.ResetProgress(row.Card.Id);

        Reload();
    }

    private void OnAddDeck(object sender, RoutedEventArgs e)
    {
        var deck = new Deck { Name = "Нова колода" };

        if (new DeckEditorWindow(deck) { Owner = this }.ShowDialog() != true)
            return;

        _repository.AddDeck(deck);
        ReloadDecks();
        Reload();
    }

    // ── Import and export ────────────────────────────────────────────────────

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var decks = _repository.GetDecks();
        if (decks.Count == 0)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Імпорт карток",
            Filter = "CSV або текст (*.csv;*.txt)|*.csv;*.txt|Усі файли (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var target = FilteredDeckId is { } deckId ? decks.First(d => d.Id == deckId) : decks[0];

        try
        {
            var result = CsvCardIo.Parse(File.ReadAllText(dialog.FileName, Encoding.UTF8), target.Id, out var cards);

            _repository.AddCards(cards);

            Reload();

            var message = new StringBuilder($"Імпортовано {result.Imported} карток у колоду «{target.Name}».");
            if (result.Skipped > 0)
                message.Append($"\nПропущено: {result.Skipped}.\n\n{string.Join('\n', result.Errors.Take(10))}");

            MessageBox.Show(this, message.ToString(), "Parrot", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Не вдалося прочитати файл:\n{ex.Message}", "Parrot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Експорт карток",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"parrot-{DateTime.Now:yyyy-MM-dd}.csv",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            // UTF-8 with a BOM, otherwise Excel opens the Cyrillic columns as mojibake.
            File.WriteAllText(dialog.FileName, CsvCardIo.Export(_rows.Select(r => r.Card)), new UTF8Encoding(true));
            MessageBox.Show(this, $"Збережено {_rows.Count} карток.", "Parrot",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Не вдалося записати файл:\n{ex.Message}", "Parrot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

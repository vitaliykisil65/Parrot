using System.Windows;
using Parrot.App.Branding;
using Parrot.Core.Models;

namespace Parrot.App.Views;

public sealed partial class CardEditorWindow : Window
{
    private readonly Card _card;

    public CardEditorWindow(Card card, IReadOnlyList<Deck> decks)
    {
        _card = card;

        InitializeComponent();
        Icon = LogoFactory.Image;

        Title = card.Id == 0 ? "Нова картка" : "Редагування картки";

        DeckBox.ItemsSource = decks;
        DeckBox.SelectedItem = decks.FirstOrDefault(d => d.Id == card.DeckId) ?? decks.FirstOrDefault();

        FrontBox.Text = card.Front;
        BackBox.Text = card.Back;
        HintBox.Text = card.Hint ?? "";
        ExampleBox.Text = card.Example ?? "";
        TagsBox.Text = card.Tags ?? "";
        NotesBox.Text = card.Notes ?? "";

        Loaded += (_, _) => FrontBox.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FrontBox.Text) || string.IsNullOrWhiteSpace(BackBox.Text))
        {
            ErrorText.Text = "Заповніть слово та переклад — без них картку не показати.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (DeckBox.SelectedItem is not Deck deck)
        {
            ErrorText.Text = "Оберіть колоду.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _card.DeckId = deck.Id;
        _card.Front = FrontBox.Text.Trim();
        _card.Back = BackBox.Text.Trim();
        _card.Hint = Optional(HintBox.Text);
        _card.Example = Optional(ExampleBox.Text);
        _card.Tags = Optional(TagsBox.Text);
        _card.Notes = Optional(NotesBox.Text);

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

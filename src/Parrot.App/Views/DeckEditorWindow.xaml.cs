using System.Windows;
using Parrot.App.Branding;
using Parrot.Core.Models;

namespace Parrot.App.Views;

public sealed partial class DeckEditorWindow : Window
{
    private readonly Deck _deck;

    public DeckEditorWindow(Deck deck)
    {
        _deck = deck;

        InitializeComponent();
        Icon = LogoFactory.Image;

        Title = deck.Id == 0 ? "Нова колода" : "Редагування колоди";

        NameBox.Text = deck.Name;
        FrontLangBox.Text = deck.FrontLang;
        BackLangBox.Text = deck.BackLang;
        ActiveBox.IsChecked = deck.IsActive;

        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Вкажіть назву колоди.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _deck.Name = NameBox.Text.Trim();
        _deck.FrontLang = Fallback(FrontLangBox.Text, "en");
        _deck.BackLang = Fallback(BackLangBox.Text, "uk");
        _deck.IsActive = ActiveBox.IsChecked == true;

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

using Parrot.App.Localization;
using Parrot.Core.Models;

namespace Parrot.App.Views;

/// <summary>How card fields read on screen, the same way in every view.</summary>
public static class CardText
{
    /// <summary>Symbols of English pronunciation that no keyboard has, offered as buttons next to the field.</summary>
    public static IReadOnlyList<string> IpaSymbols { get; } =
        ["ˈ", "ˌ", "ː", "ə", "ɪ", "ʊ", "æ", "ʌ", "ɒ", "ɔ", "ɑ", "ɜ", "θ", "ð", "ʃ", "ʒ", "ŋ", "ɡ"];

    public static IReadOnlyList<CardKind> Kinds { get; } =
        [CardKind.None, CardKind.Word, CardKind.Phrase, CardKind.PhrasalVerb, CardKind.Idiom];

    /// <summary>"ˈdedlaɪn" becomes "/ˈdedlaɪn/"; text already in slashes or brackets is kept as typed.</summary>
    public static string? Transcription(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        return text[0] is '/' or '[' ? text : $"/{text}/";
    }

    public static string? Transcription(Card card) => Transcription(card.Transcription);

    public static string KindName(CardKind kind) => L.T($"Kind.{kind}");
}

/// <summary>An item for a kind picker.</summary>
public sealed record KindOption(CardKind Value, string Label)
{
    public override string ToString() => Label;
}

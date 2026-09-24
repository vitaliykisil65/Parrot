using Parrot.Core.Answers;
using Parrot.Core.Models;

namespace Parrot.Core.Practice;

/// <summary>One tile on the match board: either a word or a translation.</summary>
public sealed class MatchTile
{
    internal MatchTile(int index, Card card, string text, bool isFront)
    {
        Index = index;
        Card = card;
        Text = text;
        IsFront = isFront;
    }

    /// <summary>Position on the board.</summary>
    public int Index { get; }

    public Card Card { get; }
    public string Text { get; }
    public bool IsFront { get; }

    public bool IsMatched { get; internal set; }
}

public enum MatchPick
{
    /// <summary>First tile of a pair chosen; waiting for the second.</summary>
    Selected,

    /// <summary>The selected tile was clicked again and let go.</summary>
    Deselected,

    /// <summary>The two tiles belong together and leave the board.</summary>
    Matched,

    /// <summary>The two tiles don't belong together; both are let go.</summary>
    Mismatched,

    /// <summary>A tile that is already matched; nothing happens.</summary>
    Ignored,
}

/// <summary>
/// A board of word and translation tiles to pair up. Pure state — the timer, the record and the
/// animation are the UI's business.
/// </summary>
public sealed class MatchBoard
{
    public const int DefaultPairs = 6;

    private readonly HashSet<long> _missed = [];

    public MatchBoard(IEnumerable<Card> cards, Random random, int pairs = DefaultPairs)
    {
        Cards = Distinct(cards).Take(pairs).ToList();

        var tiles = Cards
            .SelectMany(card => new[] { (card, card.Front, true), (card, PracticeCards.BackText(card), false) })
            .ToList();

        Tiles = PracticeSelection.Shuffle(tiles, random)
            .Select((t, i) => new MatchTile(i, t.card, t.Item2, t.Item3))
            .ToList();
    }

    public IReadOnlyList<Card> Cards { get; }
    public IReadOnlyList<MatchTile> Tiles { get; }

    /// <summary>The first tile of the pair being made, if any.</summary>
    public MatchTile? Selected { get; private set; }

    public int Mistakes { get; private set; }
    public bool IsComplete => Tiles.All(t => t.IsMatched);

    /// <summary>Whether this card was involved in a wrong pairing before it was matched.</summary>
    public bool WasMissed(Card card) => _missed.Contains(card.Id);

    public MatchPick Pick(MatchTile tile)
    {
        if (tile.IsMatched)
            return MatchPick.Ignored;

        if (Selected is null)
        {
            Selected = tile;
            return MatchPick.Selected;
        }

        if (ReferenceEquals(Selected, tile))
        {
            Selected = null;
            return MatchPick.Deselected;
        }

        var first = Selected;
        Selected = null;

        if (ReferenceEquals(first.Card, tile.Card) && first.IsFront != tile.IsFront)
        {
            first.IsMatched = tile.IsMatched = true;
            return MatchPick.Matched;
        }

        Mistakes++;
        _missed.Add(first.Card.Id);
        _missed.Add(tile.Card.Id);
        return MatchPick.Mismatched;
    }

    /// <summary>
    /// Two cards that read the same on either side ("accurate" and "precise", both "точний")
    /// would make a board where a right pairing is rejected. Only one of them gets a place.
    /// </summary>
    private static IEnumerable<Card> Distinct(IEnumerable<Card> cards)
    {
        var fronts = new HashSet<string>(StringComparer.Ordinal);
        var backs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var card in cards)
        {
            var meanings = card.AcceptedAnswers.Select(AnswerNormalizer.Normalize).Where(m => m.Length > 0).ToList();

            if (!fronts.Add(AnswerNormalizer.Normalize(card.Front)) || meanings.Any(backs.Contains))
                continue;

            backs.UnionWith(meanings);
            yield return card;
        }
    }
}

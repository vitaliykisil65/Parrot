using System.Text;
using Parrot.Core.Models;

namespace Parrot.Core.Data;

/// <param name="SkippedRows">1-based numbers of the rows that lacked a front or back side.</param>
public sealed record ImportResult(int Imported, int Skipped, IReadOnlyList<int> SkippedRows);

/// <summary>
/// Reads and writes decks as CSV, so cards can be prepared in a spreadsheet and the
/// library is never a one-way door.
/// </summary>
public static class CsvCardIo
{
    /// <summary>
    /// The column order of an export, and of an import without a header row. New columns only
    /// ever go at the end, so files written by older versions still read the same way.
    /// </summary>
    private static readonly string[] Header = ["Front", "Back", "Hint", "Example", "Tags", "Transcription", "Kind"];

    private static readonly Dictionary<CardKind, string> KindNames = new()
    {
        [CardKind.Word] = "word",
        [CardKind.Phrase] = "phrase",
        [CardKind.PhrasalVerb] = "phrasal verb",
        [CardKind.Idiom] = "idiom",
    };

    public static string Export(IEnumerable<Card> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));

        foreach (var card in cards)
        {
            sb.AppendLine(string.Join(',',
                Quote(card.Front), Quote(card.Back), Quote(card.Hint), Quote(card.Example), Quote(card.Tags),
                Quote(card.Transcription), Quote(KindNames.GetValueOrDefault(card.Kind))));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses CSV text into cards. Rows missing a front or back side are reported rather
    /// than silently dropped — a half-imported deck is worse than a clear error.
    /// </summary>
    public static ImportResult Parse(string csv, long deckId, out List<Card> cards)
    {
        cards = [];
        var skippedRows = new List<int>();
        var skipped = 0;

        var rows = ParseRows(csv);
        if (rows.Count == 0)
            return new ImportResult(0, 0, skippedRows);

        var hasHeader = LooksLikeHeader(rows[0]);
        var columns = hasHeader ? MapColumns(rows[0]) : DefaultColumns();
        var start = hasHeader ? 1 : 0;

        for (var i = start; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
                continue;

            var front = Field(row, columns, "Front");
            var back = Field(row, columns, "Back");

            if (front is null || back is null)
            {
                skipped++;
                skippedRows.Add(i + 1);
                continue;
            }

            cards.Add(new Card
            {
                DeckId = deckId,
                Front = front,
                Back = back,
                Hint = Field(row, columns, "Hint"),
                Example = Field(row, columns, "Example"),
                Tags = Field(row, columns, "Tags"),
                Transcription = Field(row, columns, "Transcription"),
                Kind = ParseKind(Field(row, columns, "Kind")),
            });
        }

        return new ImportResult(cards.Count, skipped, skippedRows);
    }

    private static string? Field(List<string> row, Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index) && index < row.Count && !string.IsNullOrWhiteSpace(row[index])
            ? row[index].Trim()
            : null;

    private static Dictionary<string, int> DefaultColumns() =>
        Header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// With a header row the columns can come in any order, and any of them but Front and Back
    /// can be left out. Unknown columns are ignored.
    /// </summary>
    private static Dictionary<string, int> MapColumns(List<string> header)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (Header.Contains(name, StringComparer.OrdinalIgnoreCase))
                columns.TryAdd(name, i);
        }

        // "Front,Back" and nothing recognisable after them still means the classic layout.
        columns.TryAdd("Back", 1);
        return columns;
    }

    /// <summary>Accepts the English names written by <see cref="Export"/> and their Ukrainian equivalents.</summary>
    public static CardKind ParseKind(string? value)
    {
        var text = value?.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');

        return text switch
        {
            null or "" => CardKind.None,
            "word" or "слово" => CardKind.Word,
            "phrase" or "фраза" or "вираз" => CardKind.Phrase,
            "phrasal verb" or "phrasalverb" or "phrasal" or "фразове дієслово" => CardKind.PhrasalVerb,
            "idiom" or "ідіома" => CardKind.Idiom,
            _ => CardKind.None,
        };
    }

    private static bool LooksLikeHeader(List<string> row) =>
        row.Count > 0 && row[0].Trim().Equals("Front", StringComparison.OrdinalIgnoreCase) ||
        row.Any(c => c.Trim().Equals("Front", StringComparison.OrdinalIgnoreCase)) &&
        row.Any(c => c.Trim().Equals("Back", StringComparison.OrdinalIgnoreCase));

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }

    private static List<List<string>> ParseRows(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];

            if (inQuotes)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;

                // Tab counts as a separator too — that is what you get pasting out of Excel.
                // Semicolon deliberately does not: it separates alternative answers inside
                // the Back field ("run; flee").
                case ',' or '\t':
                    row.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    break;

                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;

                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}

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
    private static readonly string[] Header = ["Front", "Back", "Hint", "Example", "Tags"];

    public static string Export(IEnumerable<Card> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));

        foreach (var card in cards)
        {
            sb.AppendLine(string.Join(',',
                Quote(card.Front), Quote(card.Back), Quote(card.Hint), Quote(card.Example), Quote(card.Tags)));
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

        var start = LooksLikeHeader(rows[0]) ? 1 : 0;

        for (var i = start; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
                continue;

            if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[1]))
            {
                skipped++;
                skippedRows.Add(i + 1);
                continue;
            }

            cards.Add(new Card
            {
                DeckId = deckId,
                Front = row[0].Trim(),
                Back = row[1].Trim(),
                Hint = Field(row, 2),
                Example = Field(row, 3),
                Tags = Field(row, 4),
            });
        }

        return new ImportResult(cards.Count, skipped, skippedRows);
    }

    private static string? Field(List<string> row, int index) =>
        index < row.Count && !string.IsNullOrWhiteSpace(row[index]) ? row[index].Trim() : null;

    private static bool LooksLikeHeader(List<string> row) =>
        row.Count > 0 && row[0].Trim().Equals("Front", StringComparison.OrdinalIgnoreCase);

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

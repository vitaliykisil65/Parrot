using System.Globalization;
using System.Text;

namespace Parrot.Core.Answers;

/// <summary>
/// Reduces an answer to a comparable form. The user is typing into a popup while doing
/// something else, so casing, stray punctuation and articles must never cost them a point.
/// </summary>
public static class AnswerNormalizer
{
    private static readonly string[] LeadingNoise = ["the ", "a ", "an ", "to "];

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var sb = new StringBuilder(input.Length);
        var lastWasSpace = true; // trims leading whitespace as a side effect

        foreach (var ch in input.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            // Apostrophes inside words are noise ("don't" == "dont"), but the various
            // Unicode dashes are not: "re-read" and "reread" both normalize to "reread".
            if (IsApostrophe(ch) || ch is '-' or '‐' or '‑' or '–' or '—')
                continue;

            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.OtherPunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation)
                continue;

            sb.Append(char.ToLowerInvariant(ch));
            lastWasSpace = false;
        }

        var result = sb.ToString().TrimEnd();
        return StripLeadingNoise(result);
    }

    /// <summary>
    /// Drops an English article or infinitive marker. Only one is removed: "to the point"
    /// should keep its "the", because there the words carry meaning.
    /// </summary>
    private static string StripLeadingNoise(string value)
    {
        foreach (var prefix in LeadingNoise)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal) && value.Length > prefix.Length)
                return value[prefix.Length..];
        }

        return value;
    }

    private static bool IsApostrophe(char ch) => ch is '\'' or '‘' or '’' or '`' or '´';
}

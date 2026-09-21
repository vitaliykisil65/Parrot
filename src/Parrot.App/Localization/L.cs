using System.Globalization;
using System.Resources;

namespace Parrot.App.Localization;

/// <summary>A language the UI can be shown in.</summary>
public sealed record UiLanguage(string Code, string NativeName, string Culture);

/// <summary>
/// UI text lookup. Every string the user reads comes from Localization/Strings.resx (the
/// English source) or a Strings.&lt;code&gt;.resx translation next to it; adding a language is
/// adding one file and one line in <see cref="Known"/>.
/// </summary>
public static class L
{
    public const string DefaultLanguage = "en";

    private static readonly ResourceManager Strings =
        new("Parrot.App.Localization.Strings", typeof(L).Assembly);

    /// <summary>Languages the app knows about; only those with translations are offered.</summary>
    private static readonly UiLanguage[] Known =
    [
        new("en", "English", "en-US"),
        new("uk", "Українська", "uk-UA"),
    ];

    /// <summary>The culture for text and for dates and numbers.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static string Language { get; private set; } = DefaultLanguage;

    /// <summary>The default language plus every language that ships a translation.</summary>
    public static IReadOnlyList<UiLanguage> Available { get; } = Known
        .Where(l => l.Code == DefaultLanguage || HasTranslation(l.Code))
        .ToList();

    /// <summary>
    /// Switches the UI language. Windows opened afterwards use it. An empty or unknown code
    /// follows the Windows display language, and English when that has no translation.
    /// </summary>
    public static void Apply(string? language)
    {
        var choice = Find(language) ?? Find(CultureInfo.InstalledUICulture.TwoLetterISOLanguageName) ?? Available[0];

        Language = choice.Code;
        Culture = CultureInfo.GetCultureInfo(choice.Culture);

        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
    }

    private static UiLanguage? Find(string? code) =>
        Available.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>The text for <paramref name="key"/>; a missing key shows up as ⟦key⟧ rather than blank.</summary>
    public static string T(string key) =>
        Strings.GetString(key, Culture) ?? $"⟦{key}⟧";

    /// <summary>A formatted text: <c>F("Toast.Deleted", name)</c> for "Deck “{0}” deleted".</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(Culture, T(key), args);

    /// <summary>A short time in the UI culture: "22:15" in Ukrainian, "10:15 PM" in English.</summary>
    public static string Time(DateTime time) => time.ToString("t", Culture);

    public static string Time(DateTimeOffset time) => time.ToString("t", Culture);

    public static string Time(TimeSpan timeOfDay) => Time(DateTime.Today.Add(timeOfDay));

    /// <summary>
    /// The culture's day-and-month pattern: "d MMMM" in Ukrainian, "MMMM d" in English;
    /// <paramref name="shortMonth"/> abbreviates the month.
    /// </summary>
    public static string DayMonthFormat(bool shortMonth = false)
    {
        var pattern = Culture.DateTimeFormat.MonthDayPattern;
        return shortMonth ? pattern.Replace("MMMM", "MMM") : pattern;
    }

    /// <summary>"5 cards" — the number with the right plural form of the noun in <paramref name="key"/>.</summary>
    public static string Plural(int count, string key) =>
        string.Format(Culture, "{0} {1}", count, PluralWord(count, key));

    /// <summary>
    /// Just the noun. Resource values list the forms separated by '|': English takes two
    /// (card|cards), Ukrainian three (картка|картки|карток).
    /// </summary>
    public static string PluralWord(int count, string key)
    {
        var forms = T(key).Split('|');
        var index = Math.Min(PluralForm(count), forms.Length - 1);
        return forms[index];
    }

    private static int PluralForm(int count)
    {
        count = Math.Abs(count);

        switch (Culture.TwoLetterISOLanguageName)
        {
            case "uk":
                // 1, 21, 31 … take the first form; 2–4, 22–24 … the second; the rest, and 11–14, the third.
                var mod100 = count % 100;
                var mod10 = count % 10;
                if (mod100 is >= 11 and <= 14) return 2;
                if (mod10 == 1) return 0;
                if (mod10 is >= 2 and <= 4) return 1;
                return 2;

            default:
                return count == 1 ? 0 : 1;
        }
    }

    /// <summary>Translations are keyed by language ("en"), not region, so look for exactly that satellite.</summary>
    private static bool HasTranslation(string language)
    {
        try
        {
            return Strings.GetResourceSet(CultureInfo.GetCultureInfo(language), createIfNotExists: true, tryParents: false) is not null;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}

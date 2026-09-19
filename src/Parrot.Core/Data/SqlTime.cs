using System.Globalization;

namespace Parrot.Core.Data;

/// <summary>
/// Timestamps are stored as ISO-8601 UTC strings. Keeping them in UTC means plain string
/// comparison is also chronological comparison, which would not hold if the stored offset
/// changed with daylight saving.
/// </summary>
internal static class SqlTime
{
    public static string To(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    public static string? ToNullable(DateTimeOffset? value) => value is null ? null : To(value.Value);

    public static DateTimeOffset From(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
                                                                  DateTimeStyles.AdjustToUniversal).ToLocalTime();

    public static DateTimeOffset? FromNullable(string? value) => value is null ? null : From(value);
}

namespace Parrot.Core.Answers;

public static class Levenshtein
{
    /// <summary>
    /// Edit distance between two strings, giving up early once <paramref name="max"/> is
    /// exceeded (we only ever care about "within one or two typos").
    /// </summary>
    public static int Distance(string a, string b, int max)
    {
        if (a == b) return 0;
        if (a.Length == 0) return Math.Min(b.Length, max + 1);
        if (b.Length == 0) return Math.Min(a.Length, max + 1);
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > max)
                return max + 1;

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

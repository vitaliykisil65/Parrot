using System.IO;
using System.Reflection;

namespace Parrot.App.DevData;

/// <summary>
/// The 419-card word list used while developing. It is embedded only in Debug builds
/// (see Parrot.App.csproj), so a released Parrot starts with an empty dictionary.
/// </summary>
internal static class StarterDeck
{
    private const string ResourceName = "Parrot.App.DevData.StarterDeck.csv";

    /// <returns>The CSV, or null when this build does not carry it.</returns>
    public static string? Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

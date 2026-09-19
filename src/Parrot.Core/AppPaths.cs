namespace Parrot.Core;

public static class AppPaths
{
    /// <summary>Points the app at another data folder — handy for demos and manual testing.</summary>
    public const string DataDirectoryVariable = "PARROT_DATA_DIR";

    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Parrot");

    public static string DatabaseFile => Path.Combine(DataDirectory, "parrot.db");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}

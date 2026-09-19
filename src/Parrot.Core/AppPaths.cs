namespace Parrot.Core;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Parrot");

    public static string DatabaseFile => Path.Combine(DataDirectory, "parrot.db");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}

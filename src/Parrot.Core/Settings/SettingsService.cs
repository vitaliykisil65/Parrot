using System.Text.Json;
using System.Text.Json.Serialization;

namespace Parrot.Core.Settings;

/// <summary>Loads and persists <see cref="AppSettings"/> as JSON under %AppData%\Parrot.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    public SettingsService(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    /// <summary>Raised after new settings are stored, so live components can re-read them.</summary>
    public event EventHandler<AppSettings>? Changed;

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
            Current = settings;
        }

        Changed?.Invoke(this, settings);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupted settings file must never stop the app from starting; defaults are
            // always usable and the next Save overwrites the bad file.
        }

        return new AppSettings();
    }
}

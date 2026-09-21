using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Threading;
using Microsoft.Win32;
using Parrot.Core.Settings;
using Parrot.Core.Updates;

namespace Parrot.App.Services;

public enum UpdateStage { Idle, Available, Downloading, Installing, Failed }

public enum UpdateCheckResult { UpToDate, Available, Failed, Unavailable }

/// <summary>
/// Looks for a newer release on GitHub and installs it in place: downloads the installer,
/// checks it, runs it silently and quits so it can replace the exe. The installer starts
/// the new version when it is done.
/// </summary>
public sealed class UpdateService : IDisposable
{
    /// <summary>Points the check at another "latest release" JSON — for testing without GitHub.</summary>
    public const string SourceVariable = "PARROT_UPDATE_URL";

    /// <summary>Must match AppId in installer/Parrot.iss; Inno appends "_is1" to the key name.</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{9938137F-821B-4BE9-867E-57D438BFAF47}_is1";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly SettingsService _settings;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer = new();
    private readonly string? _source;
    private CancellationTokenSource? _download;

    public UpdateService(SettingsService settings)
    {
        _settings = settings;
        _source = ResolveSource();

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Parrot/{CurrentVersion.ToString(3)}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (_settings.Current.CheckForUpdates)
                await CheckAsync();
        };
    }

    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    /// <summary>False when the build knows no release source (a dev build without PARROT_UPDATE_URL).</summary>
    public bool IsEnabled => _source is not null;

    public UpdateStage Stage { get; private set; }
    public ReleaseInfo? Release { get; private set; }

    /// <summary>Download progress, 0 to 1.</summary>
    public double Progress { get; private set; }
    public long Downloaded { get; private set; }
    public string? Error { get; private set; }

    /// <summary>
    /// The installer can only replace a copy it installed itself. A portable exe or a dev
    /// build gets the release page instead.
    /// </summary>
    public bool CanInstallInPlace { get; } = IsInstalledCopy();

    /// <summary>What the sidebar card should show; null hides it.</summary>
    public UpdateStage? CardStage => Stage switch
    {
        UpdateStage.Available when Release is not null
            && UpdateCheck.ShouldOffer(CurrentVersion, Release, _settings.Current.DismissedUpdateVersion) => UpdateStage.Available,
        UpdateStage.Downloading or UpdateStage.Installing or UpdateStage.Failed => Stage,
        _ => null,
    };

    public event EventHandler? StateChanged;

    /// <summary>Raised once the installer is running; the app must quit so it can replace the exe.</summary>
    public event EventHandler? InstallerStarted;

    public void Start()
    {
        if (!IsEnabled)
            return;

        _timer.Interval = FirstCheckDelay;
        _timer.Start();
    }

    public async Task<UpdateCheckResult> CheckAsync()
    {
        if (_source is null)
            return UpdateCheckResult.Unavailable;

        // Never swap the release out from under a download that is already running.
        if (Stage is UpdateStage.Downloading or UpdateStage.Installing)
            return UpdateCheckResult.Available;

        try
        {
            var json = await _http.GetStringAsync(_source);
            var release = ReleaseParser.Parse(json);

            if (release is null || !UpdateCheck.IsNewer(CurrentVersion, release))
            {
                Release = null;
                SetStage(UpdateStage.Idle);
                return UpdateCheckResult.UpToDate;
            }

            if (Release?.Version != release.Version)
                Log.Info($"Доступна нова версія {release.Version.ToString(3)}");

            Release = release;
            if (Stage != UpdateStage.Failed)
                SetStage(UpdateStage.Available);
            return UpdateCheckResult.Available;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Offline, rate-limited or a bad response: try again at the next tick, quietly.
            Log.Info($"Не вдалося перевірити оновлення: {ex.Message}");
            return UpdateCheckResult.Failed;
        }
    }

    public async Task InstallAsync()
    {
        if (Release is not { } release || Stage is UpdateStage.Downloading or UpdateStage.Installing)
            return;

        if (!CanInstallInPlace)
        {
            OpenReleasePage();
            return;
        }

        _download = new CancellationTokenSource();
        Progress = 0;
        Downloaded = 0;
        Error = null;
        SetStage(UpdateStage.Downloading);

        string file;
        try
        {
            file = await DownloadAsync(release, _download.Token);
        }
        catch (OperationCanceledException) when (_download.IsCancellationRequested)
        {
            SetStage(UpdateStage.Available);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            Log.Error("Не вдалося завантажити оновлення", ex);
            Fail(ex is HttpRequestException or TaskCanceledException ? Localization.L.T("Update.ErrorNetwork") : Localization.L.T("Update.ErrorFile"));
            return;
        }
        finally
        {
            _download?.Dispose();
            _download = null;
        }

        SetStage(UpdateStage.Installing);

        try
        {
            Log.Info($"Запуск інсталятора {release.Version.ToString(3)}");
            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Log.Error("Не вдалося запустити інсталятор", ex);
            Fail(Localization.L.T("Update.ErrorStart"));
            return;
        }

        // Let the card show "installing" for a moment before the window disappears.
        await Task.Delay(700);
        InstallerStarted?.Invoke(this, EventArgs.Empty);
    }

    public void CancelDownload() => _download?.Cancel();

    /// <summary>Hides the card for this version; a later release brings it back.</summary>
    public void Dismiss()
    {
        if (Release is null)
            return;

        if (Stage == UpdateStage.Failed)
            Stage = UpdateStage.Available;

        var settings = _settings.Current.Clone();
        settings.DismissedUpdateVersion = Release.Version.ToString(3);
        _settings.Save(settings);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenReleasePage()
    {
        if (Release is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(Release.PageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Не вдалося відкрити сторінку релізу", ex);
        }
    }

    /// <summary>Removes installers left over from a previous update. Files still in use are skipped.</summary>
    public static void CleanUpDownloads()
    {
        try
        {
            if (!Directory.Exists(DownloadDirectory))
                return;

            foreach (var file in Directory.GetFiles(DownloadDirectory))
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp clutter is not worth a failed start.
        }
    }

    private static string DownloadDirectory => Path.Combine(Path.GetTempPath(), "Parrot-Update");

    private async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken token)
    {
        Directory.CreateDirectory(DownloadDirectory);
        var target = Path.Combine(DownloadDirectory, $"Parrot-Setup-{release.Version.ToString(3)}.exe");
        var partial = target + ".part";

        using (var response = await _http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.InstallerSize;

            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using var output = File.Create(partial);

            var buffer = new byte[81920];
            var lastReport = Stopwatch.StartNew();
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                Downloaded += read;

                if (lastReport.ElapsedMilliseconds >= 100 || Downloaded == total)
                {
                    Progress = total > 0 ? Math.Min(1, (double)Downloaded / total) : 0;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    lastReport.Restart();
                }
            }
        }

        Verify(partial, release);
        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>A truncated or tampered installer must never run.</summary>
    private static void Verify(string file, ReleaseInfo release)
    {
        var length = new FileInfo(file).Length;
        if (release.InstallerSize > 0 && length != release.InstallerSize)
            Reject(file, $"розмір {length} замість {release.InstallerSize}");

        if (release.Sha256 is null)
            return;

        using var stream = File.OpenRead(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Close();

        if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
            Reject(file, $"SHA-256 {hash} замість {release.Sha256}");
    }

    private static void Reject(string file, string reason)
    {
        File.Delete(file);
        throw new InvalidDataException($"Інсталятор не пройшов перевірку: {reason}");
    }

    private void Fail(string message)
    {
        Error = message;
        SetStage(UpdateStage.Failed);
    }

    private void SetStage(UpdateStage stage)
    {
        Stage = stage;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? ResolveSource()
    {
        if (Environment.GetEnvironmentVariable(SourceVariable) is { Length: > 0 } custom)
            return custom;

#if DEBUG
        return null;
#else
        var repository = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value;

        return string.IsNullOrWhiteSpace(repository)
            ? null
            : $"https://api.github.com/repos/{repository.Trim()}/releases/latest";
#endif
    }

    private static bool IsInstalledCopy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is not string location || Environment.ProcessPath is not { } exe)
                return false;

            return string.Equals(
                Path.GetFullPath(location).TrimEnd('\\'),
                Path.GetDirectoryName(Path.GetFullPath(exe))?.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _download?.Cancel();
        _http.Dispose();
    }
}

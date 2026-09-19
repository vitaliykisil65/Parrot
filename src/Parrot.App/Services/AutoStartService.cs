using System.IO;
using Microsoft.Win32;

namespace Parrot.App.Services;

/// <summary>
/// Registers the app in the per-user Run key. Chosen over a scheduled task because it
/// needs no elevation and the user can audit it from Task Manager's Startup tab.
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Parrot";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Contains("Parrot", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns false when the registry refused the change, so the UI can stay honest.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
                return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
                return false;

            // --tray tells the app to start silently instead of opening the library.
            key.SetValue(ValueName, $"\"{executable}\" --tray");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}

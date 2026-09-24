using System.Windows;
using Microsoft.Win32;
using Parrot.Core.Settings;

namespace Parrot.App.Services;

public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppTheme _current = AppTheme.System;
    private static bool _listening;

    /// <summary>Raised after the palette changed, so views holding resolved brushes can redraw.</summary>
    public static event EventHandler? Applied;

    public static void Apply(AppTheme theme)
    {
        _current = theme;
        StartListening();

        var dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark(),
        };

        var palette = new ResourceDictionary
        {
            Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };

        // The palette is always the first merged dictionary, so swapping it in place keeps
        // every DynamicResource lookup pointing at the right brushes.
        Application.Current.Resources.MergedDictionaries[0] = palette;
        Applied?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Apps in dark mode (Settings → Personalization → Colors → app mode).</summary>
    public static bool IsSystemDark() => ReadFlag("AppsUseLightTheme") == 0;

    private static int? ReadFlag(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) as int?;
    }

    private static void StartListening()
    {
        if (_listening)
            return;

        _listening = true;

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General)
                return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_current == AppTheme.System)
                    Apply(AppTheme.System);
            });
        };
    }
}

using System.Windows;
using Microsoft.Win32;
using Parrot.Core.Settings;

namespace Parrot.App.Services;

public static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
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
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}

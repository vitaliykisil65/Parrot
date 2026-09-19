using Parrot.Core.Settings;

namespace Parrot.Core.Tests;

public class AppSettingsTests
{
    private static DateTimeOffset At(int hour, int minute = 0, DayOfWeek day = DayOfWeek.Monday)
    {
        // 2026-09-21 is a Monday; shift from there to land on the requested weekday.
        var monday = new DateTimeOffset(2026, 9, 21, hour, minute, 0, TimeSpan.Zero);
        var offset = ((int)day - (int)DayOfWeek.Monday + 7) % 7;
        return monday.AddDays(offset);
    }

    [Fact]
    public void Quiet_hours_spanning_midnight_block_the_night_and_allow_the_day()
    {
        var settings = new AppSettings
        {
            QuietHoursEnabled = true,
            QuietFrom = TimeSpan.FromHours(22),
            QuietTo = TimeSpan.FromHours(9),
        };

        Assert.False(settings.IsWithinActiveWindow(At(23)));
        Assert.False(settings.IsWithinActiveWindow(At(3)));
        Assert.False(settings.IsWithinActiveWindow(At(8, 59)));
        Assert.True(settings.IsWithinActiveWindow(At(9)));
        Assert.True(settings.IsWithinActiveWindow(At(14)));
        Assert.True(settings.IsWithinActiveWindow(At(21, 59)));
        Assert.False(settings.IsWithinActiveWindow(At(22)));
    }

    [Fact]
    public void Quiet_hours_inside_a_single_day_block_only_that_slot()
    {
        var settings = new AppSettings
        {
            QuietHoursEnabled = true,
            QuietFrom = TimeSpan.FromHours(13),
            QuietTo = TimeSpan.FromHours(14),
        };

        Assert.True(settings.IsWithinActiveWindow(At(12)));
        Assert.False(settings.IsWithinActiveWindow(At(13, 30)));
        Assert.True(settings.IsWithinActiveWindow(At(14)));
    }

    [Fact]
    public void Disabling_quiet_hours_opens_the_whole_day()
    {
        var settings = new AppSettings { QuietHoursEnabled = false };

        Assert.True(settings.IsWithinActiveWindow(At(3)));
    }

    [Fact]
    public void Inactive_days_are_silent_regardless_of_the_hour()
    {
        var settings = new AppSettings
        {
            QuietHoursEnabled = false,
            ActiveDays = [DayOfWeek.Monday, DayOfWeek.Tuesday],
        };

        Assert.True(settings.IsWithinActiveWindow(At(12, day: DayOfWeek.Monday)));
        Assert.False(settings.IsWithinActiveWindow(At(12, day: DayOfWeek.Saturday)));
    }

    [Fact]
    public void Jitter_keeps_the_delay_around_the_configured_interval()
    {
        var settings = new AppSettings { IntervalMinutes = 20, JitterPercent = 25 };
        var random = new Random(Seed: 7);

        for (var i = 0; i < 500; i++)
        {
            var delay = settings.NextDelay(random).TotalMinutes;
            Assert.InRange(delay, 15, 25);
        }
    }

    [Fact]
    public void Zero_jitter_gives_an_exact_interval()
    {
        var settings = new AppSettings { IntervalMinutes = 20, JitterPercent = 0 };

        Assert.Equal(20, settings.NextDelay(new Random(1)).TotalMinutes, 6);
    }

    [Fact]
    public void Settings_round_trip_through_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"parrot-settings-{Guid.NewGuid():N}.json");

        try
        {
            var service = new SettingsService(path);
            var changed = service.Current.Clone();
            changed.IntervalMinutes = 7;
            changed.Theme = AppTheme.Dark;
            changed.ActiveDays = [DayOfWeek.Friday];
            service.Save(changed);

            var reloaded = new SettingsService(path).Current;

            Assert.Equal(7, reloaded.IntervalMinutes);
            Assert.Equal(AppTheme.Dark, reloaded.Theme);
            Assert.Equal([DayOfWeek.Friday], reloaded.ActiveDays);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_corrupted_settings_file_falls_back_to_defaults_instead_of_crashing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"parrot-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not json");

        try
        {
            Assert.Equal(new AppSettings().IntervalMinutes, new SettingsService(path).Current.IntervalMinutes);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

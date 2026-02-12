namespace VoiceType.Tests;

public class AppInfoTests
{
    [Fact]
    public void FormatUptime_ClampsNegativeToZero()
    {
        var formatted = AppInfo.FormatUptime(TimeSpan.FromSeconds(-5));

        Assert.Equal("00:00:00", formatted);
    }

    [Fact]
    public void FormatUptime_FormatsShortDurationAsHhMmSs()
    {
        var formatted = AppInfo.FormatUptime(new TimeSpan(0, 3, 4));

        Assert.Equal("00:03:04", formatted);
    }

    [Fact]
    public void FormatUptime_UsesTotalHoursForLongDuration()
    {
        var formatted = AppInfo.FormatUptime(new TimeSpan(days: 1, hours: 2, minutes: 5, seconds: 6));

        Assert.Equal("26:05:06", formatted);
    }
}

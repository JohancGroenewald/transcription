namespace VoiceType.Tests;

public class AppConfigTests
{
    [Fact]
    public void NormalizeOverlayDuration_ClampsToBounds()
    {
        Assert.Equal(AppConfig.MinOverlayDurationMs, AppConfig.NormalizeOverlayDuration(-1));
        Assert.Equal(1250, AppConfig.NormalizeOverlayDuration(1250));
        Assert.Equal(AppConfig.MaxOverlayDurationMs, AppConfig.NormalizeOverlayDuration(999_999));
    }

    [Fact]
    public void NormalizeOverlayOpacityPercent_ClampsToBounds()
    {
        Assert.Equal(AppConfig.MinOverlayOpacityPercent, AppConfig.NormalizeOverlayOpacityPercent(0));
        Assert.Equal(87, AppConfig.NormalizeOverlayOpacityPercent(87));
        Assert.Equal(AppConfig.MaxOverlayOpacityPercent, AppConfig.NormalizeOverlayOpacityPercent(500));
    }

    [Fact]
    public void NormalizeOverlayWidthPercent_ClampsToBounds()
    {
        Assert.Equal(AppConfig.MinOverlayWidthPercent, AppConfig.NormalizeOverlayWidthPercent(1));
        Assert.Equal(70, AppConfig.NormalizeOverlayWidthPercent(70));
        Assert.Equal(AppConfig.MaxOverlayWidthPercent, AppConfig.NormalizeOverlayWidthPercent(500));
    }

    [Fact]
    public void NormalizeOverlayFontSizePt_ClampsToBounds()
    {
        Assert.Equal(AppConfig.MinOverlayFontSizePt, AppConfig.NormalizeOverlayFontSizePt(1));
        Assert.Equal(14, AppConfig.NormalizeOverlayFontSizePt(14));
        Assert.Equal(AppConfig.MaxOverlayFontSizePt, AppConfig.NormalizeOverlayFontSizePt(500));
    }

    [Theory]
    [InlineData(null, AppConfig.DefaultPenHotkey)]
    [InlineData("", AppConfig.DefaultPenHotkey)]
    [InlineData("f20", "F20")]
    [InlineData("LaunchApp1", "LaunchApp1")]
    [InlineData("not-a-hotkey", AppConfig.DefaultPenHotkey)]
    public void NormalizePenHotkey_ReturnsExpectedValue(string? input, string expected)
    {
        var normalized = AppConfig.NormalizePenHotkey(input);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryGetVirtualKeyForPenHotkey_ReturnsExpectedKeyCode()
    {
        var found = AppConfig.TryGetVirtualKeyForPenHotkey("F20", out var vk);

        Assert.True(found);
        Assert.Equal(0x83, vk);
    }

    [Fact]
    public void GetSupportedPenHotkeys_ContainsExpectedEntries()
    {
        var supported = AppConfig.GetSupportedPenHotkeys();

        Assert.Contains("F20", supported);
        Assert.Contains("LaunchApp1", supported);
        Assert.Contains("LaunchApp2", supported);
        Assert.Equal(supported.Count, supported.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TryGetVirtualKeyForPenHotkey_IsCaseInsensitive()
    {
        var found = AppConfig.TryGetVirtualKeyForPenHotkey("launchapp2", out var vk);

        Assert.True(found);
        Assert.Equal(0xB7, vk);
    }
}

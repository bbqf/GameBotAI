using FluentAssertions;
using GameBot.Domain.Config;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class AppConfigValidationTests
{
    [Fact]
    public void DefaultCaptureIntervalMsIsFiveHundred()
    {
        var config = new AppConfig();
        config.CaptureIntervalMs.Should().Be(500);
    }

    [Fact]
    public void DefaultTapRetryCountIsThree()
    {
        var config = new AppConfig();
        config.TapRetryCount.Should().Be(3);
    }

    [Fact]
    public void DefaultTapRetryProgressionIsOne()
    {
        var config = new AppConfig();
        config.TapRetryProgression.Should().Be(1.0);
    }

    [Fact]
    public void CaptureIntervalMsCanBeSetToCustomValue()
    {
        var config = new AppConfig { CaptureIntervalMs = 200 };
        config.CaptureIntervalMs.Should().Be(200);
    }

    [Fact]
    public void TapRetryCountCanBeSetToZero()
    {
        var config = new AppConfig { TapRetryCount = 0 };
        config.TapRetryCount.Should().Be(0);
    }

    [Fact]
    public void TapRetryProgressionCanBeSetToCustomValue()
    {
        var config = new AppConfig { TapRetryProgression = 2.5 };
        config.TapRetryProgression.Should().Be(2.5);
    }
}

using FluentAssertions;
using GameBot.Service.Security;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.Security;

public sealed class FailedAttemptLimiterTests {
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

  [Fact]
  public void FiveFailuresInFiveMinutesBlockTheSource() {
    var limiter = new FailedAttemptLimiter(_clock);

    for (var i = 0; i < 4; i++) {
      limiter.RegisterFailure("a").Should().BeFalse();
      _clock.Advance(TimeSpan.FromSeconds(10));
    }

    limiter.IsBlocked("a").Should().BeFalse();
    limiter.RegisterFailure("a").Should().BeTrue();
    limiter.IsBlocked("a").Should().BeTrue();
  }

  [Fact]
  public void DifferentSourceIsNotBlocked() {
    var limiter = new FailedAttemptLimiter(_clock);
    for (var i = 0; i < 5; i++) {
      limiter.RegisterFailure("a");
    }

    limiter.IsBlocked("b").Should().BeFalse();
  }

  [Fact]
  public void BlockEndsAfterFifteenMinutes() {
    var limiter = new FailedAttemptLimiter(_clock);
    for (var i = 0; i < 5; i++) {
      limiter.RegisterFailure("a");
    }

    _clock.Advance(TimeSpan.FromMinutes(14));
    limiter.IsBlocked("a").Should().BeTrue();
    _clock.Advance(TimeSpan.FromMinutes(2));
    limiter.IsBlocked("a").Should().BeFalse();
  }

  [Fact]
  public void OldFailuresExpire() {
    var limiter = new FailedAttemptLimiter(_clock);
    for (var i = 0; i < 4; i++) {
      limiter.RegisterFailure("a");
    }

    _clock.Advance(TimeSpan.FromMinutes(6));
    limiter.RegisterFailure("a").Should().BeFalse();
    limiter.IsBlocked("a").Should().BeFalse();
  }
}

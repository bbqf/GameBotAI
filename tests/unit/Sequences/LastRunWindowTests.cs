using System;
using FluentAssertions;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 105 (research R-007): the start of the <c>since</c> window, with daylight-saving days.
/// </summary>
public sealed class LastRunWindowTests {
  private static readonly TimeOnly Eleven = new(11, 0);

  /// <summary>A real zone with daylight-saving time. The host resolves the Windows or the IANA name.</summary>
  private static TimeZoneInfo Berlin() {
    foreach (var id in new[] { "Europe/Berlin", "W. Europe Standard Time" }) {
      if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
    }

    throw new InvalidOperationException("No Central European time zone on this host.");
  }

  private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) =>
    new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

  [Fact]
  public void LaterOnTheSameDayGivesTheSameDay() {
    var start = LastRunWindow.SinceStart(Utc(9, 24, 14), Eleven, TimeZoneInfo.Utc);
    start.Should().Be(Utc(9, 24, 11));
  }

  [Fact]
  public void BeforeTheTimeOfDayGivesThePreviousDay() {
    var start = LastRunWindow.SinceStart(Utc(9, 24, 10), Eleven, TimeZoneInfo.Utc);
    start.Should().Be(Utc(9, 23, 11));
  }

  [Fact]
  public void AtTheExactMinuteTheWindowStartsNow() {
    var now = Utc(9, 24, 11);
    LastRunWindow.SinceStart(now, Eleven, TimeZoneInfo.Utc).Should().Be(now);
  }

  [Fact]
  public void ALocalZoneUsesTheLocalTimeOfDay() {
    // 12:00 UTC is 14:00 in Berlin summer time (+02:00), so 11:00 local is today 09:00 UTC.
    var start = LastRunWindow.SinceStart(Utc(9, 24, 12), Eleven, Berlin());
    start.Should().Be(Utc(9, 24, 9));
    start.Offset.Should().Be(TimeSpan.FromHours(2));
  }

  [Fact]
  public void OnASpringForwardDayATimeInTheGapGivesThePreviousRealOccurrence() {
    // 2026-03-29: Berlin clocks go from 02:00 to 03:00, so 02:30 does not occur that day.
    var zone = Berlin();
    var now = Utc(3, 29, 8); // 10:00 local (+02:00)

    var start = LastRunWindow.SinceStart(now, new TimeOnly(2, 30), zone);

    start.Should().Be(new DateTimeOffset(2026, 3, 28, 2, 30, 0, TimeSpan.FromHours(1)));
  }

  [Fact]
  public void OnAFallBackDayTheLaterRealInstantAtOrBeforeNowIsUsed() {
    // 2026-10-25: Berlin clocks go from 03:00 back to 02:00, so 02:30 occurs at 00:30 UTC and 01:30 UTC.
    var zone = Berlin();

    var afterBoth = LastRunWindow.SinceStart(Utc(10, 25, 11), new TimeOnly(2, 30), zone);
    afterBoth.Should().Be(Utc(10, 25, 1, 30));

    var betweenThem = LastRunWindow.SinceStart(Utc(10, 25, 0, 45), new TimeOnly(2, 30), zone);
    betweenThem.Should().Be(Utc(10, 25, 0, 30));
  }
}

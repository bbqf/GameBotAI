using System;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Notify;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 137: the window start of a notify step matches the <c>lastRun</c> math.</summary>
public sealed class NotifyDedupeWindowTests {
  private static NotifyPayload Read(string field, string value) {
    var action = new SequenceActionPayload { Type = ActionTypes.Notify };
    action.Parameters["message"] = "m";
    action.Parameters["dedupeKey"] = "k";
    action.Parameters[field] = value;
    NotifyPayload.TryRead(action, out var payload, out var error).Should().BeTrue(error);
    return payload!;
  }

  [Fact]
  public void Since_UsesLastRunWindowMath() {
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
    var payload = Read("since", "02:30");
    // 2026-03-29 is the spring-forward day: 02:30 does not exist in this zone.
    var now = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.FromHours(2));

    payload.WindowStart(now, zone).Should().Be(LastRunWindow.SinceStart(now, new TimeOnly(2, 30), zone));
  }

  [Fact]
  public void Since_StartsAtTheLatestTimeOfDay() {
    var payload = Read("since", "02:05");
    var now = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    payload.WindowStart(now, TimeZoneInfo.Utc).Should().Be(new DateTimeOffset(2026, 10, 12, 2, 5, 0, TimeSpan.Zero));
  }

  [Fact]
  public void Within_EndsNow() {
    var payload = Read("within", "00:30:00");
    var now = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    payload.WindowStart(now, TimeZoneInfo.Utc).Should().Be(now.AddMinutes(-30));
  }
}

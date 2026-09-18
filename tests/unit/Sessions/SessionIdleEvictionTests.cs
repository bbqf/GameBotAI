using System;
using System.Runtime.Versioning;
using FluentAssertions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sessions;

/// <summary>
/// #217: a queue idle longer than the session idle timeout had its own session retired by the idle
/// sweep, so its next scheduled firing failed with "emulator connection lost". A queue-owned session
/// must survive the sweep; an ad-hoc one must still be retired.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionIdleEvictionTests {
  private static SessionManager NewManager() =>
    new(
      Options.Create(new SessionOptions { MaxConcurrentSessions = 8, IdleTimeoutSeconds = 60 }),
      NullLogger<SessionManager>.Instance,
      NullLogger<GameBot.Emulator.Adb.AdbClient>.Instance);

  [Fact]
  public void QueueOwnedSessionSurvivesTheIdleSweepWhileAnAdHocOneIsRetired() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      var manager = NewManager();
      var owned = manager.CreateSession("queue:q1", "emulator-5558");
      owned.OwnerQueueId = "q1";
      var adHoc = manager.CreateSession("game-1", "emulator-5560");

      // An hour of silence — twice the gap the production queues failed after.
      var longAgo = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
      owned.LastActivity = longAgo;
      adHoc.LastActivity = longAgo;

      var remaining = manager.ListSessions();

      remaining.Should().ContainSingle().Which.Id.Should().Be(owned.Id);
      manager.GetSession(owned.Id).Should().NotBeNull();
      manager.GetSession(adHoc.Id).Should().BeNull();
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", null);
    }
  }

  [Fact]
  public void QueueOwnedSessionStillEndsWhenStopped() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      var manager = NewManager();
      var owned = manager.CreateSession("queue:q1", "emulator-5558");
      owned.OwnerQueueId = "q1";

      manager.StopSession(owned.Id).Should().BeTrue();

      manager.GetSession(owned.Id).Should().BeNull();
      manager.ListSessions().Should().BeEmpty();
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", null);
    }
  }
}

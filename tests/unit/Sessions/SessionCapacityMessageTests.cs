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
/// Feature 079 (FR-015/FR-016): the concurrent-session ceiling must not be the practical limit on how
/// many queues can run at once, and hitting it must say so in words an operator can act on.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionCapacityMessageTests {
  [Fact]
  public void TheDefaultCeilingIsEight() {
    // 3 (the pre-079 default) is below a plausible emulator count, so the guard rail became the
    // ceiling on concurrency and failed runs with an opaque error.
    new SessionOptions().MaxConcurrentSessions.Should().Be(8);
  }

  [Fact]
  public void TheCapacityMessageNamesTheCountsAndTheSetting() {
    SessionManager.CapacityExceededMessage(8, 8)
      .Should().Be("session capacity reached: 8 of 8 sessions are open (Service:Sessions:MaxConcurrentSessions)");
  }

  [Fact]
  public void ExceedingCapacityThrowsWithTheActionableMessage() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      var manager = new SessionManager(
        Options.Create(new SessionOptions { MaxConcurrentSessions = 2, IdleTimeoutSeconds = 1800 }),
        NullLogger<SessionManager>.Instance,
        NullLogger<GameBot.Emulator.Adb.AdbClient>.Instance);

      manager.CreateSession("queue:q1", "emulator-5558");
      manager.CreateSession("queue:q2", "emulator-5560");

      var act = () => manager.CreateSession("queue:q3", "emulator-5562");

      act.Should().Throw<InvalidOperationException>()
        .WithMessage("session capacity reached: 2 of 2 sessions are open (Service:Sessions:MaxConcurrentSessions)");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", null);
    }
  }
}

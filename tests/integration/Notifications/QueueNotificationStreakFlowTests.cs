using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.UnitTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>
/// Feature 120 (V-04, SC-007): ten failed runs and then one good run give exactly two messages, through
/// the real queue engine, dispatcher, worker and channel.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueNotificationStreakFlowTests {
  public QueueNotificationStreakFlowTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Theory]
  [InlineData(NotificationLevel.Failure)]
  [InlineData(NotificationLevel.SuccessAndFailure)]
  public async Task V04_TenFailuresThenOneSuccessGiveTwoMessages(NotificationLevel level) {
    var channel = new RecordingChannel();
    using var app = NotificationIntegrationHelpers.HostWithChannel(channel);
    _ = app.CreateClient();
    NotificationIntegrationHelpers.AddTarget(app.Services);
    // The sequence does not exist: each run is a failure.
    await NotificationIntegrationHelpers.SeedQueueAsync(app.Services, "q-streak", level, "s-streak");

    for (var i = 0; i < 10; i++) {
      await NotificationIntegrationHelpers.RunToCompletionAsync(app.Services, "q-streak");
    }

    await NotificationIntegrationHelpers.WaitForAsync(() => channel.Calls >= 1);
    await Task.Delay(200);
    channel.Sent.Should().ContainSingle();

    // The sequence appears, so the next run works.
    await NotificationIntegrationHelpers.SeedSequenceAsync(app.Services, "s-streak", stepCount: 1);
    await NotificationIntegrationHelpers.RunToCompletionAsync(app.Services, "q-streak");
    await NotificationIntegrationHelpers.WaitForAsync(() => channel.Calls >= 2);
    await Task.Delay(200);

    channel.Sent.Select(m => m.Text).Should().Equal(
      "Q-q-streak : s-streak : \U0001F534 failure",
      "Q-q-streak : Seq-s-streak : \U0001F7E2 recovered");
  }
}

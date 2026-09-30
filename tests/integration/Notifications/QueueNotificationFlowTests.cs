using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>Feature 120 (V-02): one job for a queue entry, none for its nested steps.</summary>
[Collection("ConfigIsolation")]
public sealed class QueueNotificationFlowTests {
  public QueueNotificationFlowTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task V02_OneEntryWithThreeStepsGivesOneJob() {
    var dispatcher = new JobRecordingDispatcher();
    using var app = NotificationIntegrationHelpers.HostWithDispatcher(dispatcher);
    _ = app.CreateClient();
    await NotificationIntegrationHelpers.SeedSequenceAsync(app.Services, "s-flow", stepCount: 3);
    await NotificationIntegrationHelpers.SeedQueueAsync(app.Services, "q-flow", NotificationLevel.Failure, "s-flow");

    await NotificationIntegrationHelpers.RunToCompletionAsync(app.Services, "q-flow");

    var job = dispatcher.Jobs.Should().ContainSingle().Subject;
    job.QueueId.Should().Be("q-flow");
    job.SequenceId.Should().Be("s-flow");
    job.Status.Should().Be(NotificationRunStatus.Success);
  }

  [Fact]
  public async Task V02_AMissingSequenceIsAFailureJob() {
    var dispatcher = new JobRecordingDispatcher();
    using var app = NotificationIntegrationHelpers.HostWithDispatcher(dispatcher);
    _ = app.CreateClient();
    await NotificationIntegrationHelpers.SeedQueueAsync(app.Services, "q-gone", NotificationLevel.Failure, "s-gone");

    await NotificationIntegrationHelpers.RunToCompletionAsync(app.Services, "q-gone");

    dispatcher.Jobs.Should().ContainSingle().Which.Status.Should().Be(NotificationRunStatus.Failure);
  }

  [Fact]
  public async Task TheRealDispatcherAndWorkerSendOneFailureMessageToTheChannel() {
    var channel = new UnitTests.Notifications.RecordingChannel();
    using var app = NotificationIntegrationHelpers.HostWithChannel(channel);
    _ = app.CreateClient();
    NotificationIntegrationHelpers.AddTarget(app.Services);
    await NotificationIntegrationHelpers.SeedQueueAsync(app.Services, "q-real", NotificationLevel.Failure, "s-absent");

    await NotificationIntegrationHelpers.RunToCompletionAsync(app.Services, "q-real");
    await NotificationIntegrationHelpers.WaitForAsync(() => channel.Calls >= 1);

    channel.Sent.Should().ContainSingle().Which.Text.Should().Be("Q-q-real : s-absent : \U0001F534 failure");
  }
}

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>Feature 120 (V-02, SC-004): a manual run sends no message.</summary>
[Collection("ConfigIsolation")]
public sealed class ManualRunNotificationTests {
  public ManualRunNotificationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task AManualRestRunAtSuccessAndFailureGivesNoJobAndNoMessage() {
    var recorder = new JobRecordingDispatcher();
    using var app = NotificationIntegrationHelpers.HostWithDispatcher(recorder);
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    await NotificationIntegrationHelpers.SeedSequenceAsync(app.Services, "s-manual", stepCount: 2);
    await NotificationIntegrationHelpers.SeedQueueAsync(app.Services, "q-manual", NotificationLevel.SuccessAndFailure, "s-manual");

    var response = await client.PostAsJsonAsync(new Uri("/api/sequences/s-manual/execute", UriKind.Relative), new { });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    await Task.Delay(300);
    recorder.Jobs.Should().BeEmpty();
  }

  [Fact]
  public async Task AManualRunSendsNoMessageToTheChannelWithTheRealWorker() {
    var channel = new UnitTests.Notifications.RecordingChannel();
    using var app = NotificationIntegrationHelpers.HostWithChannel(channel);
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    NotificationIntegrationHelpers.AddTarget(app.Services);
    await NotificationIntegrationHelpers.SeedSequenceAsync(app.Services, "s-manual2", stepCount: 2);
    await NotificationIntegrationHelpers.SeedQueueAsync(app.Services, "q-manual2", NotificationLevel.SuccessAndFailure, "s-manual2");

    var response = await client.PostAsJsonAsync(new Uri("/api/sequences/s-manual2/execute", UriKind.Relative), new { });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    await Task.Delay(300);
    channel.Calls.Should().Be(0);
  }
}

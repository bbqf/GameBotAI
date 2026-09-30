using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.ContractTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Feature 120 (V-23, FR-021): every other queue change keeps the level. A duplicate and a create
/// with no level give none. The backup archive holds no queue, so a restore cannot change a level.
/// </summary>
public sealed class QueueNotificationLevelKeptTests {
  private static Uri Rel(string path) => NotificationContractHost.Rel(path);

  private static async Task<string> LevelOfAsync(NotificationContractHost host, string id) =>
    (await NotificationContractHost.ReadJsonAsync(await host.Client.GetAsync(Rel($"/api/queues/{id}")))).GetProperty("notificationLevel").GetString()!;

  private static async Task<string> QueueWithLevelFailureAsync(NotificationContractHost host) {
    var id = await host.CreateQueueAsync();
    (await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}/notification-level"), new { level = "failure" })).StatusCode.Should().Be(HttpStatusCode.OK);
    return id;
  }

  [Fact]
  public async Task V23_AFullUpdateWithNoLevelKeepsTheLevel() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await QueueWithLevelFailureAsync(host);

    var response = await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Renamed", cycleExecution = true, pauseWhenIdle = true, idleThresholdSeconds = 45 });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await NotificationContractHost.ReadJsonAsync(response)).GetProperty("notificationLevel").GetString().Should().Be("failure");
    (await LevelOfAsync(host, id)).Should().Be("failure");
    host.Dispatcher!.Resets.Should().BeEmpty();
  }

  [Fact]
  public async Task V23_TheTemplateLinkChangeKeepsTheLevel() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await QueueWithLevelFailureAsync(host);

    (await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}/template"), new { templateId = (string?)null })).StatusCode.Should().Be(HttpStatusCode.OK);

    (await LevelOfAsync(host, id)).Should().Be("failure");
  }

  [Fact]
  public async Task V23_TheGameLinkChangeKeepsTheLevel() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await QueueWithLevelFailureAsync(host);

    (await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}/game"), new { gameId = (string?)null })).StatusCode.Should().Be(HttpStatusCode.OK);

    (await LevelOfAsync(host, id)).Should().Be("failure");
  }

  [Fact]
  public async Task V23_TheEntryChangesKeepTheLevel() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await QueueWithLevelFailureAsync(host);

    var add = await host.Client.PostAsJsonAsync(Rel($"/api/queues/{id}/entries"), new { sequenceId = "seq-x" });
    add.StatusCode.Should().Be(HttpStatusCode.Created);
    (await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}/entries"), new { sequenceIds = new[] { "seq-a", "seq-b" } })).StatusCode.Should().Be(HttpStatusCode.OK);

    (await LevelOfAsync(host, id)).Should().Be("failure");
  }

  [Fact]
  public async Task V23_AStartAndStopKeepTheLevel() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await QueueWithLevelFailureAsync(host);

    (await host.Client.PostAsync(Rel($"/api/queues/{id}/start"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.Client.PostAsync(Rel($"/api/queues/{id}/stop"), null)).StatusCode.Should().Be(HttpStatusCode.OK);

    (await LevelOfAsync(host, id)).Should().Be("failure");
  }

  [Fact]
  public async Task V23_ADuplicateAndACreateWithNoLevelGiveNone() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await QueueWithLevelFailureAsync(host);

    var copy = await host.Client.PostAsJsonAsync(Rel($"/api/queues/{id}/duplicate"), new { name = "Copy", emulatorSerial = "emu-2" });
    copy.StatusCode.Should().Be(HttpStatusCode.Created);
    var copyId = (await NotificationContractHost.ReadJsonAsync(copy)).GetProperty("id").GetString()!;
    var fresh = await host.CreateQueueAsync("Fresh");

    (await LevelOfAsync(host, copyId)).Should().Be("none");
    (await LevelOfAsync(host, fresh)).Should().Be("none");
    (await LevelOfAsync(host, id)).Should().Be("failure");
  }
}

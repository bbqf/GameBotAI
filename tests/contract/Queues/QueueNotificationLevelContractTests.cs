using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.ContractTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Queues;

/// <summary>Feature 120: the queue level route and the level field in the queue responses.</summary>
public sealed class QueueNotificationLevelContractTests {
  private static Uri Rel(string path) => NotificationContractHost.Rel(path);

  [Fact]
  public async Task ANewQueueHasLevelNoneInBothResponses() {
    using var host = new NotificationContractHost(recordDispatcher: true);

    var id = await host.CreateQueueAsync();

    var list = await NotificationContractHost.ReadJsonAsync(await host.Client.GetAsync(Rel("/api/queues")));
    list[0].GetProperty("notificationLevel").GetString().Should().Be("none");
    var detail = await NotificationContractHost.ReadJsonAsync(await host.Client.GetAsync(Rel($"/api/queues/{id}")));
    detail.GetProperty("notificationLevel").GetString().Should().Be("none");
  }

  [Theory]
  [InlineData("failure")]
  [InlineData("successAndFailure")]
  [InlineData("SUCCESSANDFAILURE")]
  public async Task TheLevelRouteSavesTheLevelAndCallsNoReset(string level) {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await host.CreateQueueAsync();

    var response = await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}/notification-level"), new { level });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await NotificationContractHost.ReadJsonAsync(response);
    body.GetProperty("id").GetString().Should().Be(id);
    body.GetProperty("notificationLevel").GetString().Should().Be(level == "failure" ? "failure" : "successAndFailure");
    (await NotificationContractHost.ReadJsonAsync(await host.Client.GetAsync(Rel($"/api/queues/{id}")))).GetProperty("notificationLevel").GetString()
      .Should().Be(level == "failure" ? "failure" : "successAndFailure");
    host.Dispatcher!.Resets.Should().BeEmpty();
  }

  [Fact]
  public async Task ASaveOfNoneCallsResetStreaksOnce() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await host.CreateQueueAsync();
    await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}/notification-level"), new { level = "failure" });

    var response = await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}/notification-level"), new { level = "none" });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await NotificationContractHost.ReadJsonAsync(response)).GetProperty("notificationLevel").GetString().Should().Be("none");
    host.Dispatcher!.Resets.Should().Equal(id);
  }

  [Fact]
  public async Task ADeleteCallsResetStreaksOnce() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await host.CreateQueueAsync();

    (await host.Client.DeleteAsync(Rel($"/api/queues/{id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

    host.Dispatcher!.Resets.Should().Equal(id);
  }

  [Theory]
  [InlineData("{\"level\":\"loud\"}")]
  [InlineData("{\"level\":\"\"}")]
  [InlineData("{}")]
  [InlineData("{\"level\":5}")]
  [InlineData("{ nope")]
  [InlineData("")]
  [InlineData("null")]
  public async Task ABadLevelGives400AndChangesNothing(string body) {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await host.CreateQueueAsync();

    var response = await host.Client.PutAsync(Rel($"/api/queues/{id}/notification-level"), new StringContent(body, Encoding.UTF8, "application/json"));

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await NotificationContractHost.ReadJsonAsync(response)).GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    (await NotificationContractHost.ReadJsonAsync(await host.Client.GetAsync(Rel($"/api/queues/{id}")))).GetProperty("notificationLevel").GetString().Should().Be("none");
    host.Dispatcher!.Resets.Should().BeEmpty();
  }

  [Fact]
  public async Task ANonJsonContentTypeGives400() {
    using var host = new NotificationContractHost(recordDispatcher: true);
    var id = await host.CreateQueueAsync();

    var response = await host.Client.PutAsync(Rel($"/api/queues/{id}/notification-level"), new StringContent("failure", Encoding.UTF8, "text/plain"));

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task AnUnknownQueueGives404() {
    using var host = new NotificationContractHost(recordDispatcher: true);

    var response = await host.Client.PutAsJsonAsync(Rel("/api/queues/nope/notification-level"), new { level = "failure" });

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task CreateAndUpdateAcceptAnOptionalLevelAndRejectABadOne() {
    using var host = new NotificationContractHost(recordDispatcher: true);

    var created = await host.Client.PostAsJsonAsync(Rel("/api/queues"), new { name = "Q", emulatorSerial = "e", notificationLevel = "failure" });
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = (await NotificationContractHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
    (await host.Client.PostAsJsonAsync(Rel("/api/queues"), new { name = "Q2", emulatorSerial = "e", notificationLevel = "loud" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var updated = await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Q", notificationLevel = "successAndFailure" });
    updated.StatusCode.Should().Be(HttpStatusCode.OK);
    (await NotificationContractHost.ReadJsonAsync(updated)).GetProperty("notificationLevel").GetString().Should().Be("successAndFailure");
    (await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Q", notificationLevel = "loud" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    host.Dispatcher!.Resets.Should().BeEmpty();

    (await host.Client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Q", notificationLevel = "none" })).StatusCode.Should().Be(HttpStatusCode.OK);
    host.Dispatcher.Resets.Should().Equal(id);
  }
}

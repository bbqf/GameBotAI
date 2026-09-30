using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Domain.Queues;
using GameBot.Service.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Notifications;

/// <summary>Feature 120 (V-19): the test send route.</summary>
public sealed class NotificationTestSendContractTests {
  private static Uri Rel(string path) => NotificationContractHost.Rel(path);

  [Fact]
  public async Task V19_TheTestMessageIsFixedHasNoParseModeAndChangesNoStreakOrLevel() {
    using var host = new NotificationContractHost();
    var queueId = await host.CreateQueueAsync();
    (await host.Client.PutAsJsonAsync(Rel($"/api/queues/{queueId}/notification-level"), new { level = "failure" })).StatusCode.Should().Be(HttpStatusCode.OK);
    await host.CreateTargetAsync(enabled: false);
    var id = (await NotificationContractHost.ReadJsonAsync(await host.Client.GetAsync(Rel("/api/notifications/targets"))))[0].GetProperty("id").GetString()!;

    // Open a streak: a failure job for the queue goes through the real dispatcher and worker.
    var worker = host.App.Services.GetServices<IHostedService>().OfType<QueueNotificationWorker>().Single();
    var dispatcher = host.App.Services.GetRequiredService<INotificationDispatcher>();
    var sequenceId = "s-open";
    dispatcher.Enqueue(new QueueNotificationJob(queueId, sequenceId, NotificationRunStatus.Failure, DateTimeOffset.Now));
    await WaitAsync(() => worker.Streaks.TotalOpenCount == 1);
    var before = host.Telegram.Requests.Count;

    var response = await host.Client.PostAsync(Rel($"/api/notifications/targets/{id}/test"), null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await NotificationContractHost.ReadJsonAsync(response);
    body.GetProperty("ok").GetBoolean().Should().BeTrue();
    body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
    // The disabled target still gets the test message. The failure message did not go to it.
    var request = host.Telegram.Requests[^1];
    host.Telegram.Requests.Count.Should().Be(before + 1);
    using var doc = JsonDocument.Parse(request.Body);
    doc.RootElement.GetProperty("text").GetString().Should().Be("GameBot test message");
    doc.RootElement.TryGetProperty("parse_mode", out _).Should().BeFalse();
    worker.Streaks.TotalOpenCount.Should().Be(1);
    var queue = await host.App.Services.GetRequiredService<IQueueRepository>().GetAsync(queueId);
    queue!.NotificationLevel.Should().Be(NotificationLevel.Failure);
  }

  [Fact]
  public async Task AFailedSendGives200WithOkFalseAndTheReason() {
    using var host = new NotificationContractHost();
    var id = (await host.CreateTargetAsync()).GetProperty("id").GetString()!;
    host.Telegram.Answer(HttpStatusCode.BadRequest, "{\"ok\":false,\"description\":\"Bad Request: chat not found\"}");

    var response = await host.Client.PostAsync(Rel($"/api/notifications/targets/{id}/test"), null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await NotificationContractHost.ReadJsonAsync(response);
    body.GetProperty("ok").GetBoolean().Should().BeFalse();
    body.GetProperty("reason").GetString().Should().Be("Telegram answered 400: Bad Request: chat not found");
  }

  [Fact]
  public async Task AnUnknownIdGives404() {
    using var host = new NotificationContractHost();

    var response = await host.Client.PostAsync(Rel("/api/notifications/targets/nope/test"), null);

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task ATargetOfAnUnknownTypeGivesOkFalse() {
    using var host = new NotificationContractHost();
    var store = host.App.Services.GetRequiredService<INotificationTargetStore>();
    var target = store.Create(new NotificationTarget { Type = "smoke", Name = "Odd" });

    var response = await host.Client.PostAsync(Rel($"/api/notifications/targets/{target.Id}/test"), null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await NotificationContractHost.ReadJsonAsync(response)).GetProperty("ok").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task ATargetThatDoesNotAnswerEndsAt15SecondsWithTheTimeText() {
    using var host = new NotificationContractHost(attemptTimeout: TimeSpan.FromMinutes(5));
    var id = (await host.CreateTargetAsync()).GetProperty("id").GetString()!;
    host.Telegram.Hang();
    var clock = Stopwatch.StartNew();

    var response = await host.Client.PostAsync(Rel($"/api/notifications/targets/{id}/test"), null);

    clock.Stop();
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await NotificationContractHost.ReadJsonAsync(response);
    body.GetProperty("ok").GetBoolean().Should().BeFalse();
    body.GetProperty("reason").GetString().Should().Be("The target did not answer in time.");
    clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(14)).And.BeLessThan(TimeSpan.FromSeconds(25));
  }

  private static async Task WaitAsync(Func<bool> condition) {
    var sw = Stopwatch.StartNew();
    while (!condition() && sw.ElapsedMilliseconds < 10000) await Task.Delay(10);
  }
}

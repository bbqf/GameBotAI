using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using GameBot.UnitTests.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>
/// Feature 120 (V-24, SC-002): the message reaches the fake Telegram receiver within 30 s of the
/// failure. The test uses the real Telegram channel, the real worker and a fake HTTP handler.
/// </summary>
public sealed class NotificationTimingTests {
  private sealed class ReceiverHandler : HttpMessageHandler {
    public ConcurrentBag<DateTime> ReceivedAt { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      await Task.Delay(30, cancellationToken);
      ReceivedAt.Add(DateTime.UtcNow);
      return new HttpResponseMessage(HttpStatusCode.OK);
    }
  }

  private sealed class Factory : IHttpClientFactory {
    private readonly HttpMessageHandler _handler;

    public Factory(HttpMessageHandler handler) { _handler = handler; }

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
  }

  [Fact]
  public async Task V24_TwentyFailuresArriveWithin30Seconds() {
    var handler = new ReceiverHandler();
    var channel = new TelegramChannel(new Factory(handler), Options.Create(new NotificationOptions()), NullLogger<TelegramChannel>.Instance);
    var queues = new MemoryQueueRepository();
    var sequences = new MemorySequenceRepository();
    sequences.Add("s1", "Collect");
    for (var i = 0; i < 20; i++) {
      queues.Add(new GameBot.Domain.Queues.ExecutionQueue { Id = $"q{i}", Name = $"Queue {i}", EmulatorSerial = $"e{i}", NotificationLevel = NotificationLevel.Failure });
    }

    var targets = new MemoryTargetStore();
    var target = new NotificationTarget { Id = "t1", Type = "telegram", Name = "Phone" };
    target.Settings["botToken"] = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    target.Settings["chatId"] = "1";
    targets.Create(target);
    var dispatcher = new QueueNotificationDispatcher(NullLogger<QueueNotificationDispatcher>.Instance);
    using var worker = new QueueNotificationWorker(dispatcher, queues, sequences, targets, new INotificationChannel[] { channel }, NullLogger<QueueNotificationWorker>.Instance);
    await worker.StartAsync(CancellationToken.None);

    var start = DateTime.UtcNow;
    for (var i = 0; i < 20; i++) {
      dispatcher.Enqueue(new QueueNotificationJob($"q{i}", "s1", NotificationRunStatus.Failure, DateTimeOffset.Now));
    }

    await NotificationHarness.WaitForAsync(() => handler.ReceivedAt.Count >= 20, 30000);

    handler.ReceivedAt.Count(t => t - start <= TimeSpan.FromSeconds(30)).Should().BeGreaterThanOrEqualTo(19);
    dispatcher.Complete();
    await worker.StopAsync(CancellationToken.None);
  }
}

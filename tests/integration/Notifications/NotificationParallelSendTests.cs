using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using GameBot.UnitTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>
/// Feature 120 (V-12, FR-010, SC-002): stuck targets never delay a fast target and never slow the
/// worker. The test uses a shorter send limit than the 30 s of the design, so it runs fast. The
/// limit is a constructor value of the worker, and the rules are the same.
/// </summary>
public sealed class NotificationParallelSendTests {
  [Fact]
  public async Task V12_ThreeStuckTargetsDoNotDelayTheFastTargetOrTheWorker() {
    var limits = new NotificationDispatchLimits { SendTimeout = TimeSpan.FromSeconds(3) };
    var queues = new MemoryQueueRepository();
    var sequences = new MemorySequenceRepository();
    sequences.Add("s1", "Collect");
    for (var i = 0; i < 50; i++) {
      queues.Add(new GameBot.Domain.Queues.ExecutionQueue { Id = $"q{i}", Name = $"Queue {i}", EmulatorSerial = $"e{i}", NotificationLevel = NotificationLevel.Failure });
    }

    var targets = new MemoryTargetStore();
    targets.Create(new NotificationTarget { Id = "fast", Type = "telegram", Name = "Fast" });
    for (var i = 0; i < 3; i++) targets.Create(new NotificationTarget { Id = $"stuck{i}", Type = "stuck", Name = $"Stuck {i}" });

    var fast = new RecordingChannel("telegram");
    // A stuck channel that ignores the token, so only the send limit can end it.
    var stuck = new RecordingChannel("stuck") {
      Behavior = async (_, _, _) => { await Task.Delay(Timeout.Infinite, CancellationToken.None); return NotificationSendResult.Ok(); }
    };
    var dispatcher = new QueueNotificationDispatcher(new CapturingLogger<QueueNotificationDispatcher>(), limits);
    var log = new CapturingLogger<QueueNotificationWorker>();
    using var worker = new QueueNotificationWorker(dispatcher, queues, sequences, targets, new INotificationChannel[] { fast, stuck }, log, limits);

    var clock = Stopwatch.StartNew();
    for (var i = 0; i < 50; i++) {
      await worker.HandleAsync(NotificationWork.ForJob(new QueueNotificationJob($"q{i}", "s1", NotificationRunStatus.Failure, DateTimeOffset.Now)));
    }

    var handled = clock.Elapsed;
    handled.Should().BeLessThan(TimeSpan.FromSeconds(5));
    await NotificationHarness.WaitForAsync(() => fast.Calls == 50, 2000);
    fast.Sent.Should().HaveCount(50);
    clock.Elapsed.Should().BeLessThan(limits.SendTimeout);

    // Each stuck send ends at the limit with one log line.
    await NotificationHarness.WaitForAsync(() => log.Lines.Count(l => l.Contains("did not end within the time limit", StringComparison.Ordinal)) == 150, 10000);
    foreach (var id in new[] { "stuck0", "stuck1", "stuck2" }) {
      log.Lines.Count(l => l.Contains($"target {id} ", StringComparison.OrdinalIgnoreCase) && l.Contains("did not end within the time limit", StringComparison.Ordinal)).Should().Be(50);
    }

    await NotificationHarness.WaitForAsync(() => worker.ActiveSends == 0, 5000);
    worker.ActiveSends.Should().Be(0);
  }
}

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

/// <summary>Feature 120 (V-13, FR-010, SC-005): a blocked or broken send never slows the queue path.</summary>
public sealed class NotificationResilienceTests {
  [Fact]
  public async Task V13_ABlockedWorkerKeeps256JobsAndDrops44WithOneLogLineEach() {
    var log = new CapturingLogger<QueueNotificationDispatcher>();
    var dispatcher = new QueueNotificationDispatcher(log);
    // No worker reads the channel, so it is blocked.

    var clock = Stopwatch.StartNew();
    for (var i = 0; i < 300; i++) {
      dispatcher.Enqueue(new QueueNotificationJob("q1", $"s{i}", NotificationRunStatus.Failure, DateTimeOffset.Now));
    }

    clock.Stop();
    dispatcher.QueuedRunJobs.Should().Be(256);
    log.Lines.Should().HaveCount(44);
    log.Lines.Should().OnlyContain(l => l.Contains("q1", StringComparison.Ordinal) && l.Contains("dropped", StringComparison.OrdinalIgnoreCase));
    (clock.Elapsed.TotalMilliseconds / 300).Should().BeLessThan(1.0, "the mean hand-off must stay under 1 ms");
    await Task.CompletedTask;
  }

  [Fact]
  public async Task V13_AChannelThatFailsOrThrowsChangesNothingForTheCaller() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    var gate = new TaskCompletionSource();
    h.Channel.Behavior = async (_, _, _) => { await gate.Task; throw new InvalidOperationException("send failed"); };

    var clock = Stopwatch.StartNew();
    await h.HandleAsync(NotificationRunStatus.Failure);
    clock.Stop();

    // The worker does not wait for the send.
    clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    gate.SetResult();
    await h.WaitIdleAsync();
    h.WorkerLog.Lines.Should().ContainSingle(l => l.Contains("faulted", StringComparison.Ordinal));
  }

  [Fact]
  public async Task V13_TheRunTimeDoesNotChangeWithASlowTarget() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    h.Channel.Behavior = async (_, _, ct) => { await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None); return NotificationSendResult.Ok(); };
    var withTarget = Stopwatch.StartNew();
    await h.HandleAsync(NotificationRunStatus.Cancelled);
    withTarget.Stop();

    await using var none = new NotificationHarness(NotificationLevel.Failure, targetCount: 0);
    var without = Stopwatch.StartNew();
    await none.HandleAsync(NotificationRunStatus.Cancelled);
    without.Stop();

    withTarget.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    without.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    h.Channel.Sent.Should().BeEmpty("the slow send has not ended");
    await h.WaitIdleAsync(5000);
  }
}

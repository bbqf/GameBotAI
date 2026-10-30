using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 170 (regression): with <c>onTimeLimit</c> = <c>fail</c> or no option, the queue keeps today's
/// behaviour: a Cancelled record, a failure message, and a daily retry.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  [Theory]
  [InlineData(null)]
  [InlineData("fail")]
  public async Task FailAtLimit_KeepsTheCancelledRecordAndTheFailureMessage(string? onTimeLimit) {
    var h = new Harness(sequenceRepository: true);
    await AddLimitedSequenceAsync(h, "A", onTimeLimit);
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStatisticsStore();
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, store: store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    dispatcher.Jobs.Should().ContainSingle().Which.Status.Should().Be(NotificationRunStatus.Failure);
    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Cancelled);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("fail")]
  public async Task FailAtLimit_ArmsTheDailyRetry(string? onTimeLimit) {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock, config: RetryConfig(maxAttempts: 3), sequenceRepository: true);
    await AddLimitedSequenceAsync(h, "T", onTimeLimit);
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T", TimeOnly.FromDateTime(FakeStart.DateTime)), new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.OncePerRun } });
    var attempts = 0;
    h.Sequences.Handler = async (id, ct) => {
      if (id == "T" && Interlocked.Increment(ref attempts) == 1) await Task.Delay(Timeout.Infinite, ct);
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    clock.Advance(TimeSpan.FromMinutes(30));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Count(x => x == "T").Should().Be(2, "a failure at the limit arms a retry");
  }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 170: a firing that the time limit ends, with <c>onTimeLimit</c> = <c>end</c>, is a success for the queue.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  private static async Task AddLimitedSequenceAsync(Harness h, string id, string? onTimeLimit) =>
    await h.SequenceRepository!.CreateAsync(new CommandSequence {
      Id = id, Name = id, WatchdogTimeoutMs = 50, OnTimeLimit = onTimeLimit
    });

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task EndAtLimit_IsRecordedAsSuccessWithNoFailureMessage(bool swallowCancel) {
    var h = new Harness(sequenceRepository: true);
    await AddLimitedSequenceAsync(h, "A", "end");
    h.AddQueue("q1", new[] { "A" });
    h.Sequences.Handler = async (id, ct) => {
      try { await Task.Delay(Timeout.Infinite, ct); }
      catch (OperationCanceledException) when (swallowCancel) { return FakeSequenceExecution.Failure(id); }
      return FakeSequenceExecution.Success(id);
    };
    var store = new InMemoryRunStatisticsStore();
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, store: store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    dispatcher.Jobs.Should().ContainSingle().Which.Status.Should().Be(NotificationRunStatus.Success);
    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Success);
  }

  [Fact]
  public async Task EndAtLimit_MakesNoDailyRetry() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock, config: RetryConfig(maxAttempts: 3), sequenceRepository: true);
    await AddLimitedSequenceAsync(h, "T", "end");
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T", TimeOnly.FromDateTime(FakeStart.DateTime)), new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.OncePerRun } });
    h.Sequences.Handler = async (id, ct) => {
      if (id == "T") await Task.Delay(Timeout.Infinite, ct);
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    clock.Advance(TimeSpan.FromMinutes(30));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Count(x => x == "T").Should().Be(1, "a planned stop arms no retry");
  }
}

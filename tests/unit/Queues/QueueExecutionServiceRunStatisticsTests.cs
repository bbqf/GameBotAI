using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Queues;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Hosting;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 105: the queue records each sequence run that it starts, with the status of research R-002.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  private static readonly DateTimeOffset StatsStart = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

  private sealed class InMemoryRunStatisticsStore : ISequenceRunStatisticsStore {
    private readonly List<(string QueueId, string SequenceId, SequenceRunRecord Record)> _records = new();
    private readonly Dictionary<(string, string), SequenceRunStatistics> _stats = new();

    public bool Throws { get; set; }

    /// <summary>A delay in each record, to prove that a stop waits for the record.</summary>
    public TimeSpan RecordDelay { get; set; }

    public IReadOnlyList<(string QueueId, string SequenceId, SequenceRunRecord Record)> Records {
      get { lock (_records) return _records.ToList(); }
    }

    public async Task RecordAsync(string queueId, string sequenceId, SequenceRunRecord record, CancellationToken ct = default) {
      if (RecordDelay > TimeSpan.Zero) await Task.Delay(RecordDelay, CancellationToken.None);
      if (Throws) throw new System.IO.IOException("disk full");
      lock (_records) {
        _records.Add((queueId, sequenceId, record));
        if (!_stats.TryGetValue((queueId, sequenceId), out var stats)) {
          stats = new SequenceRunStatistics();
          _stats[(queueId, sequenceId)] = stats;
        }
        stats.Apply(record);
      }
    }

    public Task<IReadOnlyDictionary<string, SequenceRunStatistics>> GetForQueueAsync(string queueId, CancellationToken ct = default) {
      lock (_records) {
        IReadOnlyDictionary<string, SequenceRunStatistics> map = _stats
          .Where(p => p.Key.Item1 == queueId)
          .ToDictionary(p => p.Key.Item2, p => p.Value.Clone(), StringComparer.Ordinal);
        return Task.FromResult(map);
      }
    }

    public Task<SequenceRunStatistics?> GetAsync(string queueId, string sequenceId, CancellationToken ct = default) {
      lock (_records) {
        return Task.FromResult(_stats.TryGetValue((queueId, sequenceId), out var s) ? s.Clone() : null);
      }
    }

    public Task DeleteQueueAsync(string queueId, CancellationToken ct = default) => Task.CompletedTask;
  }

  /// <summary>A foreground guard that waits until the run is stopped, before the sequence starts.</summary>
  private sealed class BlockingForegroundGuard : IGameForegroundGuard {
    public int Calls;

    public async Task<GameForegroundGuardResult> EnsureForegroundAsync(string sessionId, CancellationToken ct = default) {
      Interlocked.Increment(ref Calls);
      await Task.Delay(Timeout.Infinite, ct);
      return new GameForegroundGuardResult(GameForegroundGuardOutcome.AlreadyForeground, "unreachable");
    }
  }

  private static QueueExecutionService StatsService(
      Harness h,
      InMemoryRunStatisticsStore store,
      IHostApplicationLifetime? lifetime = null,
      Microsoft.Extensions.Logging.ILogger<QueueExecutionService>? logger = null,
      IGameForegroundGuard? foregroundGuard = null)
    => new(h.Queues, h.Runtime, h.Templates, h.Sequences, h.Sessions, h.Log,
      logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QueueExecutionService>.Instance, h.Registry,
      lifetime: lifetime, timeProvider: h.Clock, foregroundGuard: foregroundGuard, sequences: h.SequenceRepository,
      runStatistics: store);

  private static SequenceRunStatus SingleStatus(InMemoryRunStatisticsStore store, string sequenceId)
    => store.Records.Where(r => r.SequenceId == sequenceId).Should().ContainSingle().Which.Record.Status;

  [Fact]
  public async Task RunStats_SucceededGivesSuccessWithTimesFromTheClock() {
    var clock = new FakeTimeProvider(StatsStart);
    var h = new Harness(clock);
    h.AddQueue("q1", new[] { "A" });
    h.Sequences.Handler = (id, ct) => {
      clock.Advance(TimeSpan.FromSeconds(42));
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    var record = store.Records.Should().ContainSingle().Subject;
    record.QueueId.Should().Be("q1");
    record.SequenceId.Should().Be("A");
    record.Record.Status.Should().Be(SequenceRunStatus.Success);
    record.Record.StartedAt.Should().Be(StatsStart);
    record.Record.EndedAt.Should().Be(StatsStart.AddSeconds(42));
  }

  [Fact]
  public async Task RunStats_ARunThatABreakStepEndsGivesSuccess() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    // A Break step ends the run without a call to Complete(); the status stays "Succeeded".
    h.Sequences.Handler = (id, ct) => Task.FromResult(GameBot.Domain.Services.SequenceExecutionResult.Start(id));
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Success);
  }

  [Fact]
  public async Task RunStats_FailedAndExceptionGiveFailure() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "F", "X" });
    h.Sequences.Handler = (id, ct) => id == "F"
      ? Task.FromResult(FakeSequenceExecution.Failure(id))
      : throw new InvalidOperationException("stale reference");
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    SingleStatus(store, "F").Should().Be(SequenceRunStatus.Failure);
    SingleStatus(store, "X").Should().Be(SequenceRunStatus.Failure);
  }

  [Fact]
  public async Task RunStats_AStopByHandGivesCancelledAndTheStopWaitsForTheRecord() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    // A slow store proves analyze finding I1: StopAsync returns only after the record is written, so
    // a DELETE after the stop cannot race the record and leave an orphan statistics file.
    var store = new InMemoryRunStatisticsStore { RecordDelay = TimeSpan.FromMilliseconds(200) };
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count == 1);
    await service.StopAsync("q1");

    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Cancelled);
  }

  [Fact]
  public async Task RunStats_AFailurePolicyStopGivesCancelled() {
    // Analyze finding A1: at this time the failure policy acts between runs (OnCycleCompleted), so it
    // never cancels a sequence that runs. This test simulates the safety rule of research R-002: it
    // sets the policy flag on the handle and cancels the run token, as the evaluator does.
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count == 1);
    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    handle.MarkStopRequestedByPolicy();
    await handle.Cts.CancelAsync();
    await WaitUntilStoppedAsync(service, "q1");

    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Cancelled);
  }

  [Fact]
  public async Task RunStats_TheSequenceTimeLimitGivesCancelled() {
    var h = new Harness(sequenceRepository: true);
    h.SequenceRepository!.SetWatchdog("A", 50);
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Cancelled);
  }

  [Fact]
  public async Task RunStats_FailedAfterTheWatchdogTimerFiredGivesCancelled() {
    var h = new Harness(sequenceRepository: true);
    h.SequenceRepository!.SetWatchdog("A", 50);
    h.AddQueue("q1", new[] { "A" });
    // A step that swallows the cancellation and reports a failure.
    h.Sequences.Handler = async (id, ct) => {
      try { await Task.Delay(Timeout.Infinite, ct); }
      catch (OperationCanceledException) { /* swallowed */ }
      return FakeSequenceExecution.Failure(id);
    };
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Cancelled);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RunStats_AHostShutdownRecordsNothing(bool swallowAndFail) {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    h.Sequences.Handler = async (id, ct) => {
      try { await Task.Delay(Timeout.Infinite, ct); }
      catch (OperationCanceledException) when (swallowAndFail) { return FakeSequenceExecution.Failure(id); }
      return FakeSequenceExecution.Success(id);
    };
    using var lifetime = new FakeLifetime();
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store, lifetime);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count == 1);
    lifetime.FireStopping();
    await WaitUntilStoppedAsync(service, "q1");

    store.Records.Should().BeEmpty();
  }

  [Fact]
  public async Task RunStats_AFaultBeforeTheSequenceStartsRecordsNothing() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    var guard = new BlockingForegroundGuard();
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store, foregroundGuard: guard);

    await service.StartAsync("q1");
    await WaitForAsync(() => Volatile.Read(ref guard.Calls) == 1);
    await service.StopAsync("q1");

    h.Sequences.Executed.Should().BeEmpty();
    store.Records.Should().BeEmpty();
  }

  [Fact]
  public async Task RunStats_AStoreThatThrowsDoesNotChangeTheRunAndLogsOneWarning() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    var store = new InMemoryRunStatisticsStore { Throws = true };
    var logger = new RunStatisticsTestLogger<QueueExecutionService>();
    var service = StatsService(h, store, logger: logger);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("1 sequence(s) executed.").And.NotContain("failed");
    logger.WarningCount.Should().Be(1);
  }

  [Fact]
  public async Task RunStats_TwoEntriesOfTheSameSequenceShareOneEntry() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "A" });
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    var map = await store.GetForQueueAsync("q1");
    map.Keys.Should().Equal("A");
    map["A"].SuccessCount.Should().Be(2);
  }

  [Fact]
  public async Task RunStats_GuardSequencesGetTheirOwnEntry() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), RelativeTimer("T", TimeSpan.Zero), OncePerRun("A"), EveryStep("E") });
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    var map = await store.GetForQueueAsync("q1");
    map.Keys.Should().Contain(new[] { "A", "B", "E", "T" });
    map["B"].SuccessCount.Should().BeGreaterThan(0);
    map["E"].SuccessCount.Should().BeGreaterThan(0);
  }

  [Fact]
  public async Task RunStats_AQueueRestartKeepsTheStatisticsAndDropsTheSelfRescheduleSlot() {
    // FR-004 and FR-014: the statistics stay after a stop and a start, and the restart does not
    // change how the template or a reschedule-self slot applies.
    var clock = new FakeTimeProvider(StatsStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") });
    var scheduled = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref scheduled, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };
    var store = new InMemoryRunStatisticsStore();
    var service = StatsService(h, store);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000);
    await service.StopAsync("q1");
    var beforeRestart = await store.GetAsync("q1", "A");
    beforeRestart!.SuccessCount.Should().Be(1);

    await service.StartAsync("q1");
    h.Runtime.GetEntries("q1").Select(e => e.SequenceId).Should().Equal("A");
    // A non-cyclic run with a pending reschedule-self slot stays alive until the slot fires. This run
    // ends by itself, so the restart dropped the slot.
    await WaitUntilStoppedAsync(service, "q1");
    service.IsRunning("q1").Should().BeFalse();
    clock.Advance(TimeSpan.FromMinutes(10));

    var afterRestart = await store.GetAsync("q1", "A");
    afterRestart!.SuccessCount.Should().Be(2);
    afterRestart.RecentRuns[0].Should().Be(beforeRestart.RecentRuns[0]);
    h.Sequences.Executed.Should().Equal("A", "A");
    (await store.GetAsync("q1", "R")).Should().BeNull();
  }
}

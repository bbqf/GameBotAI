using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 120: the queue engine hands one job to the notification dispatcher for each finished
/// queue entry, with the status of research R-008.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  private sealed class RecordingDispatcher : INotificationDispatcher {
    private readonly List<QueueNotificationJob> _jobs = new();

    public bool Throws { get; set; }

    public IReadOnlyList<QueueNotificationJob> Jobs { get { lock (_jobs) return _jobs.ToList(); } }

    public void Enqueue(QueueNotificationJob job) {
      if (Throws) throw new InvalidOperationException("hand-off failed");
      lock (_jobs) _jobs.Add(job);
    }

    public void ResetStreaks(string queueId) { }

    public void SendAlert(QueueAlert alert) { }
  }

  private static QueueExecutionService NotifyService(
      Harness h,
      RecordingDispatcher dispatcher,
      Microsoft.Extensions.Hosting.IHostApplicationLifetime? lifetime = null,
      InMemoryRunStatisticsStore? store = null,
      Microsoft.Extensions.Logging.ILogger<QueueExecutionService>? logger = null)
    => new(h.Queues, h.Runtime, h.Templates, h.Sequences, h.Sessions, h.Log,
      logger ?? NullLogger<QueueExecutionService>.Instance, h.Registry,
      lifetime: lifetime, timeProvider: h.Clock, sequences: h.SequenceRepository,
      runStatistics: store, notifications: dispatcher);

  [Fact]
  public async Task Notify_SuccessAndFailureAndAnExceptionGiveOneJobEach() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "OK", "BAD", "FAULT" });
    h.Sequences.Handler = (id, ct) => id switch {
      "OK" => Task.FromResult(FakeSequenceExecution.Success(id)),
      "BAD" => Task.FromResult(FakeSequenceExecution.Failure(id)),
      _ => throw new InvalidOperationException("stale reference")
    };
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    dispatcher.Jobs.Select(j => (j.QueueId, j.SequenceId, j.Status)).Should().Equal(
      ("q1", "OK", NotificationRunStatus.Success),
      ("q1", "BAD", NotificationRunStatus.Failure),
      ("q1", "FAULT", NotificationRunStatus.Failure));
  }

  [Fact]
  public async Task Notify_NoStatisticsStoreStillSendsTheJob() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, store: null);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    dispatcher.Jobs.Should().ContainSingle().Which.Status.Should().Be(NotificationRunStatus.Success);
  }

  [Fact]
  public async Task QueueStop_WhileHostRuns_SendsCancelled() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    using var lifetime = new FakeLifetime();
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, lifetime);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count == 1);
    await service.StopAsync("q1");

    var job = dispatcher.Jobs.Should().ContainSingle().Subject;
    job.Status.Should().Be(NotificationRunStatus.Cancelled);
    job.SequenceId.Should().Be("A");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task HostStop_SendsNothing(bool swallowAndFail) {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    h.Sequences.Handler = async (id, ct) => {
      try { await Task.Delay(Timeout.Infinite, ct); }
      catch (OperationCanceledException) when (swallowAndFail) { return FakeSequenceExecution.Failure(id); }
      return FakeSequenceExecution.Success(id);
    };
    using var lifetime = new FakeLifetime();
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, lifetime);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count == 1);
    lifetime.FireStopping();
    await WaitUntilStoppedAsync(service, "q1");

    dispatcher.Jobs.Should().BeEmpty();
  }

  [Fact]
  public async Task Watchdog_TimeLimit_MapsToFailure() {
    var h = new Harness(sequenceRepository: true);
    h.SequenceRepository!.SetWatchdog("A", 50);
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStatisticsStore();
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, store: store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    dispatcher.Jobs.Should().ContainSingle().Which.Status.Should().Be(NotificationRunStatus.Failure);
    // The statistics record does not change: it still says Cancelled.
    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Cancelled);
  }

  [Fact]
  public async Task Watchdog_FailedResultAfterTheTimerFired_MapsToFailure() {
    var h = new Harness(sequenceRepository: true);
    h.SequenceRepository!.SetWatchdog("A", 50);
    h.AddQueue("q1", new[] { "A" });
    h.Sequences.Handler = async (id, ct) => {
      try { await Task.Delay(Timeout.Infinite, ct); }
      catch (OperationCanceledException) { /* swallowed */ }
      return FakeSequenceExecution.Failure(id);
    };
    var store = new InMemoryRunStatisticsStore();
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, store: store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    dispatcher.Jobs.Should().ContainSingle().Which.Status.Should().Be(NotificationRunStatus.Failure);
    SingleStatus(store, "A").Should().Be(SequenceRunStatus.Cancelled);
  }

  [Fact]
  public async Task Notify_ADispatcherThatThrowsDoesNotChangeTheRun() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B" });
    var dispatcher = new RecordingDispatcher { Throws = true };
    var service = NotifyService(h, dispatcher);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B");
  }

  [Fact]
  public async Task Notify_ARunWithNoDispatcherWorksAsBefore() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A");
  }

  [Theory]
  [InlineData(SequenceRunStatus.Success, false, "Success")]
  [InlineData(SequenceRunStatus.Failure, false, "Failure")]
  [InlineData(SequenceRunStatus.Cancelled, false, "Cancelled")]
  [InlineData(SequenceRunStatus.Cancelled, true, "Failure")]
  public void NotificationStatusFor_MapsTheStatus(SequenceRunStatus status, bool watchdog, string expected) {
    QueueExecutionService.NotificationStatusFor(status, watchdog).ToString().Should().Be(expected);
  }
}

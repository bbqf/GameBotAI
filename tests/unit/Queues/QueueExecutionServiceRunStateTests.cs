using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 098 (#203): a queue run is recorded as Running when it starts and forgotten when it ends —
/// unless the host is shutting down, which is the ending a service restart must undo.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  private sealed class InMemoryRunStateStore : IQueueRunStateStore {
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    public bool Throws { get; set; }

    public IReadOnlyList<string> Ids { get { lock (_ids) return _ids.ToList(); } }

    public Task MarkRunningAsync(string queueId) {
      if (Throws) throw new System.IO.IOException("disk full");
      lock (_ids) _ids.Add(queueId);
      return Task.CompletedTask;
    }

    public Task ClearAsync(string queueId) {
      if (Throws) throw new System.IO.IOException("disk full");
      lock (_ids) _ids.Remove(queueId);
      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListRunningAsync() => Task.FromResult(Ids);
  }

  private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable {
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();
    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;
    public void FireStarted() => _started.Cancel();
    public void FireStopping() => _stopping.Cancel();
    public void StopApplication() => _stopping.Cancel();
    public void Dispose() { _started.Dispose(); _stopping.Dispose(); _stopped.Dispose(); }
  }

  private static QueueExecutionService RunStateService(Harness h, InMemoryRunStateStore store, IHostApplicationLifetime? lifetime = null)
    => new(h.Queues, h.Runtime, h.Templates, h.Sequences, h.Sessions, h.Log, NullLogger<QueueExecutionService>.Instance, h.Registry,
      lifetime: lifetime, runState: store);

  private static void BlockEverySequence(Harness h)
    => h.Sequences.Handler = async (id, ct) => { await Task.Delay(Timeout.Infinite, ct); return FakeSequenceExecution.Success(id); };

  [Fact]
  public async Task StartedRunIsRecordedAsRunning() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStateStore();
    var service = RunStateService(h, store);

    (await service.StartAsync("q1")).Should().Be(QueueStartOutcome.Started);

    store.Ids.Should().Equal("q1");
    await service.StopAsync("q1");
  }

  [Fact]
  public async Task RefusedStartsRecordNothing() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" }, serial: "emu-shared");
    h.AddQueue("q2", new[] { "B" }, serial: "emu-shared");
    BlockEverySequence(h);
    var store = new InMemoryRunStateStore();
    var service = RunStateService(h, store);

    (await service.StartAsync("missing")).Should().Be(QueueStartOutcome.NotFound);
    (await service.StartAsync("q1")).Should().Be(QueueStartOutcome.Started);
    (await service.StartAsync("q2")).Should().Be(QueueStartOutcome.DeviceInUse);

    store.Ids.Should().Equal("q1");
    await service.StopAsync("q1");
  }

  [Fact]
  public async Task HostShutdownKeepsTheRecord() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStateStore();
    using var lifetime = new FakeLifetime();
    var service = RunStateService(h, store, lifetime);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 1);

    lifetime.FireStopping();
    await WaitUntilStoppedAsync(service, "q1");

    service.IsRunning("q1").Should().BeFalse();
    store.Ids.Should().Equal("q1");
  }

  [Fact]
  public async Task OperatorStopForgetsTheRecord() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStateStore();
    using var lifetime = new FakeLifetime();
    var service = RunStateService(h, store, lifetime);

    await service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 1);
    await service.StopAsync("q1");

    store.Ids.Should().BeEmpty();
  }

  [Fact]
  public async Task RunThatCompletesOnItsOwnForgetsTheRecord() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B" });
    var store = new InMemoryRunStateStore();
    var service = RunStateService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");
    await WaitForAsync(() => store.Ids.Count == 0);

    store.Ids.Should().BeEmpty();
  }

  [Fact]
  public async Task RunLevelFailureForgetsTheRecord() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" }, linkTemplate: false);
    var store = new InMemoryRunStateStore();
    var service = RunStateService(h, store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");
    await WaitForAsync(() => store.Ids.Count == 0);

    h.Log.FinalStatus.Should().Be("failure");
    store.Ids.Should().BeEmpty();
  }

  [Fact]
  public async Task FailingStoreNeverBreaksStartOrStop() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    BlockEverySequence(h);
    var store = new InMemoryRunStateStore { Throws = true };
    var service = RunStateService(h, store);

    (await service.StartAsync("q1")).Should().Be(QueueStartOutcome.Started);
    await WaitForAsync(() => h.Sequences.Executed.Count >= 1);
    await service.StopAsync("q1");

    service.IsRunning("q1").Should().BeFalse();
    h.Runtime.GetStatus("q1").Should().Be(QueueExecutionStatus.Stopped);
    h.Log.QueueFinalizes.Should().Be(1);
  }
}

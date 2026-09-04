using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Config;
using GameBot.Domain.Queues;
using GameBot.Service.Hosted;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// An emulator that dies mid-run leaves the queue "Running" while every firing dispatches input into
/// nothing, and only a queue start brings the instance back. These cover the watchdog that notices:
/// it must tolerate a brief ADB blip, act on a sustained outage, and never restart in a loop.
/// </summary>
public sealed class QueueDeviceWatchdogServiceTests {
  private sealed class FakeProbe : IEmulatorDeviceProbe {
    public bool IsAvailable { get; set; } = true;
    public Dictionary<string, bool> Responsive { get; } = new(StringComparer.Ordinal);
    public Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default)
      => Task.FromResult(Responsive.TryGetValue(adbSerial, out var ok) && ok);
  }

  private sealed class FakeExecution : IQueueExecutionService {
    public HashSet<string> Running { get; } = new(StringComparer.Ordinal);
    public List<string> Stopped { get; } = new();
    public List<string> Started { get; } = new();

    public Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default) {
      Started.Add(queueId);
      Running.Add(queueId);
      return Task.FromResult(QueueStartOutcome.Started);
    }

    public Task StopAsync(string queueId, CancellationToken ct = default) {
      Stopped.Add(queueId);
      Running.Remove(queueId);
      return Task.CompletedTask;
    }

    public bool IsRunning(string queueId) => Running.Contains(queueId);

    public LiveScheduleResult ScheduleRelative(string queueId, string sequenceId, TimeSpan offset)
      => new(LiveScheduleOutcome.NotRunning, default);
  }

  private sealed class FakeQueues : IQueueRepository {
    public List<ExecutionQueue> Queues { get; } = new();
    public Task<ExecutionQueue?> GetAsync(string id) => Task.FromResult(Queues.FirstOrDefault(q => q.Id == id));
    public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => Task.FromResult<IReadOnlyList<ExecutionQueue>>(Queues);
    public Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) => Task.FromResult(queue);
    public Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) => Task.FromResult(queue);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  /// <summary>The sweep takes its collaborators as arguments, so the hosted service's own provider
  /// is never touched in these tests.</summary>
  private sealed class EmptyProvider : IServiceProvider {
    public object? GetService(Type serviceType) => null;
  }

  private static (QueueDeviceWatchdogService Svc, FakeProbe Probe, FakeExecution Exec, FakeQueues Queues, FakeTimeProvider Clock)
      Build(int strikes = 3, int cooldownMs = 600000) {
    var probe = new FakeProbe();
    var exec = new FakeExecution();
    var queues = new FakeQueues();
    queues.Queues.Add(new ExecutionQueue { Id = "q1", EmulatorSerial = "emulator-5554" });
    exec.Running.Add("q1");
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
    var config = new AppConfig { QueueDeviceWatchdogStrikes = strikes, QueueDeviceWatchdogCooldownMs = cooldownMs };
    var svc = new QueueDeviceWatchdogService(
      new EmptyProvider(), config, NullLogger<QueueDeviceWatchdogService>.Instance, clock);
    return (svc, probe, exec, queues, clock);
  }

  [Fact]
  public async Task LeavesAHealthyQueueAlone() {
    var (svc, probe, exec, queues, _) = Build();
    probe.Responsive["emulator-5554"] = true;

    for (var i = 0; i < 5; i++) await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    exec.Started.Should().BeEmpty();
    exec.Stopped.Should().BeEmpty();
  }

  [Fact]
  public async Task DoesNotRestartBeforeTheStrikeLimit() {
    // ADB drops a device briefly for reasons that resolve on their own, and a restart costs the run
    // its live self-reschedules — so one bad probe must not be enough.
    var (svc, probe, exec, queues, _) = Build(strikes: 3);
    probe.Responsive["emulator-5554"] = false;

    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);
    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    exec.Started.Should().BeEmpty();
  }

  [Fact]
  public async Task RestartsTheQueueAfterASustainedOutage() {
    var (svc, probe, exec, queues, _) = Build(strikes: 3);
    probe.Responsive["emulator-5554"] = false;

    for (var i = 0; i < 3; i++) await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    exec.Stopped.Should().ContainSingle().Which.Should().Be("q1");
    exec.Started.Should().ContainSingle().Which.Should().Be("q1");
  }

  [Fact]
  public async Task ARecoveredProbeClearsTheStrikes() {
    var (svc, probe, exec, queues, _) = Build(strikes: 3);
    probe.Responsive["emulator-5554"] = false;
    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);
    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    probe.Responsive["emulator-5554"] = true;
    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    // Strikes reset, so the next outage starts counting from zero rather than tipping over.
    probe.Responsive["emulator-5554"] = false;
    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);
    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    exec.Started.Should().BeEmpty();
  }

  [Fact]
  public async Task DoesNotRestartAgainInsideTheCooldown() {
    // A host whose emulator cannot come back must not be restarted every minute forever.
    var (svc, probe, exec, queues, clock) = Build(strikes: 1, cooldownMs: 600000);
    probe.Responsive["emulator-5554"] = false;

    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);
    exec.Started.Should().ContainSingle();

    clock.Advance(TimeSpan.FromMinutes(5));
    for (var i = 0; i < 3; i++) await svc.SweepAsync(probe, exec, queues, CancellationToken.None);
    exec.Started.Should().ContainSingle("the cooldown has not elapsed");

    clock.Advance(TimeSpan.FromMinutes(6));
    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);
    exec.Started.Should().HaveCount(2);
  }

  [Fact]
  public async Task IgnoresAQueueThatIsNotRunning() {
    var (svc, probe, exec, queues, _) = Build(strikes: 1);
    probe.Responsive["emulator-5554"] = false;
    exec.Running.Clear();

    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    exec.Started.Should().BeEmpty();
  }

  [Fact]
  public async Task DoesNothingWhenAdbProbingIsUnavailable() {
    var (svc, probe, exec, queues, _) = Build(strikes: 1);
    probe.IsAvailable = false;
    probe.Responsive["emulator-5554"] = false;

    await svc.SweepAsync(probe, exec, queues, CancellationToken.None);

    exec.Started.Should().BeEmpty();
  }
}

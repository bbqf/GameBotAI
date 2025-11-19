using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Hosted;
using GameBot.Domain.Services;
using GameBot.Emulator.Session;
using GameBot.Domain.Sessions; // Needed for EmulatorSession type
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
#pragma warning disable CA2000

namespace GameBot.UnitTests;

public class TriggerBackgroundWorkerTests {
  [Fact]
  public async Task SkipsEvaluationWhenNoSessions() {
    var logger = NullLogger<TriggerBackgroundWorker>.Instance;
    var coordinator = new FakeCoordinator();
    var sessions = new FakeSessions(activeCount: 0);
    var options = new StaticOptions(new TriggerWorkerOptions {
      IntervalSeconds = 1,
      IdleBackoffSeconds = 1,
      SkipWhenNoSessions = true,
      GameFilter = null
    });

    var metrics = new TriggerEvaluationMetrics();
    var worker = new TriggerBackgroundWorker(logger, coordinator, sessions, options, metrics);

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(1200));

    await worker.StartAsync(cts.Token).ConfigureAwait(false);
    // Wait until cancellation fires and worker stops
    await worker.StopAsync(CancellationToken.None).ConfigureAwait(false);

    coordinator.CallCount.Should().Be(0, because: "no sessions should skip evaluation cycles");
    metrics.SkippedNoSessions.Should().BeGreaterOrEqualTo(1);
  }

  [Fact]
  public async Task EvaluatesWhenSessionsExistAndPassesFilter() {
    var logger = NullLogger<TriggerBackgroundWorker>.Instance;
    var coordinator = new FakeCoordinator();
    var sessions = new FakeSessions(activeCount: 1);
    var options = new StaticOptions(new TriggerWorkerOptions {
      IntervalSeconds = 1,
      IdleBackoffSeconds = 1,
      SkipWhenNoSessions = true,
      GameFilter = "game-123"
    });

    var metrics = new TriggerEvaluationMetrics();
    var worker = new TriggerBackgroundWorker(logger, coordinator, sessions, options, metrics);

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

    await worker.StartAsync(cts.Token).ConfigureAwait(false);
    await worker.StopAsync(CancellationToken.None).ConfigureAwait(false);

    coordinator.CallCount.Should().BeGreaterOrEqualTo(1);
    metrics.Evaluations.Should().BeGreaterOrEqualTo(1);
    coordinator.LastFilter.Should().Be("game-123");
  }

  private sealed class FakeCoordinator : ITriggerEvaluationCoordinator {
    public int CallCount { get; private set; }
    public string? LastFilter { get; private set; }

    public Task<int> EvaluateAllAsync(string? gameIdFilter, CancellationToken ct) {
      CallCount++;
      LastFilter = gameIdFilter;
      return Task.FromResult(1);
    }
  }

  private sealed class FakeSessions : ISessionManager {
    public FakeSessions(int activeCount) { ActiveCount = activeCount; }
    public int ActiveCount { get; set; }
    public bool CanCreateSession => false;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotImplementedException();
    public EmulatorSession? GetSession(string id) => null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => Array.Empty<EmulatorSession>();
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class StaticOptions : IOptionsMonitor<TriggerWorkerOptions> {
    public StaticOptions(TriggerWorkerOptions value) { CurrentValue = value; }
    public TriggerWorkerOptions CurrentValue { get; }
    public TriggerWorkerOptions Get(string? name) => CurrentValue;
    public IDisposable OnChange(Action<TriggerWorkerOptions, string?> listener) => DummyDisposable.Instance;

    private sealed class DummyDisposable : IDisposable {
      public static readonly DummyDisposable Instance = new();
      public void Dispose() { }
    }
  }
}

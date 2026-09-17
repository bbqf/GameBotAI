using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Service.Hosted;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 098 (#203): after a restart the service starts again every queue that was Running and opts
/// in — and only those — with one logged attempt per recorded queue.
/// </summary>
public sealed class QueueResumeOnStartupServiceTests {
  private sealed class FakeStore : IQueueRunStateStore {
    private readonly List<string> _ids = new();
    public Exception? ListThrows { get; set; }
    public List<string> Ids { get { lock (_ids) return _ids.ToList(); } }
    public int ListCalls { get; private set; }

    public FakeStore(params string[] ids) => _ids.AddRange(ids);

    public Task MarkRunningAsync(string queueId) { lock (_ids) { if (!_ids.Contains(queueId)) _ids.Add(queueId); } return Task.CompletedTask; }
    public Task ClearAsync(string queueId) { lock (_ids) _ids.Remove(queueId); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> ListRunningAsync() {
      ListCalls++;
      if (ListThrows is not null) throw ListThrows;
      return Task.FromResult<IReadOnlyList<string>>(Ids);
    }
  }

  private sealed class FakeQueues : IQueueRepository {
    public List<ExecutionQueue> Queues { get; } = new();
    public Task<ExecutionQueue?> GetAsync(string id) => Task.FromResult(Queues.FirstOrDefault(q => q.Id == id));
    public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => Task.FromResult<IReadOnlyList<ExecutionQueue>>(Queues);
    public Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) => Task.FromResult(queue);
    public Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) => Task.FromResult(queue);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);

    public FakeQueues With(string id, bool resume) {
      Queues.Add(new ExecutionQueue { Id = id, Name = id, EmulatorSerial = "emu-" + id, ResumeOnServiceStart = resume });
      return this;
    }
  }

  private sealed class FakeExecution : IQueueExecutionService {
    public List<string> Started { get; } = new();
    public Dictionary<string, QueueStartOutcome> Outcomes { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Throws { get; } = new(StringComparer.Ordinal);
    public Action<string>? OnStart { get; set; }

    public Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default) {
      Started.Add(queueId);
      OnStart?.Invoke(queueId);
      if (Throws.Contains(queueId)) throw new InvalidOperationException("boom");
      return Task.FromResult(Outcomes.TryGetValue(queueId, out var o) ? o : QueueStartOutcome.Started);
    }
    public Task StopAsync(string queueId, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsRunning(string queueId) => false;
    public LiveScheduleResult ScheduleRelative(string queueId, string sequenceId, TimeSpan offset) => new(LiveScheduleOutcome.NotRunning, default);
  }

  private sealed class CapturingLogger : ILogger<QueueResumeOnStartupService> {
    private readonly List<(int EventId, LogLevel Level, string Message)> _entries = new();
    public IReadOnlyList<(int EventId, LogLevel Level, string Message)> Entries { get { lock (_entries) return _entries.ToList(); } }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
      lock (_entries) _entries.Add((eventId.Id, logLevel, formatter(state, exception)));
    }
  }

  private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable {
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();
    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;
    public void FireStarted() => _started.Cancel();
    public void StopApplication() => _stopping.Cancel();
    public void Dispose() { _started.Dispose(); _stopping.Dispose(); _stopped.Dispose(); }
  }

  /// <summary>For tests that call the pass directly and never touch the lifetime.</summary>
  private sealed class UnusedLifetime : IHostApplicationLifetime {
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
  }

  /// <summary>For tests that call the pass directly and never resolve anything.</summary>
  private sealed class EmptyProvider : IServiceProvider {
    public object? GetService(Type serviceType) => null;
  }

  private static (QueueResumeOnStartupService Svc, CapturingLogger Log) Build(IServiceProvider? provider = null, IHostApplicationLifetime? lifetime = null) {
    var log = new CapturingLogger();
    var svc = new QueueResumeOnStartupService(provider ?? new EmptyProvider(), lifetime ?? new UnusedLifetime(), log);
    return (svc, log);
  }

  [Fact]
  public async Task RecordedOptedInQueueIsStarted() {
    var store = new FakeStore("q1");
    var queues = new FakeQueues().With("q1", resume: true);
    var exec = new FakeExecution();
    var (svc, log) = Build();

    await svc.ResumeAsync(store, queues, () => exec,CancellationToken.None);

    exec.Started.Should().Equal("q1");
    store.Ids.Should().Equal(new[] { "q1" }, "the resumed run now owns the record");
    log.Entries.Should().ContainSingle().Which.EventId.Should().Be(7300);
  }

  [Fact]
  public async Task RecordedQueueThatDoesNotOptInStaysStoppedAndLosesItsRecord() {
    var store = new FakeStore("q1");
    var queues = new FakeQueues().With("q1", resume: false);
    var exec = new FakeExecution();
    var (svc, log) = Build();

    await svc.ResumeAsync(store, queues, () => exec,CancellationToken.None);

    exec.Started.Should().BeEmpty();
    store.Ids.Should().BeEmpty();
    log.Entries.Should().ContainSingle().Which.EventId.Should().Be(7301);
  }

  [Fact]
  public async Task RecordOfADeletedQueueIsDiscarded() {
    var store = new FakeStore("gone");
    var exec = new FakeExecution();
    var (svc, log) = Build();

    await svc.ResumeAsync(store, new FakeQueues(), () => exec,CancellationToken.None);

    exec.Started.Should().BeEmpty();
    store.Ids.Should().BeEmpty();
    log.Entries.Should().ContainSingle().Which.EventId.Should().Be(7302);
  }

  [Fact]
  public async Task OptedInQueueThatWasNotRunningIsNotStarted() {
    var store = new FakeStore();
    var queues = new FakeQueues().With("q1", resume: true);
    var exec = new FakeExecution();
    var (svc, log) = Build();

    await svc.ResumeAsync(store, queues, () => exec,CancellationToken.None);

    exec.Started.Should().BeEmpty();
    log.Entries.Should().BeEmpty();
  }

  [Fact] // building the execution service pulls in the device graph; a start with nothing to resume must not
  public async Task ExecutionServiceIsNotBuiltWhenNothingIsResumed() {
    var store = new FakeStore("gone", "q1");
    var queues = new FakeQueues().With("q1", resume: false);
    var built = 0;
    var (svc, _) = Build();

    await svc.ResumeAsync(store, queues, () => { built++; return new FakeExecution(); }, CancellationToken.None);

    built.Should().Be(0);
  }

  [Fact]
  public async Task OneFailingResumeDoesNotStopTheOthers() {
    var store = new FakeStore("q1", "q2");
    var queues = new FakeQueues().With("q1", resume: true).With("q2", resume: true);
    var exec = new FakeExecution();
    exec.Throws.Add("q1");
    var (svc, log) = Build();

    await svc.ResumeAsync(store, queues, () => exec,CancellationToken.None);

    exec.Started.Should().Equal("q1", "q2");
    store.Ids.Should().Equal("q2");
    log.Entries.Select(e => e.EventId).Should().Equal(7303, 7300);
  }

  [Fact]
  public async Task DeviceInUseIsLoggedAndTheRecordDropped() {
    var store = new FakeStore("q1");
    var queues = new FakeQueues().With("q1", resume: true);
    var exec = new FakeExecution();
    exec.Outcomes["q1"] = QueueStartOutcome.DeviceInUse;
    var (svc, log) = Build();

    await svc.ResumeAsync(store, queues, () => exec,CancellationToken.None);

    store.Ids.Should().BeEmpty();
    var entry = log.Entries.Should().ContainSingle().Subject;
    entry.EventId.Should().Be(7300);
    entry.Message.Should().Contain("DeviceInUse");
  }

  [Fact]
  public async Task UnreadableRecordResumesNothingAndLogsOnce() {
    var store = new FakeStore { ListThrows = new System.IO.InvalidDataException("corrupt") };
    var queues = new FakeQueues().With("q1", resume: true);
    var exec = new FakeExecution();
    var (svc, log) = Build();

    await svc.ResumeAsync(store, queues, () => exec,CancellationToken.None);

    exec.Started.Should().BeEmpty();
    log.Entries.Should().ContainSingle().Which.EventId.Should().Be(7304);
  }

  [Fact]
  public async Task CancellationStopsThePassAndKeepsTheRemainingRecords() {
    var store = new FakeStore("q1", "q2");
    var queues = new FakeQueues().With("q1", resume: true).With("q2", resume: true);
    using var cts = new CancellationTokenSource();
    var exec = new FakeExecution { OnStart = _ => cts.Cancel() };
    var (svc, _) = Build();

    var pass = async () => await svc.ResumeAsync(store, queues, () => exec,cts.Token);

    await pass.Should().ThrowAsync<OperationCanceledException>();
    exec.Started.Should().Equal("q1");
    store.Ids.Should().Contain("q2");
  }

  [Fact]
  public async Task PassWaitsForTheApplicationToHaveStarted() {
    var store = new FakeStore();
    var services = new ServiceCollection();
    services.AddSingleton<IQueueRunStateStore>(store);
    services.AddSingleton<IQueueRepository>(new FakeQueues());
    services.AddSingleton<IQueueExecutionService>(new FakeExecution());
    using var provider = services.BuildServiceProvider();
    using var lifetime = new FakeLifetime();
    var (svc, _) = Build(provider, lifetime);

    await svc.StartAsync(CancellationToken.None);
    await Task.Delay(100);
    store.ListCalls.Should().Be(0, "nothing may resume before the host has finished starting");

    lifetime.FireStarted();
    var sw = Stopwatch.StartNew();
    while (store.ListCalls == 0 && sw.ElapsedMilliseconds < 5000) await Task.Delay(10);

    store.ListCalls.Should().Be(1);
    await svc.StopAsync(CancellationToken.None);
    svc.Dispose();
  }
}

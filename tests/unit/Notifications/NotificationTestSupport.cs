using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Notifications;
using GameBot.Domain.Queues;
using GameBot.Service.Services.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA1002, CA1819, CA2227

namespace GameBot.UnitTests.Notifications;

/// <summary>A queue repository in memory.</summary>
internal sealed class MemoryQueueRepository : IQueueRepository {
  private readonly Dictionary<string, ExecutionQueue> _items = new(StringComparer.Ordinal);

  public void Add(ExecutionQueue queue) { lock (_items) _items[queue.Id] = queue; }

  public void Remove(string id) { lock (_items) _items.Remove(id); }

  public Task<ExecutionQueue?> GetAsync(string id) {
    lock (_items) return Task.FromResult(_items.TryGetValue(id, out var q) ? q : null);
  }

  public Task<IReadOnlyList<ExecutionQueue>> ListAsync() {
    lock (_items) return Task.FromResult((IReadOnlyList<ExecutionQueue>)_items.Values.ToList());
  }

  public Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) { Add(queue); return Task.FromResult(queue); }

  public Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) { Add(queue); return Task.FromResult(queue); }

  public Task<bool> DeleteAsync(string id) { lock (_items) return Task.FromResult(_items.Remove(id)); }
}

/// <summary>A sequence repository in memory.</summary>
internal sealed class MemorySequenceRepository : ISequenceRepository {
  private readonly Dictionary<string, CommandSequence> _items = new(StringComparer.Ordinal);

  public bool GetThrows { get; set; }

  public void Add(string id, string name) { lock (_items) _items[id] = new CommandSequence { Id = id, Name = name }; }

  public Task<CommandSequence?> GetAsync(string id) {
    if (GetThrows) throw new InvalidOperationException("read failed");
    lock (_items) return Task.FromResult(_items.TryGetValue(id, out var s) ? s : null);
  }

  public Task<IReadOnlyList<CommandSequence>> ListAsync() {
    lock (_items) return Task.FromResult((IReadOnlyList<CommandSequence>)_items.Values.ToList());
  }

  public Task<CommandSequence> CreateAsync(CommandSequence sequence) { lock (_items) _items[sequence.Id] = sequence; return Task.FromResult(sequence); }

  public Task<CommandSequence> UpdateAsync(CommandSequence sequence) { lock (_items) _items[sequence.Id] = sequence; return Task.FromResult(sequence); }

  public Task<bool> DeleteAsync(string id) { lock (_items) return Task.FromResult(_items.Remove(id)); }
}

/// <summary>A target store in memory.</summary>
internal sealed class MemoryTargetStore : INotificationTargetStore {
  private readonly List<NotificationTarget> _items = new();

  public IReadOnlyList<NotificationTarget> List() { lock (_items) return _items.Select(t => t.Clone()).ToList(); }

  public NotificationTarget? Find(string id) { lock (_items) return _items.FirstOrDefault(t => t.Id == id)?.Clone(); }

  public NotificationTarget Create(NotificationTarget target) {
    var copy = target.Clone();
    if (string.IsNullOrEmpty(copy.Id)) copy.Id = Guid.NewGuid().ToString("N");
    lock (_items) _items.Add(copy);
    return copy.Clone();
  }

  public NotificationTarget? Update(NotificationTarget target) => throw new NotSupportedException();

  public bool Delete(string id) { lock (_items) return _items.RemoveAll(t => t.Id == id) > 0; }
}

/// <summary>A channel that records each send. The behavior of a send can change per test.</summary>
internal sealed class RecordingChannel : INotificationChannel {
  private readonly List<(string TargetId, string Text)> _sent = new();
  private int _calls;

  public RecordingChannel(string type = "telegram") { Type = type; }

  public string Type { get; }

  public string DisplayName => "Recording";

  public IReadOnlyList<NotificationField> Fields { get; } = new[] { new NotificationField("chatId", "Chat ID", false, true) };

  /// <summary>When set, this function runs for each send.</summary>
  public Func<NotificationTarget, string, CancellationToken, Task<NotificationSendResult>>? Behavior { get; set; }

  public int Calls => Volatile.Read(ref _calls);

  public IReadOnlyList<(string TargetId, string Text)> Sent { get { lock (_sent) return _sent.ToList(); } }

  public string? Validate(NotificationTarget target) => null;

  public async Task<NotificationSendResult> SendAsync(NotificationTarget target, string text, CancellationToken ct) {
    Interlocked.Increment(ref _calls);
    if (Behavior is not null) {
      var result = await Behavior(target, text, ct);
      if (result.Succeeded) lock (_sent) _sent.Add((target.Id, text));
      return result;
    }

    lock (_sent) _sent.Add((target.Id, text));
    return NotificationSendResult.Ok();
  }
}

/// <summary>A logger that keeps each formatted line.</summary>
internal sealed class CapturingLogger<T> : ILogger<T> {
  private readonly List<string> _lines = new();

  public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToList(); } }

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
    lock (_lines) _lines.Add($"{logLevel}: {formatter(state, exception)}");
  }
}

/// <summary>Builds a dispatcher, a worker, one queue and one target for the worker tests.</summary>
internal sealed class NotificationHarness : IAsyncDisposable {
  public NotificationHarness(NotificationLevel level = NotificationLevel.Failure, NotificationDispatchLimits? limits = null, int targetCount = 1) {
    Queues.Add(new ExecutionQueue { Id = "q1", Name = "Farm-1", EmulatorSerial = "emu-1", NotificationLevel = level });
    Sequences.Add("s1", "PNS.Collect");
    for (var i = 0; i < targetCount; i++) {
      var target = new NotificationTarget { Id = $"t{i}", Type = "telegram", Name = $"Target {i}" };
      target.Settings["chatId"] = "1";
      Targets.Create(target);
    }

    Dispatcher = new QueueNotificationDispatcher(DispatcherLog, limits);
    Worker = new QueueNotificationWorker(Dispatcher, Queues, Sequences, Targets, new INotificationChannel[] { Channel }, WorkerLog, limits);
  }

  public MemoryQueueRepository Queues { get; } = new();

  public MemorySequenceRepository Sequences { get; } = new();

  public MemoryTargetStore Targets { get; } = new();

  public RecordingChannel Channel { get; } = new();

  public CapturingLogger<QueueNotificationDispatcher> DispatcherLog { get; } = new();

  public CapturingLogger<QueueNotificationWorker> WorkerLog { get; } = new();

  public QueueNotificationDispatcher Dispatcher { get; }

  public QueueNotificationWorker Worker { get; }

  public static QueueNotificationJob Job(NotificationRunStatus status, string queueId = "q1", string sequenceId = "s1")
    => new(queueId, sequenceId, status, DateTimeOffset.Now);

  /// <summary>Handles one job with no worker loop, in the thread of the test.</summary>
  public Task HandleAsync(NotificationRunStatus status, string queueId = "q1", string sequenceId = "s1")
    => Worker.HandleAsync(NotificationWork.ForJob(Job(status, queueId, sequenceId)));

  /// <summary>Handles a list of jobs in order, and waits until the sends end.</summary>
  public async Task<IReadOnlyList<string>> RunAsync(params NotificationRunStatus[] statuses) {
    // One job at a time: the sends run in parallel, so the wait keeps the order of the messages.
    foreach (var status in statuses) {
      await HandleAsync(status);
      await WaitIdleAsync();
    }

    return Channel.Sent.Select(s => s.Text).ToList();
  }

  public async Task WaitIdleAsync(int timeoutMs = 5000) {
    var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (Worker.ActiveSends > 0 && DateTime.UtcNow < end) await Task.Delay(5);
  }

  public static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 10000) {
    var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!condition() && DateTime.UtcNow < end) await Task.Delay(5);
  }

  public async ValueTask DisposeAsync() {
    Dispatcher.Complete();
    await Worker.StopAsync(CancellationToken.None);
    Worker.Dispose();
  }
}

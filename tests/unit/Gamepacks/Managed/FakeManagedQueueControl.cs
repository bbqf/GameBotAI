using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Queues;

#pragma warning disable CA2007, CA1707, CA1002, CA1819, CA2227, CA1822, CA1812

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>
/// A fake queue port for the managed update tests (feature 148). It reads the queues from a repository, keeps the set of
/// running queue ids, and records each stop and start call. A test can make a stop time out, make a start fail, or hold a stop.
/// </summary>
internal sealed class FakeManagedQueueControl : IManagedQueueControl {
  private readonly IQueueRepository _queues;
  private readonly object _sync = new();

  public FakeManagedQueueControl(IQueueRepository queues) {
    _queues = queues;
  }

  /// <summary>The ids of the queues that run.</summary>
  public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>The ids whose stop call times out.</summary>
  public HashSet<string> StopTimesOut { get; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>The ids whose start call fails.</summary>
  public HashSet<string> StartFails { get; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>When set, each stop call awaits this function before it ends.</summary>
  public Func<string, Task>? OnStop { get; set; }

  /// <summary>The calls in order, as <c>stop:id</c> or <c>start:id</c>.</summary>
  public List<string> Calls { get; } = new();

  public IReadOnlyList<string> StopCalls => Snapshot("stop:");

  public IReadOnlyList<string> StartCalls => Snapshot("start:");

  public async Task<IReadOnlyList<ManagedQueueInfo>> ListAsync(CancellationToken ct = default) {
    var all = await _queues.ListAsync();
    lock (_sync) {
      return all.Select(q => new ManagedQueueInfo(q.Id, q.Name, q.LinkedTemplateId, Running.Contains(q.Id))).ToList();
    }
  }

  public async Task<bool> StopAsync(string queueId, TimeSpan limit, CancellationToken ct = default) {
    lock (_sync) {
      Calls.Add("stop:" + queueId);
    }

    if (OnStop is not null) {
      await OnStop(queueId);
    }

    lock (_sync) {
      if (StopTimesOut.Contains(queueId)) {
        return false;
      }

      Running.Remove(queueId);
      return true;
    }
  }

  public Task<string?> StartAsync(string queueId, CancellationToken ct = default) {
    lock (_sync) {
      Calls.Add("start:" + queueId);
      if (StartFails.Contains(queueId)) {
        return Task.FromResult<string?>("Injected start fault.");
      }

      Running.Add(queueId);
      return Task.FromResult<string?>(null);
    }
  }

  private List<string> Snapshot(string prefix) {
    lock (_sync) {
      return Calls.Where(c => c.StartsWith(prefix, StringComparison.Ordinal)).Select(c => c[prefix.Length..]).ToList();
    }
  }
}

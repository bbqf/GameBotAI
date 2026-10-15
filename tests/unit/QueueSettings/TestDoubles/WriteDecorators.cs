using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;

#pragma warning disable CA2007, CA1707, CA1002, CA1819, CA2227, CA1822, CA1812

namespace GameBot.UnitTests.QueueSettings.TestDoubles;

/// <summary>
/// The shared probe of the test decorators (feature 145, research R11). It counts writes and deletes, records the
/// time of each write, can throw at a chosen write (FR-018), and can hold a write until the test releases it (FR-017).
/// </summary>
internal sealed class WriteProbe {
  private int _writes;
  private int _deletes;

  /// <summary>The number of writes so far.</summary>
  public int Writes => _writes;

  /// <summary>The number of delete calls so far.</summary>
  public int Deletes => _deletes;

  /// <summary>The log of all write and delete calls.</summary>
  public List<string> Log { get; } = new();

  /// <summary>The time of each write.</summary>
  public List<DateTimeOffset> Times { get; } = new();

  /// <summary>The 1-based number of the write that throws. 0 means never.</summary>
  public int FailAtWrite { get; set; }

  /// <summary>When set, the write that has this 1-based number waits until <see cref="Release"/> is called.</summary>
  public int BlockAtWrite { get; set; }

  /// <summary>Completes when a write waits at the block.</summary>
  public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

  private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

  /// <summary>Lets the blocked write go on.</summary>
  public void Release() => _release.TrySetResult();

  /// <summary>Called by a decorator before a write.</summary>
  public async Task BeforeWriteAsync(string what) {
    var number = Interlocked.Increment(ref _writes);
    lock (Log) {
      Log.Add("write:" + what);
      Times.Add(DateTimeOffset.UtcNow);
    }

    if (FailAtWrite != 0 && number == FailAtWrite) {
      throw new InvalidOperationException("Injected write fault at write " + number);
    }

    if (BlockAtWrite != 0 && number == BlockAtWrite) {
      Reached.TrySetResult();
      await _release.Task.ConfigureAwait(false);
    }
  }

  /// <summary>Called by a decorator before a delete.</summary>
  public void OnDelete(string what) {
    Interlocked.Increment(ref _deletes);
    lock (Log) {
      Log.Add("delete:" + what);
    }
  }

  /// <summary>Sets the write count to zero, and clears the log and the faults.</summary>
  public void Reset() {
    _writes = 0;
    _deletes = 0;
    FailAtWrite = 0;
    BlockAtWrite = 0;
    lock (Log) {
      Log.Clear();
      Times.Clear();
    }
  }
}

internal sealed class ProbedSequenceRepository : ISequenceRepository {
  private readonly ISequenceRepository _inner;
  private readonly WriteProbe _probe;

  public ProbedSequenceRepository(ISequenceRepository inner, WriteProbe probe) {
    _inner = inner;
    _probe = probe;
  }

  public Task<CommandSequence?> GetAsync(string id) => _inner.GetAsync(id);

  public Task<IReadOnlyList<CommandSequence>> ListAsync() => _inner.ListAsync();

  public async Task<CommandSequence> CreateAsync(CommandSequence sequence) {
    await _probe.BeforeWriteAsync("sequence:" + sequence.Name);
    return await _inner.CreateAsync(sequence);
  }

  public async Task<CommandSequence> UpdateAsync(CommandSequence sequence) {
    await _probe.BeforeWriteAsync("sequence:" + sequence.Name);
    return await _inner.UpdateAsync(sequence);
  }

  public Task<bool> DeleteAsync(string id) {
    _probe.OnDelete("sequence:" + id);
    return _inner.DeleteAsync(id);
  }
}

internal sealed class ProbedCommandRepository : ICommandRepository {
  private readonly ICommandRepository _inner;
  private readonly WriteProbe _probe;

  public ProbedCommandRepository(ICommandRepository inner, WriteProbe probe) {
    _inner = inner;
    _probe = probe;
  }

  public async Task<Command> AddAsync(Command command, CancellationToken ct = default) {
    await _probe.BeforeWriteAsync("command:" + command.Name);
    return await _inner.AddAsync(command, ct);
  }

  public Task<Command?> GetAsync(string id, CancellationToken ct = default) => _inner.GetAsync(id, ct);

  public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => _inner.ListAsync(ct);

  public async Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) {
    await _probe.BeforeWriteAsync("command:" + command.Name);
    return await _inner.UpdateAsync(command, ct);
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    _probe.OnDelete("command:" + id);
    return _inner.DeleteAsync(id, ct);
  }
}

internal sealed class ProbedQueueRepository : IQueueRepository {
  private readonly IQueueRepository _inner;
  private readonly WriteProbe _probe;

  public ProbedQueueRepository(IQueueRepository inner, WriteProbe probe) {
    _inner = inner;
    _probe = probe;
  }

  public Task<ExecutionQueue?> GetAsync(string id) => _inner.GetAsync(id);

  public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => _inner.ListAsync();

  public async Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) {
    await _probe.BeforeWriteAsync("queue:" + queue.Name);
    return await _inner.CreateAsync(queue);
  }

  public async Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) {
    await _probe.BeforeWriteAsync("queue:" + queue.Name);
    return await _inner.UpdateAsync(queue);
  }

  public Task<bool> DeleteAsync(string id) {
    _probe.OnDelete("queue:" + id);
    return _inner.DeleteAsync(id);
  }
}

internal sealed class ProbedQueueTemplateRepository : IQueueTemplateRepository {
  private readonly IQueueTemplateRepository _inner;
  private readonly WriteProbe _probe;

  public ProbedQueueTemplateRepository(IQueueTemplateRepository inner, WriteProbe probe) {
    _inner = inner;
    _probe = probe;
  }

  public Task<QueueTemplate?> GetAsync(string id) => _inner.GetAsync(id);

  public Task<IReadOnlyList<QueueTemplate>> ListAsync() => _inner.ListAsync();

  public Task<QueueTemplate?> FindByNameAsync(string name) => _inner.FindByNameAsync(name);

  public async Task<QueueTemplate> CreateAsync(QueueTemplate item) {
    await _probe.BeforeWriteAsync("template:" + item.Name);
    return await _inner.CreateAsync(item);
  }

  public async Task<QueueTemplate> UpdateAsync(QueueTemplate item) {
    await _probe.BeforeWriteAsync("template:" + item.Name);
    return await _inner.UpdateAsync(item);
  }

  public Task<bool> DeleteAsync(string id) {
    _probe.OnDelete("template:" + id);
    return _inner.DeleteAsync(id);
  }
}

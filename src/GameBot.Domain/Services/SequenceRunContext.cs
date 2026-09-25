using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

/// <summary>
/// The queue and the sequence of the current run, made ambient for the duration of that run
/// (feature 105). A <c>lastRun</c> condition needs both values, but the runner does not get them as
/// parameters: a new parameter through each private method of <see cref="SequenceRunner"/> would make
/// its large methods larger, and the build-time analyzers degrade on large methods.
/// <para>
/// Only a queue run pushes a context. An ad-hoc run and a dry-run have no context, so a <c>lastRun</c>
/// condition is false there. A nested run pushes its own context, so <c>self</c> always names the
/// sequence that owns the step. The same pattern as the device context (feature 079) and the time-limit
/// scope (feature 094).
/// </para>
/// </summary>
public sealed class SequenceRunContext {
  private static readonly AsyncLocal<SequenceRunContext?> Ambient = new();

  /// <summary>Creates a context.</summary>
  /// <param name="queueId">The queue of the run.</param>
  /// <param name="sequenceId">The sequence of the run. <c>self</c> resolves to this value.</param>
  /// <param name="lastRunEvaluator">Evaluates a <c>lastRun</c> leaf for this run. It does not apply <c>negate</c>.</param>
  public SequenceRunContext(string queueId, string sequenceId, Func<LastRunStepCondition, CancellationToken, Task<bool>> lastRunEvaluator) {
    ArgumentException.ThrowIfNullOrWhiteSpace(queueId);
    ArgumentException.ThrowIfNullOrWhiteSpace(sequenceId);
    ArgumentNullException.ThrowIfNull(lastRunEvaluator);
    QueueId = queueId;
    SequenceId = sequenceId;
    LastRunEvaluator = lastRunEvaluator;
  }

  /// <summary>The queue of the run.</summary>
  public string QueueId { get; }

  /// <summary>The sequence of the run.</summary>
  public string SequenceId { get; }

  /// <summary>Evaluates a <c>lastRun</c> leaf for this run.</summary>
  public Func<LastRunStepCondition, CancellationToken, Task<bool>> LastRunEvaluator { get; }

  /// <summary>The context of the current async flow, or null (an ad-hoc run or a dry-run).</summary>
  public static SequenceRunContext? Current => Ambient.Value;

  /// <summary>
  /// Makes <paramref name="context"/> current until the returned handle is disposed. The dispose puts
  /// back the earlier value.
  /// </summary>
  public static IDisposable Push(SequenceRunContext context) {
    ArgumentNullException.ThrowIfNull(context);
    var previous = Ambient.Value;
    Ambient.Value = context;
    return new Restore(previous);
  }

  private sealed class Restore : IDisposable {
    private readonly SequenceRunContext? _previous;
    private bool _disposed;

    public Restore(SequenceRunContext? previous) => _previous = previous;

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      Ambient.Value = _previous;
    }
  }
}

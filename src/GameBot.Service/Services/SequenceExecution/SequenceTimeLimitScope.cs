namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// The time bound a queue firing runs under, made ambient for the duration of that firing (feature 094).
/// <para>
/// A firing is cancelled for one of two reasons that look identical from inside the sequence: the
/// queue's per-sequence time bound elapsed, or the run was stopped. The sequence's execution-log entry
/// is written deep inside <see cref="SequenceExecutionService"/>, which never sees the queue's tokens,
/// so the queue pushes this scope and the finalize reads it to record which of the two happened.
/// Ad-hoc (non-queue) runs have no scope and therefore no time bound.
/// </para>
/// </summary>
internal sealed class SequenceTimeLimitScope {
  private static readonly AsyncLocal<SequenceTimeLimitScope?> Ambient = new();

  private readonly CancellationToken _timerToken;
  private readonly CancellationToken _stopToken;

  private SequenceTimeLimitScope(int timeLimitMs, CancellationToken timerToken, CancellationToken stopToken) {
    TimeLimitMs = timeLimitMs;
    _timerToken = timerToken;
    _stopToken = stopToken;
  }

  /// <summary>The innermost scope on the current async flow, or null outside a queue firing.</summary>
  public static SequenceTimeLimitScope? Current => Ambient.Value;

  /// <summary>The effective time bound, in milliseconds, applied to the current firing.</summary>
  public int TimeLimitMs { get; }

  /// <summary>
  /// True when the time bound fired and no stop was requested. A stop always wins: a run the user
  /// halted is never reported as having run out of time, even if the bound fired as well.
  /// </summary>
  public bool HasElapsed => _timerToken.IsCancellationRequested && !_stopToken.IsCancellationRequested;

  /// <summary>
  /// Makes a scope current until the returned handle is disposed, which restores whatever was current before.
  /// </summary>
  /// <param name="timeLimitMs">The bound applied to the firing.</param>
  /// <param name="timerToken">A token cancelled only by the bound's timer.</param>
  /// <param name="stopToken">The run's stop token.</param>
  /// <returns>A handle that restores the previous scope on dispose.</returns>
  public static IDisposable Push(int timeLimitMs, CancellationToken timerToken, CancellationToken stopToken) {
    var previous = Ambient.Value;
    Ambient.Value = new SequenceTimeLimitScope(timeLimitMs, timerToken, stopToken);
    return new Restore(previous);
  }

  private sealed class Restore : IDisposable {
    private readonly SequenceTimeLimitScope? _previous;
    private bool _disposed;

    public Restore(SequenceTimeLimitScope? previous) => _previous = previous;

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      Ambient.Value = _previous;
    }
  }
}

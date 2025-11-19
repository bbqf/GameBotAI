using System.Threading;

namespace GameBot.Service.Hosted;

internal sealed class TriggerEvaluationMetrics : ITriggerEvaluationMetrics {
  private long _evaluations;
  private long _skippedNoSessions;
  private long _overlapSkipped;
  private long _lastDurationMs;

  public long Evaluations => Interlocked.Read(ref _evaluations);
  public long SkippedNoSessions => Interlocked.Read(ref _skippedNoSessions);
  public long OverlapSkipped => Interlocked.Read(ref _overlapSkipped);
  public long LastCycleDurationMs => Interlocked.Read(ref _lastDurationMs);

  public void IncrementEvaluations(long count) => Interlocked.Add(ref _evaluations, count);
  public void IncrementSkippedNoSessions() => Interlocked.Increment(ref _skippedNoSessions);
  public void IncrementOverlapSkipped() => Interlocked.Increment(ref _overlapSkipped);
  public void RecordCycleDuration(long ms) => Interlocked.Exchange(ref _lastDurationMs, ms);
}

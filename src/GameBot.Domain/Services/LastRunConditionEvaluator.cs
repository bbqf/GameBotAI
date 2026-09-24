using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;

namespace GameBot.Domain.Services;

/// <summary>
/// Evaluates a <c>lastRun</c> condition against the run statistics of the current queue (feature 105).
/// The condition is true when the named sequence has a kept run record with the given status whose end
/// time is in the window. The window ends now. It starts at the last occurrence of <c>since</c>, or at
/// now minus <c>within</c>.
/// <para>
/// This class does not apply <c>negate</c>: <see cref="SequenceStepConditionEvaluator"/> applies it,
/// the same as for the other leaves.
/// </para>
/// </summary>
public sealed class LastRunConditionEvaluator {
  private readonly ISequenceRunStatisticsStore _store;
  private readonly TimeProvider _timeProvider;

  /// <summary>Creates the evaluator.</summary>
  public LastRunConditionEvaluator(ISequenceRunStatisticsStore store, TimeProvider timeProvider) {
    ArgumentNullException.ThrowIfNull(store);
    ArgumentNullException.ThrowIfNull(timeProvider);
    _store = store;
    _timeProvider = timeProvider;
  }

  /// <summary>Evaluates <paramref name="condition"/> for a run of <paramref name="ownSequenceId"/> in <paramref name="queueId"/>.</summary>
  /// <exception cref="ConditionEvaluationException">
  /// A stored value does not parse. The save path prevents this, so it shows a defect or a file that
  /// was changed by hand.
  /// </exception>
  public async Task<bool> EvaluateAsync(string queueId, string ownSequenceId, LastRunStepCondition condition, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(condition);

    var problems = LastRunConditionRules.Validate(condition);
    if (problems.Count > 0) throw Unsupported(condition, problems[0]);
    if (!LastRunConditionRules.TryParseStatus(condition.Status, out var status)) {
      throw Unsupported(condition, LastRunConditionRules.StatusInvalidMessage);
    }

    var now = _timeProvider.GetLocalNow();
    DateTimeOffset from;
    if (LastRunConditionRules.TryParseSince(condition.Since, out var since)) {
      from = LastRunWindow.SinceStart(now, since, _timeProvider.LocalTimeZone);
    }
    else if (LastRunConditionRules.TryParseWithin(condition.Within, out var within)) {
      from = now - within;
    }
    else {
      throw Unsupported(condition, LastRunConditionRules.NoWindowMessage);
    }

    var target = string.Equals(condition.Sequence, LastRunStepCondition.SelfSequence, StringComparison.Ordinal)
      ? ownSequenceId
      : condition.Sequence;

    var stats = await _store.GetAsync(queueId, target, ct).ConfigureAwait(false);
    return stats is not null && stats.HasRunInWindow(status, from, now);
  }

  private static ConditionEvaluationException Unsupported(LastRunStepCondition condition, string message) =>
    new(ConditionEvaluationFailureKind.UnsupportedCondition, SequenceStepConditionEvaluator.Describe(condition), message);
}

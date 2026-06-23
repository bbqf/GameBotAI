using System;
using System.Globalization;
using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Default <see cref="ISelfRescheduleCoordinator"/> (feature 065). Looks up the active run via the
/// <see cref="IQueueRunRegistry"/> and injects one ephemeral <see cref="SelfRescheduleEntry"/> into
/// the register matching the chosen option. All wall-clock reads go through the injected
/// <see cref="TimeProvider"/> so timing is deterministic under test.
/// </summary>
internal sealed class SelfRescheduleCoordinator : ISelfRescheduleCoordinator {
  private readonly IQueueRunRegistry _registry;
  private readonly TimeProvider _timeProvider;

  public SelfRescheduleCoordinator(
    IQueueRunRegistry registry,
    TimeProvider? timeProvider = null) {
    _registry = registry;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public SelfRescheduleResult ScheduleSelf(
    string queueId,
    string sequenceId,
    SelfRescheduleOption option,
    TimeOnly? timerTimeOfDay,
    TimeSpan? timerRelativeOffset) {
    var entryId = Guid.NewGuid().ToString("n");

    if (!_registry.TryGet(queueId, out var handle)) {
      // Race: the run ended between dispatch and here. Treated as a logged no-op (data-model §5).
      return new SelfRescheduleResult(SelfRescheduleOutcome.NotRunning, entryId, option, null, "run not active");
    }

    switch (option) {
      case SelfRescheduleOption.OncePerRun: {
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, null);
        handle.PendingOncePerRun.Enqueue(entry);
        return new SelfRescheduleResult(SelfRescheduleOutcome.Scheduled, entryId, option, null, "this cycle");
      }

      case SelfRescheduleOption.EveryStep: {
        // Idempotent per sequence: re-registering the same sequence does not stack (loop-safe, FR-008).
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, null);
        handle.EveryStepInjections[sequenceId] = entry;
        return new SelfRescheduleResult(SelfRescheduleOutcome.Scheduled, entryId, option, null, "after every step");
      }

      case SelfRescheduleOption.AtQueueStart: {
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, null);
        if (handle.CycleExecution) {
          handle.PendingNextCycleStart.Enqueue(entry);
          return new SelfRescheduleResult(SelfRescheduleOutcome.Scheduled, entryId, option, null, "next cycle");
        }
        // Non-cycling fallback: fire at the next iteration boundary, i.e. the once-per-run drain.
        handle.PendingOncePerRun.Enqueue(entry);
        return new SelfRescheduleResult(SelfRescheduleOutcome.Scheduled, entryId, option, null, "next iteration boundary");
      }

      case SelfRescheduleOption.Timer: {
        var fireAt = ResolveTimerFireAt(timerTimeOfDay, timerRelativeOffset);
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, fireAt);
        handle.AddTimerFiring(entry);
        var timing = fireAt.ToString("u", CultureInfo.InvariantCulture);
        return new SelfRescheduleResult(SelfRescheduleOutcome.Scheduled, entryId, option, fireAt, timing);
      }

      default:
        throw new ArgumentOutOfRangeException(nameof(option), option, "Unknown self-reschedule option.");
    }
  }

  /// <summary>
  /// Resolves a Timer option to an absolute fire instant. A relative offset resolves to
  /// <c>now + offset</c>; a time-of-day resolves to today at that time, collapsing to <c>now</c>
  /// when already past so it fires at the next iteration boundary (FR-005/FR-006).
  /// </summary>
  private DateTimeOffset ResolveTimerFireAt(TimeOnly? timeOfDay, TimeSpan? relativeOffset) {
    var now = _timeProvider.GetLocalNow();
    if (relativeOffset is { } offset) {
      return now + offset;
    }
    if (timeOfDay is { } tod) {
      var candidate = new DateTimeOffset(
        now.Year, now.Month, now.Day, tod.Hour, tod.Minute, tod.Second, now.Offset);
      return candidate < now ? now : candidate;
    }
    // Defensive: a Timer with no field should have been rejected by validation; fire next boundary.
    return now;
  }
}

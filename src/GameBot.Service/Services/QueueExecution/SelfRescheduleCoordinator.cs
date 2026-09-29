using System;
using System.Globalization;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Parameters;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Default <see cref="ISelfRescheduleCoordinator"/> (feature 065). Looks up the active run via the
/// <see cref="IQueueRunRegistry"/> and injects one ephemeral <see cref="SelfRescheduleEntry"/> into
/// the register matching the chosen option. The entry keeps the parameter scope of the run that
/// makes the booking, so the booked run uses the same scope (feature 116). All wall-clock reads go through the injected
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
    TimeSpan? timerRelativeOffset,
    ParameterScope? scope = null) {
    var entryId = Guid.NewGuid().ToString("n");

    if (!_registry.TryGet(queueId, out var handle)) {
      // Race: the run ended between dispatch and here. Treated as a logged no-op (data-model §5).
      return new SelfRescheduleResult(SelfRescheduleOutcome.NotRunning, entryId, option, null, "run not active");
    }

    switch (option) {
      case SelfRescheduleOption.OncePerRun: {
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, null, scope);
        handle.PendingOncePerRun.Enqueue(entry);
        return new SelfRescheduleResult(SelfRescheduleOutcome.Scheduled, entryId, option, null, "this cycle");
      }

      case SelfRescheduleOption.EveryStep: {
        // Idempotent per sequence: re-registering the same sequence does not stack (loop-safe, FR-008).
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, null, scope);
        handle.EveryStepInjections[sequenceId] = entry;
        return new SelfRescheduleResult(SelfRescheduleOutcome.Scheduled, entryId, option, null, "after every step");
      }

      case SelfRescheduleOption.AtQueueStart: {
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, null, scope);
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
        var entry = new SelfRescheduleEntry(entryId, sequenceId, option, fireAt, scope);
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
  /// <c>now + offset</c>. A time of day resolves to its next occurrence on the local clock: today
  /// when it is later than the local time now, else the next day (feature 109, issue #227). The
  /// offset is the offset of the local time zone for that date and time.
  /// </summary>
  private DateTimeOffset ResolveTimerFireAt(TimeOnly? timeOfDay, TimeSpan? relativeOffset) {
    var now = _timeProvider.GetLocalNow();
    if (relativeOffset is { } offset) {
      return now + offset;
    }
    if (timeOfDay is { } tod) {
      return NextOccurrence(now, tod, _timeProvider.LocalTimeZone);
    }
    // Defensive: a Timer with no field should have been rejected by validation; fire next boundary.
    return now;
  }

  /// <summary>
  /// Gives the next occurrence of <paramref name="tod"/> after <paramref name="now"/> on the local
  /// clock of <paramref name="zone"/>. A time of day that is equal to or earlier than the local time
  /// now gives the next day. A booking for "now" makes the sequence run again immediately.
  /// </summary>
  private static DateTimeOffset NextOccurrence(DateTimeOffset now, TimeOnly tod, TimeZoneInfo zone) {
    var localNow = DateTime.SpecifyKind(now.DateTime, DateTimeKind.Unspecified);
    var local = DateOnly.FromDateTime(localNow).ToDateTime(tod, DateTimeKind.Unspecified);
    if (local <= localNow) {
      local = local.AddDays(1);
    }
    return new DateTimeOffset(local, zone.GetUtcOffset(local));
  }
}

using System;
using System.Collections.Generic;

namespace GameBot.Domain.Queues {
  /// <summary>The kind of a pending firing that smart scheduling can use as a target (feature 189).</summary>
  public enum SmartTargetKind {
    /// <summary>A pending Timer booking that a <c>reschedule-self</c> step made.</summary>
    SelfReschedule,

    /// <summary>A Timer entry of the queue template (time of day or relative offset).</summary>
    TemplateTimer,

    /// <summary>An armed daily retry of a time-of-day template entry.</summary>
    DailyRetry,

    /// <summary>A pending live schedule.</summary>
    LiveSchedule,
  }

  /// <summary>A pending firing that can be the target of a booking (feature 189).</summary>
  /// <param name="DueAt">The due time of the firing.</param>
  /// <param name="SequenceId">The sequence of the firing.</param>
  /// <param name="Kind">The kind of the firing.</param>
  public sealed record SmartCandidate(DateTimeOffset DueAt, string SequenceId, SmartTargetKind Kind);

  /// <summary>The result of <see cref="SmartSchedulingRule.Resolve"/> (feature 189).</summary>
  /// <param name="BookedAt">The time of the booking. Equal to the requested time when <paramref name="Target"/> is null.</param>
  /// <param name="Target">The target firing. Null when the booking is not postponed.</param>
  public sealed record SmartSchedulingDecision(DateTimeOffset BookedAt, SmartCandidate? Target);

  /// <summary>
  /// The pure rule of smart scheduling (feature 189). A booking for the requested time T moves to the
  /// earliest pending firing with a due time later than T and not later than T plus the window. The
  /// booked time is thus always from T to T plus the window.
  /// </summary>
  public static class SmartSchedulingRule {
    /// <summary>
    /// Calculates the booked time. A candidate with exactly the requested time is not a target. For
    /// equal due times, the first candidate in list order is the target. The comparison uses absolute
    /// instants, so midnight and a change of daylight saving time have no effect.
    /// </summary>
    /// <param name="requestedAt">The requested time of the booking.</param>
    /// <param name="window">The window.</param>
    /// <param name="candidates">The pending firings of the other sequences.</param>
    public static SmartSchedulingDecision Resolve(
        DateTimeOffset requestedAt, TimeSpan window, IReadOnlyList<SmartCandidate> candidates) {
      ArgumentNullException.ThrowIfNull(candidates);
      var end = requestedAt + window;
      SmartCandidate? target = null;
      foreach (var candidate in candidates) {
        if (candidate.DueAt <= requestedAt || candidate.DueAt > end) {
          continue;
        }
        if (target is null || candidate.DueAt < target.DueAt) {
          target = candidate;
        }
      }
      return target is null
        ? new SmartSchedulingDecision(requestedAt, null)
        : new SmartSchedulingDecision(target.DueAt, target);
    }
  }
}

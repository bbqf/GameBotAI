using System;

namespace GameBot.Domain.Triggers.Evaluators;

public sealed class DelayTriggerEvaluator : ITriggerEvaluator {
  public bool CanEvaluate(Trigger trigger) {
    ArgumentNullException.ThrowIfNull(trigger);
    return trigger.Enabled && trigger.Type == TriggerType.Delay && trigger.Params is DelayParams;
  }
  public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(trigger);
    var p = (DelayParams)trigger.Params;
    var threshold = TimeSpan.FromSeconds(Math.Max(0, p.Seconds));
    // Use EnabledAt (or creation implied by LastEvaluatedAt/LastFiredAt null) as start reference.
    // Start reference: when enabled; if missing, use first time we see it (store EnabledAt lazily via LastEvaluatedAt) and never treat 'now' as elapsed baseline.
    var start = trigger.EnabledAt ?? trigger.LastEvaluatedAt ?? now;
    if (trigger.EnabledAt is null && trigger.LastEvaluatedAt is null) {
      // First evaluation, set baseline without satisfying unless threshold == 0.
      // Record baseline enable time (not persisted here; consumer updates trigger afterward)
      trigger.EnabledAt = now;
      if (threshold.TotalSeconds > 0)
        return new TriggerEvaluationResult { Status = TriggerStatus.Pending, EvaluatedAt = now, Reason = "delay_pending_initial" };
    }
    var elapsed = now - start;
    var satisfied = elapsed >= threshold;
    return new TriggerEvaluationResult {
      Status = satisfied ? TriggerStatus.Satisfied : TriggerStatus.Pending,
      EvaluatedAt = now,
      Reason = satisfied ? "delay_elapsed" : "waiting_delay"
    };
  }
}

public sealed class ScheduleTriggerEvaluator : ITriggerEvaluator {
  public bool CanEvaluate(Trigger trigger) {
    ArgumentNullException.ThrowIfNull(trigger);
    return trigger.Enabled && trigger.Type == TriggerType.Schedule && trigger.Params is ScheduleParams;
  }
  public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(trigger);
    var p = (ScheduleParams)trigger.Params;
    // If the schedule timestamp is already in the past at the moment of enabling, treat as Disabled (missed window)
    if (trigger.EnabledAt.HasValue && trigger.EnabledAt.Value > p.Timestamp) {
      return new TriggerEvaluationResult {
        Status = TriggerStatus.Disabled,
        EvaluatedAt = now,
        Reason = "scheduled_time_in_past_when_enabled"
      };
    }
    var satisfied = now >= p.Timestamp;
    return new TriggerEvaluationResult {
      Status = satisfied ? TriggerStatus.Satisfied : TriggerStatus.Pending,
      EvaluatedAt = now,
      Reason = satisfied ? "time_reached" : "waiting_for_time"
    };
  }
}

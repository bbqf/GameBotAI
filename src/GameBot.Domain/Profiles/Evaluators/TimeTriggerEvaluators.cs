using GameBot.Domain.Profiles;

namespace GameBot.Domain.Profiles.Evaluators;

/// <summary>
/// Evaluates schedule triggers: fires when now >= timestamp; never fires if timestamp < EnabledAt.
/// </summary>
public sealed class ScheduleTriggerEvaluator : ITriggerEvaluator
{
    public bool CanEvaluate(ProfileTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Enabled && trigger.Type == TriggerType.Schedule && trigger.Params is ScheduleParams;
    }

    public TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var p = (ScheduleParams)trigger.Params;
        // If enabled after the scheduled time, treat as disabled (spec: MUST not fire)
        if (trigger.EnabledAt.HasValue && p.Timestamp < trigger.EnabledAt.Value)
        {
            return Disabled(now, "scheduled_time_in_past_when_enabled");
        }
        if (now >= p.Timestamp)
        {
            return new TriggerEvaluationResult
            {
                Status = TriggerStatus.Satisfied,
                EvaluatedAt = now,
                Reason = "time_reached"
            };
        }
        return new TriggerEvaluationResult
        {
            Status = TriggerStatus.Pending,
            EvaluatedAt = now,
            Reason = "waiting_for_time"
        };
    }

    private static TriggerEvaluationResult Disabled(DateTimeOffset now, string reason) => new()
    {
        Status = TriggerStatus.Disabled,
        EvaluatedAt = now,
        Reason = reason
    };
}

/// <summary>
/// Evaluates delay triggers: fires when (now - EnabledAt) >= seconds. If EnabledAt missing, treat as pending.
/// </summary>
public sealed class DelayTriggerEvaluator : ITriggerEvaluator
{
    public bool CanEvaluate(ProfileTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Enabled && trigger.Type == TriggerType.Delay && trigger.Params is DelayParams;
    }

    public TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var p = (DelayParams)trigger.Params;
        if (!trigger.EnabledAt.HasValue)
        {
            return new TriggerEvaluationResult { Status = TriggerStatus.Pending, EvaluatedAt = now, Reason = "not_enabled_yet" };
        }
        var elapsed = now - trigger.EnabledAt.Value;
        if (elapsed.TotalSeconds >= p.Seconds)
        {
            return new TriggerEvaluationResult { Status = TriggerStatus.Satisfied, EvaluatedAt = now, Reason = "delay_elapsed" };
        }
        return new TriggerEvaluationResult { Status = TriggerStatus.Pending, EvaluatedAt = now, Reason = "waiting_delay" };
    }
}
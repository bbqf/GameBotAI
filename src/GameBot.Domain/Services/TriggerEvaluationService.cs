using GameBot.Domain.Triggers;

namespace GameBot.Domain.Services;

public sealed class TriggerEvaluationService
{
    private readonly IReadOnlyList<ITriggerEvaluator> _evaluators;

    public TriggerEvaluationService(IEnumerable<ITriggerEvaluator> evaluators)
    {
        _evaluators = evaluators.ToList();
    }

    public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        // Disabled triggers short-circuit
        if (!trigger.Enabled)
        {
            return new TriggerEvaluationResult
            {
                Status = TriggerStatus.Disabled,
                Reason = "trigger_disabled",
                EvaluatedAt = now
            };
        }

        // Cooldown enforcement: if fired recently, don't evaluate underlying condition
        if (trigger.LastFiredAt.HasValue && trigger.CooldownSeconds > 0)
        {
            var elapsed = now - trigger.LastFiredAt.Value;
            if (elapsed.TotalSeconds < trigger.CooldownSeconds)
            {
                return new TriggerEvaluationResult
                {
                    Status = TriggerStatus.Cooldown,
                    Reason = "cooldown_active",
                    EvaluatedAt = now
                };
            }
        }

        var evaluator = _evaluators.FirstOrDefault(e => e.CanEvaluate(trigger));
        if (evaluator is null)
        {
            return new TriggerEvaluationResult
            {
                Status = TriggerStatus.Disabled,
                Reason = "No evaluator for trigger type",
                EvaluatedAt = now
            };
        }

        var result = evaluator.Evaluate(trigger, now);
        // Persist baseline enable timestamp if newly established
        if (trigger.EnabledAt is null && result.Status == TriggerStatus.Pending && result.Reason == "delay_pending_initial")
        {
            trigger.EnabledAt = result.EvaluatedAt;
        }
        return result;
    }
}

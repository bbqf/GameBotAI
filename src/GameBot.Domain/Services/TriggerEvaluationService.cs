using GameBot.Domain.Profiles;

namespace GameBot.Domain.Services;

public sealed class TriggerEvaluationService
{
    private readonly IReadOnlyList<ITriggerEvaluator> _evaluators;

    public TriggerEvaluationService(IEnumerable<ITriggerEvaluator> evaluators)
    {
        _evaluators = evaluators.ToList();
    }

    public TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now)
    {
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

        return evaluator.Evaluate(trigger, now);
    }
}

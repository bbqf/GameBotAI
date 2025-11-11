namespace GameBot.Domain.Profiles;

public interface ITriggerEvaluator
{
    bool CanEvaluate(ProfileTrigger trigger);
    TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now);
}

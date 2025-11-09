namespace GameBot.Domain.Profiles;

internal interface ITriggerEvaluator
{
    bool CanEvaluate(ProfileTrigger trigger);
    TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now);
}

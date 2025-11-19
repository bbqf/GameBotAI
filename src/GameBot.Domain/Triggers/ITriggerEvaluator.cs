namespace GameBot.Domain.Triggers;

public interface ITriggerEvaluator {
  bool CanEvaluate(Trigger trigger);
  TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now);
}

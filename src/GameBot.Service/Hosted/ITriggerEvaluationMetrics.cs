namespace GameBot.Service.Hosted;

public interface ITriggerEvaluationMetrics
{
    long Evaluations { get; }
    long SkippedNoSessions { get; }
    long OverlapSkipped { get; }
    long LastCycleDurationMs { get; }

    void IncrementEvaluations(long count);
    void IncrementSkippedNoSessions();
    void IncrementOverlapSkipped();
    void RecordCycleDuration(long ms);
}

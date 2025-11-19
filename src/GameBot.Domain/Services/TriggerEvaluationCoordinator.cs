using GameBot.Domain.Profiles;

namespace GameBot.Domain.Services;

/// <summary>
/// Coordinates evaluation of all triggers across all profiles. Intended for background execution.
/// </summary>
public sealed class TriggerEvaluationCoordinator : ITriggerEvaluationCoordinator
{
    private readonly ITriggerRepository _triggers;
    private readonly TriggerEvaluationService _evaluation;

    public TriggerEvaluationCoordinator(ITriggerRepository triggers, TriggerEvaluationService evaluation)
    {
        _triggers = triggers;
        _evaluation = evaluation;
    }

    /// <summary>
    /// Evaluates all enabled triggers (legacy gameId filter ignored after decoupling) and persists state.
    /// </summary>
    public async Task<int> EvaluateAllAsync(string? gameId = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var all = await _triggers.ListAsync(ct).ConfigureAwait(false);
        var evaluated = 0;
        foreach (var trig in all)
        {
            var result = _evaluation.Evaluate(trig, now);
            trig.LastEvaluatedAt = result.EvaluatedAt;
            trig.LastResult = result;
            if (result.Status == TriggerStatus.Satisfied)
            {
                trig.LastFiredAt = result.EvaluatedAt;
            }
            await _triggers.UpsertAsync(trig, ct).ConfigureAwait(false);
            evaluated++;
        }
        return evaluated;
    }
}
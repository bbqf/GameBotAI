using GameBot.Domain.Profiles;

namespace GameBot.Domain.Services;

/// <summary>
/// Coordinates evaluation of all triggers across all profiles. Intended for background execution.
/// </summary>
public sealed class TriggerEvaluationCoordinator : ITriggerEvaluationCoordinator
{
    private readonly IProfileRepository _profiles;
    private readonly TriggerEvaluationService _evaluation;

    public TriggerEvaluationCoordinator(IProfileRepository profiles, TriggerEvaluationService evaluation)
    {
        _profiles = profiles;
        _evaluation = evaluation;
    }

    /// <summary>
    /// Evaluates all enabled triggers for all profiles (optionally filtered by game) and persists changes.
    /// </summary>
    /// <remarks>
    /// This performs in-memory mutation then issues an UpdateAsync per changed profile.
    /// Cooldown and disabled triggers are short-circuited by TriggerEvaluationService.
    /// </remarks>
    public async Task<int> EvaluateAllAsync(string? gameId = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var profiles = await _profiles.ListAsync(gameId, ct).ConfigureAwait(false);
        var evaluatedCount = 0;
        foreach (var profile in profiles)
        {
            var changed = false;
            foreach (var trig in profile.Triggers)
            {
                var result = _evaluation.Evaluate(trig, now);
                trig.LastEvaluatedAt = result.EvaluatedAt;
                trig.LastResult = result;
                if (result.Status == TriggerStatus.Satisfied)
                {
                    trig.LastFiredAt = result.EvaluatedAt;
                }
                changed = true; // We always persist evaluation timestamps
                evaluatedCount++;
            }
            if (changed)
            {
                await _profiles.UpdateAsync(profile, ct).ConfigureAwait(false);
            }
        }
        return evaluatedCount;
    }
}
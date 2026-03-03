using System;
using System.Collections.Generic;

namespace GameBot.Domain.Services;

public sealed class CycleIterationLimiter
{
    private readonly Dictionary<string, int> _iterationCounts = new(StringComparer.Ordinal);

    public void Reset()
    {
        _iterationCounts.Clear();
    }

    public bool TryIncrement(
        string stepId,
        int? iterationLimit,
        out int currentIteration,
        out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        failureReason = null;
        if (!iterationLimit.HasValue)
        {
            currentIteration = 0;
            return true;
        }

        if (iterationLimit.Value <= 0)
        {
            currentIteration = 0;
            failureReason = $"Step '{stepId}' has an invalid iteration limit '{iterationLimit.Value}'.";
            return false;
        }

        var nextIteration = (_iterationCounts.TryGetValue(stepId, out var current) ? current : 0) + 1;
        _iterationCounts[stepId] = nextIteration;
        currentIteration = nextIteration;

        if (nextIteration > iterationLimit.Value)
        {
            failureReason = $"Step '{stepId}' exceeded iteration limit '{iterationLimit.Value}'.";
            return false;
        }

        return true;
    }
}
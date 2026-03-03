using System;
using System.Collections.Generic;

namespace GameBot.Domain.Commands;

public enum BranchType
{
    Next,
    True,
    False
}

public sealed class BranchLink
{
    public string LinkId { get; set; } = string.Empty;
    public string SourceStepId { get; set; } = string.Empty;
    public string TargetStepId { get; set; } = string.Empty;
    public BranchType BranchType { get; set; }

    public IReadOnlyList<string> Validate(ISet<string> stepIds)
    {
        ArgumentNullException.ThrowIfNull(stepIds);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(LinkId))
        {
            errors.Add("LinkId is required.");
        }

        if (string.IsNullOrWhiteSpace(SourceStepId))
        {
            errors.Add($"Link '{LinkId}': SourceStepId is required.");
        }
        else if (!stepIds.Contains(SourceStepId))
        {
            errors.Add($"Link '{LinkId}': source step '{SourceStepId}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(TargetStepId))
        {
            errors.Add($"Link '{LinkId}': TargetStepId is required.");
        }
        else if (!stepIds.Contains(TargetStepId))
        {
            errors.Add($"Link '{LinkId}': target step '{TargetStepId}' does not exist.");
        }

        return errors;
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Commands;

public enum FlowStepType
{
    Action,
    Command,
    Condition,
    Terminal
}

public sealed class FlowStep
{
    public string StepId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public FlowStepType StepType { get; set; }
    public string? PayloadRef { get; set; }
    public int? IterationLimit { get; set; }
    public ConditionExpression? Condition { get; set; }
}

public sealed class SequenceFlowGraph
{
    private readonly List<FlowStep> _steps = new();
    private readonly List<BranchLink> _links = new();

    public string SequenceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string EntryStepId { get; set; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<FlowStep> Steps => _steps.AsReadOnly();

    [JsonIgnore]
    public IReadOnlyList<BranchLink> Links => _links.AsReadOnly();

    public void SetSteps(IEnumerable<FlowStep>? steps)
    {
        _steps.Clear();
        if (steps is null)
        {
            return;
        }

        _steps.AddRange(steps);
    }

    public void SetLinks(IEnumerable<BranchLink>? links)
    {
        _links.Clear();
        if (links is null)
        {
            return;
        }

        _links.AddRange(links);
    }

    public bool TryGetStep(string stepId, out FlowStep? step)
    {
        step = _steps.FirstOrDefault(s => string.Equals(s.StepId, stepId, StringComparison.Ordinal));
        return step is not null;
    }

    [JsonInclude]
    [JsonPropertyName("steps")]
    public Collection<FlowStep> StepsWritable
    {
        get => new(_steps);
        private set
        {
            _steps.Clear();
            if (value is null)
            {
                return;
            }

            foreach (var step in value)
            {
                _steps.Add(step);
            }
        }
    }

    [JsonInclude]
    [JsonPropertyName("links")]
    public Collection<BranchLink> LinksWritable
    {
        get => new(_links);
        private set
        {
            _links.Clear();
            if (value is null)
            {
                return;
            }

            foreach (var link in value)
            {
                _links.Add(link);
            }
        }
    }
}
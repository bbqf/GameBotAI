using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

public interface ISequenceFlowValidator
{
    SequenceFlowValidationResult Validate(SequenceFlowGraph graph);
}

public sealed class SequenceFlowValidationResult
{
    private readonly List<string> _errors = new();

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();

    public void AddError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        _errors.Add(error);
    }

    public void AddErrors(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var error in errors)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _errors.Add(error);
            }
        }
    }
}

public sealed class SequenceFlowValidator : ISequenceFlowValidator
{
    public SequenceFlowValidationResult Validate(SequenceFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var result = new SequenceFlowValidationResult();
        if (string.IsNullOrWhiteSpace(graph.EntryStepId))
        {
            result.AddError("EntryStepId is required.");
        }

        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        var conditionStepIds = new HashSet<string>(StringComparer.Ordinal);
        var nonTerminalStepIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in graph.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId))
            {
                result.AddError("Each step requires a non-empty StepId.");
                continue;
            }

            if (!stepIds.Add(step.StepId))
            {
                result.AddError($"Duplicate step id '{step.StepId}'.");
            }

            if (step.StepType == FlowStepType.Condition)
            {
                conditionStepIds.Add(step.StepId);
                if (step.Condition is null)
                {
                    result.AddError($"Condition step '{step.StepId}' must define a condition expression.");
                }
                else
                {
                    result.AddErrors(step.Condition.Validate().Select(error => $"{step.StepId}: {error}"));
                }
            }
            else if (step.Condition is not null)
            {
                result.AddError($"Non-condition step '{step.StepId}' must not define a condition expression.");
            }

            if (step.StepType != FlowStepType.Terminal)
            {
                nonTerminalStepIds.Add(step.StepId);
            }
        }

        if (!string.IsNullOrWhiteSpace(graph.EntryStepId) && !stepIds.Contains(graph.EntryStepId))
        {
            result.AddError($"Entry step '{graph.EntryStepId}' does not exist.");
        }

        var linkIds = new HashSet<string>(StringComparer.Ordinal);
        var outgoingBySource = new Dictionary<string, List<BranchLink>>(StringComparer.Ordinal);
        foreach (var link in graph.Links)
        {
            if (string.IsNullOrWhiteSpace(link.LinkId))
            {
                result.AddError("Each link requires a non-empty LinkId.");
            }
            else if (!linkIds.Add(link.LinkId))
            {
                result.AddError($"Duplicate link id '{link.LinkId}'.");
            }

            result.AddErrors(link.Validate(stepIds));

            if (!outgoingBySource.TryGetValue(link.SourceStepId, out var outgoing))
            {
                outgoing = new List<BranchLink>();
                outgoingBySource[link.SourceStepId] = outgoing;
            }

            outgoing.Add(link);
        }

        foreach (var conditionStepId in conditionStepIds)
        {
            outgoingBySource.TryGetValue(conditionStepId, out var outgoing);
            outgoing ??= new List<BranchLink>();
            var trueCount = outgoing.Count(link => link.BranchType == BranchType.True);
            var falseCount = outgoing.Count(link => link.BranchType == BranchType.False);
            var invalidBranch = outgoing.Any(link => link.BranchType == BranchType.Next);

            if (trueCount != 1 || falseCount != 1 || invalidBranch)
            {
                result.AddError($"Condition step '{conditionStepId}' must have exactly one True and one False branch.");
            }
        }

        foreach (var stepId in nonTerminalStepIds)
        {
            if (conditionStepIds.Contains(stepId))
            {
                continue;
            }

            outgoingBySource.TryGetValue(stepId, out var outgoing);
            outgoing ??= new List<BranchLink>();

            var invalidBranch = outgoing.Any(link => link.BranchType != BranchType.Next);
            if (invalidBranch)
            {
                result.AddError($"Step '{stepId}' supports only Next branch links.");
            }

            if (outgoing.Count > 1)
            {
                result.AddError($"Step '{stepId}' can define at most one Next branch link.");
            }
        }

        ValidateCycleLimits(graph, outgoingBySource, result);
        return result;
    }

    private static void ValidateCycleLimits(
        SequenceFlowGraph graph,
        IReadOnlyDictionary<string, List<BranchLink>> outgoingBySource,
        SequenceFlowValidationResult result)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var step in graph.Steps)
        {
            adjacency[step.StepId] = new List<string>();
        }

        foreach (var link in graph.Links)
        {
            if (!adjacency.TryGetValue(link.SourceStepId, out var children))
            {
                continue;
            }

            children.Add(link.TargetStepId);
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var cycleMembers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in graph.Steps)
        {
            if (!state.ContainsKey(step.StepId))
            {
                DepthFirstSearch(step.StepId, adjacency, state, stack, cycleMembers);
            }
        }

        if (cycleMembers.Count == 0)
        {
            return;
        }

        foreach (var step in graph.Steps)
        {
            if (!cycleMembers.Contains(step.StepId))
            {
                continue;
            }

            if (!step.IterationLimit.HasValue || step.IterationLimit.Value <= 0)
            {
                result.AddError($"Cycle step '{step.StepId}' must define an iteration limit greater than zero.");
            }
        }
    }

    private static void DepthFirstSearch(
        string current,
        IReadOnlyDictionary<string, List<string>> adjacency,
        IDictionary<string, int> state,
        Stack<string> stack,
        ISet<string> cycleMembers)
    {
        state[current] = 1;
        stack.Push(current);

        if (adjacency.TryGetValue(current, out var nextSteps))
        {
            foreach (var next in nextSteps)
            {
                if (!state.TryGetValue(next, out var nextState))
                {
                    DepthFirstSearch(next, adjacency, state, stack, cycleMembers);
                    continue;
                }

                if (nextState == 1)
                {
                    foreach (var node in stack)
                    {
                        cycleMembers.Add(node);
                        if (string.Equals(node, next, StringComparison.Ordinal))
                        {
                            break;
                        }
                    }
                }
            }
        }

        stack.Pop();
        state[current] = 2;
    }
}
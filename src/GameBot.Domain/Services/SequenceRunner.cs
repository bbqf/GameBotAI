using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Config;
using GameBot.Domain.Logging;
using GameBot.Domain.Utils;

namespace GameBot.Domain.Services
{
    /// <summary>
    /// Minimal runner that executes a sequence by iterating steps and applying delays.
    /// Detection gating and retries will be added in later phases.
    /// </summary>
    public class SequenceRunner
    {
        private readonly ISequenceRepository _repository;
        private readonly ILogger<SequenceRunner>? _logger;
        private readonly AppConfig _config;

        public SequenceRunner(ISequenceRepository repository)
        {
            _repository = repository;
            _config = new AppConfig();
        }

        // Optional logger-friendly constructor when DI provides ILogger
        public SequenceRunner(ISequenceRepository repository, ILogger<SequenceRunner> logger)
        {
            _repository = repository;
            _logger = logger;
            _config = new AppConfig();
        }

        public SequenceRunner(ISequenceRepository repository, ILogger<SequenceRunner> logger, AppConfig config)
        {
            _repository = repository;
            _logger = logger;
            _config = config;
        }

        public async Task<SequenceExecutionResult> ExecuteAsync(
            string sequenceId,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator = null,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(executeCommandAsync);
            var sequence = await _repository.GetAsync(sequenceId).ConfigureAwait(false);
            if (sequence == null)
            {
                return SequenceExecutionResult.NotFound(sequenceId);
            }

            var result = SequenceExecutionResult.Start(sequenceId);
            if (_logger != null) LogSequenceStart(_logger, sequenceId, null);

            if (!string.IsNullOrWhiteSpace(sequence.EntryStepId) && sequence.FlowSteps.Count > 0)
            {
                await ExecuteFlowGraphAsync(sequence, executeCommandAsync, conditionEvaluator, result, ct).ConfigureAwait(false);
                if (_logger != null) LogSequenceEnd(_logger, sequenceId, result.Status, null);
                return result;
            }

            var linearStepOutcomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var step in sequence.Steps.OrderBy(s => s.Order))
            {
                ct.ThrowIfCancellationRequested();
                var earlyStop = await ExecuteSingleStepAsync(
                    step,
                    executeCommandAsync,
                    gateEvaluator,
                    conditionEvaluator,
                    linearStepOutcomes,
                    result,
                    sequenceId,
                    ct).ConfigureAwait(false);
                if (earlyStop)
                {
                    if (_logger != null) LogSequenceEnd(_logger, sequenceId, result.Status, null);
                    return result;
                }
            }

            // Execute blocks if any (US1: repeatCount)
            if (sequence is { Blocks: { Count: > 0 } })
            {
                await ExecuteBlocksAsync(sequence.Blocks, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
            }
            result.Complete();
            if (_logger != null) LogSequenceEnd(_logger, sequenceId, result.Status, null);
            return result;
        }

        private async Task ExecuteFlowGraphAsync(
            CommandSequence sequence,
            Func<string, Task> executeCommandAsync,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            SequenceExecutionResult result,
            CancellationToken ct)
        {
            var stepsById = sequence.FlowSteps
                .Where(step => !string.IsNullOrWhiteSpace(step.StepId))
                .ToDictionary(step => step.StepId, StringComparer.Ordinal);

            if (!stepsById.TryGetValue(sequence.EntryStepId, out var entryStep))
            {
                result.Fail("sequence flow entry step not found");
                return;
            }

            var currentStep = entryStep;

            var linksBySource = sequence.FlowLinks
                .GroupBy(link => link.SourceStepId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            var limiter = new CycleIterationLimiter();
            limiter.Reset();
            var commandOutcomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (!limiter.TryIncrement(currentStep.StepId, currentStep.IterationLimit, out _, out var failureReason))
                {
                    result.Fail(failureReason);
                    return;
                }

                if (currentStep.StepType == FlowStepType.Terminal)
                {
                    result.Complete();
                    return;
                }

                if (currentStep.StepType == FlowStepType.Condition)
                {
                    if (currentStep.Condition is null)
                    {
                        result.Fail($"condition step '{currentStep.StepId}' has no condition expression");
                        return;
                    }

                    bool conditionResult;
                    ConditionEvaluationTrace conditionTrace;
                    try
                    {
                        var evaluation = await EvaluateConditionWithTraceAsync(
                            currentStep.Condition,
                            (operand, token) => EvaluateFlowOperandAsync(operand, conditionEvaluator, commandOutcomes, token),
                            ct).ConfigureAwait(false);
                        conditionResult = evaluation.Result;
                        conditionTrace = evaluation.Trace;
                        result.AddConditionTrace(currentStep.StepId, currentStep.Label, conditionTrace);
                    }
                    catch
                    {
                        result.Fail($"condition step '{currentStep.StepId}' could not be evaluated");
                        return;
                    }

                    if (_logger != null)
                    {
                        LogConditionTrace(
                            _logger,
                            currentStep.StepId,
                            conditionTrace.SelectedBranch,
                            JsonSerializer.Serialize(conditionTrace),
                            null);
                    }

                    if (!linksBySource.TryGetValue(currentStep.StepId, out var branchLinks))
                    {
                        result.Fail($"condition step '{currentStep.StepId}' has no branch links");
                        return;
                    }

                    var requiredBranch = conditionResult ? BranchType.True : BranchType.False;
                    var nextLink = branchLinks.FirstOrDefault(link => link.BranchType == requiredBranch);
                    if (nextLink is null)
                    {
                        result.Fail($"condition step '{currentStep.StepId}' target branch is unresolved");
                        return;
                    }

                    if (!stepsById.TryGetValue(nextLink.TargetStepId, out var nextConditionStep))
                    {
                        result.Fail($"condition step '{currentStep.StepId}' target branch is unresolved");
                        return;
                    }

                    currentStep = nextConditionStep;

                    continue;
                }

                var commandId = string.IsNullOrWhiteSpace(currentStep.PayloadRef)
                    ? currentStep.StepId
                    : currentStep.PayloadRef!;

                try
                {
                    await executeCommandAsync(commandId).ConfigureAwait(false);
                }
                catch
                {
                    result.Fail($"step '{currentStep.StepId}' command execution failed");
                    return;
                }

                result.AddStep(commandId, 0, actionOutcome: "executed");
                commandOutcomes[currentStep.StepId] = "success";
                if (!string.IsNullOrWhiteSpace(currentStep.PayloadRef))
                {
                    commandOutcomes[currentStep.PayloadRef] = "success";
                }

                if (!linksBySource.TryGetValue(currentStep.StepId, out var outgoing))
                {
                    result.Complete();
                    return;
                }

                var next = outgoing.FirstOrDefault(link => link.BranchType == BranchType.Next);
                if (next is null)
                {
                    result.Complete();
                    return;
                }

                if (!stepsById.TryGetValue(next.TargetStepId, out var nextStep))
                {
                    result.Fail($"step '{currentStep.StepId}' next target is unresolved");
                    return;
                }

                currentStep = nextStep;
            }
        }

        private static async ValueTask<bool> EvaluateFlowOperandAsync(
            ConditionOperand operand,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> commandOutcomes,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (operand.OperandType == ConditionOperandType.CommandOutcome)
            {
                if (!commandOutcomes.TryGetValue(operand.TargetRef, out var actualState))
                {
                    throw new InvalidOperationException($"Command outcome '{operand.TargetRef}' is unavailable.");
                }

                return string.Equals(actualState, operand.ExpectedState, StringComparison.OrdinalIgnoreCase);
            }

            if (operand.OperandType == ConditionOperandType.ImageDetection)
            {
                if (conditionEvaluator is null)
                {
                    throw new InvalidOperationException("Image detection evaluator is unavailable.");
                }

                var mode = string.Equals(operand.ExpectedState, "absent", StringComparison.OrdinalIgnoreCase)
                    ? "Absent"
                    : "Present";

                var condition = new Condition
                {
                    Source = "image",
                    TargetId = operand.TargetRef,
                    Mode = mode,
                    ConfidenceThreshold = operand.Threshold
                };

                return await conditionEvaluator(condition, ct).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Unsupported operand type '{operand.OperandType}'.");
        }

        private static async ValueTask<(bool Result, ConditionEvaluationTrace Trace)> EvaluateConditionWithTraceAsync(
            ConditionExpression expression,
            Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
            CancellationToken ct)
        {
            var operandResults = new List<Dictionary<string, object?>>();
            var operatorSteps = new List<Dictionary<string, object?>>();
            var result = await EvaluateConditionNodeWithTraceAsync(expression, operandEvaluator, operandResults, operatorSteps, ct).ConfigureAwait(false);
            var trace = new ConditionEvaluationTrace(
                result,
                result ? "true" : "false",
                null,
                operandResults,
                operatorSteps);
            return (result, trace);
        }

        private static async ValueTask<bool> EvaluateConditionNodeWithTraceAsync(
            ConditionExpression expression,
            Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
            List<Dictionary<string, object?>> operandResults,
            List<Dictionary<string, object?>> operatorSteps,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            switch (expression.NodeType)
            {
                case ConditionNodeType.Operand:
                    if (expression.Operand is null)
                    {
                        throw new InvalidOperationException("Operand node requires operand metadata.");
                    }

                    var operandResult = await operandEvaluator(expression.Operand, ct).ConfigureAwait(false);
                    operandResults.Add(new Dictionary<string, object?> {
                        ["operandType"] = expression.Operand.OperandType.ToString(),
                        ["targetRef"] = expression.Operand.TargetRef,
                        ["expectedState"] = expression.Operand.ExpectedState,
                        ["threshold"] = expression.Operand.Threshold,
                        ["result"] = operandResult
                    });
                    return operandResult;

                case ConditionNodeType.And:
                    if (expression.Children.Count < 2)
                    {
                        throw new InvalidOperationException("AND node requires at least two children.");
                    }

                    var andResult = true;
                    for (var index = 0; index < expression.Children.Count; index++)
                    {
                        var childResult = await EvaluateConditionNodeWithTraceAsync(expression.Children[index], operandEvaluator, operandResults, operatorSteps, ct).ConfigureAwait(false);
                        andResult = andResult && childResult;
                        operatorSteps.Add(new Dictionary<string, object?> {
                            ["operator"] = "and",
                            ["childIndex"] = index,
                            ["childResult"] = childResult,
                            ["aggregateResult"] = andResult
                        });
                        if (!andResult)
                        {
                            break;
                        }
                    }

                    return andResult;

                case ConditionNodeType.Or:
                    if (expression.Children.Count < 2)
                    {
                        throw new InvalidOperationException("OR node requires at least two children.");
                    }

                    var orResult = false;
                    for (var index = 0; index < expression.Children.Count; index++)
                    {
                        var childResult = await EvaluateConditionNodeWithTraceAsync(expression.Children[index], operandEvaluator, operandResults, operatorSteps, ct).ConfigureAwait(false);
                        orResult = orResult || childResult;
                        operatorSteps.Add(new Dictionary<string, object?> {
                            ["operator"] = "or",
                            ["childIndex"] = index,
                            ["childResult"] = childResult,
                            ["aggregateResult"] = orResult
                        });
                        if (orResult)
                        {
                            break;
                        }
                    }

                    return orResult;

                case ConditionNodeType.Not:
                    if (expression.Children.Count != 1)
                    {
                        throw new InvalidOperationException("NOT node requires exactly one child.");
                    }

                    var notChildResult = await EvaluateConditionNodeWithTraceAsync(expression.Children[0], operandEvaluator, operandResults, operatorSteps, ct).ConfigureAwait(false);
                    var notResult = !notChildResult;
                    operatorSteps.Add(new Dictionary<string, object?> {
                        ["operator"] = "not",
                        ["childResult"] = notChildResult,
                        ["aggregateResult"] = notResult
                    });
                    return notResult;

                default:
                    throw new InvalidOperationException($"Unsupported condition node type '{expression.NodeType}'.");
            }
        }

        private async Task<bool> ExecuteSingleStepAsync(
            SequenceStep step,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> stepOutcomes,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var stepKey = !string.IsNullOrWhiteSpace(step.StepId) ? step.StepId : step.CommandId;

            // ── Loop step dispatch ────────────────────────────────────────────
            if (step.StepType == SequenceStepType.Loop)
            {
                return await ExecuteLoopStepAsync(
                    step,
                    executeCommandAsync,
                    gateEvaluator,
                    conditionEvaluator,
                    stepOutcomes,
                    result,
                    sequenceId,
                    _config.LoopMaxIterations,
                    ct).ConfigureAwait(false);
            }

            if (step.Condition is ImageVisibleStepCondition imageCondition)
            {
                if (conditionEvaluator is null)
                {
                    result.AddStep(
                        step.CommandId,
                        0,
                        "Failed",
                        conditionType: "imageVisible",
                        conditionResult: "error",
                        actionOutcome: "failed",
                        message: "Per-step imageVisible condition evaluator is unavailable");
                    result.Fail("Per-step imageVisible condition evaluator is unavailable");
                    if (!string.IsNullOrWhiteSpace(stepKey)) stepOutcomes[stepKey] = "failed";
                    return true;
                }

                bool conditionResult;
                try
                {
                    conditionResult = await conditionEvaluator(new Condition
                    {
                        Source = "image",
                        TargetId = imageCondition.ImageId,
                        Mode = "Present",
                        ConfidenceThreshold = imageCondition.MinSimilarity
                    }, ct).ConfigureAwait(false);
                }
                catch
                {
                    result.AddStep(
                        step.CommandId,
                        0,
                        "Failed",
                        conditionType: "imageVisible",
                        conditionResult: "error",
                        actionOutcome: "failed",
                        message: $"Step '{stepKey}' condition evaluation failed");
                    result.Fail($"Step '{stepKey}' condition evaluation failed");
                    if (!string.IsNullOrWhiteSpace(stepKey)) stepOutcomes[stepKey] = "failed";
                    return true;
                }

                conditionResult = imageCondition.Negate ? !conditionResult : conditionResult;

                if (!conditionResult)
                {
                    result.AddStep(
                        step.CommandId,
                        0,
                        "Skipped",
                        conditionType: "imageVisible",
                        conditionResult: "false",
                        actionOutcome: "skipped");
                    if (!string.IsNullOrWhiteSpace(stepKey)) stepOutcomes[stepKey] = "skipped";
                    return false;
                }
            }

            if (step.Condition is CommandOutcomeStepCondition commandOutcomeCondition)
            {
                if (string.IsNullOrWhiteSpace(commandOutcomeCondition.StepRef)
                    || !stepOutcomes.TryGetValue(commandOutcomeCondition.StepRef, out var actualOutcome))
                {
                    result.AddStep(
                        step.CommandId,
                        0,
                        "Failed",
                        conditionType: "commandOutcome",
                        conditionResult: "error",
                        actionOutcome: "failed",
                        message: $"Step '{stepKey}' commandOutcome reference '{commandOutcomeCondition.StepRef}' is unavailable");
                    result.Fail($"Step '{stepKey}' commandOutcome reference '{commandOutcomeCondition.StepRef}' is unavailable");
                    if (!string.IsNullOrWhiteSpace(stepKey)) stepOutcomes[stepKey] = "failed";
                    return true;
                }

                var outcomeMatches = string.Equals(actualOutcome, commandOutcomeCondition.ExpectedState, StringComparison.OrdinalIgnoreCase);
                outcomeMatches = commandOutcomeCondition.Negate ? !outcomeMatches : outcomeMatches;

                if (!outcomeMatches)
                {
                    result.AddStep(
                        step.CommandId,
                        0,
                        "Skipped",
                        conditionType: "commandOutcome",
                        conditionResult: "false",
                        actionOutcome: "skipped");
                    if (!string.IsNullOrWhiteSpace(stepKey)) stepOutcomes[stepKey] = "skipped";
                    return false;
                }
            }

            var appliedDelay = GetAppliedDelay(step);
            if (appliedDelay > 0)
            {
                if (_logger != null) LogDelayApplied(_logger, step.CommandId, appliedDelay, null);
                await Task.Delay(appliedDelay, ct).ConfigureAwait(false);
            }

            if (step.Gate != null && gateEvaluator != null)
            {
                if (_logger != null) LogGateStart(_logger, step.CommandId, step.Gate.TargetId ?? string.Empty, step.Gate.Condition.ToString(), step.TimeoutMs.GetValueOrDefault(0), null);
                var deadline = step.TimeoutMs.HasValue && step.TimeoutMs.Value > 0
                    ? DateTimeOffset.UtcNow.AddMilliseconds(step.TimeoutMs.Value)
                    : (DateTimeOffset?)null;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var ok = await gateEvaluator(step, ct).ConfigureAwait(false);
                    if (ok)
                    {
                        if (_logger != null) LogGatePassed(_logger, step.CommandId, null);
                        break;
                    }

                    if (deadline.HasValue && DateTimeOffset.UtcNow >= deadline.Value)
                    {
                        if (_logger != null) LogGateTimeout(_logger, step.CommandId, sequenceId, null);
                        result.Fail("Gating timeout reached");
                        return true; // early stop
                    }

                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
            }

            if (_logger != null) LogCommandStart(_logger, step.CommandId, null);
            var cmdStart = DateTimeOffset.UtcNow;
            try
            {
                await executeCommandAsync(step.CommandId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var reason = !string.IsNullOrWhiteSpace(ex.Message) ? ex.Message : $"step '{stepKey}' command execution failed";
                result.AddStep(
                    step.CommandId,
                    appliedDelay,
                    "Failed",
                    conditionType: step.Condition is null ? null : step.Condition.Type,
                    conditionResult: step.Condition is null ? null : "true",
                    actionOutcome: "failed",
                    message: reason);
                result.Fail(reason);
                if (!string.IsNullOrWhiteSpace(stepKey)) stepOutcomes[stepKey] = "failed";
                return true;
            }
            var durationMs = (int)(DateTimeOffset.UtcNow - cmdStart).TotalMilliseconds;
            result.AddStep(
                step.CommandId,
                appliedDelay,
                "Succeeded",
                conditionType: step.Condition is null ? null : step.Condition.Type,
                conditionResult: step.Condition is null ? null : "true",
                actionOutcome: "executed");
            if (!string.IsNullOrWhiteSpace(stepKey)) stepOutcomes[stepKey] = "success";
            if (_logger != null) LogCommandEnd(_logger, step.CommandId, durationMs, null);
            return false;
        }

        private Task<bool> ExecuteSingleStepAsync(
            SequenceStep step,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            return ExecuteSingleStepAsync(
                step,
                executeCommandAsync,
                gateEvaluator,
                conditionEvaluator: null,
                stepOutcomes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                result,
                sequenceId,
                ct);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Loop step execution (T011 / T019 / T021 / T023)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes a <see cref="SequenceStepType.Loop"/> step.  Returns <c>true</c> when the
        /// outer sequence should stop early (failure), <c>false</c> otherwise.
        /// Perf: per-iteration dispatch overhead is dominated by body-step I/O (commands,
        /// condition evaluations). In-memory loop dispatch cost is negligible (&lt;1 ms/iter).
        /// </summary>
        private async Task<bool> ExecuteLoopStepAsync(
            SequenceStep step,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> stepOutcomes,
            SequenceExecutionResult result,
            string sequenceId,
            int globalMaxIterations,
            CancellationToken ct)
        {
            var stepKey = !string.IsNullOrWhiteSpace(step.StepId) ? step.StepId : $"loop@{step.Order}";
            var maxIterations = step.Loop?.MaxIterations ?? globalMaxIterations;

            switch (step.Loop)
            {
                case CountLoopConfig countCfg:
                    return await ExecuteCountLoopAsync(step, stepKey, countCfg,
                        executeCommandAsync, gateEvaluator, conditionEvaluator,
                        stepOutcomes, result, sequenceId, ct).ConfigureAwait(false);

                case WhileLoopConfig whileCfg:
                    return await ExecuteWhileLoopAsync(step, stepKey, whileCfg, maxIterations,
                        executeCommandAsync, gateEvaluator, conditionEvaluator,
                        stepOutcomes, result, sequenceId, ct).ConfigureAwait(false);

                case RepeatUntilLoopConfig ruCfg:
                    return await ExecuteRepeatUntilLoopAsync(step, stepKey, ruCfg, maxIterations,
                        executeCommandAsync, gateEvaluator, conditionEvaluator,
                        stepOutcomes, result, sequenceId, ct).ConfigureAwait(false);

                default:
                    result.AddStep(stepKey, 0, "Failed", message: $"Loop step '{stepKey}' has missing or unknown loop configuration.");
                    result.Fail($"Loop step '{stepKey}' has missing or unknown loop configuration.");
                    stepOutcomes[stepKey] = "failed";
                    return true;
            }
        }

        /// <summary>Count-based loop: executes body exactly <see cref="CountLoopConfig.Count"/> times.</summary>
        private async Task<bool> ExecuteCountLoopAsync(
            SequenceStep step,
            string stepKey,
            CountLoopConfig cfg,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> stepOutcomes,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var iterResults = new List<LoopIterResult>();

            for (var i = 0; i < cfg.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var iterCtx = ImmutableIterContext(i + 1);
                var (earlyStop, breakTriggered, stepCount) = await ExecuteLoopBodyAsync(
                    step.Body, executeCommandAsync, gateEvaluator, conditionEvaluator,
                    stepOutcomes, result, sequenceId, iterCtx, ct).ConfigureAwait(false);

                iterResults.Add(new LoopIterResult { IterationIndex = i + 1, BreakTriggered = breakTriggered, StepCount = stepCount });

                if (earlyStop)
                {
                    result.AddLoopStep(stepKey, "Failed", iterResults);
                    stepOutcomes[stepKey] = "failed";
                    return true;
                }
                if (breakTriggered) break;
            }

            result.AddLoopStep(stepKey, "Succeeded", iterResults);
            stepOutcomes[stepKey] = "success";
            return false;
        }

        /// <summary>While loop: re-evaluates condition before each iteration.</summary>
        private async Task<bool> ExecuteWhileLoopAsync(
            SequenceStep step,
            string stepKey,
            WhileLoopConfig cfg,
            int maxIterations,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> stepOutcomes,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var iterResults = new List<LoopIterResult>();
            var iterations = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool condResult;
                try
                {
                    condResult = await EvaluateLoopConditionAsync(
                        cfg.Condition, conditionEvaluator, stepOutcomes, ct).ConfigureAwait(false);
                }
                catch
                {
                    result.AddLoopStep(stepKey, "Failed", iterResults, $"Loop '{stepKey}' condition evaluation failed.");
                    result.Fail($"Loop '{stepKey}' condition evaluation failed.");
                    stepOutcomes[stepKey] = "failed";
                    return true;
                }

                if (!condResult)
                {
                    // condition false on entry (or after last iteration)
                    var status = iterations == 0 ? "Skipped" : "Succeeded";
                    result.AddLoopStep(stepKey, status, iterResults);
                    stepOutcomes[stepKey] = iterations == 0 ? "skipped" : "success";
                    return false;
                }

                iterations++;
                if (iterations > maxIterations)
                {
                    result.AddLoopStep(stepKey, "Failed", iterResults, $"Loop '{stepKey}' exceeded maximum iterations ({maxIterations}).");
                    result.Fail($"Loop '{stepKey}' exceeded maximum iterations ({maxIterations}).");
                    stepOutcomes[stepKey] = "failed";
                    return true;
                }

                var iterCtx = ImmutableIterContext(iterations);
                var (earlyStop, breakTriggered, stepCount) = await ExecuteLoopBodyAsync(
                    step.Body, executeCommandAsync, gateEvaluator, conditionEvaluator,
                    stepOutcomes, result, sequenceId, iterCtx, ct).ConfigureAwait(false);

                iterResults.Add(new LoopIterResult { IterationIndex = iterations, BreakTriggered = breakTriggered, StepCount = stepCount });

                if (earlyStop)
                {
                    result.AddLoopStep(stepKey, "Failed", iterResults);
                    stepOutcomes[stepKey] = "failed";
                    return true;
                }
                if (breakTriggered) break;
            }

            result.AddLoopStep(stepKey, "Succeeded", iterResults);
            stepOutcomes[stepKey] = "success";
            return false;
        }

        /// <summary>Repeat-until loop: executes body at least once then checks exit condition.</summary>
        private async Task<bool> ExecuteRepeatUntilLoopAsync(
            SequenceStep step,
            string stepKey,
            RepeatUntilLoopConfig cfg,
            int maxIterations,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> stepOutcomes,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var iterResults = new List<LoopIterResult>();
            var iterations = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                iterations++;

                var iterCtx = ImmutableIterContext(iterations);
                var (earlyStop, breakTriggered, stepCount) = await ExecuteLoopBodyAsync(
                    step.Body, executeCommandAsync, gateEvaluator, conditionEvaluator,
                    stepOutcomes, result, sequenceId, iterCtx, ct).ConfigureAwait(false);

                iterResults.Add(new LoopIterResult { IterationIndex = iterations, BreakTriggered = breakTriggered, StepCount = stepCount });

                if (earlyStop)
                {
                    result.AddLoopStep(stepKey, "Failed", iterResults);
                    stepOutcomes[stepKey] = "failed";
                    return true;
                }
                if (breakTriggered) break;

                if (iterations >= maxIterations)
                {
                    result.AddLoopStep(stepKey, "Failed", iterResults, $"Loop '{stepKey}' exceeded maximum iterations ({maxIterations}).");
                    result.Fail($"Loop '{stepKey}' exceeded maximum iterations ({maxIterations}).");
                    stepOutcomes[stepKey] = "failed";
                    return true;
                }

                // Evaluate exit condition after body executes
                bool exitCond;
                try
                {
                    exitCond = await EvaluateLoopConditionAsync(
                        cfg.Condition, conditionEvaluator, stepOutcomes, ct).ConfigureAwait(false);
                }
                catch
                {
                    result.AddLoopStep(stepKey, "Failed", iterResults, $"Loop '{stepKey}' exit condition evaluation failed.");
                    result.Fail($"Loop '{stepKey}' exit condition evaluation failed.");
                    stepOutcomes[stepKey] = "failed";
                    return true;
                }

                if (exitCond) break;
            }

            result.AddLoopStep(stepKey, "Succeeded", iterResults);
            stepOutcomes[stepKey] = "success";
            return false;
        }

        /// <summary>
        /// Executes the body steps for one loop iteration.  Returns
        /// (<c>earlyStop</c>, <c>breakTriggered</c>, <c>stepsExecuted</c>).
        /// A break step with a condition-evaluator error sets earlyStop=true.
        /// </summary>
        private async Task<(bool EarlyStop, bool BreakTriggered, int StepCount)> ExecuteLoopBodyAsync(
            IReadOnlyList<SequenceStep> bodySteps,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> stepOutcomes,
            SequenceExecutionResult result,
            string sequenceId,
            IReadOnlyDictionary<string, string> iterContext,
            CancellationToken ct)
        {
            var stepsExecuted = 0;

            foreach (var step in bodySteps.OrderBy(s => s.Order))
            {
                ct.ThrowIfCancellationRequested();

                if (step.StepType == SequenceStepType.Break)
                {
                    var brkKey = string.IsNullOrWhiteSpace(step.StepId) ? "break" : step.StepId;

                    if (step.BreakCondition is null)
                    {
                        // Unconditional break
                        result.AddStep(brkKey, 0, "Succeeded",
                            conditionType: "unconditional",
                            conditionResult: "true",
                            actionOutcome: "break",
                            message: "Unconditional break triggered");
                        return (false, true, stepsExecuted);
                    }

                    var condDesc = DescribeBreakCondition(step.BreakCondition);

                    // Conditional break
                    bool breakCond;
                    try
                    {
                        breakCond = await EvaluateLoopConditionAsync(
                            step.BreakCondition, conditionEvaluator, stepOutcomes, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        result.AddStep(brkKey, 0, "Failed",
                            conditionType: condDesc.Type,
                            conditionResult: "error",
                            actionOutcome: "failed",
                            message: $"Break step '{brkKey}' condition evaluation failed ({condDesc.Detail}).");
                        result.Fail($"Break step '{brkKey}' condition evaluation failed.");
                        return (true, false, stepsExecuted);
                    }

                    if (breakCond)
                    {
                        result.AddStep(brkKey, 0, "Succeeded",
                            conditionType: condDesc.Type,
                            conditionResult: "true",
                            actionOutcome: "break",
                            message: $"Break triggered: {condDesc.Detail} evaluated to true");
                        return (false, true, stepsExecuted);
                    }

                    result.AddStep(brkKey, 0, "Skipped",
                        conditionType: condDesc.Type,
                        conditionResult: "false",
                        actionOutcome: "continue",
                        message: $"Break skipped: {condDesc.Detail} evaluated to false");
                    continue; // condition false → keep executing body
                }

                // Apply template substitution to the body step before execution
                SequenceStep effectiveStep = ApplyIterContext(step, iterContext);

                var earlyStop = await ExecuteSingleStepAsync(
                    effectiveStep,
                    executeCommandAsync,
                    gateEvaluator,
                    conditionEvaluator,
                    stepOutcomes,
                    result,
                    sequenceId,
                    ct).ConfigureAwait(false);
                stepsExecuted++;

                if (earlyStop) return (true, false, stepsExecuted);
            }

            return (false, false, stepsExecuted);
        }

        /// <summary>
        /// Evaluates a <see cref="SequenceStepCondition"/> within a loop context.
        /// Throws when the evaluator is unavailable or throws.
        /// </summary>
        private static async Task<bool> EvaluateLoopConditionAsync(
            SequenceStepCondition condition,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            Dictionary<string, string> stepOutcomes,
            CancellationToken ct)
        {
            bool result;

            if (condition is ImageVisibleStepCondition imgCond)
            {
                if (conditionEvaluator is null)
                    throw new InvalidOperationException("Image-visible condition evaluator is unavailable.");
                result = await conditionEvaluator(new Condition
                {
                    Source = "image",
                    TargetId = imgCond.ImageId,
                    Mode = "Present",
                    ConfidenceThreshold = imgCond.MinSimilarity
                }, ct).ConfigureAwait(false);
            }
            else if (condition is CommandOutcomeStepCondition coCond)
            {
                if (!stepOutcomes.TryGetValue(coCond.StepRef, out var actual))
                    throw new InvalidOperationException($"commandOutcome reference '{coCond.StepRef}' is not available.");
                result = string.Equals(actual, coCond.ExpectedState, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported loop condition type '{condition.GetType().Name}'.");
            }

            return condition.Negate ? !result : result;
        }

        private static (string Type, string Detail) DescribeBreakCondition(SequenceStepCondition condition)
        {
            var negatePrefix = condition.Negate ? "NOT " : "";
            if (condition is ImageVisibleStepCondition img)
                return ("imageVisible", $"{negatePrefix}imageVisible(imageId={img.ImageId}, minSimilarity={img.MinSimilarity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "default"})");
            if (condition is CommandOutcomeStepCondition co)
                return ("commandOutcome", $"{negatePrefix}commandOutcome(stepRef={co.StepRef}, expected={co.ExpectedState})");
            return (condition.GetType().Name, $"{negatePrefix}{condition.GetType().Name}");
        }

        /// <summary>
        /// Returns a copy of <paramref name="step"/> with <c>{{key}}</c> placeholders in
        /// <see cref="SequenceStep.CommandId"/> and <see cref="SequenceStep.Action"/> parameters
        /// substituted from <paramref name="context"/>.
        /// </summary>
        private static SequenceStep ApplyIterContext(
            SequenceStep step,
            IReadOnlyDictionary<string, string> context)
        {
            var substitutedCommandId = TemplateSubstitutor.Substitute(step.CommandId, context);
            var substitutedAction = step.Action is not null
                ? TemplateSubstitutor.SubstitutePayload(step.Action, context)
                : null;

            return new SequenceStep
            {
                Order = step.Order,
                StepId = step.StepId,
                Label = step.Label,
                CommandId = substitutedCommandId,
                StepType = step.StepType,
                Action = substitutedAction,
                Condition = step.Condition,
                ConditionExpression = step.ConditionExpression,
                DelayMs = step.DelayMs,
                DelayRangeMs = step.DelayRangeMs,
                TimeoutMs = step.TimeoutMs,
                Retry = step.Retry,
                Gate = step.Gate,
                // Body / Loop / BreakCondition are not deep-substituted in body steps
                Loop = step.Loop,
                Body = step.Body,
                BreakCondition = step.BreakCondition
            };
        }

        /// <summary>Creates a read-only dict with iteration substitution context.</summary>
        private static Dictionary<string, string> ImmutableIterContext(int iteration)
            => new(StringComparer.Ordinal) { ["iteration"] = iteration.ToString(System.Globalization.CultureInfo.InvariantCulture) };

        private async Task ExecuteBlocksAsync(
            IReadOnlyList<object> blocks,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            foreach (var block in blocks)
            {
                if (block is JsonElement je && je.ValueKind == JsonValueKind.Object)
                {
                    if (!je.TryGetProperty("type", out var tProp) || tProp.ValueKind != JsonValueKind.String)
                        continue;
                    var type = tProp.GetString();
                    if (string.Equals(type, "repeatCount", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExecuteRepeatCountAsync(je, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                    }
                    else if (string.Equals(type, "repeatUntil", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExecuteRepeatUntilAsync(je, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                    }
                    else if (string.Equals(type, "while", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExecuteWhileAsync(je, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                    }
                    else if (string.Equals(type, "ifElse", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExecuteIfElseAsync(je, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                    }
                }
                // Exit early if sequence was marked failed by a block (T013)
                if (string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        private static bool TryGetArray(JsonElement obj, string prop, out List<object> list)
        {
            list = new List<object>();
            if (obj.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray()) list.Add(item);
                return true;
            }
            return false;
        }

        private async Task ExecuteIfElseAsync(
            JsonElement block,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            if (_logger != null) LogBlockStart(_logger, "ifElse", sequenceId, null);
            if (!block.TryGetProperty("condition", out var condEl) || condEl.ValueKind != JsonValueKind.Object)
            {
                result.AddBlock(new BlockResult { BlockType = "ifElse", Iterations = 0, DurationMs = 0, Status = "Failed" });
                result.Fail("ifElse missing condition");
                if (_logger != null) LogBlockEnd(_logger, "ifElse", "Failed", 0, 0, null);
                return;
            }
            if (conditionEvaluator == null)
            {
                result.AddBlock(new BlockResult { BlockType = "ifElse", Iterations = 0, DurationMs = 0, Status = "Failed" });
                result.Fail("ifElse condition evaluator unavailable");
                if (_logger != null) LogBlockEnd(_logger, "ifElse", "Failed", 0, 0, null);
                return;
            }

            var cond = ParseCondition(condEl);
            bool takeIf;
            try
            {
                takeIf = await conditionEvaluator(cond, ct).ConfigureAwait(false);
            }
            catch
            {
                result.AddBlock(new BlockResult { BlockType = "ifElse", Iterations = 0, DurationMs = 0, Status = "Failed" });
                result.Fail("ifElse condition evaluation failed");
                if (_logger != null) LogBlockEnd(_logger, "ifElse", "Failed", 0, 0, null);
                return;
            }

            if (_logger != null) LogBlockEvaluation(_logger, "ifElse", "condition", takeIf, null);
            var stepsProp = takeIf ? "steps" : "elseSteps";
            if (TryGetArray(block, stepsProp, out var items) && items.Count > 0)
            {
                await ExecuteStepsCollectionAsync(items, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
            }
            var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
            result.AddBlock(new BlockResult { BlockType = "ifElse", Iterations = 1, Evaluations = 1, BranchTaken = takeIf ? "then" : "else", DurationMs = dur, Status = "Succeeded" });
            if (_logger != null)
            {
                LogBlockDecision(_logger, "ifElse", takeIf ? "then" : "else", 0, null);
                LogBlockEnd(_logger, "ifElse", "Succeeded", 1, 1, null);
            }
        }

        private async Task ExecuteWhileAsync(
            JsonElement block,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            if (_logger != null) LogBlockStart(_logger, "while", sequenceId, null);
            var cadenceMs = block.TryGetProperty("cadenceMs", out var cm) && cm.ValueKind == JsonValueKind.Number ? Math.Clamp(cm.GetInt32(), 50, 5000) : 100;
            var timeoutMs = block.TryGetProperty("timeoutMs", out var to) && to.ValueKind == JsonValueKind.Number ? Math.Max(0, to.GetInt32()) : (int?)null;
            var maxIterations = block.TryGetProperty("maxIterations", out var mi) && mi.ValueKind == JsonValueKind.Number ? Math.Max(1, mi.GetInt32()) : (int?)null;

            if (!block.TryGetProperty("condition", out var condEl) || condEl.ValueKind != JsonValueKind.Object)
            {
                result.AddBlock(new BlockResult { BlockType = "while", Iterations = 0, DurationMs = 0, Status = "Failed" });
                return;
            }
            var cond = ParseCondition(condEl);
            var steps = new List<object>();
            TryGetArray(block, "steps", out steps);
            var iterations = 0;
            var startDeadline = timeoutMs.HasValue ? DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs.Value) : (DateTimeOffset?)null;
            var (breakOn, continueOn) = ParseControl(block);
            var evals = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (breakOn != null && conditionEvaluator != null)
                {
                    var brStart = await conditionEvaluator(breakOn, ct).ConfigureAwait(false);
                    evals++;
                    if (_logger != null) LogBlockEvaluation(_logger, "while", "breakOn-start", brStart, null);
                    if (brStart)
                    {
                        var durBreak = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                        result.AddBlock(new BlockResult { BlockType = "while", Iterations = iterations, Evaluations = evals, DurationMs = durBreak, Status = "Succeeded" });
                        if (_logger != null)
                        {
                            LogBlockDecision(_logger, "while", "break", iterations, null);
                            LogBlockEnd(_logger, "while", "Succeeded", iterations, evals, null);
                        }
                        return;
                    }
                }
                var satisfied = conditionEvaluator != null && await conditionEvaluator(cond, ct).ConfigureAwait(false);
                if (conditionEvaluator != null) evals++;
                if (_logger != null) LogBlockEvaluation(_logger, "while", "condition", satisfied, null);
                if (!satisfied)
                {
                    var durOk = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                    result.AddBlock(new BlockResult { BlockType = "while", Iterations = iterations, Evaluations = evals, DurationMs = durOk, Status = "Succeeded" });
                    if (_logger != null) LogBlockEnd(_logger, "while", "Succeeded", iterations, evals, null);
                    return;
                }

                iterations++;
                var skipRest = false;
                foreach (var s in steps)
                {
                    if (s is JsonElement se)
                    {
                        if (se.ValueKind == JsonValueKind.Object && se.TryGetProperty("type", out var nestedType))
                        {
                            await ExecuteBlocksAsync(new List<object> { se }, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                        }
                        else if (se.ValueKind == JsonValueKind.Object)
                        {
                            var step = ToSequenceStep(se);
                            var earlyStop = await ExecuteSingleStepAsync(step, executeCommandAsync, gateEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                            if (earlyStop)
                            {
                                var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                                result.AddBlock(new BlockResult { BlockType = "while", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Failed" });
                                return;
                            }
                        }
                        if (breakOn != null && conditionEvaluator != null)
                        {
                            var brMid = await conditionEvaluator(breakOn, ct).ConfigureAwait(false);
                            evals++;
                            if (_logger != null) LogBlockEvaluation(_logger, "while", "breakOn-mid", brMid, null);
                            if (brMid)
                            {
                                var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                                result.AddBlock(new BlockResult { BlockType = "while", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Succeeded" });
                                if (_logger != null)
                                {
                                    LogBlockDecision(_logger, "while", "break", iterations, null);
                                    LogBlockEnd(_logger, "while", "Succeeded", iterations, evals, null);
                                }
                                return;
                            }
                        }
                        if (continueOn != null && conditionEvaluator != null)
                        {
                            var contMid = await conditionEvaluator(continueOn, ct).ConfigureAwait(false);
                            evals++;
                            if (_logger != null) LogBlockEvaluation(_logger, "while", "continueOn-mid", contMid, null);
                            if (contMid)
                            {
                                skipRest = true;
                                if (_logger != null) LogBlockDecision(_logger, "while", "continue", iterations, null);
                                break;
                            }
                        }
                    }
                }
                if (skipRest)
                {
                    // continue to next iteration
                }

                if (maxIterations.HasValue && iterations >= maxIterations.Value)
                {
                    var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                    result.AddBlock(new BlockResult { BlockType = "while", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Failed" });
                    if (_logger != null) LogBlockEnd(_logger, "while", "Failed", iterations, evals, null);
                    return;
                }
                if (startDeadline.HasValue && DateTimeOffset.UtcNow >= startDeadline.Value)
                {
                    var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                    result.AddBlock(new BlockResult { BlockType = "while", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Failed" });
                    if (_logger != null) LogBlockEnd(_logger, "while", "Failed", iterations, evals, null);
                    return;
                }
                await Task.Delay(cadenceMs, ct).ConfigureAwait(false);
            }
        }

        private async Task ExecuteStepsCollectionAsync(
            List<object> items,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            foreach (var s in items)
            {
                if (s is JsonElement se)
                {
                    if (se.ValueKind == JsonValueKind.Object && se.TryGetProperty("type", out var nestedType))
                    {
                        await ExecuteBlocksAsync(new List<object> { se }, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                    }
                    else if (se.ValueKind == JsonValueKind.Object)
                    {
                        var step = ToSequenceStep(se);
                        var earlyStop = await ExecuteSingleStepAsync(step, executeCommandAsync, gateEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                        if (earlyStop) return;
                    }
                }
            }
        }

        private async Task ExecuteRepeatUntilAsync(
            JsonElement block,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            if (_logger != null) LogBlockStart(_logger, "repeatUntil", sequenceId, null);
            var cadenceMs = block.TryGetProperty("cadenceMs", out var cm) && cm.ValueKind == JsonValueKind.Number ? Math.Clamp(cm.GetInt32(), 50, 5000) : 100;
            var timeoutMs = block.TryGetProperty("timeoutMs", out var to) && to.ValueKind == JsonValueKind.Number ? Math.Max(0, to.GetInt32()) : (int?)null;
            var maxIterations = block.TryGetProperty("maxIterations", out var mi) && mi.ValueKind == JsonValueKind.Number ? Math.Max(1, mi.GetInt32()) : (int?)null;

            // Parse condition
            if (!block.TryGetProperty("condition", out var condEl) || condEl.ValueKind != JsonValueKind.Object)
            {
                // No condition; treat as failed
                result.AddBlock(new BlockResult { BlockType = "repeatUntil", Iterations = 0, DurationMs = 0, Status = "Failed" });
                result.Fail("repeatUntil missing condition");
                if (_logger != null) LogBlockEnd(_logger, "repeatUntil", "Failed", 0, 0, null);
                return;
            }
            var cond = ParseCondition(condEl);
            var (breakOn, continueOn) = ParseControl(block);
            var evals = 0;

            var iterations = 0;
            var startDeadline = timeoutMs.HasValue ? DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs.Value) : (DateTimeOffset?)null;
            var steps = new List<object>();
            if (block.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in stepsProp.EnumerateArray()) steps.Add(item);
            }

            // Gate-first semantics (FR-17): check before executing steps each iteration
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                iterations++;
                if (breakOn != null && conditionEvaluator != null)
                {
                    var brStart = await conditionEvaluator(breakOn, ct).ConfigureAwait(false);
                    evals++;
                    if (_logger != null) LogBlockEvaluation(_logger, "repeatUntil", "breakOn-start", brStart, null);
                    if (brStart)
                    {
                        var durBreak = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                        result.AddBlock(new BlockResult { BlockType = "repeatUntil", Iterations = iterations, Evaluations = evals, DurationMs = durBreak, Status = "Succeeded" });
                        if (_logger != null)
                        {
                            LogBlockDecision(_logger, "repeatUntil", "break", iterations, null);
                            LogBlockEnd(_logger, "repeatUntil", "Succeeded", iterations, evals, null);
                        }
                        return;
                    }
                }
                var satisfied = false;
                if (conditionEvaluator != null)
                {
                    satisfied = await conditionEvaluator(cond, ct).ConfigureAwait(false);
                    evals++;
                    if (_logger != null) LogBlockEvaluation(_logger, "repeatUntil", "condition", satisfied, null);
                }
                if (satisfied)
                {
                    var durOk = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                    result.AddBlock(new BlockResult { BlockType = "repeatUntil", Iterations = iterations - 1, Evaluations = evals, DurationMs = durOk, Status = "Succeeded" });
                    if (_logger != null) LogBlockEnd(_logger, "repeatUntil", "Succeeded", iterations - 1, evals, null);
                    return;
                }

                // Execute steps when not satisfied
                var skipRest = false;
                foreach (var s in steps)
                {
                    if (s is JsonElement se)
                    {
                        if (se.ValueKind == JsonValueKind.Object && se.TryGetProperty("type", out var nestedType))
                        {
                            await ExecuteBlocksAsync(new List<object> { se }, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                        }
                        else if (se.ValueKind == JsonValueKind.Object)
                        {
                            var step = ToSequenceStep(se);
                            var earlyStop = await ExecuteSingleStepAsync(step, executeCommandAsync, gateEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                            if (earlyStop)
                            {
                                var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                                result.AddBlock(new BlockResult { BlockType = "repeatUntil", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Failed" });
                                return;
                            }
                        }
                        if (breakOn != null && conditionEvaluator != null)
                        {
                            var brMid = await conditionEvaluator(breakOn, ct).ConfigureAwait(false);
                            evals++;
                            if (_logger != null) LogBlockEvaluation(_logger, "repeatUntil", "breakOn-mid", brMid, null);
                            if (brMid)
                            {
                                var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                                result.AddBlock(new BlockResult { BlockType = "repeatUntil", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Succeeded" });
                                if (_logger != null)
                                {
                                    LogBlockDecision(_logger, "repeatUntil", "break", iterations, null);
                                    LogBlockEnd(_logger, "repeatUntil", "Succeeded", iterations, evals, null);
                                }
                                return;
                            }
                        }
                        if (continueOn != null && conditionEvaluator != null)
                        {
                            var contMid = await conditionEvaluator(continueOn, ct).ConfigureAwait(false);
                            evals++;
                            if (_logger != null) LogBlockEvaluation(_logger, "repeatUntil", "continueOn-mid", contMid, null);
                            if (contMid)
                            {
                                skipRest = true;
                                if (_logger != null) LogBlockDecision(_logger, "repeatUntil", "continue", iterations, null);
                                break;
                            }
                        }
                    }
                }
                if (skipRest)
                {
                    // continue to next iteration; safeguards still apply
                }

                // Safeguards: first-hit wins (FR-20)
                if (maxIterations.HasValue && iterations >= maxIterations.Value)
                {
                    var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                    result.AddBlock(new BlockResult { BlockType = "repeatUntil", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Failed" });
                    result.Fail("repeatUntil maxIterations reached");
                    if (_logger != null) LogBlockEnd(_logger, "repeatUntil", "Failed", iterations, evals, null);
                    return;
                }
                if (startDeadline.HasValue && DateTimeOffset.UtcNow >= startDeadline.Value)
                {
                    var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                    result.AddBlock(new BlockResult { BlockType = "repeatUntil", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Failed" });
                    result.Fail("repeatUntil timeout");
                    if (_logger != null) LogBlockEnd(_logger, "repeatUntil", "Failed", iterations, evals, null);
                    return;
                }

                await Task.Delay(cadenceMs, ct).ConfigureAwait(false);
            }
        }

        private static Condition ParseCondition(JsonElement je)
        {
            string src = je.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? (s.GetString() ?? "") : "";
            string tid = je.TryGetProperty("targetId", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "") : "";
            string mode = je.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? (m.GetString() ?? "Present") : "Present";
            double? conf = je.TryGetProperty("confidenceThreshold", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;
            Rect? region = null;
            if (je.TryGetProperty("region", out var r) && r.ValueKind == JsonValueKind.Object)
            {
                region = new Rect
                {
                    X = r.TryGetProperty("x", out var rx) && rx.ValueKind == JsonValueKind.Number ? rx.GetDouble() : 0,
                    Y = r.TryGetProperty("y", out var ry) && ry.ValueKind == JsonValueKind.Number ? ry.GetDouble() : 0,
                    Width = r.TryGetProperty("width", out var rw) && rw.ValueKind == JsonValueKind.Number ? rw.GetDouble() : 0,
                    Height = r.TryGetProperty("height", out var rh) && rh.ValueKind == JsonValueKind.Number ? rh.GetDouble() : 0
                };
            }
            string? lang = je.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
            return new Condition
            {
                Source = src,
                TargetId = tid,
                Mode = mode,
                ConfidenceThreshold = conf,
                Region = region,
                Language = lang
            };
        }

        private static (Condition? BreakOn, Condition? ContinueOn) ParseControl(JsonElement je)
        {
            Condition? breakOn = null;
            Condition? continueOn = null;
            if (je.TryGetProperty("breakOn", out var bje) && bje.ValueKind == JsonValueKind.Object)
            {
                breakOn = ParseCondition(bje);
            }
            if (je.TryGetProperty("continueOn", out var cje) && cje.ValueKind == JsonValueKind.Object)
            {
                continueOn = ParseCondition(cje);
            }
            return (breakOn, continueOn);
        }

        private async Task ExecuteRepeatCountAsync(
            JsonElement block,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            Func<Condition, CancellationToken, Task<bool>>? conditionEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            if (_logger != null) LogBlockStart(_logger, "repeatCount", sequenceId, null);

            var maxIterations = block.TryGetProperty("maxIterations", out var mi) && mi.ValueKind == JsonValueKind.Number ? Math.Max(0, mi.GetInt32()) : 0;
            var cadenceMs = block.TryGetProperty("cadenceMs", out var cm) && cm.ValueKind == JsonValueKind.Number ? Math.Max(0, cm.GetInt32()) : 0;
            var iterations = 0;

            if (maxIterations <= 0)
            {
                result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = 0, DurationMs = 0, Status = "Skipped" });
                if (_logger != null) LogBlockEnd(_logger, "repeatCount", "Skipped", 0, 0, null);
                return;
            }

            var steps = new List<object>();
            if (block.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in stepsProp.EnumerateArray())
                {
                    steps.Add(item);
                }
            }

            var (breakOn, continueOn) = ParseControl(block);
            var evals = 0;

            for (var i = 0; i < maxIterations; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (breakOn != null && conditionEvaluator != null)
                {
                    var brStart = await conditionEvaluator(breakOn, ct).ConfigureAwait(false);
                    evals++;
                    if (_logger != null) LogBlockEvaluation(_logger, "repeatCount", "breakOn-start", brStart, null);
                    if (brStart)
                    {
                        var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                        result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Succeeded" });
                        if (_logger != null)
                        {
                            LogBlockDecision(_logger, "repeatCount", "break", iterations, null);
                            LogBlockEnd(_logger, "repeatCount", "Succeeded", iterations, evals, null);
                        }
                        return;
                    }
                }

                var skipRest = false;

                foreach (var s in steps)
                {
                    if (s is JsonElement se)
                    {
                        if (se.ValueKind == JsonValueKind.Object && se.TryGetProperty("type", out var nestedType))
                        {
                            await ExecuteBlocksAsync(new List<object> { se }, executeCommandAsync, gateEvaluator, conditionEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                        }
                        else if (se.ValueKind == JsonValueKind.Object)
                        {
                            var step = ToSequenceStep(se);
                            var earlyStop = await ExecuteSingleStepAsync(step, executeCommandAsync, gateEvaluator, result, sequenceId, ct).ConfigureAwait(false);
                            if (earlyStop)
                            {
                                var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                                result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Failed" });
                                if (_logger != null) LogBlockEnd(_logger, "repeatCount", "Failed", iterations, evals, null);
                                return;
                            }
                        }

                        if (breakOn != null && conditionEvaluator != null)
                        {
                            var brMid = await conditionEvaluator(breakOn, ct).ConfigureAwait(false);
                            evals++;
                            if (_logger != null) LogBlockEvaluation(_logger, "repeatCount", "breakOn-mid", brMid, null);
                            if (brMid)
                            {
                                var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                                result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = iterations, Evaluations = evals, DurationMs = dur, Status = "Succeeded" });
                                if (_logger != null)
                                {
                                    LogBlockDecision(_logger, "repeatCount", "break", iterations, null);
                                    LogBlockEnd(_logger, "repeatCount", "Succeeded", iterations, evals, null);
                                }
                                return;
                            }
                        }

                        if (continueOn != null && conditionEvaluator != null)
                        {
                            var contMid = await conditionEvaluator(continueOn, ct).ConfigureAwait(false);
                            evals++;
                            if (_logger != null) LogBlockEvaluation(_logger, "repeatCount", "continueOn-mid", contMid, null);
                            if (contMid)
                            {
                                skipRest = true;
                                if (_logger != null) LogBlockDecision(_logger, "repeatCount", "continue", iterations, null);
                                break;
                            }
                        }
                    }
                }

                if (skipRest)
                {
                    iterations++;
                    if (i < maxIterations - 1 && cadenceMs > 0)
                    {
                        await Task.Delay(cadenceMs, ct).ConfigureAwait(false);
                    }
                    continue;
                }

                iterations++;
                if (i < maxIterations - 1 && cadenceMs > 0)
                {
                    await Task.Delay(cadenceMs, ct).ConfigureAwait(false);
                }
            }

            var durationMs = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
            result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = iterations, Evaluations = evals, DurationMs = durationMs, Status = "Succeeded" });
            if (_logger != null) LogBlockEnd(_logger, "repeatCount", "Succeeded", iterations, evals, null);
        }

        private static SequenceStep ToSequenceStep(JsonElement obj)
        {
            var cmdId = obj.TryGetProperty("commandId", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty;
            var order = obj.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
            var delayMs = obj.TryGetProperty("delayMs", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : (int?)null;
            return new SequenceStep
            {
                Order = order,
                CommandId = cmdId,
                DelayMs = delayMs
            };
        }

        // LoggerMessage delegates to satisfy CA1848
        private static readonly Action<ILogger, string, Exception?> LogSequenceStart =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(51000, nameof(LogSequenceStart)), "Sequence start {SequenceId}");
        private static readonly Action<ILogger, string, int, Exception?> LogDelayApplied =
            LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(51001, nameof(LogDelayApplied)), "Delay before command {CommandId} applied: {DelayMs}ms");
        private static readonly Action<ILogger, string, string, string, int, Exception?> LogGateStart =
            LoggerMessage.Define<string, string, string, int>(LogLevel.Information, new EventId(51002, nameof(LogGateStart)), "Gate evaluation start for {CommandId}: Target={TargetId}, Condition={Condition}, TimeoutMs={TimeoutMs}");
        private static readonly Action<ILogger, string, Exception?> LogGatePassed =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(51003, nameof(LogGatePassed)), "Gate passed for {CommandId}");
        private static readonly Action<ILogger, string, string, Exception?> LogGateTimeout =
            LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(51004, nameof(LogGateTimeout)), "Gate timeout for {CommandId}; stopping sequence {SequenceId}");
        private static readonly Action<ILogger, string, Exception?> LogCommandStart =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(51005, nameof(LogCommandStart)), "Command execute start {CommandId}");
        private static readonly Action<ILogger, string, int, Exception?> LogCommandEnd =
            LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(51006, nameof(LogCommandEnd)), "Command execute end {CommandId} duration {DurationMs}ms");
        private static readonly Action<ILogger, string, string, Exception?> LogSequenceEnd =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(51007, nameof(LogSequenceEnd)), "Sequence end {SequenceId} with status {Status}");
        private static readonly Action<ILogger, string, string, string, Exception?> LogConditionTrace =
            LoggerMessage.Define<string, string, string>(LogLevel.Debug, new EventId(51012, nameof(LogConditionTrace)), "Condition trace for step {StepId}: branch {SelectedBranch} trace {TraceJson}");

        // Block-level logging (T020)
        private static readonly Action<ILogger, string, string, Exception?> LogBlockStart =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(51008, nameof(LogBlockStart)), "Block start {BlockType} for {SequenceId}");
        private static readonly Action<ILogger, string, string, int, int, Exception?> LogBlockEnd =
            LoggerMessage.Define<string, string, int, int>(LogLevel.Information, new EventId(51009, nameof(LogBlockEnd)), "Block end {BlockType} status {Status} iterations {Iterations} evaluations {Evaluations}");
        private static readonly Action<ILogger, string, string, int, Exception?> LogBlockDecision =
            LoggerMessage.Define<string, string, int>(LogLevel.Debug, new EventId(51010, nameof(LogBlockDecision)), "Block decision {BlockType} {Decision} at iteration {Iteration}");
        private static readonly Action<ILogger, string, string, bool, Exception?> LogBlockEvaluation =
            LoggerMessage.Define<string, string, bool>(LogLevel.Debug, new EventId(51011, nameof(LogBlockEvaluation)), "Block eval {BlockType} {Evaluation} outcome {Outcome}");

        private static int GetAppliedDelay(SequenceStep step)
        {
            if (step.DelayRangeMs != null)
            {
                var min = Math.Max(0, step.DelayRangeMs.Min);
                var max = Math.Max(min, step.DelayRangeMs.Max);
                // Use non-crypto randomness via a bounded linear congruential fallback to satisfy CA5394 in non-security context
                unchecked
                {
                    var seed = (int)(DateTime.UtcNow.Ticks & 0x00000000FFFFFFFF);
                    seed = 1664525 * seed + 1013904223; // LCG step
                    var range = max - min + 1;
                    var value = min + Math.Abs(seed % range);
                    return value;
                }
            }
            return step.DelayMs.GetValueOrDefault(0);
        }
    }

    public class SequenceExecutionResult
    {
        public string SequenceId { get; private set; } = string.Empty;
        public string Status { get; private set; } = "Succeeded";
        public DateTimeOffset StartedAt { get; private set; }
        public DateTimeOffset EndedAt { get; private set; }
        private readonly System.Collections.Generic.List<StepResult> _steps = new();
        public System.Collections.Generic.IReadOnlyList<StepResult> Steps => _steps.AsReadOnly();
        private readonly System.Collections.Generic.List<BlockResult> _blocks = new();
        public System.Collections.Generic.IReadOnlyList<BlockResult> Blocks => _blocks.AsReadOnly();
        private readonly System.Collections.Generic.List<ConditionStepTraceResult> _conditionTraces = new();
        public System.Collections.Generic.IReadOnlyList<ConditionStepTraceResult> ConditionTraces => _conditionTraces.AsReadOnly();

        public static SequenceExecutionResult Start(string sequenceId)
        {
            return new SequenceExecutionResult
            {
                SequenceId = sequenceId,
                StartedAt = DateTimeOffset.UtcNow
            };
        }

        public static SequenceExecutionResult NotFound(string sequenceId)
        {
            return new SequenceExecutionResult
            {
                SequenceId = sequenceId,
                Status = "Failed",
                StartedAt = DateTimeOffset.UtcNow,
                EndedAt = DateTimeOffset.UtcNow
            };
        }

        public void AddStep(
            string commandId,
            int appliedDelayMs,
            string status = "Succeeded",
            string? conditionType = null,
            string? conditionResult = null,
            string? actionOutcome = null,
            string? message = null)
        {
            _steps.Add(new StepResult
            {
                CommandId = commandId,
                Status = status,
                Attempts = 1,
                DurationMs = 0,
                AppliedDelayMs = appliedDelayMs,
                ConditionType = conditionType,
                ConditionResult = conditionResult,
                ActionOutcome = actionOutcome,
                Message = message
            });
        }

        public void AddBlock(BlockResult block)
        {
            _blocks.Add(block);
        }

        public void AddConditionTrace(string stepId, string? stepLabel, ConditionEvaluationTrace trace)
        {
            _conditionTraces.Add(new ConditionStepTraceResult
            {
                StepId = stepId,
                StepLabel = stepLabel,
                Trace = trace
            });
        }

        public void AddLoopStep(
            string stepId,
            string status,
            IReadOnlyList<LoopIterResult> iterResults,
            string? message = null)
        {
            _steps.Add(new StepResult
            {
                CommandId = stepId,
                Status = status,
                Attempts = 1,
                LoopIterations = iterResults,
                Message = message
            });
        }

        public void Complete()
        {
            EndedAt = DateTimeOffset.UtcNow;
            if (!string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                Status = "Succeeded";
            }
        }

        public void Fail(string? error)
        {
            EndedAt = DateTimeOffset.UtcNow;
            Status = "Failed";
        }
    }

    public class ConditionStepTraceResult
    {
        public string StepId { get; set; } = string.Empty;
        public string? StepLabel { get; set; }
        public ConditionEvaluationTrace Trace { get; set; } = new(false, "none", null, Array.Empty<Dictionary<string, object?>>(), Array.Empty<Dictionary<string, object?>>());
    }

    public class StepResult
    {
        public int Order { get; set; }
        public string CommandId { get; set; } = string.Empty;
        public string Status { get; set; } = "Succeeded";
        public int Attempts { get; set; }
        public int DurationMs { get; set; }
        public string? Error { get; set; }
        public int AppliedDelayMs { get; set; }
        public string? ConditionType { get; set; }
        public string? ConditionResult { get; set; }
        public string? ActionOutcome { get; set; }
        public string? Message { get; set; }
        /// <summary>Per-iteration results for loop steps; null for non-loop steps.</summary>
        public IReadOnlyList<LoopIterResult>? LoopIterations { get; set; }
    }

    /// <summary>Result summary for a single loop iteration.</summary>
    public sealed class LoopIterResult
    {
        public int IterationIndex { get; set; }
        public bool BreakTriggered { get; set; }
        public int StepCount { get; set; }
    }
}

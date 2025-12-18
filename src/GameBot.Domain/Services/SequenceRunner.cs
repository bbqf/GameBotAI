using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using GameBot.Domain.Commands.Blocks;

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

        public SequenceRunner(ISequenceRepository repository)
        {
            _repository = repository;
        }

        // Optional logger-friendly constructor when DI provides ILogger
        public SequenceRunner(ISequenceRepository repository, ILogger<SequenceRunner> logger)
        {
            _repository = repository;
            _logger = logger;
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
            foreach (var step in sequence.Steps.OrderBy(s => s.Order))
            {
                ct.ThrowIfCancellationRequested();
                var earlyStop = await ExecuteSingleStepAsync(step, executeCommandAsync, gateEvaluator, result, sequenceId, ct).ConfigureAwait(false);
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

        private async Task<bool> ExecuteSingleStepAsync(
            SequenceStep step,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            SequenceExecutionResult result,
            string sequenceId,
            CancellationToken ct)
        {
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
            await executeCommandAsync(step.CommandId).ConfigureAwait(false);
            var durationMs = (int)(DateTimeOffset.UtcNow - cmdStart).TotalMilliseconds;
            result.AddStep(step.CommandId, appliedDelay);
            if (_logger != null) LogCommandEnd(_logger, step.CommandId, durationMs, null);
            return false;
        }

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
                if (_logger != null) LogBlockEnd(_logger, "ifElse", "Failed", 0, 0, null);
                return;
            }
            var cond = ParseCondition(condEl);
            var takeIf = conditionEvaluator != null && await conditionEvaluator(cond, ct).ConfigureAwait(false);
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

        public void AddStep(string commandId, int appliedDelayMs)
        {
            _steps.Add(new StepResult
            {
                CommandId = commandId,
                Status = "Succeeded",
                Attempts = 1,
                DurationMs = 0,
                AppliedDelayMs = appliedDelayMs
            });
        }

        public void AddBlock(BlockResult block)
        {
            _blocks.Add(block);
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

    public class StepResult
    {
        public int Order { get; set; }
        public string CommandId { get; set; } = string.Empty;
        public string Status { get; set; } = "Succeeded";
        public int Attempts { get; set; }
        public int DurationMs { get; set; }
        public string? Error { get; set; }
        public int AppliedDelayMs { get; set; }
    }
}

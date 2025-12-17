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
            CancellationToken ct,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator = null)
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
                var earlyStop = await ExecuteSingleStepAsync(step, executeCommandAsync, gateEvaluator, result, ct, sequenceId).ConfigureAwait(false);
                if (earlyStop)
                {
                    if (_logger != null) LogSequenceEnd(_logger, sequenceId, result.Status, null);
                    return result;
                }
            }

            // Execute blocks if any (US1: repeatCount)
            if (sequence is { Blocks: { Count: > 0 } })
            {
                await ExecuteBlocksAsync(sequence.Blocks, executeCommandAsync, gateEvaluator, result, ct, sequenceId).ConfigureAwait(false);
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
            CancellationToken ct,
            string sequenceId)
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
            SequenceExecutionResult result,
            CancellationToken ct,
            string sequenceId)
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
                        await ExecuteRepeatCountAsync(je, executeCommandAsync, gateEvaluator, result, ct, sequenceId).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task ExecuteRepeatCountAsync(
            JsonElement block,
            Func<string, Task> executeCommandAsync,
            Func<SequenceStep, CancellationToken, Task<bool>>? gateEvaluator,
            SequenceExecutionResult result,
            CancellationToken ct,
            string sequenceId)
        {
            var start = DateTimeOffset.UtcNow;
            var maxIterations = block.TryGetProperty("maxIterations", out var mi) && mi.ValueKind == JsonValueKind.Number ? Math.Max(0, mi.GetInt32()) : 0;
            var cadenceMs = block.TryGetProperty("cadenceMs", out var cm) && cm.ValueKind == JsonValueKind.Number ? Math.Max(0, cm.GetInt32()) : 0;
            var iterations = 0;
            if (maxIterations <= 0)
            {
                // no iterations; record block result
                result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = 0, DurationMs = 0, Status = "Skipped" });
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

            for (var i = 0; i < maxIterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var s in steps)
                {
                    if (s is JsonElement se)
                    {
                        if (se.ValueKind == JsonValueKind.Object && se.TryGetProperty("type", out var nestedType))
                        {
                            // nested block
                            await ExecuteBlocksAsync(new List<object> { se }, executeCommandAsync, gateEvaluator, result, ct, sequenceId).ConfigureAwait(false);
                        }
                        else if (se.ValueKind == JsonValueKind.Object)
                        {
                            var step = ToSequenceStep(se);
                            var earlyStop = await ExecuteSingleStepAsync(step, executeCommandAsync, gateEvaluator, result, ct, sequenceId).ConfigureAwait(false);
                            if (earlyStop)
                            {
                                // early stop due to gating timeout
                                var dur = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                                result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = iterations, DurationMs = dur, Status = "Failed" });
                                return;
                            }
                        }
                    }
                }
                iterations++;
                if (i < maxIterations - 1 && cadenceMs > 0)
                {
                    await Task.Delay(cadenceMs, ct).ConfigureAwait(false);
                }
            }
            var durationMs = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
            result.AddBlock(new BlockResult { BlockType = "repeatCount", Iterations = iterations, DurationMs = durationMs, Status = "Succeeded" });
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
            Status = "Succeeded";
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

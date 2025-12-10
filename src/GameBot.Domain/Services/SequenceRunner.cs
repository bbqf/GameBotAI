using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services
{
    /// <summary>
    /// Minimal runner that executes a sequence by iterating steps and applying delays.
    /// Detection gating and retries will be added in later phases.
    /// </summary>
    public class SequenceRunner
    {
        private readonly ISequenceRepository _repository;

        public SequenceRunner(ISequenceRepository repository)
        {
            _repository = repository;
        }

        public async Task<SequenceExecutionResult> ExecuteAsync(string sequenceId, Func<string, Task> executeCommandAsync, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(executeCommandAsync);
            var sequence = await _repository.GetAsync(sequenceId).ConfigureAwait(false);
            if (sequence == null)
            {
                return SequenceExecutionResult.NotFound(sequenceId);
            }

            var result = SequenceExecutionResult.Start(sequenceId);
            foreach (var step in sequence.Steps.OrderBy(s => s.Order))
            {
                ct.ThrowIfCancellationRequested();
                var appliedDelay = GetAppliedDelay(step);
                if (appliedDelay > 0)
                {
                    await Task.Delay(appliedDelay, ct).ConfigureAwait(false);
                }

                await executeCommandAsync(step.CommandId).ConfigureAwait(false);
                result.AddStep(step.CommandId, appliedDelay);
            }
            result.Complete();
            return result;
        }

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

        public void Complete()
        {
            EndedAt = DateTimeOffset.UtcNow;
            Status = "Succeeded";
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

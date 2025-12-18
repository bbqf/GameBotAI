using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences
{
    public class GatingTests
    {
        private sealed class StubRepo : ISequenceRepository
        {
            private readonly CommandSequence _seq;
            public StubRepo(CommandSequence seq) { _seq = seq; }
            public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_seq);
            public Task<System.Collections.Generic.IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<System.Collections.Generic.IReadOnlyList<CommandSequence>>(new System.Collections.Generic.List<CommandSequence> { _seq }.AsReadOnly());
            public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
            public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
            public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
        }

        [Fact]
        public async Task GatingTimeoutFailsSequence()
        {
            var seq = new CommandSequence { Id = "g1", Name = "gate" };
            seq.SetSteps(new[]
            {
                new SequenceStep { Order = 1, CommandId = "c1", TimeoutMs = 200, Gate = new GateConfig { TargetId = "t1", Condition = GateCondition.Present } }
            });
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("g1", _ => Task.CompletedTask, gateEvaluator: async (_, __) => { await Task.Yield(); return false; }, ct: CancellationToken.None);
            res.Status.Should().Be("Failed");
        }

        [Fact]
        public async Task GatingSuccessAllowsExecution()
        {
            var seq = new CommandSequence { Id = "g2", Name = "gate" };
            seq.SetSteps(new[]
            {
                new SequenceStep { Order = 1, CommandId = "c1", TimeoutMs = 500, Gate = new GateConfig { TargetId = "t1", Condition = GateCondition.Present } }
            });
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("g2", _ => Task.CompletedTask, gateEvaluator: (_, __) => Task.FromResult(true), ct: CancellationToken.None);
            res.Status.Should().Be("Succeeded");
            res.Steps.Should().HaveCount(1);
        }
    }
}

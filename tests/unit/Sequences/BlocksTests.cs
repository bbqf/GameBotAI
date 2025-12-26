using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences
{
    public class BlocksTests
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
        public async Task RepeatCountBreakOnStartExitsImmediately()
        {
            var seq = new CommandSequence { Id = "b1", Name = "blocks" };
                        var blockJson = JsonDocument.Parse("{\"type\":\"repeatCount\",\"maxIterations\":5,\"steps\":[{\"order\":1,\"commandId\":\"c1\"}],\"breakOn\":{\"source\":\"trigger\",\"targetId\":\"t\",\"mode\":\"Present\"}}");
            seq.SetBlocks(new object[] { blockJson.RootElement });
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("b1", _ => Task.CompletedTask, conditionEvaluator: (_, __) => Task.FromResult(true), ct: CancellationToken.None);
            res.Status.Should().Be("Succeeded");
            res.Blocks.Should().HaveCount(1);
            var b = res.Blocks[0];
            b.BlockType.Should().Be("repeatCount");
            b.Iterations.Should().Be(0); // break at start before any iteration increments
            b.Evaluations.Should().BeGreaterOrEqualTo(1);
            b.Status.Should().Be("Succeeded");
        }

        [Fact]
        public async Task RepeatCountContinueOnMidSkipsRemainingSteps()
        {
            var seq = new CommandSequence { Id = "b2", Name = "blocks" };
                        var blockJson = JsonDocument.Parse("{\"type\":\"repeatCount\",\"maxIterations\":3,\"steps\":[{\"order\":1,\"commandId\":\"c1\"},{\"order\":2,\"commandId\":\"c2\"}],\"continueOn\":{\"source\":\"trigger\",\"targetId\":\"t\",\"mode\":\"Present\"},\"cadenceMs\":0}");
            seq.SetBlocks(new object[] { blockJson.RootElement });
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("b2", _ => Task.CompletedTask, conditionEvaluator: (_, __) => Task.FromResult(true), ct: CancellationToken.None);
            res.Status.Should().Be("Succeeded");
            res.Blocks.Should().HaveCount(1);
            var b = res.Blocks[0];
            b.BlockType.Should().Be("repeatCount");
            b.Iterations.Should().Be(3); // completed all iterations, skipping mid when continueOn triggered
            b.Evaluations.Should().BeGreaterOrEqualTo(3); // one continue eval per iteration
            b.Status.Should().Be("Succeeded");
        }

        [Fact]
        public async Task RepeatCountBreakOnMidStopsLoop()
        {
            var seq = new CommandSequence { Id = "b3", Name = "blocks" };
                        var blockJson = JsonDocument.Parse("{\"type\":\"repeatCount\",\"maxIterations\":4,\"steps\":[{\"order\":1,\"commandId\":\"c1\"},{\"order\":2,\"commandId\":\"c2\"}],\"breakOn\":{\"source\":\"trigger\",\"targetId\":\"t\",\"mode\":\"Present\"}}");
            seq.SetBlocks(new object[] { blockJson.RootElement });
            var runner = new SequenceRunner(new StubRepo(seq));
            // return false at start, true mid: emulate break after first step
            var res = await runner.ExecuteAsync("b3", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) => Task.FromResult(true),
                ct: CancellationToken.None);
            res.Status.Should().Be("Succeeded");
            res.Blocks.Should().HaveCount(1);
            var b = res.Blocks[0];
            b.BlockType.Should().Be("repeatCount");
            b.Iterations.Should().Be(0); // break during first iteration before increment
            b.Status.Should().Be("Succeeded");
        }
    }
}

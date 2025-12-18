using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences
{
    public class BlocksLoopTests
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
        public async Task RepeatUntilTimesOutAndFails()
        {
            var seq = new CommandSequence { Id = "ru-timeout", Name = "repeatUntil" };
            var blockJson = JsonDocument.Parse("{\"type\":\"repeatUntil\",\"timeoutMs\":150,\"cadenceMs\":40,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"}}" );
            seq.SetBlocks(new object[] { blockJson.RootElement });

            var runner = new SequenceRunner(new StubRepo(seq));
            var sw = Stopwatch.StartNew();
            var res = await runner.ExecuteAsync("ru-timeout", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) => Task.FromResult(false),
                ct: CancellationToken.None);
            sw.Stop();

            // While block failure does not mark entire sequence failed; only block is Failed
            res.Status.Should().Be("Succeeded");
            res.Blocks.Should().HaveCount(1);
            var b = res.Blocks[0];
            b.Status.Should().Be("Failed");
            b.BlockType.Should().Be("repeatUntil");
            b.Evaluations.Should().BeGreaterThan(0);
            b.DurationMs.Should().BeGreaterOrEqualTo(100); // allow tolerance
            sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(100);
        }

        [Fact]
        public async Task RepeatUntilContinueMidSkipsUntilConditionTrue()
        {
            var seq = new CommandSequence { Id = "ru-continue", Name = "repeatUntil" };
            var json = "{\"type\":\"repeatUntil\",\"timeoutMs\":1000,\"cadenceMs\":0,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"},\"continueOn\":{\"source\":\"trigger\",\"targetId\":\"cont\",\"mode\":\"Present\"},\"steps\":[{\"order\":1,\"commandId\":\"c1\"},{\"order\":2,\"commandId\":\"c2\"}]}";
            var blockJson = JsonDocument.Parse(json);
            seq.SetBlocks(new object[] { blockJson.RootElement });

            var starts = 0;
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("ru-continue", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) =>
                {
                    if (cond.TargetId == "main")
                    {
                        starts++;
                        return Task.FromResult(starts >= 3);
                    }
                    if (cond.TargetId == "cont")
                    {
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);
                },
                ct: CancellationToken.None);

            res.Status.Should().Be("Succeeded");
            res.Blocks.Should().HaveCount(1);
            var b = res.Blocks[0];
            b.BlockType.Should().Be("repeatUntil");
            b.Iterations.Should().BeGreaterOrEqualTo(2);
            b.Evaluations.Should().BeGreaterOrEqualTo(5); // 2 start + 2 cont + final start
        }

        [Fact]
        public async Task WhileBreakOnMidStopsLoop()
        {
            var seq = new CommandSequence { Id = "wh-break", Name = "while" };
            var json = "{\"type\":\"while\",\"timeoutMs\":500,\"cadenceMs\":0,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"},\"breakOn\":{\"source\":\"trigger\",\"targetId\":\"break\",\"mode\":\"Present\"},\"steps\":[{\"order\":1,\"commandId\":\"c1\"}]}";
            var blockJson = JsonDocument.Parse(json);
            seq.SetBlocks(new object[] { blockJson.RootElement });

            var firstStart = true;
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("wh-break", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) =>
                {
                    if (cond.TargetId == "main")
                    {
                        if (firstStart)
                        {
                            firstStart = false;
                            return Task.FromResult(true);
                        }
                        return Task.FromResult(false);
                    }
                    if (cond.TargetId == "break")
                    {
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);
                },
                ct: CancellationToken.None);

            res.Status.Should().Be("Succeeded");
            res.Blocks.Should().HaveCount(1);
            var b = res.Blocks[0];
            b.BlockType.Should().Be("while");
            b.Iterations.Should().BeGreaterOrEqualTo(0);
        }

        [Fact]
        public async Task CadenceAddsDelayBetweenIterations()
        {
            var seq = new CommandSequence { Id = "wh-cadence", Name = "while" };
            var json = "{\"type\":\"while\",\"timeoutMs\":1000,\"cadenceMs\":35,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"}}";
            var blockJson = JsonDocument.Parse(json);
            seq.SetBlocks(new object[] { blockJson.RootElement });

            int starts = 0;
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("wh-cadence", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) =>
                {
                    if (cond.TargetId == "main")
                    {
                        starts++;
                        return Task.FromResult(starts < 3); // allow two full waits, then stop
                    }
                    return Task.FromResult(false);
                },
                ct: CancellationToken.None);

            res.Status.Should().Be("Succeeded");
            var b = res.Blocks[0];
            b.BlockType.Should().Be("while");
            b.Iterations.Should().BeGreaterOrEqualTo(2);
            b.DurationMs.Should().BeGreaterOrEqualTo(60); // ~2 * 35ms with tolerance
        }

        [Fact]
        public async Task WhileConditionFalseAtStartSucceeds()
        {
            var seq = new CommandSequence { Id = "wh-cond-false", Name = "while" };
            var json = "{\"type\":\"while\",\"timeoutMs\":500,\"cadenceMs\":50,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"}}";
            var blockJson = JsonDocument.Parse(json);
            seq.SetBlocks(new object[] { blockJson.RootElement });

            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("wh-cond-false", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) => Task.FromResult(false),
                ct: CancellationToken.None);

            res.Status.Should().Be("Succeeded");
            var b = res.Blocks[0];
            b.BlockType.Should().Be("while");
            b.Iterations.Should().Be(0);
        }

        [Fact]
        public async Task WhileMaxIterationsLimitFails()
        {
            var seq = new CommandSequence { Id = "wh-max", Name = "while" };
            var json = "{\"type\":\"while\",\"timeoutMs\":1000,\"cadenceMs\":50,\"maxIterations\":2,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"}}";
            var blockJson = JsonDocument.Parse(json);
            seq.SetBlocks(new object[] { blockJson.RootElement });

            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("wh-max", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) => Task.FromResult(true),
                ct: CancellationToken.None);

            res.Status.Should().Be("Failed");
            var b = res.Blocks[0];
            b.BlockType.Should().Be("while");
            b.Iterations.Should().BeGreaterOrEqualTo(2);
            b.Status.Should().Be("Failed");
        }

        [Fact]
        public async Task RepeatUntilMaxIterationsLimitFails()
        {
            var seq = new CommandSequence { Id = "ru-max", Name = "repeatUntil" };
            var json = "{\"type\":\"repeatUntil\",\"timeoutMs\":1000,\"cadenceMs\":10,\"maxIterations\":2,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"}}";
            var blockJson = JsonDocument.Parse(json);
            seq.SetBlocks(new object[] { blockJson.RootElement });

            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("ru-max", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) => Task.FromResult(false),
                ct: CancellationToken.None);

            res.Status.Should().Be("Failed");
            var b = res.Blocks[0];
            b.BlockType.Should().Be("repeatUntil");
            b.Iterations.Should().BeGreaterOrEqualTo(2);
            b.Status.Should().Be("Failed");
        }

        [Fact]
        public async Task RepeatUntilConditionTrueAtStartSucceeds()
        {
            var seq = new CommandSequence { Id = "ru-start-true", Name = "repeatUntil" };
            var json = "{\"type\":\"repeatUntil\",\"timeoutMs\":1000,\"cadenceMs\":10,\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"}}";
            var blockJson = JsonDocument.Parse(json);
            seq.SetBlocks(new object[] { blockJson.RootElement });

            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("ru-start-true", _ => Task.CompletedTask,
                conditionEvaluator: (cond, __) => Task.FromResult(true),
                ct: CancellationToken.None);

            res.Status.Should().Be("Succeeded");
            var b = res.Blocks[0];
            b.BlockType.Should().Be("repeatUntil");
            b.Status.Should().Be("Succeeded");
        }
    }
}

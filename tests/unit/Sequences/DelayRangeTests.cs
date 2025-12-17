using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences
{
    public class DelayRangeTests
    {
        private sealed class StubRepo : ISequenceRepository
        {
            private readonly CommandSequence _seq;
            public StubRepo(CommandSequence seq) { _seq = seq; }
            public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_seq);
            public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
            public Task<System.Collections.Generic.IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<System.Collections.Generic.IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _seq }.AsReadOnly());
            public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
            public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
        }

        [Fact]
        public async Task RangePrecedenceOverridesFixedDelay()
        {
            var seq = new CommandSequence { Id = "s1", Name = "test" };
            seq.SetSteps(new[]
            {
                new SequenceStep { Order = 1, CommandId = "c1", DelayMs = 999, DelayRangeMs = new DelayRangeMs { Min = 10, Max = 20 } }
            });
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("s1", _ => Task.CompletedTask, CancellationToken.None);
            res.Status.Should().Be("Succeeded");
            res.Steps.Should().HaveCount(1);
            res.Steps[0].AppliedDelayMs.Should().BeGreaterOrEqualTo(10).And.BeLessOrEqualTo(20);
        }

        [Fact]
        public async Task RangeBoundsAreClampedAndApplied()
        {
            var seq = new CommandSequence { Id = "s2", Name = "bounds" };
            seq.SetSteps(new[]
            {
                new SequenceStep { Order = 1, CommandId = "c1", DelayRangeMs = new DelayRangeMs { Min = -5, Max = 0 } },
                new SequenceStep { Order = 2, CommandId = "c2", DelayRangeMs = new DelayRangeMs { Min = 5, Max = 5 } }
            });
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("s2", _ => Task.CompletedTask, CancellationToken.None);
            res.Steps[0].AppliedDelayMs.Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(0);
            res.Steps[1].AppliedDelayMs.Should().Be(5);
        }

        [Fact]
        public async Task FixedDelayAppliesWhenNoRange()
        {
            var seq = new CommandSequence { Id = "s3", Name = "fixed" };
            seq.SetSteps(new[]
            {
                new SequenceStep { Order = 1, CommandId = "c1", DelayMs = 7 }
            });
            var runner = new SequenceRunner(new StubRepo(seq));
            var res = await runner.ExecuteAsync("s3", _ => Task.CompletedTask, CancellationToken.None);
            res.Steps[0].AppliedDelayMs.Should().Be(7);
        }
    }
}

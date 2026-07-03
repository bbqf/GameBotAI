using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 066 (US2 / FR-010): the loop-level <c>breakOn</c> on a while block must behave like the
/// discrete break step — a <c>breakOn</c> that evaluates true ends the block (success), and a
/// <c>breakOn</c> whose evaluation throws is treated as "no break" (guarded) and never propagates
/// out to fail the run.
/// </summary>
public sealed class SequenceRunnerWhileBreakOnTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _seq;
    public StubRepo(CommandSequence seq) { _seq = seq; }
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_seq);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
        Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _seq }.AsReadOnly());
    public Task<CommandSequence> CreateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<CommandSequence> UpdateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static CommandSequence WhileWithBreakOn(string id) {
    var seq = new CommandSequence { Id = id, Name = id };
    var json = "{\"type\":\"while\",\"timeoutMs\":1000,\"cadenceMs\":0," +
               "\"condition\":{\"source\":\"trigger\",\"targetId\":\"main\",\"mode\":\"Present\"}," +
               "\"breakOn\":{\"source\":\"trigger\",\"targetId\":\"break\",\"mode\":\"Present\"}," +
               "\"steps\":[{\"order\":1,\"commandId\":\"c1\"}]}";
    seq.SetBlocks(new object[] { JsonDocument.Parse(json).RootElement });
    return seq;
  }

  [Fact] // T017 (US2) — breakOn evaluation error is guarded: no throw, loop continues, run Succeeded.
  public async Task WhileBreakOnEvaluationErrorIsGuardedAndRunSucceeds() {
    var seq = WhileWithBreakOn("wh-breakon-error");
    var mainChecks = 0;

    var runner = new SequenceRunner(new StubRepo(seq));
    var res = await runner.ExecuteAsync("wh-breakon-error", _ => Task.CompletedTask,
        conditionEvaluator: (cond, _) => {
          if (cond.TargetId == "break") throw new InvalidOperationException("breakOn eval error");
          // main stays true for two iterations then ends the loop normally.
          if (cond.TargetId == "main") return Task.FromResult(++mainChecks <= 2);
          return Task.FromResult(false);
        },
        ct: CancellationToken.None);

    res.Status.Should().Be("Succeeded");
    res.Blocks.Should().HaveCount(1);
    res.Blocks[0].Status.Should().NotBe("Failed");
  }

  [Fact] // T017 (US2) — breakOn that evaluates true ends the block with Status "true" (fired).
  public async Task WhileBreakOnTrueEndsBlockWithTrueStatus() {
    var seq = WhileWithBreakOn("wh-breakon-true");

    var runner = new SequenceRunner(new StubRepo(seq));
    var res = await runner.ExecuteAsync("wh-breakon-true", _ => Task.CompletedTask,
        conditionEvaluator: (cond, _) => {
          if (cond.TargetId == "main") return Task.FromResult(true);
          if (cond.TargetId == "break") return Task.FromResult(true); // fires at breakOn-start
          return Task.FromResult(false);
        },
        ct: CancellationToken.None);

    res.Status.Should().Be("Succeeded");
    res.Blocks.Should().ContainSingle();
    res.Blocks[0].Status.Should().Be("true");
  }
}

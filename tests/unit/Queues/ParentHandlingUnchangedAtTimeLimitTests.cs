using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 170: the option changes only the status of the run that the limit ends. The queue run that
/// holds it (the parent) gets the same result for <c>fail</c> and for <c>end</c>: the follow-up
/// sequence still runs and the root log entry is the same. The option also uses the default limit.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  private static async Task<(string? Status, string? Summary, string[] Executed)> RunTwoSequencesAsync(string? onTimeLimit) {
    var h = new Harness(sequenceRepository: true);
    await AddLimitedSequenceAsync(h, "child", onTimeLimit);
    h.AddQueue("q1", new[] { "child", "after" });
    h.Sequences.Handler = async (id, ct) => {
      if (id == "child") await Task.Delay(Timeout.Infinite, ct);
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");
    return (h.Log.FinalStatus, h.Log.Summary, h.Sequences.Executed.ToArray());
  }

  [Fact]
  public async Task ParentRunResult_IsTheSameForFailAndEnd() {
    var failCase = await RunTwoSequencesAsync("fail");
    var endCase = await RunTwoSequencesAsync("end");

    endCase.Status.Should().Be(failCase.Status);
    // The queue run counts the planned stop as a success, so only its failed count differs.
    failCase.Summary.Should().Contain("1 failed");
    endCase.Summary.Should().NotContain("failed");
    endCase.Executed.Should().Equal(failCase.Executed);
    endCase.Executed.Should().Equal("child", "after");
  }

  [Fact]
  public async Task AnEndSequenceWithNoOwnLimit_UsesTheDefaultLimit() {
    var h = new Harness(sequenceRepository: true);
    await h.SequenceRepository!.CreateAsync(new CommandSequence { Id = "A", Name = "A", OnTimeLimit = "end" });
    h.AddQueue("q1", new[] { "A" });
    int? seenLimit = null;
    bool? seenEnds = null;
    h.Sequences.Handler = (id, ct) => {
      seenLimit = SequenceTimeLimitScope.Current?.TimeLimitMs;
      seenEnds = SequenceTimeLimitScope.Current?.EndsRunAtLimit;
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    seenLimit.Should().Be(SequenceTimeLimits.DefaultWatchdogTimeoutMs).And.Be(240000);
    seenEnds.Should().BeTrue();
  }
}

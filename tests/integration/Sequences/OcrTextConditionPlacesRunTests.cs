using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Service.Services.SequenceExecution;
using Xunit;
using static GameBot.IntegrationTests.Sequences.OcrTextRunKit;

#pragma warning disable CA2007, CA1826

namespace GameBot.IntegrationTests.Sequences;

/// <summary>The ocrText condition in every condition place, and the scope of each run entry point (issue #338, SC-003, FR-020).</summary>
public sealed class OcrTextConditionPlacesRunTests {
  private static SequenceStep WhileLoop(SequenceStepCondition condition) => new() {
    Order = 0,
    StepId = "loop",
    StepType = SequenceStepType.Loop,
    Loop = new WhileLoopConfig { Condition = condition, MaxIterations = 1, ExitOnMaxIterations = true },
    Body = new[] { Cmd(0, "body") }
  };

  private static SequenceStep BreakLoop(SequenceStepCondition breakCondition) => new() {
    Order = 0,
    StepId = "loop",
    StepType = SequenceStepType.Loop,
    Loop = new CountLoopConfig { Count = 3 },
    Body = new[] {
      Cmd(0, "body"),
      new SequenceStep { Order = 1, StepId = "brk", StepType = SequenceStepType.Break, BreakCondition = breakCondition }
    }
  };

  [Fact]
  public async Task ALoopConditionReadsTheScreen() {
    var (_, run) = await RunAsync(new FakeReader { Text = "0" }, "s", false, WhileLoop(Expect("0")));
    var (_, skipped) = await RunAsync(new FakeReader { Text = "3" }, "s", false, WhileLoop(Expect("0")));

    run.Should().Equal("body");
    skipped.Should().BeEmpty();
  }

  [Fact]
  public async Task ABreakConditionEndsTheLoopWhenTheTextMatches() {
    var (_, broke) = await RunAsync(new FakeReader { Text = "0" }, "s", false, BreakLoop(Expect("0")));
    var (_, full) = await RunAsync(new FakeReader { Text = "3" }, "s", false, BreakLoop(Expect("0")));

    broke.Should().Equal("body");
    full.Should().Equal("body", "body", "body");
  }

  [Theory]
  [InlineData("all", "0", "then")]
  [InlineData("all", "3", "else")]
  [InlineData("any", "0", "then")]
  [InlineData("any", "3", "else")]
  [InlineData("none", "0", "else")]
  [InlineData("none", "3", "then")]
  public async Task ACompositeWithAnOcrTextChildChoosesTheBranch(string rule, string screenText, string expectedBranch) {
    var children = new SequenceStepCondition[] { Expect("0") };
    SequenceStepCondition composite = rule switch {
      "all" => new AllStepCondition { Children = children },
      "any" => new AnyStepCondition { Children = children },
      _ => new NoneStepCondition { Children = children }
    };

    var (_, executed) = await RunAsync(new FakeReader { Text = screenText }, "s", false, If(0, "check", composite));

    executed.Should().Equal(expectedBranch);
  }

  [Fact]
  public async Task APerStepGuardReadsTheScreen() {
    var (_, run) = await RunAsync(new FakeReader { Text = "0" }, "s", false, Cmd(0, "guarded", Expect("0")));
    var (_, skipped) = await RunAsync(new FakeReader { Text = "3" }, "s", false, Cmd(0, "guarded", Expect("0")));

    run.Should().Equal("guarded");
    skipped.Should().BeEmpty();
  }

  [Fact]
  public async Task ARunEntryPointWithASessionPushesTheScope() {
    var reader = new FakeReader { Text = "0" };

    var (_, executed) = await RunAsync(reader, "session-7", dryRun: false, If(0, "check", Expect("0")));

    executed.Should().Equal("then");
    reader.Sessions.Should().Equal("session-7");
  }

  [Theory]
  [InlineData(null, false)]
  [InlineData("", false)]
  [InlineData("session-7", true)]
  public async Task ARunEntryPointWithNoSessionOrADryRunPushesNoScope(string? sessionId, bool dryRun) {
    var reader = new FakeReader { Text = "0" };

    var (result, executed) = await RunAsync(reader, sessionId, dryRun, If(0, "check", Expect("0")));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("else");
    reader.Sessions.Should().BeEmpty();
    result.Steps.First().Message.Should().Contain("reason=no-session");
  }

  [Fact]
  public void NoReaderPushesNoScope() {
    SequenceExecutionService.PushOcrScope(null, "session-7", dryRun: false).Should().BeNull();
  }

  [Fact]
  public async Task ANestedPushUsesItsOwnSessionAndRestoresTheOuterScope() {
    var outer = new FakeReader { Text = "0" };
    var inner = new FakeReader { Text = "0" };

    using (SequenceExecutionService.PushOcrScope(outer, "outer", false)) {
      var (_, _) = await RunAsync(inner, "inner", false, If(0, "check", Expect("0")));
      var (_, executed) = await RunAsync(null, null, false, If(0, "check", Expect("0")));

      // The second run pushes no scope itself, so it uses the outer scope.
      executed.Should().Equal("then");
    }

    inner.Sessions.Should().Equal("inner");
    outer.Sessions.Should().Equal("outer");
    OcrTextEvaluationScope.Current.Should().BeNull();
  }
}

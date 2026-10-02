#pragma warning disable CA2007, CA1861, CA1859, CA1849
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepThroughServiceRig;

namespace GameBot.UnitTests.StepThrough;

/// <summary>Values that the author gives (feature 127, FR-013a, FR-014, FR-017).</summary>
public sealed class StepThroughValuesTests {
  [Fact]
  public async Task ChangingValuesBetweenStepsKeepsTheHistoryAndTheCursor() {
    var rig = new StepThroughServiceRig();
    var sequence = new CommandSequence { Id = "seq-v", Name = "v" };
    sequence.SetSteps(new[] { Cmd(0, "a"), Cmd(1, "b") });
    sequence.Parameters.Add(new ParameterDeclaration { Name = "n", Default = "1" });
    await rig.Sequences.CreateAsync(sequence);
    var id = await rig.StartAsync("seq-v");
    await rig.RunAsync(id);

    var result = rig.Service.SetValues(id, new SetValuesRequest {
      ParameterValues = new Dictionary<string, string> { ["n"] = "8" },
      Outcomes = new Dictionary<string, string?> { ["a"] = "failed" }
    });

    var state = result.Value!;
    state.History.Should().ContainSingle();
    state.Cursor.Should().Be("1");
    state.Parameters.Single().Value.Should().Be("8");
    state.Parameters.Single().IsSet.Should().BeTrue();
    state.Outcomes["a"].Should().Be("failed");
  }

  [Fact]
  public async Task AnOutcomeWithNoValueMakesTheOutcomeNotSet() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("two", Cmd(0, "a"), Cmd(1, "b"));
    var id = await rig.StartAsync(sequenceId);
    await rig.RunAsync(id);
    rig.Service.Get(id).Value!.Outcomes.Should().ContainKey("a");

    var state = rig.Service.SetValues(id, new SetValuesRequest { Outcomes = new Dictionary<string, string?> { ["a"] = null } }).Value!;

    state.Outcomes.Should().NotContainKey("a");
  }

  [Fact]
  public async Task AnOutcomeThatIsNotSetBehavesLikeARealRunWhenAConditionReadsIt() {
    var rig = new StepThroughServiceRig();
    var guarded = Cmd(1, "b");
    guarded.Condition = new CommandOutcomeStepCondition { StepRef = "a", ExpectedState = "success" };
    var sequenceId = await rig.SeedAsync("guarded", Cmd(0, "a"), guarded);
    var id = await rig.StartAsync(sequenceId, startPath: "1");

    var state = await rig.RunAsync(id);

    state.History.Single().Status.Should().Be("Failed");
    rig.Wiring.Executed.Should().BeEmpty();
  }

  [Fact]
  public async Task AnOutcomeThatTheAuthorSetMakesTheGuardPass() {
    var rig = new StepThroughServiceRig();
    var guarded = Cmd(1, "b");
    guarded.Condition = new CommandOutcomeStepCondition { StepRef = "a", ExpectedState = "success" };
    var sequenceId = await rig.SeedAsync("guarded", Cmd(0, "a"), guarded);
    var id = await rig.StartAsync(sequenceId, startPath: "1");
    rig.Service.SetValues(id, new SetValuesRequest { Outcomes = new Dictionary<string, string?> { ["a"] = "success" } }).IsSuccess.Should().BeTrue();

    var state = await rig.RunAsync(id);

    state.History.Single().Status.Should().Be("Succeeded");
    rig.Wiring.Executed.Should().Equal("b");
  }

  [Fact]
  public async Task ValuesAreRefusedWhileAStepRuns() {
    var rig = new StepThroughServiceRig();
    rig.Wiring.Hold = new TaskCompletionSource<bool>();
    var sequenceId = await rig.SeedAsync("slow", Cmd(0, "a"));
    var id = await rig.StartAsync(sequenceId);
    await rig.Service.RunNextAsync(id);

    var result = rig.Service.SetValues(id, new SetValuesRequest());

    result.Error!.Code.Should().Be(StepThroughErrorCodes.StepRunning);
    rig.Service.Cancel(id);
    await rig.Service.RunningTask(id)!;
  }

  [Fact]
  public async Task ValuesForUndeclaredParametersAndUnknownStepsAreRefused() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("one", Cmd(0, "a"));
    var id = await rig.StartAsync(sequenceId);

    rig.Service.SetValues(id, new SetValuesRequest { ParameterValues = new Dictionary<string, string> { ["nope"] = "1" } })
      .Error!.Code.Should().Be(StepThroughErrorCodes.UnknownParameter);
    rig.Service.SetValues(id, new SetValuesRequest { Outcomes = new Dictionary<string, string?> { ["ghost"] = "success" } })
      .Error!.Code.Should().Be(StepThroughErrorCodes.UnknownStep);
  }

  [Fact]
  public async Task AStartAtAMiddleStepLeavesTheOutcomesOfSkippedStepsNotSet() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("two", Cmd(0, "a"), Cmd(1, "b"));

    var id = await rig.StartAsync(sequenceId, startPath: "1");

    rig.Service.Get(id).Value!.Outcomes.Should().NotContainKey("a");
  }
}

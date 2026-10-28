using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 167 (User Story 4): the case of feature 023. A sequence ends the run after a recall, and it fails
/// by name when the recall fails. One condition on the Confirm tap (step 2 of the command) does it, and the
/// command does not change.
/// </summary>
public sealed class CommandStepOutcomeEndRunIntegrationTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private const int ConfirmOrder = 2;

  private static SequenceStep CommandStep(string id, SequenceStepCondition? condition = null) => new() {
    StepId = id,
    CommandId = "cmd-" + id,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-" + id } },
    Condition = condition
  };

  private static CommandStepOutcomeStepCondition ConfirmState(string state) => new() {
    StepRef = "send",
    CommandStepOrder = ConfirmOrder,
    ExpectedState = state
  };

  private static async Task<(SequenceExecutionResult Result, List<string> Ran)> RunAsync(string confirmState, params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "recall", Name = "Recall" };
    sequence.SetSteps(steps.Select((s, i) => { s.Order = i; return s; }).ToArray());
    sequence.InterStepDelayRangeMs = new DelayRangeMs { Min = 0, Max = 0 };
    var runner = new SequenceRunner(new StubRepo(sequence));
    var ran = new List<string>();

    var result = await runner.ExecuteAsync(
      "recall",
      (_, _) => Task.CompletedTask,
      commandDispatcher: (commandId, _) => {
        ran.Add(commandId);
        if (commandId == "cmd-recall-failed") throw new InvalidOperationException("recall failed");
        return Task.FromResult(commandId == "cmd-send"
          ? new CommandDispatchOutcome(true, null, StepStates: new Dictionary<int, string> { [1] = "executed", [ConfirmOrder] = confirmState })
          : CommandDispatchOutcome.Executed);
      },
      ct: CancellationToken.None);
    return (result, ran);
  }

  private static SequenceStep SendLoop() => new() {
    StepId = "loop",
    StepType = SequenceStepType.Loop,
    Loop = new CountLoopConfig { Count = 3 },
    Body = new[] {
      CommandStep("send"),
      new SequenceStep { StepId = "end-run", StepType = SequenceStepType.Break, BreakCondition = ConfirmState("executed") }
    }
  };

  [Fact]
  public async Task WhenTheConfirmTapRanTheBreakStepEndsTheRunAndNoFurtherSendRuns() {
    var (result, ran) = await RunAsync("executed", SendLoop());

    result.Status.Should().Be("Succeeded");
    ran.Count(id => id == "cmd-send").Should().Be(1);
  }

  [Fact]
  public async Task WhenTheConfirmTapWasSkippedTheRunGoesOnToTheNextSend() {
    var (result, ran) = await RunAsync("skipped_detection_failed", SendLoop());

    result.Status.Should().Be("Succeeded");
    ran.Count(id => id == "cmd-send").Should().Be(3);
  }

  [Fact]
  public async Task WhenTheRecallFailsTheRunFailsByTheNameOfTheStep() {
    var (result, ran) = await RunAsync(
      "skipped_detection_failed",
      CommandStep("send"),
      CommandStep("recall-failed", ConfirmState("skipped_detection_failed")),
      CommandStep("after"));

    result.Status.Should().Be("Failed");
    result.Steps.Should().Contain(s => s.StepId == "recall-failed" && s.Status == "Failed" && s.Message!.Contains("recall failed", StringComparison.Ordinal));
    ran.Should().NotContain("cmd-after");
  }

  [Fact]
  public async Task WhenTheRecallRanTheFailureStepIsSkipped() {
    var (result, ran) = await RunAsync(
      "executed",
      CommandStep("send"),
      CommandStep("recall-failed", ConfirmState("skipped_detection_failed")),
      CommandStep("after"));

    result.Status.Should().Be("Succeeded");
    ran.Should().Contain("cmd-after").And.NotContain("cmd-recall-failed");
  }
}

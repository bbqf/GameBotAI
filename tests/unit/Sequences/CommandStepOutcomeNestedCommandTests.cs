using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Service.Services;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 167 (FR-009a): a command with a top-level step of order 2 and a nested command with a step of
/// order 2 and another state. The condition on order 2 reads the top-level state.
/// </summary>
public sealed class CommandStepOutcomeNestedCommandTests {
  private static async Task<bool> EvaluateAsync(IReadOnlyDictionary<int, string>? states, string expected) {
    var map = new Dictionary<string, IReadOnlyDictionary<int, string>>();
    if (states is not null) map["send-1"] = states;
    var condition = new CommandStepOutcomeStepCondition { StepRef = "send-1", CommandStepOrder = 2, ExpectedState = expected };
    var result = await SequenceStepConditionEvaluator.EvaluateAsync(
      condition, null, new Dictionary<string, string>(), null, map, default);
    return result.Value;
  }

  [Fact]
  public async Task TheConditionReadsTheTopLevelStateAndNotTheNestedState() {
    var outcomes = new List<PrimitiveTapStepOutcome> {
      new(2, "executed", null, null, null),
      new(2, "skipped_detection_failed", "detection_failed_after_3_retries", null, null, Depth: 1)
    };

    var states = SequenceExecutionService.ClassifyDispatch(new CommandForceExecutionResult(1, outcomes), CancellationToken.None).StepStates;

    states.Should().NotBeNull();
    states![2].Should().Be("executed");
    (await EvaluateAsync(states, "executed")).Should().BeTrue();
    (await EvaluateAsync(states, "skipped_detection_failed")).Should().BeFalse();
  }

  [Fact]
  public async Task AnOrderThatOnlyANestedCommandHasGivesNoOutcome() {
    var outcomes = new List<PrimitiveTapStepOutcome> {
      new(1, "executed", null, null, null),
      new(2, "executed", null, null, null, Depth: 1)
    };

    var states = SequenceExecutionService.ClassifyDispatch(new CommandForceExecutionResult(1, outcomes), CancellationToken.None).StepStates;

    (await EvaluateAsync(states, "executed")).Should().BeFalse();
  }
}

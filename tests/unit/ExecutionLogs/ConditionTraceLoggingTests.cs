using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class ConditionTraceLoggingTests {
  [Fact]
  public void BuildDetailProjectionIncludesConditionTraceEnvelope() {
    var trace = new ConditionEvaluationTrace(
      true,
      "true",
      null,
      new[] {
        new Dictionary<string, object?> {
          ["operandType"] = "command-outcome",
          ["targetRef"] = "step-a",
          ["result"] = true
        }
      },
      new[] {
        new Dictionary<string, object?> {
          ["operator"] = "and",
          ["result"] = true
        }
      });

    var entry = new ExecutionLogEntry {
      Id = "trace-entry-1",
      ExecutionType = "sequence",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("sequence", "seq-1", "Trace Sequence"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-1", null),
      Hierarchy = new ExecutionHierarchyContext("root-1", null, 0, null),
      Summary = "ok",
      StepOutcomes = new[] {
        new ExecutionStepOutcome(
          1,
          "condition",
          "executed",
          null,
          "Condition evaluated",
          "seq-1",
          "step-condition",
          "Trace Sequence",
          "Condition Step",
          trace)
      },
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

    var projection = ExecutionLogService.BuildDetailProjection(entry);

    projection.StepOutcomes.Should().ContainSingle();
    var step = projection.StepOutcomes[0];
    step.ConditionTrace.Should().NotBeNull();
    step.ConditionTrace!.FinalResult.Should().BeTrue();
    step.ConditionTrace.SelectedBranch.Should().Be("true");
    step.ConditionTrace.OperandResults.Should().ContainSingle();
    step.ConditionTrace.OperatorSteps.Should().ContainSingle();
  }
}

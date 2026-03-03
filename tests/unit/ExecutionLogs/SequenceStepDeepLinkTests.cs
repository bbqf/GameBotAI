using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class SequenceStepDeepLinkTests {
  [Fact]
  public void BuildDetailProjectionMarksDeepLinkResolvedWhenStepIdExists() {
    var entry = new ExecutionLogEntry {
      Id = "deep-link-1",
      ExecutionType = "sequence",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("sequence", "seq-1", "Sequence One"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-1", null),
      Hierarchy = new ExecutionHierarchyContext("root-1", null, 0, null),
      Summary = "ok",
      StepOutcomes = new[] {
        new ExecutionStepOutcome(1, "command", "executed", null, "ok", "seq-1", "step-a", "Sequence One", "Step A")
      },
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

    var projection = ExecutionLogService.BuildDetailProjection(entry);

    projection.StepOutcomes[0].DeepLink.ResolutionStatus.Should().Be("resolved");
    projection.StepOutcomes[0].DeepLink.DirectPath.Should().Be("/authoring/sequences/seq-1?stepId=step-a");
    projection.StepOutcomes[0].DeepLink.FallbackRoute.Should().BeNull();
  }

  [Fact]
  public void BuildDetailProjectionMarksMissingStepWhenStepIdMissing() {
    var entry = new ExecutionLogEntry {
      Id = "deep-link-2",
      ExecutionType = "sequence",
      FinalStatus = "failure",
      ObjectRef = new ExecutionObjectReference("sequence", "seq-2", "Sequence Two"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-2", null),
      Hierarchy = new ExecutionHierarchyContext("root-2", null, 0, null),
      Summary = "failed",
      StepOutcomes = new[] {
        new ExecutionStepOutcome(1, "condition", "failed", "missing_step", "target missing", "seq-2", null, "Sequence Two", "Condition")
      },
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

    var projection = ExecutionLogService.BuildDetailProjection(entry);

    projection.StepOutcomes[0].DeepLink.ResolutionStatus.Should().Be("step_missing");
    projection.StepOutcomes[0].DeepLink.FallbackRoute.Should().Be("/authoring/sequences/seq-2");
  }
}

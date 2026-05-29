using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class SequenceExecutionLogProjectionTests {
  [Fact]
  public void BuildDetailProjectionIncludesCommandNameForCommandBackedSequenceSteps() {
    var entry = new ExecutionLogEntry {
      Id = "projection-command-name",
      ExecutionType = "sequence",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("sequence", "seq-1", "Sequence One"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-1", null),
      Hierarchy = new ExecutionHierarchyContext("projection-command-name", null, 0, null),
      Summary = "Sequence completed.",
      Details = new[] {
        new ExecutionDetailItem(
          "step",
          "Step 'Collect rewards' executed command 'Open Mail'.",
          new Dictionary<string, object?> {
            ["stepOrder"] = 1,
            ["stepType"] = "command",
            ["status"] = "Succeeded",
            ["actionOutcome"] = "executed",
            ["sequenceId"] = "seq-1",
            ["sequenceLabel"] = "Sequence One",
            ["stepId"] = "step-collect",
            ["stepLabel"] = "Collect rewards",
            ["commandName"] = "Open Mail"
          },
          "normal")
      },
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

    var projection = ExecutionLogService.BuildDetailProjection(entry);

    projection.StepOutcomes.Should().ContainSingle();
    projection.StepOutcomes[0].StepLabel.Should().Be("Collect rewards");
    projection.StepOutcomes[0].CommandName.Should().Be("Open Mail");
    projection.StepOutcomes[0].Message.Should().Contain("Collect rewards");
    projection.StepOutcomes[0].Message.Should().Contain("Open Mail");
  }
}
using FluentAssertions;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>
/// Feature 066: the execution-log step-status mapping must render break outcomes correctly —
/// a fired break as a success, a non-firing break as a distinct neutral "no_break" node status
/// (never the red "failure", never "skipped"), and it must keep no_break out of failure counts.
/// </summary>
public sealed class ExecutionLogServiceMapStepStatusTests {
  [Fact] // T006 (US1)
  public void MapsBreakOutcomeToSuccess() {
    ExecutionLogService.MapStepStatus("break").Should().Be("success");
  }

  [Fact] // T006 (US1)
  public void MapsNoBreakOutcomeToNeutralNoBreak() {
    var status = ExecutionLogService.MapStepStatus("no_break");
    status.Should().Be("no_break");
    status.Should().NotBe("failure");
    status.Should().NotBe("skipped");
  }

  [Fact] // T021 (US2) — FR-008: no_break is not a failure and so contributes nothing to failure counts.
  public void NoBreakIsNotCountedAsFailure() {
    // Failure counts/health/alerts key on the "failure" node status; no_break must never map to it.
    ExecutionLogService.MapStepStatus("no_break").Should().NotBe("failure");
  }

  // A while loop whose condition finally goes false, and a count loop that runs every iteration,
  // have each done exactly what they were asked. These runner statuses used to fall through to the
  // failure default, which painted a healthy recovery guard red on every single run.
  [Theory]
  [InlineData("false")]
  [InlineData("Succeeded")]
  [InlineData("true")]
  public void MapsNormalLoopExitsToSuccess(string outcome) {
    ExecutionLogService.MapStepStatus(outcome).Should().Be("success");
  }

  [Fact]
  public void MapsFailedLoopToFailure() {
    ExecutionLogService.MapStepStatus("Failed").Should().Be("failure");
  }

  [Fact]
  public void MapsExhaustedLoopToNotExecuted() {
    // The loop opted out of failing at its ceiling: the body ran, the goal was not reached, and
    // the sequence carried on. That is neither a success nor a failure.
    var status = ExecutionLogService.MapStepStatus("exhausted");
    status.Should().Be("not_executed");
    status.Should().NotBe("failure");
  }

  // Feature 065: a self-reschedule that booked the next firing is the step working, and one that
  // declined because its queue run had ended is a no-op — neither is a failure.
  [Fact]
  public void MapsSelfRescheduleOutcomes() {
    ExecutionLogService.MapStepStatus("scheduled").Should().Be("success");
    ExecutionLogService.MapStepStatus("noop").Should().Be("skipped");
  }
}

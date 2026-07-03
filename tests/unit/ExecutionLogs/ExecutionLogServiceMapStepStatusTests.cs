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
}

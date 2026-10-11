using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ci;

public class GateEvaluationTests {
  private static readonly string[] DotnetJobs = ["compile", "analyze", "test-unit", "test-integration", "test-contract"];

  private static int Evaluate(string dotnet, string webui, IDictionary<string, string> results) {
    var needs = results.ToDictionary(r => r.Key, r => new { result = r.Value });
    var json = JsonSerializer.Serialize(needs);
    return CiScriptRunner.RunWithExitCode("evaluate-gate.ps1", "-NeedsJson", json, "-Dotnet", dotnet, "-Webui", webui).ExitCode;
  }

  private static Dictionary<string, string> AllJobs(string dotnetResult, string webuiResult) {
    var results = new Dictionary<string, string> { ["changes"] = "success", ["web-ui-tests"] = webuiResult };
    foreach (var job in DotnetJobs) {
      results[job] = dotnetResult;
    }

    return results;
  }

  [Fact]
  public void EveryJobPassingPasses() {
    Evaluate("true", "true", AllJobs("success", "success")).Should().Be(0);
  }

  [Fact]
  public void JobsSkippedByAPathRulePass() {
    Evaluate("false", "false", AllJobs("skipped", "skipped")).Should().Be(0);
  }

  [Fact]
  public void WebUiOnlyPushPassesWhenTheDotnetJobsAreSkipped() {
    Evaluate("false", "true", AllJobs("skipped", "success")).Should().Be(0);
  }

  [Theory]
  [InlineData("failure")]
  [InlineData("cancelled")]
  public void AFailedOrCancelledJobFails(string result) {
    var results = AllJobs("success", "success");
    results["test-contract"] = result;
    Evaluate("true", "true", results).Should().Be(1);
  }

  [Fact]
  public void AnAnalysisFailureFailsTheGate() {
    var results = AllJobs("success", "success");
    results["analyze"] = "failure";
    Evaluate("true", "true", results).Should().Be(1);
  }

  [Fact]
  public void AJobSkippedWhileItsSwitchIsTrueFails() {
    var results = AllJobs("success", "success");
    results["test-unit"] = "skipped";
    Evaluate("true", "true", results).Should().Be(1);
  }

  [Fact]
  public void AFailedChangesJobFailsEvenWhenAllOtherJobsAreSkipped() {
    var results = AllJobs("skipped", "skipped");
    results["changes"] = "failure";
    Evaluate("", "", results).Should().Be(1);
  }

  [Fact]
  public void AMissingNeededJobFails() {
    var results = AllJobs("success", "success");
    results.Remove("compile");
    Evaluate("true", "true", results).Should().Be(1);
  }
}

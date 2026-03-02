using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionStepOutcomeIntegrationTests {
  [Fact]
  public async Task CommandLogNormalizesStepOutcomesToExecutedAndNotExecuted() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();

    var logService = app.Services.GetRequiredService<IExecutionLogService>();

    var primitiveOutcomes = new[]
    {
      new PrimitiveTapStepOutcome(1, "executed", null, new PrimitiveTapResolvedPoint(100, 120), 0.92),
      new PrimitiveTapStepOutcome(2, "skipped_detection_failed", "threshold_not_met", null, 0.41)
    };

    await logService.LogCommandExecutionAsync(
      "cmd-step-outcomes",
      "Step Outcomes Command",
      "failure",
      primitiveOutcomes,
      new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

    var item = (await logService.QueryAsync(new ExecutionLogQuery {
      ObjectType = "command",
      ObjectId = "cmd-step-outcomes",
      PageSize = 1
    }).ConfigureAwait(false)).Items.Single();

    item.StepOutcomes.Should().HaveCount(2);
    item.StepOutcomes[0].Outcome.Should().Be("executed");
    item.StepOutcomes[1].Outcome.Should().Be("not_executed");
    item.StepOutcomes[1].ReasonText.Should().Be("threshold_not_met");
  }
}

using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ConditionalSequenceStepLoggingIntegrationTests {
  [Fact]
  public async Task DetailEndpointReturnsEnrichedStepContextAndConditionTrace() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();

    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

      var repository = app.Services.GetRequiredService<IExecutionLogRepository>();
      var trace = new ConditionEvaluationTrace(
        true,
        "true",
        null,
        new[] { new Dictionary<string, object?> { ["targetRef"] = "cmd-1", ["result"] = true } },
        new[] { new Dictionary<string, object?> { ["operator"] = "and", ["result"] = true } });

      var entry = new ExecutionLogEntry {
        Id = "exe-us3-001",
        TimestampUtc = DateTimeOffset.UtcNow,
        ExecutionType = "sequence",
        FinalStatus = "success",
        ObjectRef = new ExecutionObjectReference("sequence", "seq-us3", "US3 Sequence"),
        Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-us3", null),
        Hierarchy = new ExecutionHierarchyContext("exe-us3-001", null, 0, null),
        Summary = "Conditional sequence executed.",
        StepOutcomes = new[] {
          new ExecutionStepOutcome(1, "condition", "executed", null, "Condition true", "seq-us3", "step-condition", "US3 Sequence", "Check condition", trace)
        },
        RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
      };

      await repository.AddAsync(entry).ConfigureAwait(false);

      var response = await client.GetAsync(new Uri($"/api/execution-logs/{entry.Id}", UriKind.Relative)).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();

      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
      var stepOutcomes = doc.RootElement.GetProperty("stepOutcomes");
      stepOutcomes.GetArrayLength().Should().Be(1);
      var step = stepOutcomes[0];

      step.GetProperty("sequenceId").GetString().Should().Be("seq-us3");
      step.GetProperty("stepId").GetString().Should().Be("step-condition");
      step.GetProperty("sequenceLabel").GetString().Should().Be("US3 Sequence");
      step.GetProperty("stepLabel").GetString().Should().Be("Check condition");
      step.GetProperty("deepLink").GetProperty("resolutionStatus").GetString().Should().Be("resolved");
      step.GetProperty("conditionTrace").GetProperty("selectedBranch").GetString().Should().Be("true");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}

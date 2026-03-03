using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.ExecutionLogs;

public sealed class ConditionalExecutionLogsContractTests : IDisposable {
  private readonly string? _prevToken;

  public ConditionalExecutionLogsContractTests() {
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task DetailEndpointExposesDeepLinkAndConditionTraceContracts() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var repository = app.Services.GetRequiredService<IExecutionLogRepository>();
    var entry = new ExecutionLogEntry {
      Id = "contract-us3-1",
      TimestampUtc = DateTimeOffset.UtcNow,
      ExecutionType = "sequence",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("sequence", "seq-contract-us3", "Contract Sequence"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-contract-us3", null),
      Hierarchy = new ExecutionHierarchyContext("contract-us3-1", null, 0, null),
      Summary = "ok",
      StepOutcomes = new[] {
        new ExecutionStepOutcome(
          1,
          "condition",
          "executed",
          null,
          "Condition true",
          "seq-contract-us3",
          "step-1",
          "Contract Sequence",
          "Step 1",
          new ConditionEvaluationTrace(true, "true", null, Array.Empty<Dictionary<string, object?>>(), Array.Empty<Dictionary<string, object?>>()))
      },
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
    };

    await repository.AddAsync(entry).ConfigureAwait(false);

    var response = await client.GetAsync(new Uri($"/api/execution-logs/{entry.Id}", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var step = doc.RootElement.GetProperty("stepOutcomes")[0];

    step.TryGetProperty("deepLink", out var deepLink).Should().BeTrue();
    deepLink.TryGetProperty("sequenceId", out _).Should().BeTrue();
    deepLink.TryGetProperty("stepId", out _).Should().BeTrue();
    deepLink.TryGetProperty("resolutionStatus", out _).Should().BeTrue();

    step.TryGetProperty("conditionTrace", out var trace).Should().BeTrue();
    trace.TryGetProperty("finalResult", out _).Should().BeTrue();
    trace.TryGetProperty("selectedBranch", out _).Should().BeTrue();
    trace.TryGetProperty("operandResults", out _).Should().BeTrue();
    trace.TryGetProperty("operatorSteps", out _).Should().BeTrue();
  }
}

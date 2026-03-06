using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class DeterministicSequenceOutcomeIntegrationTests {
  [Fact]
  public async Task RepeatedRunsProduceDeterministicConditionAndActionOutcomeTuples() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "deterministic-outcomes",
      version = 1,
      entryStepId = "start",
      steps = new object[] {
        new { stepId = "start", label = "Start", stepType = "command", payloadRef = "cmd-1" },
        new {
          stepId = "decision",
          label = "Decision",
          stepType = "condition",
          condition = new {
            nodeType = "operand",
            operand = new { operandType = "command-outcome", targetRef = "cmd-1", expectedState = "success" }
          }
        },
        new { stepId = "then", label = "Then", stepType = "action", payloadRef = "tap:{\"x\":12,\"y\":34}" },
        new { stepId = "end", label = "End", stepType = "terminal" }
      },
      links = new object[] {
        new { linkId = "n1", sourceStepId = "start", targetStepId = "decision", branchType = "next" },
        new { linkId = "t1", sourceStepId = "decision", targetStepId = "then", branchType = "true" },
        new { linkId = "f1", sourceStepId = "decision", targetStepId = "end", branchType = "false" },
        new { linkId = "n2", sourceStepId = "then", targetStepId = "end", branchType = "next" }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    for (var index = 0; index < 2; index++) {
      var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
      executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    var listResponse = await client.GetAsync(new Uri($"/api/execution-logs?objectType=sequence&objectId={sequenceId}&pageSize=10", UriKind.Relative)).ConfigureAwait(false);
    listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var executionIds = listPayload
      .GetProperty("items")
      .EnumerateArray()
      .Where(item => string.Equals(item.GetProperty("executionType").GetString(), "sequence", StringComparison.OrdinalIgnoreCase))
      .Select(item => item.GetProperty("id").GetString())
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Take(2)
      .ToArray();

    executionIds.Length.Should().BeGreaterOrEqualTo(2);

    var firstRun = await GetOutcomeTuplesAsync(client, executionIds[0]!).ConfigureAwait(false);
    var secondRun = await GetOutcomeTuplesAsync(client, executionIds[1]!).ConfigureAwait(false);

    firstRun.Should().NotBeEmpty();
    firstRun.Should().Equal(secondRun);
  }

  private static async Task<IReadOnlyList<string>> GetOutcomeTuplesAsync(HttpClient client, string executionId) {
    var detailResponse = await client.GetAsync(new Uri($"/api/execution-logs/{executionId}", UriKind.Relative)).ConfigureAwait(false);
    detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var detailPayload = await detailResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var tuples = new List<string>();
    foreach (var step in detailPayload.GetProperty("stepOutcomes").EnumerateArray()) {
      var status = step.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "unknown" : "unknown";
      var conditionResult = "none";
      if (step.TryGetProperty("conditionTrace", out var traceProp)
          && traceProp.ValueKind == JsonValueKind.Object
          && traceProp.TryGetProperty("finalResult", out var resultProp)
          && resultProp.ValueKind is JsonValueKind.True or JsonValueKind.False) {
        conditionResult = resultProp.GetBoolean() ? "true" : "false";
      }

      tuples.Add($"{conditionResult}|{status}");
    }

    return tuples;
  }
}

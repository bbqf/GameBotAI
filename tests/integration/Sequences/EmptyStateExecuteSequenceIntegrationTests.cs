using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class EmptyStateExecuteSequenceIntegrationTests {
  [Fact]
  public async Task FirstSequenceExecuteSucceedsFromEmptyRepository() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "empty-state-first-execute",
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
        new { stepId = "then", label = "Then", stepType = "action", payloadRef = "tap:{\"x\":50,\"y\":90}" },
        new { stepId = "end", label = "End", stepType = "terminal" }
      },
      links = new object[] {
        new { linkId = "l1", sourceStepId = "start", targetStepId = "decision", branchType = "next" },
        new { linkId = "l2", sourceStepId = "decision", targetStepId = "then", branchType = "true" },
        new { linkId = "l3", sourceStepId = "decision", targetStepId = "end", branchType = "false" },
        new { linkId = "l4", sourceStepId = "then", targetStepId = "end", branchType = "next" }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    execution.GetProperty("steps").GetArrayLength().Should().Be(2);
    execution.GetProperty("conditionTraces").GetArrayLength().Should().Be(1);
    execution.GetProperty("conditionTraces")[0].GetProperty("trace").GetProperty("finalResult").GetBoolean().Should().BeTrue();
  }
}

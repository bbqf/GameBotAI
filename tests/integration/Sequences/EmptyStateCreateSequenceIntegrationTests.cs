using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class EmptyStateCreateSequenceIntegrationTests {
  private static readonly string[] ExpectedFirstSequenceStepIds = { "start", "decision", "action-then", "end" };

  [Fact]
  public async Task FirstSequenceCreateAndSaveSucceedsFromEmptyRepository() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var initialListResponse = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    initialListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var initialList = await initialListResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    initialList.ValueKind.Should().Be(JsonValueKind.Array);
    initialList.GetArrayLength().Should().Be(0);

    var createPayload = new {
      name = "empty-state-first-sequence",
      version = 0,
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
        new { stepId = "action-then", label = "Then", stepType = "action", payloadRef = "tap:{\"x\":100,\"y\":200}" },
        new { stepId = "end", label = "End", stepType = "terminal" }
      },
      links = new object[] {
        new { linkId = "n1", sourceStepId = "start", targetStepId = "decision", branchType = "next" },
        new { linkId = "t1", sourceStepId = "decision", targetStepId = "action-then", branchType = "true" },
        new { linkId = "f1", sourceStepId = "decision", targetStepId = "end", branchType = "false" },
        new { linkId = "n2", sourceStepId = "action-then", targetStepId = "end", branchType = "next" }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();
    created.GetProperty("version").GetInt32().Should().Be(1);

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    fetched.GetProperty("entryStepId").GetString().Should().Be("start");
    fetched.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("stepId").GetString()).Should().Contain(ExpectedFirstSequenceStepIds);
  }
}

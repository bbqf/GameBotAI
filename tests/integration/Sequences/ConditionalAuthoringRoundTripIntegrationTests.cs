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

public sealed class ConditionalAuthoringRoundTripIntegrationTests {
  private static readonly string[] ExpectedStepIds = { "start", "decision", "then-step", "else-step" };
  private static readonly string[] ExpectedBranchTypes = { "next", "true", "false" };

  [Fact]
  public async Task CreateAndGetSequencePreservesConditionalFlowShape() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "round-trip-flow",
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
        new { stepId = "then-step", label = "Then", stepType = "action", payloadRef = "action-1" },
        new { stepId = "else-step", label = "Else", stepType = "action", payloadRef = "action-2" }
      },
      links = new object[] {
        new { linkId = "l1", sourceStepId = "start", targetStepId = "decision", branchType = "next" },
        new { linkId = "l2", sourceStepId = "decision", targetStepId = "then-step", branchType = "true" },
        new { linkId = "l3", sourceStepId = "decision", targetStepId = "else-step", branchType = "false" }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    created.TryGetProperty("id", out var idProp).Should().BeTrue();
    var sequenceId = idProp.GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    fetched.GetProperty("entryStepId").GetString().Should().Be("start");

    var fetchedSteps = fetched.GetProperty("steps").EnumerateArray().Select(x => x.GetProperty("stepId").GetString()).ToArray();
    fetchedSteps.Should().Contain(ExpectedStepIds);

    var fetchedLinks = fetched.GetProperty("links").EnumerateArray().Select(x => x.GetProperty("branchType").GetString()).ToArray();
    fetchedLinks.Should().Contain(ExpectedBranchTypes);
  }
}

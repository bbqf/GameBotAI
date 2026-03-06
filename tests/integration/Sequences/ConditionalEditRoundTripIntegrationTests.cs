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

public sealed class ConditionalEditRoundTripIntegrationTests {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  [Fact]
  public async Task PatchConditionalFlowPersistsEditedOperandAndBranchesAfterReload() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var uploadA = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "image-a", data = OneByOnePngBase64 }).ConfigureAwait(false);
    uploadA.StatusCode.Should().Be(HttpStatusCode.Created);
    var uploadB = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "image-b", data = OneByOnePngBase64 }).ConfigureAwait(false);
    uploadB.StatusCode.Should().Be(HttpStatusCode.Created);

    var createPayload = new {
      name = "conditional-edit-sequence",
      version = 1,
      entryStepId = "cmd-1",
      steps = new object[] {
        new { stepId = "cmd-1", label = "Command One", stepType = "command", payloadRef = "cmd-1" },
        new {
          stepId = "cond-1",
          label = "Condition One",
          stepType = "condition",
          condition = new {
            nodeType = "operand",
            operand = new { operandType = "image-detection", targetRef = "image-a", expectedState = "present", threshold = 0.80 }
          }
        },
        new { stepId = "cmd-2", label = "Command Two", stepType = "command", payloadRef = "cmd-2" }
      },
      links = new object[] {
        new { linkId = "n1", sourceStepId = "cmd-1", targetStepId = "cond-1", branchType = "next" },
        new { linkId = "t1", sourceStepId = "cond-1", targetStepId = "cmd-2", branchType = "true" },
        new { linkId = "f1", sourceStepId = "cond-1", targetStepId = "cmd-1", branchType = "false" }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var patchPayload = new {
      name = "conditional-edit-sequence",
      version = 1,
      entryStepId = "cmd-1",
      steps = new object[] {
        new { stepId = "cmd-1", label = "Command One", stepType = "command", payloadRef = "cmd-1" },
        new {
          stepId = "cond-1",
          label = "Condition One",
          stepType = "condition",
          condition = new {
            nodeType = "operand",
            operand = new { operandType = "image-detection", targetRef = "image-b", expectedState = "absent", threshold = 0.95 }
          }
        },
        new { stepId = "cmd-2", label = "Command Two", stepType = "command", payloadRef = "cmd-2" }
      },
      links = new object[] {
        new { linkId = "n1", sourceStepId = "cmd-1", targetStepId = "cond-1", branchType = "next" },
        new { linkId = "t1", sourceStepId = "cond-1", targetStepId = "cmd-1", branchType = "true" },
        new { linkId = "f1", sourceStepId = "cond-1", targetStepId = "cmd-2", branchType = "false" }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var conditionStep = fetched.GetProperty("steps").EnumerateArray().First(step => step.GetProperty("stepId").GetString() == "cond-1");
    var operand = conditionStep.GetProperty("condition").GetProperty("operand");
    operand.GetProperty("operandType").GetString().Should().Be("image-detection");
    operand.GetProperty("targetRef").GetString().Should().Be("image-b");
    operand.GetProperty("expectedState").GetString().Should().Be("absent");
    operand.GetProperty("threshold").GetDouble().Should().Be(0.95);

    var trueBranch = fetched.GetProperty("links").EnumerateArray().First(link =>
      link.GetProperty("sourceStepId").GetString() == "cond-1" && link.GetProperty("branchType").GetString() == "true");
    trueBranch.GetProperty("targetStepId").GetString().Should().Be("cmd-1");
  }
}

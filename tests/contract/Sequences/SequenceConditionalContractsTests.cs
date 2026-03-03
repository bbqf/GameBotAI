using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequenceConditionalContractsTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task CreateAndPatchSequenceEndpointsAcceptRequests() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createPayload = new {
      name = "contract-sequence",
      steps = new[] { "cmd-1", "cmd-2" }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var patchPayload = new {
      name = "contract-sequence-updated",
      steps = new[] { "cmd-2" }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task ValidateEndpointReturnsOkForValidConditionalFlow() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "validate-valid",
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
        new { stepId = "true-step", label = "True", stepType = "action", payloadRef = "action-1" },
        new { stepId = "false-step", label = "False", stepType = "action", payloadRef = "action-2" }
      },
      links = new object[] {
        new { linkId = "l1", sourceStepId = "start", targetStepId = "decision", branchType = "next" },
        new { linkId = "l2", sourceStepId = "decision", targetStepId = "true-step", branchType = "true" },
        new { linkId = "l3", sourceStepId = "decision", targetStepId = "false-step", branchType = "false" }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences/validate-seq/validate", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var result = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    result.GetProperty("valid").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task ValidateEndpointReturnsBadRequestForInvalidConditionalFlow() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "validate-invalid",
      version = 1,
      entryStepId = "decision",
      steps = new object[] {
        new {
          stepId = "decision",
          label = "Decision",
          stepType = "condition",
          condition = new {
            nodeType = "operand",
            operand = new { operandType = "command-outcome", targetRef = "cmd-1", expectedState = "success" }
          }
        },
        new { stepId = "true-step", label = "True", stepType = "action", payloadRef = "action-1" }
      },
      links = new object[] {
        new { linkId = "l1", sourceStepId = "decision", targetStepId = "true-step", branchType = "true" }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences/validate-seq-invalid/validate", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var result = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    result.GetProperty("valid").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task ExecuteEndpointReturnsResultPayloadForExistingSequence() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createPayload = new {
      name = "exec-sequence",
      steps = new[] { "cmd-1" }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var executeResponse = await client.PostAsync(new Uri($"/api/sequences/{sequenceId}/execute", UriKind.Relative), content: null).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    execution.TryGetProperty("status", out _).Should().BeTrue();
  }

  [Fact]
  public async Task PatchSequenceReturnsConflictForStaleVersion() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createPayload = new {
      name = "stale-sequence",
      steps = new[] { "cmd-1" }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var firstPatch = new {
      name = "stale-sequence-updated",
      version = 1,
      steps = new[] { "cmd-1" }
    };
    var firstPatchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", firstPatch).ConfigureAwait(false);
    firstPatchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var stalePatch = new {
      name = "stale-sequence-stale-write",
      version = 1,
      steps = new[] { "cmd-1" }
    };

    var stalePatchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", stalePatch).ConfigureAwait(false);
    stalePatchResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var conflict = await stalePatchResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    conflict.GetProperty("sequenceId").GetString().Should().Be(sequenceId);
    conflict.GetProperty("currentVersion").GetInt32().Should().BeGreaterThan(1);
  }
}
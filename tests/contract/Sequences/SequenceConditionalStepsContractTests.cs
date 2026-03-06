using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequenceConditionalStepsContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task CreateSequenceFlowRejectsStepWithoutStepType() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "missing-step-type",
      version = 1,
      entryStepId = "start",
      steps = new object[] {
        new { stepId = "start", label = "Start", payloadRef = "cmd-1" },
        new {
          stepId = "decision",
          label = "Decision",
          stepType = "condition",
          condition = new {
            nodeType = "operand",
            operand = new { operandType = "command-outcome", targetRef = "cmd-1", expectedState = "success" }
          }
        },
        new { stepId = "end", label = "End", stepType = "terminal" }
      },
      links = new object[] {
        new { linkId = "l1", sourceStepId = "start", targetStepId = "decision", branchType = "next" },
        new { linkId = "l2", sourceStepId = "decision", targetStepId = "end", branchType = "true" },
        new { linkId = "l3", sourceStepId = "decision", targetStepId = "end", branchType = "false" }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().Contain("stepType");
  }

  [Fact]
  public async Task CreateSequenceFlowReturnsConditionalStepSchema() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "schema-roundtrip",
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
        new { stepId = "next", label = "Next", stepType = "command", payloadRef = "cmd-2" },
        new { stepId = "end", label = "End", stepType = "terminal" }
      },
      links = new object[] {
        new { linkId = "l1", sourceStepId = "start", targetStepId = "decision", branchType = "next" },
        new { linkId = "l2", sourceStepId = "decision", targetStepId = "next", branchType = "true" },
        new { linkId = "l3", sourceStepId = "decision", targetStepId = "end", branchType = "false" },
        new { linkId = "l4", sourceStepId = "next", targetStepId = "end", branchType = "next" }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var decisionStep = created
      .GetProperty("steps")
      .EnumerateArray()
      .FirstOrDefault(step => string.Equals(step.GetProperty("stepId").GetString(), "decision", StringComparison.Ordinal));

    decisionStep.ValueKind.Should().NotBe(JsonValueKind.Undefined);
    decisionStep.GetProperty("stepType").GetString().Should().Be("condition");
    decisionStep.TryGetProperty("condition", out var condition).Should().BeTrue();
    condition.ValueKind.Should().NotBe(JsonValueKind.Undefined);
  }

  [Fact]
  public async Task CreateSequenceFlowRejectsMissingImageReference() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var missingImageId = $"missing-{Guid.NewGuid():N}";
    var payload = new {
      name = "missing-image-ref",
      version = 1,
      entryStepId = "start",
      steps = new object[] {
        new {
          stepId = "start",
          label = "Condition",
          stepType = "condition",
          condition = new {
            nodeType = "operand",
            operand = new { operandType = "image-detection", targetRef = missingImageId, expectedState = "present", threshold = 0.8 }
          }
        },
        new { stepId = "end", label = "End", stepType = "terminal" }
      },
      links = new object[] {
        new { linkId = "l1", sourceStepId = "start", targetStepId = "end", branchType = "true" },
        new { linkId = "l2", sourceStepId = "start", targetStepId = "end", branchType = "false" }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    body.Should().Contain("does not exist");
    body.Should().Contain(missingImageId);
  }
}

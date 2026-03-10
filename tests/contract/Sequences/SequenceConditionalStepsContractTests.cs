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
  public async Task CreateSequenceFlowRejectsForwardCommandOutcomeReference() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "invalid-forward-reference",
      version = 1,
      steps = new object[] {
        new {
          stepId = "start",
          label = "Start",
          action = new {
            type = "tap",
            parameters = new { x = 50, y = 50 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "next",
            expectedState = "success"
          }
        },
        new {
          stepId = "next",
          label = "Next",
          action = new {
            type = "tap",
            parameters = new { x = 120, y = 840 }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().MatchRegex("(?i)prior step|earlier step|must refer");
  }

  [Fact]
  public async Task CreateSequenceFlowReturnsConditionalStepSchema() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "schema-roundtrip",
      version = 1,
      steps = new object[] {
        new {
          stepId = "start",
          label = "Start",
          action = new {
            type = "tap",
            parameters = new { x = 100, y = 200 }
          }
        },
        new {
          stepId = "next",
          label = "Next",
          action = new {
            type = "tap",
            parameters = new { x = 300, y = 400 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "start",
            expectedState = "success"
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var decisionStep = created
      .GetProperty("steps")
      .EnumerateArray()
      .FirstOrDefault(step => string.Equals(step.GetProperty("stepId").GetString(), "next", StringComparison.Ordinal));

    decisionStep.ValueKind.Should().NotBe(JsonValueKind.Undefined);
    decisionStep.GetProperty("condition").GetProperty("type").GetString().Should().Be("commandOutcome");
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
      steps = new object[] {
        new {
          stepId = "start",
          label = "Condition",
          action = new {
            type = "tap",
            parameters = new { x = 80, y = 80 }
          },
          condition = new {
            type = "imageVisible",
            imageId = missingImageId,
            minSimilarity = 0.8
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    body.Should().Contain("does not exist");
    body.Should().Contain(missingImageId);
  }
}

using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Contract tests for if steps in sequences (feature 067): upsert → GET round-trip of
/// condition and branches, null-vs-empty else preservation, and validation errors.
/// </summary>
public sealed class IfStepContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static object IfStepPayload(object? elseBody = null) => new {
    stepId = "if-popup",
    label = "Dismiss popup when present",
    stepType = "If",
    @if = new {
      condition = new {
        type = "imageVisible",
        imageId = "img-popup",
        minSimilarity = 0.85,
        negate = false
      }
    },
    body = new object[] {
      new {
        stepId = "dismiss",
        stepType = "Action",
        primitiveAction = new {
          type = "command",
          schemaVersion = "v1",
          payload = new { commandId = "cmd-close" }
        }
      }
    },
    elseBody
  };

  [Fact]
  public async Task CreateAndGetSequencePreserveIfStepBranches() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createPayload = new {
      name = "if-step-contract",
      version = 1,
      steps = new object[] {
        IfStepPayload(elseBody: new object[] {
          new {
            stepId = "proceed",
            stepType = "Action",
            primitiveAction = new {
              type = "command",
              schemaVersion = "v1",
              payload = new { commandId = "cmd-continue" }
            }
          }
        })
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var step = fetched.GetProperty("steps")[0];
    step.GetProperty("stepType").GetString().Should().Be("If");
    step.GetProperty("if").GetProperty("condition").GetProperty("type").GetString().Should().Be("imageVisible");
    step.GetProperty("if").GetProperty("condition").GetProperty("imageId").GetString().Should().Be("img-popup");
    step.GetProperty("body")[0].GetProperty("stepId").GetString().Should().Be("dismiss");
    step.GetProperty("elseBody")[0].GetProperty("stepId").GetString().Should().Be("proceed");
  }

  [Fact]
  public async Task AbsentElseBodyStaysAbsentWhileEmptyElseBodyStaysEmpty() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Absent else
    var withoutElse = new { name = "if-no-else", version = 1, steps = new object[] { IfStepPayload() } };
    var createdWithoutElse = await (await client.PostAsJsonAsync("/api/sequences", withoutElse).ConfigureAwait(false))
      .Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var noElseStep = createdWithoutElse.GetProperty("steps")[0];
    var hasElse = noElseStep.TryGetProperty("elseBody", out var absentElse) && absentElse.ValueKind != JsonValueKind.Null;
    hasElse.Should().BeFalse();

    // Empty else
    var withEmptyElse = new { name = "if-empty-else", version = 1, steps = new object[] { IfStepPayload(elseBody: Array.Empty<object>()) } };
    var createdWithEmptyElse = await (await client.PostAsJsonAsync("/api/sequences", withEmptyElse).ConfigureAwait(false))
      .Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var emptyElseStep = createdWithEmptyElse.GetProperty("steps")[0];
    emptyElseStep.TryGetProperty("elseBody", out var emptyElse).Should().BeTrue();
    emptyElse.ValueKind.Should().Be(JsonValueKind.Array);
    emptyElse.GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task CreateSequenceRejectsIfStepWithoutConfiguration() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "if-missing-config",
      version = 1,
      steps = new object[] {
        new { stepId = "if1", stepType = "If", body = Array.Empty<object>() }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().MatchRegex("(?i)requires an if configuration");
  }

  [Fact]
  public async Task CreateSequenceRejectsNestedIfInsideBranch() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "if-nested-if",
      version = 1,
      steps = new object[] {
        new {
          stepId = "if1",
          stepType = "If",
          @if = new { condition = new { type = "imageVisible", imageId = "img-1" } },
          body = new object[] {
            new {
              stepId = "if2",
              stepType = "If",
              @if = new { condition = new { type = "imageVisible", imageId = "img-2" } },
              body = Array.Empty<object>()
            }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().MatchRegex("(?i)must not itself be an if step");
  }

  [Fact]
  public async Task CreateSequenceRejectsBreakInsideTopLevelIfBranch() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "if-top-level-break",
      version = 1,
      steps = new object[] {
        new {
          stepId = "if1",
          stepType = "If",
          @if = new { condition = new { type = "imageVisible", imageId = "img-1" } },
          body = new object[] {
            new { stepId = "brk", stepType = "Break" }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().MatchRegex("(?i)only valid inside a loop body");
  }
}

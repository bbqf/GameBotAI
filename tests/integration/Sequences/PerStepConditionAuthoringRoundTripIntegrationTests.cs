using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class PerStepConditionAuthoringRoundTripIntegrationTests {
  [Fact]
  public async Task MixedConditionalAndUnconditionalStepsRoundTripWithoutConditionLoss() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "roundtrip-per-step",
      version = 1,
      steps = new object[] {
        new {
          stepId = "go-home",
          label = "Go Home",
          action = new {
            type = "tap",
            parameters = new { x = 120, y = 840 }
          }
        },
        new {
          stepId = "go-back",
          label = "Go Back",
          action = new {
            type = "tap",
            parameters = new { x = 64, y = 64 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "go-home",
            expectedState = "success"
          }
        }
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
    var currentVersion = fetched.GetProperty("version").GetInt32();

    var patchPayload = new {
      name = "roundtrip-per-step",
      version = currentVersion,
      steps = new object[] {
        new {
          stepId = "go-home",
          label = "Go Home",
          action = new {
            type = "tap",
            parameters = new { x = 120, y = 840 }
          }
        },
        new {
          stepId = "go-back",
          label = "Go Back",
          action = new {
            type = "tap",
            parameters = new { x = 64, y = 64 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "go-home",
            expectedState = "skipped"
          }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var reloadResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    reloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var reloaded = await reloadResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    reloaded.GetProperty("steps")[0].TryGetProperty("condition", out var firstCondition).Should().BeTrue();
    firstCondition.ValueKind.Should().Be(JsonValueKind.Null);
    reloaded.GetProperty("steps")[1].GetProperty("condition").GetProperty("expectedState").GetString().Should().Be("skipped");
  }
}

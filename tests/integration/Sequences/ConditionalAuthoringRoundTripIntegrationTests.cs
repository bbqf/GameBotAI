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
          stepId = "decision",
          label = "Decision",
          action = new {
            type = "tap",
            parameters = new { x = 180, y = 260 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "start",
            expectedState = "success"
          }
        },
        new {
          stepId = "then-step",
          label = "Then",
          action = new {
            type = "tap",
            parameters = new { x = 280, y = 360 }
          }
        },
        new {
          stepId = "else-step",
          label = "Else",
          action = new {
            type = "tap",
            parameters = new { x = 380, y = 460 }
          }
        }
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

    var fetchedSteps = fetched.GetProperty("steps").EnumerateArray().Select(x => x.GetProperty("stepId").GetString()).ToArray();
    fetchedSteps.Should().Contain(ExpectedStepIds);

    var decisionStep = fetched.GetProperty("steps").EnumerateArray().First(x => x.GetProperty("stepId").GetString() == "decision");
    decisionStep.GetProperty("condition").GetProperty("type").GetString().Should().Be("commandOutcome");
  }
}

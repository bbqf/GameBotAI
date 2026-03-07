using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class PerStepConditionEmptyStateCreateIntegrationTests {
  [Fact]
  public async Task FirstSequenceCreateSupportsPerStepConditionPayload() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var initialListResponse = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    initialListResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var createPayload = new {
      name = "first-per-step-sequence",
      version = 0,
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
    created.GetProperty("version").GetInt32().Should().Be(1);
    created.GetProperty("steps")[0].GetProperty("stepId").GetString().Should().Be("go-home");
    created.GetProperty("steps")[1].GetProperty("condition").GetProperty("type").GetString().Should().Be("commandOutcome");
  }
}

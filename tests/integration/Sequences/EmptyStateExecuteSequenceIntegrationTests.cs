using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class EmptyStateExecuteSequenceIntegrationTests {
  [Fact]
  public async Task FirstSequenceExecuteSucceedsFromEmptyRepository() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "empty-state-first-execute",
      version = 1,
      steps = new object[] {
        new {
          stepId = "start",
          label = "Start",
          action = new {
            type = "tap",
            parameters = new { x = 20, y = 20 }
          }
        },
        new {
          stepId = "then",
          label = "Then",
          action = new {
            type = "tap",
            parameters = new { x = 50, y = 90 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "start",
            expectedState = "success"
          }
        },
        new {
          stepId = "end",
          label = "End",
          action = new {
            type = "tap",
            parameters = new { x = 10, y = 10 }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    execution.GetProperty("steps").GetArrayLength().Should().Be(3);
  }
}

using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class PerStepNoBranchingSchemaIntegrationTests {
  [Fact]
  public async Task SaveAndReloadUseLinearSchemaWithoutBranchFields() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "linear-no-branch",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          action = new {
            type = "tap",
            parameters = new { x = 100, y = 100 }
          }
        },
        new {
          stepId = "step-2",
          action = new {
            type = "tap",
            parameters = new { x = 200, y = 200 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "step-1",
            expectedState = "success"
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    created.TryGetProperty("entryStepId", out _).Should().BeFalse();
    created.TryGetProperty("links", out _).Should().BeFalse();

    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    fetched.TryGetProperty("entryStepId", out _).Should().BeFalse();
    fetched.TryGetProperty("links", out _).Should().BeFalse();
    fetched.GetProperty("steps").GetArrayLength().Should().Be(2);

    var invalidPatchPayload = new {
      name = "linear-no-branch",
      version = fetched.GetProperty("version").GetInt32(),
      entryStepId = "step-1",
      steps = fetched.GetProperty("steps"),
      links = new object[] {
        new {
          linkId = "legacy-link",
          sourceStepId = "step-1",
          targetStepId = "step-2",
          branchType = "next"
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", invalidPatchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}

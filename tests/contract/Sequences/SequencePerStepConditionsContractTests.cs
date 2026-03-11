using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequencePerStepConditionsContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task CreateGetAndPatchSequenceSupportOptionalPerStepConditions() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createPayload = new {
      name = "per-step-contract",
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
          stepId = "open-menu",
          label = "Open Menu",
          action = new {
            type = "tap",
            parameters = new { x = 520, y = 900 }
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
    created.GetProperty("steps").GetArrayLength().Should().Be(2);

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    fetched.GetProperty("steps")[0].GetProperty("stepId").GetString().Should().Be("go-home");
    fetched.GetProperty("steps")[1].GetProperty("condition").GetProperty("type").GetString().Should().Be("commandOutcome");

    var patchPayload = new {
      name = "per-step-contract-updated",
      version = fetched.GetProperty("version").GetInt32(),
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
          stepId = "open-menu",
          label = "Open Menu",
          action = new {
            type = "tap",
            parameters = new { x = 520, y = 900 }
          },
          condition = new {
            type = "commandOutcome",
            stepRef = "go-home",
            expectedState = "success"
          }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var patched = await patchResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    patched.GetProperty("name").GetString().Should().Be("per-step-contract-updated");
    patched.GetProperty("steps")[1].GetProperty("condition").GetProperty("type").GetString().Should().Be("commandOutcome");
  }
}

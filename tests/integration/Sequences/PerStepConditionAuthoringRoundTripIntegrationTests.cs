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
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 120, y = 840  } }
        },
        new {
          stepId = "go-back",
          label = "Go Back",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 64, y = 64  } },
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
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 120, y = 840  } }
        },
        new {
          stepId = "go-back",
          label = "Go Back",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 64, y = 64  } },
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

  [Fact]
  public async Task SavedLoopBodyCommandReferencesRoundTripWithoutFallingBackToStepIds() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "loop-roundtrip",
      version = 1,
      steps = new object[] {
        new {
          stepId = "loop-1",
          label = "Repeat mailbox",
          stepType = "Loop",
          loop = new {
            loopType = "count",
            count = 2,
            maxIterations = 2
          },
          body = new object[] {
            new {
              stepId = "body-step-1",
              label = "Open mailbox",
              stepType = "Action",
              primitiveAction = new {
                type = "command",
                schemaVersion = "v1",
                payload = new { commandId = "cmd-mail" }
              }
            },
            new {
              stepId = "body-step-2",
              label = "Collect rewards",
              stepType = "Action",
              primitiveAction = new {
                type = "command",
                schemaVersion = "v1",
                payload = new { commandId = "cmd-rewards" }
              }
            }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var reloadResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    reloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var reloaded = await reloadResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var body = reloaded.GetProperty("steps")[0].GetProperty("body");
    body[0].GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString().Should().Be("cmd-mail");
    body[1].GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString().Should().Be("cmd-rewards");
    body[1].GetProperty("stepId").GetString().Should().Be("body-step-2");
  }

  [Fact]
  public async Task UnchangedLoopBodyResaveDoesNotReassignCommandsToBodyStepIds() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "loop-unchanged-resave",
      version = 1,
      steps = new object[] {
        new {
          stepId = "loop-1",
          label = "Repeat mailbox",
          stepType = "Loop",
          loop = new {
            loopType = "count",
            count = 2,
            maxIterations = 2
          },
          body = new object[] {
            new {
              stepId = "body-step-1",
              label = "Open mailbox",
              stepType = "Action",
              primitiveAction = new {
                type = "command",
                schemaVersion = "v1",
                payload = new { commandId = "cmd-mail" }
              }
            },
            new {
              stepId = "body-step-2",
              label = "Collect rewards",
              stepType = "Action",
              primitiveAction = new {
                type = "command",
                schemaVersion = "v1",
                payload = new { commandId = "cmd-rewards" }
              }
            }
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

    var patchPayload = new {
      name = fetched.GetProperty("name").GetString(),
      version = fetched.GetProperty("version").GetInt32(),
      steps = new object[] {
        new {
          stepId = "loop-1",
          label = "Repeat mailbox",
          stepType = "Loop",
          loop = new {
            loopType = "count",
            count = 2,
            maxIterations = 2
          },
          body = new object[] {
            new {
              stepId = "body-step-1",
              label = "Open mailbox",
              stepType = "Action",
              primitiveAction = new {
                type = "command",
                schemaVersion = "v1",
                payload = new { commandId = fetched.GetProperty("steps")[0].GetProperty("body")[0].GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString() }
              }
            },
            new {
              stepId = "body-step-2",
              label = "Collect rewards",
              stepType = "Action",
              primitiveAction = new {
                type = "command",
                schemaVersion = "v1",
                payload = new { commandId = fetched.GetProperty("steps")[0].GetProperty("body")[1].GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString() }
              }
            }
          }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var reloadResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    reloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var reloaded = await reloadResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var body = reloaded.GetProperty("steps")[0].GetProperty("body");
    body[0].GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString().Should().Be("cmd-mail");
    body[1].GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString().Should().Be("cmd-rewards");
    body[1].GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString().Should().NotBe("body-step-2");
  }
}

using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Integration tests for FR-002 (dry-run / validate-only sequence mode): `dryRun: true` on
/// `POST /api/sequences` (per-step request shape) runs the same enrichment/validation as a real
/// create but never persists a sequence, success or failure.
/// </summary>
public sealed class SequenceCreateDryRunIntegrationTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    return client;
  }

  [Fact]
  public async Task DryRunCreateWithValidBodySucceedsWithoutPersisting() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var payload = new {
      name = "dry-run-valid-body",
      version = 1,
      dryRun = true,
      steps = new object[] {
        new {
          stepId = "tap-1",
          label = "Tap",
          stepType = "Action",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 100, y = 100 } }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    body.GetProperty("errors").GetArrayLength().Should().Be(0);

    var listResponse = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    list.EnumerateArray().Should().NotContain(s => s.GetProperty("name").GetString() == "dry-run-valid-body");
  }

  [Fact]
  public async Task DryRunCreateWithNestedLoopFailsSameAsRealCreateAndPersistsNothing() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var nestedLoopPayload = new {
      name = "dry-run-nested-loop",
      version = 1,
      dryRun = true,
      steps = new object[] {
        new {
          stepId = "outer-loop",
          label = "Outer",
          stepType = "Loop",
          loop = new { loopType = "count", count = 2, maxIterations = 2 },
          body = new object[] {
            new {
              stepId = "inner-loop",
              label = "Inner",
              stepType = "Loop",
              loop = new { loopType = "count", count = 2, maxIterations = 2 },
              body = new object[] {
                new {
                  stepId = "tap-1",
                  label = "Tap",
                  stepType = "Action",
                  primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 1 } }
                }
              }
            }
          }
        }
      }
    };

    var dryRunResponse = await client.PostAsJsonAsync("/api/sequences", nestedLoopPayload).ConfigureAwait(false);
    dryRunResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var dryRunBody = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var realPayload = new {
      name = "real-nested-loop-check",
      version = 1,
      steps = nestedLoopPayload.steps
    };
    var realResponse = await client.PostAsJsonAsync("/api/sequences", realPayload).ConfigureAwait(false);
    realResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var realBody = await realResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    dryRunBody.GetProperty("errors").ToString().Should().Be(realBody.GetProperty("errors").ToString());

    var listResponse = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    list.EnumerateArray().Should().NotContain(s => s.GetProperty("name").GetString() == "dry-run-nested-loop");
  }

  [Fact]
  public async Task DryRunCreateWithUnresolvableCommandIdFailsSameAsRealCreateAndPersistsNothing() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var badCommandPayload = new {
      name = "dry-run-bad-command-ref",
      version = 1,
      dryRun = true,
      steps = new object[] {
        new {
          stepId = "step-1",
          label = "Bad command reference",
          stepType = "Action",
          primitiveAction = new {
            type = "command",
            schemaVersion = "v1",
            payload = new { commandName = "some-command" }
          }
        }
      }
    };

    var dryRunResponse = await client.PostAsJsonAsync("/api/sequences", badCommandPayload).ConfigureAwait(false);
    dryRunResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var dryRunBody = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    dryRunBody.GetProperty("errors").EnumerateArray().Should().Contain(e => e.GetString()!.Contains("step-1") && e.GetString()!.Contains("commandId"));

    var listResponse = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    list.EnumerateArray().Should().NotContain(s => s.GetProperty("name").GetString() == "dry-run-bad-command-ref");
  }

  [Fact]
  public async Task CreateWithoutDryRunStillPersistsExactlyAsToday() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var payload = new {
      name = "dry-run-omitted-still-creates",
      version = 1,
      steps = new object[] {
        new {
          stepId = "tap-1",
          label = "Tap",
          stepType = "Action",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 1 } }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);

    var listResponse = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    list.EnumerateArray().Should().Contain(s => s.GetProperty("name").GetString() == "dry-run-omitted-still-creates");
  }
}

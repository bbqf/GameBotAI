using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Contract tests for feature 091 / issue #177 (B-008): <c>dryRun: true</c> on
/// <c>PUT</c>/<c>PATCH /api/sequences/{id}</c> validates exactly like a real update but never
/// persists — it used to be silently ignored and the update applied for real.
/// </summary>
public sealed class SequenceUpdateDryRunContractTests {
  private static readonly string[] LegacyStringSteps = { "some-command" };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static object TapStep(string stepId, int x) => new {
    stepId,
    label = "Tap",
    stepType = "Action",
    primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x, y = 1 } }
  };

  private static async Task<string> CreateTapSequenceAsync(HttpClient client, string name) {
    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name,
      version = 1,
      steps = new[] { TapStep("tap-original", 10) }
    }).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return created.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetSequenceAsync(HttpClient client, string sequenceId) {
    var response = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
  }

  private static async Task AssertUnchangedAsync(HttpClient client, string sequenceId, string name, int version) {
    var fetched = await GetSequenceAsync(client, sequenceId).ConfigureAwait(false);
    fetched.GetProperty("version").GetInt32().Should().Be(version);
    fetched.GetProperty("name").GetString().Should().Be(name);
    fetched.GetProperty("steps").GetArrayLength().Should().Be(1);
    fetched.GetProperty("steps")[0].GetProperty("stepId").GetString().Should().Be("tap-original");
  }

  private static async Task AssertDryRunEnvelopeAsync(HttpResponseMessage response) {
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    body.GetProperty("errors").GetArrayLength().Should().Be(0);
  }

  private static object NestedLoopBody(string name, bool dryRun) => new {
    name,
    version = 1,
    dryRun,
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
            body = new[] { TapStep("tap-1", 1) }
          }
        }
      }
    }
  };

  [Fact]
  public async Task PutWithDryRunDoesNotPersistAndReturnsEnvelope() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateTapSequenceAsync(client, "put-dry-run-valid").ConfigureAwait(false);

    var response = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "put-dry-run-renamed",
      version = 1,
      dryRun = true,
      steps = new[] { TapStep("tap-a", 1), TapStep("tap-b", 2) }
    }).ConfigureAwait(false);

    await AssertDryRunEnvelopeAsync(response).ConfigureAwait(false);
    await AssertUnchangedAsync(client, sequenceId, "put-dry-run-valid", 1).ConfigureAwait(false);
  }

  [Fact]
  public async Task PatchWithDryRunDoesNotPersistAndReturnsEnvelope() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateTapSequenceAsync(client, "patch-dry-run-valid").ConfigureAwait(false);

    var response = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "patch-dry-run-renamed",
      version = 1,
      dryRun = true,
      steps = new[] { TapStep("tap-a", 1), TapStep("tap-b", 2) }
    }).ConfigureAwait(false);

    await AssertDryRunEnvelopeAsync(response).ConfigureAwait(false);
    await AssertUnchangedAsync(client, sequenceId, "patch-dry-run-valid", 1).ConfigureAwait(false);
  }

  [Fact]
  public async Task PutWithDryRunAndInvalidBodyFailsLikeRealPutAndChangesNothing() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateTapSequenceAsync(client, "put-dry-run-invalid").ConfigureAwait(false);

    var dryRunResponse = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", NestedLoopBody("put-dry-run-invalid", dryRun: true)).ConfigureAwait(false);
    dryRunResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var dryRunBody = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var realResponse = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", NestedLoopBody("put-dry-run-invalid", dryRun: false)).ConfigureAwait(false);
    realResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var realBody = await realResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    dryRunBody.GetProperty("errors").ToString().Should().Be(realBody.GetProperty("errors").ToString());
    await AssertUnchangedAsync(client, sequenceId, "put-dry-run-invalid", 1).ConfigureAwait(false);
  }

  [Fact]
  public async Task PutWithDryRunAndStaleVersionReturnsConflictAndChangesNothing() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateTapSequenceAsync(client, "put-dry-run-stale").ConfigureAwait(false);

    var realPut = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "put-dry-run-stale",
      version = 1,
      steps = new[] { TapStep("tap-original", 20) }
    }).ConfigureAwait(false);
    realPut.StatusCode.Should().Be(HttpStatusCode.OK);

    var staleDryRun = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "put-dry-run-stale-renamed",
      version = 1,
      dryRun = true,
      steps = new[] { TapStep("tap-a", 1), TapStep("tap-b", 2) }
    }).ConfigureAwait(false);

    staleDryRun.StatusCode.Should().Be(HttpStatusCode.Conflict);
    await AssertUnchangedAsync(client, sequenceId, "put-dry-run-stale", 2).ConfigureAwait(false);
  }

  [Fact]
  public async Task PutWithDryRunOnUnknownSequenceReturnsNotFound() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PutAsJsonAsync($"/api/sequences/missing-{Guid.NewGuid():N}", new {
      name = "nothing",
      version = 1,
      dryRun = true,
      steps = new[] { TapStep("tap-a", 1) }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Theory]
  [InlineData(null)]
  [InlineData(false)]
  public async Task PutWithoutDryRunOrWithDryRunFalseStillPersists(bool? dryRun) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateTapSequenceAsync(client, "put-real-write").ConfigureAwait(false);

    object body = dryRun is null
      ? new { name = "put-real-write-renamed", version = 1, steps = new[] { TapStep("tap-a", 1), TapStep("tap-b", 2) } }
      : new { name = "put-real-write-renamed", version = 1, dryRun = dryRun.Value, steps = new[] { TapStep("tap-a", 1), TapStep("tap-b", 2) } };
    var response = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", body).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await GetSequenceAsync(client, sequenceId).ConfigureAwait(false);
    fetched.GetProperty("version").GetInt32().Should().Be(2);
    fetched.GetProperty("name").GetString().Should().Be("put-real-write-renamed");
    fetched.GetProperty("steps").GetArrayLength().Should().Be(2);
  }

  [Fact]
  public async Task PutWithDryRunAndLegacyStringStepsBodyDoesNotPersist() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateTapSequenceAsync(client, "put-dry-run-legacy").ConfigureAwait(false);

    var response = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "put-dry-run-legacy-renamed",
      dryRun = true,
      steps = LegacyStringSteps
    }).ConfigureAwait(false);

    await AssertDryRunEnvelopeAsync(response).ConfigureAwait(false);
    await AssertUnchangedAsync(client, sequenceId, "put-dry-run-legacy", 1).ConfigureAwait(false);
  }

  [Fact]
  public async Task PutWithNonBooleanDryRunOnPerStepBodyIsRejectedAsMalformed() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateTapSequenceAsync(client, "put-dry-run-non-boolean").ConfigureAwait(false);

    var json = JsonSerializer.Serialize(new {
      name = "put-dry-run-non-boolean-renamed",
      version = 1,
      dryRun = "yes",
      steps = new[] { TapStep("tap-a", 1) }
    });
    using var content = new StringContent(json, Encoding.UTF8, "application/json");
    var response = await client.PutAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative), content).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    await AssertUnchangedAsync(client, sequenceId, "put-dry-run-non-boolean", 1).ConfigureAwait(false);
  }
}

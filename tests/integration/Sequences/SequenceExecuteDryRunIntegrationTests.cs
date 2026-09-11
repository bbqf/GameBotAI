using System;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Integration tests for FR-002 (dry-run / validate-only sequence mode): `dryRun: true` on
/// `POST /api/sequences/{id}/execute` walks the real step tree but skips every step that would
/// dispatch to the emulator, start/use a session, or read live capture state — while a stale
/// `commandId` reference still fails loudly, exactly as a real execution does today.
/// </summary>
public sealed class SequenceExecuteDryRunIntegrationTests {
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
  public async Task DryRunExecuteWithNoSessionRunningSkipsTapStepAndSucceeds() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var createPayload = new {
      name = "dry-run-execute-tap",
      version = 1,
      steps = new object[] {
        new {
          stepId = "tap-1",
          label = "Tap",
          stepType = "Action",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 20, y = 20 } }
        }
      }
    };
    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    // No session is started anywhere in this test — a real (non-dry-run) execute would fail fast
    // with "no session available" here (see EmptyStateExecuteSequenceIntegrationTests).
    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { dryRun = true }).ConfigureAwait(false);

    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    var steps = execution.GetProperty("steps");
    steps.GetArrayLength().Should().Be(1);
    steps[0].GetProperty("actionOutcome").GetString().Should().Be("skipped_dry_run");
  }

  [Fact]
  public async Task DryRunExecuteWithStaleCommandIdStillFailsWithTheRealError() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var commandRepository = app.Services.GetRequiredService<ICommandRepository>();
    await commandRepository.AddAsync(new Command { Id = "stale-cmd", Name = "Stale Command" }).ConfigureAwait(false);

    var createPayload = new {
      name = "dry-run-execute-stale-command",
      version = 1,
      steps = new object[] {
        new {
          stepId = "cmd-1",
          label = "Command",
          stepType = "Action",
          primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId = "stale-cmd" } }
        }
      }
    };
    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    // The command is deleted after the sequence references it — a stale reference, not a
    // create-time validation failure.
    (await commandRepository.DeleteAsync("stale-cmd").ConfigureAwait(false)).Should().BeTrue();

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { dryRun = true }).ConfigureAwait(false);

    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    execution.GetProperty("status").GetString().Should().Be("Failed");
    var steps = execution.GetProperty("steps");
    steps[0].GetProperty("message").GetString().Should().Contain("stale-cmd").And.Contain("was not found");
  }

  [Fact]
  public async Task DryRunExecuteWithImageVisibleConditionNeverReadsLiveCaptureAndSucceeds() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var imageRepository = app.Services.GetRequiredService<IImageRepository>();
    using (var imageBytes = new MemoryStream(Encoding.ASCII.GetBytes("not-a-real-png"))) {
      await imageRepository.SaveAsync("gate-image", imageBytes, "image/png", null, overwrite: true).ConfigureAwait(false);
    }

    var createPayload = new {
      name = "dry-run-execute-image-condition",
      version = 1,
      steps = new object[] {
        new {
          stepId = "gated-tap",
          label = "Gated Tap",
          stepType = "Action",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } },
          condition = new { type = "imageVisible", imageId = "gate-image", minSimilarity = 0.8 }
        }
      }
    };
    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    // No session/capture exists anywhere in this test — a real (non-dry-run) evaluation of this
    // imageVisible condition would need one; dry-run must never attempt that read.
    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { dryRun = true }).ConfigureAwait(false);

    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    var steps = execution.GetProperty("steps");
    steps[0].GetProperty("status").GetString().Should().Be("Skipped");
  }
}

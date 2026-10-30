using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 170: the shared set-up and fixtures of the tests for <c>onTimeLimit</c>. It sets the test
/// environment and removes it when the test ends.
/// </summary>
public abstract class OnTimeLimitIntegrationTestBase : IDisposable {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevScreenImage;

  protected OnTimeLimitIntegrationTestBase() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevScreenImage = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual void Dispose(bool disposing) {
    if (!disposing) return;
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", _prevScreenImage);
  }

  protected static HttpClient CreateClient<T>(WebApplicationFactory<T> app) where T : class {
    ArgumentNullException.ThrowIfNull(app);
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  // A targetless wait is a plain delay: a step that outlives a short limit without an emulator.
  protected static CommandSequence SlowSequence(string id, string? onTimeLimit) {
    var sequence = new CommandSequence { Id = id, Name = id, OnTimeLimit = onTimeLimit };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "slow",
        StepType = SequenceStepType.Action,
        WaitForImage = new WaitForImageConfig { TimeoutMs = 60_000 }
      }
    });
    return sequence;
  }

  protected static CommandSequence QuickSequence(string id, string? onTimeLimit) {
    var sequence = new CommandSequence { Id = id, Name = id, OnTimeLimit = onTimeLimit };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "quick",
        StepType = SequenceStepType.Action,
        WaitForImage = new WaitForImageConfig { TimeoutMs = 0 }
      }
    });
    return sequence;
  }

  // A sequence that fails on its own terms without an exception: its command step taps an image that
  // does not exist, so the run fails in the normal end-of-run finalize.
  protected static async Task<(string SequenceId, string SessionId)> CreateFailingSequenceAsync(HttpClient client, string name, string? onTimeLimit = null) {
    var commandResponse = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new {
      name = name + "-cmd",
      triggerId = (string?)null,
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new { referenceImageId = "does_not_exist", confidence = 0.99, offsetX = 0, offsetY = 0 }
          }
        }
      }
    }).ConfigureAwait(false);
    commandResponse.EnsureSuccessStatusCode();
    var command = await commandResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var commandId = command.GetProperty("id").GetString();

    var body = new System.Collections.Generic.Dictionary<string, object?> {
      ["name"] = name,
      ["version"] = 1,
      ["steps"] = new object[] {
        new {
          stepId = "tap-1",
          stepType = "Action",
          requireDispatch = true,
          primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } }
        }
      }
    };
    if (onTimeLimit is not null) body["onTimeLimit"] = onTimeLimit;
    var sequenceResponse = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), body).ConfigureAwait(false);
    sequenceResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var sequence = await sequenceResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var gameResponse = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = name + "-game", description = "desc" }).ConfigureAwait(false);
    gameResponse.EnsureSuccessStatusCode();
    var game = await gameResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sessionResponse = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = game.GetProperty("id").GetString() }).ConfigureAwait(false);
    sessionResponse.EnsureSuccessStatusCode();
    var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    return (sequence.GetProperty("id").GetString()!, session.GetProperty("id").GetString()!);
  }

  protected static async Task<ExecutionLogEntry> SequenceEntryAsync(IServiceProvider services, string sequenceId) {
    var page = await services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", ObjectId = sequenceId, RootsOnly = true, PageSize = 100 })
      .ConfigureAwait(false);
    page.Items.Should().ContainSingle();
    return page.Items[0];
  }
}

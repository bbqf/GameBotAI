using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 094 (issue #182): a queue firing cut off by its time bound must be told apart from one that
/// failed on its own terms. The entry keeps status <c>failure</c> and gains
/// <c>cancellationReason: sequence_time_limit</c> plus the bound that applied — and nothing else
/// (successes, user stops, ordinary failures, ad-hoc runs, child commands) carries it.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceTimeLimitLogIntegrationTests : IDisposable {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevScreenImage;

  public SequenceTimeLimitLogIntegrationTests() {
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
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", _prevScreenImage);
    GC.SuppressFinalize(this);
  }

  // ── fixtures ──────────────────────────────────────────────────────────

  private static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  // A targetless wait is a plain delay: a step that reliably outlives a short bound without an emulator.
  private static CommandSequence SlowSequence(string id) {
    var sequence = new CommandSequence { Id = id, Name = id };
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

  private static CommandSequence QuickSequence(string id) {
    var sequence = new CommandSequence { Id = id, Name = id };
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

  // A sequence that fails on its own terms WITHOUT throwing: its one command step taps an image that
  // was never uploaded, so detection misses, the step does not dispatch, and requireDispatch fails the
  // run through the normal end-of-run finalize — the path a step that swallowed the cancellation takes.
  private static async Task<(string SequenceId, string SessionId)> CreateFailingSequenceAsync(HttpClient client, string name) {
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

    var sequenceResponse = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new {
      name,
      version = 1,
      steps = new object[] {
        new {
          stepId = "tap-1",
          stepType = "Action",
          requireDispatch = true,
          primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } }
        }
      }
    }).ConfigureAwait(false);
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

  private static async Task<ExecutionLogEntry> SequenceEntryAsync(IServiceProvider services, string sequenceId) {
    var page = await services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", ObjectId = sequenceId, RootsOnly = true, PageSize = 100 })
      .ConfigureAwait(false);
    page.Items.Should().ContainSingle();
    return page.Items[0];
  }

  // ── time bound fired ─────────────────────────────────────────────────

  [Fact]
  public async Task FiringCutOffByItsTimeBoundIsRecordedAsATimeLimitCancellation() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var services = app.Services;
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(SlowSequence("seq-timed-out")).ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var firing = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, stop.Token);
    timer.CancelAfter(TimeSpan.FromMilliseconds(200));

    Func<Task> act;
    using (SequenceTimeLimitScope.Push(200, timer.Token, stop.Token)) {
      act = async () => await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync("seq-timed-out", sessionId: null, parentContext: null, firing.Token).ConfigureAwait(false);
      await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, "seq-timed-out").ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure", "the status vocabulary is unchanged");
    entry.CancellationReason.Should().Be(ExecutionCancellationReasons.SequenceTimeLimit);
    entry.TimeLimitMs.Should().Be(200);
    entry.Summary.Should().Contain("time limit");

    // The same fields reach every API projection of the entry.
    var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/execution-logs?objectType=sequence&objectId=seq-timed-out", UriKind.Relative)).ConfigureAwait(false);
    var item = list.GetProperty("items").EnumerateArray().Single();
    item.GetProperty("cancellationReason").GetString().Should().Be("sequence_time_limit");
    item.GetProperty("timeLimitMs").GetInt32().Should().Be(200);

    var detail = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution-logs/{entry.Id}", UriKind.Relative)).ConfigureAwait(false);
    detail.GetProperty("cancellationReason").GetString().Should().Be("sequence_time_limit");
    detail.GetProperty("timeLimitMs").GetInt32().Should().Be(200);

    var subtree = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution-logs/{entry.Id}/subtree", UriKind.Relative)).ConfigureAwait(false);
    subtree.GetProperty("root").GetProperty("cancellationReason").GetString().Should().Be("sequence_time_limit");
    subtree.GetProperty("root").GetProperty("timeLimitMs").GetInt32().Should().Be(200);
  }

  [Fact]
  public async Task UserStopIsNotRecordedAsATimeLimitCancellation() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(SlowSequence("seq-stopped")).ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var firing = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, stop.Token);
    stop.CancelAfter(TimeSpan.FromMilliseconds(200));

    using (SequenceTimeLimitScope.Push(240000, timer.Token, stop.Token)) {
      var act = async () => await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync("seq-stopped", sessionId: null, parentContext: null, firing.Token).ConfigureAwait(false);
      await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, "seq-stopped").ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure");
    entry.CancellationReason.Should().BeNull();
    entry.TimeLimitMs.Should().BeNull();
  }

  [Fact]
  public async Task FailureReachedAfterTheBoundFiredIsRecordedAsATimeLimitCancellation() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var services = app.Services;
    var (sequenceId, sessionId) = await CreateFailingSequenceAsync(client, "seq-swallowed").ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    await timer.CancelAsync().ConfigureAwait(false); // the bound has already fired; the run is not handed that token (as if a step swallowed it)

    GameBot.Domain.Services.SequenceExecutionResult result;
    using (SequenceTimeLimitScope.Push(240000, timer.Token, CancellationToken.None)) {
      result = await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync(sequenceId, sessionId, parentContext: null).ConfigureAwait(false);
    }

    result.Status.Should().Be("Failed");
    var entry = await SequenceEntryAsync(services, sequenceId).ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure");
    entry.Summary.Should().NotContain("cancelled", "this is the normal end-of-run finalize, not the abort path");
    entry.CancellationReason.Should().Be(ExecutionCancellationReasons.SequenceTimeLimit);
    entry.TimeLimitMs.Should().Be(240000);

    // Child command entries keep their own outcomes (FR-004b).
    var children = await services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "command", PageSize = 100 }).ConfigureAwait(false);
    children.Items.Where(c => c.Hierarchy.ParentExecutionId == entry.Id)
      .Should().OnlyContain(c => c.CancellationReason == null && c.TimeLimitMs == null);
  }

  // ── nothing else carries the reason ───────────────────────────────────

  [Fact]
  public async Task OrdinaryFailureInsideTheBoundCarriesNoReason() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var services = app.Services;
    var (sequenceId, sessionId) = await CreateFailingSequenceAsync(client, "seq-ordinary-failure").ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    using (SequenceTimeLimitScope.Push(240000, timer.Token, CancellationToken.None)) {
      await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync(sequenceId, sessionId, parentContext: null).ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, sequenceId).ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure");
    entry.CancellationReason.Should().BeNull();
    entry.TimeLimitMs.Should().BeNull();
  }

  [Fact]
  public async Task SuccessIsNeverMarkedEvenAfterTheBoundFired() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(QuickSequence("seq-late-success")).ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    await timer.CancelAsync().ConfigureAwait(false);
    using (SequenceTimeLimitScope.Push(240000, timer.Token, CancellationToken.None)) {
      var result = await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync("seq-late-success", sessionId: null, parentContext: null).ConfigureAwait(false);
      result.Status.Should().Be("Succeeded");
    }

    var entry = await SequenceEntryAsync(services, "seq-late-success").ConfigureAwait(false);
    entry.FinalStatus.Should().Be("success");
    entry.CancellationReason.Should().BeNull();
    entry.TimeLimitMs.Should().BeNull();
  }

  [Fact]
  public async Task AdHocFailureWithoutAQueueCarriesNoReason() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var services = app.Services;
    var (sequenceId, sessionId) = await CreateFailingSequenceAsync(client, "seq-adhoc-failure").ConfigureAwait(false);

    SequenceTimeLimitScope.Current.Should().BeNull();
    await services.GetRequiredService<ISequenceExecutionService>()
      .ExecuteAsync(sequenceId, sessionId, parentContext: null).ConfigureAwait(false);

    var entry = await SequenceEntryAsync(services, sequenceId).ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure");
    entry.CancellationReason.Should().BeNull();
    entry.TimeLimitMs.Should().BeNull();
  }
}

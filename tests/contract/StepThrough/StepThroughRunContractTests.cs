#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.ContractTests.StepThrough.StepThroughFixture;

namespace GameBot.ContractTests.StepThrough;

/// <summary>
/// Contract of start, read, run next, cancel, and end (feature 127, contracts/step-through-api.md): the
/// documented status for each documented error code.
/// </summary>
public sealed class StepThroughRunContractTests : IClassFixture<StepThroughFixture> {
  private readonly StepThroughFixture _f;

  public StepThroughRunContractTests(StepThroughFixture fixture) { _f = fixture; }

  private static string Error(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

  [Fact]
  public async Task StartReturns201WithTheStepListAndTheMarkerOnTheFirstStep() {
    var sequenceId = await _f.SeedSequenceAsync("three", Tap(0, "a"), Tap(1, "b"), Tap(2, "c"));
    var session = _f.NewGameSession();
    var before = _f.Sessions.Inputs.Count;

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = session });

    status.Should().Be(201);
    body.GetProperty("state").GetString().Should().Be("idle");
    body.GetProperty("cursor").GetString().Should().Be("0");
    body.GetProperty("sequenceName").GetString().Should().Be("three");
    body.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("path").GetString()).Should().Equal("0", "1", "2");
    body.GetProperty("history").GetArrayLength().Should().Be(0);
    body.GetProperty("leaseExpiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
    _f.Sessions.Inputs.Count.Should().Be(before, "a start does not run a step");
  }

  [Fact]
  public async Task ThreePushesRunThreeStepsInOrderAndTheStateEndsComplete() {
    var sequenceId = await _f.SeedSequenceAsync("three", Tap(0, "a", 1, 1), Tap(1, "b", 2, 2), Tap(2, "c", 3, 3));
    var session = _f.NewGameSession();
    var id = await _f.StartAsync(sequenceId, session);
    var before = _f.Sessions.Inputs.Count;

    for (var i = 0; i < 3; i++) {
      var (status, _) = await _f.PostAsync($"/api/step-through/{id}/run-next");
      status.Should().Be(202);
      var state = await _f.WaitIdleAsync(id);
      state.GetProperty("history").GetArrayLength().Should().Be(i + 1);
      state.GetProperty("history")[i].GetProperty("status").GetString().Should().Be("Succeeded");
      state.GetProperty("history")[i].GetProperty("executionLogId").ValueKind.Should().Be(JsonValueKind.String);
    }

    var final = await _f.WaitIdleAsync(id);
    final.GetProperty("state").GetString().Should().Be("complete");
    final.GetProperty("cursor").ValueKind.Should().Be(JsonValueKind.Null);
    final.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("path").GetString()).Should().Equal("0", "1", "2");
    _f.Sessions.Inputs.Count.Should().Be(before + 3);
    _f.Sessions.Inputs.Skip(before).Select(i => i.Type).Should().OnlyContain(t => t == "tap");
  }

  [Fact]
  public async Task RunNextOnACompleteSequenceReturns409SequenceComplete() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    await _f.WaitIdleAsync(id);

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/run-next");

    status.Should().Be(409);
    Error(body).Should().Be("sequence_complete");
    body.GetProperty("error").GetProperty("message").GetString().Should().Contain("Select a step");
  }

  [Fact]
  public async Task StartWithAnUnknownSequenceReturns404SequenceNotFound() {
    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId = "nope", gameSessionId = _f.NewGameSession() });

    status.Should().Be(404);
    Error(body).Should().Be("sequence_not_found");
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("")]
  public async Task StartWithAnUnavailableGameSessionReturns409SessionUnavailable(string gameSessionId) {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId });

    status.Should().Be(409);
    Error(body).Should().Be("session_unavailable");
  }

  [Fact]
  public async Task StartWithADisconnectedGameSessionReturns409SessionUnavailable() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var session = "gs-stopped-" + Guid.NewGuid().ToString("N")[..6];
    _f.Sessions.Add(session, "emu-x", GameBot.Domain.Sessions.SessionStatus.Stopped);

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = session });

    status.Should().Be(409);
    Error(body).Should().Be("session_unavailable");
  }

  [Fact]
  public async Task AGameSessionAllowsOneStepThroughAtATime() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var session = _f.NewGameSession();
    var id = await _f.StartAsync(sequenceId, session);

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = session });

    status.Should().Be(409);
    Error(body).Should().Be("session_in_use");

    (await _f.SendAsync(HttpMethod.Delete, $"/api/step-through/{id}")).Status.Should().Be(204);
    (await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = session })).Status.Should().Be(201);
  }

  [Fact]
  public async Task ASecondPushWhileAStepRunsReturns409StepRunningAndCancelEndsTheStep() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    _f.Sessions.Hold = new TaskCompletionSource<bool>();
    try {
      (await _f.PostAsync($"/api/step-through/{id}/run-next")).Status.Should().Be(202);

      var (status, body) = await _f.PostAsync($"/api/step-through/{id}/run-next");
      status.Should().Be(409);
      Error(body).Should().Be("step_running");

      var (cancelStatus, _) = await _f.PostAsync($"/api/step-through/{id}/cancel");
      cancelStatus.Should().Be(202);

      var state = await _f.WaitIdleAsync(id);
      var entry = state.GetProperty("history").EnumerateArray().Last();
      entry.GetProperty("status").GetString().Should().Be("Cancelled");
      state.GetProperty("cursor").GetString().Should().Be("0", "the cursor stays on the cancelled step");
    }
    finally {
      _f.Sessions.Hold = null;
    }
  }

  [Fact]
  public async Task CancelWhenNoStepRunsReturns200() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    var (status, _) = await _f.PostAsync($"/api/step-through/{id}/cancel");

    status.Should().Be(200);
  }

  [Fact]
  public async Task ARunningQueueOnTheDeviceReturns409QueueRunningWithTheQueueInTheDetails() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var serial = "emu-queue-" + Guid.NewGuid().ToString("N")[..6];
    var session = _f.NewGameSession(serial);
    var queueId = "q-" + Guid.NewGuid().ToString("N")[..6];
    var runs = _f.Services.GetRequiredService<IQueueRunRegistry>();
    var claims = _f.Services.GetRequiredService<IDeviceClaimRegistry>();
    runs.TryAdd(queueId, new QueueRunHandle { QueueId = queueId, Cts = new System.Threading.CancellationTokenSource() }).Should().BeTrue();
    claims.TryClaim(serial, queueId, "Daily 5558").Should().BeTrue();
    var id = await _f.StartAsync(sequenceId, session);
    var before = _f.Sessions.Inputs.Count;
    try {
      var (status, body) = await _f.PostAsync($"/api/step-through/{id}/run-next");

      status.Should().Be(409);
      Error(body).Should().Be("queue_running");
      var details = body.GetProperty("error").GetProperty("details");
      details.GetProperty("queueId").GetString().Should().Be(queueId);
      details.GetProperty("queueName").GetString().Should().Be("Daily 5558");
      details.GetProperty("canPause").GetBoolean().Should().BeTrue();
      _f.Sessions.Inputs.Count.Should().Be(before, "no step ran on a device that a queue owns");
    }
    finally {
      runs.Remove(queueId, out _);
      claims.Release(serial, queueId);
    }
  }

  [Fact]
  public async Task AnUnknownIdReturns404OnReadAndRunNextAndDeleteIsIdempotent() {
    (await _f.GetAsync("/api/step-through/nope")).Status.Should().Be(404);
    var (status, body) = await _f.PostAsync("/api/step-through/nope/run-next");
    status.Should().Be(404);
    Error(body).Should().Be("step_through_not_found");

    (await _f.SendAsync(HttpMethod.Delete, "/api/step-through/nope")).Status.Should().Be(204);
  }

  [Fact]
  public async Task ReadWithAfterSeqReturnsOnlyNewerHistoryEntries() {
    var sequenceId = await _f.SeedSequenceAsync("two", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    await _f.WaitIdleAsync(id);
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    await _f.WaitIdleAsync(id);

    var (_, body) = await _f.GetAsync($"/api/step-through/{id}?afterSeq=1");

    body.GetProperty("history").GetArrayLength().Should().Be(1);
    body.GetProperty("history")[0].GetProperty("seq").GetInt32().Should().Be(2);
  }

  [Fact]
  public async Task TheSwaggerDocumentListsTheRoutesAndTheOriginFilter() {
    var (status, body) = await _f.GetAsync("/swagger/v1/swagger.json");

    status.Should().Be(200);
    var paths = body.GetProperty("paths");
    foreach (var route in new[] {
      "/api/step-through", "/api/step-through/{id}", "/api/step-through/{id}/run-next", "/api/step-through/{id}/select",
      "/api/step-through/{id}/cancel", "/api/step-through/{id}/restart", "/api/step-through/{id}/values", "/api/step-through/{id}/pause-queue"
    }) {
      paths.TryGetProperty(route, out _).Should().BeTrue(route);
    }

    var listParameters = paths.GetProperty("/api/execution-logs").GetProperty("get").GetProperty("parameters").EnumerateArray();
    listParameters.Should().Contain(p => p.GetProperty("name").GetString() == "origin");
  }
}

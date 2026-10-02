#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308
using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;
using static GameBot.ContractTests.StepThrough.StepThroughFixture;

namespace GameBot.ContractTests.StepThrough;

/// <summary>
/// Queue safety, restart, and values (feature 127, FR-009, FR-012, FR-012a to FR-012c, FR-017). The tests
/// cover the pause of the owning queue, the resume at the end, and the refusal while a firing runs.
/// They also cover the values that the author sets.
/// </summary>
public sealed class StepThroughQueueContractTests : IClassFixture<StepThroughFixture> {
  private readonly StepThroughFixture _f;

  public StepThroughQueueContractTests(StepThroughFixture fixture) { _f = fixture; }

  private static string Error(System.Text.Json.JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

  private static string NewSerial() => "emu-" + Guid.NewGuid().ToString("N")[..8];

  [Fact]
  public async Task PauseQueueThenRunNextRunsTheStepAndEndResumesTheQueue() {
    var serial = NewSerial();
    using var queue = _f.AttachQueue(serial);
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession(serial));

    (await _f.PostAsync($"/api/step-through/{id}/run-next")).Status.Should().Be(409);
    var (pauseStatus, paused) = await _f.PostAsync($"/api/step-through/{id}/pause-queue");

    pauseStatus.Should().Be(200);
    var queueInfo = paused.GetProperty("queue");
    queueInfo.GetProperty("queueId").GetString().Should().Be(queue.QueueId);
    queueInfo.GetProperty("pausedByStepThrough").GetBoolean().Should().BeTrue();
    queue.Handle.IsPolicyPaused.Should().BeTrue();

    (await _f.PostAsync($"/api/step-through/{id}/run-next")).Status.Should().Be(202);
    await _f.WaitIdleAsync(id);

    (await _f.SendAsync(HttpMethod.Delete, $"/api/step-through/{id}")).Status.Should().Be(204);
    queue.Handle.IsPolicyPaused.Should().BeFalse("the queue resumes when the step-through ends (FR-012b)");
  }

  [Fact]
  public async Task AQueueThatWasAlreadyPausedStaysPausedWhenTheStepThroughEnds() {
    var serial = NewSerial();
    using var queue = _f.AttachQueue(serial);
    queue.Handle.EnterPolicyPause("failure policy", DateTimeOffset.UtcNow);
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession(serial));

    var (pauseStatus, paused) = await _f.PostAsync($"/api/step-through/{id}/pause-queue");
    pauseStatus.Should().Be(200);
    paused.GetProperty("queue").GetProperty("alreadyPaused").GetBoolean().Should().BeTrue();
    paused.GetProperty("queue").GetProperty("pausedByStepThrough").GetBoolean().Should().BeFalse();

    (await _f.PostAsync($"/api/step-through/{id}/run-next")).Status.Should().Be(202);
    await _f.WaitIdleAsync(id);
    await _f.SendAsync(HttpMethod.Delete, $"/api/step-through/{id}");

    queue.Handle.IsPolicyPaused.Should().BeTrue();
    queue.Handle.PauseReason.Should().Be("failure policy");
  }

  [Fact]
  public async Task PauseQueueWhileAFiringRunsReturns409QueueRunActiveAndRunNextStaysRefused() {
    var serial = NewSerial();
    using var queue = _f.AttachQueue(serial);
    queue.Handle.SetCurrentSequence("seq-live", DateTimeOffset.UtcNow);
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession(serial));

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/pause-queue");
    status.Should().Be(409);
    Error(body).Should().Be("queue_run_active");
    queue.Handle.IsPolicyPaused.Should().BeFalse();

    var (runStatus, runBody) = await _f.PostAsync($"/api/step-through/{id}/run-next");
    runStatus.Should().Be(409);
    Error(runBody).Should().Be("queue_running");
    runBody.GetProperty("error").GetProperty("details").GetProperty("canPause").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task PauseQueueWithNoOwningQueueReturns409NoOwningQueue() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/pause-queue");

    status.Should().Be(409);
    Error(body).Should().Be("no_owning_queue");
  }

  [Fact]
  public async Task RestartKeepsTheQueuePauseAndTheParameterValuesAndClearsTheHistory() {
    var serial = NewSerial();
    using var queue = _f.AttachQueue(serial);
    var sequenceId = await _f.SeedParameterizedSequenceAsync(
      "param", new[] { new ParameterDeclaration { Name = "n", Default = "5" } }, Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession(serial));
    await _f.PostAsync($"/api/step-through/{id}/pause-queue");
    await _f.SendAsync(HttpMethod.Put, $"/api/step-through/{id}/values", new { parameterValues = new { n = "9" } });
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    (await _f.WaitIdleAsync(id)).GetProperty("history").GetArrayLength().Should().Be(1);

    var (status, state) = await _f.PostAsync($"/api/step-through/{id}/restart");

    status.Should().Be(200);
    state.GetProperty("history").GetArrayLength().Should().Be(0);
    state.GetProperty("cursor").GetString().Should().Be("0");
    state.GetProperty("queue").GetProperty("pausedByStepThrough").GetBoolean().Should().BeTrue();
    queue.Handle.IsPolicyPaused.Should().BeTrue("a restart does not resume the queue (FR-009)");
    var parameter = state.GetProperty("parameters")[0];
    parameter.GetProperty("value").GetString().Should().Be("9");
    parameter.GetProperty("isSet").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task ValuesKeepTheHistoryAndTheCursorAndShowDefaultsForUnsetParameters() {
    var sequenceId = await _f.SeedParameterizedSequenceAsync(
      "param", new[] { new ParameterDeclaration { Name = "n", Default = "5" } }, Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    await _f.WaitIdleAsync(id);

    var (status, state) = await _f.SendAsync(
      HttpMethod.Put, $"/api/step-through/{id}/values", new { parameterValues = new { n = "7" }, outcomes = new { a = "failed" } });

    status.Should().Be(200);
    state.GetProperty("history").GetArrayLength().Should().Be(1);
    state.GetProperty("cursor").GetString().Should().Be("1");
    state.GetProperty("parameters")[0].GetProperty("value").GetString().Should().Be("7");
    state.GetProperty("outcomes").GetProperty("a").GetString().Should().Be("failed");

    var (_, cleared) = await _f.SendAsync(HttpMethod.Put, $"/api/step-through/{id}/values", new { parameterValues = new { } });
    cleared.GetProperty("parameters")[0].GetProperty("value").GetString().Should().Be("5");
    cleared.GetProperty("parameters")[0].GetProperty("isSet").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task ValuesWithAnUnknownParameterReturn400() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    var (status, body) = await _f.SendAsync(HttpMethod.Put, $"/api/step-through/{id}/values", new { parameterValues = new { nope = "1" } });

    status.Should().Be(400);
    Error(body).Should().Be("unknown_parameter");
  }

  [Fact]
  public async Task ValuesWithAnOutcomeForAnUnknownStepReturn400() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    var (status, body) = await _f.SendAsync(HttpMethod.Put, $"/api/step-through/{id}/values", new { outcomes = new { ghost = "success" } });

    status.Should().Be(400);
    Error(body).Should().Be("unknown_step");
  }

  [Fact]
  public async Task ValuesWhileAStepRunsReturn409StepRunning() {
    var sequenceId = await _f.SeedSequenceAsync("two", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    _f.Sessions.Hold = new TaskCompletionSource<bool>();
    try {
      await _f.PostAsync($"/api/step-through/{id}/run-next");

      var (status, body) = await _f.SendAsync(HttpMethod.Put, $"/api/step-through/{id}/values", new { });
      status.Should().Be(409);
      Error(body).Should().Be("step_running");

      (await _f.PostAsync($"/api/step-through/{id}/restart")).Status.Should().Be(409);
      await _f.PostAsync($"/api/step-through/{id}/cancel");
      await _f.WaitIdleAsync(id);
    }
    finally {
      _f.Sessions.Hold = null;
    }
  }

  [Fact]
  public async Task StartWithValuesForUndeclaredParametersReturns400() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = _f.NewGameSession(), parameterValues = new { nope = "1" } });

    status.Should().Be(400);
    Error(body).Should().Be("unknown_parameter");
  }
}

#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;
using static GameBot.ContractTests.StepThrough.StepThroughFixture;

namespace GameBot.ContractTests.StepThrough;

/// <summary>Contract of the manual selection of the next step and of <c>startPath</c> (feature 127, FR-007, FR-013a).</summary>
public sealed class StepThroughSelectContractTests : IClassFixture<StepThroughFixture> {
  private readonly StepThroughFixture _f;

  public StepThroughSelectContractTests(StepThroughFixture fixture) { _f = fixture; }

  private static string Error(System.Text.Json.JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

  private static SequenceStep Loop(int order, string id, params SequenceStep[] body)
    => new() { Order = order, StepId = id, StepType = SequenceStepType.Loop, Loop = new CountLoopConfig { Count = 2 }, Body = body };

  [Fact]
  public async Task SelectMovesTheMarkerAndTheNextRunExecutesThatStep() {
    var sequenceId = await _f.SeedSequenceAsync("three", Tap(0, "a", 1, 1), Tap(1, "b", 2, 2), Tap(2, "c", 3, 3));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    var before = _f.Sessions.Inputs.Count;

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/select", new { path = "2" });
    status.Should().Be(200);
    body.GetProperty("cursor").GetString().Should().Be("2");

    await _f.PostAsync($"/api/step-through/{id}/run-next");
    await _f.WaitIdleAsync(id);
    await _f.PostAsync($"/api/step-through/{id}/select", new { path = "0" });
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    var state = await _f.WaitIdleAsync(id);

    state.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("path").GetString()).Should().Equal("2", "0");
    state.GetProperty("cursor").GetString().Should().Be("1");
    _f.Sessions.Inputs.Skip(before).Select(i => Convert.ToInt32(i.Args["x"], System.Globalization.CultureInfo.InvariantCulture)).Should().Equal(3, 1);
  }

  [Fact]
  public async Task SelectAfterCompleteLetsTheAuthorRunAStepAgain() {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    (await _f.WaitIdleAsync(id)).GetProperty("state").GetString().Should().Be("complete");

    await _f.PostAsync($"/api/step-through/{id}/select", new { path = "0" });
    var (status, _) = await _f.PostAsync($"/api/step-through/{id}/run-next");

    status.Should().Be(202);
    (await _f.WaitIdleAsync(id)).GetProperty("history").GetArrayLength().Should().Be(2);
  }

  [Theory]
  [InlineData("9")]
  [InlineData("0/body/9")]
  [InlineData("zzz")]
  public async Task SelectOfAnUnknownPathReturns400UnknownStep(string path) {
    var sequenceId = await _f.SeedSequenceAsync("one", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/select", new { path });

    status.Should().Be(400);
    Error(body).Should().Be("unknown_step");
  }

  [Fact]
  public async Task SelectOfALoopHeaderReturns400NotSelectable() {
    var sequenceId = await _f.SeedSequenceAsync("loop", Loop(0, "loop", Tap(0, "in")));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/select", new { path = "0" });

    status.Should().Be(400);
    Error(body).Should().Be("not_selectable");
  }

  [Fact]
  public async Task SelectWhileAStepRunsReturns409StepRunning() {
    var sequenceId = await _f.SeedSequenceAsync("two", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    _f.Sessions.Hold = new TaskCompletionSource<bool>();
    try {
      await _f.PostAsync($"/api/step-through/{id}/run-next");

      var (status, body) = await _f.PostAsync($"/api/step-through/{id}/select", new { path = "1" });

      status.Should().Be(409);
      Error(body).Should().Be("step_running");
      await _f.PostAsync($"/api/step-through/{id}/cancel");
      await _f.WaitIdleAsync(id);
    }
    finally {
      _f.Sessions.Hold = null;
    }
  }

  [Fact]
  public async Task StartPathMakesAMiddleStepTheFirstStep() {
    var sequenceId = await _f.SeedSequenceAsync("three", Tap(0, "a"), Tap(1, "b"), Tap(2, "c"));
    var session = _f.NewGameSession();

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = session, startPath = "1" });

    status.Should().Be(201);
    body.GetProperty("cursor").GetString().Should().Be("1");
  }

  [Fact]
  public async Task StartPathInsideALoopBodyIsAccepted() {
    var sequenceId = await _f.SeedSequenceAsync("loop", Loop(0, "loop", Tap(0, "in1"), Tap(1, "in2")));

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = _f.NewGameSession(), startPath = "0/body/1" });

    status.Should().Be(201);
    body.GetProperty("cursor").GetString().Should().Be("0/body/1");
  }

  [Theory]
  [InlineData("7", 400, "unknown_step")]
  [InlineData("0", 400, "not_selectable")]
  public async Task StartPathErrorsReturn400AndLeaveNoStepThroughBehind(string startPath, int expectedStatus, string expectedCode) {
    var sequenceId = await _f.SeedSequenceAsync("loop", Loop(0, "loop", Tap(0, "in")));
    var session = _f.NewGameSession();

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = session, startPath });

    status.Should().Be(expectedStatus);
    Error(body).Should().Be(expectedCode);
    // The game session is free again: a start without the bad path works.
    (await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = session })).Status.Should().Be(201);
  }
}

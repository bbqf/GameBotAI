using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.EnsureGameRunning;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Services.EnsureGameRunning;

public sealed class GameForegroundGuardTests {
  // Replays a scripted sequence of handler outcomes, repeating the last one once exhausted, and
  // counts how many times it was asked.
  private sealed class ScriptedEnsureGameRunning : IEnsureGameRunningActionHandler {
    private readonly Queue<EnsureGameRunningOutcome> _script;
    private EnsureGameRunningOutcome _last;

    public ScriptedEnsureGameRunning(params EnsureGameRunningOutcome[] script) {
      _script = new Queue<EnsureGameRunningOutcome>(script);
      _last = script.Length > 0 ? script[^1] : EnsureGameRunningOutcome.GameRunning;
    }

    public int Calls { get; private set; }
    public string? LastSessionId { get; private set; }

    public Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) {
      Calls++;
      LastSessionId = sessionId;
      if (_script.Count > 0) _last = _script.Dequeue();
      return Task.FromResult(new EnsureGameRunningActionResult(_last));
    }
  }

  // Short windows keep the confirm loop deterministic without waiting real seconds.
  private static GameForegroundGuard Guard(IEnsureGameRunningActionHandler handler)
    => new(handler, confirmTimeout: TimeSpan.FromMilliseconds(200), pollInterval: TimeSpan.FromMilliseconds(5));

  [Fact]
  public async Task Game_already_in_front_passes_after_a_single_check() {
    var handler = new ScriptedEnsureGameRunning(EnsureGameRunningOutcome.GameRunning);

    var result = await Guard(handler).EnsureForegroundAsync("s-1");

    result.Outcome.Should().Be(GameForegroundGuardOutcome.AlreadyForeground);
    result.Recovered.Should().BeFalse();
    result.Failed.Should().BeFalse();
    // The common case must not pay for the confirm loop.
    handler.Calls.Should().Be(1);
    handler.LastSessionId.Should().Be("s-1");
  }

  [Fact]
  public async Task Game_pushed_out_of_the_foreground_is_brought_back() {
    // First check finds the launcher and fires a launch; the next confirms the game returned.
    var handler = new ScriptedEnsureGameRunning(
      EnsureGameRunningOutcome.GameNotRunning,
      EnsureGameRunningOutcome.GameRunning);

    var result = await Guard(handler).EnsureForegroundAsync("s-1");

    result.Outcome.Should().Be(GameForegroundGuardOutcome.Recovered);
    result.Recovered.Should().BeTrue();
    handler.Calls.Should().Be(2);
  }

  [Fact]
  public async Task Game_that_never_returns_reports_failure_instead_of_waiting_forever() {
    var handler = new ScriptedEnsureGameRunning(EnsureGameRunningOutcome.GameNotRunning);

    var result = await Guard(handler).EnsureForegroundAsync("s-1");

    result.Outcome.Should().Be(GameForegroundGuardOutcome.RecoveryFailed);
    result.Failed.Should().BeTrue();
    result.ReasonCode.Should().Be("game_not_running");
    // It kept trying rather than giving up after the first launch.
    handler.Calls.Should().BeGreaterThan(1);
  }

  // EnsureGameRunningOutcome is internal, so it cannot be a public [Theory] parameter — these stay
  // separate facts over a shared assertion.
  private static async Task AssertNotApplicable(EnsureGameRunningOutcome outcome) {
    var handler = new ScriptedEnsureGameRunning(outcome);

    var result = await Guard(handler).EnsureForegroundAsync("s-1");

    result.Outcome.Should().Be(GameForegroundGuardOutcome.NotApplicable);
    result.Recovered.Should().BeFalse();
    result.Failed.Should().BeFalse();
    // A configuration gap must not spin the confirm loop.
    handler.Calls.Should().Be(1);
  }

  [Fact]
  public Task Session_outside_a_queue_context_is_not_applicable()
    => AssertNotApplicable(EnsureGameRunningOutcome.NoQueueContext);

  [Fact]
  public Task Queue_without_a_linked_game_is_not_applicable()
    => AssertNotApplicable(EnsureGameRunningOutcome.NoLinkedGame);

  [Fact]
  public Task Game_without_a_package_name_is_not_applicable()
    => AssertNotApplicable(EnsureGameRunningOutcome.NoPackageName);

  [Fact]
  public Task Host_without_adb_is_not_applicable()
    => AssertNotApplicable(EnsureGameRunningOutcome.PlatformUnsupported);

  [Fact]
  public async Task A_context_that_becomes_unusable_mid_confirm_stops_the_loop() {
    var handler = new ScriptedEnsureGameRunning(
      EnsureGameRunningOutcome.GameNotRunning,
      EnsureGameRunningOutcome.NoLinkedGame);

    var result = await Guard(handler).EnsureForegroundAsync("s-1");

    result.Outcome.Should().Be(GameForegroundGuardOutcome.NotApplicable);
    handler.Calls.Should().Be(2);
  }

  [Fact]
  public async Task Cancellation_during_the_confirm_window_propagates() {
    var handler = new ScriptedEnsureGameRunning(EnsureGameRunningOutcome.GameNotRunning);
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(20));

    var act = async () => await Guard(handler).EnsureForegroundAsync("s-1", cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
  }
}

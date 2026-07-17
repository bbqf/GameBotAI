using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Service.Services.EnsureEmulatorRunning;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Covers the connect-to-game emulator pre-heal decision (feature 071): which emulator outcomes let the
/// connect PROCEED to StartSession vs FAIL FAST before it, the success-message clause, and the
/// "no instance id ⇒ no pre-heal" gate. The decision is exercised through the same
/// <see cref="ConnectEmulatorPreheat"/> helper the dispatcher uses.
/// </summary>
public sealed class ConnectToGameEmulatorPreheatTests {
  private static EnsureEmulatorRunningActionResult Result(EnsureEmulatorRunningOutcome o) => new(o);

  [Theory]
  // Proceed (no fail-fast) for success + neutral unsupported outcomes.
  [InlineData(EnsureEmulatorRunningOutcome.AlreadyHealthy, false)]
  [InlineData(EnsureEmulatorRunningOutcome.Started, false)]
  [InlineData(EnsureEmulatorRunningOutcome.Restarted, false)]
  [InlineData(EnsureEmulatorRunningOutcome.PlatformUnsupported, false)]
  [InlineData(EnsureEmulatorRunningOutcome.ControlUnavailable, false)]
  // Fail fast (do NOT start the session) for genuine emulator failures.
  [InlineData(EnsureEmulatorRunningOutcome.RecoveryTimedOut, true)]
  [InlineData(EnsureEmulatorRunningOutcome.InstanceNotFound, true)]
  internal void FailFastOnlyOnGenuineFailure(EnsureEmulatorRunningOutcome outcome, bool expectFailFast) {
    var reason = ConnectEmulatorPreheat.FailFastReason(Result(outcome));
    (reason is not null).Should().Be(expectFailFast);
    if (expectFailFast) reason.Should().Contain("emulator pre-heal failed");
  }

  [Fact]
  public void NoPreheatWhenNoInstanceId() {
    // Null result models "no instance id supplied" ⇒ no pre-heal ran ⇒ always proceed, no clause.
    ConnectEmulatorPreheat.FailFastReason(null).Should().BeNull();
    ConnectEmulatorPreheat.MessageClause(null).Should().BeEmpty();
  }

  [Fact]
  public void MessageClauseIncludesReasonWhenPreheatRan() {
    ConnectEmulatorPreheat.MessageClause(Result(EnsureEmulatorRunningOutcome.Started))
      .Should().Be("emulator: started; ");
  }

  [Fact]
  public void PreheatGateSkipsWhenConnectHasNoInstanceId() {
    // Connect params without an instance id ⇒ the emulator-args gate the dispatcher uses returns false.
    var noInstance = new Dictionary<string, object?> { ["gameId"] = "pns", ["adbSerial"] = "emulator-5558" };
    EnsureEmulatorRunningArgs.TryFrom(noInstance, out _).Should().BeFalse();

    var withInstance = new Dictionary<string, object?> {
      ["gameId"] = "pns", ["adbSerial"] = "emulator-5558", ["instanceName"] = "LDPlayer-5558"
    };
    EnsureEmulatorRunningArgs.TryFrom(withInstance, out var args).Should().BeTrue();
    args!.AdbSerial.Should().Be("emulator-5558");
  }
}

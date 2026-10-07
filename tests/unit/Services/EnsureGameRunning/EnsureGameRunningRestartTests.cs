using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.EnsureGameRunning;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Services.EnsureGameRunning;

/// <summary>Feature 129: the forced restart of the game (RestartAsync) and the plain path that stays as it was.</summary>
public sealed class EnsureGameRunningRestartTests {
  private const string Package = "com.example.game";

  private static readonly EnsureGameRunningRestartOptions Fast = new() {
    RestartForegroundWait = TimeSpan.FromMilliseconds(300),
    RestartPollInterval = TimeSpan.FromMilliseconds(10),
    RestartSettleDelay = TimeSpan.FromMilliseconds(5),
    RestartStopTimeout = TimeSpan.FromMilliseconds(100),
    RestartStartTimeout = TimeSpan.FromMilliseconds(100),
    RestartProbeTimeout = TimeSpan.FromMilliseconds(50)
  };

  private sealed record Rig(
    EnsureGameRunningActionHandler Handler,
    FakeAdbGameOperations Adb);

  private static Rig Build(
    string? packageName = Package,
    string? serial = "emulator-5554",
    FakeAdbGameOperations? adb = null,
    EnsureGameRunningRestartOptions? options = null) {
    var sessions = new EnsureGameRunningActionHandlerTests.FakeSessionManager();
    sessions.Seed(new EmulatorSession { Id = "s1", GameId = "queue:q1", Status = SessionStatus.Running, DeviceSerial = serial });
    var queues = new EnsureGameRunningActionHandlerTests.FakeQueueRepository();
    queues.Seed(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "x", LinkedGameId = "g1" });
    var games = new EnsureGameRunningActionHandlerTests.FakeGameRepository();
    games.Seed(new GameArtifact { Id = "g1", Name = "MyGame", PackageName = packageName });
    var fake = adb ?? new FakeAdbGameOperations { ForegroundScript = () => Package };
    return new Rig(new EnsureGameRunningActionHandler(sessions, queues, games, fake, options ?? Fast), fake);
  }

  // ── US1: the restart ──────────────────────────────────────────────────────

  [Fact]
  public async Task RestartCallsStopThenStartThenPolls() {
    var rig = Build();

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.Restarted);
    result.IsSuccess.Should().BeTrue();
    result.ReasonCode.Should().Be("restarted");
    rig.Adb.CallOrder.Should().Equal("stop", "try-launch", "probe");
  }

  [Fact]
  public async Task RestartWaitsTheSettleTimeBetweenStopAndStart() {
    var options = Fast with { RestartSettleDelay = TimeSpan.FromMilliseconds(150) };
    var rig = Build(options: options);
    var clock = Stopwatch.StartNew();

    await rig.Handler.RestartAsync("s1");

    clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(120));
  }

  [Fact]
  public async Task RestartGivesRestartedForAGameThatWasRunning() {
    var rig = Build(adb: new FakeAdbGameOperations { ForegroundPackage = Package });

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.Restarted);
    rig.Adb.StoppedSerials.Should().ContainSingle();
  }

  [Fact]
  public async Task RestartGivesRestartedForAGameThatWasStopped() {
    // The game is not in front at first. It comes to the front after the start.
    var adb = new FakeAdbGameOperations();
    adb.ForegroundScript = () => adb.LaunchedPackages.Count > 0 ? Package : "com.android.launcher";
    var rig = Build(adb: adb);

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.Restarted);
  }

  [Fact]
  public async Task RestartUsesTheSessionSerialOnly() {
    var rig = Build(serial: "emulator-5560");

    await rig.Handler.RestartAsync("s1");

    rig.Adb.StoppedSerials.Should().Equal("emulator-5560");
    rig.Adb.LaunchSerials.Should().Equal("emulator-5560");
    rig.Adb.ProbeSerials.Should().OnlyContain(s => s == "emulator-5560");
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public async Task ABlankSerialGivesRestartNoDeviceAndNoAdbCall(string? serial) {
    var rig = Build(serial: serial);

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartNoDevice);
    result.ReasonCode.Should().Be("restart_no_device");
    rig.Adb.TotalCalls.Should().Be(0);
  }

  // ── US2: the plain path is unchanged ──────────────────────────────────────

  [Fact]
  public async Task ExecuteOnARunningGameMakesNoStopCallAndNoLaunchCall() {
    var rig = Build(adb: new FakeAdbGameOperations { ForegroundPackage = Package });

    var result = await rig.Handler.ExecuteAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.GameRunning);
    rig.Adb.CallOrder.Should().Equal("probe");
    rig.Adb.StoppedSerials.Should().BeEmpty();
    rig.Adb.LaunchedPackages.Should().BeEmpty();
  }

  [Fact]
  public async Task ExecuteWithAnUnsafePackageNameKeepsTheOldBehavior() {
    // The plain path does not check the name. It gives the same result as before feature 129.
    var rig = Build(packageName: "com.example.game; reboot", adb: new FakeAdbGameOperations { ForegroundPackage = "other" });

    var result = await rig.Handler.ExecuteAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.GameNotRunning);
    rig.Adb.LaunchedPackages.Should().ContainSingle().Which.Should().Be("com.example.game; reboot");
  }

  // ── US4: failures and time limits ─────────────────────────────────────────

  [Fact]
  public async Task AStopWithANonZeroExitGivesRestartStopFailedAndNoStart() {
    var rig = Build(adb: new FakeAdbGameOperations { StopResult = false });

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartStopFailed);
    result.ReasonCode.Should().Be("restart_stop_failed");
    result.IsSuccess.Should().BeFalse();
    rig.Adb.CallOrder.Should().Equal("stop");
  }

  [Fact]
  public async Task AStopThatThrowsGivesRestartStopFailedAndNoStart() {
    var rig = Build(adb: new FakeAdbGameOperations { StopThrows = new InvalidOperationException("adb died") });

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartStopFailed);
    rig.Adb.CallOrder.Should().Equal("stop");
  }

  [Fact]
  public async Task AStartThatReturnsFalseGivesRestartStartFailed() {
    var rig = Build(adb: new FakeAdbGameOperations { StartResult = false });

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartStartFailed);
    result.ReasonCode.Should().Be("restart_start_failed");
    rig.Adb.CallOrder.Should().Equal("stop", "try-launch");
  }

  [Fact]
  public async Task AStartThatThrowsGivesRestartStartFailed() {
    var rig = Build(adb: new FakeAdbGameOperations { StartThrows = new InvalidOperationException("adb died") });

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartStartFailed);
  }

  [Fact]
  public async Task AGameThatNeverReachesTheForegroundGivesRestartForegroundTimeout() {
    var rig = Build(adb: new FakeAdbGameOperations { ForegroundPackage = "com.android.launcher" });

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartForegroundTimeout);
    result.ReasonCode.Should().Be("restart_foreground_timeout");
    result.IsSuccess.Should().BeFalse();
  }

  [Fact]
  public async Task AProbeThatThrowsIsAMissAndThePollGoesOn() {
    var calls = 0;
    var adb = new FakeAdbGameOperations();
    adb.ForegroundScript = () => {
      calls++;
      if (calls < 3) throw new InvalidOperationException("dumpsys failed");
      return Package;
    };
    var rig = Build(adb: adb);

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.Restarted);
    calls.Should().BeGreaterThanOrEqualTo(3);
  }

  [Fact]
  public async Task ACancelFromTheCallerRethrowsAndGivesNoFailureCode() {
    var rig = Build(adb: new FakeAdbGameOperations { StopHangs = true }, options: Fast with { RestartStopTimeout = TimeSpan.FromSeconds(30) });
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(40));

    var act = async () => await rig.Handler.RestartAsync("s1", cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
  }

  [Fact]
  public async Task ACancelFromTheCallerDuringThePollRethrows() {
    var rig = Build(adb: new FakeAdbGameOperations { ForegroundPackage = "other" }, options: Fast with { RestartForegroundWait = TimeSpan.FromSeconds(30) });
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(60));

    var act = async () => await rig.Handler.RestartAsync("s1", cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
  }

  [Fact]
  public async Task AStopThatNeverReturnsEndsAtTheStopLimit() {
    var rig = Build(adb: new FakeAdbGameOperations { StopHangs = true });
    var clock = Stopwatch.StartNew();

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartStopFailed);
    clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    rig.Adb.CallOrder.Should().Equal("stop");
  }

  [Fact]
  public async Task AStopThatIgnoresTheCancelStillEndsAtTheStopLimit() {
    var rig = Build(adb: new FakeAdbGameOperations { StopHangs = true, HangsIgnoreCancel = true });
    var clock = Stopwatch.StartNew();

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartStopFailed);
    clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
  }

  [Fact]
  public async Task AStartThatNeverReturnsEndsAtTheStartLimit() {
    var rig = Build(adb: new FakeAdbGameOperations { StartHangs = true });
    var clock = Stopwatch.StartNew();

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartStartFailed);
    clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
  }

  [Fact]
  public async Task AProbeThatNeverReturnsCountsAsAMissAndTheRestartTimesOut() {
    var rig = Build(adb: new FakeAdbGameOperations { ProbeHangs = true });

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartForegroundTimeout);
  }

  [Fact]
  public async Task TheNormalCaseEndsWithinTheForegroundWaitPlusTenSeconds() {
    // SC-002: with the production limits the normal case ends within 30 s + 10 s. The fake game is
    // in front at once, so the restart ends at once.
    var rig = Build(options: new EnsureGameRunningRestartOptions { RestartSettleDelay = TimeSpan.FromMilliseconds(5) });
    var clock = Stopwatch.StartNew();

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.Restarted);
    clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(40));
  }

  [Fact]
  public async Task TheWorstCaseEndsUnderTheSumOfAllLimits() {
    // The stop and the start end at once. Each foreground probe never returns. The restart must end
    // under settle + foreground wait (each probe limit is cut to the time that is left), plus slack.
    var options = Fast with {
      RestartSettleDelay = TimeSpan.FromMilliseconds(50),
      RestartForegroundWait = TimeSpan.FromMilliseconds(400),
      RestartProbeTimeout = TimeSpan.FromMilliseconds(300)
    };
    var rig = Build(adb: new FakeAdbGameOperations { ProbeHangs = true, HangsIgnoreCancel = true }, options: options);
    var clock = Stopwatch.StartNew();

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.RestartForegroundTimeout);
    var bound = options.RestartStopTimeout + options.RestartSettleDelay + options.RestartStartTimeout + options.RestartForegroundWait;
    clock.Elapsed.Should().BeLessThan(bound + TimeSpan.FromSeconds(2));
  }

  // ── US4: no device call when the game cannot be resolved ──────────────────

  [Fact]
  public async Task NoQueueContextMakesNoDeviceCall() {
    var adb = new FakeAdbGameOperations();
    var handler = new EnsureGameRunningActionHandler(
      new EnsureGameRunningActionHandlerTests.FakeSessionManager(),
      new EnsureGameRunningActionHandlerTests.FakeQueueRepository(),
      new EnsureGameRunningActionHandlerTests.FakeGameRepository(),
      adb,
      Fast);

    var result = await handler.RestartAsync("missing");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.NoQueueContext);
    adb.TotalCalls.Should().Be(0);
  }

  [Fact]
  public async Task NoLinkedGameMakesNoDeviceCall() {
    var sessions = new EnsureGameRunningActionHandlerTests.FakeSessionManager();
    sessions.Seed(new EmulatorSession { Id = "s1", GameId = "queue:q1", Status = SessionStatus.Running, DeviceSerial = "emulator-5554" });
    var queues = new EnsureGameRunningActionHandlerTests.FakeQueueRepository();
    queues.Seed(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "x" });
    var adb = new FakeAdbGameOperations();
    var handler = new EnsureGameRunningActionHandler(sessions, queues, new EnsureGameRunningActionHandlerTests.FakeGameRepository(), adb, Fast);

    var result = await handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.NoLinkedGame);
    adb.TotalCalls.Should().Be(0);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("com.example.game; reboot")]
  [InlineData("com.example game")]
  [InlineData("$(id)")]
  [InlineData("1com.example")]
  public async Task AMissingOrUnsafePackageNameGivesNoPackageNameAndNoDeviceCall(string? packageName) {
    var rig = Build(packageName: packageName);

    var result = await rig.Handler.RestartAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.NoPackageName);
    rig.Adb.TotalCalls.Should().Be(0);
    rig.Adb.StoppedSerials.Should().BeEmpty();
    rig.Adb.LaunchedPackages.Should().BeEmpty();
  }

  [Theory]
  [InlineData("com.example.game", true)]
  [InlineData("com.example.game_2", true)]
  [InlineData("a", true)]
  [InlineData("", false)]
  [InlineData("com..example", false)]
  [InlineData("com.example.", false)]
  [InlineData("com.example -x", false)]
  public void ThePackageNameCheckAcceptsPlainNamesOnly(string name, bool expected) {
    AdbGameOperations.IsSafePackageName(name).Should().Be(expected);
  }

  [Fact]
  public async Task TheAdbWrapperRunsNoProcessForABlankSerialOrAnUnsafeName() {
    var ops = new AdbGameOperations();

    (await ops.ForceStopAppAsync("", Package)).Should().BeFalse();
    (await ops.TryLaunchAppAsync("  ", Package)).Should().BeFalse();
    (await ops.ForceStopAppAsync("emulator-5554", "bad name; id")).Should().BeFalse();
    (await ops.TryLaunchAppAsync("emulator-5554", "$(id)")).Should().BeFalse();
  }
}

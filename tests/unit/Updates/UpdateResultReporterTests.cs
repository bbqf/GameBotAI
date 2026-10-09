using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Service.Hosted;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateResultReporterTests {
  private readonly FakeResultStore _store = new();
  private readonly FakeInstalledVersion _installed = new("1.7.0.430");

  private static UpdateAttempt Attempt(UpdateState state) => new() {
    AttemptId = Guid.NewGuid(),
    TargetVersion = "1.7.0.430",
    FromVersion = "1.7.0.412",
    State = state,
    StartedAtUtc = new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)
  };

  private UpdateResultReporter Reporter(TimeSpan? wait = null) =>
    new(
      _store,
      _installed,
      TimeProvider.System,
      NullLogger<UpdateResultReporter>.Instance,
      wait ?? TimeSpan.FromMilliseconds(40),
      TimeSpan.FromMilliseconds(10));

  [Fact]
  public async Task NoFileMeansNoResult() {
    var reporter = Reporter();

    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult.Should().BeNull();
    _store.MarkReportedCalls.Should().Be(0);
  }

  [Fact]
  public async Task SucceededResultIsKeptRenamedAndCleaned() {
    _store.Stored = Attempt(UpdateState.Succeeded);
    var reporter = Reporter();

    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult!.State.Should().Be(UpdateState.Succeeded);
    _store.MarkReportedCalls.Should().Be(1);
    _store.CleanupCalls.Should().Be(1);
  }

  [Fact]
  public async Task FailedResultIsKeptWithoutCleanup() {
    _store.Stored = Attempt(UpdateState.Failed) with { ErrorCode = "update_install_failed", MsiexecExitCode = 1603 };
    var reporter = Reporter();

    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult!.State.Should().Be(UpdateState.Failed);
    reporter.LastResult.MsiexecExitCode.Should().Be(1603);
    _store.CleanupCalls.Should().Be(0);
  }

  [Fact]
  public async Task UpdaterThatFinishesLateIsWaitedFor() {
    _store.ReadQueue.Enqueue(Attempt(UpdateState.Restarting));
    _store.ReadQueue.Enqueue(Attempt(UpdateState.Restarting));
    _store.Stored = Attempt(UpdateState.Succeeded);
    var reporter = Reporter(TimeSpan.FromSeconds(2));

    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult!.State.Should().Be(UpdateState.Succeeded);
  }

  [Theory]
  [InlineData(UpdateState.Installing)]
  [InlineData(UpdateState.Restarting)]
  public async Task NoFinalStateAndTheTargetVersionRunsMeansSuccess(UpdateState state) {
    _store.Stored = Attempt(state);
    var reporter = Reporter();

    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult!.State.Should().Be(UpdateState.Succeeded);
    reporter.LastResult.FinishedAtUtc.Should().NotBeNull();
  }

  [Fact]
  public async Task NoFinalStateAndAnOldVersionRunsMeansFailure() {
    _installed.Version = GameBot.Domain.Versioning.SemanticVersion.Parse("1.7.0.412");
    _store.Stored = Attempt(UpdateState.Installing);
    var reporter = Reporter();

    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult!.State.Should().Be(UpdateState.Failed);
    reporter.LastResult.ErrorCode.Should().Be("update_install_failed");
    reporter.LastResult.ErrorHint.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task HostedServiceLoadsTheResultAtStart() {
    _store.Stored = Attempt(UpdateState.Succeeded);
    var reporter = Reporter();
    var service = new UpdateResultReportingService(reporter, NullLogger<UpdateResultReportingService>.Instance);

    await service.StartAsync(CancellationToken.None).ConfigureAwait(true);
    await service.StopAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult.Should().NotBeNull();
  }

  [Fact]
  public async Task DefaultConstructorWorks() {
    var reporter = new UpdateResultReporter(_store, _installed, TimeProvider.System, NullLogger<UpdateResultReporter>.Instance);

    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult.Should().BeNull();
  }
}

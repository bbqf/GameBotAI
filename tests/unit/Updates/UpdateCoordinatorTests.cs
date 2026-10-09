using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateCoordinatorTests : IDisposable {
  private readonly string _root = UpdateTestData.NewTempDirectory();
  private readonly List<string> _steps = new();
  private readonly FakeReleaseClient _client = new() { Release = UpdateTestData.Release("1.7.0.430", size: 1000) };
  private readonly FakeDownloader _downloader = new();
  private readonly FakeLauncher _launcher = new();
  private readonly FakeResultStore _store = new();
  private readonly FakeQueueStopper _queues = new();
  private readonly FakeFreeSpace _space = new();
  private readonly FakeLifetime _lifetime = new();
  private readonly FakeInstalledVersion _installed = new("1.7.0.412");
  private readonly UpdateCheckService _check;
  private readonly UpdateCoordinator _coordinator;

  public UpdateCoordinatorTests() {
    _downloader.Steps = _steps;
    _launcher.Steps = _steps;
    _queues.Steps = _steps;
    _lifetime.Steps = _steps;
    _check = new UpdateCheckService(_client, _installed, TimeProvider.System, NullLogger<UpdateCheckService>.Instance);
    _coordinator = new UpdateCoordinator(
      _check, _downloader, _launcher, _store, _installed, _queues, _space,
      new UpdatePaths(_root), _lifetime, TimeProvider.System, NullLogger<UpdateCoordinator>.Instance);
  }

  public void Dispose() {
    _coordinator.Dispose();
    _lifetime.Dispose();
    Directory.Delete(_root, recursive: true);
    GC.SuppressFinalize(this);
  }

  private async Task<InstallStartResult> CheckAndStart(string target = "1.7.0.430") {
    await _check.CheckAsync(CancellationToken.None).ConfigureAwait(true);
    return _coordinator.TryStartInstall(target);
  }

  [Fact]
  public async Task InstallDownloadsVerifiesLaunchesAndStopsInOrder() {
    var start = await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    start.Outcome.Should().Be(InstallStartOutcome.Started);
    start.Attempt!.State.Should().Be(UpdateState.Downloading);
    start.Attempt.FromVersion.Should().Be("1.7.0.412");
    start.Attempt.TargetVersion.Should().Be("1.7.0.430");
    _steps.Should().Equal("download", "verify", "launch", "stopApplication", "stopQueues");
    _coordinator.CurrentAttempt!.State.Should().Be(UpdateState.Installing);
    _store.Stored!.State.Should().Be(UpdateState.Installing);
  }

  [Fact]
  public async Task LauncherGetsTheRightRequest() {
    var start = await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    var request = _launcher.Requests.Should().ContainSingle().Which;
    request.AttemptId.Should().Be(start.Attempt!.AttemptId);
    request.MsiPath.Should().EndWith("GameBot-1.7.0.430.msi");
    request.FromVersion.Should().Be("1.7.0.412");
    request.TargetVersion.Should().Be("1.7.0.430");
    request.StartedAtUtc.Should().Be(start.Attempt.StartedAtUtc);
  }

  [Fact]
  public async Task QueuesStopAfterTheHostIsAskedToStopAndWithoutWaitingForIdle() {
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    // The host stopping token is already cancelled when the queues stop. The queue engine then
    // keeps the running record, so queues with resumeOnServiceStart start again after the restart.
    _steps.IndexOf("stopApplication").Should().BeLessThan(_steps.IndexOf("stopQueues"));
    _queues.Calls.Should().Be(1);
    _lifetime.ApplicationStopping.IsCancellationRequested.Should().BeTrue();
  }

  [Fact]
  public async Task FailedQueueStopDoesNotFailTheUpdate() {
    _queues.Failure = new InvalidOperationException("stop failed");
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    _coordinator.CurrentAttempt!.State.Should().Be(UpdateState.Installing);
  }

  [Fact]
  public void NoCheckMeansNotAvailable() {
    var start = _coordinator.TryStartInstall("1.7.0.430");

    start.Outcome.Should().Be(InstallStartOutcome.NotAvailable);
    _coordinator.IsInstallActive.Should().BeFalse();
  }

  [Theory]
  [InlineData("1.7.0.431")]
  [InlineData("1.7.0.412")]
  [InlineData("bad")]
  [InlineData("")]
  public async Task OtherVersionThanTheCheckedOneIsNotAvailable(string target) {
    var start = await CheckAndStart(target).ConfigureAwait(true);

    start.Outcome.Should().Be(InstallStartOutcome.NotAvailable);
    _steps.Should().BeEmpty();
  }

  [Fact]
  public async Task SecondRequestDuringAnInstallIsInProgress() {
    var release = new TaskCompletionSource();
    _downloader.BeforeDownload = () => release.Task;
    await CheckAndStart().ConfigureAwait(true);

    var second = _coordinator.TryStartInstall("1.7.0.430");

    second.Outcome.Should().Be(InstallStartOutcome.InProgress);
    _coordinator.IsInstallActive.Should().BeTrue();
    release.SetResult();
    await _coordinator.Running.ConfigureAwait(true);
  }

  [Fact]
  public async Task LowDiskSpaceIsRefusedBeforeTheDownload() {
    _space.Free = 2999;

    var start = await CheckAndStart().ConfigureAwait(true);

    start.Outcome.Should().Be(InstallStartOutcome.DiskSpace);
    _steps.Should().BeEmpty();
    _coordinator.IsInstallActive.Should().BeFalse();
  }

  [Fact]
  public async Task ThreeTimesTheSizeIsEnoughDiskSpace() {
    _space.Free = 3000;

    var start = await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    start.Outcome.Should().Be(InstallStartOutcome.Started);
  }

  [Fact]
  public async Task ChecksumMismatchFailsTheAttemptAndDoesNotStartTheUpdater() {
    _downloader.VerifyFailure = new UpdateFailureException(new UpdateError("update_checksum_mismatch", "bad checksum", "hint"));
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    var attempt = _coordinator.CurrentAttempt!;
    attempt.State.Should().Be(UpdateState.Failed);
    attempt.ErrorCode.Should().Be("update_checksum_mismatch");
    attempt.ErrorHint.Should().Be("hint");
    attempt.FinishedAtUtc.Should().NotBeNull();
    _launcher.Requests.Should().BeEmpty();
    _lifetime.StopCalls.Should().Be(0);
    _queues.Calls.Should().Be(0);
    _coordinator.IsInstallActive.Should().BeFalse();
  }

  [Fact]
  public async Task DownloadFailureFailsTheAttempt() {
    _downloader.DownloadFailure = new UpdateFailureException(new UpdateError("update_download_failed", "stopped", "hint"));
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    _coordinator.CurrentAttempt!.ErrorCode.Should().Be("update_download_failed");
    _steps.Should().Equal("download");
  }

  [Fact]
  public async Task LauncherFailureFailsTheAttemptAndRemovesTheInstallingFile() {
    _launcher.Failure = new UpdateFailureException(new UpdateError("update_install_failed", "no updater", "hint"));
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    _coordinator.CurrentAttempt!.State.Should().Be(UpdateState.Failed);
    _store.MarkReportedCalls.Should().Be(1);
    _store.Stored.Should().BeNull();
    _lifetime.StopCalls.Should().Be(0);
  }

  [Fact]
  public async Task FailedAttemptAllowsANewAttempt() {
    _downloader.DownloadFailure = new UpdateFailureException(new UpdateError("update_download_failed", "stopped", null));
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);
    _downloader.DownloadFailure = null;

    var again = _coordinator.TryStartInstall("1.7.0.430");
    await _coordinator.Running.ConfigureAwait(true);

    again.Outcome.Should().Be(InstallStartOutcome.Started);
    _coordinator.CurrentAttempt!.State.Should().Be(UpdateState.Installing);
  }

  [Fact]
  public async Task IoErrorFailsTheAttemptWithAFixHint() {
    _downloader.BeforeDownload = () => throw new IOException("disk full");
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    var attempt = _coordinator.CurrentAttempt!;
    attempt.ErrorCode.Should().Be("update_install_failed");
    attempt.ErrorHint.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task CancelFailsTheAttempt() {
    _downloader.BeforeDownload = () => throw new OperationCanceledException();
    await CheckAndStart().ConfigureAwait(true);
    await _coordinator.Running.ConfigureAwait(true);

    _coordinator.CurrentAttempt!.State.Should().Be(UpdateState.Failed);
  }

  [Fact]
  public void NoAttemptBeforeTheFirstInstall() {
    _coordinator.CurrentAttempt.Should().BeNull();
    _coordinator.IsInstallActive.Should().BeFalse();
  }
}

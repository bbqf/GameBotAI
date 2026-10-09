using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateResultStoreTests : IDisposable {
  private readonly string _root = UpdateTestData.NewTempDirectory();
  private readonly UpdatePaths _paths;
  private readonly UpdateResultStore _store;

  public UpdateResultStoreTests() {
    _paths = new UpdatePaths(_root);
    _store = new UpdateResultStore(_paths, NullLogger<UpdateResultStore>.Instance);
  }

  public void Dispose() {
    Directory.Delete(_root, recursive: true);
    GC.SuppressFinalize(this);
  }

  private static UpdateAttempt Attempt(UpdateState state = UpdateState.Succeeded) => new() {
    AttemptId = Guid.NewGuid(),
    TargetVersion = "1.7.0.430",
    FromVersion = "1.7.0.412",
    State = state,
    StartedAtUtc = new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero),
    ErrorCode = state == UpdateState.Failed ? "update_install_failed" : null,
    MsiexecExitCode = 0
  };

  [Fact]
  public async Task MissingFileReadsAsNull() {
    (await _store.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task WrittenAttemptReadsBack() {
    var attempt = Attempt(UpdateState.Failed);

    await _store.WriteAsync(attempt, CancellationToken.None).ConfigureAwait(true);
    var read = await _store.ReadAsync(CancellationToken.None).ConfigureAwait(true);

    read.Should().Be(attempt);
    File.Exists(_paths.ResultFile + ".tmp").Should().BeFalse();
  }

  [Fact]
  public async Task StateIsWrittenAsCamelCaseText() {
    await _store.WriteAsync(Attempt(), CancellationToken.None).ConfigureAwait(true);

    var text = await File.ReadAllTextAsync(_paths.ResultFile).ConfigureAwait(true);
    text.Should().Contain("\"state\": \"succeeded\"");
  }

  [Fact]
  public async Task FileFromTheUpdaterProgramReads() {
    Directory.CreateDirectory(_paths.Directory);
    await File.WriteAllTextAsync(_paths.ResultFile,
      "{\"attemptId\":\"6f1c0000-0000-0000-0000-000000000001\",\"targetVersion\":\"1.7.0.430\",\"fromVersion\":\"1.7.0.412\","
      + "\"state\":\"failed\",\"startedAtUtc\":\"2026-10-09T10:00:00+00:00\",\"errorCode\":\"update_install_failed\",\"msiexecExitCode\":1603}").ConfigureAwait(true);

    var read = await _store.ReadAsync(CancellationToken.None).ConfigureAwait(true);

    read!.State.Should().Be(UpdateState.Failed);
    read.MsiexecExitCode.Should().Be(1603);
    read.ErrorCode.Should().Be("update_install_failed");
  }

  [Fact]
  public async Task BrokenFileReadsAsNull() {
    Directory.CreateDirectory(_paths.Directory);
    await File.WriteAllTextAsync(_paths.ResultFile, "{ not json").ConfigureAwait(true);

    (await _store.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task MarkReportedRenamesTheFile() {
    await _store.WriteAsync(Attempt(), CancellationToken.None).ConfigureAwait(true);

    _store.MarkReported();

    File.Exists(_paths.ResultFile).Should().BeFalse();
    File.Exists(_paths.ReportedResultFile).Should().BeTrue();
  }

  [Fact]
  public async Task MarkReportedReplacesAnOlderReportedFile() {
    await _store.WriteAsync(Attempt(), CancellationToken.None).ConfigureAwait(true);
    _store.MarkReported();
    await _store.WriteAsync(Attempt(UpdateState.Failed), CancellationToken.None).ConfigureAwait(true);

    _store.MarkReported();

    File.Exists(_paths.ReportedResultFile).Should().BeTrue();
    File.Exists(_paths.ResultFile).Should().BeFalse();
  }

  [Fact]
  public void MarkReportedWithoutAFileDoesNothing() {
    var act = () => _store.MarkReported();

    act.Should().NotThrow();
  }

  [Fact]
  public void CleanupWithoutAFolderDoesNothing() {
    var act = () => _store.Cleanup(DateTimeOffset.UtcNow);

    act.Should().NotThrow();
  }

  [Fact]
  public async Task LockedResultFileReadsAsNull() {
    await _store.WriteAsync(Attempt(), CancellationToken.None).ConfigureAwait(true);
    using var locked = new FileStream(_paths.ResultFile, FileMode.Open, FileAccess.Read, FileShare.None);

    (await _store.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task MarkReportedOfALockedFileDoesNotThrow() {
    await _store.WriteAsync(Attempt(), CancellationToken.None).ConfigureAwait(true);
    using var locked = new FileStream(_paths.ResultFile, FileMode.Open, FileAccess.Read, FileShare.None);

    var act = () => _store.MarkReported();

    act.Should().NotThrow();
    File.Exists(_paths.ResultFile).Should().BeTrue();
  }

  [Fact]
  public void CleanupOfALockedDownloadDoesNotThrow() {
    Directory.CreateDirectory(_paths.Directory);
    File.WriteAllText(_paths.MsiPath("1.7.0.430"), "x");
    using var locked = new FileStream(_paths.MsiPath("1.7.0.430"), FileMode.Open, FileAccess.Read, FileShare.None);

    var act = () => _store.Cleanup(DateTimeOffset.UtcNow);

    act.Should().NotThrow();
  }
  [Fact]
  public void CleanupDeletesDownloadsUpdaterCopyAndOldLogsOnly() {
    Directory.CreateDirectory(_paths.UpdaterCopyDirectory);
    File.WriteAllText(Path.Combine(_paths.UpdaterCopyDirectory, "GameBot.Updater.dll"), "x");
    File.WriteAllText(_paths.MsiPath("1.7.0.430"), "x");
    var oldLog = _paths.MsiexecLogPath(Guid.NewGuid());
    var freshLog = _paths.MsiexecLogPath(Guid.NewGuid());
    File.WriteAllText(oldLog, "x");
    File.WriteAllText(freshLog, "x");
    File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-31));
    File.WriteAllText(_paths.ReportedResultFile, "{}");

    _store.Cleanup(DateTimeOffset.UtcNow);

    File.Exists(_paths.MsiPath("1.7.0.430")).Should().BeFalse();
    Directory.Exists(_paths.UpdaterCopyDirectory).Should().BeFalse();
    File.Exists(oldLog).Should().BeFalse();
    File.Exists(freshLog).Should().BeTrue();
    File.Exists(_paths.ReportedResultFile).Should().BeTrue();
  }
}

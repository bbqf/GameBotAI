using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateLogTests {
  private sealed class CapturingLogger : ILogger {
    public List<(int EventId, LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
      Entries.Add((eventId.Id, logLevel, formatter(state, exception)));
    }
  }

  [Fact]
  public void EveryLogMethodWritesOneEntryWithItsValues() {
    var logger = new CapturingLogger();
    var id = Guid.Parse("6f1c0000-0000-0000-0000-000000000001");

    UpdateLog.TagIgnored(logger, "nightly");
    UpdateLog.CheckFinished(logger, UpdateCheckStatus.UpdateAvailable, new SemanticVersion(1, 7, 0, 412), new SemanticVersion(1, 7, 0, 430));
    UpdateLog.CheckFailed(logger, "update_network_error", "no network");
    UpdateLog.InstallStarted(logger, id, "1.7.0.412", "1.7.0.430");
    UpdateLog.InstallState(logger, id, UpdateState.Verifying);
    UpdateLog.InstallFailed(logger, id, "update_install_failed", "failed");
    UpdateLog.QueueStopFailed(logger, "stop failed");
    UpdateLog.ResultReported(logger, id, UpdateState.Succeeded);
    UpdateLog.ResultUnreadable(logger, "bad file");
    UpdateLog.CleanupFailed(logger, "locked");
    UpdateLog.QueueStopped(logger, "q1");

    logger.Entries.Select(e => e.EventId).Should().Equal(7400, 7401, 7402, 7403, 7404, 7405, 7406, 7407, 7408, 7409, 7410);
    logger.Entries[0].Message.Should().Contain("nightly");
    logger.Entries[1].Message.Should().Contain("UpdateAvailable").And.Contain("1.7.0.430");
    logger.Entries[4].Message.Should().Contain("Verifying");
    logger.Entries[5].Level.Should().Be(LogLevel.Error);
    logger.Entries[10].Message.Should().Contain("q1");
  }
}

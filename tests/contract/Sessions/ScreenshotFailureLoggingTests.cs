#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.Liveness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.ContractTests.Sessions;

/// <summary>
/// Feature 141 (FR-017): a failed screenshot request writes one log entry with the session id and the
/// result. A successful screenshot request writes none. The default logging policy (Warning) lets the entry
/// pass, so the test needs no change of the logging gate.
/// </summary>
public sealed class ScreenshotFailureLoggingTests : MonitoringContractTestBase {
  private sealed class CapturingProvider : ILoggerProvider {
    public List<(string Category, LogLevel Level, string Message)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);
    public void Dispose() { }

    private sealed class CapturingLogger : ILogger {
      private readonly CapturingProvider _owner;
      private readonly string _category;
      public CapturingLogger(CapturingProvider owner, string category) { _owner = owner; _category = category; }
      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        lock (_owner.Entries) _owner.Entries.Add((_category, logLevel, formatter(state, exception)));
      }
    }

    public List<string> Monitoring() {
      lock (Entries) {
        return Entries.Where(e => e.Category.EndsWith("MonitoringLoggingTag", StringComparison.Ordinal)).Select(e => e.Message).ToList();
      }
    }
  }

  [Fact]
  public async Task FailedSnapshotRequestIsLoggedOnceWithTheSessionIdAndSuccessIsNot() {
    var sessions = new LivenessFakeSessionManager { HangSnapshots = false };
    var good = sessions.Add("emulator-5558");
    using var provider = new CapturingProvider();
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.AddSingleton<ILoggerProvider>(provider);
    });
    var client = Client(app);

    var ok = await client.GetAsync(new Uri($"/api/sessions/{good.Id}/snapshot", UriKind.Relative));
    ok.StatusCode.Should().Be(HttpStatusCode.OK);
    provider.Monitoring().Should().BeEmpty();

    var missing = await client.GetAsync(new Uri("/api/sessions/no-such-session/snapshot", UriKind.Relative));
    missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

    var entries = provider.Monitoring();
    entries.Should().ContainSingle();
    entries[0].Should().Contain("no-such-session").And.Contain("not_found");
  }

  [Fact]
  public async Task SnapshotTimeoutIsLoggedWithTheSessionIdAndTheResult() {
    var sessions = new LivenessFakeSessionManager { HangSnapshots = true };
    var session = sessions.Add("emulator-5558");
    var liveness = new FixedLivenessService { Options = new DeviceLivenessOptions { CaptureTimeoutMs = 100 } };
    using var provider = new CapturingProvider();
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.RemoveAll<ISessionLivenessService>();
      services.AddSingleton<ISessionLivenessService>(liveness);
      services.AddSingleton<ILoggerProvider>(provider);
    });

    var resp = await Client(app).GetAsync(new Uri($"/api/sessions/{session.Id}/snapshot", UriKind.Relative));

    resp.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    var entries = provider.Monitoring();
    entries.Should().ContainSingle();
    entries[0].Should().Contain(session.Id).And.Contain("capture_timeout");
  }
}

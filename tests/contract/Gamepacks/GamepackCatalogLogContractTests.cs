using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Services.Logging;
using GameBot.Domain.Logging;
using GameBot.UnitTests.Gamepacks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 147 (T025, FR-019, SC-004): one log message for each list and install, and no token in any log message.</summary>
public sealed class GamepackCatalogLogContractTests {
  private const string Canary = "CANARY-LOG-TOKEN-77aa-do-not-log";

  private sealed class CapturingProvider : ILoggerProvider {
    public List<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose() {
    }

    public List<string> Catalog() {
      lock (Entries) {
        return Entries.Where(e => e.Category.Contains("GamepackCatalog", StringComparison.Ordinal)).Select(e => e.Message).ToList();
      }
    }

    public List<string> All() {
      lock (Entries) {
        return Entries.Select(e => e.Message).ToList();
      }
    }

    private sealed class CapturingLogger : ILogger {
      private readonly CapturingProvider _owner;
      private readonly string _category;

      public CapturingLogger(CapturingProvider owner, string category) {
        _owner = owner;
        _category = category;
      }

      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        lock (_owner.Entries) {
          _owner.Entries.Add((_category, logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
        }
      }
    }
  }

  [Fact]
  public async Task EachListAndInstallWritesOneMessageAndNoTokenIsLogged() {
    using var provider = new CapturingProvider();
    using var host = new GamepackContractHost(configure: (services, _) => services.AddSingleton<ILoggerProvider>(provider));
    // The logging gate lets only warnings pass by default. Open the component first.
    host.Services.GetRequiredService<ILoggingPolicyApplier>().ApplyComponent(new LoggingComponentSetting {
      Name = "GameBot.Domain.Gamepacks.Catalog",
      Enabled = true,
      EffectiveLevel = LogLevel.Information,
      DefaultLevel = LogLevel.Information
    });
    var folder = Path.Combine(host.Root, "catalog");
    new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0")).WriteTo(folder);
    await host.SetCatalogAsync(folder, token: Canary);

    (await host.CatalogListAsync()).EnsureSuccessStatusCode();
    provider.Catalog().Should().ContainSingle().Which.Should().Contain("list");

    (await host.CatalogInstallAsync("pns-daily", "1.0.0")).EnsureSuccessStatusCode();
    var messages = provider.Catalog();
    messages.Should().HaveCount(2);
    messages[1].Should().Contain("pns-daily").And.Contain("1.0.0").And.Contain("Ok");

    await host.SetCatalogAsync("https://127.0.0.1:1/gp", token: Canary);
    await host.CatalogListAsync();
    provider.Catalog().Should().HaveCount(3);
    provider.Catalog()[2].Should().Contain("unreachable");

    provider.All().Should().NotContain(m => m.Contains(Canary, StringComparison.Ordinal));
  }
}

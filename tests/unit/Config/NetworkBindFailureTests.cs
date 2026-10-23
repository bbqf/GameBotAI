using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using GameBot.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class NetworkBindFailureTests {
  [Fact]
  public async Task StartingOnAPortThatAnotherListenerHoldsIsSeenAsAddressInUse() {
    using var holder = new TcpListener(IPAddress.Loopback, 0);
    holder.Start();
    var port = ((IPEndPoint)holder.LocalEndpoint).Port;

    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    await using var app = builder.Build();

    var act = async () => await app.StartAsync().ConfigureAwait(false);

    var thrown = (await act.Should().ThrowAsync<Exception>().ConfigureAwait(false)).Which;
    NetworkBindFailure.IsAddressInUse(thrown).Should().BeTrue();
  }

  [Fact]
  public void AnUnrelatedExceptionIsNotAddressInUse() {
    NetworkBindFailure.IsAddressInUse(new InvalidOperationException("boom")).Should().BeFalse();
  }

  [Fact]
  public void ASocketErrorInsideAnotherExceptionIsFound() {
    var inner = new SocketException((int)SocketError.AddressAlreadyInUse);

    NetworkBindFailure.IsAddressInUse(new IOException("Failed to bind", inner)).Should().BeTrue();
  }

  [Fact]
  public void TheLogTextNamesTheHostThePortAndTheSources() {
    var address = new GameBotServiceSetup.ListenAddress("0.0.0.0", 9090, "NetworkFile", "Environment", new List<string>());

    var text = NetworkBindFailure.Describe(address);

    text.Should().Contain("0.0.0.0:9090").And.Contain("NetworkFile").And.Contain("Environment");
  }

  [Fact]
  public void TheExitCodeIsNotZero() {
    NetworkBindFailure.ExitCode.Should().NotBe(0);
  }

  [Fact]
  public void LogWritesAnErrorWithTheTextAndTheException() {
    var logger = new CapturingLogger();
    var exception = new IOException("Failed to bind");

    NetworkBindFailure.Log(logger, exception, "The bot cannot listen on 127.0.0.1:8080 because the address is in use.");

    var entry = logger.Entries.Should().ContainSingle().Subject;
    entry.Level.Should().Be(LogLevel.Error);
    entry.Message.Should().Contain("127.0.0.1:8080");
    entry.Exception.Should().BeSameAs(exception);
  }

  private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

  private sealed class CapturingLogger : ILogger {
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
      ArgumentNullException.ThrowIfNull(formatter);
      Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
  }
}

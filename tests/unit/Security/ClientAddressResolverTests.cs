using System.Net;
using FluentAssertions;
using GameBot.Service.Security;
using GameBot.UnitTests.Queues;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GameBot.UnitTests.Security;

public sealed class ClientAddressResolverTests : IDisposable {
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "gamebot-resolver-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

  public void Dispose() {
    if (Directory.Exists(_dir)) {
      Directory.Delete(_dir, recursive: true);
    }
  }

  private ClientAddressResolver NewResolver(string trusted) =>
    new(AuthOptions.Create("true", "https://bot.example.com", trusted, null), new SignInRecorder(_dir, _clock));

  private string Resolve(string trusted, string remote, string? forwardedFor) =>
    Resolve(NewResolver(trusted), remote, forwardedFor);

  private static string Resolve(ClientAddressResolver resolver, string remote, string? forwardedFor) {
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
    if (forwardedFor is not null) {
      context.Request.Headers["X-Forwarded-For"] = forwardedFor;
    }

    return resolver.Resolve(context);
  }

  [Fact]
  public void TrustedProxyGivesLastEntryThatIsNotTrusted() =>
    Resolve("192.168.1.5", "192.168.1.5", "6.6.6.6, 8.8.8.8").Should().Be("8.8.8.8");

  [Fact]
  public void ChainOfTwoTrustedProxiesSkipsBoth() =>
    Resolve("192.168.1.5,192.168.1.6", "192.168.1.5", "8.8.8.8, 192.168.1.6").Should().Be("8.8.8.8");

  [Fact]
  public void LoopbackGatewayGivesLastEntry() =>
    Resolve("", "127.0.0.1", "9.9.9.9").Should().Be("9.9.9.9");

  [Fact]
  public void TrustedProxyWithNoHeaderGivesProxyAddressAndRecordsOneTime() {
    var resolver = NewResolver("192.168.1.5");
    Resolve(resolver, "192.168.1.5", null).Should().Be("192.168.1.5");
    Resolve(resolver, "192.168.1.5", null).Should().Be("192.168.1.5");

    File.ReadAllLines(Path.Combine(_dir, "sign-in-records.jsonl")).Should().ContainSingle()
      .Which.Should().Contain("proxy_no_client_address");
  }

  [Fact]
  public void RequestThatIsNotFromProxyIgnoresForgedHeader() =>
    Resolve("192.168.1.5", "203.0.113.7", "1.1.1.1").Should().Be("203.0.113.7");

  [Fact]
  public void HeaderWithOnlyTrustedEntriesGivesRemoteAddress() =>
    Resolve("192.168.1.5", "192.168.1.5", "192.168.1.5").Should().Be("192.168.1.5");
}


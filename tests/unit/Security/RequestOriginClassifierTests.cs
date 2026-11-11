using System.Net;
using FluentAssertions;
using GameBot.Service.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GameBot.UnitTests.Security;

public sealed class RequestOriginClassifierTests {
  private const string PublicHost = "bot.example.com";

  private static RequestOrigin Classify(
    string? remote,
    string host,
    string? trustedProxies = null,
    params string[] headers) {
    var options = AuthOptions.Create("true", "https://" + PublicHost, trustedProxies, null);
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);
    context.Request.Host = new HostString(host);
    foreach (var header in headers) {
      context.Request.Headers[header] = "x";
    }

    return new RequestOriginClassifier(options).Classify(context);
  }

  [Fact]
  public void NullAddressWithLoopbackHostIsLocalMachine() =>
    Classify(null, "localhost").Should().Be(RequestOrigin.LocalMachine);

  [Theory]
  [InlineData("127.0.0.1", "localhost:8080")]
  [InlineData("127.0.0.1", "127.0.0.1:8080")]
  [InlineData("::1", "[::1]:8080")]
  [InlineData("::ffff:127.0.0.1", "localhost")]
  public void LoopbackAddressWithLoopbackHostIsLocalMachine(string remote, string host) =>
    Classify(remote, host).Should().Be(RequestOrigin.LocalMachine);

  [Fact]
  public void LoopbackWithForwardedForIsInternet() =>
    Classify("127.0.0.1", "localhost", null, "X-Forwarded-For").Should().Be(RequestOrigin.Internet);

  [Fact]
  public void LoopbackWithPublicHostIsInternet() =>
    Classify("127.0.0.1", PublicHost).Should().Be(RequestOrigin.Internet);

  [Theory]
  [InlineData("192.168.1.30")]
  [InlineData("10.1.2.3")]
  [InlineData("172.20.0.9")]
  [InlineData("169.254.3.4")]
  [InlineData("fd00::5")]
  [InlineData("fe80::1")]
  [InlineData("::ffff:192.168.1.30")]
  public void PrivateAddressIsHomeNetwork(string remote) =>
    Classify(remote, "192.168.1.20:8080").Should().Be(RequestOrigin.HomeNetwork);

  [Fact]
  public void PrivateAddressWithPublicHostIsInternet() =>
    Classify("192.168.1.30", PublicHost).Should().Be(RequestOrigin.Internet);

  [Theory]
  [InlineData("8.8.8.8")]
  [InlineData("172.32.0.1")]
  [InlineData("2001:db8::1")]
  public void PublicAddressIsInternet(string remote) =>
    Classify(remote, "192.168.1.20").Should().Be(RequestOrigin.Internet);

  [Theory]
  [InlineData("Forwarded")]
  [InlineData("X-Forwarded-For")]
  [InlineData("X-Forwarded-Host")]
  [InlineData("X-Forwarded-Proto")]
  [InlineData("X-Real-IP")]
  [InlineData("CF-Connecting-IP")]
  [InlineData("True-Client-IP")]
  [InlineData("Via")]
  public void EachForwardHeaderAloneGivesInternet(string header) =>
    Classify("192.168.1.30", "192.168.1.20", null, header).Should().Be(RequestOrigin.Internet);

  [Fact]
  public void TrustedProxyWithTraefikProfileIsInternet() =>
    Classify("192.168.1.5", PublicHost, "192.168.1.5", "X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host")
      .Should().Be(RequestOrigin.Internet);

  [Fact]
  public void TrustedProxyWithNginxProfileIsInternet() =>
    Classify("192.168.1.5", "192.168.1.20:8080", "192.168.1.5").Should().Be(RequestOrigin.Internet);

  [Fact]
  public void AddressInTrustedRangeIsInternet() =>
    Classify("172.18.0.7", "192.168.1.20:8080", "172.18.0.0/16").Should().Be(RequestOrigin.Internet);

  [Fact]
  public void SameAddressThatIsNotTrustedIsHomeNetwork() =>
    Classify("192.168.1.5", "192.168.1.20:8080").Should().Be(RequestOrigin.HomeNetwork);

  [Fact]
  public void LoopbackInTrustedProxiesIsInternet() =>
    Classify("127.0.0.1", "localhost:8080", "127.0.0.1,::1").Should().Be(RequestOrigin.Internet);
}

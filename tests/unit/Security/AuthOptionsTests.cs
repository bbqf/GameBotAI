using System.Net;
using FluentAssertions;
using GameBot.Service.Security;
using Xunit;

namespace GameBot.UnitTests.Security;

public sealed class AuthOptionsTests {
  [Fact]
  public void DefaultIsOff() {
    var options = AuthOptions.Create(null, null, null, null);

    options.Enabled.Should().BeFalse();
    options.PublicOrigin.Should().BeNull();
    options.Token.Should().BeNull();
    options.TrustedProxyCount.Should().Be(0);
  }

  [Fact]
  public void HttpsOriginGivesRpIdFromHost() {
    var options = AuthOptions.Create("true", "https://bot.example.com/some/path", null, null);

    options.Enabled.Should().BeTrue();
    options.IsValid.Should().BeTrue();
    options.RpId.Should().Be("bot.example.com");
    options.PublicOrigin!.ToString().Should().Be("https://bot.example.com/");
  }

  [Theory]
  [InlineData("http://bot.example.com")]
  [InlineData("bot.example.com")]
  [InlineData("ftp://bot.example.com")]
  public void OriginThatIsNotHttpsIsNotValid(string origin) {
    var options = AuthOptions.Create("true", origin, null, null);

    options.IsValid.Should().BeFalse();
    options.Problems.Should().NotBeEmpty();
  }

  [Fact]
  public void EnabledWithNoOriginGivesProblem() {
    var options = AuthOptions.Create("true", null, null, null);

    options.IsValid.Should().BeFalse();
    options.Problems.Should().ContainSingle();
  }

  [Fact]
  public void OneAddressIsTrusted() {
    var options = AuthOptions.Create(null, null, "192.168.1.5", null);

    options.IsTrustedProxy(IPAddress.Parse("192.168.1.5")).Should().BeTrue();
    options.IsTrustedProxy(IPAddress.Parse("192.168.1.6")).Should().BeFalse();
  }

  [Fact]
  public void CidrRangeIsTrusted() {
    var options = AuthOptions.Create(null, null, "172.18.0.0/16, 10.0.0.1", null);

    options.IsTrustedProxy(IPAddress.Parse("172.18.4.9")).Should().BeTrue();
    options.IsTrustedProxy(IPAddress.Parse("172.19.0.1")).Should().BeFalse();
    options.IsTrustedProxy(IPAddress.Parse("10.0.0.1")).Should().BeTrue();
  }

  [Fact]
  public void Ipv4MappedIpv6AddressMatchesIpv4Entry() {
    var options = AuthOptions.Create(null, null, "192.168.1.5", null);

    options.IsTrustedProxy(IPAddress.Parse("::ffff:192.168.1.5")).Should().BeTrue();
  }

  [Fact]
  public void LoopbackAddressesArePermitted() {
    var options = AuthOptions.Create(null, null, "127.0.0.1,::1", null);

    options.IsTrustedProxy(IPAddress.Loopback).Should().BeTrue();
    options.IsTrustedProxy(IPAddress.IPv6Loopback).Should().BeTrue();
    options.Problems.Should().BeEmpty();
  }

  [Fact]
  public void EntryThatIsNotValidIsIgnoredWithProblem() {
    var options = AuthOptions.Create(null, null, "not-an-address,192.168.1.5,10.0.0.0/99", null);

    options.TrustedProxyCount.Should().Be(1);
    options.Problems.Should().HaveCount(2);
    options.IsTrustedProxy(IPAddress.Parse("192.168.1.5")).Should().BeTrue();
  }

  [Fact]
  public void NullAddressIsNotTrusted() {
    var options = AuthOptions.Create(null, null, "192.168.1.5", null);

    options.IsTrustedProxy(null).Should().BeFalse();
  }

  [Fact]
  public void BlankTokenIsNull() {
    AuthOptions.Create(null, null, null, "  ").Token.Should().BeNull();
    AuthOptions.Create(null, null, null, "abc").Token.Should().Be("abc");
  }
}

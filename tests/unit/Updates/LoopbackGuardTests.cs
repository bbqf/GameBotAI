using System.Net;
using FluentAssertions;
using GameBot.Service.Services.Updates;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class LoopbackGuardTests {
  [Theory]
  [InlineData("127.0.0.1")]
  [InlineData("127.0.0.2")]
  [InlineData("::1")]
  [InlineData("::ffff:127.0.0.1")]
  public void LoopbackAddressesAreLocal(string address) {
    LoopbackGuard.IsLoopback(IPAddress.Parse(address)).Should().BeTrue();
  }

  [Theory]
  [InlineData("192.168.1.20")]
  [InlineData("10.0.0.5")]
  [InlineData("8.8.8.8")]
  [InlineData("2001:db8::1")]
  [InlineData("::ffff:192.168.1.20")]
  public void RemoteAddressesAreNotLocal(string address) {
    LoopbackGuard.IsLoopback(IPAddress.Parse(address)).Should().BeFalse();
  }

  [Fact]
  public void MissingAddressIsNotLocal() {
    LoopbackGuard.IsLoopback(null).Should().BeFalse();
  }

  [Fact]
  public void ForwardedForHeaderHasNoEffect() {
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");
    context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";

    LoopbackGuard.IsLocalRequest(context).Should().BeFalse();
  }

  [Fact]
  public void LocalSocketWithAForwardedForHeaderIsStillLocal() {
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Loopback;
    context.Request.Headers["X-Forwarded-For"] = "8.8.8.8";

    LoopbackGuard.IsLocalRequest(context).Should().BeTrue();
  }
}

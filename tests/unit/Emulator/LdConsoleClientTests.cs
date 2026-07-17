using System.Runtime.Versioning;
using GameBot.Emulator.Adb;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Emulator;

[SupportedOSPlatform("windows")]
public class LdConsoleClientTests {
  [Theory]
  [InlineData(0, "running", LdConsoleRunState.Running)]
  [InlineData(0, "  running  ", LdConsoleRunState.Running)]
  [InlineData(0, "RUNNING", LdConsoleRunState.Running)]
  [InlineData(0, "stop", LdConsoleRunState.Stopped)]
  [InlineData(0, "stopped", LdConsoleRunState.Stopped)]
  [InlineData(0, "", LdConsoleRunState.NotFound)]
  [InlineData(0, "player not exist", LdConsoleRunState.NotFound)]
  [InlineData(1, "running", LdConsoleRunState.NotFound)]
  public void ParseRunStateMapsOutput(int exitCode, string stdout, LdConsoleRunState expected) {
    LdConsoleClient.ParseRunState(exitCode, stdout, string.Empty).Should().Be(expected);
  }

  [Fact]
  public void InstanceArgPrefersName() {
    LdConsoleClient.InstanceArg("LDPlayer-5558", 3).Should().Be("--name \"LDPlayer-5558\"");
  }

  [Fact]
  public void InstanceArgUsesIndexWhenNoName() {
    LdConsoleClient.InstanceArg(null, 2).Should().Be("--index 2");
    LdConsoleClient.InstanceArg("   ", 0).Should().Be("--index 0");
  }

  [Fact]
  public void InstanceArgEmptyWhenNeither() {
    LdConsoleClient.InstanceArg(null, null).Should().BeEmpty();
  }
}

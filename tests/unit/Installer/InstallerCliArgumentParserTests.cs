using FluentAssertions;
using GameBot.Service.Services.Installer;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class InstallerCliArgumentParserTests {
  [Fact]
  public void ParseReturnsMappedRequestForValidArguments() {
    var result = InstallerCliArgumentParser.Parse([
      "--mode", "service",
      "--backend-port", "5500",
      "--web-port", "8088",
      "--protocol", "http",
      "--unattended",
      "--confirm-firewall-fallback", "true"
    ]);

    result.IsValid.Should().BeTrue();
    result.Request.Should().NotBeNull();
    result.Request!.InstallMode.Should().Be("service");
    result.Request.BackendPort.Should().Be(5500);
    result.Request.RequestedWebUiPort.Should().Be(8088);
    result.Request.Unattended.Should().BeTrue();
  }

  [Fact]
  public void ParseReturnsErrorsForInvalidPort() {
    var result = InstallerCliArgumentParser.Parse(["--backend-port", "not-a-port"]);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(error => error.Contains("--backend-port", StringComparison.Ordinal));
  }

  [Fact]
  public void ParseReturnsShowHelpForHelpSwitch() {
    var result = InstallerCliArgumentParser.Parse(["--help"]);

    result.ShowHelp.Should().BeTrue();
  }
}

using FluentAssertions;
using GameBot.Service.Services.Installer;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class PortProbeServiceTests {
  [Fact]
  public void ProbeReturnsAlternativesWhenRequestedPortsConflict() {
    var unavailable = new HashSet<int> { 5000, 8080 };
    var service = new PortProbeService(port => !unavailable.Contains(port));

    var result = service.Probe(5000, 8080);

    result.BackendPortAvailable.Should().BeFalse();
    result.WebUiPortAvailable.Should().BeFalse();
    result.BackendAlternatives.Should().NotBeEmpty();
    result.WebUiAlternatives.Should().Contain(8088);
  }

  [Fact]
  public void ProbeReturnsNoAlternativesWhenPortsAreAvailable() {
    var service = new PortProbeService(_ => true);

    var result = service.Probe(5000, 8080);

    result.BackendPortAvailable.Should().BeTrue();
    result.WebUiPortAvailable.Should().BeTrue();
    result.BackendAlternatives.Should().BeEmpty();
    result.WebUiAlternatives.Should().BeEmpty();
  }
}

using FluentAssertions;
using GameBot.Service.Services.Installer;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class PortSelectionOrderTests {
  [Fact]
  public void ResolvePreferredWebUiPortUsesFixedFallbackOrderWhenRequestedNotProvided() {
    var unavailable = new HashSet<int> { 8080, 8088 };
    var service = new PortProbeService(port => !unavailable.Contains(port));

    var selected = service.ResolvePreferredWebUiPort(5000, requestedWebUiPort: null);

    selected.Should().Be(8888);
  }

  [Fact]
  public void ResolvePreferredWebUiPortUsesRequestedPortWhenAvailable() {
    var service = new PortProbeService(_ => true);

    var selected = service.ResolvePreferredWebUiPort(5000, requestedWebUiPort: 9090);

    selected.Should().Be(9090);
  }
}

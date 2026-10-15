using System.Net;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 143 (T033): the list route (SC-006).</summary>
public sealed class GamepackListContractTests {
  [Fact]
  public async Task AnEmptyInstallationGivesAnEmptyList() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/api/gamepacks", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackContractHost.ReadAsync(response)).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task TwoInstalledGamepacksAreListedWithTheirFields() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(GamepackTestKit.Pack("beta", "2.0", changelog: "Beta text."))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync("{\"manifest\":{\"name\":\"alpha\",\"version\":\"1.0\",\"changelog\":\"Alpha text.\",\"requiredApiMajor\":" + GameBot.Domain.Gamepacks.ApiVersion.Major + "}}")).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(GamepackTestKit.Pack("beta", "2.1", changelog: "Beta two."))).StatusCode.Should().Be(HttpStatusCode.OK);

    var list = await host.GetJsonAsync("/api/gamepacks");

    list.GetArrayLength().Should().Be(2);
    var alpha = list[0];
    alpha.GetProperty("name").GetString().Should().Be("alpha");
    alpha.GetProperty("version").GetString().Should().Be("1.0");
    alpha.GetProperty("changelog").GetString().Should().Be("Alpha text.");
    alpha.GetProperty("requiredApiMajor").GetInt32().Should().Be(GameBot.Domain.Gamepacks.ApiVersion.Major);
    alpha.GetProperty("hasPreviousVersion").GetBoolean().Should().BeFalse();
    alpha.GetProperty("installedAtUtc").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
    var beta = list[1];
    beta.GetProperty("version").GetString().Should().Be("2.1");
    beta.GetProperty("hasPreviousVersion").GetBoolean().Should().BeTrue();
  }
}

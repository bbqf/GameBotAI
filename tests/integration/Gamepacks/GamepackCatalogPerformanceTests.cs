using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T026, SC-002): a list of 50 gamepacks with 10 versions each shows in under 5 seconds.</summary>
public sealed class GamepackCatalogPerformanceTests {
  [Fact]
  public async Task ListOfFiftyTimesTenVersionsIsFast() {
    var fixture = new CatalogFixture();
    for (var p = 0; p < 50; p++) {
      for (var v = 0; v < 10; v++) {
        fixture.Add("pack-" + p, "1." + v + ".0", "{}", "Change text " + v);
      }
    }

    await using var server = await CatalogTestServer.StartAsync();
    server.Set("/gp/index.json", fixture.IndexJson());
    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, null);

    var watch = Stopwatch.StartNew();
    var items = await rig.Service.ListAsync();
    watch.Stop();

    items.Should().HaveCount(50);
    items[0].Versions.Should().HaveCount(10);
    watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
  }
}

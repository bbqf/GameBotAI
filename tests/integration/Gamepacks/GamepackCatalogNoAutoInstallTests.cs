using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T037, FR-015, SC-005): a newer listed version is never installed without a request.</summary>
public sealed class GamepackCatalogNoAutoInstallTests {
  [Fact]
  public async Task ManyListCallsAndAWaitLeaveTheInstalledVersionUnchanged() {
    using var rig = new CatalogRig();
    new CatalogFixture()
      .Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"))
      .Add("pns-daily", "2.0.0", CatalogFixture.SimplePack("pns-daily", "2.0.0"))
      .WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);
    var before = rig.Kit.Snapshot();

    for (var i = 0; i < 20; i++) {
      var items = await rig.Service.ListAsync();
      items.Single().Versions[0].UpdateAvailable.Should().BeTrue();
    }

    await Task.Delay(TimeSpan.FromSeconds(2));

    rig.Gamepacks.List().Single().Version.Should().Be("1.0.0");
    rig.Kit.Snapshot().Should().Equal(before);
  }
}

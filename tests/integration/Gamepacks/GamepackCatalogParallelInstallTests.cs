using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T034): two catalog installs of one name at the same time follow the feature 143 rules.</summary>
public sealed class GamepackCatalogParallelInstallTests {
  [Fact]
  public async Task TwoParallelInstallsOfOneNameLeaveOneConsistentState() {
    using var rig = new CatalogRig();
    new CatalogFixture()
      .Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"))
      .Add("pns-daily", "1.1.0", CatalogFixture.SimplePack("pns-daily", "1.1.0"))
      .WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);

    var outcomes = await Task.WhenAll(
      rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false),
      rig.Service.InstallAsync("pns-daily", "1.1.0", dryRun: false));

    outcomes.Should().OnlyContain(o => o.Status == GamepackInstallStatus.Ok || o.Status == GamepackInstallStatus.Invalid);
    outcomes.Should().Contain(o => o.Status == GamepackInstallStatus.Ok);
    var record = rig.Gamepacks.List().Single();
    record.Version.Should().BeOneOf("1.0.0", "1.1.0");
    (await rig.Kit.Sequences.ListAsync()).Should().ContainSingle();
    (await rig.Kit.Commands.ListAsync()).Should().ContainSingle();
  }
}

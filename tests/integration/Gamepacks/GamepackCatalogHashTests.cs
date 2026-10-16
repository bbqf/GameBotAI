using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T033, FR-014): the SHA-256 check.</summary>
public sealed class GamepackCatalogHashTests {
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ACorrectHashInstalls(bool upperCase) {
    var pack = CatalogFixture.SimplePack("pns-daily", "1.0.0");
    using var rig = new CatalogRig();
    new CatalogFixture().Add("pns-daily", "1.0.0", pack, sha256: CatalogFixture.Sha256Hex(pack, upperCase)).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    rig.Gamepacks.List().Select(g => g.Name).Should().Equal("pns-daily");
  }

  [Fact]
  public async Task AWrongHashIsRejectedAndNothingChanges() {
    var pack = CatalogFixture.SimplePack("pns-daily", "1.0.0");
    using var rig = new CatalogRig();
    new CatalogFixture().Add("pns-daily", "1.0.0", pack, sha256: CatalogFixture.Sha256Hex(pack + " ")).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    var before = rig.Kit.Snapshot();

    var caught = await Assert.ThrowsAsync<CatalogException>(() => rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false));

    caught.Code.Should().Be(CatalogFailure.HashMismatch);
    rig.Kit.Snapshot().Should().Equal(before);
    rig.Gamepacks.List().Should().BeEmpty();
  }
}

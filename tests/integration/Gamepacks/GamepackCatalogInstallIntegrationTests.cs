using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T031): install and dry run from a folder and from a web source, under the signature policy.</summary>
public sealed class GamepackCatalogInstallIntegrationTests {
  private static CatalogFixture Fixture(string? signature = null) =>
    new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"), "First.", signature);

  [Fact]
  public async Task AFolderInstallWorks() {
    using var rig = new CatalogRig();
    Fixture().WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    rig.Gamepacks.List().Select(g => g.Name).Should().Equal("pns-daily");
    (await rig.Kit.Sequences.ListAsync()).Should().HaveCount(1);
  }

  [Fact]
  public async Task AWebInstallWorks() {
    await using var server = await CatalogTestServer.StartAsync();
    Fixture().ServeFrom(server, "/gp");
    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, null);

    var outcome = await rig.Service.InstallAsync("PNS-Daily", "1.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    rig.Gamepacks.List().Single().Version.Should().Be("1.0.0");
  }

  [Fact]
  public async Task ADryRunChangesNothing() {
    using var rig = new CatalogRig();
    Fixture().WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    var before = rig.Kit.Snapshot();

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.DryRun.Should().BeTrue();
    outcome.Created.Should().BeGreaterThan(0);
    rig.Kit.Snapshot().Should().Equal(before);
    rig.Gamepacks.List().Should().BeEmpty();
  }

  [Fact]
  public async Task PolicyRequiredRejectsABadSignatureAndChangesNothing() {
    using var signer = new TestSigner();
    using var rig = new CatalogRig();
    rig.SignatureSettings.SetPolicy(SignaturePolicy.Required);
    rig.SignatureSettings.AddKey("Builder", signer.PublicKey);
    var pack = CatalogFixture.SimplePack("pns-daily", "1.0.0");
    new CatalogFixture().Add("pns-daily", "1.0.0", pack, signature: signer.SignWithBadSignature(pack)).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    var before = rig.Kit.Snapshot();

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.SignatureRejected);
    outcome.Problems.Single().Code.Should().StartWith("signature_");
    rig.Kit.Snapshot().Should().Equal(before);
    rig.Gamepacks.List().Should().BeEmpty();
  }

  [Fact]
  public async Task PolicyRequiredAcceptsAGoodSignature() {
    using var signer = new TestSigner();
    using var rig = new CatalogRig();
    rig.SignatureSettings.SetPolicy(SignaturePolicy.Required);
    rig.SignatureSettings.AddKey("Builder", signer.PublicKey);
    var pack = CatalogFixture.SimplePack("pns-daily", "1.0.0");
    new CatalogFixture().Add("pns-daily", "1.0.0", pack, signature: signer.Sign(pack)).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Signature!.Status.Should().Be(SignatureStatus.Valid);
  }

  [Fact]
  public async Task AnUpdateKeepsThePreviousVersionForRollback() {
    using var rig = new CatalogRig();
    new CatalogFixture()
      .Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"))
      .Add("pns-daily", "1.1.0", CatalogFixture.SimplePack("pns-daily", "1.1.0"))
      .WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);

    await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);
    var outcome = await rig.Service.InstallAsync("pns-daily", "1.1.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    var item = rig.Gamepacks.List().Single();
    item.Version.Should().Be("1.1.0");
    item.HasPreviousVersion.Should().BeTrue();
  }
}

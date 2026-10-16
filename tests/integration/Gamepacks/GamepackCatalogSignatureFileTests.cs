using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T036, FR-008): where the signature text comes from.</summary>
public sealed class GamepackCatalogSignatureFileTests {
  private static readonly string Pack = CatalogFixture.SimplePack("pns-daily", "1.0.0");

  private static string SigRoute => "/gp/" + CatalogFixture.SignaturePath("pns-daily", "1.0.0");

  [Fact]
  public async Task ASignatureFileIsDownloadedWithTheTokenAndChecked() {
    using var signer = new TestSigner();
    await using var server = await CatalogTestServer.StartAsync();
    new CatalogFixture().Add("pns-daily", "1.0.0", Pack, signature: signer.Sign(Pack)).ServeFrom(server, "/gp");
    using var rig = new CatalogRig();
    rig.SignatureSettings.SetPolicy(SignaturePolicy.Required);
    rig.SignatureSettings.AddKey("Builder", signer.PublicKey);
    rig.Settings.Save(server.BaseUrl + "/gp", false, "tok");

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Signature!.Status.Should().Be(SignatureStatus.Valid);
    server.Requests.Where(r => r.Path == SigRoute).Should().ContainSingle().Which.Authorization.Should().Be("Bearer tok");
  }

  [Fact]
  public async Task AnEntryWithNoSignatureFileGivesMissingAndPolicyRequiredRejects() {
    using var rig = new CatalogRig();
    new CatalogFixture().Add("pns-daily", "1.0.0", Pack).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    rig.SignatureSettings.SetPolicy(SignaturePolicy.Required);

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.SignatureRejected);
    outcome.Signature!.Status.Should().Be(SignatureStatus.Missing);
    rig.Gamepacks.List().Should().BeEmpty();
  }

  [Theory]
  [InlineData(SignaturePolicy.Optional, SignatureStatus.Missing)]
  [InlineData(SignaturePolicy.Off, SignatureStatus.NotChecked)]
  public async Task AnEntryWithNoSignatureFileFollowsFeature146ForOptionalAndOff(SignaturePolicy policy, SignatureStatus status) {
    using var rig = new CatalogRig();
    new CatalogFixture().Add("pns-daily", "1.0.0", Pack).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    rig.SignatureSettings.SetPolicy(policy);

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Signature!.Status.Should().Be(status);
  }

  [Fact]
  public async Task ASignatureFileInAFolderCatalogIsRead() {
    using var signer = new TestSigner();
    using var rig = new CatalogRig();
    new CatalogFixture().Add("pns-daily", "1.0.0", Pack, signature: signer.Sign(Pack)).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    rig.SignatureSettings.SetPolicy(SignaturePolicy.Required);
    rig.SignatureSettings.AddKey("Builder", signer.PublicKey);

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Signature!.Status.Should().Be(SignatureStatus.Valid);
  }

  [Theory]
  [InlineData("404", CatalogFailure.NotFound)]
  [InlineData("401", CatalogFailure.Unauthorized)]
  [InlineData("redirect", CatalogFailure.RedirectRefused)]
  public async Task AFailedSignatureDownloadFailsTheInstallLikeAFileDownload(string kind, string code) {
    await using var server = await CatalogTestServer.StartAsync();
    await using var other = await CatalogTestServer.StartAsync();
    new CatalogFixture().Add("pns-daily", "1.0.0", Pack, signature: "x").ServeFrom(server, "/gp");
    switch (kind) {
      case "404":
        server.Routes.TryRemove(SigRoute, out _);
        break;
      case "401":
        server.Set(SigRoute, new CatalogReply { Status = 401 });
        break;
      default:
        server.Set(SigRoute, new CatalogReply { Status = 302, Location = other.BaseUrl + "/sig.json" });
        break;
    }

    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, "tok");
    var before = rig.Kit.Snapshot();

    var caught = await Assert.ThrowsAsync<CatalogException>(() => rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false));

    caught.Code.Should().Be(code);
    other.Requests.Should().BeEmpty();
    rig.Kit.Snapshot().Should().Equal(before);
  }
}

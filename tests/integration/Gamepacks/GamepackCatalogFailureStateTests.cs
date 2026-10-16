using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>
/// Feature 147 (T032, FR-013, FR-014, SC-003): for each failure kind the registry, sequences, commands, images, and settings
/// are equal before and after.
/// </summary>
public sealed class GamepackCatalogFailureStateTests {
  private static readonly string Pack = CatalogFixture.SimplePack("pns-daily", "2.0.0", "Daily seq");

  private static CatalogFixture Fixture(string? sha256 = null) =>
    new CatalogFixture().Add("pns-daily", "2.0.0", Pack, "Two.", signature: new string('s', 100), sha256: sha256);

  [Theory]
  [InlineData("not_configured")]
  [InlineData("unreachable")]
  [InlineData("unauthorized")]
  [InlineData("index_invalid")]
  [InlineData("missing_index")]
  [InlineData("not_found")]
  [InlineData("file_too_large")]
  [InlineData("signature_too_large")]
  [InlineData("timeout")]
  [InlineData("hash_mismatch")]
  [InlineData("file_missing_folder")]
  [InlineData("missing_index_folder")]
  public async Task EachFailureLeavesAllStateEqual(string kind) {
    await using var server = await CatalogTestServer.StartAsync();
    using var rig = new CatalogRig();
    // An installed gamepack makes the state snapshot worth comparing.
    (await rig.Gamepacks.InstallAsync(CatalogFixture.SimplePack("installed", "1.0.0", "Installed seq"), dryRun: false)).Status
      .Should().Be(GameBot.Domain.Gamepacks.GamepackInstallStatus.Ok);
    var expected = Arrange(kind, server, rig);
    var before = rig.Kit.Snapshot();

    var caught = await Assert.ThrowsAsync<CatalogException>(() => rig.Service.InstallAsync("pns-daily", "2.0.0", dryRun: false));

    caught.Code.Should().Be(expected);
    rig.Kit.Snapshot().Should().Equal(before);
    rig.Gamepacks.List().Should().ContainSingle().Which.Name.Should().Be("installed");
    (await rig.Kit.Sequences.ListAsync()).Should().ContainSingle();
  }

  private static string Arrange(string kind, CatalogTestServer server, CatalogRig rig) {
    var web = server.BaseUrl + "/gp";
    switch (kind) {
      case "not_configured":
        return CatalogFailure.NotConfigured;
      case "unreachable":
        rig.Settings.Save("https://127.0.0.1:1/gp", false, "tok");
        return CatalogFailure.Unreachable;
      case "unauthorized":
        server.Set("/gp/index.json", new CatalogReply { Status = 401 });
        rig.Settings.Save(web, false, "wrong");
        return CatalogFailure.Unauthorized;
      case "index_invalid":
        server.Set("/gp/index.json", "garbage");
        rig.Settings.Save(web, false, null);
        return CatalogFailure.IndexInvalid;
      case "missing_index":
        rig.Settings.Save(web, false, null);
        return CatalogFailure.IndexInvalid;
      case "not_found":
        server.Set("/gp/index.json", Fixture().IndexJson());
        rig.Settings.Save(web, false, null);
        return CatalogFailure.NotFound;
      case "file_too_large":
        Fixture().ServeFrom(server, "/gp");
        rig.Options.FileLimitBytes = 10;
        rig.Settings.Save(web, false, null);
        return CatalogFailure.FileTooLarge;
      case "signature_too_large":
        Fixture().ServeFrom(server, "/gp");
        rig.Options.SignatureLimitBytes = 10;
        rig.Settings.Save(web, false, null);
        return CatalogFailure.FileTooLarge;
      case "timeout":
        Fixture().ServeFrom(server, "/gp");
        server.Set("/gp/" + CatalogFixture.FilePath("pns-daily", "2.0.0"), new CatalogReply { Body = new byte[] { 1 }, Stall = TimeSpan.FromSeconds(10) });
        rig.Options.RequestTimeout = TimeSpan.FromMilliseconds(300);
        rig.Settings.Save(web, false, null);
        return CatalogFailure.Timeout;
      case "hash_mismatch":
        Fixture(new string('0', 64)).ServeFrom(server, "/gp");
        rig.Settings.Save(web, false, null);
        return CatalogFailure.HashMismatch;
      case "file_missing_folder":
        Fixture().WriteTo(rig.CatalogFolder);
        File.Delete(Path.Combine(rig.CatalogFolder, "pns-daily", "2.0.0", "pns-daily.gamepack.json"));
        rig.Settings.Save(rig.CatalogFolder, false, null);
        return CatalogFailure.FileMissing;
      case "missing_index_folder":
        Directory.CreateDirectory(rig.CatalogFolder);
        rig.Settings.Save(rig.CatalogFolder, false, null);
        return CatalogFailure.IndexInvalid;
      default:
        throw new ArgumentOutOfRangeException(nameof(kind));
    }
  }
}

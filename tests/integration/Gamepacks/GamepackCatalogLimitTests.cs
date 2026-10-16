using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T035, FR-012): the size limits and the request time limit, with small option values.</summary>
public sealed class GamepackCatalogLimitTests {
  private static readonly string Pack = CatalogFixture.SimplePack("pns-daily", "1.0.0");

  private static async Task<(CatalogTestServer Server, CatalogRig Rig)> StartAsync(string? signature) {
    var server = await CatalogTestServer.StartAsync();
    new CatalogFixture().Add("pns-daily", "1.0.0", Pack, signature: signature).ServeFrom(server, "/gp");
    var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, null);
    return (server, rig);
  }

  [Fact]
  public async Task AnOversizeSignatureFileGivesFileTooLarge() {
    var (server, rig) = await StartAsync(new string('s', 2000));
    await using var _ = server;
    using var __ = rig;
    rig.Options.SignatureLimitBytes = 1000;
    var before = rig.Kit.Snapshot();

    var caught = await Assert.ThrowsAsync<CatalogException>(() => rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false));

    caught.Code.Should().Be(CatalogFailure.FileTooLarge);
    rig.Kit.Snapshot().Should().Equal(before);
  }

  [Fact]
  public async Task AnOversizeGamepackFileGivesFileTooLargeAndTheDownloadStops() {
    var (server, rig) = await StartAsync(null);
    await using var _ = server;
    using var __ = rig;
    rig.Options.FileLimitBytes = Pack.Length - 1;
    var before = rig.Kit.Snapshot();

    var caught = await Assert.ThrowsAsync<CatalogException>(() => rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false));

    caught.Code.Should().Be(CatalogFailure.FileTooLarge);
    rig.Kit.Snapshot().Should().Equal(before);
  }

  [Fact]
  public async Task AFileOfExactlyTheLimitIsAccepted() {
    var (server, rig) = await StartAsync(null);
    await using var _ = server;
    using var __ = rig;
    rig.Options.FileLimitBytes = System.Text.Encoding.UTF8.GetByteCount(Pack);

    var outcome = await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: true);

    outcome.Status.Should().Be(GameBot.Domain.Gamepacks.GamepackInstallStatus.Ok);
  }

  [Fact]
  public async Task AStalledFileDownloadGivesTimeout() {
    var (server, rig) = await StartAsync(null);
    await using var _ = server;
    using var __ = rig;
    server.Set("/gp/" + CatalogFixture.FilePath("pns-daily", "1.0.0"), new CatalogReply { Body = new byte[] { 1 }, Stall = TimeSpan.FromSeconds(10) });
    rig.Options.RequestTimeout = TimeSpan.FromMilliseconds(300);
    var before = rig.Kit.Snapshot();

    var caught = await Assert.ThrowsAsync<CatalogException>(() => rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false));

    caught.Code.Should().Be(CatalogFailure.Timeout);
    rig.Kit.Snapshot().Should().Equal(before);
  }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 147 (T024): the web source against a local test server.</summary>
public sealed class GamepackCatalogHttpIntegrationTests {
  private static CatalogFixture OnePack() => new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"));

  private static async Task<CatalogException> FailureOf(Func<Task> action) {
    var caught = await Assert.ThrowsAsync<CatalogException>(action);
    return caught;
  }

  [Fact]
  public async Task TheBearerTokenIsSentToTheCatalogHostOnEachRequest() {
    await using var server = await CatalogTestServer.StartAsync();
    OnePack().ServeFrom(server, "/gp");
    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, "secret-token");

    await rig.Service.ListAsync();
    await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: true);

    server.Requests.Should().NotBeEmpty();
    server.Requests.Should().OnlyContain(r => r.Authorization == "Bearer secret-token");
    server.Paths.Should().Contain("/gp/pns-daily/1.0.0/pns-daily.gamepack.json");
  }

  [Fact]
  public async Task NoTokenMeansNoAuthorizationHeader() {
    await using var server = await CatalogTestServer.StartAsync();
    OnePack().ServeFrom(server, "/gp");
    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, null);

    await rig.Service.ListAsync();

    server.Requests.Should().OnlyContain(r => r.Authorization == null);
  }

  [Fact]
  public async Task ARedirectToAnotherHostIsRefusedAndTheTokenIsNotSent() {
    await using var catalog = await CatalogTestServer.StartAsync();
    await using var other = await CatalogTestServer.StartAsync();
    other.Set("/index.json", OnePack().IndexJson());
    catalog.Set("/gp/index.json", new CatalogReply { Status = 302, Location = other.BaseUrl + "/index.json" });
    using var rig = new CatalogRig();
    rig.Settings.Save(catalog.BaseUrl + "/gp", false, "secret-token");

    var failure = await FailureOf(() => rig.Service.ListAsync());

    failure.Code.Should().Be(CatalogFailure.RedirectRefused);
    other.Requests.Should().BeEmpty();
  }

  [Fact]
  public async Task ARedirectOnTheSameHostIsFollowedUpToTheLimit() {
    await using var server = await CatalogTestServer.StartAsync();
    server.Set("/index.json", OnePack().IndexJson());
    server.Set("/gp/index.json", new CatalogReply { Status = 302, Location = "/hop1" });
    server.Set("/hop1", new CatalogReply { Status = 302, Location = "/hop2" });
    server.Set("/hop2", new CatalogReply { Status = 301, Location = "/index.json" });
    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, "tok");

    (await rig.Service.ListAsync()).Should().ContainSingle();

    server.Requests.Should().OnlyContain(r => r.Authorization == "Bearer tok");
    rig.Options.MaxRedirects = 2;
    var failure = await FailureOf(() => rig.Service.ListAsync());
    failure.Code.Should().Be(CatalogFailure.RedirectRefused);
  }

  [Fact]
  public async Task AnOversizeIndexIsRefused() {
    await using var server = await CatalogTestServer.StartAsync();
    OnePack().ServeFrom(server, "/gp");
    using var rig = new CatalogRig();
    rig.Options.IndexLimitBytes = 50;
    rig.Settings.Save(server.BaseUrl + "/gp", false, null);

    var failure = await FailureOf(() => rig.Service.ListAsync());

    failure.Code.Should().Be(CatalogFailure.IndexTooLarge);
  }

  [Theory]
  [InlineData(401, CatalogFailure.Unauthorized)]
  [InlineData(403, CatalogFailure.Forbidden)]
  [InlineData(500, CatalogFailure.Unreachable)]
  public async Task AnErrorStatusGivesItsReason(int status, string code) {
    await using var server = await CatalogTestServer.StartAsync();
    server.Set("/gp/index.json", new CatalogReply { Status = status });
    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, "canary-7f3a9");

    var failure = await FailureOf(() => rig.Service.ListAsync());

    failure.Code.Should().Be(code);
    failure.Message.Should().NotContain("canary-7f3a9");
  }

  [Fact]
  public async Task AnUnreachableHostGivesUnreachable() {
    using var rig = new CatalogRig();
    rig.Settings.Save("https://127.0.0.1:1/gp", false, null);

    var failure = await FailureOf(() => rig.Service.ListAsync());

    failure.Code.Should().Be(CatalogFailure.Unreachable);
  }

  [Fact]
  public async Task AFileLocationThatEscapesTheBaseIsNeverRequested() {
    await using var server = await CatalogTestServer.StartAsync();
    var index = "{\"formatVersion\":1,\"gamepacks\":[{\"name\":\"evil\",\"versions\":[{\"version\":\"1.0.0\",\"file\":\"../secret.json\"}]}]}";
    server.Set("/gp/index.json", index);
    server.Set("/secret.json", "secret");
    using var rig = new CatalogRig();
    rig.Settings.Save(server.BaseUrl + "/gp", false, null);

    (await rig.Service.ListAsync()).Should().BeEmpty();
    var failure = await FailureOf(() => rig.Service.InstallAsync("evil", "1.0.0", dryRun: false));

    failure.Code.Should().Be(CatalogFailure.EntryNotListed);
    server.Paths.Should().NotContain("/secret.json");
  }
}

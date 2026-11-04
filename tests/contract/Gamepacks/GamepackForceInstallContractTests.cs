using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 178: the force option on the four routes, the response members, the dry run, and the list member.</summary>
public sealed class GamepackForceInstallContractTests {
  // The same name and version, with a command that has a different content. The stored command then differs from the file.
  private static string Pack(bool withImage) => GamepackTestKit.Pack("pns", "1.0.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1") },
    commands: new[] { withImage ? GamepackTestKit.Cmd("c1", "Tap", imageId: "icon") : GamepackTestKit.Cmd("c1", "Tap") },
    images: withImage ? new[] { GamepackTestKit.Img("icon") } : Array.Empty<object>());

  private static async Task InstallPlainAsync(GamepackContractHost host, string json) {
    var response = await host.InstallAsync(json);
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
  }

  // ---- without force ----

  [Fact]
  public async Task AnInstallOfTheSameVersionWithoutForceHasNeitherNewMember() {
    using var host = new GamepackContractHost();
    await InstallPlainAsync(host, Pack(withImage: false));

    var response = await host.InstallAsync(Pack(withImage: true));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.TryGetProperty("reinstalled", out _).Should().BeFalse();
    body.TryGetProperty("overwritten", out _).Should().BeFalse();
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("hasReinstallPoint").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task AFirstInstallWithForceHasNeitherNewMember() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(Pack(withImage: false), force: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.TryGetProperty("reinstalled", out _).Should().BeFalse();
    body.TryGetProperty("overwritten", out _).Should().BeFalse();
  }

  // ---- install route ----

  [Fact]
  public async Task AForcedInstallListsOnlyTheObjectsThatDiffer() {
    using var host = new GamepackContractHost();
    await InstallPlainAsync(host, Pack(withImage: false));

    var response = await host.InstallAsync(Pack(withImage: true), force: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("reinstalled").GetBoolean().Should().BeTrue();
    var items = body.GetProperty("overwritten").EnumerateArray().Select(e => (e.GetProperty("type").GetString(), e.GetProperty("name").GetString())).ToList();
    items.Should().Equal(("command", "Tap"));
  }

  [Fact]
  public async Task AForcedInstallOfAnEqualPackHasAnEmptyList() {
    using var host = new GamepackContractHost();
    await InstallPlainAsync(host, Pack(withImage: true));

    var response = await host.InstallAsync(Pack(withImage: true), force: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("reinstalled").GetBoolean().Should().BeTrue();
    body.GetProperty("overwritten").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task AForcedDryRunReportsTheListAndChangesNothing() {
    using var host = new GamepackContractHost();
    await InstallPlainAsync(host, Pack(withImage: false));
    var before = await host.StateSnapshotAsync();

    var response = await host.InstallAsync(Pack(withImage: true), dryRun: true, force: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    body.GetProperty("reinstalled").GetBoolean().Should().BeTrue();
    body.GetProperty("overwritten").GetArrayLength().Should().Be(1);
    (await host.StateSnapshotAsync()).Should().Be(before);
    File.Exists(Path.Combine(host.Root, "gamepacks", "reinstall", "pns.json")).Should().BeFalse();
  }

  [Fact]
  public async Task TheListShowsTheReinstallPointAndTheRollbackRemovesIt() {
    using var host = new GamepackContractHost();
    await InstallPlainAsync(host, Pack(withImage: false));
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("hasReinstallPoint").GetBoolean().Should().BeFalse();

    (await host.InstallAsync(Pack(withImage: true), force: true)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("hasReinstallPoint").GetBoolean().Should().BeTrue();

    var rollback = await host.RollbackAsync("pns");

    rollback.StatusCode.Should().Be(HttpStatusCode.OK, await rollback.Content.ReadAsStringAsync());
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("hasReinstallPoint").GetBoolean().Should().BeFalse();
    (await host.RollbackAsync("pns")).StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task AFailedForcedInstallGivesTheNormalWriteError() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    await InstallPlainAsync(host, Pack(withImage: false));
    var before = await host.StateSnapshotAsync();
    host.Gate.Arm(1);

    var response = await host.InstallAsync(Pack(withImage: true), force: true);

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("failed");
    (await host.StateSnapshotAsync()).Should().Be(before);
  }

  // ---- the other three routes (FR-004) ----

  private static async Task<ManagedContractHost> ArrangeCatalogAsync() {
    var host = new ManagedContractHost();
    var folder = Path.Combine(host.Host.Root, "catalog");
    new CatalogFixture().Add("pns", "1.0.0", ManagedContractHost.Pack("1.0.0", "alpha")).WriteTo(folder);
    await host.Host.SetCatalogAsync(folder);
    return host;
  }

  [Fact]
  public async Task TheCatalogInstallRouteTakesForceInTheBody() {
    using var host = await ArrangeCatalogAsync();
    (await host.Host.CatalogInstallAsync("pns", "1.0.0")).StatusCode.Should().Be(HttpStatusCode.OK);

    var plain = await GamepackContractHost.ReadAsync(await host.Host.CatalogInstallAsync("pns", "1.0.0"));
    var forced = await GamepackContractHost.ReadAsync(await host.Host.CatalogInstallAsync("pns", "1.0.0", force: true));

    plain.TryGetProperty("reinstalled", out _).Should().BeFalse();
    forced.GetProperty("reinstalled").GetBoolean().Should().BeTrue();
    forced.GetProperty("overwritten").ValueKind.Should().Be(JsonValueKind.Array);
  }

  [Fact]
  public async Task TheUpdateRouteTakesForceInTheQuery() {
    using var host = await ArrangeCatalogAsync();
    (await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var plain = await GamepackContractHost.ReadAsync(await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha")));
    var forced = await GamepackContractHost.ReadAsync(await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"), force: true));

    plain.GetProperty("result").GetString().Should().Be("done");
    plain.TryGetProperty("reinstalled", out _).Should().BeFalse();
    forced.GetProperty("result").GetString().Should().Be("done");
    forced.GetProperty("reinstalled").GetBoolean().Should().BeTrue();
    forced.GetProperty("overwritten").ValueKind.Should().Be(JsonValueKind.Array);
  }

  [Fact]
  public async Task TheCatalogUpdateRouteTakesForceInTheBody() {
    using var host = await ArrangeCatalogAsync();
    (await host.CatalogUpdateAsync("pns", "1.0.0")).StatusCode.Should().Be(HttpStatusCode.OK);

    var plain = await GamepackContractHost.ReadAsync(await host.CatalogUpdateAsync("pns", "1.0.0"));
    var forced = await GamepackContractHost.ReadAsync(await host.CatalogUpdateAsync("pns", "1.0.0", force: true));

    plain.TryGetProperty("reinstalled", out _).Should().BeFalse();
    forced.GetProperty("reinstalled").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task ADryRunOfTheManagedUpdateWithForceReportsTheListAndMakesNoPoint() {
    using var host = await ArrangeCatalogAsync();
    (await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"), dryRun: true, force: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("planned");
    body.GetProperty("reinstalled").GetBoolean().Should().BeTrue();
    File.Exists(Path.Combine(host.Host.Root, "gamepacks", "reinstall", "pns.json")).Should().BeFalse();
  }

  [Fact]
  public async Task TheManagedRollbackUsesAReinstallPoint() {
    using var host = await ArrangeCatalogAsync();
    (await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"), force: true)).StatusCode.Should().Be(HttpStatusCode.OK);

    var rollback = await host.ManagedRollbackAsync("pns");

    rollback.StatusCode.Should().Be(HttpStatusCode.OK, await rollback.Content.ReadAsStringAsync());
    (await host.Host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("hasReinstallPoint").GetBoolean().Should().BeFalse();
  }
}

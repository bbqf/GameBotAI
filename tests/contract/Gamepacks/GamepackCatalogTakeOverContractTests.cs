using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 149 (T020): the catalog install route with the body field takeOver.</summary>
public sealed class GamepackCatalogTakeOverContractTests {
  private static async Task<GamepackContractHost> ArrangeAsync(string name = "pns-daily") {
    var host = new GamepackContractHost();
    var folder = Path.Combine(host.Root, "catalog");
    new CatalogFixture().Add(name, "1.0.0", TakeOverContractKit.Pack(name, "1.0.0")).WriteTo(folder);
    await host.SetCatalogAsync(folder);
    return host;
  }

  [Fact]
  public async Task TheOptionOffKeepsTheConflictAndTheOptionMissingEqualsOff() {
    using var host = await ArrangeAsync();
    await TakeOverContractKit.MakeUserObjectsAsync(host);

    var response = await host.CatalogInstallAsync("pns-daily", "1.0.0");

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("takenOver").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task TheOptionOnTakesOverAndTheBackupCanBeRestored() {
    using var host = await ArrangeAsync();
    await TakeOverContractKit.MakeUserObjectsAsync(host);
    var sequenceId = await host.SequenceIdAsync("Help All");

    var response = await host.CatalogInstallAsync("pns-daily", "1.0.0", takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("takenOver").GetInt32().Should().Be(2);
    var backupId = body.GetProperty("takeOverBackupId").GetString()!;
    backupId.Should().MatchRegex(TakeOverContractKit.BackupIdPattern);
    (await TakeOverContractKit.OwnerKindOfSequenceAsync(host, sequenceId)).Should().Be("gamepack");

    var restore = await host.RestoreTakeOverAsync(backupId);

    restore.StatusCode.Should().Be(HttpStatusCode.OK);
    (await TakeOverContractKit.StepCountOfSequenceAsync(host, sequenceId)).Should().Be(0);
    (await TakeOverContractKit.OwnerKindOfSequenceAsync(host, sequenceId)).Should().Be("user");
  }

  [Fact]
  public async Task ADryRunWithTheOptionOnListsTheObjects() {
    using var host = await ArrangeAsync();
    await TakeOverContractKit.MakeUserObjectsAsync(host);

    var response = await host.CatalogInstallAsync("pns-daily", "1.0.0", dryRun: true, takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("takeOvers").GetArrayLength().Should().Be(2);
    TakeOverContractKit.BackupFileCount(host).Should().Be(0);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AConflictWithAnotherGamepackGives422WithTheOptionOnAndOffAndNever409(bool takeOver) {
    using var host = await ArrangeAsync();
    (await host.InstallAsync(TakeOverContractKit.Pack("other", "1.0.0"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.CatalogInstallAsync("pns-daily", "1.0.0", takeOver: takeOver);

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
  }
}

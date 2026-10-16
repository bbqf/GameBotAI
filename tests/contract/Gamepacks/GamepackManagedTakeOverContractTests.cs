using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 149 (T042): the managed update route with the query takeOver.</summary>
public sealed class GamepackManagedTakeOverContractTests {
  private static async Task MakeUserObjectsAsync(ManagedContractHost host) {
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "alpha" })).StatusCode.Should().Be(HttpStatusCode.Created);
    (await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "MainCmd-pns", steps = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task AnUpdateWithTheOptionOnTakesOverAndReportsTheFields() {
    using var host = new ManagedContractHost();
    await MakeUserObjectsAsync(host);

    var response = await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"), takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("done");
    body.GetProperty("takenOver").GetInt32().Should().Be(2);
    body.GetProperty("takeOverBackupId").GetString().Should().MatchRegex(TakeOverContractKit.BackupIdPattern);
    body.GetProperty("takeOvers").GetArrayLength().Should().Be(0);
    body.TryGetProperty("backupId", out var backupId).Should().BeTrue();
    backupId.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "the queue template backup keeps its meaning");
  }

  [Fact]
  public async Task ADryRunListsTheObjectsAndChangesNothing() {
    using var host = new ManagedContractHost();
    await MakeUserObjectsAsync(host);
    var before = await host.Host.StateSnapshotAsync();

    var response = await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"), dryRun: true, takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("planned");
    body.GetProperty("takenOver").GetInt32().Should().Be(2);
    body.GetProperty("takeOvers").EnumerateArray().Select(e => e.GetProperty("name").GetString()).Should().BeEquivalentTo("alpha", "MainCmd-pns");
    body.TryGetProperty("takeOverBackupId", out _).Should().BeFalse();
    (await host.Host.StateSnapshotAsync()).Should().Be(before);
    TakeOverContractKit.BackupFileCount(host.Host).Should().Be(0);
  }

  [Fact]
  public async Task TheOptionOffKeepsTheConflict() {
    using var host = new ManagedContractHost();
    await MakeUserObjectsAsync(host);

    var real = await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"));
    var dry = await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"), dryRun: true);

    real.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    dry.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackContractHost.ReadAsync(dry)).GetProperty("problems").GetArrayLength().Should().BeGreaterThan(0);
    (await GamepackContractHost.ReadAsync(real)).GetProperty("takenOver").GetInt32().Should().Be(0);
  }

  [Theory]
  [InlineData(false, HttpStatusCode.UnprocessableEntity)]
  [InlineData(true, HttpStatusCode.OK)]
  public async Task AConflictWithAnotherGamepackGivesTheSameStatusWithTheOptionOnAndOff(bool dryRun, HttpStatusCode expected) {
    using var host = new ManagedContractHost();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha").Replace("\"pns\"", "\"other\""))).StatusCode.Should().Be(HttpStatusCode.OK);

    foreach (var takeOver in new[] { false, true }) {
      var response = await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha"), dryRun: dryRun, takeOver: takeOver);

      response.StatusCode.Should().Be(expected, "takeOver=" + takeOver);
      (await GamepackContractHost.ReadAsync(response)).GetProperty("problems").GetArrayLength().Should().BeGreaterThan(0);
    }
  }

  [Fact]
  public async Task TheManagedRollbackHasNoTakeOverOption() {
    using var host = new ManagedContractHost();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.Host.InstallAsync(ManagedContractHost.Pack("2.0.0", "alpha", "beta"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.Client.PostAsync(new Uri("/api/gamepacks/pns/rollback?managed=true&takeOver=true", UriKind.Relative), content: null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("takenOver").GetInt32().Should().Be(0);
    body.TryGetProperty("takeOverBackupId", out _).Should().BeFalse();
  }
}

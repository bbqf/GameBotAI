using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 148 (T047): the managed rollback and the unchanged plain rollback.</summary>
public sealed class GamepackManagedRollbackContractTests {
  private static async Task<ManagedContractHost> ArrangeAsync() {
    var host = new ManagedContractHost();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).EnsureSuccessStatusCode();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("2.0.0", "alpha", "gamma"))).EnsureSuccessStatusCode();
    await host.AddTemplateAndQueueAsync("daily", running: true, "alpha", "gamma");
    return host;
  }

  [Fact]
  public async Task ARollbackWithoutManagedKeepsTheOldResponseAndStopsNoQueue() {
    using var host = await ArrangeAsync();

    var response = await host.Host.RollbackAsync("pns");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("rolled_back");
    body.TryGetProperty("restore", out _).Should().BeFalse();
    host.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task ManagedTrueGivesAManagedReport() {
    using var host = await ArrangeAsync();

    var response = await host.ManagedRollbackAsync("pns");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("done");
    body.GetProperty("dryRun").GetBoolean().Should().BeFalse();
    body.GetProperty("gamepack").GetProperty("version").GetString().Should().Be("1.0.0");
    body.GetProperty("stoppedQueues").GetArrayLength().Should().Be(1);
    body.GetProperty("backupId").GetString().Should().NotBeNullOrEmpty();
  }

  [Fact]
  public async Task AManagedDryRunChangesNothing() {
    using var host = await ArrangeAsync();
    var before = await host.Host.StateSnapshotAsync();

    var response = await host.ManagedRollbackAsync("pns", dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("planned");
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    host.Control.Calls.Should().BeEmpty();
    (await host.Host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task TheErrorCasesHaveTheContractStatusAndReason() {
    using var host = new ManagedContractHost();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).EnsureSuccessStatusCode();

    var noPrevious = await host.ManagedRollbackAsync("pns");
    var unknown = await host.ManagedRollbackAsync("nope");

    noPrevious.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await ManagedContractHost.ReasonOfAsync(noPrevious)).Should().Be("no_previous_version");
    unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ManagedContractHost.ReasonOfAsync(unknown)).Should().Be("gamepack_not_found");
  }
}

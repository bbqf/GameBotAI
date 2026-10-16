using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 148 (T044, SC-005): a dry run changes nothing.</summary>
public sealed class GamepackManagedDryRunIntegrationTests {
  [Fact]
  public async Task TheStateIsEqualBeforeAndAfterAndNoBackupFileExists() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    await rig.AddQueueAsync("Q", template, running: true);
    var files = rig.Packs.Snapshot();
    var state = await rig.StateAsync();

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma"), dryRun: true));
    var rollback = await rig.RunAsync(ManagedTestRig.Rollback("pns", dryRun: true));

    report.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(report));
    rollback.Reason.Should().Be(ManagedUpdateReasons.NoPreviousVersion);
    rig.Packs.Snapshot().Should().Equal(files);
    (await rig.StateAsync()).Should().Be(state);
    Directory.Exists(Path.Combine(rig.Root, "gamepacks", "backups")).Should().BeFalse();
    rig.Control.Calls.Should().BeEmpty();
  }
}

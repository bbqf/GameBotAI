using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 148 (T048): the managed rollback after an update.</summary>
public sealed class GamepackManagedRollbackIntegrationTests {
  [Fact]
  public async Task ARollbackAfterAnUpdateBringsBackTheOldVersionAndRestartsTheQueue() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    await rig.AddQueueAsync("Q", template, running: true);
    var update = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));
    update.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(update));

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    rig.Service.List().Single().Version.Should().Be("1.0.0");
    report.StoppedQueues.Should().HaveCount(1);
    report.RestartedQueues.Should().HaveCount(1);
    report.BackupId.Should().NotBeNull().And.NotBe(update.BackupId);
    (await rig.Packs.SequenceByNameAsync("beta")).Should().NotBeNull();
    (await rig.Packs.SequenceByNameAsync("gamma")).Should().BeNull();
  }

  [Fact]
  public async Task AFailedRollbackRestoresTheStateBeforeTheCall() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    await rig.InstallAsync("2.0.0", "alpha", "gamma");
    var template = await rig.AddTemplateAsync("daily", "alpha", "gamma");
    await rig.AddQueueAsync("Q", template, running: true);
    var before = await rig.StateAsync();
    rig.Packs.Gate.Arm(2);

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
    (await rig.StateAsync()).Should().Be(before);
  }
}

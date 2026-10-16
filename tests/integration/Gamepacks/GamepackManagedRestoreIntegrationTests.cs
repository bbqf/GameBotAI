using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 148 (T037, SC-004): a failed update leaves the version and the templates as before.</summary>
public sealed class GamepackManagedRestoreIntegrationTests {
  private static async Task<ManagedTestRig> ArrangeAsync() {
    var rig = new ManagedTestRig(TimeSpan.FromSeconds(1));
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    await rig.AddQueueAsync("Q1", template, running: true);
    await rig.AddQueueAsync("Q2", template, running: true);
    return rig;
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  public async Task AWriteFaultAtAnyInstallWriteRestoresVersionAndTemplates(int faultAt) {
    using var rig = await ArrangeAsync();
    var before = await rig.StateAsync();
    rig.Packs.Gate.Arm(faultAt);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
    (await rig.StateAsync()).Should().Be(before);
  }

  [Fact]
  public async Task AStopTimeoutRestoresTheQueuesAndChangesNoGamepackOrTemplate() {
    using var rig = await ArrangeAsync();
    var before = await rig.StateAsync();
    var queues = await rig.Queues.ListAsync();
    rig.Control.StopTimesOut.Add(queues[^1].Id);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Reason.Should().Be(ManagedUpdateReasons.StopTimeout);
    (await rig.StateAsync()).Should().Be(before);
  }

  [Fact]
  public async Task AFailedRestartOfOneQueueDoesNotStopTheOthers() {
    using var rig = await ArrangeAsync();
    var queues = await rig.Queues.ListAsync();
    rig.Control.StartFails.Add(queues[0].Id);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.NotRestartedQueues.Should().ContainSingle().Which.Id.Should().Be(queues[0].Id);
    report.RestartedQueues.Should().ContainSingle().Which.Id.Should().Be(queues[1].Id);
    rig.Control.StartCalls.Should().HaveCount(2);
  }
}

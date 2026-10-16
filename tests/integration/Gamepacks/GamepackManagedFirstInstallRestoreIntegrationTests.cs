using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 148 (T036, SC-004): a failed first install with an injected write fault leaves no gamepack and no file.</summary>
public sealed class GamepackManagedFirstInstallRestoreIntegrationTests {
  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  public async Task AWriteFaultAtAnyPointLeavesTheInstanceAsBefore(int faultAt) {
    using var rig = new ManagedTestRig();
    var before = rig.Packs.Snapshot();
    rig.Packs.Gate.Arm(faultAt);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha", "beta")));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Restore.Happened.Should().BeTrue();
    report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
    rig.Service.List().Should().BeEmpty();
    (await rig.Packs.Sequences.ListAsync()).Should().BeEmpty();
    (await rig.Packs.Commands.ListAsync()).Should().BeEmpty();
    rig.Packs.Snapshot().Keys.Should().BeEquivalentTo(before.Keys);
  }

  [Fact]
  public async Task AFaultInTheApplierRestoreToo_TheFlowStillLeavesNoObject() {
    using var rig = new ManagedTestRig();
    rig.Packs.Gate.Arm(3);
    rig.Packs.Gate.ThrowOnRestore = true;

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha", "beta")));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    rig.Service.List().Should().BeEmpty();
    (await rig.Packs.Sequences.ListAsync()).Should().BeEmpty();
    (await rig.Packs.Commands.ListAsync()).Should().BeEmpty();
    report.Restore.Steps.Select(s => s.Step).Should().Contain("gamepack_remove");
  }
}

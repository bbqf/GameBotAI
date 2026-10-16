using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T036, SC-004): a failed first install returns to "no gamepack installed".</summary>
public sealed class GamepackUpdateOrchestratorFirstInstallFailureTests {
  [Fact]
  public async Task AFailedFirstInstallLeavesNoGamepackAndNoFile() {
    using var rig = new ManagedTestRig();
    rig.Packs.Gate.Arm(2);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha", "beta")));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Reason.Should().Be(ManagedUpdateReasons.InstallFailed);
    report.Restore.Happened.Should().BeTrue();
    report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
    rig.Service.List().Should().BeEmpty();
    (await rig.Packs.Sequences.ListAsync()).Should().BeEmpty();
    (await rig.Packs.Commands.ListAsync()).Should().BeEmpty();
    rig.Packs.Store.Load().Gamepacks.Should().BeEmpty();
    File.Exists(Path.Combine(rig.Root, "gamepacks", "registry.json")).Should().BeFalse();
  }

  [Fact]
  public async Task AnApplierRestoreThatFailsLeavesNothingBecauseTheFlowRemovesWhatIsLeft() {
    using var rig = new ManagedTestRig();
    rig.Packs.Gate.Arm(2);
    rig.Packs.Gate.ThrowOnRestore = true;

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha", "beta")));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Restore.Happened.Should().BeTrue();
    report.Restore.Steps.Select(s => s.Step).Should().Contain("gamepack_remove");
    rig.Service.List().Should().BeEmpty();
    (await rig.Packs.Sequences.ListAsync()).Should().BeEmpty();
    (await rig.Packs.Commands.ListAsync()).Should().BeEmpty();
  }
}

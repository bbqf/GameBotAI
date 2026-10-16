using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T041): a dry run changes nothing and returns the plan.</summary>
public sealed class GamepackUpdateOrchestratorDryRunTests {
  [Fact]
  public async Task ADryRunCallsNoStopNoBackupNoInstallNoTemplateSave() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    var queue = await rig.AddQueueAsync("Q", template, running: true);
    var before = await rig.StateAsync();

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma"), dryRun: true));

    report.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(report));
    report.Reason.Should().BeNull();
    report.DryRun.Should().BeTrue();
    report.StoppedQueues.Select(q => q.Id).Should().Equal(queue.Id);
    report.RestartedQueues.Should().BeEmpty();
    report.BackupId.Should().BeNull();
    report.Created.Should().Be(1);
    report.Removed.Should().Be(1);
    report.NewEntries.Should().ContainSingle().Which.SequenceName.Should().Be("gamma");
    report.RemovedSequenceEntries.Should().ContainSingle().Which.SequenceName.Should().Be("beta");
    rig.Control.Calls.Should().BeEmpty();
    rig.Templates.Updates.Should().Be(0);
    Directory.Exists(Path.Combine(rig.Root, "gamepacks", "backups")).Should().BeFalse();
    (await rig.StateAsync()).Should().Be(before);
  }

  [Fact]
  public async Task ADryRunWithAProblemGivesRejectedAndAllProblems() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");

    var report = await rig.RunAsync(ManagedTestRig.Update("not json", dryRun: true));

    report.Result.Should().Be(ManagedUpdateResults.Rejected);
    report.Reason.Should().Be(ManagedUpdateReasons.InvalidFile);
    report.Problems.Should().NotBeEmpty();
    rig.Control.Calls.Should().BeEmpty();
  }
}

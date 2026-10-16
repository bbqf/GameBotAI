using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T022, FR-018): the same version is a no-op.</summary>
public sealed class GamepackUpdateOrchestratorSameVersionTests {
  [Fact]
  public async Task TheSameVersionTextStopsNothingStoresNoBackupAndChangesNothing() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var template = await rig.AddTemplateAsync("daily", "alpha");
    await rig.AddQueueAsync("Q", template, running: true);
    var before = await rig.StateAsync();

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha", "extra")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.StoppedQueues.Should().BeEmpty();
    report.RestartedQueues.Should().BeEmpty();
    report.BackupId.Should().BeNull();
    report.NewEntries.Should().BeEmpty();
    rig.Control.Calls.Should().BeEmpty();
    Directory.Exists(Path.Combine(rig.Root, "gamepacks", "backups")).Should().BeFalse();
    (await rig.StateAsync()).Should().Be(before);
  }

  [Fact]
  public async Task AVersionTextThatDiffersOnlyInCaseIsAnotherVersion() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0-RC", "alpha");

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0-rc", "alpha")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    rig.Service.List()[0].Version.Should().Be("1.0.0-rc");
  }
}

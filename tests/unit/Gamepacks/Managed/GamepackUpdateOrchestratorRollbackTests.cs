using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T046): the managed rollback.</summary>
public sealed class GamepackUpdateOrchestratorRollbackTests {
  [Fact]
  public async Task ANameThatNoGamepackHasGivesGamepackNotFound() {
    using var rig = new ManagedTestRig();

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Rejected);
    report.Reason.Should().Be(ManagedUpdateReasons.GamepackNotFound);
  }

  [Fact]
  public async Task AGamepackWithNoPreviousVersionGivesNoPreviousVersion() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Reason.Should().Be(ManagedUpdateReasons.NoPreviousVersion);
  }

  [Fact]
  public async Task APreviousFileThatIsNotValidGivesRollbackRefused() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    await rig.InstallAsync("2.0.0", "alpha");
    rig.Packs.Store.WritePrevious("pns", "not json");

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Reason.Should().Be(ManagedUpdateReasons.RollbackRefused);
    rig.Service.List().Single().Version.Should().Be("2.0.0");
  }

  [Fact]
  public async Task AManagedRollbackStopsBacksUpRollsBackAndRestarts() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    await rig.InstallAsync("2.0.0", "alpha", "gamma");
    var template = await rig.AddTemplateAsync("daily", "alpha", "gamma");
    var queue = await rig.AddQueueAsync("Q", template, running: true);

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.GamepackVersion.Should().Be("1.0.0");
    report.PreviousVersion.Should().Be("2.0.0");
    report.StoppedQueues.Select(q => q.Id).Should().Equal(queue.Id);
    report.RestartedQueues.Select(q => q.Id).Should().Equal(queue.Id);
    report.BackupId.Should().NotBeNull();
    report.RemovedSequenceEntries.Should().ContainSingle().Which.SequenceName.Should().Be("gamma");
    rig.Service.List().Single().Version.Should().Be("1.0.0");
  }

  [Fact]
  public async Task ARollbackBooksDisabledEntriesForSequencesThatThePreviousVersionAdds() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    await rig.InstallAsync("2.0.0", "alpha");
    var template = await rig.AddTemplateAsync("daily", "alpha");
    await rig.AddQueueAsync("Q", template, running: false);

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.NewEntries.Should().ContainSingle().Which.SequenceName.Should().Be("beta");
    var after = await rig.TemplateAsync("daily");
    after.Entries.Should().HaveCount(2);
    after.Entries[1].Enabled.Should().BeFalse();
  }

  [Fact]
  public async Task AFailedRollbackRestoresTheStateBeforeTheCall() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    await rig.InstallAsync("2.0.0", "alpha", "gamma");
    var template = await rig.AddTemplateAsync("daily", "alpha", "gamma");
    await rig.AddQueueAsync("Q", template, running: true);
    var before = await rig.StateAsync();
    rig.Packs.Gate.Arm(1);

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
    (await rig.StateAsync()).Should().Be(before);
  }

  [Fact]
  public async Task AFailureAfterAGoodRollbackPutsBackTheRecordedStateWithoutASecondRollback() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    await rig.InstallAsync("2.0.0", "alpha");
    var template = await rig.AddTemplateAsync("daily", "alpha");
    await rig.AddQueueAsync("Q", template, running: true);
    var before = await rig.StateAsync();
    rig.Templates.FailUpdate = true;

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Reason.Should().Be(ManagedUpdateReasons.BookingFailed);
    report.Restore.Steps.Select(s => s.Step).Should().Contain("gamepack_reapply").And.NotContain("gamepack_rollback");
    report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
    rig.Service.List().Single().Version.Should().Be("2.0.0");
    (await rig.StateAsync()).Should().Be(before);
  }
}

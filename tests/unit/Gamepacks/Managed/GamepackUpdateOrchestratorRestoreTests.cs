using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T035): the restore after a failed step.</summary>
public sealed class GamepackUpdateOrchestratorRestoreTests {
  private static async Task<(ManagedTestRig Rig, string Before, string QueueId)> ArrangeAsync() {
    var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    var queue = await rig.AddQueueAsync("Q", template, running: true);
    return (rig, await rig.StateAsync(), queue.Id);
  }

  [Fact]
  public async Task AFailedInstallLeavesTheStateAsBeforeAndRestartsTheStoppedQueue() {
    var (rig, before, queueId) = await ArrangeAsync();
    using (rig) {
      rig.Packs.Gate.Arm(1);

      var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

      report.Result.Should().Be(ManagedUpdateResults.Failed);
      report.Reason.Should().Be(ManagedUpdateReasons.InstallFailed);
      report.Restore.Happened.Should().BeTrue();
      report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
      report.Restore.FailedObjects.Should().BeEmpty();
      report.StoppedQueues.Select(q => q.Id).Should().Equal(queueId);
      report.RestartedQueues.Select(q => q.Id).Should().Equal(queueId);
      (await rig.StateAsync()).Should().Be(before);
    }
  }

  [Fact]
  public async Task AFailedBookingRollsTheGamepackBackAndImportsTheBackup() {
    var (rig, before, _) = await ArrangeAsync();
    using (rig) {
      rig.Templates.FailUpdate = true;

      var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

      report.Result.Should().Be(ManagedUpdateResults.Failed);
      report.Reason.Should().Be(ManagedUpdateReasons.BookingFailed);
      report.BackupId.Should().NotBeNull();
      report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
      report.Restore.Steps.Select(s => s.Step).Should().ContainInOrder("gamepack_rollback", "gamepack_metadata", "settings_import");
      report.Restore.Steps[^1].Step.Should().StartWith("start_queue:");
      (await rig.StateAsync()).Should().Be(before);
    }
  }

  [Fact]
  public async Task AFailedRestoreStepIsListedAndTheOtherStepsStillRun() {
    var (rig, _, queueId) = await ArrangeAsync();
    using (rig) {
      rig.Templates.FailUpdate = true;
      rig.Control.StartFails.Add(queueId);

      var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

      report.Result.Should().Be(ManagedUpdateResults.Failed);
      report.Restore.Happened.Should().BeTrue();
      report.Restore.Restored.Should().BeFalse();
      report.Restore.FailedObjects.Should().Contain("queue:Q");
      report.Restore.Steps.Single(s => s.Step == "gamepack_rollback").Outcome.Should().Be("ok");
      report.NotRestartedQueues.Select(q => q.Id).Should().Equal(queueId);
      rig.Service.List().Single().Version.Should().Be("1.0.0");
    }
  }

  [Fact]
  public async Task AStopTimeoutRestartsTheStoppedQueuesAndTouchesNoGamepackOrTemplate() {
    var (rig, before, firstId) = await ArrangeAsync();
    using (rig) {
      var template = await rig.TemplateAsync("daily");
      var second = await rig.AddQueueAsync("Q2", template, running: true);
      before = await rig.StateAsync();
      rig.Control.StopTimesOut.Add(second.Id);

      var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

      report.Result.Should().Be(ManagedUpdateResults.Failed);
      report.Reason.Should().Be(ManagedUpdateReasons.StopTimeout);
      report.BackupId.Should().BeNull();
      report.Restore.Happened.Should().BeTrue();
      report.Restore.Steps.Should().OnlyContain(s => s.Step.StartsWith("start_queue:", System.StringComparison.Ordinal));
      report.Restore.Steps.Should().NotBeEmpty();
      rig.Service.List().Single().Version.Should().Be("1.0.0");
      (await rig.TemplateAsync("daily")).Entries.Should().HaveCount(2);
      Directory.Exists(Path.Combine(rig.Root, "gamepacks", "backups")).Should().BeFalse();
      rig.Control.StartCalls.Should().Contain(second.Id);
      firstId.Should().NotBeNull();
      (await rig.StateAsync()).Should().Be(before);
    }
  }

  [Fact]
  public async Task ABackupFailureEndsWithBackupFailedAndRestartsTheQueues() {
    var (rig, before, _) = await ArrangeAsync();
    using (rig) {
      // A file where the backup folder must be makes the write fail.
      Directory.CreateDirectory(Path.Combine(rig.Root, "gamepacks"));
      await File.WriteAllTextAsync(Path.Combine(rig.Root, "gamepacks", "backups"), "not a folder");

      var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

      report.Result.Should().Be(ManagedUpdateResults.Failed);
      report.Reason.Should().Be(ManagedUpdateReasons.BackupFailed);
      report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
      (await rig.StateAsync()).Should().Be(before);
    }
  }
}

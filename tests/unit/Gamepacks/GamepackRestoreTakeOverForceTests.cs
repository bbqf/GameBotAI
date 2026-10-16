using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T028): the second restore, the force mode, and the gate (FR-016).</summary>
public sealed class GamepackRestoreTakeOverForceTests {
  private static async Task<(GamepackTestKit Kit, string BackupId, string SequenceId, string CommandId)> ArrangeAsync() {
    var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    var sequenceId = (await kit.SequenceByNameAsync("Help All"))!.Id;
    var commandId = (await kit.CommandByNameAsync("Tap help"))!.Id;
    var outcome = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());
    return (kit, outcome.TakeOverBackupId!, sequenceId, commandId);
  }

  [Fact]
  public async Task ASecondRestoreOfTheSameBackupGivesOwnerChangedAndChangesNothing() {
    var (kit, backupId, _, _) = await ArrangeAsync();
    using (kit) {
      (await kit.Service.RestoreTakeOverAsync(backupId)).Status.Should().Be(TakeOverRestoreStatus.Restored);
      var before = kit.Snapshot();

      var second = await kit.Service.RestoreTakeOverAsync(backupId);

      second.Status.Should().Be(TakeOverRestoreStatus.Refused);
      second.Reasons.Should().OnlyContain(r => r.Code == "owner_changed");
      kit.Snapshot().Should().BeEquivalentTo(before);
    }
  }

  [Fact]
  public async Task ForceSkipsTheOwnerCheckOnly() {
    var (kit, backupId, sequenceId, _) = await ArrangeAsync();
    using (kit) {
      kit.Service.RemoveOwner(GamepackObjectTypes.Sequence, sequenceId);

      (await kit.Service.RestoreTakeOverAsync(backupId)).Status.Should().Be(TakeOverRestoreStatus.Refused);
      var forced = await kit.Applier.RestoreTakeOverAsync(backupId, TakeOverRestoreMode.Force);

      forced.Status.Should().Be(TakeOverRestoreStatus.Restored);
      forced.Restored.Should().Be(3);
    }
  }

  [Fact]
  public async Task ForceStillRefusesAMissingObject() {
    var (kit, backupId, _, commandId) = await ArrangeAsync();
    using (kit) {
      await kit.Commands.DeleteAsync(commandId);

      var forced = await kit.Applier.RestoreTakeOverAsync(backupId, TakeOverRestoreMode.Force);

      forced.Status.Should().Be(TakeOverRestoreStatus.Refused);
      forced.Reasons.Should().ContainSingle().Which.Code.Should().Be("object_missing");
    }
  }

  [Fact]
  public async Task ForceStillRefusesAChangedObject() {
    var (kit, backupId, sequenceId, _) = await ArrangeAsync();
    using (kit) {
      var live = (await kit.Sequences.GetAsync(sequenceId))!;
      live.WatchdogTimeoutMs = 4242;
      await kit.Sequences.UpdateAsync(live);

      var forced = await kit.Applier.RestoreTakeOverAsync(backupId, TakeOverRestoreMode.Force);

      forced.Status.Should().Be(TakeOverRestoreStatus.Refused);
      forced.Reasons.Should().ContainSingle().Which.Code.Should().Be("object_changed");
    }
  }

  [Fact]
  public async Task TheForcedRestoreRunsInsideTheGateThatTheCallerHoldsWithoutADeadlock() {
    var (kit, backupId, _, _) = await ArrangeAsync();
    using (kit) {
      var task = kit.Service.RunExclusiveAsync(() => kit.Service.RestoreTakeOverForcedInGateAsync(backupId));

      var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));

      done.Should().BeSameAs(task, "the forced restore must not take the gate a second time");
      (await task).Status.Should().Be(TakeOverRestoreStatus.Restored);
    }
  }

  [Fact]
  public async Task ThePublicRestoreTakesTheGateItself() {
    var (kit, backupId, _, _) = await ArrangeAsync();
    using (kit) {
      var started = new TaskCompletionSource();
      var release = new TaskCompletionSource();
      var hold = kit.Service.RunExclusiveAsync(async () => {
        started.SetResult();
        await release.Task;
        return 0;
      });
      await started.Task;

      var restore = kit.Service.RestoreTakeOverAsync(backupId);
      await Task.Delay(300);
      restore.IsCompleted.Should().BeFalse("the restore waits for the gate");
      release.SetResult();
      await hold;

      (await restore).Status.Should().Be(TakeOverRestoreStatus.Restored);
    }
  }
}

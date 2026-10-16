using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T027): the strict restore of a take-over backup.</summary>
public sealed class GamepackRestoreTakeOverTests {
  private static async Task<(GamepackTestKit Kit, string BackupId, string SequenceId, string CommandId, string SequenceHash)> ArrangeAsync() {
    var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    var sequence = (await kit.SequenceByNameAsync("Help All"))!;
    var hash = GamepackContentComparer.Hash(sequence);
    var commandId = (await kit.CommandByNameAsync("Tap help"))!.Id;
    var outcome = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());
    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    return (kit, outcome.TakeOverBackupId!, sequence.Id, commandId, hash);
  }

  [Fact]
  public async Task ARestorePutsTheOldContentBackAndTheOwnerIsTheUser() {
    var (kit, backupId, sequenceId, commandId, hash) = await ArrangeAsync();
    using (kit) {
      var outcome = await kit.Service.RestoreTakeOverAsync(backupId);

      outcome.Status.Should().Be(TakeOverRestoreStatus.Restored);
      outcome.Restored.Should().Be(3);
      GamepackContentComparer.Hash((await kit.SequenceByNameAsync("Help All"))!).Should().Be(hash);
      kit.OwnerOf(GamepackObjectTypes.Sequence, sequenceId).IsUser.Should().BeTrue();
      kit.OwnerOf(GamepackObjectTypes.Command, commandId).IsUser.Should().BeTrue();
      kit.OwnerOf(GamepackObjectTypes.Image, "icon").IsUser.Should().BeTrue();
      kit.Store.Load().Gamepacks["pns"].Objects.Select(o => o.Id).Should().NotContain(new[] { sequenceId, commandId, "icon" });
      File.Exists(Path.Combine(kit.Root, "gamepacks", "takeover-backups", backupId + ".json")).Should().BeTrue();
    }
  }

  [Fact]
  public async Task AMissingObjectRefusesWithObjectMissing() {
    var (kit, backupId, _, commandId, _) = await ArrangeAsync();
    using (kit) {
      await kit.Commands.DeleteAsync(commandId);
      var before = kit.Snapshot();

      var outcome = await kit.Service.RestoreTakeOverAsync(backupId);

      outcome.Status.Should().Be(TakeOverRestoreStatus.Refused);
      outcome.Reasons.Should().ContainSingle().Which.Should().Match<TakeOverRestoreReason>(r => r.Code == "object_missing" && r.Type == "command");
      kit.Snapshot().Should().BeEquivalentTo(before);
    }
  }

  [Fact]
  public async Task AChangedObjectRefusesWithObjectChanged() {
    var (kit, backupId, sequenceId, _, _) = await ArrangeAsync();
    using (kit) {
      var live = (await kit.Sequences.GetAsync(sequenceId))!;
      live.WatchdogTimeoutMs = 123456;
      await kit.Sequences.UpdateAsync(live);
      var before = kit.Snapshot();

      var outcome = await kit.Service.RestoreTakeOverAsync(backupId);

      outcome.Status.Should().Be(TakeOverRestoreStatus.Refused);
      outcome.Reasons.Should().ContainSingle().Which.Code.Should().Be("object_changed");
      kit.Snapshot().Should().BeEquivalentTo(before);
    }
  }

  [Fact]
  public async Task ALostOwnerRefusesWithOwnerChanged() {
    var (kit, backupId, sequenceId, _, _) = await ArrangeAsync();
    using (kit) {
      kit.Service.RemoveOwner(GamepackObjectTypes.Sequence, sequenceId);
      var before = kit.Snapshot();

      var outcome = await kit.Service.RestoreTakeOverAsync(backupId);

      outcome.Status.Should().Be(TakeOverRestoreStatus.Refused);
      outcome.Reasons.Should().ContainSingle().Which.Code.Should().Be("owner_changed");
      kit.Snapshot().Should().BeEquivalentTo(before);
    }
  }

  [Theory]
  [InlineData("to-20200101T000000000-00000000")]
  [InlineData("not-an-id")]
  [InlineData("..\\..\\registry")]
  public async Task AnUnknownOrMalformedIdGivesNotFound(string id) {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.RestoreTakeOverAsync(id);

    outcome.Status.Should().Be(TakeOverRestoreStatus.NotFound);
  }

  [Fact]
  public async Task AFaultInTheRestoreLeavesTheStateAsBefore() {
    var (kit, backupId, _, _, _) = await ArrangeAsync();
    using (kit) {
      var before = kit.Snapshot();
      for (var n = 1; n <= 4; n++) {
        kit.Gate.Arm(n);

        var outcome = await kit.Service.RestoreTakeOverAsync(backupId);

        outcome.Status.Should().Be(TakeOverRestoreStatus.Failed, "a fault at restore write " + n);
        kit.Snapshot().Should().BeEquivalentTo(before, "a fault at restore write " + n);
      }

      kit.Gate.Arm(0);
      (await kit.Service.RestoreTakeOverAsync(backupId)).Status.Should().Be(TakeOverRestoreStatus.Restored);
    }
  }

  [Fact]
  public async Task OnlyThreeReasonCodesExist() {
    var (kit, backupId, sequenceId, commandId, _) = await ArrangeAsync();
    using (kit) {
      await kit.Commands.DeleteAsync(commandId);
      var live = (await kit.Sequences.GetAsync(sequenceId))!;
      live.WatchdogTimeoutMs = 99999;
      await kit.Sequences.UpdateAsync(live);
      kit.Service.RemoveOwner(GamepackObjectTypes.Image, "icon");

      var outcome = await kit.Service.RestoreTakeOverAsync(backupId);

      outcome.Status.Should().Be(TakeOverRestoreStatus.Refused);
      outcome.Reasons.Select(r => r.Code).Should().BeEquivalentTo("object_missing", "object_changed", "owner_changed");
      new[] { TakeOverRestoreReasonCodes.ObjectMissing, TakeOverRestoreReasonCodes.ObjectChanged, TakeOverRestoreReasonCodes.OwnerChanged }
        .Should().BeEquivalentTo("object_missing", "object_changed", "owner_changed");
    }
  }
}

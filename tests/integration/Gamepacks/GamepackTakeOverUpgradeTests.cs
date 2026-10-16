using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 149 (T018): an upgrade takes over only the user objects that are new in the pack (FR-003, FR-012).</summary>
public sealed class GamepackTakeOverUpgradeTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  [Fact]
  public async Task AnUpgradeTakesOverTheNewObjectAndCountsTheOwnedObjectAsUpdated() {
    var v1 = GamepackTestKit.Pack("pns", "1.0", sequences: new[] { GamepackTestKit.Seq("a", "Object A") });
    var v2 = GamepackTestKit.Pack("pns", "2.0", sequences: new[] { GamepackTestKit.Seq("a", "Object A"), GamepackTestKit.Seq("b", "Object B", "x") },
      commands: new[] { GamepackTestKit.Cmd("x", "Cmd X") });
    (await _kit.Service.InstallAsync(v1, dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);
    await _kit.AddUserObjectsAsync(GamepackTestKit.Pack("user", "1", sequences: new[] { GamepackTestKit.Seq("ub", "Object B") }));
    var userBId = (await _kit.SequenceByNameAsync("Object B"))!.Id;

    var upgrade = await _kit.InstallTakeOverAsync(v2);

    upgrade.Status.Should().Be(GamepackInstallStatus.Ok);
    upgrade.TakenOver.Should().Be(1);
    upgrade.Updated.Should().Be(1);
    upgrade.Created.Should().Be(1);
    upgrade.TakeOverBackupId.Should().NotBeNull();
    (await _kit.SequenceByNameAsync("Object B"))!.Id.Should().Be(userBId);
    _kit.OwnerOf(GamepackObjectTypes.Sequence, userBId).Name.Should().Be("pns");
    _kit.Store.Load().Gamepacks["pns"].Previous.Should().NotBeNull("the kept previous version still works");
  }

  [Fact]
  public async Task ARollbackAfterATakeOverUpgradeLeavesNoStrayObject() {
    var v1 = GamepackTestKit.Pack("pns", "1.0", sequences: new[] { GamepackTestKit.Seq("a", "Object A") });
    var v2 = GamepackTestKit.Pack("pns", "2.0", sequences: new[] { GamepackTestKit.Seq("a", "Object A"), GamepackTestKit.Seq("b", "Object B") });
    (await _kit.Service.InstallAsync(v1, dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);
    await _kit.AddUserObjectsAsync(GamepackTestKit.Pack("user", "1", sequences: new[] { GamepackTestKit.Seq("ub", "Object B") }));
    (await _kit.InstallTakeOverAsync(v2)).Status.Should().Be(GamepackInstallStatus.Ok);

    var rollback = await _kit.Service.RollbackAsync("pns");

    rollback.Status.Should().Be(GamepackRollbackStatus.RolledBack);
    (await _kit.SequenceByNameAsync("Object B")).Should().BeNull("a rollback removes an object that the old version did not own");
  }
}

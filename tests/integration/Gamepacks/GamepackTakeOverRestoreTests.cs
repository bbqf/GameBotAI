using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 149 (T030): the restore on real file stores (SC-004, SC-005).</summary>
public sealed class GamepackTakeOverRestoreTests {
  [Fact]
  public async Task InstallThenRestoreGivesTheOldContentAndTheUserOwner() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTakeOverLargeCaseTests.UserPack());
    var sequences = await kit.Sequences.ListAsync();
    var before = new System.Collections.Generic.Dictionary<string, string>();
    foreach (var s in sequences) {
      before[s.Id] = GamepackContentComparer.Hash(s);
    }

    var install = await kit.InstallTakeOverAsync(GamepackTakeOverLargeCaseTests.BigPack());
    install.Status.Should().Be(GamepackInstallStatus.Ok);
    install.TakenOver.Should().Be(144);

    var restore = await kit.Service.RestoreTakeOverAsync(install.TakeOverBackupId!);

    restore.Status.Should().Be(TakeOverRestoreStatus.Restored);
    restore.Restored.Should().Be(144);
    foreach (var s in await kit.Sequences.ListAsync()) {
      GamepackContentComparer.Hash(s).Should().Be(before[s.Id]);
      kit.OwnerOf(GamepackObjectTypes.Sequence, s.Id).IsUser.Should().BeTrue();
    }

    var again = await kit.Service.RestoreTakeOverAsync(install.TakeOverBackupId!);
    again.Status.Should().Be(TakeOverRestoreStatus.Refused);
    again.Reasons.Should().OnlyContain(r => r.Code == TakeOverRestoreReasonCodes.OwnerChanged);
  }

  [Fact]
  public async Task AForcedFailureInTheMiddleOfAnInstallLeavesNoChangedObject() {
    var pack = GamepackTestKit.Pack("pns", "1",
      sequences: new[] { GamepackTestKit.Seq("p1", "One", "pc"), GamepackTestKit.Seq("p2", "Two") },
      commands: new[] { GamepackTestKit.Cmd("pc", "Cmd", imageId: "icon") },
      images: new[] { GamepackTestKit.Img("icon") });
    var user = GamepackTestKit.Pack("user", "1",
      sequences: new[] { GamepackTestKit.Seq("u1", "One", "uc"), GamepackTestKit.Seq("u2", "Two") },
      commands: new[] { GamepackTestKit.Cmd("uc", "Cmd") },
      images: new[] { GamepackTestKit.Img("icon", data: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==") });
    int total;
    using (var count = new GamepackTestKit()) {
      await count.AddUserObjectsAsync(user);
      count.Gate.Arm(0);
      (await count.InstallTakeOverAsync(pack)).Status.Should().Be(GamepackInstallStatus.Ok);
      total = count.Gate.Calls;
    }

    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      await kit.AddUserObjectsAsync(user);
      var before = kit.Snapshot();
      kit.Gate.Arm(n);

      var outcome = await kit.InstallTakeOverAsync(pack);

      outcome.Status.Should().Be(GamepackInstallStatus.Failed, "a fault at call " + n);
      kit.Snapshot().Should().BeEquivalentTo(before, "a fault at call " + n);
      kit.Store.Load().Owners.Should().BeEmpty();
    }
  }
}

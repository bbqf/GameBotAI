using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T017): the applier writes a take-over through the update path and sets the owners.</summary>
public sealed class GamepackApplierTakeOverTests {
  internal static string UserPack() => GamepackTestKit.Pack("user-objects", "1",
    sequences: new[] { GamepackTestKit.Seq("u-seq", "Help All", "u-cmd") },
    commands: new[] { GamepackTestKit.Cmd("u-cmd", "Tap help") },
    images: new[] { GamepackTestKit.Img("icon") });

  internal static string Pack(string version = "1") => GamepackTestKit.Pack("pns", version,
    sequences: new[] { GamepackTestKit.Seq("p-seq", "Help All", "p-cmd"), GamepackTestKit.Seq("p-new", "Brand New") },
    commands: new[] { GamepackTestKit.Cmd("p-cmd", "Tap help", imageId: "icon") },
    images: new[] { GamepackTestKit.Img("icon", data: GamepackTestKit.PngBase64) });

  [Fact]
  public async Task ATakeOverKeepsTheIdAndMovesTheOwnerToTheGamepack() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack());
    var sequenceId = (await kit.SequenceByNameAsync("Help All"))!.Id;
    var commandId = (await kit.CommandByNameAsync("Tap help"))!.Id;

    var outcome = await kit.InstallTakeOverAsync(Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok, outcome.ErrorMessage + string.Join(";", outcome.Problems.Select(p => p.Message)));
    outcome.TakenOver.Should().Be(3);
    outcome.Created.Should().Be(1);
    outcome.Updated.Should().Be(0);
    (await kit.SequenceByNameAsync("Help All"))!.Id.Should().Be(sequenceId);
    (await kit.CommandByNameAsync("Tap help"))!.Id.Should().Be(commandId);
    kit.OwnerOf(GamepackObjectTypes.Sequence, sequenceId).Name.Should().Be("pns");
    kit.OwnerOf(GamepackObjectTypes.Command, commandId).Name.Should().Be("pns");
    kit.OwnerOf(GamepackObjectTypes.Image, "icon").Name.Should().Be("pns");
    var record = kit.Store.Load().Gamepacks["pns"];
    record.Objects.Select(o => o.Id).Should().Contain(new[] { sequenceId, commandId, "icon" });
  }

  [Fact]
  public async Task TheTakenOverCountEqualsTheReplacedObjects() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack());

    var outcome = await kit.InstallTakeOverAsync(Pack());

    outcome.TakenOver.Should().Be(3);
    outcome.TakeOverBackupId.Should().NotBeNull();
    var backup = kit.TakeOverBackups.Read(outcome.TakeOverBackupId!);
    backup!.Entries.Should().HaveCount(outcome.TakenOver);
  }

  [Fact]
  public async Task AnInstallWithTheOptionOffDoesNotCountATakeOver() {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.InstallAsync(Pack(), dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.TakenOver.Should().Be(0);
    outcome.TakeOverBackupId.Should().BeNull();
  }

  [Fact]
  public async Task AnApplierWithoutAStoreFailsAndChangesNothingWhenThePlanHasATakeOver() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack());
    var parsed = GamepackFileSerializer.Parse(Pack());
    var plan = (await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, takeOver: true)).Plan!;
    var applier = new GamepackApplier(kit.Sequences, kit.Commands, kit.Images, kit.Store, kit.Gate);
    var before = kit.Snapshot();

    var outcome = await applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.Failed);
    kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task ABackupFileExistsInTheDataFolderAfterATakeOver() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack());

    var outcome = await kit.InstallTakeOverAsync(Pack());

    File.Exists(Path.Combine(kit.Root, "gamepacks", "takeover-backups", outcome.TakeOverBackupId + ".json")).Should().BeTrue();
  }
}

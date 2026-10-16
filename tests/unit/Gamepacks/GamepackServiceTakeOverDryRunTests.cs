using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T038): a dry run with the option on lists the take-overs and changes nothing.</summary>
public sealed class GamepackServiceTakeOverDryRunTests {
  [Fact]
  public async Task ADryRunCountEqualsTheCountOfTheRealInstallAndWritesNoBackup() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    var before = kit.Snapshot();

    var dry = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack(), dryRun: true);

    dry.Status.Should().Be(GamepackInstallStatus.Ok);
    dry.DryRun.Should().BeTrue();
    dry.TakeOverBackupId.Should().BeNull();
    dry.TakeOvers.Should().NotBeNull();
    kit.Snapshot().Should().BeEquivalentTo(before);
    Directory.Exists(Path.Combine(kit.Root, "gamepacks", "takeover-backups")).Should().BeFalse();

    var real = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());
    real.TakenOver.Should().Be(dry.TakenOver);
    dry.TakeOvers!.Count.Should().Be(real.TakenOver);
  }

  [Fact]
  public async Task TheListMarksIdenticalAndDifferentObjects() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1",
      sequences: new[] { GamepackTestKit.Seq("u-seq", "Help All", "u-cmd") },
      commands: new[] { GamepackTestKit.Cmd("u-cmd", "Tap help", imageId: "other") },
      images: new[] { GamepackTestKit.Img("icon") }));

    var dry = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack(), dryRun: true);

    dry.TakeOvers!.Single(t => t.Type == "image").Identical.Should().BeTrue();
    dry.TakeOvers!.Single(t => t.Type == "command").Identical.Should().BeFalse();
    dry.TakeOvers!.Single(t => t.Type == "sequence").Identical.Should().BeTrue();
  }

  [Fact]
  public async Task ADryRunWithTheOptionOffReportsTheConflictAndNoList() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());

    var dry = await kit.Service.InstallAsync(GamepackApplierTakeOverTests.Pack(), new GamepackInstallOptions(DryRun: true));

    dry.Status.Should().Be(GamepackInstallStatus.Invalid);
    dry.Problems.Should().NotBeEmpty();
    dry.TakeOvers.Should().BeNull();
  }
}

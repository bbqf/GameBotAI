using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 161 (T011): the field WouldInstallWithTakeOver of the install outcome.</summary>
public sealed class GamepackServiceTakeOverOutcomeTests {
  private static Task<GamepackInstallOutcome> DryAsync(GamepackTestKit kit, string json, bool takeOver = false) =>
    kit.Service.InstallAsync(json, new GamepackInstallOptions(DryRun: true, TakeOver: takeOver));

  [Fact]
  public async Task ManyUserConflictsWouldInstallWithTakeOver() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());

    var outcome = await DryAsync(kit, GamepackApplierTakeOverTests.Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.WouldInstallWithTakeOver.Should().BeTrue();
  }

  [Fact]
  public async Task AConflictWithAMissingReferenceWouldNotInstall() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    var json = GamepackTestKit.Pack("pns", "1",
      sequences: new[] { GamepackTestKit.Seq("p-seq", "Help All", "missing-cmd") });

    var outcome = await DryAsync(kit, json);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.WouldInstallWithTakeOver.Should().BeFalse();
  }

  [Fact]
  public async Task AValidFileGivesTrue() {
    using var kit = new GamepackTestKit();

    var outcome = await DryAsync(kit, GamepackApplierTakeOverTests.Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.WouldInstallWithTakeOver.Should().BeTrue();
  }

  [Fact]
  public async Task AConflictWithAnotherGamepackGivesFalse() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(GamepackTestKit.Pack("alpha", "1", commands: new[] { GamepackTestKit.Cmd("c", "Shared") }), dryRun: false))
      .Status.Should().Be(GamepackInstallStatus.Ok);

    var outcome = await DryAsync(kit, GamepackTestKit.Pack("beta", "1", commands: new[] { GamepackTestKit.Cmd("c9", "Shared") }));

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.WouldInstallWithTakeOver.Should().BeFalse();
  }

  [Fact]
  public async Task WithTakeOverOnAndARemainingReferenceProblemTheValueIsFalse() {
    using var kit = new GamepackTestKit();
    var json = GamepackTestKit.Pack("pns", "1",
      sequences: new[] { GamepackTestKit.Seq("p-seq", "Help All", "missing-cmd") });

    var outcome = await DryAsync(kit, json, takeOver: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.WouldInstallWithTakeOver.Should().BeFalse();
  }

  [Fact]
  public async Task InvalidJsonGivesFalse() {
    using var kit = new GamepackTestKit();

    var outcome = await DryAsync(kit, "{ not json");

    outcome.Status.Should().Be(GamepackInstallStatus.InvalidJson);
    outcome.WouldInstallWithTakeOver.Should().BeFalse();
  }

  [Fact]
  public async Task TwoUserObjectsWithTheSameNameGiveFalseAndThenAreNotGuessed() {
    using var kit = new GamepackTestKit();
    await kit.Sequences.CreateAsync(new CommandSequence { Id = "u1", Name = "Dup" });
    await kit.Sequences.CreateAsync(new CommandSequence { Id = "u2", Name = "Dup" });
    var json = GamepackTestKit.Pack("pns", "1", sequences: new[] { GamepackTestKit.Seq("p1", "Dup") });

    var outcome = await DryAsync(kit, json);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    var answer = GamepackFailureExplainer.Summarize(outcome.Problems, takeOverOn: false);
    answer.Entries.Single().Count.Should().Be(outcome.Problems.Count);
    // The value comes from the second check, not from a guess on the problem list.
    var withTakeOver = await DryAsync(kit, json, takeOver: true);
    outcome.WouldInstallWithTakeOver.Should().Be(withTakeOver.Status == GamepackInstallStatus.Ok);
  }

  [Fact]
  public async Task TheSecondCheckWritesNothing() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    var before = kit.Snapshot();

    var outcome = await DryAsync(kit, GamepackApplierTakeOverTests.Pack());

    outcome.WouldInstallWithTakeOver.Should().BeTrue();
    kit.Snapshot().Should().BeEquivalentTo(before);
    System.IO.Directory.Exists(System.IO.Path.Combine(kit.Root, "gamepacks", "takeover-backups")).Should().BeFalse();
  }
}

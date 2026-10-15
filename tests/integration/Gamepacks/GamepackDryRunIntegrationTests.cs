using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 143 (T042): the dry run changes no data and gives the same result as the real install (SC-005, FR-014).</summary>
public sealed class GamepackDryRunIntegrationTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private static string Pack(string version) => GamepackTestKit.Pack("pns", version,
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap", imageId: "icon") },
    images: new[] { GamepackTestKit.Img("icon") });

  [Fact]
  public async Task AValidDryRunShowsTheCountsAndChangesNoFile() {
    var before = _kit.Snapshot();

    var outcome = await _kit.Service.InstallAsync(Pack("1"), dryRun: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.DryRun.Should().BeTrue();
    (outcome.Created, outcome.Updated, outcome.Removed).Should().Be((3, 0, 0));
    _kit.Snapshot().Should().BeEquivalentTo(before);
    _kit.Gate.Calls.Should().Be(0, "a dry run never reaches a write");
  }

  [Fact]
  public async Task TheDryRunCountsEqualTheRealInstallCounts() {
    await _kit.Service.InstallAsync(Pack("1"), false);
    var next = GamepackTestKit.Pack("pns", "2", sequences: new[] { GamepackTestKit.Seq("s1", "Daily"), GamepackTestKit.Seq("n", "New") });
    var before = _kit.Snapshot();

    var dry = await _kit.Service.InstallAsync(next, dryRun: true);
    _kit.Snapshot().Should().BeEquivalentTo(before);
    var real = await _kit.Service.InstallAsync(next, dryRun: false);

    (dry.Created, dry.Updated, dry.Removed).Should().Be((real.Created, real.Updated, real.Removed));
    (real.Created, real.Updated, real.Removed).Should().Be((1, 1, 2));
  }

  [Fact]
  public async Task AnInvalidDryRunReportsEveryProblemKindAndChangesNothing() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "mine", Name = "Daily" });
    var json = GamepackTestKit.Pack("CON", "1",
      sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "ghost") },
      images: new[] { GamepackTestKit.Img("bad", contentType: "text/plain") });
    var before = _kit.Snapshot();

    var dry = await _kit.Service.InstallAsync(json, dryRun: true);
    var real = await _kit.Service.InstallAsync(json, dryRun: false);

    dry.Status.Should().Be(GamepackInstallStatus.Invalid);
    dry.Problems.Select(p => p.Category).Should().Contain(new[] { "structure", "reference", "conflict" });
    real.Problems.Select(p => (p.Category, p.ObjectType, p.Name, p.Message)).Should().Equal(dry.Problems.Select(p => (p.Category, p.ObjectType, p.Name, p.Message)));
    _kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task ImageAndNameRulesAreCheckedInADryRun() {
    var big = Convert.ToBase64String(new byte[10_000_001]);
    var dry = await _kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1", images: new[] { GamepackTestKit.Img("big", big) }), dryRun: true);
    dry.Status.Should().Be(GamepackInstallStatus.Invalid);
    dry.Problems.Should().ContainSingle(p => p.Category == "structure" && p.ObjectType == "image");
  }
}

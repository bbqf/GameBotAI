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

/// <summary>Feature 143 (T038): the rollback runs the conflict and reference check first (FR-018, FR-016).</summary>
public sealed class GamepackRollbackConflictTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private async Task InstallAsync(string name, string json) {
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, name + ": " + string.Join("; ", outcome.Problems.Select(p => p.Message)));
  }

  private static string V1() => GamepackTestKit.Pack("pns", "1",
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily"), GamepackTestKit.Seq("s2", "Old") });

  private static string V2() => GamepackTestKit.Pack("pns", "2",
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily") });

  [Fact]
  public async Task AHandMadeObjectWithAPreviousVersionNameRefusesTheRollbackAndChangesNothing() {
    await InstallAsync("v1", V1());
    await InstallAsync("v2", V2());
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "hand", Name = "Old", Version = 5 });
    var before = _kit.Snapshot();

    var outcome = await _kit.Service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.Refused);
    outcome.Problems.Should().ContainSingle(p => p.Category == "conflict" && p.Owner == "user" && p.Name == "Old");
    _kit.Snapshot().Should().BeEquivalentTo(before);
    (await _kit.Sequences.GetAsync("hand"))!.Version.Should().Be(5);
  }

  [Fact]
  public async Task APreviousVersionObjectNowOwnedByAnotherGamepackRefusesTheRollback() {
    await InstallAsync("v1", V1());
    await InstallAsync("v2", V2());
    await InstallAsync("other", GamepackTestKit.Pack("other", "1", sequences: new[] { GamepackTestKit.Seq("o", "Old") }));
    var before = _kit.Snapshot();

    var outcome = await _kit.Service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.Refused);
    outcome.Problems.Should().ContainSingle(p => p.Category == "conflict" && p.Owner == "other 1");
    _kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task ABrokenReferenceInThePreviousContentRefusesTheRollback() {
    var v1 = GamepackTestKit.Pack("pns", "1",
      sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Tap", imageId: "icon") },
      images: new[] { GamepackTestKit.Img("icon") });
    await InstallAsync("v1", v1);
    await InstallAsync("v2", GamepackTestKit.Pack("pns", "2"));
    // Corrupt the kept previous content so that the command it needs is missing.
    var text = _kit.Store.ReadPrevious("pns")!;
    var parsed = GamepackFileSerializer.Parse(text);
    var broken = new GamepackFile { Manifest = parsed.File!.Manifest, Sequences = parsed.File.Sequences, Images = parsed.File.Images };
    _kit.Store.WritePrevious("pns", GamepackFileSerializer.Serialize(broken));
    var before = _kit.Snapshot();

    var outcome = await _kit.Service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.Refused);
    outcome.Problems.Should().Contain(p => p.Category == "reference");
    _kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task AnOwnedNameThatTheCurrentVersionHoldsIsNotAConflict() {
    await InstallAsync("v1", V1());
    await InstallAsync("v2", V2());
    var outcome = await _kit.Service.RollbackAsync("pns");
    outcome.Status.Should().Be(GamepackRollbackStatus.RolledBack);
    (await _kit.SequenceByNameAsync("Old")).Should().NotBeNull();
  }
}

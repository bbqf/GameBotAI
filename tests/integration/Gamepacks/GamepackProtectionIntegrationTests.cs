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

/// <summary>Feature 143 (T026): user objects and objects of other gamepacks stay safe (US2, FR-016).</summary>
public sealed class GamepackProtectionIntegrationTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  [Fact]
  public async Task ARefusedInstallChangesNothingAndLeavesAHandMadeObjectUnchanged() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "mine", Name = "Help All", Version = 7 });
    var before = _kit.Snapshot();

    var outcome = await _kit.Service.InstallAsync(
      GamepackTestKit.Pack("p", "1",
        sequences: new[] { GamepackTestKit.Seq("a", "Help All") },
        commands: new[] { GamepackTestKit.Cmd("c", "New command") }),
      dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.Problems.Should().ContainSingle(p => p.Category == "conflict" && p.Owner == "user");
    _kit.Snapshot().Should().BeEquivalentTo(before);
    (await _kit.Sequences.GetAsync("mine"))!.Version.Should().Be(7);
  }

  [Fact]
  public async Task AnObjectOfAnotherGamepackIsNotOverwritten() {
    (await _kit.Service.InstallAsync(GamepackTestKit.Pack("alpha", "1", sequences: new[] { GamepackTestKit.Seq("a", "Shared") }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    var before = _kit.Snapshot();

    var outcome = await _kit.Service.InstallAsync(GamepackTestKit.Pack("beta", "1", sequences: new[] { GamepackTestKit.Seq("b", "Shared") }), false);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.Problems.Single().Owner.Should().Be("alpha 1");
    _kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task AnUpdateOfOneGamepackNeverRemovesObjectsOfAnother() {
    await _kit.Service.InstallAsync(GamepackTestKit.Pack("alpha", "1", sequences: new[] { GamepackTestKit.Seq("a", "A") }), false);
    await _kit.Service.InstallAsync(GamepackTestKit.Pack("beta", "1", sequences: new[] { GamepackTestKit.Seq("b", "B") }), false);

    (await _kit.Service.InstallAsync(GamepackTestKit.Pack("alpha", "2"), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    (await _kit.SequenceByNameAsync("A")).Should().BeNull();
    (await _kit.SequenceByNameAsync("B")).Should().NotBeNull();
  }

  [Fact]
  public async Task AHandDeleteRemovesTheOwnerEntryThenAHandMadeObjectWithTheNameIsAUserObject() {
    await _kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a", "Help All") }), false);
    var id = (await _kit.SequenceByNameAsync("Help All"))!.Id;

    // The delete route does these two steps (FR-015).
    (await _kit.Sequences.DeleteAsync(id)).Should().BeTrue();
    _kit.Service.RemoveOwner(GamepackObjectTypes.Sequence, id);
    _kit.Store.Load().Owners.Should().BeEmpty();

    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "hand", Name = "Help All" });
    _kit.OwnerOf("sequence", "hand").IsUser.Should().BeTrue();
    (await _kit.Service.IsNameOwnedAsync(GamepackObjectTypes.Sequence, "Help All")).Should().BeFalse();

    var again = await _kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a", "Help All") }), false);
    again.Status.Should().Be(GamepackInstallStatus.Invalid);
    again.Problems.Should().ContainSingle(p => p.Owner == "user");
  }

  [Fact]
  public async Task AnOwnerEntryWithoutAnObjectIsNotShownAsOwnerOfANewObject() {
    await _kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a", "Help All") }), false);
    var id = (await _kit.SequenceByNameAsync("Help All"))!.Id;
    // Delete the file without the hook: the owner entry stays, and a new object with another id is a user object.
    await _kit.Sequences.DeleteAsync(id);
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "other", Name = "Help All" });

    _kit.OwnerOf("sequence", "other").IsUser.Should().BeTrue();
    (await _kit.Service.IsNameOwnedAsync(GamepackObjectTypes.Sequence, "Help All")).Should().BeFalse();
  }

  [Fact]
  public async Task AnOwnedNameIsReportedForTheHandCreateGuard() {
    await _kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1",
      sequences: new[] { GamepackTestKit.Seq("a", "Seq") },
      commands: new[] { GamepackTestKit.Cmd("c", "Cmd") },
      images: new[] { GamepackTestKit.Img("img") }), false);

    (await _kit.Service.IsNameOwnedAsync(GamepackObjectTypes.Sequence, "SEQ")).Should().BeTrue();
    (await _kit.Service.IsNameOwnedAsync(GamepackObjectTypes.Command, "cmd")).Should().BeTrue();
    (await _kit.Service.IsNameOwnedAsync(GamepackObjectTypes.Image, "IMG")).Should().BeTrue();
    (await _kit.Service.IsNameOwnedAsync(GamepackObjectTypes.Sequence, "Other")).Should().BeFalse();
  }
}

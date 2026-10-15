using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 143 (T011): the apply path.</summary>
public sealed class GamepackApplierTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private async Task<GamepackInstallOutcome> InstallAsync(string json) {
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)) + outcome.ErrorMessage);
    return outcome;
  }

  private static string Pack1() => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "img") },
    images: new[] { GamepackTestKit.Img("img") });

  [Fact]
  public async Task CreateWritesAllObjectsAndOwnerRecords() {
    var outcome = await InstallAsync(Pack1());
    outcome.Created.Should().Be(3);
    outcome.Updated.Should().Be(0);

    var seq = await _kit.SequenceByNameAsync("Seq");
    var cmd = await _kit.CommandByNameAsync("Cmd");
    seq.Should().NotBeNull();
    cmd.Should().NotBeNull();
    seq!.Steps.Single().CommandId.Should().Be(cmd!.Id);
    (await _kit.Images.ExistsAsync("img")).Should().BeTrue();
    _kit.OwnerOf("sequence", seq.Id).ToText().Should().Be("p 1");
    _kit.OwnerOf("command", cmd.Id).ToText().Should().Be("p 1");
    _kit.OwnerOf("image", "img").ToText().Should().Be("p 1");
  }

  [Fact]
  public async Task UpdateKeepsIdsAndRewritesReferencesPerFr020() {
    await InstallAsync(Pack1());
    var seqBefore = await _kit.SequenceByNameAsync("Seq");
    var cmdBefore = await _kit.CommandByNameAsync("Cmd");

    // Version 2 uses other file ids for the same names. The stored ids must stay.
    var v2 = GamepackTestKit.Pack("p", "2",
      sequences: new[] { GamepackTestKit.Seq("other-s", "Seq", "other-c") },
      commands: new[] { GamepackTestKit.Cmd("other-c", "Cmd") });
    var outcome = await InstallAsync(v2);

    outcome.Updated.Should().Be(2);
    outcome.Removed.Should().Be(1, "the image is dropped");
    var seqAfter = await _kit.SequenceByNameAsync("Seq");
    var cmdAfter = await _kit.CommandByNameAsync("Cmd");
    seqAfter!.Id.Should().Be(seqBefore!.Id);
    cmdAfter!.Id.Should().Be(cmdBefore!.Id);
    seqAfter.Steps.Single().CommandId.Should().Be(cmdBefore.Id, "the reference follows the kept id");
    _kit.OwnerOf("sequence", seqAfter.Id).ToText().Should().Be("p 2");
  }

  [Fact]
  public async Task DroppedOwnedObjectsAreRemovedButUserObjectsStay() {
    await InstallAsync(Pack1());
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "user-seq", Name = "Mine" });

    await InstallAsync(GamepackTestKit.Pack("p", "2"));

    (await _kit.SequenceByNameAsync("Seq")).Should().BeNull();
    (await _kit.CommandByNameAsync("Cmd")).Should().BeNull();
    (await _kit.Images.ExistsAsync("img")).Should().BeFalse();
    (await _kit.Sequences.GetAsync("user-seq")).Should().NotBeNull();
    _kit.Store.Load().Owners.Should().BeEmpty();
  }

  [Fact]
  public async Task AnInstallCapturesTheLiveContentIncludingHandEditsAsThePreviousVersion() {
    await InstallAsync(Pack1());
    var cmd = (await _kit.CommandByNameAsync("Cmd"))!;
    cmd.Steps.Clear();
    cmd.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = "HAND-EDIT" } });
    await _kit.Commands.UpdateAsync(cmd);

    await InstallAsync(GamepackTestKit.Pack("p", "2"));

    var text = _kit.Store.ReadPrevious("p");
    text.Should().NotBeNull();
    text.Should().Contain("HAND-EDIT");
    _kit.Store.Load().Gamepacks["p"].Previous!.Version.Should().Be("1");
  }

  [Fact]
  public async Task AReinstallOfTheSameVersionKeepsTheOldPreviousVersion() {
    await InstallAsync(Pack1());
    await InstallAsync(GamepackTestKit.Pack("p", "2", commands: new[] { GamepackTestKit.Cmd("c1", "Cmd") }));
    var previousText = _kit.Store.ReadPrevious("p");

    await InstallAsync(GamepackTestKit.Pack("p", "2", commands: new[] { GamepackTestKit.Cmd("c1", "Cmd"), GamepackTestKit.Cmd("c2", "Cmd2") }));

    _kit.Store.ReadPrevious("p").Should().Be(previousText);
    _kit.Store.Load().Gamepacks["p"].Previous!.Version.Should().Be("1");
    _kit.Store.Load().Gamepacks["p"].Version.Should().Be("2");
  }

  [Fact]
  public async Task AnImageUpdateReplacesTheBytes() {
    await InstallAsync(GamepackTestKit.Pack("p", "1", images: new[] { GamepackTestKit.Img("img") }));
    var other = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
    await InstallAsync(GamepackTestKit.Pack("p", "2", images: new[] { GamepackTestKit.Img("img", data: other) }));

    await using var stream = (await _kit.Images.OpenReadAsync("img"))!;
    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms);
    ms.ToArray().Should().Equal(1, 2, 3, 4);
  }

  [Fact]
  public async Task AnInstallRefusedByTheCheckChangesNothing() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "u", Name = "Seq" });
    var before = _kit.Snapshot();
    var outcome = await _kit.Service.InstallAsync(Pack1(), dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    _kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task TheListShowsNameVersionChangelogAndPrevious() {
    await InstallAsync(GamepackTestKit.Pack("p", "1", changelog: "first"));
    await InstallAsync(GamepackTestKit.Pack("p", "2", changelog: "second"));
    var item = _kit.Service.List().Should().ContainSingle().Subject;
    item.Name.Should().Be("p");
    item.Version.Should().Be("2");
    item.Changelog.Should().Be("second");
    item.HasPreviousVersion.Should().BeTrue();
  }
}

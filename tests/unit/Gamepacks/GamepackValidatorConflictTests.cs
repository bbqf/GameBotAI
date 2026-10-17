using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 143 (T025): conflict checks.</summary>
public sealed class GamepackValidatorConflictTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private async Task InstallAsync(string json) =>
    (await _kit.Service.InstallAsync(json, dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);

  [Fact]
  public async Task AUserObjectWithTheSameNameIsAConflict() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "mine", Name = "Help All" });
    var result = await _kit.CheckAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("x", "help all") }));
    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("conflict");
    problem.ObjectType.Should().Be("sequence");
    problem.Name.Should().Be("help all");
    problem.Owner.Should().Be("user");
  }

  [Fact]
  public async Task AnObjectOfARemovedGamepackFollowsTheUserObjectRule() {
    await InstallAsync(GamepackTestKit.Pack("alpha", "1", commands: new[] { GamepackTestKit.Cmd("c", "Shared") }));
    (await _kit.Service.RemoveAsync("alpha")).Status.Should().Be(GamepackRemoveStatus.Removed);

    var result = await _kit.CheckAsync(GamepackTestKit.Pack("alpha", "1", commands: new[] { GamepackTestKit.Cmd("c9", "Shared") }));

    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("conflict");
    problem.Owner.Should().Be("user");
  }

  [Fact]
  public async Task AnObjectOfAnotherGamepackIsAConflictWithOwnerText() {
    await InstallAsync(GamepackTestKit.Pack("alpha", "1.2.0", commands: new[] { GamepackTestKit.Cmd("c", "Shared") }));
    var result = await _kit.CheckAsync(GamepackTestKit.Pack("beta", "1", commands: new[] { GamepackTestKit.Cmd("c9", "Shared") }));
    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("conflict");
    problem.Owner.Should().Be("alpha 1.2.0");
  }

  [Fact]
  public async Task ThreeConflictsAreAllReported() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "u1", Name = "One" });
    await _kit.Commands.AddAsync(new Command { Id = "u2", Name = "Two" });
    await _kit.Images.SaveAsync("three", new System.IO.MemoryStream(Convert.FromBase64String(GamepackTestKit.PngBase64)), "image/png", null, true);
    var json = GamepackTestKit.Pack("p", "1",
      sequences: new[] { GamepackTestKit.Seq("a", "One") },
      commands: new[] { GamepackTestKit.Cmd("b", "Two") },
      images: new[] { GamepackTestKit.Img("three") });
    var result = await _kit.CheckAsync(json);
    result.Problems.Where(p => p.Category == "conflict").Select(p => p.ObjectType).Should().BeEquivalentTo(new[] { "sequence", "command", "image" });
  }

  [Fact]
  public async Task TheSameGamepackIsAnUpdateNotAConflict() {
    await InstallAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a", "Seq") }));
    var result = await _kit.CheckAsync(GamepackTestKit.Pack("P", "2", sequences: new[] { GamepackTestKit.Seq("z", "SEQ") }));
    result.IsValid.Should().BeTrue();
    result.Plan!.Updated.Should().Be(1);
    result.Plan.Created.Should().Be(0);
  }

  [Fact]
  public async Task TheSameIdWithAnotherNameIsNotAConflict() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "shared-id", Name = "User Sequence" });
    var result = await _kit.CheckAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("shared-id", "Pack Sequence") }));
    result.IsValid.Should().BeTrue();
    result.Plan!.Writes.Single().FinalId.Should().NotBe("shared-id", "a new object never takes the id of a stored object");
  }

  [Fact]
  public async Task ConflictsAndOtherProblemsAreReportedTogether() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "u1", Name = "One" });
    var json = GamepackTestKit.Pack("p", "1",
      sequences: new[] { GamepackTestKit.Seq("a", "One", "ghost") });
    var result = await _kit.CheckAsync(json);
    result.Problems.Select(p => p.Category).Should().BeEquivalentTo(new[] { "conflict", "reference" });
  }
}

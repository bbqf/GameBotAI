using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 143 (T010): structure and reference checks.</summary>
public sealed class GamepackValidatorStructureTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  [Fact]
  public async Task EmptyListsAreValid() {
    var result = await _kit.CheckAsync(GamepackTestKit.Pack("empty", "1"));
    result.IsValid.Should().BeTrue();
    result.Plan!.Writes.Should().BeEmpty();
  }

  [Fact]
  public async Task MissingManifestIsAStructureProblem() {
    var result = await _kit.CheckAsync("{\"sequences\":[]}");
    result.Problems.Should().ContainSingle(p => p.Category == "structure" && p.ObjectType == "manifest");
  }

  [Theory]
  [InlineData("", "1")]
  [InlineData("ok", "")]
  [InlineData("ok", "   ")]
  [InlineData(".bad", "1")]
  [InlineData("CON", "1")]
  [InlineData("a/b", "1")]
  public async Task BadNameOrVersionIsAStructureProblem(string name, string version) {
    var result = await _kit.CheckAsync(GamepackTestKit.Pack(name, version));
    result.IsValid.Should().BeFalse();
    result.Problems.Should().OnlyContain(p => p.Category == "structure");
  }

  [Fact]
  public async Task DuplicateNamesWithoutCaseAreAStructureProblem() {
    var json = GamepackTestKit.Pack("p", "1",
      sequences: new[] { GamepackTestKit.Seq("a", "Same"), GamepackTestKit.Seq("b", "same") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Dup"), GamepackTestKit.Cmd("c2", "DUP") },
      images: new[] { GamepackTestKit.Img("img"), GamepackTestKit.Img("IMG") });
    var result = await _kit.CheckAsync(json);
    result.Problems.Where(p => p.Category == "structure").Select(p => p.ObjectType).Should().BeEquivalentTo(new[] { "sequence", "command", "image" });
  }

  [Fact]
  public async Task ImageRulesAreStructureProblems() {
    var big = Convert.ToBase64String(new byte[10_000_001]);
    var json = GamepackTestKit.Pack("p", "1", images: new[] {
      GamepackTestKit.Img("bad-type", contentType: "text/plain"),
      GamepackTestKit.Img("bad-b64", data: "!!!not base64!!!"),
      GamepackTestKit.Img("too-big", data: big),
      GamepackTestKit.Img("empty", data: ""),
      GamepackTestKit.Img("bad id!")
    });
    var result = await _kit.CheckAsync(json);
    result.Problems.Should().HaveCount(5);
    result.Problems.Should().OnlyContain(p => p.Category == "structure" && p.ObjectType == "image");
  }

  [Fact]
  public async Task ImageOfExactlyTheLimitIsValid() {
    var data = Convert.ToBase64String(new byte[10_000_000]);
    var result = await _kit.CheckAsync(GamepackTestKit.Pack("p", "1", images: new[] { GamepackTestKit.Img("max", data: data) }));
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task MissingCommandAndImageReferencesAreReported() {
    var json = GamepackTestKit.Pack("p", "1",
      sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "missing-cmd") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "missing-img", callsCommandId: "missing-cmd2") });
    var result = await _kit.CheckAsync(json);
    result.Problems.Should().OnlyContain(p => p.Category == "reference");
    result.Problems.Should().HaveCount(3);
  }

  [Fact]
  public async Task ReferencesInsideTheFileResolve() {
    var json = GamepackTestKit.Pack("p", "1",
      sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "img") },
      images: new[] { GamepackTestKit.Img("img") });
    (await _kit.CheckAsync(json)).IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task ReferencesToAnExistingCommandResolve() {
    var existing = await _kit.Commands.AddAsync(new GameBot.Domain.Commands.Command { Id = "user-cmd", Name = "User" });
    var json = GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("s1", "Seq", existing.Id) });
    (await _kit.CheckAsync(json)).IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task AReferenceToAnObjectThatTheInstallRemovesIsABrokenReference() {
    // Version 1 owns command "Old". Version 2 drops it, and a user-made sequence is not checked (FR-015).
    // But a sequence of version 2 that still names the dropped command id is broken.
    (await _kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1", commands: new[] { GamepackTestKit.Cmd("old", "Old") }), dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);
    var result = await _kit.CheckAsync(GamepackTestKit.Pack("p", "2", sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "old") }));
    result.Problems.Should().ContainSingle(p => p.Category == "reference");
  }

  [Fact]
  public async Task AnEntryWithTheWrongShapeIsAStructureProblemNotACrash() {
    var json = "{\"manifest\":{\"name\":\"p\",\"version\":\"1\"},\"sequences\":[{\"id\":5}],\"commands\":\"x\"}";
    var result = await _kit.CheckAsync(json);
    result.Problems.Should().NotBeEmpty();
    result.Problems.Should().OnlyContain(p => p.Category == "structure");
  }

  [Fact]
  public void InvalidJsonIsReported() {
    var parsed = GamepackFileSerializer.Parse("{ not json");
    parsed.InvalidJson.Should().BeTrue();
    parsed.Problems.Should().ContainSingle();
    JsonSerializer.Serialize(parsed.Problems).Should().NotBeNullOrEmpty();
  }
}

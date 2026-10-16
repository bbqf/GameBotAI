using System;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T005): the comparer decides "identical" and gives the content hash.</summary>
public sealed class GamepackContentComparerTests {
  private static GamepackFile Parse(object[]? sequences = null, object[]? commands = null) =>
    GamepackFileSerializer.Parse(GamepackTestKit.Pack("p", "1", sequences: sequences, commands: commands)).File!;

  private static CommandSequence Sequence(string id, string name, string commandId) =>
    Parse(sequences: new[] { GamepackTestKit.Seq(id, name, commandId) }).Sequences[0];

  private static Command CommandOf(string id, string name, string? imageId = null) =>
    Parse(commands: new[] { GamepackTestKit.Cmd(id, name, imageId) }).Commands[0];

  [Fact]
  public void TwoSequencesWithTheSameContentAreIdentical() {
    GamepackContentComparer.Identical(Sequence("s1", "Daily", "c1"), Sequence("s1", "Daily", "c1")).Should().BeTrue();
  }

  [Fact]
  public void ASequenceWithAnotherCommandReferenceIsDifferent() {
    GamepackContentComparer.Identical(Sequence("s1", "Daily", "c1"), Sequence("s1", "Daily", "c2")).Should().BeFalse();
  }

  [Fact]
  public void ASequenceWithAnotherIdIsDifferent() {
    GamepackContentComparer.Identical(Sequence("s1", "Daily", "c1"), Sequence("s2", "Daily", "c1")).Should().BeFalse();
  }

  [Fact]
  public void TheCreateAndUpdateTimesOfASequenceAreIgnored() {
    var left = Sequence("s1", "Daily", "c1");
    var right = Sequence("s1", "Daily", "c1");
    left.CreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    right.UpdatedAt = new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero);

    GamepackContentComparer.Identical(left, right).Should().BeTrue();
    GamepackContentComparer.Hash(left).Should().Be(GamepackContentComparer.Hash(right));
  }

  [Fact]
  public void TwoCommandsWithTheSameContentAreIdenticalAndOtherContentIsDifferent() {
    GamepackContentComparer.Identical(CommandOf("c1", "Tap", "icon"), CommandOf("c1", "Tap", "icon")).Should().BeTrue();
    GamepackContentComparer.Identical(CommandOf("c1", "Tap", "icon"), CommandOf("c1", "Tap", "other")).Should().BeFalse();
    GamepackContentComparer.Hash(CommandOf("c1", "Tap", "icon")).Should().NotBe(GamepackContentComparer.Hash(CommandOf("c1", "Tap", "other")));
  }

  [Fact]
  public void AnImageIsIdenticalWhenTheTypeAndTheBytesAreEqual() {
    var bytes = new byte[] { 1, 2, 3, 4 };

    GamepackContentComparer.ImagesIdentical("image/png", bytes, "image/png", new byte[] { 1, 2, 3, 4 }).Should().BeTrue();
    GamepackContentComparer.ImagesIdentical("image/png", bytes, "image/jpeg", new byte[] { 1, 2, 3, 4 }).Should().BeFalse();
    GamepackContentComparer.ImagesIdentical("image/png", bytes, "image/png", new byte[] { 1, 2, 3, 5 }).Should().BeFalse();
  }

  [Fact]
  public void TheImageHashDependsOnTheTypeAndTheBytesOnly() {
    var bytes = new byte[] { 9, 8, 7 };

    GamepackContentComparer.HashImage("image/png", bytes).Should().Be(GamepackContentComparer.HashImage("IMAGE/PNG", bytes));
    GamepackContentComparer.HashImage("image/png", bytes).Should().NotBe(GamepackContentComparer.HashImage("image/png", new byte[] { 9, 8, 6 }));
    GamepackContentComparer.HashImage("image/png", bytes).Should().MatchRegex("^[0-9a-f]{64}$");
  }
}

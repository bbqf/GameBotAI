using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA1707, CA2007

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 154: the member order of a file does not change its content hash.</summary>
public sealed class GamepackMemberOrderHashTests {
  [Fact]
  public void FilesThatDifferOnlyInMemberOrderGiveAnEqualHash() {
    var first = M.PackWithCondition(M.ConditionFirst);
    var last = M.PackWithCondition(M.ConditionLast);

    first.Should().NotBe(last);
    var a = GamepackContentHasher.Compute(first)!;
    var b = GamepackContentHasher.Compute(last)!;
    a.ManifestSha256.Should().Be(b.ManifestSha256);
    a.Files.Should().BeEquivalentTo(b.Files);
  }
}

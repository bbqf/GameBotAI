using System.Security.Cryptography;
using FluentAssertions;
using GameBot.Domain.Updates;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class ChecksumVerifierTests {
  private static readonly byte[] Content = { 1, 2, 3, 4, 5 };

  private static string ExpectedHex() => Convert.ToHexStringLower(SHA256.HashData(Content));

  [Fact]
  public async Task ComputeGivesLowerCaseHex() {
    using var stream = new MemoryStream(Content);
    var hex = await ChecksumVerifier.ComputeSha256HexAsync(stream).ConfigureAwait(true);
    hex.Should().Be(ExpectedHex());
    hex.Should().HaveLength(64);
  }

  [Fact]
  public async Task MatchIgnoresCase() {
    using var stream = new MemoryStream(Content);
    var matches = await ChecksumVerifier.MatchesAsync(stream, ExpectedHex().ToUpperInvariant()).ConfigureAwait(true);
    matches.Should().BeTrue();
  }

  [Fact]
  public async Task WrongHashDoesNotMatch() {
    using var stream = new MemoryStream(Content);
    var matches = await ChecksumVerifier.MatchesAsync(stream, new string('a', 64)).ConfigureAwait(true);
    matches.Should().BeFalse();
  }

  [Theory]
  [InlineData("")]
  [InlineData("abc")]
  [InlineData("zzzz0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a")]
  public async Task BadHashLengthOrCharactersNeverMatch(string expected) {
    using var stream = new MemoryStream(Content);
    var matches = await ChecksumVerifier.MatchesAsync(stream, expected).ConfigureAwait(true);
    matches.Should().BeFalse();
  }

  [Fact]
  public void ValidHexNeedsExactly64HexCharacters() {
    ChecksumVerifier.IsValidSha256Hex(new string('A', 64)).Should().BeTrue();
    ChecksumVerifier.IsValidSha256Hex(new string('a', 63)).Should().BeFalse();
    ChecksumVerifier.IsValidSha256Hex(new string('a', 65)).Should().BeFalse();
    ChecksumVerifier.IsValidSha256Hex(null).Should().BeFalse();
  }
}

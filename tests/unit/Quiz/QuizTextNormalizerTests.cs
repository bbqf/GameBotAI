using FluentAssertions;
using GameBot.Domain.Quiz;
using Xunit;

#pragma warning disable CA5394, CA2007, CA1861

namespace GameBot.UnitTests.Quiz;

/// <summary>Feature 142: the text normalizer and the similarity score.</summary>
public sealed class QuizTextNormalizerTests {
  [Theory]
  [InlineData("Which Planet Is Red?", "which planet is red")]
  [InlineData("  Which   planet\tis\nred ?? ", "which planet is red")]
  [InlineData("Mars!", "mars")]
  [InlineData("A-B_C", "a b c")]
  [InlineData("２０２６", "2026")]
  [InlineData("ﬁsh", "fish")]
  [InlineData("Ünï", "ünï")]
  public void NormalizeGivesLowerCaseWordsWithOneSpace(string input, string expected) {
    QuizTextNormalizer.Normalize(input).Should().Be(expected);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("?!...")]
  public void NormalizeGivesEmptyTextWhenNoLetterOrDigitExists(string? input) {
    QuizTextNormalizer.Normalize(input).Should().BeEmpty();
  }

  [Fact]
  public void NormalizeKeepsTextWithAnInvalidCodePoint() {
    var result = QuizTextNormalizer.Normalize("ab\uD800cd");

    result.Should().Contain("ab").And.Contain("cd");
  }

  [Fact]
  public void NormalizeIsStableWhenRunTwice() {
    var once = QuizTextNormalizer.Normalize("  Hello,  World! ");

    QuizTextNormalizer.Normalize(once).Should().Be(once);
  }

  [Fact]
  public void SimilarityIsOneForEqualText() {
    QuizSimilarity.Score("mars", "mars").Should().Be(1);
  }

  [Theory]
  [InlineData(null, "mars")]
  [InlineData("mars", null)]
  [InlineData("", "mars")]
  [InlineData("", "")]
  public void SimilarityIsZeroWhenATextIsEmpty(string? left, string? right) {
    QuizSimilarity.Score(left, right).Should().Be(0);
  }

  [Fact]
  public void SimilarityDropsWithEachEdit() {
    // One edit in 10 characters.
    QuizSimilarity.Score("jupiterxxx", "jupiterxxy").Should().BeApproximately(0.9, 1e-9);
    // Completely different text of the same length.
    QuizSimilarity.Score("abcd", "wxyz").Should().Be(0);
  }

  [Fact]
  public void SimilarityUsesTheLongerLength() {
    QuizSimilarity.Score("mars", "marsh").Should().BeApproximately(0.8, 1e-9);
  }

  [Theory]
  [InlineData("which planet is red", "which planet is red", 0.85)]
  [InlineData("which planet is red", "whlch planet is red", 0.85)]
  [InlineData("which planet is red", "which planet is blue", 0.85)]
  [InlineData("which planet is red", "which planet is blue", 0.5)]
  [InlineData("abcdefghij", "abcdefghijklmnop", 0.5)]
  [InlineData("abcdefghij", "abcdefghijklmnop", 0.7)]
  [InlineData("a", "b", 0.0)]
  [InlineData("a", "b", 1.0)]
  [InlineData("kitten", "sitting", 0.5)]
  [InlineData("kitten", "sitting", 0.6)]
  [InlineData("kitten", "sitting", 0.7)]
  [InlineData("completely different", "nothing alike here", 0.2)]
  [InlineData("completely different", "nothing alike here", 0.85)]
  [InlineData("short", "a much much longer text than the other", 0.1)]
  public void TheBoundedScoreEqualsTheFullScoreOrIsZeroBelowTheLimit(string left, string right, double limit) {
    var full = QuizSimilarity.Score(left, right);
    var bounded = QuizSimilarity.Score(left, right, limit);

    // A score that is lower than the limit only by a rounding error counts as the limit.
    if (full >= limit - 1e-9) {
      bounded.Should().BeApproximately(full, 1e-9);
    }
    else {
      bounded.Should().Be(0);
    }
  }

  [Fact]
  public void TheBoundedScoreAgreesWithTheFullScoreOnManyRandomPairs() {
    var random = new Random(142);
    string Word() => new(Enumerable.Range(0, random.Next(1, 14)).Select(_ => (char)('a' + random.Next(0, 4))).ToArray());

    for (var i = 0; i < 400; i++) {
      var left = Word();
      var right = Word();
      var limit = random.Next(0, 11) / 10.0;
      var full = QuizSimilarity.Score(left, right);
      var bounded = QuizSimilarity.Score(left, right, limit);

      (bounded >= limit).Should().Be(full >= limit - 1e-9, $"'{left}' vs '{right}' at {limit}");
      if (bounded > 0 && full >= limit) bounded.Should().BeApproximately(full, 1e-9);
    }
  }

  [Fact]
  public void SimilarityStaysInRange() {
    QuizSimilarity.Score("a", "bcdefghij").Should().BeInRange(0, 1);
  }
}

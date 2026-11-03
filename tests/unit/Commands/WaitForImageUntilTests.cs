using FluentAssertions;
using GameBot.Domain.Commands.Execution;
using Xunit;

namespace GameBot.UnitTests.Commands;

public sealed class WaitForImageUntilTests {
  [Theory]
  [InlineData("present", false)]
  [InlineData("absent", true)]
  [InlineData("ABSENT", true)]
  [InlineData("Present", false)]
  public void TryParseAcceptsKnownValuesInAnyCase(string value, bool expectedAbsent) {
    WaitForImageUntil.TryParse(value, out var isAbsent).Should().BeTrue();
    isAbsent.Should().Be(expectedAbsent);
  }

  [Fact]
  public void TryParseAcceptsNullAsPresent() {
    WaitForImageUntil.TryParse(null, out var isAbsent).Should().BeTrue();
    isAbsent.Should().BeFalse();
  }

  [Theory]
  [InlineData("gone")]
  [InlineData("")]
  [InlineData("  ")]
  public void TryParseRejectsOtherValues(string value) {
    WaitForImageUntil.TryParse(value, out var isAbsent).Should().BeFalse();
    isAbsent.Should().BeFalse();
  }

  [Fact]
  public void DefaultAbsentCapturesIsTwo() {
    WaitForImageUntil.DefaultAbsentCaptures.Should().Be(2);
  }
}

using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Commands;

/// <summary>Feature 192: the permitted characters and the maximum length of a text input.</summary>
public sealed class TextInputRulesTests {
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  public void AnEmptyTextIsEmpty(string? text) {
    TextInputRules.Check(text).Should().Be(TextInputCheck.Empty);
  }

  [Theory]
  [InlineData("2")]
  [InlineData("250")]
  [InlineData("   ")]
  [InlineData(" ")]
  [InlineData("~")]
  [InlineData("250 Fighters & it's ok")]
  public void APrintableAsciiTextIsOk(string text) {
    TextInputRules.Check(text).Should().Be(TextInputCheck.Ok);
  }

  [Fact]
  public void FiveHundredCharactersAreOk() {
    TextInputRules.Check(new string('a', 500)).Should().Be(TextInputCheck.Ok);
  }

  [Fact]
  public void FiveHundredAndOneCharactersAreTooLong() {
    TextInputRules.Check(new string('a', 501)).Should().Be(TextInputCheck.TooLong);
  }

  [Theory]
  [InlineData("\u001F")]
  [InlineData("\u007F")]
  [InlineData("a\tb")]
  [InlineData("a\nb")]
  [InlineData("é")]
  public void ACharacterOutsideTheRangeIsNotPermitted(string text) {
    TextInputRules.Check(text).Should().Be(TextInputCheck.UnsupportedCharacter);
  }

  [Fact]
  public void TheLengthIsCheckedBeforeTheCharacters() {
    TextInputRules.Check(new string('é', 501)).Should().Be(TextInputCheck.TooLong);
  }

  [Theory]
  [InlineData(TextInputCheck.Ok, null)]
  [InlineData(TextInputCheck.Empty, "text_input_empty_text")]
  [InlineData(TextInputCheck.TooLong, "text_input_too_long")]
  [InlineData(TextInputCheck.UnsupportedCharacter, "text_input_unsupported_character")]
  public void EachCheckHasItsReasonCode(TextInputCheck check, string? reason) {
    TextInputRules.ReasonCode(check).Should().Be(reason);
  }
}

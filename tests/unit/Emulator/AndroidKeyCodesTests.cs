using FluentAssertions;
using GameBot.Emulator.Session;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>Feature 164: the rules that turn the key text of a key input into an Android key code.</summary>
public sealed class AndroidKeyCodesTests {
  private static readonly Dictionary<string, int> OldNames = new() {
    ["HOME"] = 3, ["BACK"] = 4, ["ESCAPE"] = 111, ["MENU"] = 82, ["ENTER"] = 66, ["SPACE"] = 62,
    ["TAB"] = 61, ["DEL"] = 67, ["DELETE"] = 67, ["UP"] = 19, ["DOWN"] = 20, ["LEFT"] = 21,
    ["RIGHT"] = 22, ["VOLUME_UP"] = 24, ["VOLUME_DOWN"] = 25, ["POWER"] = 26
  };

  [Fact]
  public void EveryOldNameKeepsItsKeyCode() {
    foreach (var (name, expected) in OldNames) {
      AndroidKeyCodes.TryResolve(name, out var code).Should().Be(KeyResolveResult.Ok, name);
      code.Should().Be(expected, name);
    }
  }

  [Fact]
  public void LettersAToZKeepTheirKeyCodes() {
    for (var i = 0; i < 26; i++) {
      var name = ((char)('A' + i)).ToString();
      AndroidKeyCodes.TryResolve(name, out var code).Should().Be(KeyResolveResult.Ok, name);
      code.Should().Be(29 + i, name);
    }
  }

  [Theory]
  [InlineData("enter", 66)]
  [InlineData("  Enter  ", 66)]
  [InlineData("volume_up", 24)]
  [InlineData("\ta\n", 29)]
  public void NameMatchIgnoresCaseAndSpacesAtStartAndEnd(string text, int expected) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.Ok);
    code.Should().Be(expected);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void EmptyTextGivesEmpty(string? text) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.Empty);
    code.Should().Be(0);
  }

  [Theory]
  [InlineData("F5")]
  [InlineData("NOPE")]
  [InlineData("ENTER ENTER")]
  public void UnknownNameGivesUnknownName(string text) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.UnknownName);
    code.Should().Be(0);
  }

  [Fact]
  public void DigitNamesZeroToNineGiveKeyCodesSevenToSixteen() {
    for (var digit = 0; digit <= 9; digit++) {
      var name = digit.ToString(System.Globalization.CultureInfo.InvariantCulture);
      AndroidKeyCodes.TryResolve(name, out var code).Should().Be(KeyResolveResult.Ok, name);
      code.Should().Be(7 + digit, name);
    }
  }

  [Fact]
  public void NumberPadNamesGiveKeyCodes144To153() {
    for (var digit = 0; digit <= 9; digit++) {
      var name = "NUMPAD_" + digit.ToString(System.Globalization.CultureInfo.InvariantCulture);
      AndroidKeyCodes.TryResolve(name, out var code).Should().Be(KeyResolveResult.Ok, name);
      code.Should().Be(144 + digit, name);
    }
  }

  [Theory]
  [InlineData("numpad_6", 150)]
  [InlineData(" 6 ", 13)]
  [InlineData("Numpad_0", 144)]
  public void DigitAndNumberPadNamesIgnoreCaseAndSpaces(string text, int expected) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.Ok);
    code.Should().Be(expected);
  }

  [Theory]
  [InlineData("85", 85)]
  [InlineData(" 85 ", 85)]
  [InlineData("300", 300)]
  [InlineData("10", 10)]
  [InlineData("0085", 85)]
  public void AWholeNumberFromZeroTo300IsARawKeyCode(string text, int expected) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.Ok);
    code.Should().Be(expected);
  }

  [Theory]
  [InlineData("6", 13)]
  [InlineData("0", 7)]
  [InlineData("9", 16)]
  public void ADigitNameHasPriorityOverARawKeyCode(string text, int expected) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.Ok);
    code.Should().Be(expected, "a name in the key list wins over the number with the same text");
  }

  [Theory]
  [InlineData("301")]
  [InlineData("1000")]
  [InlineData("2147483647")]
  [InlineData("99999999999")]
  [InlineData("99999999999999999999999999")]
  public void AWholeNumberAbove300OrTooLargeToReadIsOutOfRange(string text) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.OutOfRange);
    code.Should().Be(0);
  }

  [Theory]
  [InlineData("-1")]
  [InlineData("+5")]
  [InlineData("6.5")]
  [InlineData("6a")]
  [InlineData("1 2")]
  [InlineData("0x55")]
  [InlineData("٨٥")] // Arabic-Indic digits are not ASCII digits
  public void TextThatIsNotAWholeNumberOfAsciiDigitsIsAnUnknownName(string text) {
    AndroidKeyCodes.TryResolve(text, out var code).Should().Be(KeyResolveResult.UnknownName);
    code.Should().Be(0);
  }
}

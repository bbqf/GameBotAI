using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 143, FR-019: the gamepack name rules.</summary>
public sealed class GamepackNameRulesTests {
  [Theory]
  [InlineData("pns-daily")]
  [InlineData("A")]
  [InlineData("pack_1.2")]
  [InlineData("a.b.c")]
  [InlineData("COM10")]
  [InlineData("LPT0")]
  [InlineData("console")]
  public void ValidNamesPass(string name) {
    GamepackNameRules.Validate(name).Should().BeNull();
    GamepackNameRules.IsValid(name).Should().BeTrue();
  }

  [Fact]
  public void SixtyFourCharactersPassAndSixtyFiveFail() {
    GamepackNameRules.IsValid(new string('a', 64)).Should().BeTrue();
    GamepackNameRules.IsValid(new string('a', 65)).Should().BeFalse();
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  [InlineData(".hidden")]
  [InlineData("a/b")]
  [InlineData("a\\b")]
  [InlineData("a b")]
  [InlineData("a:b")]
  [InlineData("naïve")]
  public void BadNamesFail(string? name) {
    GamepackNameRules.Validate(name).Should().NotBeNull();
  }

  [Theory]
  [InlineData("CON")]
  [InlineData("Con")]
  [InlineData("con.txt")]
  [InlineData("PRN")]
  [InlineData("AUX")]
  [InlineData("nul.txt")]
  [InlineData("COM1")]
  [InlineData("COM9")]
  [InlineData("LPT1.log")]
  [InlineData("lpt9")]
  public void ReservedDeviceNamesFail(string name) {
    GamepackNameRules.IsReserved(name).Should().BeTrue();
    GamepackNameRules.Validate(name).Should().Contain("reserved");
  }

  [Fact]
  public void NamesThatDifferOnlyInCaseAreEqual() {
    GamepackNameRules.Comparer.Equals("Pns-Daily", "pns-daily").Should().BeTrue();
    GamepackNameRules.Key("Pns-Daily").Should().Be(GamepackNameRules.Key("PNS-DAILY"));
  }
}

using FluentAssertions;
using GameBot.Domain.Config;
using Xunit;

namespace GameBot.UnitTests.Config;

/// <summary>Feature 195: the clamp of the character pause setting of the TextInput step.</summary>
public sealed class AppConfigTextInputDelayTests {
  [Theory]
  [InlineData(null, 100)]
  [InlineData(-5, 100)]
  [InlineData(0, 0)]
  [InlineData(150, 150)]
  [InlineData(2000, 2000)]
  [InlineData(5000, 2000)]
  public void NormalizeTextInputCharDelayMsGivesAValueInTheRange(int? value, int expected) {
    AppConfig.NormalizeTextInputCharDelayMs(value).Should().Be(expected);
  }

  [Fact]
  public void ANewAppConfigHasTheDefaultPauseOf100() {
    new AppConfig().TextInputCharDelayMs.Should().Be(100);
  }
}

#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 168: checks of a binding value against its declaration.</summary>
public sealed class ParameterValueRulesTests {
  private static ParameterDeclaration Number(int? min = null, int? max = null) => new() {
    Name = "n", Type = ParameterValueType.Number, Min = min, Max = max
  };

  private static ParameterDeclaration Choice() => new() {
    Name = "c", Kind = ParameterInputKind.Choice,
    Options = new[] { new ParameterOption { Label = "Fast", Value = "f" }, new ParameterOption { Label = "Slow", Value = "s" } }
  };

  private static ParameterDeclaration Image() => new() {
    Name = "i", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b" }
  };

  [Fact]
  public void NullValueIsNotChecked() {
    ParameterValueRules.Check(Number(1, 2), null).Should().BeNull();
    ParameterValueRules.Check(Choice(), null).Should().BeNull();
    ParameterValueRules.Check(Image(), null).Should().BeNull();
  }

  [Fact]
  public void PlaceholderValueIsNotChecked() {
    ParameterValueRules.Check(Number(1, 2), "{{other}}").Should().BeNull();
    ParameterValueRules.Check(Choice(), "{{other}}").Should().BeNull();
    ParameterValueRules.Check(Image(), "{{other}}").Should().BeNull();
  }

  [Theory]
  [InlineData("5", null)]
  [InlineData("1", null)]
  [InlineData("9", null)]
  public void NumberInRangeIsAccepted(string value, string? expected) {
    ParameterValueRules.Check(Number(1, 9), value).Should().Be(expected);
  }

  [Theory]
  [InlineData("abc")]
  [InlineData("1.5")]
  [InlineData("0")]
  [InlineData("10")]
  public void NumberOutsideRangeOrNotWholeIsRefusedWithHint(string value) {
    var message = ParameterValueRules.Check(Number(1, 9), value);
    message.Should().NotBeNull();
    message!.Should().Contain(". ", "the message names the problem and gives a fix");
  }

  [Fact]
  public void NumberWithoutRangeAcceptsAnyWholeNumber() {
    ParameterValueRules.Check(Number(), "-5").Should().BeNull();
  }

  [Fact]
  public void ChoiceValueMustBeAnOptionValue() {
    ParameterValueRules.Check(Choice(), "f").Should().BeNull();
    var message = ParameterValueRules.Check(Choice(), "zzz");
    message.Should().NotBeNull();
    message!.Should().Contain(". ");
  }

  [Fact]
  public void ImageValueMustBeAnAllowedImage() {
    ParameterValueRules.Check(Image(), "a").Should().BeNull();
    var message = ParameterValueRules.Check(Image(), "zzz");
    message.Should().NotBeNull();
    message!.Should().Contain(". ");
  }

  [Fact]
  public void TextParameterAcceptsAnyValue() {
    ParameterValueRules.Check(new ParameterDeclaration { Name = "t" }, "anything").Should().BeNull();
  }
}

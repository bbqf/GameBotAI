#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 179: declaration rules for multi-value parameters.</summary>
public sealed class ParameterNameRulesMultipleTests {
  private static IReadOnlyList<string> Validate(ParameterDeclaration d) =>
      ParameterNameRules.ValidateDeclarations(new[] { d });

  private static ParameterDeclaration Image(
      bool multiple = true, int? min = null, int? max = null, string? def = null, params string[] images) => new() {
    Name = "quests", Kind = ParameterInputKind.Image,
    AllowedImages = images.Length == 0 ? new[] { "a", "b", "c" } : images,
    Multiple = multiple, MinItems = min, MaxItems = max, Default = def
  };

  [Fact]
  public void MultiValueImageAndChoiceAreAccepted() {
    Validate(Image(min: 1, max: 3)).Should().BeEmpty();
    Validate(new ParameterDeclaration {
      Name = "mode", Kind = ParameterInputKind.Choice, Multiple = true,
      Options = new[] { new ParameterOption { Label = "A", Value = "a" }, new ParameterOption { Label = "B", Value = "b" } }
    }).Should().BeEmpty();
  }

  [Fact]
  public void MultipleOnTextOrNumberIsRefused() {
    Validate(new ParameterDeclaration { Name = "t", Multiple = true }).Should().ContainSingle()
        .Which.Should().Contain("image or a choice");
    Validate(new ParameterDeclaration { Name = "n", Type = ParameterValueType.Number, Kind = ParameterInputKind.Number, Multiple = true })
        .Should().NotBeEmpty();
  }

  [Fact]
  public void LimitsBelowOneAreRefused() {
    Validate(Image(min: 0)).Should().Contain(e => e.Contains("below 1", System.StringComparison.Ordinal));
    Validate(Image(max: 0)).Should().Contain(e => e.Contains("below 1", System.StringComparison.Ordinal));
  }

  [Fact]
  public void MinAboveMaxIsRefused() {
    Validate(Image(min: 3, max: 2)).Should().Contain(e => e.Contains("more than the maximum", System.StringComparison.Ordinal));
  }

  [Fact]
  public void MinAboveAllowedCountIsRefused() {
    Validate(Image(min: 4)).Should().Contain(e => e.Contains("allowed items", System.StringComparison.Ordinal));
  }

  [Fact]
  public void LimitsWithoutMultipleAreRefused() {
    Validate(Image(multiple: false, min: 1)).Should().ContainSingle()
        .Which.Should().Contain("does not allow several values");
  }

  [Fact]
  public void SeparatorInAllowedItemIsRefused() {
    Validate(Image(images: new[] { "a|b", "c" })).Should().Contain(e => e.Contains("'|'", System.StringComparison.Ordinal));
    Validate(new ParameterDeclaration {
      Name = "mode", Kind = ParameterInputKind.Choice, Multiple = true,
      Options = new[] { new ParameterOption { Label = "A", Value = "a|x" } }
    }).Should().NotBeEmpty();
  }

  [Fact]
  public void SeparatorIsAllowedWhenParameterHoldsOneValue() {
    Validate(Image(multiple: false, images: new[] { "a|b", "c" })).Should().BeEmpty();
  }

  [Fact]
  public void DefaultListOfAllowedItemsIsAccepted() {
    Validate(Image(min: 1, max: 3, def: "a|b")).Should().BeEmpty();
    Validate(Image(def: "a")).Should().BeEmpty();
  }

  [Fact]
  public void DefaultWithNotAllowedItemIsRefused() {
    Validate(Image(def: "a|zzz")).Should().ContainSingle().Which.Should().Contain("'zzz'");
  }

  [Fact]
  public void DefaultOutsideLimitsIsRefused() {
    Validate(Image(max: 1, def: "a|b")).Should().ContainSingle().Which.Should().Contain("at most 1");
    Validate(Image(min: 2, def: "a")).Should().ContainSingle().Which.Should().Contain("at least 2");
  }

  [Fact]
  public void MessagesDoNotMatchTheDefaultValueCode() {
    // The service maps "default value" messages to invalid_parameter_default. List defaults use the declaration code.
    Validate(Image(def: "a|zzz")).Should().OnlyContain(e => !e.Contains("default value", System.StringComparison.Ordinal));
  }

  [Fact]
  public void OldDeclarationIsUnchanged() {
    Validate(new ParameterDeclaration { Name = "a" }).Should().BeEmpty();
    Validate(Image(multiple: false, def: "a")).Should().BeEmpty();
    Validate(Image(multiple: false, def: "zzz")).Should().ContainSingle();
  }
}

#pragma warning disable CA1861 // Test data arrays are small and used one time.
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 179: list helpers for multi-value parameters.</summary>
public sealed class ParameterListRulesTests {
  private static ParameterDeclaration Multi(int? min = null, int? max = null) => new() {
    Name = "quests", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b", "c" },
    Multiple = true, MinItems = min, MaxItems = max
  };

  [Fact]
  public void NormalizeKeepsOrderAndFirstOccurrence() {
    ParameterListRules.Normalize(new[] { "b", "a", "b", "c", "a" }).Should().Equal("b", "a", "c");
  }

  [Fact]
  public void NormalizeDropsEmptyItemsAndAcceptsNull() {
    ParameterListRules.Normalize(new[] { "a", "", "b" }).Should().Equal("a", "b");
    ParameterListRules.Normalize(null).Should().BeEmpty();
  }

  [Fact]
  public void JoinAndSplitAreInverse() {
    var text = ParameterListRules.Join(new[] { "a", "b", "c" });
    text.Should().Be("a|b|c");
    ParameterListRules.Split(text).Should().Equal("a", "b", "c");
  }

  [Fact]
  public void SplitOfEmptyOrNullTextGivesNoItems() {
    ParameterListRules.Split(null).Should().BeEmpty();
    ParameterListRules.Split(string.Empty).Should().BeEmpty();
    ParameterListRules.Count("a||b").Should().Be(2);
  }

  [Fact]
  public void ScalarValueReadsAsListOfOneForMultiValueParameter() {
    var binding = new ParameterBinding { Name = "quests", Value = "a" };
    ParameterListRules.ReadAsList(Multi(), binding).Should().Equal("a");
  }

  [Fact]
  public void ScalarValueOfSingleValueParameterIsNotAList() {
    var single = new ParameterDeclaration { Name = "x" };
    ParameterListRules.ReadAsList(single, new ParameterBinding { Name = "x", Value = "a" }).Should().BeNull();
  }

  [Fact]
  public void ValuesWinOverScalarValue() {
    var binding = new ParameterBinding { Name = "quests", Value = "a|b", Values = new[] { "a", "b" } };
    ParameterListRules.ReadAsList(Multi(), binding).Should().Equal("a", "b");
  }

  [Fact]
  public void InheritBindingReadsAsNull() {
    ParameterListRules.ReadAsList(Multi(), new ParameterBinding { Name = "quests" }).Should().BeNull();
  }

  [Fact]
  public void LimitsAreChecked() {
    ParameterListRules.CheckLimits(Multi(2, 3), 2).Should().BeNull();
    ParameterListRules.CheckLimits(Multi(2, 3), 1).Should().Contain("at least 2");
    ParameterListRules.CheckLimits(Multi(2, 3), 4).Should().Contain("at most 3");
    ParameterListRules.CheckLimits(Multi(), 50).Should().BeNull();
  }
}

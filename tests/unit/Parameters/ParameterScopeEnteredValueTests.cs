using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

#pragma warning disable CA1707

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 193: <see cref="ParameterScope.TryResolveEntered"/> gives the value before the number pattern.</summary>
public sealed class ParameterScopeEnteredValueTests {
  private static ParameterDeclaration Number(string name, string? @default = null, string? pattern = null) => new() {
    Name = name,
    Type = ParameterValueType.Number,
    Default = @default,
    Pattern = pattern
  };

  [Fact]
  public void GivesTheBoundValueThenTheDefaultThenNoValue() {
    var bound = ParameterScope.Empty.Child(ParameterScopeLayers.Sequence,
      new[] { new ParameterBinding { Name = "batch", Value = "250" } }, new[] { Number("batch", "10") });
    bound.TryResolveEntered("batch", out var value).Should().BeTrue();
    value.Text.Should().Be("250");

    var defaulted = ParameterScope.Empty.Child(ParameterScopeLayers.Sequence, null, new[] { Number("batch", "10") });
    defaulted.TryResolveEntered("batch", out var fromDefault).Should().BeTrue();
    fromDefault.Text.Should().Be("10");
    fromDefault.OriginLayer.Should().Be(ParameterScopeLayers.Default);

    var none = ParameterScope.Empty.Child(ParameterScopeLayers.Sequence, null, new[] { Number("batch") });
    none.TryResolveEntered("batch", out _).Should().BeFalse();
  }

  [Fact]
  public void DoesNotApplyThePattern() {
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Sequence,
      new[] { new ParameterBinding { Name = "batch", Value = "5" } }, new[] { Number("batch", pattern: "x{value}") });

    scope.TryResolve("batch", out var mapped).Should().BeTrue();
    mapped.Text.Should().Be("x5");
    scope.TryResolveEntered("batch", out var entered).Should().BeTrue();
    entered.Text.Should().Be("5");

    var defaulted = ParameterScope.Empty.Child(ParameterScopeLayers.Sequence, null, new[] { Number("batch", "7", "x{value}") });
    defaulted.TryResolveEntered("batch", out var fromDefault).Should().BeTrue();
    fromDefault.Text.Should().Be("7");
  }

  [Fact]
  public void AMappedBoundValueStaysAsItIs() {
    var outer = ParameterScope.Empty.Child(ParameterScopeLayers.Sequence,
      new[] { new ParameterBinding { Name = "count", Value = "3" } }, new[] { Number("count") });
    outer.TryBindChild(ParameterScopeLayers.Command,
      new[] { new ParameterBinding { Name = "label", Value = "n{{count}}" } }, out var child, out _).Should().BeTrue();

    child!.TryResolveEntered("label", out var value).Should().BeTrue();
    value.Text.Should().Be("n3");
    value.Mapped.Should().BeTrue();
  }
}

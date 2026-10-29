#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 168: the number pattern, the Mapped flag, and option values as plain text.</summary>
public sealed class ParameterScopePatternTests {
  private static readonly ParameterDeclaration Instance = new() {
    Name = "instance", Type = ParameterValueType.Number, Pattern = "emulator-{value}"
  };

  private static ParameterScope WithEntry(ParameterDeclaration declaration, string? value) =>
      ParameterScope.Empty
          .Child(ParameterScopeLayers.Sequence, null, new[] { declaration })
          .Child(ParameterScopeLayers.Entry,
              value is null ? null : new[] { new ParameterBinding { Name = declaration.Name, Value = value } }, null);

  [Fact]
  public void PatternAppliesToABindingValue() {
    WithEntry(Instance, "5").TryResolve("instance", out var value).Should().BeTrue();

    value.Text.Should().Be("emulator-5");
    value.Mapped.Should().BeTrue();
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  [Fact]
  public void PatternAppliesToADefault() {
    var declaration = new ParameterDeclaration {
      Name = "instance", Type = ParameterValueType.Number, Pattern = "emulator-{value}", Default = "7"
    };

    WithEntry(declaration, null).TryResolve("instance", out var value).Should().BeTrue();

    value.Text.Should().Be("emulator-7");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Default);
  }

  [Fact]
  public void NameWithoutPatternIsUnchanged() {
    var plain = new ParameterDeclaration { Name = "instance", Type = ParameterValueType.Number };

    WithEntry(plain, "5").TryResolve("instance", out var value).Should().BeTrue();

    value.Text.Should().Be("5");
    value.Mapped.Should().BeFalse();
  }

  [Fact]
  public void MappedValueGetsNoSecondPattern() {
    var scope = WithEntry(Instance, "5");
    scope.TryBindChild(ParameterScopeLayers.Command,
        new[] { new ParameterBinding { Name = "serial", Value = "{{instance}}" } },
        out var child, out _, new[] { new ParameterDeclaration { Name = "serial", Pattern = null } }).Should().BeTrue();

    // The callee declares a pattern for the same name. The value was mapped already, so it stays.
    var inner = child!.Child(ParameterScopeLayers.Command, null,
        new[] { new ParameterDeclaration { Name = "serial", Type = ParameterValueType.Number, Pattern = "x{value}" } });

    inner.TryResolve("serial", out var value).Should().BeTrue();
    value.Text.Should().Be("emulator-5");
  }

  [Fact]
  public void PatternParameterInsideALoopScopeStillMaps() {
    WithEntry(Instance, "3").WithIteration(2).TryResolve("instance", out var value).Should().BeTrue();

    value.Text.Should().Be("emulator-3");
  }

  [Fact]
  public void PatternWithBracesIsNotResolvedAgain() {
    var declaration = new ParameterDeclaration {
      Name = "instance", Type = ParameterValueType.Number, Pattern = "{{x}}-{value}"
    };

    WithEntry(declaration, "5").TryResolve("instance", out var value).Should().BeTrue();

    value.Text.Should().Be("{{x}}-5");
    value.Mapped.Should().BeTrue();
  }

  [Fact]
  public void SubstitutionContextAndDescribeShowTheMappedValue() {
    var scope = WithEntry(Instance, "5");

    scope.ToSubstitutionContext()["instance"].Should().Be("emulator-5");
    scope.Describe().Should().Contain(e => e.Name == "instance" && e.Value == "emulator-5");
  }

  [Fact]
  public void OptionValueWithBracesIsPlainTextInABinding() {
    var callee = new[] {
      new ParameterDeclaration {
        Name = "mode", Kind = ParameterInputKind.Choice,
        Options = new[] { new ParameterOption { Label = "Fast", Value = "go-{{unknown}}" } }
      }
    };

    ParameterScope.Empty.TryBindChild(ParameterScopeLayers.Command,
        new[] { new ParameterBinding { Name = "mode", Value = "go-{{unknown}}" } },
        out var child, out var error, callee).Should().BeTrue();

    error.Should().BeNull();
    child!.TryResolve("mode", out var value).Should().BeTrue();
    value.Text.Should().Be("go-{{unknown}}");
    value.Mapped.Should().BeTrue();
  }

  [Fact]
  public void OtherBracedBindingValueIsStillResolvedAndFailsWhenUnknown() {
    ParameterScope.Empty.TryBindChild(ParameterScopeLayers.Command,
        new[] { new ParameterBinding { Name = "mode", Value = "go-{{unknown}}" } },
        out _, out var error).Should().BeFalse();

    error.Should().NotBeNull();
  }

  [Fact]
  public void FindDeclarationReturnsTheInnermostDeclaration() {
    var scope = WithEntry(Instance, "5");

    scope.TryFindDeclaration("instance", out var declaration).Should().BeTrue();
    declaration!.Pattern.Should().Be("emulator-{value}");
    scope.TryFindDeclaration("nothing", out _).Should().BeFalse();
  }
}

using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 115 (issue #246): <see cref="ParameterScope.TryBindChild"/> resolves a placeholder in a
/// binding value against the outer scope before it makes the new layer.
/// </summary>
public sealed class ParameterScopeBindingTests {
  private static Collection<ParameterBinding> Bind(params (string Name, string? Value)[] values) {
    var bindings = new Collection<ParameterBinding>();
    foreach (var (name, value) in values) bindings.Add(new ParameterBinding { Name = name, Value = value });
    return bindings;
  }

  private static ParameterScope QueueScope() => ParameterScope.FromQueue(new ExecutionQueue {
    Id = "q", Name = "Q", EmulatorSerial = "emulator-5558"
  });

  /// <summary>The queue layer, then an entry layer with <paramref name="values"/>, then the sequence layer.</summary>
  private static ParameterScope OuterScope(
      Collection<ParameterDeclaration>? declarations = null,
      params (string Name, string? Value)[] values) =>
      QueueScope()
          .Child(ParameterScopeLayers.Entry, Bind(values), null)
          .Child(ParameterScopeLayers.Sequence, null, declarations);

  private static ParameterScope BindOrFail(ParameterScope outer, Collection<ParameterBinding> bindings) {
    outer.TryBindChild(ParameterScopeLayers.Command, bindings, out var child, out var error)
        .Should().BeTrue(error?.ToMessage("s1"));
    error.Should().BeNull();
    return child!;
  }

  [Fact]
  public void WholePlaceholderGetsTheEntryValueAndKeepsTheEntryLayer() {
    var outer = OuterScope(null, ("novaOptionImage", "pns-alliance-nav-button"));

    var child = BindOrFail(outer, Bind(("novaOptionImage", "{{novaOptionImage}}")));

    child.TryResolve("novaOptionImage", out var value).Should().BeTrue();
    value.Text.Should().Be("pns-alliance-nav-button");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
    value.Sources.Should().BeNull();
  }

  [Fact]
  public void SameNameBindingResolvesOutwardNotAgainstItself() {
    var outer = OuterScope(null, ("x", "outer-x"));

    var child = BindOrFail(outer, Bind(("x", "{{x}}")));

    child.TryResolve("x", out var value).Should().BeTrue();
    value.Text.Should().Be("outer-x");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  [Fact]
  public void BindingDoesNotSeeAnotherBindingOfTheSameStep() {
    var outer = OuterScope(null, ("b", "outer-b"));

    var child = BindOrFail(outer, Bind(("a", "{{b}}"), ("b", "other")));

    child.TryResolve("a", out var a).Should().BeTrue();
    a.Text.Should().Be("outer-b");
    a.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
    child.TryResolve("b", out var b).Should().BeTrue();
    b.Text.Should().Be("other");
    b.OriginLayer.Should().Be(ParameterScopeLayers.Command);
  }

  [Fact]
  public void PlaceholderThatResolvesToADeclaredDefaultKeepsTheDefaultLayer() {
    var declarations = new Collection<ParameterDeclaration> {
      new() { Name = "novaOptionImage", Default = "pns-todo-radar" }
    };
    var outer = OuterScope(declarations);

    var child = BindOrFail(outer, Bind(("novaOptionImage", "{{novaOptionImage}}")));

    child.TryResolve("novaOptionImage", out var value).Should().BeTrue();
    value.Text.Should().Be("pns-todo-radar");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Default);
  }

  [Fact]
  public void IterationPlaceholderGetsTheLoopValue() {
    var outer = OuterScope().WithIteration(3);

    var child = BindOrFail(outer, Bind(("n", "{{iteration}}")));

    child.TryResolve("n", out var value).Should().BeTrue();
    value.Text.Should().Be("3");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Loop);
  }

  [Fact]
  public void MixedValueGetsTheCommandLayerAndTheSources() {
    var outer = OuterScope(null, ("option", "b"));

    var child = BindOrFail(outer, Bind(("novaOptionImage", "nova-{{option}}")));

    child.TryResolve("novaOptionImage", out var value).Should().BeTrue();
    value.Text.Should().Be("nova-b");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Command);
    value.Sources.Should().ContainSingle()
        .Which.Should().Be(new ResolvedParameter("option", "b", ParameterScopeLayers.Entry));
  }

  [Theory]
  [InlineData("pns-todo-radar")]
  [InlineData("${x}")]
  [InlineData("")]
  public void LiteralValueStaysLiteralWithTheCommandLayer(string literal) {
    var outer = OuterScope(null, ("x", "outer-x"));

    var child = BindOrFail(outer, Bind(("novaOptionImage", literal)));

    child.TryResolve("novaOptionImage", out var value).Should().BeTrue();
    value.Text.Should().Be(literal);
    value.OriginLayer.Should().Be(ParameterScopeLayers.Command);
    value.Sources.Should().BeNull();
  }

  [Fact]
  public void NullValueIsSkippedSoTheNameInheritsTheOuterValue() {
    var outer = OuterScope(null, ("novaOptionImage", "pns-alliance-nav-button"));

    var child = BindOrFail(outer, Bind(("novaOptionImage", null)));

    child.TryResolve("novaOptionImage", out var value).Should().BeTrue();
    value.Text.Should().Be("pns-alliance-nav-button");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  [Fact]
  public void UnresolvedPlaceholderGivesTheErrorAndNoChild() {
    var outer = OuterScope();

    outer.TryBindChild(ParameterScopeLayers.Command, Bind(("novaOptionImage", "{{missing}}")), out var child, out var error)
        .Should().BeFalse();

    child.Should().BeNull();
    error.Should().Be(new ParameterResolutionError(
        "missing", "parameterBindings.novaOptionImage", ParameterResolutionReasons.Unresolved));
  }

  [Fact]
  public void SpaceAroundAPlaceholderGoesToTheMixedPathAndIsKept() {
    var outer = OuterScope(null, ("x", "value"));

    var child = BindOrFail(outer, Bind(("y", " {{x}}")));

    child.TryResolve("y", out var value).Should().BeTrue();
    value.Text.Should().Be(" value");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Command);
    value.Sources.Should().ContainSingle().Which.Name.Should().Be("x");
  }

  [Fact]
  public void WithNoPlaceholderTheResultIsTheSameAsChild() {
    var outer = OuterScope(null, ("x", "outer-x"), ("keep", "k"));
    var bindings = Bind(("x", "inner-x"), ("y", "literal"), ("z", null));

    var bound = BindOrFail(outer, bindings);
    var plain = outer.Child(ParameterScopeLayers.Command, bindings, null);

    bound.Describe().Should().BeEquivalentTo(plain.Describe(), options => options.WithStrictOrdering());
    foreach (var name in new[] { "x", "y", "z", "keep", "queue.emulatorSerial" }) {
      bound.TryResolve(name, out var a).Should().Be(plain.TryResolve(name, out var b));
      a.Should().Be(b);
    }
  }

  [Fact]
  public void TheDefaultOfTheCalledCommandIsNotUsed() {
    var outer = OuterScope();
    // The scope of the called command has a default for the bound name. It is not in the outer scope.
    var calledCommand = outer.Child(ParameterScopeLayers.Command, null, new Collection<ParameterDeclaration> {
      new() { Name = "novaOptionImage", Default = "pns-todo-radar" }
    });
    calledCommand.TryResolve("novaOptionImage", out _).Should().BeTrue();

    outer.TryBindChild(ParameterScopeLayers.Command, Bind(("novaOptionImage", "{{novaOptionImage}}")), out var child, out var error)
        .Should().BeFalse();

    child.Should().BeNull();
    error.Should().Be(new ParameterResolutionError(
        "novaOptionImage", "parameterBindings.novaOptionImage", ParameterResolutionReasons.Unresolved));
  }
}

#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 192: the resolver gives the text of a TextInput step from a parameter.</summary>
public sealed class CommandStepResolverTextInputTests {
  private static ParameterScope Scope(ParameterDeclaration declaration, string value) {
    var bindings = new Collection<ParameterBinding> { new() { Name = declaration.Name, Value = value } };
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, new[] { declaration });
  }

  private static ParameterDeclaration Number(string name) => new() { Name = name, Type = ParameterValueType.Number };

  private static ParameterDeclaration Text(string name) => new() { Name = name };

  private static CommandStep Step(string text, string? overlay = null) => new() {
    Type = CommandStepType.TextInput,
    Order = 2,
    TextInput = new TextInputConfig { Text = text },
    FieldTemplates = overlay is null ? null : new Dictionary<string, string> { ["textInput.text"] = overlay }
  };

  [Fact]
  public void TheOverlayGivesTheValueOfANumberParameter() {
    CommandStepResolver.TryResolve(Step(string.Empty, "{{fighterBatch}}"), Scope(Number("fighterBatch"), "250"),
        out var resolved, out var error, out var used).Should().BeTrue();

    error.Should().BeNull();
    resolved!.TextInput!.Text.Should().Be("250");
    used.Should().ContainSingle(p => p.Name == "fighterBatch" && p.Value == "250");
  }

  [Fact]
  public void TheOverlayGivesTheValueOfATextParameter() {
    CommandStepResolver.TryResolve(Step(string.Empty, "{{fighterBatch}}"), Scope(Text("fighterBatch"), "100"),
        out var resolved, out _, out _).Should().BeTrue();

    resolved!.TextInput!.Text.Should().Be("100");
  }

  [Fact]
  public void AnInlinePlaceholderResolves() {
    CommandStepResolver.TryResolve(Step("{{fighterBatch}}"), Scope(Number("fighterBatch"), "250"),
        out var resolved, out _, out var used).Should().BeTrue();

    resolved!.TextInput!.Text.Should().Be("250");
    used.Should().ContainSingle(p => p.Name == "fighterBatch");
  }

  [Fact]
  public void InlineTextAroundAPlaceholderResolves() {
    CommandStepResolver.TryResolve(Step("x{{a}}y"), Scope(Text("a"), "5"),
        out var resolved, out _, out _).Should().BeTrue();

    resolved!.TextInput!.Text.Should().Be("x5y");
  }

  [Fact]
  public void TheOverlayReplacesTheInlineTextAndTheInlineTextIsNotResolved() {
    CommandStepResolver.TryResolve(Step("{{unknownInline}}", "{{fighterBatch}}"), Scope(Number("fighterBatch"), "250"),
        out var resolved, out var error, out _).Should().BeTrue();

    error.Should().BeNull();
    resolved!.TextInput!.Text.Should().Be("250");
  }

  [Fact]
  public void AnUnknownOverlayNameIsUnresolved() {
    CommandStepResolver.TryResolve(Step(string.Empty, "{{fighterBach}}"), Scope(Number("fighterBatch"), "250"),
        out var resolved, out var error, out _).Should().BeFalse();

    resolved.Should().BeNull();
    error!.ParameterName.Should().Be("fighterBach");
    error.FieldPath.Should().Be("textInput.text");
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
  }

  [Fact]
  public void AMultiValueParameterWithTwoItemsGivesListInSingleField() {
    var quests = new ParameterDeclaration {
      Name = "quests", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b" }, Multiple = true
    };
    var bindings = new Collection<ParameterBinding> {
      new() { Name = "quests", Value = ParameterListRules.Join(new[] { "a", "b" }), Values = new[] { "a", "b" } }
    };
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, new[] { quests });

    CommandStepResolver.TryResolve(Step("{{quests}}"), scope, out _, out var inlineError, out _).Should().BeFalse();
    inlineError!.Reason.Should().Be(ParameterResolutionReasons.ListInSingleField);

    CommandStepResolver.TryResolve(Step(string.Empty, "{{quests}}"), scope, out _, out var overlayError, out _).Should().BeFalse();
    overlayError!.Reason.Should().Be(ParameterResolutionReasons.ListInSingleField);
  }

  [Fact]
  public void ALiteralTextDoesNotChange() {
    var step = Step("250");

    CommandStepResolver.TryResolve(step, ParameterScope.Empty, out var resolved, out _, out var used).Should().BeTrue();

    resolved!.TextInput!.Text.Should().Be("250");
    used.Should().BeEmpty();
  }
}

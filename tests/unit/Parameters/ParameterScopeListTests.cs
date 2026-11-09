#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 179: how a list value resolves in the scope and in a step.</summary>
public sealed class ParameterScopeListTests {
  private static readonly ParameterDeclaration Quests = new() {
    Name = "quests", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b", "c" }, Multiple = true
  };

  private static ParameterScope ScopeWithList(params string[] items) {
    var bindings = new Collection<ParameterBinding> {
      new() { Name = "quests", Value = ParameterListRules.Join(items), Values = items }
    };
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, new[] { Quests });
  }

  private static CommandStep TapWithImage(string imageId) => new() {
    Type = CommandStepType.PrimitiveTap,
    Order = 1,
    PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget(imageId, 0.8, 0, 0) }
  };

  [Fact]
  public void JoinedTextResolvesInATextField() {
    var step = new CommandStep {
      Type = CommandStepType.KeyInput, Order = 0, KeyInput = new KeyInputConfig { Key = "K_{{quests}}" }
    };

    CommandStepResolver.TryResolve(step, ScopeWithList("a", "b"), out var resolved, out var error, out _).Should().BeTrue();
    error.Should().BeNull();
    resolved!.KeyInput!.Key.Should().Be("K_a|b");
  }

  [Fact]
  public void OneItemResolvesInAnImageField() {
    CommandStepResolver.TryResolve(TapWithImage("{{quests}}"), ScopeWithList("b"), out var resolved, out var error, out _)
        .Should().BeTrue();
    error.Should().BeNull();
    resolved!.PrimitiveTap!.DetectionTarget!.ReferenceImageId.Should().Be("b");
  }

  [Fact]
  public void TwoItemsInAnImageFieldFailTheStepWithAClearMessage() {
    CommandStepResolver.TryResolve(TapWithImage("{{quests}}"), ScopeWithList("a", "b"), out var resolved, out var error, out _)
        .Should().BeFalse();
    resolved.Should().BeNull();
    error!.Reason.Should().Be(ParameterResolutionReasons.ListInSingleField);
    var message = error.ToMessage("1");
    message.Should().Contain("quests").And.Contain("primitiveTap.detectionTarget.referenceImageId").And.Contain("2 items");
  }

  [Fact]
  public void TwoItemsInANumericOverlayFieldFailTheStep() {
    var step = new CommandStep {
      Type = CommandStepType.Swipe, Order = 0,
      Swipe = new SwipeConfig { StartX = 0, StartY = 10, EndX = 20, EndY = 30 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "{{quests}}" }
    };

    CommandStepResolver.TryResolve(step, ScopeWithList("a", "b"), out _, out var error, out _).Should().BeFalse();
    error!.Reason.Should().Be(ParameterResolutionReasons.ListInSingleField);
  }

  [Fact]
  public void ListInOverlayImageFieldWithManyItemsFails() {
    var step = TapWithImage("x");
    step = new CommandStep {
      Type = step.Type, Order = step.Order, PrimitiveTap = step.PrimitiveTap,
      FieldTemplates = new Dictionary<string, string> { ["primitiveTap.detectionTarget.referenceImageId"] = "{{quests}}" }
    };

    CommandStepResolver.TryResolve(step, ScopeWithList("a", "c"), out _, out var error, out _).Should().BeFalse();
    error!.Reason.Should().Be(ParameterResolutionReasons.ListInSingleField);
    error.OffendingValue.Should().Be("2");
  }

  [Fact]
  public void ParameterThatHoldsOneValueIsNotChecked() {
    var single = new ParameterDeclaration { Name = "pic", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a|b" } };
    var scope = ParameterScope.Empty.Child(
        ParameterScopeLayers.Entry,
        new[] { new ParameterBinding { Name = "pic", Value = "a|b" } },
        new[] { single });
    var step = TapWithImage("{{pic}}");

    CommandStepResolver.TryResolve(step, scope, out var resolved, out var error, out _).Should().BeTrue();
    error.Should().BeNull();
    resolved!.PrimitiveTap!.DetectionTarget!.ReferenceImageId.Should().Be("a|b");
  }

  [Fact]
  public void PassThroughBindingBetweenMultiValueParametersKeepsTheFullList() {
    var outer = ScopeWithList("a", "b", "c");
    var calleeDeclaration = new ParameterDeclaration {
      Name = "picks", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b", "c" }, Multiple = true
    };

    outer.TryBindChild(
        ParameterScopeLayers.Command,
        new[] { new ParameterBinding { Name = "picks", Value = "{{quests}}" } },
        out var child, out var error,
        new[] { calleeDeclaration }).Should().BeTrue();
    error.Should().BeNull();
    child!.TryResolve("picks", out var value).Should().BeTrue();
    value.Text.Should().Be("a|b|c");
  }

  [Fact]
  public void MultiValueDefaultReadsAsTheJoinedText() {
    var declaration = new ParameterDeclaration {
      Name = "quests", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b" }, Multiple = true, Default = "a|b"
    };
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Sequence, null, new[] { declaration });

    scope.TryResolve("quests", out var value).Should().BeTrue();
    value.Text.Should().Be("a|b");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Default);
  }

  [Fact]
  public void OptionLabelLookupGivesNoLabelForAJoinedTextAndDoesNotThrow() {
    var declaration = new ParameterDeclaration {
      Name = "mode", Kind = ParameterInputKind.Choice, Multiple = true,
      Options = new[] { new ParameterOption { Label = "A", Value = "a" }, new ParameterOption { Label = "B", Value = "b" } }
    };
    var scope = ParameterScope.Empty.Child(
        ParameterScopeLayers.Entry,
        new[] { new ParameterBinding { Name = "mode", Value = "a|b", Values = new[] { "a", "b" } } },
        new[] { declaration });

    scope.TryResolve("mode", out var value).Should().BeTrue();
    value.Text.Should().Be("a|b");
    value.Label.Should().BeNull();
  }
}

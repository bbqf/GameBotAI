using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 114: the condition resolver substitutes each <c>imageVisible.imageId</c> against a scope
/// before the evaluation.
/// </summary>
public sealed class SequenceStepConditionResolverTests {
  private static ParameterScope ScopeWith(params (string Name, string Value)[] values) {
    var bindings = new Collection<ParameterBinding>();
    foreach (var (name, value) in values) bindings.Add(new ParameterBinding { Name = name, Value = value });
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, null);
  }

  private static ImageVisibleStepCondition Image(string id, double? minSimilarity = null, bool negate = false) =>
      new() { ImageId = id, MinSimilarity = minSimilarity, Negate = negate };

  [Fact]
  public void TreeWithNoPlaceholderComesBackAsTheSameInstance() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        Image("option-a"),
        new CommandOutcomeStepCondition { StepRef = "s1", ExpectedState = "success" }
      }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "condition", out var resolved, out var error, out var used)
        .Should().BeTrue();

    resolved.Should().BeSameAs(condition);
    error.Should().BeNull();
    used.Should().BeEmpty();
  }

  [Fact]
  public void LeafPlaceholderResolvesAndKeepsTheOtherMembers() {
    var condition = Image("{{novaOption}}", minSimilarity: 0.9, negate: true);

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(("novaOption", "option-b")), "condition",
            out var resolved, out var error, out var used)
        .Should().BeTrue();

    error.Should().BeNull();
    var image = resolved.Should().BeOfType<ImageVisibleStepCondition>().Subject;
    image.Should().NotBeSameAs(condition);
    image.ImageId.Should().Be("option-b");
    image.MinSimilarity.Should().Be(0.9);
    image.Negate.Should().BeTrue();
    condition.ImageId.Should().Be("{{novaOption}}", "the stored condition does not change");
    used.Should().ContainSingle(p => p.Name == "novaOption" && p.Value == "option-b"
        && p.OriginLayer == ParameterScopeLayers.Entry);
  }

  [Fact]
  public void LeafPlaceholderInSurroundingTextResolves() {
    SequenceStepConditionResolver.TryResolve(Image("nova-{{option}}"), ScopeWith(("option", "b")), "condition",
            out var resolved, out _, out _)
        .Should().BeTrue();

    resolved.Should().BeOfType<ImageVisibleStepCondition>().Which.ImageId.Should().Be("nova-b");
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void CompositeResolvesEachChildAndKeepsNegate(string rule) {
    var children = new SequenceStepCondition[] {
      Image("{{novaOption}}"),
      new CommandOutcomeStepCondition { StepRef = "s1", ExpectedState = "success" },
      Image("option-z")
    };
    CompositeStepCondition condition = rule switch {
      "all" => new AllStepCondition { Children = children, Negate = true },
      "any" => new AnyStepCondition { Children = children, Negate = true },
      _ => new NoneStepCondition { Children = children, Negate = true }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(("novaOption", "option-b")), "if.condition",
            out var resolved, out _, out var used)
        .Should().BeTrue();

    var composite = resolved.Should().BeAssignableTo<CompositeStepCondition>().Subject;
    composite.Should().NotBeSameAs(condition);
    composite.Rule.Should().Be(condition.Rule);
    composite.Negate.Should().BeTrue();
    composite.Children.Should().HaveCount(3);
    composite.Children[0].Should().BeOfType<ImageVisibleStepCondition>().Which.ImageId.Should().Be("option-b");
    composite.Children[1].Should().BeSameAs(children[1]);
    composite.Children[2].Should().BeSameAs(children[2]);
    used.Should().ContainSingle(p => p.Name == "novaOption");
  }

  [Fact]
  public void OtherLeafTypeComesBackAsTheSameInstance() {
    var condition = new LastRunStepCondition { Sequence = "self", Status = "success", Within = "1h" };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "condition", out var resolved, out _, out _)
        .Should().BeTrue();

    resolved.Should().BeSameAs(condition);
  }

  [Fact]
  public void UnknownNameInACompositeChildFailsWithTheChildPath() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { Image("option-a"), Image("{{novaOption}}") }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "if.condition", out _, out var error, out _)
        .Should().BeFalse();

    error!.ParameterName.Should().Be("novaOption");
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.FieldPath.Should().Be("if.condition.children[1].imageId");
  }

  [Fact]
  public void UnknownNameInALeafFailsWithThePrefixPath() {
    SequenceStepConditionResolver.TryResolve(Image("{{novaOption}}"), ScopeWith(), "breakCondition",
            out _, out var error, out _)
        .Should().BeFalse();

    error!.FieldPath.Should().Be("breakCondition.imageId");
    error.ToMessage("brk").Should().Be(
        "Step 'brk': parameter 'novaOption' used by field 'breakCondition.imageId' could not be resolved from any scope.");
  }

  [Fact]
  public void EmptyResultFailsAsUnresolved() {
    SequenceStepConditionResolver.TryResolve(Image("{{novaOption}}"), ScopeWith(("novaOption", "  ")), "condition",
            out _, out var error, out _)
        .Should().BeFalse();

    error!.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.FieldPath.Should().Be("condition.imageId");
  }
}

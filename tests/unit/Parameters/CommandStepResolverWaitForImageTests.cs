using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 186: resolution of the placeholders of an inline WaitForImage configuration.</summary>
public sealed class CommandStepResolverWaitForImageTests {
  private static readonly ParameterDeclaration Quests = new() {
    Name = "q", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b", "c" }, Multiple = true
  };

  private static ParameterScope SingleScope(string value) {
    var bindings = new Collection<ParameterBinding> { new() { Name = "s", Value = value } };
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, null);
  }

  private static ParameterScope ListScope(params string[] items) {
    var bindings = new Collection<ParameterBinding> {
      new() { Name = "q", Value = ParameterListRules.Join(items), Values = items }
    };
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, new[] { Quests });
  }

  private static WaitForImageConfig Config(string imageId) =>
    new() { TimeoutMs = 500, DetectionTarget = new DetectionTarget(imageId, 0.9) };

  [Fact]
  public void SinglePlaceholderResolvesToTheImageId() {
    CommandStepResolver.TryResolveWaitForImage(Config("{{s}}"), SingleScope("city"), out var resolved, out var error)
      .Should().BeTrue();

    error.Should().BeNull();
    resolved!.DetectionTarget!.ReferenceImageId.Should().Be("city");
    resolved.TimeoutMs.Should().Be(500);
  }

  [Fact]
  public void MultiValuePlaceholderResolvesToTheList() {
    CommandStepResolver.TryResolveWaitForImage(Config("{{q}}"), ListScope("a", "b"), out var resolved, out _)
      .Should().BeTrue();

    resolved!.DetectionTarget!.ReferenceImageId.Should().Be("a");
    resolved.DetectionTarget.ReferenceImageIds.Should().Equal("a", "b");
  }

  [Fact]
  public void LiteralIdStaysAsItIs() {
    var config = Config("literal-image");

    CommandStepResolver.TryResolveWaitForImage(config, ParameterScope.Empty, out var resolved, out _)
      .Should().BeTrue();

    resolved.Should().BeSameAs(config);
  }

  [Fact]
  public void UnknownNameGivesAnError() {
    CommandStepResolver.TryResolveWaitForImage(Config("{{missing}}"), ParameterScope.Empty, out var resolved, out var error)
      .Should().BeFalse();

    resolved.Should().BeNull();
    error!.ParameterName.Should().Be("missing");
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
  }
}

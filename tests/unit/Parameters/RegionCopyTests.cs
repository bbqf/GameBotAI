using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 130: the parameter resolvers copy a condition or a target field by field. The region must
/// survive the copy, or a parametrized step loses its region at run time.
/// </summary>
public sealed class RegionCopyTests {
  private static readonly PixelRegion Region = new(120, 640, 300, 90);

  private static ParameterScope ScopeWith(string name, string value) {
    var bindings = new Collection<ParameterBinding> { new() { Name = name, Value = value } };
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, null);
  }

  private static void ShouldBeRegion(PixelRegion? actual) {
    actual.Should().NotBeNull();
    (actual!.X, actual.Y, actual.Width, actual.Height).Should().Be((120, 640, 300, 90));
  }

  [Fact]
  public void TryResolveImageKeepsTheRegionWhenItSubstitutesTheImageId() {
    var condition = new ImageVisibleStepCondition { ImageId = "{{img}}", MinSimilarity = 0.9, Region = Region };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith("img", "price-icon"), "condition", out var resolved, out _, out _)
        .Should().BeTrue();

    var image = resolved.Should().BeOfType<ImageVisibleStepCondition>().Subject;
    image.ImageId.Should().Be("price-icon");
    ShouldBeRegion(image.Region);
  }

  [Fact]
  public void TryResolveImageKeepsTheRegionInsideAComposite() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "{{img}}", Region = Region } }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith("img", "price-icon"), "condition", out var resolved, out _, out _)
        .Should().BeTrue();

    var child = resolved.Should().BeOfType<AllStepCondition>().Subject.Children.Single();
    ShouldBeRegion(child.Should().BeOfType<ImageVisibleStepCondition>().Subject.Region);
  }

  [Fact]
  public void TryDetectionKeepsTheRegionForPrimitiveTap() {
    var step = new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{img}}", 0.9, region: Region) }
    };

    CommandStepResolver.TryResolve(step, ScopeWith("img", "exchange-button"), out var resolved, out var error, out _)
        .Should().BeTrue();

    error.Should().BeNull();
    resolved!.PrimitiveTap!.DetectionTarget.ReferenceImageId.Should().Be("exchange-button");
    ShouldBeRegion(resolved.PrimitiveTap.DetectionTarget.Region);
  }

  [Fact]
  public void TryDetectionKeepsTheRegionForWaitForImage() {
    var step = new CommandStep {
      Type = CommandStepType.WaitForImage,
      Order = 0,
      WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("{{img}}", 0.9, region: Region), TimeoutMs = 1000 }
    };

    CommandStepResolver.TryResolve(step, ScopeWith("img", "exchange-button"), out var resolved, out var error, out _)
        .Should().BeTrue();

    error.Should().BeNull();
    ShouldBeRegion(resolved!.WaitForImage!.DetectionTarget!.Region);
  }

  [Fact]
  public void TryDetectionKeepsTheRegionForTheReadinessImage() {
    var step = new CommandStep {
      Type = CommandStepType.EnsureGameRunning,
      Order = 0,
      EnsureGameRunning = new EnsureGameRunningConfig { ReadinessImage = new DetectionTarget("{{img}}", 0.9, region: Region), ReadinessTimeoutMs = 1000 }
    };

    CommandStepResolver.TryResolve(step, ScopeWith("img", "city-hud"), out var resolved, out var error, out _)
        .Should().BeTrue();

    error.Should().BeNull();
    ShouldBeRegion(resolved!.EnsureGameRunning!.ReadinessImage!.Region);
  }

  [Fact]
  public void TryDetectionKeepsANullRegionAsNull() {
    var step = new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{img}}", 0.9) }
    };

    CommandStepResolver.TryResolve(step, ScopeWith("img", "exchange-button"), out var resolved, out _, out _)
        .Should().BeTrue();

    resolved!.PrimitiveTap!.DetectionTarget.Region.Should().BeNull();
  }
}

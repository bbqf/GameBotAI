using System.Collections.Generic;
using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 078: applying a scope to a command step — inline string substitution, the numeric overlay,
/// coercion failures, and the fields that must stay literal.
/// </summary>
public sealed class CommandStepResolverTests {
  private static ParameterScope ScopeWith(params (string Name, string Value)[] values) {
    var bindings = new Collection<ParameterBinding>();
    foreach (var (name, value) in values) bindings.Add(new ParameterBinding { Name = name, Value = value });
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, null);
  }

  // ── String fields resolve inline (FR-005) ──────────────────────────────────

  [Fact]
  public void StringFieldResolvesFromScope() {
    var step = new CommandStep {
      Type = CommandStepType.EnsureEmulatorRunning,
      Order = 0,
      EnsureEmulatorRunning = new EnsureEmulatorRunningConfig { AdbSerial = "{{adbSerial}}", InstanceName = "PNS" }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("adbSerial", "emulator-5560")), out var resolved, out var error, out var used)
        .Should().BeTrue();

    error.Should().BeNull();
    resolved!.EnsureEmulatorRunning!.AdbSerial.Should().Be("emulator-5560");
    resolved.EnsureEmulatorRunning.InstanceName.Should().Be("PNS");
    used.Should().ContainSingle(p => p.Name == "adbSerial" && p.Value == "emulator-5560");
  }

  [Fact]
  public void StringFieldResolvesWhenEmbeddedInSurroundingText() {
    var step = new CommandStep {
      Type = CommandStepType.KeyInput,
      Order = 0,
      KeyInput = new KeyInputConfig { Key = "KEYCODE_{{keyName}}" }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("keyName", "HOME")), out var resolved, out _, out _)
        .Should().BeTrue();

    resolved!.KeyInput!.Key.Should().Be("KEYCODE_HOME");
  }

  [Fact]
  public void UnresolvableStringReferenceFailsWithoutProducingAStep() {
    var step = new CommandStep {
      Type = CommandStepType.EnsureEmulatorRunning,
      Order = 3,
      EnsureEmulatorRunning = new EnsureEmulatorRunningConfig { AdbSerial = "{{missing}}" }
    };

    CommandStepResolver.TryResolve(step, ParameterScope.Empty, out var resolved, out var error, out _)
        .Should().BeFalse();

    resolved.Should().BeNull();
    error!.ParameterName.Should().Be("missing");
    error.FieldPath.Should().Be("ensureEmulatorRunning.adbSerial");
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.ToMessage("3").Should().Contain("could not be resolved from any scope");
  }

  // ── Numeric overlay (FR-006, FR-019) ───────────────────────────────────────

  [Fact]
  public void NumericFieldResolvesThroughTheFieldTemplateOverlay() {
    var step = new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 0, StartY = 10, EndX = 20, EndY = 30 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "{{originX}}" }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("originX", "144")), out var resolved, out _, out var used)
        .Should().BeTrue();

    resolved!.Swipe!.StartX.Should().Be(144);
    resolved.Swipe.StartY.Should().Be(10, "unparametrized numeric fields keep their stored value");
    used.Should().ContainSingle(p => p.Name == "originX" && p.Value == "144");
  }

  [Fact]
  public void NumericFieldRejectsAValueThatIsNotAWholeNumber() {
    var step = new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 2,
      Swipe = new SwipeConfig { StartX = 0, StartY = 0, EndX = 1, EndY = 1 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "{{originX}}" }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("originX", "left")), out var resolved, out var error, out _)
        .Should().BeFalse();

    resolved.Should().BeNull();
    error!.Reason.Should().Be(ParameterResolutionReasons.NotANumber);
    error.ParameterName.Should().Be("originX");
    error.OffendingValue.Should().Be("left");
    error.ToMessage("2").Should().Contain("is not a whole number for field 'swipe.startX'");
  }

  [Fact]
  public void NumericFieldAcceptsOnlyAWholeFieldPlaceholder() {
    // Embedding a placeholder in surrounding text is legal for strings but not for numbers, because
    // the result must parse cleanly into an int.
    var step = new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 0, StartY = 0, EndX = 1, EndY = 1 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "x{{originX}}" }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("originX", "144")), out _, out var error, out _)
        .Should().BeFalse();

    error!.Reason.Should().Be(ParameterResolutionReasons.NotANumber);
  }

  [Fact]
  public void UnresolvableNumericOverlayFails() {
    var step = new CommandStep {
      Type = CommandStepType.EnsureGameRunning,
      Order = 0,
      EnsureGameRunning = new EnsureGameRunningConfig { ReadinessTimeoutMs = 90_000 },
      FieldTemplates = new Dictionary<string, string> { ["ensureGameRunning.readinessTimeoutMs"] = "{{timeout}}" }
    };

    CommandStepResolver.TryResolve(step, ParameterScope.Empty, out _, out var error, out _).Should().BeFalse();

    error!.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.ParameterName.Should().Be("timeout");
  }

  // ── References stay literal (FR-007) ───────────────────────────────────────

  [Fact]
  public void CommandStepTargetIdIsNeverSubstituted() {
    // TargetId on a Command step is a command *reference*; substituting it would defeat the existing
    // dangling-reference validation.
    var step = new CommandStep { Type = CommandStepType.Command, Order = 0, TargetId = "{{which}}" };

    CommandStepResolver.TryResolve(step, ScopeWith(("which", "other-command")), out var resolved, out _, out _)
        .Should().BeTrue();

    resolved!.TargetId.Should().Be("{{which}}");
  }

  // ── Pass-through and idempotence ───────────────────────────────────────────

  [Fact]
  public void UnparametrizedStepIsReturnedUnchanged() {
    var step = new CommandStep {
      Type = CommandStepType.KeyInput,
      Order = 0,
      KeyInput = new KeyInputConfig { Key = "KEYCODE_HOME" }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("unused", "x")), out var resolved, out _, out var used)
        .Should().BeTrue();

    resolved.Should().BeSameAs(step, "an unparametrized step needs no clone");
    used.Should().BeEmpty();
  }

  [Fact]
  public void ResolvedStepPreservesEveryUnrelatedField() {
    var step = new CommandStep {
      Type = CommandStepType.EnsureEmulatorRunning,
      Order = 7,
      TargetId = "t",
      EnsureEmulatorRunning = new EnsureEmulatorRunningConfig {
        AdbSerial = "{{adbSerial}}", InstanceName = "PNS", InstanceIndex = 3
      }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("adbSerial", "s")), out var resolved, out _, out _)
        .Should().BeTrue();

    resolved!.Order.Should().Be(7);
    resolved.Type.Should().Be(CommandStepType.EnsureEmulatorRunning);
    resolved.EnsureEmulatorRunning!.InstanceIndex.Should().Be(3);
  }

  [Fact]
  public void DetectionTargetImageIdResolves() {
    var step = new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{img}}", 0.9, 4, 5) }
    };

    CommandStepResolver.TryResolve(step, ScopeWith(("img", "city-view")), out var resolved, out _, out _)
        .Should().BeTrue();

    resolved!.PrimitiveTap!.DetectionTarget.ReferenceImageId.Should().Be("city-view");
    resolved.PrimitiveTap.DetectionTarget.Confidence.Should().Be(0.9);
    resolved.PrimitiveTap.DetectionTarget.OffsetX.Should().Be(4);
    resolved.PrimitiveTap.DetectionTarget.OffsetY.Should().Be(5);
  }

  [Fact]
  public void SupportedFieldTemplatePathsCoverTheDocumentedSet() {
    CommandStepFieldPaths.IsSupported("swipe.startX").Should().BeTrue();
    CommandStepFieldPaths.IsSupported("ensureEmulatorRunning.instanceIndex").Should().BeTrue();
    CommandStepFieldPaths.IsSupported("primitiveTap.detectionTarget.confidence").Should().BeTrue();
    CommandStepFieldPaths.IsSupported("ensureEmulatorRunning.adbSerial").Should().BeFalse(
        "string fields carry their placeholder inline, not through the overlay");
    CommandStepFieldPaths.IsSupported("nonsense.path").Should().BeFalse();
  }
}

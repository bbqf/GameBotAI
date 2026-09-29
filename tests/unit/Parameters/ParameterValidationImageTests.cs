using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 114: the image keys of <c>fieldTemplates</c> and the rule for their value.
/// </summary>
public sealed class ParameterValidationImageTests {
  private const string TapImageKey = "primitiveTap.detectionTarget.referenceImageId";
  private const string WaitImageKey = "waitForImage.detectionTarget.referenceImageId";

  private static Command TapCommand(string key, string value, int order = 0) {
    var command = new Command { Id = "c", Name = "C" };
    command.Parameters.Add(new ParameterDeclaration { Name = "novaOption" });
    command.Parameters.Add(new ParameterDeclaration { Name = "option" });
    command.Parameters.Add(new ParameterDeclaration { Name = "a" });
    command.Parameters.Add(new ParameterDeclaration { Name = "b" });
    command.Parameters.Add(new ParameterDeclaration { Name = "x", Type = ParameterValueType.Number });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = order,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("option-a") },
      FieldTemplates = new Dictionary<string, string> { [key] = value }
    });
    return command;
  }

  // ── Key set ─────────────────────────────────────────────────────────────────

  [Theory]
  [InlineData(TapImageKey)]
  [InlineData(WaitImageKey)]
  public void ImageKeysAreSupportedImagePaths(string key) {
    CommandStepFieldPaths.IsSupported(key).Should().BeTrue();
    CommandStepFieldPaths.IsImagePath(key).Should().BeTrue();
  }

  [Theory]
  [InlineData("swipe.startX")]
  [InlineData("primitiveTap.detectionTarget.confidence")]
  [InlineData("waitForImage.timeoutMs")]
  public void NumericKeysStaySupportedAndAreNotImagePaths(string key) {
    CommandStepFieldPaths.IsSupported(key).Should().BeTrue();
    CommandStepFieldPaths.IsImagePath(key).Should().BeFalse();
  }

  [Fact]
  public void ReadinessImageKeyIsNotSupported() {
    CommandStepFieldPaths.IsSupported("ensureGameRunning.readinessImage.referenceImageId").Should().BeFalse();
    CommandStepFieldPaths.IsImagePath("ensureGameRunning.readinessImage.referenceImageId").Should().BeFalse();
  }

  // ── Value rule ──────────────────────────────────────────────────────────────

  [Theory]
  [InlineData(TapImageKey)]
  [InlineData(WaitImageKey)]
  public void ImageKeyAcceptsOneWholePlaceholder(string key) {
    var result = ParameterValidationService.ValidateCommand(TapCommand(key, "{{novaOption}}"));

    result.Errors.Should().BeEmpty();
  }

  [Theory]
  [InlineData("nova-{{option}}")]
  [InlineData("option-a")]
  [InlineData("{{a}}{{b}}")]
  public void ImageKeyRejectsAValueThatIsNotOneWholePlaceholder(string value) {
    var result = ParameterValidationService.ValidateCommand(TapCommand(TapImageKey, value, order: 4));

    var error = result.Errors.Should().ContainSingle(
        e => e.Code == ParameterValidationCodes.InvalidFieldTemplateValue).Subject;
    error.FieldPath.Should().Be(TapImageKey);
    error.Message.Should().Be(
        $"Step 4: the value of '{TapImageKey}' must be one whole placeholder, for example {{{{name}}}}.");
  }

  [Fact]
  public void UnknownKeyGivesTheNewMessageText() {
    var command = TapCommand("ensureGameRunning.readinessImage.referenceImageId", "{{novaOption}}");

    var result = ParameterValidationService.ValidateCommand(command);

    var error = result.Errors.Should().ContainSingle(
        e => e.Code == ParameterValidationCodes.UnknownFieldTemplatePath).Subject;
    error.Message.Should().Be(
        "Step 0: 'ensureGameRunning.readinessImage.referenceImageId' is not a parametrizable field.");
    error.Message.Should().NotContain("numeric");
  }

  [Fact]
  public void NumericKeyWithALiteralValueStillPassesTheSave() {
    // FR-013: the numeric keys get no value check at save time.
    var result = ParameterValidationService.ValidateCommand(
        TapCommand("primitiveTap.detectionTarget.offsetX", "12"));

    result.Errors.Should().BeEmpty();
  }

  [Fact]
  public void ImageKeyGivesTheStaticCheckSkippedWarning() {
    var result = ParameterValidationService.ValidateCommand(TapCommand(TapImageKey, "{{novaOption}}"));

    result.Warnings.Should().Contain(w => w.Code == ParameterValidationCodes.StaticCheckSkipped
        && w.FieldPath == TapImageKey && w.ParameterName == "novaOption");
  }

  [Fact]
  public void ImageKeyWithAnUndeclaredNameIsUnresolvable() {
    var result = ParameterValidationService.ValidateCommand(TapCommand(TapImageKey, "{{undeclared}}"));

    result.Errors.Select(e => e.Code).Should().Contain(ParameterValidationCodes.UnresolvableReference);
  }

  // ── FindImageValueCandidates (US3, FR-010) ───────────────────────────────────

  private static QueueTemplateEntry Entry(params (string Name, string Value)[] values) {
    var entry = new QueueTemplateEntry { SequenceId = "seq" };
    foreach (var (name, value) in values) entry.ParameterValues.Add(new ParameterBinding { Name = name, Value = value });
    return entry;
  }

  private static ParameterDeclaration Declare(string name, string? defaultValue = null) =>
      new() { Name = name, Default = defaultValue };

  private static CommandStep TapStep(string inlineId, int order = 0, string? overlay = null) => new() {
    Type = CommandStepType.PrimitiveTap,
    Order = order,
    PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget(inlineId) },
    FieldTemplates = overlay is null ? null : new Dictionary<string, string> { [TapImageKey] = overlay }
  };

  private static CommandStep CallStep(string targetId, int order = 1, params (string Name, string? Value)[] bindings) {
    var step = new CommandStep {
      Type = CommandStepType.Command,
      Order = order,
      TargetId = targetId,
      ParameterBindings = bindings.Length == 0 ? null : new System.Collections.ObjectModel.Collection<ParameterBinding>()
    };
    foreach (var (name, value) in bindings) step.ParameterBindings!.Add(new ParameterBinding { Name = name, Value = value });
    return step;
  }

  private static Command MakeCommand(string id, IEnumerable<CommandStep> steps, params ParameterDeclaration[] declarations) {
    var command = new Command { Id = id, Name = id };
    foreach (var step in steps) command.Steps.Add(step);
    foreach (var declaration in declarations) command.Parameters.Add(declaration);
    return command;
  }

  private static SequenceStep CommandCall(string stepId, string commandId, int order = 0, params (string Name, string? Value)[] bindings) {
    var step = new SequenceStep {
      Order = order,
      StepId = stepId,
      StepType = SequenceStepType.Command,
      CommandId = commandId,
      ParameterBindings = bindings.Length == 0 ? null : new System.Collections.ObjectModel.Collection<ParameterBinding>()
    };
    foreach (var (name, value) in bindings) step.ParameterBindings!.Add(new ParameterBinding { Name = name, Value = value });
    return step;
  }

  private static SequenceStep GuardedTap(string stepId, string imageId, int order = 0) => new() {
    Order = order,
    StepId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = "tap" },
    Condition = new ImageVisibleStepCondition { ImageId = imageId }
  };

  private static CommandSequence MakeSequence(IEnumerable<SequenceStep> steps, params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    sequence.SetSteps(steps.ToArray());
    return sequence;
  }

  private static IReadOnlyList<ImageValueCandidate> Find(
      QueueTemplateEntry entry, CommandSequence sequence, params Command[] commands) =>
      ParameterValidationService.FindImageValueCandidates(entry, 3, sequence, commands);

  [Fact]
  public void EntryValueToAConditionImageIdGivesOneCandidate() {
    var sequence = MakeSequence(new[] { GuardedTap("tap-option", "{{novaOption}}") }, Declare("novaOption"));

    Find(Entry(("novaOption", "no-such-image")), sequence).Should().Equal(
        new ImageValueCandidate(3, "novaOption", "no-such-image", "condition.imageId"));
  }

  [Fact]
  public void DefaultsFollowTheScopeOrderOfTheRun() {
    var command = MakeCommand("cmd", new[] { TapStep("option-a", overlay: "{{novaOption}}") },
        Declare("novaOption", "cmd-default"));
    var sequence = MakeSequence(
        new[] { GuardedTap("guard", "{{novaOption}}"), CommandCall("call", "cmd", order: 1) },
        Declare("novaOption", "seq-default"));

    Find(Entry(), sequence, command).Should().BeEquivalentTo(new[] {
      new ImageValueCandidate(3, "novaOption", "seq-default", "condition.imageId"),
      new ImageValueCandidate(3, "novaOption", "cmd-default", TapImageKey)
    });
  }

  [Fact]
  public void CommandFieldFallsBackToTheSequenceDefault() {
    var command = MakeCommand("cmd", new[] { TapStep("{{novaOption}}") }, Declare("novaOption"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd") }, Declare("novaOption", "seq-default"));

    Find(Entry(), sequence, command).Should().Equal(
        new ImageValueCandidate(3, "novaOption", "seq-default", TapImageKey));
  }

  [Fact]
  public void SequenceStepBindingCoversTheNameForTheCommandFields() {
    var command = MakeCommand("cmd", new[] { TapStep("option-a", overlay: "{{novaOption}}") }, Declare("novaOption"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd", 0, ("novaOption", "option-b")) });

    Find(Entry(("novaOption", "no-such-image")), sequence, command).Should().BeEmpty();
  }

  [Fact]
  public void BindingToAnotherNameIsNotFollowed() {
    var command = MakeCommand("cmd", new[] { TapStep("option-a", overlay: "{{novaOption}}") }, Declare("novaOption"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd", 0, ("novaOption", "{{otherName}}")) },
        Declare("otherName", "other-default"));

    Find(Entry(("novaOption", "no-such-image"), ("otherName", "x")), sequence, command).Should().BeEmpty();
  }

  [Fact]
  public void NullBindingDoesNotCoverTheName() {
    var command = MakeCommand("cmd", new[] { TapStep("option-a", overlay: "{{novaOption}}") }, Declare("novaOption"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd", 0, ("novaOption", null)) });

    Find(Entry(("novaOption", "no-such-image")), sequence, command).Should().Equal(
        new ImageValueCandidate(3, "novaOption", "no-such-image", TapImageKey));
  }

  [Fact]
  public void NestedCommandStepBindingCoversTheNameForTheInnerCommand() {
    var inner = MakeCommand("y", new[] { TapStep("option-a", overlay: "{{novaOption}}") }, Declare("novaOption"));
    var outer = MakeCommand("x", new[] { CallStep("y", 0, ("novaOption", "option-b")) });
    var sequence = MakeSequence(new[] { CommandCall("call", "x") });

    Find(Entry(("novaOption", "no-such-image")), sequence, outer, inner).Should().BeEmpty();
  }

  [Theory]
  [InlineData("y-default", "x-default", "seq-default", "y-default")]
  [InlineData(null, "x-default", "seq-default", "x-default")]
  [InlineData(null, null, "seq-default", "seq-default")]
  public void InnerCommandFieldUsesTheInnermostDefault(string? yDefault, string? xDefault, string seqDefault, string expected) {
    var inner = MakeCommand("y", new[] { TapStep("{{novaOption}}") }, Declare("novaOption", yDefault));
    var outer = MakeCommand("x", new[] { CallStep("y", 0) }, Declare("novaOption", xDefault));
    var sequence = MakeSequence(new[] { CommandCall("call", "x") }, Declare("novaOption", seqDefault));

    Find(Entry(), sequence, outer, inner).Should().Equal(
        new ImageValueCandidate(3, "novaOption", expected, TapImageKey));
  }

  [Fact]
  public void OneCommandOnTwoPathsGetsTheCandidateOfTheUnboundPath() {
    var command = MakeCommand("cmd", new[] { TapStep("{{novaOption}}") }, Declare("novaOption"));
    var sequence = MakeSequence(new[] {
      CommandCall("bound", "cmd", 0, ("novaOption", "option-b")),
      CommandCall("unbound", "cmd", 1)
    });

    Find(Entry(("novaOption", "no-such-image")), sequence, command).Should().Equal(
        new ImageValueCandidate(3, "novaOption", "no-such-image", TapImageKey));
  }

  [Fact]
  public void InlineReadinessImagePlaceholderGivesACandidate() {
    var command = MakeCommand("cmd", new[] {
      new CommandStep {
        Type = CommandStepType.EnsureGameRunning,
        Order = 0,
        EnsureGameRunning = new EnsureGameRunningConfig { ReadinessImage = new DetectionTarget("{{novaOption}}") }
      }
    }, Declare("novaOption"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd") });

    Find(Entry(("novaOption", "no-such-image")), sequence, command).Should().Equal(
        new ImageValueCandidate(3, "novaOption", "no-such-image", "ensureGameRunning.readinessImage.referenceImageId"));
  }

  [Fact]
  public void BuiltInOrUnknownNameGivesNoCandidate() {
    var sequence = MakeSequence(new[] {
      GuardedTap("a", "{{queue.gameId}}"),
      GuardedTap("b", "{{nobodySuppliesThis}}", order: 1)
    }, Declare("nobodySuppliesThis"));

    Find(Entry(), sequence).Should().BeEmpty();
  }

  [Fact]
  public void ValueThatGoesToAnImageFieldAndATextFieldGivesACandidate() {
    var command = MakeCommand("cmd", new[] {
      TapStep("{{novaOption}}"),
      new CommandStep { Type = CommandStepType.KeyInput, Order = 1, KeyInput = new KeyInputConfig { Key = "{{novaOption}}" } }
    }, Declare("novaOption"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd") });

    Find(Entry(("novaOption", "no-such-image")), sequence, command).Should().Equal(
        new ImageValueCandidate(3, "novaOption", "no-such-image", TapImageKey));
  }

  [Fact]
  public void SameImageIdFromTwoFieldsGivesNoDuplicate() {
    var command = MakeCommand("cmd", new[] {
      TapStep("{{novaOption}}", order: 0),
      TapStep("{{novaOption}}", order: 1)
    }, Declare("novaOption"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd") });

    Find(Entry(("novaOption", "no-such-image")), sequence, command).Should().ContainSingle();
  }

  [Fact]
  public void OverlayKeysInlineFieldsAndCommandDetectionGiveCandidates() {
    var command = MakeCommand("cmd", new[] {
      TapStep("option-a", order: 0, overlay: "{{novaOption}}"),
      new CommandStep {
        Type = CommandStepType.WaitForImage,
        Order = 1,
        WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("wait-{{novaOption}}") }
      }
    }, Declare("novaOption"));
    command.Detection = new DetectionTarget("det-{{novaOption}}");
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd") });

    Find(Entry(("novaOption", "b")), sequence, command).Should().BeEquivalentTo(new[] {
      new ImageValueCandidate(3, "novaOption", "b", TapImageKey),
      new ImageValueCandidate(3, "novaOption", "wait-b", WaitImageKey),
      new ImageValueCandidate(3, "novaOption", "det-b", "detection.referenceImageId")
    });
  }

  [Fact]
  public void InlineImageIdIsNotACandidateWhenTheOverlayKeyReplacesIt() {
    var command = MakeCommand("cmd", new[] { TapStep("{{fallback}}", overlay: "{{novaOption}}") },
        Declare("novaOption"), Declare("fallback", "missing-fallback"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd") });

    Find(Entry(("novaOption", "option-b")), sequence, command).Should().Equal(
        new ImageValueCandidate(3, "novaOption", "option-b", TapImageKey));
  }

  [Fact]
  public void NumericFieldsGiveNoCandidate() {
    var command = MakeCommand("cmd", new[] {
      new CommandStep {
        Type = CommandStepType.Swipe,
        Order = 0,
        Swipe = new SwipeConfig { StartX = 0, StartY = 0, EndX = 1, EndY = 1 },
        FieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "{{originX}}" }
      }
    }, Declare("originX"));
    var sequence = MakeSequence(new[] { CommandCall("call", "cmd") });

    Find(Entry(("originX", "12")), sequence, command).Should().BeEmpty();
  }
}

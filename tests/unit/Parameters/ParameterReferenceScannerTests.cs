using System.Collections.Generic;
using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 078: locating parameter references and their field paths for validation.</summary>
public sealed class ParameterReferenceScannerTests {
  [Fact]
  public void FindsReferenceInAnInlineStringField() {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.EnsureEmulatorRunning,
      Order = 0,
      EnsureEmulatorRunning = new EnsureEmulatorRunningConfig { AdbSerial = "{{adbSerial}}" }
    });

    var found = ParameterReferenceScanner.Scan(command);

    found.Should().ContainSingle(r => r.ParameterName == "adbSerial"
        && r.FieldPath == "ensureEmulatorRunning.adbSerial");
  }

  [Fact]
  public void FindsReferenceInTheNumericOverlay() {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 0, StartY = 0, EndX = 1, EndY = 1 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "{{originX}}" }
    });

    ParameterReferenceScanner.Scan(command)
        .Should().ContainSingle(r => r.ParameterName == "originX" && r.FieldPath == "swipe.startX");
  }

  [Fact]
  public void MarksImageReferencesAsDefeatingStaticChecks() {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{img}}") }
    });

    ParameterReferenceScanner.Scan(command)
        .Should().ContainSingle(r => r.ParameterName == "img" && r.DefeatsStaticCheck);
  }

  [Fact]
  public void IgnoresCommandStepTargetIdBecauseItIsAReference() {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep { Type = CommandStepType.Command, Order = 0, TargetId = "{{which}}" });

    ParameterReferenceScanner.Scan(command).Should().BeEmpty();
  }

  [Fact]
  public void FindsReferenceInASequenceActionPayload() {
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    var action = new SequenceActionPayload { Type = "ensure-emulator-running" };
    action.Parameters["adbSerial"] = "{{adbSerial}}";
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "s1", StepType = SequenceStepType.Action, Action = action }
    });

    ParameterReferenceScanner.Scan(sequence)
        .Should().ContainSingle(r => r.ParameterName == "adbSerial"
            && r.FieldPath == "action.adbSerial" && r.StepLabel == "s1");
  }

  [Fact]
  public void MarksReferencesInsideALoopBodyAsInsideLoop() {
    var bodyAction = new SequenceActionPayload { Type = "tap" };
    bodyAction.Parameters["x"] = "{{iteration}}";
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "loop1",
        StepType = SequenceStepType.Loop,
        Loop = new CountLoopConfig { Count = 2 },
        Body = new[] {
          new SequenceStep { Order = 0, StepId = "b1", StepType = SequenceStepType.Action, Action = bodyAction }
        }
      }
    });

    ParameterReferenceScanner.Scan(sequence)
        .Should().ContainSingle(r => r.ParameterName == "iteration" && r.InsideLoop);
  }

  [Fact]
  public void MarksTopLevelReferencesAsOutsideLoop() {
    var action = new SequenceActionPayload { Type = "tap" };
    action.Parameters["x"] = "{{originX}}";
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "s1", StepType = SequenceStepType.Action, Action = action }
    });

    ParameterReferenceScanner.Scan(sequence).Should().ContainSingle(r => !r.InsideLoop);
  }

  [Fact]
  public void FindsReferenceInAStepParameterBinding() {
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "s1",
        StepType = SequenceStepType.Command,
        CommandId = "cmd",
        ParameterBindings = new Collection<ParameterBinding> {
          new() { Name = "adbSerial", Value = "{{queue.emulatorSerial}}" }
        }
      }
    });

    ParameterReferenceScanner.Scan(sequence)
        .Should().ContainSingle(r => r.ParameterName == "queue.emulatorSerial");
  }

  [Fact]
  public void LiteralOnlyEntitiesProduceNoReferences() {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.KeyInput,
      Order = 0,
      KeyInput = new KeyInputConfig { Key = "KEYCODE_HOME" }
    });

    ParameterReferenceScanner.Scan(command).Should().BeEmpty();
  }

  [Fact]
  public void NullEntitiesScanToEmpty() {
    ParameterReferenceScanner.Scan((Command?)null).Should().BeEmpty();
    ParameterReferenceScanner.Scan((CommandSequence?)null).Should().BeEmpty();
  }

  [Fact]
  public void DistinctNamesDeduplicatesAcrossFields() {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.EnsureEmulatorRunning,
      Order = 0,
      EnsureEmulatorRunning = new EnsureEmulatorRunningConfig {
        AdbSerial = "{{s}}", InstanceName = "{{s}}"
      }
    });

    ParameterReferenceScanner.DistinctNames(ParameterReferenceScanner.Scan(command))
        .Should().ContainSingle().Which.Should().Be("s");
  }

  // ── Feature 114: image keys in the overlay ────────────────────────────────

  [Theory]
  [InlineData("primitiveTap.detectionTarget.referenceImageId")]
  [InlineData("waitForImage.detectionTarget.referenceImageId")]
  public void ImageOverlayKeyDefeatsTheStaticCheck(string key) {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("option-a") },
      FieldTemplates = new Dictionary<string, string> { [key] = "{{novaOption}}" }
    });

    ParameterReferenceScanner.Scan(command).Should().ContainSingle(r => r.ParameterName == "novaOption"
        && r.FieldPath == key && r.DefeatsStaticCheck && r.SourceText == "{{novaOption}}");
  }

  [Fact]
  public void NumericOverlayKeyDoesNotDefeatTheStaticCheck() {
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 0, StartY = 0, EndX = 1, EndY = 1 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "{{originX}}" }
    });

    ParameterReferenceScanner.Scan(command)
        .Should().ContainSingle(r => r.FieldPath == "swipe.startX" && !r.DefeatsStaticCheck);
  }

  // ── Feature 114: imageVisible leaves in each condition position ───────────

  private static ImageVisibleStepCondition Image(string id) => new() { ImageId = id };

  private static SequenceActionPayload TapAction() => new() { Type = "tap" };

  private static ParameterReference ScanSingle(SequenceStep step) {
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] { step });
    return ParameterReferenceScanner.Scan(sequence).Should().ContainSingle().Subject;
  }

  [Fact]
  public void StepConditionImageIdIsScanned() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "tap-option", StepType = SequenceStepType.Action, Action = TapAction(),
      Condition = Image("{{novaOption}}")
    });

    reference.ParameterName.Should().Be("novaOption");
    reference.FieldPath.Should().Be("condition.imageId");
    reference.StepLabel.Should().Be("tap-option");
    reference.DefeatsStaticCheck.Should().BeTrue();
    reference.InsideLoop.Should().BeFalse();
    reference.SourceText.Should().Be("{{novaOption}}");
  }

  [Fact]
  public void IfConditionImageIdIsScannedOutsideTheLoop() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "if1", StepType = SequenceStepType.If,
      If = new IfConfig { Condition = Image("nova-{{option}}") }
    });

    reference.FieldPath.Should().Be("if.condition.imageId");
    reference.InsideLoop.Should().BeFalse();
    reference.DefeatsStaticCheck.Should().BeTrue();
    reference.SourceText.Should().Be("nova-{{option}}");
  }

  [Fact]
  public void WhileConditionImageIdIsScannedInsideTheLoop() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "w1", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = Image("{{novaOption}}"), MaxIterations = 3 }
    });

    reference.FieldPath.Should().Be("loop.condition.imageId");
    reference.InsideLoop.Should().BeTrue();
    reference.DefeatsStaticCheck.Should().BeTrue();
  }

  [Fact]
  public void RepeatUntilConditionImageIdIsScannedInsideTheLoop() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "r1", StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig { Condition = Image("{{iteration}}"), MaxIterations = 3 }
    });

    reference.FieldPath.Should().Be("loop.condition.imageId");
    reference.ParameterName.Should().Be("iteration");
    reference.InsideLoop.Should().BeTrue();
  }

  [Fact]
  public void BreakConditionImageIdIsScannedInsideTheLoop() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new[] {
        new SequenceStep {
          Order = 0, StepId = "brk", StepType = SequenceStepType.Break,
          BreakCondition = Image("{{novaOption}}")
        }
      }
    };

    var reference = ScanSingle(loop);

    reference.FieldPath.Should().Be("breakCondition.imageId");
    reference.StepLabel.Should().Be("brk");
    reference.InsideLoop.Should().BeTrue();
    reference.DefeatsStaticCheck.Should().BeTrue();
  }

  [Fact]
  public void CompositeChildImageIdIsScannedWithTheChildPath() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "tap", StepType = SequenceStepType.Action, Action = TapAction(),
      Condition = new AllStepCondition {
        Children = new SequenceStepCondition[] {
          Image("option-a"),
          new AnyStepCondition { Children = new SequenceStepCondition[] { Image("x"), Image("{{novaOption}}") } }
        }
      }
    });

    reference.FieldPath.Should().Be("condition.children[1].children[1].imageId");
    reference.DefeatsStaticCheck.Should().BeTrue();
  }

  [Fact]
  public void LiteralConditionImageIdGivesNoReference() {
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "tap", StepType = SequenceStepType.Action, Action = TapAction(),
        Condition = Image("option-a")
      }
    });

    ParameterReferenceScanner.Scan(sequence).Should().BeEmpty();
  }

  // ── Feature 165: ocrText.pattern and ocrText.expected ─────────────────────

  private static OcrTextStepCondition OcrPattern(string pattern) =>
      new() { Region = new PixelRegion(10, 20, 300, 400), Pattern = pattern };

  private static OcrTextStepCondition OcrExpected(string expected) =>
      new() { Region = new PixelRegion(10, 20, 300, 400), Expected = expected };

  [Fact]
  public void OcrPatternKeysAreScannedAsRegexPattern() {
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "s3", StepType = SequenceStepType.Action, Action = TapAction(),
        Condition = OcrPattern("{{word}}.*{{{n}}}")
      }
    });

    var found = ParameterReferenceScanner.Scan(sequence);

    found.Select(r => r.ParameterName).Should().Equal("word", "n");
    found.Should().OnlyContain(r => r.FieldPath == "condition.pattern" && r.IsRegexPattern
        && !r.DefeatsStaticCheck && r.SourceText == "{{word}}.*{{{n}}}" && r.StepLabel == "s3" && !r.InsideLoop);
  }

  [Fact]
  public void OcrExpectedKeyIsScannedAsPlainText() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "s3", StepType = SequenceStepType.Action, Action = TapAction(),
      Condition = OcrExpected("Level {{level}}")
    });

    reference.ParameterName.Should().Be("level");
    reference.FieldPath.Should().Be("condition.expected");
    reference.IsRegexPattern.Should().BeFalse();
    reference.DefeatsStaticCheck.Should().BeFalse();
    reference.SourceText.Should().Be("Level {{level}}");
  }

  [Fact]
  public void OcrPatternInIfConditionIsScannedOutsideTheLoop() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "if1", StepType = SequenceStepType.If,
      If = new IfConfig { Condition = OcrPattern("{{p}}") }
    });

    reference.FieldPath.Should().Be("if.condition.pattern");
    reference.InsideLoop.Should().BeFalse();
  }

  [Fact]
  public void OcrPatternInLoopConditionIsScannedInsideTheLoop() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "w1", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = OcrPattern("{{iteration}}"), MaxIterations = 3 }
    });

    reference.FieldPath.Should().Be("loop.condition.pattern");
    reference.InsideLoop.Should().BeTrue();
  }

  [Fact]
  public void OcrExpectedInBreakConditionIsScannedInsideTheLoop() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new[] {
        new SequenceStep {
          Order = 0, StepId = "brk", StepType = SequenceStepType.Break,
          BreakCondition = OcrExpected("{{accountName}}")
        }
      }
    });

    reference.FieldPath.Should().Be("breakCondition.expected");
    reference.StepLabel.Should().Be("brk");
    reference.InsideLoop.Should().BeTrue();
  }

  [Fact]
  public void OcrPatternInACompositeChildIsScannedWithTheChildPath() {
    var reference = ScanSingle(new SequenceStep {
      Order = 0, StepId = "tap", StepType = SequenceStepType.Action, Action = TapAction(),
      Condition = new AllStepCondition {
        Children = new SequenceStepCondition[] { Image("option-a"), OcrPattern("(Gathering.*){{{n}}}") }
      }
    });

    reference.FieldPath.Should().Be("condition.children[1].pattern");
    reference.IsRegexPattern.Should().BeTrue();
  }

  [Fact]
  public void OcrPatternDoubleBraceDigitsGivesAReference() {
    ScanSingle(new SequenceStep {
      Order = 0, StepId = "tap", StepType = SequenceStepType.Action, Action = TapAction(),
      Condition = OcrPattern("(Gathering.*){{3}}")
    }).ParameterName.Should().Be("3");
  }

  [Fact]
  public void OcrParserGivesNoReference() {
    var condition = OcrPattern("(Gathering.*){3}");
    condition.Parser = "{{parser}}";
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "tap", StepType = SequenceStepType.Action, Action = TapAction(), Condition = condition }
    });

    ParameterReferenceScanner.Scan(sequence).Should().BeEmpty();
  }
}

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
}
